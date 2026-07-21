# Phase 2.2 Companion Analysis: Should we use Akka.Streams more directly? 🌊

Made with 💖 by Ami-Chan! UwU ✨

[Back to Phase 2.2](Phase2-2-AdvancedFlowControl.md) | [All Phases](README.md)

---

## TL;DR — Recommendation

**Stay on raw Akka actors for the 2.2 control-flow primitives. Adopt Akka.Streams *selectively* in 2.2.3 (parallel + fan-out/fan-in) and revisit a broader migration in Phase 3 when we have throughput data.**

> **Why:** Workflow execution is fundamentally **graph-shaped with branches, loops, error containment zones, and per-iteration scopes** — not a linear back-pressured pipeline. Akka.Streams is exceptional for the latter and awkward for the former. The places where Streams *does* shine in 2.2 are bounded: parallel coordinators, fan-out/fan-in throttling, and any future event-source ingestion (webhooks, tail logs). Going all-in would force us to model loops as cyclic graphs and try/catch as `Restart`/`RecoverWithRetries` decider policies — which works on paper but loses the per-execution observability we already get from `IExecutionHistoryRepository`.

---

## Context

The current engine is built on raw Akka actors:

- `Workflow.Engine/Actors/WorkflowExecutor.cs` — per-execution actor, manages node state map, dispatches downstream connections on completion.
- `Workflow.Engine/Actors/WorkflowSupervisor.cs` — spawns/lifecycles executors per workflow instance.
- Persistence wired via `PipeTo(Self)` for non-blocking awaited writes (Phase 2.1.5).

Phase 2.2 adds:

- Multi-port routing (selective output activation).
- Sub-graph execution (recursive containment).
- Loop scopes with per-iteration variable namespaces.
- Try/catch error containment zones.
- Hierarchical cancellation.
- Parallel coordinators with bounded parallelism + fail-fast.

The question raised: given the complexity (originally rated 🔥 High and now split into 2.2.0a/2.2.0b), would Akka.Streams give us a more natural runtime?

---

## What Akka.Streams brings to the table 🌊✨

| Capability | Streams strength | Notes |
|------------|-----------------|-------|
| **Back-pressure** | First-class, end-to-end | Reactive Streams compliant. Producers throttled by slowest consumer automatically. |
| **Bounded concurrency** | `mapAsync(parallelism)` / `mapAsyncUnordered` | Built-in semaphore semantics. Replaces hand-rolled `SemaphoreSlim`. |
| **Fan-out / fan-in** | `Broadcast`, `Balance`, `Merge`, `Concat`, `Zip` | Exactly the shapes 2.2.3 needs. |
| **Cancellation** | Stream completion cancels upstream | Aligns with our cooperative cancellation model. |
| **Throttling / windowing** | `throttle`, `groupedWithin`, `sliding` | Useful for future rate-limited connectors and trigger debouncing. |
| **Error decider** | `Supervision.Decider` (Stop / Resume / Restart) | Per-stage policy for transient errors. |
| **GraphDSL** | Expressive DAG builder | Map directly to a workflow definition, in theory. |
| **Materialization** | One stream, many runs | Re-runnable graphs without rebuild cost. |

---

## What Akka.Streams *isn’t* great at for our workload ⚠️

1. **Cycles (loops) are second-class.** Streams DAGs are acyclic by default. Cyclic graphs require `MergePreferred` + `feedback` loops, a known footgun (deadlock-prone, hard to size buffers correctly). Our `ForEach` / `While` are **exactly** loops.
2. **Per-iteration mutable scopes.** Streams stages are stateless by design (or carry state per materialized instance). Per-iteration variable subscope (Q4) is awkward — we’d either materialise a fresh sub-stream per iteration (expensive bookkeeping) or smuggle state in the element envelope (leaks abstraction).
3. **Try/catch isn’t a stream stage.** Stream supervision (`Resume`/`Restart`) handles *element* errors, not *region* errors with a parallel catch path that itself is a sub-graph with its own outputs (`error`, `success`, optional `finally`).
4. **History granularity.** Phase 2.1.5 records per-node executions to `IExecutionHistoryRepository` with caller identity, variable updates, terminal status, etc. Inside a Streams graph, individual stage executions don’t emit those events naturally — we’d need a tap on every stage. Cross-cuts the existing persistence story.
5. **Snapshot/replay.** Workflow snapshots (Phase 2.1.5 snapshot bridge + open hardening item) are designed around discrete node-state maps. Streams have no equivalent of "freeze the in-flight DAG" — they assume re-run from source. Resumability would require Akka.Persistence integration on top, not a freebie.
6. **Debugging surface.** Today: open a node, see inputs/outputs/error in history. With Streams: stages are anonymous lambdas; correlating a failure to a workflow node id requires extra envelope plumbing.
7. **Mental model shift.** Authors of `IWorkflowModule` understand "given inputs, produce outputs". Streams stages mix `Push` / `Pull` semantics (`GraphStageLogic`), which is harder to teach and harder to test.
8. **DI / lifecycle.** Modules are resolved per node via `IServiceProvider`. Streams stages are typically constructed once per materialisation; our per-execution DI scope (caller identity, variable write mode from 2.1.5) doesn't map cleanly.
9. **Akka.Streams licensing & velocity.** Akka .NET keeps pace with JVM Akka's stream evolution slower than core actors. Newer combinators sometimes lag. We’re on the actor side of that gap today.

---

## Hybrid model — where Streams *does* fit ✨

Even staying on actors as the dominant model, Streams is a great **embedded tool** at specific seams:

### 1) `2.2.3` parallel coordinator (high-value adoption)
Replace hand-rolled `SemaphoreSlim` + `Task.WhenAll` in `ParallelExecutionCoordinator` with:

```csharp
Source.From(branchPlans)
    .SelectAsync(maxDegreeOfParallelism, plan => RunSubGraph(plan, ct))
    .RunWith(Sink.Seq<BranchResult>(), materializer);
```

Wins:
- Built-in bounded parallelism, no manual semaphore.
- Fail-fast = `Decider.Stop` materialises a `Failure`; siblings cancel via stream completion.
- Easy switch to `Decider.Resume` for `failFast: false`.
- No need to invent our own coordinator state machine.

### 2) Future webhook / file-tail ingestion (Phase 3)
`Source.Queue` + `Throttle` + actor sink → natural fit for trigger pipelines. Worth flagging now so we don’t reinvent it.

### 3) Bulk module input streaming
Modules that produce *streams* of outputs (e.g. CSV reader, paged HTTP) could expose `Source<T>` directly and let downstream modules `RunWith` a sink. This is an **API extension**, not a runtime change.

### 4) Variable change watch / SignalR push
Existing `Workflow.Api/Hubs/*` could subscribe to a Streams `BroadcastHub` of execution events, replacing ad-hoc actor subscriptions.

---

## Side-by-side: 2.2 primitives mapped to each model

| Primitive | Raw actors (current plan) | Akka.Streams | Verdict |
|-----------|--------------------------|--------------|---------|
| Multi-port routing (2.2.0a) | Trivial — extend dispatch logic | Would need typed envelopes per port; possible via `UnzipWith` + N outputs | **Actors win** — no new abstractions needed |
| Sub-graph executor (2.2.0a) | New actor with isolated state map | Materialise sub-graph per region | **Actors win** — sub-graphs need persistence/history hooks |
| Loop scope (2.2.0b) | Stack of `LoopContext`, per-iter subscope | Cyclic graphs (`MergePreferred` + feedback) — fragile | **Actors win** clearly |
| Error boundary (2.2.0b) | Boundary stack, route to catch entry | Per-stage supervision decider — wrong granularity | **Actors win** |
| Hierarchical cancellation (2.2.0b) | Linked CTSs | Stream completion = cancel | **Tie**; actors fit existing `CancellationToken` contract |
| Conditional / switch (2.2.1) | Set `ActivePorts` | `PartitionWith(predicate)` | **Actors win** — cleaner schema mapping |
| Loops (2.2.2) | Sub-graph per iteration | Cyclic stream | **Actors win** strongly |
| Parallel + fan-out/in (2.2.3) | Coordinator actor + semaphore | `mapAsync` / `Broadcast` / `Merge` | **Streams win** — adopt here |
| Try/catch (2.2.4) | Boundary + catch entry | Decider + recover | **Actors win** |
| Expression evaluator (2.2.5) | Pure service | Pure service | **N/A** (independent of runtime model) |

Score: actors ahead in 7/9 primitives, Streams ahead in 1, tie in 1. Adopting Streams everywhere = net loss of clarity for marginal gain.

---

## Cost of going Streams-first ⚠️

If we *did* try to migrate the engine to Akka.Streams as the primary execution model:

- **Re-architect `WorkflowExecutor`** as a `GraphStage` factory rather than an actor — invalidates Phase 1 + Phase 2.1.5 supervision and persistence wiring.
- **Cyclic-graph design** for loops with hand-tuned buffer sizes; deadlock risk is real.
- **Re-do snapshot strategy** — `IExecutionStateStore` and `SaveSnapshotAsync` lose their natural mapping. Would need Akka.Persistence Streams sources, with their own learning curve.
- **Re-do execution history hooks** — every stage would need a side-effecting tap to emit `NodeExecutionRecord`.
- **Module API churn** — either keep `IWorkflowModule` and adapt at the boundary (extra layer), or push authors into `GraphStageLogic` (steep learning curve, breaks every existing module).
- **Migration risk under live persistence** — existing executions in Phase 2.1.5 history need to keep replaying.

Rough estimate: a Streams-first engine rewrite is **a phase-sized effort on its own**, easily 4–6 weeks plus stabilisation, with no user-visible feature delta over the current 2.2 plan.

---

## What we’d need to know to revisit (decision triggers)

Adopt Streams more broadly *if and only if* one of these becomes true:

1. **Throughput ceiling hit:** measured per-execution latency dominated by actor mailbox contention (unlikely at our current node-per-message granularity; modules tend to be I/O-bound).
2. **Back-pressure problems with bulk modules:** if a module produces unbounded items into the engine (e.g. CSV reader pushing 1M rows), we currently have no native back-pressure. Streams would solve this cleanly.
3. **Streaming triggers (webhooks, kafka, file tails):** Phase 3 adds these. Streams is the obvious fit and would be introduced *at the edge*, not in the engine core.
4. **Deterministic replay of in-flight pipelines:** if we ever need exactly-once replay across nodes (vs the current per-node persistence), Streams + Persistence may justify the lift.

Until then, the cost/benefit favours actors.

---

## Recommended adoption plan 🎀

| Sub-phase | Approach | Notes |
|-----------|----------|-------|
| **2.2.0a** Routing + sub-graphs | Raw actors | Fits cleanly into existing `WorkflowExecutor` |
| **2.2.0b** Loop / boundary / cancel | Raw actors | Streams cycles + decider don’t fit |
| **2.2.1** Conditional / switch | Raw actors | `ActivePorts` is the natural primitive |
| **2.2.2** Loops | Raw actors | Sub-graph per iteration; loops + Streams = pain |
| **2.2.3** Parallel + fan-out/in | **Akka.Streams (selective)** | Use `mapAsync` / `Broadcast` / `Merge` inside `ParallelExecutionCoordinator`; expose actor-shaped API to modules |
| **2.2.4** Try/catch | Raw actors | Stream supervision is wrong granularity |
| **2.2.5** Expression evaluator | N/A | Pure service |
| **2.2.6** E2E demo | Raw actors + 2.2.3 streams under the hood | No public API change |

Net add to Phase 2.2 if we adopt 2.2.3 with Streams: ~½ day for `Akka.Streams` package wiring + ~3 small additional tests asserting back-pressure and decider behaviour. Easily within the existing 2.2.3 budget.

---

## Risks of the *recommended* path (actor-first, Streams in 2.2.3 only)

- **Coordinator divergence:** the only place using Streams becomes a slightly different mental model. Mitigation: keep the public actor message protocol identical; Streams stays an implementation detail of `ParallelExecutionCoordinator`.
- **Future drift to Streams everywhere:** if subsequent phases keep adding Streams pockets (webhooks, bulk readers), we end up with a hybrid that's harder to teach. Mitigation: maintain a short doc (this one) and require an ADR-style update before adding new Streams seams.
- **We hit a throughput wall in Phase 3 we didn't predict:** revisit decision triggers above; the routing/sub-graph extraction in 2.2.0a is also re-usable from Streams stages, so a future migration isn't blocked.

---

## Open questions for the next session 🙏

- [ ] **A1** — Confirm 2.2.3 adopts Akka.Streams under the hood for the parallel coordinator (recommended). Yes/No.
- [ ] **A2** — Should we add a thin `IExecutionStream` abstraction now to leave a swap-in seam for Phase 3 (cost: small abstraction; benefit: future-proofing)? Recommend deferring until concrete need.
- [ ] **A3** — Any module authors already using Akka.Streams downstream of `IWorkflowModule.ExecuteAsync`? If yes, document the recommended pattern (likely `Source...RunWith(Sink.Seq)` to materialise to outputs).
- [ ] **A4** — Decision-trigger telemetry: do we have actor mailbox / per-execution latency metrics today? If not, add them in 2.1.5 follow-up so we can measure the "throughput ceiling hit" trigger empirically.

---

## Decision log entry (suggested)

> **2026-04-25 — Akka.Streams adoption posture for Phase 2.2 Advanced Flow Control**
>
> Decision: Stay on raw Akka actors as the primary execution model. Adopt Akka.Streams selectively inside `ParallelExecutionCoordinator` (2.2.3) for bounded parallelism and fan-out/fan-in. Defer broader Streams adoption to Phase 3, gated on documented decision triggers (throughput, back-pressure for bulk modules, streaming triggers, replayable pipelines).
>
> Rationale: workflow execution is graph-shaped with loops, scopes, and error zones — areas where Akka.Streams adds friction (cycles, supervision granularity, snapshot mismatch) without offsetting wins. Streams' real strengths (back-pressure, bounded concurrency, fan-out/in) map cleanly only to 2.2.3.
>
> Owners: Engine team. Revisit: end of Phase 2 retro, or sooner if any decision trigger fires.

---

> 💖 **Ami’s take:** Streams are gorgeous for **rivers**. Workflows are **trees with knots and patches**, nya~ 🎀 We use the river only where we actually have a river (parallel branches), and keep the actor scaffolding for everything that has to remember where it came from.

