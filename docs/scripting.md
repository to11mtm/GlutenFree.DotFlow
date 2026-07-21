# Scripting Engine 📜

> Phase 3.1 — a general-purpose, sandboxed, multi-language script node plus inline
> expressions in property bindings. Made with 💖 by Ami-Chan~ ✨

GlutenFree.DotFlow lets workflow authors drop into real code when visual nodes aren't
enough. Scripts run inside a **deny-by-default sandbox** with per-execution timeout and
memory ceilings, and reach the workflow only through a single, capability-gated
**workflow script API**.

- [Languages](#languages)
- [The `builtin.script` node](#the-builtinscript-node)
- [Workflow script API](#workflow-script-api)
- [Sandbox & security](#sandbox--security)
- [Script libraries](#script-libraries)

> 💻 Prefer a UI? **[Script Studio](script-studio.md)** (`/scripts` in the designer app) is an
> in-browser editor for writing, testing (with IntelliSense + templates), and managing these
> scripts and libraries.
- [Script test endpoint](#script-test-endpoint)
- [Inline expressions in property bindings](#inline-expressions-in-property-bindings)

---

## Languages

| Language     | Id           | Engine     | Async model |
|--------------|--------------|------------|-------------|
| JavaScript   | `javascript` | Jint (ES2020) | `async/await`; a returned promise is awaited |
| C#           | `csharp`     | Roslyn scripting | native `await` |
| Lua          | `lua`        | MoonSharp  | synchronous; `workflow.wait(ms)`; pure-Lua coroutines work |

The registered set depends on which executor projects the host references
(`Workflow.Scripting` ships JavaScript; `Workflow.Scripting.Roslyn` adds C#;
`Workflow.Scripting.Lua` adds Lua). Query the live set via
`GET /api/v1/scripts/languages`.

> **Language nuances (Lua):** arrays are **1-based**; a Lua table maps to a .NET
> dictionary/list via a depth-capped marshaller; `io`/`os`/`load` are removed by the
> soft sandbox; `coroutine.*` is available for pure-Lua cooperative code (bridging Lua
> coroutines to .NET async is deferred — see the roadmap slice 3.1.P5).

---

## The `builtin.script` node

Configuration properties:

| Property         | Type    | Notes |
|------------------|---------|-------|
| `language`       | dropdown | one of the registered executor ids |
| `code`           | code    | the script source |
| `timeoutSeconds` | number  | clamped to the host ceiling |
| `allowNetwork`   | boolean | default **false** |
| `allowFileSystem`| boolean | default **false** |
| `allowedPaths`   | json    | file paths the script may touch when file access is on |

- **Input port** `input` (object, optional) flows in as the script's `input`.
- **Output port** `result` (object) carries the script's return value.
- Variable writes staged via the API become `VariableUpdates` that the engine applies,
  so **downstream nodes observe them**.

Scripts return **data only** — they cannot select active ports / drive control flow
(that is the deferred slice 3.1.P4).

### Examples

**JavaScript**

```javascript
const total = input.price * input.qty;
workflow.setVariable('lastTotal', total);
workflow.logInfo(`computed ${total}`);
return { total, when: workflow.utcNow() };
```

**C#**

```csharp
var total = Convert.ToInt32(Input["price"]) * Convert.ToInt32(Input["qty"]);
Workflow.SetVariable("lastTotal", total);
return new { total };
```

> C# over the wire receives JSON integers as `long`; use `Convert.ToInt32(...)` rather
> than an `(int)` unboxing cast.

**Lua**

```lua
local total = input.price * input.qty
workflow.setVariable('lastTotal', total)
return { total = total }
```

---

## Workflow script API

Every language reaches the workflow through the same surface (`workflow` global in
JS/Lua, `Workflow` in C#):

- **Variables** — `getVariable(name)`, `setVariable(name, value)` (staged),
  `deleteVariable(name)` (staged tombstone), `variableExists(name)`.
- **Logging** — `logDebug/logInfo/logWarning/logError` (forwarded to the module logger
  **and** captured into the result for the test endpoint).
- **Utilities** — `newGuid()`, `now()`/`utcNow()`, `formatDateTime(date, fmt)`,
  `base64Encode/Decode`, `hash(data, algorithm)` (SHA-256/512/MD5), `parseJson`/`toJson`,
  `parseCsv`/`toCsv`.
- **Context** — `getExecutionId()`, `getWorkflowId()`, `getNodeId()`, `wait(ms)`.
- **Gated HTTP** — `httpGetAsync/httpPostAsync/httpPutAsync/httpDeleteAsync`. Throws a
  `ScriptSecurityException` when `allowNetwork` is false; a per-execution counter enforces
  `MaxHttpRequests`.
- **Gated file system** — `readFileAsync/writeFileAsync/fileExists/deleteFile`. Throws when
  `allowFileSystem` is false; every path is validated against `allowedPaths` and is
  traversal-proof.

There is **no database API** by design — connection strings in scripts are a security
anti-pattern. Compose a database module upstream and pass its result into the script's
`input` instead.

---

## Sandbox & security

Deny-by-default `ScriptExecutionConfig`:

| Setting          | Default |
|------------------|---------|
| `TimeoutSeconds` | 30 |
| `MaxMemoryBytes` | 64 MB |
| `AllowNetwork`   | false |
| `AllowFileSystem`| false |
| `AllowedPaths`   | *(empty)* |
| `MaxHttpRequests`| 10 |

Node-level config is **clamped to host ceilings** (`Scripting:*` configuration) — a node
can request *less* than the host allows, never *more*. Timeouts and memory are enforced
by each engine; C# additionally runs through a forbidden-syntax walker (no raw
`System.IO`, reflection, P/Invoke, or `Environment` access) inside a collectible load
context.

---

## Script libraries

Reusable snippets stored per language:

- `ScriptLibraryDefinition` — `LibraryId`, `Name`, `Description`, `Language`, `Code`,
  `ExportedFunctions` (docs), `Dependencies` (other library ids, same language).
- Persisted through `IScriptLibraryStore` — a blob-backed store when a blob provider is
  configured, otherwise an in-memory fallback (mirrors the Phase 2.8 state-store pattern).
- Dependency cycles are rejected; imports are **explicit** (no auto-injection).

Import mechanics per language:

- **JavaScript** — `const lib = workflow.require('libraryId')`
- **Lua** — `local lib = require('libraryId')` (preloaded into `package.preload`)
- **C#** — library source is prepended in dependency order

---

## Script test endpoint

See [REST API › Scripts](rest-api.md#scripts--apiv1scripts) for the full contract.

- `POST /api/v1/scripts/test` — run a script and return
  `{ success, result, logs, variableUpdates, durationMs, error? }`. Config is clamped to
  host ceilings; unknown language / empty code → `422`. A script *error* is reported in a
  `200` body with `success: false`.
- `GET /api/v1/scripts/languages` — registered ids + display names.
- `GET/PUT/DELETE /api/v1/scripts/libraries[/{id}]` — library CRUD (`?language=` filter on
  list).

---

## Inline expressions in property bindings

Phase 3.1.7 extends the property binder beyond plain references. Any `{{ ... }}` template
that is **not** a pure `Variable.X` / `NodeId.Output` reference is evaluated as a
JavaScript expression through the same sandboxed `IExpressionEvaluator` (Jint, 250 ms /
4 MB).

```text
{{Variable.Count > 5}}              → true / false   (bool)
{{1 + 2 * 3}}                       → 7              (number)
{{Variable.Name + '!'}}             → "Ami!"         (string)
{{orderNode.total * 2}}             → 42             (number)
{{Math.max(Variable.A, Variable.B)}}→ built-ins work
```

Semantics:

- **Pure references keep their fast path** — `{{Variable.User.Name}}` and
  `{{nodeId.output}}` resolve exactly as before (no evaluator involved), so existing
  bindings are unchanged.
- **Reference tokens inside an expression** (`Variable.X`, `NodeId.Output`) are resolved
  first and injected as scope variables; dotted identifiers that are *not* workflow
  references (e.g. `Math.max`, `JSON.parse`) are left intact for the evaluator.
- **Whole-template** expressions preserve the raw evaluated **type** (a `bool` stays a
  `bool`); **mixed** templates like `Total is {{Variable.Count + 1}}!` interpolate to a
  string.
- **Failure is never silent** — a parse error, runtime error, or timeout becomes a
  binding error that names the offending expression.
- Expression evaluation can be disabled per binder (`enableExpressions: false`), and a
  binder constructed without an evaluator falls back to reference-only (pre-3.1) behavior.

In the host, `IPropertyBinder` is registered with the default `IExpressionEvaluator`, so
expressions are enabled for workflow execution out of the box.
