# Phase 2.2: Advanced Flow Control (Weeks 9-10) 🔀🔁⚡

Made with 💖 by Ami-Chan! UwU ✨

[Back to Phase 2](Phase2-CoreFeatures.md) | [All Phases](README.md)

---

## Overview

Phase 2.2 turns the workflow engine from a **linear DAG runner** into a **proper control-flow runtime**: conditionals, loops, parallel branches, fan-out/fan-in, and try/catch error boundaries — all expressed as first-class **modules** plus targeted engine upgrades. Modules stay declarative (schema-first), while the engine learns about *multi-port routing*, *sub-graph execution*, *loop scopes*, and *error containment zones*~ ✨

**Timeline:** 2 weeks (Weeks 9-10)
**Complexity:** 🔥 High — engine semantics change (sub-graphs, scopes, error zones)

> **CopilotNote:** Many of these modules cannot be built as “just another node” — they need engine support
> for *executing other nodes* (sub-graphs). 2.2.0 is the engine prep phase that unlocks the rest. Build it
> first, even though it has no user-visible module~ 🧠

### Confirmed Design Decisions ✅

| # | Decision |
|---|----------|
| **Q1 Sub-graph model** | Control modules execute **inline sub-graphs** by reference (not separate workflows). Loop body = a tagged region of the same workflow, addressed by an entry-port connection. |
| **Q2 Multi-port routing** | Output ports already exist in `PortDefinition`/`ModuleSchema`. Engine routing must learn to **selectively activate** outgoing connections by source port name (vs today’s “fire all outputs”). |
| **Q3 Expression language** | Use a **safe, sandboxed** expression evaluator (no arbitrary C#). Start with a thin wrapper (DynamicExpresso or a small in-house parser). No reflection, no I/O, deterministic. |
| **Q4 Loop scope** | Each loop iteration gets a fresh **execution-scoped variable subscope** (`VariableScope.ForExecution(...)` augmented with iteration index). Loop variables don’t leak across iterations. |
| **Q5 Error boundaries** | Try/Catch is an **error containment zone** managed by the engine, not by Akka supervision. Inner failures are caught + transformed into typed error values, keeping the parent workflow healthy. |
| **Q6 Fan-out/Fan-in** | Implemented as **dedicated modules** on top of the same parallel coordinator used by `ParallelModule`. Fan-in uses a barrier with optional `failFast`. |

### TO RESOLVE 🙏

- [X] **Q7 Expression engine choice:** ~~DynamicExpresso (battle-tested, MIT) vs in-house mini-parser (zero deps, fewer features).~~ **Resolved: Jint (JS/ES2020, BSD-2)** — preferred over DynamicExpresso because it ships a first-class `EvaluateAsync(script, ct)` with native `CancellationToken` support, full JS `async/await` + `Promise` semantics inside expressions, and richer built-in array/string transforms (`map`, `filter`, `reduce`, `?.`, `??`) with no helper registration needed. DynamicExpresso remains available as a lighter-weight fallback behind the same `IExpressionEvaluator` interface. See full analysis: [Phase2-2-ExpressionEngine-Analysis.md](./Phase2-2-ExpressionEngine-Analysis.md)~
- [x] **Q8 Loop-body addressing:** ~~ports only vs explicit RegionId~~ **Resolved: Hybrid in 2.2** — ports drive execution (SubGraphExecutor gets entry node from `loopBody` connection, engine ignores `regionId` entirely), and `NodeDefinition` gains an optional `regionId?` hint field populated by author tooling / designer for future bounding-box rendering. Zero engine complexity added; schema is forward-compatible with Phase 3 visual designer. See full breakdown + diagrams: [Phase2-2-LoopBodyAddressing.md](./Phase2-2-LoopBodyAddressing.md)~
- [ ] **Q9 Cancellation semantics for parallel/loop:** when one branch fails with `failFast: true`, do siblings get a cooperative `CancellationToken` cancel, or hard kill? Recommend cooperative cancel + grace timeout.
- [ ] **Q10 Switch module scope:** include `builtin.switch` in 2.2 (multi-way) or defer to 2.2.x add-on? Recommend including, since it shares the multi-port routing work.

---

## Pre-Existing Work (from Phase 1) ✅

| Component | File | Status |
|-----------|------|--------|
| `PortDefinition` (multi-port schema) | `Workflow.Core/Models/ModuleSchema.cs` | ✅ Already supports multiple inputs/outputs |
| `NodeDefinition` (id, module, inputs) | `Workflow.Core/Models/NodeDefinition.cs` | ✅ Existing |
| `ConnectionDefinition` (source/target) | `Workflow.Core/Models/WorkflowDefinition.cs` | ✅ Connections already keyed by ports |
| `WorkflowExecutor` (Akka actor) | `Workflow.Engine/Actors/WorkflowExecutor.cs` | ⚠️ Today fires **all** downstream connections — needs port-aware routing |
| `IWorkflowModule` | `Workflow.Modules/Abstractions/IWorkflowModule.cs` | ✅ Module contract reused as-is |
| `IExecutionHistoryRepository` | `Workflow.Persistence/Abstractions/IExecutionHistoryRepository.cs` | ✅ Records per-node executions (good for loop iterations) |

> **CopilotNote:** Connections in `WorkflowDefinition` already carry source/target port names — the data
> model is fine. The work in 2.2.0 is **how the engine consumes them** (selective activation), not the schema~ 💡

---

## 2.2.0 Engine Prep — Split Slices 🛠️

> **Why split:** the original 2.2.0 bundled four cross-cutting engine changes (routing, sub-execution, scoping, error containment) + cancellation. Each is medium individually, but together they touch the hot path of `WorkflowExecutor.cs` simultaneously, which makes regressions hard to localise. Splitting into **2.2.0a** (routing + sub-graphs) and **2.2.0b** (loop scope + error boundary + hierarchical cancellation) keeps blast radius small per PR and lets 2.2.1 (conditional/switch) ship as soon as 2.2.0a lands~ 🧠
>
> **Note:** No feature flag for port-aware routing. Backwards compatibility is guaranteed by the *behavioural contract*: when a module emits no `ActivePorts`, the engine fires all outgoing connections (i.e. today's behaviour). Existing modules don't change, existing workflows don't change. The new path is purely additive~ ✨

**Companion analysis:** [Phase2-2-Akka-Streams-Analysis.md](./Phase2-2-Akka-Streams-Analysis.md) — should we lean on Akka.Streams more directly given this complexity? (Read before starting 2.2.0a if Akka.Streams adoption is on the table.)

---

### 2.2.0a Port-Aware Routing & Sub-Graph Execution 🎯🌿

**Purpose:** Teach the engine to (a) selectively activate downstream connections by port and (b) run a contained sub-graph on demand. These two together are the *minimum* primitives needed to ship `builtin.condition` / `builtin.switch` (2.2.1)~ ⚡

**Complexity:** 🟡 Medium

#### Tasks:

- [ ] **Port-aware connection activation in `WorkflowExecutor`** 🎯
  - [ ] Add optional `ActivePorts: Arr<string>` to the node-completion message in `Workflow.Engine/Messages/WorkflowMessages.cs` (and the corresponding result type returned by `IWorkflowModule`).
  - [ ] In `WorkflowExecutor`, when dispatching downstream connections:
    - [ ] If `ActivePorts` is **empty/null** → fire all outgoing connections (today’s behaviour, no change for existing modules).
    - [ ] If `ActivePorts` is **non-empty** → fire only connections whose `SourcePort` ∈ `ActivePorts`.
  - [ ] Workflow load-time validation: every connection's `SourcePort` must reference a declared output port on the source node's module schema. Catch typos before execution.
  - [ ] Update existing engine tests to assert the unchanged-default contract (no test should need to set `ActivePorts`).

- [ ] **Sub-graph execution primitive** 🌿
  - [ ] New file: `Workflow.Engine/Actors/SubGraphExecutor.cs`
    - [ ] Accepts `(parentExecutionId, entryNodeIds, inputs, parentScope)`.
    - [ ] Re-uses the same dispatch protocol as `WorkflowExecutor` but with **isolated** node-state map and **scoped** to a subset of the workflow's nodes/connections.
    - [ ] Reports `SubGraphCompleted(outputs)` / `SubGraphFailed(error)` to the caller actor.
  - [ ] New file: `Workflow.Engine/Messages/SubGraphMessages.cs`
    - [ ] `StartSubGraph`, `SubGraphCompleted`, `SubGraphFailed`.
  - [ ] Refactor `WorkflowExecutor` to extract a small **dispatch core** that both it and `SubGraphExecutor` share (avoid copy-paste of routing logic).

- [ ] **Persistence & history**
  - [ ] Sub-graph node executions persist to `IExecutionHistoryRepository` under the parent execution id with `Metadata.subGraphId` so 2.1.5 history queries still work.
  - [ ] No new repository surface area required.

**Tests (target ~6):** → `Workflow.Tests/Engine/PortRoutingTests.cs`, `Workflow.Tests/Engine/SubGraphExecutorTests.cs`
- [ ] `ActivePorts = ["true"]` fires only `true` connections; `false` connections do not run
- [ ] Backwards-compat: nodes without `ActivePorts` fire all outgoing connections
- [ ] Connection referencing an undeclared `SourcePort` fails at workflow load with a clear error
- [ ] Sub-graph runs entry → terminal nodes and returns aggregated outputs
- [ ] Sub-graph failure surfaces as `SubGraphFailed` to caller without failing the parent execution by itself
- [ ] Sub-graph node executions appear in history under the parent execution

---

### 2.2.0b Loop Scope, Error Boundary & Hierarchical Cancellation 🔁🛡️🛑

**Purpose:** Add the *stateful* engine primitives that 2.2.2 (loops) and 2.2.4 (try/catch) depend on. Builds directly on the routing + sub-graph primitive from 2.2.0a, but now we’re changing how state, failure, and cancellation propagate~ 🌷

**Complexity:** 🟡 Medium-High

#### Tasks:

- [ ] **Iteration & loop scope** 🔁
  - [ ] New file: `Workflow.Engine/Models/LoopContext.cs`
    - [ ] Fields: `LoopId`, `Iteration`, `Item`, `Index`, `ParentScope`.
    - [ ] Helpers: `BreakRequested`, `ContinueRequested` flags.
  - [ ] Per-iteration variable subscope wrapping `VariableScope.ForExecution({execId})` (e.g. logical namespace `loop:{loopId}:{iter}`) so writes inside the body don't leak across iterations.
  - [ ] Loop context **stack** kept on the executor so nested loops can locate their innermost loop deterministically (used by `BreakModule`/`ContinueModule` in 2.2.2).
  - [ ] Iteration metadata recorded on `NodeExecutionRecord` (`loopId`, `iter`) for replay/UI.

- [ ] **Error containment zone** 🛡️
  - [ ] New file: `Workflow.Engine/Models/ErrorBoundary.cs`
    - [ ] Tracks `OwnerNodeId`, `CatchEntryNodeId`, `FinallyEntryNodeId?`, `CatchTypes`.
  - [ ] In `WorkflowExecutor`, when a node fails:
    - [ ] Walk the active boundary stack for the failing node's region.
    - [ ] If a boundary catches it → emit a typed `WorkflowError` value and route to its `CatchEntryNodeId`; do **not** fail the parent execution.
    - [ ] Else → existing failure path (terminal status update via 2.1.5).
  - [ ] **Snapshot-bridge interaction** *(addresses the open 2.1.5 follow-up about double terminal writes)*: when a boundary handles a failure, do **not** emit `UpdateExecutionStatusAsync(Failed)` for the parent — only the boundary outcome is persisted (`Metadata.boundaryOutcome = "caught"|"rethrown"|"finally"`).

- [ ] **Hierarchical cancellation** 🛑
  - [ ] Each `SubGraphExecutor` (from 2.2.0a) receives a `CancellationToken` linked to the parent's per-execution `CancellationTokenSource`.
  - [ ] Disposing a sub-graph (success, failure, or cancellation) disposes the linked CTS to prevent leaks.
  - [ ] Exposed contract for 2.2.3 / 2.2.4: `RequestCooperativeCancel(reason)` triggers the linked CTS with grace window (default 250 ms, configurable).
  - [ ] No hard kill of in-flight nodes — modules are expected to honour `CancellationToken` (existing contract).

#### Tests (target ~7): → `Workflow.Tests/Engine/LoopScopeTests.cs`, `Workflow.Tests/Engine/ErrorBoundaryTests.cs`, `Workflow.Tests/Engine/HierarchicalCancellationTests.cs`
- [ ] Loop subscope isolates iteration variables (read inside iter N cannot see writes from iter N-1 or N+1 in the same scope)
- [ ] Nested loop contexts are stacked correctly; `LoopContext.Innermost` returns the deepest active one
- [ ] Error boundary catches sub-graph failure and routes to its `CatchEntryNodeId`; parent execution stays Running
- [ ] Error boundary `finally` always runs (success path)
- [ ] Error boundary `finally` always runs (catch path)
- [ ] Nested boundaries: inner catch handles before outer; outer's `finally` still runs after inner unwinds
- [ ] Hierarchical cancel disposes child CTS; no leaked tokens after coordinator disposes
- [ ] Boundary-handled failure does **not** double-write terminal status to `IExecutionHistoryRepository` (regression guard for the open 2.1.5 follow-up)

---

## 2.2.1 Conditional Branching (`builtin.condition`, `builtin.switch`) 🔀

**Purpose:** First user-facing payoff of the multi-port routing primitive — express simple if/else and multi-way branching as nodes~ 🌸

**Complexity:** 🟡 Medium

### Tasks:

- [ ] **Create `ConditionalModule`** 🔀
  - [ ] New file: `Workflow.Modules/Builtin/Flow/ConditionalModule.cs`
  - [ ] `ModuleId: "builtin.condition"`, `Category: "Flow Control"`, `DisplayName: "Conditional Branch"`.
  - [ ] Schema:
    - [ ] Input: `condition` (bool *or* expression string, required)
    - [ ] Output port: `true` (no payload, activation only)
    - [ ] Output port: `false` (no payload)
    - [ ] Output: `result` (bool — for diagnostics/branch logging)
  - [ ] `ExecuteAsync`:
    - [ ] If `condition` is `bool`, use directly; else evaluate via `IExpressionEvaluator`.
    - [ ] Set `ActivePorts = ["true"]` or `["false"]` so engine routes only one branch.
  - [ ] Diagnostics: log evaluated value + (if expression) source string.

- [ ] **Create `SwitchModule`** 🔢 *(scope per Q10)*
  - [ ] New file: `Workflow.Modules/Builtin/Flow/SwitchModule.cs`
  - [ ] `ModuleId: "builtin.switch"`, `Category: "Flow Control"`.
  - [ ] Schema:
    - [ ] Input: `value` (object, required)
    - [ ] Input: `cases` (array of `{ match, port }`, required)
    - [ ] Input: `defaultPort` (string, optional)
    - [ ] Dynamic output ports = case names + optional `default`.
  - [ ] `ExecuteAsync`: pick first matching case, fall back to `default`, set `ActivePorts` accordingly.

- [ ] **Engine integration**
  - [ ] Validate at workflow load that conditional/switch outgoing connections all reference declared ports.

**Tests (target ~10):** → `Workflow.Tests/Modules/Flow/ConditionalModuleTests.cs`, `Workflow.Tests/Modules/Flow/SwitchModuleTests.cs`
- [ ] Bool `condition: true` → only `true` port fires
- [ ] Bool `condition: false` → only `false` port fires
- [ ] Expression `condition: "x > 5"` reads variable `x`, evaluates correctly
- [ ] Invalid expression → execution fails with clear `WorkflowError`
- [ ] Both branches reachable across two separate runs of same workflow
- [ ] `Switch` matches first case, ignores later ones
- [ ] `Switch` falls back to `default` when no match
- [ ] `Switch` with no match + no default → fails with descriptive error
- [ ] Switch port name validation rejects unknown ports at load
- [ ] Diagnostics carry evaluated value for debugging

---

## 2.2.2 Loops (`builtin.loop.foreach`, `builtin.loop.while`, break/continue) 🔁

**Purpose:** Iterate over collections / repeat while a condition holds, leveraging the sub-graph executor + loop context~ ✨

**Complexity:** 🟡 Medium

### Tasks:

- [ ] **`ForEachModule`** 🔁
  - [ ] New file: `Workflow.Modules/Builtin/Flow/ForEachModule.cs`
  - [ ] `ModuleId: "builtin.loop.foreach"`.
  - [ ] Schema:
    - [ ] Input: `collection` (`IEnumerable`, required)
    - [ ] Input: `maxIterations` (int, optional, default `1000`)
    - [ ] Input: `continueOnError` (bool, optional, default `false`)
    - [ ] Output port: `loopBody` (activation per iteration)
    - [ ] Output port: `done` (after all iterations)
    - [ ] Outputs: `results` (array), `count` (int), `errors` (array)
  - [ ] Execution:
    - [ ] For each item: build `LoopContext`, run sub-graph rooted at downstream `loopBody` consumers.
    - [ ] Honour `BreakRequested` / `ContinueRequested` flags from `BreakModule` / `ContinueModule`.
    - [ ] Enforce `maxIterations`; on overflow, fail with `LoopLimitExceeded`.
    - [ ] If `continueOnError`, capture per-iter error, continue; else propagate first error.

- [ ] **`WhileModule`** 🌀
  - [ ] New file: `Workflow.Modules/Builtin/Flow/WhileModule.cs`
  - [ ] Schema mirrors `ForEachModule`, plus Input: `condition` (bool/expression).
  - [ ] Pre-iteration evaluate; same break/continue semantics.

- [ ] **`BreakModule` / `ContinueModule`** ⏹️➡️
  - [ ] New files: `Workflow.Modules/Builtin/Flow/BreakModule.cs`, `ContinueModule.cs`
  - [ ] No outputs; set the corresponding flag on the **innermost** `LoopContext` for current execution.
  - [ ] Validation: must be inside a loop region, else load-time error.

- [ ] **Loop diagnostics & history**
  - [ ] Each iteration recorded as a `NodeExecutionRecord` with `Metadata: { loopId, iter }` so persistence/UI can replay.

- [ ] **`regionId` hint field (Hybrid Q8 resolution)** 🗺️
  - [ ] Add `RegionId? string` to `Workflow.Core/Models/NodeDefinition.cs` — optional, nullable, ignored by engine.
  - [ ] Engine **never reads** `regionId` for routing or subgraph discovery — execution is always port-driven.
  - [ ] Author tooling / workflow serializer should auto-populate `regionId = "{loopNodeId}-body"` when writing a `loopBody` connection for a node.
  - [ ] Load-time: emit a **warning** (not error) if a node's `regionId` references a loop node whose `loopBody` port does not reach that node — indicates designer drift, execution still proceeds via ports.
  - [ ] Schema is forward-compatible: Phase 3 visual designer reads `regionId` to render bounding boxes without engine changes.

**Tests (target ~14):** → `Workflow.Tests/Modules/Flow/ForEachModuleTests.cs`, `Workflow.Tests/Modules/Flow/WhileModuleTests.cs`
- [ ] Foreach over 10 items runs 10 iterations
- [ ] Foreach with `break` stops early
- [ ] Foreach with `continue` skips current iteration
- [ ] Foreach over empty collection → `count: 0`, `done` fires
- [ ] Foreach honours `maxIterations` (overflow → fail)
- [ ] `continueOnError: true` collects errors, continues
- [ ] `continueOnError: false` short-circuits on first error
- [ ] While condition false from start → 0 iterations
- [ ] While increments counter and exits at threshold
- [ ] While honours `maxIterations`
- [ ] Nested foreach (inner uses outer’s item) works
- [ ] Iteration variables isolated per iteration (Q4)
- [ ] Each iteration recorded individually in execution history
- [ ] `Break`/`Continue` outside a loop fails load-time validation

---

## 2.2.3 Parallel Execution & Fan-out / Fan-in — Split Slices ⚡🌟

> **Why split:** the original 2.2.3 bundled three concerns — a brand-new actor (`ParallelExecutionCoordinator`) with non-trivial concurrency primitives (semaphore, fail-fast cooperative cancel, snapshot tracking), plus three modules with distinct semantics (static branches vs per-item fan-out vs barrier aggregation). Concurrency bugs in the coordinator would mask module bugs and vice versa, so we land them in two PRs: **2.2.3a** ships the coordinator + `ParallelModule` (the primitive + its most direct consumer), and **2.2.3b** ships `FanOutModule` + `FanInModule` (fan-shaped patterns built on the proven coordinator). This mirrors the 2.2.0a/2.2.0b split~ 🧠
>
> **Q-resolve hint (Q9):** cooperative cancel + grace timeout is the working assumption for 2.2.3a; if it changes, only 2.2.3a needs re-litigating. 🎀

---

### 2.2.3a Parallel Coordinator & `ParallelModule` 🎛️⚡

**Purpose:** Land the shared concurrency primitive (`ParallelExecutionCoordinator`) and prove it via the simplest consumer — a static N-branch parallel module with bounded parallelism and fail-fast semantics~ 💫

**Complexity:** 🟡 Medium-High

#### Tasks:

- [ ] **`ParallelExecutionCoordinator` actor** 🎛️
  - [ ] New file: `Workflow.Engine/Actors/ParallelExecutionCoordinator.cs`
  - [ ] Spawns N child `SubGraphExecutor`s (from 2.2.0a), tracks per-branch completion state.
  - [ ] Bounded parallelism: enforce `maxDegreeOfParallelism` via `SemaphoreSlim` around branch spawn.
  - [ ] `failFast` cooperative cancellation (Q9): on first branch failure, trigger linked CTS from 2.2.0b's hierarchical cancellation contract; honour configurable grace window (default 250 ms).
  - [ ] Aggregates `(results, completedCount, failedCount)` and reports `ParallelCompleted` / `ParallelFailed` to caller.
  - [ ] New file: `Workflow.Engine/Messages/ParallelMessages.cs` — `StartParallel`, `BranchCompleted`, `BranchFailed`, `ParallelCompleted`, `ParallelFailed`.

- [ ] **`ParallelModule`** ⚡
  - [ ] New file: `Workflow.Modules/Builtin/Flow/ParallelModule.cs`
  - [ ] `ModuleId: "builtin.parallel"`, `Category: "Flow Control"`.
  - [ ] Schema:
    - [ ] Output ports: `branch1..branchN` (declared dynamically based on connections at load)
    - [ ] Inputs: `maxDegreeOfParallelism` (int, optional), `waitForAll` (bool, default `true`), `failFast` (bool, default `true`)
    - [ ] Outputs: `results` (array), `completedCount` (int), `failedCount` (int)
  - [ ] Activation: sets `ActivePorts = [all connected branches]`, then awaits coordinator for completion.

- [ ] **Engine support**
  - [ ] Track in-flight sub-graphs per parallel coordinator for snapshot/persistence (record `Metadata.parallelId` + `branchIndex` on `NodeExecutionRecord`).
  - [ ] Hierarchical cancellation token plumbed via 2.2.0b — no new cancellation surface area.

#### Tests (target ~7): → `Workflow.Tests/Engine/ParallelCoordinatorTests.cs`, `Workflow.Tests/Modules/Flow/ParallelModuleTests.cs`
- [ ] 3-way parallel split runs concurrently (assert wall time < sum of branch times)
- [ ] `maxDegreeOfParallelism = 2` over 5 branches enforces limit (observe at most 2 in-flight)
- [ ] One branch fails + `failFast: true` → siblings receive cooperative cancel within grace window
- [ ] One branch fails + `failFast: false` → others complete, error captured in `failedCount`
- [ ] `waitForAll: false` returns when first branch completes; remaining branches drain or cancel
- [ ] Unbalanced branch durations don't starve coordinator (slow branch finishes; fast branches don't block scheduling)
- [ ] Cancellation tokens cleaned up after coordinator disposes (no leaked CTS — regression guard)

---

### 2.2.3b Fan-out / Fan-in Modules 🌟🪄

**Purpose:** Build the dynamic fan-shaped patterns on top of the (now proven) `ParallelExecutionCoordinator` — `FanOutModule` for per-item parallel sub-graphs, `FanInModule` for barrier aggregation~ ✨

**Complexity:** 🟡 Medium

#### Tasks:

- [ ] **`FanOutModule`** 🌟
  - [ ] New file: `Workflow.Modules/Builtin/Flow/FanOutModule.cs`
  - [ ] `ModuleId: "builtin.fanout"`, `Category: "Flow Control"`.
  - [ ] Schema:
    - [ ] Input: `items` (array, required)
    - [ ] Input: `maxDegreeOfParallelism` (int, optional)
    - [ ] Input: `failFast` (bool, default `true`)
    - [ ] Output port: `branch` (activation per item, payload = `{ item, index }`)
    - [ ] Output port: `done` (after all items processed)
    - [ ] Outputs: `results` (array), `completedCount` (int), `failedCount` (int)
  - [ ] Behaviour: like `ForEachModule` (2.2.2) but parallel — spawns one sub-graph per item via `ParallelExecutionCoordinator`.

- [ ] **`FanInModule`** 🪄
  - [ ] New file: `Workflow.Modules/Builtin/Flow/FanInModule.cs`
  - [ ] `ModuleId: "builtin.fanin"`, `Category: "Flow Control"`.
  - [ ] Schema:
    - [ ] Input: `branches` (array of inputs collected from upstream connections)
    - [ ] Input: `mode` (enum: `Concat`, `Merge`, `First`, `Last`, default `Concat`)
    - [ ] Input: `timeout` (TimeSpan, optional — barrier safety net)
    - [ ] Output: `result` (aggregated)
  - [ ] Behaviour: barrier — waits until all upstream branches complete (or timeout), then aggregates per `mode`.
  - [ ] Engine hook: `WorkflowExecutor` must hold `FanIn` activation until **all** declared upstream connections have either delivered or been cancelled (new "barrier node" predicate).

- [ ] **Engine support**
  - [ ] Barrier-node activation gating in `WorkflowExecutor` (general primitive, but `FanIn` is currently the only consumer).
  - [ ] No new persistence rows beyond what 2.2.3a already records.

#### Tests (target ~6): → `Workflow.Tests/Modules/Flow/FanOutModuleTests.cs`, `Workflow.Tests/Modules/Flow/FanInModuleTests.cs`
- [ ] FanOut spawns one sub-graph per item (10 items → 10 iterations recorded)
- [ ] FanOut respects `maxDegreeOfParallelism` (delegated to 2.2.3a coordinator — smoke check only)
- [ ] FanIn `Concat` preserves upstream connection order
- [ ] FanIn `Merge` deduplicates dictionary keys deterministically (last writer wins, documented)
- [ ] FanIn `First` / `Last` semantics return the first/last completed branch's payload
- [ ] Combined `FanOut → work → FanIn` produces correctly aggregated result end-to-end

---

## 2.2.4 Error Handling Nodes (`builtin.trycatch`, `builtin.throw`) 🛡️

**Purpose:** Let workflows recover from failures locally, transform errors, and run cleanup `finally` blocks — all without taking down the parent execution~ 🌷

**Complexity:** 🟡 Medium

### Tasks:

- [ ] **`TryCatchModule`** 🛡️
  - [ ] New file: `Workflow.Modules/Builtin/Flow/TryCatchModule.cs`
  - [ ] `ModuleId: "builtin.trycatch"`.
  - [ ] Schema:
    - [ ] Output port: `try` (activation)
    - [ ] Output port: `catch` (activation, payload = `WorkflowError`)
    - [ ] Output port: `finally` (activation, optional)
    - [ ] Inputs: `rethrow` (bool, default `false`), `catchTypes` (array of error type names, optional filter)
    - [ ] Outputs: `error` (object, nullable), `success` (bool)
  - [ ] Behaviour:
    - [ ] Activates `try`; engine wraps downstream sub-graph in an `ErrorBoundary` (2.2.0).
    - [ ] On failure matching `catchTypes`, routes to `catch` with serialised error.
    - [ ] After `try` (success) or `catch` (recovery), routes to `finally` if present.
    - [ ] `rethrow: true` re-emits the error after `finally`.

- [ ] **`ThrowModule`** 💥
  - [ ] New file: `Workflow.Modules/Builtin/Flow/ThrowModule.cs`
  - [ ] Schema:
    - [ ] Input: `errorType` (string, required), `message` (string, required), `data` (object, optional)
  - [ ] `ExecuteAsync`: throws `WorkflowUserError` with metadata.

- [ ] **`WorkflowError` value type** 🪶
  - [ ] New file: `Workflow.Core/Models/WorkflowError.cs`
  - [ ] Fields: `ErrorType`, `Message`, `NodeId`, `Data`, `OccurredAt`, `StackTrace?`.
  - [ ] JSON-serialisable (via existing engine serialization).

- [ ] **Engine integration**
  - [ ] Hook `ErrorBoundary` (2.2.0) to populate `WorkflowError` and route to catch port.
  - [ ] Persist boundary outcomes in execution history for replay/debug.

**Tests (target ~10):** → `Workflow.Tests/Modules/Flow/TryCatchModuleTests.cs`
- [ ] `try` block succeeds → only `try` + `finally` fire; `success: true`
- [ ] `try` block throws → routes to `catch` with `WorkflowError`
- [ ] `finally` always runs (success path)
- [ ] `finally` always runs (catch path)
- [ ] `rethrow: true` re-raises after finally
- [ ] `catchTypes` filter: only matching types caught; others propagate
- [ ] Nested try/catch: inner handles before outer
- [ ] `Throw` module produces structured `WorkflowError`
- [ ] Error includes `NodeId` of failing node
- [ ] Error boundary persisted in execution history

---

## 2.2.5 Expression Evaluator 🧮

**Purpose:** A safe, deterministic, sandboxed evaluator for `condition` / `switch` expressions and (later) data transformation modules~ 🌟

**Companion analysis:** [Phase2-2-ExpressionEngine-Analysis.md](./Phase2-2-ExpressionEngine-Analysis.md) — side-by-side syntax comparison + integration sketches for DynamicExpresso vs JavaScript (Jint) vs Lua (MoonSharp). **Jint selected as default for v1** — native `EvaluateAsync` + CT support + full JS `async/await`~ 🧮

**Complexity:** 🟡 Medium

### Tasks:

- [ ] **Define `IExpressionEvaluator`**
  - [ ] New file: `Workflow.Core/Abstractions/IExpressionEvaluator.cs`
  - [ ] API: `EvaluateAsync<T>(string expression, IReadOnlyDictionary<string, object?> variables, CancellationToken)` — returns `ValueTask<T>`.
  - [ ] Object path: `EvaluateObjectAsync(...)` — returns `ValueTask<JsonElement>` for structured/array returns.
  - [ ] Errors: `ExpressionParseException` (syntax / parse-time), `ExpressionRuntimeException` (runtime / timeout).

- [ ] **Implement default evaluator — Jint** *(per Q7 resolution)* 🟡
  - [ ] New file: `Workflow.Engine/Services/JintExpressionEvaluator.cs`
  - [ ] Engine config: `LimitMemory(4MB)`, `TimeoutInterval(250ms)` (secondary cap), `LimitRecursion(64)`, `Strict()`, `CatchClrExceptions()`.
  - [ ] Use built-in `engine.EvaluateAsync(expression, ct)` — true async, CT is first-class, no `Task.Run` wrapper needed.
  - [ ] Pool engine instances via `ObjectPool<Engine>` — instances are not thread-safe.
  - [ ] Only inject safe projected DTOs into JS scope — never raw services, EF entities, or `IServiceProvider`.
  - [ ] Pre-parse validation via Esprima `JavaScriptParser.ParseExpression()` — catches syntax errors before execution.
  - [ ] `EvaluateObjectAsync` uses `JsValueToJsonNode` walker → `JsonElement` (no `ExpandoObject` leakage).

- [ ] **Keep `DynamicExpressoEvaluator` as opt-in fallback** *(C#-syntax, zero async overhead)*
  - [ ] New file: `Workflow.Engine/Services/DynamicExpressoEvaluator.cs`
  - [ ] Registered under keyed DI as `"csharp"` — not the default.
  - [ ] `DisableReflection()` + whitelist helpers: `len(x)`, `contains(x,y)`, `lower(s)`, `upper(s)`, `now()`.
  - [ ] Returns `ValueTask.FromResult(result)` (zero-alloc synchronous path).

- [ ] **`IExpressionEvaluatorFactory` + DI wiring**
  - [ ] New file: `Workflow.Engine/Services/KeyedExpressionEvaluatorFactory.cs`
  - [ ] Resolves by `engineName` from `WorkflowDefinition` metadata (default: `"javascript"`).
  - [ ] Register in `Workflow.Api/Program.cs`:
    - [ ] `AddSingleton<IExpressionEvaluator, JintExpressionEvaluator>()` — default primary
    - [ ] `AddKeyedSingleton<IExpressionEvaluator, DynamicExpressoEvaluator>("csharp")` — opt-in fallback
    - [ ] `AddSingleton<IExpressionEvaluatorFactory, KeyedExpressionEvaluatorFactory>()`

- [ ] **Operator / feature coverage (Jint — JS/ES2020 native)**
  - [ ] Comparison: `>`, `<`, `>=`, `<=`, `===`, `!==`
  - [ ] Logical: `&&`, `||`, `!`, `??` (null coalescing), `?.` (optional chaining)
  - [ ] Arithmetic: `+`, `-`, `*`, `/`, `%`
  - [ ] Array transforms: `.map()`, `.filter()`, `.reduce()`, `.includes()`, `.find()`
  - [ ] String helpers: `.toLowerCase()`, `.toUpperCase()`, `.includes()`, `.startsWith()`
  - [ ] Ternary + template literals: `` `Hello ${name}` ``
  - [ ] `async/await` + `Promise` — resolved natively via `EvaluateAsync`

- [ ] **Determinism / safety**
  - [ ] No CLR type injection beyond explicitly `SetValue`d safe DTOs
  - [ ] Hard timeout via `TimeoutInterval` config (secondary) + `CancellationToken` (primary)
  - [ ] Memory limit + recursion limit enforced by engine config

**Tests (target ~12):** → `Workflow.Tests/Engine/ExpressionEvaluatorTests.cs`
- [ ] Boolean comparisons evaluate correctly
- [ ] Logical operators short-circuit (`&&`, `||`)
- [ ] Null coalescing (`??`) and optional chaining (`?.`) work correctly
- [ ] Arithmetic with int/double mix
- [ ] Variable lookup from supplied dictionary
- [ ] Missing variable in strict mode → `ExpressionRuntimeException` (ReferenceError)
- [ ] Array `.map()` / `.filter()` return correctly projected results
- [ ] `async` expression (`Promise.resolve(x)`) resolves correctly via `EvaluateAsync`
- [ ] Timeout aborts long-running expression (infinite loop)
- [ ] `CancellationToken` cancels mid-evaluation natively
- [ ] `EvaluateObjectAsync` returns `JsonElement` for JS object literal return
- [ ] Concurrent calls via pool don't share engine state (isolation regression guard)

---

## 2.2.6 Engine Integration & End-to-End Demo Workflow 🎯

**Purpose:** Prove the new control-flow primitives compose cleanly via realistic sample workflows + integration tests with persistence + API~ 💖

**Complexity:** 🟡 Medium

### Tasks:

- [ ] **End-to-end sample workflow** 🌈
  - [ ] New file: `examples/definitions/flow-control-demo.json`
  - [ ] Shape:
    ```
    Start → ForEach(items)
              ├─ loopBody → Condition(item.priority > 5)
              │                ├─ true  → Parallel
              │                │            ├─ branch1 → HttpRequest
              │                │            └─ branch2 → SetVariable
              │                └─ false → Log
              └─ done    → FanIn → SetVariable("summary")
    ```

- [ ] **Persistence integration tests**
  - [ ] New file: `Workflow.Tests/Engine/AdvancedFlowPersistenceTests.cs`
  - [ ] Run demo workflow on `SqlitePersistenceProvider` (`:memory:`); assert per-iteration node records.
  - [ ] Verify try/catch error boundary outcomes recorded.

- [ ] **API smoke tests**
  - [ ] New file: `Workflow.Tests/Api/AdvancedFlowApiTests.cs`
  - [ ] POST execute → 202 → poll status → completed; assert `Outputs` includes aggregated results.

- [ ] **Docs**
  - [ ] New file: `docs/advanced-flow-control.md`
  - [ ] Cover: condition, switch, foreach/while + break/continue, parallel/fanout/fanin, try/catch + throw, expression cheatsheet, common patterns.

**Tests (target ~6):** → `Workflow.Tests/Engine/AdvancedFlowPersistenceTests.cs`, `Workflow.Tests/Api/AdvancedFlowApiTests.cs`
- [ ] Demo workflow completes end-to-end on SQLite `:memory:`
- [ ] Per-iteration node executions recorded with `loopId`/`iter`
- [ ] Parallel branches recorded with concurrent timestamps
- [ ] Try/catch outcomes recorded with `WorkflowError`
- [ ] API run returns aggregated outputs
- [ ] Cancellation via API cancels in-flight parallel branches

---

## Phase 2.2 Deliverables ✅

**Completion Criteria:**
- [ ] Engine supports **port-aware connection activation** (selective output routing, additive default)
- [ ] Engine supports **sub-graph execution**, **loop scopes**, **error boundaries**, **hierarchical cancellation**
- [ ] 2.2.0 split shipped as **2.2.0a** (routing + sub-graphs) and **2.2.0b** (loop scope + error boundary + cancellation)
- [ ] 2.2.3 split shipped as **2.2.3a** (coordinator + `ParallelModule`) and **2.2.3b** (`FanOutModule` + `FanInModule`)
- [ ] Modules: `builtin.condition`, `builtin.switch`, `builtin.loop.foreach`, `builtin.loop.while`, `builtin.break`, `builtin.continue`, `builtin.parallel`, `builtin.fanout`, `builtin.fanin`, `builtin.trycatch`, `builtin.throw`
- [ ] `IExpressionEvaluator` + safe default implementation registered via DI
- [ ] ~77 unit + integration tests passing across 2.2.0a/2.2.0b–2.2.6 (2.2.0a ~6 + 2.2.0b ~7 + 2.2.1 ~10 + 2.2.2 ~14 + 2.2.3a ~7 + 2.2.3b ~6 + 2.2.4 ~10 + 2.2.5 ~12 + 2.2.6 ~6, replacing the original ~12 for 2.2.3 with ~13 across the split)
- [ ] XML docs + `docs/advanced-flow-control.md`
- [ ] Sample workflow runs end-to-end on persistence + API stack

**New / Modified Files (planned):**
```
Workflow.Core/
  Abstractions/IExpressionEvaluator.cs                  ← new
  Models/WorkflowError.cs                               ← new
  Models/NodeDefinition.cs                              ← + optional RegionId? hint (Hybrid Q8)

Workflow.Engine/
  Actors/SubGraphExecutor.cs                            ← new
  Actors/ParallelExecutionCoordinator.cs                ← new
  Actors/WorkflowExecutor.cs                            ← port-aware routing, error boundary hooks
  Messages/SubGraphMessages.cs                          ← new
  Messages/ParallelMessages.cs                          ← new (2.2.3a)
  Messages/WorkflowMessages.cs                          ← + ActivePorts, loop/error messages
  Models/LoopContext.cs                                 ← new
  Models/ErrorBoundary.cs                               ← new
  Services/JintExpressionEvaluator.cs                  ← new (default evaluator)
  Services/DynamicExpressoEvaluator.cs                  ← new (opt-in "csharp" fallback)
  Services/KeyedExpressionEvaluatorFactory.cs           ← new

Workflow.Modules/Builtin/Flow/
  ConditionalModule.cs                                  ← new
  SwitchModule.cs                                       ← new
  ForEachModule.cs                                      ← new
  WhileModule.cs                                        ← new
  BreakModule.cs                                        ← new
  ContinueModule.cs                                     ← new
  ParallelModule.cs                                     ← new
  FanOutModule.cs                                       ← new
  FanInModule.cs                                        ← new
  TryCatchModule.cs                                     ← new
  ThrowModule.cs                                        ← new

Workflow.Tests/
  Engine/PortRoutingTests.cs                            ← new
  Engine/SubGraphExecutorTests.cs                       ← new
  Engine/ParallelCoordinatorTests.cs                    ← new (2.2.3a)
  Engine/ErrorBoundaryTests.cs                          ← new
  Engine/ExpressionEvaluatorTests.cs                    ← new
  Engine/AdvancedFlowPersistenceTests.cs                ← new
  Api/AdvancedFlowApiTests.cs                           ← new
  Modules/Flow/*.cs                                     ← new (per module)

docs/advanced-flow-control.md                           ← new
examples/definitions/flow-control-demo.json             ← new
Directory.Packages.props                                ← + Jint (default); DynamicExpresso.Core (opt-in fallback)
```

---

## ✅ Resolved vs ❓ Open

| # | Question | Status                                      | Note |
|---|----------|---------------------------------------------|------|
| **Q1** | Sub-graph model | ✅ Inline tagged region via ports            | Simpler than nested workflows; reuses existing schema |
| **Q2** | Multi-port routing | ✅ Engine adds `ActivePorts`                 | Backwards compatible; default keeps current behaviour |
| **Q3** | Expression language | ✅ Sandboxed                                 | Implementation choice in Q7 |
| **Q4** | Loop scope | ✅ Per-iteration variable subscope           | Uses existing `IVariableStore` |
| **Q5** | Error boundaries | ✅ Engine-managed zones                      | Not Akka supervision |
| **Q6** | Fan-out/Fan-in | ✅ Dedicated modules over shared coordinator | — |
| **Q7** | Expression engine choice | ✅ **Jint (JS/ES2020)** — default; DynamicExpresso as `"csharp"` fallback | Native `EvaluateAsync` + CT + `async/await` — full analysis: [Phase2-2-ExpressionEngine-Analysis.md](./Phase2-2-ExpressionEngine-Analysis.md) |
| **Q8** | Loop-body addressing | ✅ **Hybrid in 2.2** — ports drive execution; `NodeDefinition.RegionId?` hint field for visual designer | Engine ignores `regionId`; tooling auto-populates; Phase 3 designer reads for bounding box. Full analysis: [Phase2-2-LoopBodyAddressing.md](./Phase2-2-LoopBodyAddressing.md) |
| **Q9** | Parallel cancel semantics | Use Cooperative plus grace timeout          | Recommend cooperative + grace timeout |
| **Q10** | `builtin.switch` in 2.2 | Use In for now                              | Recommend in (shares routing work) |

---

> 💖 **Ami’s Phase 2.2 Tips:**
> - Build **2.2.0a → 2.2.0b first** — every other sub-phase needs port-aware routing + sub-graph execution. 2.2.1 (conditional/switch) can ship right after 2.2.0a. Don’t skip ahead, nya~ 🧠
> - Keep modules **declarative**: control-flow logic lives in the engine, not in module code beyond setting `ActivePorts` and asking for sub-graph runs~ ✨
> - Pin `IExpressionEvaluator` behind an interface from day one — Jint is the default, but DynamicExpresso (`"csharp"`) and Lua (`"lua"`) can be added as keyed opt-ins without touching any module code~ 🔌
> - Use the **SQLite `:memory:`** provider from Phase 2.1 for end-to-end tests — no Docker, fast, full persistence path exercised~ 💾
> - Loop persistence rows = goldmine for the future UI replay timeline; record `loopId`/`iter` from day one~ 🎀 UwU

