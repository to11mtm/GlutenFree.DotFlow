# Designer Start & End Modules — Plan

> 📋 Response to user feedback (2026-08-02): *"a 'start' module that takes no inputs and lets them
> provide an output (either provided by the user or empty)"* and *"an 'end' module that either
> simply logs the final result or is a no-op for its input"*.
>
> **Revision 2 — implemented and verified.** Scope is small and the conventions are well
> established, so this documents the findings and decisions rather than gating on questions. Two
> decisions worth reviewing are called out at the bottom.

## The ask

| # | Feedback | Reading |
| --- | --- | --- |
| 1 | A **Start** module, no inputs, one output the user provides or leaves empty | A visible anchor for "the workflow begins here" |
| 2 | A **End** module that logs the final result, or is a no-op | A visible anchor for "and this is the result" |
| 3 | "make it easier to understand the start of a Workflow" | The real goal is **legibility** — the engine doesn't need these, people do |

## Summary

| # | Item | Kind | Size | Status |
| --- | --- | --- | --- | --- |
| S1 | `builtin.start` module | Feature | S | ✅ done |
| S2 | `builtin.end` module | Feature | S | ✅ done |
| S3 | Designer validation rules for Start/End misuse | UX | S | ✅ done |
| S4 | Tests | Tests | S | ✅ done |
| S5 | Docs | Docs | S | ✅ done |

---

## Findings 🔬

### F1 — The engine needs no changes ✅

`GetStartNodes()` returns every node with in-degree 0, and fires **all** of them
(`WorkflowExecutor.cs`). A module with no input ports has no way to receive a connection, so a Start
node is a start node automatically. Nothing in the engine needs to know these modules exist.

Likewise `builtin.passthrough` already proves the shape: a trivial module with an `object` in and an
`object` out.

### F2 — ⚠️ A naive End module would **silently erase the workflow's result**

This is the one non-obvious thing here. Workflow outputs are collected from nodes with **no
successors**:

```csharp
// WorkflowExecutor.GatherWorkflowOutputs()
var endNodes = _nodeSuccessors.Where(kv => kv.Value.Count == 0).Select(kv => kv.Key);
foreach (var endNodeId in endNodes)
{
    if (_nodeOutputs.TryGetValue(endNodeId, out var nodeOutputs))
    {
        foreach (var (key, value) in nodeOutputs)
        {
            outputs[$"{endNodeId}.{key}"] = value;
        }
    }
}
```

So if "no-op" were taken literally — an End module that consumes an input and emits nothing —
then **appending an End node to a workflow would replace its outputs with nothing**. The node that
used to be terminal is no longer terminal, and the new terminal node contributes no outputs. The
workflow would complete successfully with an empty result, and the cause would be invisible.

**Therefore End always echoes its input to a `result` output**, whatever its mode. "No-op" means
*"performs no side effect"*, not *"produces nothing"*. This actually makes the workflow result
**more** predictable than before: `end-1.result` instead of whatever the last few nodes happened to
emit.

### F3 — Categories and the palette

Existing categories: `Utilities`, `Flow Control`, `Variables`, `Triggers`, `Network`, `Scripting`,
`Transformation`, `File System`. The palette groups by category. Start/End go in **Flow Control** —
they are flow-shape markers, and a two-module category would fragment the palette for no gain.

### F4 — Validation has a natural home

`GraphValidator` (client-side, `Designer/State/`) already reports cycles, dangling connections and
unknown modules, and is what the designer surfaces while editing. Server-side `WorkflowValidator`
already has a `WF010` "at least one start node" rule.

Start/End misuse is a **legibility** concern, not a correctness one — the engine runs these
workflows fine — so the rules belong in the client validator as warnings, where the author sees
them while editing. They deliberately do **not** block saving.

---

## Decisions ✅

| # | Decision |
|---|----------|
| **D1 Start's value is a template-enabled text property plus a type** | `value` (Text, `SupportsTemplates: true`) and `valueType` (dropdown: `text`/`number`/`boolean`/`json`). Making it template-enabled means a Start node can surface a run input or variable as its output — `{{Variable.orderId}}` — which turns out to be the most useful thing a Start node can do, and costs nothing since V4 already resolves properties. |
| **D2 Blank value → `null` output** | The feedback's "or empty" needs no special type. A blank `value` yields a null output regardless of `valueType`, so "empty" is just the default state. |
| **D3 End always echoes to `result`** | See **F2**. `mode` chooses the *side effect* (`log` / `silent`), never whether the value survives. |
| **D4 Both modules are optional and additive** | No existing workflow changes behaviour. They're documentation you can execute, not new machinery. |
| **D5 Misuse is warned, never blocked** | Multiple Starts, a non-terminal End, and a Start with an incoming connection are all *legal* to the engine. Warn in the designer; don't fail the save. |

---

## Items

### S1 — `builtin.start` 🚀

- [x] S1.1 `StartModule` — no inputs; one `value` output.
- [x] S1.2 Properties: `value` (Text, template-enabled) and `valueType`
      (`text`/`number`/`boolean`/`json`, default `text`).
- [x] S1.3 Blank `value` → `null` output (D2).
- [x] S1.4 `ValidateConfiguration` rejects an unknown `valueType`, and invalid JSON when
      `valueType` is `json` — caught at save time rather than at run time.
- [x] S1.5 Registered in `BuiltinModules.GetAll()`.

### S2 — `builtin.end` 🏁

- [x] S2.1 `EndModule` — one optional `result` input; one `result` output that **always** echoes
      the input (F2).
- [x] S2.2 `mode` property: `log` (default) or `silent`.
- [x] S2.3 `log` writes the value at a configurable `level` (reusing the Log module's level names).
- [x] S2.4 Registered in `BuiltinModules.GetAll()`.
- [x] S2.5 *(added during implementation)* `label` property — a template-enabled prefix for the
      logged line, so "which workflow just finished?" is answerable from the log alone.

### S3 — Designer validation 🧭

- [x] S3.1 More than one Start node → warning (the engine starts them **all**, in parallel — which
      is legal, and rarely what someone drawing a "start" intends).
- [x] S3.2 A Start node with an incoming connection → warning. Structurally impossible to draw in
      the designer (no input ports), but reachable via an imported or hand-edited file, which is a
      live path now that import exists.
- [x] S3.3 An End node with outgoing connections → warning: the nodes after it become the
      workflow's result instead, which is precisely the confusion these modules exist to prevent.
- [x] S3.4 Tests for each rule.

### S4 — Tests 🧪

- [x] S4.1 `StartModuleTests` — metadata, each `valueType`, blank → null, template resolution is
      the binder's job, config validation. *(23 tests)*
- [x] S4.2 `EndModuleTests` — echo in both modes, logging behaviour, missing input. *(30 tests)*
- [x] S4.3 A regression guard for **F2**: an End module's outputs are non-empty, so workflow output
      collection still has something to collect.
- [x] S4.4 *(needed during implementation)* `BuiltinModuleIntegrationTests` updated — it asserts an
      exact module roster and count (38 → 40) and a discovery roster, all three of which the two new
      modules break by existing.

### S5 — Docs 📚

- [x] S5.1 Document both in [`docs/advanced-flow-control.md`](../docs/advanced-flow-control.md) — a
      new "Start & End Markers" section, plus the TOC and the Quick Module Index.
- [x] S5.2 Explain the `end-1.result` output-shape consequence (F2) — it's the useful half of the
      trap.

---

## What shipped 📦

| File | Change |
| --- | --- |
| `Workflow.Modules/Builtin/StartModule.cs` | New — `builtin.start`. |
| `Workflow.Modules/Builtin/EndModule.cs` | New — `builtin.end`, with the F2 rationale in the class remarks. |
| `Workflow.Modules/Builtin/BuiltinModuleRegistration.cs` | Both registered, at the top of the list. |
| `Workflow.UI/Workflow.UI.Client/Designer/State/GraphValidator.cs` | New `ValidateStartAndEnd`, called from `Validate`. |
| `Workflow.Tests/Modules/StartModuleTests.cs` | New — 23 tests. |
| `Workflow.Tests/Modules/EndModuleTests.cs` | New — 30 tests. |
| `Workflow.Tests/Modules/BuiltinModuleIntegrationTests.cs` | Roster + count updated (38 → 40). |
| `Workflow.Tests.UI/State/StartEndValidationTests.cs` | New — 8 tests. |
| `docs/advanced-flow-control.md` | New "Start & End Markers" section. |

**Verification.** `Workflow.Tests.UI` 574/574 green. `Workflow.Tests` 1533/1538 — the 5 failures are
the known parallel-contention flakes (different test names on each run; all pass in isolation).

---

## Worth a second opinion 🤔

Neither blocks anything; both are easy to change.

- **Category.** Start/End are in **Flow Control** (F3). The alternative is a dedicated
  "Start & End" category, which surfaces them more prominently in the palette — arguably the point,
  since the request is about legibility — at the cost of a category holding two modules.
- **`valueType` vs. a raw JSON editor.** A `value` + `valueType` pair is friendlier than making
  people quote `"hello"`, but it is two concepts where one might do. If it feels redundant, the
  fallback is a single `Json` editor property.

---

*Created 2026-08-02. Findings verified against the code. Implemented and verified in the same pass.*
