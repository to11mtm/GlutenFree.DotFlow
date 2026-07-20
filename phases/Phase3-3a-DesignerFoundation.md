# Phase 3.3.a: Designer Foundation (Weeks 27-28) 🏗️

Made with 💖 by Ami-Chan! UwU ✨

[Master plan: Phase3-3-WorkflowDesigner.md](Phase3-3-WorkflowDesigner.md) | [Next: 3.3.b Editing](Phase3-3b-DesignerEditing.md)

> **Scope:** App refit, the typed API client layer, auth pane, workflow list page, the
> framework-free designer state core, and a **read-only** canvas (render, pan, zoom, fit).
> At the end of 3.3.a a user can log in, browse workflows, open one, and inspect it
> visually — no editing yet (D11 read-only-first).

> **🤖 Agent notes (read [master instructions](Phase3-3-WorkflowDesigner.md#agent-implementation-instructions-) first):**
> - Slice order: **a.0 → (a.1 ∥ a.2) → a.3**. a.1 and a.2 are independent after a.0.
> - Before a.0: inspect the real wire JSON — run the API and capture `GET /api/v1/workflows/{id}` + `GET /api/v1/modules` responses (or read the contract records in `Workflow.Api/Contracts/*`); build the DTO mirrors from *observed* shapes, then lock them with the golden-file round-trip tests.
> - a.2 is **pure C#** — if you find yourself importing a Blazor namespace there, stop (D2 guardrail).
> - a.0's refit should also fix `.gitignore` for `Workflow.UI/**/bin|obj` and untrack committed build artifacts.
> - bUnit JS-interop: use `JSInterop.Mode = JSRuntimeMode.Loose` in the test context for canvas components; pointer-event tests assert on state/transform changes, not real hit-testing.

---

## 3.3.a.0 Project Refit + API Client Layer 🧰

> **Purpose:** Turn the existing `Workflow.UI`/`Workflow.UI.Client` skeleton into the
> designer host + establish the typed, contract-mirroring API client layer that everything
> else (and a future React port) is defined against.

**Complexity:** 🟢 Low-Medium

### Tasks

- [x] **Refit the existing projects** — confirm `Workflow.UI` (host) + `Workflow.UI.Client` (WASM) build in `Workflow.sln`; align both csproj files with repo conventions (nullable, implicit usings, StyleCop where practical); delete template pages (Counter/Weather remnants) if present
- [x] **Dev API connectivity** — `ApiClientOptions` (`BaseUrl` from `appsettings.json`/`wwwroot/appsettings.json`); document the two dev modes: (a) CORS — API allows the UI origin via `Api:RealTime:AllowedOrigins` + a REST CORS note, or (b) host proxy — `Workflow.UI` forwards `/api` + `/hubs` to the API in dev (keeps a single origin; **no designer logic in the proxy** per D2)
- [x] **Wire DTO mirrors (D2)** — `Api/Dtos/*`: plain System.Text.Json records mirroring the wire JSON: `WorkflowDto` (id/name/description/version/nodes/connections/variables/tags/updatedAt), `NodeDto` (id/moduleId/name/properties as `Dictionary<string, JsonElement>`/position/metadata), `ConnectionDto`, `PositionDto`, `WorkflowSummaryDto` + paged envelope, `ModuleDto` + `ModuleSchemaDto` (ports, properties incl. `editorType`/`allowedValues`/`validationRules`), `ExecutionStatusDto`, `StartExecutionResultDto`, and the 3.2 event payload records — **no LanguageExt types anywhere in the client**
- [x] **`WorkflowsClient`** — `ListAsync(search?, page)`, `GetAsync(id)`, `CreateAsync(dto)`, `UpdateAsync(dto)`, `DeleteAsync(id)`; ProblemDetails-aware error surface (`ApiError` with status/title/detail/errors[])
- [x] **`ModulesClient`** — `ListAsync()` (palette), `GetAsync(moduleId)` (schema detail); client-side cache (modules change rarely)
- [x] **`ExecutionsClient`** — `ExecuteAsync(workflowId, inputs?)`, `GetStatusAsync(executionId)`, `CancelAsync(executionId)`
- [x] **`RealTimeClient`** — thin SignalR wrapper: `ConnectAsync(token?)`, `SubscribeToExecutionAsync(id)`, typed C# events for `ExecutionStarted/Completed/Failed`, `NodeStarted/Completed/Failed`, `ExecutionProgress`, `ExecutionSnapshot`; auto-reconnect + re-subscribe on `Reconnected` (3.2 D9); `Microsoft.AspNetCore.SignalR.Client` PackageReference added to the **client** csproj (version already in `Directory.Packages.props` from 3.2)
- [x] **`AuthState`** — holds the JWT/API key (in-memory + `localStorage` persist opt-in); a `DelegatingHandler` stamps `Authorization: Bearer`/`X-API-Key` on REST; feeds `access_token` to `RealTimeClient`
- [x] **`Workflow.Tests.UI` project** — new xUnit + bUnit + FluentAssertions test project added to the solution; `bunit` PackageVersion added to `Directory.Packages.props`; API clients tested against a fake `HttpMessageHandler`

### Tests (target ~10): → `Workflow.Tests.UI/Api/ApiClientTests.cs`

- [x] `WorkflowsClient_List_ParsesPagedEnvelope` · `WorkflowsClient_Get_ParsesFullDefinition` *(nodes, connections, positions, JsonElement properties)*
- [x] `WorkflowsClient_Update_SendsWireShapeJson` *(golden-file JSON: what we PUT equals what GET returned, modulo edits)*
- [x] `WorkflowsClient_ServerError_SurfacesProblemDetails` · `ModulesClient_List_ParsesSchemas` *(editorType/allowedValues/validation preserved)*
- [x] `ModulesClient_Caches_SecondCallNoHttp` · `ExecutionsClient_Execute_ReturnsExecutionId`
- [x] `AuthState_Handler_StampsBearer` · `AuthState_Handler_StampsApiKey` · `Dtos_RoundTrip_NoDataLoss` *(deserialize → serialize → byte-compare canonical JSON)*

---

## 3.3.a.1 App Shell, Auth Pane, Workflow List 🪟

> **Purpose:** The chrome around the designer: layout, routing, the token-paste auth pane
> (D9), and the workflow list landing page (mockup **S1**).

**Complexity:** 🟢 Low-Medium

### Tasks

- [x] **Layout + tokens** — `MainLayout` with top bar (product name, settings, auth status dot); `wwwroot/css/tokens.css` (colors incl. node-state palette: pending/running/completed/failed/skipped, spacing, type scale, dark-friendly defaults); scoped CSS per component (D4)
- [x] **Routing** — `/` → WorkflowList · `/designer/{id:guid}` → Designer · `/designer/new` → Designer (blank) · `/settings` → Settings
- [x] **Settings/auth pane (D9)** — API base URL display, token textarea (JWT) or API-key input, "remember on this device" toggle (`localStorage`), connection test button (`GET /api/v1/status` → 🟢/🔴 + version)
- [x] **WorkflowList page (S1)** — paged table from `WorkflowsClient.ListAsync`: name/version/node count/updated; search box (server `?search=` if supported, else client filter); actions: **Open** (navigate), **▶ Run** (fire + toast with execution id), **🗑 Delete** (confirm dialog → DELETE; admin-policy failures surface the 403 ProblemDetails cleanly)
- [x] **[＋ New Workflow]** — name prompt dialog → navigate to `/designer/new` with the name staged (POST happens on first save, 3.3.b.4)
- [x] **Error + toast surface** — a lightweight `Toasts` service + component (info/success/error), used by every page; API failures render title + detail
- [x] **Loading/empty states** — skeleton rows while loading; friendly empty-state ("No workflows yet — create one!")

### Tests (target ~8): → `Workflow.Tests.UI/Components/ShellAndListTests.cs` *(bUnit)*

- [x] `Layout_Renders_TopBarAndOutlet` · `Settings_TestConnection_ShowsStatus`
- [x] `AuthPane_SavesToken_HandlerUsesIt` · `WorkflowList_RendersRows_FromClient`
- [x] `WorkflowList_Search_Filters` · `WorkflowList_Delete_ConfirmsThenCalls`
- [x] `WorkflowList_Run_ShowsExecutionToast` · `WorkflowList_Empty_ShowsEmptyState`

---

## 3.3.a.2 Designer Document + Geometry (state core) 🧠

> **Purpose:** The framework-free heart of the designer (D2): the mutable client-side graph
> model that mirrors the wire format 1:1 (D5), plus the coordinate math every canvas
> interaction depends on. **No Blazor types in this slice.**

**Complexity:** 🟡 Medium *(correctness-critical — everything sits on this)*

### Tasks

- [x] **`DesignerDocument`** — `Id`, `Name`, `Description`, `Version`, `List<DesignerNode>`, `List<DesignerConnection>`, `Dictionary<string, VariableDto>`, `Tags`; `FromDto(WorkflowDto)` / `ToDto()` round-trip (lossless — unknown/extra fields preserved via the DTO layer); node lookup by id; `Changed` event for view invalidation
- [x] **`DesignerNode`** — `Id`, `ModuleId`, `Name`, `Properties (Dictionary<string, JsonElement>)`, `X`, `Y`, `Metadata`; plus resolved-at-load `ModuleSchemaDto? Schema` (ports for rendering; null-safe for unknown modules → "missing module" visual)
- [x] **`DesignerConnection`** — source/target node + port names, `Condition`, `Priority`
- [x] **`CanvasGeometry`** — pure functions: `ScreenToCanvas(point, pan, zoom)` / `CanvasToScreen`; zoom-about-cursor math (`NewPanForZoom(cursor, oldPan, oldZoom, newZoom)`); zoom clamping (0.1–3.0); `FitToContent(nodeBounds[], viewport, padding)` → (pan, zoom); node bounds calc (port-count-aware height); **bezier path builder** for edges (`M sx,sy C sx+dx,sy sx′−dx,ty tx,ty` with dx from horizontal distance) + port anchor positions (left edge inputs / right edge outputs, evenly spaced)
- [x] **`GraphValidator` (structural core)** — `Validate(document, knownModules)` → issues list: unknown `moduleId`, connection referencing missing node/port, duplicate connection, **cycle detection** (DFS), self-connection; used read-only in 3.3.a (status bar) and as the save gate in 3.3.b
- [x] **Unique id generation** — `MakeNodeId(moduleId, existingIds)` → `http-1`, `http-2`, … (short module stem + counter; stable, human-friendly)

### Tests (target ~16): → `Workflow.Tests.UI/State/DocumentAndGeometryTests.cs` *(pure xUnit — these specs double as the React port spec, D2)*

- [x] `Document_FromDto_ToDto_RoundTripsLosslessly` *(golden JSON)* · `Document_UnknownModule_KeptNotDropped`
- [x] `Geometry_ScreenToCanvas_InvertsCanvasToScreen` *(property-style across pans/zooms)*
- [x] `Geometry_ZoomAboutCursor_KeepsCursorPointFixed` · `Geometry_Zoom_ClampedToLimits`
- [x] `Geometry_FitToContent_ContainsAllNodes_WithPadding` · `Geometry_FitToContent_EmptyDocument_DefaultView`
- [x] `Geometry_PortAnchors_EvenlySpaced_InputsLeft_OutputsRight` · `Geometry_BezierPath_EndpointsMatchAnchors`
- [x] `Validator_DetectsCycle` · `Validator_AllowsDiamond_NotACycle` *(A→B, A→C, B→D, C→D)*
- [x] `Validator_UnknownModule_Flagged` · `Validator_DanglingConnection_Flagged` · `Validator_DuplicateConnection_Flagged` · `Validator_SelfConnection_Flagged`
- [x] `NodeId_Generation_UniqueAndStable`

---

## 3.3.a.3 Read-Only Canvas 🖼️ (render, pan, zoom, fit)

> **Purpose:** Render an opened workflow faithfully (mockup **S2** minus editing): HTML
> nodes over an SVG edge layer inside a transform container (D3), with pan/zoom/fit and
> zoom controls. The Designer page loads real data and displays it.

**Complexity:** 🟠 Medium-High *(the canvas math meets the DOM here)*

### Tasks

- [ ] **`CanvasView` component (D3)** — structure: outer viewport `div` (captures pointer + wheel events) → inner transform `div` (`transform: translate(panX,panY) scale(zoom)`) containing **(1)** `EdgeLayer` (single absolutely-positioned SVG covering content bounds) and **(2)** absolutely-positioned `NodeView`s; renders from `DesignerDocument` + re-renders on `Changed`
- [ ] **`NodeView` component (S4)** — header (module icon by category, node name, dimmed module id), port rows from the schema (labels + `●` anchors), state-classes hook (used by 3.3.c overlay), fixed width / port-count-based height matching `CanvasGeometry` bounds; "missing module" fallback visual (⚠ unknown `moduleId`)
- [ ] **`EdgeLayer` component** — one SVG `<path>` per connection using the geometry bezier builder; optional condition label at the midpoint; `marker-end` arrowhead def
- [ ] **Pan** — pointer-down on canvas background (not a node) + drag → pan updates (uses `setPointerCapture`); cursor feedback (`grab`/`grabbing`); touch drag works via pointer events
- [ ] **Zoom** — wheel (with `ctrl` for fine steps) zooms **about the cursor** via `CanvasGeometry.NewPanForZoom`; pinch zoom deferred to polish (c.2) if pointer-event pinch proves fiddly
- [ ] **Zoom controls UI (S2)** — `[＋] [－] [100%▾ (25/50/75/100/150/200)] [⤢ Fit]` cluster; Fit uses `FitToContent`
- [ ] **Designer page load path** — `/designer/{id}` → parallel `WorkflowsClient.Get` + `ModulesClient.List` → build `DesignerDocument` (schemas resolved) → initial Fit; load-error state with retry
- [ ] **Status bar (S2 bottom)** — validation summary from `GraphValidator` (read-only ✓/⚠), node/connection counts, API status dot
- [ ] **Perf guard** — node views keyed by id (`@key`); pan/zoom mutate only the transform style (no per-node re-render); target: 100-node document pans smoothly

### Tests (target ~12): → `Workflow.Tests.UI/Components/CanvasReadOnlyTests.cs` *(bUnit)*

- [ ] `Canvas_RendersNode_PerDocumentNode` · `Canvas_RendersEdge_PerConnection`
- [ ] `Node_ShowsName_ModuleId_AndPorts` · `Node_UnknownModule_ShowsFallback`
- [ ] `Edge_PathEndpoints_MatchPortAnchors` · `Edge_ConditionLabel_RendersWhenPresent`
- [ ] `Pan_Drag_UpdatesTransform` · `Wheel_ZoomsAboutCursor` *(transform assertions vs geometry expectations)*
- [ ] `ZoomControls_PlusMinusReset_Work` · `FitButton_AppliesFitTransform`
- [ ] `DesignerPage_LoadsAndRenders_FromClients` *(fake handlers)* · `DesignerPage_LoadError_ShowsRetry`

---

## Exit Criteria for 3.3.a ✅

- [ ] `Workflow.UI` + `Workflow.UI.Client` + `Workflow.Tests.UI` build in `Workflow.sln`; full test suite green
- [ ] With the API running (auth off), the list page shows real workflows; opening one renders its true graph at persisted positions
- [ ] With auth ON, pasting a JWT/API key in Settings makes list + open work; without a token the UI shows clean auth errors (no crashes)
- [ ] Pan / wheel-zoom-about-cursor / fit behave correctly (geometry specs green; manual feel-check)
- [ ] `GraphValidator` reports real issues on a deliberately-broken definition (status bar ⚠)
- [ ] No editing affordances shipped (read-only by design, D11)

---

*Made with 💖 by Ami-Chan! First we look, then we touch~ UwU* ✨
