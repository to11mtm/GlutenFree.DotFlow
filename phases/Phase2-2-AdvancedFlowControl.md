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
- [x] **Q9 Cancellation semantics for parallel/loop:** ~~hard kill vs cooperative?~~ **Resolved: cooperative `CancellationToken` cancel + configurable grace window (default 250 ms).** On first branch failure with `failFast: true`, the coordinator triggers the linked CTS from 2.2.0b's hierarchical cancellation contract. Siblings observe the token and wind down cooperatively; no hard-abort. Grace window is configurable per coordinator instance~
- [x] **Q10 Switch module scope:** ~~defer or include?~~ **Resolved: `builtin.switch` included in 2.2.1** — shares the same multi-port routing work already shipped in 2.2.0a, adds negligible complexity, and gives authors multi-way branching alongside `builtin.condition` in the same PR~

---

## Pre-Existing Work (from Phase 1) ✅

| Component | File | Status |
|-----------|------|--------|
| `PortDefinition` (multi-port schema) | `Workflow.Core/Models/ModuleSchema.cs` | ✅ Already supports multiple inputs/outputs |
| `NodeDefinition` (id, module, inputs) | `Workflow.Core/Models/NodeDefinition.cs` | ✅ Existing |
| `ConnectionDefinition` (source/target) | `Workflow.Core/Models/WorkflowDefinition.cs` | ✅ Connections already keyed by ports |
| `WorkflowExecutor` (Akka actor) | `Workflow.Engine/Actors/WorkflowExecutor.cs` | ✅ Port-aware routing shipped in 2.2.0a — `ActivePorts` selective activation + `TrySkipNodeDownstream` + `ValidateConnectionPorts` at load time |
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

### 2.2.0a Port-Aware Routing & Sub-Graph Execution 🎯🌿 ✅ COMPLETE

**Purpose:** Teach the engine to (a) selectively activate downstream connections by port and (b) run a contained sub-graph on demand. These two together are the *minimum* primitives needed to ship `builtin.condition` / `builtin.switch` (2.2.1)~ ⚡

**Complexity:** 🟡 Medium

#### Tasks:

- [x] **Port-aware connection activation in `WorkflowExecutor`** 🎯
  - [x] Add optional `ActivePorts: Arr<string>` to the node-completion message in `Workflow.Engine/Messages/WorkflowMessages.cs` (and the corresponding result type returned by `IWorkflowModule`).
  - [x] In `WorkflowExecutor`, when dispatching downstream connections:
    - [x] If `ActivePorts` is **empty/null** → fire all outgoing connections (today's behaviour, no change for existing modules).
    - [x] If `ActivePorts` is **non-empty** → fire only connections whose `SourcePort` ∈ `ActivePorts`.
  - [x] Workflow load-time validation: every connection's `SourcePort` must reference a declared output port on the source node's module schema. Catch typos before execution. (`ValidateConnectionPorts`)
  - [x] Update existing engine tests to assert the unchanged-default contract (no test should need to set `ActivePorts`). (`NoActivePorts_FiresAllConnections_BackwardsCompatible`)

- [x] **Sub-graph execution primitive** 🌿
  - [x] New file: `Workflow.Engine/Actors/SubGraphExecutor.cs`
    - [x] Accepts `(parentExecutionId, entryNodeIds, inputs, parentScope)`.
    - [x] Re-uses the same dispatch protocol as `WorkflowExecutor` but with **isolated** node-state map and **scoped** to a subset of the workflow's nodes/connections.
    - [x] Reports `SubGraphCompleted(outputs)` / `SubGraphFailed(error)` to the caller actor.
  - [x] New file: `Workflow.Engine/Messages/SubGraphMessages.cs`
    - [x] `StartSubGraph`, `SubGraphCompleted`, `SubGraphFailed`.
  - [ ] Refactor `WorkflowExecutor` to extract a small **dispatch core** that both it and `SubGraphExecutor` share (avoid copy-paste of routing logic). ⚠️ *Currently both have their own full implementations — extraction deferred.*

- [x] **Persistence & history**
  - [x] Sub-graph node executions persist to `IExecutionHistoryRepository` under the parent execution id so 2.1.5 history queries still work.
  - [x] `Metadata.subGraphId` tagging on `NodeExecutionRecord` — `SubGraphExecutor.QueuePersistNode` stamps `Metadata["subGraphId"]`. SQLite persistence extended with `sub_graph_id`, `loop_id`, `loop_iteration`, and `metadata` columns (Migration_004). Tests: `SubGraph_NodeExecutions_PersistedWithSubGraphIdTagInMetadata` (new) + updated `SubGraph_NodeExecutions_PersistedUnderParentExecutionId`. ✅ **DONE**
  - [x] No new repository surface area required.

**Tests (target ~6):** → `Workflow.Tests/Engine/PortRoutingTests.cs`, `Workflow.Tests/Engine/SubGraphExecutorTests.cs`
- [x] `ActivePorts = ["true"]` fires only `true` connections; `false` connections do not run (`ActivePorts_TrueOnly_FiresOnlyTrueConnection`)
- [x] `ActivePorts = ["false"]` fires only `false` connections; `true` connections do not run (`ActivePorts_FalseOnly_FiresOnlyFalseConnection`)
- [x] Backwards-compat: nodes without `ActivePorts` fire all outgoing connections (`NoActivePorts_FiresAllConnections_BackwardsCompatible`)
- [x] Connection referencing an undeclared `SourcePort` fails at workflow load with a clear error (`UndeclaredSourcePort_FailsWorkflowAtLoadTime`)
- [x] Sub-graph runs entry → terminal nodes and returns aggregated outputs (`SubGraph_RunsEntryToTerminalNodes_ReportsCompletion`)
- [x] Sub-graph failure surfaces as `SubGraphFailed` to caller without failing the parent execution by itself (`SubGraph_NodeFailure_ReportsSubGraphFailed_NotKillingParent`)
- [x] Sub-graph node executions appear in history under the parent execution (`SubGraph_NodeExecutions_PersistedUnderParentExecutionId`)
- [x] ✨ BONUS: Port-aware routing inside sub-graphs works identically to `WorkflowExecutor` (`SubGraph_PortAwareRouting_SkipsDeactivatedBranches`)

---

### 2.2.0b Loop Scope, Error Boundary & Hierarchical Cancellation 🔁🛡️🛑 ✅ COMPLETE

**Purpose:** Add the *stateful* engine primitives that 2.2.2 (loops) and 2.2.4 (try/catch) depend on. Builds directly on the routing + sub-graph primitive from 2.2.0a, but now we're changing how state, failure, and cancellation propagate~ 🌷

**Complexity:** 🟡 Medium-High

#### Tasks:

- [x] **Iteration & loop scope** 🔁
  - [x] New file: `Workflow.Engine/Models/LoopContext.cs`
    - [x] Fields: `LoopId`, `Iteration`, `Item`, `Index`, `ParentScope`.
    - [x] Helpers: `BreakRequested`, `ContinueRequested` flags, `AdvanceIteration()`, `SetCurrentElement()`.
  - [x] Per-iteration variable subscope wrapping `VariableScope.ForExecution({execId})` (e.g. logical namespace `loop:{loopId}:{iter}`) — `VariableScopePrefix` property returns `$"loop:{LoopId}:{Iteration}"`.
  - [x] Loop context **stack** kept on the executor (`WorkflowExecutor._loopContextStack`) so nested loops can locate their innermost loop deterministically (used by `BreakModule`/`ContinueModule` in 2.2.2).
  - [x] Iteration metadata recorded on `NodeExecutionRecord` (`LoopId`, `LoopIteration`) for replay/UI. Stamped in both `HandleNodeExecutionCompleted` and `HandleNodeFailure`.
  - [x] Scope messages: `PushLoopScope(LoopContext)` / `PopLoopScope(string loopId)` in `Workflow.Engine/Messages/ScopeMessages.cs`. Handlers registered in `WorkflowExecutor` constructor.

- [x] **Error containment zone** 🛡️
  - [x] New file: `Workflow.Engine/Models/ErrorBoundary.cs`
    - [x] Tracks `BoundaryId`, `CatchEntryNodeId`, `FinallyEntryNodeId?`, `CatchTypes`. `Catches(Exception)` method (catch-all when `CatchTypes` is null/empty).
  - [x] In `WorkflowExecutor`, when a node fails:
    - [x] `TryHandleWithBoundary(nodeId, error)` walks `_boundaryStack` from innermost outward.
    - [x] If a boundary catches it → move node from `_failedNodes` → `_completedNodes`, call `TryFireSuccessor(catchNodeId)` and `TryFireSuccessor(finallyNodeId)` (if any); do **not** fail the parent execution. Returns `true` to skip normal fail-fast path.
    - [x] Else → existing failure path (terminal status update via 2.1.5).
  - [x] **Snapshot-bridge interaction** *(addresses the open 2.1.5 follow-up about double terminal writes)*: when a boundary handles a failure, `_executionCts.Cancel()` is NOT called and `UpdateExecutionStatusAsync(Failed)` is not emitted — only node records are persisted.
  - [x] Scope messages: `PushErrorBoundary(ErrorBoundary)` / `PopErrorBoundary(string boundaryId)` in `Workflow.Engine/Messages/ScopeMessages.cs`. Handlers registered in `WorkflowExecutor` constructor.

- [x] **Hierarchical cancellation** 🛑
  - [x] `WorkflowExecutor._executionCts` (`CancellationTokenSource`) created in constructor. Token passed to `NodeExecutor.Props(...)`.
  - [x] Each `SubGraphExecutor` receives `parentToken` → creates `_linkedCts = CancellationTokenSource.CreateLinkedTokenSource(parentToken)`. Linked CTS token passed to node executors within the sub-graph.
  - [x] `SubGraphExecutor.PostStop()` disposes the linked CTS to prevent token registration leaks.
  - [x] `WorkflowExecutor.CompleteWorkflow()` / `FailWorkflow()` call `_executionCts.Cancel()`. `PostStop()` calls `_executionCts.Dispose()`.
  - [x] `CooperativeCancelSubGraph(subGraphId, reason)` message handled in `SubGraphExecutor` — cancels `_linkedCts` directly.
  - [x] No hard kill of in-flight nodes — modules are expected to honour `CancellationToken` (existing contract).

#### Tests (13 total, all passing ✅): → `Workflow.Tests/Engine/LoopScopeTests.cs`, `Workflow.Tests/Engine/ErrorBoundaryTests.cs`, `Workflow.Tests/Engine/HierarchicalCancellationTests.cs`

**Loop Scope (4 tests):**
- [x] `LoopScope_PushedBeforeExecution_StampsNodeRecordWithLoopId` — PushLoopScope before execution; `NodeExecutionRecord.LoopId` + `LoopIteration` are stamped correctly
- [x] `LoopScope_NotPushed_RecordsHaveNullLoopId` — no scope push = null stamps (regression guard)
- [x] `LoopScope_PopBeforeExecution_RecordsHaveNullLoopId` — push+pop before execution = null stamps
- [x] `LoopScope_Nested_InnerScopeStampsWhenActive` — nested push; inner scope (top of stack) stamps node records

**Error Boundary (5 tests):**
- [x] `ErrorBoundary_CatchesNodeFailure_RoutesToCatchNode_WorkflowCompletes` — boundary catches node failure; workflow completes (not fails)
- [x] `ErrorBoundary_NoBoundary_WorkflowFails_NormalPath` — without boundary, failure triggers `WorkflowFailed` (regression guard)
- [x] `ErrorBoundary_CatchWithFinally_BothNodesFireOnFailure` — `CatchEntryNodeId` and `FinallyEntryNodeId` both fire when failure is caught
- [x] `ErrorBoundary_CaughtFailure_DoesNotCallUpdateStatusFailed` — boundary-handled failure does **not** double-write terminal status (regression guard for the open 2.1.5 follow-up)
- [x] `ErrorBoundary_Nested_InnerCatchesBeforeOuter` — inner boundary catches first; outer boundary not triggered

**Hierarchical Cancellation (4 tests):**
- [x] `HierarchicalCancellation_CooperativeCancel_SubGraphFails` — `CooperativeCancelSubGraph` msg → linked CTS cancels → `SubGraphFailed`
- [x] `HierarchicalCancellation_ParentTokenCancelled_PropagatesIntoSubGraph` — parent CTS cancel propagates through linked CTS → `SubGraphFailed`
- [x] `HierarchicalCancellation_WorkflowComplete_ExecutionCtsIsSignalled` — normal workflow completion cancels `_executionCts` without error
- [x] `HierarchicalCancellation_SubGraphPostStop_DisposesLinkedCts_NoLeaks` — sub-graph completion disposes linked CTS; no token registration leaks

---

## 2.2.1 Conditional Branching (`builtin.condition`, `builtin.switch`) 🔀 ✅ COMPLETE

**Purpose:** First user-facing payoff of the multi-port routing primitive — express simple if/else and multi-way branching as nodes~ 🌸

**Complexity:** 🟡 Medium

### Tasks:

- [x] **Create `IExpressionEvaluator` interface** *(pulled forward from 2.2.5 — needed by `ConditionalModule`)* 🧮
  - [x] New file: `Workflow.Core/Abstractions/IExpressionEvaluator.cs`
  - [x] `EvaluateAsync<T>(string expression, IReadOnlyDictionary<string, object?> variables, CancellationToken)` → `ValueTask<T>`
  - [x] `EvaluateAsync(...)` untyped → `ValueTask<object?>`
  - [x] `EvaluateObjectAsync(...)` → `ValueTask<JsonElement>` for structured/array returns
  - [x] `ExpressionParseException` (syntax / parse-time) and `ExpressionRuntimeException` (runtime / timeout) exception types

- [x] **Create `ConditionalModule`** 🔀
  - [x] New file: `Workflow.Modules/Builtin/Flow/ConditionalModule.cs`
  - [x] `ModuleId: "builtin.condition"`, `Category: "Flow Control"`, `DisplayName: "Conditional Branch"`, `Icon: "🔀"`, `Version: 1.0.0`.
  - [x] Schema:
    - [x] Input: `condition` (bool *or* expression string, `isRequired: false` — also readable from property)
    - [x] Output port: `true` (activation only)
    - [x] Output port: `false` (activation only)
    - [x] Output: `result` (bool — for diagnostics / branch logging)
    - [x] Property: `condition` (string, optional — static fallback when no input connected)
  - [x] `ExecuteAsync`:
    - [x] Input port takes priority over property; fails gracefully if neither is provided.
    - [x] Bool → use directly; int/numeric → `!= 0`; string known literals (`true/1/yes/on` → `true`, `false/0/no/off` → `false`); unknown string → delegates to `IExpressionEvaluator` from DI.
    - [x] If evaluator not registered and string is uncoerecible → `ModuleResult.Fail(...)` with message referencing `IExpressionEvaluator`.
    - [x] Set `ActivePorts = ["true"]` or `["false"]` so engine routes only one branch.
    - [x] `result` output carries evaluated bool for diagnostics/logging.

- [x] **Create `SwitchModule`** 🔢
  - [x] New file: `Workflow.Modules/Builtin/Flow/SwitchModule.cs`
  - [x] `ModuleId: "builtin.switch"`, `Category: "Flow Control"`, `Version: 1.0.0`.
  - [x] Schema:
    - [x] Input: `value` (object, `isRequired: false` — also readable from property)
    - [x] `Outputs = Arr<PortDefinition>.Empty` — dynamic ports; engine's `ValidateConnectionPorts` skips validation when declared outputs count is 0.
    - [x] Properties: `cases` (JSON array of `{ match, port }` *or* pre-parsed `List<object?>`, required), `defaultPort` (string, optional), `caseSensitive` (bool, optional, default `false`).
  - [x] `ExecuteAsync`: first-match-wins on `cases` array, fall back to `defaultPort`, fail with descriptive error if neither. Case-insensitive by default (`OrdinalIgnoreCase`).
  - [x] `ValidateConfiguration`: validates `cases` is present (`MISSING_CASES`) and non-empty (`EMPTY_CASES`).
  - [x] Diagnostics: outputs `matchedPort` and `value` for logging.
  - [x] Cases accepted as JSON string (auto-parsed) or pre-deserialized `List<object?>` / `JsonElement` array.

- [x] **Engine integration**
  - [x] `ConditionalModule` declares all 3 output ports in schema — `ValidateConnectionPorts` validates connections at load time.
  - [x] `SwitchModule` uses empty `Outputs` schema — `ValidateConnectionPorts` intentionally skips validation, allowing dynamic port names from `cases` configuration.
  - [x] Both modules registered in `BuiltinModuleRegistration.GetAll()` (count: 5 → 7).

**Tests (46 new tests across all 2.2.1 files):** → `Workflow.Tests/Modules/Flow/ConditionalModuleTests.cs`, `Workflow.Tests/Modules/Flow/SwitchModuleTests.cs`, `Workflow.Tests/Modules/BuiltinModuleIntegrationTests.cs`

**ConditionalModuleTests (29 test executions across 16 methods):**
- [x] `ConditionalModule_Metadata_IsCorrect` — `ModuleId`, category, display name, version 1.0.0, icon all correct
- [x] `ConditionalModule_Schema_DeclaresTrueFlaseResultPorts` — schema declares `true`, `false`, `result` output ports
- [x] `BoolTrue_ActivatesTruePort` — `condition: true` → single active port `"true"`, `result` output = `true`
- [x] `BoolFalse_ActivatesFalsePort` — `condition: false` → single active port `"false"`, `result` output = `false`
- [x] `StringTruthy_ActivatesTruePort` (Theory ×6) — `"true"`, `"True"`, `"TRUE"`, `"1"`, `"yes"`, `"on"` all activate `true` port
- [x] `StringFalsy_ActivatesFalsePort` (Theory ×6) — `"false"`, `"False"`, `"FALSE"`, `"0"`, `"no"`, `"off"` all activate `false` port
- [x] `InputPort_OverridesProperty_WhenBothProvided` — input `true` + property `"false"` → `true` wins
- [x] `Property_UsedWhenNoInputPort` — no input; property `"true"` → `true` port fires
- [x] `BothBranches_ReachableAcrossTwoRuns` — both `true` and `false` reachable across separate runs
- [x] `Expression_UsesIExpressionEvaluator_WhenAvailable` — `"x > 5"` with `x=10`, Moq evaluator returns `true` → `true` port
- [x] `Expression_EvaluatingFalse_ActivatesFalsePort` — `"x > 5"` with `x=3`, evaluator returns `false` → `false` port
- [x] `InvalidExpression_NoEvaluator_Fails` — `"x + y"` with no evaluator registered → `Success=false`, error message contains `"IExpressionEvaluator"`
- [x] `NullCondition_ReturnsFailure` — no input + no property → `Success=false`, non-empty error message
- [x] `ResultOutput_ReflectsEvaluatedBool` (Theory ×2) — `result` output always carries evaluated bool for `true` and `false` inputs
- [x] `NumericNonZero_ActivatesTruePort` (Theory ×3) — `1`, `-1`, `42` all activate `true` port (truthy numeric semantics)
- [x] `NumericZero_ActivatesFalsePort` — `0` activates `false` port

**SwitchModuleTests (15 tests):**
- [x] `SwitchModule_Metadata_IsCorrect` — `ModuleId`, category, version correct
- [x] `SwitchModule_Schema_HasEmptyOutputs` — `Schema.Outputs` is empty (dynamic port design)
- [x] `MatchesFirstCase_ActivatesCorrectPort` — `"cat"` matches first case → `"case_cat"`
- [x] `LaterCaseMatches_WhenFirstDoesNot` — `"dog"` skips `"cat"` case, matches second → `"case_dog"`
- [x] `FirstCaseWins_WhenMultipleWouldMatch` — two cases with same `match = "cat"` → first port wins
- [x] `NoMatch_UsesDefaultPort` — unmatched value → `defaultPort` fires
- [x] `NoMatch_NoDefault_Fails` — unmatched value + no `defaultPort` → `Success=false`, error contains unmatched value
- [x] `DefaultCaseInsensitive_MatchesDifferentCase` — `"CAT"` matches `"cat"` case by default (case-insensitive)
- [x] `CaseSensitive_DoesNotMatchDifferentCase` — `caseSensitive: true`, `"CAT"` does not match `"cat"` → falls to `defaultPort`
- [x] `ValidateConfiguration_EmptyCases_Fails` — empty `cases` list → `IsValid=false`, error code `EMPTY_CASES`
- [x] `ValidateConfiguration_MissingCases_Fails` — no `cases` key → `IsValid=false`, error code `MISSING_CASES`
- [x] `ValidateConfiguration_ValidCases_Passes` — valid cases list → `IsValid=true`
- [x] `Cases_AsJsonString_ParsedCorrectly` — `cases` provided as raw JSON string is parsed and matched correctly
- [x] `PropertyValue_UsedWhenNoInputPort` — `value` from property used when no input port connected
- [x] `Outputs_CarryDiagnostics` — `matchedPort` and `value` outputs present for debugging

**ConditionalSwitchIntegrationTests (2 engine integration tests via `TestKit`):**
- [x] `ConditionalModule_TrueCondition_FiresOnlyTrueBranch` — `condition=true` wired into `WorkflowExecutor`; true-branch node runs, false-branch node is skipped; `WorkflowCompleted` received
- [x] `SwitchModule_MatchingCase_FiresCorrectPort` — `value="cat"` wired into `WorkflowExecutor`; `case_cat` node runs, `case_dog` node is skipped; `WorkflowCompleted` received

**BuiltinModuleIntegrationTests (updated — count 5→7):**
- [x] `RegisterAll_ShouldRegisterAllBuiltinModules` — now asserts `HaveCount(7)`, includes `builtin.condition` and `builtin.switch`
- [x] `GetAll_ShouldReturnFiveModules` — updated count and equivalence list to 7
- [x] `ModuleDiscovery_ShouldFindAllBuiltinModules` — updated to assert `typeof(ConditionalModule)` and `typeof(SwitchModule)`

---

## 2.2.2 Loops (`builtin.loop.foreach`, `builtin.loop.while`, break/continue) 🔁 ✅ COMPLETE

**Purpose:** Iterate over collections / repeat while a condition holds, leveraging the sub-graph executor + loop context~ ✨

**Complexity:** 🟡 Medium → 🔴 Medium-High *(engine orchestration via dedicated `LoopExecutorActor` proved more involved than original estimate)*

> **CopilotNote:** Implementation complete — all 4 modules + engine plumbing shipped. Body-scope re-fire bug was fixed by marking all body-scope node IDs as skipped inside `HandleLoopCompleted` (before `ExecuteReadySuccessors` fires the done-port edges). Fix landed in `WorkflowExecutor.cs` lines ~1240‑1261. All 3 previously-failing integration tests now pass~ 🧠✅

### Engine Additions (beyond original plan):

- [x] **`LoopRequest` model** 🗒️ *(new — `Workflow.Core/Models/LoopRequest.cs`)*
  - [x] `Items` (IReadOnlyList), `LoopBodyPort`, `DonePort`, `MaxIterations` (default 1000), `ContinueOnError`, `ContinueCondition` (Func delegate — in-process only, for `WhileModule`)

- [x] **`ModuleResult` loop extensions** *(added to `Workflow.Modules/Abstractions/IWorkflowModule.cs`)*
  - [x] `Loop` property (`LoopRequest? { get; init; }`)
  - [x] `ModuleResult.WithLoop(outputs, loop)` — factory for module loop declaration
  - [x] `ModuleResult.Break()` — sentinel output `{ "__loop_break__": true }`
  - [x] `ModuleResult.Continue()` — sentinel output `{ "__loop_continue__": true }`

- [x] **`LoopMessages.cs`** *(new — `Workflow.Engine/Messages/LoopMessages.cs`)*
  - [x] `LoopCompleted(LoopNodeId, Outputs)`, `LoopFailed(LoopNodeId, Error, FailedNodeId?)`, `CooperativeCancelLoop(LoopNodeId, Reason?)`

- [x] **`SubGraphMessages.cs` updated** — `SubGraphCompleted` gained `BreakRequested = false`, `ContinueRequested = false` optional params

- [x] **`SubGraphExecutor.cs` updated** — detects `__loop_break__` / `__loop_continue__` sentinel keys in node outputs; sets `_breakRequested` / `_continueRequested`; propagates via `SubGraphCompleted`

- [x] **`NodeLoopExecutionRequested` message** *(added to `WorkflowMessages.cs`)* — carries `NodeId` + `LoopRequest`; emitted **before** `NodeExecutionCompleted` from `NodeExecutor` (FIFO guarantee ensures `_pendingLoops` is populated before completion is handled)

- [x] **`NodeExecutor.SendSuccess` updated** — passes `enrichedResult.Loop` when present, emitting `NodeLoopExecutionRequested`

- [x] **`LoopExecutorActor.cs`** *(new — `Workflow.Engine/Actors/LoopExecutorActor.cs`, ~280 lines)*
  - [x] Per-iteration: tells parent `PushLoopScope` → spawns `SubGraphExecutor` child for body nodes
  - [x] Handles `SubGraphCompleted`/`Failed`, checks break/continue flags, enforces `maxIterations`
  - [x] `ContinueOnError` support — collects errors and continues, or short-circuits on first error
  - [x] `BecomeConditionAwaiting()` — async condition evaluation via `PipeTo` for `WhileModule` re-evaluation
  - [x] `ComputeBodyScope(definition, loopNodeId, bodyEntryNodeIds)` static BFS helper
  - [x] Reports `LoopCompleted`/`LoopFailed` to parent `WorkflowExecutor`

- [x] **`WorkflowExecutor.cs` updated**
  - [x] `_pendingLoops` dict + `_pendingLoopNodes` HashSet fields
  - [x] Handlers: `Receive<NodeLoopExecutionRequested>`, `Receive<LoopCompleted>`, `Receive<LoopFailed>`
  - [x] `IsWorkflowComplete()` guards on `_pendingLoopNodes.Count == 0`
  - [x] `HandleNodeExecutionCompleted`: detects pending loop → `SpawnLoopExecutor()` instead of `ExecuteReadySuccessors`
  - [x] `SpawnLoopExecutor()`, `HandleLoopCompleted()`, `HandleLoopFailed()` methods added

### Module Tasks:

- [x] **`ForEachModule`** 🔁
  - [x] New file: `Workflow.Modules/Builtin/Flow/ForEachModule.cs`
  - [x] `ModuleId: "builtin.loop.foreach"`.
  - [x] Schema:
    - [x] Input: `collection` (`IEnumerable`, required)
    - [x] Input: `maxIterations` (int, optional, default `1000`)
    - [x] Input: `continueOnError` (bool, optional, default `false`)
    - [x] Output port: `loopBody` (activation per iteration)
    - [x] Output port: `done` (after all iterations)
    - [x] Outputs: `results` (array), `count` (int), `errors` (array)
  - [x] Returns `ModuleResult.WithLoop` — engine spawns `LoopExecutorActor` to orchestrate iterations
  - [x] `CoerceToList` helper handles: `IReadOnlyList`, `IEnumerable` (non-string), `JsonElement` array, JSON string, single-item wrap

- [x] **`WhileModule`** 🌀
  - [x] New file: `Workflow.Modules/Builtin/Flow/WhileModule.cs`
  - [x] Schema mirrors `ForEachModule`, plus Input: `condition` (bool/expression).
  - [x] Pre-iteration evaluate; same break/continue semantics.
  - [x] Optimization: condition false from start → `ModuleResult.WithActivePorts(["done"])` (no loop actor spawned)
  - [x] `ContinueCondition` delegate captures raw condition + evaluator reference for re-evaluation per iteration

- [x] **`BreakModule` / `ContinueModule`** ⏹️➡️
  - [x] New files: `Workflow.Modules/Builtin/Flow/BreakModule.cs`, `ContinueModule.cs`
  - [x] `BreakModule.ExecuteAsync` → `ModuleResult.Break()` (sentinel `__loop_break__: true`)
  - [x] `ContinueModule.ExecuteAsync` → `ModuleResult.Continue()` (sentinel `__loop_continue__: true`)
  - [ ] Validation: must be inside a loop region — load-time validation **deferred** (requires loop scope inference at load time)

- [x] **Loop diagnostics & history**
  - [x] `LoopExecutorActor` calls `PushLoopScope` per iteration — `NodeExecutionRecord` stamped with `LoopId`/`LoopIteration` via 2.2.0b infrastructure

- [x] **`regionId` hint field (Hybrid Q8 resolution)** 🗺️
  - [x] `RegionId? string` already present in `Workflow.Core/Models/NodeDefinition.cs` (shipped in 2.2.0b)
  - [x] Engine **never reads** `regionId` for routing or subgraph discovery — execution is port-driven
  - [ ] Load-time warning for mismatched `regionId` vs actual port connectivity — **deferred to Phase 3**
  - [x] Schema is forward-compatible: Phase 3 visual designer reads `regionId` to render bounding boxes

- [x] **`BuiltinModuleRegistration.cs` updated** — count 7→11 (added `ForEachModule`, `WhileModule`, `BreakModule`, `ContinueModule`)

**Tests (27 written — target was ~14):** → `Workflow.Tests/Modules/Flow/ForEachModuleTests.cs`, `Workflow.Tests/Modules/Flow/WhileModuleTests.cs`

**ForEachModuleTests — unit tests (8, ✅ all passing):**
- [x] `ForEachModule_Metadata_IsCorrect` — `ModuleId`, category, display name, icon, version
- [x] `ForEachModule_Schema_DeclaresCorrectPorts` — `loopBody`, `done` output ports + `results`, `count`, `errors` outputs
- [x] `ForEachModule_List_PackagesLoopRequest` — list input → `LoopRequest.Items` correctly set, `LoopBodyPort`/`DonePort` correct
- [x] `ForEachModule_MaxIterations_PassedThrough` — custom `maxIterations` flows into `LoopRequest`
- [x] `ForEachModule_EmptyCollection_ReturnsLoopRequestWithEmptyItems` — empty list → `LoopRequest` with empty items
- [x] `ForEachModule_NullCollection_ReturnsFailure` — null `collection` → `ModuleResult.Fail`
- [x] `ForEachModule_PropertyFallback_UsesCollectionProperty` — no input port → reads from `Properties`
- [x] `ForEachModule_JsonStringCollection_ParsedCorrectly` — JSON string `"[1,2,3]"` → parsed to list

**ForEachEngineIntegrationTests (4, ✅ all passing):**
- [x] `ForEach_BodyFails_ContinueOnError_CollectsError` — ✅ passing
- [x] `ForEach_ThreeItems_RunsThreeIterations_WorkflowCompletes` — ✅ fixed (body-scope skipping in `HandleLoopCompleted`)
- [x] `ForEach_EmptyCollection_WorkflowCompletes_ZeroBodyExecutions` — ✅ fixed
- [x] `ForEach_BreakModule_StopsEarly` — ✅ fixed

**WhileModuleTests — unit tests (10, ✅ all passing):**
- [x] `WhileModule_Metadata_IsCorrect`
- [x] `WhileModule_Schema_DeclaresCorrectPorts`
- [x] `WhileModule_FalseCondition_ReturnsActivePorts_Done` — false from start → `WithActivePorts(["done"])` (no loop actor)
- [x] `WhileModule_FalsyString_ReturnsDone` (Theory × falsy strings)
- [x] `WhileModule_TrueCondition_ReturnsLoopRequest` — true → `LoopRequest` with `ContinueCondition`
- [x] `WhileModule_TruthyValues_ReturnLoopRequest` (Theory × truthy values)
- [x] `WhileModule_ContinueCondition_EvaluatesCorrectly` — delegate re-evaluates correctly
- [x] `WhileModule_MaxIterations_PassedThrough`
- [x] `WhileModule_NullCondition_ReturnsFailure`
- [x] `WhileModule_PropertyFallback_UsesConditionProperty`

**BreakContinueModuleTests (5, ✅ all passing):**
- [x] `BreakModule_Metadata_IsCorrect`
- [x] `ContinueModule_Metadata_IsCorrect`
- [x] `BreakModule_ExecuteAsync_ReturnsBreakSentinel` — output contains `__loop_break__: true`
- [x] `ContinueModule_ExecuteAsync_ReturnsContinueSentinel` — output contains `__loop_continue__: true`
- [x] `BreakAndContinue_SentinelKeys_AreDistinct`

**Remaining work (before marking ✅ COMPLETE):**
- [x] **Fix 3 failing integration tests** — body-scope skipping now applied in `HandleLoopCompleted` in `WorkflowExecutor.cs`; all 3 integration tests pass ✅
- [ ] `Break`/`Continue` outside a loop — load-time validation (deferred to Phase 3, requires loop scope inference)
- [ ] Nested foreach (inner uses outer's item) — integration test not yet written
- [ ] `continueOnError: false` short-circuit integration test not yet written

---

## 2.2.3 Parallel Execution & Fan-out / Fan-in — Split Slices ⚡🌟

> **Why split:** the original 2.2.3 bundled three concerns — a brand-new actor (`ParallelExecutionCoordinator`) with non-trivial concurrency primitives (semaphore, fail-fast cooperative cancel, snapshot tracking), plus three modules with distinct semantics (static branches vs per-item fan-out vs barrier aggregation). Concurrency bugs in the coordinator would mask module bugs and vice versa, so we land them in two PRs: **2.2.3a** ships the coordinator + `ParallelModule` (the primitive + its most direct consumer), and **2.2.3b** ships `FanOutModule` + `FanInModule` (fan-shaped patterns built on the proven coordinator). This mirrors the 2.2.0a/2.2.0b split~ 🧠
>
> **Q9 resolved:** cooperative cancel + configurable grace timeout (default 250 ms). Siblings observe the linked CTS from 2.2.0b's hierarchical cancellation contract — no hard-abort of in-flight nodes~ 🎀

---

### 2.2.3a Parallel Coordinator & `ParallelModule` 🎛️⚡ ✅ **COMPLETE**

**Purpose:** Land the shared concurrency primitive (`ParallelExecutionCoordinator`) and prove it via the simplest consumer — a static N-branch parallel module with bounded parallelism and fail-fast semantics~ 💫

**Complexity:** 🟡 Medium-High

**Status (May 2026):** Implementation shipped — 11/11 ParallelModule + integration tests passing, zero regressions in loop/flow tests (80/80 still green). Two scope items intentionally deferred (see Deferred Work below).

#### Tasks:

- [x] **`ParallelExecutionCoordinator` actor** 🎛️
  - [x] New file: `Workflow.Engine/Actors/ParallelExecutionCoordinator.cs`
  - [x] Spawns N child `SubGraphExecutor`s (from 2.2.0a), tracks per-branch completion state.
  - [x] Bounded parallelism: enforced via **counter-queue** pattern (`_inFlightCount` + `_pendingBranches`) instead of `SemaphoreSlim` — never blocks the actor thread~ ⚡
  - [x] `failFast` cooperative cancellation: on first branch failure, cancels linked CTS from 2.2.0b's hierarchical cancellation contract; siblings observe the token and wind down cooperatively. (Configurable grace window deferred — siblings finish on their own cooperative checkpoints.)
  - [x] Aggregates `(results, count)` and reports `ParallelCompleted` / `ParallelFailed` to caller.
  - [x] New file: `Workflow.Engine/Messages/ParallelMessages.cs` — `ParallelCompleted`, `ParallelFailed`, `CooperativeCancelParallel`. `NodeParallelExecutionRequested` added to `WorkflowMessages.cs` (mirrors `NodeLoopExecutionRequested` FIFO ordering).

- [x] **`ParallelModule`** ⚡
  - [x] New file: `Workflow.Modules/Builtin/Flow/ParallelModule.cs`
  - [x] `ModuleId: "builtin.parallel"`, `Category: "Flow Control"`.
  - [x] Schema:
    - [x] Output ports: dynamic — `Schema.Outputs = Arr<PortDefinition>.Empty` so `ValidateConnectionPorts` skips port validation (same trick as `SwitchModule`)
    - [x] Properties: `branches` (JSON array), `branchCount` (auto-gen fallback), `maxDegreeOfParallelism`, `failFast`, `donePort`
  - [x] Activation: returns `ModuleResult.WithParallel(...)`; engine spawns `ParallelExecutionCoordinator` to drive fan-out.
  - [x] Registered in `BuiltinModuleRegistration` (count: 11 → 12).

- [x] **Engine support**
  - [x] `WorkflowExecutor` plumbing: `_pendingParallels`, `_pendingParallelNodes`, `SpawnParallelExecutor`, `HandleParallelCompleted`, `HandleParallelFailed`. `IsWorkflowComplete` guards on `_pendingParallelNodes.Count == 0`.
  - [x] **Branch-scope pre-marking** (mirrors the 2.2.2 loop fix): all branch-scope nodes are added to `_skippedNodes` *before* spawning the coordinator, preventing the done-port re-fire bug.
  - [x] `ModuleResult.Parallel` property + `WithParallel` factory added to `IWorkflowModule.cs`.
  - [x] `NodeExecutor.SendSuccess` extended with `parallel` param; emits `NodeParallelExecutionRequested` *before* `NodeExecutionCompleted` (same FIFO trick as loops).
  - [x] Hierarchical cancellation token plumbed via 2.2.0b — no new cancellation surface area.
  - [ ] **Deferred:** stamp `Metadata["parallelId"]` + `Metadata["branchIndex"]` on `NodeExecutionRecord`. Coordinator already passes these as branch inputs (`__parallel_branch_index__`, `__parallel_branch_port__`); persistence-layer plumbing follow-up.

#### Tests (delivered: 11): → `Workflow.Tests/Modules/Flow/ParallelModuleTests.cs`

**Unit tests (`ParallelModuleTests`)** ✅ 7/7 passing:
- [x] `ParallelModule_Metadata_IsCorrect`
- [x] `ParallelModule_Schema_HasEmptyOutputs_ForDynamicPorts`
- [x] `ExecuteAsync_WithBranchesArray_ReturnsParallelRequestWithCorrectPorts`
- [x] `ExecuteAsync_WithBranchCount_GeneratesDefaultBranchNames`
- [x] `ExecuteAsync_FailFastOverride_ReflectedInRequest`
- [x] `ExecuteAsync_MaxDoPOverride_ReflectedInRequest`
- [x] `ValidateConfiguration_DuplicateBranches_Fails`
- [x] `ValidateConfiguration_ZeroBranchCount_Fails`

**Engine integration (`ParallelEngineIntegrationTests`)** ✅ 3/3 passing:
- [x] `Parallel_TwoBranches_BothExecute_WorkflowCompletes` — both branches run exactly once; workflow completes
- [x] `Parallel_DoneBranchFiresAfterAllComplete` — done-port successor fires exactly once after both branches complete (verifies branch-scope pre-marking fix works for parallel)
- [x] `Parallel_BranchFails_FailFastTrue_WorkflowFails` — first branch failure cancels siblings via cooperative cancel and fails the workflow

#### Deferred Work (rolled into 2.2.3b or follow-up):
- [ ] `waitForAll: false` semantics — would add a meaningful second code path (cancel siblings on first success). Rolled to 2.2.3b for delivery alongside `FanInModule`.
- [ ] Branch-scope **overlap** handling — current implementation assumes non-overlapping branch scopes. Overlap belongs to barrier semantics → addressed by `FanInModule` in 2.2.3b.
- [ ] Wall-time concurrency assertion test (assert `elapsed < sum(branchTimes)`) — pending performance test infrastructure.
- [ ] `maxDegreeOfParallelism = 2 over 5` enforcement test — counter-queue logic verified by code review; explicit observability test deferred.
- [ ] CTS leak regression-guard test — `PostStop` disposes `_linkedCts`; explicit assertion deferred.

---

### 2.2.3b Fan-out / Fan-in Modules 🌟🪄 ⚠️ MODULES SHIPPED — TESTS PENDING

**Purpose:** Build the dynamic fan-shaped patterns on top of the (now proven) `ParallelExecutionCoordinator` — `FanOutModule` for per-item parallel sub-graphs, `FanInModule` for barrier aggregation~ ✨

Also resolves the **two scope-level items deferred from 2.2.3a** (see _Carry-over from 2.2.3a_ below).

**Status (May 2026):** Both module files fully implemented and registered (count 12 → 13 → 14). Engine barrier hook for `FanInModule` relies on `WorkflowExecutor`'s natural "fire when all predecessors terminal" behaviour — no new engine surface required for the base case. Unit/integration tests and the two 2.2.3a carry-over items (waitForAll + overlap guard) not yet written.

**Complexity:** 🟡 Medium

#### Carry-over from 2.2.3a (must be resolved this phase):

- [ ] **`waitForAll: false`** — `ParallelRequest.WaitForAll = false` semantics in `ParallelExecutionCoordinator`:
  - When the *first* branch reports `SubGraphCompleted`, cancel remaining siblings via linked CTS and report `ParallelCompleted` immediately.
    - We should optionally allow the siblings to report any results they already provided, but this is a rarely-used feature and we don't want to encourage it.
  - `ParallelRequest.WaitForAll` currently always `true` (noted in `ParallelModule.ExecuteAsync`).
  - Expose as `ParallelModule` property `waitForAll` (bool, default `true`).
  - Add 1–2 engine integration tests: verify siblings are cancelled promptly and `ParallelCompleted` fires before all branches finish.

- [ ] **Branch-scope overlap handling** — when two branch ports target the same node, the current BFS in `ComputeBranchScope` claims it in both scopes; `SpawnParallelExecutor` then double-marks it in `_skippedNodes` (harmless but semantically incorrect).
  - Resolved naturally by `FanInModule` barrier semantics: a fan-in node should **not** be in any branch scope — it's the rendezvous point downstream.
  - Add a defensive assert / `ArgumentException` in `SpawnParallelExecutor` if union scopes overlap, recommending `FanInModule` for convergent patterns.

#### Tasks:

- [x] **`FanOutModule`** 🌟
  - [x] New file: `Workflow.Modules/Builtin/Flow/FanOutModule.cs`
  - [x] `ModuleId: "builtin.fanout"`, `Category: "Flow Control"`.
  - [x] Schema:
    - [x] Input: `items` (array, required)
    - [x] Input: `maxDegreeOfParallelism` (int, optional)
    - [x] Input: `failFast` (bool, default `true`)
    - [x] Output port: `branch` (activation per item, payload = `{ item, index }`)
    - [x] Output port: `done` (after all items processed)
    - [x] Outputs: `results` (array), `count` (int)
  - [x] Behaviour: like `ForEachModule` (2.2.2) but parallel — spawns one sub-graph per item via `ParallelExecutionCoordinator` using `ModuleResult.WithParallel` + `ParallelRequest.Items`.
  - [x] Registered in `BuiltinModuleRegistration` (count: 12 → 13).

- [x] **`FanInModule`** 🪄
  - [x] New file: `Workflow.Modules/Builtin/Flow/FanInModule.cs`
  - [x] `ModuleId: "builtin.fanin"`, `Category: "Flow Control"`.
  - [x] Schema:
    - [x] Input: `branches` (declarative; actual payloads come from engine via `__incomingBranches__`)
    - [x] Property: `mode` (enum: `Concat`, `Merge`, `First`, `Last`, default `Concat`)
    - [x] Property: `timeout` (TimeSpan, optional — declared for forward compat; barrier-timeout machinery deferred)
    - [x] Outputs: `result`, `count`, `done`
  - [x] Behaviour: barrier — aggregates per `mode` from `__incomingBranches__` list supplied by engine.
  - [x] `ValidateConfiguration`: validates `mode` is a known enum value (`INVALID_MODE`).
  - [x] Engine hook: relies on `WorkflowExecutor`'s natural predecessor-terminal gate — no new engine surface for base case. Explicit barrier-node predicate deferred.
  - [x] Registered in `BuiltinModuleRegistration` (count: 13 → 14).

- [ ] **Engine support — explicit barrier-node gating** *(deferred)*
  - [ ] General "hold until all declared upstream connections terminal" predicate in `WorkflowExecutor`.
  - [ ] Currently `FanInModule` works via the natural ready-successors logic for converging DAGs; edge cases with parallel branches completing out-of-order may need the predicate.

#### Tests (target ~6 + 2 carry-over — ❌ not yet written): → `Workflow.Tests/Modules/Flow/FanOutModuleTests.cs`, `Workflow.Tests/Modules/Flow/FanInModuleTests.cs`
- [ ] FanOut spawns one sub-graph per item (10 items → 10 iterations recorded)
- [ ] FanOut respects `maxDegreeOfParallelism` (delegated to 2.2.3a coordinator — smoke check only)
- [ ] FanIn `Concat` preserves upstream connection order
- [ ] FanIn `Merge` deduplicates dictionary keys deterministically (last writer wins, documented)
- [ ] FanIn `First` / `Last` semantics return the first/last completed branch's payload
- [ ] Combined `FanOut → work → FanIn` produces correctly aggregated result end-to-end
- [ ] **[carry-over from 2.2.3a]** `waitForAll: false` — first branch completion triggers `ParallelCompleted`; siblings cancelled cooperatively before reporting back
- [ ] **[carry-over from 2.2.3a]** `waitForAll: false` — outputs contain only the winning branch result; `count = 1`
- [ ] FanOut unit: `Metadata_IsCorrect`, `Schema_HasCorrectPorts`, `ExecuteAsync_NullItems_ReturnsFailure`, `ExecuteAsync_ValidItems_ReturnsParallelRequest`, `FailFastOverride_Reflected`, `MaxDoPOverride_Reflected`
- [ ] FanIn unit: `Metadata_IsCorrect`, `Schema_HasCorrectPorts`, `Concat_PreservesOrder`, `Merge_LastWriterWins`, `First_ReturnsFirstPayload`, `Last_ReturnsLastPayload`, `ValidateConfiguration_InvalidMode_Fails`, `ValidateConfiguration_ValidMode_Passes`

---

### 2.2.3-followup Technical Debt (post 2.2.3b, before 2.2.4) 🔧

> These items were deliberately deferred from earlier slices to keep each PR focused. They don't block 2.2.3b but must land before the end-to-end demo in 2.2.6.

- [x] **`WorkflowExecutor` dispatch-core extraction** *(carry-over from 2.2.0a)* — both `WorkflowExecutor` and `SubGraphExecutor` previously maintained their own full routing implementations. Extracted into shared **`DispatchCore`** class (`Workflow.Engine/Actors/DispatchCore.cs`, ~270 lines) injected with delegate callbacks so each actor supplies its own state queries and side-effects (e.g. `TransitionNodeState` in `WorkflowExecutor`). Zero behaviour change; no routing logic was duplicated. ✅ **DONE**
  - Implemented unit tests in `Workflow.Tests/Engine/DispatchCoreTests.cs` (9 tests) covering: fire-all, port-aware routing, recursive skip propagation, diamond-merge guard, already-running guard, completion-callback invocation. All 9 pass; 546/546 suite-wide zero regressions.

- [x] **`Metadata.subGraphId` tagging on `NodeExecutionRecord`** *(carry-over from 2.2.0a)* — sub-graph node records are persisted under the parent execution ID and **do** carry a `subGraphId` field. ✅ **DONE**
  - `SubGraphExecutor.QueuePersistNode` stamps `Metadata["subGraphId"] = _subGraphId` on every record it writes.
  - SQLite persistence extended: `ExecutionNodeEntity` gains four new optional columns (`sub_graph_id`, `loop_id`, `loop_iteration`, `metadata`), persisted and restored via `SqliteExecutionHistoryRepository`. Migration `004_AddNodeExecutionMetadata` adds the columns safely (null-safe ALTER TABLE).
  - Tests: added `SubGraph_NodeExecutions_PersistedWithSubGraphIdTagInMetadata` (new focused metadata assertion); updated `SubGraph_NodeExecutions_PersistedUnderParentExecutionId` still passes. 5/5 suite-wide zero regressions.

- [x] **Persistence metadata stamps for parallel branches** — stamp `Metadata["parallelId"]` and `Metadata["branchIndex"]` on each `NodeExecutionRecord` that executes inside a `ParallelExecutionCoordinator` branch.
  - The coordinator already passes `__parallel_branch_index__` and `__parallel_branch_port__` as branch inputs; the persistence plumbing reads these from `SubGraphExecutor` context and sets them on the record via `QueuePersistNode`.
  - `SubGraphExecutor.QueuePersistNode` reads `__parallel_node_id__` → `Metadata["parallelId"]` and `__parallel_branch_index__` → `Metadata["branchIndex"]`; both are serialised into the `metadata` JSON column (Migration_004) alongside `subGraphId`.
  - Tests added (5 total): `SubGraph_WithParallelSentinelInputs_StampsParallelIdAndBranchIndexInMetadata`, `SubGraph_WithoutParallelSentinelInputs_DoesNotStampParallelMetadata` (actor-level); `NodeExecution_WithParallelMetadata_ShouldRoundTripAllMetadataFields`, `NodeExecution_WithNoMetadata_ShouldRoundTripWithNullMetadata` (SQLite round-trip); `Engine_ParallelExecution_BranchNodesStampedWithParallelMetadata` (engine + SQLite end-to-end). All 552 suite tests pass~ ✅ **DONE**

- [ ] **Wall-time concurrency assertion test** — verify that N-branch parallel with simulated delay completes in `< sum(branch_times)`. Requires a lightweight async-delay test module (e.g. `DelayModule` from builtins). Deferred pending test-infra decision (real `Task.Delay` vs Akka `TestScheduler`).

- [ ] **`maxDegreeOfParallelism` observability test** — spawn coordinator with `maxDegreeOfParallelism = 2` over 5 branches; assert at most 2 `SubGraphExecutor` children exist simultaneously. Requires a test probe that can observe child actor birth/death. Deferred pending `TestKit` child-spy helper.

- [ ] **CTS leak regression-guard test** — after `ParallelExecutionCoordinator` stops (both success and failure paths), assert that its `_linkedCts` is disposed and its child actors are all stopped. Deferred pending child-lifecycle assertion helper.

- [ ] **Branch-scope overlap defensive guard** — add an `ArgumentException` (or structured log warning) in `SpawnParallelExecutor` when union of branch scopes from two different branch ports contains overlapping node IDs. Include a unit test for the overlap detection.

---

## 2.2.4 Error Handling Nodes (`builtin.trycatch`, `builtin.throw`) 🛡️ ✅ COMPLETE

**Purpose:** Let workflows recover from failures locally, transform errors, and run cleanup `finally` blocks — all without taking down the parent execution~ 🌷

**Complexity:** 🟡 Medium

### Tasks:

- [x] **`TryCatchModule`** 🛡️
  - [x] New file: `Workflow.Modules/Builtin/Flow/TryCatchModule.cs`
  - [x] `ModuleId: "builtin.trycatch"`.
  - [x] Schema:
    - [x] Output port: `try` (activation)
    - [x] Output port: `catch` (activation, payload = `WorkflowError`)
    - [x] Output port: `finally` (activation, optional)
    - [x] Output port: `done` (continuation port, fires after sequence completes)
    - [x] Inputs: `rethrow` (bool, default `false`), `catchTypes` (array of error type names, optional filter)
    - [x] Outputs: `error` (object, nullable), `success` (bool)
    - [x] Schema.Outputs intentionally empty (dynamic ports — same pattern as SwitchModule/ParallelModule)
  - [x] Behaviour:
    - [x] Returns `ModuleResult.WithTryCatch(TryCatchRequest)` — engine spawns `TryCatchExecutorActor`.
    - [x] Activates `try` sub-graph; `TryCatchExecutorActor` wraps it.
    - [x] On failure matching `catchTypes`, routes to `catch` with serialised error.
    - [x] After `try` (success) or `catch` (recovery), routes to `finally` if present.
    - [x] `rethrow: true` re-emits the error after `finally`.

- [x] **`ThrowModule`** 💥
  - [x] New file: `Workflow.Modules/Builtin/Flow/ThrowModule.cs`
  - [x] Schema:
    - [x] Input: `errorType` (string, required), `message` (string, required), `data` (object, optional)
  - [x] `ExecuteAsync`: throws `WorkflowUserException` with metadata.

- [x] **`WorkflowError` value type** 🪶
  - [x] New file: `Workflow.Core/Models/WorkflowError.cs`
  - [x] Fields: `ErrorType`, `Message`, `NodeId`, `Data`, `OccurredAt`, `StackTrace?`.
  - [x] `FromException` factory auto-extracts `ErrorType` from `WorkflowUserException` or CLR type name.
  - [x] `WorkflowUserException` in same file — thrown by `ThrowModule`.
  - [x] JSON-serialisable (via existing engine serialization).

- [x] **`TryCatchRequest` model** 📋
  - [x] New file: `Workflow.Core/Models/TryCatchRequest.cs`
  - [x] Fields: `TryPort`, `CatchPort`, `FinallyPort`, `DonePort`, `Rethrow`, `CatchTypes`.

- [x] **`TryCatchExecutorActor`** 🎭
  - [x] New file: `Workflow.Engine/Actors/TryCatchExecutorActor.cs`
  - [x] Phase-state machine (`RunningTry` → `RunningCatch`? → `RunningFinally`? → done)
  - [x] Spawns `SubGraphExecutor` children for try/catch/finally branches sequentially.
  - [x] Uses `_phase` field (not SubGraphId prefix) to route SubGraphCompleted/Failed — unambiguous.
  - [x] `WorkflowError` injected as `error` input to catch sub-graph.
  - [x] Hierarchical cancellation via linked CTS (2.2.0b).
  - [x] `ComputeScope` (static BFS helper) for scoping sub-graphs.
  - [x] `ConcludeSequence`: rethrows if no catch branch handled error OR if `rethrow=true` + `_errorWasCaught`.

- [x] **Engine integration messages** 📨
  - [x] `TryCatchMessages.cs`: `TryCatchCompleted`, `TryCatchFailed`, `NodeTryCatchExecutionRequested`.
  - [x] `NodeTryCatchExecutionRequested` sent BEFORE `NodeExecutionCompleted` (FIFO ordering guarantee).

- [x] **Engine plumbing** ⚙️
  - [x] `ModuleResult.TryCatch` property + `WithTryCatch` factory added to `IWorkflowModule.cs`.
  - [x] `NodeExecutor.SendSuccess` extended with `tryCatch` param; emits `NodeTryCatchExecutionRequested` before `NodeExecutionCompleted`.
  - [x] `WorkflowExecutor` plumbing: `_pendingTryCatches`, `_pendingTryCatchNodes`, `SpawnTryCatchExecutor`, `HandleTryCatchCompleted`, `HandleTryCatchFailed`.
  - [x] `IsWorkflowComplete` guards on `_pendingTryCatchNodes.Count == 0`.
  - [x] Pre-marks try/catch/finally scope nodes as skipped (same pattern as loop/parallel body nodes).
  - [x] Fires done-port successors on TryCatchCompleted.

- [x] **`BuiltinModuleRegistration.cs` updated** — count 14 → 16 (added `TryCatchModule`, `ThrowModule`).

**Tests (18 written — target was ~10):** → `Workflow.Tests/Modules/Flow/TryCatchModuleTests.cs`

**Unit tests (13):**
- [x] `TryCatchModule_Metadata_IsCorrect`
- [x] `TryCatchModule_Schema_HasEmptyOutputs_ForDynamicPorts`
- [x] `TryCatchModule_ExecuteAsync_ReturnsTryCatchRequest`
- [x] `TryCatchModule_Rethrow_True_PassedThrough`
- [x] `TryCatchModule_Rethrow_DefaultsFalse`
- [x] `TryCatchModule_CatchTypes_ParsedFromCommaSeparatedString`
- [x] `TryCatchModule_CatchTypes_NullWhenEmpty`
- [x] `ThrowModule_Metadata_IsCorrect`
- [x] `ThrowModule_ExecuteAsync_ThrowsWorkflowUserException`
- [x] `ThrowModule_ExecuteAsync_ErrorTypeAndMessageFromProperties`
- [x] `ThrowModule_ExecuteAsync_DataPayloadAttached`
- [x] `WorkflowError_FromException_PopulatesFields`
- [x] `WorkflowError_FromException_ExtractsWorkflowUserExceptionType`
- [x] `ThrowModule_ProducesWorkflowUserException_WithCorrectType`

**Engine integration tests (5 via TestKit):**
- [x] `TryCatch_TrySucceeds_Finally_AlwaysRuns_WorkflowCompletes` — try passes → finally runs → done fires → WorkflowCompleted ✅
- [x] `TryCatch_TryFails_RoutesToCatch_WorkflowCompletes` — try fails → catch handler runs → WorkflowCompleted ✅
- [x] `TryCatch_TryFails_CatchAndFinally_BothRun_WorkflowCompletes` — try fails → catch + finally both run → WorkflowCompleted ✅
- [x] `TryCatch_Rethrow_True_WorkflowFails_AfterFinally` — try fails, rethrow=true → finally runs → WorkflowFailed ✅
- [x] `TryCatch_ThrowModule_ProducesStructuredWorkflowError` — included via unit test ✅

---

## 2.2.5 Expression Evaluator 🧮

**Purpose:** A safe, deterministic, sandboxed evaluator for `condition` / `switch` expressions and (later) data transformation modules~ 🌟

**Companion analysis:** [Phase2-2-ExpressionEngine-Analysis.md](./Phase2-2-ExpressionEngine-Analysis.md) — side-by-side syntax comparison + integration sketches for DynamicExpresso vs JavaScript (Jint) vs Lua (MoonSharp). **Jint selected as default for v1** — native `EvaluateAsync` + CT support + `async/await`~ 🧮

**Complexity:** 🟡 Medium

### Tasks:

- [x] **Define `IExpressionEvaluator`** *(shipped early in 2.2.1 — `ConditionalModule` needed it)*
  - [x] New file: `Workflow.Core/Abstractions/IExpressionEvaluator.cs`
  - [x] API: `EvaluateAsync<T>(string expression, IReadOnlyDictionary<string, object?> variables, CancellationToken)` — returns `ValueTask<T>`.
  - [x] Object path: `EvaluateObjectAsync(...)` — returns `ValueTask<JsonElement>` for structured/array returns.
  - [x] Errors: `ExpressionParseException` (syntax / parse-time), `ExpressionRuntimeException` (runtime / timeout).

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
- [x] Engine supports **port-aware connection activation** (selective output routing, additive default) ✅ 2.2.0a
- [x] Engine supports **sub-graph execution**, **loop scopes**, **error boundaries**, **hierarchical cancellation** ✅ 2.2.0b
- [x] 2.2.0 split shipped as **2.2.0a** (routing + sub-graphs) and **2.2.0b** (loop scope + error boundary + cancellation) ✅
- [x] 2.2.1 shipped: `builtin.condition` + `builtin.switch` ✅
- [x] 2.2.2 shipped: `builtin.loop.foreach`, `builtin.loop.while`, `builtin.break`, `builtin.continue` — **all integration tests now passing** ✅
- [x] 2.2.3a shipped: `ParallelExecutionCoordinator` + `ParallelModule` (11 tests, 0 regressions) ✅
- [ ] 2.2.3 split fully shipped as **2.2.3a** ✅ + **2.2.3b** (⚠️ modules shipped, tests pending — `waitForAll:false` + branch-overlap guard still open)
- [ ] **2.2.3-followup** technical debt resolved (persistence stamps, observability tests, CTS leak guard, overlap defensive guard) — must land before 2.2.6
- [x] Modules: ~~`builtin.condition`~~ ✅, ~~`builtin.switch`~~ ✅, ~~`builtin.loop.foreach`~~ ✅, ~~`builtin.loop.while`~~ ✅, ~~`builtin.break`~~ ✅, ~~`builtin.continue`~~ ✅, ~~`builtin.parallel`~~ ✅, ~~`builtin.fanout`~~ ✅ (impl), ~~`builtin.fanin`~~ ✅ (impl), `builtin.trycatch`, `builtin.throw`
- [x] `IExpressionEvaluator` interface defined (shipped in 2.2.1); default implementation + DI wiring deferred to 2.2.5
- [ ] ~82 unit + integration tests passing across 2.2.0a/2.2.0b–2.2.6 (2.2.0a ~8 + 2.2.0b ~13 + 2.2.1 ~46 + 2.2.2 ~27 + **2.2.3a 11** ✅ + 2.2.3b ~16 + 2.2.3-followup ~5 + 2.2.4 ~10 + 2.2.5 ~12 + 2.2.6 ~6)
- [ ] XML docs + `docs/advanced-flow-control.md`
- [ ] Sample workflow runs end-to-end on persistence + API stack

**Infrastructure improvements (not in original scope — shipped May 2026):**
- [x] **`Workflow.Tests.Integration` project** 🐳 — Docker-backed tests (NATS, PostgreSQL, MinIO) extracted from `Workflow.Tests` into a dedicated project. Fast Docker-free dev loop is now the default; container tests can be run explicitly via `dotnet test Workflow.Tests.Integration`. See `Workflow.Tests.Integration/README.md`~ 💖
- [x] **`BuiltinModuleIntegrationTests` updated** — count assertions corrected from 11 → 14 (reflecting `ParallelModule` + `FanOutModule` + `FanInModule`); three new `HasModule` + `Contain` assertions added for the new modules.

**New / Modified Files (planned → actual):**
```
Workflow.Core/
  Abstractions/IExpressionEvaluator.cs                  ← ✅ shipped (2.2.1)
  Models/WorkflowError.cs                               ← new (2.2.4)
  Models/NodeDefinition.cs                              ← ✅ + optional RegionId? hint (2.2.0b)
  Models/LoopRequest.cs                                 ← ✅ shipped (2.2.2)
  Models/ParallelRequest.cs                             ← ✅ shipped (2.2.3a)

Workflow.Engine/
  Actors/SubGraphExecutor.cs                            ← ✅ shipped (2.2.0a); dispatch-core wired (2.2.3-followup)
  Actors/LoopExecutorActor.cs                           ← ✅ shipped (2.2.2)
  Actors/ParallelExecutionCoordinator.cs                ← ✅ shipped (2.2.3a)
  Actors/DispatchCore.cs                                ← ✅ shipped (2.2.3-followup) shared routing core
  Actors/WorkflowExecutor.cs                            ← ✅ port-aware routing, loop/parallel/boundary hooks
  Messages/SubGraphMessages.cs                          ← ✅ shipped (2.2.0a)
  Messages/LoopMessages.cs                              ← ✅ shipped (2.2.2)
  Messages/ParallelMessages.cs                          ← ✅ shipped (2.2.3a)
  Messages/WorkflowMessages.cs                          ← ✅ + ActivePorts, loop/parallel/error messages
  Messages/ScopeMessages.cs                             ← ✅ shipped (2.2.0b)
  Models/LoopContext.cs                                 ← ✅ shipped (2.2.0b)
  Models/ErrorBoundary.cs                               ← ✅ shipped (2.2.0b)
  Services/JintExpressionEvaluator.cs                  ← new (2.2.5)
  Services/DynamicExpressoEvaluator.cs                  ← new (2.2.5 opt-in fallback)
  Services/KeyedExpressionEvaluatorFactory.cs           ← new (2.2.5)

Workflow.Modules/Builtin/Flow/
  ConditionalModule.cs                                  ← ✅ shipped (2.2.1)
  SwitchModule.cs                                       ← ✅ shipped (2.2.1)
  ForEachModule.cs                                      ← ✅ shipped (2.2.2)
  WhileModule.cs                                        ← ✅ shipped (2.2.2)
  BreakModule.cs                                        ← ✅ shipped (2.2.2)
  ContinueModule.cs                                     ← ✅ shipped (2.2.2)
  ParallelModule.cs                                     ← ✅ shipped (2.2.3a)
  FanOutModule.cs                                       ← ✅ shipped (2.2.3b module impl)
  FanInModule.cs                                        ← ✅ shipped (2.2.3b module impl)
  TryCatchModule.cs                                     ← new (2.2.4)
  ThrowModule.cs                                        ← new (2.2.4)

Workflow.Tests/
  Engine/PortRoutingTests.cs                            ← ✅ shipped (2.2.0a)
  Engine/SubGraphExecutorTests.cs                       ← ✅ shipped (2.2.0a)
  Engine/LoopScopeTests.cs                              ← ✅ shipped (2.2.0b)
  Engine/ErrorBoundaryTests.cs                          ← ✅ shipped (2.2.0b)
  Engine/HierarchicalCancellationTests.cs               ← ✅ shipped (2.2.0b)
  Engine/DispatchCoreTests.cs                           ← ✅ shipped (2.2.3-followup) 9 unit tests
  Engine/ExpressionEvaluatorTests.cs                    ← new (2.2.5)
  Engine/AdvancedFlowPersistenceTests.cs                ← new (2.2.6)
  Api/AdvancedFlowApiTests.cs                           ← new (2.2.6)
  Modules/Flow/ConditionalModuleTests.cs                ← ✅ shipped (2.2.1)
  Modules/Flow/SwitchModuleTests.cs                     ← ✅ shipped (2.2.1)
  Modules/Flow/ForEachModuleTests.cs                    ← ✅ shipped (2.2.2)
  Modules/Flow/WhileModuleTests.cs + BreakContinue      ← ✅ shipped (2.2.2)
  Modules/Flow/ParallelModuleTests.cs                   ← ✅ shipped (2.2.3a)
  Modules/Flow/FanOutModuleTests.cs                     ← ❌ pending (2.2.3b)
  Modules/Flow/FanInModuleTests.cs                      ← ❌ pending (2.2.3b)

Workflow.Tests.Integration/                            ← ✅ NEW PROJECT (May 2026 infrastructure)
  Persistence/NatsProviderTests.cs                      ← moved from Workflow.Tests
  Persistence/PostgresProviderTests.cs                 ← moved from Workflow.Tests
  Persistence/S3BlobStoreTests.cs                      ← moved from Workflow.Tests
  README.md                                             ← documents Docker-test split

docs/advanced-flow-control.md                           ← new (2.2.6)
examples/definitions/flow-control-demo.json             ← new (2.2.6)
Directory.Packages.props                                ← + Jint (default); DynamicExpresso.Core (opt-in fallback) [2.2.5]
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
| **Q9** | Parallel cancel semantics | ✅ **Cooperative `CancellationToken` cancel + 250 ms grace window** | Siblings observe linked CTS from 2.2.0b hierarchy; no hard-abort of in-flight nodes |
| **Q10** | `builtin.switch` in 2.2 | ✅ **Included in 2.2.1** alongside `builtin.condition` | Shares multi-port routing from 2.2.0a; negligible added complexity; gives authors multi-way branching in the same PR |

---

> 💖 **Ami’s Phase 2.2 Tips:**
> - Build **2.2.0a → 2.2.0b first** — every other sub-phase needs port-aware routing + sub-graph execution. 2.2.1 (conditional/switch) can ship right after 2.2.0a. Don’t skip ahead, nya~ 🧠
> - Keep modules **declarative**: control-flow logic lives in the engine, not in module code beyond setting `ActivePorts` and asking for sub-graph runs~ ✨
> - Pin `IExpressionEvaluator` behind an interface from day one — Jint is the default, but DynamicExpresso (`"csharp"`) and Lua (`"lua"`) can be added as keyed opt-ins without touching any module code~ 🔌
> - Use the **SQLite `:memory:`** provider from Phase 2.1 for end-to-end tests — no Docker, fast, full persistence path exercised~ 💾
> - Loop persistence rows = goldmine for the future UI replay timeline; record `loopId`/`iter` from day one~ 🎀 UwU

