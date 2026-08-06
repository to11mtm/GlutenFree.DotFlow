# 06 — Design: Streaming Data Plane (Chunked Ports) 🌊

> Closes the "streaming data plane" gap from [`02`](02-snaplogic-comparison.md) §2/§7.
> SnapLogic parity target: per-document streaming between Snaps (bounded memory for large ETL).
> Design-level; not implementation-ready. **Largest and riskiest of the four designs.**

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
- **Channel capacity (decided):** engine-level default (**64**) overridable at two levels —
  per workflow (`Metadata`/settings) and per connection via a new typed
  `ConnectionDefinition.BufferCapacity : int?` field (validated, schema-visible; pre-1.0 so
  the core-record change is acceptable).
- Region completes when the source stage completes and all channels drain; then normal
  port-dispatch resumes downstream (scalar outputs like `itemCount`, plus any collected
  results, feed non-streaming successors).
- Cancellation: region's CTS linked to `_executionCts` as usual. A stage fault cancels the
  region and surfaces as the region's node failure (parent trycatch applies).

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

### 3.4 First streaming-capable modules

`builtin.database.query` (reader → items), `builtin.file.csv.read` / `.json.read` (array
streaming), `builtin.transform.map` / `.query` (per-item), `builtin.database.bulkinsert` and
`builtin.file.*.write` (sinks), plus the two bridge nodes.

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
   accumulator guard**: hard `maxGroups` / `maxAccumulatorBytes` limits that **fail loudly by
   default**, with an opt-in per-stage `spill` mode (spill accumulator state to temp storage)
   for larger datasets (decided). Stream-stream `join` is **not** in v1 (SnapLogic's own
   sorted-stream/hang caveats show the cost); collect-then-join instead.
   - **Unified policy (decided):** the guard + spill options form one reusable "bounded
     accumulator" policy shared by every unbounded-memory stage — `aggregate`,
     `builtin.stream.collect`, and any future buffering stage (a `stream.sort`, 5.1.P5).
   - **Guard defaults (decided):** derive defaults from available memory and expected
     workload rather than fixed constants; user-overridable per stage. Concrete numbers to be
     calibrated with real measurements during the proving slice (phase 3).
   - **Spill storage (decided):** spill targets a **configured persistence provider** — v1
     prefers **database or NATS** over S3 for simplicity (single-writer semantics make orphan
     identification trivial; S3 multi-instance naming/sweep concerns deferred with it); a
     **local temp-file option** remains for testing and small datasets. **Cleanup contract:**
     spilled data is deleted on region completion, failure, *and* cancellation (tied to the
     region's CTS/terminal path) — no resource leaks; orphan sweep on engine startup as a
     backstop.
   - **Spill encryption (decided):** spilled state must be recoverable (it's working data,
     not redactable), so it is **encrypted at rest with the same mechanism used for secret
     variables**, not redacted. ⚠ Verify the current secrets mechanism (see
     docs/variables.md "Secrets — current limits") supports this use before phase 3.
   - **Guard-default budget (decided):** defaults derive from **host memory at engine
     start** (engine-wide cap shared by concurrent regions); a per-region budget arbiter is
     a possible later refinement.

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
| Spill storage | configured persistence provider (S3/NATS/DB) + temp-file option; cleanup on complete/fail/cancel + startup orphan sweep (§4.5) |
| Guard defaults | memory/workload-derived, per-stage overridable; calibrate in proving slice (§4.5) |
| `stream.collect` guards | yes — one unified "bounded accumulator" policy across all buffering stages (§4.5) |
| Doc 07 error-schema alignment | agreed — per-item variant added to doc 07 §2 |
| ~~Resequencing scope~~ | ⚠️ **SUPERSEDED (D25)** — ordering holds at every cardinality, so there is no scope rule and the `Cardinality` hint loses its purpose (phase-plan Q7) |
| Spill encryption | reversible encryption at rest, same mechanism as secret variables (§4.5) |
| Guard budget basis | host memory at engine start (engine-wide cap); per-region arbiter later if needed (§4.5) |
| Spill providers v1 | prefer DB/NATS over S3 (simplicity, orphan-sweep safety); temp-file for testing (§4.5) |

## 7. Open questions

All first- and second-round questions are resolved (§6). Remaining items are
verification/calibration tasks rather than design decisions:

- [ ] Verify the secret-variable encryption mechanism is suitable for spill-at-rest use
      (docs/variables.md notes current secrets limits) — before phase 3.
- [ ] Decide the fate of `StreamCardinality` now that D25 removed its purpose: delete it
      (recommended) or demote it to a designer-only "may change item counts" hint —
      phase-plan **Q7**, during 5.1.4.
- [ ] Calibrate guard defaults (`maxGroups`, `maxAccumulatorBytes`) from measurements —
      during phase 3 (proving slice).
- [ ] Revisit deferred items when triggered by demand: S3 spill provider (naming/sweep
      design), per-region memory arbiter, field-level sensitivity annotations,
      region-spanning parallel (§5.1c), source-offset checkpointing (§5.2),
      `builtin.stream.sort` (5.1.P5).

## 8. Phasing

> 📋 **Detailed implementation plan:**
> [`phases/Phase5-1-StreamingDataPlane.md`](../../phases/Phase5-1-StreamingDataPlane.md) —
> sub-phase checklists (5.1.0–5.1.6 + post-MVP), designer-UX acceptance criteria per slice,
> and plan-level decisions D15–D18 / questions Q1–Q5. Summary below is superseded by that plan.

1. Bridge modules + Option A guidance doc (cheap, immediate).
2. Port capability + validation + designer rendering.
3. `StreamRegionExecutorActor` + channels + 3 modules (db.query → map → bulkinsert) as the
   proving slice.
4. Remaining modules, per-item error routing, per-stage `maxWorkers`.
5. Streaming child-execution stage (Reuse-mode parity, §3.5); source-offset checkpointing if
   demanded (§5.2).
