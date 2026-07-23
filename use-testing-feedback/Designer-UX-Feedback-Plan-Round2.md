# Designer UX Feedback — Resolution Plan (Round 2)

> 📋 Response to [`Designer-module-option-editor-concerns.md`](Designer-module-option-editor-concerns.md)
> (stakeholder review, 2026-07-23). Same format as
> [`Designer-UX-Feedback-Plan.md`](Designer-UX-Feedback-Plan.md): each item records what the
> code does today, the proposed resolution, and slices. Checkboxes tick as work lands.

## Summary

| # | Feedback item | Kind | Size | Status |
| --- | --- | --- | --- | --- |
| G1 | Arrow dragging renders ghost in top-left corner (#8) | **Bug** | S | ⏳ planned |
| G2 | No way back to the workflow list / designer from the nav bar (#6) | **Bug/gap** | S | ⏳ planned |
| G3 | Missing tooltips & docs on property options (#1, #4) | UX | S | ⏳ planned |
| G4 | Easy variable-based option values (#5) | Feature | M | ⏳ planned |
| G5 | Modal (expanded) option editor (#3) | Feature | M | ⏳ planned |
| G6 | Options ↔ output port relationship unclear (#7) | UX | S–M | ⏳ planned |
| G7 | Consistency of layout/interaction across modules (#2) | Cross-cutting | — | ⏳ audit |

Recommended order: **G1 → G2 → G3 → G4 → G6 → G5 → G7** (bugs first; G3/G4 share the
property-editor surface; G5 builds on the editor once tooltips/variables exist; G7 is an
audit pass across the rest).

---

## G1 — Arrow-drag ghost renders in the top-left 🐛 (#8)

**Finding (root cause confirmed).** While drawing a connection, `CanvasView.OnViewportPointerMove`
sets the ghost endpoint from `e.OffsetX/OffsetY`. In the browser, **offset coordinates are
relative to the event's *target* element**, not the element the handler is attached to. The
moment the pointer crosses over a node or port (exactly what happens when you drag toward a
target), the offsets become tiny node-relative values → the bezier endpoint snaps toward the
viewport's top-left. The connection itself still commits correctly because the drop is handled
by the input port's own `pointerup`. The **rubber-band select** (`OffsetX` in
`OnViewportPointerDown`/`Move`) and the **fan-in drop-zone arming** (`OnDragOver`) share the
same latent defect.

**Resolution.** Use viewport-relative coordinates derived from **client** coordinates:

- [ ] G1.1 Extend `dotflowCanvas.measure` (canvas.js) to also return the viewport rect's
      `left`/`top`; cache it in `CanvasView` on pointer-down / connect-start (re-measure cheap).
- [ ] G1.2 Compute cursor positions as `ClientX - rect.Left` / `ClientY - rect.Top` in the
      ghost path, rubber band, and drag-over arming; keep an offset fallback when JS is
      unavailable (bUnit).
- [ ] G1.3 Tests: ghost path endpoint follows client coords when the event target is a child
      node (regression for the top-left snap); rubber band unaffected.

## G2 — Navigation back to workflows/designer 🧭 (#6)

**Finding.** `TopBar.razor` links to Scripts / Monitor / Modules / Settings, but has **no
Workflows/home link** — even the "🌊 DotFlow Designer" brand is a plain, non-clickable span.
From `/scripts` (e.g. after "Edit in Script Studio") the only way back is Studio's own return
flow or the browser Back button.

**Resolution.**

- [ ] G2.1 Make the brand a link to `/` and add a `📋 Workflows` nav button (first position,
      `data-testid="nav-workflows"`), highlighting nothing special (consistent with siblings).
- [ ] G2.2 bUnit test: TopBar navigates to `/` from brand + button.

## G3 — Tooltips & docs on property options 💬 (#1, #4)

**Finding.** Every `ModulePropertyDefinition` already carries a `Description` (they're rich —
"inner (default) / left / full~ 🔗" etc.) and the DTO projects it, but **`PropertyEditor`
never renders it** — only `DisplayName` + a required `*`. The information exists end-to-end
and is simply dropped at the last step.

**Resolution.**

- [ ] G3.1 `PropertyEditor`: render the description as a hover **tooltip** (`title` on the
      label + a small `ⓘ` affordance) and, for `MultilineText`/`Json`/`Code` editors, as a
      muted helper line under the label (hover is awkward on tall editors).
- [ ] G3.2 Show the property's **default value** in the tooltip when one is declared
      ("Default: `inner`").
- [ ] G3.3 Sweep builtin modules for empty/unhelpful descriptions; fill gaps (survey says most
      are good — expect a handful).
- [ ] G3.4 bUnit tests: title/helper rendered from Description; absent when no description.

## G4 — Variable-friendly option values 🔗 (#5)

**Finding.** The binder already resolves `{{Variable.Name}}` (and `{{NodeId.Output}}`)
templates in any string property (`PropertyBinder`, `VariablePrefix = "Variable."`), and many
descriptions advertise it — but the designer gives **no discovery or assistance**: users must
know the syntax. The API exposes `/api/v1/variables` (client: `SystemClient`/variables client)
for the pick-list.

**Resolution.** A lightweight **insert-variable picker** on text-like editors:

- [ ] G4.1 Framework-free `VariableTokens` helper (State): builds `{{Variable.X}}` tokens,
      validates names, lists `{{NodeId.Output}}` candidates from the document's upstream nodes
      (nodes with a path to the selected node — reuse graph traversal from `StructuralRegions`).
- [ ] G4.2 `PropertyEditor`: a small `{{x}}` button on Text/Expression/MultilineText editors
      opening a dropdown of (a) workflow variables fetched once per designer session, and
      (b) upstream node outputs; picking one inserts the token at the end of the current value
      (cursor-position insertion is post-MVP).
- [ ] G4.3 Visual hint when a value *contains* a template token (mono font + subtle badge), so
      bound values read differently from literals.
- [ ] G4.4 Tests: token insertion, upstream-output candidate list, badge rendering; fetch
      failure degrades gracefully (picker hidden, manual syntax still works).

## G5 — Modal (expanded) option editor 🪟 (#3)

**Finding.** Properties live in the fixed 300px right panel; `Code`/`Json` get Monaco but
cramped. A modal exists already in the codebase (`df-modal` used by rename/save dialogs), so
this is composition, not new infrastructure. Note the stakeholder ask is "keep the user
focused **without navigating away**" — an in-designer modal, not a page.

**Resolution.**

- [ ] G5.1 "⤢ Expand" button in the PropertiesPanel header opening a **modal editor** for the
      selected node: same `PropertyEditor` list, wider layout (labels left, editors right),
      Monaco at comfortable size; Apply/Cancel semantics identical to the panel (buffer +
      single undoable apply).
- [ ] G5.2 Reuse: extract the panel's node-edit buffer logic so panel and modal share one
      state object (no divergence).
- [ ] G5.3 Tests: modal opens/closes, edits apply as one command, cancel discards.

## G6 — Options ↔ output port relationship 📤 (#7)

**Finding.** The relationship is invisible today: nothing in the properties panel indicates
which output ports the node produces or how options change them (e.g. FanIn's `mode`/`meta`
reshape `result` and hide ports; `outputMode=merged` collapses everything to `output`; HTTP's
ports carry statusCode/body/…). Round-1 work added the *mechanics*; this asks for *visibility*.

**Resolution.**

- [ ] G6.1 PropertiesPanel: an **"Outputs" summary strip** for the selected node — the live
      port list (from `NodePorts.Outputs`, so meta/merged selections reflect instantly) with
      each port's schema description as tooltip.
- [ ] G6.2 When `outputMode`/FanIn-`meta` changes, the strip re-renders immediately (already
      guaranteed by `NodePorts`-driven rendering) — add a one-line caption under those
      selectors: "changes this node's output ports" so cause→effect is explicit.
- [ ] G6.3 Tests: strip lists ports; toggling merged/meta updates the strip.

## G7 — Consistency audit 🎨 (#2)

**Finding.** No single defect named; treat as an acceptance pass over G3–G6 rather than a
standalone build item.

**Resolution.**

- [ ] G7.1 After G3–G6 land: audit all builtin modules in the designer for label casing,
      dropdown coverage (F1 guard already enforces), tooltip presence, editor-type sanity
      (e.g. things that should be `Json` vs `MultilineText`); file fixes as small follow-ups.
- [ ] G7.2 Update `docs/designer.md` (editing section) with the new tooltips, variable picker,
      modal editor, and outputs strip.

---

## Questions to resolve before implementation ❓

- [ ] **Q1 (G4):** Should the variable picker also offer `{{NodeId.Output}}` upstream-output
      tokens (richer, needs graph traversal) or **variables only** for the first cut?
      *Proposed: both — upstream outputs are cheap to compute and are the more common need.*
  - Agreed 
- [ ] **Q2 (G5):** Modal scope — properties only, or include the node name + G6 outputs strip
      for a full "node inspector" feel? *Proposed: include name + outputs strip; it's the same
      components and makes the modal genuinely better than the panel.*
  - Agreed 
- [ ] **Q3 (G3):** Tooltip style — native `title` attributes (zero cost, delay before showing)
      or a custom styled tooltip component (consistent, instant, more work)?
      *Proposed: native `title` + visible helper line for tall editors now; custom tooltips
      only if testers complain.*
  - Agreed but document as potential future improvement.
- [ ] **Q4 (G2):** Should Scripts/Monitor/Modules pages' top bars also gain the Workflows
      link (they use the same `TopBar`, so it's free) — any reason to scope it to the designer
      only? *Proposed: everywhere — it's one shared component.*
  - Agreed 

---

*Created 2026-07-23. Update statuses/checkboxes in place as items land.*
