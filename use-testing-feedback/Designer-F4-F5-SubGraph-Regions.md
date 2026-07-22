# F4/F5 — Structural Sub-Graph Visualization & Starter Snippets (Design)

> 📋 Breakdown doc for the remaining F4/F5 items from
> [`Designer-UX-Feedback-Plan.md`](Designer-UX-Feedback-Plan.md): **F4.3** body-region
> visualization, **F4.5** loop starter snippet, **F5.2** try/catch regions, **F5.3** try/catch
> snippet. Per **Q4** the approach is a **region/halo on the main canvas** (one document, no
> sub-canvas); the collapsible "subwindow" stays post-MVP.

## Goal

Make loop bodies and try/catch bodies **visible as regions** on the canvas, and make the
constructs **one click to scaffold**, so a first-time user can build a loop or an error
boundary without reading docs.

## What already exists (foundation)

- `NodePorts.IsStructuralPort(port)` classifies `loopBody`/`try`/`catch`/`finally` edges;
  they already render **dashed** (F4.2/F5.1).
- TryCatch's dynamic ports (`try`/`catch`/`finally`/`done`) are surfaced by `NodePorts` (F5.4).
- Insertion machinery: `CompositeCommand` + `AddNodeCommand`/`AddConnectionCommand` (used by
  Paste and the fan-in gestures) gives single-undo scaffolding; `EnsureSchema` caches module
  schemas; the canvas context menu is extensible.
- `CanvasView` re-renders on document change; regions can be computed at render time.

## Design decisions

- **D1 — Region = downstream closure of a structural edge.** For every connection whose source
  port is structural, BFS from its target following outgoing connections; the region is the union
  bounding box (plus padding) of the reached nodes. The owner node itself is excluded. If a body
  node also feeds the main flow the region simply grows — accepted MVP behaviour (documented),
  no containment model is added to the document.
- **D2 — Regions are pure presentation.** Computed in a framework-free helper
  (`Designer/State/StructuralRegions.cs`) from `DesignerDocument`; nothing is persisted, no new
  document structure, so save format / copy-paste / undo are untouched. (Keeps the D2
  contracts-only boundary and keeps Q4(b) additive later.)
- **D3 — Rendering.** Soft rounded rects with a small label ("🔁 loop body", "🛡️ try", "catch",
  "finally"), drawn inside `df-canvas-content` **behind** edges and nodes, `pointer-events: none`.
  Kind-tinted: loop = accent blue, try = green, catch = red, finally = neutral.
- **D4 — Snippets insert at the right-click point.** Two new canvas context-menu items —
  **"Insert loop skeleton"** and **"Insert try/catch skeleton"** — build the construct as one
  undoable `CompositeCommand` at the cursor's canvas position (converted through the live
  `CanvasTransform`). Bodies use `builtin.passthrough` placeholder steps the user replaces.
  - Loop: `For Each` + body step, wired `loopBody → input`.
  - Try/catch: `Try Catch` + try step + catch step, wired `try → input`, `catch → input`.
- **D5 — Empty regions still teach.** A structural port with **no** outgoing edge renders no
  region (nothing to bound); discoverability of *unwired* ports is already handled by the F4.1
  properties-panel callouts.

## Slices

- [x] **S0 — `StructuralRegions` state helper** (framework-free): `Compute(document)` →
      regions `{ OwnerNodeId, Kind, Port, Bounds, NodeIds }`; BFS with cycle guard; bounds via
      `CanvasGeometry.NodeBounds` + padding. Unit tests: loop body closure (chain), trycatch
      three regions, shared/downstream nodes, no-edge → no region, cycle safety. *(7 tests.)*
- [x] **S1 — Region rendering in `CanvasView`**: draw regions behind `EdgeLayer`, kind CSS
      classes + labels, `pointer-events:none`. bUnit tests: region divs + labels render; none
      for ordinary graphs. *(3 tests.)*
- [x] **S2 — Starter snippets**: canvas context-menu items inserting the loop / try-catch
      skeletons at the cursor (single undo, new nodes selected, toast). bUnit tests via the
      Designer page (menu item present, nodes + dashed edges added, undo removes all).
      *(2 tests; placement converts through the live `CanvasTransform`.)*
- [x] **S3 — Docs + plan sync**: extend the designer docs' loops/try-catch sections with the
      region + snippet behaviour; tick F4.3/F4.5/F5.2/F5.3 in the feedback plan.

- [x] **S4 — Structural palette drops with zones** (follow-up request 2026-07-21): dragging
      **For Each / While / Try Catch** from the palette behaves like the fan-in gesture:
      - **On empty canvas** → inserts the full **skeleton** (construct + placeholder body step(s),
        wired) instead of a bare node — "empty bodies to drop in".
      - **On a node's output side** (same `DropZones` rects, now shown for all structural
        modules, label per kind) → skeleton **plus** an auto-wire from the source node's primary
        output into the construct's conventional input: `foreach.collection`, `while.condition`,
        `trycatch.input` (activation).
      - `NodePorts` surfaces an `input` activation port for trycatch (its schema declares only
        `rethrow`/`catchTypes`; the server skips port validation for this module).
      - All single-undo `CompositeCommand`s + toast; tests for zones per module, both drop paths,
        and wiring. *(Done — zone labels: "🔁/🌀 loop from here", "🛡️ guard from here"; 5 new
        tests; 323 UI tests green.)*

## Out of scope (unchanged)

Collapsible sub-canvas editing (Q4(b)), body containment in the save format, drag-into-region
membership semantics, region-based selection.
