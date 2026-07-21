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

### Combining outputs (Fan In) 🪄

Three ways to funnel multiple outputs into **one object** on the `result` port:

- **Drop gesture (one node, all ports):** drag **Fan In** from the palette over a node —
  dashed drop zones appear on every node's output side (green when armed). Drop it and *all*
  of that node's outputs are wired in with **`named` mode**: a node with `foo/bar/baz`
  outputs yields `{ "foo": …, "bar": …, "baz": … }`.
- **Context menu (many nodes):** select 2+ nodes → right-click → **Merge outputs → Fan In**.
  Wires each node's primary output in with **`merge` mode** (shallow union).
- **Manual:** drop a Fan In anywhere and connect edges yourself, then pick a `mode` in the
  Properties panel: `concat` (array), `merge` (union), `named` (keyed by source port),
  `first` / `last`.

Connect downstream nodes to **`result`** — `count` and `done` are auxiliary
(branch count / ordering-only activation).

### Loops 🔁

Drop **For Each** (`builtin.loop.foreach`) or **While** (`builtin.loop.while`) onto the
canvas. The loop **body** is simply the sub-graph you connect from the **`loopBody`**
output port — those nodes run **once per item** (For Each) or **per iteration** (While).
Body edges render **dashed** so the loop structure reads differently from the main flow,
and the Properties panel shows a 💡 callout explaining the convention.

- Connect `loopBody →` your per-item work (chain as many nodes as you like).
- The body's terminal output is collected into **`results`** (For Each).
- **`done`** fires after the last iteration — continue the main flow from there.
- `builtin.break` / `builtin.continue` inside the body control iteration.

Worked example: `HTTP (list) → For Each · loopBody → Transform → …` with
`For Each · done → next step` — see [Advanced Flow Control](advanced-flow-control.md).

### Error handling (Try/Catch) 🛡️

Drop **Try Catch** (`builtin.trycatch`). The designer exposes its conventional routing
ports — **`try`**, **`catch`**, **`finally`**, **`done`**:

- `try →` the guarded sub-graph.
- `catch →` runs only if a try node fails (error details flow in).
- `finally →` always runs.
- `done →` continue the main flow.

Like loop bodies, these routes render **dashed**. `rethrow` re-raises the error after
`finally`; `catchTypes` filters which error types are caught.

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
