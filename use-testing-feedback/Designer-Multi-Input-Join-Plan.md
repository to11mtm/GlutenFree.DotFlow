# Designer Multi-Input Ports & FanIn Ports Mode — Plan

> 📋 Response to user feedback (2026-08-02): *"how the fanin module works as far as multiple
> inputs"*, *"a 'join' module that can take multiple inputs in"*, and *"multiple inputs for these in
> the UI to make it clear to the users, potentially specifying how many inputs are expected"*.
>
> **Revision 4 — Q17/Q19/Q20 settled; Q18 rebuilt from scratch.** Your Q18 answer trailed off with
> *"modules should be handling inputs as they come in"* — which turned out to be the most useful
> thing in the exchange, because it's a **streaming** model and DotFlow is a **run-once DAG**. I'd
> asked you to choose between three implementations without first establishing the model they'd run
> on. §*Q18, explained properly* fixes that. Round-4 questions are **Q18 (re-asked)**, **Q22** and
> **Q23**. Everything except J4b is now decided. Still nothing implemented.

## The ask

| # | Feedback | Reading |
| --- | --- | --- |
| 1 | "How does the fanin module work as far as multiple inputs?" | A **documentation gap** first and foremost — the mechanism is real but invisible. |
| 2 | "A 'join' module that can take multiple inputs in" | A barrier whose inputs are **named and addressable**, not an anonymous ordered pile. |
| 3 | "Multiple inputs for these in the UI… specifying how many inputs are expected" | **Declared arity**: the node shows N sockets because the author said N, and the designer can then check wired-vs-declared. |

## Summary

| # | Item | Kind | Size | Status |
| --- | --- | --- | --- | --- |
| J1 | Document how FanIn actually receives branches (+ fix the wrong ordering claim, F14) | Docs | S | ⬜ |
| J2 | 🐞 Fix FanIn inside sub-graphs (**zero** branches today) — likely a prerequisite for Q16 (F18) | Bug | S | ⬜ **do first** |
| J3 | Dynamic-input-port derivation (interface + server validation) | Core | M | ⬜ |
| J3b | Variadic port flag on `PortDefinition` + value collection (D7, D9) | Core | M | ⬜ |
| J4 | FanIn **ports mode** — N named inputs, `input1..inputN`, default 2 | Feature | M | ⬜ |
| J4b | 1-to-1 pairing | Feature | ? | ⛔ **blocked on Q18** |
| J5 | Designer renders per-node derived input ports | UX | M | ⬜ |
| J6 | Stepper arity control, redistribute-on-mode-switch, confirm-then-delete on shrink | UX | M | ⬜ |
| J7 | Rules: multi-edge **error** (client + server), missing-input errors per D5 | UX/Core | M | ⬜ |
| J8 | FanIn legibility (arity badge on the variadic port) | UX | S | ⬜ *(J8.2 deferred)* |
| J9 | Tests | Tests | M | ⬜ |
| J10 | Docs | Docs | S | ⬜ |
| J11 | 🐞 `builtin.parallel` branch ports are unwireable | Bug | S | ⬜ **in scope** |
| J12 | 🐞 TryCatch's designer-only `input` port — **verify first** | Bug | S | ⬜ |
| J13 | 🆕 Missing-value semantics across all nodes (from your Q17) | Design | ? | ⬜ **Q22** |

**Only J4b is blocked.** Everything else is now decided. See **Q23** for what I'd suggest doing
first — J2 is small, fixes a silent wrong answer, and may well *be* the answer to Q18.

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

### F12 — ⚠️ "Error on two edges into one input port" (Q8) would invalidate **every FanIn workflow**

FanIn offers exactly one input port, `branches` (F1), so in the designer *every* edge drawn into a
FanIn node targets that same port. A rule that errors on two-or-more edges per input port therefore
condemns the module the rule was written to support.

**Proposed resolution: make "many edges welcome" an explicit, declared property of a port.** Add a
flag to `PortDefinition` (`AcceptsMany` / `IsVariadic`), following exactly how `SupportsTemplates`
was added as an explicit opt-in. Then:

- FanIn's `branches` port declares it → many edges are legal *and self-documenting*;
- every other input port doesn't → Q8's error rule is safe, and now says something precise:
  *"`transform` has one value slot and you've wired two things into it; only the last arrives"*;
- the designer gains something concrete to draw (a stacked/ringed socket), and the `branches (3)`
  arity badge from J8.1 becomes part of the port rather than a special case.

This turns Q8 from a rule that fights the codebase into one that documents it. It does mean Q8's
answer ("error") should be re-confirmed against the *revised* rule — see **Q13**.

### F13 — ⚠️ "Unwired input is a validation error, node doesn't run" (Q5) has two very different readings

Two distinct checks hide in that sentence, and they behave very differently:

| Check | When | Question it asks |
|---|---|---|
| **Design-time** | Save gate | "Is every declared input port *connected to something*?" |
| **Run-time** | Node execution | "Did a value actually *arrive* on every declared input?" |

They come apart in a case that is not exotic at all — it's the most common reason to want a merge:

```
        ┌─ true ──▶ priceFromApi ──┐
condition                          ├──▶ merge
        └─ false ─▶ priceFromCache ┘
```

When the condition takes the `true` branch, the engine **skips** the false branch
(`DispatchCore.ExecuteReadySuccessors` → `TrySkipNodeDownstream`, `:181-184`). A skipped predecessor
still counts as satisfied, so the merge fires (`TryFireSuccessor`, `:206-208`) — but
`GatherNodeInputs` produces no value for that port (`WorkflowExecutor.cs:850-864` logs a warning and
adds nothing).

So under a **run-time** reading of Q5, this workflow fails every single time, whichever branch runs.
The merge would be unusable downstream of any conditional, switch or error boundary — which is most
of the interesting graph shapes.

Under a **design-time** reading, both ports are wired, the graph saves, and at run time the merge
receives one value and one absence — which is exactly what "merge the branches of an if/else" means.

**I'd strongly recommend design-time only**, with the absent value then falling to Q5's *other* half
(omit the key, or `null` — see **Q11**). But this changes what the module does, so it needs your call
rather than my assumption.

### F14 — 📄 The FanIn docs are wrong about ordering

`docs/advanced-flow-control.md:363` says:

> `Concat` — collects payloads into an array in **branch-completion order**

It doesn't. `GatherNodeInputs` iterates `_definition.Connections.Where(c => c.TargetNodeId == nodeId)`
(`WorkflowExecutor.cs:794-796`) — the order edges were **drawn**, fixed at author time and completely
independent of which branch finishes first. The module's own source says so (`FanInModule.cs:194-195`).
Worth fixing whichever naming option wins, and a good example of why J1 exists.

### F15 — 🙂 The blast radius of renaming `builtin.fanin` is small

No workflow definition under `examples/` references `builtin.fanin` or `builtin.fanout` at all — the
only occurrences are `docs/advanced-flow-control.md` (5 places), the module, its tests, and the
palette. So "we can accept the breakage" is well founded on the *repo* side; the only real exposure
is workflows your testers have already saved. That materially lowers the cost of **Option B** below.

### F16 — FanOut's rendezvous **cannot** be a named-port module

`FanOutModule`'s `branch` port "fires once per item" (`FanOutModule.cs:43`, `:145`) over a
data-dependent collection. The number of branches is unknown until run time, so nothing downstream of
a FanOut can be a fixed set of named sockets. The docs already name FanIn as exactly this rendezvous
(`advanced-flow-control.md:355`: *"The convergence point downstream from a `parallel` or `fanout`"*).

**Whichever module is positional must remain the partner of Parallel/FanOut**, and that pairing is a
real discoverability asset: "Fan Out" → "Fan In" is a name pair users can guess. This is the
strongest argument against the swap in Option B.

### F17 — 🔴 Q11 and Q16 contradict each other

Your two answers:

- **Q11 = (b)**: *"the node fails if any declared input didn't receive a value."*
- **Q16**: *"vital for the use case of **merging multiple branches of a conditional**."*

Under Q11(b), merging the branches of a conditional **fails on every single run**. Only one branch of
a condition executes; the other is skipped (`DispatchCore` → `TrySkipNodeDownstream`, `:181-184`), and
a skipped predecessor produces no value for its port (`WorkflowExecutor.cs:850-864`). So the merge
node always has at least one declared input with no value, and always fails. The use case you called
vital is precisely the one Q11(b) forbids.

I think the answer is that **"didn't receive a value" is really three different situations**, and only
some of them are errors:

| # | Situation | Author's intent | Recommended |
|---|---|---|---|
| 1 | Port has **no connection at all** | Author declared an input and forgot to wire it | 🔴 **Error** — design-time *and* run-time |
| 2 | Port is wired, but the upstream branch was **skipped** | Entirely deliberate — this is the if/else merge | ✅ **Tolerate** — the value is simply absent |
| 3 | Port is wired, upstream **ran** but emitted nothing on that output port | Usually a genuine mistake (wrong port name, module didn't produce it) | 🔴 **Error** at run time |

This is implementable exactly as stated: the engine already distinguishes skipped from completed
predecessors (`_isSkipped` / `_isCompleted`, used at `DispatchCore.cs:208`), and `GatherNodeInputs`
already logs the two cases separately (`:830-834` for "ran but didn't produce it", `:852-855` for "no
outputs available"). So case 2 can be tolerated and case 3 can fail, with no new bookkeeping.

That gives you the strictness of Q11(b) for real mistakes while keeping Q16's vital case working.
**Q17** asks you to confirm.

### F18 — 🔴 Q16's "1-to-1 merge": why I got stuck, and what the real question is

Your Q18 answer trails off with the most useful sentence in this whole exchange:

> *"This has me a little lost because modules should be handling inputs as they come in and outputting…"*

That's not confusion — it's a **different execution model**, and it's a perfectly reasonable one. It's
just not the one DotFlow has. Reconciling the two is the actual question, so I've rewritten this
finding to start there. See §*Q18, explained properly* below for the full walkthrough; the short
version:

**DotFlow nodes are not stream processors.** Every node fires **exactly once**, when all of its
predecessors have finished, and receives one snapshot of its inputs
(`DispatchCore.TryFireSuccessor:206-208` → `GatherNodeInputs`). There is no "as they come in": by the
time a node runs, everything upstream is already done. A node cannot emit A₁B₁, then later emit A₂B₂,
because there is no "later" — it runs once and returns one result.

The **only** exception is a fan-out/loop body, where the *whole sub-graph* is instantiated once per
item (`ParallelExecutionCoordinator.BuildPerItemBranches:184-213`). Inside a branch, a node does run
once per item — but each instance is isolated and sees only its own item.

So "A1 with B1, then A2 with B2" has to resolve into one of these shapes, and which one it is depends
entirely on **where the numbering comes from** — that's the question I failed to ask clearly.

### F19 — 🙂 Option C's discoverability cost is cheaply mitigated

Option C means there is no palette entry called "Merge" or "Join" for users to find. But the palette
filter already searches **Description** as well as id and display name
(`ModulePalette.razor:90-93`):

```csharp
query = query.Where(m =>
    m.Id.Contains(f, …) ||
    m.DisplayName.Contains(f, …) ||
    m.Description.Contains(f, …));
```

So putting the words *merge* and *join* in FanIn's `Description` makes it findable by the terms your
users actually reached for, at a cost of about eight characters. Optionally its `DisplayName` could
become something like "Fan In (Merge)" — **Q19**.

### F20 — Switching an existing FanIn between modes strands every edge

Under Option C, a FanIn node in variadic mode has all of its incoming edges on the single `branches`
port. Switch it to ports mode and `branches` disappears — so **every** existing edge is stranded at
once. That's the same class of problem as Q6's shrinking arity, but hits harder and on the very first
interaction anyone will try.

The confirm-then-delete flow from Q6 covers it correctly but destructively. A nicer option:
**redistribute** the existing edges across the newly derived ports in order (edge 1 → `input1`, edge 2
→ `input2`, …), growing the port count to fit if needed, so switching modes is lossless and
reversible. **Q20**.

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

### Naming & the FanIn/Merge split — the interplay you spotted 🎭

Your three answers pull in different directions:

- **Q1**: the new module is **Merge**.
- **Q2**: **FanIn should gain named/declared inputs.**
- **Q4**: *"should 'fanin' become the merge, and this 'new' module be the 'fanin' that provides
  results by portname?"*

Q1 and Q4 name the new module differently, and **Q2 partly dissolves the need for a second module at
all** — if FanIn gains named inputs, what is Merge for? These are the three coherent end-states:

#### Option A — two modules, names follow behaviour ✅ *(my recommendation)*

| Module | Id | Job |
|---|---|---|
| **Fan In** *(unchanged id)* | `builtin.fanin` | **Positional / variadic.** Arity unknown at design time. The rendezvous for Parallel and FanOut. Gains a properly declared variadic `branches` port (F12), the arity badge, doc fixes (F14) and the sub-graph fix (F4). |
| **Merge** *(new)* | `builtin.merge` | **Declared / named.** Author says "I expect 3 inputs", names them, gets `{ input1: …, input2: … }`. |

- Honours Q1's name.
- **Answers Q2 by building Merge rather than by modifying FanIn** — the user-facing wish ("I want
  named inputs") is granted; it just lives in the module whose whole purpose is named inputs.
- Zero breakage. No id changes meaning.
- Keeps the FanOut → FanIn name pairing that F16 shows is structurally necessary anyway.
- Cost: two palette entries, and someone might reach for "Fan In" when they want Merge. Mitigated by
  the "which do I want?" table in the docs and by both sitting adjacent in Flow Control.

#### Option B — the swap you floated in Q4

| Module | Id | Job |
|---|---|---|
| **Merge** | `builtin.merge` | Today's FanIn code, renamed. |
| **Fan In** | `builtin.fanin` | New named-ports module — same id, **different contract**. |

- Gives the prominent, guessable name to the module people will reach for most.
- Breakage is genuinely cheap on the repo side (F15).
- **But**: `builtin.fanin` would keep its id while changing meaning. Existing saved nodes have edges
  on a `branches` port that no longer exists — they'd fail MA004 on next save (loud, at least) or sit
  broken. Reusing an id for a different contract is the one kind of breakage that's hard to diagnose.
- **And the bigger problem (F16)**: the module that pairs with FanOut *must* be positional, because a
  per-item fan-out produces an unknown number of branches. Under Option B, the answer to "I fanned
  out — how do I fan back in?" is *"use Merge"*, and the module actually called Fan In is the one
  that **can't** do it. That inverts the names relative to the concepts.

#### Option C — one module, two port modes

`builtin.fanin` keeps its variadic behaviour by default and grows a declared-ports mode: set
`inputs`/`inputCount` and it derives named sockets and returns `{ name: value }`.

- The most literal reading of Q2. No new module, no breakage, one palette entry.
- Cost: one module with two quite different contracts and a properties panel that changes shape; and
  the thing your users asked for by name ("a join / merge") isn't findable by that name in the
  palette — though F19 shows that's cheaply mitigated via the description.

**Recommendation: Option A**, on the strength of F16 — the FanOut/FanIn pairing is a structural fact,
not just a naming habit, so the positional module should keep the positional name. **Q10** asks you
to confirm or overrule.

> **✅ SETTLED (Q10): Option C.** One module. `builtin.fanin` keeps its variadic behaviour by default
> and gains a declared-ports mode. No `builtin.merge`, no new palette entry, no breakage. Q1's
> "Merge" answer is therefore moot as a *module id*, though it may survive as wording in the display
> name and description (F19, **Q19**). The rest of this plan is written against Option C.
>
> Consequences to keep in view while implementing:
> - **Two port shapes on one module.** `NodePorts` and the properties panel both branch on the mode.
>   This is the cost of C and it lands mostly in the designer.
> - **Default stays variadic**, so a freshly dropped FanIn node is never in error (it has no declared
>   inputs to leave unwired) — the strictness from Q5/Q11 only applies once someone opts into ports
>   mode. That's a happy accident of C worth preserving deliberately.
> - **Mode switching strands edges** — F20, **Q20**.
> - **`branches` should be hidden in ports mode**, or the node shows a socket that does nothing.

> If you pick **A**, note that Q2 is satisfied *indirectly*. If what you actually want is named inputs
> **on FanIn itself, in addition to Merge**, say so — but then the two modules overlap almost
> entirely and I'd argue for Option C instead.

### What the two modes give you

Under Option C these are two **modes of one module**, selected by whether declared inputs are
configured:

| | variadic mode *(default)* | ports mode *(new)* |
|---|---|---|
| Arity | Implicit — however many edges you drew | **Declared** by the author, visible as N sockets |
| Ordering | Edge-creation order, invisible (F2) | Irrelevant — each value has a name |
| Naming | Upstream's port names, usually colliding (F3) | **Author's** names |
| Payload per input | The whole source node's output dictionary | Exactly the one output port you wired |
| Checkable? | No — "did I forget an edge?" is unanswerable | Yes — declared vs. wired (J7) |
| Pairs with FanOut? | **Yes** (F16) | No — can't, arity is data-dependent |

Note row 4: a named input receives *only the single upstream output port you wired to it*
(`WorkflowExecutor.cs:818-820`), whereas a variadic branch receives the **whole** source node's output
dictionary (`:843`). **Confirmed by your Q12.**

### The mode matrix (Q14)

You asked for `merge` and `concat` alongside the named shape. Under Option C the module now has *two*
axes — port shape and aggregation mode — so they need to compose cleanly rather than become two
overlapping lists. Proposal:

| `mode` | variadic mode | ports mode |
|---|---|---|
| `named` | key by **source port name**, `nodeId.port` on collision (today's behaviour) | key by **input port name** — no collisions possible ✨ *(default in ports mode)* |
| `concat` | array in edge order *(default today)* | array in **declared port order** — stable and visible |
| `merge` | shallow union, last wins by edge order | shallow union, last wins by **declared port order** |
| `first` / `last` | first/last by edge order | first/last **declared port** |
| `zip` | — | pending **Q18** |

The pleasing part: every existing mode acquires a *better* definition in ports mode, because
"declared port order" is visible on the canvas whereas "edge order" never was. Nothing is
special-cased away, and the family resemblance you asked for is real rather than cosmetic.

---

## Q18, explained properly 🎓

*This section exists because my last attempt asked you to choose between three implementations
without first establishing the model they'd be implemented on. Your "modules should be handling
inputs as they come in" is the right place to start.*

### The model you're describing

> *"modules should be handling inputs as they come in and outputting…"*

That's a **streaming / dataflow** model. A node is a long-lived worker: values arrive on its inputs
over time, and each time it has a complete set it emits a result. A₁ and B₁ arrive → emit; A₂ and B₂
arrive → emit again. Pairing is automatic, because the node is *stateful* and lives across many
values. This is how Node-RED, Apache NiFi, Kafka Streams and hardware dataflow languages work, and
it's a completely sensible thing to expect from a boxes-and-arrows tool.

### The model DotFlow actually has

DotFlow is a **DAG runner**. A node is not a worker; it's a **single scheduled task**.

```
DispatchCore.TryFireSuccessor(successorId)      // DispatchCore.cs:206-208
    var preds = _nodePredecessors[successorId];
    if (preds.All(p => _isCompleted(p) || _isSkipped(p)))   //  ← ALL, past tense
        _executeNode(successorId);                          //  ← exactly once
```

A node fires **once**, when everything upstream has already finished, and gets **one snapshot** of its
inputs (`GatherNodeInputs`). Then it returns one result and it's over. There is no "as they come in",
because by the time a node runs, there is no more incoming — it's all already there. There is no
"then A2 and B2", because there is no *then*.

The two models differ in one crucial way:

| | streaming | DotFlow's DAG |
|---|---|---|
| A node is… | a worker that lives across many values | one task that runs once |
| "Multiple values" means… | many arrivals **over time** | one collection **in one arrival** |
| Pairing A₁/B₁ then A₂/B₂ | the node's own job, automatic | must be *inside* a value, or *across separate runs of a sub-graph* |

**This isn't a limitation I'm defending** — it's just what's there, and it's why your sentence and my
three options didn't meet. Multiplicity in DotFlow has to live somewhere other than time.

### So where can multiplicity live? Exactly two places

**Place 1 — inside a single value.** A node receives one input whose *value* happens to be a list.
`[a₁, a₂, a₃]` arrives all at once, in one execution.

```
fetchOrders ──[list of 3]──▶ processAll
```

**Place 2 — across repeated runs of a sub-graph.** `fanout`/`foreach` instantiates its whole body
once per item. The body's nodes each run three times, in three isolated copies, each seeing one item.

```
              ┌──────── branch (runs once per item) ────────┐
fanout ──────▶│  getPrice ─┐                                │
              │            ├──▶ fanin ──▶ …                 │
              │  getStock ─┘                                │
              └────────────────────────────────────────────┘
```

That's it. Those are the only two. **Your A₁/B₁ then A₂/B₂ has to be one of them**, and that's the
whole question — not "which of my three implementations", but **where does the numbering in
A₁/A₂ come from?**

### The same requirement in each place

**If the numbering is inside a value** (Place 1): port A holds `[a₁,a₂,a₃]`, port B holds `[b₁,b₂,b₃]`,
and you want `[{A:a₁,B:b₁}, {A:a₂,B:b₂}, {A:a₃,B:b₃}]`. That's a **zip**, and it's a pure data
transform — one new mode, no engine work. Small.

**If the numbering is across sub-graph runs** (Place 2): the pairing is *automatic and already
correct*, because each branch is isolated. Put the merge **inside** the branch: `getPrice` and
`getStock` for item 2 both run in branch 2, and a merge in branch 2 can only ever see item 2's
values. Cross-contamination is structurally impossible. The deliverable here is a **documented
recipe** — "merge inside the loop, not after it" — plus the J2 bug fix, because a FanIn inside a
sub-graph currently aggregates **zero** branches (F4). *Your vital use case may therefore be a bug
fix and a doc page rather than a feature.*

**If it's neither** — if you genuinely need one node to pair up values that arrive from separate,
independent executions — then we're talking about correlation identities flowing through the engine,
which is a real engine feature and deserves its own plan. I want to be sure that's what you need
before proposing it.

### Why the conditional framing confused me

Your Q16 said this was *"vital for merging multiple branches of a conditional"*. But a conditional
runs **once** and takes **one** branch. There is no A₂ — there's one A, or one B, and the "merge"
is just picking up whichever branch ran (which is exactly the Q17 case we've now settled: tolerate the
skipped side, omit it from the result).

So either:
- the conditional case and the 1-to-1 case are **two different requirements** that landed in one
  answer — the conditional one is already solved by Q17, and the pairing one is about loops; or
- the conditionals are **inside a loop**, which makes it Place 2; or
- I'm still missing the shape entirely.

### What would settle it fastest

One real workflow, however roughly sketched. Something like:

> *"We fetch a list of 50 orders. For each order we call the pricing API and the inventory API. We
> need the price and the stock level **for the same order** to end up together, then write all 50
> rows to a CSV."*

That description alone answers it: it's Place 2, the merge goes inside the fan-out branch, and the
work is J2 plus a doc page. Whereas:

> *"We get a CSV of names and a CSV of addresses, both in the same order, and we need to pair row 1
> with row 1."*

…is Place 1, and the work is a `zip` mode.

**A sketch of what your users are actually doing is worth more than any answer to a multiple-choice
question here** — including "we don't have a concrete case yet, it just seemed like something a merge
should do", which is a completely fine answer and would let us defer J4b entirely.

---

Decisions marked 🆕 or ✏️ changed in this revision. ✅ = settled by your answers.

| # | Decision |
|---|---|
| **D1** ✅ Ports are derived, not merely trusted | The module owns the derivation; the server validates against the derived list; the designer mirrors it with a drift test (your Q9). |
| **D2** ✅ Arity is configured the way Parallel already does it | An `inputs` property (JSON array of names, wins when present) plus an `inputCount` property (int). **Default names `input1..inputN`, default count 2** (your Q3). |
| **D3** ✏️ Output shape | `result` = `{ portName: value, … }` plus `count` in ports mode, with `concat`/`merge`/`first`/`last` also available per the matrix above (your Q14). |
| **D4** ✅ **One module, two modes** | Option **C** (your Q10). No `builtin.merge`. `builtin.fanin` keeps variadic as its default and gains a ports mode. |
| **D5** ✅ Missing inputs — three-way split | Your Q17. **Error** when a port has no connection at all, and when the upstream ran but emitted nothing on that port; **tolerate** when the upstream was skipped. A tolerated-missing value is **omitted** from `result`, not null. |
| **D6** ✅ Arity changes never silently orphan an edge | Confirm-then-delete for shrinking (your Q6), stepper + free-text control (your Q15), and **redistribute** on mode switch (your Q20). |
| **D7** ✅ "Accepts many edges" is a declared port property | New `PortDefinition` flag (F12), making the multi-edge rule safe. |
| **D8** ✅ Multi-edge into a non-variadic input is an **error, client and server** | Your Q13. New MA code; some already-saved test workflows will start returning 422. |
| **D9** ✅ Variadic ports collect their values | Your Q16 — in scope. `__incomingBranches__` stops being a private convention and becomes a documented consequence of a declared port property. |
| **D10** ⛔ 1-to-1 pairing | Your Q16 called it vital, but **Q18 is unresolved** — see the rewritten §*Q18, explained properly* below. Blocking J4b only; nothing else depends on it. |
| **D11** 🆕 "Fan In" keeps its display name | Your Q19. Discoverability comes from `merge`/`join` keywords in the **description**, which the palette filter already searches (F19). |
| **D12** 🆕 Missing-value semantics get their own cross-cutting plan | Your Q17 asked for consistency "across all nodes", which is broader than this plan — see J13. |

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

### J3b — Variadic input ports 🔀

- [ ] J3b.1 Add the flag to `PortDefinition` (D7, F12), defaulting to `false` so every existing port
      keeps single-value semantics.
- [ ] J3b.2 Declare it on FanIn's `branches` port — the first honest description of what that port
      has always done.
- [ ] J3b.3 Mirror it in `PortDefinitionDto` (client + server) so the designer can see it.
- [ ] J3b.4 Render variadic sockets distinctly (a stacked/ringed dot) so "many edges land here" is
      visible before you draw the second edge, not after.
- [ ] J3b.5 **Collect values on a variadic port into a list** rather than last-writer-wins
      (D9, your Q16). `GatherNodeInputs` currently does `inputs[conn.TargetPortName] = outputValue`
      (`:820`); for a variadic port it should append. Nothing regresses today — FanIn ignores
      `branches` entirely — but this becomes the documented contract going forward.
- [ ] J3b.6 Decide whether `__incomingBranches__` should then be *reframed* as "the collected value
      of the variadic `branches` port" rather than a reserved magic key. Cleaner story, and it makes
      the sub-graph fix (J2) fall out of one shared code path instead of two.

### J4 — FanIn ports mode 🔗

- [ ] J4.1 Derived named inputs per D2 (`input1..inputN`, default 2), selected by configuring
      `inputs`/`inputCount`; absent ⇒ today's variadic behaviour, unchanged.
- [ ] J4.2 `ValidateConfiguration`: reject a non-positive or absurd `inputCount` (pick a ceiling),
      duplicate names in `inputs`, and names colliding with reserved keys (`branches`,
      `__incomingBranches__`, the merged-output `output` port).
- [ ] J4.3 Missing-input behaviour per **D5** (your Q17): error when a declared port has no
      connection or when the upstream ran and produced nothing; tolerate a skipped upstream, omitting
      the key from `result`.
- [ ] J4.4 The mode matrix from the design section (your Q14): `named` (default in ports mode),
      `concat`, `merge`, `first`, `last`, each defined over declared-port order.
- [ ] J4.5 Hide the `branches` port in ports mode, so the node never shows a socket that does nothing.
- [ ] J4.6 No registry change — the module already exists. `BuiltinModuleIntegrationTests`'s roster
      and count stay at 40 ✨ *(one of Option C's quieter wins)*.

### J4b — ⛔ 1-to-1 pairing *(blocked on Q18 — see §Q18, explained properly)*

The work depends entirely on **where the multiplicity lives**:

- [ ] J4b.1 **Place 1 (inside a value)** → a `zip` mode over list-valued inputs. Small, pure data
      transform, no engine work.
- [ ] J4b.2 **Place 2 (across sub-graph runs)** → **already correct by construction**; the
      deliverable is J2 (the bug fix) plus a documented "merge inside the loop, not after it" recipe
      with a worked example. No new feature.
- [ ] J4b.3 **Neither** → correlation identities threaded through branch payloads. A genuine engine
      feature; **should get its own plan rather than a bullet here.**

### J13 — 🆕 Missing-value semantics across all nodes 📐 *(new, from your Q17)*

You asked for a plan to "handle these cases consistently across all nodes", which is broader than
this feature — today the behaviour is implicit and undocumented everywhere:

- [ ] J13.1 Write down what actually happens now when a node's input is missing, for each cause
      (upstream skipped, upstream produced nothing, no connection). Right now it's two log lines and
      an absent dictionary key (`WorkflowExecutor.cs:830-834`, `:850-864`), with each module left to
      cope however it likes.
- [ ] J13.2 Decide whether the D5 three-way split should be the **engine-wide** rule rather than
      FanIn-specific. If it should, that's a behaviour change for every existing module and wants its
      own plan and its own risk assessment — **Q22**.
- [ ] J13.3 Either way, document the rule in the module-author guide so new modules stop each
      inventing their own.

### J5 — Designer renders the ports 🎨

- [ ] J5.1 Mirrored derivation helper in `Designer/State/`, used by `NodePorts.Inputs`.
- [ ] J5.2 Drift test asserting the client mirror and the server module agree across a table of
      configurations (pattern: `VariableEnumDriftTests`).
- [ ] J5.3 Confirm geometry and edge anchoring need no change (F8 says they don't — verify with a
      6-input node, and check the properties panel and node header still look sane at that height).

### J6 — Inspector affordance & arity changes 🎛️

- [ ] J6.1 A **stepper (+/−) as the primary control, with free-text entry for large jumps** (your
      Q15). Free-text commits on blur/Enter so typing `10` over `2` doesn't transiently read as `1`
      and strand edges mid-keystroke.
- [ ] J6.2 Confirm-then-delete for stranded edges (D6, your Q6): a dialog naming the connections that
      will be removed, with proceed/cancel.
- [ ] J6.3 **Mode switching** (variadic ⇄ ports) strands *every* edge at once (F20) — **redistribute**
      them across the newly derived ports in order, growing the port count to fit (your Q20). Lossless
      and reversible.
- [ ] J6.4 The whole thing — property change plus any edge deletions or rewiring — must be **one**
      undo step.
- [ ] J6.5 Display name stays "Fan In"; `merge`/`join` keywords go in the **description**, which the
      palette filter already searches (your Q19, F19).

### J7 — Rules 🧭

- [ ] J7.1 **Two or more edges into the same non-variadic input port → error, client and server**
      (your Q8/Q13, made safe by D7/F12). Applies to *all* modules, not just FanIn — this is the trap
      users actually fall into (F6). Must name the port and say only the last value arrives.
- [ ] J7.2 A declared input port with **no connection** → error (your Q5), listing the empty ports.
- [ ] J7.3 A wired input that received nothing at **run time** → per D5 (your Q17): fail if the
      upstream ran and produced nothing; tolerate if it was skipped, omitting the key from `result`.
- [ ] J7.4 A connection targeting a port outside the node's current derived list → error
      ("this edge is no longer connected to anything") — the client-side complement to J6.2/J6.3.
- [ ] J7.5 New MA code(s) for the server-side rules; note some already-saved test workflows will
      begin returning 422 (your Q13 accepted this).
- [ ] J7.6 Tests for each rule, asserting severity explicitly.

### J8 — FanIn legibility 🪄

- [ ] J8.1 Show arity on the variadic port: `branches (3)`, derived from the document. With D7 this
      becomes generic behaviour for any variadic port rather than a FanIn special case.
- [ ] J8.2 *(deferred to a follow-up, per your Q7)* Show the branch **order** (F2) — an ordered,
      read-only list of incoming edges in the inspector.

### J9 — Tests 🧪

- [ ] J9.1 Module tests — derivation from both properties, each mode, unwired inputs, config
      validation.
- [ ] J9.2 Validator tests (J3.5) and designer rule tests (J7.5).
- [ ] J9.3 FanIn sub-graph regression (J2.3).
- [ ] J9.4 An end-to-end engine test: three parallel branches into one named-input node, asserting
      the named result — the thing the users actually asked for, proven end to end.
- [ ] J9.5 **The F13 case, end to end**: a conditional whose two branches both feed the node, run
      once per branch, asserting the agreed behaviour. This is the test that will catch it if we get
      Q11 wrong.
- [ ] J9.6 Update `BuiltinModuleIntegrationTests` roster/count (J4.5).

### J10 — Docs 📚

- [ ] J10.1 The new module gets its own section in `docs/advanced-flow-control.md`, next to FanIn,
      with the "which do I want?" table from above.
- [ ] J10.2 Call out the `builtin.transform.join` distinction (F9) in both places.
- [ ] J10.3 Fix the false "branch-completion order" claim (F14) and document that order is
      edge-creation order.
- [ ] J10.4 Quick Module Index + TOC entries.

### J11 — 🐞 Parallel's branch ports *(in scope, your Q7)*

- [ ] J11.1 Make `builtin.parallel`'s derived branch ports renderable and wireable (F10) — the same
      derivation mechanism as J3/J5, applied to outputs.

### J12 — 🐞 TryCatch's `input` port *(verify first, your Q7)*

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

## TO RESOLVE — round 2 ❓

Q10 and Q11 are the blocking ones; the rest are refinements.

**Q10 — 🔴 The naming & split (Q1 × Q2 × Q4).** Which end-state? See §*Naming & the FanIn/Merge
split* for the full argument.
> *(a) **Option A** *(recommended)* — `builtin.fanin` unchanged and positional; new `builtin.merge`
> with named inputs. Q2 is satisfied by Merge existing, not by changing FanIn. Zero breakage, and
> keeps the FanOut→FanIn pairing that F16 shows is structurally required.
> (b) **Option B** — the swap you floated: today's FanIn becomes `builtin.merge`; `builtin.fanin`
> becomes the new named-port module. Prominent name goes to the popular module, but the id changes
> meaning and the FanOut partner ends up called "Merge".
> (c) **Option C** — one module: `builtin.fanin` gains a named-ports mode. No new module, no
> breakage, but "merge/join" isn't findable by name in the palette.
> (d) A **and** named inputs on FanIn too — possible, but then the two modules overlap almost
> entirely and I'd push back towards (c).*
>
> **Answer:**
> Option C.

**Q11 — 🔴 Design-time or run-time?** (F13.) Your Q5 said an unwired input is a validation error and
the node shouldn't run. Which of these did you mean?
> *(a) **Design-time only** *(recommended)* — the save gate errors if a declared input port has no
> connection. At run time, a wired-but-skipped branch simply contributes nothing, so
> `condition → {A, B} → merge` works. Then: should the missing value be **omitted** from `result`, or
> present as **null**?
> (b) **Run-time too** — the node fails if any declared input didn't receive a value. Strict and
> predictable, but makes the module unusable downstream of any conditional/switch/try — which is most
> of the graphs people will want it for.
> (c) **Both, but with an opt-out** — e.g. a per-port "optional" marker or a module-level
> `allowMissing` property, so the strict default can be relaxed where branches are involved.*
>
> **Answer:**
> Option B.

**Q12 — What a named input receives.** A named port gets **only the single upstream output port you
wired to it** (`inputs[TargetPortName] = outputValue`), whereas a FanIn branch gets the **whole**
source node's output dictionary. So wiring `http-1.body` into `input1` gives `result.input1 = <body>`,
not `{ body: …, statusCode: … }`. I think that's the right call — more precise and more predictable.
Confirm?
>
> **Answer:**
> Yes, that is the right call. A named input should receive only the specific value from the upstream output port it is connected to, ensuring clarity and predictability in the data flow.

**Q13 — How far does the multi-edge error reach?** With D7/F12 the rule becomes *"two or more edges
into the same **non-variadic** input port is an error"*, which no longer breaks FanIn. Two follow-ups:
> *(a) Confirm **error** (not warning) under that revised wording.
> (b) Should it also be a **server-side** rule (a new MA code), so imports and hand-edited files
> can't bypass it? That would make some already-saved test workflows start returning 422 on save —
> loudly, which is arguably the point, but worth choosing deliberately.*
>
> **Answer:**
> Confirm error under the revised wording. It should also be a server-side rule to ensure consistency and prevent bypassing through imports or hand-edited files.

**Q14 — Modes on the named module.** With named ports, `result = { input1: …, input2: … }` is the
obvious shape. Do you also want FanIn-style alternatives — `merge` (shallow-union the inputs into one
flat object) and `concat` (array in port order)? They're cheap to add and give the two modules a
family resemblance; or they're three ways to say the same thing on a module whose selling point is
clarity.
>
> **Answer:**
> Yes, having the FanIn-style alternatives `merge` and `concat` would be beneficial. It provides flexibility for users who may want to combine inputs in different ways while maintaining a family resemblance between the modules.

**Q15 — The arity control's feel.** Editing `inputCount` from `2` to `10` passes through `1`, which
would fire the confirm-then-delete dialog mid-keystroke. Preference?
> *(a) Commit on blur / Enter (the dialog appears once, when you're done typing);
> (b) a stepper (+/−) instead of a free-text number, so every change is a deliberate single step;
> (c) both — stepper as the primary control, free text for large jumps.*
>
> **Answer:**
> Both — stepper as the primary control, free text for large jumps. This allows for precise adjustments with the stepper while still providing flexibility for users who need to make larger changes quickly.

**Q16 — Should a variadic port actually collect its values?** (J3b.5.) Today a variadic port's *value*
is last-writer-wins like any other; FanIn ignores it entirely and reads `__incomingBranches__`. We
could make the engine collect values on a variadic port into a list, which would make
`__incomingBranches__` a legible, documented feature rather than a private convention — and would let
future modules take "many of these" without engine special-casing. It's a bigger change to
`GatherNodeInputs` though. In scope, or note it as a follow-up?
>
> **Answer:**
> In scope. An important thing for certain workflows is making sure that the merges are 1-1 for the given inputs. i.e. if I have Input A and Input B, I want to have A1 and B1 merged, then A2 and B2 merged.
> This is actually vital for the use case of merging multiple branches of a conditional, and is a common pattern in workflows. Collecting values into a list would allow for more complex data structures to be handled effectively.

---

## TO RESOLVE — round 3 ❓

Two blockers (**Q17**, **Q18**) and three small ones.

**Q17 — 🔴 Q11 and Q16 contradict each other.** (F17.) Q11(b) says the node fails when a declared
input received no value; Q16 says merging the branches of a conditional is vital. But a conditional
*always* skips one branch, so under Q11(b) that merge fails every run. My proposed refinement splits
"didn't receive a value" three ways:

| Situation | Recommended |
|---|---|
| Port has **no connection at all** | 🔴 Error (design-time **and** run-time) |
| Port is wired but the upstream was **skipped** | ✅ Tolerate — this *is* the if/else merge |
| Port is wired, upstream **ran** but emitted nothing on that port | 🔴 Error at run time |

> *(a) Adopt the three-way split above *(recommended)* — strict about real mistakes, tolerant of
> deliberate branching. And then: is a tolerated-missing value **omitted** from `result`, or present
> as **null**?
> (b) Keep strict Q11(b) everywhere and accept that this module can't sit downstream of a
> conditional/switch/try — i.e. the if/else merge is out of scope.
> (c) Strict by default with an explicit per-node opt-out (e.g. `allowMissing: true`).*
>
> **Answer:**
> Adopt the three-way split above. A tolerated-missing value should be omitted from `result`, as it reflects the actual data flow and avoids introducing nulls that may not be meaningful in the context of the workflow.
> We should put together a plan to handle these cases consistently across all nodes. Especially if/else merges, we need to ensure that the behavior is predictable and clear to users.

**Q18 — 🔴 What does "A1 and B1 merged, then A2 and B2" actually look like?** (F18.) This is new and
the three readings differ by roughly two orders of magnitude in cost. **A concrete example of one of
your users' workflows would settle it faster than picking from this list** — the conditional framing
in your Q16 answer doesn't obviously produce an A₂/B₂ to pair with, which makes me think the real
shape involves a loop or a fan-out.

> *(a) **Collection zip** — port A carries `[a₁,a₂,a₃]`, port B carries `[b₁,b₂,b₃]`, output is
> `[{A:a₁,B:b₁}, …]`. Cheap: a `zip` mode, no engine work.
> (b) **Iteration-correlated** — inside a `fanout`/`foreach`, the A and B from the *same iteration*
> pair up. **This may already work for free**: each per-item branch runs in its own sub-graph
> (`ParallelExecutionCoordinator:184-213`), so a merge placed **inside** the branch only ever sees one
> iteration — and FanOut's `results` collects them branch-indexed. If so the deliverable is a
> documented recipe, not code. **Caveat: it needs J2 first, because a FanIn inside a sub-graph is
> broken today (F4).**
> (c) **Positional zip across variadic ports** — the k-th edge into A pairs with the k-th edge into B.
> Doable, but pairs on invisible edge-creation order (F2), which I'd want to fix before relying on it.
> (d) Something else — please describe the workflow.*
>
> **Answer:**
> This has me a little lost because modules should be handling inputs as they come in and outputting


**Q19 — Naming under Option C.** There's now no palette entry called Merge or Join. The palette filter
already searches **Description** (F19), so putting "merge" and "join" in FanIn's description makes it
findable for ~8 characters. Do you also want the **display name** to change?
> *(a) Description keywords only — display name stays "Fan In";
> (b) display name becomes "Fan In (Merge)" or similar;
> (c) display name changes per mode — "Fan In" when variadic, "Merge" when in ports mode. Cute, and
> the palette entry would still have to pick one.*
>
> **Answer:**
> Description keywords only — display name stays "Fan In". This keeps the naming consistent and avoids confusion for users who are familiar with the existing module.

**Q20 — Switching an existing FanIn node between modes** strands *every* edge at once, since they all
sit on the single `branches` port (F20).
> *(a) Confirm-then-delete, same as Q6;
> (b) **redistribute** — assign existing edges to `input1`, `input2`, … in order, growing the port
> count to fit *(recommended: lossless and reversible, and the ordering it uses is the same one that
> already governed the variadic behaviour, so nothing changes meaning)*;
> (c) block the switch until the node has no incoming edges.*
>
> **Answer:**
> Redistribute — assign existing edges to `input1`, `input2`, … in order, growing the port count to fit. This approach is lossless and reversible, maintaining the existing order of edges and ensuring that no data is lost during the mode switch.

**Q21 — Sequencing.** J2 is unblocked, small, fixes a silent-wrong-answer bug, and looks like a
prerequisite for Q16's use case (F18b). J1, J3, J3b, J11 and J12 are also unblocked. Shall I start on
those while Q17/Q18 settle, or hold everything until the whole design is agreed?
>
> **Answer:**
> Need better explanation around this

---

## TO RESOLVE — round 4 ❓

Only three, and two of them are optional.

**Q18 (re-asked) — 🔴 Where does the "1" and the "2" in A₁/A₂ come from?** Please read §*Q18,
explained properly* first — it replaces the three-option list with the actual question. The short
form:

DotFlow nodes run **once**, not continuously, so "A₁B₁ then A₂B₂" has to live in one of exactly two
places:

> *(a) **Inside one value** — port A holds a *list* `[a₁,a₂,a₃]`, port B holds `[b₁,b₂,b₃]`, and you
> want them paired index-wise. → a small `zip` mode. **Small.**
> (b) **Across repeated runs of a loop/fan-out body** — A and B are produced *per item*, and each
> item's pair must stay together. → **already correct** if the merge sits inside the branch; the work
> is the J2 bug fix plus a documented recipe. **Small, and mostly a bug fix.**
> (c) **Neither** — one node must pair values from genuinely separate executions. → correlation
> identities in the engine. **Large; deserves its own plan.**
> (d) **No concrete case yet** — it seemed like something a merge ought to do. → completely fine;
> we defer J4b and revisit when a real workflow needs it.*
>
> **A rough sketch of a real workflow answers this better than picking a letter.** Two examples of
> what "enough detail" looks like are at the end of that section.
>
> **Answer:**

**Q22 — How far should the missing-value rule reach?** (J13, from your Q17: *"a plan to handle these
cases consistently across all nodes"*.) The D5 three-way split is currently scoped to FanIn's declared
inputs. Making it engine-wide would be a behaviour change for **every** module — mostly a good one
(today a missing input is two log lines and an absent key), but not something to do as a side-effect
of this feature.
> *(a) Apply D5 to FanIn's declared inputs now; **write J13 as a separate plan** for the engine-wide
> rule *(recommended — keeps this feature shippable and gives the bigger change its own risk
> assessment)*;
> (b) do it engine-wide as part of this work;
> (c) FanIn only, and drop J13.*
>
> **Answer:**

**Q23 — Sequencing** *(re-asked with the detail you wanted).* Here's what's actually ready, in the
order I'd do it and why:

| Order | Item | Why here | Risk |
|---|---|---|---|
| 1 | **J2** — FanIn in sub-graphs | Silent wrong answer today: a FanIn in a loop/try/transaction aggregates **zero** branches and reports success (F4). Also the likely answer to Q18(b). Small, self-contained, no design left to settle. | Very low |
| 2 | **J12** — verify TryCatch's `input` port | 10 minutes. If confirmed, wiring anything into a TryCatch node fails the save gate (F11), and it changes what J3 has to cover. Cheap to check, annoying to discover later. | None (investigation) |
| 3 | **J1 + F14 doc fixes** | The docs actively state something false about ordering. Independent of every design question. | None |
| 4 | **J3b** — variadic port flag + value collection | The foundation for D7/D8/D9. Everything in J4–J7 sits on it. | Low — new flag defaults to false |
| 5 | **J3** — derivation interface + server validation | Unblocks ports mode. | Medium — touches MA004 |
| 6 | **J4–J7** — ports mode, designer, rules | The feature proper. Needs 4 and 5. | Medium |
| 7 | **J11** — Parallel's unwireable branch ports | Same mechanism as 5, applied to outputs. | Low |

> *(a) Start at 1 and work down while Q18 settles *(recommended — items 1–3 are bug fixes and doc
> corrections with no design risk, and item 1 may answer Q18 for us);
> (b) do items 1–3 only, then stop and review before the design-dependent work;
> (c) hold everything until Q18 is settled and the whole design is agreed.*
>
> **Answer:**



---

## Worth a second opinion 🤔

- **Option C's real cost lands in the designer, not the engine.** One module with two port shapes
  means `NodePorts`, the properties panel, the validation rules and the docs all branch on the mode.
  None of it is hard, but it's the kind of thing that accumulates special cases. Worth a look at the
  finished `NodePorts` to check it hasn't become a pile of module-id conditionals — it already has
  four (F8).
- **Declared arity vs. just letting people draw edges.** Everything in J4–J7 exists to make arity
  explicit. The cheaper alternative is to leave arity implicit and spend the effort purely on
  *visibility* — J8's arity badge, J3b's variadic socket, J7.1's collision error, and J1's doc fixes.
  That addresses asks 1 and 3 for maybe a fifth of the work. Now less compelling than it was, since
  Q16 wants pairing semantics that implicit arity can't express.
- **Q5/Q11's strictness is a notable break with the Start/End precedent**, which deliberately warns
  and never blocks. An unwired declared input is genuinely different — the author stated an
  expectation the graph doesn't meet — so I think strictness is right here. Flagging it so the
  inconsistency is a choice rather than an accident.
- **J4b could quietly become the biggest item in the plan.** If Q18 lands on "iteration-correlated,
  and merging inside the branch doesn't cover it", we'd be talking about correlation identities
  flowing through branch payloads — an engine feature that deserves its own plan rather than a
  bullet in this one.

---

*Created 2026-08-02. Revision 4 settles Q17/Q19/Q20, rewrites the 1-to-1 pairing question around the
streaming-vs-DAG model mismatch (§Q18, explained properly), and adds J13. Findings verified against
the code at the cited lines. No implementation yet.*
