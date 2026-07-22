# Designer UX Feedback — Resolution Plan

> 📋 Response to [`Designer-and-maybe-output-port-concerns.md`](Designer-and-maybe-output-port-concerns.md)
> (user-testing feedback, 2026-07-21). Each item below records what the code actually does today,
> the proposed resolution, and implementation slices. Checkboxes are ticked as work completes.

## Summary

| # | Feedback item | Kind | Size | Status |
| --- | --- | --- | --- | --- |
| F1 | Dropdowns don't populate (e.g. Join's `joinType`) | **Bug** | S | ✅ done |
| F2 | Output ports of one node → single object keyed by port name | Feature | M | ✅ done |
| F3 | Fan-in with a single obvious output / simpler mental model | UX + docs | S | ✅ done |
| F4 | Loop construct is undiscoverable; wants a "subwindow" body view | Feature | L | ✅ done (regions + snippet; sub-canvas deferred post-MVP per Q4) |
| F5 | Try/catch construct is undiscoverable | Feature | L (shares F4) | ✅ done (regions + snippet + port fix) |

Recommended order: **F1 → F2 → F3 → F4 → F5**. F1 is a plain bug. F2/F3 are small and
compound the recent fan-in UX work. F4/F5 share one "sub-graph visualization" foundation and
should be planned as their own phase-style breakdown once Q4 is resolved.

---

## F1 — Editor dropdowns don't populate 🐛

**Finding.** `PropertyEditor.razor` renders `Dropdown` properties as a `<select>` over
`Property.AllowedValues` — but almost every builtin module declares `PropertyEditorType.Dropdown`
**without** passing `AllowedValues`, so the select renders empty. Only `ScriptModule` (`language`)
does it right. Affected (from a repo-wide search): `DataJoinModule.joinType`,
`StringTransformModule.operation`, `JsonTransformModule.operation`, `AggregateModule.operation`,
`LogModule` (level), `HttpRequestModule` ×5, `FileReadModule.readAs`, `FileWriteModule.mode`,
`CompressModule.format` + `compressionLevel`, `DecompressModule.format`, `FanInModule.mode`,
`S3Module.operation`, `AzureBlobModule.operation`, `DatabaseQueryModule` ×2,
`DatabaseExecuteModule`, `DatabaseTransactionModule` ×2, `DatabaseBulkInsertModule`.

The valid values already exist in each module's `ValidateConfiguration` (and usually in the
property description text) — they just were never surfaced in the schema.

**Resolution.**

- [x] F1.1 Add `AllowedValues` to every builtin `Dropdown` property listed above, sourced from the
      module's own validation logic so they cannot drift apart. Where validation accepts values the
      description doesn't mention, trust the validation. *(Done — 25 dropdowns across 16 modules;
      also promoted `FanInModule.mode` from Text to Dropdown with default `concat`.)*
- [x] F1.2 Add a schema guard test: every `ModulePropertyDefinition` with
      `EditorType == Dropdown` across all registered builtin modules must have a non-empty
      `AllowedValues`, and each `DefaultValue` must be contained in it.
      *(`Workflow.Tests/Modules/DropdownSchemaGuardTests.cs` — 51 tests.)*
- [x] F1.3 UI hardening: when a Dropdown property has no `AllowedValues` (e.g. a third-party
      module), `PropertyEditor` falls back to a free-text input instead of an empty select.
      *(Also Q1: non-required dropdowns get an "(auto)" option that stores null, not "".)*
- [x] F1.4 Verify end-to-end: Join node's `joinType` shows inner/left/full in the designer.
      *(Verified over REST: joinType=[inner,left,full], fanin.mode=[concat,merge,first,last],
      http.method=[GET…OPTIONS].)*

---

## F2 — Map one node's output ports into a single object 📦

**Feedback.** "If I have a component with non-error output ports `foo, bar, baz` I want an easy
single JSON output where those ports are properties of the resulting object."

**Finding.** Fan In's `merge` mode is a **dictionary union** of branch payloads (last-writer-wins),
so port names are lost. However the engine already delivers everything needed: each downstream
node receives `{sourceNodeId}.{portName}`-prefixed inputs alongside `__incomingBranches__`
(`WorkflowExecutor.BuildNodeInputs`), and each branch snapshot is the source's full output dict.
What's missing is a mode that **keys each branch by its source port name**.

**Resolution.** Add a `named` fan-in mode (name TBD — see Q2): output is
`{ foo: <foo payload>, bar: <bar payload>, baz: <baz payload> }`.

- [x] F2.1 Engine: extend the branch snapshots (or a parallel `__incomingBranchMeta__` input) with
      the source connection's `SourcePortName` + `SourceNodeId`, preserving connection order.
      Additive — existing modes ignore it. *(Done — `WorkflowExecutor.GatherNodeInputs` emits
      index-aligned `__incomingBranchMeta__`, including for skipped predecessors.)*
- [x] F2.2 `FanInModule`: add `FanInMode.Named` — result object keyed by source port name; on key
      collision (two branches from equally-named ports of different nodes) fall back to
      `nodeId.port` keys for the colliding entries. Update `mode` validation + `AllowedValues`
      (F1 covers the dropdown itself). *(Done — also positional `branch{i}` keys when metadata is
      absent; the value is the port's payload, falling back to the whole branch snapshot.)*
- [x] F2.3 Designer: the "drop Fan In on a node's output side" gesture (and the multi-select
      "Merge outputs → Fan In" action) pre-sets `mode=named` when wiring **multiple distinct ports
      of one node** — that gesture's intent is exactly this feature. *(Done — the drop gesture
      pre-sets `named`; the multi-node context-menu action keeps `merge`.)*
- [x] F2.4 Tests: module-level (named merge, ordering, collision fallback, empty branches) +
      engine metadata plumbing + designer gesture pre-setting the mode. *(4 unit tests + 1 engine
      end-to-end test — 23 fan-in tests green; 295 UI tests green.)*
- [x] F2.5 Docs: fan-in mode table in the module docs / `docs/` gains `named` with an example.
      *(`docs/advanced-flow-control.md` mode table updated.)*

---

## F3 — "Fan-in with a single output" mental model 🧠

**Feedback.** "It is confusing that there is no way to just have a Fan-in with a single output…
take the input properties and have them all just map to an output object."

**Finding.** This is mostly F2 plus presentation: Fan In *does* funnel to one logical result, but
its schema exposes three ports (`result`, `count`, `done`), which reads as "multiple outputs" on
the canvas, and nothing in the UI explains the modes.

**Resolution (after F2).**

- [x] F3.1 Reorder/re-describe Fan In's outputs so `result` is unmistakably the primary port
      (description text; port order already puts `result` first — verify). *(Done — `result`
      described as "⭐ … connect downstream nodes here"; `count`/`done` marked "Auxiliary".)*
- [x] F3.2 Designer polish: when the fan-in drop gesture auto-wires, show a toast naming the mode
      chosen ("Merged 3 outputs into one object (named mode)") so behaviour is discoverable.
      *(Done — both gestures toast their mode.)*
- [x] F3.3 Docs: a short "Combining outputs" how-to in `docs/designer.md` covering the drop
      gesture, the context-menu action, and the mode table. *(Done.)*
- [x] F3.4 ~~(Q3) Decide whether `count`/`done` should be hidden-by-default~~ RESOLVED (Q3):
      keep all ports visible; no change.

---

## F4 — Loop construct exposure ("subwindow") 🔁

**Feedback.** "I don't understand how to build a loop in the UI… expose the loop construct, almost
like a 'subwindow' to aid in visualizing the loop structure."

**Finding.** The engine has `builtin.loop.foreach` (ports `loopBody`/`results`/`count`/`errors`/
`done`) and `builtin.loop.while`, plus `builtin.break`/`builtin.continue`. The loop *body* is the
sub-graph hanging off the `loopBody` port — a pure convention that nothing in the designer
explains or visualizes. The palette shows the modules but gives no hint that `loopBody` edges
re-enter per item.

**Resolution.** Two-stage: make loops *understandable* cheaply now, *visual* properly later.

- [x] F4.1 (cheap, now) Designer affordances: distinct node styling for loop modules (🔁 badge),
      a callout in the properties panel explaining "nodes connected from `loopBody` run once per
      item; their terminal output feeds `results`", and a `docs/designer.md` loops section with a
      worked example. *(Done — 🔁 icon, 💡 properties-panel callout for foreach/while, docs.)*
- [x] F4.2 (cheap, now) Canvas hint: render `loopBody` edges with a distinct style (dashed +
      loop-back arrowhead) so body sub-graphs read differently from the main flow.
      *(Done — `loopBody`/`try`/`catch`/`finally` edges render dashed via
      `NodePorts.IsStructuralPort`.)*
- [x] F4.3 (foundation) Body-region visualization: compute the sub-graph reachable from
      `loopBody` (stopping at the loop node's other ports) and render a soft container/halo
      around it on the canvas — the "subwindow" feel without new document structure.
      *(Done — see [`Designer-F4-F5-SubGraph-Regions.md`](Designer-F4-F5-SubGraph-Regions.md):
      `StructuralRegions` helper + `CanvasView` region halos.)*
- [x] F4.4 ~~(Q4) Decide: containment region vs sub-canvas~~ RESOLVED (Q4): region/halo on the
      main canvas (F4.3); collapsible sub-canvas deferred post-MVP. F4.3 gets its own breakdown
      doc before implementation (Q5).
- [x] F4.5 Starter template: "Loop over items" canvas snippet (palette or context menu → inserts
      foreach + body skeleton wired correctly). *(Done — canvas context menu →
      "Insert loop skeleton", single undo.)*

---

## F5 — Try/catch construct exposure 🛡️

**Feedback.** Same shape as F4 for `builtin.trycatch` (which wraps a sub-graph error boundary;
its schema exposes **no output ports** — routing is via try/catch/finally conventions).

**Resolution.** Ride the F4 foundation; do not build separately.

- [x] F5.1 Same cheap affordances as F4.1/F4.2: badge, properties-panel callout, docs section,
      distinct edge styling for `catch`/`finally` routes. *(Done — 🛡️ icon, callout, dashed
      routes, docs "Error handling (Try/Catch)" section.)*
- [x] F5.2 Same region visualization as F4.3 once it exists (try body + catch body as two regions).
      *(Done — try/catch/finally regions, kind-tinted.)*
- [x] F5.3 Starter template: "Try / catch" canvas snippet. *(Done — canvas context menu →
      "Insert try/catch skeleton", single undo.)*
- [x] F5.4 Review whether `TryCatchModule`'s empty output-port schema renders sensibly in the
      designer today (a node with zero outputs may confuse the port-alignment/edge code).
      *(Confirmed broken — it fell back to a bogus generic `output` port, making try/catch
      unwireable in the UI. Fixed: `NodePorts` now surfaces the conventional dynamic ports
      `try`/`catch`/`finally`/`done` for `builtin.trycatch`; the server already skips port-name
      validation for this module.)*

---

## Questions — RESOLVED ✅ (2026-07-21)

- [x] **Q1 (F1):** For dropdowns whose validation is more permissive than the docs (e.g.
    `DecompressModule.format` optional/inferred), should the dropdown include an explicit
    "(auto)" empty option? *Proposed: yes, render an empty option when the property is
    not required.*
- **RESOLVED: Agreed.**
- [x] **Q2 (F2):** Mode name for port-keyed aggregation: `named`, `byPort`, or `map`?
    *Proposed: `named`.*
- **RESOLVED: `named` (proposed default, no objection).**
- [x] **Q3 (F3):** Hide `count`/`done` auxiliary ports by default in the designer (with a
    "show all ports" toggle), or keep all ports always visible? *Proposed: keep visible for
    MVP; revisit with F4 styling work.*
- **RESOLVED: Agreed — keep visible; F3.4 closed as "leave as-is".**
- [x] **Q4 (F4/F5):** Sub-graph visualization approach — (a) region/halo on the main canvas
    (cheaper, keeps one document), or (b) collapsible sub-canvas "subwindow" (matches the
    feedback wording, much larger scope)? *Proposed: (a) for the next milestone, with (b)
    captured as a post-MVP phase doc if the region approach still confuses testers.*
- **RESOLVED: (a). Users agree in testing; (b) deferred as post-MVP.**
- [x] **Q5 (scope):** Should F4/F5 get a full phase-style breakdown doc (like
    `phases/Phase3-4-*`) before any implementation, or implement the cheap slices (F4.1/F4.2/
    F5.1) immediately and only write the breakdown for the visualization foundation?
    *Proposed: implement cheap slices immediately; breakdown doc for F4.3+/Q4(b).*
- **RESOLVED: Agreed — cheap slices now; breakdown doc for the F4.3 region-visualization
  foundation before that work starts.**--

*Created 2026-07-21. Update statuses/checkboxes in place as items land.*
