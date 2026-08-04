# Designer Start & End Clarity — Plan

> 📋 Response to user feedback ([Designer-Start-And-End-Clarity.md](Designer-Start-And-End-Clarity.md)):
> users can't easily tell where a workflow *starts* and *ends* on the canvas, especially with many
> modules and connections. Validation should clearly indicate start/end — or point at the confusion
> when there are multiple candidates.
>
> **Revision 1 — proposal.** Open questions are at the bottom; the recommended answers are baked
> into the items so implementation can proceed if they stand.

## The ask

| # | Feedback | Reading |
| --- | --- | --- |
| 1 | Easily understand what is the 'start' vs the 'end' of a workflow in the UX | Passive, always-on visual delineation on the canvas |
| 2 | Hard to identify start/end when there are many modules and connections | The cue must survive visual noise — color/badge on the node itself, not just a list entry |
| 3 | Validation should show a clear indication of start and end | Surfacing, not just checking |
| 4 | …or indicate *where the confusion is* (multiple possible starts/ends) | Ambiguity is a first-class state, not an error |

## Guiding principle 🧭

**Inexperienced devs must get this for free.** The [Start/End modules](Designer-Start-End-Modules-Plan.md)
shipped earlier are opt-in — a beginner who doesn't know they exist gets no help. So the designer
should **derive** start/end from the graph itself, exactly the way the engine does, and paint it.
The marker modules then become the *stronger* version of the same signal, not the only one.

## Summary

| # | Item | Kind | Size | Status |
| --- | --- | --- | --- | --- |
| C1 | Structural start/end derivation (client-side) | Infra | S | ✅ done |
| C2 | Always-on start/end styling on canvas nodes (badge + edge color) | UX | M | ✅ done |
| C3 | Ambiguity indication (multiple starts/ends → softer "one of N" styling + status hint) | UX | S | ✅ done |
| C4 | Fix NodeView icon mismatch (canvas ignores module-declared icons) | Bug | S | ✅ done |
| C5 | Validation issues highlight their node on the canvas | UX | S | ✅ done |
| C6 | Tests | Tests | S | ✅ done |
| C7 | Docs | Docs | S | ✅ done |

---

## Findings 🔬

### F1 — The engine's definition is the only honest one

`WorkflowExecutor.GetStartNodes()` starts **every node with in-degree 0**, and
`GatherWorkflowOutputs()` collects from **every node with out-degree 0**. Whatever the designer
paints as "start"/"end" must match this, or it teaches beginners a lie. So:

- **Start** = node with no incoming connections.
- **End** = node with no outgoing connections.
- `builtin.start` / `builtin.end` are just nodes that are *always* in those sets (barring the
  misuse cases GraphValidator already warns about).

A single isolated node is both — that's fine and should render as both (it *is* the whole
workflow).

### F2 — The canvas already has all the state it needs

`DesignerDocument` (client-side) holds `Nodes` and `Connections`; in/out-degree is a trivial
client-side computation, no API involvement. `CanvasView.razor` re-renders on every document
mutation, so the derivation can be computed per-render (documents are small) or cached on the
document's `Changed` event.

### F3 — 🐛 NodeView has its own icon table and it's stale

Modules declare an `Icon` (e.g. `StartModule` → 🚀, `EndModule` → 🏁), and the **palette** renders
it from `ModuleSummaryDto.Icon`. But `NodeView.razor` computes icons from a private
`Node.ModuleId switch { ... }` that predates many modules — Start/End nodes render as ⚙️ on the
canvas. This is directly tied to this feedback (the markers users *did* place don't look special)
and will keep rotting for every new module. The schema is already on the node
(`DesignerNode.Schema`), so the fix is to prefer a schema/summary-provided icon and keep the
switch only as fallback for unknown modules.
**→ needs `Icon` added to `ModuleSchemaDto` (or resolved via the module summary list the designer
already loads) — verify which is cheaper during implementation.**

### F4 — Existing hooks for node styling and validation surfacing

- `NodeView` takes a `StateClass` parameter (used for run overlay: `df-node--running` etc.),
  passed from `CanvasView` via a `NodeStateClass` callback. Start/end styling should **not** ride
  this channel — run state must win visually during runs. A separate computed class on the node
  root is cleaner.
- `GraphIssue` already carries an optional `NodeId`, and the save dialog already has a
  `JumpTo(nodeId)` affordance. What's missing is the canvas *showing* which node an issue points
  at (C5) — cheap, and it generalizes rule 4 of the ask beyond start/end.

### F5 — What "confusion" looks like in practice

Multiple in-degree-0 nodes are *legal* (they all run, in parallel) and sometimes intended
(fan-in). Same for multiple terminal nodes. So ambiguity must be **informative, not alarming**:
distinct "one of N starts" presentation + a one-line status hint, no warning triangles unless the
existing `builtin.start`/`builtin.end` misuse rules fire. A disconnected node that someone just
dropped on the canvas is technically a start *and* an end — that's the most common "confusion"
case for a beginner and the styling should make it look obviously unwired rather than decorated.

---

## Proposed design 🎨

### The visual language

| State | Treatment |
| --- | --- |
| The single start node | Green left edge + `â–¶ Start` pill badge above the header |
| The single end node | Checkered/neutral right edge + `⏹ End` pill badge |
| One of N starts | Same badge, but reading `â–¶ Start 1 of 3`, dashed edge accent |
| One of N ends | Same, `⏹ End 2 of 2` |
| Disconnected node (both) | Muted "unwired" treatment (dashed outline) instead of both badges |
| `builtin.start` / `builtin.end` node | Same treatment as above + its real 🚀/🏁 icon (F3) |

Colors from existing tokens: start = `--df-state-completed`-family green, end = a neutral/dark
checkered motif (avoid red — end is not failure). All treatments are borders/badges *outside* the
run-state channel so `df-node--running`/`--completed` still dominate during runs.

### The status line

`StatusBar` gains a start/end summary: `▶ 1 start · ⏹ 1 end` in the common case;
`▶ 3 starts (parallel) · ⏹ 2 ends` when plural, clickable to cycle-jump between them (reuses
`JumpTo`). This is the "validation UI shows the start and end" half of the ask without a modal.

---

## Items

### C1 — Structural derivation 🧮

- [x] C1.1 `GraphTopology` (or extend an existing state helper): given a `DesignerDocument`,
      return `StartNodeIds` (in-degree 0) and `EndNodeIds` (out-degree 0). Dangling connections
      (already a validation error) count as edges anyway — don't special-case them.
- [x] C1.2 Isolated nodes appear in both sets; expose `IsolatedNodeIds` for the unwired styling.
- [x] C1.3 Recompute on document `Changed` (or per-render — measure; docs are small).

### C2 — Canvas styling 🖌️

- [x] C2.1 `CanvasView` computes topology and passes role (`Start`/`End`/`Both`/`None` + index/count)
      to `NodeView` as a new parameter (not via `StateClass`, F4).
- [x] C2.2 `NodeView` renders the pill badge + `df-node--role-start` / `df-node--role-end` /
      `df-node--unwired` classes.
- [x] C2.3 CSS in `tokens.css` per the visual-language table; verify run-state classes still win.

### C3 — Ambiguity indication 🔀

- [x] C3.1 Badge text carries `n of N` when plural; dashed accent variant.
- [x] C3.2 StatusBar summary line with counts, `(parallel)` note for multiple starts, and
      click-to-jump cycling.

### C4 — Icon fix 🐛

- [x] C4.1 Surface module `Icon` to the client (schema DTO or module summary lookup — see F3).
- [x] C4.2 `NodeView.Icon` prefers it; keep the switch as unknown-module fallback.
- [x] C4.3 Palette and canvas now agree; 🚀/🏁 appear on canvas nodes.

### C5 — Issues highlight their node 📍

- [x] C5.1 Pass current `issues` into `CanvasView`; nodes referenced by a `GraphIssue.NodeId` get
      `df-node--issue-warning` / `df-node--issue-error` (border tint + the existing âš  affordance
      with the issue message as tooltip).
- [x] C5.2 This makes the existing Start/End misuse warnings (multiple `builtin.start`, non-terminal
      `builtin.end`) visible *at the node*, which is rule 4 of the ask.

### C6 — Tests 🧪

- [x] C6.1 Topology derivation: linear chain, diamond, multiple starts/ends, isolated node, empty
      doc, self-referencing doc with dangling connection.
- [x] C6.2 bUnit: badge presence/text on `NodeView`/`CanvasView` for the table's states
      (follow `DesignerPageTests` structure).
- [x] C6.3 bUnit: StatusBar summary counts; issue-highlight class applied when a `GraphIssue`
      carries a `NodeId`.
- [x] C6.4 Icon: canvas node for `builtin.start` shows 🚀 (regression for F3).

### C7 — Docs 📚

- [x] C7.1 Extend the "Start & End Markers" section of
      [`docs/advanced-flow-control.md`](../docs/advanced-flow-control.md): the designer now shows
      start/end automatically; markers are for pinning intent, not the only signal.

---

## Open questions ❓ (recommended answers baked in above)

- [x] **Q1 — Structural derivation vs. markers-only?** ✅ **Decided 2026-08-04: structural** (F1,
      guiding principle). Markers-only was rejected — it leaves beginners who don't use the
      markers with nothing, the exact population this feedback is about.
- [x] **Q2 — Badges + colored edges, or color only?** ✅ **Decided 2026-08-04: both** — pill badges
      with the words "Start"/"End" plus colored edge accents. Color alone is not self-describing
      (and is an accessibility risk).
- [x] **Q3 — Should multiple starts ever *warn* structurally?** ✅ **Decided 2026-08-04: no** —
      informative styling + status hint only (F5). The existing `builtin.start` misuse warnings
      stay as-is.
- [x] **Q4 — Is C5 (generic issue→node highlighting) in scope?** ✅ **Decided 2026-08-04: yes** —
      it's the mechanism rule 4 of the ask wants, and it benefits every existing validation rule.

---

## What shipped 📦

| File | Change |
| --- | --- |
| `Workflow.UI.Client/Designer/State/GraphTopology.cs` | New — engine-faithful start/end/isolated derivation with per-node `NodeRole` (index/count for "n of N"). |
| `Workflow.UI.Client/Designer/Components/NodeView.razor` | Role badges + `df-node--role-start/-end/-ambiguous/-unwired` classes; issue marker with tooltip; icon now prefers the catalog icon (`IconOverride`), with `builtin.start`/`builtin.end` added to the fallback table. |
| `Workflow.UI.Client/Designer/Components/CanvasView.razor` | Computes topology per render; new `ModuleIcon` and `Issues` parameters; worst-severity-per-node issue lookup. |
| `Workflow.UI.Client/Designer/Components/StatusBar.razor` | `▶ n start(s) · ⏹ n end(s)` summary with `(parallel)` note and click-to-cycle `OnJumpTo`. |
| `Workflow.UI.Client/Pages/Designer.razor` | Caches module icons from the catalog; passes `ModuleIcon`/`Issues` to the canvas (both modes) and `OnJumpTo` to the status bar. |
| `Workflow.UI.Client/wwwroot/css/tokens.css` | `--df-role-start`/`--df-role-end` tokens; role/badge/unwired/issue/statusbar rules, placed before selection/run-state rules so those still win. |
| `Workflow.Tests.UI/State/GraphTopologyTests.cs` | New — 7 tests (chain, diamond, plural, isolated, empty, dangling, cycle). |
| `Workflow.Tests.UI/Components/StartEndClarityTests.cs` | New — 15 bUnit tests covering C2–C5 and the StatusBar summary/cycling. |
| `docs/advanced-flow-control.md` | "The Designer Shows Start & End Automatically" subsection under Start & End Markers. |

**Verification.** `Workflow.Tests.UI` 596/596 green (574 pre-existing + 22 new, no regressions).
Engine/server untouched — this is presentation-only, per F1/F2.

---

*Created 2026-08-04. Findings verified against `WorkflowExecutor.cs`, `GraphValidator.cs`,
`NodeView.razor`, `CanvasView.razor`, `StatusBar.razor`, and the Start/End modules plan.
Implemented and verified 2026-08-04.*
