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

### TO RESOLVE 🤔

- [ ] **Q1 Verification: secrets encryption for spill.** Confirm the secret-variable encryption
      mechanism (docs/variables.md "Secrets — current limits") is suitable for spill-at-rest;
      if not, pick a Data-Protection-based envelope (as 2.4.a.5 did for connections). **Blocks 5.1.6.**
- [ ] **Q2 Region executor internals: raw channels or Akka.Streams?** Design default is
      `System.Threading.Channels`; the [Phase 2.2 analysis](Phase2-2-Akka-Streams-Analysis.md)
      explicitly ear-marked linear back-pressured pipelines as the Streams sweet spot
      (`mapAsync` ≈ `maxWorkers`, built-in backpressure, supervision deciders ≈ per-item error
      policy). Spike both in 5.1.3's proving slice; pick by observability + test ergonomics,
      not micro-benchmarks. **Decide during 5.1.3, before 5.1.4 scales module coverage.**
- [ ] **Q3 `Cardinality` placement.** ✅ **RESOLVED (5.1.0):** a **per-module** default-interface
      member on `IStreamingWorkflowModule`. Per-port cardinality deferred to 5.1.P3 — no v1 module
      needs it, and per-module keeps the designer's resequencing rule simple.
- [ ] **Q4 Palette treatment.** New "Streaming" category vs. streaming *variants* surfaced on
      existing modules (e.g. db.query gains a streaming output port, no separate module)?
      V1 recommendation: same module, schema exposes both port sets; palette badge "🌊-capable".
- [ ] **Q5 `{{item}}` in the binding picker.** The `{{x}}` picker and ƒx builder
      (`ExpressionBuilder`) must offer `{{item}}` inside regions and *hide* `{{nodeId.port}}`
      upstream refs that aren't meaningful per-item — confirm exact rules with docs/variables.md
      semantics during 5.1.2.

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

### 5.1.1 — Bridges + Option A guidance 🌉 (~2 days)

- [ ] `builtin.stream.collect` (stream→array; bounded-accumulator guard from day one, D10)
      and `builtin.stream.fromitems` (array→stream).
- [ ] Guidance doc: chunked-ForEach patterns for users who need bounded memory *today*
      (docs/advanced-flow-control.md addendum).
- [ ] Unit tests for both bridges incl. guard-limit failures.

### 5.1.2 — Validation + designer rendering 🎨 (~1 week) — **the UX slice**

Engine/shared validation:
- [ ] Region detection (maximal stream-linked sub-graph) as a shared routine usable by engine
      *and* designer validation.
- [ ] Rules: shape mismatch; `SetVariable`-in-region; stream crossing construct boundaries
      (D14); resequencing cardinality rule (D8); required bridge suggestions.

Designer (Workflow.UI.Client):
- [ ] `NodePorts`/`NodeView`: diamond glyph + tooltip for streaming ports (D16).
- [ ] `EdgeLayer`: distinct stream-edge stroke; **drag-time refusal** of shape-mismatched
      connections (snap-back + toast, matching existing invalid-wire behavior).
- [ ] `CanvasView`: computed region halo over stream-linked nodes (D17).
- [ ] `GraphValidator`: all rules above as lints with fix-it hints ("insert Collect bridge").
- [ ] `VariableLint` + `{{x}}` picker + `ExpressionBuilder`: `{{item}}` availability inside
      regions per Q5; `SetVariable` lint.
- [ ] Connection properties popover: `BufferCapacity` editor on stream edges (D18).
- [ ] Palette treatment per Q4 (🌊 badge or category).
- [ ] bUnit tests for each designer behavior; screenshot/story added to docs/designer.md.

### 5.1.3 — Region executor proving slice ⚙️ (~2 weeks)

- [ ] **Q2 spike:** same 3-stage region on raw Channels vs Akka.Streams; decide + record D19.
- [ ] `StreamRegionExecutorActor`: stage lifecycle, bounded buffers, backpressure, linked
      cancellation, fault → region-as-node failure (parent trycatch applies).
- [ ] Streaming variants: `builtin.database.query` (source) → `builtin.transform.map`
      (1:1 transform) → `builtin.database.bulkinsert` (sink).
- [ ] Region entry/exit hand-off: scalar outputs (`itemCount`) dispatch normally downstream.
- [ ] History: per-node item counts + duration records (D-history, doc 06 §4.4).
- [ ] Monitor v1: `RunState` gains per-stage `itemsIn/itemsOut/rate`; `NodeInspector` shows
      them; SignalR `StreamStageProgress` event (throttled).
- [ ] Integration tests: 1M-row Docker-gated Postgres → map → bulkinsert with bounded memory
      assertion; cancellation mid-stream; guard calibration measurements (feeds D10 defaults).

### 5.1.4 — Scale-out: workers, errors, module coverage 🧯 (~2 weeks)

- [ ] `maxWorkers` competing consumers + optional resequencer (overflow policy, tombstones — D8/D9).
- [ ] Per-item error policy (`fail`/`skip`/error port) + per-item error envelope (D7);
      streaming `error` port wiring in designer (same diamond rules).
- [ ] **Designer UX:** stage knob editors in `PropertiesPanel` (maxWorkers, resequence,
      onResequenceOverflow, error policy); "⚠ unordered" node badge logic (D8); lint pairing
      resequence⇄maxWorkers.
- [ ] Remaining v1 streaming modules: `builtin.file.csv.read`/`.json.read` (sources),
      `builtin.transform.query` (filter, emits tombstones on drop), `builtin.file.*.write` (sinks).
- [ ] Streaming `aggregate` under the unified guard policy (no spill yet).

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

---

## Test & acceptance summary

- Every slice: unit tests beside the code; designer behaviors get bUnit coverage.
- Proving-slice acceptance (5.1.3): 1M rows end-to-end with peak managed memory below a fixed
  budget; region cancel < 2s; monitor shows live rates.
- UX acceptance (5.1.2/5.1.4): a user cannot *draw* an invalid streaming graph (drag-time
  refusal), and every streaming knob is discoverable in the properties panel without docs.
- Docs updated per slice: designer.md, variables.md, module-author-guide.md,
  advanced-flow-control.md, execution-monitor.md.
