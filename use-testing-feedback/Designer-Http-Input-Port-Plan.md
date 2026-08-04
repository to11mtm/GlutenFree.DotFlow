# Designer HTTP Input Port Mismatch â€” Plan

> ðŸ“‹ Response to user feedback (2026-08-04): *"when trying to pass an input into an Http Request
> module, the users get the error: Connection to 'request-1' uses input port 'input' which is not
> declared in module 'builtin.http.request' schema inputs."*
>
> **Revision 1 â€” proposal.** This is a **bug**, not a feature request: the designer offers a port
> the server then rejects. Q1 picks the fix shape; the recommended answer is baked in. Sibling
> plans from this feedback round: [Designer-Input-Shape-Hinting-Plan.md](Designer-Input-Shape-Hinting-Plan.md),
> [Designer-Split-Preview-Plan.md](Designer-Split-Preview-Plan.md).

## The ask

| # | Feedback | Reading |
| --- | --- | --- |
| 1 | Connecting into HTTP Request errors with "input port 'input' â€¦ not declared in schema inputs" | The designer lets users draw a connection the platform then refuses â€” a trap, not a validation |

## Findings ðŸ”¬

### F1 â€” The designer invents the port; the server rejects it. Both are "right." ðŸ›

The full trap, step by step:

1. `builtin.http.request` declares **zero input ports** (`HttpRequestModule.cs` â€”
   `Inputs: Arr<PortDefinition>.Empty`). It is purely property-driven: URL, method, body etc. all
   come from configuration. `builtin.log` is identical.
2. The designer's `NodePorts.Inputs()` **falls back to `["input"]`** when a schema declares no
   inputs (`DefaultInputs`) â€” so the canvas renders an `input` port that does not exist.
3. The user wires to that rendered port â†’ `AddConnectionCommand` with `TargetPortName = "input"`.
4. Server validation (`ModuleAwareWorkflowValidator`, **MA004**) checks target ports against
   declared schema inputs â†’ rejects. The engine's `ValidateConnectionPorts` does the same at run.
5. The client `GraphValidator` has **no port-name rule at all**, so nothing warns while editing.

### F2 â€” The connection the user drew is *necessary*, not wrong âš ï¸

This is the important nuance: in DotFlow, connections are also **sequencing**. "Fetch the user,
*then* call this webhook" requires an edge into the HTTP node â€” there is no other way to order it.
A module with zero declared input ports is therefore **impossible to place anywhere except the
start of a workflow** (any incoming edge is invalid). That can't be intended: an HTTP call
mid-workflow is the single most common integration shape. The user wasn't misusing the tool; the
module schema is missing the port the workflow model requires.

Note also: the engine's `GatherNodeInputs` happily delivers the value (`inputs["input"] = â€¦` plus
prefixed `sourceId.port` keys) â€” execution semantics already support the edge. Only validation
refuses it.

### F3 â€” Which modules are affected

Modules declaring zero inputs (audit during implementation; known so far):

| Module | Inputs | Property-driven? |
| --- | --- | --- |
| `builtin.http.request` | none | yes â€” URL/method/body via properties (templates can reference upstream) |
| `builtin.log` | none | yes â€” message via property templates |
| *(audit `Arr<PortDefinition>.Empty` across Workflow.Modules)* | | |

Contrast: `builtin.passthrough` and `builtin.script` declare `input`; transforms declare
`data`/`other`. The convention exists â€” these modules just predate it or opted out.

### F4 â€” The trycatch precedent points the wrong way here

`NodePorts.DynamicExtraInputs` adds a designer-only `input` to trycatch/transaction, and the
server *skips* validation for those modules. Extending that skip-list would fix the symptom but
grow an invisible special-case registry. The schema is supposed to be the truth; better to make it
true (D1).

### F5 â€” `{{input.â€¦}}` needs one new binder concept, and the hooks already exist

The Q2 requirement (use the incoming value in URL/body via `{{input.Thing.Id}}`) maps onto the
existing template pipeline cleanly:

- `PropertyBinder.ResolveSingleReference` currently routes a dotted reference to **two** roots:
  `Variable.` â†’ `PropertyBindingContext.Variables`, anything else â†’ `NodeOutputs[nodeId][port]`
  with dot-path traversal (`TraverseProperty` already walks dictionaries/JsonElements/POCOs).
- `PropertyBindingContext` does **not** carry the node's own inputs â€” but `NodeExecutor` has
  `_inputs` (the gathered inputs) in hand at the moment it builds the context
  (`NodeExecutor.cs:207-208`, `BuildBindingContext`). Adding a `SelfInputs` dictionary to the
  context and an `input` root to the resolver is a natural third branch, not a redesign.
- Traversal below the port value (`.Thing.Id`) is `TraverseProperty`, unchanged.

One collision to legislate: `{{input.x}}` could today mean *"output port `x` of a node whose id is
`input`"*. Node ids are generated (`http-1` style) so this is import-only exotica â€” see D5.

## Decisions âœ…

| # | Decision |
|---|----------|
| **D1 Declare a real, optional `input` port on property-driven modules** *(Q1 âœ… confirmed)* | An activation + data input. Schema becomes honest, MA004 stays strict, port tooltips explain it for free. |
| **D2 Client validator learns port names** *(Q3 âœ… error severity confirmed)* | `GraphValidator` gains a rule: connection target port not in the node's known input ports (schema + dynamic extras) â†’ **error**, matching MA004, caught while editing instead of at save. Same for source/output ports (schema-declared modules only; property-derived and empty-schema dynamic modules are exempt exactly as the server exempts them). |
| **D3 The designer never renders a port the server would reject** | With D1 in place, `DefaultInputs` fallback remains only for *unknown* modules (schema unavailable â€” designer can't know better). For known modules the rendered ports come from the schema, which now includes `input`. |
| **D4 `{{input}}` / `{{input.path}}` template root** *(from Q2 âœ…)* | Resolves to the value on the node's **`input` port**, dot-path traversal below it. Works in any templated property (URL, body, headersâ€¦), so "use the incoming ID in the URI" is just `{{input.Thing.Id}}` wherever it's needed â€” no per-module `useInputAs` switches. Implemented in `PropertyBinder` via a `SelfInputs` context addition (F5). |
| **D5 `input` is a reserved template root** | Self-input wins over a hypothetical node id `input`. Generated node ids can never collide (`module-N` style); an imported workflow with a literal `input` node id gets a client-validator warning telling the author to rename. |
| **D6 Port-name grammar stays singular** | `{{input}}` addresses the port literally named `input` â€” the port D1 adds everywhere. Modules with *other* declared input ports (e.g. transform's `data`) are not covered by this root in MVP; their values are already addressable as `{{sourceId.port}}`. Extending to `{{inputs.<port>}}` is a compatible future step if ever needed. |
| **D7 Expressions included if free, pure references guaranteed** | `{{input.id}}` (pure reference) is the MVP bar. The expression rewriter tokenizes dotted roots generically, so `{{input.count > 5}}` likely works with the same change â€” verify, but don't gold-plate. |

## Open questions â“ â€” all answered 2026-08-04 âœ…

- [x] **Q1 â€” Fix at the module or relax the validator?** âœ… Fix at the module (D1).
- [x] **Q2 â€” Does `builtin.http.request` *use* the input value?** âœ… **Yes â€” MVP must-have.**
      Users need the incoming value in the request body or URI, addressed via templating like
      `{{input.Thing.Id}}`. Absorbed into **D4â€“D7** above (new `input` template root, reserved
      word, singular grammar, expressions-if-free).
- [x] **Q3 â€” Severity of the new client rule?** âœ… Error (D2).

## Items

| # | Item | Kind | Size | Status |
| --- | --- | --- | --- | --- |
| H1 | Audit all builtin modules for empty `Inputs`; add optional `input` activation port (D1) with beginner description | Core | S | â¬œ |
| H2 | Client `GraphValidator` port-name rule (D2) + reserved-id warning (D5) | UX | S | â¬œ |
| H3 | Verify server: MA004 + engine `ValidateConnectionPorts` pass for the new port; no behaviour change in `ExecuteAsync` | Core | S | â¬œ |
| H4 | Tests: module schema tests, binder `{{input.â€¦}}` tests, validator rule tests (client), MA004 regression (server), roster/schema-assertion updates | Tests | M | â¬œ |
| H5 | Docs: activation-input + `{{input.â€¦}}` templating in the docs; module author guide note | Docs | S | â¬œ |
| H6 | Binder: `SelfInputs` on `PropertyBindingContext`; `input` root in `ResolveSingleReference`; `NodeExecutor` passes gathered inputs (D4/F5); expression rewrite coverage (D7) | Core | M | â¬œ |

### H1 â€” Module schemas ðŸ”Œ

- [ ] H1.1 Grep `Arr<PortDefinition>.Empty` inputs across `Workflow.Modules`; classify each:
      property-driven data module (gets the port) vs. genuinely input-less by design
      (`builtin.start` â€” must NOT get one; its whole identity is having no inputs).
- [ ] H1.2 Port definition: `input`, optional, `object`, description per D1.
- [ ] H1.3 Confirm designer picks it up with zero UI changes (schema-driven rendering).

### H2 â€” Client validation ðŸ§­

- [ ] H2.1 Rule: for nodes with a loaded schema, a connection targeting a port not in
      `NodePorts.Inputs(node)` â†’ error naming the port and the valid ones.
- [ ] H2.2 Mirror for source ports against `NodePorts.Outputs(node)`, exempting dynamic-output
      modules exactly like the server does (empty declared outputs â†’ skip).
- [ ] H2.3 Unknown-module nodes (no schema): skip silently â€” already flagged by the
      unknown-module error.

---

*Created 2026-08-04. Findings verified against `HttpRequestModule.cs`,
`ModuleAwareWorkflowValidator.cs` (MA004), `NodePorts.cs` (`DefaultInputs` fallback),
`CanvasView.razor` (connection creation), `WorkflowExecutor.GatherNodeInputs` /
`ValidateConnectionPorts`, and `GraphValidator.cs` (no port rule today).*


---

## What shipped 📦

| File | Change |
| --- | --- |
| `Workflow.Modules/Binding/IPropertyBinder.cs` | `PropertyBindingContext.SelfInputs` (optional, non-breaking). |
| `Workflow.Modules/Binding/PropertyBinder.cs` | `input` reserved template root: bare `{{input}}` (special-cased ahead of the pure-reference gate) and `{{input.path}}` (ahead of the NodeId branch, D5 precedence); `ResolveSelfInputReference` with dot-path traversal; expression tokens like `{{input.count > 5}}` resolve via the same path (D7). |
| `Workflow.Engine/Actors/NodeExecutor.cs` | `BuildBindingContext` passes the node''s gathered `_inputs` as `SelfInputs`. |
| 13 modules (`HttpRequest`, `Log`, `Delay`, `GetVariable`, `Break`, `Continue`, `Parallel`, `Compress`, `Decompress`, `CsvRead`, `FileRead`, `JsonRead`, `XmlRead`) | Optional `input` activation port declared (D1). Excluded by design: `builtin.start`, `builtin.webhook.trigger` (entry points). |
| `TryCatchModule` / `DatabaseTransactionModule` | Also declare `input` — their designer-only extra port was the same MA004 trap via import (F4 resolved by making the schema true). |
| `Workflow.Modules/Validation/ModuleAwareWorkflowValidator.cs` | MA003 now skips dynamic-output modules (empty declared outputs), matching the engine''s `ValidateConnectionPorts` — switch/partition/parallel workflows no longer fail save. |
| `Workflow.UI.Client/Designer/State/GraphValidator.cs` | `ValidatePorts` (D2): undeclared target/source ports → **error** listing valid ports; merged-mode + dynamic-output exemptions mirror the server; reserved node-id `input` → warning (D5). |
| `Workflow.Tests/Modules/Binding/PropertyBinderSelfInputTests.cs` | New — 9 tests (bare/dotted/JsonElement/case-insensitive/missing/inert/reserved-precedence/coexistence/null-traversal). |
| `Workflow.Tests/Modules/Http/HttpRequestModuleTests.cs` | Schema assertion updated (0 → 1 input). |
| `Workflow.Tests.UI/State/PortValidationTests.cs` | New — 9 tests for the client rule. |
| `docs/variables.md`, `docs/http-and-network.md`, `docs/module-author-guide.md` | `{{input}}` reference docs; HTTP module note; activation-input convention for module authors. |

**Verification.** `Workflow.Tests` 1573/1578 — the 5 failures are the documented parallel-contention
flakes (each verified green in isolation). `Workflow.Tests.UI` 624/624. Solution build: 0 errors.

*Implemented and verified 2026-08-04.*
