# Phase 3.3.b: Designer Editing (Weeks 28-29) ✏️

Made with 💖 by Ami-Chan! UwU ✨

[Master plan: Phase3-3-WorkflowDesigner.md](Phase3-3-WorkflowDesigner.md) | [Prev: 3.3.a Foundation](Phase3-3a-DesignerFoundation.md) | [Next: 3.3.c Runtime](Phase3-3c-DesignerRuntime.md)

> **Scope:** Everything that mutates the graph: palette drag-and-drop, selection + node
> movement, connection drawing, the schema-driven properties panel, undo/redo, save with
> validation, dirty tracking, and keyboard shortcuts. Every mutation flows through
> `IDesignerCommand` on the `CommandStack` (D7) — **no component mutates the document
> directly.**

> **🤖 Agent notes (read [master instructions](Phase3-3-WorkflowDesigner.md#agent-implementation-instructions-) first):**
> - Slice order: **b.0 ∥ b.1 → b.2 → b.3 → b.4** (b.0 and b.1 are independent after a.3).
> - **Command-pattern discipline:** write the command + its Do/Undo xUnit spec *first*, then the component that invokes it. Audit at b.4: grep the Components tree for direct `DesignerDocument` mutation — there must be none outside `Commands/*`.
> - b.3: enumerate `PropertyEditorType` from `Workflow.Core/Models/ModuleSchema.cs` (11 values) — the editor-matrix theory test must cover every enum member so a future enum addition fails loudly.
> - b.3 Monaco (D13): pin a specific Monaco version; loader lives in `CodeEditor.razor` + `monaco-interop.js`; the textarea fallback must be reachable in tests (fake a load failure). Do not let Monaco types leak below the view layer.
> - b.4 D14 endpoint: implement in `Workflow.Api/V1/WorkflowEndpoints.cs` following that file's existing handler style; resolve `ModuleAwareWorkflowValidator` (see `Workflow.Modules/Validation/`) via DI as the existing endpoints resolve services; tests go in `Workflow.Tests` (not Tests.UI); update `docs/rest-api.md` in the same slice.
> - Keyboard shortcuts: suppression-when-focused-in-input is a frequent bug source — test it explicitly (`Shortcuts_SuppressedInInputs`).

---

## Command Pattern Contract (D7) 📜

```text
IDesignerCommand { string Description; void Do(DesignerDocument doc); void Undo(DesignerDocument doc); }

CommandStack
  ├─ Execute(cmd)   → cmd.Do, push, truncate redo tail, cap 50 (drop oldest)
  ├─ Undo() / Redo() · CanUndo / CanRedo
  ├─ MarkSaved()    → records save-point; IsDirty = (position != save-point)
  └─ Changed event  → toolbar/status refresh

Commands: AddNode · RemoveNodes(nodes + their connections, restored on undo) ·
          MoveNodes(ids, from[], to[]) — one command per completed drag, not per pixel ·
          EditNodeProperties(id, before, after) · RenameNode ·
          AddConnection · RemoveConnections · EditConnection(condition/priority) ·
          EditWorkflowMeta(name/description/tags) · EditVariables
```

---

## 3.3.b.0 Module Palette + Drag-to-Create 📦

> **Purpose:** The left rail (mockup **S2**): browse/search all registered modules grouped
> by category, drag one onto the canvas to create a node.

**Complexity:** 🟡 Medium

### Tasks

- [ ] **`ModulePalette` component** — groups from module category metadata (fallback: id prefix — `builtin.http.*` → "HTTP"); collapsible sections; each entry: icon, display name, one-line description tooltip; data from the cached `ModulesClient.ListAsync`
- [ ] **Search/filter** — text box filters across id/name/description; highlights matches; empty-result state
- [ ] **Module details flyout** — click (not drag) opens a details pane: description, input/output ports, property list with types — straight from `ModuleSchemaDto`
- [ ] **Drag-to-create** — HTML5 drag (or pointer-based fallback) from palette entry; ghost preview follows cursor; drop on canvas → `ScreenToCanvas` position → `Execute(new AddNodeCommand(...))` with unique id (`MakeNodeId`), schema-resolved node, **schema defaults pre-filled** into `Properties` (from `PropertyDefinition.DefaultValue`)
- [ ] **Drop rejection** — drop outside the canvas does nothing; drop while read-only/run-mode disabled

### Tests (target ~8): → `Workflow.Tests.UI/Components/PaletteTests.cs` *(bUnit)* + `State/AddNodeCommandTests.cs`

- [ ] `Palette_RendersGroups_FromModules` · `Palette_Search_FiltersEntries`
- [ ] `Palette_Details_ShowsSchema` · `AddNodeCommand_Do_AddsNodeWithDefaults`
- [ ] `AddNodeCommand_Undo_RemovesNode` · `AddNode_GeneratesUniqueId_AcrossRepeats`
- [ ] `Drop_TranslatesScreenPosition_ToCanvas` · `Drop_OutsideCanvas_NoCommand`

---

## 3.3.b.1 Selection + Node Movement + Context Menus 🖱️

> **Purpose:** Select (single/multi/rubber-band), move nodes (drag with live edge
> re-routing), and the node/connection/canvas context menus.

**Complexity:** 🟡 Medium

### Tasks

- [ ] **`SelectionState` (framework-free)** — selected node ids + connection ids; `Select`, `AddToSelection`, `Toggle`, `Clear`, `SelectAll`; `Changed` event
- [ ] **Click semantics** — click node → select (clears others); `Ctrl+Click` → toggle-add; click canvas background → clear; click edge → select connection; selection visuals (accent border on nodes, thicker stroke on edges)
- [ ] **Rubber-band select** — drag on empty canvas with `Shift` (plain background-drag stays pan, from 3.3.a) → selection rectangle; nodes intersecting rect selected on release
- [ ] **Node drag-move** — pointer-drag on a node header moves **all selected** nodes (delta applied to each); live edge re-render during drag; on release → single `MoveNodesCommand` (before/after positions) so undo restores the whole gesture
- [ ] **Node context menu (S4 `[⋮]` + right-click)** — Rename (inline edit) · Duplicate (AddNode with copied properties, offset position, fresh id) · Delete · *(runtime "View outputs" arrives in 3.3.c)*
- [ ] **Connection context menu** — Edit condition… (small dialog → `EditConnectionCommand`) · Delete
- [ ] **Canvas context menu** — Select all · Paste *(wired in b.4)* · Fit to screen
- [ ] **`RemoveNodesCommand`** — removes selected nodes **and all attached connections**; undo restores both (order-safe)

### Tests (target ~10): → `Workflow.Tests.UI/State/SelectionAndMoveTests.cs` + `Components/SelectionInteractionTests.cs`

- [ ] `Selection_Click_SelectsSingle` · `Selection_CtrlClick_Toggles` · `Selection_CanvasClick_Clears`
- [ ] `RubberBand_SelectsIntersectingNodes` · `MoveNodes_DragMovesAllSelected`
- [ ] `MoveNodesCommand_UndoRestoresPositions` *(one command per gesture)*
- [ ] `RemoveNodesCommand_RemovesAttachedConnections` · `RemoveNodesCommand_UndoRestoresEverything`
- [ ] `Duplicate_CreatesOffsetCopy_FreshId` · `ContextMenu_Rename_AppliesViaCommand`

---

## 3.3.b.2 Connection Drawing + Validation 🔗

> **Purpose:** Port-to-port connection creation (mockup **S4**): drag from an output port,
> valid targets highlight, drop snaps and creates the `ConnectionDefinition`; invalid
> targets are rejected with visible feedback.

**Complexity:** 🟠 Medium-High *(interaction + live validation)*

### Tasks

- [ ] **Drag initiation** — pointer-down on an **output** port anchor starts connection mode (does not move the node); ghost bezier from the source anchor to the cursor, re-rendered on pointer-move
- [ ] **Target highlighting** — while dragging, all **compatible input ports** get a highlight class; compatibility = target is an input port on a different node AND adding the edge passes live checks
- [ ] **Live validation (uses `GraphValidator` pieces)** — reject: self-connection, duplicate (same source port → same target port), **cycle creation** (validator's DFS on document + candidate edge); incompatible hover shows ⛔ cursor/tint
- [ ] **Drop** — within snap radius (~12px) of a valid input anchor → snap + `Execute(new AddConnectionCommand(...))` (default `Condition=null`, `Priority=0`); drop anywhere else → cancel, ghost disappears
- [ ] **Reverse drag (nice-to-have)** — drag from an *input* port to an output port creates the same edge (direction normalized); skip if it complicates the state machine — note the decision
- [ ] **Condition affordance** — connections with a `Condition` render the label chip (from 3.3.a `EdgeLayer`); the b.1 context-menu dialog edits it; condition text is free-form (the engine evaluates it — no client parsing beyond non-empty trim)

### Tests (target ~9): → `Workflow.Tests.UI/State/ConnectionRulesTests.cs` + `Components/ConnectDragTests.cs`

- [ ] `Connect_ValidPorts_CreatesConnection` · `AddConnectionCommand_UndoRemovesIt`
- [ ] `Connect_SelfNode_Rejected` · `Connect_Duplicate_Rejected` · `Connect_WouldCreateCycle_Rejected`
- [ ] `Connect_DiamondShape_Allowed` *(regression: diamonds aren't cycles)*
- [ ] `DragGhost_FollowsCursor` *(bUnit: ghost path updates on move)* · `ValidTargets_HighlightDuringDrag`
- [ ] `Drop_OutsideSnapRadius_Cancels`

---

## 3.3.b.3 Schema-Driven Properties Panel 🧾

> **Purpose:** The right rail (mockup **S2**): render the correct editor for every property
> of the selected node from its `ModuleSchemaDto` (D6), validate per schema rules, and
> write changes back through `EditNodePropertiesCommand`.

**Complexity:** 🟠 Medium-High *(editor matrix + validation)*

### Tasks

- [ ] **`PropertiesPanel` component** — shows on single node selection (multi-select → "N nodes selected" + shared actions only); header: node name (inline-editable → `RenameNodeCommand`), module id, pinned `Metadata["moduleVersion"]` read-only chip when present (Q7)
- [ ] **Editor matrix (`PropertyEditors/*.razor`, one per `PropertyEditorType`)** —
  - `Text`/`ConnectionString`/`FilePath`/`DirectoryPath` → text input *(paths get no browser file-picker — they're server paths; plain text + hint)*
  - `MultilineText` → textarea · `Number` → numeric input (min/max from rules) · `Boolean` → checkbox
  - `Dropdown` → select over `AllowedValues`
  - `Expression` → mono-font input with `{{ }}` placeholder hint (3.1.7 syntax link)
  - `Code` → **Monaco editor via `CodeEditor.razor` (D13)** — lazy-loaded JS interop (assets fetched on first open), language mode from a sibling `language` property when present (`javascript`/`csharp`/`lua`), mono theme from tokens; **plain-textarea fallback** renders automatically if Monaco fails to load; optional **[🧪 Test]** button for `builtin.script` nodes calling `POST /api/v1/scripts/test` with the current code/language and showing result/logs in a flyout
  - `Json` → **Monaco with `json` language mode (D13)** + parse-on-blur validation (⚠ on invalid JSON); same fallback rules
  - *(`Expression` stays a plain mono input — single-line expressions don't need Monaco)*
- [ ] **Value plumbing** — editors read/write `JsonElement` values via a small `JsonValueEditor` helper (string/number/bool/object round-trip preserving JSON types — a `Number` property must not become a JSON string)
- [ ] **Validation (schema rules → client checks)** — `Required` (non-empty), `Min`/`Max`, `MinLength`/`MaxLength`, `Regex`, `Enum`; inline ⚠ messages under the field (S2); panel-level summary; invalid values still *editable* but flagged (save gate handles blocking, b.4)
- [ ] **Apply semantics** — edits buffer locally; **[Apply]** (or field blur — pick one, document it) → one `EditNodePropertiesCommand` with before/after property bags; Esc reverts the buffer
- [ ] **Workflow-level pane** — when nothing is selected the panel shows workflow meta: name, description, tags (→ `EditWorkflowMetaCommand`) and the **variables editor** (name/type/default rows → `EditVariablesCommand`)

### Tests (target ~14): → `Workflow.Tests.UI/Components/PropertiesPanelTests.cs` + `State/EditCommandsTests.cs`

- [ ] `Panel_RendersEditor_PerEditorType` *(theory across all 11 editor types)*
- [ ] `Dropdown_UsesAllowedValues` · `Number_RespectsMinMax` · `Json_InvalidShowsWarning`
- [ ] `Required_EmptyShowsError` · `Regex_Violation_ShowsError`
- [ ] `Apply_ProducesSingleEditCommand` · `EditCommand_UndoRestoresBefore`
- [ ] `Esc_RevertsBuffer_NoCommand` · `Rename_ViaHeader_UsesRenameCommand`
- [ ] `NumberValue_StaysJsonNumber_NotString` *(JsonElement typing)* · `MultiSelect_ShowsSummaryOnly`
- [ ] `NoSelection_ShowsWorkflowMeta` · `Variables_EditRoundTrip`
- [ ] `CodeEditor_MonacoInterop_InitializedWithLanguage` *(bUnit + JS interop fake)* · `CodeEditor_MonacoLoadFailure_FallsBackToTextarea`

---

## 3.3.b.4 Undo/Redo + Save + Dirty Tracking + Shortcuts 💾

> **Purpose:** The command stack surfaces (toolbar + shortcuts), copy/paste, the save
> pipeline with validation gate, and unsaved-changes protection. Completes the edit loop.

**Complexity:** 🟡 Medium

### Tasks

- [ ] **`CommandStack` finalization (D7)** — 50-entry cap (drop oldest), redo-tail truncation on new command, `MarkSaved()`/`IsDirty`, `Changed` event; **all** b.0–b.3 commands registered through it (audit)
- [ ] **Toolbar (S2)** — `[💾 Save]` `[↩ Undo]` `[↪ Redo]` with enabled-state binding + hover tooltips showing the command description ("Undo: Move 3 nodes"); dirty dot `●unsaved` next to the workflow name
- [ ] **Keyboard shortcuts** — global handler on the designer page: `Ctrl+Z` undo · `Ctrl+Y`/`Ctrl+Shift+Z` redo · `Delete` remove selection · `Ctrl+A` select all · `Ctrl+C`/`Ctrl+V` copy/paste · `Ctrl+S` save (preventDefault); **suppressed while focus is in an input/textarea**
- [ ] **Copy/paste** — clipboard = in-app serialized node set + *internal* connections; paste → AddNode commands with fresh ids + offset positions, internal edges re-created between the new ids; works across undo boundaries
- [ ] **Validate endpoint (D14 — the one API change in 3.3)** — `POST /api/v1/workflows/validate` in `Workflow.Api/V1/WorkflowEndpoints.cs`: accepts a full `WorkflowDefinition` body, runs the **existing** `ModuleAwareWorkflowValidator` (graph + per-module `ValidateConfiguration`), returns `{ valid, issues: [{ severity, message, nodeId? }] }` without persisting; `WorkflowRead` policy (dry-run); Swagger-tagged `Workflows`; documented in `docs/rest-api.md`; ~4 API tests in `Workflow.Tests/Api/V1/WorkflowEndpointsTests.cs` (valid → `valid:true`; unknown module / bad module config / structural issue → itemized issues)
- [ ] **Save pipeline (D5/D14/Q5)** — two-stage gate: **(1)** client `GraphValidator.Validate` (instant — errors block with dialog + jump-to-node links); **(2)** `POST /workflows/validate` (authoritative server check — issues render in the same dialog with node links; validate-endpoint-unavailable degrades to stage 1 with a notice); then `ToDto()` → `PUT /api/v1/workflows/{id}` (or `POST` for `/designer/new`, then navigate to the real id); success → `MarkSaved()` + toast; residual server 400/409/422 ProblemDetails rendered in the save dialog
- [ ] **Save As** — name prompt → `POST` a copy with a fresh id/name → navigate
- [ ] **Unsaved-changes protection** — `beforeunload` browser warning when dirty; in-app navigation guard (leaving `/designer/*` while dirty → confirm dialog with Save/Discard/Cancel)
- [ ] **New-workflow flow** — `/designer/new`: blank document + staged name; first save POSTs

### Tests (target ~15): → `Workflow.Tests.UI/State/CommandStackTests.cs` + `Components/SaveFlowTests.cs` + `Workflow.Tests/Api/V1/WorkflowEndpointsTests.cs` *(validate endpoint)*

- [ ] `Stack_UndoRedo_RoundTrips_AllCommandTypes` *(theory over every command)*
- [ ] `Stack_CapAt50_DropsOldest` · `Stack_NewCommand_TruncatesRedoTail`
- [ ] `Dirty_TrueAfterEdit_FalseAfterSave_TrueAfterUndoPastSavePoint`
- [ ] `Shortcuts_UndoRedoDeleteSelectAll_Work` · `Shortcuts_SuppressedInInputs`
- [ ] `CopyPaste_ClonesNodesAndInternalEdges_FreshIds`
- [ ] `Save_ValidatorErrors_BlocksWithDialog` · `Save_CallsPut_MarksSaved`
- [ ] `Save_CallsServerValidate_BeforePut` · `Save_ServerValidateIssues_BlockWithNodeLinks` · `Save_ValidateUnavailable_DegradesWithNotice`
- [ ] `SaveNew_Posts_ThenNavigates` · `Save_ServerProblem_ShownInDialog`
- [ ] `NavigateAwayDirty_PromptsSaveDiscardCancel`

---

## Exit Criteria for 3.3.b ✅

- [ ] Full edit loop works end-to-end against the live API: open → drag nodes in → connect → configure → save → reopen reproduces the identical canvas
- [ ] Every mutation is undoable/redoable; dirty tracking + both unsaved-changes guards behave
- [ ] Connection rules hold under manual abuse (cycles/self/duplicates impossible to create)
- [ ] Properties panel renders correct editors for **all** `PropertyEditorType`s across the real builtin module set; validation messages match schema rules
- [ ] Save gate blocks structurally-broken definitions with actionable messages (client + `POST /workflows/validate` server stage, D14); server errors surface cleanly
- [ ] `Code`/`Json` properties open the lazy-loaded Monaco editor (D13); the textarea fallback engages when Monaco can't load
- [ ] All state-service specs + component tests green; full repo suite unaffected

---

*Made with 💖 by Ami-Chan! Ctrl+Z is a love language~ UwU* ✨
