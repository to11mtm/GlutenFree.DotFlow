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

- [ ] **Extend `ModuleResult`** 📊
  - [ ] Add `VariableUpdates` property:
    ```csharp
    public IReadOnlyDictionary<string, object?>? VariableUpdates { get; init; }
    ```
  - [ ] Add `ModuleResult.Ok(outputs, variableUpdates)` factory overload
  - [ ] Add `ModuleResult.Ok(outputs, metrics, variableUpdates)` factory overload
  - [ ] Keep existing overloads unchanged (non-breaking)
  - [ ] Add XML documentation explaining the mechanism

- [ ] **Wire into `NodeExecutor`** ⚡
  - [ ] After a successful `ExecuteAsync`, check `result.VariableUpdates`
  - [ ] If non-null and non-empty, include in `NodeExecutionCompleted` message
  - [ ] Add `VariableUpdates` field to `NodeExecutionCompleted` message record

- [ ] **Wire into `WorkflowExecutor`** 🔄
  - [ ] On receiving `NodeExecutionCompleted`, check for `VariableUpdates`
  - [ ] Merge updates into the current `WorkflowExecutionContext.Variables`
  - [ ] Merged variables flow into the next node's `ModuleExecutionContext`
  - [ ] Log variable mutations at Debug level for traceability

**Tests (~5):** → `Workflow.Tests/Modules/ModuleContractTests.cs` (extend) + `Workflow.Tests/Engine/`
- [ ] Test `ModuleResult.Ok` with `VariableUpdates` sets the property
- [ ] Test `ModuleResult.Ok` without `VariableUpdates` is null (backwards compat)
- [ ] Test `NodeExecutor` forwards `VariableUpdates` in completed message
- [ ] Test `WorkflowExecutor` applies `VariableUpdates` to execution context
- [ ] Test updated variables are visible to the next node in sequence

---

## 1.5.1 `LogModule` (`builtin.log`) 📝

**Purpose:** Write a structured log message at a configurable level during workflow execution. The reference implementation for property-driven modules (no data-flow inputs — all configuration comes from properties)~

**Complexity:** 🟢 Low

**Tasks:**

- [ ] **Create `LogModule` class** 📝
  - [ ] New file: `Workflow.Modules/Builtin/LogModule.cs`
  - [ ] Module metadata:
    - [ ] `ModuleId` → `"builtin.log"`
    - [ ] `DisplayName` → `"Log Message"`
    - [ ] `Category` → `"Utilities"`
    - [ ] `Icon` → `"📝"`
    - [ ] `Version` → `new Version(1, 0, 0)`
    - [ ] `Description` → descriptive, non-empty
  - [ ] Schema — **Properties** (configured per node, resolved by PropertyBinder):
    - [ ] `message` (string, required) — the message text; supports `{{Variable.Name}}` references
    - [ ] `level` (string, optional, default `"Information"`) — log level name (`"Trace"`, `"Debug"`, `"Information"`, `"Warning"`, `"Error"`, `"Critical"`)
    - [ ] `includeContext` (bool, optional, default `false`) — append `ExecutionId` and `NodeId` to message
  - [ ] Schema — **Outputs**:
    - [ ] `timestamp` (DateTimeOffset) — when the message was logged
    - [ ] `message` (string) — the resolved/final message that was logged
  - [ ] Schema — **Inputs**: none (log is purely property-driven)
  - [ ] `ExecuteAsync` implementation:
    - [ ] Read `message` from `context.Properties` (PropertyBinder resolves variables already)
    - [ ] Read `level` from `context.Properties`, parse to `LogLevel` enum
    - [ ] Append context info if `includeContext = true`
    - [ ] Call `context.Logger.Log(level, message)` with structured logging
    - [ ] Return `ModuleResult.Ok` with `timestamp = DateTimeOffset.UtcNow` and resolved `message`
    - [ ] Handle unknown log level gracefully (default to `Information`)
  - [ ] Add XML documentation

- [ ] **`ValidateConfiguration` override** ✅
  - [ ] Validate `level` property is a known `LogLevel` name if provided
  - [ ] Return `ValidationResult.Failure` with descriptive message for unknown levels

**Tests (~10):** → `Workflow.Tests/Modules/LogModuleTests.cs`
- [ ] Test module passes `ModuleValidator`
- [ ] Test `ModuleDiscovery` finds `LogModule` in `Workflow.Modules` assembly
- [ ] Test execute with message → output contains `timestamp` and `message`
- [ ] Test execute at each supported level (Trace, Debug, Information, Warning, Error, Critical)
- [ ] Test execute with `includeContext = true` appends ExecutionId/NodeId info
- [ ] Test execute with `includeContext = false` does not append context
- [ ] Test execute with unknown level falls back to Information
- [ ] Test execute with empty message still succeeds
- [ ] Test `ValidateConfiguration` rejects unknown level name
- [ ] Test `ValidateConfiguration` accepts valid level names

---

## 1.5.2 `DelayModule` (`builtin.delay`) ⏱️

**Purpose:** Pause workflow execution for a configurable duration. Useful for rate-limiting, waiting for external systems, or testing timing-sensitive workflows~

**Complexity:** 🟡 Low-Medium

**Tasks:**

- [ ] **Create `DelayModule` class** ⏱️
  - [ ] New file: `Workflow.Modules/Builtin/DelayModule.cs`
  - [ ] Module metadata:
    - [ ] `ModuleId` → `"builtin.delay"`
    - [ ] `DisplayName` → `"Delay"`
    - [ ] `Category` → `"Flow Control"`
    - [ ] `Icon` → `"⏱️"`
    - [ ] `Version` → `new Version(1, 0, 0)`
  - [ ] Schema — **Properties**:
    - [ ] `durationMs` (long, required) — delay in milliseconds; supports `{{Variable.Name}}` references
    - [ ] `maxDurationMs` (long, optional, default `300000`) — safety cap (5 minutes); rejects absurd values
  - [ ] Schema — **Outputs**:
    - [ ] `actualDurationMs` (long) — real elapsed milliseconds (via `Stopwatch`)
    - [ ] `wasCancelled` (bool) — `true` if `CancellationToken` fired before delay completed
  - [ ] Schema — **Inputs**: none
  - [ ] `ExecuteAsync` implementation:
    - [ ] Read `durationMs` from `context.Properties`, convert to `int`
    - [ ] Validate `durationMs >= 0` (negative = immediate pass-through, not an error)
    - [ ] Validate `durationMs <= maxDurationMs` (fail-fast on absurd delay)
    - [ ] Start `Stopwatch`
    - [ ] `await Task.Delay((int)durationMs, cancellationToken)` wrapped in try/catch `OperationCanceledException`
    - [ ] Stop `Stopwatch`
    - [ ] Return `actualDurationMs` and `wasCancelled`
  - [ ] Add XML documentation

- [ ] **`ValidateConfiguration` override** ✅
  - [ ] Validate `durationMs` can be parsed as a non-negative long
  - [ ] Validate `durationMs <= maxDurationMs` if both provided

**Tests (~10):** → `Workflow.Tests/Modules/DelayModuleTests.cs`
- [ ] Test module passes `ModuleValidator`
- [ ] Test `ModuleDiscovery` finds `DelayModule` in `Workflow.Modules` assembly
- [ ] Test execute with `durationMs = 0` completes immediately, `actualDurationMs >= 0`
- [ ] Test execute with `durationMs = 50` completes in ~50ms (allow ±30ms tolerance)
- [ ] Test execute with cancellation mid-delay → `wasCancelled = true`, no exception thrown
- [ ] Test execute returns `wasCancelled = false` when not cancelled
- [ ] Test `ValidateConfiguration` rejects negative `durationMs`
- [ ] Test `ValidateConfiguration` rejects `durationMs > maxDurationMs`
- [ ] Test `ValidateConfiguration` accepts `durationMs = 0`
- [ ] Test execute with `durationMs` exceeding `maxDurationMs` → `ModuleResult.Fail`

---

## 1.5.3 `SetVariableModule` (`builtin.setvariable`) 💾

**Purpose:** Write a named value into the workflow's variable store so downstream nodes can read it. Depends on `ModuleResult.VariableUpdates` (1.5.0)~

**Complexity:** 🟡 Low-Medium

**Tasks:**

- [ ] **Create `SetVariableModule` class** 💾
  - [ ] New file: `Workflow.Modules/Builtin/SetVariableModule.cs`
  - [ ] Module metadata:
    - [ ] `ModuleId` → `"builtin.setvariable"`
    - [ ] `DisplayName` → `"Set Variable"`
    - [ ] `Category` → `"Variables"`
    - [ ] `Icon` → `"💾"`
    - [ ] `Version` → `new Version(1, 0, 0)`
  - [ ] Schema — **Properties**:
    - [ ] `name` (string, required) — variable name to create/update; must match `^[a-zA-Z_][a-zA-Z0-9_.]*$`
    - [ ] `value` (string, optional) — serialised value; supports `{{Variable.Name}}` references
  - [ ] Schema — **Inputs**:
    - [ ] `value` (object, optional) — when connected, overrides the `value` property (runtime data wins over config)
  - [ ] Schema — **Outputs**:
    - [ ] `previousValue` (object) — previous value of the variable, or `null` if new
    - [ ] `wasCreated` (bool) — `true` if the variable was new, `false` if it was updated
  - [ ] `ExecuteAsync` implementation:
    - [ ] Read `name` from `context.Properties`, validate format
    - [ ] Determine `newValue`: prefer `context.Inputs["value"]` if present, else `context.Properties["value"]`
    - [ ] Look up `context.Variables[name]` to capture `previousValue`
    - [ ] Return `ModuleResult.Ok` with:
      - [ ] `Outputs`: `previousValue`, `wasCreated`
      - [ ] `VariableUpdates`: `{ [name] = newValue }` ← this is how the write happens!
    - [ ] Handle null `newValue` correctly (deleting a variable is valid)
  - [ ] Add XML documentation

- [ ] **`ValidateConfiguration` override** ✅
  - [ ] Validate `name` matches `^[a-zA-Z_][a-zA-Z0-9_.]*$`
  - [ ] Return descriptive error if name contains invalid characters

**Tests (~10):** → `Workflow.Tests/Modules/SetVariableModuleTests.cs`
- [ ] Test module passes `ModuleValidator`
- [ ] Test `ModuleDiscovery` finds `SetVariableModule` in `Workflow.Modules` assembly
- [ ] Test execute creates new variable → `wasCreated = true`, `previousValue = null`
- [ ] Test execute updates existing variable → `wasCreated = false`, `previousValue` = old value
- [ ] Test `VariableUpdates` in result contains the correct name/value pair
- [ ] Test `value` from connected input overrides `value` property
- [ ] Test setting `null` value is valid (marks deletion)
- [ ] Test `ValidateConfiguration` rejects empty name
- [ ] Test `ValidateConfiguration` rejects name with spaces/special chars
- [ ] Test `ValidateConfiguration` accepts valid dotted name (`user.count`)

---

## 1.5.4 `GetVariableModule` (`builtin.getvariable`) 🔍

**Purpose:** Read a named value from the workflow's variable store and expose it as an output for downstream nodes. The companion to `SetVariableModule`~

**Complexity:** 🟢 Low

**Tasks:**

- [ ] **Create `GetVariableModule` class** 🔍
  - [ ] New file: `Workflow.Modules/Builtin/GetVariableModule.cs`
  - [ ] Module metadata:
    - [ ] `ModuleId` → `"builtin.getvariable"`
    - [ ] `DisplayName` → `"Get Variable"`
    - [ ] `Category` → `"Variables"`
    - [ ] `Icon` → `"🔍"`
    - [ ] `Version` → `new Version(1, 0, 0)`
  - [ ] Schema — **Properties**:
    - [ ] `name` (string, required) — variable name to read
    - [ ] `defaultValue` (string, optional, default null) — returned if variable not found; supports `{{Variable.Name}}`
    - [ ] `throwIfMissing` (bool, optional, default `false`) — fail execution if variable not found and no default
  - [ ] Schema — **Inputs**: none (variable name is always configured, not connected)
  - [ ] Schema — **Outputs**:
    - [ ] `value` (object) — the resolved variable value (or `defaultValue`)
    - [ ] `exists` (bool) — whether the variable was found in the store
    - [ ] `typeName` (string) — `value.GetType().Name` or `"null"` if not found and no default
  - [ ] `ExecuteAsync` implementation:
    - [ ] Read `name` from `context.Properties`
    - [ ] Try `context.Variables.TryGetValue(name, out var val)`
    - [ ] If not found AND `throwIfMissing = true` → `ModuleResult.Fail("Variable '{name}' not found")`
    - [ ] If not found AND `defaultValue` provided → use `defaultValue` as `value`, `exists = false`
    - [ ] If not found AND no default → `value = null`, `exists = false`
    - [ ] Return outputs: `value`, `exists`, `typeName`
  - [ ] Add XML documentation

- [ ] **`ValidateConfiguration` override** ✅
  - [ ] Validate `name` is not empty
  - [ ] Validate `throwIfMissing = true` and `defaultValue` provided → warning (default takes priority, throw is ignored)

**Tests (~9):** → `Workflow.Tests/Modules/GetVariableModuleTests.cs`
- [ ] Test module passes `ModuleValidator`
- [ ] Test `ModuleDiscovery` finds `GetVariableModule` in `Workflow.Modules` assembly
- [ ] Test execute with existing variable → `value = expected`, `exists = true`
- [ ] Test execute with missing variable, no default → `value = null`, `exists = false`
- [ ] Test execute with missing variable + default → `value = default`, `exists = false`
- [ ] Test execute with missing variable + `throwIfMissing = true` → `ModuleResult.Fail`
- [ ] Test `typeName` output matches actual type name
- [ ] Test `typeName` is `"null"` when value is null
- [ ] Test `ValidateConfiguration` rejects empty `name`

---

## 1.5.5 Integration & End-to-End Tests 🎯

**Purpose:** Validate the complete pipeline: actors → PropertyBinder → modules → VariableUpdates → next node. Proves the Phase 1 demo workflow runs correctly~

**Complexity:** 🟡 Medium

**Tasks:**

- [ ] **Register all builtin modules** 📦
  - [ ] New file: `Workflow.Modules/Builtin/BuiltinModuleRegistration.cs`
  - [ ] `static class BuiltinModules` with:
    - [ ] `RegisterAll(IModuleRegistry registry)` — registers all 4 builtin modules + PassThrough
    - [ ] `GetAll()` → `IReadOnlyList<IWorkflowModule>` — factory list without registry dependency
  - [ ] All 4 modules auto-discoverable via `ModuleDiscovery` (validate this in tests)

- [ ] **Integration tests (unit-level, no Akka)** 🔬
  - [ ] New file: `Workflow.Tests/Modules/BuiltinModuleIntegrationTests.cs`
  - [ ] Test: SetVariable → GetVariable chain (variable persists across call)
  - [ ] Test: Log → SetVariable (log returns timestamp, SetVariable captures it)
  - [ ] Test: GetVariable → Log (variable value appears in output)
  - [ ] Test: all 4 modules register successfully via `BuiltinModules.RegisterAll`
  - [ ] Test: `ModuleDiscovery` auto-discovers all 4 builtin modules from assembly

- [ ] **End-to-end workflow test (Akka actor stack)** 🚀
  - [ ] New file: `Workflow.Tests/Engine/BuiltinModuleEndToEndTests.cs`
  - [ ] Build and register all 4 modules in a live `InMemoryModuleRegistry`
  - [ ] Construct the Phase 1 demo workflow definition:
    ```
    Log("Starting workflow") → SetVariable("count", "1") → Delay(100) → GetVariable("count") → Log("count={{Variable.count}}")
    ```
  - [ ] Execute via `WorkflowSupervisor` + `WorkflowExecutor` (Akka TestKit)
  - [ ] Assert all nodes complete successfully
  - [ ] Assert `count` variable = `"1"` after SetVariable node
  - [ ] Assert GetVariable output `value = "1"` and `exists = true`
  - [ ] Assert Delay `actualDurationMs >= 100`
  - [ ] Assert final Log message contains resolved variable value
  - [ ] Test cancellation mid-Delay propagates cleanly
  - [ ] Test error in one node (e.g., GetVariable `throwIfMissing=true` for unknown var) → workflow fails gracefully

**Tests (~12):** → split between `BuiltinModuleIntegrationTests.cs` and `BuiltinModuleEndToEndTests.cs`

---

## Phase 1.5 Deliverables

**Completion Criteria:**
- [ ] `ModuleResult.VariableUpdates` added, wired through `NodeExecutor` → `WorkflowExecutor`
- [ ] `LogModule` implemented and tested
- [ ] `DelayModule` implemented and tested
- [ ] `SetVariableModule` implemented and tested
- [ ] `GetVariableModule` implemented and tested
- [ ] `BuiltinModules.RegisterAll` convenience method
- [ ] ~40 unit tests written and passing
- [ ] End-to-end demo workflow executes successfully via Akka actor stack
- [ ] All 4 modules pass `ModuleValidator`, auto-discoverable via `ModuleDiscovery`
- [ ] XML documentation on all new APIs

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

