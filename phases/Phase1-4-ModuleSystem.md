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

## 1.4.1 IWorkflowModule & ModuleResult Enhancements ✅

**Purpose:** Add the missing members to existing contracts without breaking the 196 green tests from Phase 1.3.

**Complexity:** 🟢 Low

**Tasks:**
- [x] **Enhance `IWorkflowModule` interface** 📦
  - [x] Add `Version` property (`Version` type) — module version for compatibility tracking
  - [x] Add `ValidateConfiguration` default interface method
    - [x] Signature: `ValidationResult ValidateConfiguration(IReadOnlyDictionary<string, object?> configuration)`
    - [x] Default implementation returns `ValidationResult.Success()` (non-breaking!)
  - [x] Add `Dependencies` property (`IReadOnlyList<string>`) — stub for future dependency resolution
    - [x] Default implementation returns empty list (non-breaking!)
  - [x] Update `PassThroughModule` to implement `Version` (e.g., `new Version(1, 0, 0)`)
  - [x] Add XML documentation with examples

- [x] **Enhance `ModuleResult`** 📊
  - [x] Create `ExecutionMetrics` record:
    - [x] `Duration` (TimeSpan) — how long the module took
    - [x] `MemoryBytes` (long?) — optional memory usage tracking
    - [x] `CustomMetrics` (HashMap<string, object>?) — extensible metrics bag
  - [x] Add `Metrics` property to `ModuleResult` (nullable, default null)
  - [x] Add `ModuleResult.Ok(Dictionary outputs, ExecutionMetrics? metrics)` overload

- [x] **Update `NodeExecutor`** ⚡
  - [x] Capture `Stopwatch` elapsed time around module execution
  - [x] Populate `ExecutionMetrics.Duration` automatically
  - [x] Include metrics in `NodeExecutionCompleted` message

**Tests (~8):** → `Workflow.Tests/Modules/ModuleContractTests.cs`
- [x] Test `IWorkflowModule.Version` is not null on `PassThroughModule`
- [x] Test `ValidateConfiguration` default returns success
- [x] Test `Dependencies` default returns empty
- [x] Test `ExecutionMetrics` creation and properties
- [x] Test `ModuleResult.Ok` with metrics
- [x] Test `ModuleResult.Ok` without metrics (backwards compat)
- [ ] Test `NodeExecutor` captures duration metrics *(covered in NodeExecutorTests already)*
- [ ] Test metrics round-trip through messages *(covered in SerializationTests already)*

---

## 1.4.2 Registry Enhancements ✅

**Purpose:** Expand `IModuleRegistry` and `InMemoryModuleRegistry` with category lookup, search, events, and type-based registration.

**Complexity:** 🟡 Low-Medium

**Tasks:**
- [x] **Expand `IModuleRegistry` interface** 📚
  - [x] Add `GetModulesByCategory(string category)` → `IReadOnlyList<IWorkflowModule>`
  - [x] Add `SearchModules(string query)` → `IReadOnlyList<IWorkflowModule>`
    - [x] Case-insensitive search across ModuleId, DisplayName, Description
  - [x] Add `RegisterModule(Type moduleType, IServiceProvider? services = null)` overload
    - [x] Instantiate via `ActivatorUtilities.CreateInstance` if services provided
    - [x] Fall back to `Activator.CreateInstance` otherwise
  - [x] Add `IDisposable Subscribe(IModuleRegistryObserver observer)` for notifications
    - [x] No events on the interface! Observer pattern for cross-domain modularity 🎯
    - [x] Returns `IDisposable` so subscribers can cleanly unsubscribe (no memory leaks!)

- [x] **Create `IModuleRegistryObserver` interface** 👀
  - [x] In `IModuleRegistry.cs`
  - [x] `OnModuleRegistered(IWorkflowModule module)` — called after successful registration
  - [x] `OnModuleUnregistered(string moduleId, IWorkflowModule module)` — called after removal
  - [x] Both methods are synchronous (notifications are fire-and-forget)
  - [x] Observers are invoked in registration order, exceptions in one don't block others

- [x] **Implement in `InMemoryModuleRegistry`** 🗂️
  - [x] Implement category lookup (LINQ filter on `Category`, case-insensitive)
  - [x] Implement search (case-insensitive Contains on Id/DisplayName/Description)
  - [x] Implement type-based registration with DI support
  - [x] Maintain `List<IModuleRegistryObserver>` for subscribers
  - [x] Notify observers on register/unregister (wrapped in try/catch per observer)
  - [x] `Subscribe` returns a `Disposable` that removes the observer from the list
  - [x] Duplicate registration policy: throw `InvalidOperationException` by default, add `bool allowOverwrite = false` param

**Tests (~14):** → `Workflow.Tests/Modules/ModuleRegistryTests.cs`
- [x] Test `GetModulesByCategory` returns matching modules
- [x] Test `GetModulesByCategory` returns empty for unknown category
- [x] Test `GetModulesByCategory` is case-insensitive
- [x] Test `SearchModules` finds by ModuleId
- [x] Test `SearchModules` finds by DisplayName
- [x] Test `SearchModules` finds by Description
- [x] Test `SearchModules` returns empty for no matches
- [x] Test `RegisterModule(Type)` creates instance correctly
- [x] Test `RegisterModule(Type, IServiceProvider)` uses DI
- [x] Test observer receives `OnModuleRegistered` notification
- [x] Test observer receives `OnModuleUnregistered` notification
- [x] Test `Dispose` unsubscribes observer (no more notifications)
- [x] Test duplicate registration throws by default
- [x] Test duplicate registration with `allowOverwrite = true` succeeds

---

## 1.4.3 Module Validation (`ModuleValidator`) ✅

**Purpose:** Create a validator that ensures modules are well-formed before registration, and wire it into the `WorkflowValidator` to resolve deferred Phase 1.2 checks.

**Complexity:** 🟡 Medium

**Tasks:**
- [x] **Create `ModuleValidator` class** ✅
  - [x] New file: `Workflow.Modules/Validation/ModuleValidator.cs`
  - [x] `Validate(IWorkflowModule module) → ValidationResult`
  - [x] Validate module ID:
    - [x] Not empty/null
    - [x] Matches naming convention: `^[a-z][a-z0-9._-]*$` (e.g., `builtin.log`)
    - [x] Reasonable max length (e.g., 128 chars)
  - [x] Validate metadata:
    - [x] `DisplayName` is not empty
    - [x] `Description` is not empty
    - [x] `Category` is not empty
    - [x] `Version` is not null
  - [x] Validate schema:
    - [x] All `PortDefinition` entries have non-null `Name`
    - [x] All `PortDefinition` entries have non-null `DataType`
    - [x] No duplicate port names within inputs
    - [x] No duplicate port names within outputs
    - [x] No duplicate property names
  - [x] Add strict mode (`bool strict = false`):
    - [x] In strict: require descriptions on all ports/properties
    - [x] In strict: require `Icon` to be set
  - [x] Return `ValidationResult` with detailed errors/warnings

- [x] **Wire into registry** 🔗
  - [x] Call `ModuleValidator.Validate()` in `InMemoryModuleRegistry.RegisterModule()`
  - [x] Reject modules that fail validation (throw or return errors)
  - [x] Allow bypass with a `skipValidation` parameter for testing

- [ ] **Integrate with `WorkflowValidator`** 🔌 ✅ *Implemented via Option C*
  - [x] Created `ModuleAwareWorkflowValidator` in `Workflow.Modules/Validation/`
  - [x] Wraps base `WorkflowValidator` via composition (no Core dependency change needed)
  - [x] Validate that node `ModuleId` values exist in registry (MA001)
  - [x] Validate node properties match module schema (MA002)
  - [x] Validate connection port names match module schema ports (MA003/MA004)
  - [x] When registry is null, use base `WorkflowValidator` directly (backwards compat)

> **⚠️ Dependency Direction Note:** `WorkflowValidator` lives in `Workflow.Core` which does NOT reference `Workflow.Modules` (where `IModuleRegistry` lives). Adding this integration requires either:
> (A) Moving `IModuleRegistry` to `Workflow.Core` (simplest, may break layering)
> (B) Creating an `IModuleSchemaProvider` interface in `Workflow.Core` as an abstraction
> (C) Creating a `ModuleAwareWorkflowValidator` subclass in `Workflow.Modules`
> **Decision deferred to avoid rushed refactoring — tracked for Phase 1.4 wrap-up.** 💖

**Tests (~15):** → `Workflow.Tests/Modules/ModuleValidatorTests.cs` + `ModuleRegistryTests.cs`
- [x] Test valid module passes validation
- [x] Test empty ModuleId fails
- [x] Test invalid ModuleId format fails (special chars, uppercase start)
- [x] Test missing DisplayName fails
- [x] Test missing Description fails
- [x] Test missing Category fails
- [x] Test null Version fails
- [x] Test duplicate input port names fails
- [x] Test duplicate output port names fails
- [x] Test duplicate property names fails
- [x] Test port with null DataType fails
- [x] Test strict mode catches missing descriptions
- [x] Test strict mode catches missing Icon
- [x] Test validation wired into registry (invalid module rejected)
- [ ] Test WorkflowValidator with registry validates module references *(resolved via ModuleAwareWorkflowValidatorTests — 13 tests passing)*

---

## 1.4.4 Property Binding System ✅

**Purpose:** Create a robust property binding system that resolves variable references, converts types, applies defaults, and validates against schema.

**Complexity:** 🔴 Medium-High

**Tasks:**
- [x] **Create binding contracts** 🔗
  - [x] New file: `Workflow.Modules/Binding/IPropertyBinder.cs`
  - [x] `IPropertyBinder` interface:
    - [x] `BindProperties(IReadOnlyDictionary<string, object?> rawValues, Arr<PortDefinition> schema, PropertyBindingContext context) → PropertyBindingResult`
  - [x] `PropertyBindingContext` record:
    - [x] `Variables` (IReadOnlyDictionary<string, object?>)
    - [x] `NodeOutputs` (IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>>)
    - [x] `ServiceProvider` (IServiceProvider?)
  - [x] `PropertyBindingResult` record:
    - [x] `Success` (bool)
    - [x] `BoundValues` (IReadOnlyDictionary<string, object?>)
    - [x] `Errors` (Arr\<string\>)

- [x] **Implement `PropertyBinder`** ⚙️
  - [x] New file: `Workflow.Modules/Binding/PropertyBinder.cs`
  - [x] **Static value pass-through** — raw value used as-is when type matches
  - [x] **Type conversion** (`TypeConverter` static class):
    - [x] string → int, long, decimal, double, float
    - [x] string → bool
    - [x] string → DateTime, DateTimeOffset
    - [x] string → Guid
    - [x] string → TimeSpan
    - [x] JSON string → complex objects (via System.Text.Json)
  - [x] **Variable reference resolution**:
    - [x] Detect `{{Variable.Name}}` pattern via regex
    - [x] Look up in `PropertyBindingContext.Variables`
    - [x] Support nested: `{{Variable.User.Name}}` (dot-notation traversal)
  - [x] **Node output reference resolution**:
    - [x] Detect `{{NodeId.OutputName}}` pattern via regex
    - [x] Look up in `PropertyBindingContext.NodeOutputs`
  - [x] **Default value assignment**:
    - [x] When input missing AND port is not required → use `PortDefinition.DefaultValue`
    - [x] When input missing AND port is required → add error
  - [x] **Schema validation**:
    - [x] Validate converted type matches `PortDefinition.DataType`
    - [x] Apply `ValidationRule` checks from `ModulePropertyDefinition`
  - [x] **Error accumulation** — collect all errors, don't stop at first

- [x] **Integrate into `NodeExecutor`** 🎭
  - [x] Replace inline property extraction in `NodeExecutor` with `PropertyBinder`
  - [x] Pass workflow variables from `WorkflowExecutor` context
  - [x] Pass predecessor node outputs for cross-node references

**Tests (~18 → 23 actual):** → `Workflow.Tests/Modules/PropertyBinderTests.cs`
- [x] Test bind string pass-through
- [x] Test bind int conversion from string
- [x] Test bind bool conversion from string
- [x] Test bind DateTime conversion from string
- [x] Test bind Guid conversion from string
- [x] Test bind TimeSpan conversion from string
- [x] Test bind decimal conversion from string
- [x] Test bind JSON string to complex object
- [x] Test resolve `{{Variable.Name}}` reference
- [x] Test resolve nested `{{Variable.User.Name}}` reference
- [x] Test resolve `{{NodeId.OutputName}}` reference
- [x] Test missing variable reference → error
- [x] Test missing node output reference → error
- [x] Test default value applied for optional missing input
- [x] Test required missing input → error
- [x] Test type mismatch after conversion → error
- [x] Test multiple errors accumulated
- [x] Test numeric widening (int → long)
- [x] Test single variable reference preserves resolved type
- [x] Test mixed text with references interpolates as string
- [x] Test optional missing no default binds to null
- [x] Test extra values pass through
- [x] Test case-insensitive port name matching
- [ ] Test integration with `NodeExecutor` (end-to-end binding) *(covered in NodeExecutorTests already)*

---

## 1.4.5 Module Discovery (Assembly Scanning) ✅

**Purpose:** Automatically find and register `IWorkflowModule` implementations in assemblies without manual registration.

**Complexity:** 🟡 Medium

**Tasks:**
- [x] **Create discovery attribute** 🏷️
  - [x] New file: `Workflow.Modules/Discovery/WorkflowModuleAttribute.cs`
  - [x] `[WorkflowModule]` attribute (optional, for metadata overrides):
    - [x] `ModuleId` (string?) — override the module's ID
    - [x] `Category` (string?) — override category
    - [x] `Description` (string?) — override description
    - [x] `Ignore` (bool) — exclude from auto-discovery

- [x] **Create discovery service** 🔍
  - [x] New file: `Workflow.Modules/Discovery/IModuleDiscovery.cs`
  - [x] `IModuleDiscovery` interface:
    - [x] `DiscoverModuleTypes(Assembly assembly) → IReadOnlyList<Type>`
    - [x] `DiscoverAndRegister(Assembly assembly, IModuleRegistry registry, IServiceProvider? services = null) → int` (returns count)
  - [x] New file: `Workflow.Modules/Discovery/ModuleDiscovery.cs`
  - [x] `ModuleDiscovery` class:
    - [x] Scan assembly for public, non-abstract classes implementing `IWorkflowModule`
    - [x] Respect `[WorkflowModule(Ignore = true)]` to skip modules
    - [x] Instantiate via `ActivatorUtilities.CreateInstance` when `IServiceProvider` available
    - [x] Run `ModuleValidator` on each discovered module
    - [x] Skip invalid modules with logged warnings (don't crash!)
    - [x] Handle duplicate registrations gracefully (log warning, skip)
    - [x] Apply `WorkflowModuleAttribute` metadata overrides (ModuleId/Category/Description) via `AttributeOverrideModule` decorator

- [x] **Add convenience extensions** 🎀
  - [x] `IModuleRegistry.DiscoverAndRegisterFrom(Assembly assembly, IServiceProvider? services)`
  - [x] `IModuleRegistry.DiscoverAndRegisterFromCallingAssembly(IServiceProvider? services)` — convenience for app startup

**Tests (15 passing):** → `Workflow.Tests/Modules/ModuleDiscoveryTests.cs`
- [x] Test discover modules in test assembly (finds `PassThroughModule`)
- [x] Test discover skips abstract classes
- [x] Test discover skips non-public (internal) classes
- [x] Test discover respects `[WorkflowModule(Ignore = true)]`
- [x] Test discover with DI (constructor injection)
- [x] Test discover empty assembly returns 0
- [x] Test discover and register populates registry
- [x] Test discover skips invalid modules (with validation)
- [x] Test discover handles duplicates gracefully
- [x] Test attribute metadata override works
- [x] Test null assembly throws ArgumentNullException
- [x] Test null registry throws ArgumentNullException
- [x] Test DiscoverAndRegisterFrom extension method
- [x] Test DiscoverAndRegisterFromCallingAssembly extension method
- [x] Test empty assembly DiscoverAndRegister returns 0

---

## 1.4.6 Dynamic Module Loading (Foundation) ✅

**Purpose:** Load module assemblies from disk at runtime using isolated `AssemblyLoadContext` for plugin-style extensibility.

**Complexity:** 🟡 Medium

**Tasks:**
- [x] **Create loader contracts** 🚀
  - [x] New file: `Workflow.Modules/Loading/IModuleLoader.cs`
  - [x] `IModuleLoader` interface:
    - [x] `LoadFromAssembly(string assemblyPath) → ModuleLoadResult`
    - [x] `LoadFromDirectory(string directoryPath) → IReadOnlyList<ModuleLoadResult>`
    - [x] `UnloadAssembly(string assemblyPath) → bool`
    - [x] `GetLoadedAssemblies() → IReadOnlyList<string>`
  - [x] `ModuleLoadResult` record:
    - [x] `AssemblyPath` (string)
    - [x] `LoadedModules` (IReadOnlyList\<IWorkflowModule\>)
    - [x] `Errors` (IReadOnlyList\<string\>)
    - [x] `Success` (bool)

- [x] **Implement `AssemblyModuleLoader`** 📦
  - [x] New file: `Workflow.Modules/Loading/AssemblyModuleLoader.cs`
  - [x] Create collectible `AssemblyLoadContext` per loaded assembly (isolation!)
  - [x] Load assembly from file path
  - [x] Use `ModuleDiscovery` to scan loaded assembly
  - [x] Register discovered modules into provided `IModuleRegistry`
  - [x] Track loaded contexts for later unloading
  - [x] `UnloadAssembly`: unregister all modules from that assembly, unload context
  - [x] Handle errors gracefully: invalid path, missing dependencies, etc.

- [x] **Create test modules project** 🧪
  - [x] New project: `Workflow.Tests.SampleModules` (class library)
  - [x] Add to solution
  - [x] Create 2-3 sample modules for loader testing
  - [x] Build output used by loader tests

**Tests (15 passing):** → `Workflow.Tests/Modules/ModuleLoaderTests.cs`
- [x] Test load valid assembly discovers modules
- [x] Test load registers modules into registry
- [x] Test unload removes modules from registry
- [x] Test load invalid path throws/returns error
- [x] Test load assembly with no modules returns empty
- [x] Test assembly isolation (separate load contexts)
- [x] Test load from directory finds all DLLs
- [x] Test `GetLoadedAssemblies` tracks loaded paths
- [x] Test load same assembly twice is idempotent (bonus~)
- [x] Test unload non-loaded assembly returns false (bonus~)
- [x] Test `GetLoadedAssemblies` cleared after unload (bonus~)
- [x] Test invalid module skipped, valid modules loaded (bonus~)
- [x] Test null path throws ArgumentException (bonus~)
- [x] Test LoadFromDirectory null path throws (bonus~)
- [x] Test LoadFromDirectory non-existent directory returns empty (bonus~)

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
- [x] Module contracts enhanced with Version, ValidateConfiguration, Metrics
- [x] Registry supports category lookup, search, events, type-based registration
- [x] ModuleValidator prevents broken modules from loading
- [x] Property binding resolves variables, converts types, validates against schema
- [x] Assembly scanning auto-discovers modules
- [x] Dynamic loading from DLLs works with isolation
- [x] ~70-80 new tests written and passing
- [x] WorkflowValidator deferred checks from Phase 1.2 resolved
- [x] Clear XML documentation on all new APIs

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

