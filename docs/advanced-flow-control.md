# 🔀 Advanced Flow Control — Authoring Guide

*Made with 💖 by Ami-Chan! UwU* ✨

This guide covers every control-flow primitive shipped in **Phase 2.2** of DotFlow: conditional branching, loops, parallelism, fan-out/fan-in, and error boundaries. All examples use built-in modules only — **no external services required**~ 🌸

> **Audience:** Workflow authors who want to compose advanced logic (branching, iteration, concurrency, error recovery) declaratively. Module developers should also read [`module-author-guide.md`](./module-author-guide.md)~ 💖

---

## Table of Contents

1. [Core Concepts](#-core-concepts)
2. [Conditional Branching](#-conditional-branching)
   - [`builtin.condition`](#builtincondition---ifelse)
   - [`builtin.switch`](#builtinswitch---multi-way)
3. [Loops](#-loops)
   - [`builtin.loop.foreach`](#builtinloopforeach---iterate-a-collection)
   - [`builtin.loop.while`](#builtinloopwhile---iterate-while-condition)
   - [`builtin.break` / `builtin.continue`](#builtinbreak--builtincontinue)
4. [Parallelism & Fan-Shaped Patterns](#-parallelism--fan-shaped-patterns)
   - [`builtin.parallel`](#builtinparallel---static-n-branch-fan-out)
   - [`builtin.fanout`](#builtinfanout---per-item-fan-out)
   - [`builtin.fanin`](#builtinfanin---barrier-aggregation)
5. [Error Handling](#-error-handling)
   - [`builtin.trycatch`](#builtintrycatch---error-boundary)
   - [`builtin.throw`](#builtinthrow---structured-failure)
6. [Expression Cheatsheet (Jint / JavaScript)](#-expression-cheatsheet-jint--javascript)
7. [Common Patterns & Recipes](#-common-patterns--recipes)
8. [Further Reading](#-further-reading)

---

## 🧠 Core Concepts

Phase 2.2 turned DotFlow from a **linear DAG runner** into a **proper control-flow runtime**. Five engine primitives underpin every module in this guide~ 🌟

### 1. Port-Aware Routing 🎯

A node's output ports are **selectively activated**. When a module returns `ActivePorts = ["true"]`, the engine fires *only* connections whose `sourcePortName` is `"true"` — other branches are **skipped** (not failed). Backwards-compatible default: when no `ActivePorts` are set, *all* outgoing connections fire.

```jsonc
// Example: only the "true" branch runs
{
  "sourceNodeId": "myCondition",
  "sourcePortName": "true",
  "targetNodeId": "approveBranch",
  "targetPortName": "input"
}
```

### 2. Sub-Graphs 🌿

Some modules (loops, try/catch, parallel) need to execute a **contained region** of the workflow on demand. The engine spawns a `SubGraphExecutor` actor that runs a slice of the same workflow (entry nodes + reachable body nodes) under a parent execution. Sub-graph node executions are persisted under the parent's execution ID with `subGraphId` metadata~ 💾

### 3. Loop Scope 🔁

Each loop iteration gets a fresh **variable subscope** (`loop:{loopId}:{iter}`) so loop-local variables don't leak across iterations. The current `item` and `index` are injected into the body sub-graph's inputs~ 🎀

### 4. Error Boundaries 🛡️

`builtin.trycatch` is an **engine-managed error containment zone**, not Akka supervision. A failure inside the `try` sub-graph is caught + routed to the `catch` branch as a structured `WorkflowError`. The parent workflow stays healthy~ 💖

### 5. Hierarchical Cancellation 🛑

Every sub-graph receives a `CancellationToken` linked to its parent's CTS. Cancel the parent → siblings observe the token cooperatively and wind down. **No hard-abort of in-flight nodes** — modules are expected to honour `CancellationToken`~ ✨

---

## 🔀 Conditional Branching

### `builtin.condition` — If/Else

Routes execution down a `true` or `false` branch based on a boolean, numeric, string-literal, or expression value~ 🎯

| Property / Input | Type | Required | Notes |
|---|---|---|---|
| `condition` (input) | `bool` / `string` / `number` | optional | Input port wins over property |
| `condition` (property) | `string` | optional | Static fallback (expression or literal) |

| Output Port | Activated when |
|---|---|
| `true` | Condition evaluates truthy |
| `false` | Condition evaluates falsy |
| `result` | *(data output)* Always present — carries the evaluated bool for diagnostics |

**Truthy/falsy rules:**
- `bool` → used directly
- `int` / `number` → `0` is false, everything else true
- `string` (case-insensitive) → `"true"`, `"1"`, `"yes"`, `"on"` are true; `"false"`, `"0"`, `"no"`, `"off"` are false
- Any other string → delegated to `IExpressionEvaluator` (Jint by default) — see [Expression Cheatsheet](#-expression-cheatsheet-jint--javascript)

```jsonc
{
  "id": "is_approved",
  "moduleId": "builtin.condition",
  "properties": { "condition": "score >= 600" }
}
```

> **CopilotNote:** If you provide an expression but no `IExpressionEvaluator` is registered in DI, the node will fail with a clear error message. `Workflow.Api/Program.cs` already wires Jint as default~ 🧮

---

### `builtin.switch` — Multi-Way

First-match-wins routing across N dynamic ports~ 🔢

| Property | Type | Required | Notes |
|---|---|---|---|
| `cases` | JSON array `[{match, port}]` | **yes** | Order matters — first match wins |
| `value` | `string` | optional | Static value to match (input port overrides) |
| `defaultPort` | `string` | optional | Activated if nothing matches; otherwise node fails |
| `caseSensitive` | `bool` | optional, default `false` | String comparison mode |

| Output Port | Activated when |
|---|---|
| *(dynamic — each `port` in `cases`)* | First matching case |
| *(dynamic — `defaultPort`)* | Nothing matched |

```jsonc
{
  "id": "route_by_status",
  "moduleId": "builtin.switch",
  "properties": {
    "cases": "[{\"match\":\"pending\",\"port\":\"to_review\"},{\"match\":\"approved\",\"port\":\"to_ship\"}]",
    "defaultPort": "to_archive"
  }
}
```

> **Note:** `Schema.Outputs` is intentionally empty (dynamic ports). `ValidateConnectionPorts` skips schema validation for this module — author-defined port names are trusted~ 🎗️

---

## 🔁 Loops

### `builtin.loop.foreach` — Iterate a Collection

Iterates over `collection`, running a downstream sub-graph (the **loop body**) once per item. The body sub-graph receives `item` and `index` as inputs on each iteration~ 🌸

| Property / Input | Type | Required | Notes |
|---|---|---|---|
| `collection` | `IEnumerable` / `JsonElement[]` / JSON string | **yes** | Auto-coerced; single value wrapped to `[value]` |
| `maxIterations` | `int` | optional, default `1000` | Safety bound — exceeds = fail |
| `continueOnError` | `bool` | optional, default `false` | Collect errors and continue vs short-circuit |

| Output Port | Behaviour |
|---|---|
| `loopBody` | Activated once per iteration (sub-graph entry) |
| `done` | Activated after all iterations finish |
| `results`, `count`, `errors` | *(data outputs)* Aggregated after loop completes |

```jsonc
{
  "id": "process_items",
  "moduleId": "builtin.loop.foreach",
  "properties": {
    "collection": "[1, 2, 3, 4, 5]",
    "maxIterations": 100,
    "continueOnError": true
  }
}
```

> **CopilotNote:** The `LoopExecutorActor` injects `item` and `index` into every body sub-graph's input map. Body nodes that read `ctx.Inputs["item"]` see the current element~ 🎯

---

### `builtin.loop.while` — Iterate While Condition

Like `foreach`, but iterates while a condition evaluates truthy (re-evaluated each iteration via the same `IExpressionEvaluator` as `builtin.condition`)~ 🌀

| Property / Input | Type | Required | Notes |
|---|---|---|---|
| `condition` | `bool` / `string` (expression) | **yes** | Re-evaluated before each iteration |
| `maxIterations` | `int` | optional, default `1000` | Hard cap to prevent runaway loops |
| `continueOnError` | `bool` | optional, default `false` | Same as `foreach` |

| Output Port | Behaviour |
|---|---|
| `loopBody` | Activated per iteration (while condition holds) |
| `done` | Activated once condition is false |

> **Optimization:** if the condition is false from the start, no `LoopExecutorActor` is spawned — the module short-circuits and fires `done` directly~ ⚡

---

### `builtin.break` / `builtin.continue`

Sentinel modules placed **inside a loop body**. When executed, they signal the surrounding loop:

| Module | Effect |
|---|---|
| `builtin.break` | Stops the loop *after the current iteration* — fires `done` |
| `builtin.continue` | Skips the rest of the current iteration body, advances to the next |

They emit special sentinel keys (`__loop_break__: true` / `__loop_continue__: true`) in their outputs, which the `SubGraphExecutor` detects and reports back to the `LoopExecutorActor`~ 🎀

```jsonc
// Inside a foreach body, break when an error condition is hit:
// Condition(item < 0) ─true→ break_node ─→ (next iteration interrupted)
{ "id": "break_node", "moduleId": "builtin.break" }
```

> **Caveat:** Load-time validation that `break`/`continue` are actually *inside* a loop region is deferred to Phase 3 (requires loop-scope inference). For now, misuse fails at runtime with a clear error~ 💔

---

## ⚡ Parallelism & Fan-Shaped Patterns

### `builtin.parallel` — Static N-Branch Fan-Out

Fans out execution to N concurrent **named** branches; each branch is its own sub-graph. Waits for all to complete (or first to complete, with `waitForAll: false`)~ 🌐

| Property | Type | Required | Notes |
|---|---|---|---|
| `branches` | JSON array of port names | optional | e.g. `["fetch_user", "fetch_orders"]` |
| `branchCount` | `int` | optional, default `2` | Fallback — auto-generates `branch1..branchN` |
| `maxDegreeOfParallelism` | `int` | optional | Bounded concurrency (`0` or `<0` = unbounded) |
| `failFast` | `bool` | optional, default `true` | First failure cancels siblings cooperatively |
| `waitForAll` | `bool` | optional, default `true` | When `false`, first **success** wins and cancels siblings |

| Output Port | Behaviour |
|---|---|
| *(dynamic — each branch port)* | Activated concurrently at start |
| `done` | Fires after all branches complete (or first success if `waitForAll=false`) |

```jsonc
{
  "id": "parallel_fetch",
  "moduleId": "builtin.parallel",
  "properties": {
    "branches": "[\"notify\", \"persist\"]",
    "maxDegreeOfParallelism": 2,
    "failFast": true
  }
}
```

> **CopilotNote:** Branch scopes (sets of body nodes reachable from each branch port) **must not overlap**. Convergent diamonds (`A→C, B→C`) should use [`builtin.fanin`](#builtinfanin---barrier-aggregation) as the rendezvous, not a node shared across two branches~ 🎀

---

### `builtin.fanout` — Per-Item Fan-Out

Like `builtin.loop.foreach` but **parallel** — spawns one sub-graph per item via `ParallelExecutionCoordinator`~ 🌟

| Property / Input | Type | Required | Notes |
|---|---|---|---|
| `items` | `IEnumerable` / `JsonElement[]` / JSON string | **yes** | Each item = one parallel branch |
| `maxDegreeOfParallelism` | `int` | optional | Throttle concurrency |
| `failFast` | `bool` | optional, default `true` | Cancel siblings on first failure |

| Output Port | Behaviour |
|---|---|
| `branch` | Activated per item (sub-graph payload includes `item` + `index`) |
| `done` | Fires after all items processed |
| `results`, `count` | *(data outputs)* Aggregated branch results |

```jsonc
{
  "id": "process_users_in_parallel",
  "moduleId": "builtin.fanout",
  "properties": {
    "items": "[\"alice\", \"bob\", \"carol\"]",
    "maxDegreeOfParallelism": 5
  }
}
```

---

### `builtin.fanin` — Barrier Aggregation

The convergence point downstream from a `parallel` or `fanout`. Holds until *all declared upstream branches* complete, then aggregates per `mode`~ 🪄

| Property | Type | Required | Default | Notes |
|---|---|---|---|---|
| `mode` | `string` enum | optional | `"Concat"` | One of: `Concat`, `Merge`, `Named`, `First`, `Last` |
| `meta` | `string` enum | optional | `"separate"` | Where `count` goes: `separate` (own output port), `embedded` (`result = { value, count }` — one item), `hidden` (result only) |

**Modes:**
- `Concat` — collects payloads into an array in branch-completion order
- `Merge` — last-writer-wins shallow merge across branches
- `Named` — one object keyed by each branch's **source port name** — e.g. a node with outputs `foo, bar, baz` fanned in yields `{ "foo": …, "bar": …, "baz": … }`. Port-name collisions (same port from different nodes) fall back to `nodeId.port` keys
- `First` — only the first branch's payload
- `Last` — only the last branch's payload

| Output | Description |
|---|---|
| `result` | Aggregated payload (shape depends on `mode`) |
| `count` | Number of incoming branches |
| `done` | Activation port for downstream |

```jsonc
{ "id": "aggregate", "moduleId": "builtin.fanin", "properties": { "mode": "Concat" } }
```

> **Note:** For the base case (a DAG diamond), `WorkflowExecutor`'s natural "fire when all predecessors terminal" behaviour is enough — no extra engine plumbing needed. The explicit "all declared upstream connections terminal" predicate for harder cases is deferred~ 🌸

---

## 🛡️ Error Handling

### `builtin.trycatch` — Error Boundary

The structural backbone of recoverable workflows. Spawns a `TryCatchExecutorActor` that runs `try` → optionally `catch` → optionally `finally` → fires `done`~ 🌷

| Property / Input | Type | Required | Notes |
|---|---|---|---|
| `rethrow` | `bool` | optional, default `false` | If `true`, re-emit error after `finally` |
| `catchTypes` | JSON array or comma-separated string of error type names | optional | Empty = catch-all |

| Output Port | Activated |
|---|---|
| `try` | At start (entry into the try-body sub-graph) |
| `catch` | On `try` failure that matches `catchTypes` (payload: `WorkflowError` injected as `error` input) |
| `finally` | After `try` (success) or `catch` (recovery) — *always* runs if present |
| `done` | After the full sequence completes (and if `rethrow` is false, or no error occurred) |

```jsonc
{
  "id": "safe_zone",
  "moduleId": "builtin.trycatch",
  "properties": { "rethrow": false, "catchTypes": [] }
}
```

**Wiring example** (from `examples/definitions/flow-control-demo.json`):
```
trycatch.try     ─→ condition
trycatch.catch   ─→ catch_handler   (logs WorkflowError)
trycatch.finally ─→ finally_audit   (cleanup, always runs)
trycatch.done    ─→ next_step
```

> **WorkflowError shape** (injected as `error` input to `catch` sub-graph):
> ```jsonc
> {
>   "errorType": "BelowThreshold",
>   "message":   "Item is below threshold",
>   "nodeId":    "throw_low",
>   "data":      { "reason": "below_threshold" },
>   "occurredAt": "2026-05-19T20:00:00Z",
>   "stackTrace": "..."
> }
> ```

---

### `builtin.throw` — Structured Failure

Throws a `WorkflowUserException`. When caught by an enclosing `builtin.trycatch`, it surfaces as a structured `WorkflowError`~ 💥

| Property / Input | Type | Required |
|---|---|---|
| `errorType` | `string` | **yes** | Classification tag (e.g. `"ValidationError"`) |
| `message` | `string` | **yes** | Human-readable description |
| `data` | `object` | optional | Structured payload attached to the error |

```jsonc
{
  "id": "throw_low",
  "moduleId": "builtin.throw",
  "properties": {
    "errorType": "BelowThreshold",
    "message":   "Item is at or below threshold — skipped",
    "data":      { "reason": "below_threshold" }
  }
}
```

> **Outside of a `trycatch`?** The thrown exception propagates normally → `WorkflowFailed` for the whole workflow. Throw is most useful **inside** a try-body~ 💖

---

## 🧮 Expression Cheatsheet (Jint / JavaScript)

`builtin.condition`, `builtin.loop.while`, and any future expression-driven modules delegate to `IExpressionEvaluator`. The default implementation is **Jint** (JavaScript ES2020). DynamicExpresso (C#-syntax) is registered as an opt-in fallback under the key `"csharp"`~ 🎀

### Variable Scope

Whatever's in the node's `ctx.Inputs` and workflow `Variables` is exposed to the expression as global identifiers. Inside a loop body, `item` and `index` are also available~ 🌟

### Operator Coverage

| Category | Operators |
|---|---|
| **Comparison** | `>`, `<`, `>=`, `<=`, `===`, `!==`, `==`, `!=` |
| **Logical** | `&&`, `||`, `!`, `??` (null-coalescing) |
| **Arithmetic** | `+`, `-`, `*`, `/`, `%`, `**` |
| **Ternary** | `cond ? a : b` |
| **Optional chaining** | `obj?.prop?.subprop` |
| **Array transforms** | `.map(x => x*2)`, `.filter(x => x>0)`, `.reduce(...)`, `.some()`, `.every()` |
| **String** | `.toLowerCase()`, `.includes()`, template literals (\`hi ${name}\`) |

### Examples

```js
// Boolean comparison
score >= 600

// Null-safe chain
user?.address?.country === "JP"

// Array transform
items.filter(x => x.priority > 5).length > 0

// Ternary
status === "active" ? 1 : 0
```

### Safety Limits

| Limit | Value | Notes |
|---|---|---|
| Memory | 4 MB | Per evaluation |
| Timeout | 250 ms | Plus your `CancellationToken` (primary signal) |
| Recursion depth | 64 | Stack guard |
| Strict mode | enabled | Missing variables throw `ExpressionRuntimeException` |
| CLR access | **forbidden** | No `System.IO`, no reflection, no `ProcessStartInfo`, no leakage |

> **CopilotNote:** Each evaluation creates a fresh Jint `Engine` via `Task.Run` — engine instances are not thread-safe, so per-call isolation guarantees concurrency safety with zero `ExpandoObject` leakage~ ✨

---

## 🌟 Common Patterns & Recipes

### 1. Validate-or-Fail Gate

> "If invalid, throw a structured error and stop."

```
inputs → Condition(isValid) ─true→ continue...
                            ─false→ Throw("ValidationError")
```

### 2. Recoverable Per-Item Processing

> "Process a batch — skip items that fail, keep going."

```
ForEach(items) [continueOnError: true]
  └─ loopBody → TryCatch
                  ├─ try     → process_item
                  ├─ catch   → Log("skipped: {{error.message}}")
                  ├─ finally → audit_log
                  └─ done    → (next iteration)
```

*(See [`examples/definitions/flow-control-demo.json`](../examples/definitions/flow-control-demo.json) for the full wiring~ 🌸)*

### 3. Parallel Notify + Persist with Barrier

> "Fan out side-effects, then converge."

```
trigger → Parallel
            ├─ notify  → email_module
            ├─ persist → setvariable
            └─ webhook → external_call
trigger → FanIn(mode: "Concat") → continue_after_barrier
```

### 4. Retry-Like Recovery via TryCatch + Loop

> "Retry an operation up to N times."

```
foreach.collection: [1, 2, 3]   // 3 attempts
  loopBody → TryCatch
               ├─ try   → flaky_operation ─→ break (success)
               └─ catch → Log("attempt failed")
foreach.done → (if last_processed unset → fail)
```

### 5. Switch-Based Workflow Router

> "Route a request to one of many handlers."

```
inputs → Switch(value: status, cases: [
                  {match:"new",      port:"intake"},
                  {match:"approved", port:"ship"},
                  {match:"refunded", port:"reverse"}
                ], defaultPort: "fallback")
```

### 6. Cancellation-Friendly Long Loop

> "Make sure cancellation actually stops the loop."

Just use `builtin.loop.foreach` or `while` — the engine threads a linked `CancellationToken` through every body sub-graph automatically. Your custom modules should honour the token in their `ExecuteAsync`~ 🛑

---

## 📚 Further Reading

| Topic | Reference |
|---|---|
| Phase 2.2 plan & progress | [`phases/Phase2-2-AdvancedFlowControl.md`](../phases/Phase2-2-AdvancedFlowControl.md) |
| Expression engine analysis | [`phases/Phase2-2-ExpressionEngine-Analysis.md`](../phases/Phase2-2-ExpressionEngine-Analysis.md) |
| Loop-body addressing (Q8) | [`phases/Phase2-2-LoopBodyAddressing.md`](../phases/Phase2-2-LoopBodyAddressing.md) |
| End-to-end demo workflow | [`examples/definitions/flow-control-demo.json`](../examples/definitions/flow-control-demo.json) |
| Module authoring | [`docs/module-author-guide.md`](./module-author-guide.md) |

---

## ✨ Quick Module Index

| Category | Module ID | Purpose |
|---|---|---|
| Conditional | `builtin.condition` | If/else routing |
| Conditional | `builtin.switch` | Multi-way routing |
| Loops | `builtin.loop.foreach` | Iterate collection |
| Loops | `builtin.loop.while` | Iterate while condition |
| Loops | `builtin.break` | Exit current loop |
| Loops | `builtin.continue` | Skip to next iteration |
| Parallelism | `builtin.parallel` | Static N-branch fan-out |
| Parallelism | `builtin.fanout` | Per-item fan-out |
| Parallelism | `builtin.fanin` | Barrier aggregation |
| Error handling | `builtin.trycatch` | Error boundary |
| Error handling | `builtin.throw` | Structured failure |
| Utilities | `builtin.log` | Structured logging |
| Utilities | `builtin.setvariable` / `builtin.getvariable` | Variable I/O |
| Utilities | `builtin.delay` | Pause execution |
| Utilities | `builtin.passthrough` | Identity / pipeline glue |

> 💖 **Ami's tip:** Build complex flows from these primitives bottom-up. If you find yourself wanting a new control-flow module, check whether composition (e.g. `trycatch` inside `foreach`) does the job first — most useful patterns are already expressible~ UwU 🎀

