# Phase 2.2: Loop-Body Addressing Analysis (Q8) 🔁🗺️

Made with 💖 by Ami-Chan! UwU ✨

[Back to Phase 2.2](Phase2-2-AdvancedFlowControl.md) | [All Phases](README.md)

---

## The Question 🙏

> **Q8:** How does the engine know which nodes form the "body" of a loop — by following the loop module's `loopBody` output port connection (**ports approach**), or by reading an explicit `RegionId` tag on each `NodeDefinition` (**regions approach**)?

This is a schema + engine architecture decision that ripples into:
- How `WorkflowDefinition` JSON looks for loop authors
- How `SubGraphExecutor` discovers the subgraph to execute each iteration
- How nested loops are expressed and validated
- How `BreakModule` / `ContinueModule` locate their owning loop
- How a future visual designer would group + highlight loop bodies

---

## Option A — Ports (Connection-Driven Body) 🔌

The loop module has a dedicated `loopBody` output port. The nodes reachable via that port connection are the loop body. The engine follows the connection graph from the `loopBody` port, collecting all nodes until it hits nodes with no further outgoing connections *within that scope* — those are the implicit exit points.

### Workflow JSON shape

```json
{
  "nodes": [
    { "id": "start",    "module": "builtin.start" },
    { "id": "forEach1", "module": "builtin.loop.foreach",
      "inputs": { "collection": "@variables.items" } },
    { "id": "process",  "module": "acme.processItem" },
    { "id": "log",      "module": "builtin.log" },
    { "id": "summary",  "module": "builtin.setVariable" }
  ],
  "connections": [
    { "from": "start",    "fromPort": "output",   "to": "forEach1", "toPort": "input" },
    { "from": "forEach1", "fromPort": "loopBody", "to": "process",  "toPort": "input" },
    { "from": "process",  "fromPort": "output",   "to": "log",      "toPort": "input" },
    { "from": "forEach1", "fromPort": "done",     "to": "summary",  "toPort": "input" }
  ]
}
```

### ASCII Diagram

```
  ┌─────────┐
  │  Start  │
  └────┬────┘
       │ output
       ▼
  ┌────────────────────────────────────────────────┐
  │  ForEach ("forEach1")                          │
  │                                                │
  │  ports:  loopBody ──────────────────────────── ┼──┐
  │          done ─────────────────────────────── ─┼──┼──→ [summary]
  └────────────────────────────────────────────────┘  │
                                                       │
          ┌────────────────────────────────────────────┘
          │  (engine follows loopBody connection
          │   to discover the body subgraph)
          ▼
     ┌──────────┐     ┌─────┐
     │ process  │────▶│ log │   ← terminal node (no further connections
     └──────────┘     └─────┘     back to forEach1) = implicit body exit
```

### Nested loops — ports approach

```
forEach1 ──loopBody──▶ forEach2 ──loopBody──▶ innerProcess
                   │            └──done──▶   innerSummary
                   └──done──▶ outerSummary
```

```
  ┌──────────────────────────────────────────────────────────┐
  │  ForEach ("forEach1")                                    │
  │                                                          │
  │  loopBody ────────────────────────────────────────────── ┼──┐
  │  done ──────────────────────────────────────────────── ──┼──┼──→ [outerSummary]
  └──────────────────────────────────────────────────────────┘  │
                                                                  │
  ┌───────────────────────────────────────────────────────────────┘
  │
  ▼
  ┌──────────────────────────────────────────────────────────┐
  │  ForEach ("forEach2")    ← body of forEach1              │
  │                                                          │
  │  loopBody ────────────────────────────────────────────── ┼──┐
  │  done ──────────────────────────────────────────────── ──┼──┼──→ [innerSummary]
  └──────────────────────────────────────────────────────────┘  │
                                                                  │
  ┌───────────────────────────────────────────────────────────────┘
  │
  ▼
  [innerProcess]   ← body of forEach2
```

### How `BreakModule` finds its loop — ports

The engine maintains a **loop context stack** per execution (2.2.0b). When `forEach1.loopBody` fires a subgraph, `forEach1`'s `LoopContext` is pushed. When `forEach2.loopBody` fires an inner subgraph, `forEach2`'s context is pushed on top. `BreakModule` pops/signals the **innermost** context without needing any explicit owner reference.

```
Execution frame:
  LoopContext stack: [ forEach1 ctx (bottom) | forEach2 ctx (top) ]
  BreakModule → signals forEach2 ctx.BreakRequested = true
                (forEach1 is unaffected)
```

### Validation at load time — ports

- Every `loopBody` connection must point to a node that exists in the definition ✅
- The subgraph reachable from `loopBody` must not contain a cycle back to the loop module itself (detect via DFS) ✅
- `BreakModule` / `ContinueModule` must appear inside a subgraph rooted at some loop module's `loopBody` port (traceable via connection graph ancestry) ✅

### Pros ✅
- **No new schema fields** — `NodeDefinition` needs zero changes; `RegionId` is never introduced
- **Natural sub-graph discovery**: `SubGraphExecutor` just receives the entry node from the `loopBody` connection and runs it — zero special casing
- **Composable with multi-port routing**: already decided in Q2; `loopBody` is just another port
- **Break/continue work via context stack** — no owner lookup in the graph structure
- **Visual designer story**: draw a line from `loopBody` port to the first body node — the group emerges naturally from tracing connections
- **Works today** — the connection model already carries port names

### Cons ⚠️
- **No explicit visual boundary** — a designer can't draw a "bounding box" around the loop body without inferring it from the graph; must traverse to render the group highlight
- **Entry must be a single node** — the `loopBody` connection points to *one* entry node; parallel entry into multiple body nodes requires a `ParallelModule` as the first body node (extra boilerplate)
- **Implicit exit** — the body "ends" when you run out of connected nodes; less obvious to authors where the iteration boundary is
- **Ambiguous with `done`** — if a body node connects both to a downstream body node *and* to `done`, the graph becomes harder to validate cleanly

---

## Option B — RegionId (Tag-Driven Body) 🏷️

Each `NodeDefinition` carries an optional `RegionId` string. A `Region` registry in `WorkflowDefinition` maps region IDs to their owning loop node. The engine collects all nodes with the matching `RegionId` to form the subgraph.

### Workflow JSON shape

```json
{
  "nodes": [
    { "id": "start",    "module": "builtin.start" },
    { "id": "forEach1", "module": "builtin.loop.foreach",
      "inputs": { "collection": "@variables.items",
                  "bodyRegion": "forEach1-body" } },
    { "id": "process",  "module": "acme.processItem",  "regionId": "forEach1-body" },
    { "id": "log",      "module": "builtin.log",       "regionId": "forEach1-body" },
    { "id": "summary",  "module": "builtin.setVariable" }
  ],
  "regions": [
    { "id": "forEach1-body", "ownerId": "forEach1", "type": "loopBody" }
  ],
  "connections": [
    { "from": "start",    "fromPort": "output", "to": "forEach1", "toPort": "input" },
    { "from": "process",  "fromPort": "output", "to": "log",      "toPort": "input" },
    { "from": "forEach1", "fromPort": "done",   "to": "summary",  "toPort": "input" }
  ]
}
```

### ASCII Diagram

```
  ┌─────────┐
  │  Start  │
  └────┬────┘
       │
       ▼
  ┌─────────────────────────────────┐
  │  ForEach ("forEach1")           │
  │  bodyRegion: "forEach1-body"    │
  │  done ──────────────────────── ─┼──→ [summary]
  └─────────────────────────────────┘

  Region "forEach1-body" (owned by forEach1):
  ╔══════════════════════════════════════════╗
  ║  ┌──────────┐       ┌─────┐             ║
  ║  │ process  │──────▶│ log │             ║
  ║  └──────────┘       └─────┘             ║
  ╚══════════════════════════════════════════╝
```

### Nested loops — regions approach

```json
{ "id": "forEach1", "module": "builtin.loop.foreach",
  "inputs": { "bodyRegion": "forEach1-body" } },
{ "id": "forEach2", "module": "builtin.loop.foreach",
  "inputs": { "bodyRegion": "forEach2-body" },
  "regionId": "forEach1-body" },
{ "id": "innerProcess", "module": "...", "regionId": "forEach2-body" }
```

```
  Region "forEach1-body":
  ╔════════════════════════════════════════════════════════════════╗
  ║  ┌──────────────────────────────────────────────────────────┐ ║
  ║  │  ForEach ("forEach2")    regionId: "forEach1-body"       │ ║
  ║  │  bodyRegion: "forEach2-body"                             │ ║
  ║  └──────────────────────────────────────────────────────────┘ ║
  ║                                                                ║
  ║  Region "forEach2-body":                                       ║
  ║  ╔══════════════════════════╗                                  ║
  ║  ║  [innerProcess]          ║                                  ║
  ║  ╚══════════════════════════╝                                  ║
  ╚════════════════════════════════════════════════════════════════╝
```

### How `BreakModule` finds its loop — regions

`BreakModule` needs to know the `RegionId` it lives in, then look up which loop owns that region:

```json
{ "id": "breakNode", "module": "builtin.break", "regionId": "forEach2-body" }
```

The engine resolves: `"forEach2-body"` → owned by `"forEach2"` → signal `forEach2`'s `LoopContext`.
This requires a `Region` lookup per break/continue (O(1) with a dictionary, but more schema surface to maintain).

### Validation at load time — regions

- Every `regionId` on a node must reference an existing `Region` ✅
- Every `Region.ownerId` must reference a loop module ✅
- No node may belong to two regions at the same level (overlapping regions are illegal) ✅
- `BreakModule`/`ContinueModule` must have a `regionId` that resolves to a loop region ✅
- Region nesting must form a valid tree (no cross-parent references) ⚠️ (harder to validate)

### Pros ✅
- **Explicit visual grouping** — the bounding box in a designer maps 1:1 to a `Region`; no graph traversal needed to render the group
- **Multi-entry body** — multiple body nodes can be tagged with the same `regionId` and serve as parallel entry points within the body (no forced `ParallelModule` wrapper)
- **Explicit exit** — the body is exactly the tagged set; nothing implicit about where it ends
- **Easier serialization diff** — adding/removing a node from the loop body is a 1-field change (`regionId`), not a connection change
- **Good fit for Phase 3 visual designer** (drag-and-drop into region box = set `regionId`)

### Cons ⚠️
- **New schema surface** — `NodeDefinition` gains `RegionId?`, `WorkflowDefinition` gains `Regions[]`, loop module schema gains `bodyRegion` input — more to validate, more to serialize
- **Leaky abstractions** — `bodyRegion` input on the loop module is a special-cased string ref, not a data value; modules become schema-aware of the region system
- **Connection model grows**  — the connection between `forEach1` → body nodes still needs to exist for the engine routing to work, *or* the engine must infer entry nodes differently (which fork? which entry?)
- **Two sources of truth** — connections describe execution order; regions describe ownership; they can disagree (node tagged in a region but not reachable from loop module via connections)
- **Late payoff** — the main benefit (visual bounding box) is a Phase 3+ UI concern; adds schema complexity now for a feature we don't need until later

---

## Side-by-Side Comparison 📋

| Dimension | Ports (Option A) | RegionId (Option B) |
|-----------|-----------------|---------------------|
| Schema changes to `NodeDefinition` | ❌ None | ✅ + `RegionId?` |
| New top-level `WorkflowDefinition` fields | ❌ None | ✅ + `Regions[]` |
| Body discovery | Follow `loopBody` port connection | Collect all nodes with matching `regionId` |
| Entry node(s) | Single (the node `loopBody` connects to) | Multiple possible (all body nodes with no incoming body-connection) |
| Exit definition | Implicit (body terminal nodes) | Implicit (body nodes with outgoing connection to `done` or outside region) |
| Nested loop expression | Natural (port chains) | Explicit (nested `regionId` ancestry) |
| `Break`/`Continue` owner lookup | Context stack (O(1), zero schema) | Region dictionary lookup (O(1), needs schema) |
| Load-time validation complexity | Low (DFS reachability) | Medium (region ownership tree + reachability) |
| Two-source-of-truth risk | ❌ None | ⚠️ Region + connection can drift |
| Visual designer bounding box | Inferred (graph traversal) | ✅ Direct (1:1 Region → box) |
| Drag-drop into loop body (Phase 3 UI) | Requires connecting a port | Requires setting `regionId` (simpler) |
| `SubGraphExecutor` bootstrap | Entry node from port connection | Entry node(s) inferred from region + connection graph |
| Backwards compat (Phase 1 workflows) | ✅ No change needed | ✅ `regionId` is optional — defaults to null |
| When to implement regions | Defer to Phase 3+ | Now (Phase 2.2) |

---

## Nested Loop Walkthrough — Both Options 🔁🔁

For a concrete nested loop: *"for each order, for each item in order, process item"*

### Option A JSON + execution trace

```json
{
  "nodes": [
    { "id": "forOrders", "module": "builtin.loop.foreach",
      "inputs": { "collection": "@variables.orders" } },
    { "id": "forItems",  "module": "builtin.loop.foreach",
      "inputs": { "collection": "@loop.item.items" } },
    { "id": "processItem", "module": "acme.processItem" },
    { "id": "orderSummary", "module": "builtin.setVariable" }
  ],
  "connections": [
    { "from": "forOrders",  "fromPort": "loopBody", "to": "forItems",     "toPort": "input" },
    { "from": "forItems",   "fromPort": "loopBody", "to": "processItem",  "toPort": "input" },
    { "from": "forOrders",  "fromPort": "done",     "to": "orderSummary", "toPort": "input" }
  ]
}
```

```
Execution trace for orders=[O1,O2], O1.items=[A,B]:

  forOrders iter 0 (O1):
    push LoopContext{ loopId: "forOrders", iter: 0, item: O1 }
    SubGraph ──▶ forItems
      forItems iter 0 (A):
        push LoopContext{ loopId: "forItems", iter: 0, item: A }
        SubGraph ──▶ processItem(A)
        pop → forItems ctx
      forItems iter 1 (B):
        push LoopContext{ loopId: "forItems", iter: 1, item: B }
        SubGraph ──▶ processItem(B)
        pop → forItems ctx
      forItems done → (no done connection → implicit completion)
    pop → forOrders ctx
  forOrders iter 1 (O2):
    ... (same pattern)
  forOrders done ──▶ orderSummary
```

### Option B JSON + execution trace

```json
{
  "nodes": [
    { "id": "forOrders",   "module": "builtin.loop.foreach",
      "inputs": { "collection": "@variables.orders", "bodyRegion": "forOrders-body" } },
    { "id": "forItems",    "module": "builtin.loop.foreach",
      "inputs": { "collection": "@loop.item.items",  "bodyRegion": "forItems-body" },
      "regionId": "forOrders-body" },
    { "id": "processItem", "module": "acme.processItem",
      "regionId": "forItems-body" },
    { "id": "orderSummary","module": "builtin.setVariable" }
  ],
  "regions": [
    { "id": "forOrders-body", "ownerId": "forOrders", "type": "loopBody" },
    { "id": "forItems-body",  "ownerId": "forItems",  "type": "loopBody",
      "parentRegionId": "forOrders-body" }
  ],
  "connections": [
    { "from": "forOrders", "fromPort": "done", "to": "orderSummary", "toPort": "input" }
  ]
}
```

> ⚠️ Notice: with regions, you still need connections *within* the body to express execution order between `forOrders` and `forItems`. Without a connection from `forOrders`'s `loopBody` port to `forItems`, the engine doesn't know what to run first in the body — it only knows the *set* of nodes, not the *order*. So **connections are still required inside the region**, creating two coexisting systems~ 🤔

---

## Visual Designer Implications 🎨

### Option A — Designer renders loop body by graph traversal

```
 ┌──────────────────────────────────────────────────────────┐
 │  WORKFLOW CANVAS                                         │
 │                                                          │
 │  [Start] ──▶ [ForEach] ────────────────────────────────▶ [summary]
 │               │ loopBody                  done ↗         │
 │               │                                          │
 │               ▼                                          │
 │          ╔════════════════════════════════╗              │
 │          ║  (auto-inferred body region)  ║              │
 │          ║  [process] ──▶ [log]          ║              │
 │          ╚════════════════════════════════╝              │
 │          (designer renders dashed box by traversing      │
 │           loopBody connections — requires layout engine) │
 └──────────────────────────────────────────────────────────┘
```

The designer must:
1. Detect nodes reachable from `loopBody` port
2. Compute a bounding box for that node group
3. Refresh that box whenever connections change

### Option B — Designer renders loop body directly from Region

```
 ┌──────────────────────────────────────────────────────────┐
 │  WORKFLOW CANVAS                                         │
 │                                                          │
 │  [Start] ──▶ [ForEach]                      [summary]   │
 │               │ done ──────────────────────────▶        │
 │                                                          │
 │          ╔════════════════════════════════╗              │
 │          ║  Region: "forOrders-body"      ║              │
 │          ║  [process] ──▶ [log]           ║  ← Drag &   │
 │          ║                                ║    drop to  │
 │          ╚════════════════════════════════╝    add node  │
 └──────────────────────────────────────────────────────────┘
```

The designer can:
1. Render the bounding box from `Region.id` without any traversal
2. Drag a node *into* the box → set `node.regionId = "forOrders-body"`
3. Drag a node *out* → clear `regionId`

---

## `BreakModule` / `ContinueModule` Detail 🛑➡️

A key constraint: `BreakModule` must be able to signal the **correct** owning loop when nested.

### Ports — context stack resolution

```
  Execution stack (implicit, maintained by engine):
  ┌──────────────────────────────────┐
  │ LoopContext(forOrders, iter=1)   │  ← bottom
  │ LoopContext(forItems,  iter=0)   │  ← top (innermost)
  └──────────────────────────────────┘

  BreakModule fires:
    → ctx = stack.Peek()  // = forItems context
    → ctx.BreakRequested = true
    → SubGraphExecutor for forItems stops after current iteration
    → forOrders continues its outer iteration
```

No schema information needed. `BreakModule` is a pure engine primitive~ ✅

### RegionId — region walk resolution

```
  BreakModule node:
  { "id": "breakNode", "regionId": "forItems-body" }

  Engine resolves:
    1. breakNode.regionId = "forItems-body"
    2. regions["forItems-body"].ownerId = "forItems"
    3. Find active LoopContext where loopId == "forItems"
    4. Signal break on that context

  ⚠️ What if breakNode.regionId is null?
  ⚠️ What if the author places BreakModule outside any loop region? (Must validate at load time)
  ⚠️ What if a BreakModule is in a region owned by a TryCatch, not a loop? (Region type must be checked)
```

---

## Hybrid Option 💡

A **middle path**: use Port connections as the v1 mechanism (no schema changes, simpler engine) but also add an *optional* `regionId` hint for visual grouping purposes only. The engine **ignores** `regionId` entirely — SubGraphExecutor still uses port connections. The designer uses `regionId` purely for rendering the bounding box.

```json
{
  "nodes": [
    { "id": "process", "module": "acme.processItem",
      "regionId": "forEach1-body"  // ← HINT ONLY, engine doesn't use this
    }
  ]
}
```

This gives:
- ✅ Engine stays simple (port-driven, no region awareness)
- ✅ Designer gets an explicit box to render without graph traversal
- ✅ Author tooling can populate `regionId` as a derived hint (auto-set when adding a `loopBody` connection)
- ⚠️ Two coexisting representations can still drift (but the engine always trusts ports, so drift only affects the visual, not execution)

---

## Decision Summary 🎀

| Option | When it wins | Risk |
|--------|-------------|------|
| **A — Ports only** | Engine simplicity; no UI work at all | Visual grouping must be inferred; harder for Phase 3 designer |
| **B — Regions only** | Visual-first designer from day one | Schema complexity + two sources of truth |
| **Hybrid — Ports + optional hint** | ✅ **Selected for Phase 2.2** — simple engine + cheap visual grouping + forward-compatible schema | `regionId` can drift from ports (mitigated: engine ignores it; only UI reads it; load-time warns on drift) |

### ✅ Decision: Hybrid — shipped in Phase 2.2

**Execution:** Port-driven (unchanged from Option A) — `SubGraphExecutor` receives the entry node from the `loopBody` connection. The engine **never reads `regionId`**.

**Schema:** `NodeDefinition` gains a single optional field:
```csharp
/// <summary>
/// Optional visual grouping hint for designer tooling~ 🗺️
/// CopilotNote: The engine ignores this field entirely.
/// Populated by author tooling / workflow serializer when a loopBody connection is drawn.
/// Phase 3 visual designer reads this to render loop-body bounding boxes without graph traversal.
/// </summary>
public string? RegionId { get; init; }
```

**Load-time behaviour:**
- ✅ Engine routes via port connections as always
- ⚠️ Emit a structured **warning** (not validation error) if a node's `regionId` references a loop node whose `loopBody` port does not reach that node — indicates a designer drift that doesn't affect execution but should be fixed by the tooling

**Why not defer `regionId` to Phase 3?**
Adding it now costs one nullable field on `NodeDefinition` and one warning check. Deferring it means Phase 3 has a breaking schema change (existing workflows lack `regionId`). The cost of doing it now is ~1 day; the cost of doing it later is a migration + potential author confusion~ 🌷

### Ami's recommendation 🌸

~~**Ship Option A (ports) for Phase 2.2** — zero schema changes...~~

✅ **Ship Hybrid in Phase 2.2** — add `RegionId?` to `NodeDefinition` now (one field, engine ignores it), keep execution purely port-driven, and let author tooling auto-populate `regionId` as a derived hint from the `loopBody` connection. Phase 3 visual designer gets the bounding box feature for free without a schema migration~ 🧠

The key insight remains: **regions are a rendering concern, not an execution concern**. The Hybrid honours that boundary by keeping `regionId` entirely out of the execution path while making it available in the data model~ ✨

---

## Open Sub-Questions 🙏

- [x] **LB1**: Should `regionId` be added as an optional/ignored field in Phase 2.2 schema to future-proof the JSON format, even if the engine doesn't read it until Phase 3? **→ Yes, adding in 2.2 as part of Hybrid decision. One nullable field, zero engine impact, avoids Phase 3 migration.**
- [ ] **LB2**: If a loop has **multiple entry connections** to the body (e.g. `loopBody → nodeA` and `loopBody → nodeB` for a parallel-entry body), should that be a validation error or supported via a parallel split? *(v1 recommendation: validation error — require a `ParallelModule` as the single body entry)*
- [ ] **LB3**: How does the `done` port interact with nodes that have no outgoing connections *within* the body? Should terminal body nodes implicitly trigger `done`, or must authors explicitly connect them back to a `merge` port on the loop module?
- [ ] **LB4**: For the visual designer, should the bounding box auto-expand when a node is connected downstream of a body node (user adds to body via connection), or require an explicit "add to body" gesture?

---

> 💖 **Ami's Loop-Body Tips:**
> - A `loopBody` port connection is the **minimum** schema change — the whole subgraph machinery works from just that one connection without touching `NodeDefinition`~ ✨
> - Lock down `BreakModule`/`ContinueModule` at **load time** — if a break node isn't reachable from any loop's `loopBody` port, reject the workflow definition immediately with a helpful error message, nya~ 🛡️
> - When Phase 3 designer arrives, auto-populate `regionId` from the connection graph — don't ask authors to set it manually. It should be an implementation detail of the designer tool, not of the workflow author~ 🎨 UwU



