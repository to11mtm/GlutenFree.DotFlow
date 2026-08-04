# Designer FanOut â€” Partitioned Legs & Clarity â€” Plan

> ðŸ“‹ Response to user feedback (2026-08-04,
> [Designer-FanOut-Clarity.md](Designer-FanOut-Clarity.md)): *"a way to have items fan out based on
> their itemsâ€¦ Foo on one output and Bar and Baz on a separate leg"*, and *"they are uncertain how
> it works in current state overall â€” better clarity around the existing case"*.
>
> **Revision 1 â€” proposal.** Feedback 2 (clarity) is well-understood and can proceed. Feedback 1
> (partitioned legs) has a genuine design fork â€” **Q1â€“Q3 below need answers before P-items start.**
> Recommended answers are baked in.

## The ask

| # | Feedback | Reading |
| --- | --- | --- |
| 1 | "Fan out based on their items â€” separate outputs for Foo, Bar, Baz" | Route each item of a collection to a leg chosen by **what the item is** |
| 2 | "â€¦or Foo on one output and Bar and Baz on a separate leg" | Legs are **user-defined groups**, not necessarily one-per-kind |
| 3 | "Uncertain how it works in current state overall" | The per-item `branch` mechanism is **invisible** in the designer |
| 4 | "Better clarity around the existing case, especially if we need a new module" | Clarity work stands alone; don't hold it hostage to the new module |

## The gap in one table ðŸ—ºï¸

What exists today, and why none of it is what the users asked for:

| Module | Input | Legs | Chooses leg by | Items per leg |
| --- | --- | --- | --- | --- |
| `builtin.switch` | **one value** | named, one per case | matching the value | the single value, to exactly one leg |
| `builtin.parallel` | *(none â€” pure trigger)* | named, static | *(all fire)* | no data routing at all |
| `builtin.fanout` | a collection | **one** (`branch`) | *(no choice â€” every item)* | every item, same leg, concurrently |
| **the ask** | a collection | named, user-defined | **what each item is** | each item goes to the leg its group says |

So the ask is genuinely new: **Switch's routing table applied per-item to FanOut's collection** â€” a
*partition*. `Foo â†’ leg A; Bar, Baz â†’ leg B` is exactly a Switch `cases` table, except evaluated
for every item of a collection instead of once.

## Summary

| # | Item | Kind | Size | Status |
| --- | --- | --- | --- | --- |
| **Clarity (feedback 2 â€” not blocked)** | | | | |
| K1 | FanOut `branch` sub-graph gets a visual region on the canvas (like loop bodies have) | UX | S | â¬œ |
| K2 | Port tooltips on the canvas â€” surface each port's schema description on hover | UX | S | â¬œ |
| K3 | FanOut port/property descriptions rewritten for beginners (say "each item", name the `item`/`index` payload) | Docs/UX | S | â¬œ |
| K4 | Docs: a worked "what actually happens" walkthrough for fanout (items â†’ N branches â†’ fanin) | Docs | S | â¬œ |
| K5 | Designer warning: FanOut `branch` wired to nothing (runs N times into the void) | UX | S | â¬œ |
| **Partitioned legs (feedback 1 â€” decided, Q1 = both modules)** | | | | |
| P1 | `builtin.partition` module â€” split a collection into named legs by per-item routing rules | Feature | M | â¬œ |
| P1b | `builtin.split` module â€” object-property split: one output port per named property (Q1 = both) | Feature | M | â¬œ |
| P2 | Designer support: dynamic output ports from the routing table (Switch already sets the precedent) | UX | M | â¬œ |
| P3 | Region + clarity affordances for P1 (reuse K1/K2 machinery) | UX | S | â¬œ |
| P4 | Tests | Tests | M | â¬œ |
| P5 | Docs | Docs | S | â¬œ |

---

## Findings ðŸ”¬

### F1 â€” "Fan out by item" is a partition, and nothing does it today

- `SwitchModule` routes **one value** to one of many named ports via its `cases` table
  (`{ match, port }`, first match wins, optional `defaultPort`). Exactly the *shape* of routing the
  users described â€” but for a single value, not a collection's items.
- `FanOutModule` is declarative: it returns `ModuleResult.WithParallel` with `Items`, and the
  engine's `ParallelExecutionCoordinator` runs the `branch` sub-graph once per item (inputs `item`
  + `index`). One leg only; no per-item choice.
- `builtin.parallel` has named legs but routes **no data** â€” its branch ports are pure triggers.

### F2 â€” Two different reads of "the input has Foo and Bar and Baz" âš ï¸

The feedback supports two readings, and they are **different modules**:

- **(a) Collection partition** â€” input is a *list* of items of kinds Foo/Bar/Baz; each item goes to
  the leg its kind maps to. Legs receive **sub-collections**.
- **(b) Object split** â€” input is an *object* with properties `Foo`, `Bar`, `Baz`; each property
  value goes out its own port. Legs receive **single values**. (Closer to a "destructure" module;
  note `builtin.fanin` mode `Named` is precisely its inverse.)

The "Bar and Baz on a separate leg" phrasing fits both. **This is Q1** â€” the single most important
question, because (a) and (b) share almost no implementation.

### F3 â€” Loops get a visual body region; fanout doesn't. That's most of feedback 2

`StructuralRegions` draws a labelled box around the downstream closure of `loopBody` / `try` /
`catch` / `finally` / `transactionBody` â€” but `NodePorts.StructuralPortNames` does **not** include
fanout's `branch` (or parallel's branch ports). So a foreach body is visibly "a body that runs per
item", while the *concurrent* equivalent is just a bare edge. The single highest-leverage clarity
fix is adding `branch` to the structural-region machinery with a label like *"ðŸŒŸ per item
(parallel)"*.

Caveat to verify during implementation: `IsStructuralPort` matches by **port name only**, so a
plain module with an output that happens to be named `branch` would grow a region. Check whether
region computation can consult the source node's module id (it has the node â€” likely trivial).

### F4 â€” The `item`/`index` payload is invisible until runtime

Inside the branch sub-graph, each run receives `item` + `index` inputs â€” but nothing in the
designer says so. The `branch` port's schema description is a blank `PortDefinition.Create<object>`
(no description at all), and canvas ports render bare labels with no tooltips (only the palette
shows module descriptions). K2 (port tooltips, generic) + K3 (actually write the descriptions) fix
this for every module at once, fanout included.

### F5 â€” FanOut's `failFast` metadata contradicts itself ðŸ› (drive-by)

`FanOutModule` property description says *"default false"* and declares `DefaultValue: false`, but
`ExecuteAsync` initialises `var failFast = true` when the property is absent. The docs table says
default `true`. Small, but it's exactly the kind of thing confused testers hit. Fix the code-vs-
metadata mismatch as part of K3 (decide which default is intended â€” the sibling `parallel` module
defaults `true`).

### F6 â€” Related plan overlap

[Designer-Multi-Input-Join-Plan.md](Designer-Multi-Input-Join-Plan.md) (not yet implemented) owns
the **fan-in/input side** (J-items) and flags `builtin.parallel` branch-port wireability (J11).
This plan deliberately stays on the **output side**. P2's dynamic-output-port work should be built
so J5's dynamic-*input*-port rendering can share it, but neither blocks the other.

---

## Proposed design (P-items) ðŸŽ¨ â€” pending Q1

Assuming Q1 = collection partition (recommended; see Q1):

**`builtin.partition`** â€” "Split items into legs." One `items` input (same coercion as
FanOut/ForEach). A `rules` table in Switch's format, evaluated **per item**:

```jsonc
{
  "id": "split_1",
  "moduleId": "builtin.partition",
  "properties": {
    // Same shape as Switch cases~ first match wins, per item.
    "rules": "[ { \"match\": \"Foo\", \"port\": \"foos\" }, { \"match\": \"Bar\", \"port\": \"others\" }, { \"match\": \"Baz\", \"port\": \"others\" } ]",
    "matchOn": "item",            // or a property path like "item.kind" for object items
    "defaultPort": "unmatched"     // optional; omitted â†’ unmatched items dropped? (Q3)
  }
}
```

- Each output port emits **the sub-collection** of items that matched it (empty list if none â€”
  ports always fire, so downstream isn't silently skipped; consistent with D2 below).
- Two rules mapping to the same port (Bar *and* Baz â†’ `others`) is the "separate leg" ask â€” no
  special grouping syntax needed, the table already expresses it.
- **Sequential, not parallel** â€” partition just *sorts items into buckets*. Users who want
  concurrent processing per bucket chain a `fanout` after a leg. One job per module keeps each one
  explainable (the whole point of this feedback round).
- Dynamic output ports exactly like Switch (empty declared outputs â†’ port-name validation skipped);
  the designer derives ports from the `rules` table (P2), the same trick `NodePorts` already plays
  for trycatch/transaction.

**Decisions baked into the above:**

| # | Decision |
|---|----------|
| **D1 Partition is a new module, not a FanOut mode** | FanOut is already the confusing one; giving it a second personality makes feedback 2 worse. A module named for what it does *is* the clarity fix. |
| **D2 Empty legs still emit `[]`** | A leg that silently never fires is the F2-of-the-last-plan trap all over again. Empty list â‰  no signal. |
| **D3 Rules reuse Switch's `{match, port}` vocabulary** | One routing grammar to learn across the toolbox, not two. `matchOn` extends it to object items without breaking the simple case. |

---

## Open questions â“ â€” all decided 2026-08-04 âœ…

- [x] **Q1 â€” Which reading of "the input has Foo and Bar and Baz" (F2)?** âœ… **Both, as two
      separate modules**: `builtin.partition` (collection â†’ legs by rules) and `builtin.split`
      (object property â†’ its own port). Each does one explainable thing (D1 spirit).
- [x] **Q2 â€” How do items declare their kind?** âœ… `matchOn` property path with plain string
      equality (default: the item itself). Can grow into predicates later.
- [x] **Q3 â€” What happens to unmatched items?** âœ… With `defaultPort` set they go there; without
      it, **fail the run** listing the unmatched values. Silent dropping is invisible data loss.
- [x] **Q4 â€” Should `builtin.parallel`'s branch ports also get regions (K1 scope)?** âœ… Yes, if
      cheap â€” same mechanism (noting its ports are unwireable until the other plan's J11).
- [x] **Q5 â€” FanOut `failFast` default (F5)?** âœ… `true` is intended â€” fix the property metadata
      (and description) to match the code and docs.

---

## Items

### K1 â€” FanOut branch region ðŸŒŸ

- [x] K1.1 Add fanout's `branch` to the structural-region derivation, labelled
      *"ðŸŒŸ per item (parallel)"* â€” kind `fanout` for CSS.
- [x] K1.2 Guard against port-name collisions with non-fanout modules (F3 caveat) â€” region only
      when the source node's module is `builtin.fanout`.
- [x] K1.3 Region styling consistent with loop/try regions (tokens.css).
- [x] K1.4 Q4 decides whether `builtin.parallel` joins in.

### K2 â€” Port tooltips ðŸ”Œ

- [x] K2.1 `NodeView` port rows get `title` from the port's schema `Description` (input + output),
      falling back to nothing (no tooltip) when blank.
- [x] K2.2 Verify `ModuleSchemaDto.PortDefinitionDto.Description` is populated end-to-end (it's in
      the DTO; check the server maps it).

### K3 â€” FanOut speaks human ðŸ—£ï¸

- [x] K3.1 Rewrite FanOut's module/port/property descriptions: `branch` = *"runs once per item â€”
      receives `item` and `index`"*, `done` = *"fires after all items finish"*, `items` = *"the
      collection; each element becomes one parallel run"*.
- [x] K3.2 Fix the `failFast` code/metadata mismatch per Q5 (F5).
- [x] K3.3 Update the module-count/roster integration tests if descriptions are asserted anywhere.

### K4 â€” Docs walkthrough ðŸ“š

- [x] K4.1 Add a "What actually happens" subsection under `builtin.fanout` in
      [`docs/advanced-flow-control.md`](../docs/advanced-flow-control.md): items in â†’ branch runs
      per item with `item`/`index` â†’ results/fanin out, with a small diagram.
- [x] K4.2 Cross-link switch vs fanout vs parallel vs (future) partition â€” the table from *The gap
      in one table* belongs in the docs.

### K5 â€” Unwired branch warning ðŸ§­

- [x] K5.1 `GraphValidator`: FanOut node whose `branch` port has no outgoing connection â†’ warning
      *"Fan Out's branch isn't connected â€” each item will run an empty sub-graph."*
- [x] K5.2 Tests.

### P1â€“P5 â€” Partitioned legs & object split (Q1 = both)

- [x] P1 `builtin.partition` per the proposed design; registration; `ValidateConfiguration`
      (rules table parses, ports named, `matchOn` path syntax).
- [x] P1b `builtin.split` â€” input `value` (object); property `keys` (JSON array of property
      names, each becomes an output port emitting that property's value); optional `restPort`
      (remaining properties as one object). Missing key â†’ `null` output (objects legitimately have
      optional fields; the inverse of `builtin.fanin` mode `Named`). All ports fire (D2).
- [x] P2 Designer derives output ports from the `rules` property (partition) and the
      `keys`/`restPort` properties (split) â€” extend the `NodePorts` dynamic machinery; keep it
      shareable with the other plan's J5.
- [x] P3 Clarity affordances for the new modules from day one (K2/K3 patterns applied).
- [x] P4 Module tests (partition semantics, empty legs, unmatched handling per Q3, coercion;
      split keys/rest/missing); designer tests (ports from rules/keys, validation).
- [x] P5 Docs (module sections + the comparison table cross-links).

---

*Created 2026-08-04. Findings verified against `FanOutModule.cs`, `SwitchModule.cs`,
`ParallelModule.cs` (schema), `NodePorts.cs`, `StructuralRegions.cs`, and
`docs/advanced-flow-control.md`. Implemented and verified 2026-08-04.*

---

## What shipped 📦

| File | Change |
| --- | --- |
| `Workflow.Modules/Builtin/Flow/PartitionModule.cs` | New — `builtin.partition`: `{match, port}` rules per item, `matchOn` path, `defaultPort`-or-fail (Q3), empty legs emit `[]` (D2), `counts`/`total`, fire-all `Ok`. |
| `Workflow.Modules/Builtin/Transform/SplitModule.cs` | New — `builtin.split`: `keys` → one port each (missing → `null`), optional `restPort`. |
| `Workflow.Modules/Builtin/Flow/FanOutModule.cs` | K3: beginner descriptions on module/ports/properties; `failFast` metadata fixed to default `true` (Q5). |
| `Workflow.Modules/Builtin/BuiltinModuleRegistration.cs` | Both modules registered. |
| `Workflow.UI.Client/Designer/State/NodePorts.cs` | P2: `PropertyDerivedOutputs` — output ports derived live from `rules` (partition), `keys`/`restPort` (split), `cases`/`defaultPort` (**switch — its case ports were unwireable before**), `branches`/`branchCount` (**parallel — J11 of the join plan, fixed here for free**). `IsStructuralEdge` (module-aware). |
| `Workflow.UI.Client/Designer/State/StructuralRegions.cs` | K1: fanout `branch` → *"🌟 per item (parallel)"* region; parallel branch ports → *"🌐 branch: name"* regions; module-guarded so a plain port named `branch` stays plain (F3 caveat). |
| `Workflow.UI.Client/Designer/State/GraphValidator.cs` | K5: `ValidateFanOut` — unwired `branch` → warning. |
| `Workflow.UI.Client/Designer/Components/NodeView.razor` | K2: port `title` tooltips from schema port descriptions. |
| `Workflow.UI.Client/Designer/Components/EdgeLayer.razor` | Structural edge styling now module-aware. |
| `wwwroot/css/tokens.css` | `df-region--fanout` / `df-region--parallel` styles. |
| `Workflow.Tests/Modules/Flow/PartitionModuleTests.cs`, `Transform/SplitModuleTests.cs` | New module test suites. |
| `Workflow.Tests/Modules/BuiltinModuleIntegrationTests.cs` | Roster/count 40 → 42. |
| `Workflow.Tests.UI/State/FanOutClarityTests.cs`, `Components/PortTooltipTests.cs` | New — 19 UI tests (regions, warning, derived ports, anchor math, tooltips). |
| `docs/advanced-flow-control.md` | "Which fan-shaped module do I want?" table; FanOut "What actually happens" walkthrough + diagram; partition/split sections; module index. |

**Verification.** Targeted server tests (Partition/Split/FanOut/Switch/Integration): 62/62 green.
`Workflow.Tests.UI`: 615/615 green. Full solution build: 0 errors.
