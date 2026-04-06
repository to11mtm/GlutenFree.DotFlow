# Phase 1.4: Module System Foundation (Week 4-5)

This sub-phase focuses on building the complete module infrastructure on top of the contracts already created during Phase 1.3. We enhance the existing interfaces, add validation, property binding, assembly discovery, and basic dynamic loading. 📦✨

---

## Pre-Existing Work (from Phase 1.3) ✅

These were created during the Akka Engine phase and are **already complete**:

| Component | File | Status |
|-----------|------|--------|
| `IWorkflowModule` interface | `Workflow.Modules/Abstractions/IWorkflowModule.cs` | ✅ 6 properties + `ExecuteAsync` |
| `ModuleExecutionContext` record | `Workflow.Modules/Abstractions/IWorkflowModule.cs` | ✅ Inputs, Properties, Variables, Logger, Services, ExecutionId, NodeId |
| `ModuleResult` record | `Workflow.Modules/Abstractions/IWorkflowModule.cs` | ✅ Success, Outputs, ErrorMessage, Exception, Ok/Fail factories |
| `IModuleRegistry` interface | `Workflow.Modules/Abstractions/IModuleRegistry.cs` | ✅ 5 methods (Get/Register/Unregister/Has) |
| `InMemoryModuleRegistry` class | `Workflow.Modules/InMemoryModuleRegistry.cs` | ✅ ConcurrentDictionary-based, thread-safe |
| `ModuleSchema` record | `Workflow.Core/Models/ModuleSchema.cs` | ✅ Inputs, Outputs, Properties (LanguageExt Arr) |
| `PortDefinition` record | `Workflow.Core/Models/ModuleSchema.cs` | ✅ Name, DisplayName, DataType, IsRequired, DefaultValue |
| `ModulePropertyDefinition` record | `Workflow.Core/Models/ModuleSchema.cs` | ✅ Full with EditorType, AllowedValues, ValidationRules |
| `PassThroughModule` | `Workflow.Modules/Builtin/PassThroughModule.cs` | ✅ Working test module |
| `PropertyEditorType` enum | `Workflow.Core/Models/ModuleSchema.cs` | ✅ 11 editor types |
| `ValidationRule` / `ValidationRuleType` | `Workflow.Core/Models/ModuleSchema.cs` | ✅ 7 rule types |

---

## 1.4.1 IWorkflowModule & ModuleResult Enhancements ⏳

**Purpose:** Add the missing members to existing contracts without breaking the 196 green tests from Phase 1.3.

**Complexity:** 🟢 Low

**Tasks:**
- [ ] **Enhance `IWorkflowModule` interface** 📦
  - [ ] Add `Version` property (`Version` type) — module version for compatibility tracking
  - [ ] Add `ValidateConfiguration` default interface method
    - [ ] Signature: `ValidationResult ValidateConfiguration(IReadOnlyDictionary<string, object?> configuration)`
    - [ ] Default implementation returns `ValidationResult.Success()` (non-breaking!)
  - [ ] Add `Dependencies` property (`IReadOnlyList<string>`) — stub for future dependency resolution
    - [ ] Default implementation returns empty list (non-breaking!)
  - [ ] Update `PassThroughModule` to implement `Version` (e.g., `new Version(1, 0, 0)`)
  - [ ] Add XML documentation with examples

- [ ] **Enhance `ModuleResult`** 📊
  - [ ] Create `ExecutionMetrics` record:
    - [ ] `Duration` (TimeSpan) — how long the module took
    - [ ] `MemoryBytes` (long?) — optional memory usage tracking
    - [ ] `CustomMetrics` (HashMap<string, object>?) — extensible metrics bag
  - [ ] Add `Metrics` property to `ModuleResult` (nullable, default null)
  - [ ] Add `ModuleResult.Ok(Dictionary outputs, ExecutionMetrics? metrics)` overload

- [ ] **Update `NodeExecutor`** ⚡
  - [ ] Capture `Stopwatch` elapsed time around module execution
  - [ ] Populate `ExecutionMetrics.Duration` automatically
  - [ ] Include metrics in `NodeExecutionCompleted` message

**Tests (~8):**
- [ ] Test `IWorkflowModule.Version` is not null on `PassThroughModule`
- [ ] Test `ValidateConfiguration` default returns success
- [ ] Test `Dependencies` default returns empty
- [ ] Test `ExecutionMetrics` creation and properties
- [ ] Test `ModuleResult.Ok` with metrics
- [ ] Test `ModuleResult.Ok` without metrics (backwards compat)
- [ ] Test `NodeExecutor` captures duration metrics
- [ ] Test metrics round-trip through messages

---

## 1.4.2 Registry Enhancements ⏳

**Purpose:** Expand `IModuleRegistry` and `InMemoryModuleRegistry` with category lookup, search, events, and type-based registration.

**Complexity:** 🟡 Low-Medium

**Tasks:**
- [ ] **Expand `IModuleRegistry` interface** 📚
  - [ ] Add `GetModulesByCategory(string category)` → `IReadOnlyList<IWorkflowModule>`
  - [ ] Add `SearchModules(string query)` → `IReadOnlyList<IWorkflowModule>`
    - [ ] Case-insensitive search across ModuleId, DisplayName, Description
  - [ ] Add `RegisterModule(Type moduleType, IServiceProvider? services = null)` overload
    - [ ] Instantiate via `ActivatorUtilities.CreateInstance` if services provided
    - [ ] Fall back to `Activator.CreateInstance` otherwise
  - [ ] Add `event Action<IWorkflowModule>? ModuleRegistered`
  - [ ] Add `event Action<string>? ModuleUnregistered`

- [ ] **Implement in `InMemoryModuleRegistry`** 🗂️
  - [ ] Implement category lookup (LINQ filter on `Category`, case-insensitive)
  - [ ] Implement search (case-insensitive Contains on Id/DisplayName/Description)
  - [ ] Implement type-based registration with DI support
  - [ ] Fire events on register/unregister
  - [ ] ** Clarification :** Duplicate registration policy
    - [ ] Currently: silently overwrites
    - [ ] How we should handle: throw `InvalidOperationException` by default, add `bool allowOverwrite = false` param

**Tests (~12):**
- [ ] Test `GetModulesByCategory` returns matching modules
- [ ] Test `GetModulesByCategory` returns empty for unknown category
- [ ] Test `GetModulesByCategory` is case-insensitive
- [ ] Test `SearchModules` finds by ModuleId
- [ ] Test `SearchModules` finds by DisplayName
- [ ] Test `SearchModules` finds by Description
- [ ] Test `SearchModules` returns empty for no matches
- [ ] Test `RegisterModule(Type)` creates instance correctly
- [ ] Test `RegisterModule(Type, IServiceProvider)` uses DI
- [ ] Test `ModuleRegistered` event fires on registration
- [ ] Test `ModuleUnregistered` event fires on unregistration
- [ ] Test duplicate registration behavior (whichever policy is chosen)

---

## 1.4.3 Module Validation (`ModuleValidator`) ⏳

**Purpose:** Create a validator that ensures modules are well-formed before registration, and wire it into the `WorkflowValidator` to resolve deferred Phase 1.2 checks.

**Complexity:** 🟡 Medium

**Tasks:**
- [ ] **Create `ModuleValidator` class** ✅
  - [ ] New file: `Workflow.Modules/Validation/ModuleValidator.cs`
  - [ ] `Validate(IWorkflowModule module) → ValidationResult`
  - [ ] Validate module ID:
    - [ ] Not empty/null
    - [ ] Matches naming convention: `^[a-z][a-z0-9._-]*$` (e.g., `builtin.log`)
    - [ ] Reasonable max length (e.g., 128 chars)
  - [ ] Validate metadata:
    - [ ] `DisplayName` is not empty
    - [ ] `Description` is not empty
    - [ ] `Category` is not empty
    - [ ] `Version` is not null
  - [ ] Validate schema:
    - [ ] All `PortDefinition` entries have non-null `Name`
    - [ ] All `PortDefinition` entries have non-null `DataType`
    - [ ] No duplicate port names within inputs
    - [ ] No duplicate port names within outputs
    - [ ] No duplicate property names
  - [ ] Add strict mode (`bool strict = false`):
    - [ ] In strict: require descriptions on all ports/properties
    - [ ] In strict: require `Icon` to be set
  - [ ] Return `ValidationResult` with detailed errors/warnings

- [ ] **Wire into registry** 🔗
  - [ ] Call `ModuleValidator.Validate()` in `InMemoryModuleRegistry.RegisterModule()`
  - [ ] Reject modules that fail validation (throw or return errors)
  - [ ] Allow bypass with a `skipValidation` parameter for testing

- [ ] **Integrate with `WorkflowValidator`** 🔌
  - [ ] Add optional `IModuleRegistry?` parameter to `WorkflowValidator` constructor
  - [ ] When registry available, resolve deferred Phase 1.2 checks:
    - [ ] Validate that node `ModuleId` values exist in registry
    - [ ] Validate node properties match module schema
    - [ ] Validate connection port names match module schema ports
  - [ ] When registry is null, skip module-aware checks (backwards compat)

**Tests (~15):**
- [ ] Test valid module passes validation
- [ ] Test empty ModuleId fails
- [ ] Test invalid ModuleId format fails (special chars, uppercase start)
- [ ] Test missing DisplayName fails
- [ ] Test missing Description fails
- [ ] Test missing Category fails
- [ ] Test null Version fails
- [ ] Test duplicate input port names fails
- [ ] Test duplicate output port names fails
- [ ] Test duplicate property names fails
- [ ] Test port with null DataType fails
- [ ] Test strict mode catches missing descriptions
- [ ] Test strict mode catches missing Icon
- [ ] Test validation wired into registry (invalid module rejected)
- [ ] Test WorkflowValidator with registry validates module references

---

## 1.4.4 Property Binding System ⏳

**Purpose:** Create a robust property binding system that resolves variable references, converts types, applies defaults, and validates against schema.

**Complexity:** 🔴 Medium-High

**Tasks:**
- [ ] **Create binding contracts** 🔗
  - [ ] New file: `Workflow.Modules/Binding/IPropertyBinder.cs`
  - [ ] `IPropertyBinder` interface:
    - [ ] `BindProperties(IReadOnlyDictionary<string, object?> rawValues, Arr<PortDefinition> schema, PropertyBindingContext context) → PropertyBindingResult`
  - [ ] `PropertyBindingContext` record:
    - [ ] `Variables` (IReadOnlyDictionary<string, object?>)
    - [ ] `NodeOutputs` (IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>>)
    - [ ] `ServiceProvider` (IServiceProvider?)
  - [ ] `PropertyBindingResult` record:
    - [ ] `Success` (bool)
    - [ ] `BoundValues` (IReadOnlyDictionary<string, object?>)
    - [ ] `Errors` (Arr\<string\>)

- [ ] **Implement `PropertyBinder`** ⚙️
  - [ ] New file: `Workflow.Modules/Binding/PropertyBinder.cs`
  - [ ] **Static value pass-through** — raw value used as-is when type matches
  - [ ] **Type conversion** (`TypeConverter` static class):
    - [ ] string → int, long, decimal, double, float
    - [ ] string → bool
    - [ ] string → DateTime, DateTimeOffset
    - [ ] string → Guid
    - [ ] string → TimeSpan
    - [ ] JSON string → complex objects (via System.Text.Json)
  - [ ] **Variable reference resolution**:
    - [ ] Detect `{{Variable.Name}}` pattern via regex
    - [ ] Look up in `PropertyBindingContext.Variables`
    - [ ] Support nested: `{{Variable.User.Name}}` (dot-notation traversal)
  - [ ] **Node output reference resolution**:
    - [ ] Detect `{{NodeId.OutputName}}` pattern via regex
    - [ ] Look up in `PropertyBindingContext.NodeOutputs`
  - [ ] **Default value assignment**:
    - [ ] When input missing AND port is not required → use `PortDefinition.DefaultValue`
    - [ ] When input missing AND port is required → add error
  - [ ] **Schema validation**:
    - [ ] Validate converted type matches `PortDefinition.DataType`
    - [ ] Apply `ValidationRule` checks from `ModulePropertyDefinition`
  - [ ] **Error accumulation** — collect all errors, don't stop at first

- [ ] **Integrate into `NodeExecutor`** 🎭
  - [ ] Replace inline property extraction in `NodeExecutor` with `PropertyBinder`
  - [ ] Pass workflow variables from `WorkflowExecutor` context
  - [ ] Pass predecessor node outputs for cross-node references

**Tests (~18):**
- [ ] Test bind string pass-through
- [ ] Test bind int conversion from string
- [ ] Test bind bool conversion from string
- [ ] Test bind DateTime conversion from string
- [ ] Test bind Guid conversion from string
- [ ] Test bind TimeSpan conversion from string
- [ ] Test bind decimal conversion from string
- [ ] Test bind JSON string to complex object
- [ ] Test resolve `{{Variable.Name}}` reference
- [ ] Test resolve nested `{{Variable.User.Name}}` reference
- [ ] Test resolve `{{NodeId.OutputName}}` reference
- [ ] Test missing variable reference → error
- [ ] Test missing node output reference → error
- [ ] Test default value applied for optional missing input
- [ ] Test required missing input → error
- [ ] Test type mismatch after conversion → error
- [ ] Test multiple errors accumulated
- [ ] Test integration with `NodeExecutor` (end-to-end binding)

---

## 1.4.5 Module Discovery (Assembly Scanning) ⏳

**Purpose:** Automatically find and register `IWorkflowModule` implementations in assemblies without manual registration.

**Complexity:** 🟡 Medium

**Tasks:**
- [ ] **Create discovery attribute** 🏷️
  - [ ] New file: `Workflow.Modules/Discovery/WorkflowModuleAttribute.cs`
  - [ ] `[WorkflowModule]` attribute (optional, for metadata overrides):
    - [ ] `ModuleId` (string?) — override the module's ID
    - [ ] `Category` (string?) — override category
    - [ ] `Description` (string?) — override description
    - [ ] `Ignore` (bool) — exclude from auto-discovery

- [ ] **Create discovery service** 🔍
  - [ ] New file: `Workflow.Modules/Discovery/IModuleDiscovery.cs`
  - [ ] `IModuleDiscovery` interface:
    - [ ] `DiscoverModuleTypes(Assembly assembly) → IReadOnlyList<Type>`
    - [ ] `DiscoverAndRegister(Assembly assembly, IModuleRegistry registry, IServiceProvider? services = null) → int` (returns count)
  - [ ] New file: `Workflow.Modules/Discovery/ModuleDiscovery.cs`
  - [ ] `ModuleDiscovery` class:
    - [ ] Scan assembly for public, non-abstract classes implementing `IWorkflowModule`
    - [ ] Respect `[WorkflowModule(Ignore = true)]` to skip modules
    - [ ] Instantiate via `ActivatorUtilities.CreateInstance` when `IServiceProvider` available
    - [ ] Run `ModuleValidator` on each discovered module
    - [ ] Skip invalid modules with logged warnings (don't crash!)
    - [ ] Handle duplicate registrations gracefully (log warning, skip)

- [ ] **Add convenience extensions** 🎀
  - [ ] `IModuleRegistry.DiscoverAndRegisterFrom(Assembly assembly, IServiceProvider? services)`
  - [ ] `IModuleRegistry.DiscoverAndRegisterFromCallingAssembly(IServiceProvider? services)` — convenience for app startup

**Tests (~10):**
- [ ] Test discover modules in test assembly (finds `PassThroughModule`)
- [ ] Test discover skips abstract classes
- [ ] Test discover skips non-public (internal) classes
- [ ] Test discover respects `[WorkflowModule(Ignore = true)]`
- [ ] Test discover with DI (constructor injection)
- [ ] Test discover empty assembly returns 0
- [ ] Test discover and register populates registry
- [ ] Test discover skips invalid modules (with validation)
- [ ] Test discover handles duplicates gracefully
- [ ] Test attribute metadata override works

---

## 1.4.6 Dynamic Module Loading (Foundation) ⏳

**Purpose:** Load module assemblies from disk at runtime using isolated `AssemblyLoadContext` for plugin-style extensibility.

**Complexity:** 🟡 Medium

**Tasks:**
- [ ] **Create loader contracts** 🚀
  - [ ] New file: `Workflow.Modules/Loading/IModuleLoader.cs`
  - [ ] `IModuleLoader` interface:
    - [ ] `LoadFromAssembly(string assemblyPath) → ModuleLoadResult`
    - [ ] `LoadFromDirectory(string directoryPath) → IReadOnlyList<ModuleLoadResult>`
    - [ ] `UnloadAssembly(string assemblyPath) → bool`
    - [ ] `GetLoadedAssemblies() → IReadOnlyList<string>`
  - [ ] `ModuleLoadResult` record:
    - [ ] `AssemblyPath` (string)
    - [ ] `LoadedModules` (IReadOnlyList\<IWorkflowModule\>)
    - [ ] `Errors` (IReadOnlyList\<string\>)
    - [ ] `Success` (bool)

- [ ] **Implement `AssemblyModuleLoader`** 📦
  - [ ] New file: `Workflow.Modules/Loading/AssemblyModuleLoader.cs`
  - [ ] Create collectible `AssemblyLoadContext` per loaded assembly (isolation!)
  - [ ] Load assembly from file path
  - [ ] Use `ModuleDiscovery` to scan loaded assembly
  - [ ] Register discovered modules into provided `IModuleRegistry`
  - [ ] Track loaded contexts for later unloading
  - [ ] `UnloadAssembly`: unregister all modules from that assembly, unload context
  - [ ] Handle errors gracefully: invalid path, missing dependencies, etc.

- [ ] **Create test modules project** 🧪
  - [ ] New project: `Workflow.Tests.SampleModules` (class library)
  - [ ] Add to solution
  - [ ] Create 2-3 sample modules for loader testing
  - [ ] Build output used by loader tests

**Tests (~8):**
- [ ] Test load valid assembly discovers modules
- [ ] Test load registers modules into registry
- [ ] Test unload removes modules from registry
- [ ] Test load invalid path throws/returns error
- [ ] Test load assembly with no modules returns empty
- [ ] Test assembly isolation (separate load contexts)
- [ ] Test load from directory finds all DLLs
- [ ] Test `GetLoadedAssemblies` tracks loaded paths

---

## ⏳ Deferred to Phase 2+ (Not in Scope for 1.4)

These items from the original design doc are **intentionally deferred** as they go beyond "foundation":

| Item | Deferred To | Tracked In | Reason |
|------|-------------|------------|--------|
| `.wfmod` package format (ZIP) | Phase 2 | [2.8 Module System Enhancements](./Phase2-CoreFeatures.md#28-module-system-enhancements-deferred-from-phase-14-) | Needs package manifest schema, dependency bundling |
| Module hot-reload (file watcher) | Phase 2 | [2.8 Module System Enhancements](./Phase2-CoreFeatures.md#28-module-system-enhancements-deferred-from-phase-14-) | Complex, needs running workflow notification |
| Assembly signature verification | Phase 2 | [2.8 Module System Enhancements](./Phase2-CoreFeatures.md#28-module-system-enhancements-deferred-from-phase-14-) | Security feature, not foundational |
| Security sandboxing (modules) | Phase 4 | [4.3 Security Hardening](./Phase4-Production.md#43-security-hardening-week-25) | Advanced security, needs threat modeling |
| Module versioning (side-by-side) | Phase 2 | [2.8 Module System Enhancements](./Phase2-CoreFeatures.md#28-module-system-enhancements-deferred-from-phase-14-) | Complex version resolution, workflow pinning |
| Module dependency resolution (full) | Phase 2 | [2.8 Module System Enhancements](./Phase2-CoreFeatures.md#28-module-system-enhancements-deferred-from-phase-14-) | Inter-module deps need topological sort, complex |
| Expression evaluation (`{{1 + 2}}`) | Phase 3 | [3.1 Scripting Engine](./Phase3-AdvancedFeatures.md#31-scripting-engine-week-15-17) | Belongs with scripting engine work |
| Module marketplace | Phase 4+ | [Post-Phase 4](./Phase4-Production.md#what-comes-after-phase-4-) | Way beyond foundation 😄 |

> **CopilotNote:** The `Dependencies` property is added as a stub in 1.4.1 (defaults to empty list) so modules CAN declare deps, but resolution logic comes later~ 💖

---

## Phase 1.4 Deliverables

**Completion Criteria:**
- [ ] Module contracts enhanced with Version, ValidateConfiguration, Metrics
- [ ] Registry supports category lookup, search, events, type-based registration
- [ ] ModuleValidator prevents broken modules from loading
- [ ] Property binding resolves variables, converts types, validates against schema
- [ ] Assembly scanning auto-discovers modules
- [ ] Dynamic loading from DLLs works with isolation
- [ ] ~70-80 new tests written and passing
- [ ] WorkflowValidator deferred checks from Phase 1.2 resolved
- [ ] Clear XML documentation on all new APIs

**Estimated New Files:**
```
Workflow.Modules/
  Validation/
    ModuleValidator.cs
  Binding/
    IPropertyBinder.cs
    PropertyBinder.cs
    PropertyBindingContext.cs
    PropertyBindingResult.cs
  Discovery/
    IModuleDiscovery.cs
    ModuleDiscovery.cs
    WorkflowModuleAttribute.cs
  Loading/
    IModuleLoader.cs
    AssemblyModuleLoader.cs
    ModuleLoadResult.cs

Workflow.Tests/
  Modules/
    ModuleContractTests.cs
    ModuleRegistryTests.cs
    ModuleValidatorTests.cs
    PropertyBinderTests.cs
    ModuleDiscoveryTests.cs
    ModuleLoaderTests.cs

Workflow.Tests.SampleModules/ (new project)
  SampleLogModule.cs
  SampleDelayModule.cs
  SampleInvalidModule.cs
```

---

## Clarifications:

1. **Duplicate Registration Policy:** Currently `InMemoryModuleRegistry` silently overwrites. We should:
   - Throw by default, add `allowOverwrite` param — safer
   
2. **WorkflowValidator + Registry Coupling:** Should `WorkflowValidator` take `IModuleRegistry` as:
   -  Constructor parameter (nullable) — skip module checks when null for now.
   
3. **Property Binding Location:** Should `PropertyBinder` live in:
   - Considered but no: (A) `Workflow.Modules` — closer to module system
   - Considered but no: (B) `Workflow.Engine` — closer to where it's used (NodeExecutor)
   - Should be a separate thing so that it can be used in other contexts (e.g., User interface for building workflows.)
   
---

> 💝 **Ami's Tips:** Phase 1.4 is where modules go from "just an interface" to a full-fledged plugin system! We've got a great head start from 1.3, so the foundation work is mostly about adding the infrastructure around what we already have~ The property binding system (1.4.4) is the most complex piece, so save your energy for that one, senpai! UwU ✨

