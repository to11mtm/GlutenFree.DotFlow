# Phase 1.5: Basic Built-in Modules (Week 5-6)

This sub-phase implements the 4 essential built-in workflow modules and the supporting `ModuleResult.VariableUpdates` mechanism. These modules act as the reference implementation for all future module authors and validate the full execution pipeline end-to-end~ 💖✨

---

## Pre-Existing Work (from Phase 1.3/1.4) ✅

| Component | File | Status |
|-----------|------|--------|
| `IWorkflowModule` | `Workflow.Modules/Abstractions/IWorkflowModule.cs` | ✅ Version, ValidateConfiguration, Dependencies |
| `ModuleExecutionContext` | `Workflow.Modules/Abstractions/IWorkflowModule.cs` | ✅ Inputs, Properties, Variables (read-only), Logger, Services |
| `ModuleResult` | `Workflow.Modules/Abstractions/IWorkflowModule.cs` | ✅ Ok/Fail factories + ExecutionMetrics |
| `ModuleSchema` / `PortDefinition` | `Workflow.Core/Models/ModuleSchema.cs` | ✅ Arr-based, fully typed |
| `ModulePropertyDefinition` | `Workflow.Core/Models/ModuleSchema.cs` | ✅ EditorType, ValidationRules |
| `PropertyBinder` | `Workflow.Modules/Binding/PropertyBinder.cs` | ✅ `{{Variable.Name}}` resolution, type conversion |
| `ModuleValidator` | `Workflow.Modules/Validation/ModuleValidator.cs` | ✅ ID convention, schema checks |
| `InMemoryModuleRegistry` | `Workflow.Modules/InMemoryModuleRegistry.cs` | ✅ Validates on register |
| `ModuleDiscovery` | `Workflow.Modules/Discovery/ModuleDiscovery.cs` | ✅ Auto-discovers public non-abstract types |
| `PassThroughModule` | `Workflow.Modules/Builtin/PassThroughModule.cs` | ✅ Reference pattern for new modules |
| `NodeExecutor` | `Workflow.Engine/Actors/NodeExecutor.cs` | ✅ PropertyBinder integrated, metrics |
| `WorkflowExecutor` | `Workflow.Engine/Actors/WorkflowExecutor.cs` | ✅ Passes variables into context |

> **CopilotNote:** `ModuleExecutionContext.Variables` is `IReadOnlyDictionary<string, object?>`.
> Modules cannot mutate variables directly — they return `ModuleResult.VariableUpdates` which
> `NodeExecutor` forwards to `WorkflowExecutor` to apply~ 💖

---

## 1.5.0 `ModuleResult.VariableUpdates` Mechanism ⚙️

**Purpose:** Enable modules to declare variable mutations as part of their result, keeping the immutable data-flow design intact. `SetVariableModule` depends on this entirely; other modules may use it for side-effect-free variable management too.

**Complexity:** 🟢 Low

**Tasks:**

- [x] **Extend `ModuleResult`** 📊 ✅
  - [x] Add `VariableUpdates` property
  - [x] Add `ModuleResult.Ok(outputs, variableUpdates)` factory overload
  - [x] Add `ModuleResult.Ok(outputs, metrics, variableUpdates)` factory overload
  - [x] Keep existing overloads unchanged (non-breaking)
  - [x] Add XML documentation explaining the mechanism

- [x] **Wire into `NodeExecutor`** ⚡ ✅
  - [x] After a successful `ExecuteAsync`, check `result.VariableUpdates`
  - [x] If non-null and non-empty, include in `NodeExecutionCompleted` message
  - [x] Add `VariableUpdates` field to `NodeExecutionCompleted` message record

- [x] **Wire into `WorkflowExecutor`** 🔄 ✅
  - [x] On receiving `NodeExecutionCompleted`, check for `VariableUpdates`
  - [x] Merge updates into the current `WorkflowExecutionContext.Variables`
  - [x] Merged variables flow into the next node's `ModuleExecutionContext`
  - [x] Log variable mutations at Debug level for traceability

**Tests (6/6 passing):** → `Workflow.Tests/Modules/VariableUpdatesTests.cs` ✅
- [x] Test `ModuleResult.Ok` with `VariableUpdates` sets the property
- [x] Test `ModuleResult.Ok` without `VariableUpdates` is null (backwards compat)
- [x] Test `ModuleResult.Ok` with metrics + variable updates
- [x] Test `ModuleResult.Fail` has null VariableUpdates
- [x] Test null variable value is valid (deletion)
- [x] Test metrics-only overload has null VariableUpdates

---

## 1.5.1 `LogModule` (`builtin.log`) 📝

**Purpose:** Write a structured log message at a configurable level during workflow execution. The reference implementation for property-driven modules (no data-flow inputs — all configuration comes from properties)~

**Complexity:** 🟢 Low

**Tasks:**

- [x] **Create `LogModule` class** 📝 ✅
  - [x] New file: `Workflow.Modules/Builtin/LogModule.cs`
  - [x] Module metadata:
    - [x] `ModuleId` → `"builtin.log"`
    - [x] `DisplayName` → `"Log Message"`
    - [x] `Category` → `"Utilities"`
    - [x] `Icon` → `"📝"`
    - [x] `Version` → `new Version(1, 0, 0)`
    - [x] `Description` → descriptive, non-empty
  - [x] Schema — **Properties** (configured per node, resolved by PropertyBinder):
    - [x] `message` (string, required)
    - [x] `level` (string, optional, default `"Information"`)
    - [x] `includeContext` (bool, optional, default `false`)
  - [x] Schema — **Outputs**:
    - [x] `timestamp` (DateTimeOffset)
    - [x] `message` (string)
  - [x] Schema — **Inputs**: none
  - [x] `ExecuteAsync` implementation
  - [x] Add XML documentation

- [x] **`ValidateConfiguration` override** ✅
  - [x] Validate `level` property is a known `LogLevel` name if provided
  - [x] Return `ValidationResult.Failure` with descriptive message for unknown levels

**Tests (13/13 passing):** → `Workflow.Tests/Modules/LogModuleTests.cs` ✅
- [x] Test module passes `ModuleValidator`
- [x] Test `ModuleDiscovery` finds `LogModule` in `Workflow.Modules` assembly
- [x] Test execute with message → output contains `timestamp` and `message`
- [x] Test execute at each supported level (Trace, Debug, Information, Warning, Error, Critical)
- [x] Test execute with `includeContext = true` appends ExecutionId/NodeId info
- [x] Test execute with `includeContext = false` does not append context
- [x] Test execute with unknown level falls back to Information
- [x] Test execute with empty message still succeeds
- [x] Test `ValidateConfiguration` rejects unknown level name
- [x] Test `ValidateConfiguration` accepts valid level names

---

## 1.5.2 `DelayModule` (`builtin.delay`) ⏱️

**Purpose:** Pause workflow execution for a configurable duration. Useful for rate-limiting, waiting for external systems, or testing timing-sensitive workflows~

**Complexity:** 🟡 Low-Medium

**Tasks:**

- [x] **Create `DelayModule` class** ⏱️ ✅
  - [x] New file: `Workflow.Modules/Builtin/DelayModule.cs`
  - [x] Module metadata
  - [x] Schema — **Properties**: `durationMs`, `maxDurationMs`
  - [x] Schema — **Outputs**: `actualDurationMs`, `wasCancelled`
  - [x] Schema — **Inputs**: none
  - [x] `ExecuteAsync` implementation with Stopwatch + cancellation handling
  - [x] Add XML documentation

- [x] **`ValidateConfiguration` override** ✅
  - [x] Validate `durationMs` can be parsed as a non-negative long
  - [x] Validate `durationMs <= maxDurationMs` if both provided

**Tests (11/11 passing):** → `Workflow.Tests/Modules/DelayModuleTests.cs` ✅
- [x] Test module passes `ModuleValidator`
- [x] Test `ModuleDiscovery` finds `DelayModule` in `Workflow.Modules` assembly
- [x] Test execute with `durationMs = 0` completes immediately, `actualDurationMs >= 0`
- [x] Test execute with `durationMs = 50` completes in ~50ms (allow ±30ms tolerance)
- [x] Test execute with cancellation mid-delay → `wasCancelled = true`, no exception thrown
- [x] Test execute returns `wasCancelled = false` when not cancelled
- [x] Test `ValidateConfiguration` rejects negative `durationMs`
- [x] Test `ValidateConfiguration` rejects `durationMs > maxDurationMs`
- [x] Test `ValidateConfiguration` accepts `durationMs = 0`
- [x] Test execute with `durationMs` exceeding `maxDurationMs` → `ModuleResult.Fail`
- [x] Test metadata is correct

---

## 1.5.3 `SetVariableModule` (`builtin.setvariable`) 💾

**Purpose:** Write a named value into the workflow's variable store so downstream nodes can read it. Depends on `ModuleResult.VariableUpdates` (1.5.0)~

**Complexity:** 🟡 Low-Medium

**Tasks:**

- [x] **Create `SetVariableModule` class** 💾 ✅
  - [x] New file: `Workflow.Modules/Builtin/SetVariableModule.cs`
  - [x] Module metadata
  - [x] Schema — **Properties**: `name`, `value`
  - [x] Schema — **Inputs**: `value` (optional override)
  - [x] Schema — **Outputs**: `previousValue`, `wasCreated`
  - [x] `ExecuteAsync` implementation with `VariableUpdates`
  - [x] Add XML documentation

- [x] **`ValidateConfiguration` override** ✅
  - [x] Validate `name` matches `^[a-zA-Z_][a-zA-Z0-9_.]*$`
  - [x] Return descriptive error if name contains invalid characters

**Tests (10/10 passing):** → `Workflow.Tests/Modules/SetVariableModuleTests.cs` ✅
- [x] Test module passes `ModuleValidator`
- [x] Test `ModuleDiscovery` finds `SetVariableModule` in `Workflow.Modules` assembly
- [x] Test execute creates new variable → `wasCreated = true`, `previousValue = null`
- [x] Test execute updates existing variable → `wasCreated = false`, `previousValue` = old value
- [x] Test `VariableUpdates` in result contains the correct name/value pair
- [x] Test `value` from connected input overrides `value` property
- [x] Test setting `null` value is valid (marks deletion)
- [x] Test `ValidateConfiguration` rejects empty name
- [x] Test `ValidateConfiguration` rejects name with spaces/special chars
- [x] Test `ValidateConfiguration` accepts valid dotted name (`user.count`)

---

## 1.5.4 `GetVariableModule` (`builtin.getvariable`) 🔍

**Purpose:** Read a named value from the workflow's variable store and expose it as an output for downstream nodes. The companion to `SetVariableModule`~

**Complexity:** 🟢 Low

**Tasks:**

- [x] **Create `GetVariableModule` class** 🔍 ✅
  - [x] New file: `Workflow.Modules/Builtin/GetVariableModule.cs`
  - [x] Module metadata
  - [x] Schema — **Properties**: `name`, `defaultValue`, `throwIfMissing`
  - [x] Schema — **Inputs**: none
  - [x] Schema — **Outputs**: `value`, `exists`, `typeName`
  - [x] `ExecuteAsync` implementation
  - [x] Add XML documentation

- [x] **`ValidateConfiguration` override** ✅
  - [x] Validate `name` is not empty

**Tests (9/9 passing):** → `Workflow.Tests/Modules/GetVariableModuleTests.cs` ✅
- [x] Test module passes `ModuleValidator`
- [x] Test `ModuleDiscovery` finds `GetVariableModule` in `Workflow.Modules` assembly
- [x] Test execute with existing variable → `value = expected`, `exists = true`
- [x] Test execute with missing variable, no default → `value = null`, `exists = false`
- [x] Test execute with missing variable + default → `value = default`, `exists = false`
- [x] Test execute with missing variable + `throwIfMissing = true` → `ModuleResult.Fail`
- [x] Test `typeName` output matches actual type name
- [x] Test `typeName` is `"null"` when value is null
- [x] Test `ValidateConfiguration` rejects empty `name`

---

## 1.5.5 Integration & End-to-End Tests 🎯

**Purpose:** Validate the complete pipeline: actors → PropertyBinder → modules → VariableUpdates → next node. Proves the Phase 1 demo workflow runs correctly~

**Complexity:** 🟡 Medium

**Tasks:**

- [x] **Register all builtin modules** 📦 ✅
  - [x] New file: `Workflow.Modules/Builtin/BuiltinModuleRegistration.cs`
  - [x] `static class BuiltinModules` with `RegisterAll` and `GetAll`
  - [x] All 5 modules auto-discoverable via `ModuleDiscovery`

- [x] **Integration tests (unit-level, no Akka)** 🔬 ✅
  - [x] New file: `Workflow.Tests/Modules/BuiltinModuleIntegrationTests.cs`
  - [x] Test: SetVariable → GetVariable chain (variable persists)
  - [x] Test: Log → SetVariable (timestamp captured)
  - [x] Test: GetVariable → Log (variable value in output)
  - [x] Test: all 5 modules register via `BuiltinModules.RegisterAll`
  - [x] Test: `ModuleDiscovery` auto-discovers all builtin modules

- [x] **End-to-end workflow test (Akka actor stack)** 🚀 ✅
  - [x] New file: `Workflow.Tests/Engine/BuiltinModuleEndToEndTests.cs`
  - [x] Build and register all modules in `InMemoryModuleRegistry`
  - [x] Single-node E2E: Log, SetVariable, Delay all complete
  - [x] Multi-node E2E: SetVariable → GetVariable with VariableUpdates flow
  - [x] Execute via `WorkflowExecutor` (Akka TestKit)
  - [x] Fixed `NodeExecutor.ConvertHashMapToDictionary` for safe HashMap→Dict conversion

**Tests (10/10 passing):** → `BuiltinModuleIntegrationTests.cs` (6) + `BuiltinModuleEndToEndTests.cs` (4) ✅

---

## Phase 1.5 Deliverables

**Completion Criteria:**
- [x] `ModuleResult.VariableUpdates` added, wired through `NodeExecutor` → `WorkflowExecutor` ✅
- [x] `LogModule` implemented and tested ✅
- [x] `DelayModule` implemented and tested ✅
- [x] `SetVariableModule` implemented and tested ✅
- [x] `GetVariableModule` implemented and tested ✅
- [x] `BuiltinModules.RegisterAll` convenience method ✅
- [x] 59 unit tests written and passing (exceeded ~40 target) ✅
- [x] End-to-end demo workflow executes successfully via Akka actor stack ✅
- [x] All 5 modules pass `ModuleValidator`, auto-discoverable via `ModuleDiscovery` ✅
- [x] XML documentation on all new APIs ✅

**Estimated New Files:**
```
Workflow.Modules/
  Builtin/
    LogModule.cs
    DelayModule.cs
    SetVariableModule.cs
    GetVariableModule.cs
    BuiltinModuleRegistration.cs

Workflow.Tests/
  Modules/
    LogModuleTests.cs
    DelayModuleTests.cs
    SetVariableModuleTests.cs
    GetVariableModuleTests.cs
    BuiltinModuleIntegrationTests.cs
  Engine/
    BuiltinModuleEndToEndTests.cs
```

**Modified Files:**
```
Workflow.Modules/Abstractions/IWorkflowModule.cs   ← add VariableUpdates to ModuleResult
Workflow.Engine/Messages/WorkflowMessages.cs        ← add VariableUpdates to NodeExecutionCompleted
Workflow.Engine/Actors/NodeExecutor.cs              ← forward VariableUpdates from result
Workflow.Engine/Actors/WorkflowExecutor.cs          ← apply VariableUpdates to context
```

---

## Schema Reference (for Module Authors) 🎨

All modules follow the same construction pattern based on `PassThroughModule`:

```csharp
public ModuleSchema Schema => new(
    Inputs: Arr.create(
        new PortDefinition(
            Name: "portName",
            DisplayName: "Display Name",
            DataType: typeof(string),
            Description: "What this port does.",
            IsRequired: true,
            DefaultValue: null)),
    Outputs: Arr.create(
        new PortDefinition(
            Name: "result",
            DisplayName: "Result",
            DataType: typeof(string),
            Description: "The output value.",
            IsRequired: false)),
    Properties: Arr.create(
        new ModulePropertyDefinition(
            Name: "myProp",
            DisplayName: "My Property",
            DataType: typeof(string),
            Description: "A configurable property.",
            IsRequired: false,
            DefaultValue: "default",
            EditorType: PropertyEditorType.TextBox)));
```

> **Key distinction:**
> - `Inputs` = runtime data flowing IN from connected nodes (connection ports)
> - `Outputs` = runtime data flowing OUT to connected nodes (connection ports)
> - `Properties` = design-time configuration values (set per-node in the designer, can reference variables via `{{...}}`)

> **CopilotNote:** `context.Properties` gets values from `NodeDefinition.Properties` after `PropertyBinder`
> has resolved any `{{Variable.Name}}` references. `context.Inputs` gets values from the
> `NodeExecutionCompleted` outputs of predecessor nodes~ 💖

---

> 💝 **Ami's Tips:** The VariableUpdates mechanism (1.5.0) is the most critical piece to get right
> before implementing SetVariable and GetVariable — make sure the round-trip through actors
> works before building the modules that depend on it! LogModule and DelayModule can be built
> in parallel since they have no dependencies on each other or on variables~ UwU ✨

