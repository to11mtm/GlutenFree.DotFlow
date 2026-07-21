# Visual Workflow Designer 🎨

> Phase 3.3 — a browser-based visual designer for building, editing, running, and reviewing
> workflows. Blazor WebAssembly (MVP). Made with 💖 by Ami-Chan~ ✨

The designer lets you assemble workflows visually: drag modules onto a canvas, wire them
together, configure them, save, run, and watch execution live. It talks to the API only
through the public REST endpoints + the SignalR hub — no server-side designer logic.

- [Getting started](#getting-started)
- [Authentication](#authentication)
- [Workflow list](#workflow-list-s1)
- [Editing (S2)](#editing-s2)
- [Running (S3)](#running-s3)
- [Execution history (S5)](#execution-history-s5)
- [Keyboard shortcuts](#keyboard-shortcuts)
- [Troubleshooting](#troubleshooting)

---

## Getting started

1. Run the API (`dotnet run --project Workflow.Api`).
2. Run the designer host (`dotnet run --project Workflow.UI/Workflow.UI`), which serves the
   WASM client.
3. Set the API base URL: the client uses `Api:BaseUrl` from `wwwroot/appsettings.json`
   (empty = same origin as the host; use the host's dev proxy or configure CORS on the API
   via `Api:RealTime:AllowedOrigins` for a separate origin).

## Authentication

Open **⚙ Settings** and paste a JWT (or an API key). "Remember on this device" persists it
to `localStorage`. **Test connection** pings `GET /api/v1/status`. When the API runs with
`Api:Auth:Require=false` (dev default) the designer works anonymously.

## Workflow list (S1)

The landing page (`/`) lists workflows with search, and per-row **Open** / **▶ Run** /
**🗑 Delete** (with inline confirm). **＋ New Workflow** starts a blank canvas.

## Editing (S2)

- **Palette (left):** modules grouped by category with search. **Drag** a module onto the
  canvas to create a node (schema defaults pre-filled).
- **Canvas (center):** pan (drag background), zoom (wheel, about the cursor), **⤢ Fit**,
  and a corner **minimap** (click to navigate).
- **Select:** click a node; Ctrl-click to multi-select; Shift-drag for a rubber-band.
- **Move:** drag selected nodes (one undoable move per gesture).
- **Connect:** drag from an **output** port to a compatible **input** port. Cycles,
  self-connections, and duplicates are rejected with visual feedback.
- **Configure (right):** the **Properties** panel renders the right editor for each
  property (text, number, checkbox, dropdown, expression, JSON, code). `Code`/`Json` use
  the Monaco editor (with a plain-textarea fallback). **Apply** commits your edits.
- **Context menus:** right-click a node (Rename / Duplicate / Delete) or the canvas
  (Select all / Paste / Fit).
- **Undo/redo:** toolbar ↩ / ↪ or Ctrl+Z / Ctrl+Y (50-step history).
- **Save:** 💾 or Ctrl+S. Saving runs client structural checks **and** the server
  validate endpoint; blocking issues show in a dialog with jump-to-node links. The
  `●unsaved` indicator + a browser warning protect against losing edits.

## Running (S3)

**▶ Run** opens an inputs dialog (JSON), starts the execution, and enters **run mode**
(editing disabled). Nodes light up live via the SignalR hub: pending → running (pulse) →
completed/failed, with a progress bar and a run log. **⏹ Cancel** stops it; **✖ Close**
returns to editing. If the hub can't connect, the designer falls back to polling.

## Execution history (S5)

**🕘 History** lists past executions for the workflow. Selecting one enters a read-only
history view: the canvas paints the final node states, and the panel shows outputs/errors.
**↻ Re-run** starts a fresh execution. Share a failure with
`/designer/{id}?execution={executionId}`.

## Keyboard shortcuts

| Shortcut | Action |
|----------|--------|
| Ctrl+Z / Ctrl+Y (or Ctrl+Shift+Z) | Undo / Redo |
| Ctrl+A | Select all |
| Ctrl+C / Ctrl+V | Copy / Paste |
| Delete / Backspace | Delete selection |
| Ctrl+S | Save |

Shortcuts are suppressed while typing in an input/textarea.

## Troubleshooting

- **401/403 errors:** set a valid token in Settings (or run the API with auth disabled).
- **Blank canvas / CORS errors:** configure the API base URL, and allow the UI origin
  (`Api:RealTime:AllowedOrigins`) or use the host's dev proxy.
- **No live updates during a run:** the hub may be blocked (WebSockets) — the designer
  degrades to polling; check the run log for the notice.
- **Monaco doesn't load:** the editor falls back to a plain textarea automatically.

See also: [REST API](rest-api.md) · [Real-Time Hub](realtime.md) ·
[Designer architecture](designer-architecture.md).
