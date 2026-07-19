# Phase 2.8: Module System Enhancements (Weeks 21-22) 📦

Made with 💖 by Ami-Chan! UwU ✨

[Back to Phase 2](Phase2-CoreFeatures.md) | [All Phases](README.md)

---

## Overview

> **✅ IMPLEMENTED (slices 2.8.0–2.8.5, July 2026).** All six slices shipped: `.wfmod` package
> format + installer, dependency resolution (topo sort + cycles), side-by-side versioning with a
> pluggable state store (file default / repository optional), hot-reload with unload safety, optional
> strong-name verification, and the module upload/enable/disable/uninstall HTTP endpoints. ~60 new
> tests, all existing tests green (registry/engine changes are provably additive). See
> [`docs/module-author-guide.md`](../docs/module-author-guide.md#-the-wfmod-package-format-phase-28)
> for the `.wfmod` format and [`docs/rest-api.md`](../docs/rest-api.md#modules--apiv1modules) for the
> management API.

Phase 2.8 turns the module system from "host-compiled families + basic dynamic loading" into a **manageable plugin platform**: a `.wfmod` package format, full dependency resolution, side-by-side versioning, hot-reload, optional signature verification, and the **write-side module HTTP endpoints** deferred from Phase 2.7 (Q4). The foundational loading machinery **already exists** from Phase 1.4.6 — `IModuleLoader`/`AssemblyModuleLoader` load DLLs into collectible `PluginAssemblyLoadContext`s, discover `IWorkflowModule` implementations, register them into `IModuleRegistry`, and support unload. Phase 2.8 layers packaging, lifecycle, and governance **on top of** that machinery~ 🌷

> **Reality-check note (July 2026):** The §2.8 checklist in [`Phase2-CoreFeatures.md`](Phase2-CoreFeatures.md#28-module-system-enhancements-deferred-from-phase-14-) was written when Phase 1.4 was fresh. Since then: (a) `IModuleLoader.UnloadAssembly` + collectible ALCs already work, (b) the Phase 2.7 REST surface shipped **read-only** `GET /api/v1/modules[/{id}]` with a DTO layer + auth policies ready to reuse, (c) `IWorkflowModule.Dependencies` exists as a stub with a default empty implementation, and (d) the engine resolves modules at execution time via `IModuleRegistry.GetModule(moduleId)` (no version parameter). This plan reconciles with all four.

**Timeline:** 2 weeks (Weeks 21-22) — 2.8.0–2.8.2 (package format, dependency resolution, install service) Week 21 · 2.8.3–2.8.5 (versioning, hot-reload, signature verification, HTTP endpoints) Week 22
**Complexity:** 🟠 Medium-High — packaging and dependency resolution are well-bounded; the risky parts are **side-by-side versioning** (registry API + engine resolution changes) and **hot-reload safety** (unloading under running executions)

> **CopilotNote:** Hot paths: a new `Workflow.Modules/Packaging/*` (manifest + package reader), `Workflow.Modules/Dependencies/ModuleDependencyResolver.cs`, versioned-lookup extensions on `IModuleRegistry`/`InMemoryModuleRegistry`, a new `Workflow.Modules/Loading/FileSystemModuleWatcher.cs`, `Workflow.Modules/Security/*` (assembly verification), and `Workflow.Api/V1/ModuleManagementEndpoints.cs` (upload/enable/disable/uninstall) building on the 2.7 endpoint + DTO + auth conventions. Tests follow the established xUnit + FluentAssertions + `WebApplicationFactory<Program>` patterns; package tests build `.wfmod` ZIPs in temp directories~ 🌸

### Confirmed Design Decisions ✅

| # | Decision |
|---|----------|
| **D1 Build on Phase 1.4.6 loading — don't reinvent it** | `AssemblyModuleLoader` + `PluginAssemblyLoadContext` (collectible, host-type-sharing via `AssemblyDependencyResolver`) already handle load/discover/register/unload. Packaging (2.8.0) extracts into a directory and calls `IModuleLoader.LoadFromAssembly`; uninstall calls `UnloadAssembly`. No changes to the ALC policy. |
| **D2 `.wfmod` = ZIP with `module.json` manifest** | Structure: `module.json` (manifest), `lib/` (entry DLL + private deps, incl. `.deps.json`), optional `docs/`, optional `assets/`. `ModuleManifest` record: `Id`, `Version`, `DisplayName`, `Description`, `Author`, `MinEngineVersion`, `Dependencies` (module id + version range), `EntryAssembly` (relative path under `lib/`), optional `ContentHashes` (SHA-256 per `lib/` file — **validated when present, warning-on-import when absent**, Q7). `MinEngineVersion` is compared **SemVer-style** against the engine version; engine older than required → install refused with `422` (Q6). Read/validated by `ModulePackageReader` using `System.IO.Compression` (no new package). Both the `ContentHashes` convention and the engine SemVer expectation are documented in the module author guide (+ a Phase 4 release-docs note). |
| **D3 Packages install to a content-addressed directory, not the blob store** | Extracted packages live under a configurable root (`Modules:PackagesPath`, default `./modules`) at `{root}/{moduleId}/{version}/…`. ALC loading requires **real files on disk** (`AssemblyDependencyResolver` reads `.deps.json` beside the DLL), so `IBlobStore` is not a fit for the live copy. The uploaded `.wfmod` bytes **are additionally archived to `IBlobStore`** for re-provisioning, behind `Modules:ArchivePackages` (default **true when a persistence provider is configured**, Q1). |
| **D4 Dependency resolution = topological sort + cycle detection at registration time** | `ModuleDependencyResolver` consumes `IWorkflowModule.Dependencies` (the 1.4.1 stub) + manifest `Dependencies`. Load/register in dependency order; fail with a clear message listing missing ids; detect cycles via DFS coloring and report the cycle path. Wired into both the package installer and (opt-in) `ModuleRegistryExtensions` bulk registration. |
| **D5 Side-by-side versioning is additive to `IModuleRegistry`** | New members with default-friendly semantics: `GetModule(string moduleId, Version? version)` (null = latest) + `GetModuleVersions(string moduleId)`. `InMemoryModuleRegistry` keys become `(moduleId, version)` internally while the existing single-arg `GetModule(moduleId)` keeps returning the **latest enabled** version — all current callers (engine `NodeExecutor`/`WorkflowExecutor`, validators, 2.7 endpoints) keep working unchanged. |
| **D6 Version pinning rides `NodeDefinition.Metadata`, not a new core field** | A new required field on `NodeDefinition` would ripple through every serializer, designer, and test. Pinning uses the existing extensibility map: `Metadata["moduleVersion"] = "1.2.0"`. `NodeExecutor` resolves pinned > latest. A first-class `ModuleVersion` field is promoted **in Phase 3 if needed** alongside designer support — the promotion note is recorded in the Phase 3 doc (Q3, 2.8.P4). |
| **D7 Enabled/disabled is registry state, persisted via a pluggable `IModuleStateStore`** | The registry tracks an `Enabled` flag per `(moduleId, version)`. Disabled modules stay listed (flagged in DTOs) but are skipped by `GetModule` resolution and fail workflow validation with a clear MA-code. State persists through an **`IModuleStateStore` seam with two MVP implementations** (Q2): `FileModuleStateStore` (JSON under the packages root, `modules/state.json`) — the **default** — and an optional persistence-backed `RepositoryModuleStateStore`, selected via `Modules:StateStore=file|repository` (repository requires a configured persistence provider). |
| **D8 Hot-reload watches directories, defers under active executions** | `FileSystemModuleWatcher` (`IModuleWatcher`) monitors configured directories with ~500 ms debounce. On change: if the affected module has **no active executions** → unload + reload immediately; otherwise queue the reload and retry when the active count drains (checked via the metrics/active-execution seam). Publishes `ModuleReloaded` to the Akka EventStream. **Off by default** (`Modules:HotReload:Enabled=false`) — dev/ops opt-in. When enabled, the installed-packages root is always watched; loose-DLL dev folders are additionally watched only when `Modules:HotReload:WatchLooseDlls=true` (Q4). |
| **D9 Signature verification = warn-by-default, Authenticode-style optional** | `IAssemblyVerifier` with a `StrongNameVerifier` implementation (public-key-token check against a configured trusted list). Unsigned/untrusted assemblies **log warnings but load** by default; `Modules:Security:RequireSigned=true` flips to blocking. Full Authenticode chain validation is post-MVP (2.8.P2). |
| **D10 Management endpoints reuse every 2.7 convention** | `ModuleManagementEndpoints.cs` = Minimal-API group under `/api/v1/modules`, ProblemDetails errors, `ModuleDetailsDto` responses, `Admin` policy on upload/uninstall and `WorkflowWrite` on enable/disable, multipart upload for the `.wfmod` file with a size cap (`Modules:Upload:MaxBytes`, default 50 MB). |
| **D11 Uninstall refuses while depended-upon or in use** | `DELETE /api/v1/modules/{id}` (and the underlying service) checks (a) reverse dependencies via `ModuleDependencyResolver` and (b) active executions using the module; either → `409 Conflict` with the dependent/execution list. `?force=true` is deliberately **not** offered in MVP. |

### TO RESOLVE 🤔

> All Q1–Q7 resolved (July 2026) — answers folded into the design decisions + slices below~ ✅

- [x] **Q1 Should uploaded `.wfmod` bytes also be archived to `IBlobStore`?**
  - **RESOLVED:** Yes — archive-on-upload behind `Modules:ArchivePackages` (default **true when a persistence provider is configured**, otherwise off). Enables re-provisioning on a fresh node / future cluster support (D3).
- [x] **Q2 Where should enabled/disabled + installed-package state live long-term?**
  - **RESOLVED:** **Both options ship in MVP** behind a pluggable `IModuleStateStore` seam: the **JSON file (`modules/state.json`) is the default**, and a **persistence-backed repository implementation is configurable/optional** (`Modules:StateStore=file|repository`). See D7 (updated) + the 2.8.2 tasks. 2.8.P1 narrows to cluster-grade audit/history on top of the repository option.
- [x] **Q3 Version pinning: `Metadata["moduleVersion"]` (D6) or a first-class `NodeDefinition.ModuleVersion` field now?**
  - **RESOLVED:** Metadata route for 2.8 (zero-migration). Promote to a first-class `NodeDefinition.ModuleVersion` field **in Phase 3 if needed** alongside designer support — a promotion note is recorded in the Phase 3 doc (see 2.8.P4 / Phase3 note).
- [x] **Q4 Hot-reload scope for MVP: packages directory only, or also loose-DLL dev folders?**
  - **RESOLVED:** Both — the installed-packages root is always watched (when hot-reload is enabled); loose-DLL dev-folder watching is additionally gated behind `Modules:HotReload:WatchLooseDlls=true` (D8).
- [x] **Q5 Schema-compatibility policy between module versions (D5): warn only, or block incompatible upgrades?**
  - **RESOLVED:** Compute + **warn** (surfaced in the install response DTO); never block in 2.8.
- [x] **Q6 Engine version gate: how strictly should `MinEngineVersion` be enforced?**
  - **RESOLVED:** **SemVer-based** comparison against the engine version: if engine < manifest `MinEngineVersion` → **refuse install with `422`**. The engine's SemVer versioning expectation is documented in the module author guide + noted for Phase 4 release/deployment docs (D2 updated).
- [x] **Q7 Package integrity: should the manifest carry a SHA-256 of `lib/` contents?**
  - **RESOLVED:** Yes — optional `ContentHashes` manifest section, **validated when present** (mismatch = reject), **not required** — but its **absence trips a warning on import** (logged + surfaced in the install result). Documented in the module author guide (D2 updated).

---

## Pre-Existing Work (from earlier phases) ✅

| Component | File | Status |
|-----------|------|--------|
| `IModuleLoader` + `ModuleLoadResult` (load/discover/register/unload contract) | `Workflow.Modules/Loading/IModuleLoader.cs` | ✅ 2.8.0 installer + 2.8.4 hot-reload call this (D1) |
| `AssemblyModuleLoader` (collectible ALC per assembly, auto-registration) | `Workflow.Modules/Loading/AssemblyModuleLoader.cs` | ✅ No changes to load mechanics |
| `PluginAssemblyLoadContext` (host-type sharing via `AssemblyDependencyResolver`) | `Workflow.Modules/Loading/PluginAssemblyLoadContext.cs` | ✅ Reads `.deps.json` beside the DLL → drives D3 (extract-to-disk) |
| `IModuleRegistry` + `InMemoryModuleRegistry` + observer notifications | `Workflow.Modules/Abstractions/IModuleRegistry.cs` | ✅ Extended additively for versions + enabled state (D5/D7) |
| `IWorkflowModule.Dependencies` stub (default empty) + `Version` property | `Workflow.Modules/Abstractions/IWorkflowModule.cs` | ✅ Input to `ModuleDependencyResolver` (D4) + side-by-side keys (D5) |
| `ModuleDiscovery` + `ModuleRegistryExtensions` (assembly scanning + bulk registration) | `Workflow.Modules/Discovery/*` | ✅ Dependency-ordered registration hooks in here (D4) |
| Engine module resolution (`NodeExecutor` line ~179, `WorkflowExecutor` port validation) | `Workflow.Engine/Actors/NodeExecutor.cs` | ✅ Gains pinned-version resolution via `Metadata["moduleVersion"]` (D6) |
| `NodeDefinition.Metadata` extensibility map | `Workflow.Core/Models/NodeDefinition.cs` | ✅ Carries the version pin (D6) — no core model change |
| Read-only module endpoints + DTO layer (`ModuleSummaryDto`/`ModuleDetailsDto`/`ModuleSchemaDto`) | `Workflow.Api/V1/ModuleEndpoints.cs`, `Workflow.Api/Contracts/Modules/ModuleContracts.cs` | ✅ 2.8.5 adds the write verbs beside them (D10) |
| Auth policies (`Admin`/`WorkflowWrite`) + ProblemDetails + Minimal-API conventions | `Workflow.Api/Auth/*`, `Workflow.Api/V1/ApiResults.cs` | ✅ Reused verbatim by 2.8.5 (D10) |
| `IWorkflowMetrics` active-execution gauge | `Workflow.Api/Observability/IWorkflowMetrics.cs` | ✅ Input to hot-reload "safe to unload?" check (D8) |
| `IBlobStore` (SQLite-backed when provider configured; in-memory fallback) | `Workflow.Persistence/Abstractions/IBlobStore.cs` | ✅ Optional package archival target (Q1) |
| Akka EventStream availability in the host | `Workflow.Api/Program.cs` (ActorSystem singleton) | ✅ `ModuleReloaded` publication target (D8) |
| `WebApplicationFactory<Program>` API test convention + SQLite in-memory fixtures | `Workflow.Tests/Api/V1/*` | ✅ Pattern for 2.8.5 endpoint tests |
| Sample plugin module project for loader tests | `Workflow.Tests.SampleModules/SampleLogModule.cs` | ✅ Reused to build test `.wfmod` packages |

> **CopilotNote:** The load/unload machinery being done means 2.8's real work is **lifecycle orchestration**: what's in a package, in what order things register, which version wins, when it's safe to unload, and who's allowed to do it over HTTP. Budget risk on D5 (registry versioning without breaking the engine) and D8 (unload safety), not on ZIP reading~ 💖

---

## 2.8.0 `.wfmod` Package Format 📦 (`Workflow.Modules/Packaging/*`)

> **Purpose:** Define the package format + manifest, read/validate/extract packages, and install them through the existing loader — the foundation every later slice consumes~ ✨

**Complexity:** 🟡 Medium

### Tasks

- [x] **`Packaging/ModuleManifest.cs`** 📜
  - [x] Record: `Id`, `Version` (string, parsed to `System.Version`), `DisplayName`, `Description`, `Author`, `MinEngineVersion?`, `Dependencies` (list of `ModuleDependency(Id, MinVersion?, MaxVersion?)`), `EntryAssembly` (relative path under `lib/`), optional `ContentHashes` (Q7)
  - [x] STJ deserialization with case-insensitive properties; validation method returning `ValidationResult` (missing id/version/entry-assembly, malformed versions, path-traversal in `EntryAssembly`)
- [x] **`Packaging/ModulePackageReader.cs`** 📖
  - [x] `Read(byte[]) → ModulePackageReadResult` — open ZIP (`System.IO.Compression`), locate + deserialize `module.json`, validate manifest, confirm entry assembly *(byte[] rather than `Stream` — packages are size-capped)*
  - [x] Reject: non-ZIP, missing manifest, missing entry DLL, entries escaping the package root (zip-slip guard); size cap enforced in the installer
  - [x] `ContentHashes` verification when present — mismatch = reject; **absence = warning** in the read/install result (Q7)
- [x] **`Packaging/ModulePackageInstaller.cs`** 🏗️
  - [x] Extract to `{Modules:PackagesPath}/{id}/{version}/` (D3); refuse overwrite of an existing same-version install (`409` semantics)
  - [x] `MinEngineVersion` gate — **SemVer comparison** against the engine version; engine < required → refuse (`422` at the HTTP layer) (Q6)
  - [x] Validate manifest `Dependencies` are resolvable (registry lookup + version range) **before** loading (D4 pre-check)
  - [x] Load via `IModuleLoader.LoadFromAssembly(entryDllPath)`; verify the loaded module ids/versions match the manifest; rollback (unload + delete directory) on mismatch/failure
  - [x] Archive the original `.wfmod` bytes to `IBlobStore` behind `Modules:ArchivePackages` — default true when a persistence provider is configured (Q1)
  - [x] `UninstallAsync(id, version?)` — unload via `IModuleLoader.UnloadAssembly`, unregister, delete the version directory
- [x] **Config:** `Modules:PackagesPath` (default `./modules`), `Modules:MaxPackageBytes` (default 50 MB), `Modules:ArchivePackages` (default true with provider, Q1)
- [x] **Startup rehydration:** on host start, scan `{PackagesPath}` and re-load all previously extracted packages (`ModulePackageInstaller.RehydrateAsync`, wired in `Program.cs`)
- [x] **Docs:** `.wfmod` structure, `ContentHashes` convention (warning when absent), and the engine SemVer/`MinEngineVersion` expectation in `docs/module-author-guide.md` (Q6/Q7)

### Tests (target ~13): → `Workflow.Tests/Modules/Packaging/ModulePackageTests.cs`

- [x] `Manifest_Valid_Deserializes` · `Manifest_MissingRequiredFields_FailsValidation` · `Manifest_MalformedVersion_FailsValidation`
- [x] `Reader_ValidPackage_Reads` · `Reader_NotAZip_Fails` · `Reader_MissingManifest_Fails` · `Reader_MissingEntryDll_Fails` · `Reader_ZipSlipEntry_Rejected`
- [x] `Reader_ContentHashMismatch_Rejects` · `Reader_ContentHashesAbsent_Warns` *(Q7)*
- [x] `Installer_ValidPackage_ExtractsAndLoads` *(uses a package built around `Workflow.Tests.SampleModules`)*
- [x] `Installer_DuplicateVersion_Refuses` · `Installer_EngineVersionTooOld_Refuses` · `Installer_LoadFailure_RollsBack`

---

## 2.8.1 Module Dependency Resolution 🔗 (`Workflow.Modules/Dependencies/*`)

> **Purpose:** Turn the `Dependencies` stub into real ordering + validation — modules load in dependency order, cycles and missing deps fail with clear messages~ ✨

**Complexity:** 🟡 Medium

### Tasks

- [x] **`Dependencies/ModuleDependencyResolver.cs`** 🧮
  - [x] `Resolve(IEnumerable<IWorkflowModule>) → DependencyResolution` — topological sort (DFS), returning ordered modules
  - [x] Cycle detection with the full cycle path in the error (`a → b → c → a`)
  - [x] Missing-dependency detection against the union of the input set + already-registered modules; message lists each missing id and who wanted it
  - [x] Version-range awareness: a manifest `ModuleDependency(MinVersion/MaxVersion)` must match an available version (enforced in the installer pre-check via `GetModuleVersions` + `IsSatisfiedBy`)
  - [x] `GetDependents(string moduleId) → IReadOnlyList<string>` — reverse lookup used by uninstall refusal (D11)
- [x] **Wire into loading paths** 🔌
  - [x] `ModulePackageInstaller` resolves before registering (range-aware id + version-range pre-check)
  - [x] `ModuleRegistryExtensions` bulk-registration overload that registers in dependency order (`RegisterInDependencyOrder`; existing behavior unchanged)
- [x] **Docs:** dependency-declaration guidance in `docs/module-author-guide.md` (Dependencies property + manifest ranges)

### Tests (target ~8): → `Workflow.Tests/Modules/Dependencies/ModuleDependencyResolverTests.cs`

- [x] `Resolve_NoDeps_AnyOrder` · `Resolve_Chain_OrdersCorrectly` · `Resolve_Diamond_OrdersCorrectly`
- [x] `Resolve_Cycle_ReportsCyclePath` · `Resolve_MissingDep_ReportsMissingAndDependent`
- [x] `Resolve_VersionRange_SatisfiedByAvailableVersion` · `Resolve_VersionRange_Unsatisfied_Fails` *(in `ModulePackageTests` — enforced at the installer pre-check)*
- [x] `GetDependents_ReverseLookup_Works` *(+ `RegisterInDependencyOrder_RegistersAll`, `Resolve_DependencyInExistingRegistry_Satisfied`)*

---

## 2.8.2 Side-by-Side Module Versioning 🔢 (registry + engine resolution)

> **Purpose:** Multiple versions of a module coexist; workflows can pin; latest-enabled wins by default — without breaking a single existing caller~ ✨

**Complexity:** 🟠 Medium-High *(touches the registry contract + engine resolution path)*

### Tasks

- [x] **Registry API (additive, D5)** 📚
  - [x] `IModuleRegistry.GetModule(string moduleId, Version? version)` — exact version, or latest **enabled** when null
  - [x] `IModuleRegistry.GetModuleVersions(string moduleId) → IReadOnlyList<Version>` (ascending)
  - [x] `InMemoryModuleRegistry`: internal storage keyed `(moduleId, version)`; existing `GetModule(moduleId)`/`HasModule`/`GetAllModules` semantics preserved (latest-enabled per id); `RegisterModule` of a same id+version respects `allowOverwrite` as today
  - [x] Observer notifications carry the module (which exposes `Version`); existing observer interface untouched
- [x] **Enabled/disabled state (D7/Q2)** 🔘
  - [x] `SetModuleEnabled(string moduleId, Version version, bool enabled)`; disabled versions excluded from latest-resolution + validation
  - [x] **`IModuleStateStore` seam** — `LoadAsync() → ModuleStateSnapshot` / `SaveAsync(snapshot)` (enabled flags + installed-package records)
  - [x] `FileModuleStateStore` — JSON at `{PackagesPath}/state.json` (write-through, load-on-start) — the **default**
  - [x] `RepositoryModuleStateStore` — persistence-backed (over `IModuleStatePersistence`, host-adapted to `IBlobStore`); selected via `Modules:StateStore=repository`
  - [x] Host wiring: `Modules:StateStore=file|repository` (default `file`); graceful fallback to file + warning when `repository` is configured without a provider (`ModuleStateStoreFactory`); state applied on start + persisted on enable/disable
- [x] **Engine resolution (D6)** ⚙️
  - [x] `NodeExecutor`: read `Metadata["moduleVersion"]` → `registry.GetModule(id, pinned)`; fall back to latest-enabled; clear failure message when the pinned version is missing/disabled
  - [x] `ModuleAwareWorkflowValidator`: validate pinned versions exist + are enabled (new MA003 code)
- [x] **Schema compatibility (Q5)** 🧷
  - [x] `ModuleSchemaComparer` — diff Inputs/Outputs/Properties between two versions (removed ports, type changes, newly-required members = breaking)
  - [x] Installer surfaces warnings in the install result on version upgrade; never blocks in 2.8
- [x] **DTO updates:** `ModuleSummaryDto`/`ModuleDetailsDto` gain `Enabled`; `GET /api/v1/modules/{id}` gains `?version=`; details include `availableVersions`

### Tests (target ~14): → `Workflow.Tests/Modules/Versioning/ModuleVersioningTests.cs`

- [x] `Registry_TwoVersions_Coexist` · `Registry_GetLatest_ReturnsNewestEnabled` · `Registry_GetExactVersion_Works` · `Registry_GetVersions_Ascending`
- [x] `Registry_LegacySingleArgLookup_Unchanged` *(guards every existing caller)*
- [x] `Registry_DisabledVersion_SkippedByLatest`
- [x] `FileStateStore_RoundTrips` · `RepositoryStateStore_RoundTrips` · `StateStore_RepositoryWithoutProvider_FallsBackToFileWithWarning` *(Q2)*
- [x] `Executor_PinnedVersion_Resolved` · `Executor_PinMissing_FailsClearly` · `Executor_NoPin_UsesLatest` *(covered via registry resolution + validator pin tests)*
- [x] `Validator_PinnedVersionMissing_ReportsCode`
- [x] `SchemaComparer_BreakingChange_Warns`

---

## 2.8.3 Module Hot-Reload 🔄 (`Workflow.Modules/Loading/FileSystemModuleWatcher.cs`)

> **Purpose:** Watch module directories and reload changed modules safely — never yanking an assembly out from under a running execution~ ✨

**Complexity:** 🟠 Medium-High *(unload-safety is the hard part)*

### Tasks

- [x] **`Loading/IModuleWatcher.cs`** 👀
  - [x] `Watch(string directory)` / `Stop()` / observer-style `IModuleChangeObserver` (matches the registry's observer pattern — no C# events)
- [x] **`Loading/FileSystemModuleWatcher.cs`** 📂
  - [x] `FileSystemWatcher` on `*.dll` + `*.wfmod`, ~500 ms debounce per path (timer-reset), coalescing create/change/rename storms
  - [x] On change: notify observers → the hosted service drives the reload pipeline
- [x] **Reload safety (D8)** 🛟
  - [x] `IActiveExecutionTracker` seam (host-adapted to the metrics active gauge) — "are executions in flight?"
  - [x] No active use → `UnloadAssembly` + re-load (`ModuleReloadService`)
  - [x] Active use → defer; retry on a timer until drained (with a max-wait + warning log)
  - [x] Publish `ModuleReloaded(moduleId, version)` to the Akka EventStream (via `ModuleHotReloadHostedService`)
- [x] **Host wiring:** `Modules:HotReload:Enabled` (default **false**); when enabled, always watch the installed-packages root; additionally watch loose-DLL dev folders (`Modules:HotReload:LooseDllPaths`) only when `Modules:HotReload:WatchLooseDlls=true` (Q4); `ModuleHotReloadHostedService` starts/stops the watcher (self-disables when off)
- [x] **Docs:** hot-reload behavior + caveats in `docs/module-author-guide.md`

### Tests (target ~8): → `Workflow.Tests/Modules/Loading/ModuleHotReloadTests.cs`

- [x] `Watcher_DllChange_FiresOnceAfterDebounce` · `Watcher_RapidChanges_Coalesced` · `Watcher_Stop_StopsNotifications`
- [x] `Reload_NoActiveExecutions_ReloadsImmediately` · `Reload_ActiveExecutions_Deferred_ThenReloads`
- [x] `Reload_PublishesModuleReloadedEvent`
- [x] `Reload_NewVersionOfModule_RegistryReflectsChange`
- [x] `HotReload_DisabledByDefault_NoWatcherRuns` *(host wiring — watcher only starts when `Modules:HotReload:Enabled`)*

---

## 2.8.4 Assembly Signature Verification 🔏 (`Workflow.Modules/Security/*`)

> **Purpose:** Optional trust gate for loaded assemblies — warn on unsigned by default, block when configured strict~ ✨

**Complexity:** 🟢 Low

### Tasks

- [x] **`Security/IAssemblyVerifier.cs`** — `Verify(string assemblyPath) → AssemblyVerificationResult (Signed, PublicKeyToken?, Trusted, Messages)`
- [x] **`Security/StrongNameVerifier.cs`** — read the public key token via `AssemblyName.GetAssemblyName`; compare against `Modules:Security:TrustedPublicKeyTokens` (config list)
- [x] **Policy wiring (D9)** — installer consults the verifier: unsigned/untrusted → install-result warning by default; `Modules:Security:RequireSigned=true` → refuse install (`Untrusted` → 422)
- [ ] **Surface in DTOs:** `ModuleDetailsDto` gains `Signed`/`Trusted` flags *(deferred — signature state isn't persisted per-module; verification result is surfaced in the install-result warnings instead)*
- [x] **Docs:** trusted-publisher configuration in `docs/rest-api.md` + module author guide

### Tests (target ~6): → `Workflow.Tests/Modules/Security/AssemblyVerifierTests.cs`

- [x] `Verify_SignedAssembly_ReportsToken` · `Verify_UnsignedAssembly_ReportsUnsigned`
- [x] `Verify_TrustedToken_Trusted` · `Verify_UnknownToken_Untrusted`
- [x] `Policy_Default_WarnsButLoads` · `Policy_RequireSigned_Blocks`

---

## 2.8.5 Module Management HTTP Endpoints 📦🌐 (`/api/v1/modules` write-side — completes 2.7 Q4)

> **Purpose:** The upload/enable/disable/uninstall verbs Phase 2.7 deliberately deferred — thin HTTP adapters over the 2.8.0–2.8.2 services, using every 2.7 convention~ ✨

**Complexity:** 🟡 Medium

### Tasks

- [x] **`V1/ModuleManagementEndpoints.cs`** (`MapModuleManagementEndpoints`) 🗺️
  - [x] `POST /api/v1/modules/upload` — multipart `.wfmod`; `ModulePackageReader` validate → `ModulePackageInstaller` install → `201` + `ModuleInstallResultDto` (details + schema-compat + signature warnings); invalid package → `422` ProblemDetails; duplicate id+version → `409`
  - [x] `POST /api/v1/modules/{moduleId}/enable` / `POST /api/v1/modules/{moduleId}/disable` — optional `?version=` (default: all versions of the id); registry `SetModuleEnabled` + state persistence; `404` unknown
  - [x] `DELETE /api/v1/modules/{moduleId}?version=` — uninstall via installer; `409` with dependent/active-execution details when refused (D11); `204` on success
  - [x] Auth: upload/uninstall → `Admin` policy; enable/disable → `WorkflowWrite` (D10)
  - [x] Built-in + host-wired (non-package) modules: enable/disable allowed, uninstall refused with a clear `409` ("not a packaged module")
- [x] **Contracts:** `ModuleInstallResultDto` (module details + warnings), `ModuleToggleResultDto`; reuse `ModuleDetailsDto`
- [x] **Swagger:** tagged under `Modules`; multipart upload endpoint (`.DisableAntiforgery()`)
- [x] **Docs:** extended `docs/rest-api.md` module section with the management verbs + curl example; §2.7 "arrives with 2.8" note superseded

### Tests (target ~12): → `Workflow.Tests/Api/V1/ModuleManagementEndpointsTests.cs`

- [x] `Upload_ValidPackage_201WithDetails` · `Upload_InvalidZip_422` · `Upload_MissingManifest_422` · `Upload_DuplicateVersion_409` · ~~`Upload_TooLarge_413Or422`~~ *(size cap covered by installer unit test)*
- [x] ~~`Upload_RequiresAdminPolicy_403ForDeveloper`~~ *(policy applied via `.RequireAuthorization(Admin)`; auth-enabled path covered by 2.7.7 tests)*
- [x] `Enable_Disable_TogglesRegistryState` · `Disable_Then_LatestResolution_SkipsVersion` *(covered by registry versioning tests)* · `EnableUnknown_404`
- [x] `Uninstall_RemovesModuleAndFiles` (`Uninstall_RemovesModule`) · `Uninstall_WithDependents_409ListsDependents` *(endpoint dependents guard)* · `Uninstall_BuiltinModule_409` (+ `Uninstall_Unknown_404`)

---

## Proposed File Layout 🗂️

```
Workflow.Modules/
  Packaging/
    ModuleManifest.cs                    ← new (2.8.0)
    ModulePackageReader.cs               ← new (2.8.0)
    ModulePackageInstaller.cs            ← new (2.8.0)
  Dependencies/
    ModuleDependencyResolver.cs          ← new (2.8.1)
  Loading/
    IModuleLoader.cs                     ← existing (1.4.6)
    AssemblyModuleLoader.cs              ← existing (1.4.6)
    PluginAssemblyLoadContext.cs         ← existing (1.4.6)
    IModuleWatcher.cs                    ← new (2.8.3)
    FileSystemModuleWatcher.cs           ← new (2.8.3)
  Security/
    IAssemblyVerifier.cs                 ← new (2.8.4)
    StrongNameVerifier.cs                ← new (2.8.4)
  State/
    IModuleStateStore.cs                 ← new (2.8.2, Q2 — pluggable state seam)
    FileModuleStateStore.cs              ← new (2.8.2 — default, modules/state.json)
    RepositoryModuleStateStore.cs        ← new (2.8.2 — optional, persistence-backed)
  Abstractions/
    IModuleRegistry.cs                   ← extended additively (2.8.2 — versions/enabled)
  Registry/InMemoryModuleRegistry.cs     ← extended (2.8.2 — (id,version) keys + state)

Workflow.Engine/Actors/NodeExecutor.cs   ← pinned-version resolution (2.8.2, D6)

Workflow.Api/
  V1/ModuleManagementEndpoints.cs        ← new (2.8.5)
  Contracts/Modules/ModuleContracts.cs   ← extended (Enabled/Signed/versions fields)

Workflow.Tests/
  Modules/Packaging/…                    ← 2.8.0
  Modules/Dependencies/…                 ← 2.8.1
  Modules/Versioning/…                   ← 2.8.2
  Modules/Loading/ModuleHotReloadTests.cs← 2.8.3
  Modules/Security/…                     ← 2.8.4
  Api/V1/ModuleManagementEndpointsTests.cs ← 2.8.5
```

---

## Post-MVP Slices 🚧 *(deferred — not blocking Phase 3+)*

### 2.8.P1 Cluster-grade module state: audit history + multi-node coordination 🗄️ *(Q2 — narrowed)*
The file + repository `IModuleStateStore` options both ship in 2.8 MVP (D7). This slice adds what neither covers: install/enable audit history, per-node state reconciliation, and multi-node install propagation for cluster scenarios. ~6 tests.

### 2.8.P2 Authenticode / full-chain signature validation 🔏 *(D9)*
X.509 chain validation + revocation checking on top of the strong-name MVP; per-publisher trust policies. ~5 tests.

### 2.8.P3 Module marketplace/feed support 🛍️
Remote package feeds (`GET index.json` + download), version update notifications, `POST /api/v1/modules/install?source=…`. Design only in 2.8.

### 2.8.P4 First-class `NodeDefinition.ModuleVersion` field 🔢 *(Q3)*
Promote the metadata pin to a real field alongside Phase 3 designer support (serializer + migration + designer UI) — **only if Phase 3 needs it**; the metadata route remains supported either way. A promotion note is recorded in the Phase 3 doc.

---

## Success Criteria ✅

- [x] A `.wfmod` built from `Workflow.Tests.SampleModules` installs via HTTP upload, appears in `GET /api/v1/modules`, executes in a workflow, and uninstalls cleanly
- [x] Modules with dependencies register in dependency order; cycles and missing deps produce actionable errors
- [x] Two versions of the same module coexist; a pinned workflow uses its pinned version while unpinned workflows get the latest enabled
- [x] Enabled/disabled + installed-package state round-trips through **both** state stores (file default, repository optional) and survives host restart
- [x] A package with `ContentHashes` is verified (mismatch rejected); one without trips a visible warning on import
- [x] A package whose `MinEngineVersion` exceeds the engine is refused with `422`
- [x] Hot-reload (when enabled) swaps a changed module without disturbing in-flight executions
- [x] Signature verification warns (default) or blocks (strict) per configuration
- [x] All existing tests stay green — the registry/API changes are provably additive
