# Designer Input Shape Hinting — Plan

> 📋 Response to user feedback (2026-08-04): *"It would be really useful if when multiple inputs
> (especially a merged output from a prior module) are passed into another module, that there is
> hinting at least for what exists on the input for composition."*
>
> **Revision 1 — proposal.** The good news up front: **everything needed is already known at
> design time** — no runtime data required for the core ask (F4). Q1–Q3 pick the depth. Sibling
> plans from this round: [Designer-Http-Input-Port-Plan.md](Designer-Http-Input-Port-Plan.md),
> [Designer-Split-Preview-Plan.md](Designer-Split-Preview-Plan.md) (which is the sample-driven
> extension of this idea for one module).

## The ask

| # | Feedback | Reading |
| --- | --- | --- |
| 1 | "Hinting for what exists on the input" when composing | At the node being configured, show **what data is arriving and how to address it** |
| 2 | "especially a merged output from a prior module" | The worst case today: merged mode collapses N documented ports into one opaque `output` — the picker offers `{{A.output}}` and nothing else |
| 3 | "for composition" | The hint must be *actionable* — insertable tokens, not just prose |

## Findings 🔬

### F1 — The token picker already exists; it stops one level too shallow

`PropertyEditor` has a working token picker fed by `VariableTokens.OptionsFor()`: Variables,
Globals, and **Upstream outputs** (`nodeName · port` per schema-declared output port). Two gaps:

- **Merged nodes** (the feedback's exact case): when upstream node A is in merged mode,
  `NodePorts.Outputs(A)` collapses to `["output"]`, so the picker offers `{{A.output}}` — and the
  user is left guessing that `{{A.output.statusCode}}` exists.
- **No descriptions/types**: port schema carries `Description` and `DataType`, but the picker
  shows neither.

### F2 — The merged shape is 100% deterministic at design time ✨

`OutputShaping.Merge` wraps the module's *raw outputs dictionary* under one `output` key —
and the raw output keys are exactly the module's **schema-declared output port names**. So for a
merged HTTP node the designer can already compute, with no runtime data at all:

```
{{A.output.statusCode}}  int     — HTTP response status code
{{A.output.body}}        string  — Response body
{{A.output.headers}}     dict    — Flattened response headers
… (8 keys, straight from HttpRequestModule's schema)
```

The runtime resolver (`PropertyBinder.TraverseProperty`) already supports the dot-path into the
merged dictionary — these tokens *work today*; they're just not offered.

### F3 — "Multiple inputs" has a second half: what lands on the node's input ports

`GatherNodeInputs` gives a node: `inputs[targetPort] = value` per connection **plus** prefixed
`inputs["sourceId.port"]` for *every* upstream output. So "what exists on the input" is, at design
time: the wired input ports (with which source feeds each) + every upstream node's addressable
outputs. All derivable from the document + schemas.

### F4 — No runtime data needed for the core ask; runtime data is the natural v2

Schema + graph answers "what keys exist". It cannot answer "what *values* look like" — that needs
a sample (static config, pasted sample, or last-run outputs from
`GET /api/v1/executions/{id}/nodes`). That's the Split-preview plan's territory and Q3's
follow-up, not this plan's core.

### F5 — Where hints can live (existing slots)

- The **token picker** itself (F1) — richer options list, zero new UI concepts.
- The **PropertiesPanel** has precedent for contextual sections (structural hint, outputs strip,
  SQL params modal) — room for a compact "Incoming data" section on the selected node.
- Port **tooltips** (shipped in the fanout-clarity round) — already explain declared ports on
  hover; this plan extends the same philosophy into composition.

## Proposed design 🎨

### 1. Token picker goes one level deeper (the merged fix — feedback's core)

In `VariableTokens.OptionsFor()`, when an upstream node is merged
(`OutputShapingUx.IsMerged`), offer **both** `{{A.output}}` and one token per schema output port:
`{{A.output.statusCode}}` etc., labelled `A · output.statusCode`, with the port's description as
`Detail`. Same treatment for property-derived dynamic ports (partition legs, split keys) — the
port list to expand is whatever `NodePorts.Outputs()` says *before* merged-mode collapse.

### 2. Tokens carry type + description

`TokenOption` gains the port's `DataType` and shows `Detail` in the picker row (it's already
plumbed for hover; surface it). Beginner-readable: `body — string — Response body`.

### 3. "Incoming data" section in the PropertiesPanel

For the selected node, a compact list, one row per wired input port:

```
┌─ Incoming data ────────────────────────────────┐
│ input ← http-1.output (merged: 8 keys ▾)       │
│   statusCode int · body string · headers dict… │
│ also addressable: {{http-1.output.…}}          │
└────────────────────────────────────────────────┘
```

Rows expand a merged/object source into its key list; each key is click-to-copy (or
click-to-insert when a property editor has focus — Q2). Unwired nodes show nothing (no noise).

## Open questions ❓

- [ ] **Q1 — Scope: picker-only (1+2), or picker + panel section (1+2+3)?** Recommended: all
      three — the picker fixes *composition*, the panel section fixes *understanding*, and they
      share the same shape-derivation helper. If trimming, ship 1+2 first; it's the direct
      feedback fix.
  - Picker plus panel section as Reccomended.
- [ ] **Q2 — Insert-at-caret or copy-to-clipboard from the panel section?** Recommended:
      **insert into the focused property editor**, falling back to clipboard when none is focused
      — matching how the existing picker inserts tokens. (Verify the focus-tracking cost; if it's
      awkward, clipboard-only is acceptable for v1.)
  - Insert into the focused property editor, falling back to clipboard when none is focused.
- [ ] **Q3 — Runtime samples ("last run" values beside each key)?** Recommended: defer.
      It's the same execution-history plumbing the Split-preview plan defers (its Q3); do both
      together as a follow-up round once the schema-based hints prove out.
  - Defer and document for follow-up round. 
- [ ] **Q4 — FanIn's `__incomingBranches__`?** FanIn nodes receive an engine-supplied ordered
      branch list, invisible to all of this. The multi-input-join plan
      ([Designer-Multi-Input-Join-Plan.md](Designer-Multi-Input-Join-Plan.md)) owns FanIn
      legibility; this plan deliberately excludes it to avoid double-owning. Confirm.
  -  We should try to fix this, lets see if we can get a design proposed. Ask more questions if needed about the proposed design.

## Items

| # | Item | Kind | Size | Status |
| --- | --- | --- | --- | --- |
| T1 | Shape-derivation helper: upstream node → addressable keys (ports; merged expansion; property-derived ports), with type + description | Infra | M | ⬜ |
| T2 | `VariableTokens.OptionsFor()` offers merged sub-keys + type/description details (design §1–2) | UX | S | ⬜ |
| T3 | Token picker rows render detail text (type — description) | UX | S | ⬜ |
| T4 | "Incoming data" PropertiesPanel section (design §3, Q1/Q2) | UX | M | ⬜ |
| T5 | Tests (helper unit tests; picker options; panel rendering; insert/copy behavior) | Tests | M | ⬜ |
| T6 | Docs: composition guide snippet — "how to reference upstream data", incl. merged-mode addressing | Docs | S | ⬜ |

### T1 — Shape derivation 🧮

- [ ] T1.1 `InputShape.For(document, nodeId)`: wired input ports with their sources; upstream
      nodes with addressable key lists. Merged nodes expand via schema output ports (F2);
      partition/split/switch expand via `NodePorts.PropertyDerivedOutputs`.
- [ ] T1.2 Depth stops at one level (port → merged keys). Deeper nesting (e.g. keys of a `dict`
      port's value) is unknowable from schema — explicitly out of scope.
- [ ] T1.3 Framework-free (D2 convention), so it's testable and portable.

### T2/T3 — Picker 🎯

- [ ] T2.1 Merged upstream → `{{A.output}}` plus `{{A.output.<port>}}` per pre-collapse port.
- [ ] T2.2 `TokenOption.Detail` = `"{type} — {description}"`; picker rows show it.
- [ ] T2.3 Ordering: wired-input sources first, then other upstream nodes, then variables/globals
      (composition-relevance order). Verify against existing picker tests.

### T4 — Panel section 🖥️

- [ ] T4.1 Only when the node has ≥1 incoming connection.
- [ ] T4.2 Merged/dict sources expandable; keys insert/copy per Q2.
- [ ] T4.3 Reuses T1; no independent graph walking.

---

*Created 2026-08-04. Findings verified against `VariableTokens.cs`, `OutputShaping.cs` /
`OutputShapingUx.cs`, `PropertyBinder.cs` (dot-path traversal), `WorkflowExecutor.GatherNodeInputs`,
`NodePorts.cs`, `PropertyEditor.razor` / `PropertiesPanel.razor`.*
