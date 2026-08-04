# Designer Input Shape Hinting â€” Plan

> ðŸ“‹ Response to user feedback (2026-08-04): *"It would be really useful if when multiple inputs
> (especially a merged output from a prior module) are passed into another module, that there is
> hinting at least for what exists on the input for composition."*
>
> **Revision 1 â€” proposal.** The good news up front: **everything needed is already known at
> design time** â€” no runtime data required for the core ask (F4). Q1â€“Q3 pick the depth. Sibling
> plans from this round: [Designer-Http-Input-Port-Plan.md](Designer-Http-Input-Port-Plan.md),
> [Designer-Split-Preview-Plan.md](Designer-Split-Preview-Plan.md) (which is the sample-driven
> extension of this idea for one module).

## The ask

| # | Feedback | Reading |
| --- | --- | --- |
| 1 | "Hinting for what exists on the input" when composing | At the node being configured, show **what data is arriving and how to address it** |
| 2 | "especially a merged output from a prior module" | The worst case today: merged mode collapses N documented ports into one opaque `output` â€” the picker offers `{{A.output}}` and nothing else |
| 3 | "for composition" | The hint must be *actionable* â€” insertable tokens, not just prose |

## Findings ðŸ”¬

### F1 â€” The token picker already exists; it stops one level too shallow

`PropertyEditor` has a working token picker fed by `VariableTokens.OptionsFor()`: Variables,
Globals, and **Upstream outputs** (`nodeName Â· port` per schema-declared output port). Two gaps:

- **Merged nodes** (the feedback's exact case): when upstream node A is in merged mode,
  `NodePorts.Outputs(A)` collapses to `["output"]`, so the picker offers `{{A.output}}` â€” and the
  user is left guessing that `{{A.output.statusCode}}` exists.
- **No descriptions/types**: port schema carries `Description` and `DataType`, but the picker
  shows neither.

### F2 â€” The merged shape is 100% deterministic at design time âœ¨

`OutputShaping.Merge` wraps the module's *raw outputs dictionary* under one `output` key â€”
and the raw output keys are exactly the module's **schema-declared output port names**. So for a
merged HTTP node the designer can already compute, with no runtime data at all:

```
{{A.output.statusCode}}  int     â€” HTTP response status code
{{A.output.body}}        string  â€” Response body
{{A.output.headers}}     dict    â€” Flattened response headers
â€¦ (8 keys, straight from HttpRequestModule's schema)
```

The runtime resolver (`PropertyBinder.TraverseProperty`) already supports the dot-path into the
merged dictionary â€” these tokens *work today*; they're just not offered.

### F3 â€” "Multiple inputs" has a second half: what lands on the node's input ports

`GatherNodeInputs` gives a node: `inputs[targetPort] = value` per connection **plus** prefixed
`inputs["sourceId.port"]` for *every* upstream output. So "what exists on the input" is, at design
time: the wired input ports (with which source feeds each) + every upstream node's addressable
outputs. All derivable from the document + schemas.

### F4 â€” No runtime data needed for the core ask; runtime data is the natural v2

Schema + graph answers "what keys exist". It cannot answer "what *values* look like" â€” that needs
a sample (static config, pasted sample, or last-run outputs from
`GET /api/v1/executions/{id}/nodes`). That's the Split-preview plan's territory and Q3's
follow-up, not this plan's core.

### F5 â€” Where hints can live (existing slots)

- The **token picker** itself (F1) â€” richer options list, zero new UI concepts.
- The **PropertiesPanel** has precedent for contextual sections (structural hint, outputs strip,
  SQL params modal) â€” room for a compact "Incoming data" section on the selected node.
- Port **tooltips** (shipped in the fanout-clarity round) â€” already explain declared ports on
  hover; this plan extends the same philosophy into composition.

## Proposed design ðŸŽ¨

### 1. Token picker goes one level deeper (the merged fix â€” feedback's core)

In `VariableTokens.OptionsFor()`, when an upstream node is merged
(`OutputShapingUx.IsMerged`), offer **both** `{{A.output}}` and one token per schema output port:
`{{A.output.statusCode}}` etc., labelled `A Â· output.statusCode`, with the port's description as
`Detail`. Same treatment for property-derived dynamic ports (partition legs, split keys) â€” the
port list to expand is whatever `NodePorts.Outputs()` says *before* merged-mode collapse.

### 2. Tokens carry type + description

`TokenOption` gains the port's `DataType` and shows `Detail` in the picker row (it's already
plumbed for hover; surface it). Beginner-readable: `body â€” string â€” Response body`.

### 3. "Incoming data" section in the PropertiesPanel

For the selected node, a compact list, one row per wired input port:

```
â”Œâ”€ Incoming data â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”
â”‚ input â† http-1.output (merged: 8 keys â–¾)       â”‚
â”‚   statusCode int Â· body string Â· headers dictâ€¦ â”‚
â”‚ also addressable: {{http-1.output.â€¦}}          â”‚
â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜
```

Rows expand a merged/object source into its key list; each key is click-to-copy (or
click-to-insert when a property editor has focus â€” Q2). Unwired nodes show nothing (no noise).

## Open questions â“ â€” Q1â€“Q3 decided 2026-08-04 âœ…; Q4 expanded below

- [x] **Q1 â€” Scope?** âœ… Picker + panel section (all of design Â§1â€“3).
- [x] **Q2 â€” Insert or copy from the panel section?** âœ… Insert into the focused property editor,
      falling back to clipboard when none is focused.
- [x] **Q3 â€” Runtime samples?** âœ… Deferred to a follow-up round together with the Split-preview
      plan's Q3 (execution-history plumbing benefits both at once).
- [x] **Q4 â€” FanIn?** âœ… In scope â€” design proposed below (Â§FanIn hinting), with its own
      questions (Q5â€“Q7).

## FanIn hinting â€” proposed design ðŸª„ (from Q4)

FanIn's inputs are invisible twice over: the engine feeds it a hidden `__incomingBranches__` list
(its `branches` port is decorative), and three of its five modes depend on **branch order**, which
is connection *declaration* order â€” deterministic (the engine iterates
`Connections.Where(target == fanin)` in saved order; FanIn's own remarks confirm it) but shown
nowhere. *(Note: `advanced-flow-control.md` claims Concat is "branch-completion order" â€” that's
the wrong-ordering claim the join plan flagged as F14; the code says connection order. This plan's
docs item corrects it.)*

Everything needed for hints is client-side derivable:

**1. Ordered branch list** (the FanIn flavour of the "Incoming data" section):

```
â”Œâ”€ Incoming branches (order matters for merge/first/last) â”€â”
â”‚ 1. http-1 Â· body                                         â”‚
â”‚ 2. http-2 Â· body                                         â”‚
â”‚ 3. script-1 Â· result                                     â”‚
â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜
```

Order = document connection order, exactly what the engine will use.

**2. Mode-aware result shape**, recomputed live as `mode` changes:

| Mode | Hint shown |
| --- | --- |
| `concat` | `result = [ branch 1, branch 2, branch 3 ]` (in the order above) |
| `merge` | union of branch payload keys; colliding keys flagged with *"branch N wins"* (last-writer, connection order) |
| `named` | the **computed key list** â€” source port names, falling back to `nodeId.port` on collision, mirroring the engine's `NamedBranches` naming rule exactly |
| `first` / `last` | "result = branch 1's payload" / "â€¦branch N's payload" |

**3. Downstream tokens**: nodes after a FanIn get picker entries derived from the same
computation â€” `{{fanin-1.result.<key>}}` for `named` (keys are computable), `{{fanin-1.result}}` +
`{{fanin-1.count}}` for the rest.

Like the Split preview, Â§2/Â§3 **mirror engine semantics client-side** (the Named collision rule,
Merge precedence), so they get the same **drift-guard treatment**: shared fixtures asserted
against `FanInModule` in `Workflow.Tests` and the client helper in `Workflow.Tests.UI`.

Deliberately *not* in scope (stays with the join plan): changing FanIn's mechanism, declared
arity/ports mode (J4), the sub-graph zero-branches bug (J2 â€” a runtime bug, not a hinting
concern; see Q7 for how the hint handles it honestly).

### FanIn questions â“ â€” decided 2026-08-04 âœ…

- [x] **Q5 â€” Scope confirmation:** âœ… All three parts (branch list, mode-aware shape, downstream
      tokens), client-side with drift guards.
- [x] **Q6 â€” Show branch order as editable?** âœ… **Reorder controls in this round** â€” up/down
      controls on the branch rows that rewrite the document's connection order (a proper undoable
      command), since order drives `merge`/`first`/`last`. (T8)
- [x] **Q7 â€” FanIn inside a sub-graph (J2)?** âœ… **Both**: fix the engine bug (SubGraphExecutor
      doesn't populate `__incomingBranches__`/`__incomingBranchMeta__` â€” silent zero-branch
      aggregation) *and* keep the section's note mechanism for honestly surfacing future known
      gaps. (T9; join plan J2 satisfied here, cross-referenced.)

## Addendum â€” the `{{input}}` root (from the HTTP-input plan) ðŸ”Œ

Since this plan was drafted, `{{input}}` / `{{input.path}}` shipped as a reserved template root.
The picker should offer it as the **first** upstream token whenever the selected node's `input`
port is wired â€” it's the beginner-friendliest spelling ("the value coming in"), and for merged
upstream sources it expands like any other: `{{input.statusCode}}` etc. via the same
shape-derivation helper. (New decision D-I, folded into T2.)

## Items

| # | Item | Kind | Size | Status |
| --- | --- | --- | --- | --- |
| T1 | Shape-derivation helper: upstream node â†’ addressable keys (ports; merged expansion; property-derived ports), with type + description | Infra | M | â¬œ |
| T2 | `VariableTokens.OptionsFor()` offers `{{input}}` first (D-I), merged sub-keys, type/description details (design Â§1â€“2) | UX | S | â¬œ |
| T3 | Token picker rows render detail text (type â€” description) | UX | S | â¬œ |
| T4 | "Incoming data" PropertiesPanel section (design Â§3, Q2 insert-with-clipboard-fallback) | UX | M | â¬œ |
| T5 | Tests (helper unit tests; picker options; panel rendering; insert/copy behavior) | Tests | M | â¬œ |
| T6 | Docs: composition guide snippet â€” "how to reference upstream data", incl. merged-mode addressing; fix the Concat ordering claim in `advanced-flow-control.md` | Docs | S | â¬œ |
| T7 | FanIn hinting per Â§FanIn design (Q5): branch list, mode-aware shape w/ drift guards, downstream tokens | UX | M | â¬œ |
| T8 | Branch reorder controls on FanIn's branch rows â€” undoable connection-reorder command (Q6) | UX | M | â¬œ |
| T9 | ðŸž Fix J2: `SubGraphExecutor.GatherNodeInputs` populates `__incomingBranches__`/`__incomingBranchMeta__` like `WorkflowExecutor` (Q7); regression tests | Core | S | â¬œ |

### T1 â€” Shape derivation ðŸ§®

- [ ] T1.1 `InputShape.For(document, nodeId)`: wired input ports with their sources; upstream
      nodes with addressable key lists. Merged nodes expand via schema output ports (F2);
      partition/split/switch expand via `NodePorts.PropertyDerivedOutputs`.
- [ ] T1.2 Depth stops at one level (port â†’ merged keys). Deeper nesting (e.g. keys of a `dict`
      port's value) is unknowable from schema â€” explicitly out of scope.
- [ ] T1.3 Framework-free (D2 convention), so it's testable and portable.

### T2/T3 â€” Picker ðŸŽ¯

- [ ] T2.1 Merged upstream â†’ `{{A.output}}` plus `{{A.output.<port>}}` per pre-collapse port.
- [ ] T2.2 `TokenOption.Detail` = `"{type} â€” {description}"`; picker rows show it.
- [ ] T2.3 Ordering: wired-input sources first, then other upstream nodes, then variables/globals
      (composition-relevance order). Verify against existing picker tests.

### T4 â€” Panel section ðŸ–¥ï¸

- [ ] T4.1 Only when the node has â‰¥1 incoming connection.
- [ ] T4.2 Merged/dict sources expandable; keys insert/copy per Q2.
- [ ] T4.3 Reuses T1; no independent graph walking.

---

*Created 2026-08-04. Findings verified against `VariableTokens.cs`, `OutputShaping.cs` /
`OutputShapingUx.cs`, `PropertyBinder.cs` (dot-path traversal), `WorkflowExecutor.GatherNodeInputs`,
`NodePorts.cs`, `PropertyEditor.razor` / `PropertiesPanel.razor`.*


---

## What shipped 📦

| File | Change |
| --- | --- |
| `Workflow.UI.Client/Designer/State/InputShape.cs` | New (T1) — wired-input rows, addressable keys per upstream node, merged-node expansion (pre-collapse schema ports), named-FanIn expansion, `{{input}}` wiring check. |
| `Workflow.UI.Client/Designer/State/FanInShape.cs` | New (T7) — declaration-order branch list, mode, named-key computation (engine collision rule mirrored), merge-key precedence, beginner shape summaries. |
| `Workflow.UI.Client/Designer/State/VariableTokens.cs` | T2/D-I — `{{input}}` offered first when wired (with source detail + sub-keys); upstream tokens expand via `InputShape` with `type — description` detail. |
| `Workflow.UI.Client/Designer/Components/PropertyEditor.razor` | T3 — picker rows render detail text; new `Focused` callback (focusin on the field) for insert-at-focus. |
| `Workflow.UI.Client/Designer/Components/PropertiesPanel.razor` | T4/T7/T8 — "Incoming data" section (per-port rows, expandable keys, insert-into-focused-editor with clipboard fallback); FanIn flavour: numbered branch list with ↑/↓ reorder, live mode-aware shape summary, known-issue note slot (Q7 mechanism, currently empty since J2 is fixed). |
| `Workflow.UI.Client/Designer/State/Commands/Commands.cs` | T8 — `ReorderIncomingConnectionCommand`: undoable branch reorder that leaves unrelated connections untouched. |
| `Workflow.Engine/Actors/SubGraphExecutor.cs` | 🐞 T9/J2 — populates `__incomingBranches__`/`__incomingBranchMeta__` like the top level; FanIn now works inside loop/try/transaction/parallel bodies. |
| `Workflow.Tests/Engine/SubGraphExecutorTests.cs` | J2 regression tests (concat + named in a sub-graph; top-level guard). |
| `Workflow.Tests/Modules/Flow/FanInShapeDriftGuardTests.cs` | New — shared fixtures asserting the engine side of the named/merge predictions. |
| `Workflow.Tests.UI/State/InputShapeHintingTests.cs` | New — 15 tests: InputShape, FanInShape (client side of the drift fixtures), picker options, reorder command. |
| `docs/variables.md`, `docs/advanced-flow-control.md` | Picker/Incoming-data description; **fixed the wrong "branch-completion order" claim** (join plan F14) — order is connection declaration order; noted FanIn-in-sub-graph now works. |

**Verification.** `Workflow.Tests.UI` 639/639 green. Server FanIn/SubGraph suites 43/43 green
(incl. J2 regressions + drift guards). Solution build: 0 errors.

*Implemented and verified 2026-08-04. Join-plan items J1 (partially — ordering doc), J2 (fixed
here), and F14 (corrected) are satisfied by this round; noted for that plan's next revision.*