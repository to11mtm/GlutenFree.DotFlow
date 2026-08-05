# 01 — DotFlow Architecture from a Composition Standpoint 🌊

> Companion to [`00-PLAN.md`](00-PLAN.md). Explains how workflows are *composed* in
> GlutenFree.DotFlow, as the baseline for the SnapLogic comparison in
> [`02-snaplogic-comparison.md`](02-snaplogic-comparison.md).

## 1. The composition unit: `WorkflowDefinition`

A workflow is an immutable record (`Workflow.Core/Models/WorkflowDefinition.cs`):

| Field | Meaning |
| --- | --- |
| `Nodes : Arr<NodeDefinition>` | the vertices — each an *instance of a module* |
| `Connections : Arr<ConnectionDefinition>` | the edges — explicit port-to-port data links |
| `Variables : HashMap<string, VariableDefinition>` | declared shared state (typed, seedable, secret-flaggable) |
| `Trigger : TriggerDefinition?` | how a run starts: `Manual`, `Scheduled` (cron), `Webhook`, `Event` |
| `ErrorHandling : ErrorHandling?` | workflow-wide default error behavior |
| `Version`, `Tags`, timestamps | metadata |

The definition is a pure blueprint — no runtime state. Serialized as JSON (LanguageExt
immutable collections give structural equality, so definitions diff & compare cleanly).

## 2. Nodes: module instances with authored configuration

`NodeDefinition` (`Workflow.Core/Models/NodeDefinition.cs`):

- `ModuleId` — which module the node instantiates (e.g. `builtin.http.request`).
- `Properties : HashMap<string, JsonElement>` — the *authored configuration* ("knobs on top").
- Per-node `ErrorHandling`, `Timeout` (ms), `RetryPolicy` — resilience is declared **per node**,
  overriding the workflow default.
- `Position`, `RegionId`, `Metadata` — designer-only hints; the engine ignores them.

**Ports vs properties** is a core distinction (`ModuleSchema.cs`): *ports* carry runtime data
between nodes; *properties* are configuration the author typed. Each module publishes a
`ModuleSchema` — `Inputs`/`Outputs` as `PortDefinition`s (name, .NET `DataType`, required,
default) and `ModulePropertyDefinition`s with validation rules. The designer renders its
editors and the engine binds/validates from the *same* schema.

## 3. Connections: explicit, named, conditional edges

`ConnectionDefinition` = `(SourceNodeId, SourcePortName, TargetNodeId, TargetPortName,
Condition?, Priority)`.

- **Nothing flows implicitly** — data moves only where an edge exists.
- **Named ports on both ends** let one node expose multiple semantic outputs
  (`condition.true`/`false`, `trycatch.try`/`catch`/`done`, `foreach.loopBody`…).
- An optional `Condition` expression gates the edge at runtime; `Priority` orders fan-out.

## 4. Data flow semantics: one run, one graph traversal

A trigger produces **one execution** that flows through the graph once (per-run batch, not a
document stream). The Akka.NET engine (`Workflow.Engine`) spawns a `WorkflowExecutor` per run;
node outputs are captured per node/port and downstream inputs are bound from them.

Inputs are referenced with a template syntax (docs/variables.md):

```text
{{input}}                this node's own incoming value (reserved root)
{{input.orderId}}        a path into it
{{nodeId.portName}}      any upstream node's output, explicitly
{{Variable.apiBaseUrl}}  declared variables (global / workflow / execution scope)
{{Variable.count > 5}}   sandboxed-JavaScript expressions
```

Key rules with architectural weight:

- **Unresolvable references fail the node** — never silently pass through as text.
- **Only opted-in fields expand** (`SupportsTemplates` on the schema). Input ports default to
  *no expansion* as a security boundary: upstream data containing `{{Variable.apiKey}}` must not
  read your variables. SQL text never expands; SQL *parameter values* do, then bind as parameters.

## 5. Variables: three scopes, layered precedence

(docs/variables.md) Global → Workflow → declared initials (seed-only or always-reset) → run
inputs, later layers winning. All writes are versioned with history. `IsSecret` marks
credentials for redaction. This is DotFlow's stand-in for both SnapLogic *pipeline parameters*
and (partially) *Accounts*.

## 6. Control flow is first-class nodes

Where many engines bury control flow in the runtime, DotFlow expresses it as ordinary modules
wired through ports (`Workflow.Modules/Builtin/Flow/`):

| Construct | Module(s) | Mechanism |
| --- | --- | --- |
| Branch | `builtin.condition`, `builtin.switch` | route to `true`/`false` / case ports |
| Loops | `builtin.loop.foreach`, `builtin.loop.while`, `builtin.break`, `builtin.continue` | `LoopRequest` → dedicated loop executor actor runs the `loopBody` sub-graph per iteration |
| Parallel | `builtin.parallel`, `builtin.fanout`, `builtin.fanin`, `builtin.partition` | coordinator actor runs branches concurrently, fan-in aggregates (incl. `named` mode) |
| Error boundary | `builtin.trycatch`, `builtin.throw` | `try`/`catch`/`finally`/`done` ports; catch scoped to a sub-graph |
| Transactions | `builtin.database.transaction` | transaction executor actor brackets a sub-graph |
| Structure | `builtin.start`, `builtin.end`, `builtin.passthrough`, `builtin.delay` | |

Each construct spawns its own child actor (`LoopExecutorActor`, parallel coordinator,
`TryCatchExecutorActor`) giving **per-construct supervision**: independent timeouts,
hierarchical error propagation, cascading cancellation.

## 7. The module library (built-ins)

| Area | Module IDs |
| --- | --- |
| HTTP | `builtin.http.request`, `builtin.http.webhook` |
| Transforms | `builtin.transform.map`, `.join`, `.aggregate`, `.query`, `.jsonquery`, `.xmlquery`, `.json`, `.string`, `.validate`, `builtin.split` |
| Database | `builtin.database.query`, `.execute`, `.bulkinsert`, `.transaction`, `.linq` |
| Files | `builtin.file.read`/`.write`, `.csv.*`, `.json.*`, `.xml.*`, `.compress`/`.decompress` |
| Cloud | `builtin.cloud.s3`, `builtin.cloud.azureblob` |
| Scripting | `builtin.script`, `builtin.transform.script` |
| Variables/misc | `builtin.setvariable`, `builtin.getvariable`, `builtin.json.value`, `builtin.log` |

Custom modules are .NET types implementing `IWorkflowModule`, packaged as **`.wfmod`** files and
uploaded at runtime (loaded via `AssemblyLoadContext`); they declare the same `ModuleSchema`.

## 8. Scripting: sandboxed, capability-gated

Script nodes run **JavaScript (Jint)**, **C# (Roslyn)**, or **Lua (MoonSharp)** against a unified
`workflow.*` API (variables, logging, HTTP, files, utilities). Network/file access is
**deny-by-default** and capability-gated (docs/scripting.md). The same sandboxed JS engine
evaluates `{{…}}` expressions and connection conditions.

## 9. Runtime & operational architecture

- **Single-process engine**: `Workflow.Api` hosts the REST API (`/api/v1/*`, OpenAPI, API-key or
  JWT auth), the SignalR real-time hub, and the Akka.NET actor system. The Blazor WASM UI
  (designer, Script Studio, execution monitor, module manager) is a second process.
- **Actor hierarchy per run**: supervisor → `WorkflowExecutor` (one per execution) →
  node/construct child actors. Failures are contained and reported per node.
- **Pluggable persistence**: SQLite / PostgreSQL / NATS / S3 stores for definitions, execution
  state/history, and variables — individually composable. Execution state is persisted at
  terminal states; history and variable versions are queryable.
- **Observability**: SignalR events for execution & node lifecycle; metrics per workflow/node.

## 10. Composition philosophy (summary)

1. **Explicit over implicit** — every data hop is a declared, typed, port-to-port edge.
2. **Control flow as data** — branching, loops, parallelism, and error scopes are nodes in the
   same graph, supervised by their own actors.
3. **One schema, three consumers** — module schemas drive the designer UI, engine binding, and
   validation identically.
4. **Security by default** — template expansion is opt-in per field; scripts are sandboxed and
   capability-gated; secrets are declared and redacted.
5. **Batch-per-trigger execution** — a run is one traversal of the graph with a shared
   variable context, not a continuous document stream. (This is the single biggest semantic
   difference from SnapLogic — see artifact 02.)
