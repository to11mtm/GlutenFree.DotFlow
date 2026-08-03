# Designer Multi-Input Ports & a Join Module — Plan

> 📋 Response to user feedback (2026-08-02): *"how the fanin module works as far as multiple
> inputs"*, *"a 'join' module that can take multiple inputs in"*, and *"multiple inputs for these in
> the UI to make it clear to the users, potentially specifying how many inputs are expected"*.
>
> **Revision 1 — plan only, nothing implemented.** The findings below are verified against the code
> (file + line references throughout). Several are load-bearing enough that they change what I'd
> recommend building, so please read **Findings** before the items. Questions are at the bottom.

## The ask

| # | Feedback | Reading |
| --- | --- | --- |
| 1 | "How does the fanin module work as far as multiple inputs?" | A **documentation gap** first and foremost — the mechanism is real but invisible. |
| 2 | "A 'join' module that can take multiple inputs in" | A barrier whose inputs are **named and addressable**, not an anonymous ordered pile. |
| 3 | "Multiple inputs for these in the UI… specifying how many inputs are expected" | **Declared arity**: the node shows N sockets because the author said N, and the designer can then check wired-vs-declared. |

## Summary

| # | Item | Kind | Size | Status |
| --- | --- | --- | --- | --- |
| J1 | Document how FanIn actually receives branches | Docs | S | ⬜ |
| J2 | 🐞 Fix FanIn inside sub-graphs (currently receives **zero** branches) | Bug | S | ⬜ |
| J3 | Dynamic-input-port opt-in (schema flag + interface + server validation) | Core | M | ⬜ |
| J4 | `builtin.join` module with N named inputs | Feature | M | ⬜ |
| J5 | Designer renders per-node dynamic input ports | UX | M | ⬜ |
| J6 | Inspector affordance for arity + edge handling when it shrinks | UX | M | ⬜ |
| J7 | Designer warnings (multi-edge collision, wired-vs-declared arity) | UX | S | ⬜ |
| J8 | FanIn legibility (arity badge + ordered branch list) | UX | S | ⬜ |
| J9 | Tests | Tests | M | ⬜ |
| J10 | Docs | Docs | S | ⬜ |
| J11 | 🐞 *(adjacent, optional)* `builtin.parallel` branch ports are unwireable | Bug | S | ⬜ |
| J12 | 🐞 *(adjacent, optional)* TryCatch's designer-only `input` port likely fails save | Bug | S | ⬜ |

---

## Findings 🔬

### F1 — FanIn doesn't use input ports at all; it reads a hidden engine-supplied key

`FanInModule` declares exactly **one** input port (`FanInModule.cs:85-87`):

```csharp
Inputs: Arr.create(
    // Declarative — actual ordered payloads come from the engine via __incomingBranches__
    PortDefinition.Create<object>("branches", isRequired: false)),
```

The real mechanism is engine-side. `WorkflowExecutor.GatherNodeInputs` (`WorkflowExecutor.cs:804-869`)
builds an ordered list of **every** incoming connection's source-node output dictionary and injects
it under a reserved key:

```csharp
inputs["__incomingBranches__"] = incomingBranches;
inputs["__incomingBranchMeta__"] = incomingBranchMeta;
```

FanIn reads only that (`FanInModule.cs:127-136`) — its `branches` port is decorative.

**So the honest answer to the user's question is:** a FanIn node's arity is *however many edges you
happen to draw into it*. There is no place to say "I expect 3". Every edge into the node is a
branch, regardless of which port it lands on.

### F2 — Branch **order** is edge-creation order, which is invisible in the UI

`incomingBranches` is built by iterating `_definition.Connections.Where(c => c.TargetNodeId == nodeId)`
(`WorkflowExecutor.cs:794-796`) — i.e. the order edges appear in the saved array, which is the order
they were **drawn**. FanIn's own docs acknowledge this (`FanInModule.cs:194-195`):

```csharp
/// CopilotNote: precedence = connection-order (engine populates branches in
/// <c>WorkflowDefinition.Connections</c> declaration order), so this is deterministic~ 💖
```

Deterministic, yes — but three of FanIn's five modes depend on it (`First`, `Last`, and `Merge`'s
last-writer-wins), and nothing on the canvas shows it. Redrawing an edge silently changes the result.

### F3 — FanIn's `named` mode names branches by their **source port**, which usually collide

`Named` keys each branch by `sourcePortName` (`FanInModule.cs:155`, meta from `WorkflowExecutor.cs:844-848`).
Most modules' primary output port is called `result` or `output`, so joining three HTTP calls gives
three branches all named `result` → the documented collision fallback kicks in and you get
`nodeId.port` keys instead. The naming is therefore *accidental*, derived from whatever the upstream
modules happen to call their ports. **This is the strongest argument for a Join module**: the author
should name the inputs.

### F4 — 🐞 FanIn is broken inside every sub-graph

`SubGraphExecutor` has its own `GatherNodeInputs` (`SubGraphExecutor.cs:333-361`) and it **does not**
populate `__incomingBranches__` or `__incomingBranchMeta__` — compare it with the WorkflowExecutor
version. A FanIn node inside a loop body, `try`/`catch`/`finally`, a transaction body, or a parallel
branch therefore reads the fallback (`FanInModule.cs:130`):

```csharp
: new List<Dictionary<string, object?>>();
```

…and aggregates **zero** branches: `result` is an empty list, `count` is `0`, and the node reports
success. Silent wrong answer, no warning. This is squarely within "how does fan-in work with
multiple inputs" and should be fixed here.

### F5 — Every node is already a barrier; a "join" is about *naming*, not *waiting*

`DispatchCore.TryFireSuccessor` (`DispatchCore.cs:206-208`):

```csharp
var preds = _nodePredecessors.GetValueOrDefault(successorId, new List<string>());
if (preds.All(p => _isCompleted(p) || _isSkipped(p)))
```

**All** predecessors must be satisfied before any node fires. So join/barrier semantics are
universal — FanIn adds no waiting, only aggregation. A new Join module likewise doesn't need engine
work to *wait*; its value is entirely in named, declared, visible inputs.

### F6 — 🐞 Two edges into the same input port silently discard all but the last

`WorkflowExecutor.cs:818-820`:

```csharp
if (sourceOutputs.TryGetValue(conn.SourcePortName, out var outputValue))
{
    inputs[conn.TargetPortName] = outputValue;
```

Last writer wins, ordered by the connections array. The designer permits drawing this — `IsCompatibleInput`
(`CanvasView.razor:548-572`) blocks only self-connections, exact-duplicate edges and cycles.

This matters a lot here: a user who wants "join these two things" will very reasonably drag both
edges onto the one input socket of an ordinary node and get one value with **no** error, no warning,
and no log line beyond a debug trace. Whatever else we build, this deserves a designer warning.

### F7 — Dynamic **output** ports have a precedent; dynamic **inputs** have none

`ParallelModule` declares `Outputs: Arr<PortDefinition>.Empty` (`ParallelModule.cs:68`) and derives
its ports from properties (`ParallelModule.cs:256-274`):

```csharp
for (var i = 1; i <= count; i++)
{
    generated.Add($"branch{i}");
}
```

…driven by a `branches` (JSON array of names) property and a `branchCount` (int) property. Both
validators special-case it by **skipping** when the declared output list is empty
(`WorkflowExecutor.cs:515-521`).

There is **no equivalent for inputs.** `ModuleAwareWorkflowValidator.ValidateConnectionPorts`
(`ModuleAwareWorkflowValidator.cs:255-273`) checks target ports with no escape hatch at all:

```csharp
if (!inputPortNames.Contains(connection.TargetPortName))
{
    errors.Add(new ValidationError("MA004", …));
}
```

and this runs on every create/update (`WorkflowEndpoints.cs:124-128`, `Results 422`). **Any
dynamic-input design needs a deliberate server-side change; ad-hoc extra input ports are simply not
savable today.**

Note also that "empty `Inputs`" cannot be reused as the signal the way empty `Outputs` was: the new
`builtin.start` has empty inputs precisely because nothing may connect to it. Overloading emptiness
would make Start nodes wire-able. We need an explicit flag.

### F8 — The designer already has a per-node port-override choke point

`NodePorts` (`Designer/State/NodePorts.cs`) is the single place both `NodeView` (rendering) and
`EdgeLayer` (anchor math) resolve ports, and it *already* does module-specific work:

- `DynamicOutputs` / `DynamicExtraInputs` hardcoded tables for `builtin.trycatch` and
  `builtin.database.transaction` (`NodePorts.cs:24-41`);
- FanIn's `meta` property hiding `count`/`done` (`NodePorts.cs:82-86`);
- `OutputShapingUx` collapsing outputs to a single `output` port (`NodePorts.cs:88-92`).

Node geometry already scales with port count (`CanvasGeometry.NodeBounds`):

```csharp
var rows = Math.Max(1, Math.Max(inputCount, outputCount));
var height = HeaderHeight + (rows * PortRowHeight) + NodePadding;
```

So **N input sockets will lay out and connect correctly for free** — the only new work is telling
`NodePorts` what N is. `DesignerNode.Properties` is a free-form `Dictionary<string, JsonElement>`
persisted verbatim (`DesignerNode.cs:288`, `ToDto` at `:333`), so it can carry the arity with no
model change. The properties panel already has a `Number` editor and a `Json` editor
(`PropertyEditor.razor:440-441`, `:465`).

### F9 — `builtin.transform.join` already exists and is something else entirely

`DataJoinModule` (`Workflow.Modules/Builtin/Transform/DataJoinModule.cs:43-60`) is a **relational**
join: `left`/`right` collection inputs, `leftKey`/`rightKey` expressions, `inner`/`left`/`full`
semantics, projection. It is a SQL-style data transform, not a control-flow join. Naming the new
module "Join" is therefore a collision risk in the palette and the docs (see **Q1**).

### F10 — 🐞 *(adjacent)* `builtin.parallel`'s branch ports can't be wired in the designer

Following F7 + F8: Parallel's schema outputs are empty, and it is **not** in `NodePorts.DynamicOutputs`,
so `Outputs()` falls through to `DefaultOutputs` = `["output"]` (`NodePorts.cs:78-80`). A Parallel
node with `branchCount: 3` renders one generic socket, and `branch1`/`branch2`/`branch3` cannot be
connected visually at all. This is the same "let the user say how many, and show that many" gap the
user is asking about, on the fan-**out** side.

### F11 — 🐞 *(adjacent, verify)* TryCatch's designer-only `input` port likely 422s on save

`NodePorts.DynamicExtraInputs` adds an `input` port for `builtin.trycatch` with the comment *"the
server skips port-name validation for this module"* (`NodePorts.cs:32-41`). I could find no such
skip — MA004 has no module allowlist, and `TryCatchModule`'s schema declares only `rethrow` and
`catchTypes` (`TryCatchModule.cs:65-77`). Wiring a predecessor into a TryCatch node should therefore
fail the save gate. **Worth a 10-minute confirmation before acting on it**, but if true, the escape
hatch built for J3 also fixes it.

---

## Design discussion 🧭

### How should dynamic input ports be permitted server-side?

| Option | Idea | Verdict |
|---|---|---|
| **A** | Mirror the output rule: empty `Inputs` ⇒ skip MA004 | ❌ Rejected. Empty inputs already means "takes no inputs" (`builtin.start`); this would make Start nodes wire-able. |
| **B** | A `SupportsDynamicInputs` flag on `ModuleSchema`; MA004 skips when set | ⚠️ Safe and explicit, but disables input validation wholesale — typos and stale edges become silent dead wires. |
| **C** | A small interface the module implements to *derive* its ports from node configuration; the validator calls it and validates against the derived list | ✅ **Recommended.** Keeps the derivation rule in the module (one definition), and keeps real validation: `in4` on a 3-input node is still an error. |

**Recommended shape for C** (names illustrative, to be settled in implementation):

```csharp
public interface IDynamicInputPorts
{
    IReadOnlyList<string> GetInputPortNames(IReadOnlyDictionary<string, JsonElement> nodeProperties);
}
```

`ModuleAwareWorkflowValidator.ValidateConnectionPorts` already has the `IWorkflowModule` instance
for the target node (`:256`) and the `WorkflowDefinition` (`:225`), so it can look up the node's
properties and substitute the derived list for `Schema.Inputs`. No other call site changes.

**The designer still needs its own copy of the rule.** Schema is cached per *module id*
(`Designer.razor` `schemaCache`), not per node, so asking the server per node would mean a round trip
on every keystroke in the arity field. The pragmatic answer — consistent with how the designer
already mirrors `VariableValueType`/`VariableSeed` and is kept honest by `VariableEnumDriftTests` —
is a mirrored derivation helper in `Designer/State/` plus a drift test asserting the two agree for a
table of configurations. **Q9** asks whether you'd rather pay the round trip instead.

### What makes Join worth having, given FanIn exists?

| | `builtin.fanin` | proposed Join |
|---|---|---|
| Arity | Implicit — however many edges you drew | **Declared** by the author, visible as N sockets |
| Ordering | Edge-creation order, invisible (F2) | Irrelevant — each value has a name |
| Naming | Upstream's port names, usually colliding (F3) | **Author's** names |
| Payload per branch | The whole source node's output dictionary | Exactly the one output port you wired |
| Checkable? | No — "did I forget an edge?" is unanswerable | Yes — declared arity vs. wired arity (J7) |

That last row is the real prize and it is exactly what the user asked for.

### Should FanIn also get dynamic named inputs?

I lean **no** (see **Q2**). FanIn's contract is positional and engine-supplied; bolting named ports
onto it creates two competing mental models in one module and changes how existing FanIn nodes
render. Better to keep FanIn as the *unnamed barrier*, make Join the *named* one, fix F4, and spend
a little effort on FanIn legibility (J8).

---

## Proposed decisions ✅

These are **proposals**, not settled — the questions below cover the contested ones.

| # | Decision |
|---|---|
| **D1** Ports are derived, not merely trusted | Option **C**. The module owns the derivation; the server validates against the derived list; the designer mirrors it with a drift test. |
| **D2** Arity is configured the way Parallel already does it | An `inputs` property (JSON array of names, wins when present) plus an `inputCount` property (int, generates default names). Same shape as `ParallelModule`'s `branches`/`branchCount`, so the two feel like siblings. |
| **D3** Join outputs one named object | `result` = `{ portName: value, … }`, plus `count`. Not one output port per input. |
| **D4** FanIn keeps its positional contract | No new input ports on FanIn; fix F4, document F1–F3, add the legibility affordances in J8. |
| **D5** Arity mismatches warn, never block | Consistent with the Start/End rules: a partly-wired Join is legal and runnable. Warn in the designer while editing. |
| **D6** Shrinking arity never silently orphans an edge | If reducing the count would strand connections, the designer must say so and offer a single undoable action rather than leaving dead wires (see **Q6**). |

---

## Items

### J1 — Document how FanIn actually works 📚

- [ ] J1.1 In `docs/advanced-flow-control.md`, state plainly that FanIn's arity is the number of
      incoming edges, that the `branches` port is decorative, and that **order is edge-creation
      order** (F1, F2).
- [ ] J1.2 Document the `named` collision behaviour honestly (F3) and point at Join for the
      author-named case.
- [ ] J1.3 Note that `done` carries no value — it is an ordering signal only (it is never present in
      FanIn's outputs dictionary; routing still fires because FanIn returns no ActivePorts, so
      `DispatchCore` takes the fire-all path).

### J2 — 🐞 Fix FanIn inside sub-graphs

- [ ] J2.1 Populate `__incomingBranches__` / `__incomingBranchMeta__` in
      `SubGraphExecutor.GatherNodeInputs`, matching `WorkflowExecutor`'s behaviour including the
      skipped-predecessor placeholder.
- [ ] J2.2 Decide whether the two implementations should be extracted to one shared helper rather
      than fixed twice — they have already drifted once. *(Note the subtlety: SubGraph filters
      connections by `_nodeSuccessors.ContainsKey(c.SourceNodeId)` (`:338`), so a shared helper
      needs that predicate injected.)*
- [ ] J2.3 Regression test: a FanIn inside a loop body and inside a `try` branch both aggregate the
      expected number of branches.

### J3 — Dynamic input ports, server-side 🔌

- [ ] J3.1 Add the derivation interface (D1) in `Workflow.Modules/Abstractions/`.
- [ ] J3.2 `ModuleAwareWorkflowValidator.ValidateConnectionPorts`: when the target module implements
      it, validate against the derived list instead of `Schema.Inputs`.
- [ ] J3.3 Decide whether `WorkflowExecutor.ValidateConnectionPorts` (load-time, outputs only today)
      needs a matching input-side check — currently it does not validate inputs at all.
- [ ] J3.4 Surface the capability to the client: extend `ModuleSchemaDto` / the modules endpoint so
      the designer knows a module has dynamic inputs (and mirror it in
      `Workflow.UI.Client/Api/Dtos/ModuleDtos.cs`).
- [ ] J3.5 Tests: derived port accepted; out-of-range port (`in4` on a 3-input node) still MA004s;
      non-dynamic modules unaffected.

### J4 — `builtin.join` 🔗

- [ ] J4.1 The module: N inputs derived per D2, one `result` output (D3) plus `count`.
- [ ] J4.2 `ValidateConfiguration`: reject a non-positive or absurd `inputCount` (pick a ceiling),
      duplicate names in `inputs`, and names that collide with reserved keys (`__incomingBranches__`,
      the merged-output `output` port).
- [ ] J4.3 Decide the unwired-input behaviour (**Q5**).
- [ ] J4.4 Optional `mode` for parity with FanIn (`named` default / `merge` / `concat`) — **Q4**.
- [ ] J4.5 Register in `BuiltinModules.GetAll()`; remember `BuiltinModuleIntegrationTests` pins an
      exact roster **and** count (currently 40) and will fail until updated.

### J5 — Designer renders the ports 🎨

- [ ] J5.1 Mirrored derivation helper in `Designer/State/`, used by `NodePorts.Inputs`.
- [ ] J5.2 Drift test asserting the client mirror and the server module agree across a table of
      configurations (pattern: `VariableEnumDriftTests`).
- [ ] J5.3 Confirm geometry and edge anchoring need no change (F8 says they don't — verify with a
      6-input node, and check the properties panel and node header still look sane at that height).

### J6 — Inspector affordance & arity changes 🎛️

- [ ] J6.1 The arity control in the properties panel (`Number` editor for `inputCount`, `Json` for
      `inputs`) must re-render the node's sockets immediately on change.
- [ ] J6.2 Handle shrinking arity with attached edges per D6 / **Q6** — this is the one genuinely
      fiddly interaction in this plan.
- [ ] J6.3 Confirm the change participates in undo/redo as a single step, including any edge
      deletions it causes.

### J7 — Designer warnings 🧭

- [ ] J7.1 **Two or more edges into the same input port** on a module that isn't FanIn → warning,
      naming the port and stating that only the last value arrives (F6). Applies to *all* modules,
      not just Join — this is the trap users will actually fall into. Severity per **Q8**.
- [ ] J7.2 A Join node whose declared inputs are not all wired → warning listing the empty ones (D5).
- [ ] J7.3 A connection targeting a port outside the node's current derived list → warning
      ("this edge is no longer connected to anything"), which is the client-side complement to J6.2.
- [ ] J7.4 Tests for each rule, asserting severity explicitly.

### J8 — FanIn legibility 🪄

- [ ] J8.1 Show arity on the node: render the `branches` port label with the incoming-edge count,
      e.g. `branches (3)` — derived from the document, no schema change.
- [ ] J8.2 Show the branch **order** somewhere (F2). Cheapest useful version: an ordered list of
      incoming edges in the inspector when a FanIn node is selected. Whether that list should be
      *reorderable* is a bigger question — it would mean reordering the connections array — so I'd
      start read-only. **Q7**.

### J9 — Tests 🧪

- [ ] J9.1 `JoinModuleTests` — derivation from both properties, each mode, unwired inputs, config
      validation.
- [ ] J9.2 Validator tests (J3.5) and designer rule tests (J7.4).
- [ ] J9.3 FanIn sub-graph regression (J2.3).
- [ ] J9.4 An end-to-end engine test: three parallel branches into one Join, asserting the named
      result — the thing the user actually asked for, proven end to end.
- [ ] J9.5 Update `BuiltinModuleIntegrationTests` roster/count (J4.5).

### J10 — Docs 📚

- [ ] J10.1 Join gets its own section in `docs/advanced-flow-control.md`, next to FanIn, with an
      explicit "FanIn vs Join — which do I want?" table (the one above).
- [ ] J10.2 Call out the `builtin.transform.join` distinction (F9) in both places.
- [ ] J10.3 Quick Module Index + TOC entries.

### J11 — 🐞 *(optional)* Parallel's branch ports

- [ ] J11.1 Make `builtin.parallel`'s derived branch ports renderable and wireable (F10). Naturally
      the same mechanism as J3/J5, applied to outputs. In or out of scope per **Q7**.

### J12 — 🐞 *(optional)* TryCatch's `input` port

- [ ] J12.1 Confirm F11, then fix — either declare `input` on `TryCatchModule`'s schema or let the
      J3 mechanism cover it.

---

## TO RESOLVE ❓

Please answer inline; I'll fold the answers in and revise before implementing.

**Q1 — Name.** `builtin.transform.join` already exists as a relational join (F9). What should the new
control-flow module be called?
> *(a) `builtin.join` / "Join" — matches what the users asked for, accepts the palette collision;
> (b) `builtin.merge` / "Merge"; (c) `builtin.combine` / "Combine"; (d) `builtin.collect` / "Collect";
> (e) something else.*
>
> **Answer:**
> Merge

**Q2 — Should FanIn also gain named/declared inputs,** or stay the positional barrier while Join
becomes the named one (D4)?
>
> **Answer:**
> Yes please, FanIn should gain named/declared inputs. It would make it more versatile and user-friendly.

**Q3 — Default port names and default count.** `in1..inN`? `input1..inputN`? `a`/`b`/`c`? And what
should a freshly dropped Join node start with — 2 inputs?
>
> **Answer:**
> `input1..inputN` seems clear and descriptive. Starting with 2 inputs is a good default, as it encourages users to think about combining multiple sources without overwhelming them with too many options at once.

**Q4 — Join's output shape.** Is `result` = `{ portName: value, … }` plus `count` enough (D3), or do
you also want FanIn-style `mode` options (`merge`, `concat`) for parity? Would per-input
*pass-through output ports* ever be useful, or is that clutter?
>
> **Answer:**
> This actually alongside Q2 makes me wonder if we should have 'fanin' become the merge, and have this 'new' module be the 'fanin' that provides results by portname.
> We can accept the breakage because we are only user testing and it is a new module.

**Q5 — Declared but unwired input.** If a Join declares 3 inputs and only 2 are connected, should
`result` (a) omit the missing key, (b) include it as `null`, or (c) should the node fail?
>
> **Answer:**
> Unwired inputs should be treated as a validation error and the node should not run.

**Q6 — Reducing the input count with edges attached** (D6, J6.2). Should the designer (a) refuse the
reduction while those ports are wired, (b) delete the stranded edges as part of the same undoable
change and tell the user, or (c) allow it and flag the stranded edges as a warning?
>
> **Answer:**
> The designer should provide a confirmation before deleting the edges, allowing the user to either proceed with the deletion or cancel the action. This ensures that users are aware of the consequences of their actions and prevents accidental loss of connections.

**Q7 — Scope.** Include the adjacent bugs in this piece of work, or split them out?
> *J2 (FanIn in sub-graphs) — I'd argue **in**, it's the same question. J11 (Parallel's unwireable
> branch ports) — same mechanism, opposite direction; **in** makes for a coherent "ports you can see
> and count" story, **out** keeps this smaller. J12 (TryCatch) — small, verify first. J8.2 (a
> reorderable FanIn branch list) — probably a follow-up.*
>
> **Answer:**
> Include J2 and J11 in this piece of work for a coherent story, but verify J12 first before deciding. J8.2 can be a follow-up.

**Q8 — Severity of the multi-edge-into-one-port rule (J7.1).** Warning, or error? It is currently
silent data loss (F6), which argues for error; but it's legal today and someone may be relying on
last-writer-wins, which argues for warning.
>
> **Answer:**
> We should error on multi-edge-into-one-port to prevent silent data loss. This will ensure that users are aware of the issue and can take corrective action.

**Q9 — Client/server duplication.** D1 has the designer mirror the port-derivation rule, guarded by a
drift test (matching the existing `VariableEnumDriftTests` pattern). The alternative is a server
endpoint that returns a node's derived ports for a given configuration — no duplication, but a round
trip whenever the arity changes. Which do you prefer?
>
> **Answer:**
> Mirror the port-derivation rule in the designer, guarded by a drift test, to avoid duplication and ensure consistency.
---

## Worth a second opinion 🤔

- **Is Join actually a new module, or a mode on FanIn?** A `builtin.fanin` with
  `mode: "ports"` could derive named input ports and reuse everything. Fewer modules in the palette;
  but it doubles down on a module whose existing contract is positional and hidden, and the palette
  entry "Fan In" doesn't say "join" to anyone looking for a join. I lean towards a separate module
  for discoverability — the users asked for a thing called "join".
- **Declared arity vs. just letting people draw edges.** Everything in J4–J7 exists to make arity
  explicit. The cheaper alternative is to leave arity implicit (as FanIn does) and spend the effort
  purely on *visibility* — J8's arity badge, plus J7.1's collision warning, plus J1's docs. That
  would address bullets 1 and 3 of the ask for maybe a fifth of the work, and skip bullet 2. Worth
  weighing if you'd rather ship something small first.

---

*Created 2026-08-02. Findings verified against the code at the cited lines. No implementation yet.*
