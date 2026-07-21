# Phase 3.6: Module Manager UI — "The Foundry" 📦

Made with 💖 by Ami-Chan! UwU ✨

[Back to Phase 3](Phase3-AdvancedFeatures.md) | [All Phases](README.md)

> ## ✅ COMPLETE — all 5 slices (3.6.0–3.6.4) implemented, **41 Foundry tests** (283 total in
> `Workflow.Tests.UI`), documented in [`docs/module-manager.md`](../docs/module-manager.md).
> **Zero new backend** — a pure client feature over the shipped read (2.7.3) + write (2.8.5)
> `/modules/*` endpoints; the docs viewer is generated from the schema (D6). The D2 framework-free
> boundary keeps the React port additive~ 🌸

---

## Overview

Phase 3.6 delivers a **dedicated module-management experience** — "The Foundry" — a page where
operators browse every registered module, read generated documentation (ports + properties +
dependencies + versions), **upload** custom `.wfmod` packages, **enable/disable** modules and
versions, and **uninstall** them. Like Phases 3.4 and 3.5 (and unlike 3.3), it is **almost entirely
a thin front-end** over infrastructure that already shipped — the **read-side module API (2.7.3)**
*and* the **write-side management API (2.8.5)** both exist. **Zero new backend for the MVP.**

> **CopilotNote:** Hot paths: a new `Workflow.UI.Client/Modules/*` feature area
> (`ModuleManager.razor` page at `/modules`, plus components + framework-free state), **extending
> `ModulesClient`** with the four management calls (upload / enable / disable / uninstall — the
> endpoints already exist), a **generated documentation model** built framework-free from the
> details DTO, and an **upload** flow (`InputFile` + drag-drop + multipart). Tests extend the
> existing `Workflow.Tests.UI` (bUnit + xUnit). **No `Workflow.Api`/`Workflow.Modules` changes for
> the MVP** — the client speaks the shipped `/modules/*` endpoints verbatim~ 🌸

> **Reality-check note (July 2026):** the §3.6 checklist in
> [`Phase3-AdvancedFeatures.md`](Phase3-AdvancedFeatures.md#36-ui---module-manager-week-21)
> predates Phases 2.7/2.8 and 3.3. Since then almost the entire backend exists:
> - **Read API (2.7.3):** `GET /api/v1/modules` (list, with `category`/`q`/`groupByCategory`
>   filters) + `GET /api/v1/modules/{id}` (details: schema, `dependencies`, `availableVersions`,
>   `enabled`).
> - **Write API (2.8.5):** `POST /modules/upload` (multipart `.wfmod`, **admin**, with package
>   validation → `ModuleInstallResultDto` warnings + `422`/`409` failures), `POST /modules/{id}/
>   enable|disable` (per-version, **WorkflowWrite**), `DELETE /modules/{id}` (**admin**, guarded
>   against dependents + in-flight executions → `409`).
> - **Client + UI (3.3):** `ModulesClient` (list/get, cached + `InvalidateCache`) and the designer's
>   `ModulePalette` (browse + search + category grouping + icons + details flyout) already exist.
>
> **The genuine gaps** are all client-side (management calls, a full manager page, upload UI,
> toggle/version/uninstall UI) plus **one honest limitation**: `IWorkflowModule` exposes
> `Description` + `Schema` + `Dependencies` + `Version` but **no README / usage examples /
> changelog** — so the "documentation viewer" is **generated** from the schema for the MVP, and
> first-class docs are deferred (3.6.P1). This plan reconciles the checklist and supersedes it.

**Timeline:** ~1 week (Week 33 — the checklist's original "Week 21" renumbered to follow 3.5).
**Complexity:** 🟡 Medium — the mechanics are well-bounded (existing endpoints, reuse the palette's
browse pattern); the fiddly parts are the **multipart upload + drag-drop + progress/validation** and
the **admin-gated actions' graceful degradation**.

---

## Confirmed Design Decisions ✅

| # | Decision |
|---|----------|
| **D1 A dedicated "Module Manager" page in the existing Blazor client** | Module Manager lives at `/modules` in `Workflow.UI.Client`, reachable from the top bar (`📦 Modules`). It reuses the 3.3 shell (TopBar, ToastHost, tokens.css, `AuthState`, ProblemDetails handling). No new host/project. |
| **D2 Contracts-only + framework-free boundary (inherited from 3.3 D2)** | The manager talks to the backend **only** through `/api/v1/modules/*`. All *logic* — catalog filtering/grouping, the generated **documentation model**, upload/validation state — lives in **framework-free C# services** (`Modules/State/*` — no Blazor/JSInterop/LanguageExt types), with plain STJ wire-DTO mirrors. Keeps the React+TS port (3.3.P7) additive. |
| **D3 Reuse `ModulesClient` + the palette's browse pattern** | The manager reuses `ModulesClient.ListAsync`/`GetAsync` (cached) and generalizes the designer palette's search + **category grouping** into a framework-free `ModuleCatalog` helper. The compact `ModulePalette` stays the in-designer picker; the manager is the full-screen experience (a "Manage modules →" link bridges them). |
| **D4 Zero new backend for the MVP** | Browse/details → 2.7.3; upload/enable/disable/uninstall → 2.8.5. The client adds **four `ModulesClient` methods** + DTO mirrors (`ModuleInstallResultDto`, `ModuleToggleResultDto`) and a page — **no `Workflow.Api`/`Workflow.Modules`/registry changes**. |
| **D5 Upload via `InputFile` + drag-drop + multipart** | The upload dialog accepts a `.wfmod` file (button + drag-drop), POSTs it as `multipart/form-data` (field `package`) to `/modules/upload`, shows a progress/spinner state, and renders **validation feedback**: success + `warnings[]` (missing hashes / schema-compat / signature) from `ModuleInstallResultDto`, or the ProblemDetails **error** (`422` invalid package, `409` duplicate version). On success it invalidates the cache and refreshes the grid. |
| **D6 A *generated* documentation viewer (README/examples/changelog are post-MVP)** | `IWorkflowModule` has no README/examples/changelog, so the docs drawer is **generated** from the details DTO: description, **inputs/outputs** (name, type, required, description, default), **properties** (editor type, required, default, allowed values, description), **dependencies**, **versions**, and enabled state. First-class README + usage examples + changelog (needs manifest + `IWorkflowModule` + registry + DTO plumbing) → **3.6.P1**. |
| **D7 Version management = per-version enable/disable + upload-new (no new endpoints)** | The registry resolves the **newest *enabled*** version, so "upgrade" = upload a new version (then it's newest+enabled) and "rollback" = disable the newer version / enable an older one. The version panel lists `availableVersions`, shows which is active, and toggles each via `/enable|disable?version=`. No `upgrade`/`rollback` endpoints are invented. |
| **D8 Dependency-aware warnings; uninstall is server-guarded** | Disable shows a **client heads-up** computed from the module→module `Dependencies` graph ("other modules depend on this"); *workflow-level* usage isn't indexed, so the warning says so (a workflow-usage query → 3.6.P3). **Uninstall** is guarded server-side (`409` when dependents exist or executions are in flight) — surfaced as a clear, actionable error. |
| **D9 Admin/write gating + graceful degradation** | Upload + uninstall need **Admin**; enable/disable need **WorkflowWrite**. The client can't cheaply know the caller's role, so it **attempts** the action and surfaces `401/403` as a friendly "requires admin/write" toast (Q1) — everyone gets the read-only browse + docs experience; the actions just fail clearly for the unprivileged. Anonymous-friendly when `Api:Auth:Require=false`. |
| **D10 Cache invalidation after mutations** | `ModulesClient.InvalidateCache()` after a successful install/enable/disable/uninstall, then re-list so the grid + the designer palette reflect reality. |
| **D11 Testing** | New `Workflow.Tests.UI` specs: framework-free catalog/doc-model specs + `ModulesClient` management tests (multipart request shape, toggle, uninstall, ProblemDetails) via `FakeHttpMessageHandler`; bUnit render/interaction for the grid, docs drawer, upload dialog, and toggle/version/uninstall flows. |
| **D12 Reuse the 3.3 client plumbing** | `ModulesClient` (extended), `AuthState`/`AuthMessageHandler`, `ApiException`/`ApiError`, `ToastService`, `ILocalStorage`, `tokens.css`, and the palette's grouping pattern — all reused. |

---

## RESOLVED ✅

> Q1–Q6 confirmed (all proposed answers **Agreed** by the user) — implementation proceeds on these~ ✅

- [x] **Q1 Admin/write gating → attempt-and-surface, with all users treated as admin for the MVP/demo.** The client shows **all** management actions unconditionally (no role-hiding) and relies on the server; in the demo posture (auth disabled / all-users-admin) they just work, and if a deployment enables auth an unprivileged `401/403` surfaces as a clear "requires admin/write" toast. Proper role-based access control + a `/auth/whoami` (or JWT-role decode) to pre-gate actions → **3.6.P2**.
- [x] **Q2 Documentation viewer → generated-from-schema now.** The docs drawer is generated from Description + schema + dependencies + versions (D6). First-class README/usage-examples/changelog → **3.6.P1**.
- [x] **Q3 Version upgrade/rollback → per-version enable/disable + upload-new (D7).** The registry resolves the newest *enabled* version; no new backend. Dedicated upgrade/rollback semantics deferred.
- [x] **Q4 Dependent warning → client heads-up from module→module `Dependencies` (D8).** A workflow-usage index ("N workflows use this module") → **3.6.P3**.
- [x] **Q5 Routing → `/modules` page + `📦 Modules` top-bar link + designer "Manage modules →" bridge (D1/Q5).**
- [x] **Q6 Upload → `accept=".wfmod"` + drag-drop, rely on server validation, no hard client size cap (surface `413`/`422`).**

---

## Pre-Existing Work (from earlier phases) ✅

| Component | File / Endpoint | Status |
|-----------|-----------------|--------|
| Module list (category/search/group) | `GET /api/v1/modules` (2.7.3) | ✅ Reused for the browse grid (D3) |
| Module details (schema/versions/enabled/deps) | `GET /api/v1/modules/{id}` (2.7.3) | ✅ Drives the generated docs drawer (D6) |
| Module upload (multipart `.wfmod`, admin, validation) | `POST /api/v1/modules/upload` → `ModuleInstallResultDto` (2.8.5) | ✅ The upload flow rides it (D5) |
| Enable / disable (per-version) | `POST /api/v1/modules/{id}/enable\|disable` → `ModuleToggleResultDto` (2.8.5) | ✅ Toggle + version panel (D7) |
| Uninstall (admin, dependents/active guards) | `DELETE /api/v1/modules/{id}` → `409` on conflict (2.8.5) | ✅ Uninstall flow (D8) |
| Typed module client (list/get, cache) | `Workflow.UI.Client/Api/ModulesClient.cs` (3.3.a.0) | ✅ Extended with the 4 management methods (D4) |
| Module DTOs (summary/details/schema) | `Api/Dtos/ModuleDtos.cs` (3.3.a.0) | ✅ + install-result/toggle mirrors |
| Designer palette (browse/search/category/icons) | `Designer/Components/ModulePalette.razor` (3.3.b.0) | ✅ Grouping generalized → `ModuleCatalog`; keeps the compact picker (D3) |
| Client shell + auth + toasts + tests | `Shared/*`, `Api/AuthState.cs`, `Api/ApiError.cs`, `Workflow.Tests.UI` (3.3) | ✅ Reused (D1/D9/D12) |

> **CopilotNote:** The 3.4 mirror, exactly: **both the read and write backends already exist**.
> Phase 3.6 writes **no** `Workflow.Api`/`Workflow.Modules`/registry code for the MVP — it's a client
> feature over the shipped `/modules/*` endpoints. Budget risk on the **multipart upload +
> drag-drop/progress** and the **admin-gated degradation** — not on plumbing~ 💖

---

## Screen Mockups 🖼️

### S1 — The Foundry (manager grid + docs drawer: `/modules`)

```text
┌──────────────────────────────────────────────────────────────────────────────────────────┐
│ 🌊 DotFlow · 📦 Modules          [🔍 search…] [Category: all ▾] [☑ enabled only] [⬆ Upload] │
├──────────────────────────────────────────────────────────────────────────────────────────┤
│ ▾ HTTP 🌐                                                                                   │
│  ┌─ 🌐 HTTP Request ───────┐  ┌─ 🌐 HTTP Response ──────┐                                   │
│  │ builtin.http.request    │  │ builtin.http.response   │   click → docs drawer ▶           │
│  │ v1.2.0 · 🟢 enabled     │  │ v1.0.0 · 🟢 enabled     │                                   │
│  └─────────────────────────┘  └─────────────────────────┘                                  │
│ ▾ Scripting 📜                                            ┌──────────────────────────────┐ │
│  ┌─ 📜 Script ─────────────┐                              │ 📜 Script  builtin.script    │ │
│  │ builtin.script          │                              │ v1.0.0  🟢 enabled           │ │
│  │ v1.0.0 · ⚪ disabled     │                              │ Runs sandboxed JS/C#/Lua…    │ │
│  └─────────────────────────┘                              │ ── inputs ──  input : object │ │
│                                                           │ ── outputs ── result : any   │ │
│                                                           │ ── properties ──             │ │
│                                                           │  language (Select) *required │ │
│                                                           │  code (Code)      *required  │ │
│                                                           │ ── versions ── 1.0.0 (active)│ │
│                                                           │ [🔘 Disable] [🗑 Uninstall]   │ │
│                                                           └──────────────────────────────┘ │
└──────────────────────────────────────────────────────────────────────────────────────────┘
```

### S2 — Upload dialog (validation feedback)

```text
┌─────────────────────────  Upload module (.wfmod)  ─────────────────────────┐
│                                                                             │
│     ┌───────────────────────────────────────────────────────────────┐      │
│     │   ⬆  Drag a .wfmod here, or [choose file]                      │      │
│     │        my-connectors-1.3.0.wfmod  (412 KB)                     │      │
│     └───────────────────────────────────────────────────────────────┘      │
│                                                                             │
│   ▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓░░░░  installing…                                          │
│                                                                             │
│   ✅ Installed my.connectors v1.3.0                                         │
│   ⚠ warnings:  • package is unsigned   • manifest hash missing              │
│   ✖ error (on failure):  Duplicate version 1.3.0 already installed (409)    │
│                                                                             │
│                                              [Close]  [Upload another]      │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Architecture (framework-swap ready) 🏗️

```text
┌──────────────────  Workflow.UI.Client (WASM)  ───────────────────┐
│  Blazor components (THIN views)                                  │
│   Pages/ModuleManager.razor                                      │
│   Modules/Components/: ModuleCard · ModuleDocsDrawer ·           │
│     UploadDialog · VersionPanel · ModuleFilters                  │
│        │                                                         │
│        ▼   framework-free C#  ◀── React port re-implements ──▶   │
│   Modules/State/: ModuleCatalog (filter/group) · ModuleDocModel  │
│     (schema → generated docs) · DependencyHints                  │
│   Api/: ModulesClient(+upload/enable/disable/uninstall)          │
│          Dtos/ModuleDtos.cs (+install-result/toggle mirrors)     │
└───────────────┬──────────────────────────────────────────────────┘
                │ REST (JSON + multipart)
                ▼
   /modules (list) · /modules/{id} · /upload · /{id}/enable|disable · DELETE /{id}
```

**React exit checklist** additions live in `docs/designer-architecture.md`: `ModuleCatalog`,
`ModuleDocModel`, and `DependencyHints` are framework-free with xUnit specs; the only browser-API
surface is the `InputFile`/drag-drop upload (swap for a React file input + `FormData`).

---

## Proposed File Layout 🗂️

```text
Workflow.UI.Client/
  Api/
    ModulesClient.cs                 (+ UploadAsync, EnableAsync, DisableAsync, UninstallAsync)
    Dtos/ModuleDtos.cs               (+ ModuleInstallResultDto, ModuleToggleResultDto mirrors)
  Modules/
    State/
      ModuleCatalog.cs               (filter/search/group — framework-free)
      ModuleDocModel.cs              (details DTO → generated doc sections)
      DependencyHints.cs             (module→module dependents heads-up)
    Components/
      ModuleCard.razor
      ModuleDocsDrawer.razor
      UploadDialog.razor
      VersionPanel.razor
      ModuleFilters.razor
  Pages/ModuleManager.razor          (route /modules)
  Designer/Components/ModulePalette.razor   (+ "Manage modules →" link)
Workflow.Tests.UI/
  Modules/State/*   (catalog, doc-model, dependency hints)
  Modules/Components/*   (grid, docs drawer, upload, versions, uninstall — bUnit)
  Modules/ModulesClientManagementTests.cs
docs/module-manager.md   (user guide) + cross-links (module-author-guide.md)
```

---

## Slices & Dependencies 🧭

| Slice | Scope | Depends on |
|-------|-------|-----------|
| 3.6.0 Client management methods + DTO mirrors + Manager shell (browse/search/filter/category) | — |
| 3.6.1 Module docs drawer (generated documentation from schema) | 3.6.0 |
| 3.6.2 Upload (`InputFile` + drag-drop + progress + validation feedback) | 3.6.0 |
| 3.6.3 Enable/disable + version panel + uninstall (management actions + guards/warnings) | 3.6.0, 3.6.1 |
| 3.6.4 Designer bridge + docs + polish | 3.6.1–3.6.3 |

---

## 3.6.0 Client Methods + Manager Shell 🧰 ✅ DONE

> **Purpose:** The management client calls, DTO mirrors, and the `/modules` page skeleton with a
> browse grid (grouped by category) + search + filter.

**Complexity:** 🟢 Low-Medium

### Tasks

- [x] **`Api/Dtos/ModuleDtos.cs`** (+) — `ModuleInstallResultDto` (`Module`, `Warnings`) and `ModuleToggleResultDto` (`ModuleId`, `Enabled`, `AffectedVersions`) mirrors
- [x] **`ModulesClient`** (+) — `UploadAsync` (multipart, field `package`), `EnableAsync`/`DisableAsync`/`UninstallAsync` (optional `?version=`); each calls `InvalidateCache()` on success; ProblemDetails-aware
- [x] **`Modules/State/ModuleCatalog.cs`** — framework-free: search (id/name/description) + category + enabled-only filters, then **group by category** (blank → "Other"); stable ordering; `Categories`
- [x] **`Pages/ModuleManager.razor`** — route `/modules`; top bar (Upload placeholder), a category-grouped **grid** of `ModuleCard`s, a details drawer stub (replaced in 3.6.1); loads via `ModulesClient.ListAsync`; nav entry (`📦 Modules`)
- [x] **`Modules/Components/ModuleCard.razor` + `ModuleFilters.razor`** — card (icon, name, id, version, enabled dot) opening the drawer; filter bar bound to `ModuleCatalog`

### Tests (18 green): → `Workflow.Tests.UI/Modules/State/ModuleCatalogTests.cs` + `Modules/ModulesClientManagementTests.cs` + `Components/ManagerShellTests.cs`

- [x] `Catalog_Search_FiltersByNameAndDescription` · `Catalog_Category_Filters` · `Catalog_EnabledOnly_Filters` · `Catalog_GroupByCategory_Orders_AndNormalizesBlank` · `Catalog_Categories_Distinct_Sorted`
- [x] `ModulesClient_Upload_SendsMultipartPackage` · `ModulesClient_Enable_Posts_WithVersion` · `ModulesClient_Disable_Posts` · `ModulesClient_Uninstall_Deletes` · `ModulesClient_Upload_Duplicate_SurfacesConflict` · `ModulesClient_Enable_InvalidatesCache`
- [x] `Manager_RendersGrid_Grouped` · `Manager_Search_Filters` · `Manager_CardClick_OpensDrawer` · `Manager_EnabledOnly_HidesDisabled`

---

## 3.6.1 Module Docs Drawer (generated) 📖 ✅ DONE

> **Purpose:** Click a module → a drawer with generated documentation: description, ports,
> properties, dependencies, versions, enabled state (mockup **S1** right drawer).

**Complexity:** 🟢 Low-Medium

### Tasks

- [x] **`Modules/State/ModuleDocModel.cs`** — framework-free: projects a `ModuleDetailsDto` into ordered doc sections — **Inputs**/**Outputs** (`DocPort`: name, type→"any" fallback, required, default, description), **Properties** (`DocProperty`: editor, required, default, allowed, description), **Dependencies**, **Versions** (`DocVersion` w/ active flag). No README/examples/changelog (D6)
- [x] **`Modules/Components/ModuleDocsDrawer.razor`** — renders the doc model: header (icon, name, id, version, enabled), description, ports/properties tables, dependency chips, version chips, plus optional `ActionsContent`/`VersionsContent` slots (wired in 3.6.3)
- [x] **Details fetch** — the page fetches via `ModulesClient.GetAsync(id)` (cached) on card click → `ModuleDocModel.From`; loading + not-found (toast) states

### Tests (9 green): → `Workflow.Tests.UI/Modules/State/DocModelTests.cs` + `Components/DocsDrawerTests.cs`

- [x] `DocModel_Ports_Projected_WithRequiredAndType` · `DocModel_Properties_IncludeEditorAllowedAndDefault` · `DocModel_Versions_FlagActive` · `DocModel_Dependencies_Projected` · `DocModel_NoDeps_NoVersions_EmptySections`
- [x] `Drawer_RendersSchema_FromDetails` · `Drawer_ShowsVersionsAndDeps` · `Drawer_Close_RaisesCallback`
- [x] `Manager_CardClick_OpensDrawer` *(page fetches details)*

---

## 3.6.2 Upload (drag-drop + validation) ⬆️ ✅ DONE

> **Purpose:** Upload a `.wfmod` package with drag-drop + progress + validation feedback (mockup
> **S2**).

**Complexity:** 🟡 Medium *(the multipart + drag-drop is the risky bit)*

### Tasks

- [x] **`Modules/Components/UploadDialog.razor`** — an `InputFile` (`accept=".wfmod"`) over a drag-drop zone; shows the selected file name/size; **Upload** streams it (`OpenReadStream`, 100 MB cap) to `ModulesClient.UploadAsync`; an installing/progress state
- [x] **Validation feedback** — success → "Installed {id} v{ver}" + `warnings[]` list; failure → the ProblemDetails error inline (`422` invalid / `409` duplicate); the dialog stays open with a clear result
- [x] **Post-install refresh** — on success, `OnUploaded` → page `Reload()` (the client's `UploadAsync` already `InvalidateCache()`d) so the new module appears
- [x] **Admin degradation (D9)** — `401/403` surfaces as "Uploading requires admin"

### Tests (6 green): → `Workflow.Tests.UI/Modules/Components/UploadTests.cs`

- [x] `Upload_SelectFile_ShowsNameAndSize` · `Upload_Success_ShowsInstalledAndWarnings` *(+ refresh callback)*
- [x] `Upload_Invalid_422_ShownInline` · `Upload_Duplicate_409_ShownInline` · `Upload_Forbidden_ShowsAdminHint`

---

## 3.6.3 Enable/Disable + Versions + Uninstall 🔘🔢🗑 ✅ DONE

> **Purpose:** The management actions — toggle a module/version, manage versions, and uninstall
> (with the server guards surfaced).

**Complexity:** 🟡 Medium

### Tasks

- [x] **`Modules/Components/VersionPanel.razor`** — lists `availableVersions`, flags the active, and raises `OnToggle((version, enable))` per version (the page calls `Enable/DisableAsync(id, version)`); "upgrade" = Upload (D7), "rollback" = enable an older / disable the newer
- [x] **`Modules/Components/ModuleActions.razor`** — enable/disable the module (resolved version) with a **dependents heads-up confirm** + **uninstall** (confirm → `DELETE`, `409` dependents/in-flight surfaced inline); admin `401/403` → "requires admin/write"
- [x] **`Modules/State/DependencyHints.cs`** — framework-free: dependents of a module from the loaded module details (workflow-level usage not indexed — noted, Q4)
- [x] **Drawer wiring** — `ModuleDocsDrawer` `ActionsContent` = `ModuleActions`, `VersionsContent` = `VersionPanel`; the page keeps a `knownDetails` cache for the dependents hint
- [x] **Cache/UI refresh** — every mutation `InvalidateCache()` + re-list + re-fetch the open module's details; uninstall closes the drawer

### Tests (11 green): → `Workflow.Tests.UI/Modules/State/DependencyHintsTests.cs` + `Components/VersionsAndToggleTests.cs`

- [x] `Deps_Dependents_Listed_SortedDistinct` · `Deps_None_Empty` · `Deps_ExcludesSelf`
- [x] `Toggle_Disable_CallsClient_AndWarnsOnDependents` · `Toggle_Enable_CallsClient`
- [x] `Versions_ListsAll_FlagsActive` · `Versions_EnableSpecific_PostsVersion` *(page posts `?version=`)*
- [x] `Uninstall_Confirm_CallsDelete` · `Uninstall_Conflict_409_ShownClearly`

---

## 3.6.4 Designer Bridge + Docs + Polish 🔗📚✨ ✅ DONE

> **Purpose:** Bridge the designer palette to the manager, document it, and close the phase.

**Complexity:** 🟢 Low

### Tasks

- [x] **Designer bridge (Q5)** — the designer's `ModulePalette` gains a **"📦 Manage modules →"** link to `/modules`
- [x] **`docs/module-manager.md`** — user guide: browsing/search/category, the docs drawer, uploading a `.wfmod`, enabling/disabling + versions, uninstalling, and permissions; screen tour matching **S1**/**S2**; cross-linked to/from `docs/module-author-guide.md`
- [x] **Cross-links + port checklist** — appended `ModuleCatalog`/`ModuleDocModel`/`DependencyHints`/`ModulesClient` to the `docs/designer-architecture.md` React-port checklist; `phases/README.md` + `Phase3-AdvancedFeatures.md` §3.6 → COMPLETE
- [x] **Polish** — enabled-only persisted in `localStorage` (restored on load); load-error retry; `dotnet build Workflow.sln` (0 errors) + full `Workflow.Tests.UI` (283) green

### Tests (4 green): → `Workflow.Tests.UI/Modules/Components/BridgeAndPolishTests.cs`

- [x] `Palette_ManageLink_NavigatesToModules` · `Manager_EnabledOnly_Persists` · `Manager_EnabledOnly_RestoredFromStorage` · `Manager_LoadError_ShowsRetry`

---

## Agent Implementation Instructions 🤖

> **Audience:** an AI coding agent implementing this phase. Follow the same loop and guardrails as
> [Phase 3.4](Phase3-4-ScriptEditor.md#agent-implementation-instructions-); highlights specific to 3.6:

- **Slice order:** **3.6.0 first** (client methods + shell unblock everything), then **3.6.1 ∥ 3.6.2**,
  then **3.6.3**, then **3.6.4**. The docs drawer (3.6.1) and upload (3.6.2) are independent.
- **No new backend for the MVP.** The client speaks the shipped `/modules/*` endpoints. If a task
  seems to need server work (README/examples/changelog, a workflow-usage query, role-awareness,
  upgrade/rollback endpoints), **stop** — it's post-MVP (3.6.P1–P3).
- **Read the real contracts first:** `ModuleEndpoints.cs` + `ModuleManagementEndpoints.cs`
  (routes/policies/multipart shape/guards), `ModuleContracts.cs` (`ModuleInstallResultDto`,
  `ModuleToggleResultDto`, `ModuleDetailsDto`), the existing `ModulesClient` + `ModuleDtos`, and the
  designer `ModulePalette` (the browse pattern you generalize). Capture a real `/upload` +
  `/{id}/enable` response before building the feedback UIs.
- **D2 guardrail (hard):** nothing in `Modules/State/*` or `Api/*` may reference Blazor/JS-interop
  types. `ModuleCatalog`, `ModuleDocModel`, `DependencyHints` are pure C# with xUnit specs. The only
  browser-API surface is the `InputFile`/drag-drop upload in `UploadDialog`.
- **Multipart is the risk.** Build `UploadAsync` with `MultipartFormDataContent` + a
  `StreamContent` named `package`; test the **request shape** (content-type + field name) with the
  fake handler before wiring the dialog. Blazor's `IBrowserFile.OpenReadStream` feeds the stream.
- **Admin degradation:** management calls can return `401/403`; catch `ApiException` and toast a
  "requires admin/write" message — never hard-fail the whole page.
- **Cache discipline:** call `ModulesClient.InvalidateCache()` after **every** successful mutation,
  then re-list — otherwise the grid + designer palette go stale.
- **bUnit patterns:** register all injected services before the first render; fake `InputFile` via
  bUnit's `InputFileContent`; use `data-testid` selectors (emoji text is mojibake-prone).
- **Bookkeeping ritual:** check off Tasks **and** Tests per slice, flip slice headers to `✅ DONE`,
  then at phase end check the Success Criteria, add an Overview completion banner, and update the
  `Phase3-AdvancedFeatures.md` §3.6 + `phases/README.md` pointers — exactly as 3.1–3.5 were closed.
  Track a todo per slice (`3-6-0`…`3-6-4`).
- **Repo gotchas:** `Workflow.sln`; Central Package Management (no new packages expected); PowerShell
  has no `&&`; known-flaky parallel engine tests verified in isolation are not regressions.

---

## Post-MVP Slices 🚧 *(deferred — not blocking 4.x)*

### 3.6.P1 First-class module docs 📖 *(Q2)*
Extend the `.wfmod` manifest + `IWorkflowModule` + registry + `ModuleDetailsDto` with **README**,
**usage examples**, and **changelog**, and render them in the docs drawer (beyond generated schema
docs).

### 3.6.P2 Role-aware UI 🔐 *(Q1)*
A `/auth/whoami` (or JWT-role decode) so the manager can pre-hide admin/write actions from
unprivileged users instead of attempting-and-surfacing.

### 3.6.P3 Workflow-usage index 🔗 *(Q4)*
A "which workflows use module X" query so disable/uninstall can warn with real workflow-level
impact (not just module→module dependencies).

### 3.6.P4 Module marketplace / remote registry 🛒
Browse + install modules from a remote registry (beyond local `.wfmod` upload).

---

## Success Criteria ✅

- [x] Open `/modules` and **browse** every module grouped by category, with **search** + **category/enabled** filters
- [x] Click a module → a **generated documentation** drawer (description, inputs/outputs, properties, dependencies, versions, enabled)
- [x] **Upload** a `.wfmod` (button + drag-drop) with **progress** + **validation feedback** (warnings on success, `422`/`409` errors on failure)
- [x] **Enable/disable** a module (+ a dependent heads-up) and manage **versions** (enable a specific version)
- [x] **Uninstall** a module, with the server's dependents/in-flight `409` surfaced clearly
- [x] **Zero new API/module/registry code** for the MVP; the client touches only the shipped `/modules/*` endpoints (D4)
- [x] Admin/write-gated actions **degrade gracefully** for the unprivileged (clear toast, read-only browse still works)
- [x] `docs/module-manager.md` exists and cross-links; the React-port checklist is updated
- [x] State services covered by xUnit specs; components have bUnit tests; full `Workflow.Tests.UI` suite green (283)

---

*Made with 💖 by Ami-Chan! A tidy foundry makes for happy workflows~ UwU* ✨
