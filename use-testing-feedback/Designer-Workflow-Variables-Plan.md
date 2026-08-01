# Designer Workflow Variables — Resolution Plan

> 📋 Response to [`Designer-Workflow-Variables.md`](Designer-Workflow-Variables.md)
> (user testing, 2026-08-01). Same format as
> [`Designer-UX-Feedback-Plan-Round2.md`](Designer-UX-Feedback-Plan-Round2.md): each item records
> what the code does today, the proposed resolution, and slices. Checkboxes tick as work lands.
>
> ⚠️ **Read the "Structural findings" section first.** This round is *not* primarily a UX gap.
> Three engine-level gaps mean that variables largely **do not work end-to-end today** — which is
> almost certainly *why* the feedback reads as confusion rather than as a feature request.

## Summary

| # | Feedback item | Kind | Size | Status |
| --- | --- | --- | --- | --- |
| V1 | No way to declare workflow variables in the designer (#1) | **Gap** | M | ☐ |
| V2 | Declared variables' `InitialValue` never reaches a run | **Bug** | S | ☐ |
| V3 | Global / workflow-scoped stored variables never hydrate into a run (#2) | **Bug** | M | ☐ |
| V4 | `{{…}}` tokens are **not** expanded in node *properties* (#3) | **Bug** | M–L | ☐ |
| V5 | Run dialog is a raw JSON blob, not a typed variable form (#1) | UX | M | ☐ |
| V6 | Globals invisible in the designer's token picker (#2) | UX | S | ☐ |
| V7 | No lint for unknown / misspelled variable references (#3) | UX | S | ☐ |
| V8 | No documentation of the variable lifecycle (#1, #2, #3) | Docs | S | ☐ |

Recommended order: **V2 → V4 → V1 → V3 → V6 → V7 → V5 → V8**.
Rationale: make the *semantics* true before building UI on top of them. Shipping V1 (an authoring
panel) first would let users declare variables that still silently do nothing — worse than today.

---

## Structural findings — what the code actually does today 🔬

Verified by reading the code, not inferred.

### F1 — Variables *are* modelled, in three separate places that don't meet

| Layer | Type | File |
| --- | --- | --- |
| Definition (design time) | `VariableDefinition(Name, Type: PropertyType, InitialValue: JsonElement?, Description)` in `WorkflowDefinition.Variables` (`HashMap<string, VariableDefinition>`) | `Workflow.Core/Models/VariableDefinition.cs:21`, `Workflow.Core/Models/WorkflowDefinition.cs:38` |
| Runtime (per execution) | `WorkflowExecutionContext.Variables` (`HashMap<string, object?>`) | `Workflow.Engine/Models/WorkflowExecutionContext.cs:49` |
| Persisted (versioned, scoped) | `VariableEntry(Scope, Name, Value, ValueTypeName, Version, …)` behind `IVariableStore`; `VariableScope` = **Global / Workflow / Execution** | `Workflow.Persistence/Models/VariableEntry.cs:16`, `Workflow.Persistence/Models/VariableScope.cs:10` |

So a **three-level scope model already exists** (global, workflow, execution) and is fully exposed
over REST at `/api/v1/variables?scope=…&scopeId=…` with get / set / delete / version history
(`Workflow.Api/V1/VariableEndpoints.cs:28-197`). The user's question #2 ("workflow-specific vs
global") has a *correct answer in the data model* — it just isn't reachable from the product.

### F2 — 🐛 Declared variables never reach the execution context

`WorkflowExecutor` seeds the context from the **run request inputs only**:

```csharp
// Workflow.Engine/Actors/WorkflowExecutor.cs:255-260
_context = WorkflowExecutionContext.Create(
    ...
    initialVariables: inputs.ToHashMap());
```

`definition.Variables` is **never read**. A variable declared on the workflow — with a type, a
description and an `InitialValue` — has **zero runtime effect**. `{{Variable.x}}` fails unless `x`
happened to be passed in the run inputs or was written by a `SetVariable` node earlier in the graph.

### F3 — 🐛 `IVariableStore` is write-only from the engine

The executor resolves the store and writes to it (`VariableWriteMode` = `Execution` / `Workflow` /
`Dual`, `WorkflowExecutor.cs:2734-2770`), but **never reads it**. Grepping `GetVariableAsync` /
`ListVariablesAsync` across `Workflow.Engine` returns **zero hits** — the only readers are the REST
endpoints. Consequences:

- **Global variables are inert at runtime.** You can `PUT /api/v1/variables/apiBaseUrl?scope=global`
  and no workflow can ever reference it via `{{Variable.apiBaseUrl}}`.
- **`VariableWriteMode.Workflow` is a one-way trip.** Values written to workflow scope are never
  hydrated into the next execution, so the "persist across runs" affordance doesn't round-trip.

### F4 — 🐛 `{{…}}` is expanded in module **inputs**, never in node **properties**

This is the most damaging one, because *properties are the entire designer editing surface*.

```csharp
// Workflow.Engine/Actors/NodeExecutor.cs:208 — only the INPUT schema is bound
var bindingResult = binder.BindProperties(_inputs, module.Schema.Inputs, bindingContext);

// Workflow.Engine/Actors/NodeExecutor.cs:575-579 — properties are passed through RAW
foreach (var prop in _nodeDefinition.Properties)
{
    properties[prop.Key] = ConvertJsonElement(prop.Value);   // no reference resolution
}
```

`GatherNodeInputs` (`WorkflowExecutor.cs:699+`) builds `_inputs` from workflow inputs and upstream
node outputs — node properties are **not** merged in. So:

- `HttpRequestModule`'s `url` is a **Property** whose own description reads *"Absolute request URL.
  Supports `{{Variable.Name}}` references~ 🌐"* (`Workflow.Modules/Builtin/Http/HttpRequestModule.cs:144-147`)
  and `docs/http-and-network.md:424` repeats the claim — but the module receives the literal string
  `"{{Variable.host}}/orders"`. The module even *skips URL validation* when it sees `{{`
  (`HttpRequestModule.cs:378-380`), so the failure surfaces late and cryptically.
- The designer's `{{x}}` picker and **ƒx builder** are wired to `PropertyEditor`, i.e. to
  properties (`PropertyEditor.razor:195` — `Text`/`Expression`/`MultilineText`/`FilePath`/`DirectoryPath`).
  **Every token a user inserts through the UI we shipped in Round 2 lands somewhere the engine
  doesn't expand.**
- The only property-shaped path that *does* work is the bespoke retrofit for SQL parameter values
  (`Workflow.Modules.Database/Internal/SqlParameterTemplateResolver.cs`), added in G9.4 — whose
  header comment already documents this exact gap.
- No test covers property-level expansion: `{{Variable.` appears only in `PropertyBinderTests`,
  `PropertyBinderExpressionTests` (both input-schema based) and `DatabaseQueryModuleTests`.

### F5 — No authoring UI, so the picker is empty anyway

`DesignerDocument.Variables` is a lossless **passthrough** (`DesignerDocument.cs:42`, round-tripped
in `FromDto`/`ToDto`) and nothing ever mutates it — there is no variables command in
`Commands.cs`, and the workflow properties pane offers only Name / Description / Tags
(`Designer.razor:237-252`). `VariableTokens.OptionsFor` builds its **Variables** group from
`document.Variables`, so in practice that group is **always empty** and the picker shows only
upstream node outputs. A user following the ƒx hints (`ExpressionBuilder.cs:26-33`, which advertise
`{{Variable.count}}`) has no way to make `count` exist.

### F6 — Documentation describes referencing, never defining

`docs/designer.md:57-67` explains the `{{x}}` picker and ƒx builder; `docs/rest-api.md:154-166`
documents the variables endpoints; `docs/scripting.md` documents `getVariable`/`setVariable`.
**Nothing** explains where a workflow variable comes from, what the three scopes mean, or which
fields expand templates. There is no `docs/variables.md`.

---

## V1 — Workflow Variables panel 🧾

**Finding.** See F5. No create/rename/delete/default-value affordance anywhere in the designer.

**Resolution.** A **Variables** section in the workflow properties pane (shown when nothing is
selected, beside Name/Description/Tags), plus a fuller modal for larger sets.

- [ ] V1.1 `WorkflowVariables` state helper (framework-free, `Designer/State/`): parse/emit the
      `VariableDefinition` JSON shape (`name`, `type`, `initialValue`, `description`), name
      validation (`^[a-zA-Z_][a-zA-Z0-9_.]*$` — matching `SetVariableModule`'s runtime regex),
      **case-insensitive** uniqueness (the binder resolves case-insensitively — see Q6), and
      `PropertyType` ↔ editor mapping.
- [ ] V1.2 `AddVariableCommand` / `EditVariableCommand` / `RemoveVariableCommand` in
      `Commands.cs` so variable edits are undoable and mark the document dirty like every other edit.
- [ ] V1.3 Panel UI: list of declared variables (name · type · default · 🔗 usage count), inline
      add row, edit/remove per row. Initial value edited with the editor matching the declared
      `PropertyType`.
- [ ] V1.4 **Usage awareness** — show how many nodes reference each variable, and warn on
      rename/delete when references exist (offer "rename references too", a pure text substitution
      over property values).
- [ ] V1.5 `VariableTokens.OptionsFor` gains type + description so the picker entries read
      `count — Int · "orders processed so far"`.
- [ ] V1.6 Tests: command undo/redo, round-trip through `ToDto`/`FromDto` preserving unknown JSON
      fields, name validation, rename-with-references.

## V2 — Seed declared variables into the run 🌱

**Finding.** F2 — `definition.Variables` is never read by the engine.

**Resolution.**

- [ ] V2.1 In `WorkflowExecutor`, build the initial variable map as
      **declared `InitialValue`s first, then run inputs override** (inputs win, so ad-hoc runs stay
      easy). Declared-but-no-initial-value variables materialise as *present null* or stay absent —
      see **Q3**.
- [ ] V2.2 Convert `JsonElement` initial values to CLR values with the existing
      `ConvertJsonElement` helper so types match `PropertyType`.
- [ ] V2.3 Engine tests: declared default visible to a `GetVariable` node; run input overrides the
      declared default; absent variable still errors as today.

## V3 — Hydrate global / workflow-scoped variables 🌍

**Finding.** F3 — the store is write-only from the engine, so scope is a data-model fiction.

**Resolution.** Give the executor a read path at startup, with an explicit precedence chain.

- [ ] V3.1 At execution start, when `IVariableStore` is present, load
      `VariableScope.Global` then `VariableScope.ForWorkflow(definition.Id)` and layer them under
      the declared defaults and run inputs. **Proposed precedence (lowest → highest):**
      `Global` → `Workflow (stored)` → `Declared InitialValue` → `Run inputs`. See **Q1**.
- [ ] V3.2 Make hydration failures non-fatal but *visible* (log + an execution warning), since the
      store is an optional DI service.
- [ ] V3.3 Decide and document whether hydration is a snapshot at start (proposed) or a live
      read-through per node (costlier, race-prone).
- [ ] V3.4 Tests: global value referenced by `{{Variable.x}}`; workflow-scope value written by a
      previous run with `VariableWriteMode.Workflow` is visible in the next run (closing the
      round-trip); precedence order asserted.

## V4 — Expand `{{…}}` in node properties 🔗

**Finding.** F4 — the single biggest correctness gap; it invalidates the Round-2 ƒx/token UX and
several module descriptions and doc pages.

**Resolution.** Resolve references in node properties in `NodeExecutor.BuildExecutionContext`,
using the *existing* `PropertyBinder` reference machinery so semantics match inputs exactly
(whole-token keeps its type, embedded tokens interpolate, expressions evaluate).

- [ ] V4.1 Extend `IPropertyBinder` with a property-oriented entry point that takes
      `Arr<ModulePropertyDefinition>` (properties have no `PortDefinition`), reusing
      `ResolveReferences` verbatim.
- [ ] V4.2 Call it from `NodeExecutor.BuildExecutionContext` before handing `Properties` to the
      module.
- [ ] V4.3 **Opt-out is mandatory** — some properties must never be expanded:
      SQL `query`/`command` (D7: values go through parameters, never concatenation), script bodies,
      Linq user code, and arguably all `Code`/`Json` editors. Proposed rule: expand exactly the set
      the designer already offers tokens on (`PropertyEditor.SupportsTokens` =
      `Text`/`Expression`/`MultilineText`/`FilePath`/`DirectoryPath`), so UI affordance and engine
      behaviour are defined by one list. See **Q2**.
- [ ] V4.4 Once V4.3 lands, retire or re-base `SqlParameterTemplateResolver` so there is one
      resolution path, not two (it stays if parameter *values* remain outside the property rule).
- [ ] V4.5 Failure mode: an unresolvable reference in a property should fail the node the way an
      input does (`ReferenceResolution.Failed`) rather than silently passing `{{…}}` downstream —
      but this is a **behaviour change for existing workflows**. See **Q4**.
- [ ] V4.6 Tests: HTTP `url` built from `{{Variable.host}}`; whole-token type preservation on a
      numeric property; `Code`/SQL properties provably *not* expanded; unresolved reference
      behaviour per Q4. Add an integration test — none exists today.
- [ ] V4.7 Audit module property descriptions + `docs/http-and-network.md:424` etc. so claims match
      reality after the fix.

## V5 — Typed run-inputs dialog ▶️

**Finding.** `Designer.razor:88-99` renders a bare `<textarea>` for run inputs and
`StartRun` (`:651-687`) just `JsonSerializer.Deserialize<Dictionary<string, JsonElement>>` it. Users
must know both the variable names and JSON syntax, with no discovery.

**Resolution.**

- [ ] V5.1 Generate a **form** from the declared variables (V1): one field per variable, editor by
      `PropertyType`, pre-filled with `InitialValue`, description as helper text.
- [ ] V5.2 Keep a "raw JSON" toggle as the escape hatch for extra/undeclared inputs (see **Q5**).
- [ ] V5.3 Surface `VariableWriteMode` (`execution` / `workflow` / `dual`) as an explicit choice —
      the API already accepts it (`ExecutionContracts.cs:15-18`) and the UI never sends it. This is
      the natural place to teach "does this run's writes persist for later runs?".
- [ ] V5.4 Tests: form generated from declared variables; values serialise into `Inputs`; raw JSON
      mode still works; invalid input surfaces the existing toast.

## V6 — Show globals in the designer 🌐

**Finding.** No client-side variables API exists (`Workflow.UI.Client/Api` has no variables client);
the picker can only ever offer document-local variables and upstream outputs.

**Resolution.**

- [ ] V6.1 `VariablesClient` in `Workflow.UI.Client/Api` wrapping `/api/v1/variables`
      (list / get / set / delete / history).
- [ ] V6.2 Fetch **global** variables once per designer session and add a third picker group
      **"Globals"** alongside Variables and Inputs, clearly badged as shared across workflows.
      Degrade silently (group hidden) when the call fails — the picker must not become a hard
      dependency on the API.
- [ ] V6.3 Read-only by default; whether the designer may *edit* global values is **Q7**.
- [ ] V6.4 Tests: globals group rendered; fetch failure hides the group without breaking the picker.

## V7 — Lint unknown variable references 🧭

**Finding.** `GraphValidator` has no notion of variables or tokens (grep for `variable|token|{{`
returns nothing). A typo in `{{Variable.cout}}` is discovered only when the node fails at run time.

**Resolution.**

- [ ] V7.1 Extend the designer validator: scan property values for `{{Variable.X}}` and warn when
      `X` is neither declared (V1) nor a known global (V6). **Warning, not error** — a value may
      legitimately be created at run time by an upstream `SetVariable` node.
- [ ] V7.2 Suppress the warning when some upstream node is a `builtin.setvariable` writing that
      name (a cheap static check that removes most false positives).
- [ ] V7.3 Differentiate the existing 🔗 bound badge: declared/known = neutral, unknown = ⚠️.
- [ ] V7.4 Tests: unknown name warns; declared name doesn't; upstream `SetVariable` suppresses.

## V8 — Document the variable lifecycle 📚

- [ ] V8.1 New `docs/variables.md`: the three scopes and when to use each, precedence order (V3),
      declaring variables in the designer (V1), supplying values at run time (V5), referencing them
      (`{{Variable.x}}`, `{{nodeId.port}}`, expressions), **exactly which fields expand templates**
      (V4.3) and which deliberately don't (SQL, scripts, Linq) and why.
- [ ] V8.2 Cross-link from `docs/designer.md`, `docs/rest-api.md` (§Variables),
      `docs/scripting.md`, `docs/module-author-guide.md`.
- [ ] V8.3 A worked example: a global `apiBaseUrl`, a workflow-scoped `lastRunAt` persisted with
      `VariableWriteMode.Workflow`, and a per-run `orderId` — one page that answers all three
      feedback bullets.

---

## Questions — OPEN ❓

These change the shape of the work; I'd like answers before starting V2/V3/V4.

- [ ] **Q1 (V3): Scope precedence.** Proposed lowest → highest:
      `Global` → `Workflow (stored)` → `Declared InitialValue` → `Run inputs`.
      The debatable step is placing the **declared `InitialValue` above the stored workflow value** —
      it means a design-time default overrides a value a previous run persisted. The alternative
      (stored above declared) makes `InitialValue` a true "first run only" seed. Which do you want?
  - We should allow for InitialValue to have a flag describing whether it should override the stored workflow value or not. This would give module authors more control over the behavior of their workflows.

- [ ] **Q2 (V4): Which properties expand templates?** Proposed: exactly the editor types the
      designer already offers tokens on (`Text`, `Expression`, `MultilineText`, `FilePath`,
      `DirectoryPath`), never `Code`/`Json`/`Secret`. The alternative is an explicit
      `SupportsTemplates` flag on `ModulePropertyDefinition` — more precise and self-documenting for
      module authors, but a schema change that touches every builtin module. Editor-type rule, or
      explicit flag?
  - Explicit flag. The editor-type rule is a convenient shortcut for the current builtins, but it is
    fragile and will break if a new editor type is added that should or shouldn't expand templates.
    An explicit flag is more future-proof and makes the intent clear to module authors.

- [ ] **Q3 (V2): Declared-with-no-initial-value.** Should such a variable materialise as a
      *present null* (so `{{Variable.x}}` resolves to null and `GetVariable.exists` is true), or
      stay absent (so referencing it fails loudly until something sets it)? *Proposed: stay absent —
      "declared" should mean "documented", not "exists".*
  - We should warn but not fail. Make sure to make a note of this in the documentation, especially for validating a workflow.

- [ ] **Q4 (V4.5): Unresolvable reference in a property.** Today it silently passes `{{…}}` through
      as a literal. Making it fail the node matches input behaviour and is far more debuggable, but
      **could break existing saved workflows** that contain a literal `{{`. Fail hard, warn-and-pass,
      or make it configurable per workflow?
  - Fail hard. The UX is already a warning toast for unresolvable inputs, so the user is already
    aware of the problem and can fix it. A warning-and-pass would be confusing because the node
    would still run but produce unexpected results. We should make sure that users still have a way to do a literal {{ via an escape sequence or specific function if needed.

- [ ] **Q5 (V5): Run dialog.** Replace the JSON textarea with a generated form, or add the form and
      keep JSON behind a toggle? *Proposed: form by default + JSON toggle, since undeclared inputs
      are currently legal and some users will already have JSON payloads saved.*
  - Form by Default + JSON toggle. The form is the main path, but the toggle is a necessary escape hatch for
    advanced users.

- [ ] **Q6 (V1): Case sensitivity.** `PropertyBinder` resolves variable names
      **case-insensitively** (`PropertyBinder.cs:280`), so `count` and `Count` are the same variable
      at run time. Should the authoring UI enforce case-insensitive uniqueness (proposed: yes, with
      a clear message), or should we tighten the binder to case-sensitive instead?
  - We should enforce case-insensitive uniqueness in the designer. The binder is already case-insensitive, so this keeps the behaviour consistent.

- [ ] **Q7 (V6): Can the designer write globals?** Read-only reference is safe and is the smaller
      change. Editing global values from the designer is convenient but is effectively an
      environment-configuration action with blast radius across every workflow — and
      `PUT /api/v1/variables` sits behind `WorkflowWrite`, which developers have. Read-only, or
      full edit (perhaps gated on an admin-only Settings screen instead)?
  - We would want a specific permission-gated settings screen for editing globals. The designer should be read-only.

- [ ] **Q8 (scope of the ask): what does "global" mean to the user?** The store already has a
      `Global` scope (one flat shared namespace). The feedback says *"global to multiple
      workflows"*, which could instead mean a **named, shareable variable set** that specific
      workflows opt into (an environments/config-set concept). The former is a small engine change
      (V3); the latter is a new domain concept. Can you confirm which the testers meant?
  - Start with the existing `Global` scope, and if users want a named set later we can add it as a
    follow-up.

- [ ] **Q9 (not in the feedback, but adjacent): secrets.** `VariableDefinition` has **no secret
      flag**, and variable values flow into execution history, the monitor's per-node snapshots and
      the real-time hub. As soon as we make globals real (V3), users *will* put connection strings
      and API keys in them. Should a `IsSecret` flag (masked in UI, redacted in
      history/logs/realtime) be in scope for this round, or tracked separately?
  - Yes, we want an IsSecret flag in this round. IT should not be difficult to implement. 

---

*Created 2026-08-01. Findings F1–F6 verified against the code at that date. No implementation has
started — V2/V3/V4 are engine behaviour changes and are blocked on Q1–Q4.*
