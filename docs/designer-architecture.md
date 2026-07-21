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

## Performance notes

- Node views are keyed by id (`@key`); pan/zoom mutate only the transform style, so panning
  a large graph does not re-render nodes.
- The minimap and edge layer recompute from node bounds only on render (cheap for typical
  workflow sizes). Rendered-thumbnail minimaps + auto-layout are deferred to 3.3.P6.

## Accessibility (MVP scope)

Toolbar/panel controls are keyboard-focusable with `aria`-labels on icon buttons and
visible focus rings from `tokens.css`. Canvas interactions are pointer-only in the MVP;
full canvas keyboard a11y is out of scope (noted for a later pass).
