# Plan 03 — Data binding & variables remediation

> Fix plan for the confirmed binding/variable findings from
> `Workflow.Tests/Findings/Binding/*` (2026-09-29). Source:
> `Workflow.Modules/Binding/PropertyBinder.cs`,
> `Workflow.Engine/Actors/{NodeExecutor,WorkflowExecutor,SubGraphExecutor}.cs`,
> `Workflow.Engine/Models/InitialVariables.cs`.
>
> Up: [architecture findings](../README.md#findings-at-a-glance) ·
> [02 — Data binding & variables §10](../02-data-binding-and-variables.md#10-findings-to-verify)

**Status:** proposed. Nothing built yet.

| # | Finding | Sev | Guard test |
| --- | --- | :-: | --- |
| A | `{{nodeId.port}}` only resolves direct predecessors (docs/code mismatch) (§10.1) | Med | `BindingFindingTests.NonAdjacentNodeOutputReference_ShouldResolve` |
| B | Plain input keys shadow declared variables (§10.2) | Low | `BindingFindingTests.InputKey_ShouldNotShadow_DeclaredVariable` |
| C | Loop-body variable writes not visible after the loop (§10.3) | Med | `BindingFindingTests.VariableWriteInsideLoopBody_…` (blocked by 01 §12.9) |
| D | Secret values persisted verbatim in node history (§10.6) | Med (security) | `SecretPersistenceFindingTests.SecretValue_ShouldNotBePersisted_…` |

---

## Finding A — `{{nodeId.port}}` only reaches direct predecessors (§10.1)

### Evidence
- `NodeExecutor.BuildBindingContext` builds `PropertyBindingContext.NodeOutputs` only from the
  `"srcId.key"` entries in the node's own gathered inputs — and only **direct predecessors** put
  those keys there (`WorkflowExecutor.GatherNodeInputs`).
- `NonAdjacentNodeOutputReference_ShouldResolve` → `{{src.value}}` two hops up is unresolvable, so the
  node fails and the run fails.
- `docs/variables.md` claims node-output refs are "the only way to reach nodes **further upstream**."

### Root cause
Docs and code disagree. The engine deliberately passes each node only its direct inputs; the docs (and
possibly the designer's `{{…}}` picker) promise transitive reach.

### Options
- **A1 (recommended): make the behaviour the spec.** Keep direct-predecessor-only resolution — it is
  what [the architecture's "explicit over implicit" principle](../00-overview.md#6-observations)
  already states ("every data hop is a declared, typed, port-to-port edge"). Fix `docs/variables.md`
  and the designer's variable picker so they only offer refs to **direct** predecessors, and give a
  clear binding error when a non-adjacent ref is written by hand.
- **A2: make the code match the docs.** Give the binder a read-only view of **all** node outputs
  (thread `WorkflowExecutor._nodeOutputs` into the `Execute` message or the binding context). More
  powerful, but it quietly re-introduces implicit data flow the design avoided, and it has ordering
  pitfalls (a "further upstream" node on an unrelated branch may not have run).

### Recommendation
**A1.** It aligns with the stated design and is the smaller, safer change. Downgrade the finding to a
docs/UX fix. Flip `NonAdjacentNodeOutputReference_ShouldResolve` into a **negative** test (a clear
error, not silent failure) rather than asserting resolution.

### Acceptance
`docs/variables.md` corrected; designer picker offers only direct predecessors; a hand-written
non-adjacent ref yields a clear "not a direct input" binding error (not a bare "unresolved").

**Effort:** ~0.5–1 day (docs + picker + error message).

### Decided 2026-11-08 (asked, after an options walkthrough) — A2 across bodies — fixed
The plan recommended A1; the user chose **A2, including across construct bodies**:
- `{{nodeId.port}}` resolves for **any ancestor**, meaning any node with a connection path into this
  node in the definition, once it has run. This holds inside loop / branch / try / transaction bodies
  too, so a body node can read nodes that ran before its construct (e.g. `{{auth.token}}` inside a
  For Each). Direct-predecessor values are unchanged.
- **Still unresolvable:** a non-ancestor (a sibling branch, a downstream node), and an ancestor that
  didn't run (skipped branch). Both fail the node with a message that says which case it is.
- **Designer:** the picker already offers ancestors, including across bodies, so it stays. The lint
  adds an **error** for a reference to an existing node that isn't an ancestor. It keeps the error
  as the default and offers two optional fixes:
  - **wire it in:** connect the referenced port into a free input port of this node, so it becomes
    an ancestor.
  - **via a variable:** insert a SetVariable after the source that stores the value, and rewrite the
    token to `{{Variable.x}}`.
- **Save time:** the validator warns on the same case.
- **Engine:** `Execute` carries the ancestors' outputs for binding only (not merged into node
  inputs, so history doesn't grow). Bodies get a snapshot of the outer outputs when the construct
  starts, threaded the same way as the §10.3 variables.

Implementation:
- **Engine:** `NodeAncestry` (cached ancestor sets, `ViewFor`, `Snapshot`) and `Execute.Upstream`
  (`UpstreamView`). Construct actors and `SubGraphExecutor` take an `outerOutputs` parameter.
  `PropertyBindingContext.ExplainMissingNode` produces the "not upstream" vs "didn't run" messages.
- **Designer:** `UpstreamReferences` (`Find` / `WireItIn` / `ViaVariable`), a lint error in
  `VariableLint`, buttons in `PropertyEditor`, and `PropertiesPanel.SchemaFor` (from the designer's
  `EnsureSchema`).
- **Server:** `NodeReferenceChecker` (VB002).

Tests:
- `Engine/UpstreamReferenceTests` (5)
- `Modules/NodeReferenceValidationTests` (3)
- UI `State/UpstreamReferencesTests` (6) and `Components/NonUpstreamReferencePanelTests` (4)

`VariableLintTests.KnownNodeReference_IsClean` now wires its node in: before, it endorsed a
reference that fails at run time. The guard is retired, which removes `BindingFindingTests`.

---

## Finding B — input keys shadow declared variables (§10.2)

### Evidence
- `NodeExecutor.BuildBindingContext` overlays **every plain input key** (run inputs *and* mapped
  input-port values) onto the variable map: `variables[key] = inputs[key]`.
- `InputKey_ShouldNotShadow_DeclaredVariable` → `{{Variable.count}}` returned the input port value
  `7`, not the declared variable `99`.
- Run inputs are *already* in `_context.Variables` via `InitialVariables.Resolve`, so the overlay is
  redundant for them and harmful for port names that collide with variables.

### Root cause
A pre-Phase-3.5 compatibility shim (plain keys became variables back when run inputs were the only
variables) now shadows real variables whenever an input **port** shares a name.

### Options
- **B1 (recommended): drop the port-value overlay; keep variables from the resolved context.** Build
  the binder's `Variables` from `_context.Variables` (which already layers global/workflow/declared/
  run-input per `InitialVariables`). Do **not** overlay mapped port values as variables. `{{input}}` /
  `{{input.path}}` already cover "this node's incoming value" without polluting the variable
  namespace.
- **B2: conditional overlay.** Overlay a plain key only when no variable of that name exists. Smaller
  behavioural change, but keeps a surprising rule on the books.

### Recommendation
**B1.** Cleaner model: variables come from the variable layers; a node's own inputs are reached via
`{{input…}}` and `{{nodeId.port}}`. Watch for workflows that leaned on the shim (a run input read as
`{{Variable.x}}` still works because run inputs are in the variable layers; a *port value* read as
`{{Variable.portName}}` stops working — which is the bug).

### Acceptance
`InputKey_ShouldNotShadow_DeclaredVariable` green; existing `PropertyBinderTests`,
`PropertyTemplateIntegrationTests`, `VariableSeedingIntegrationTests` green.

### Decided 2026-11-08 (asked) — fixed
- **Runtime: B1.** Port values are no longer copied into the variable map; `{{Variable.x}}` only
  ever means a variable (global / workflow / declared / run input / SetVariable).
- **Construct-body values stay reachable on purpose:** ForEach/While and Parallel bodies get
  `item` / `index`, and catch bodies get `error`, **seeded as body-scoped variables** (that
  iteration / branch only, never persisted). Inside the body they shadow a declared variable of the
  same name. Designer picker and lint learn to offer and accept them inside bodies.
- **Designer clash help.** When a property uses `{{Variable.x}}` and `x` is also one of the node's
  wired input ports:
  - **error** if there's no variable `x` (the run would fail);
  - **warning** if there is one.
  Quick-fixes:
  - *Use the incoming value*: rewrite to `{{sourceNode.sourcePort}}`.
  - *Keep the variable*: acknowledge it in node `Metadata["variableClashAck"]` (comma-separated
    names). The warning comes back if the wiring or names change.
  - *Rename the variable*: rename it and update every `{{Variable.x}}` reference in the workflow.
- **Save time:** the server validator gives the same warning, and honours the acknowledgement.
- **Run time:** an undeclared `{{Variable.x}}` where `x` is one of the node's input ports fails
  with a hint to use `{{input…}}` / `{{nodeId.port}}`.

Tests: `Engine/VariableShadowingTests` (6), `Modules/VariableClashValidationTests` (4), UI
`State/VariableClashTests` (15) + `Components/VariableClashPanelTests` (6). Guard retired.
Found along the way: the docs' catch example `{{error.message}}` names a node called `error`, so it
never resolved — it's `{{Variable.error.message}}`.

**Effort:** ~1 day including a sweep for shim-dependent tests.

---

## Finding C — loop-body variable writes aren't visible after the loop (§10.3)

> **Blocked by [01 §12.9](02-engine-lifecycle-remediation.md) (loop done-port node skipped).** The
> post-loop read can't run until that lands. Fix §12.9 first, then this.

### Evidence
- `SubGraphExecutor` handles `NodeExecutionCompleted` but never reads its `VariableUpdates`, and
  `SubGraphCompleted` carries no variable channel. So a `SetVariable` inside a loop/branch body never
  reaches the workflow context.

### Root cause
`SubGraphExecutor` is a deliberately "lean" executor (its own doc comment says so): it has routing
(via `DispatchCore`) but not variable propagation — and, per [02 §12.4](02-engine-lifecycle-remediation.md),
not per-node error handling either. These are the same theme: **sub-graph bodies lack feature parity
with the top-level executor.**

### Options
- **C1 (recommended): propagate variable updates up.** Have `SubGraphExecutor` forward each body
  node's `VariableUpdates` to its owner (via a new message, or on `SubGraphCompleted`), and have the
  owning construct actor / `WorkflowExecutor` apply and persist them exactly as a top-level node's.
- **C2: shared context.** Fold this into the same "sub-graph parity" work as §12.4 — extract the
  context-mutation + persistence path so both executors share it.

### Recommendation
**C2, done with §12.4.** Variable propagation and error handling are the two parity gaps; fixing them
together (one "sub-graph behaves like the executor" change) avoids two overlapping refactors. If
scheduled separately, C1 is the minimal standalone fix.

### Acceptance
After §12.9 and this land: `VariableWriteInsideLoopBody_…` green (reads `"c"`); `LoopScopeTests`,
`SubGraphExecutorTests` green; add a parallel-branch variant (a write in one branch visible after
fan-in, per the chosen scoping rule).

### Open question
Scoping: should a body write be visible to **later iterations** of the same loop, to **sibling
parallel branches**, or only **after** the construct? Decide this before implementing — it changes the
propagation timing. (Recommend: visible after the construct; iteration-local otherwise, to avoid
races in parallel branches.)

> **Decided 2026-10-10 (implemented with §12.4):** the recommendation above was revised —
> iteration-local writes would break `WhileModule`'s documented "variable-driven while" contract and
> loop accumulators.
> - **Sequential bodies** (loop iterations, try → catch → finally, transaction bodies): later steps
>   see earlier writes; the construct's writes apply to the workflow when it ends.
> - **Parallel branches:** isolated while running (each starts from the pre-parallel snapshot);
>   merged in **branch order** when the parallel ends — on a conflict the highest branch index
>   wins, regardless of timing.
> - **Failures:** writes made before a body failed are kept (try/catch, rolled-back transactions,
>   failed loops/parallels all report them), matching top-level behaviour.

**Effort:** ~2–3 days standalone; less if merged with the §12.4 extraction.

---

## Finding D — secret values persisted verbatim in node history (§10.6)

### Evidence
- Run inputs are copied into every node's input dictionary and persisted as
  `NodeExecutionRecord.Inputs` (`WorkflowExecutor.QueueRecordNodeExecution`).
- `QueuePersistVariableUpdates` / the completion handler log variable updates as `name = value` at
  debug level.
- `SecretValue_ShouldNotBePersisted_InNodeHistoryInputs` → the secret value is present in the
  persisted inputs.
- `VariableDefinition.IsSecret` is currently read only by the designer (02 §8).

### Root cause
`IsSecret` has no server-side effect. Nothing redacts secret-flagged values before they are
persisted, logged, or (once [01 §12.1](02-engine-lifecycle-remediation.md) is fixed) broadcast.

### Options
- **A (recommended): redact declared secrets on the way out.** Build the set of secret variable names
  from `WorkflowDefinition.Variables` where `IsSecret`, and redact matching keys/values at every sink:
  `NodeExecutionRecord.Inputs/Outputs`, variable-update persistence, the debug logs, and the SignalR
  broadcast. Store a placeholder (e.g. `"***"`) so history still shows the key was present.
- B: don't persist node inputs/outputs at all (loses debuggability — rejected).
- C: encrypt history at rest (orthogonal; doesn't stop logs/broadcast leakage).

### Recommendation
**A.** Thread the secret-name set through the persistence/broadcast path and redact. Coordinate with
§12.1: when terminal/node events start broadcasting outputs, they must redact too.

### Policy to settle first
What is "secret"? Recommend: **declared `IsSecret` variables** (by name), plus any run input whose key
matches a declared secret variable. Document it so authors know marking a variable secret actually
protects it.

### Acceptance
`SecretValue_…` green; a redaction golden test (secret present as `***`, non-secrets intact) across
history inputs, outputs, variable updates, and the broadcast payload.

### Risk / blast radius
Low-medium. Touches the persistence and (future) broadcast paths; mainly additive. Main risk is
missing a sink — enumerate them (history inputs, history outputs, variable store, logs, SignalR).

**Effort:** ~2 days.

---

## Cross-cutting theme

Findings **C (§10.3)** and **[§12.4](02-engine-lifecycle-remediation.md)** are both "`SubGraphExecutor`
lacks parity with `WorkflowExecutor`." Finding **D** interlocks with **[§12.1](02-engine-lifecycle-remediation.md)**
(don't broadcast secrets). Sequence accordingly: do the engine-plan items first (§12.9, §12.1, the
sub-graph parity extraction), then B and A here slot in cleanly.
