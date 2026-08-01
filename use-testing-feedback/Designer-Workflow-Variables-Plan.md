# Designer Workflow Variables — Resolution Plan

> 📋 Response to [`Designer-Workflow-Variables.md`](Designer-Workflow-Variables.md)
> (user testing, 2026-08-01). Same format as
> [`Designer-UX-Feedback-Plan-Round2.md`](Designer-UX-Feedback-Plan-Round2.md): each item records
> what the code does today, the proposed resolution, and slices. Checkboxes tick as work lands.
>
> **Revision 4 (2026-08-01)** — Q1–Q16 all answered and folded in; **no open questions remain**.
> Secret handling is split into its own document per Q13:
> [`Designer-Workflow-Variables-Secrets-Plan.md`](Designer-Workflow-Variables-Secrets-Plan.md).
>
> ⚠️ **Read the "Structural findings" section first.** This round is *not* primarily a UX gap.
> Four engine-level gaps mean that variables largely **do not work end-to-end today** — which is
> almost certainly *why* the feedback reads as confusion rather than as a feature request.

## Summary

| # | Feedback item | Kind | Size | Status |
| --- | --- | --- | --- | --- |
| V1 | No way to declare workflow variables in the designer (#1) | **Gap** | M | ✅ done |
| V2 | Declared variables' `InitialValue` never reaches a run | **Bug** | S | ☐ |
| V3 | Global / workflow-scoped stored variables never hydrate into a run (#2) | **Bug** | M | ☐ |
| V4 | `{{…}}` expanded in inputs but not properties; no author control (#3) | **Bug** | L | ☐ |
| V5 | Run dialog is a raw JSON blob, not a typed variable form (#1) | UX | M | ☐ |
| V6 | Globals invisible in the designer's token picker (#2) | UX | S | ☐ |
| V7 | No lint for unknown / misspelled variable references (#3) | UX | M | ☐ |
| V8 | No documentation of the variable lifecycle (#1, #2, #3) | Docs | S | ☐ |
| V9 | Admin-gated screen for editing global variables (Q7) | Feature | M | ☐ |
| V10 | Secret variables — **split out**, see the secrets plan (Q9/Q13) | Feature | L | ☐ (separate plan) |

Recommended order: **V1.2 → V1 → V2 → V4 → V3 → V6 → V9 → V7 → V5 → V8**. Per **Q16**, V3 ships
**ahead of** the secrets plan, carrying an interim "not for credentials yet" warning (V3.5) that the
secrets plan removes when it lands.

### Sequencing notes 🧭

- **V1.2 (the model change) comes first and is tiny.** V2.1 honours `VariableSeedMode`, which V1.2
  introduces — so V2 cannot be completed before it. Land `VariableSeedMode` + `IsSecret` on
  `VariableDefinition` as a standalone change, then everything else can proceed in parallel.
- **Build order ≠ ship order.** V1's panel must not reach users before V2 lands: a UI that lets you
  declare variables which then silently do nothing is a worse lie than today's absence of UI.
  Building V1 first is fine and preferable; *releasing* it is gated on V2.
- **V2's seed-mode logic is inert until V3.** With no store hydration there is no stored value to
  override, so `SeedOnly` vs `AlwaysOverride` cannot differ. V2 minus the mode comparison is a very
  small change ("declared `InitialValue` reaches the run"); the mode only becomes meaningful once
  V3 exists, and its tests belong with V3.4.
- **V4 is independent of V1–V3** and can run in parallel. Worth knowing: V4 alone revives
  `{{nodeId.port}}` upstream-output tokens, which involve no variables at all — so a meaningful
  slice of the dead Round-2 ƒx/token UX starts working without waiting for the variable work.

---

## Structural findings — what the code actually does today 🔬

Verified by reading the code, not inferred.

### F1 — Variables *are* modelled, in three separate places that don't meet

| Layer | Type | File |
| --- | --- | --- |
| Definition (design time) | `VariableDefinition(Name, Type: PropertyType, InitialValue: JsonElement?, Description)` in `WorkflowDefinition.Variables` | `Workflow.Core/Models/VariableDefinition.cs:21`, `WorkflowDefinition.cs:38` |
| Runtime (per execution) | `WorkflowExecutionContext.Variables` (`HashMap<string, object?>`) | `Workflow.Engine/Models/WorkflowExecutionContext.cs:49` |
| Persisted (versioned, scoped) | `VariableEntry(Scope, Name, Value, ValueTypeName, Version, …)` behind `IVariableStore`; `VariableScope` = **Global / Workflow / Execution** | `Workflow.Persistence/Models/VariableEntry.cs:16`, `VariableScope.cs:10` |

A **three-level scope model already exists** and is fully exposed at
`/api/v1/variables?scope=…&scopeId=…` with get / set / delete / version history
(`Workflow.Api/V1/VariableEndpoints.cs:28-197`). The user's question #2 ("workflow-specific vs
global") has a *correct answer in the data model* — it just isn't reachable from the product.

### F2 — 🐛 Declared variables never reach the execution context

```csharp
// Workflow.Engine/Actors/WorkflowExecutor.cs:255-260
_context = WorkflowExecutionContext.Create(
    ...
    initialVariables: inputs.ToHashMap());
```

`definition.Variables` is **never read**. A variable declared with a type, description and
`InitialValue` has **zero runtime effect**.

### F3 — 🐛 `IVariableStore` is write-only from the engine

The executor writes to it (`VariableWriteMode` = `Execution`/`Workflow`/`Dual`,
`WorkflowExecutor.cs:2734-2770`) but **never reads it** — `GetVariableAsync`/`ListVariablesAsync`
have **zero hits** across `Workflow.Engine`. So global variables are inert at runtime, and
`VariableWriteMode.Workflow` is a one-way trip that never hydrates the next run.

### F4 — 🐛 `{{…}}` is expanded in module **inputs**, never in node **properties**

The most damaging one, because *properties are the entire designer editing surface*.

```csharp
// Workflow.Engine/Actors/NodeExecutor.cs:208 — only the INPUT schema is bound
var bindingResult = binder.BindProperties(_inputs, module.Schema.Inputs, bindingContext);

// Workflow.Engine/Actors/NodeExecutor.cs:575-579 — properties are passed through RAW
foreach (var prop in _nodeDefinition.Properties)
{
    properties[prop.Key] = ConvertJsonElement(prop.Value);   // no reference resolution
}
```

- `HttpRequestModule`'s `url` is a **Property** whose description reads *"Supports
  `{{Variable.Name}}` references"* (`HttpRequestModule.cs:144-147`), repeated in
  `docs/http-and-network.md:424` — but the module receives the literal string. It even *skips URL
  validation* when it sees `{{` (`:378-380`), so the failure surfaces late and cryptically.
- The designer's `{{x}}` picker and ƒx builder are wired to `PropertyEditor` — i.e. to properties.
  **Every token the Round-2 UI inserts lands somewhere the engine doesn't expand.**
- The only working property-shaped path is the bespoke `SqlParameterTemplateResolver` (G9.4), whose
  header comment already documents this exact gap.
- No test covers property-level expansion.

### F5 — No authoring UI, so the picker is empty anyway

`DesignerDocument.Variables` is a lossless passthrough (`DesignerDocument.cs:42`) that nothing
mutates — no variable command in `Commands.cs`, and the workflow pane offers only Name /
Description / Tags (`Designer.razor:237-252`). `VariableTokens.OptionsFor` builds its **Variables**
group from `document.Variables`, so that group is **always empty** in practice.

### F6 — Documentation describes referencing, never defining

`docs/designer.md:57-67`, `docs/rest-api.md:154-166` and `docs/scripting.md` cover *referencing*.
Nothing explains where a variable comes from, what the scopes mean, or which fields expand
templates. There is no `docs/variables.md`.

### F7 — Global variables are **not** admin-gated today

`PUT`/`DELETE /api/v1/variables/{name}` sit behind `WorkflowWritePolicy` for **every scope**,
including `global` (`VariableEndpoints.cs:35-36`), and `WorkflowWrite` is held by Admin **and
Developer** (`docs/rest-api.md:55`). An `AdminPolicy` exists and is used elsewhere
(`WorkflowEndpoints.cs:39`, `ModuleManagementEndpoints.cs:39,51`).

### F8 — 🔒 Secret values would leak through persisted execution surfaces

`ExecutionRecord.Inputs/Outputs` (`ExecutionRecord.cs:18-19`) and
`NodeExecutionRecord.Inputs/Outputs` (`NodeExecutionRecord.cs:23-24`) are persisted verbatim and
projected to the API (`ExecutionContracts.cs:88-89,130-131`), which the monitor's node inspector
renders. **Good news:** the realtime hub is *not* a leak surface — every event
(`Workflow.Api/Contracts/RealTime/RealTimeEvents.cs`) carries only ids, timings and percentages,
never values. Detail moved to the secrets plan.

### F9 — 🛡️ (new) Template expansion on **inputs** is an injection vector

`PropertyBinder` decides to expand based on the **runtime value**, not the declared type:

```csharp
// Workflow.Modules/Binding/PropertyBinder.cs:136
if (rawValue is string stringValue && ReferencePattern.IsMatch(stringValue))
```

And `NodeDefinition` (`Workflow.Core/Models/NodeDefinition.cs:37-47`) carries **only `Properties`** —
there is *no* design-time store for input values. So an input's value at runtime is always either a
run input or an upstream node's output; it is never something an author typed. Consequences:

- **Untrusted data can reference workflow variables.** An HTTP response or DB row containing
  `{{Variable.apiKey}}` flows into a downstream input and is faithfully substituted with the real
  value — which the node may then write to a file, post onward, or store. Q12's escape sequence
  cannot mitigate this, because you cannot escape data you don't control.
- **Benign data gets mangled or kills the node.** A Mustache/Handlebars email template read out of
  a database fails the node with "variable not found".

This is a **pre-existing** defect, not one introduced by V4. It is the reason Q11 was answered
`false` on both sides.

---

## Decisions — Q1–Q15 RESOLVED ✅ (2026-08-01)

**Round 1**

- **Q1 Scope precedence** → `InitialValue` carries a flag controlling whether it overrides the
  stored workflow value. *(Variables are authored by **workflow** authors; the flag lives on
  `VariableDefinition`.)*
- **Q2 Which properties expand** → **Explicit flag**, not an editor-type rule.
- **Q3 Declared-with-no-initial-value** → **Warn, don't fail**; call it out in docs and validation.
- **Q4 Unresolvable reference in a property** → **Fail hard**, plus an escape for a literal `{{`.
- **Q5 Run dialog** → **Form by default + JSON toggle.**
- **Q6 Case sensitivity** → Enforce **case-insensitive uniqueness** in the designer.
- **Q7 Designer writes globals?** → **No** — read-only; editing moves to a permission-gated
  Settings screen (**V9**).
- **Q8 Scope of "global"** → Start with the existing flat `Global` scope; named sets are a follow-up.
- **Q9 Secrets** → **In scope**, as its own workstream (**Q13**).

**Round 2**

- **Q10 Seed/override flag** → **Enum** `VariableSeedMode`. No saved workflows exist yet, so the
  default is not a migration concern; *taking `SeedOnly` as the default* (a stored value wins) since
  it is the least surprising for persisted state.
- **Q11 `SupportsTemplates` defaults** → **`false` on properties** *and* **`false` on input ports**.
  Templates become an authored, opt-in concept everywhere; the F9 injection path closes by default.
- **Q12 Escape syntax** → **Option (a), backslash** — `\{\{not a token}}`.
- **Q13 Secret redaction** → Implement `IsSecret` with real redaction, in a **separate plan**:
  [`Designer-Workflow-Variables-Secrets-Plan.md`](Designer-Workflow-Variables-Secrets-Plan.md).
- **Q14 Breaking the variables API** → **Accept the breakage** (still in testing). No deprecation
  window; workflow/execution-scoped writes stay on `WorkflowWrite`.
- **Q15 Q3 ↔ Q4 reconciliation** → Confirmed: **declaring a variable is a contract.**
  *Declared but unset* → resolves to null, node runs, validation warning. *Undeclared* → fails the
  node.

---

## V1 — Workflow Variables panel 🧾

**Finding.** F5 — no create/rename/delete/default-value affordance anywhere in the designer.

- [x] V1.1 `WorkflowVariables` state helper (framework-free, `Designer/State/`): parse/emit the
      `VariableDefinition` JSON shape, name validation (`^[a-zA-Z_][a-zA-Z0-9_.]*$`, matching
      `SetVariableModule`'s runtime regex), **case-insensitive uniqueness** *(Q6)*, and
      `PropertyType` ↔ editor mapping.
- [x] V1.2 **Land this first, standalone.** Extend `VariableDefinition` (Workflow.Core) with
      `VariableSeedMode Seed = SeedOnly` *(Q10)* and `bool IsSecret = false` *(Q9 — declaration only
      here; behaviour in the secrets plan)*. Both are trailing optional record parameters, so
      existing JSON deserialises unchanged. V2 depends on this, so it should not be buried inside
      the panel work.
- [x] V1.3 `AddVariableCommand` / `EditVariableCommand` / `RemoveVariableCommand` in `Commands.cs`
      so variable edits are undoable and mark the document dirty like every other edit.
- [x] V1.4 Panel UI: list of declared variables (name · type · default · 🔒 · 🔗 usage count),
      inline add row, edit/remove per row. Initial value uses the editor matching the declared
      `PropertyType`; seed mode is a checkbox with plain-language labelling ("keep the value saved
      by previous runs" vs "always reset to this value").
- [x] V1.5 **Usage awareness** — show how many nodes reference each variable; warn on rename/delete
      when references exist, offering "rename references too" (text substitution over property
      values).
- [x] V1.6 `VariableTokens.OptionsFor` gains type + description so picker entries read
      `count — Int · "orders processed so far"`.
- [x] V1.7 Tests: command undo/redo; `ToDto`/`FromDto` round-trip preserving unknown JSON fields;
      name validation incl. case-insensitive collision; rename-with-references.

## V2 — Seed declared variables into the run 🌱

**Finding.** F2.

- [ ] V2.1 In `WorkflowExecutor`, build the initial variable map from declared variables, with run
      inputs winning last. **Depends on V1.2** for `VariableSeedMode`; the mode comparison itself is
      inert until V3 supplies stored values to compare against, so it can land as a no-op and be
      exercised by V3.4. Declared-but-unset variables materialise as **null and warn, never fail**
      *(Q3/Q15)*.
- [ ] V2.2 Convert `JsonElement` initial values via the existing `ConvertJsonElement` helper so
      runtime types match the declared `PropertyType`; report a mismatch as a warning.
- [ ] V2.3 Engine tests: declared default visible to a `GetVariable` node; run input overrides it;
      both seed modes assert the right winner against a stored value; declared-but-unset warns
      rather than failing.

## V3 — Hydrate global / workflow-scoped variables 🌍

**Finding.** F3.

- [ ] V3.1 At execution start, when `IVariableStore` is present, load `VariableScope.Global` then
      `VariableScope.ForWorkflow(definition.Id)`. **Precedence (lowest → highest):**
      `Global` → `Workflow (stored)` → `Declared InitialValue` *(only when
      `Seed = AlwaysOverride`)* → `Run inputs`.
- [ ] V3.2 Hydration failures are non-fatal but *visible* (log + execution warning) — the store is
      an optional DI service.
- [ ] V3.3 Snapshot at start rather than live read-through per node; document the choice.
- [ ] V3.4 Tests: a global referenced by `{{Variable.x}}`; a workflow-scope value written by a
      previous run with `VariableWriteMode.Workflow` visible in the next run (closing the
      round-trip); full precedence asserted in both seed modes.
- [ ] V3.5 **Interim credential warning** *(Q16 — ship V3 first, warning-based)*. Globals become
      functional here, before the secrets plan lands, so every surface that shows or accepts a
      global carries a plain "🔒 Not for credentials yet — values are stored and returned in
      plaintext until secret support ships" notice: `docs/variables.md` (V8.1), the designer's
      Globals picker group (V6.2) and the admin screen (V9.2). Tracked for **removal** by the
      secrets plan (S8) so it can't go stale.
      *Note:* the mechanical name check offered in Q16 option 3 was **declined**, so this is
      advisory only — see "Accepted risk" below.

## V4 — Author-controlled template expansion 🔗

**Finding.** F4 (properties don't expand) **and** F9 (inputs expand indiscriminately). Q11 answers
both with one mechanism: an explicit, default-off flag on each side.

- [ ] V4.1 Add **`SupportsTemplates = false`** to `ModulePropertyDefinition`
      (`ModuleSchema.cs:105-115`) and to `PortDefinition`, as trailing optional record parameters —
      source-compatible with every existing positional call site.
- [ ] V4.2 Project the flag through `ModulePropertyDefinitionDto` → `ModulesClient` → the designer,
      so the `{{x}}` picker and ƒx builder are driven by **the flag** rather than
      `PropertyEditor.SupportsTokens`' hard-coded editor-type list (`PropertyEditor.razor:195`).
      One source of truth shared by engine and UI — a field that doesn't expand shows no token
      button, so the Round-2 confusion mode becomes structurally impossible.
- [ ] V4.3 Extend `IPropertyBinder` with a property-oriented entry point taking
      `Arr<ModulePropertyDefinition>`, reusing `ResolveReferences` verbatim; honour the flag on both
      the property and the input paths.
- [ ] V4.4 Call it from `NodeExecutor.BuildExecutionContext` before handing `Properties` to the
      module.
- [ ] V4.5 **Fail hard** on an unresolvable reference in a template-enabled field *(Q4)*, plus the
      **`\{\{` backslash escape** *(Q12)*, implemented once and applied consistently to properties,
      inputs and SQL parameter values.
- [ ] V4.6 **Opt-in audit (the bulk of V4).** Of 218 property definitions across 174 module files —
      158 Text, 16 Json, 16 Number, 12 Dropdown, 7 Boolean, 4 ConnectionString, 3 Code, 1 Expression
      — opt in the ones that genuinely want templating (HTTP `url`/headers, file paths, text
      fields), and explicitly leave off SQL `query`/`command` (D7), script bodies and Linq user
      code. Separately review the ~166 port definitions; **the expectation is that few or no inputs
      opt back in** (F9) — I'll report the proposed list rather than opting anything in silently.
- [ ] V4.7 Retire or re-base `SqlParameterTemplateResolver` so there is one resolution path, not two.
- [ ] V4.8 Tests: HTTP `url` built from `{{Variable.host}}`; whole-token type preservation on a
      numeric property; SQL/Code properties provably *not* expanded; `\{\{` yields a literal;
      unresolved reference fails the node; **an input carrying `{{…}}` from upstream data is passed
      through untouched** (the F9 regression guard). Add an **integration** test — none exists today.
      Note `PropertyBinderTests` / `PropertyBinderExpressionTests` assert input expansion and must
      be updated to set the flag explicitly.
- [ ] V4.9 Correct the docs and module descriptions that currently over-promise: module property
      `Description` strings, `docs/http-and-network.md:424`, and — because expression evaluation
      (`{{Variable.Count > 5}}`) is documented as an *input* feature in
      `docs/module-author-guide.md:432-435` and `docs/scripting.md:180-201` — restate it as a
      *property* feature, which is where it now belongs.

## V5 — Typed run-inputs dialog ▶️

**Finding.** `Designer.razor:88-99` renders a bare `<textarea>`; `StartRun` (`:651-687`) just
deserialises it. No discovery of names or types.

- [ ] V5.1 Generate a **form** from the declared variables (V1): one field per variable, editor by
      `PropertyType`, pre-filled with `InitialValue`, description as helper text.
- [ ] V5.2 Keep a **raw JSON toggle** *(Q5)* for undeclared inputs.
- [ ] V5.3 Surface `VariableWriteMode` (`execution`/`workflow`/`dual`) as an explicit choice — the
      API already accepts it (`ExecutionContracts.cs:15-18`) and the UI never sends it. The natural
      place to teach "do this run's writes persist for later runs?".
- [ ] V5.4 Tests: form generated from declared variables; values serialise into `Inputs`; raw JSON
      still works; write-mode round-trips; invalid input surfaces the existing toast.

## V6 — Show globals in the designer 🌐

- [ ] V6.1 `VariablesClient` in `Workflow.UI.Client/Api` wrapping `/api/v1/variables` — shared with V9.
- [ ] V6.2 Fetch **global** variables once per designer session; add a third picker group
      **"Globals"**, badged as shared across workflows, carrying the V3.5 interim credential
      warning. Degrade silently (group hidden) on failure — the picker must not become a hard
      dependency on the API.
- [ ] V6.3 **Read-only in the designer** *(Q7)*.
- [ ] V6.4 Tests: globals group rendered; fetch failure hides the group without breaking the picker.

## V7 — Lint unknown variable references 🧭

**Finding.** `GraphValidator` has no notion of variables or tokens. With V4 failing hard, catching
this at design time becomes materially more valuable.

- [ ] V7.1 Scan template-enabled property values for `{{Variable.X}}`; report when `X` is neither
      declared (V1) nor a known global (V6). Also warn on **declared-but-never-assigned** *(Q3)*.
- [ ] V7.2 Suppress the unknown-name report when an upstream node is a `builtin.setvariable` writing
      that name.
- [ ] V7.3 Severity: **error** when nothing declares or writes the name (V4 will fail the node);
      **warning** for the ambiguous cases above.
- [ ] V7.4 **Migration lint** — flag existing property values containing a literal `{{` that no
      longer parses as a valid reference and offer to escape them as `\{\{` *(Q12)*, so V4's
      fail-hard switch doesn't break saved work.
- [ ] V7.5 Differentiate the 🔗 bound badge: known = neutral, unknown = ⚠️.
- [ ] V7.6 Tests: unknown name errors; declared name doesn't; upstream `SetVariable` suppresses;
      declared-but-unassigned warns; literal `{{` offered an escape.

## V8 — Document the variable lifecycle 📚

- [ ] V8.1 New `docs/variables.md`: the three scopes and when to use each; the **precedence chain**
      and `VariableSeedMode`; declaring variables (V1); supplying values at run time (V5);
      referencing them; **exactly which fields expand templates** and which deliberately don't, and
      why; the **`\{\{` escape**; **declaring is a contract** *(Q15)* — declared-but-unset warns,
      undeclared fails — called out explicitly for workflow validation; the **interim "not for
      credentials yet" warning** (V3.5); and a pointer to the secrets plan.
- [ ] V8.2 Cross-link from `docs/designer.md`, `docs/rest-api.md` (§Variables), `docs/scripting.md`
      and `docs/module-author-guide.md` (the new `SupportsTemplates` flag is module-author-facing).
- [ ] V8.3 A worked example: a global `apiBaseUrl`, a workflow-scoped `lastRunAt` persisted with
      `VariableWriteMode.Workflow`, and a per-run `orderId` — one page answering all three feedback
      bullets.

## V9 — Global variables admin screen 🔐

**Finding.** F7.

- [ ] V9.1 Tighten the API: `PUT`/`DELETE /api/v1/variables/{name}` require **`AdminPolicy`** when
      `scope=global`; workflow/execution scope stays on `WorkflowWritePolicy`. **Breaking change
      accepted** *(Q14)* — no deprecation window.
- [ ] V9.2 A **Global Variables** section in `Pages/Settings.razor`: list, add, edit, delete, and
      version history (the store already versions every write). Carries the V3.5 interim credential
      warning — this is the *write* surface, so it is where the notice matters most. *(Q16 named
      the docs and the designer's Globals group; I've extended it here because warning on the
      read-only picker but not on the screen that creates globals would be the obvious miss.)*
- [ ] V9.3 Drive visibility from the 403 response rather than guessing client-side — `AuthState` has
      no role model today (the UI only knows whether a credential exists, `TopBar.razor:14`).
- [ ] V9.4 Tests: endpoint policy per scope; UI renders the list; 403 degrades to a clear
      "admin only" message rather than an error toast.
- [ ] V9.5 Update `docs/rest-api.md:54-55` (the policy table) for the new per-scope rule.

## V10 — Secret variables 🔒 → separate plan

Per **Q13**, the redaction engineering is tracked in
[`Designer-Workflow-Variables-Secrets-Plan.md`](Designer-Workflow-Variables-Secrets-Plan.md).
Only the *declaration* lands here (V1.2: `IsSecret` on `VariableDefinition`), so that the field
exists and the panel can render it before the behaviour is complete.

---

## Questions — Q16 RESOLVED ✅ (round 3)

- **Q16 Sequencing between V3 and the secrets plan** → **Ship V3 first** (option 2), with a
  prominent "not for credentials yet" warning in the docs and the designer's Globals group.
  The Q16 option-3 mechanical name check (`password|secret|token|key|credential`) was **declined**.
  Folded into V3.5, V6.2, V9.2 and V8.1.

**No open questions remain. All of V1–V9 are ready to start.**

### Accepted risk from Q16 📌

Recording this plainly so it's a decision on the record rather than an oversight — it does not need
further action unless you want it to:

Between V3 shipping and the secrets plan landing, a global variable is stored **in plaintext** and
its value is **returned by `GET /api/v1/variables`** to anyone holding `WorkflowRead` (Admin,
Developer *and* Viewer — `docs/rest-api.md:54`). The warning is advisory, so if a tester puts an API
key in a global during that window it will be readable by a broader audience than the admin-gated
write path (V9.1) implies.

Two consequences worth carrying forward:

1. **The window should be kept short** — this argues for starting the secrets plan's cheap items
   (S1, S2, S5) alongside V3 rather than strictly after it.
2. **Values written during the window won't be encrypted at rest.** When S2 lands it needs to
   migrate or re-write any pre-existing plaintext global values, not just protect new ones — now
   tracked as **S2.4** in the secrets plan.

---

*Revision 4, 2026-08-01. Findings F1–F9 verified against the code at that date. No implementation
has started. **All questions (Q1–Q16) are answered and V1–V9 are unblocked.** V3 ships ahead of the
secrets plan under the Q16 decision, carrying the V3.5 interim warning.*
