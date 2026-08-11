# 06 — Design: Streaming Data Plane (Chunked Ports) 🌊

> Closes the "streaming data plane" gap from [`02`](02-snaplogic-comparison.md) §2/§7.
> SnapLogic parity target: per-document streaming between Snaps (bounded memory for large ETL).
> **Largest and riskiest of the four designs.**

> ### 📌 Implementation status — *mostly shipped*
>
> This document was written as design-level, not implementation-ready. It is now **largely
> implemented**; it stays the normative record of *why*, with drift marked inline.
>
> | Slice | Status |
> | --- | --- |
> | 5.1.0 core contracts · 5.1.1 bridges · 5.1.2 designer · 5.1.3 region executor · 5.1.4 modules & scale-out · 5.1.6 spill | ✅ shipped |
> | 5.1.5 observability & sampling (§4.4) | ⏳ **not started** — the only unbuilt MVP slice |
>
> **Where reality diverged from this document** (each marked ⚠️ **SUPERSEDED** at the point it
> occurs, with the reasoning kept rather than overwritten):
>
> - **Ordering/resequencing (§5.1, D25)** — the entire resequencing design was **deleted**, not
>   built. A spike showed Akka.Streams orders by *input slot*, so ordering holds at every
>   cardinality for free. Tombstones, `onResequenceOverflow` and `StreamCardinality` went with it.
> - **Spill storage & encryption (§4.5, 5.1.6)** — both recorded decisions turned out to rest on
>   things that **didn't exist**: the preferred blob providers returned `null`, and there was no
>   secret-variable encryption to reuse. Both were built, and the storage model was reworked around
>   a sharper question (*what kind of disk does this host have?*).
> - **Region executor internals (§3.2)** — Akka.Streams, not raw `System.Threading.Channels`,
>   chosen by spike on strategic reach (triggers, queues, throttled connectors) as much as fit.
> - **Module contract (§3.3)** — grew two interfaces beyond the original one: `IStreamTerminalModule`
>   (5.1.1) and `IStreamItemProcessor` (5.1.4, D26).
>
> Detailed slice-by-slice status:
> [`phases/Phase5-1-StreamingDataPlane.md`](../../phases/Phase5-1-StreamingDataPlane.md).
> Spill decision record: [`phases/Phase5-1-6-SpillMode-Plan.md`](../../phases/Phase5-1-6-SpillMode-Plan.md).

## 1. Goal & explicitly-not-goals

**Goal:** let a chain of nodes process a large dataset (DB result set, big CSV/JSON file, paged
API) with **bounded memory**, without materializing the whole set in the execution context.

**Not goals:** rewriting DotFlow into a document-streaming engine; changing default semantics.
Batch-per-trigger stays the model — streaming is an **opt-in port capability**, preserving
DotFlow's composition philosophy (explicit over implicit).

## 2. Approach comparison

| Option | Sketch | Verdict |
| --- | --- | --- |
| A. Chunked ForEach (no engine change) | producers emit pages; `foreach` over pages | ✅ works today-ish; high friction; keep as guidance |
| B. **Stream-typed ports** | ports of type `IAsyncEnumerable<JsonElement>`-like; engine runs a streaming *region* concurrently | ✅ **recommended** |
| C. Full document streaming (SnapLogic model) | every edge is a stream | ❌ breaks variables/staged writes/trycatch semantics; huge — see full assessment in [`09`](09-full-streaming-rewrite-assessment.md) |

## 3. Recommended architecture (Option B)

### 3.1 Stream ports

- New port capability on `PortDefinition`: `IsStreaming` (or a `DataStream` marker `DataType`).
  Designer draws them distinctly (SnapLogic's circle-vs-diamond precedent).
- **Shape rule** like SnapLogic: streaming outputs connect only to streaming inputs. Bridging
  nodes convert: `builtin.stream.collect` (stream→array, with `maxItems` guard) and
  `builtin.stream.fromitems` (array→stream).

### 3.2 Streaming regions

A maximal connected sub-graph of stream-linked nodes forms a **streaming region**. The engine
(`WorkflowExecutor`) detects the region and hands it to a new `StreamRegionExecutorActor`
(same family as `LoopExecutorActor`/`ParallelExecutionCoordinator`):

- Each node in the region runs as a long-lived stage; items flow via **bounded channels**
  (`System.Threading.Channels`) — real backpressure, no Akka mailbox flooding.
  > ⚠️ **SUPERSEDED (5.1.3, D27):** the region executor is built on **Akka.Streams**, not raw
  > channels. A spike tested the five things the executor actually needs — consuming
  > `IAsyncEnumerable`, bounded backpressure, `maxWorkers`, per-item `skip` supervision, and region
  > cancellation — and all five passed with built-in operators. The deciding argument was reach
  > rather than this slice: the same library is the natural implementation for queue-fed and
  > scheduled triggers ([`08`](08-always-on-serving-design.md)), Reuse-mode child stages
  > ([`05`](05-subworkflow-design.md)), and rate-limited connectors. The choice stays **internal** —
  > modules only ever see `IAsyncEnumerable<StreamItem>` — so it remains swappable.
- **Channel capacity (decided):** engine-level default (**64**) overridable at two levels —
  per workflow (`Metadata`/settings) and per connection via a new typed
  `ConnectionDefinition.BufferCapacity : int?` field (validated, schema-visible; pre-1.0 so
  the core-record change is acceptable).
- Region completes when the source stage completes and all channels drain; then normal
  port-dispatch resumes downstream (scalar outputs like `itemCount`, plus any collected
  results, feed non-streaming successors).
- Cancellation: region's CTS linked to `_executionCts` as usual. A stage fault cancels the
  region and surfaces as the region's node failure (parent trycatch applies).
- **v1 regions are linear chains (D22, 5.1.3):** branching *inside* a region fails loudly rather
  than materialising a graph shape the designer can't yet validate or explain. Broadcast/merge
  inside a region waits for the operators that need it.

### 3.3 Module contract extension

New optional interface (modules keep `IWorkflowModule` for scalar work):

```csharp
public interface IStreamingWorkflowModule : IWorkflowModule
{
    IAsyncEnumerable<StreamItem> ExecuteStreamAsync(
        ModuleExecutionContext context,
        IAsyncEnumerable<StreamItem> input,      // empty for sources
        CancellationToken ct);
}
```

`StreamItem` ≈ payload + lightweight metadata (index, isLast?). **Payload (decided):** v1 is
**JSON only** (`JsonElement`), but `StreamItem.Payload` is modeled as a closed union from day
one — `JsonPayload` now, `BinaryPayload` (stream/chunk, SnapLogic diamond-view analog) reserved
— so binary support later doesn't break the module contract. Sources ignore `input`; sinks
drain it and emit nothing; transforms do both.

**Terminal stages (added during 5.1.1 implementation):** a stage with a streaming *input* but
batch *outputs* — `builtin.stream.collect` and every sink that reports `rowsAffected`/`itemCount`
— can't express itself through `ExecuteStreamAsync`, which only yields items. Those implement a
second interface:

```csharp
public interface IStreamTerminalModule : IStreamingWorkflowModule
{
    Task<ModuleResult> ExecuteTerminalAsync(
        ModuleExecutionContext context,
        IAsyncEnumerable<StreamItem> input,
        CancellationToken ct);
}
```

Returning a plain `ModuleResult` means the engine's existing port dispatch takes over at the
region's edge with no special cases. State is safe in both methods: it lives in locals for the
duration of the call, so singleton module instances still serve concurrent executions.

**Per-item stages (added during 5.1.4 — D26):** `ExecuteStreamAsync` hands the module the *whole*
stream, so the module owns the loop and the engine cannot parallelise or order it. Any stage that
wants `maxWorkers > 1` therefore implements a third, per-item interface:

```csharp
public interface IStreamItemProcessor : IStreamingWorkflowModule
{
    Task<IReadOnlyList<StreamItem>> ProcessAsync(
        StreamItem item,
        ModuleExecutionContext context,
        CancellationToken ct);   // 0 = drop, 1 = transform, N = split
}
```

This is also the *simpler* contract for module authors — no async-iterator boilerplate — and it is
what makes D25's free ordering reachable, because the engine can then own the `SelectAsync`.
Whole-stream modules keep working unchanged and are pinned to one worker (with a designer warning
rather than a silent downgrade).

Which of the three a module implements is the only thing that distinguishes *per-item* from
*whole-stream*; there is no declared cardinality enum (see §6, `StreamCardinality`).

### 3.4 First streaming-capable modules

`builtin.database.query` (reader → items), `builtin.file.csv.read` / `.json.read` (array
streaming), `builtin.transform.map` / `.query` (per-item), `builtin.database.bulkinsert` and
`builtin.file.*.write` (sinks), plus the two bridge nodes.

> ✅ **Shipped in 5.1.4** — all of the above, plus `builtin.transform.aggregate` as a *terminal*
> stage. `builtin.file.xml.read`/`.write` were **deliberately deferred**: XML has no equivalent of a
> self-delimiting element stream without committing to a document shape, and the CSV/JSON pair
> already covers the file story.

### 3.5 Sub-workflows as streaming stages (Reuse-mode parity)

Per feedback, SnapLogic **Pipeline Execute Reuse mode** (long-lived child instances streaming
documents) should be accounted for here rather than deferred blindly:

- Target shape: `builtin.workflow.execute` ([`05`](05-subworkflow-design.md)) gains a
  streaming variant — the node sits *inside* a region as a stage; each `StreamItem` becomes
  one pass through a **persistent child execution** whose entry/exit are stream bridge ports.
- Prerequisite: the child workflow itself must be a single streaming region (or wholly
  batch-per-item, which is just Standard mode = one child launch per item — expressible today
  as foreach + workflow.execute and *cheap but slow*).
- Recommended sequencing: design the `StreamItem`/stage contract so a child-execution stage is
  *just another `IStreamingWorkflowModule`* — no special engine case. Ship in a later phase
  (see §7), but keep the contract compatible from v1.

## 4. Semantics decisions (the hard part)

1. **Variables inside a region:** reads allowed (snapshot at region start); `SetVariable` is
   **not allowed inside a region** (non-deterministic ordering). Enforced at validation.
2. **Templates per item:** streaming transforms get `{{item}}` (analogous to loop semantics)
   rather than `{{input}}`.
3. **Error granularity:** per-item error policy on each stage — `fail` (default), `skip`, or
   route to an optional streaming `error` output port (SnapLogic error-view analog).
   **Envelope (decided):** an error item carries the failing item **and** the error metadata
   in one envelope, and the metadata follows the standard error-document contract from
   [`07`](07-reusable-error-workflow-design.md) §2 applied per item (same `error` block:
   message, errorType, nodeId). Doc 07's schema now includes the per-item variant
   (`item` + `offset` fields) — resolved.
4. **History & observability:** per-node record stores item counts + duration, not per-item
   records. Per-stage item-count/rate metrics stream to the monitor. **Item sampling
   (decided):** the monitor may sample **N example items per stage**, but sampling is
   **off by default** and configurable (per workflow and per stage: sample size, on/off);
   sampled payloads pass through secret redaction (`IsSecret` variables) before persistence,
   and sampling is denied on stages downstream of fields marked sensitive — mechanics shared
   with doc 07's PII open question. **Redaction basis (decided):** start with
   `IsSecret`-based redaction; revisit field-level sensitivity annotations on module schemas
   only if real use shows `IsSecret` insufficient.
5. **Joins/aggregates over streams:** v1 restriction — `aggregate` allowed with a **bounded
   accumulator guard**: hard limits that **fail loudly by default**, with an opt-in per-stage
   `spill` mode for larger datasets (decided). Stream-stream `join` is **not** in v1 (SnapLogic's
   own sorted-stream/hang caveats show the cost); collect-then-join instead.
   - **Unified policy (decided):** the guard + spill options form one reusable "bounded
     accumulator" policy shared by every unbounded-memory stage — `aggregate`,
     `builtin.stream.collect`, and any future buffering stage (a `stream.sort`, 5.1.P5).
     *Shipped as `BoundedAccumulator` with `maxItems` (default 100 000) + optional `maxBytes`;
     the byte ceiling is measured only when configured, so nobody pays for a limit they didn't set.*
   - **Guard defaults (decided):** derive defaults from available memory and expected
     workload rather than fixed constants; user-overridable per stage. Concrete numbers to be
     calibrated with real measurements during the proving slice (phase 3).
     ⏳ *Still outstanding — the shipped `maxItems = 100 000` is an explicit placeholder pending
     the Docker-gated 1M-row calibration run.*
   - **Spill still means bounded (clarified in 5.1.6):** `spill` changes **where** buffered items
     live, not whether there is a bound. The configured ceiling continues to apply; a spill flag
     that quietly meant "unlimited" would undo the very guarantee this policy exists to give.
     A guard test pins this.
   - ⚠️ **Spill storage — SUPERSEDED (5.1.6).** The original decision below ("prefer database or
     NATS; local temp-file option") was found to be **unimplementable as written**: both
     `PostgresPersistenceProvider.Blobs` and `NatsPersistenceProvider.Blobs` returned `null`. Those
     stores were then built, and the storage model was reworked around a sharper question — *what
     kind of disk does this host actually have?* See
     [`phases/Phase5-1-6-SpillMode-Plan.md`](../../phases/Phase5-1-6-SpillMode-Plan.md) §11/§13.
     Current model:

     | Target | Use when | Why |
     | --- | --- | --- |
     | 🗄️ **SQLite** (one configured file) | local disk is fast | lowest latency, no network hop, one provisionable file rather than thousands of scratch files |
     | ☁️ **Blob** (Postgres / NATS / S3) | local disk is slow or throttled (small cloud nodes, overlay FS) | scratch leaves the node for infrastructure an operator already sizes |
     | 📁 **Directory** | explicit opt-in only | documented as "only when you know local disk is fast" |

     Two changes worth flagging against the original text. **There is no implicit default** — an
     unconfigured host fails loudly naming the setting, because a silent fallback to a temp
     directory lands scratch on whatever filesystem the container happened to have. And **S3 is no
     longer deferred**: it arrives free through `IBlobStore`, so the orphan-sweep concern that
     justified deferring it was solved generally (`IBlobStore.ListAsync(prefix)`) rather than
     avoided.
   - **Cleanup contract (revised in 5.1.6):** eager deletion when the buffering stage disposes is
     the *primary* mechanism; the background sweeper is a safety net for crashes. Retention keys off
     **execution completion, not artefact age**, which is what makes it cluster-safe — any host can
     evaluate the rule because it depends on shared execution state rather than "did I write this?".
     A second, much longer *abandoned* threshold reclaims spill for executions whose record is stuck
     non-terminal (a host that died mid-run), closing what would otherwise be a guaranteed leak.
   - ⚠️ **Spill encryption — SUPERSEDED (5.1.6).** The original decision said "same mechanism used
     for secret variables". Investigation found **there is no such mechanism**: `IsSecret` is a
     designer-only flag and `docs/variables.md` states globals are stored in plaintext. Spill
     therefore builds its own `ISpillProtector` on **ASP.NET Data Protection** (purpose
     `Workflow.Streaming.Spill`), copying the `IConnectionStringProtector` layering — abstraction in
     the modules layer, implementation supplied by the host. Encryption is **on by default**; a host
     may opt out, but never quietly: startup logs it, every spilling execution logs it, and the
     designer warns on stages where spill could engage.
   - **Guard-default budget (decided):** defaults derive from **host memory at engine
     start** (engine-wide cap shared by concurrent regions); a per-region budget arbiter is
     a possible later refinement. ⏳ *Not yet implemented — see the calibration item above.*

## 5. Tradeoff analyses (requested)

### 5.1 `builtin.parallel` composing with regions

Two distinct things get conflated here:

| | What it means | Cost | Value |
| --- | --- | --- | --- |
| **(a) Stage parallelism** *within* a region | one stage runs N worker instances consuming the same channel (competing consumers) | moderate: a `maxWorkers` property per streaming node; ordering becomes non-deterministic past that stage | high — this is where streaming throughput actually comes from (CPU-bound transforms, slow sinks) |
| **(b) `builtin.parallel` branches *containing* regions** | each parallel branch may embed its own independent region | low — falls out naturally if regions are self-contained sub-graph constructs like loops already are | medium |
| **(c) A region *spanning* parallel branches** | fanout/fanin of live streams across the parallel construct's branches | high: region detection must reason across construct boundaries; failure/cancellation coupling between constructs; deadlock risk at fan-in (SnapLogic's Join-hang caveat) | low — same effect achievable with in-region broadcast/merge stages later |

**Recommendation:** v1 ships **(b)** implicitly (regions inside branches "just work" if regions
are ordinary sub-graph citizens) and **(a)** as a per-stage `maxWorkers` property (ordered=1
default). **(c)** is explicitly out of scope — document that streams may not cross construct
boundaries; bridge nodes are the escape hatch.

**Ordering — ⚠️ REVISED (Phase 5.1.3/5.1.4, D25).** This section originally specified an optional
per-stage **resequencing buffer** with an `onResequenceOverflow` policy and **sequence tombstones**
from skipping stages. A spike against the chosen executor (Akka.Streams) showed that machinery is
**unnecessary at every cardinality** and it has been **deleted from the design**:

| Stage shape under `SelectAsync(4)` + `SelectMany`, random delays | Ordered? |
| --- | --- |
| 1:1 transform | ✅ |
| Filter (0..1 outputs per input) | ✅ |
| Splitter (1..N outputs per input) | ✅ |
| In-flight work under a slow straggler | bounded by parallelism, not stream length |

Ordering is by **input slot**, not by a sequence key: a stage emitting zero outputs contributes
nothing at its slot, and one emitting N keeps them contiguous. So the model is simply:

- **`maxWorkers`** (default 1) and **`ordered`** (default **true**) — two knobs, no buffers.
- The designer badges a stage "⚠ unordered" only when `maxWorkers > 1 && !ordered`.
- **Documented v1 ordering contract:** output is in source order unless a stage opts out of
  ordering. (Previously: "source order iff every stage is single-worker.")
- Stages that consume the whole stream at their own rhythm (`aggregate`) can't be parallelised and
  run single-worker; "restore source order" is meaningless for them anyway.

`SourceOffset` **remains** — it was reserved for checkpointing (§5.2, D12), which is unaffected.

**Consequence for the module contract (D26):** `ExecuteStreamAsync` hands a module the *whole*
stream, so the module owns the loop and the engine can neither parallelise nor order it. Getting
`maxWorkers` therefore requires an optional **per-item** entry point
(`ProcessAsync(StreamItem) → IEnumerable<StreamItem>`); stream-shaped modules keep working but stay
single-worker. Per-item is also the simpler contract for module authors.

### 5.2 Region checkpointing / resumability

| Option | Sketch | Cost | Notes |
| --- | --- | --- | --- |
| **None (v1)** | region failure = node failure; re-run reprocesses from source | zero | acceptable iff sources are re-readable and sinks idempotent — same posture as SnapLogic standard pipelines and our webhook redelivery stance ([`08`](08-always-on-serving-design.md) §2.2) |
| Source-offset checkpoint | periodically persist source position (row offset, file byte, page cursor) + flush barrier; resume = seek | moderate; needs per-source seek support and a barrier protocol through stages | gives *at-least-once* with bounded reprocessing; only meaningful once sinks are transactional/idempotent |
| Full stage-state checkpoint | Flink-style barriers snapshotting every stage's state | very high | not justified — stateful stages in v1 are only `aggregate` |

**Recommendation:** **defer checkpointing from v1**, but bake in the two cheap enablers now:
(1) `StreamItem` carries an optional `SourceOffset` — an **opaque per-source token that must
be totally ordered within its source** (decided): shape is
`record SourceOffset(string Token, long Sequence)`, where `Sequence` is a monotonic ordinal
supplied by the source (row number, item index) and `Token` is whatever the source needs to
seek (page cursor, byte offset). `Sequence` is a uniform ordinal for checkpoint bookkeeping;
`Token` gives resumability. (2) The source module contract includes an optional
`StartFrom(SourceOffset)` capability flag. That keeps source-offset checkpointing a purely
additive later phase. Document the v1 recovery contract explicitly: *re-runnable source +
idempotent sink, or don't stream it.*

## 6. Resolved decisions (from review)

| Question | Decision |
| --- | --- |
| Channel capacity | default 64, overridable per workflow and per connection; pre-1.0 breakage acceptable (§3.2) |
| Buffer override placement | typed `ConnectionDefinition.BufferCapacity : int?` field, not Metadata (§3.2) |
| Binary streams | JSON-only v1; `StreamItem.Payload` modeled as a union so binary is additive (§3.3) |
| Reuse-mode sub-workflows | accounted for: child-execution stage as a plain `IStreamingWorkflowModule`, later phase (§3.5) |
| Parallel composition | (b) regions-inside-branches + (a) per-stage `maxWorkers` in v1; region-spanning-constructs (c) out of scope (§5.1) |
| ~~Ordering / resequencing~~ | ⚠️ **SUPERSEDED (D25)** — resequencing buffer, overflow policy and tombstones **deleted**; ordering is `maxWorkers` + `ordered` (default true), correct at every cardinality (§5.1) |
| Module contract for parallelism | per-item `ProcessAsync` entry point required for `maxWorkers`; stream-shaped modules stay single-worker (D26, §5.1) |
| Checkpointing | deferred; `SourceOffset` + `StartFrom` enablers reserved in contracts now (§5.2) |
| `SourceOffset` shape | `(Token: string, Sequence: long)` — opaque seekable token + monotonic ordinal for ordering (§5.2) |
| Streaming error envelope | failing item + per-item error document (doc 07 contract) in one envelope (§4.3) |
| Monitor item sampling | supported but off by default; configurable per workflow/stage; redaction-aware (§4.4) |
| Aggregate memory guard | hard limits fail loudly by default; opt-in per-stage spill mode (§4.5) |
| ~~Resequencing overflow~~ | ⚠️ **SUPERSEDED (D25)** — no resequencer, so no overflow policy and no tombstones (§5.1) |
| Sampling redaction basis | `IsSecret`-based v1; field-level annotations only if proven insufficient (§4.4) |
| ~~Spill storage~~ | ⚠️ **SUPERSEDED (5.1.6)** — the preferred targets didn't exist (both providers returned `null` for `Blobs`). Now: SQLite for fast local disk, blob (Postgres/NATS/S3) for low-IOPS nodes, directory opt-in, **no implicit default** (§4.5) |
| Guard defaults | memory/workload-derived, per-stage overridable; calibrate in proving slice (§4.5) — ⏳ still outstanding |
| `stream.collect` guards | yes — one unified "bounded accumulator" policy across all buffering stages (§4.5) |
| Doc 07 error-schema alignment | agreed — per-item variant added to doc 07 §2 |
| ~~Resequencing scope~~ | ⚠️ **SUPERSEDED (D25)** — ordering holds at every cardinality, so there is no scope rule and the `Cardinality` hint loses its purpose (phase-plan Q7) |
| `StreamCardinality` | ✅ **DELETED (5.1.4, Q7)** — per-item vs whole-stream follows from *which interface a module implements*, not a declared enum. `ModuleSchemaDto.StreamShape` carries the fact to the designer; a guard test asserts the type is gone |
| ~~Spill encryption~~ | ⚠️ **SUPERSEDED (5.1.6)** — there is no secret-variable encryption to reuse (`IsSecret` is designer-only). Spill has its own `ISpillProtector` on ASP.NET Data Protection, on by default, loudly announced when off (§4.5) |
| Guard budget basis | host memory at engine start (engine-wide cap); per-region arbiter later if needed (§4.5) |
| ~~Spill providers v1~~ | ⚠️ **SUPERSEDED (5.1.6)** — S3 is no longer deferred; it arrives free via `IBlobStore`, and the orphan-sweep concern that justified deferring it was solved generally with `IBlobStore.ListAsync(prefix)` (§4.5) |
| Spill is still bounded | ✅ **CLARIFIED (5.1.6)** — `spill` changes *where* buffered items live, not whether there's a ceiling. The configured limit still applies; a guard test pins it (§4.5) |

## 7. Open questions

All first- and second-round questions are resolved (§6). Remaining items are
verification/calibration tasks rather than design decisions:

- [x] ~~Verify the secret-variable encryption mechanism is suitable for spill-at-rest use.~~
      ✅ **Resolved (5.1.6): there is nothing to reuse.** `IsSecret` is a designer-only flag and
      `docs/variables.md` states globals are stored in plaintext, so the question's premise was
      false. Spill built its own protector on ASP.NET Data Protection instead (§4.5).
- [x] ~~Decide the fate of `StreamCardinality`.~~ ✅ **Resolved (5.1.4): deleted.** Per-item vs
      whole-stream follows from which interface a module implements; `ModuleSchemaDto.StreamShape`
      carries it to the designer.
- [ ] ⏳ **Calibrate guard defaults from measurements.** The shipped `maxItems = 100 000` is an
      explicit placeholder. Needs the Docker-gated 1M-row Postgres run. *(Bounded memory itself is
      already proven Docker-free on SQLite, CSV and JSON in the default suite — this is for scale
      numbers, not for correctness.)* Note the property names settled as `maxItems`/`maxBytes`,
      not the `maxGroups`/`maxAccumulatorBytes` this document originally sketched.
- [ ] ⏳ **Host-memory-derived guard budget** (§4.5) — designed, not yet implemented; blocked on the
      same calibration.
- [ ] Revisit deferred items when triggered by demand: per-region memory arbiter, field-level
      sensitivity annotations, region-spanning parallel (§5.1c), source-offset checkpointing (§5.2),
      `builtin.stream.sort` (5.1.P5), incremental fold-based aggregate (5.1.P6),
      `builtin.file.xml.*` streaming (§3.4), binary payloads (§3.3).
      *(S3 spill has left this list — it shipped in 5.1.6 via `IBlobStore`.)*

## 8. Phasing

> 📋 **Detailed implementation plan:**
> [`phases/Phase5-1-StreamingDataPlane.md`](../../phases/Phase5-1-StreamingDataPlane.md) —
> sub-phase checklists (5.1.0–5.1.6 + post-MVP), designer-UX acceptance criteria per slice,
> and plan-level decisions D15–D26 / questions Q1–Q7. Summary below is superseded by that plan.

| # | Slice | Status |
| --- | --- | --- |
| 1 | Bridge modules + Option A guidance doc | ✅ 5.1.1 |
| 2 | Port capability + validation + designer rendering | ✅ 5.1.2 |
| 3 | Region executor + 3 modules (db.query → map → bulkinsert) as the proving slice | ✅ 5.1.3 (Akka.Streams, not raw channels — D27) |
| 4 | Remaining modules, per-item error routing, per-stage `maxWorkers` | ✅ 5.1.4 |
| 5 | Observability & sampling | ⏳ 5.1.5 — **not started** |
| 6 | Spill mode | ✅ 5.1.6 |
| — | Streaming child-execution stage (Reuse-mode parity, §3.5); source-offset checkpointing (§5.2) | 📋 post-MVP (5.1.P1 / 5.1.P2) |

**Sequencing note:** 5.1.6 was built before 5.1.5. Spill was unblocked (its questions were answered)
while observability still needs its own design pass, and building it first also forced the two
missing blob stores into existence — which several other features want anyway.
