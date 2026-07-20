# Phase 3.3: Visual Workflow Designer (Weeks 27-30) 🎨

Made with 💖 by Ami-Chan! UwU ✨

[Back to Phase 3](Phase3-AdvancedFeatures.md) | [All Phases](README.md)

**Sub-phase breakout files** *(implement in order)*:

| File | Family | Scope |
|------|--------|-------|
| [Phase3-3a-DesignerFoundation.md](Phase3-3a-DesignerFoundation.md) | **3.3.a Foundation** | App shell, API client layer, workflow list, module palette, read-only canvas (pan/zoom, node + connection rendering) |
| [Phase3-3b-DesignerEditing.md](Phase3-3b-DesignerEditing.md) | **3.3.b Editing** | Drag-and-drop, connection drawing, selection, properties panel, undo/redo, save/validate, keyboard shortcuts |
| [Phase3-3c-DesignerRuntime.md](Phase3-3c-DesignerRuntime.md) | **3.3.c Runtime** | Execute from designer, real-time execution overlay (3.2 hub), execution history panel, docs + polish |

---

## Overview

> **Progress (2026-07-19):** Phase 3.3 is **COMPLETE ✅**. All 13 slices (3.3.a.0–a.3, b.0–b.4, c.0–c.3) plus the D14 validate endpoint are implemented, tested, and documented. New `Workflow.UI.Client` designer (Blazor WASM): typed API + SignalR clients over plain wire-DTO mirrors, framework-free state core (document/geometry/validator/commands/selection/clipboard/run-state), custom SVG/HTML canvas (pan/zoom/fit/minimap), palette drag-to-create, port-to-port connections with cycle rejection, schema-driven properties panel with lazy Monaco (+textarea fallback), command-pattern undo/redo, two-stage save validation, live run overlay via the 3.2 hub (+polling fallback), and execution history review. **142 UI tests + validate-endpoint API tests green; full solution builds clean.** Docs: `docs/designer.md` + `docs/designer-architecture.md` (incl. the React-port checklist). The one backend addition is `POST /api/v1/workflows/validate`; the React+TS port (3.3.P7) is kept additive by the D2 contracts-only boundary.

Phase 3.3 delivers the **browser-based visual workflow designer** — the first real UI for
GlutenFree.DotFlow. Users browse workflows, drag modules from a palette onto a canvas,
wire nodes together, configure them through a schema-driven properties panel, save through
the existing REST API, execute, and **watch nodes light up live** via the Phase 3.2
SignalR hub. The backend is *already finished* for this phase: workflows CRUD (2.7.1),
module discovery with full schemas + editor hints (2.7.3), execution start/status (2.7.2),
and real-time events (3.2) are all shipped and tested. Phase 3.3 is a **pure front-end
consumer of existing public contracts** — which is also exactly what makes the
React-swap-later strategy work (D2)~ 🌷

> **CopilotNote:** Hot paths: the existing `Workflow.UI` (server host) + `Workflow.UI.Client`
> (WASM) projects already scaffolded in `Workflow.sln`; `Workflow.UI.Client/Designer/*`
> (canvas, palette, properties panel as Blazor components over **plain-C# state services**);
> `Workflow.UI.Client/Api/*` (typed REST + SignalR client); `Workflow.Tests.UI` (new bUnit
> test project). The API needs exactly **one new endpoint** for MVP — `POST
> /api/v1/workflows/validate` (D14, a thin wrapper over the existing
> `ModuleAwareWorkflowValidator`); everything else speaks `/api/v1/workflows`,
> `/api/v1/modules`, `/api/v1/executions`, `/hubs/workflow` verbatim~ 🌸

> **Reality-check note (July 2026):** The §3.3 checklist in
> [`Phase3-AdvancedFeatures.md`](Phase3-AdvancedFeatures.md#33-ui---visual-workflow-designer-week-18-19)
> predates Phases 2.7–3.2. Since then: (a) the "choose framework" task is **resolved by the
> user: Blazor WebAssembly for MVP** with a documented React+TypeScript escape hatch (D1/D2);
> (b) a hosted **Blazor Web App skeleton already exists in-tree** (`Workflow.UI` +
> `Workflow.UI.Client`, .NET 8, already in the solution) — setup is a refit, not a green-field;
> (c) `NodeDefinition` **already carries `Position(X, Y)`** — no schema change needed to
> persist layout; (d) `ModuleSchema` ships `PropertyEditorType`
> (Text/MultilineText/Number/Boolean/Dropdown/FilePath/DirectoryPath/ConnectionString/
> Expression/Json/Code) + `AllowedValues` + validation rules — the properties panel is
> **schema-driven, not hand-coded per module**; (e) the checklist's "real-time execution
> visualization" deliverable now rides the **shipped 3.2 hub** instead of new plumbing.
> This plan reconciles all five and supersedes the checklist.

**Timeline:** 4 weeks (Weeks 27-30) — 3.3.a Weeks 27-28 · 3.3.b Weeks 28-29 · 3.3.c Week 30
**Complexity:** 🔴 High — the canvas interaction model (drag/pan/zoom/connect with correct
coordinate math) and undo/redo correctness are the risky parts; everything server-side is done.

---

## Confirmed Design Decisions ✅

| # | Decision |
|---|----------|
| **D1 Blazor WebAssembly for MVP (user decision)** | The MVP ships on the **existing** hosted Blazor Web App pair: `Workflow.UI` (ASP.NET Core host, serves the WASM payload + can proxy the API in dev) + `Workflow.UI.Client` (the WASM app, where all designer code lives). .NET 8, `Microsoft.AspNetCore.Components.WebAssembly` already in `Directory.Packages.props`. Rationale: C# end-to-end, direct reuse of contract records, one toolchain, team expertise. |
| **D2 React-swap readiness via a hard "contracts only" boundary** | The designer must remain portable to React+TypeScript without backend changes. Enforced by four rules: **(1)** the client talks to the backend **only** through the public REST endpoints + the 3.2 SignalR hub — no Blazor-host-only endpoints, no server-side designer logic in `Workflow.UI`; **(2)** the client keeps its own **plain-DTO mirror** of the wire contracts (`Workflow.UI.Client/Api/Dtos/*`, System.Text.Json-friendly, no LanguageExt) — the same JSON a TS client would consume; **(3)** all designer *logic* (graph state, selection, command stack, validation, layout math) lives in **framework-free C# services** (`Designer/State/*` — no Blazor types), so a React port re-implements mechanical TS mirrors of documented state machines rather than untangling component code; **(4)** `docs/designer-architecture.md` documents the seams + a port checklist. Blazor components are deliberately **thin views** over the state services. |
| **D3 Custom SVG/HTML canvas — no heavyweight diagram library** | The canvas is **HTML nodes absolutely positioned over an SVG edge layer** inside a pan/zoom transform container — no `Blazor.Diagrams` dependency. Rationale: the interaction set we need (pan, zoom, drag, port-to-port connect, selection) is well-bounded; a library locks the interaction model to Blazor and hurts the React exit (React Flow ≠ Blazor.Diagrams concepts). The math (screen↔canvas transforms) lives in a framework-free `CanvasGeometry` helper — directly portable. *(Q1 confirms this.)* |
| **D4 Component styling: minimal dependency posture** | Plain CSS (scoped `.razor.css` + a small design-token stylesheet) for the MVP; **no MudBlazor** unless Q2 overrides. Rationale: the designer surface is mostly custom-drawn anyway (canvas, nodes, ports); a component library adds download weight (WASM is already heavy) and another React-exit mismatch. Standard controls (buttons, inputs, dialogs) are simple enough to hand-roll with tokens. |
| **D5 The wire format IS the persistence format** | The canvas edits a client-side `DesignerDocument` that maps 1:1 to the `WorkflowDefinition` JSON already served by `GET /api/v1/workflows/{id}` — nodes (`id`, `moduleId`, `name`, `properties`, `position`), connections (`sourceNodeId`/`sourcePortName`/`targetNodeId`/`targetPortName`/`condition`/`priority`), variables. **`NodeDefinition.Position` already exists** — zero schema/serializer changes. Save = `PUT /api/v1/workflows/{id}`; create = `POST`. |
| **D6 Schema-driven properties panel** | The panel renders editors from `ModuleSchema.Properties[].EditorType` (the 2.7.3 module DTO): Text→input, MultilineText→textarea, Number→numeric, Boolean→checkbox, Dropdown→select over `AllowedValues`, Expression→input with `{{ }}` hinting, Json→textarea with JSON validation, Code→code textarea (language from the node's `language` property when present, e.g. `builtin.script`). Validation rules (`Required`, `Min`/`Max`, `Regex`, …) map to client-side checks. **No per-module UI code** — new modules get UI for free. |
| **D7 Undo/redo via command pattern over the document** | Every mutation is an `IDesignerCommand` (`Do`/`Undo`) against the `DesignerDocument`: AddNode, RemoveNode(s), MoveNode(s), EditNodeProperties, RenameNode, AddConnection, RemoveConnection, EditConnection, EditVariables. A bounded `CommandStack` (50 entries) with dirty-tracking (`SavePoint` marker → "unsaved changes" indicator + close warning). Framework-free (D2). |
| **D8 Real-time overlay rides the 3.2 hub as-is** | Run mode subscribes via `SubscribeToExecution(executionId)` and paints node states from `NodeStarted`/`NodeCompleted`/`NodeFailed` + a progress bar from `ExecutionProgress`; `ExecutionSnapshot` seeds late joins. **No new hub methods or events** — 3.2 shipped everything the overlay needs. Reconnect = re-subscribe (3.2 D9). |
| **D9 Auth: token-based, config-driven, dev-friendly** | The client stores a JWT (or API key) entered on a lightweight settings/login pane, sends `Authorization: Bearer`/`X-API-Key` on REST and `access_token` on the hub (3.2 D5). When the API runs with `Api:Auth:Require=false` (dev default) the designer works anonymously. No cookie/OIDC flow in MVP *(full OIDC login → 3.3.P3)*. |
| **D10 Testing: bUnit for components, xUnit for state services, E2E deferred** | New `Workflow.Tests.UI` project (bUnit + xUnit + FluentAssertions, mirroring repo conventions). The framework-free state services get plain xUnit tests (the bulk of the logic — cheap and fast); Blazor components get bUnit render/interaction tests; browser E2E (Playwright) is **post-MVP (3.3.P2)** — the seams make it additive. |
| **D11 Read-only-first slicing** | 3.3.a ships a **read-only** designer (browse, open, render, pan/zoom) before any editing exists — de-risks the canvas math with the simplest possible interaction set, and is independently demoable. Editing (3.3.b) and runtime (3.3.c) layer on top. Mirrors the 2.4 shared-infrastructure-first pattern. |
| **D12 Designer-specific layout data stays in the definition** | Node position persists in `NodeDefinition.Position` (exists). Any *future* designer-only annotations (notes, colors, collapsed groups) ride `NodeDefinition.Metadata["ui.*"]` keys — the same zero-migration convention as 2.8's `moduleVersion` pin. No parallel "layout file". |
| **D13 Monaco code editor in MVP (Q3 override)** | `Code`/`Expression`/`Json` properties use the **Monaco editor** via JS interop, **lazy-loaded** so the WASM app's initial payload doesn't carry it (fetched on first code-editor open). Wrapped behind an `ICodeEditor` component seam with the plain-textarea implementation kept as the built-in fallback (load failure → textarea, and a user-facing settings toggle to force the textarea → 3.3.P1). Language modes: `javascript`/`csharp`/`lua` from the node's `language` property, `json` for Json editors, plain for Expression (with `{{ }}` hinting). The interop wrapper is view-layer only (D2-safe — React ports swap in `@monaco-editor/react`). |
| **D14 Server validate endpoint in MVP (Q5 override)** | `POST /api/v1/workflows/validate` — the **only new API code in Phase 3.3** — accepts a full `WorkflowDefinition` body and returns the issue list from the **existing** `ModuleAwareWorkflowValidator` (graph structure + per-module `ValidateConfiguration`, already implemented and tested) without persisting anything. `WorkflowRead` policy (it's a dry-run, not a write). The save pipeline runs client structural checks first (instant feedback), then the server validate (authoritative), then PUT/POST. Errors render with node-id links in the save dialog. |

---

## TO RESOLVE 🤔 → RESOLVED ✅

> All Q1–Q7 resolved (July 2026) — user answers folded into the design decisions + slices
> (Q3/Q5 overrode the proposals; Q6 got a compromise)~ ✅

- [x] **Q1 Canvas approach: custom SVG/HTML (D3) or the `Z.Blazor.Diagrams` library?**
  - **RESOLVED (agreed):** Custom (D3). Revisit only if 3.3.a canvas work overruns badly.
- [x] **Q2 Component library: plain CSS (D4) or MudBlazor?**
  - **RESOLVED (agreed):** Plain CSS + design tokens (D4). If form-building in 3.3.b drags, MudBlazor can be adopted *inside* the thin view layer without touching state services.
- [x] **Q3 Code editor for `Code`/`Expression` properties: plain `<textarea>` MVP or Monaco via JS interop?**
  - **RESOLVED (overridden): Monaco in MVP.** It's a large dependency, but the authoring experience is worth it and saves time long-run. Ships **lazy-loaded** (only fetched when a Code/Expression/Json editor first opens) behind an `ICodeEditor` seam with a plain-textarea fallback — a settings toggle to force the textarea (dependency-size escape hatch, per the user note) lands as **3.3.P1**. See D13 + 3.3.b.3.
- [x] **Q4 Auto-save: none, or 30s draft timer?**
  - **RESOLVED (agreed):** None in MVP — explicit save + dirty indicator + close warning (D7). Not worth the complexity; add later if users lose work. Draft/auto-save → **3.3.P4**.
- [x] **Q5 Workflow validation before save: client-side only, or add a server dry-run validate endpoint?**
  - **RESOLVED (overridden): server validate endpoint in MVP.** Client structural checks alone aren't sufficient; users need detailed server-grade errors *before* saving. `POST /api/v1/workflows/validate` wraps the **existing** `ModuleAwareWorkflowValidator` (module-aware config validation already implemented) — a thin endpoint, the only new API code in 3.3. See D14 + 3.3.b.4; former 3.3.P5 is absorbed.
- [x] **Q6 Minimap: MVP or post-MVP?**
  - **RESOLVED (compromise):** A **lightweight minimap in MVP** — corner overlay drawing node bounds as scaled rectangles + a viewport frame, click/drag-to-navigate, collapsible, no live node content. Lands in 3.3.c.2. Full-fidelity minimap (rendered thumbnails) + auto-layout stay post-MVP (**3.3.P6**).
- [x] **Q7 Module version-pin UI (the 2.8 Q3/2.8.P4 note): in 3.3 MVP?**
  - **RESOLVED (agreed):** No — read-only pinned-version chip in the node inspector when `Metadata["moduleVersion"]` is present; full pin/unpin UI → 3.3.P8/2.8.P4.

---

## Pre-Existing Work (from earlier phases) ✅

| Component | File / Endpoint | Status |
|-----------|-----------------|--------|
| Hosted Blazor Web App skeleton (server + WASM client), already in `Workflow.sln` | `Workflow.UI/Workflow.UI/*`, `Workflow.UI/Workflow.UI.Client/*` | ✅ Refit, don't scaffold (D1) |
| Blazor WASM packages centrally versioned | `Directory.Packages.props` (`Microsoft.AspNetCore.Components.WebAssembly[.Server]` 8.0.22) | ✅ No new server packages |
| Node position in the definition | `Workflow.Core/Models/NodeDefinition.cs` (`Position(double X, double Y)`) | ✅ Layout persists for free (D5) |
| Workflows CRUD + soft-delete/restore | `GET/POST/PUT/DELETE /api/v1/workflows[/{id}]` (2.7.1) | ✅ Save/load/list/delete (D5) |
| Module discovery with schemas + editor hints | `GET /api/v1/modules[/{moduleId}]` (2.7.3); `PropertyEditorType`, `AllowedValues`, validation rules in `ModuleSchema.cs` | ✅ Palette + properties panel are schema-driven (D6) |
| Execution start/status/cancel/list | `POST /api/v1/workflows/{id}/execute`, `GET /api/v1/executions/{id}` (2.7.2) | ✅ Run mode (3.3.c) |
| Real-time execution events | `/hubs/workflow` — `SubscribeToExecution`, `NodeStarted/Completed/Failed`, `ExecutionProgress`, `ExecutionSnapshot` (3.2) | ✅ Live overlay rides it as-is (D8) |
| Auth schemes + query-string hub token | `Workflow.Api/Auth/*` (2.7.7), 3.2 D5 | ✅ D9 client token handling |
| Variables API | `GET/PUT/DELETE /api/v1/variables` (2.7.4) | ✅ Workflow-variables editor backend |
| Script test endpoint (code-property authoring aid) | `POST /api/v1/scripts/test` (3.1.6) | ✅ Optional "test script" button (3.3.b) |
| ProblemDetails error convention | 2.7 `ApiResults` | ✅ Uniform save/execute error surfaces |
| Module-aware workflow validation (server-side) | `Workflow.Modules/Validation/ModuleAwareWorkflowValidator.cs`, `Workflow.Core/Abstractions/WorkflowValidator.cs` | ✅ D14 validate endpoint wraps it as-is |

> **CopilotNote:** The mirror of the 2.4/3.1/3.2 insight: **the backend for this phase
> already exists** — Phase 3.3 adds exactly one thin API endpoint (D14 validate, wrapping
> the existing validator) and otherwise writes no C# in `Workflow.Api`/`Workflow.Engine`
> beyond serving the WASM app. Budget risk on **canvas interaction math** and
> **undo/redo correctness** — not on plumbing~ 💖

---

## Screen Mockups 🖼️

### S1 — Workflow list (landing page)

```text
┌──────────────────────────────────────────────────────────────────────────────┐
│ 🌊 DotFlow Designer                                    [⚙ Settings] [🔑 Auth] │
├──────────────────────────────────────────────────────────────────────────────┤
│  Workflows                                              [＋ New Workflow]     │
│  ┌──────────────────────────────────────────────────────────────────────┐    │
│  │ 🔍 Search workflows…                                    [Tags ▾]     │    │
│  ├──────────────────────────────────────────────────────────────────────┤    │
│  │  Name              Version  Nodes  Updated           Actions         │    │
│  │  ─────────────────────────────────────────────────────────────────── │    │
│  │  order-pipeline    1.4.0    12     2026-07-18 14:02  [Open] [▶] [🗑] │    │
│  │  nightly-report    2.0.1     7     2026-07-17 09:30  [Open] [▶] [🗑] │    │
│  │  webhook-fanout    1.0.0     5     2026-07-15 11:12  [Open] [▶] [🗑] │    │
│  ├──────────────────────────────────────────────────────────────────────┤    │
│  │                      ‹ 1 2 3 ›   (24 workflows)                      │    │
│  └──────────────────────────────────────────────────────────────────────┘    │
└──────────────────────────────────────────────────────────────────────────────┘
```

### S2 — Designer (main screen, edit mode)

```text
┌──────────────────────────────────────────────────────────────────────────────────────┐
│ 🌊 order-pipeline v1.4.0 ●unsaved   [💾 Save] [↩ Undo] [↪ Redo] [▶ Run] [⤢ Fit] [100%▾]│
├────────────────┬─────────────────────────────────────────────────────┬───────────────┤
│ MODULE PALETTE │                CANVAS  (pan: drag bg · zoom: wheel)  │ PROPERTIES    │
│ 🔍 filter…     │                                                      │               │
│                │   ┌────────────┐         ┌──────────────┐            │ http-1        │
│ ▾ Control Flow │   │ ⚡ trigger  │  ╭─────▶│ 🌐 http-1     │───╮       │ HTTP Request  │
│   ◇ condition  │   │  webhook   ●──╯     ●│  GET /orders  │●  │       │ ───────────── │
│   ◇ switch     │   └────────────┘         └──────────────┘   │       │ Name          │
│   ◇ loop.for.. │                                             ▼       │ [http-1     ] │
│   ◇ parallel   │                          ┌──────────────────────┐   │ Url *         │
│ ▾ Data         │                          │ ◇ cond-1              │   │ [https://api…]│
│   ◇ transform..│                       ●──│  status == 200        │   │ Method        │
│   ◇ database..│                          │        [true] [false] │   │ [GET       ▾] │
│ ▾ Scripting    │                          └───●──────────────●───┘   │ Headers (json)│
│   ◇ script     │                       ╭─────╯                ╰───╮  │ [{...}      ] │
│ ▾ Files        │                       ▼                          ▼  │               │
│   ◇ file.read  │             ┌──────────────┐          ┌──────────────┐  Timeout (s) │
│   ◇ file.write │             │ 📜 script-1   │          │ 📝 log-fail  │  [30       ] │
│   ◇ csv.read   │            ●│  enrich data │●        ●│  level: warn │               │
│ ▾ HTTP         │             └──────────────┘          └──────────────┘  [Apply]      │
│   ◇ http.req.. │                                                      │               │
│      (drag →)  │                                        [＋ zoom] [－] │ ⚠ Url is     │
│                │                                                      │   required    │
├────────────────┴─────────────────────────────────────────────────────┴───────────────┤
│ ✓ Valid (12 nodes, 14 connections)  ·  Last saved 14:02:11  ·  🟢 API connected       │
└──────────────────────────────────────────────────────────────────────────────────────┘
```

### S3 — Run mode (live execution overlay via the 3.2 hub)

```text
┌──────────────────────────────────────────────────────────────────────────────────────┐
│ 🌊 order-pipeline — RUN  exec 7f3a…  ▓▓▓▓▓▓▓▓░░░░ 60% (3/5)      [⏹ Cancel] [✖ Close] │
├────────────────┬─────────────────────────────────────────────────────┬───────────────┤
│ RUN LOG        │                                                      │ NODE STATUS   │
│ 14:03:01 start │   ┌────────────┐         ┌──────────────┐            │ ✅ trigger    │
│ 14:03:01 trig..│   │ ✅ trigger  │────────▶│ ✅ http-1     │───╮       │ ✅ http-1     │
│ 14:03:02 http..│   └────────────┘         └──────────────┘   │       │ ✅ cond-1     │
│ 14:03:02 cond..│        (done 0.2s)          (done 0.8s)     ▼       │ 🔄 script-1   │
│ 14:03:03 scri..│                          ┌──────────────────────┐   │ ⬜ log-fail   │
│      …         │                          │ ✅ cond-1  → [true]   │   │               │
│                │                          └───────────┬──────────┘   │ exec 7f3a…    │
│                │                                      ▼              │ state Running │
│                │             ┌──────────────┐   ┌──────────────┐     │ 60% · 3/5     │
│                │             │ 🔄 script-1   │   │ ⬜ log-fail   │     │               │
│                │             │  running 1.2s │   │  (waiting)   │     │ [View in     │
│                │             └──────────────┘   └──────────────┘     │  Executions]  │
├────────────────┴─────────────────────────────────────────────────────┴───────────────┤
│ ▶ Running  ·  live via /hubs/workflow  🟢                                              │
└────────────────┴──────────────────────────────────────────────────────────────────────┘
   Legend: ⬜ pending · 🔄 running (pulse) · ✅ completed · ❌ failed · ⏭ skipped
```

### S4 — Node anatomy + connection drag (SVG structure)

```text
        ┌───────────────────────────────┐
        │ 🌐  http-1          [⋮ menu]  │   ← header: icon, name, context menu
        │     builtin.http.request      │   ← module id (small, dimmed)
        ├───────────────────────────────┤
   ●────┤ input                  success├────●   ← ports: in (left) / out (right)
        │                          error├────●      labels from ModuleSchema
        └───────────────────────────────┘
        border: 2px  selected=accent · running=pulse · failed=red · completed=green

   Connection drag:   [source port ●]───╌╌╌╌▶ ghost bezier follows cursor;
   compatible target ports highlight; drop snaps + creates ConnectionDefinition;
   invalid target (cycle / same node / type clash) shows ⛔ and cancels.
```

> **S5 — Execution history panel** (past-run review: list, final-state canvas painting,
> outputs/errors, re-run) lives with its slice in
> [Phase3-3c-DesignerRuntime.md §3.3.c.2](Phase3-3c-DesignerRuntime.md#33c2-execution-history-panel-).

---

## Architecture (framework-swap ready) 🏗️

```text
┌──────────────────────────  Workflow.UI.Client (WASM)  ─────────────────────────┐
│                                                                                │
│  Blazor components (THIN views — no logic)                                     │
│   Pages/: WorkflowList · Designer · Settings                                   │
│   Designer/Components/: CanvasView · NodeView · EdgeLayer · Palette ·          │
│                         PropertiesPanel · Toolbar · RunOverlay · StatusBar     │
│        │  bind/render only                                                     │
│        ▼                                                                       │
│  Framework-free C# services  ◀━━ the part a React port re-implements in TS ━━▶ │
│   Designer/State/:  DesignerDocument (graph model, 1:1 wire JSON)              │
│                     CommandStack (undo/redo, dirty tracking)                   │
│                     SelectionState · CanvasGeometry (screen↔canvas math)       │
│                     GraphValidator (cycles, dangling, required props)          │
│   Api/:             WorkflowsClient · ModulesClient · ExecutionsClient         │
│                     RealTimeClient (SignalR wrapper) · AuthState               │
│   Api/Dtos/:        plain STJ records mirroring the wire contracts             │
└───────────────┬───────────────────────────────────┬───────────────────────────┘
                │ REST (JSON)                        │ SignalR
                ▼                                    ▼
      /api/v1/workflows · /modules ·          /hubs/workflow
      /executions · /variables                (3.2 — as-is)
```

**React exit checklist** (kept current in `docs/designer-architecture.md`): the wire DTOs
are already TS-shaped; `DesignerDocument`/`CommandStack`/`CanvasGeometry`/`GraphValidator`
have documented behaviors + xUnit specs that double as porting specs; views are disposable.

---

## Proposed File Layout 🗂️

```text
Workflow.UI/Workflow.UI/                       ← existing host (serves WASM; dev API proxy)
Workflow.UI/Workflow.UI.Client/
  Api/
    Dtos/ (WorkflowDtos.cs · ModuleDtos.cs · ExecutionDtos.cs · RealTimeDtos.cs)
    WorkflowsClient.cs · ModulesClient.cs · ExecutionsClient.cs
    RealTimeClient.cs · AuthState.cs · ApiClientOptions.cs
  Designer/
    State/
      DesignerDocument.cs · DesignerNode.cs · DesignerConnection.cs
      CommandStack.cs · IDesignerCommand.cs · Commands/*.cs
      SelectionState.cs · CanvasGeometry.cs · GraphValidator.cs
    Components/
      CanvasView.razor(+.cs/.css) · NodeView.razor · EdgeLayer.razor
      ModulePalette.razor · PropertiesPanel.razor · PropertyEditors/*.razor
      CodeEditor.razor (+ monaco-interop.js — lazy-loaded, D13)
      Minimap.razor (lightweight, Q6 compromise)
      ExecutionHistory.razor (past-run review, 3.3.c.2)
      Toolbar.razor · RunOverlay.razor · StatusBar.razor · ContextMenu.razor
  Pages/ (WorkflowList.razor · Designer.razor · Settings.razor)
  wwwroot/css/tokens.css
  wwwroot/js/monaco/ (lazy-loaded Monaco assets, D13)
Workflow.Api/V1/WorkflowEndpoints.cs           ← + POST /workflows/validate (D14 — the one API change)
Workflow.Tests.UI/                              ← NEW project (bUnit + xUnit)
  State/*.cs (document/commands/geometry/validator specs)
  Components/*.cs (bUnit render + interaction tests)
docs/designer.md · docs/designer-architecture.md
```

---

## Sub-Phase Index & Dependencies 🧭

| Slice | File | Depends on |
|-------|------|-----------|
| 3.3.a.0 Project refit + API client layer | [3-3a](Phase3-3a-DesignerFoundation.md) | — |
| 3.3.a.1 App shell, auth pane, workflow list | [3-3a](Phase3-3a-DesignerFoundation.md) | a.0 |
| 3.3.a.2 Designer document + geometry (state core) | [3-3a](Phase3-3a-DesignerFoundation.md) | a.0 |
| 3.3.a.3 Read-only canvas (render, pan, zoom, fit) | [3-3a](Phase3-3a-DesignerFoundation.md) | a.1, a.2 |
| 3.3.b.0 Module palette + drag-to-create | [3-3b](Phase3-3b-DesignerEditing.md) | a.3 |
| 3.3.b.1 Selection + node move + context menus | [3-3b](Phase3-3b-DesignerEditing.md) | a.3 |
| 3.3.b.2 Connection drawing + validation | [3-3b](Phase3-3b-DesignerEditing.md) | b.1 |
| 3.3.b.3 Properties panel (schema-driven) | [3-3b](Phase3-3b-DesignerEditing.md) | b.1 |
| 3.3.b.4 Undo/redo + save + dirty tracking + shortcuts | [3-3b](Phase3-3b-DesignerEditing.md) | b.0–b.3 |
| 3.3.c.0 Execute from designer | [3-3c](Phase3-3c-DesignerRuntime.md) | b.4 |
| 3.3.c.1 Real-time overlay (3.2 hub) | [3-3c](Phase3-3c-DesignerRuntime.md) | c.0 |
| 3.3.c.2 Execution history panel | [3-3c](Phase3-3c-DesignerRuntime.md) | c.1 |
| 3.3.c.3 Docs + polish + minimap + a11y/perf pass | [3-3c](Phase3-3c-DesignerRuntime.md) | c.2 |

---

## Agent Implementation Instructions 🤖

> **Audience:** an AI coding agent (e.g. Claude Opus 4.8) implementing this phase. These
> instructions encode the repo's process + gotchas learned in Phases 2.4–3.2. Follow them
> exactly; when a decision isn't covered here or in D1–D14, **ask the user before
> improvising** — do not silently change scope, dependencies, or public contracts.

### Workflow loop (per slice)

1. **Work strictly in dependency order** from the Sub-Phase Index above. One slice at a
   time: read the slice's Tasks + Tests blocks in its breakout file *before* writing code.
2. **Read before you write.** Before touching a file, view it. Before consuming an
   existing contract (DTO shape, endpoint, hub event), grep/read the real source — do not
   code from memory of this document alone. Key sources of truth:
   `Workflow.Core/Models/*` (WorkflowDefinition/NodeDefinition/ModuleSchema),
   `Workflow.Api/V1/*` + `Workflow.Api/Contracts/*` (wire shapes),
   `Workflow.Api/RealTime/*` + `Workflow.Api/Contracts/RealTime/*` (hub),
   `Workflow.Api/Auth/AuthConfiguration.cs` (policies/schemes).
3. **Build early, build often:** `dotnet build <project>.csproj` after each meaningful
   unit of work; run the slice's tests before moving on. Never start slice N+1 with slice
   N red.
4. **Check off plan boxes as you go** — both the **Tasks** and **Tests** checkboxes in the
   breakout file (`- [ ]` → `- [x]`). When a slice completes: set its `##` header to
   `✅ DONE`, and when a whole breakout file completes, check its Exit Criteria. When the
   phase completes: check the master Success Criteria, add a completion banner under
   `## Overview`, and flip the pointer notes in `Phase3-AdvancedFeatures.md` §3.3 and
   `phases/README.md` to COMPLETE ✅. *(This mirrors how 3.1/3.2 were closed — see those
   files for the exact style.)*
5. **Track progress in todos** (one todo per slice, `3-3a-0` … `3-3c-3`, with
   dependencies) and update statuses (`in_progress`/`done`) as you work.
6. **If you deviate** from a Task (better approach, blocked, deferred detail), do not
   silently skip the checkbox — annotate the plan line with a short note
   (*"done via X instead"* / *"deferred to c.3 because Y"*) so the plan stays truthful.

### Repo conventions & gotchas (hard-won — respect these)

- **Solution file is `Workflow.sln`** (not GlutenFree.DotFlow.sln). Add new projects with
  `dotnet sln Workflow.sln add <csproj>`.
- **Central Package Management:** every `PackageReference` needs a matching
  `PackageVersion` in `Directory.Packages.props` (no `Version=` on references). New
  packages for this phase: `bunit` (tests). Monaco is **static JS assets** under
  `wwwroot/js/monaco/`, not a NuGet package — download/pin a specific version and note it.
- **Some csproj/md files contain UTF-8 emoji** that can mangle with naive string edits —
  if a targeted edit fails to match, fall back to PowerShell `-replace` on raw content.
- **PowerShell environment:** no `&&`/`||` operators — use `;` and `if ($?)`. Each command
  runs in a fresh process (no persisted cwd/env).
- **Known flaky tests (pre-existing, parallel-timing only):** `NodeTimeout_OverHostCeiling_Clamped`,
  `Parallel_BranchFails_FailFastTrue_WorkflowFails`, `Status_AfterCompletion_ReturnsCompleted`,
  occasional Roslyn script tests under full parallel load. A full-suite run with 1–3
  failures from this set that **pass in isolation** is not a regression — verify by
  re-running the failing test alone; never "fix" these as part of 3.3.
- **Style:** repo uses StyleCop analyzers + XML doc comments with the established emoji
  voice (see any 3.2 file in `Workflow.Api/RealTime/*` for the register). Match it in new
  C# files. `this.`-prefix convention in `Workflow.Api`-style projects; check the target
  project's existing files and mirror them.
- **Do not commit `bin/`/`obj/`** — `Workflow.UI` has stray build artifacts in git; the
  a.0 refit should fix `.gitignore` coverage and remove them from tracking, not add more.
- **Never edit generated/lock files by hand.**

### Architecture guardrails (D2 — non-negotiable)

- `Workflow.UI.Client/Designer/State/*` and `Api/*` must have **zero Blazor/JSInterop
  types** (no `ComponentBase`, `IJSRuntime`, `EventCallback`). JS interop lives only in
  view-layer wrappers (`CodeEditor.razor`, canvas pointer glue). If a state service needs
  something from the DOM, pass it in as plain data.
- **No LanguageExt in the client.** DTOs are plain System.Text.Json records.
- **No new server endpoints** beyond D14's `POST /api/v1/workflows/validate`. If a slice
  seems to need one, stop and ask.
- The Blazor host (`Workflow.UI`) holds **no designer logic** — static hosting + optional
  dev proxy only.

### Verification gates

- **Per slice:** slice tests green + `dotnet build Workflow.sln` clean.
- **Per breakout file (a/b/c):** full `Workflow.Tests` + `Workflow.Tests.UI` suites green
  (modulo the known flaky set, verified in isolation); Exit Criteria checklist satisfied;
  **manual smoke** against a live API (`dotnet run` on `Workflow.Api`, then the UI) for
  the flows that bUnit can't cover (real pan/zoom feel, real SignalR).
- **API-affecting work (D14 endpoint):** its tests live in `Workflow.Tests` (not
  Tests.UI), follow the existing `WebApplicationFactory` endpoint-test pattern, and
  `docs/rest-api.md` gets the endpoint row in the same edit.

### Scope discipline

- Post-MVP slices (3.3.P1–P8) are **out of scope** — do not implement them opportunistically.
- Don't refactor backend code you pass through unless a slice explicitly says so; the
  known-flaky tests and pre-existing analyzer warnings are not yours to fix here.
- Mockups S1–S5 are **layout guides, not pixel specs** — match structure and affordances,
  not exact box-drawing.

---

## Post-MVP Slices 🚧 *(deferred — not blocking Phase 4)*

### 3.3.P1 Code-editor preference toggle 🖋️ *(Q3 — Monaco is MVP per D13)*
A settings toggle to force the plain-textarea implementation of `ICodeEditor` (dependency-
size / restricted-environment escape hatch, per the Q3 resolution). Also: deeper Monaco
integration — inline diagnostics + run-in-editor wired to `POST /api/v1/scripts/test`.

### 3.3.P2 Browser E2E suite 🎭 *(D10)*
Playwright E2E over the hosted app: open→edit→save→run happy path, drag/connect flows,
regression screenshots.

### 3.3.P3 OIDC login flow 🔐 *(D9)*
Full `Api:Auth:Jwt:Authority` OIDC code-flow login (redirect, silent renew) replacing the
paste-a-token pane.

### 3.3.P4 Drafts + auto-save 📝 *(Q4)*
Server-side draft slots (or localStorage drafts) + 30s auto-save without clobbering the
published definition.

### 3.3.P5 Validation depth extensions ✅ *(Q5 — the validate endpoint itself is MVP per D14)*
Extend `POST /api/v1/workflows/validate` beyond the current validator: expression syntax
pre-checks (via the 3.1 evaluator), script compile dry-runs, connection-condition linting.

### 3.3.P6 Full-fidelity minimap + auto-layout 🗺️ *(Q6 — a lightweight minimap is MVP per the compromise)*
Upgrade the MVP rectangle minimap to rendered thumbnails with live run-state tinting;
dagre-style auto-layout button for imported/position-less workflows.

### 3.3.P7 React + TypeScript port 🎯 *(D2 exit)*
Execute the port checklist in `docs/designer-architecture.md`: TS mirrors of the state
services (specs from `Workflow.Tests.UI/State`), React Flow or custom canvas, MUI shell.

### 3.3.P8 Module version-pin UI 🔢 *(Q7 / 2.8.P4)*
Pin/unpin module versions per node once `NodeDefinition.ModuleVersion` is promoted.

---

## Success Criteria ✅

- [x] Browse, search, open, create, and delete workflows from the browser (list page, S1)
- [x] Open a workflow → nodes + connections render at their persisted positions; pan/zoom/fit work smoothly (S2)
- [x] Drag a module from the palette onto the canvas → a configured node is created with a unique id
- [x] Drag port-to-port → a valid connection is created; cycles/self/duplicate connections are rejected with visible feedback (S4)
- [x] Properties panel renders the right editor for every `PropertyEditorType`, validates per schema rules, and writes back to the document (D6); `Code`/`Expression`/`Json` properties get the lazy-loaded Monaco editor with textarea fallback (D13)
- [x] Undo/redo works across all mutation types with a 50-entry history; dirty indicator + unsaved-changes warning behave (D7)
- [x] Save round-trips through `PUT/POST /api/v1/workflows` — reload reproduces the identical canvas (D5); the save pipeline runs client structural checks **and** `POST /api/v1/workflows/validate` (D14) with detailed, node-linked error messages
- [x] ▶ Run starts an execution and the canvas lights up live via the 3.2 hub: pending→running→completed/failed per node + progress bar (S3, D8)
- [x] Past executions are reviewable in the designer: per-workflow history list, final node states painted on the canvas, outputs/errors inspectable, re-run (S5, 3.3.c.2)
- [x] A lightweight minimap shows node bounds + viewport frame with click-to-navigate (Q6 compromise)
- [x] The designer works against an auth-required API using a pasted JWT/API key (D9)
- [x] **API/engine changes limited to the single D14 validate endpoint** (wrapping the existing validator) + UI hosting; the client touches only public REST + hub contracts (D2)
- [x] `docs/designer.md` (user guide) + `docs/designer-architecture.md` (incl. React port checklist) exist
- [x] State services ≥ 80% covered by xUnit specs; all components have bUnit render tests; full suite green

---

*Made with 💖 by Ami-Chan! Drawing boxes and arrows is serious business~ UwU* ✨
