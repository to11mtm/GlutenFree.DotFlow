# 09 — Assessment: Rewriting DotFlow as a Document-Streaming Engine 🔥

> Stakeholder evaluation of "Option C" from [`06-streaming-data-plane-design.md`](06-streaming-data-plane-design.md)
> §2 — making per-document streaming (the SnapLogic model, [`02`](02-snaplogic-comparison.md) §2)
> DotFlow's *native* execution semantics, rather than an opt-in region capability.
> **This is an assessment, not a proposal.** Recommendation: §7.

## 1. What "document-streaming engine" means

Today a trigger produces **one run = one traversal** of the graph with a shared context.
In a streaming engine, a run is a **stream session**: the source node emits N JSON documents
and every downstream node processes documents one at a time with backpressure. "The output of
a node" stops being a value and becomes a flow.

## 2. What survives (the good news)

| Asset | Survives? | Notes |
| --- | --- | --- |
| `WorkflowDefinition` / `NodeDefinition` / `ConnectionDefinition` | ✅ ~intact | a node-and-port graph describes a streaming topology fine (SnapLogic's `.slp` is exactly this) |
| `ModuleSchema` (ports/properties), typed ports | ✅ | port `DataType` becomes the *document/element* type |
| Designer canvas, module manager, Script Studio | ✅ mostly | new stage-state rendering needed in monitor |
| REST API shell, auth, SignalR plumbing | ✅ | event payloads change |
| Persistence *providers* (SQLite/Postgres/NATS/S3) | ✅ | stored *shapes* change (see §3.5) |
| Sandboxed scripting engines (Jint/Roslyn/MoonSharp) | ✅ | invoked per document |
| **Akka.Streams** | ✅✅ | Akka.NET already ships a mature backpressured streaming graph DSL — the rewrite is realistically "compile `WorkflowDefinition` → Akka.Streams `GraphDsl`", not hand-rolled stage actors. This is the main reason the rewrite is *large* rather than *infeasible*. |

## 3. What breaks (the cost, in decreasing severity)

### 3.1 The template language — the composition break
`{{nodeId.port}}` grants random access to *any* upstream node's output. That semantic only
exists because a run is one traversal with a snapshot context (`docs/variables.md`). In a
stream there is no "the" output of node X — only the document currently passing. Consequence:
- Reference model collapses to current-document refs (`{{doc.field}}`, SnapLogic's `$field`).
- `{{input}}` changes meaning from "the run's incoming value" to "this document".
- **Every existing workflow's bindings change meaning.** No mechanical migration exists for
  cross-node references; they must be redesigned (usually into per-document enrichment joins).

### 3.2 Variables
Staged writes and global→workflow→initial→run-input precedence assume one logical "now" per
run. Per-document `SetVariable` is a shared-state race. Required redesign: variables become
read-only stream configuration; mutable accumulation moves into explicit stateful operators
(aggregate/accumulator stages). `builtin.setvariable`/`getvariable` semantics gone.

### 3.3 Control flow inversion (all 9+ flow modules re-conceived, not ported)
| Today | Streaming equivalent |
| --- | --- |
| `builtin.loop.foreach` / `while`, `break`, `continue` | **cease to exist** — the stream *is* the loop |
| `builtin.condition` / `switch` | per-document routers (Router/Filter semantics) |
| `builtin.trycatch` (+ supervised sub-graph actors) | per-document error ports / error streams |
| `builtin.parallel` / `fanout` / `fanin` / `partition` | stage parallelism, broadcast, merge, partition operators |
| `builtin.database.transaction` | per-batch/windowed transactions — genuinely hard |

The actor-supervised sub-graph pattern (`LoopExecutorActor`, `TryCatchExecutorActor`,
`SubGraphExecutor`) — one of DotFlow's distinctive strengths ([`01`](01-dotflow-architecture.md) §6) — is retired.

### 3.4 Module contract
`ExecuteAsync(context) → ModuleResult` runs once per run. Streaming needs per-document
processing (≈ `Flow<Doc, Doc>`). **All ~40 built-in modules rewritten**, and every custom
`.wfmod` in the field breaks (an adapter can wrap old modules as one-in/one-out stages, but
anything using variables, active ports, or sub-graph requests won't adapt cleanly).

### 3.5 Execution model, history & observability
`WorkflowExecutor`'s traversal loop, node states (Completed/Failed/Skipped), per-node
execution records with outputs, terminal-state snapshots, the execution monitor's node
timeline — all replaced by stage lifecycles, throughput/watermark metrics, per-item error
counters. Mid-stream failure/resume needs real checkpointing (does not exist today; ties to
[`06`](06-streaming-data-plane-design.md) open questions).

### 3.6 Semantics you must now own
Ordering guarantees across parallel stages, join/window memory bounds (SnapLogic's own docs
warn of Join hangs and Gate memory blowups), backpressure vs. deadlock in cyclic-ish graphs,
at-least-once vs. exactly-once on redelivery. These are permanent engineering taxes of the
model, not one-time costs.

## 4. Size & shape of the effort

Rough proportions against the current codebase (not a schedule commitment):

| Area | Impact |
| --- | --- |
| `Workflow.Engine` | ~rewritten (compile-to-Akka.Streams + new runtime services) |
| `Workflow.Modules*` | all modules touched; flow modules replaced by operator set |
| Template/binding + variables | redesigned language & semantics (spec work, not just code) |
| `Workflow.Core` | models mostly stable; `ModuleResult`, requests, execution states replaced |
| API/SignalR/UI monitor | event model + monitor rebuilt; designer largely kept |
| Persistence | providers kept; stored schemas + history model replaced |
| Tests | effectively a new suite for engine/modules |

**Order of magnitude: a v2 of the product** — comparable to everything built since Phase 2 —
plus a breaking-change migration for every existing workflow and custom module. Two engines
would need parallel maintenance during any transition window.

## 5. What we'd gain

- Native large-volume ETL with bounded memory (the one 🔴 gap DotFlow can't otherwise close fully).
- Closer SnapLogic porting fidelity (per-document semantics map 1:1; importer gets easier).
- Ultra-style continuous pipelines fall out naturally (a stream session over a queue source
  subsumes much of [`08`](08-always-on-serving-design.md)).

## 6. What we'd lose

- The **explicit, analyzable batch model**: one run = one auditable snapshot (per-node inputs/
  outputs in history) — a genuine differentiator for debugging and compliance.
- `{{nodeId.port}}` cross-graph references and typed scoped variables — the features users
  touch most.
- Supervised control-flow-as-nodes (trycatch/loop actors with independent timeouts).
- Backward compatibility: definitions survive structurally but not semantically; the
  ecosystem (docs, examples, tests, custom modules) resets.

## 7. Recommendation

**Do not rewrite.** The incremental path already designed in
[`06`](06-streaming-data-plane-design.md) (streaming *regions*) is Option C's escape hatch:

1. Regions deliver bounded-memory ETL now, inside the existing model.
2. Implement the region executor **on Akka.Streams internally** from day one — the rewrite's
   core technology gets proven under containment.
3. If regions grow until a whole workflow is one region, we arrive at the streaming engine
   *incrementally*, per-workflow, with batch and streaming semantics coexisting and zero
   forced migration.

Revisit this assessment only if (a) large-volume ETL becomes the dominant workload, (b)
regions prove insufficient in practice, and (c) a breaking v2 is commercially acceptable.

## 8. Decision checklist for stakeholders

- [ ] Is >50% of target workload large-volume per-document ETL?
- [ ] Are we willing to break every existing workflow binding (`{{nodeId.port}}`) and custom module?
- [ ] Can we fund parallel maintenance of two engines during transition?
- [ ] Is per-run snapshot auditability expendable, or must the v2 replicate it per-document?
- [ ] Has the Akka.Streams-backed region executor (06 §6 phase 3) been tried first?
