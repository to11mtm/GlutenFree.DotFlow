# Designer UX Feedback — Resolution Plan (Round 2)

> 📋 Response to [`Designer-module-option-editor-concerns.md`](Designer-module-option-editor-concerns.md)
> (stakeholder review, 2026-07-23). Same format as
> [`Designer-UX-Feedback-Plan.md`](Designer-UX-Feedback-Plan.md): each item records what the
> code does today, the proposed resolution, and slices. Checkboxes tick as work lands.

## Summary

| # | Feedback item | Kind | Size | Status |
| --- | --- | --- | --- | --- |
| G1 | Arrow dragging renders ghost in top-left corner (#8) | **Bug** | S | ✅ done |
| G2 | No way back to the workflow list / designer from the nav bar (#6) | **Bug/gap** | S | ✅ done |
| G3 | Missing tooltips & docs on property options (#1, #4) | UX | S | ✅ done |
| G4 | Easy variable-based option values (#5) | Feature | M | ✅ done |
| G5 | Modal (expanded) option editor (#3) | Feature | M | ✅ done |
| G6 | Options ↔ output port relationship unclear (#7) | UX | S–M | ✅ done |
| G7 | Consistency of layout/interaction across modules (#2) | Cross-cutting | — | ✅ done (audit: flow-module property descriptions filled; docs updated) |

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

- [x] G1.1 Extend `dotflowCanvas.measure` (canvas.js) to also return the viewport rect's
      `left`/`top`; cache it in `CanvasView` on pointer-down / connect-start (re-measure cheap).
- [x] G1.2 Compute cursor positions as `ClientX - rect.Left` / `ClientY - rect.Top` in the
      ghost path, rubber band, and drag-over arming; keep an offset fallback when JS is
      unavailable (bUnit).
- [x] G1.3 Tests: ghost path endpoint follows client coords when the event target is a child
      node (regression for the top-left snap); rubber band unaffected.

## G2 — Navigation back to workflows/designer 🧭 (#6)

**Finding.** `TopBar.razor` links to Scripts / Monitor / Modules / Settings, but has **no
Workflows/home link** — even the "🌊 DotFlow Designer" brand is a plain, non-clickable span.
From `/scripts` (e.g. after "Edit in Script Studio") the only way back is Studio's own return
flow or the browser Back button.

**Resolution.**

- [x] G2.1 Make the brand a link to `/` and add a `📋 Workflows` nav button (first position,
      `data-testid="nav-workflows"`), highlighting nothing special (consistent with siblings).
- [x] G2.2 bUnit test: TopBar navigates to `/` from brand + button.

## G3 — Tooltips & docs on property options 💬 (#1, #4)

**Finding.** Every `ModulePropertyDefinition` already carries a `Description` (they're rich —
"inner (default) / left / full~ 🔗" etc.) and the DTO projects it, but **`PropertyEditor`
never renders it** — only `DisplayName` + a required `*`. The information exists end-to-end
and is simply dropped at the last step.

**Resolution.**

- [x] G3.1 `PropertyEditor`: render the description as a hover **tooltip** (`title` on the
      label + a small `ⓘ` affordance) and, for `MultilineText`/`Json`/`Code` editors, as a
      muted helper line under the label (hover is awkward on tall editors).
- [x] G3.2 Show the property's **default value** in the tooltip when one is declared
      ("Default: `inner`").
- [x] G3.3 Sweep builtin modules for empty/unhelpful descriptions; fill gaps (survey says most
      are good — expect a handful).
- [x] G3.4 bUnit tests: title/helper rendered from Description; absent when no description.

## G4 — Variable-friendly option values 🔗 (#5)

**Finding.** The binder already resolves `{{Variable.Name}}` (and `{{NodeId.Output}}`)
templates in any string property (`PropertyBinder`, `VariablePrefix = "Variable."`), and many
descriptions advertise it — but the designer gives **no discovery or assistance**: users must
know the syntax. The API exposes `/api/v1/variables` (client: `SystemClient`/variables client)
for the pick-list.

**Resolution.** A lightweight **insert-variable picker** on text-like editors:

- [x] G4.1 Framework-free `VariableTokens` helper (State): builds `{{Variable.X}}` tokens,
      validates names, lists `{{NodeId.Output}}` candidates from the document's upstream nodes
      (nodes with a path to the selected node — reuse graph traversal from `StructuralRegions`).
- [x] G4.2 `PropertyEditor`: a small `{{x}}` button on Text/Expression/MultilineText editors
      opening a dropdown of (a) workflow variables fetched once per designer session, and
      (b) upstream node outputs; picking one inserts the token at the end of the current value
      (cursor-position insertion is post-MVP).
- [x] G4.3 Visual hint when a value *contains* a template token (mono font + subtle badge), so
      bound values read differently from literals.
- [x] G4.4 Tests: token insertion, upstream-output candidate list, badge rendering; fetch
      failure degrades gracefully (picker hidden, manual syntax still works).

## G5 — Modal (expanded) option editor 🪟 (#3)

**Finding.** Properties live in the fixed 300px right panel; `Code`/`Json` get Monaco but
cramped. A modal exists already in the codebase (`df-modal` used by rename/save dialogs), so
this is composition, not new infrastructure. Note the stakeholder ask is "keep the user
focused **without navigating away**" — an in-designer modal, not a page.

**Resolution.**

- [x] G5.1 "⤢ Expand" button in the PropertiesPanel header opening a **modal editor** for the
      selected node: same `PropertyEditor` list, wider layout (labels left, editors right),
      Monaco at comfortable size; Apply/Cancel semantics identical to the panel (buffer +
      single undoable apply).
- [x] G5.2 Reuse: extract the panel's node-edit buffer logic so panel and modal share one
      state object (no divergence).
- [x] G5.3 Tests: modal opens/closes, edits apply as one command, cancel discards.

## G6 — Options ↔ output port relationship 📤 (#7)

**Finding.** The relationship is invisible today: nothing in the properties panel indicates
which output ports the node produces or how options change them (e.g. FanIn's `mode`/`meta`
reshape `result` and hide ports; `outputMode=merged` collapses everything to `output`; HTTP's
ports carry statusCode/body/…). Round-1 work added the *mechanics*; this asks for *visibility*.

**Resolution.**

- [x] G6.1 PropertiesPanel: an **"Outputs" summary strip** for the selected node — the live
      port list (from `NodePorts.Outputs`, so meta/merged selections reflect instantly) with
      each port's schema description as tooltip.
- [x] G6.2 When `outputMode`/FanIn-`meta` changes, the strip re-renders immediately (already
      guaranteed by `NodePorts`-driven rendering) — add a one-line caption under those
      selectors: "changes this node's output ports" so cause→effect is explicit.
- [x] G6.3 Tests: strip lists ports; toggling merged/meta updates the strip.

## G7 — Consistency audit 🎨 (#2)

**Finding.** No single defect named; treat as an acceptance pass over G3–G6 rather than a
standalone build item.

**Resolution.**

- [x] G7.1 After G3–G6 land: audit all builtin modules in the designer for label casing,
      dropdown coverage (F1 guard already enforces), tooltip presence, editor-type sanity
      (e.g. things that should be `Json` vs `MultilineText`); file fixes as small follow-ups.
- [x] G7.2 Update `docs/designer.md` (editing section) with the new tooltips, variable picker,
      modal editor, and outputs strip.

---

## G8 — Expression builder modal ƒx (follow-up, 2026-07-23)

**Ask.** An optional modal breakout on expression-accepting fields with hints and common
builders for **Variable** vs **input** sources.

**Resolution (implemented).**

- [x] G8.1 `ExpressionBuilder` state helper (framework-free): operators, `Compose(token, op,
      value)` with auto-quoting (numbers/booleans/pre-quoted stay raw), and the hint catalog.
- [x] G8.2 `PropertyEditor`: an **ƒx builder** button on every template-supporting editor
      (`Expression`, `Text`, `MultilineText`, `FilePath`, `DirectoryPath` — anything advertising
      `{{…}}` support) opens a modal —
      working-copy textarea, a builder row (source dropdown grouped **Variables** /
      **Inputs (upstream outputs)** + comparison operator + value → Insert appends the composed
      `{{…}}`), a syntax hints list, and Apply/Cancel writing through the normal value path.
      *(Initially Expression-only; broadened 2026-07-23 after user feedback on FilePath fields.)*
- [x] G8.3 Tests: 7 compose unit tests + 2 bUnit flow tests (build+apply round-trip; ƒx on all
      template fields but not booleans; hints shown).

---

## G9 — Safe SQL parameter builder 🛡️ (follow-up, 2026-07-23)

**Ask.** A similar assisted interface guaranteeing **safe parameters in SQL** (Database Query's
"Verbatim SELECT SQL. NOT template-expanded (D7) — use parameters for values").

**Finding.** SQL properties (`query`/`command`, `Code` editors) are deliberately never
template-expanded; values bind through the sibling `parameters` JSON map (`@name` placeholders,
`SqlParameterBinder` — mandatory parameterisation, no concatenation). Also verified: the binder
resolves `{{…}}` only in **top-level string** properties, so tokens nested inside the parameters
JSON would NOT resolve — the builder therefore produces **typed literals**, the supported path.

**Resolution (implemented).**

- [x] G9.1 `SqlParams` state helper (framework-free): SQL-node detection (`Code` property named
      query/command/sql + `parameters` Json map), name sanitising (strips `@:?`/invalid chars),
      `@name` placeholders, typed `Upsert`/`Remove`/`Parse` (numbers & booleans stay typed).
- [x] G9.2 PropertiesPanel: a **🛡️ SQL parameters** button on SQL-shaped nodes opens a modal —
      lists current parameters (insert-placeholder / remove per row), an add row
      (name + value → **Add + insert** upserts the map *and* appends `@name` to the SQL buffer),
      and D7 safety hints ("never paste values into the SQL"). Buffer-based; the panel's Apply
      commits both properties as usual.
- [x] G9.3 Tests: 5 `SqlParams` unit tests + 2 bUnit flow tests (add→placeholder→apply
      round-trip; button absent on non-SQL nodes).
- [x] G9.4 (follow-up 2026-07-28) **Bind parameter values from variables / inputs.** Users
      couldn't see how to use an input's value as a parameter — and tokens in parameter values
      didn't even resolve (the engine's PropertyBinder only template-expands module *inputs*,
      never properties). Added `SqlParameterTemplateResolver` (Workflow.Modules.Database):
      resolves `{{Variable.x}}` / `{{nodeId.port}}` in parameter **values** (whole-token →
      typed value; embedded → interpolated; unresolved → crisp `SqlParameterBindingException`),
      wired into Query + Execute between `Normalize` and `Bind` — so bound values still go
      through `DataParameter`s, never concatenated (D7). Modal gained a **"🔗 …or bind the value
      from a variable / input"** picker (fills value with the token + suggests the name) and a
      🔗 badge on bound rows. 4 module tests + 1 bUnit test. *(Transaction/BulkInsert parameter
      maps not yet wired — follow-up if requested.)*

---

## Questions — RESOLVED ✅ (2026-07-23)

- [x] **Q1 (G4):** Should the variable picker also offer `{{NodeId.Output}}` upstream-output
      tokens (richer, needs graph traversal) or **variables only** for the first cut?
      *Proposed: both — upstream outputs are cheap to compute and are the more common need.*
  - **RESOLVED: Agreed — both.**
- [x] **Q2 (G5):** Modal scope — properties only, or include the node name + G6 outputs strip
      for a full "node inspector" feel? *Proposed: include name + outputs strip; it's the same
      components and makes the modal genuinely better than the panel.*
  - **RESOLVED: Agreed — full node inspector (name + properties + outputs strip).**
- [x] **Q3 (G3):** Tooltip style — native `title` attributes (zero cost, delay before showing)
      or a custom styled tooltip component (consistent, instant, more work)?
      *Proposed: native `title` + visible helper line for tall editors now; custom tooltips
      only if testers complain.*
  - **RESOLVED: Agreed — native now. A custom styled tooltip component is recorded as a
    potential future improvement (post-MVP polish).**
- [x] **Q4 (G2):** Should Scripts/Monitor/Modules pages' top bars also gain the Workflows
      link (they use the same `TopBar`, so it's free) — any reason to scope it to the designer
      only? *Proposed: everywhere — it's one shared component.*
  - **RESOLVED: Agreed — everywhere via the shared component.**

---

*Created 2026-07-23. All items G1–G7 implemented 2026-07-23 — UI suite 344/344 green, solution
builds clean. Notes: G4's variable picker uses the document's own variables (no API fetch
needed); G5's modal shares the panel's edit buffer (zero divergence); custom styled tooltips
(Q3) remain a recorded future improvement.*
