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
| G | ~~A construct nested in a body runs as a plain node (§12.10, found 2026-10-10)~~ **Fixed (G1)** | High | retired → `Engine/NestedConstructTests` |

> **Narrowed by testing:** port-aware routing *inside* a loop/branch body already works
> (`PortRoutingInsideLoopBody_ShouldSkipDeactivatedBranch` passed), so only **per-node error
> handling** is missing in bodies (Finding C), not branching. The join-skip concern (§12.6) was
> **refuted** and is dropped. Routing working did *not* mean constructs nested in a body worked —
> they didn't (Finding G, §12.10; fixed 2026-10-11).

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

**Decided 2026-11-08 (asked): catch-style handler.**
- When a node with `UseErrorHandler` fails (after its retries), the engine runs `ErrorNodeId` with
  the error available as `{{Variable.error}}`.
- The failing node's downstream is skipped.
- The run continues unless the handler fails.
- `MaxConsecutiveErrors` fails the run after N handled failures of that node in a row (e.g. across
  loop iterations).

Detail questions (asked 2026-11-08, all answered with the recommended option):
- **Handler start:** a node referenced as an `ErrorNodeId` (node- or workflow-level) never starts
  with the run on its own. It runs only when a failure routes to it, or through its own incoming
  connections if it has any.
- **Retries:** if the failing node has a `RetryPolicy`, retry as usual first. The handler runs once
  retries are exhausted. Without a policy, it runs on the first failure.
- **After the handler:** the handler's own outgoing path continues, like a catch body. The failing
  node's downstream is skipped. The run completes unless something on the handler's path fails.
- **Designer support:** added in the same change:
  - an "On error" choice (Fail / Continue / Retry / Use error handler);
  - a handler picker;
  - a max-consecutive-errors field;
  - a dashed "on error" link on the canvas;
  - lint for a missing/invalid handler.
- **Defaults stated, not asked:**
  - A workflow-level `ErrorHandling` is already the fallback for nodes without their own, so a
    workflow-level handler is the default handler.
  - `MaxConsecutiveErrors` counts handled failures per node across the whole run (loop iterations
    included), and is reset when that node succeeds.
  - Implementation: the handler and its downstream run as a sub-graph with `error` as a body
    variable, like a catch body.

**Fixed 2026-11-08.**
- Engine: `ErrorHandlerCore`, `ErrorHandlerCounters`, the `NodeFailureCore` `UseErrorHandler`
  branch, and `RunErrorHandler` in both executors.
- Designer: `NodeErrorHandling`, the *On error* panel section, and the `EdgeLayer` link.
- Docs: `docs/error-handling.md`, with diagrams and alternatives A–G.
- Tests: `Engine/ErrorHandlerTests` (8, 6 mutants killed), UI `NodeErrorHandlingTests` (9) and
  `OnErrorPanelTests` (2). The guard is retired.

**Alternatives considered (kept on file):**
- retire the setting in favour of Try/Catch;
- handler-then-continue-downstream;
- a workflow-level "on run failure" hook only.

---

## Finding G (High) — a construct nested inside a body runs as a plain node (§12.10)

*Found 2026-10-10 while closing Stage 3 (§12.4 / §10.3). The earlier "constructs in bodies work"
reading rested on `PortRoutingInsideLoopBody_…`, which uses a port-routing node, not a construct, so
it never exercised this path.*

> **Status: fixed 2026-10-11 with G1.** The user chose G1 (2026-10-10).
> 1. `ConstructSpawnCore` (`Workflow.Engine/Actors/ConstructSpawnCore.cs`) was extracted from
>    `WorkflowExecutor` as a pure refactor, with the full suite green and no test changes.
> 2. `SubGraphExecutor` uses the same core for **all four** constructs (loop, parallel, try/catch,
>    transaction). It stores `Node*ExecutionRequested`, spawns the construct as its own child (with
>    its linked CTS token and a copy of its current `BodyVariables`) and handles `*Completed` (applies
>    `VariableUpdates`, then done-port routing). It sends `*Failed` and unexpected `Terminated` into
>    `NodeFailureCore` / `ReportFailure`, and `IsComplete` waits while a nested construct runs. Stages
>    2 and 3 of the recommendation landed together because the core already covers all four.
> 3. Break/continue bind to the innermost loop by construction: a nested loop's body runs in that
>    loop's own sub-graph, so its sentinels never reach the outer body.
>
> On the open sub-questions, the fix takes the defaults this plan proposed and adds nothing new.
> **History:** *(updated 2026-10-11, scope-path follow-up)* every body history record now says
> where it ran. Each construct passes a scope path down to the bodies it starts (`ScopeStep`:
> `nodeId`, `kind` loop / parallel / trycatch / transaction, plus `iteration` / `branchIndex` /
> `branch` where it applies); `WorkflowExecutor` starts from an empty path, `SubGraphExecutor`
> passes its own (via `ConstructSpawnCore`). `SubGraphExecutor.QueuePersistNode` writes the path to
> `Metadata["scopePath"]` (outermost first, so a loop-in-loop leaf reads `outer#2/inner#1`) and the
> innermost loop step to `LoopId` / `LoopIteration`, which the UI already shows. `subGraphId` keeps
> its old format; no migration (`MetadataJson` takes any keys). Not done: showing the full path in
> the API/UI, and live node events from bodies (separate follow-up). The outer body still accepts
> `Push/PopLoopScope` and ignores them. Tests: `Workflow.Tests/Engine/NestedConstructHistoryTests`
> (5 incl. transaction-in-loop, all red without the change).
> **Variables:** the Stage 3 rules apply recursively (start from the body's current variables,
> report writes back), as the "every construct passes variables in and reports writes" invariant
> requires.
>
> Guards retired to `Workflow.Tests/Engine/NestedConstructTests` (7 tests: the 2 originals plus
> parallel-in-loop, loop-in-try, break-in-inner, failing inner loop caught by an outer try/catch,
> and recursive variable propagation). All 7 are red without the `SubGraphExecutor` change.
>
> **Break/continue through constructs** *(gap closed 2026-10-11)*: after the G1 fix, `break` /
> `continue` inside a try/catch (or parallel / transaction) inside a loop body no longer reached the
> loop, because the construct ran its branches in its own sub-graph. Now `TryCatchCompleted` /
> `ParallelCompleted` / `TransactionCompleted` carry `BreakRequested` / `ContinueRequested` (ORed
> over the construct's completed bodies) and `SubGraphExecutor` raises them in its own body, so they
> reach the nearest enclosing loop. `LoopCompleted` carries none, so a break never crosses a nested
> loop. Tests: `NestedConstructTests.BreakIn*` (5, all red before).
>
> **Nested transaction** *(added 2026-10-11)*: covered by
> `DatabaseTransactionStructuralEngineTests.TransactionNestedInLoopBody_*` (temp-file SQLite). A
> transaction in a loop body opens and commits once per iteration (3 inserts, committed ×3,
> rolledBack ×0), and a failing body rolls back per iteration while the loop still completes. Both
> are red with nested-construct start disabled in `SubGraphExecutor`.
> `NestedConstructHistoryTests.TransactionInLoop_*` checks the `loop#k/txn` scope path.

### Evidence
`Workflow.Tests/Findings/Engine/NestedConstructFindingTests.cs` (now retired, see Status). Both tests were **red** and use the real
`builtin.loop.foreach` / `builtin.trycatch` modules:

- `ForeachNestedInForeachBody_ShouldRunInnerBodyItemsTimesOuterItems`: outer foreach `[1,2,3]`,
  inner foreach `[a,b]`, leaf in the inner body. Expected 6 leaf runs, **got 3**: the inner loop node
  ran once per outer iteration and its `loopBody` edge fired once as an ordinary edge. The run still
  *completes*, so nothing tells the user the result is wrong.
- `TryCatchInsideForeachBody_ShouldCatchPerIteration_AndLoopCompletes`: foreach `[1,2,3]` whose body
  is a try/catch over an always-failing node. Expected the run to complete with 3 catches, **got
  `WorkflowFailed`**: the try/catch never became a boundary, so the failure failed the body, then the
  loop (no `continueOnError`), then the run.

### Root cause
A construct module only *describes* itself. `NodeExecutor.SendSuccess` tells its parent
`Node{Loop,Parallel,TryCatch,Transaction}ExecutionRequested` just before `NodeExecutionCompleted`
(the "FIFO trick"). Only `WorkflowExecutor` handles those messages: it fills its `_pending*` maps,
then calls `Spawn*Executor` in `HandleNodeExecutionCompleted`. Inside a body the parent is
`SubGraphExecutor`, which has **no handler** for them. The request goes unhandled (to dead letters),
and the `NodeExecutionCompleted` that follows takes the normal path,
`HandleNodeCompleted` → `ExecuteReadySuccessors(nodeId, ActivePorts)`. Construct modules set no
`ActivePorts`, so **every** outgoing edge fires once: `loopBody` *and* `done`; `try`, `catch`,
`finally` *and* `done`.

The outer body's scope (`LoopExecutorActor.ComputeBodyScope`, a BFS from the body entries) already
includes the nested construct's own body nodes. They do run in the outer sub-graph, but only once
each, as ordinary successors. That is why the loop-in-loop case looks almost right.

The fix must also cover two related gaps:
- **Break/continue bind to the wrong loop.** `SubGraphExecutor.HandleNodeCompleted` sets
  `_breakRequested` / `_continueRequested` from `__loop_break__` / `__loop_continue__` on *any* body
  node. A `builtin.break` in an inner loop's body would therefore break the **outer** loop.
- Streaming regions in bodies are a separate known gap (plan 04, "no regions inside construct
  bodies"). A shared spawn core would be the natural place to add them later.

### Options
- **G1 (recommended): `SubGraphExecutor` spawns nested constructs through a shared spawn core.**
  Extract what `WorkflowExecutor` does for each construct into a `ConstructSpawnCore`: resolve body
  entries from the body port(s), compute the body scope, build the actor `Props`, and on
  `*Completed` mark the nested body scope as handled and fire the done-port successors. Use the same
  style as `DispatchCore` / `NodeFailureCore`: shared logic, with state kept in the actor and wired
  in by callbacks. `SubGraphExecutor` then:
  - stores `Node*ExecutionRequested`. On the matching completion it spawns the construct as its own
    child instead of routing, and passes its linked CTS token and its current `BodyVariables`.
  - handles `Loop/Parallel/TryCatch/TransactionCompleted`: applies `VariableUpdates`, stores the
    construct's outputs, skips the nested body scope and fires the done ports. It sends `*Failed`
    into `NodeFailureCore`, so an *outer* try/catch or `continueOnError` loop sees the failure.
  - waits in `IsComplete` until pending nested constructs finish.
  - honours break/continue sentinels only from nodes outside any nested loop's scope.

  `WorkflowExecutor` moves to the same core as a pure refactor first, with the full suite green
  (the template is how `NodeFailureCore` landed). Nesting depth is then unbounded by construction.
- **G2: reject nested constructs at submit time.** `WorkflowSupervisor` fails a definition whose
  construct body scope contains another construct node. This check would sit next to the §12.5
  unknown-module check. It is small (~0.5 day) and honest, but it takes away a capability the
  editor lets users build.
- **G3: delegate up.** `SubGraphExecutor` forwards the request to `WorkflowExecutor`, which spawns
  the construct. Rejected because the top-level executor would own an actor whose lifetime, cancel
  scope and variables belong to one iteration of a body. Replies would also have to be routed back
  down.

### Recommendation
**G1**, in three stages:
1. Extract `ConstructSpawnCore` from `WorkflowExecutor` as a pure refactor.
2. Add loop + try/catch to `SubGraphExecutor`. This turns both finding tests green.
3. Add parallel + transaction.

**G2** is a reasonable stop-gap if G1 won't land soon. It turns a silently wrong result into a clear
error at submit time.

### Decisions to settle first
1. **G1 or G2**, or G2 now and G1 later.
2. **History/events for nested runs.** Today body nodes are persisted through `QueuePersistNode`
   only, without per-iteration loop metadata, and body node events are already a follow-up. Is it
   acceptable for the first cut to run nested constructs correctly, with the same history fidelity
   as a single level of body?
3. **Variable scoping:** confirm that the Stage 3 rules apply recursively. A nested construct starts
   from the body's current variables and reports its writes back to the body.

### Acceptance
Both `NestedConstructFindingTests` tests are green and retired to a permanent class. New tests cover:
- parallel inside a loop
- a loop inside a try
- a break in the inner loop, which ends only the inner loop
- a failing inner loop caught by an outer try/catch

These suites still pass: `SubGraphParityTests`, `SubGraphExecutorTests`, `LoopScopeTests`,
`ErrorBoundaryTests`, `LoopCompletionTests`, `DatabaseTransactionStructuralEngineTests`.

### Risk / blast radius
Medium-high. The change touches the spawn and completion path of every construct in both executors,
and the refactor step must not change top-level behaviour. Transactions need care: a nested
transaction must still roll back on cancel, because §12.3 cancels through the token, not
`Context.Stop`.

**Effort:** G1 ~4–6 days (≈1 for the refactor, 2 for loop + try/catch, 1–2 for parallel +
transaction, plus tests). G2 ~0.5 day.

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
6. **G (nested constructs)** after C — it builds on `NodeFailureCore`; needs the G1/G2 decision.

Each step lands behind its finding test plus the full existing engine suite.
