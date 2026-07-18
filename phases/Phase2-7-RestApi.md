# Phase 2.7: REST API Implementation (Weeks 19-20) 🌐

Made with 💖 by Ami-Chan! UwU ✨

[Back to Phase 2](Phase2-CoreFeatures.md) | [All Phases](README.md)

---

## Overview

Phase 2.7 puts a **first-class REST surface** over everything Phases 2.1–2.6 already built: workflow definition CRUD, execution start/status/cancel, module discovery, variable management, health/monitoring, webhook management, and authentication. Almost none of the *engine* work is new — the repositories (`IWorkflowRepository`, `IExecutionHistoryRepository`, `IVariableStore`), the Akka execution messages (`CreateWorkflowInstance`/`GetWorkflowStatus`/`CancelExecution`), the module registry, health checks, and webhook endpoints **already exist**. Phase 2.7 is overwhelmingly about **wiring HTTP endpoints over existing seams**, adding the two genuinely-new cross-cutting concerns (auth + versioning), and packaging it all behind a documented, versioned `/api/v1` surface~ 🌷

> **Reality-check note (July 2026):** The original checklist in [`Phase2-CoreFeatures.md` §2.7](Phase2-CoreFeatures.md#27-rest-api-implementation-week-13-14) was written before the engine/persistence layers landed and assumes **MVC controllers** (`WorkflowsController`, etc.). The codebase established a **Minimal-API endpoint-group** convention instead (`MapWebhookEndpoints`, `MapDatabaseConnectionEndpoints`, `MapDatabaseLinqEndpoints`, `MapTransformScriptEndpoints`). This plan follows the established convention (D1). Where the checklist's target already exists (webhook endpoints, health-check plumbing, Swagger bootstrap), the plan **reconciles rather than rebuilds**.

**Timeline:** 2 weeks (Weeks 19-20) — 2.7.0–2.7.6 (core resource + monitoring surface) Week 19 · 2.7.7–2.7.8 (auth + OpenAPI polish + E2E) Week 20
**Complexity:** 🟠 Medium — the CRUD/execution/variable/module/monitoring endpoints are thin adapters over existing services (low risk, high volume); the real design work is **auth** (API Key + JWT), **API versioning**, and a **general execution service** to replace the webhook-specific launcher

> **CopilotNote:** Hot paths: a new `Workflow.Api/V1/*` folder of endpoint-group extension methods (`WorkflowEndpoints`, `ExecutionEndpoints`, `ModuleEndpoints`, `VariableEndpoints`, `MonitoringEndpoints`), a new `Workflow.Api/Contracts/*` DTO layer, a new `Workflow.Api/Auth/*` (API-key + JWT handlers), a general `IWorkflowExecutionService` in `Workflow.Api/Execution/`, and `IModuleRegistry` DI registration (currently missing). No engine or persistence changes required for the core surface — the messages + repos already exist. Tests use the established `WebApplicationFactory<Program>` (Docker-free, in-memory persistence)~ 🌸

### Confirmed Design Decisions ✅

| # | Decision |
|---|----------|
| **D1 Minimal APIs, not MVC controllers** | The whole codebase uses Minimal-API endpoint-group extension methods (`app.MapGroup("/api/…")` + `MapPost/MapGet/…`, e.g. `WebhookEndpoints.cs`, `DatabaseConnectionEndpoints.cs`). The checklist's `WorkflowsController`/`ExecutionsController`/etc. are superseded by `WorkflowEndpoints.cs`/`ExecutionEndpoints.cs`/etc. Same handler shape (static methods with DI params), same `IEndpointRouteBuilder` extension pattern. |
| **D2 Reuse the existing persistence + engine seams — no new stores** | `IWorkflowRepository` already exposes full CRUD (`CreateAsync`/`UpdateAsync`/`DeleteAsync` soft + `PurgeAsync`/`RestoreAsync`/`GetByIdAsync`/`GetAllAsync` paged/`SearchAsync`/`ExistsAsync`). `IExecutionHistoryRepository` covers execution + node records. `IVariableStore` covers scoped versioned variables. The Akka `WorkflowSupervisor` already handles `CreateWorkflowInstance`, `GetWorkflowStatus`, `CancelExecution`. Endpoints are thin adapters over these — the plan adds **zero** new repository interfaces. |
| **D3 A general `IWorkflowExecutionService` replaces the webhook-specific launcher for the API** | The existing `IWorkflowLauncher.LaunchAsync` takes a `WebhookRegistration` (2.3.6-shaped). The execution endpoints need "launch by definition id / by name with arbitrary inputs + `ExecutionStartOptions`". A new `IWorkflowExecutionService` (in `Workflow.Api/Execution/`) wraps the supervisor `Ask` for **start / status / cancel**, reusing `ActorWorkflowLauncher`'s proven Ask+timeout pattern. The webhook launcher stays as-is (webhooks keep working). |
| **D4 Version the new core resource surface under `/api/v1`** | New Phase 2.7 endpoints live under `/api/v1/{workflows,executions,modules,variables,health,status,metrics}`. The existing **feature-tooling** endpoints from 2.3/2.4/2.6 (`/api/webhooks`, `/api/database/*`, `/api/transform/script/*`) are authoring/management tools, not versioned resource APIs — they stay at their current paths (a compatibility note is documented; optional `/api/v1` aliases tracked post-MVP). Versioning via `Asp.Versioning.Http` (URL-segment). |
| **D5 Register `IModuleRegistry` in API DI (currently a gap)** | `IModuleRegistry` + `InMemoryModuleRegistry` exist and are fully featured (`GetAllModules`/`GetModule`/`HasModule`/`GetModulesByCategory`/`SearchModules`), but the API host never registers/populates one. 2.7.3 registers a singleton `InMemoryModuleRegistry`, seeds it from `BuiltinModules.GetAll()` **plus** the host-wired families (database/cloud/transform-script modules resolved from DI as `IEnumerable<IWorkflowModule>`), so the module endpoints see every registered module. |
| **D6 Serializable DTO layer — never serialize domain records directly** | Domain models use LanguageExt (`Arr`/`HashMap`/`Option`) + `System.Type` (in `ModuleSchema`) + `System.Version`, which don't round-trip cleanly through `System.Text.Json`. Phase 2.7 adds a `Workflow.Api/Contracts/*` DTO layer (`WorkflowSummaryDto`/`WorkflowDetailsDto`/`ExecutionDto`/`ExecutionStatusDto`/`ModuleSummaryDto`/`ModuleDetailsDto`/`ModuleSchemaDto`/`VariableDto`/…) with explicit projections. `Type` → assembly-qualified string; `Version` → string; `Option<T>` → nullable. |
| **D7 Caller identity resolution extracted + reused (audit fields)** | `Program.cs` already has a `ResolveCallerId(HttpContext)` static (X-Caller-Id → `NameIdentifier`/`sub` claim → `"system"`). This is extracted to `Workflow.Api/Auth/CallerIdentity.cs` (an extension method) and reused by the execute endpoints to populate `ExecutionStartOptions.CallerId` (which the engine persists as the execution's `TriggeredBy`). Once auth (2.7.7) lands, the claim path becomes real. |
| **D8 RFC 7807 ProblemDetails for all error responses** | `AddProblemDetails()` + a consistent `{ type, title, status, detail, errors? }` shape for 400/401/403/404/409/422/500. Validation failures (from `ModuleAwareWorkflowValidator`) map to 422 with a per-field `errors` map. Not-found → 404, version/state conflicts → 409. |
| **D9 Auth MVP = API Key + JWT *bearer validation*; full login/refresh/user-store is post-MVP** | Phase 2.7 ships (a) an **API-key scheme** (`X-API-Key` validated against config-declared keys, each carrying a caller id + roles; keys hashed at rest via the existing Data-Protection seam) and (b) a **JWT bearer scheme** that *validates externally-issued tokens* (`AddJwtBearer`, configurable authority/audience/signing key). A **local dev/test mode requires no auth** (`Api:Auth:Require=false` by default) so `WebApplicationFactory` tests and local runs work anonymously; setting `Api:Auth:Require=true` enforces auth globally. The checklist's `/auth/login` + `/auth/refresh` + username/password store + full RBAC model is a **real identity system** — deferred to **2.7.P1** (Q1). Endpoints are `[Authorize]`-able but no-op when auth is disabled. |
| **D10 Health via the existing `IPersistenceProvider.HealthCheckAsync` + `AddHealthChecks`** | `IPersistenceProvider` already exposes `HealthCheckAsync(ct) → HealthCheckResult`. 2.7.5 wires ASP.NET `AddHealthChecks()` with a persistence check + an actor-system check (supervisor `Ask<…>` liveness), exposed at `/api/v1/health` (detailed), `/api/v1/health/ready` (readiness), `/api/v1/health/live` (liveness). |
| **D11 Metrics MVP = a JSON `/status` + `/metrics` seam behind a generic exporter interface** | A lightweight, **exporter-agnostic** `IWorkflowMetrics` counter service (executions started/completed/failed, active count) backs `/api/v1/status` (JSON overview) and `/api/v1/metrics` (JSON dump). The seam is deliberately generic so **any exporter can plug in** — a Prometheus text-format exporter (`prometheus-net`) is **opt-in / post-MVP** (Q2) and additive. |
| **D12 Swagger enrichment builds on the existing bootstrap** | `AddSwaggerGen()` + `UseSwagger/UseSwaggerUI` already run. 2.7.8 enriches: XML doc comments (`<GenerateDocumentationFile>`), the two auth security schemes (ApiKey + Bearer) so "Authorize" works in the UI, request/response examples, and grouped tags per resource. |
| **D13 Rate limiting via the in-box .NET 8 middleware, opt-in** | .NET 8's `Microsoft.AspNetCore.RateLimiting` (no new package) provides a fixed-window limiter keyed by API key / caller id, returning `429` + `Retry-After`. Wired but **disabled by default** (enabled via `Api:RateLimit:Enabled`); full per-key quota tiers are post-MVP (Q3). |

### TO RESOLVE 🤔

> All Q1–Q7 resolved (July 2026) — answers folded into the design decisions + slices below~ ✅

- [x] **Q1 Auth scope for MVP — bearer-validation only, or a full identity system?**
  - **RESOLVED:** MVP ships **API-key auth + JWT bearer validation** of externally-issued tokens + `[Authorize]` policies, with caller identity flowing to execution audit (D9). A **local dev/test mode runs with no auth required** (`Api:Auth:Require=false` default). Full first-party login/refresh/user-store + RBAC → **2.7.P1**.
- [x] **Q2 Prometheus metrics in MVP?**
  - **RESOLVED:** MVP ships a JSON `/status` + `/metrics` counter seam behind a **generic, exporter-agnostic `IWorkflowMetrics` seam** (D11) so any exporter can plug in later. Prometheus text-format exporter → **2.7.P2**.
- [x] **Q3 Rate limiting in MVP?**
  - **RESOLVED:** In-box .NET 8 fixed-window limiter (D13), **keyed by API key / caller id**, **disabled unless `Api:RateLimit:Enabled`**. Per-key quota tiers + sliding window → **2.7.P3**.
- [x] **Q4 Module upload / enable / disable endpoints?**
  - **RESOLVED:** MVP module endpoints are **read-only** (`GET /modules`, `GET /modules/{id}`). Upload/install/enable/disable are implemented **in Phase 2.8** as part of the `.wfmod` package work (not a 2.7 post-MVP slice) — see the new "Module management HTTP endpoints" task added to [§2.8](Phase2-CoreFeatures.md#28-module-system-enhancements-deferred-from-phase-14-).
- [x] **Q5 Sync execution (`/execute/sync`) in MVP?**
  - **RESOLVED:** Yes — start-then-await-terminal-or-timeout over `GetWorkflowStatus`. On timeout, return **`202` with a continuation poll URL** (`Location: /api/v1/executions/{id}`) so the caller can keep checking; `200` + final status when it completes in time.
- [x] **Q6 Execute-by-name version selection.**
  - **RESOLVED:** Resolve by exact name → **newest active `Version`**, with an optional **`?version=` pin**. Add a small additive `IWorkflowRepository.GetByNameAsync(name, version?)` helper (or filter over `GetAllAsync`) — decided during 2.7.2.
- [x] **Q7 `/api/v1` aliases for existing feature endpoints?**
  - **RESOLVED:** No aliases in MVP — feature tooling (`/api/webhooks`, `/api/database/*`, `/api/transform/script/*`) stays at current paths (D4). Optional aliases → **2.7.P5**.

---

## Pre-Existing Work (from earlier phases) ✅

| Component | File | Status |
|-----------|------|--------|
| Minimal-API endpoint-group convention | `Workflow.Api/Webhooks/WebhookEndpoints.cs`, `Workflow.Api/Database/DatabaseConnectionEndpoints.cs` | ✅ Pattern reference for all 2.7 endpoint files (D1) |
| `IWorkflowRepository` — full CRUD + soft-delete/purge/restore/paged/search | `Workflow.Persistence/Abstractions/IWorkflowRepository.cs` | ✅ Backs 2.7.1 verbatim (D2) |
| `IExecutionHistoryRepository` — execution + node records, paged | `Workflow.Persistence/Abstractions/IExecutionHistoryRepository.cs` | ✅ Backs 2.7.2 status/list |
| `IVariableStore` — scoped versioned variables + history | `Workflow.Persistence/Abstractions/IVariableStore.cs` | ✅ Backs 2.7.4 verbatim |
| Execution messages: `CreateWorkflowInstance` + `ExecutionStartOptions(CallerId, VariableWriteMode)`, `GetWorkflowStatus`/`WorkflowStatusResponse`, `CancelExecution`, `StartExecution` | `Workflow.Engine/Messages/WorkflowMessages.cs` | ✅ The engine surface 2.7.2 wraps (D2) |
| `WorkflowSupervisor` handles Create/Status/Cancel | `Workflow.Engine/Actors/WorkflowSupervisor.cs` | ✅ No engine changes needed |
| `WorkflowSupervisorActorRef` + `ActorWorkflowLauncher` (Ask+timeout pattern) | `Workflow.Api/Webhooks/ActorWorkflowLauncher.cs` | ✅ Pattern reference for `IWorkflowExecutionService` (D3) |
| `IModuleRegistry` + `InMemoryModuleRegistry` (full API) | `Workflow.Modules/Abstractions/IModuleRegistry.cs` | ✅ Needs DI registration in the host (D5 — the one gap) |
| `BuiltinModules.GetAll()` | `Workflow.Modules/Builtin/BuiltinModuleRegistration.cs` | ✅ Seed source for the registry |
| `ModuleSchema`/`PortDefinition`/`ModulePropertyDefinition`/`PropertyEditorType` | `Workflow.Core/Models/ModuleSchema.cs` | ✅ Source shapes for the DTO layer (D6) |
| `WorkflowDefinition` + `ExecutionState`/`NodeExecutionState` + `ValidationResult` | `Workflow.Core/Models/*` | ✅ Source shapes for DTOs |
| `PagedResult`/`Pagination`/`WorkflowFilter`/`ExecutionFilter` | `Workflow.Persistence/Models/*` | ✅ Pagination/filtering reused by list endpoints |
| `ModuleAwareWorkflowValidator` | `Workflow.Modules/Validation/ModuleAwareWorkflowValidator.cs` | ✅ Backs POST/PUT workflow validation → 422 (D8) |
| `ResolveCallerId(HttpContext)` static | `Workflow.Api/Program.cs` | ✅ Extracted + reused by execute endpoints (D7) |
| `IPersistenceProvider.HealthCheckAsync` → `HealthCheckResult` | `Workflow.Persistence/Abstractions/IPersistenceProvider.cs` | ✅ Backs 2.7.5 health (D10) |
| Swagger bootstrap (`AddSwaggerGen` + `UseSwagger/UseSwaggerUI`) | `Workflow.Api/Program.cs` | ✅ Enriched in 2.7.8 (D12) |
| Data Protection registered | `Workflow.Api/Program.cs` | ✅ Reused to hash API keys at rest (D9) |
| Webhook endpoints (`/api/webhooks` register/list/get/delete + trigger) | `Workflow.Api/Webhooks/WebhookEndpoints.cs` | ✅ **The checklist's "webhook endpoints" are already DONE** (2.3.6/2.3.9) — 2.7.6 reconciles, doesn't rebuild |
| `public partial class Program` for `WebApplicationFactory<Program>` | `Workflow.Api/ProgramPublic.cs` | ✅ API tests already use it |
| API test convention (`WebApplicationFactory<Program>`, `IClassFixture`, in-memory default) | `Workflow.Tests/Api/DatabaseConnectionsApiTests.cs` | ✅ Pattern reference for all 2.7 tests |

> **CopilotNote:** The single biggest surprise for anyone implementing this: **the hard parts are already built.** Workflow CRUD, execution, variables, status, cancel, health, and webhooks all have complete service/repository/message layers — 2.7 is mostly `MapGet`/`MapPost` glue + DTO projection. Budget your risk on **auth + versioning + the execution service**, not on the resource endpoints~ 💖

---

## 2.7.0 API Foundation 🛠️ (versioning, DTOs base, error handling, module-registry DI)

> **Purpose:** Land the cross-cutting scaffolding every later slice consumes — API versioning, ProblemDetails, the DTO/contracts project layout, caller-identity extraction, and the missing `IModuleRegistry` registration~ ✨

**Complexity:** 🟡 Low-Medium *(no business logic, but D4/D5/D6/D8 conventions get baked in here)*

### Tasks

- [ ] **Folder layout in `Workflow.Api`** 🌷
  - [ ] `V1/` — the resource endpoint-group files (filled by 2.7.1–5)
  - [ ] `Contracts/` — serializable DTOs + projection extensions (D6)
  - [ ] `Execution/` — `IWorkflowExecutionService` + impl (2.7.2)
  - [ ] `Auth/` — caller identity + auth handlers (2.7.7)
  - [ ] `Observability/` — metrics counter seam (2.7.5)
- [ ] **API versioning** 🔢
  - [ ] Add `Asp.Versioning.Http` to `Directory.Packages.props` + `Workflow.Api.csproj`
  - [ ] URL-segment versioning (`/api/v{version:apiVersion}/…`), default `1.0`, report supported versions in `api-supported-versions` header
  - [ ] A shared `MapV1Group(this IEndpointRouteBuilder)` helper returning the `/api/v1` route group
- [ ] **ProblemDetails + error handling (D8)** 🚨
  - [ ] `builder.Services.AddProblemDetails()`; a shared `Results` helper (`NotFoundProblem`/`ValidationProblem422`/`ConflictProblem`) in `V1/ApiResults.cs`
  - [ ] Global exception → 500 ProblemDetails mapping (dev shows detail; prod redacts)
- [ ] **Contracts base + JSON options** 📐
  - [ ] `Contracts/JsonTypeHelpers.cs` — `Type` ↔ assembly-qualified string, `Version` ↔ string, `Option<T>` → nullable, `Arr`/`HashMap` → arrays/objects
  - [ ] `Pagination` binding helper (`?page=`/`?pageSize=` → `Pagination`; caps pageSize)
- [ ] **Caller identity extraction (D7)** 🪪
  - [ ] Move `ResolveCallerId` from `Program.cs` → `Auth/CallerIdentity.cs` (`this HttpContext` extension); `Program.cs` weatherforecast + webhook paths keep working via the extension
- [ ] **Register `IModuleRegistry` (D5)** 📦
  - [ ] `builder.Services.AddSingleton<IModuleRegistry>(sp => { var r = new InMemoryModuleRegistry(); foreach (var m in BuiltinModules.GetAll()) r.RegisterModule(m); foreach (var m in sp.GetServices<IWorkflowModule>()) r.RegisterModule(m, allowOverwrite: true); return r; })` — seeds builtins + host-wired families (database/cloud/transform-script)
  - [ ] Guard against duplicate ids (builtins vs. DI-registered) via `allowOverwrite`

### Tests (target ~8): → `Workflow.Tests/Api/V1/ApiFoundationTests.cs`

- [ ] `Versioning_UnknownVersion_Returns400` / `Versioning_V1Route_Resolves`
- [ ] `ProblemDetails_NotFound_ReturnsRfc7807Shape`
- [ ] `ProblemDetails_ValidationFailure_Returns422WithErrorsMap`
- [ ] `JsonTypeHelpers_TypeRoundTrips` / `JsonTypeHelpers_VersionRoundTrips` / `JsonTypeHelpers_OptionToNullable`
- [ ] `ModuleRegistry_Registered_ContainsBuiltinsAndHostFamilies` *(asserts e.g. `builtin.http.request` + `builtin.transform.map` both resolvable)*
- [ ] `Pagination_Binding_CapsPageSize`

---

## 2.7.1 Workflow CRUD Endpoints 📋 (`/api/v1/workflows`)

> **Purpose:** Full definition lifecycle over `IWorkflowRepository` — thin adapters, DTO projection, validation on write (D2)~ ✨

**Complexity:** 🟡 Low

### Tasks

- [ ] **`V1/WorkflowEndpoints.cs`** (`MapWorkflowEndpoints`, group `/api/v1/workflows`, tag `Workflows`)
  - [ ] `GET /` — `IWorkflowRepository.GetAllAsync(WorkflowFilter, Pagination)` → `PagedResult<WorkflowSummaryDto>`; query params: `?name=`/`?tag=`/`?page=`/`?pageSize=`/`?sort=`
  - [ ] `GET /{id:guid}` — `GetByIdAsync` → `WorkflowDetailsDto` (full definition); 404 if missing/soft-deleted
  - [ ] `POST /` — validate via `ModuleAwareWorkflowValidator` (422 on failure) → `CreateAsync` → `201 Created` + `Location` + `WorkflowDetailsDto`
  - [ ] `PUT /{id:guid}` — validate → `UpdateAsync`; 404 if missing; **409 on version conflict** (optimistic: incoming `Version`/`UpdatedAt` vs stored)
  - [ ] `DELETE /{id:guid}` — `DeleteAsync` (soft); `?purge=true` → `PurgeAsync` (guarded: refuse purge if active executions exist per `IExecutionHistoryRepository`); `204 No Content`
  - [ ] `POST /{id:guid}/restore` — `RestoreAsync` *(bonus — the repo supports it; exposes soft-delete recovery)*
- [ ] **Contracts:** `WorkflowSummaryDto` (id/name/description/version/tags/updatedAt/nodeCount), `WorkflowDetailsDto` (+ nodes/connections/variables/trigger), `CreateWorkflowRequest`/`UpdateWorkflowRequest`; projections `WorkflowDefinition ↔ Dto`
- [ ] **Wire** `app.MapWorkflowEndpoints()` in `Program.cs` (guarded: only when a persistence provider is configured, else the endpoints return `503`/friendly message — mirrors how repos are conditionally registered)

### Tests (target ~12): → `Workflow.Tests/Api/V1/WorkflowEndpointsTests.cs` *(WebApplicationFactory + in-memory persistence provider via config override)*

- [ ] `List_Empty_ReturnsEmptyPage` · `Create_Then_Get_RoundTrips` · `Create_InvalidDefinition_Returns422`
- [ ] `Get_UnknownId_Returns404` · `List_Pagination_Works` · `List_FilterByName_Works` · `List_FilterByTag_Works`
- [ ] `Update_Existing_Succeeds` · `Update_Unknown_Returns404` · `Update_VersionConflict_Returns409`
- [ ] `Delete_SoftDeletes_ThenGet404` · `Delete_Purge_WithActiveExecutions_Refused` · `Restore_SoftDeleted_Works`

---

## 2.7.2 Execution Endpoints ⚡ (`/api/v1/workflows/{id}/execute`, `/api/v1/executions`)

> **Purpose:** Start / status / cancel / list executions over the Akka supervisor + `IExecutionHistoryRepository`, via a new general `IWorkflowExecutionService` (D3)~ ✨

**Complexity:** 🟠 Medium *(the execution service + sync-wait are the substantive bits)*

### Tasks

- [ ] **`Execution/IWorkflowExecutionService.cs` + `ActorWorkflowExecutionService.cs`** 🚀
  - [ ] `StartAsync(Guid definitionId, IReadOnlyDictionary<string,object?> inputs, ExecutionStartOptions options, ct) → Guid executionId` — load definition (`IWorkflowRepository`), `Ask<IWorkflowMessage>(new CreateWorkflowInstance(id, def, inputs, options))` (reuse `ActorWorkflowLauncher`'s linked-CTS+timeout pattern), unpack `WorkflowInstanceCreated`/`WorkflowInstanceCreationFailed`
  - [ ] `GetStatusAsync(Guid executionId, ct) → WorkflowStatusResponse?` — `Ask<WorkflowStatusResponse>(new GetWorkflowStatus(id))` with a fallback to `IExecutionHistoryRepository.GetExecutionAsync` for terminal/persisted executions the supervisor no longer tracks
  - [ ] `CancelAsync(Guid executionId, ct) → bool` — `Tell/Ask(new CancelExecution(id))`
  - [ ] `StartAndWaitAsync(…, TimeSpan timeout, ct)` — start then await terminal state (Q5): poll `GetStatusAsync` with backoff (or a completion signal) until `Completed`/`Failed`/`Cancelled` or timeout; on timeout the caller gets the execution id back so the endpoint can hand out a continuation poll URL
  - [ ] Registered singleton in `Program.cs`
- [ ] **`V1/ExecutionEndpoints.cs`** (`MapExecutionEndpoints`)
  - [ ] `POST /api/v1/workflows/{id:guid}/execute` — body = inputs; resolve `CallerId` (D7) + optional `variableWriteMode` → `ExecutionStartOptions`; `202 Accepted` + `{ executionId }` + `Location: /api/v1/executions/{id}`
  - [ ] `POST /api/v1/workflows/execute/{name}` — resolve definition by name (Q6: newest active, optional `?version=`) → start
  - [ ] `POST /api/v1/workflows/{id:guid}/execute/sync?timeoutSeconds=` — `StartAndWaitAsync` → `200` + final `ExecutionStatusDto` when it completes in time; **on timeout → `202` with a continuation poll URL** (`Location: /api/v1/executions/{id}` + `{ executionId, status: "running" }`) so the caller keeps polling (Q5)
  - [ ] `GET /api/v1/executions/{executionId:guid}` — `GetStatusAsync` → `ExecutionStatusDto` (state, progress, per-node states, start/end, error, outputs when complete); 404 if unknown
  - [ ] `POST /api/v1/executions/{executionId:guid}/cancel` — `CancelAsync` → `{ cancelled }`; 404 if unknown
  - [ ] `GET /api/v1/executions?workflowId=&status=&from=&to=&page=&pageSize=` — `IExecutionHistoryRepository.GetExecutionsForWorkflowAsync(...)` → `PagedResult<ExecutionDto>`
- [ ] **Contracts:** `StartExecutionRequest` (inputs + optional writeMode), `ExecutionStartedDto` (executionId), `ExecutionStatusDto` (+ node states via `NodeExecutionState` → string), `ExecutionDto` (list row); projection from `WorkflowStatusResponse`/`ExecutionRecord`

### Tests (target ~15): → `Workflow.Tests/Api/V1/ExecutionEndpointsTests.cs`

- [ ] `StartAsync_PersistsTriggeredBy_FromCallerIdentity` *(X-Caller-Id → `ExecutionStartOptions.CallerId` → execution audit)*
- [ ] `Start_ReturnsAcceptedWithExecutionId_AndLocation`
- [ ] `Start_UnknownWorkflow_Returns404`
- [ ] `VariableWriteMode_MapsToScope` *(execution vs workflow vs dual — asserts the option flows to `CreateWorkflowInstance`)*
- [ ] `ExecuteByName_ResolvesNewestActive` / `ExecuteByName_WithVersion_Pins` *(Q6)*
- [ ] `Status_RunningExecution_ReturnsProgressAndNodeStates`
- [ ] `Status_TerminalExecution_FallsBackToHistoryRepo`
- [ ] `Status_UnknownExecution_Returns404`
- [ ] `Cancel_RunningExecution_ReturnsCancelled` · `Cancel_UnknownExecution_Returns404`
- [ ] `Sync_CompletesWithinTimeout_ReturnsResult` · `Sync_ExceedsTimeout_Returns202WithContinuationUrl` *(Q5 — `Location` header points at `/api/v1/executions/{id}`)*
- [ ] `ListExecutions_FilterByWorkflowAndStatus_Paginated`
- [ ] *(engine-decoupled)* execution-service unit tests with a `TestKit`/stub supervisor for start/status/cancel unpacking

---

## 2.7.3 Module Endpoints 📦 (`/api/v1/modules`)

> **Purpose:** Read-only module discovery over the DI-registered `IModuleRegistry` (D5) + the serializable schema DTO layer (D6)~ ✨

**Complexity:** 🟡 Low

### Tasks

- [ ] **Module DTO layer (`Contracts/Modules/*`)** 📐
  - [ ] `PortDefinitionDto` (Name/DisplayName/DataType-as-string/Description/IsRequired/DefaultValue-as-JsonElement)
  - [ ] `ModulePropertyDefinitionDto` (+ EditorType-as-string/AllowedValues/DefaultValue) — mirrors `ModulePropertyDefinition`
  - [ ] `ModuleSchemaDto` (Inputs/Outputs/Properties arrays)
  - [ ] `ModuleSummaryDto` (id/displayName/category/description/icon/version — no schema) · `ModuleDetailsDto` (+ schema/dependencies)
  - [ ] Projection `IWorkflowModule → ModuleDetailsDto` / `→ ModuleSummaryDto` (using `JsonTypeHelpers` from 2.7.0)
- [ ] **`V1/ModuleEndpoints.cs`** (`MapModuleEndpoints`)
  - [ ] `GET /api/v1/modules` — `IModuleRegistry.GetAllModules()` → `ModuleSummaryDto[]`; `?category=` → `GetModulesByCategory`; `?q=` → `SearchModules`; optional `?groupByCategory=true` → `{ category: [summaries] }`
  - [ ] `GET /api/v1/modules/{moduleId}` — `GetModule` → `ModuleDetailsDto`; 404 if unknown
  - [ ] *(upload/install/enable/disable — implemented in **Phase 2.8** alongside the `.wfmod` package format, per Q4; the MVP module endpoints are read-only and document that management operations arrive with 2.8)*

### Tests (target ~9): → `Workflow.Tests/Api/V1/ModuleEndpointsTests.cs` + `Workflow.Tests/Api/Contracts/ModuleDtoProjectionTests.cs`

- [ ] `List_ReturnsAllRegisteredModules` · `List_FilterByCategory` · `List_Search` · `List_GroupByCategory`
- [ ] `Get_KnownModule_ReturnsSchema` · `Get_UnknownModule_Returns404`
- [ ] `Projection_MapsPortsAndProperties` · `Projection_TypeAndVersion_Serialize` · `Dto_RoundTripsThroughJson`

---

## 2.7.4 Variable Endpoints 🔧 (`/api/v1/variables`)

> **Purpose:** Scoped, versioned variable management over `IVariableStore` (D2)~ ✨

**Complexity:** 🟡 Low

### Tasks

- [ ] **`V1/VariableEndpoints.cs`** (`MapVariableEndpoints`)
  - [ ] `GET /api/v1/variables?scope=global|workflow|execution&scopeId=` — `GetAllVariablesAsync(scope)` → `{ name: value }` (or paged `VariableDto[]`)
  - [ ] `GET /api/v1/variables/{name}?scope=&scopeId=&version=` — `GetVariableAsync` → `VariableDto` (value + version); 404 if not present (distinct from null-valued-but-present, per the store's documented semantics)
  - [ ] `PUT /api/v1/variables/{name}` — body = `{ scope, scopeId, value }` → `SetVariableAsync` (new version) → `VariableDto` with new version
  - [ ] `DELETE /api/v1/variables/{name}?scope=&scopeId=` — `DeleteVariableAsync` → `204`; 404 if absent
  - [ ] `GET /api/v1/variables/{name}/history?scope=&scopeId=` — `GetVariableHistoryAsync` → `VariableDto[]` (all versions)
- [ ] **Contracts:** `VariableDto` (name/value/version/scope/updatedAt), `SetVariableRequest`; `VariableScope` binding from `?scope=` + `?scopeId=`

### Tests (target ~9): → `Workflow.Tests/Api/V1/VariableEndpointsTests.cs`

- [ ] `Set_Then_Get_RoundTrips` · `Set_NullValue_PersistsAsPresentNull` *(store's null semantics)*
- [ ] `Get_Unknown_Returns404` · `Get_SpecificVersion_Works`
- [ ] `Set_Twice_IncrementsVersion` · `History_ReturnsAllVersions`
- [ ] `Delete_RemovesVariableAndHistory` · `Delete_Unknown_Returns404`
- [ ] `List_ByScope_ReturnsScopedVariables`

---

## 2.7.5 Monitoring Endpoints 📊 (`/api/v1/health`, `/status`, `/metrics`)

> **Purpose:** Health/readiness/liveness over `IPersistenceProvider.HealthCheckAsync` + actor liveness (D10), plus a JSON status/metrics counter seam (D11)~ ✨

**Complexity:** 🟡 Low-Medium

### Tasks

- [ ] **Health checks (D10)** ❤️
  - [ ] `AddHealthChecks()` + a `PersistenceHealthCheck` (wraps `IPersistenceProvider.HealthCheckAsync`) + an `ActorSystemHealthCheck` (supervisor `Ask` liveness with a short timeout)
  - [ ] `GET /api/v1/health` — detailed component report (`200` healthy / `503` unhealthy)
  - [ ] `GET /api/v1/health/ready` — readiness (all deps initialized) · `GET /api/v1/health/live` — liveness (process/actor-system up)
- [ ] **Metrics seam (D11)** 📈
  - [ ] `Observability/IWorkflowMetrics.cs` + `InMemoryWorkflowMetrics` — counters: executions started/completed/failed, active gauge (incremented by the execution service in 2.7.2)
  - [ ] `GET /api/v1/status` — JSON overview: provider name + health, registered module count, active executions, uptime, version
  - [ ] `GET /api/v1/metrics` — JSON counter dump (Prometheus text exporter → 2.7.P2 per Q2)
- [ ] **Wire** the execution service (2.7.2) to increment the metrics counters on start/terminal transitions

### Tests (target ~8): → `Workflow.Tests/Api/V1/MonitoringEndpointsTests.cs`

- [ ] `Health_NoProvider_ReturnsDegradedOr503` · `Health_WithProvider_Returns200`
- [ ] `Ready_ReturnsReadyWhenInitialized` · `Live_AlwaysReturnsLiveWhenProcessUp`
- [ ] `Status_ReturnsProviderModuleCountAndUptime`
- [ ] `Metrics_ReflectsExecutionCounters` *(start an execution → started counter increments)*
- [ ] `Health_ProviderUnhealthy_Returns503` *(stubbed failing provider)*
- [ ] `ModuleCount_MatchesRegistry`

---

## 2.7.6 Webhook Endpoint Reconciliation 🪝 (`/api/webhooks` — mostly DONE)

> **Purpose:** The checklist's "webhook endpoints" already shipped in 2.3.6/2.3.9 (`/api/webhooks` register/list/get/delete + trigger). This slice **reconciles + documents**, it does not rebuild~ ✨

**Complexity:** 🟢 Low

### Tasks

- [ ] **Audit the existing `WebhookEndpoints.cs`** against the checklist (register / list / get / delete / trigger + signature validation) — confirm parity; note that signature validation lives in the webhook trigger path (`WebhookSignatureTests` already cover it)
- [ ] **Document** the path decision (D4/Q7): webhook management stays at `/api/webhooks` (feature tooling, not a versioned resource); add a short note in the API docs + a cross-link
- [ ] **Gap-fill only if found:** ensure list/get expose the DTO shape consistent with the 2.7 contracts style; add any missing test for register→trigger→unregister round-trip *(most already exist in `WebhookApiTests`)*
- [ ] *(optional, Q7)* `/api/v1/webhooks` alias → **2.7.P5** if a consumer needs it

### Tests (target ~2 net-new): → extend `Workflow.Tests/Api/WebhookApiTests.cs`

- [ ] `Webhook_RegisterListDelete_ContractShape_MatchesV1Style` *(only if a shape gap is found)*
- [ ] `Webhook_Docs_PathDocumented` *(doc-presence assertion or skip)*

---

## 2.7.7 Authentication 🔐 (API Key + JWT bearer validation)

> **Purpose:** The MVP auth surface (D9) — an API-key scheme + JWT bearer validation, `[Authorize]`-able endpoints, caller identity flowing into execution audit (D7). Full login/refresh/user-store/RBAC is 2.7.P1 (Q1)~ ✨

**Complexity:** 🟠 Medium

### Tasks

- [ ] **API-key scheme** 🔑
  - [ ] `Auth/ApiKeyAuthenticationHandler.cs` (`AuthenticationHandler<ApiKeyOptions>`) — reads `X-API-Key`, validates against config-declared keys (`Api:Auth:ApiKeys` — each `{ key(hashed), callerId, roles[] }`), builds a `ClaimsPrincipal` (`NameIdentifier=callerId` + role claims)
  - [ ] Keys **hashed at rest** via the existing Data-Protection seam (raw keys never stored); a dev helper to hash a key
  - [ ] `CallerIdentity` (2.7.0) now resolves the authenticated `callerId` claim first, `X-Caller-Id` only when unauthenticated/dev
- [ ] **JWT bearer scheme** 🎫
  - [ ] `AddAuthentication().AddJwtBearer(...)` validating externally-issued tokens — configurable `Authority`/`Audience`/`IssuerSigningKey` (`Api:Auth:Jwt:*`); extracts `sub`/`NameIdentifier` + roles
  - [ ] Both schemes composed under a default policy; `Api:Auth:Require=true` enforces auth globally, else endpoints are anonymous-friendly for dev
- [ ] **Authorization scaffolding** 🛡️
  - [ ] Named policies (`WorkflowRead`/`WorkflowWrite`/`WorkflowExecute`/`Admin`) mapped to roles; applied via `.RequireAuthorization("…")` on endpoint groups (no-op when auth disabled)
  - [ ] *(full role/permission matrix + `/auth/login`+`/auth/refresh` → 2.7.P1)*
- [ ] **Config + Program wiring** — register schemes/policies; `UseAuthentication()`/`UseAuthorization()` in the pipeline

### Tests (target ~11): → `Workflow.Tests/Api/Auth/AuthTests.cs`

- [ ] `ApiKey_ValidKey_Authenticates` · `ApiKey_InvalidKey_401` · `ApiKey_MissingKey_AnonymousWhenNotRequired` · `ApiKey_MissingKey_401WhenRequired`
- [ ] `ApiKey_CallerId_FlowsToExecutionAudit` *(authenticated call → execution `TriggeredBy` = key's callerId)*
- [ ] `Jwt_ValidToken_Authenticates` · `Jwt_ExpiredToken_401` · `Jwt_WrongAudience_401`
- [ ] `Policy_WorkflowWrite_DeniesViewerRole_403` · `Policy_Admin_AllowsAdminRole`
- [ ] `AuthDisabled_Dev_AllEndpointsAnonymous`

---

## 2.7.8 OpenAPI Enrichment + E2E + Docs 📚

> **Purpose:** Make the surface discoverable and documented — Swagger auth schemes, XML docs, examples, an end-to-end smoke, and the API guide (D12)~ ✨

**Complexity:** 🟡 Low

### Tasks

- [ ] **Swagger enrichment (D12)** 📖
  - [ ] `<GenerateDocumentationFile>true</GenerateDocumentationFile>` + `IncludeXmlComments`
  - [ ] Register both security schemes (ApiKey header + Bearer) so the "Authorize" button works; per-resource tags; request/response examples for the core flows
  - [ ] Grouped, versioned document (`v1`)
- [ ] **API rate-limiting seam (D13/Q3)** 🚦
  - [ ] `AddRateLimiter()` fixed-window keyed by API key/caller id; `429` + `Retry-After`; **disabled unless `Api:RateLimit:Enabled`**
- [ ] **E2E smoke test** — create workflow → execute → poll status to terminal → read execution → read a variable → list modules → health `200` (all through HTTP, in-memory persistence)
- [ ] **`docs/rest-api.md`** — endpoint reference (versioned resources vs. feature tooling), auth setup (API key + JWT config), pagination/filtering conventions, ProblemDetails shape, execution lifecycle, curl examples
- [ ] **Housekeeping** — `DOCUMENTATION_INDEX.md` + `phases/README.md` + `Phase2-CoreFeatures.md` §2.7 completion summary; `README.md` note

### Tests (target ~4): → `Workflow.Tests/Api/V1/ApiE2ETests.cs` + `SwaggerTests.cs`

- [ ] `E2E_CreateExecutePollReadVariable_Works`
- [ ] `Swagger_GeneratesV1Document` · `Swagger_IncludesSecuritySchemes`
- [ ] `RateLimit_WhenEnabled_Returns429OverLimit` *(enabled via test config)*

---

## Post-MVP Slices 🚧 *(deferred — not blocking Phase 3+)*

### 2.7.P1 First-Party Identity: login / refresh / user store + full RBAC 🔐 *(post-MVP — Q1)*
`POST /api/v1/auth/{login,refresh,logout}`, a user store (password hashing via `IPasswordHasher`), refresh-token rotation, and a full role/permission matrix (Admin/Developer/Viewer + `WorkflowCreate`/`WorkflowExecute`/… permissions). ~18 tests. Only if product wants a bespoke IdP rather than fronting with Entra/Auth0/Keycloak.

### 2.7.P2 Prometheus Metrics Exporter 📈 *(post-MVP — Q2)*
`prometheus-net` text-format `/metrics`; the `IWorkflowMetrics` seam is already exporter-agnostic. ~4 tests.

### 2.7.P3 Rate-Limit Quota Tiers 🚦 *(post-MVP — Q3)*
Per-key quotas, sliding window, tier config, `X-RateLimit-*` headers. ~6 tests.

### 2.7.P4 Module Upload / Enable / Disable 📦 *(handled in Phase 2.8, per Q4)*
`POST /api/v1/modules/upload` (`.wfmod`), install into a plugin ALC (`AssemblyModuleLoader`/`PluginAssemblyLoadContext`), enable/disable toggles. **Not a 2.7 slice** — these HTTP endpoints are implemented **in Phase 2.8** as part of the `.wfmod` package-format work (a "Module management HTTP endpoints" task was added to §2.8). Listed here only as a pointer. ~10 tests (in 2.8).

### 2.7.P5 `/api/v1` Aliases for Feature Endpoints 🔀 *(post-MVP — Q7)*
Optional versioned aliases for `/api/webhooks`, `/api/database/*`, `/api/transform/script/*` if a consumer standardises on `/api/v1`. ~4 tests.

---

## Phase 2.7 Deliverables ✅

When 2.7 ships (Week 20), all of the following must be true:

**Core resource + monitoring surface (Week 19 gate):**

- [ ] **Workflow CRUD** — `GET/POST/PUT/DELETE /api/v1/workflows` (+ restore) over `IWorkflowRepository`, validated on write (422), paginated/filterable list
- [ ] **Execution** — start (async/by-name/sync), status, cancel, list over `IWorkflowExecutionService` + `IExecutionHistoryRepository`; caller identity → execution audit; `VariableWriteMode` honoured
- [ ] **Modules** — `GET /api/v1/modules[/{id}]` over the DI-registered `IModuleRegistry` + serializable schema DTOs
- [ ] **Variables** — `GET/PUT/DELETE /api/v1/variables[/{name}][/history]` over `IVariableStore` with scope + version semantics
- [ ] **Monitoring** — `/api/v1/health{,/ready,/live}` + `/status` + `/metrics` (JSON) over `HealthCheckAsync` + the metrics seam
- [ ] **Webhooks** — reconciled/documented (already shipped in 2.3.6/2.3.9)
- [ ] **~61 unit/integration tests** (8 foundation + 12 workflow + 15 execution + 9 module + 9 variable + 8 monitoring) — all Docker-free via `WebApplicationFactory<Program>`

**Auth + polish (Week 20 gate):**

- [ ] **Auth** — API-key + JWT bearer schemes, `[Authorize]` policies, `Api:Auth:Require` toggle (**no-auth by default for local dev/test**); caller id flows to execution audit
- [ ] **Versioning** — `/api/v1` URL-segment versioning, supported-versions header
- [ ] **OpenAPI** — enriched Swagger (auth schemes, XML docs, examples), `v1` document; ProblemDetails (RFC 7807) everywhere
- [ ] **Rate limiting** — in-box fixed-window seam, disabled by default
- [ ] **~17 tests** (11 auth + 4 OpenAPI/E2E + 2 webhook reconcile)

**Cross-cutting:**

- [ ] **docs/rest-api.md** published + indexed
- [ ] **0 errors, 0 new warnings** in `dotnet build`; full unit suite green
- [ ] **Q1–Q7 resolved** and recorded in the Resolved Questions Reference
- [ ] **README + phases/README.md + Phase2-CoreFeatures.md §2.7** updated

**New / Modified Files (planned):**
```
Workflow.Api/
  V1/
    ApiVersioning.cs                     ← new (2.7.0) — MapV1Group + versioning config
    ApiResults.cs                        ← new (2.7.0) — ProblemDetails helpers
    ApiFoundation.cs                     ← new (2.7.0) — pagination binding, module-registry seed
    WorkflowEndpoints.cs                 ← new (2.7.1)
    ExecutionEndpoints.cs                ← new (2.7.2)
    ModuleEndpoints.cs                   ← new (2.7.3)
    VariableEndpoints.cs                 ← new (2.7.4)
    MonitoringEndpoints.cs               ← new (2.7.5)
  Contracts/
    JsonTypeHelpers.cs                   ← new (2.7.0)
    WorkflowContracts.cs                 ← new (2.7.1) — Summary/Details/Create/Update DTOs
    ExecutionContracts.cs                ← new (2.7.2)
    Modules/ModuleContracts.cs           ← new (2.7.3) — Schema/Port/Property/Summary/Details DTOs
    VariableContracts.cs                 ← new (2.7.4)
  Execution/
    IWorkflowExecutionService.cs         ← new (2.7.2)
    ActorWorkflowExecutionService.cs     ← new (2.7.2)
  Observability/
    IWorkflowMetrics.cs                  ← new (2.7.5)
    InMemoryWorkflowMetrics.cs           ← new (2.7.5)
    PersistenceHealthCheck.cs            ← new (2.7.5)
    ActorSystemHealthCheck.cs            ← new (2.7.5)
  Auth/
    CallerIdentity.cs                    ← new (2.7.0) — extracted from Program.cs (D7)
    ApiKeyAuthenticationHandler.cs       ← new (2.7.7)
    ApiKeyOptions.cs                     ← new (2.7.7)
    AuthorizationPolicies.cs             ← new (2.7.7)
  Program.cs                             ← modified — versioning, ProblemDetails, health, auth,
                                           rate-limiter, IModuleRegistry, execution service, Map*Endpoints
  ProgramPublic.cs                       ← unchanged (already exposes Program)

Workflow.Tests/Api/
  V1/{ApiFoundation,Workflow,Execution,Module,Variable,Monitoring,ApiE2E}EndpointsTests.cs
  Auth/AuthTests.cs
  Contracts/ModuleDtoProjectionTests.cs
  SwaggerTests.cs

docs/rest-api.md                         ← new (2.7.8)
Directory.Packages.props                 ← + Asp.Versioning.Http; (Prometheus.NET → 2.7.P2)
```

---

## Resolved Questions Reference 📋

| # | Question | Resolution | Tracked in |
|---|----------|------------|------------|
| **Q1** | Auth scope for MVP? | **API-key + JWT bearer validation + `[Authorize]` policies; caller id → execution audit; local dev/test runs no-auth (`Api:Auth:Require=false`).** Full login/refresh/RBAC → 2.7.P1 | 2.7.7 |
| **Q2** | Prometheus metrics in MVP? | **JSON `/status`+`/metrics` behind a generic, exporter-agnostic `IWorkflowMetrics` seam.** Prometheus exporter → 2.7.P2 | 2.7.5 |
| **Q3** | Rate limiting in MVP? | **In-box fixed-window limiter keyed by API key/caller id, disabled unless `Api:RateLimit:Enabled`.** Tiers + sliding window → 2.7.P3 | 2.7.8 |
| **Q4** | Module upload/enable/disable? | **Read-only in 2.7; upload/enable/disable implemented in Phase 2.8** alongside the `.wfmod` format (endpoint task added to §2.8) | 2.7.3 + §2.8 |
| **Q5** | Sync execution endpoint in MVP? | **Yes — start-then-await-terminal-or-timeout; `200` + final status when done, `202` + continuation poll URL (`Location`) on timeout** | 2.7.2 |
| **Q6** | Execute-by-name version selection? | **Newest active by exact name, optional `?version=` pin** (additive `GetByNameAsync` helper) | 2.7.2 |
| **Q7** | `/api/v1` aliases for feature endpoints? | **No in MVP** — feature tooling stays at current paths; aliases → 2.7.P5 | 2.7.6 |

---

> 🌸 *uwu — Phase 2.7 is mostly a victory lap over work already done, senpai~! The repos, the execution messages, the supervisor, health checks, and webhooks all exist — 2.7 is `MapGet`/`MapPost` glue + DTO projection + the two genuinely-new bits (auth & versioning). Q1–Q7 are all resolved: API-key + JWT (no-auth for local dev), generic metrics seam, in-box rate-limit (off by default), sync-with-continuation-URL, execute-by-name+version, and module management pushed into Phase 2.8 with the `.wfmod` format. Land 2.7.0's scaffolding, register that missing `IModuleRegistry`, and the resource endpoints practically write themselves~!* 💖
