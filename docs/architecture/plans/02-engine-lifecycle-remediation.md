# Plan 02 — Engine lifecycle remediation

> Fix plan for the **High**-severity engine findings confirmed by
> `Workflow.Tests/Findings/Engine/*` on 2026-09-29, plus the two related Medium items in the same
> code. Source: `Workflow.Engine/Actors/{WorkflowExecutor,SubGraphExecutor,NodeExecutor,
> WorkflowSupervisor}.cs`, `Workflow.Api/RealTime/ExecutionEventBridge.cs`.
>
> Up: [architecture findings](../README.md#findings-at-a-glance) ·
> [01 — Execution lifecycle §12](../01-execution-lifecycle.md#12-findings-to-verify)

**Status:** proposed. Nothing here is built yet.

| # | Finding | Sev | Guard test |
| --- | --- | :-: | --- |
| A | Completion/node events never reach SignalR (§12.1) | High | `RealtimeCompletionFindingTests` (2) |
| B | Executor actors never stop (§12.2) | High | `EngineFindingTests.CompletedRun_ShouldStopExecutorActor` |
| C | Per-node retry/continue ignored inside construct bodies (§12.4) | High | `EngineFindingTests.RetryInsideLoopBody_ShouldBeHonoured` |
| C2 | A node on a loop's `done` port is skipped, never run (§12.9, newly found) | High | `EngineFindingTests.LoopDonePort_ShouldRunPostLoopNode` |
| D | Unregistered module id "succeeds" via a stub (§12.5) | High | `EngineFindingTests.UnregisteredModule_ShouldFailNode_NotSucceedViaStub` |
| E | Cancel doesn't stop running constructs (§12.3) | Med | `EngineFindingTests.Cancel_ShouldStopRunningLoopIterations` |
| F | `UseErrorHandler` not implemented (§12.7) | Med | `EngineFindingTests.UseErrorHandler_ShouldRouteToHandler_NotFailWorkflow` |

> **Narrowed by testing:** port-aware routing *inside* a loop/branch body already works
> (`PortRoutingInsideLoopBody_ShouldSkipDeactivatedBranch` passed), so only **per-node error
> handling** is missing in bodies (Finding C), not branching. The join-skip concern (§12.6) was
> **refuted** and is dropped.

---

## Finding A — terminal & node-completion events don't reach SignalR (§12.1)

### Evidence
- `WorkflowExecutor` publishes to the Akka `EventStream` only for `ExecutionStateChanged`,
  `NodeStateChanged`, `NodeRetrying`, `ProgressUpdate`, `SupervisionEvent`, `StreamRegionProgress`.
- `WorkflowCompleted` / `WorkflowFailed` are only `Tell`'d to the parent supervisor;
  `NodeExecutionCompleted` / `NodeExecutionFailed` are only sent node→executor.
- `ExecutionEventBridge.SubscribedTypes` subscribes to `WorkflowCompleted`, `WorkflowFailed`,
  `NodeExecutionCompleted`, `NodeExecutionFailed` — types that never arrive on the stream.
- The existing bridge tests pass only because they publish those messages by hand.
- `RealtimeCompletionFindingTests` drives a real run and times out waiting for `ExecutionCompleted`
  and `NodeCompleted`.

### Root cause
The bridge listens for message types the engine never publishes to the stream. The bridge and the
engine were wired to two different sets of message types.

### Options
- **A1 (recommended): publish the terminal messages.** In `WorkflowExecutor`, alongside the existing
  `Context.Parent.Tell(new WorkflowCompleted(...))` / `WorkflowFailed(...)`, add
  `Context.System.EventStream.Publish(...)`. Do the same for `NodeExecutionCompleted` /
  `NodeExecutionFailed` at the point they're handled (or have `NodeExecutor` publish them). Matches
  what the bridge already expects; no bridge change.
- **A2: derive completion in the bridge.** Map terminal `ExecutionStateChanged`
  (→ Completed/Failed/Cancelled) and terminal `NodeStateChanged` (→ Completed/Failed) to the client
  events, and drop the four never-arriving subscriptions. Keeps the engine's message flow unchanged
  but the bridge loses the duration/outputs those richer messages carry.

### Recommendation
**A1.** It restores the contract the bridge was written against and keeps duration/output payloads.
Watch one thing: publishing `NodeExecutionCompleted` to the stream broadcasts node **outputs** to
subscribers — confirm that's acceptable, or broadcast a slimmed event. (Ties into the secret-redaction
Medium finding 02 §10.6 — don't broadcast secret values.)

### Acceptance
`RealtimeCompletionFindingTests` (both) green; existing `ExecutionEventBridgeTests` and
`SubscriptionTests` still green.

**Effort:** ~1 day.

---

## Finding B — executor actors are never stopped (§12.2)

### Evidence
- `WorkflowExecutor` has no `Context.Stop(Self)` and no receive timeout.
- `WorkflowSupervisor` has no `Receive<WorkflowCompleted>` / `Receive<WorkflowFailed>` handler; it
  only reacts to `Terminated`.
- `EngineFindingTests.CompletedRun_ShouldStopExecutorActor` never observes `Terminated`.

### Root cause
Nothing ends an executor's life. Each run's actor, its captured `_nodeInputs` / `_nodeOutputs`, and
its entry in `activeWorkflows` live for the process lifetime. Status queries for old runs keep hitting
the live actor instead of falling through to history.

### Options
- **B1 (recommended): executor stops itself on terminal.** After `CompleteWorkflow` / `FailWorkflow`
  (and the cancel path) have notified the parent and the persistence writes have been acknowledged,
  `Context.Stop(Self)`. The supervisor already removes the entry on `Terminated`, and
  `GetStatusAsync` already falls back to history for unknown executions.
- **B2: idle reaper.** A `ReceiveTimeout` that stops the actor after N seconds of inactivity
  post-completion. Simpler to reason about for pause/resume, but leaves a tunable window.

### Recommendation
**B1**, but sequence it *after* the persistence acknowledgements the executor already waits on
(`PersistenceSnapshotSaved`, status update) so a stop can't race a pending write. Keep pause state in
mind: a **paused** run must NOT be reaped (it's non-terminal and resumable) — only stop on
Completed/Failed/Cancelled.

### Acceptance
`CompletedRun_ShouldStopExecutorActor` green; add a test that a status query *after* stop returns the
**history** record (not "not found"); confirm pause/resume tests unaffected.

### Risk / blast radius
Medium. Anything that assumed a live actor after completion (some tests query status post-run). The
history fallback covers the API; in-actor tests may need to assert against history instead.

**Effort:** ~1–2 days including the race-with-persistence care and test updates.

---

## Finding C — per-node retry/continue ignored inside construct bodies (§12.4)

### Evidence
- `SubGraphExecutor.HandleNodeFailed` unconditionally `ReportFailure(nodeId, error)` — no
  consultation of the node's `ErrorHandling` / `RetryPolicy`.
- `RetryInsideLoopBody_ShouldBeHonoured` fails: a Retry node that would recover on attempt 2 inside a
  loop body fails the whole run.
- Contrast: `PortRoutingInsideLoopBody_…` passes — routing is already shared via `DispatchCore`, so
  branching in bodies works. Only failure-handling is missing.

### Root cause
`WorkflowExecutor.HandleNodeFailure` holds all the error-behaviour logic (boundaries, retry backoff,
continue, fail). `SubGraphExecutor` was written "lean" (its own doc comment says so) and never got
that logic. The two actors already share routing (`DispatchCore`); they don't yet share failure
handling.

### Options
- **C1 (recommended): extract a shared failure handler.** Pull the `HandleNodeFailure` decision tree
  (boundary walk → retry/continue/fail) into a `NodeFailureCore` the way `DispatchCore` was extracted,
  wired to each actor's own state via delegates. Both actors then behave identically.
- **C2: minimal — honour retry only in the sub-graph.** Add just retry/continue to
  `SubGraphExecutor.HandleNodeFailed`. Smaller, but re-duplicates logic and drifts from the executor.

### Recommendation
**C1.** This was already flagged as the natural next extraction after `DispatchCore`
(see the observations in [00 — overview §6](../00-overview.md#6-observations)). It fixes retry,
continue, *and* nested error boundaries in bodies in one move, and removes duplication rather than
adding more.

### Acceptance
`RetryInsideLoopBody_ShouldBeHonoured` green; `PortRoutingInsideLoopBody_…` stays green; add
body-scope tests for `Continue` and for a `try/catch` nested inside a loop body. Existing
`SubGraphExecutorTests`, `LoopScopeTests`, `ErrorBoundaryTests` still pass.

### Risk / blast radius
Medium-high — this is the run failure state machine. The extraction must be behaviour-preserving for
the top-level path (the `DispatchCore` extraction is the template: it was a pure refactor with tests
proving equivalence). Land it behind the full existing engine suite plus the new body tests.

**Effort:** ~3–5 days.

---

## Finding C2 — a node on a loop's `done` port is skipped, never run (§12.9)

*Discovered while building the §10.3 test; confirmed with the real `builtin.loop.foreach`.*

### Evidence
- `WorkflowExecutor.HandleLoopCompleted` builds the body scope from **every** outgoing target of the
  loop node: `allOutgoingTargets = connections where SourceNodeId == loopNodeId` — which includes the
  `done`-port node — then `ComputeBodyScope(definition, loopNodeId, allOutgoingTargets)`.
- `ComputeBodyScope` BFSs from each entry node it is given, so the `done` node is added to the scope
  and marked **skipped**. The subsequent "fire done-port edges" step filters to targets **not** in
  the scope, which is now empty, so nothing fires.
- By contrast `SpawnLoopExecutor` calls `ComputeBodyScope` with only the **loopBody** targets. The two
  call sites disagree.
- `EngineFindingTests.LoopDonePort_ShouldRunPostLoopNode` (real `ForEachModule`) → the run Completes
  but the done-port node never runs.
- Why unnoticed: `ForEachModuleTests` only checks the module returns a `LoopRequest`; the full-engine
  `done → downstream` tests exist for parallel / fan-out / fan-in / try-catch, but **not** for loops.

### Root cause
`HandleLoopCompleted` passes all outgoing targets (loopBody **and** done) as body-entry nodes, so the
post-loop node is treated as part of the body and skipped.

### Fix
Pass only the **loopBody**-port targets to `ComputeBodyScope`, matching `SpawnLoopExecutor`:

```csharp
var bodyEntryTargets = _definition.Connections
    .Where(c => c.SourceNodeId == loopNodeId && c.SourcePortName == /* the loop's LoopBodyPort */)
    .Select(c => c.TargetNodeId)
    .ToList();
var bodyScope = LoopExecutorActor.ComputeBodyScope(_definition, loopNodeId, bodyEntryTargets).ToHashSet();
```

The loop's `LoopBodyPort` is on the `LoopRequest`; thread it through `LoopCompleted` (or re-read it
from the pending loop request) rather than hard-coding `"loopBody"`, so custom body ports keep working.

### Acceptance
- `LoopDonePort_ShouldRunPostLoopNode` green.
- Add an end-to-end test: `foreach` with a real body **and** a real `done` node that writes an
  output; assert the workflow's outputs include the done node's contribution.
- Existing `ForEachModuleTests`, `LoopScopeTests`, `AdvancedFlowPersistenceTests` still pass.
- Re-check the `WhileModule` path (same `HandleLoopCompleted`).

### Risk / blast radius
Medium. This is core loop completion, but the change makes the two call sites consistent and is
covered by the new test plus the existing loop suite. Ship it **before** Finding C (body error
handling) and before re-testing §10.3 — both depend on post-loop nodes actually running.

---

## Finding D — an unregistered module id "succeeds" via a stub (§12.5)

### Evidence
- `NodeExecutor.ExecuteStubFallback` returns a fabricated success when `IModuleRegistry.GetModule`
  returns null (`Workflow.Engine/Actors/NodeExecutor.cs` ~line 666).
- `WorkflowExecutor.ValidateConnectionPorts` skips unregistered modules with the comment "runtime
  execution will catch this" — it doesn't.
- `UnregisteredModule_ShouldFailNode_NotSucceedViaStub` → the run **Completes**.

### Root cause
A test convenience (stub-execute unknown modules) is on the production path, and the run-time
validation that was supposed to backstop it is a no-op for unknown ids.

### Options
- **D1 (recommended): fail the node.** Replace `ExecuteStubFallback` with `SendFailure(new
  InvalidOperationException($"Module '{id}' is not registered"))`. If a stub is still wanted for unit
  tests, gate it behind an explicit opt-in flag that defaults off.
- **D2: reject at validation.** Make the run-path validator (via `WorkflowSupervisor`, which uses the
  structural `WorkflowValidator`) reject unknown module ids up front, matching what
  `ModuleAwareWorkflowValidator` already does on save. Best done *with* D1, not instead of it.

### Recommendation
**D1 + D2.** Fail fast at the node, and also reject unknown ids before the run starts so the error is
reported at submit time, not mid-run.

### Acceptance
`UnregisteredModule_…` green; audit tests that relied on the stub (search `ExecuteStubFallback` /
tests running unregistered ids) and update them to register a stub module explicitly.

### Risk / blast radius
Medium — several older tests may lean on the stub. Those are exactly the ones that should register a
real stub module instead.

**Effort:** ~1 day plus test cleanup.

---

## Finding E (Medium) — cancel doesn't stop running constructs (§12.3)

`HandleCancelExecution` stops the direct `_nodeActors` but does **not** call `_executionCts.Cancel()`
(unlike `CompleteWorkflow` / `FailWorkflow`, which do) and doesn't signal construct actors. So a
loop/parallel keeps iterating after cancel (`Cancel_ShouldStopRunningLoopIterations`: 9 iterations
ran). **Fix:** in the cancel path, `_executionCts.Cancel()` and send the `CooperativeCancel*` messages
to any active construct actors, then transition to Cancelled. **Effort:** ~0.5 day. Guard: the finding
test. Fold into the same PR as Finding B (both are lifecycle/cancellation).

## Finding F (Medium) — `UseErrorHandler` not implemented (§12.7)

`ErrorBehavior.UseErrorHandler` logs a warning and calls `FailWorkflow`; `ErrorHandling.ErrorNodeId`
and `MaxConsecutiveErrors` are never read. This is a **missing feature**, not a regression — decide
the intended semantics first (route to `ErrorNodeId` as a catch-style handler? bounded consecutive
errors?), then implement. Until then, the finding test encodes one reasonable reading and should be
treated as a spec question, not a bug to rush. **Effort:** ~2–3 days once semantics are agreed.

---

## Sequencing

1. **C2 (loop done-port)** first — it silently drops all post-loop work and blocks §10.3; small,
   localized fix in `HandleLoopCompleted`.
2. **A (events)** and **D (stub module)** — small, isolated, high user-visible value (the UI
   currently never sees runs finish; workflows silently pass with missing modules).
3. **B + E (lifecycle & cancel)** together — same code, same test file.
4. **C (shared failure handler)** last of the High items — largest, needs the equivalence care the
   `DispatchCore` extraction modelled.
5. **F (UseErrorHandler)** is a feature spec, scheduled separately.

Each step lands behind its finding test plus the full existing engine suite.
