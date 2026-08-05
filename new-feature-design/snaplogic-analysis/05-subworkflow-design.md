# 05 — Design: Sub-Workflow Module (`builtin.workflow.execute`) 🧩

> Closes gap #1 from [`02-snaplogic-comparison.md`](02-snaplogic-comparison.md) §7.
> SnapLogic parity target: **Pipeline Execute** (Standard mode first; Reuse mode later).
> Design-level; not implementation-ready.

## 1. Goal

A node that executes **another workflow by id** as a real child execution — DotFlow's unit of
reuse. Also the enabler for reusable error workflows ([`07`](07-reusable-error-workflow-design.md)).

## 2. Architecture

Follow the existing sub-orchestration pattern (`LoopRequest` → `LoopExecutorActor`,
`TryCatchRequest` → `TryCatchExecutorActor`, all built on `SubGraphExecutor`):

```
builtin.workflow.execute module
  └─ returns SubWorkflowRequest (new, Workflow.Core/Models)
       └─ WorkflowExecutor detects request → spawns SubWorkflowExecutorActor
            └─ resolves child WorkflowDefinition via the workflow registry/service
            └─ Asks WorkflowSupervisor: CreateWorkflowInstance (same path as
               ActorWorkflowExecutionService.StartAsync / ActorWorkflowLauncher)
            └─ child runs as a REAL execution: own execution id, own history,
               own variable Execution scope, own snapshot on terminal state
            └─ on child terminal state → maps child outputs → node output ports
```

Key architectural choice: **child = first-class execution**, *not* an inlined sub-graph.
Rationale: matches SnapLogic semantics (child pipelines are visible in Monitor), reuses all
existing persistence/monitoring/SignalR machinery unchanged, and keeps the parent's
`WorkflowExecutionContext` small.

- **Cancellation**: child's `CancellationTokenSource` linked to the parent's `_executionCts`
  (existing hierarchical-cancellation pattern) — cancelling the parent cancels children.
- **Correlation**: add `ParentExecutionId` (and `RootExecutionId`) to the execution record —
  analogous to SnapLogic `pipe.parentRuuid`/`pipe.rootRuuid`. Monitor UI can then render the
  parent/child tree.
- **Depth limit**: configurable, default 16 (SnapLogic allows 64) — checked at spawn by
  counting ancestry via `ParentExecutionId`, which also catches simple recursion. Static cycle
  detection at save time is best-effort only (ids can be dynamic).

## 3. Module surface

| Property | Type | Notes |
| --- | --- | --- |
| `workflowId` | string (template-enabled) | target definition id; templates allow `{{Variable.x}}` dispatch |
| `waitForCompletion` | bool, default `true` | `false` = fire-and-forget; outputs only `executionId` |
| `inputs` | object (template-enabled values) | becomes the child's **run inputs** (wins over child variable seeds — existing precedence) |
| `timeout` | node-level `Timeout` (existing) | applies to the wait |

| Port | Direction | Notes |
| --- | --- | --- |
| `input` | in (optional) | passed to child as run input `input` when `inputs` doesn't override |
| `output` | out | child's terminal outputs (see open Q2) |
| `executionId` | out | child execution id |

Errors: child `Failed` state → node failure with the child's `WorkflowError` — parent's
`RetryPolicy`/`ErrorHandling`/trycatch apply normally. No new error machinery.

## 4. Iteration & pooling (SnapLogic Standard-mode parity)

Do **not** build pooling into the module. Per-document child launches compose from existing
nodes: `builtin.loop.foreach` (or `builtin.parallel` over partitions) wrapping a
`workflow.execute` node. Parallel's `maxDegreeOfParallelism` *is* the pool size. Reuse mode
(long-lived child instances streaming documents) is deferred to the streaming design
([`06`](06-streaming-data-plane-design.md)).

## 5. Key decisions

1. Child as first-class execution (vs inline sub-graph) — **first-class**, see §2.
2. Resolve target at **execution time** via the registry (latest saved version), not bind at
   design time. Version pinning is an open question.
3. Fire-and-forget children **survive parent completion** but not parent cancellation? →
   simpler: they stay linked to parent cancellation; document it.
4. `SupportsTemplates` on `workflowId`/`inputs` values follows the standard opt-in rules.

## 6. Open questions

- [ ] What is "the child's output" when several terminal nodes exist — map of terminal
      nodeId→outputs, or require a `builtin.end` node with declared outputs? (Recommend:
      designate `builtin.end` as the output contract; else expose the map.)
- [ ] Version pinning (`workflowVersion` property) — needed for stable estates?
- [ ] Should child executions of a fire-and-forget node be cancellable independently via API?
- [ ] Designer treatment: render child workflow name + jump-to link; warn on missing target.
