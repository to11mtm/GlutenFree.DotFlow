# Phase 3.6: Module Manager UI — "The Foundry" 📦

Made with 💖 by Ami-Chan! UwU ✨

[Back to Phase 3](Phase3-AdvancedFeatures.md) | [All Phases](README.md)

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

## TO RESOLVE 🤔

> Proposed answers below so work can proceed; please confirm/override~ ✅

- [ ] **Q1 Admin/write gating UX: attempt-and-surface-403, or decode the JWT role / add `/auth/whoami` to hide actions?**
  - **Proposed:** Attempt-and-surface for the MVP — show the management actions; on `401/403` show a "requires admin/write" toast (D9). No client role-awareness. A `whoami`/role decode to pre-hide actions → **3.6.P2**.
    - **Rationale:** the client can't cheaply know the caller's role; the server already enforces it. The UX is clear: unprivileged users can browse + read docs, but management actions fail with a clear toast.
    - Agreed but we need to make sure that for MVP we can have all users be treated as admin for demo purposes, and then we can finish the whoami endpoint later for role-based access control.
- [ ] **Q2 Documentation viewer: generated-from-schema now, README/examples/changelog later?**
  - **Proposed:** Yes — generate the docs drawer from Description + schema + dependencies + versions (D6). First-class README/usage-examples/changelog (manifest + `IWorkflowModule` + registry + DTO plumbing) → **3.6.P1**.
    - Agreed 
- [ ] **Q3 Version "upgrade/rollback": map to per-version enable/disable + upload-new, or add upgrade/rollback endpoints?**
  - **Proposed:** Map to the existing per-version enable/disable + upload-new (D7) — the registry already resolves the newest *enabled* version. No new backend. Dedicated upgrade/rollback semantics → post-MVP if ever needed.
    - Agreed
- [ ] **Q4 "Disable dependent workflows warning": client heads-up from module `Dependencies` only, or a backend "which workflows use module X" query?**
  - **Proposed:** Client heads-up from the module→module dependency graph for the MVP (D8); a workflow-usage index/endpoint (so we can warn "N workflows use this module") → **3.6.P3**.
    - Agreed
- [ ] **Q5 Routing/nav: a `/modules` page + top-bar link; keep the designer palette as the compact picker with a "Manage modules →" link?**
  - **Proposed:** Yes — `/modules` (the full manager) + a `📦 Modules` top-bar link; the designer keeps its compact `ModulePalette` and gains a "Manage modules →" deep link (mirroring 3.4/3.5's bridges).
    - Agreed 
- [ ] **Q6 Upload constraints: rely on server validation (.wfmod/manifest/dll/deps) + `accept=".wfmod"`; any client size cap?**
  - **Proposed:** `accept=".wfmod"` on the picker + drag-drop; **no hard client size cap** (rely on the server's validation + any host request-size limit, surfacing `413`/`422`). A configurable client cap → post-MVP if needed.
    - Agreed 

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

## 3.6.0 Client Methods + Manager Shell 🧰

> **Purpose:** The management client calls, DTO mirrors, and the `/modules` page skeleton with a
> browse grid (grouped by category) + search + filter.

**Complexity:** 🟢 Low-Medium

### Tasks

- [ ] **`Api/Dtos/ModuleDtos.cs`** (+) — `ModuleInstallResultDto` (`Module: ModuleDetailsDto`, `Warnings: List<string>`) and `ModuleToggleResultDto` (`ModuleId`, `Enabled`, `AffectedVersions`) mirrors
- [ ] **`ModulesClient`** (+) — `UploadAsync(fileName, stream, ct)` (multipart, field `package`), `EnableAsync(id, version?)`, `DisableAsync(id, version?)`, `UninstallAsync(id, version?)`; each calls `InvalidateCache()` on success; ProblemDetails-aware
- [ ] **`Modules/State/ModuleCatalog.cs`** — framework-free: filter by search (id/name/description) + category + enabled-only, then **group by category** (generalizing the palette's grouping); stable ordering
- [ ] **`Pages/ModuleManager.razor`** — route `/modules`; top bar (search, category dropdown, "enabled only" toggle, Upload button placeholder), a category-grouped **grid** of `ModuleCard`s; loads via `ModulesClient.ListAsync`; nav entry from the app shell (`📦 Modules`)
- [ ] **`Modules/Components/ModuleCard.razor` + `ModuleFilters.razor`** — a card (icon, name, id, version, enabled dot) that opens the docs drawer on click; the filter bar bound to `ModuleCatalog`

### Tests (target ~8): → `Workflow.Tests.UI/Modules/State/ModuleCatalogTests.cs` + `Modules/ModulesClientManagementTests.cs` + `Components/ManagerShellTests.cs`

- [ ] `Catalog_Search_FiltersByNameAndDescription` · `Catalog_Category_Filters` · `Catalog_EnabledOnly_Filters` · `Catalog_GroupByCategory_Orders`
- [ ] `ModulesClient_Upload_SendsMultipartPackage` · `ModulesClient_Enable_Posts` · `ModulesClient_Uninstall_Deletes` · `ModulesClient_Upload_Duplicate_SurfacesConflict`
- [ ] `Manager_RendersGrid_Grouped` · `Manager_Search_Filters`

---

## 3.6.1 Module Docs Drawer (generated) 📖

> **Purpose:** Click a module → a drawer with generated documentation: description, ports,
> properties, dependencies, versions, enabled state (mockup **S1** right drawer).

**Complexity:** 🟢 Low-Medium

### Tasks

- [ ] **`Modules/State/ModuleDocModel.cs`** — framework-free: projects a `ModuleDetailsDto` into ordered doc sections — **Inputs** (name, type, required, default, description), **Outputs**, **Properties** (editor type, required, default, allowed values, description), **Dependencies**, **Versions** (with the active one flagged). No README/examples/changelog (D6)
- [ ] **`Modules/Components/ModuleDocsDrawer.razor`** — renders the doc model: header (icon, name, id, version, enabled), description, the ports/properties tables, dependency chips, version list, and the action row (Enable/Disable/Uninstall — wired in 3.6.3); a "used-as `builtin.script`?" hint links to `docs/scripting.md`/relevant guide when applicable
- [ ] **Details fetch** — via `ModulesClient.GetAsync(id)` (cached); loading + not-found states

### Tests (target ~6): → `Workflow.Tests.UI/Modules/State/DocModelTests.cs` + `Components/DocsDrawerTests.cs`

- [ ] `DocModel_Ports_Projected_WithRequiredAndType` · `DocModel_Properties_IncludeEditorAndAllowed` · `DocModel_Versions_FlagActive` · `DocModel_NoDeps_EmptySection`
- [ ] `Drawer_RendersSchema_FromDetails` · `Drawer_ShowsVersionsAndDeps`

---

## 3.6.2 Upload (drag-drop + validation) ⬆️

> **Purpose:** Upload a `.wfmod` package with drag-drop + progress + validation feedback (mockup
> **S2**).

**Complexity:** 🟡 Medium *(the multipart + drag-drop is the risky bit)*

### Tasks

- [ ] **`Modules/Components/UploadDialog.razor`** — an `InputFile` (`accept=".wfmod"`) + a drag-drop zone; shows the selected file name/size; **Upload** streams it to `ModulesClient.UploadAsync`; a progress/spinner state while installing
- [ ] **Validation feedback** — success → "Installed {id} v{ver}" + `warnings[]` list; failure → the ProblemDetails error inline (`422` invalid package / `409` duplicate version); the dialog stays open with a clear result
- [ ] **Post-install refresh** — on success, `InvalidateCache()` + re-list so the new module appears (and the designer palette picks it up on next open)
- [ ] **Admin degradation (D9)** — a `401/403` surfaces as "Uploading requires admin"

### Tests (target ~6): → `Workflow.Tests.UI/Modules/Components/UploadTests.cs`

- [ ] `Upload_SelectFile_ShowsNameAndSize` · `Upload_Success_ShowsInstalledAndWarnings`
- [ ] `Upload_Invalid_422_ShownInline` · `Upload_Duplicate_409_ShownInline`
- [ ] `Upload_Success_RefreshesList` · `Upload_Forbidden_ShowsAdminHint`

---

## 3.6.3 Enable/Disable + Versions + Uninstall 🔘🔢🗑

> **Purpose:** The management actions — toggle a module/version, manage versions, and uninstall
> (with the server guards surfaced).

**Complexity:** 🟡 Medium

### Tasks

- [ ] **`Modules/Components/VersionPanel.razor`** — lists `availableVersions`, flags the active (newest enabled), and enables/disables each via `EnableAsync/DisableAsync(id, version)`; "upgrade" = Upload (D7); "rollback" = enable an older / disable the newer
- [ ] **Enable/disable toggle** — a switch in the docs drawer + per version; on disable, a **dependent heads-up** (`DependencyHints`) when other modules depend on this one (workflow-level usage not indexed — noted, Q4)
- [ ] **`Modules/State/DependencyHints.cs`** — framework-free: given the catalog + a module id, list modules that declare it as a dependency
- [ ] **Uninstall** — a confirm → `UninstallAsync`; the server `409` (dependents / in-flight executions) surfaces as a clear, actionable error; success invalidates the cache + closes the drawer
- [ ] **Cache/UI refresh** — every mutation `InvalidateCache()` + re-list; the enabled dot + version list update

### Tests (target ~8): → `Workflow.Tests.UI/Modules/State/DependencyHintsTests.cs` + `Components/VersionsAndToggleTests.cs`

- [ ] `Deps_Dependents_Listed` · `Deps_None_Empty`
- [ ] `Toggle_Disable_CallsClient_AndWarnsOnDependents` · `Toggle_Enable_CallsClient`
- [ ] `Versions_ListsAll_FlagsActive` · `Versions_EnableSpecific_PostsVersion`
- [ ] `Uninstall_Confirm_CallsDelete` · `Uninstall_Conflict_409_ShownClearly`

---

## 3.6.4 Designer Bridge + Docs + Polish 🔗📚✨

> **Purpose:** Bridge the designer palette to the manager, document it, and close the phase.

**Complexity:** 🟢 Low

### Tasks

- [ ] **Designer bridge (Q5)** — the designer's `ModulePalette` gains a **"Manage modules →"** link to `/modules`; the manager's docs drawer offers **"use in designer"** context (drag hint / open-designer)
- [ ] **`docs/module-manager.md`** — user guide: browsing/search/category, the docs drawer, uploading a `.wfmod`, enabling/disabling + versions, and uninstalling; screen tour matching **S1**/**S2**; cross-link `docs/module-author-guide.md`
- [ ] **Cross-links + port checklist** — append `ModuleCatalog`/`ModuleDocModel`/`DependencyHints` to the `docs/designer-architecture.md` React-port checklist; `phases/README.md` + `Phase3-AdvancedFeatures.md` §3.6 → COMPLETE
- [ ] **Polish** — empty/error/loading states; a11y labels on icon buttons; enabled-only persisted in `localStorage`; `dotnet build Workflow.sln` + full `Workflow.Tests.UI` green

### Tests (target ~3): → `Workflow.Tests.UI/Modules/Components/BridgeAndPolishTests.cs`

- [ ] `Palette_ManageLink_NavigatesToModules` · `Manager_EnabledOnly_Persists` · `Manager_LoadError_ShowsRetry`

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

- [ ] Open `/modules` and **browse** every module grouped by category, with **search** + **category/enabled** filters
- [ ] Click a module → a **generated documentation** drawer (description, inputs/outputs, properties, dependencies, versions, enabled)
- [ ] **Upload** a `.wfmod` (button + drag-drop) with **progress** + **validation feedback** (warnings on success, `422`/`409` errors on failure)
- [ ] **Enable/disable** a module (+ a dependent heads-up) and manage **versions** (enable a specific version)
- [ ] **Uninstall** a module, with the server's dependents/in-flight `409` surfaced clearly
- [ ] **Zero new API/module/registry code** for the MVP; the client touches only the shipped `/modules/*` endpoints (D4)
- [ ] Admin/write-gated actions **degrade gracefully** for the unprivileged (clear toast, read-only browse still works)
- [ ] `docs/module-manager.md` exists and cross-links; the React-port checklist is updated
- [ ] State services covered by xUnit specs; components have bUnit tests; full `Workflow.Tests.UI` suite green

---

*Made with 💖 by Ami-Chan! A tidy foundry makes for happy workflows~ UwU* ✨
