# Script Studio 💻

> Phase 3.4 — an in-browser editor for writing, testing, and managing the scripts that power
> `builtin.script` nodes and script libraries. Made with 💖 by Ami-Chan~ ✨

Script Studio lives at **`/scripts`** in the DotFlow designer app (top bar → **💻 Scripts**). It's
a thin front-end over the shipped scripting backend: it speaks the existing `/api/v1/scripts/*`
endpoints and reuses the same lazy-loaded Monaco editor as the visual designer — no new server code.

- [Opening the studio](#opening-the-studio)
- [Choosing a language](#choosing-a-language)
- [IntelliSense & the API reference](#intellisense--the-api-reference)
- [Templates](#templates)
- [Testing a script](#testing-a-script)
- [Libraries](#libraries)
- [Editing a designer node](#editing-a-designer-node)
- [Keyboard shortcuts](#keyboard-shortcuts)

## Opening the studio

Navigate to **`/scripts`**, or deep-link straight to a library with **`/scripts/{libraryId}`**.
From the visual designer, a `builtin.script` node's properties panel has an **"Edit in Script
Studio →"** link (see [Editing a designer node](#editing-a-designer-node)).

```text
┌────────────────────────────────────────────────────────────────────────────────┐
│ 🌊 Script Studio  [Language JavaScript ▾] [📄 Templates ▾] [📚 Libraries ▾] [💾] │
├───────────────┬──────────────────────────────────────────────┬─────────────────┤
│ API REFERENCE │  EDITOR (Monaco · JS/TS · Lua · Python)       │ TEST            │
│ 🔍 filter…    │  1  const orders = input.orders;              │ Inputs (JSON)   │
│ ▾ Variables   │  2  workflow.logInfo(orders.length);          │ { "orders": [] }│
│  getVariable  │  3  return { count: orders.length };          │ ☑ network ☐ files│
│ ▾ Logging     │                                               │ ── result ──    │
│  logInfo …    │                                               │ ✅ 12ms         │
├───────────────┴──────────────────────────────────────────────┴─────────────────┤
│ ✓ JavaScript · 📚 order-utils · 🟢 API                                          │
└──────────────────────────────────────────────────────────────────────────────────┘
```

## Choosing a language

The **Language** dropdown is populated from `GET /api/v1/scripts/languages` (the registered
executors — JavaScript, C#, and Lua for the MVP). **Python** is offered for editing and syntax
highlighting, but running it is disabled until the Python executor ships — a banner explains this
and the **▶ Test** button stays disabled.

## IntelliSense & the API reference

The left rail is a searchable, category-grouped reference for the `workflow.*` API (Variables,
Logging, Utilities, Context, HTTP, Files). Click a method to insert `workflow.method(...)` at the
cursor and see its signature + docs. Gated calls (HTTP / file system) are marked with a 🔒.

For **JavaScript**, the same catalog also drives Monaco **completions** (type `workflow.`) and
**hover** docs. C#/Lua get the reference panel today; live completions for them are a later phase.
The catalog is kept in sync with the real `IWorkflowScriptApi` contract by a drift-guard test, so
it never lies about what's available. See [Scripting Engine](scripting.md) for the full API.

## Templates

**📄 Templates** offers ≥10 starter snippets grouped by category (HTTP, data transforms, JSON/CSV,
variables, logging, hashing, error handling, files, and the "database via node composition"
pattern). Templates are filtered to the current language (toggle **All languages** to see the
rest). Inserting into an empty editor replaces it; into a non-empty editor you're asked whether to
insert at the cursor or replace everything.

## Testing a script

The right rail runs the current script in the sandbox via `POST /api/v1/scripts/test`:

- **Inputs (JSON)** — a JSON object surfaced to the script as `input`.
- **Config** — `timeout` (seconds), **network**, and **files** toggles (clamped to host ceilings).
- **▶ Test** — sends `{ language, code, inputs, libraries, config }` and shows the outcome.

Results render inline: a success/failure banner with duration, the pretty-printed **result**, the
captured **logs** (filter by level, search the text), and any staged **variable updates**. A script
*error* is a normal result with `success: false` — its message shows in the panel, not a toast.
Inputs are remembered per language.

## Libraries

**📚 Libraries** lists script libraries (`GET /scripts/libraries`), filtered to the current
language. **Open** loads one into the editor (and the status bar shows the active library + a
`●unsaved` marker when you've edited it). **💾 Save** updates an open library (`PUT`) or saves the
editor as a new one — id / name / description / language / exported functions. Validation and
dependency-cycle errors from the server surface in the dialog. **Delete** (with confirm) removes a
library and clears the editor if it was open.

## Editing a designer node

In the visual designer, selecting a `builtin.script` node shows **"Edit in Script Studio →"** in
the properties panel. It opens Script Studio seeded with the node's code + language; clicking
**✅ Apply to node** returns the edited code to the node as a single undoable edit and navigates
back to the designer.

## Keyboard shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl/Cmd + Enter` | Run the test |
| `Ctrl/Cmd + S` | Open the save dialog |

---

See also: [Scripting Engine](scripting.md) · [Designer](designer.md) ·
[Designer Architecture & React-Port Guide](designer-architecture.md).
