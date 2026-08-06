# Phase 5.1: Streaming Data Plane 🌊

Made with 💖 by Ami-Chan! UwU ✨

[All Phases](README.md) | [Design Doc](../new-feature-design/snaplogic-analysis/06-streaming-data-plane-design.md) | [Akka.Streams Analysis](Phase2-2-Akka-Streams-Analysis.md)

> **Numbering note:** provisional — first slice of the post-Phase-4 "SnapLogic gap closure" track
> ([snaplogic-analysis/00-PLAN.md](../new-feature-design/snaplogic-analysis/00-PLAN.md)).

---

## Overview

Phase 5.1 ships **streaming regions**: opt-in stream-typed ports that let a chain of nodes
process large datasets (DB result sets, big CSV/JSON files, paged APIs) with **bounded memory**,
without materializing the whole set in the execution context. Batch-per-trigger stays the
default semantics — streaming is an explicit, validated, designer-visible capability
(Option B of the [design doc](../new-feature-design/snaplogic-analysis/06-streaming-data-plane-design.md)).

All design-level decisions are **already resolved** — see the design doc §6 (20 decisions).
This plan re-states only the ones that shape implementation slices, adds plan-level decisions,
and breaks the work down with **designer UX as a first-class track**, not an afterthought.

**Complexity:** 🔴 High — new engine construct (region executor), new module contract, core
model changes (`PortDefinition`, `ConnectionDefinition`), and designer surface across canvas,
properties panel, validation, and monitor.

> **CopilotNote:** Hot paths — `Workflow.Core/Models/*` (StreamItem, port/connection fields),
> `Workflow.Engine/Actors/StreamRegionExecutorActor.cs` (new, sibling of `LoopExecutorActor`),
> `Workflow.Modules/Abstractions/` (`IStreamingWorkflowModule`), streaming variants of existing
> modules, and `Workflow.UI.Client` (`GraphValidator`, `VariableLint`, `NodePorts`,
> `NodeView`/`EdgeLayer`, `PropertyEditor`, `RunState`). Per the
> [Phase 2.2 Akka.Streams analysis](Phase2-2-Akka-Streams-Analysis.md), regions are exactly the
> "linear back-pressured pipeline" shape where Streams/channels shine — the region executor may
> use `System.Threading.Channels` (design default) or Akka.Streams internally (**Q2**), while
> the surrounding graph stays on raw actors.

### Confirmed Design Decisions ✅

Carried from the design doc §6 (abbreviated — the design doc is normative):

| # | Decision |
|---|----------|
| **D1 Opt-in regions, not engine rewrite** | Streaming is a port capability; maximal stream-linked sub-graphs form regions run by `StreamRegionExecutorActor`; graph semantics elsewhere unchanged. Full-rewrite assessment rejected ([09](../new-feature-design/snaplogic-analysis/09-full-streaming-rewrite-assessment.md)). |
| **D2 Shape rule + bridges** | Streaming outputs connect only to streaming inputs; `builtin.stream.collect` / `builtin.stream.fromitems` bridge the two worlds. |
| **D3 Bounded channels, capacity 64 default** | Overridable per workflow and per connection via new typed `ConnectionDefinition.BufferCapacity : int?`. |
| **D4 JSON-only `StreamItem`, payload union** | `JsonPayload` v1; `BinaryPayload` reserved (additive later). |
| **D5 `IStreamingWorkflowModule`** | Optional interface beside `IWorkflowModule`; sources/transforms/sinks by how they use `input`/output enumerables. |
| **D6 Variables read-only in regions** | Snapshot at region start; `SetVariable` forbidden inside a region (validated). `{{item}}` token for per-item templating. |
| **D7 Per-item error policy** | `fail` (default) / `skip` / streaming `error` port; error envelope = failing item + per-item error document (doc 07 §2 schema, `item`+`offset` fields). |
| **D8 `maxWorkers` + optional resequencer** | 1:1-cardinality stages only (module `Cardinality` hint, design-time validated); `onResequenceOverflow: backpressure\|fail`; sequence tombstones from skipping stages. Designer badges "⚠ unordered". |
| **D9 Ordering contract** | Source order iff all stages single-worker; otherwise only downstream of a resequenced stage. |
| **D10 Bounded-accumulator policy (unified)** | `maxGroups`/`maxAccumulatorBytes` fail loudly by default; opt-in `spill` per stage; shared by aggregate, `stream.collect`, resequencer. Guards derived from host memory at engine start; calibrated in the proving slice. |
| **D11 Spill storage** | DB/NATS providers preferred v1 (+ temp-file for tests); encrypted at rest via the secret-variable mechanism; cleanup on complete/fail/cancel + startup orphan sweep. S3 deferred. |
| **D12 Checkpointing deferred, enablers reserved** | `SourceOffset(Token: string, Sequence: long)` on `StreamItem`; optional `StartFrom` source capability. No resume machinery in 5.1. |
| **D13 Sampling off by default** | Monitor may sample N items/stage; per-workflow/stage config; `IsSecret`-based redaction. |
| **D14 Region-spanning parallel out of scope** | Regions compose inside parallel branches (b) and via per-stage workers (a); streams never cross construct boundaries (c). |

**Plan-level decisions (new):**

| # | Decision |
|---|----------|
| **D15 Designer UX is a per-slice acceptance criterion** | Every engine-visible capability lands with its designer surface in the *same* slice (port glyphs with validation; knobs with their editors; runtime with monitor). No "UI later" backlog. |
| **D16 Stream ports render as diamonds** | Following SnapLogic's circle/diamond precedent and doc 06 §3.1: `NodeView`/`NodePorts` render streaming ports with a distinct glyph + tooltip; `EdgeLayer` draws stream edges with a distinct stroke (dashed/animated). Shape mismatch is refused at drag time, not just linted after. |
| **D17 Region halo reuses `RegionId` machinery** | Designer auto-assigns a computed region grouping for stream-linked nodes and renders a halo (same visual language as loop-body regions); the engine continues to *ignore* `RegionId` — region detection is connection-driven. |
| **D18 Connection properties UI** | Selecting a stream edge opens a small properties popover (BufferCapacity) — first time connections get editable properties; `EdgeLayer` hit-test already exists. |
| **D19 Streaming category in the palette** | Bridges ship under a new **"Streaming"** category (Q4 partially answered: bridges are genuinely new *nodes*; streaming-capable *existing* modules will instead grow streaming ports and a 🌊 badge, no duplicate module ids). |
| **D20 `IStreamTerminalModule` for stream→batch stages** | Found during 5.1.1: `ExecuteStreamAsync` can only yield items, so a stage with a streaming input but batch outputs (`stream.collect`, and every sink reporting `rowsAffected`/`itemCount`) had no way to express itself. Terminal stages implement `ExecuteTerminalAsync` returning a plain `ModuleResult`, so the engine's existing port dispatch handles the region edge with no special cases. Design doc 06 §3.3 updated. |
| **D21 Region detection is mirrored, not shared** | `Workflow.UI.Client` deliberately has no `Workflow.Core` reference (Phase 3.3 D2 keeps a React port additive), so the designer's `StreamGraph` and the engine's future region detector are two implementations of one rule. They get a **drift-guard test** (the `SplitPreviewDriftGuardTests` pattern) in 5.1.3 rather than a shared library. |
| **D19 Akka.Streams is the region executor** | Resolved in 5.1.3 by spike — see the Q2 entry for the evidence table and the long-term strategic reasoning (triggers/queues, Reuse-mode child stages, throttled connectors). Kept an internal detail: modules only ever see `IAsyncEnumerable<StreamItem>`. |
| **D22 v1 regions are linear chains** | `StreamRegion.ChainOrder()` throws on branching inside a region. Broadcast/merge inside a region waits for the operators that need it; the alternative — materializing a graph shape we can't yet validate or explain in the designer — trades a clear error for a mysterious hang. |
| **D23 A region runs as one dispatch unit** | `WorkflowExecutor` starts the pipeline when dispatch reaches the region's **source** and no-ops for other members; the region completes or fails as a whole. Members share Running/Completed transitions so the monitor shows the pipeline lighting up together, which is what physically happens. Failure routes through the ordinary `HandleNodeFailure` path, so per-node retries, error handling and enclosing try/catch keep working unchanged. |
| **D24 Stage progress rides the existing EventStream** | `StreamRegionProgress` is published to the Akka `EventStream`, which the Phase 3.2 `ExecutionEventBridge` already forwards to SignalR. No hub changes, and `Workflow.Engine` still has no SignalR dependency. |
| **D25 Ordering is a stage shape, not a buffer** | Resolved by the Q6 spike: `maxWorkers` gets **one** companion knob, `ordered` (default **true** → `SelectAsync`, opt out → `SelectAsyncUnordered`). **Deleted from the design:** the resequencing buffer, `onResequenceOverflow`, and sequence tombstones. Ordering holds at every cardinality because Akka orders by input slot, and in-flight work is bounded by the parallelism, not the stream length. |
| **D26 `maxWorkers` needs a per-item contract** | Surfaced while verifying Q6: today's `ExecuteStreamAsync(context, IAsyncEnumerable, ct)` hands the module the **whole stream**, so the module owns the loop and the engine cannot parallelise or order it. 5.1.4 therefore adds an optional per-item entry point (`ProcessAsync(StreamItem) → IEnumerable<StreamItem>`); stream-shaped modules keep working but run single-worker. Per-item is also the *simpler* contract for authors — no async-iterator boilerplate — and it's what makes D25's free ordering reachable. |

### TO RESOLVE 🤔

- [ ] **Q1 Verification: secrets encryption for spill.** Confirm the secret-variable encryption
      mechanism (docs/variables.md "Secrets — current limits") is suitable for spill-at-rest;
      if not, pick a Data-Protection-based envelope (as 2.4.a.5 did for connections). **Blocks 5.1.6.**
- [ ] **Q2 Region executor internals: raw channels or Akka.Streams?** ✅ **RESOLVED (5.1.3, D19):
      Akka.Streams.** A throwaway spike tested the five things the executor actually needs, and all
      five passed decisively:

      | Need | Akka.Streams answer | Spike result |
      | --- | --- | --- |
      | Consume our module contract | `Source.From(IAsyncEnumerable)` | 1 000 items, no adapter needed |
      | Bounded memory / backpressure | `.Buffer(n, Backpressure)` | producer emitted ~22 while consumer took 5 (unbounded would be 10 000) |
      | `maxWorkers` (D8) | `SelectAsync(n)` / `SelectAsyncUnordered(n)` | ordered **and** unordered variants built in |
      | Per-item `skip` policy (D7) | supervision `Directive.Resume` | 9/10 items survived a thrower |
      | Region cancellation | `KillSwitch` | infinite stream stopped in 131 ms |

      **Long-term weighting (as directed):** the choice was made on strategic reach, not just this
      slice. The same library is the natural implementation for work already designed elsewhere —
      queue-fed and scheduled triggers with `RestartSource` backoff and `Throttle`
      ([08](../new-feature-design/snaplogic-analysis/08-always-on-serving-design.md) §2.1/§2.2),
      Reuse-mode child stages ([05](../new-feature-design/snaplogic-analysis/05-subworkflow-design.md)),
      rate-limited connectors, and `GroupedWithin` batching. The
      [Phase 2.2 analysis](Phase2-2-Akka-Streams-Analysis.md) reached the same conclusion from the
      other direction: keep graph-shaped orchestration on raw actors, adopt Streams exactly at the
      "linear back-pressured pipeline" seams. A streaming region *is* that seam.

      **Cost:** one new package (`Akka.Streams`, same family/version as the actor core, so no
      version-skew risk). **Contained:** modules only ever see `IAsyncEnumerable<StreamItem>`, so
      the choice is swappable without touching a single module.

- [x] ✅ **Q6 (from the 5.1.3 spike): is the resequencing design redundant?** **RESOLVED — yes,
      drop it entirely (D25).** Your instinct that a resequencer might still be needed for
      *non*-1:1 stages was the right thing to check, so it was checked with a spike rather than
      argued. Result: **ordering survives at every cardinality**, because Akka.Streams orders by
      **input slot**, not by a sequence key:

      | Stage shape | `SelectAsync(4)` + `SelectMany`, random delays | Ordered? |
      | --- | --- | --- |
      | 1:1 transform | 30 items | ✅ |
      | **Filter (0..1 per input)** | 15 of 30 survive | ✅ `0,2,4,6,8,…` |
      | **Splitter (1..N per input)** | 15 outputs from 15 inputs | ✅ `100,200,201,400,…` |
      | Unordered opt-in | `SelectAsyncUnordered(4)` | ❌ (as intended) |
      | Memory under a straggler | 200 items, item 0 slow | max 4 in flight — bounded by parallelism, not stream length |

      A stage that emits 0 outputs simply contributes nothing at its slot, and a stage that emits
      N keeps them contiguous within its slot — so **sequence tombstones, `SourceOffset`-keyed
      resequencing and `onResequenceOverflow` all solve a problem that doesn't exist.** The one
      shape this doesn't cover is a stage that consumes the whole stream at its own rhythm
      (`aggregate`, an internal batcher) — but "restore source order" is meaningless there anyway,
      and those stages simply run single-worker.

      **Your "generic-ish sort module" idea is kept, but as a different thing** — see **5.1.P5**.
      A `builtin.stream.sort` is a *user-facing data operation* ("order by `$.customerId`"), not
      ordering repair; it needs the whole stream in memory, so it belongs under the bounded-
      accumulator policy alongside `collect`. Worth having; unrelated to `maxWorkers`.

      Design doc 06 §5.1/§6 updated; `SourceOffset` **stays** (it was reserved for checkpointing in
      D12, which is untouched by this).
- [ ] **Q3 `Cardinality` placement.** ✅ **RESOLVED (5.1.0):** a **per-module** default-interface
      member on `IStreamingWorkflowModule`. Per-port cardinality deferred to 5.1.P3.
      ⚠️ **Superseded in part by Q6/D25:** its original job — gating which stages could be
      resequenced — no longer exists, since ordering now holds at every cardinality. The hint is
      currently **vestigial**. See **Q7**.
- [ ] ⚠️ **Q7 (new, from Q6): repurpose or remove `StreamCardinality`?** The distinction that
      actually matters after D25/D26 is **per-item** (can run with `maxWorkers > 1`) vs
      **whole-stream** (owns its own iteration → must run single-worker, e.g. `aggregate`), which
      falls out of *which interface a module implements* rather than a declared enum. Options for
      5.1.4: (a) delete `StreamCardinality` and let the implemented interface speak; (b) keep it
      purely as a designer hint ("this stage may change item counts"). Recommendation: **(a)** —
      a knob with no behaviour attached is a knob that will eventually lie. Removing it is a
      pre-1.0 breaking change to an interface nothing ships against yet.
- [ ] **Q4 Palette treatment.** ⚙️ **PARTIALLY RESOLVED (5.1.1, D19):** the two *bridges* are new
      nodes in a new **"Streaming"** category. Still open for **existing** modules gaining
      streaming ports (5.1.3+): a 🌊 "stream-capable" badge on the same module id (recommended)
      vs. separate module ids.
- [ ] **Q5 `{{item}}` in the binding picker.** ✅ **RESOLVED (5.1.2):** the picker offers `{{item}}`
      only when the node sits inside a streaming region (listed first, since it's *the* thing you
      bind to there), and the lint gives a targeted error when `{{item}}` is used outside one.
      Upstream `{{nodeId.port}}` refs were left available — they resolve to the region's inputs, and
      hiding them would break legitimate config references.

---

## Pre-Existing Work (reused) ✅

| Component | File | Use |
|-----------|------|-----|
| Sub-graph orchestration pattern | `Workflow.Engine/Actors/LoopExecutorActor.cs`, `SubGraphExecutor.cs`, `ParallelExecutionCoordinator.cs` | `StreamRegionExecutorActor` is a sibling; hierarchical cancellation + `PipeTo` persistence patterns reused |
| Port-aware dispatch | `Workflow.Engine/Actors/DispatchCore.cs`, `ModuleResult.WithActivePorts` | Region entry/exit hand-off to normal dispatch |
| Module schema & DTOs | `Workflow.Core/Models/ModuleSchema.cs`, `Workflow.Api/Contracts/Modules/ModuleContracts.cs` | Extended with `IsStreaming`, `Cardinality` |
| Designer graph state | `Workflow.UI.Client/Designer/State/{DesignerDocument,NodePorts,GraphValidator,VariableLint}.cs` | All four extended (5.1.2) |
| Canvas & edges | `Designer/Components/{CanvasView,NodeView,EdgeLayer}.razor` | Diamond ports, stream edge styling, region halo |
| Properties panel | `Designer/Components/{PropertiesPanel,PropertyEditor}.razor` | Stage knobs (maxWorkers, resequence, error policy, guards) |
| Monitor state & UI | `Execution/State/RunState.cs`, `Components/NodeInspector.razor`, SignalR `RealTimeClient.cs` | Item-count/rate metrics, region progress |
| Bounded-accumulator prior art | `builtin.transform.aggregate` (Phase 2.6) | Wrapped by the unified guard policy |
| Blob/persistence stores | `IBlobStore`, NATS/DB providers (Phase 2.1) | Spill storage (5.1.6) |

---

## Sub-Phase Checklist

### 5.1.0 — Core contracts 📐 (~3 days) ✅ **COMPLETE**

- [x] `StreamItem` record: `Payload` union (`JsonPayload` now, `BinaryPayload` reserved),
      `SourceOffset?(Token, Sequence)`, engine-internal tombstone flag.
      → `Workflow.Core/Models/StreamItem.cs`
- [x] `IStreamingWorkflowModule` in `Workflow.Modules/Abstractions` (+ `Cardinality` hint per Q3).
      → `Workflow.Modules/Abstractions/IStreamingWorkflowModule.cs`; **Q3 resolved: per-module hint**
      (default-interface member, so existing modules are untouched and a module can override it
      explicitly when it filters/splits).
- [x] `PortDefinition.IsStreaming` (+ `PortDefinition.CreateStreaming` factory);
      `ConnectionDefinition.BufferCapacity : int?` (typed, D3).
- [x] Serialization round-trip + structural-equality tests for changed core records.
      → `Workflow.Tests/Core/Models/StreamingContractsTests.cs` (18 tests)
- [x] Module contracts DTO surface (`ModuleContracts.cs`) exposes new flags to the UI:
      `PortDefinitionDto.IsStreaming`, `ModuleDetailsDto.StreamCapable` + `Cardinality`.
- [x] **UI wire-DTO mirrors + designer fidelity** (not in the original list, but required so the
      designer can't silently drop the new fields on save): `Workflow.UI.Client/Api/Dtos/*`,
      `DesignerConnection` FromDto/ToDto/Clone + 2 bUnit guards.
- [x] **Docs:** module-author-guide.md §7a "Streaming modules".

**Result:** solution builds clean; `Workflow.Tests` 1608 passed (+18 new), `Workflow.Tests.UI`
661 passed (+2 new). Pre-existing flaky tests unrelated to this slice (they pass in isolation).

### 5.1.1 — Bridges + Option A guidance 🌉 (~2 days) ✅ **COMPLETE**

- [x] `builtin.stream.collect` (stream→array; bounded-accumulator guard from day one, D10)
      and `builtin.stream.fromitems` (array→stream).
      → `Workflow.Modules/Builtin/Stream/*` (auto-discovered; new "Streaming" palette category)
- [x] **Unified bounded-accumulator guard** → `Workflow.Modules/Streaming/BoundedAccumulator.cs`:
      `maxItems` (default 100 000, placeholder pending 5.1.3 calibration) + optional `maxBytes`
      (measured only when configured), fail-loud via `StreamAccumulatorLimitException`, and
      `Spill` **rejected with a "not until 5.1.6" message** rather than silently ignored.
- [x] **`IStreamTerminalModule`** (contract addition — see D20 below).
- [x] Guidance doc: chunked-ForEach patterns for bounded memory *today*
      → `docs/advanced-flow-control.md` §"Working with Large Data" (+ TOC and module index).
- [x] Unit tests for both bridges incl. guard-limit failures.
      → `Workflow.Tests/Modules/Stream/StreamBridgeTests.cs` (19 tests)

**Result:** `Workflow.Tests` 1629 passed / 1632 total; the 3 failures are the known pre-existing
flaky set (all present in the pre-change baseline). Module discovery + validator accept both new
modules unchanged.

### 5.1.2 — Validation + designer rendering 🎨 (~1 week) — **the UX slice** ✅ **COMPLETE**

Engine/shared validation:
- [x] Region detection (maximal stream-linked sub-graph) → `Designer/State/StreamGraph.cs`
      (`Regions`, `RegionIndexByNode`, `IsStreamingPort/Edge`, `IsShapeMismatch`, `BridgeFor`).
      ⚠️ **Not literally shared** — `Workflow.UI.Client` has no `Workflow.Core` reference by design
      (Phase 3.3 D2), so the engine gets its own copy in 5.1.3 **plus a drift-guard test**, the same
      pattern as `SplitPreviewDriftGuardTests`. Recorded as **D21**.
- [x] Rules → `GraphValidator.ValidateStreaming` (wired into `Validate`): shape mismatch (names the
      bridge to insert), `SetVariable`-in-region, stream crossing a construct boundary.
- [ ] Resequencing/cardinality rule — **moved to 5.1.4**, where `maxWorkers` exists (there is
      nothing to validate against before then).

Designer (Workflow.UI.Client):
- [x] `NodeView`: diamond glyph (`df-port--stream`, `data-port-streaming`) + a tooltip that teaches
      the rule; `StreamGraph.IsStreamingPort` is the single source of truth.
- [x] `EdgeLayer`: distinct stream stroke/colour + `data-edge-streaming`.
- [x] `CanvasView`: **drag-time refusal** of shape-mismatched wires (`IsCompatibleInput`) and the
      🌊 region halo (`StructuralRegions.UnionBounds` promoted to public and reused).
- [x] `VariableLint` + `{{x}}` picker: `{{item}}` offered **inside** regions, with a targeted error
      when used outside one. Also fixed an adjacent bug — `{{input.field}}` was linted as a missing
      *node* named `input`; reserved roots are now explicit (`SelfInputRoot`, `StreamItemRoot`).
- [x] Connection properties: buffer-capacity editor on stream edges only, undoable via the new
      `EditConnectionBufferCommand` (D18).
- [x] Palette: 🌊 stream-capable badge (`ModuleSummaryDto.StreamCapable` added server + client).
- [x] Tests: `StreamingTopologyTests` (23 state specs) + `StreamingCanvasTests` (11 bUnit specs).

**Result:** `Workflow.Tests.UI` **695 passed / 0 failed** (+34 new). `Workflow.Tests` 1628/1632 —
the 4 failures are the known flaky API set (all pass in isolation; unrelated files).

### 5.1.3 — Region executor proving slice ⚙️ (~2 weeks) ✅ **COMPLETE**

- [x] **Q2 spike** — decided **Akka.Streams** (D19; full reasoning + spike numbers in the Q2 entry
      above). Spike project was throwaway and has been deleted.
- [x] Engine-side region detection → `Workflow.Engine/Streaming/StreamRegionDetector.cs`
      (`Regions`, `RegionOf`, `StreamRegion.SourceNodeIds/TerminalNodeIds/ChainOrder`). Mirrors the
      designer's `StreamGraph` per **D21**, with a documented drift guard in both test suites.
      Detection is connection-driven; `RegionId` is explicitly ignored (test proves it).
- [x] `StreamRegionRunner` → materializes a region as an Akka.Streams graph: per-edge bounded
      buffers (`BufferCapacity` → engine default 64), backpressure, linked cancellation,
      terminal-stage hand-off returning a plain `ModuleResult`, per-stage item counts + duration.
      v1 shape is a **linear chain** (**D22**); branching inside a region fails loudly.
- [x] **`WorkflowExecutor` wiring** → `StreamRegionExecutor` actor (sibling of `LoopExecutorActor`):
      `ExecuteNode` detects region membership, starts the pipeline at the region's **source** and
      no-ops for other members; `StreamRegionCompleted` marks every member Completed, stores the
      terminal outputs, and resumes ordinary port dispatch; `StreamRegionFailed` routes through
      `HandleNodeFailure` so retries/error-handling/try-catch behave exactly as for a batch node.
      Region detection is lazy and cached — a non-streaming workflow pays one scan and nothing else.
- [x] Region entry/exit hand-off: terminal outputs feed downstream batch nodes; non-terminal members
      expose `itemCount` as their node output.
- [x] Monitor v1: `StreamRegionProgress` published to the Akka `EventStream` (per-stage item counts
      + duration), which the **existing** Phase 3.2 `ExecutionEventBridge` already forwards to
      SignalR — no hub changes and no engine→API coupling.
- [x] Tests → `Workflow.Tests/Engine/Streaming/` (24 total): detection + drift-guard fixtures (9),
      runner incl. **bounded-memory proof**, cancellation, stage failure, real 5.1.1 bridge modules
      (10), and **end-to-end through the real `WorkflowExecutor`** (5) — region as one unit, all
      members Completed, stage failure fails the execution, per-stage counts published, plus a
      regression guard that a **non-streaming workflow is entirely unaffected**.
- [ ] Streaming variants of `builtin.database.query` / `builtin.transform.map` /
      `builtin.database.bulkinsert` — **moved to 5.1.4** with the rest of module coverage.
- [ ] Docker-gated 1M-row Postgres integration test + guard calibration — **moved to 5.1.4**,
      follows the streaming database modules.

**Result:** solution builds clean. `Workflow.Tests` 1650/1656, `Workflow.Tests.UI` **695/695**.
The unit-suite failures are pre-existing parallel-collection flakiness — **verified** by running the
suite with all streaming tests excluded (5 failures out of 1632) and by running every failing class
in isolation (95/95 pass).

### 5.1.4 — Scale-out: workers, errors, module coverage 🧯 (~2 weeks)

- [x] **Q6 resolved** ahead of the slice (D25) — resequencing buffer, overflow policy and
      tombstones are **deleted from the design**, not built.
- [ ] **Per-item stage contract (D26)** — an optional `ProcessAsync(StreamItem) → IEnumerable<StreamItem>`
      entry point; the runner uses `SelectAsync(maxWorkers)`/`SelectAsyncUnordered` + `SelectMany`.
      Stream-shaped modules keep working, pinned to one worker. **Blocks `maxWorkers`.**
- [ ] **Q7**: delete `StreamCardinality` (recommended) or demote it to a pure designer hint.
- [ ] `maxWorkers` + `ordered` (default true) per stage — two knobs, no buffers (D25/D9).
- [ ] Per-item error policy (`fail`/`skip`/error port) + per-item error envelope (D7);
      streaming `error` port wiring in designer (same diamond rules). `skip` is now trivial —
      a stage returning zero outputs contributes nothing at its slot.
- [ ] **Designer UX:** stage knob editors in `PropertiesPanel` (maxWorkers, ordered, error policy);
      "⚠ unordered" node badge when `maxWorkers > 1 && !ordered`; lint pairing the two.
- [ ] Streaming variants of `builtin.database.query` (source), `builtin.transform.map` (per-item),
      `builtin.database.bulkinsert` (sink) — **carried over from 5.1.3**.
- [ ] Remaining v1 streaming modules: `builtin.file.csv.read`/`.json.read` (sources),
      `builtin.transform.query` (filter — per-item returning 0 or 1), `builtin.file.*.write` (sinks).
- [ ] Streaming `aggregate` under the unified guard policy (whole-stream shape, single worker).
- [ ] Docker-gated 1M-row Postgres → map → bulkinsert test with a bounded-memory assertion, and
      guard-default calibration from those measurements — **carried over from 5.1.3**.

### 5.1.5 — Observability & sampling 🔭 (~1 week)

- [ ] Item sampling per D13: config surface (workflow + stage), redaction, off by default.
- [ ] Monitor: region progress panel (per-stage rates, buffer fill %, sampled items viewer
      with redaction notice); execution-detail replay shows final stage counters.
- [ ] Metrics (`IWorkflowMetrics`): stage throughput/error counters for dashboards.
- [ ] docs/execution-monitor.md + docs/variables.md updates.

### 5.1.6 — Spill mode 💾 (~1 week, gated on Q1)

- [ ] Unified bounded-accumulator policy object; spill to DB/NATS provider + temp-file (D11).
- [ ] Encryption at rest per Q1 resolution; cleanup on terminal states + startup orphan sweep.
- [ ] Spill option surfaced in `PropertyEditor` for guard-bearing stages, with "prefer
      limits; spill is a scale valve" helper text.
- [ ] Docker-gated integration tests (NATS + Postgres spill; crash-cleanup sweep).

### Post-MVP slices (tracked, not scheduled)

- **5.1.P1** Streaming child-execution stage (Reuse-mode parity — needs Phase 5.2 sub-workflows, [05](../new-feature-design/snaplogic-analysis/05-subworkflow-design.md)).
- **5.1.P2** Source-offset checkpointing (`StartFrom`) — enablers already in D12.
- **5.1.P3** S3 spill provider (naming/orphan-sweep design), binary payloads, per-port cardinality.
- **5.1.P4** Region-spanning parallel (D14 revisit) — only on demonstrated demand.
- **5.1.P5** 🔀 **`builtin.stream.sort`** — a user-facing "order by `$.field`" stage (your Q6 idea).
  Distinct from ordering repair, which D25 made unnecessary: this is a *data* operation. It must
  buffer the whole stream, so it ships under the unified bounded-accumulator policy (§4.5) with
  `collect`, and gains real value once spill (5.1.6) exists. Pairs naturally with a future
  `sorted: true` hint that would let `aggregate`/`join` stream instead of accumulate.

---

## Test & acceptance summary

- Every slice: unit tests beside the code; designer behaviors get bUnit coverage.
- Proving-slice acceptance (5.1.3): 1M rows end-to-end with peak managed memory below a fixed
  budget; region cancel < 2s; monitor shows live rates.
- UX acceptance (5.1.2/5.1.4): a user cannot *draw* an invalid streaming graph (drag-time
  refusal), and every streaming knob is discoverable in the properties panel without docs.
- Docs updated per slice: designer.md, variables.md, module-author-guide.md,
  advanced-flow-control.md, execution-monitor.md.
