# Designer Architecture & React-Port Guide 🏗️

> Phase 3.3 — how the Blazor WebAssembly designer is structured, and the checklist for a
> future React+TypeScript port (D2). Made with 💖 by Ami-Chan~ ✨

## Layering

```text
┌──────────────  Workflow.UI.Client (WASM)  ─────────────────┐
│  Blazor components (THIN views — bind/render only)          │
│   Pages/: WorkflowList · Designer · Settings                │
│   Designer/Components/: CanvasView · NodeView · EdgeLayer · │
│     ModulePalette · PropertiesPanel · PropertyEditor ·      │
│     CodeEditor · Toolbar(inline) · RunOverlay · Minimap ·   │
│     ExecutionHistory · ContextMenu · StatusBar              │
│        │  (no logic)                                         │
│        ▼                                                     │
│  Framework-free C# services  ◀── a React port re-implements │
│   Designer/State/: DesignerDocument · DesignerNode ·        │
│     DesignerConnection · CanvasGeometry · GraphValidator ·  │
│     NodePorts · NodeIdGenerator · CommandStack ·            │
│     IDesignerCommand + Commands/* · SelectionState ·        │
│     DesignerClipboard · JsonValues · RunState               │
│   Api/: WorkflowsClient · ModulesClient · ExecutionsClient ·│
│     SystemClient · RealTimeClient · AuthState · Dtos/*      │
│   Services/: ToastService · PaletteDragState · ILocalStorage│
└───────────────┬───────────────────────────┬───────────────┘
                │ REST (JSON)                │ SignalR
                ▼                            ▼
   /api/v1/workflows · /modules ·      /hubs/workflow (3.2)
   /executions · /workflows/validate
```

**Rules (D2):**
1. The client talks to the backend **only** through public REST + the 3.2 hub. The one
   API addition for this phase is `POST /api/v1/workflows/validate` (D14) — a thin wrapper
   over the existing `ModuleAwareWorkflowValidator`.
2. `Api/Dtos/*` are **plain System.Text.Json records** mirroring the wire JSON — no
   LanguageExt. They round-trip the server's exact bytes (proven by
   `Workflow.Tests.UI/Api/ApiClientTests.Dtos_RoundTrip_NoDataLoss`).
3. All designer **logic** lives in `Designer/State/*` and `Api/*` as **framework-free C#**
   (no `ComponentBase`, `IJSRuntime`, `EventCallback`). JS interop lives only in view
   wrappers (`CodeEditor`, `CanvasView` pointer glue, `keys.js`, `canvas.js`,
   `monaco-interop.js`).
4. Blazor components are **thin views** over the state services.

## State-service catalog (the port spec)

Each service has an xUnit spec in `Workflow.Tests.UI/State/*` — these are the authoritative
behavioral contracts a TypeScript port must satisfy:

| Service | Responsibility | Spec |
|---------|----------------|------|
| `DesignerDocument` (+Node/Connection) | Mutable graph model, 1:1 wire mirror, `FromDto`/`ToDto` | `DocumentAndGeometryTests` |
| `CanvasGeometry` | screen↔canvas transforms, zoom-about-cursor, fit, port anchors, bezier | `DocumentAndGeometryTests` |
| `GraphValidator` | unknown-module / dangling / duplicate / self / cycle; `WouldCreateCycle` | `GraphValidatorTests` |
| `CommandStack` + `IDesignerCommand`/`Commands/*` | undo/redo (50-cap), dirty tracking | `CommandStackTests` |
| `SelectionState` | single/multi/rubber-band selection | `SelectionStateTests` |
| `DesignerClipboard` + `CompositeCommand` | copy/paste (fresh ids, internal edges) | `ClipboardTests` |
| `JsonValues` | type-preserving JSON value plumbing | `JsonValuesTests` |
| `RunState` | live/historical execution state + node CSS painting | `RunStateTests` |
| `Api/*` clients | typed REST + SignalR wrappers | `ApiClientTests` |

## React + TypeScript port checklist (3.3.P7)

1. **DTOs → TS types.** The `Api/Dtos/*` records are already TS-shaped (camelCase JSON,
   `JsonElement` → `unknown`/`any`). Generate or hand-write `interface`s.
2. **State services → TS classes.** Port `DesignerDocument`, `CanvasGeometry`,
   `GraphValidator`, `CommandStack`, `SelectionState`, `DesignerClipboard`, `JsonValues`,
   `RunState` mechanically; the xUnit specs above become Jest/Vitest specs (same cases).
3. **API layer → TS.** `fetch`-based clients mirroring `WorkflowsClient`/`ModulesClient`/
   `ExecutionsClient`/`SystemClient`; `@microsoft/signalr` for `RealTimeClient`; a token
   store for `AuthState`.
4. **Views → React.** Rebuild the thin components with React Flow **or** a custom SVG/HTML
   canvas (the geometry math is already framework-free). Monaco via `@monaco-editor/react`.
   A component library (MUI) may replace the plain-CSS tokens.
5. **Untouched:** the **entire backend** — REST endpoints, the validate endpoint, the 3.2
   hub, and all engine code. A React client is a drop-in replacement for `Workflow.UI.Client`.

## Script Studio state services (Phase 3.4)

Script Studio (`/scripts`, see [`script-studio.md`](script-studio.md)) follows the same D2
framework-free boundary — these `Scripts/State/*` + `Api/*` types are pure C# with xUnit specs and
port mechanically:

| Service | Responsibility | Spec |
|---------|----------------|------|
| `ScriptsClient` + `Api/Dtos/ScriptDtos` | typed `/scripts/*` REST (test/languages/libraries) | `ScriptsClientTests` |
| `WorkflowApiDescriptor` + `ApiMethodInfo` | the `workflow.*` catalog driving completions/hover + the reference panel; drift-guarded vs `IWorkflowScriptApi` | `DescriptorTests` |
| `ScriptTemplateCatalog` + `ScriptTemplate` | the static starter-template catalog | `TemplateCatalogTests` |
| `TestRunState` | inputs/config/run status + log filtering | `TestRunStateTests` |
| `ScriptEditorOptions` | theme/options + language↔Monaco-mode map | `ScriptsClientTests` |
| `ScriptStudioHandoff` | designer ↔ studio code round-trip carrier | `DesignerIntegrationTests` |

The only JS-interop surface is the shared `monaco-interop.js` (editor + completion/hover provider
registration) — swap for `@monaco-editor/react` in a port.

## Execution Monitor state services (Phase 3.5)

The Execution Monitor (`/monitor`, see [`execution-monitor.md`](execution-monitor.md)) reuses the
shared `RunState` (moved to `Execution/State` in 3.5.1) and adds these framework-free services with
xUnit specs — all portable:

| Service | Responsibility | Spec |
|---------|----------------|------|
| `MonitorState` + `MonitorRow` | live dashboard rows + REST-seed + hub event merge | `MonitorStateTests` |
| `ExecutionFilterModel` + `RunLogClassifier` | status/date→server + duration/sort client-side + log-level classification | `FilterModelTests` |
| `ReplayCursor` | read-only step/scrub over ordered node records | `ReplayCursorTests` |
| `RunState` (shared, moved) | live/historical node run state + run log | `RunStateTests` |
| `ExecutionsClient` (+detail/nodes) · `RealTimeClient` (+SubscribeToAll) | typed REST + hub firehose | `ExecutionsClientMonitorTests` |

3.5's **only backend addition** is two read-only endpoints (`/executions/{id}/detail` + `/nodes`) —
a React port consumes them unchanged.

## Module Manager state services (Phase 3.6)

The Module Manager (`/modules`, see [`module-manager.md`](module-manager.md)) is a client feature
over the shipped `/modules/*` endpoints (read 2.7.3 + write 2.8.5) — **zero new backend**. Its
framework-free services with xUnit specs port cleanly:

| Service | Responsibility | Spec |
|---------|----------------|------|
| `ModuleCatalog` | search + category + enabled-only filter + category grouping | `ModuleCatalogTests` |
| `ModuleDocModel` | details DTO → generated documentation (ports/properties/deps/versions) | `DocModelTests` |
| `DependencyHints` | module→module dependents for the disable heads-up | `DependencyHintsTests` |
| `ModulesClient` (+upload/enable/disable/uninstall) | typed `/modules/*` REST (multipart upload) | `ModulesClientManagementTests` |

The only browser-API surface is the `InputFile`/drag-drop upload in `UploadDialog` (swap for a React
file input + `FormData`).

## Performance notes

- Node views are keyed by id (`@key`); pan/zoom mutate only the transform style, so panning
  a large graph does not re-render nodes.
- The minimap and edge layer recompute from node bounds only on render (cheap for typical
  workflow sizes). Rendered-thumbnail minimaps + auto-layout are deferred to 3.3.P6.

## Accessibility (MVP scope)

Toolbar/panel controls are keyboard-focusable with `aria`-labels on icon buttons and
visible focus rings from `tokens.css`. Canvas interactions are pointer-only in the MVP;
full canvas keyboard a11y is out of scope (noted for a later pass).
