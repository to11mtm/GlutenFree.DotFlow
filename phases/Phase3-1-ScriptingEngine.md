# Phase 3.1: Scripting Engine (Weeks 23-25) 📜

Made with 💖 by Ami-Chan! UwU ✨

[Back to Phase 3](Phase3-AdvancedFeatures.md) | [All Phases](README.md)

---

## Overview

Phase 3.1 gives workflow authors a **general-purpose script node** (`builtin.script`) with a unified, sandboxed, multi-language execution seam — plus the **workflow script API** (variables/logging/utilities and gated HTTP/file access), a **script library system** for reusable snippets, a **script test endpoint**, and the long-deferred **PropertyBinder inline expression evaluation** from Phase 1.4. Much of the underlying machinery **already exists**: Jint is in the tree as the sandboxed `IExpressionEvaluator` (Phase 2.2.5), the `Workflow.Scripting.Roslyn` core ships typed C# script compilation with forbidden-syntax analysis and collectible-ALC execution (Phase 2.6.b), and the transform-script family already proves out the validate/preview/compile endpoint pattern. Phase 3.1 is about **generalizing** these proven seams into a first-class scripting surface, not building from scratch~ 🌷

> **Reality-check note (July 2026):** The §3.1 checklist in [`Phase3-AdvancedFeatures.md`](Phase3-AdvancedFeatures.md#31-scripting-engine-week-15-17) was written before Phase 2 landed. Since then: (a) **Jint is already integrated** as `JintExpressionEvaluator` with memory/timeout/recursion sandboxing — the "Implement JavaScript executor (Jint)" task is a generalization, not a green-field build; (b) **typed C# scripting already exists** (`Workflow.Scripting.Roslyn` + `builtin.transform.script`) — C# joins the language set nearly for free; (c) the checklist's `ScriptTestController` becomes a **Minimal-API endpoint group** per the D1 convention from 2.7; (d) the checklist's script Database API overlaps with the Phase 2.4 database module family and its named-connection registry — raw connection strings in scripts are a **security anti-pattern** we won't ship; (e) `PropertyBinder` exists with `{{Variable.X}}`/`{{NodeId.Output}}` resolution and an explicit note deferring inline expressions to this phase. This plan reconciles all five.

**Timeline:** 3 weeks (Weeks 23-25) — 3.1.0–3.1.2 (executor seam, JS/C# executors, script API) Week 23 · 3.1.3–3.1.5 (Lua, `builtin.script`, libraries) Week 24 · 3.1.6–3.1.7 (test endpoint, PropertyBinder expressions, docs/polish) Week 25
**Complexity:** 🟠 Medium-High — the executor seam and API bridging are well-bounded; the risky parts are **sandbox hardening** (script-visible API must not become an escape hatch) and **type marshalling** (JS/Lua values ↔ .NET across three engines)

> **CopilotNote:** Hot paths: a new `Workflow.Scripting` project (language-agnostic abstractions — deliberately separate from the Roslyn-quarantined project per the 2.4.b/2.6.b convention), `Workflow.Scripting/Executors/JavaScriptScriptExecutor.cs` (Jint, generalized from the expression evaluator), `Workflow.Scripting.Lua` (MoonSharp, quarantined project for the new dependency), `Workflow.Modules/Builtin/Script/ScriptModule.cs` (`builtin.script`), `Workflow.Api/V1/ScriptEndpoints.cs`, and a `PropertyBinder` extension consuming the existing `IExpressionEvaluator`. Tests follow the established xUnit + FluentAssertions + `WebApplicationFactory<Program>` patterns~ 🌸

### Confirmed Design Decisions ✅

| # | Decision |
|---|----------|
| **D1 A new `IScriptExecutor` seam in a new `Workflow.Scripting` project** | `IScriptExecutor` (language id, `ExecuteAsync(code, ScriptExecutionContext, ct) → ScriptExecutionResult`) lives in a new dependency-light `Workflow.Scripting` project. Language executors register keyed in DI (`AddKeyedSingleton<IScriptExecutor>("javascript")`, matching the 2.2.5 keyed-evaluator convention) with an `IScriptExecutorFactory` resolver. **`IExpressionEvaluator` stays untouched** — it remains the lightweight single-expression seam for control-flow modules; `IScriptExecutor` is the multi-statement, API-bridged big sibling. |
| **D2 JavaScript = Jint (already in the tree), generalized** | `JavaScriptScriptExecutor` wraps Jint with the same safety posture as `JintExpressionEvaluator` (strict mode, memory cap, recursion cap, timeout via `TimeoutInterval` + linked CTS) but with **script-level limits** from `ScriptExecutionConfig` (default 30 s / 64 MB vs the evaluator's 250 ms / 4 MB) and the `workflow` API object injected. No new package — Jint is already a dependency. |
| **D3 C# joins the language set via the existing Roslyn core** | `CSharpScriptExecutor` adapts `Workflow.Scripting.Roslyn` (compiler + `ForbiddenSyntaxWalker` + collectible runner + compiled cache) to the `IScriptExecutor` seam. It lives in the **existing Roslyn-quarantined project** (`Workflow.Scripting.Roslyn`) so the heavy Roslyn dependency stays opt-in; hosts that call `AddRoslynScripting()` get C# scripts, others simply don't have the language key registered. |
| **D4 Lua = MoonSharp in a quarantined `Workflow.Scripting.Lua` project** | MoonSharp (MIT, pure managed, no native deps) implements `LuaScriptExecutor`. New dependency → new quarantined project per the cloud/Roslyn convention, wired via `AddLuaScripting()`. Lua tables ↔ .NET marshalling via MoonSharp `DynValue` conversion + a JSON-round-trip fallback for complex object graphs. |
| **D5 Python is deferred to post-MVP (3.1.P1)** | IronPython lags CPython (3.4 dialect), and Python.NET requires a native CPython install on the host — both are poor fits for a sandboxed, portable engine. The MVP language set is **JavaScript + Lua + C#** (all pure-managed, all sandboxable). The `IScriptExecutor` seam makes Python purely additive later; the checklist's Python items map to **3.1.P1**. |
| **D6 The script API is capability-gated, not all-powerful** | `IWorkflowScriptApi` exposes: **always-on** — variables (get/set/delete/exists, staged as `VariableUpdates` per the ModuleResult convention), logging (debug/info/warn/error), utilities (guid/now/format/base64/hash/json/csv), workflow context (executionId/workflowId), `WaitAsync`; **gated by `ScriptExecutionConfig`** — HTTP (`AllowNetwork`, via `IHttpClientFactory`'s existing `dotflow.http` client, request-count capped) and file system (`AllowFileSystem` **default false** + `AllowedPaths`, reusing the Phase 2.5 path-security sandbox). **No raw database API** — the checklist's `QueryDatabase(connectionString, …)` is rejected: scripts that need data go through workflow nodes (2.4 database modules + named connections) instead of carrying raw connection strings (Q2). |
| **D7 Variable writes are staged, not direct** | Scripts mutate variables through the API, but writes are **collected and returned** as `ModuleResult.VariableUpdates` (the same mechanism `builtin.setvariable` uses) so the engine applies them transactionally after the node completes. Reads see execution-scope variables passed into the `ModuleExecutionContext`. No direct `IVariableStore` access from inside a running script. |
| **D8 `builtin.script` is a first-class module in the builtin family** | `ScriptModule` (`builtin.script`) — properties: `language` (dropdown: registered executor keys), `code` (Code editor), `timeoutSeconds`, `allowNetwork`, `allowFileSystem`, `allowedPaths`; inputs: `input` (object, optional); outputs: `result` + whatever the script returns as an object. Registered in `BuiltinModules.GetAll()` only when at least one executor is registered — schema's language `AllowedValues` reflects the registered set. `builtin.transform.script` stays as-is (typed, cached, transform-shaped); `builtin.script` is the general-purpose sibling (Q6). |
| **D9 Script libraries persist via the existing blob-store seam** | `ScriptLibraryDefinition` (id/name/description/language/code/exported functions) + `IScriptLibraryStore` with a blob-store-backed default (key `scripts/libraries/{id}.json`, using the same `IBlobStore` seam as 2.8's package archive) and in-memory fallback for provider-less hosts. Libraries are **prepended** to script code at execution (JS: injected as a module-object; Lua: preloaded via `require`; C#: prepended `#load`-style source) — no cross-language imports. |
| **D10 Script test endpoint = Minimal-API group under `/api/v1/scripts`** | `ScriptEndpoints.MapScriptEndpoints()`: `POST /api/v1/scripts/test` (code + language + inputs + config → outputs/logs/duration/errors), `GET /api/v1/scripts/languages` (registered executor keys), plus library CRUD (`GET/PUT/DELETE /api/v1/scripts/libraries[/{id}]`). ProblemDetails errors, `WorkflowWrite` policy on test + library writes, `WorkflowRead` on reads — all 2.7 conventions verbatim. |
| **D11 PropertyBinder expressions ride the existing `IExpressionEvaluator`** | The deferred Phase 1.4 item: `PropertyBinder` gains an **expression path** using the already-registered Jint evaluator (250 ms sandbox — right-sized for binding). Detection: an inner template that is **not** a pure `Variable.X`/`NodeId.Output` reference is treated as an expression (e.g. `{{Variable.Count > 5}}`, `{{1 + 2}}`), with references inside expressions resolved to evaluator variables first. Behavior is **opt-in via constructor flag** (`enableExpressions`, default true in hosts, false-able for compat) so the existing binder tests stay meaningful. Evaluation failures produce binding errors, never silent nulls. |
| **D12 Sandbox posture: deny-by-default for anything that touches the outside world** | `ScriptExecutionConfig` defaults: `TimeoutSeconds=30`, `MaxMemoryBytes=64 MB`, `AllowNetwork=false`, `AllowFileSystem=false`, `AllowedPaths=[]`, `MaxHttpRequests=10`. The node's properties can loosen these **up to host-configured ceilings** (`Scripting:MaxTimeoutSeconds` etc.) — a workflow author can never exceed what the host operator allows. Engines get no CLR access beyond the injected API object (Jint: no `SetTypeResolver`; MoonSharp: `CoreModules.Preset_SoftSandbox`; C#: `ForbiddenSyntaxWalker`). |

### TO RESOLVE 🤔

- [ ] **Q1 MVP language set: is JavaScript + Lua + C# (Python deferred) acceptable?**
  - IronPython is stuck at a 3.4-era dialect; Python.NET needs native CPython on the host (deployment + sandbox pain). **Proposed:** defer Python to **3.1.P1** and revisit with Python.NET + containerized isolation — *needs confirmation*.
    - OK
- [ ] **Q2 Script Database API: OK to reject the raw `QueryDatabase(connectionString, …)` checklist item?**
  - Raw connection strings in script code bypass the 2.4 named-connection registry + encryption-at-rest. **Proposed:** no database API in scripts for MVP; workflows compose `builtin.database.*` nodes with script nodes instead. A named-connection-based script API (`QueryAsync(connectionName, sql, params)`) can be **3.1.P2** if demanded — *needs confirmation*.
    - OK 
- [ ] **Q3 Script HTTP API default: `AllowNetwork` default false (deny-by-default) even though the checklist says default true?**
  - D12 proposes deny-by-default with per-node opt-in under host ceilings. **Proposed:** default false — *needs confirmation*.
    - Okay
- [ ] **Q4 Library storage: blob-store seam (D9) or a dedicated `IScriptLibraryRepository` in persistence?**
  - Blob storage is zero-migration and matches 2.8's pattern; a repository adds queryability (list by language) at the cost of a new table/migration per provider. **Proposed:** blob-backed for MVP with an in-memory fallback; repository promotion tracked as **3.1.P3** — *needs confirmation*.
    - Okay
- [ ] **Q5 PropertyBinder expression detection: implicit (any non-reference template = expression, D11) or explicit marker (e.g. `{{= expr }}`)?**
  - Implicit is what the deferred checklist describes and reads naturally; explicit is safer against accidental evaluation of literal text that happens to contain braces. **Proposed:** implicit with the constructor opt-out, since today non-reference templates resolve to null/unchanged (so semantics only *improve*) — *needs confirmation*.
    -  Okay
- [ ] **Q6 Should `builtin.script` support returning `ActivePorts` (control-flow from scripts)?**
  - Letting scripts pick output ports would make them mini-routers. **Proposed:** not in MVP — scripts return data; routing stays with `builtin.condition`/`builtin.switch`. Revisit post-MVP — *needs confirmation*.
    - OKay 
- [ ] **Q7 Script async support: JS `async/await` + `WaitAsync` only, or full task-bridging for Lua/C# too?**
  - Jint 4.x handles promises; MoonSharp coroutines are a different model; Roslyn scripts can be `async` natively. **Proposed:** MVP = JS promises + C# async natively supported; Lua scripts are synchronous (with `workflow.wait(ms)` provided); coroutine bridging deferred — *needs confirmation*.
    - Have a plan for Lua coroutines, but defer bridging to post-MVP. JS and C# should allow async.

---

## Pre-Existing Work (from earlier phases) ✅

| Component | File | Status |
|-----------|------|--------|
| Jint dependency + sandboxed evaluator (strict, 4 MB, 250 ms, recursion cap) | `Workflow.Engine/Services/JintExpressionEvaluator.cs` | ✅ Safety-posture reference + engine reuse for D2/D11 |
| `IExpressionEvaluator` contract + parse/runtime exceptions | `Workflow.Core/Abstractions/IExpressionEvaluator.cs` | ✅ D11 consumes as-is; stays the control-flow seam |
| Keyed evaluator registration convention (`AddKeyedSingleton` + factory) | `Workflow.Api/Program.cs`, `Workflow.Engine/Services/KeyedExpressionEvaluatorFactory.cs` | ✅ Pattern for keyed `IScriptExecutor` registration (D1) |
| Typed C# scripting core: compiler, `ForbiddenSyntaxWalker`, collectible runner, compiled cache, HMAC signer | `Workflow.Scripting.Roslyn/*` | ✅ `CSharpScriptExecutor` adapts it (D3) |
| `builtin.transform.script` + validate/preview/compile endpoints | `Workflow.Modules.Transform.Script/*`, `Workflow.Api/Transform/TransformScriptEndpoints.cs` | ✅ Stays as-is; endpoint pattern reference (D8/D10) |
| `PropertyBinder` with `{{Variable.X}}`/`{{NodeId.Output}}` refs (expressions explicitly deferred here) | `Workflow.Modules/Binding/PropertyBinder.cs` | ✅ Extended in 3.1.6 (D11) |
| `ModuleResult.VariableUpdates` staged-write mechanism | `Workflow.Modules/Abstractions/IWorkflowModule.cs` | ✅ Script variable writes ride it (D7) |
| Path-security sandbox (allowed-path validation) from file modules | `Workflow.Modules/Builtin/Files/*` (Phase 2.5.a) | ✅ Reused by the file-system API gate (D6) |
| Named HTTP client `dotflow.http` via `IHttpClientFactory` | `Workflow.Modules/Builtin/Http/*` (Phase 2.3) | ✅ Backs the gated HTTP API (D6) |
| `IBlobStore` seam (+ in-memory fallback registration) | `Workflow.Persistence/Abstractions/IBlobStore.cs`, `Workflow.Api/Program.cs` | ✅ Library storage target (D9) |
| Minimal-API conventions: `MapV1Group`, `ApiResults`, auth policies, `WebApplicationFactory` tests | `Workflow.Api/V1/*`, `Workflow.Api/Auth/*` (Phase 2.7) | ✅ Reused verbatim by 3.1.5 (D10) |
| `PropertyEditorType.Code` / `.Dropdown` editor hints | `Workflow.Core/Models/ModuleSchema.cs` | ✅ `builtin.script` property editors (D8) |
| `JsonValueConverter` (iterative, stack-safe CLR↔JSON) | `Workflow.Modules/Internal/JsonValueConverter.cs` | ✅ Marshalling helper for script inputs/outputs |

> **CopilotNote:** The big insight mirroring 2.7/2.8: **two of the three MVP languages are already in the dependency tree** (Jint, Roslyn). The genuinely-new work is the executor seam, the workflow API bridge + its capability gates, MoonSharp, libraries, and the binder expressions. Budget risk on **API sandbox hardening** (D6/D12) and **marshalling** — not on engine integration~ 💖

---

## 3.1.0 Scripting Abstractions + JavaScript Executor 🟨 (`Workflow.Scripting/*`)

> **Purpose:** The seam everything else plugs into — `IScriptExecutor`, config/result contracts, and the first executor (Jint/JS), generalized from the proven evaluator posture~ ✨

**Complexity:** 🟡 Medium

### Tasks

- [ ] **New project `Workflow.Scripting`** 📦 — references `Workflow.Core` only (+ Jint); wired into the solution + `Directory.Packages.props`
- [ ] **`Abstractions/IScriptExecutor.cs`** — `LanguageId` (e.g. `"javascript"`), `DisplayName`, `ExecuteAsync(string code, ScriptExecutionContext context, CancellationToken ct) → ScriptExecutionResult`
- [ ] **`Abstractions/ScriptExecutionContext.cs`** — `Inputs` (dict), `Variables` (read snapshot), `Api` (the bridge object, D6), `Config` (`ScriptExecutionConfig`), `ExecutionId`/`WorkflowId`/`NodeId`, `Logger`
- [ ] **`Abstractions/ScriptExecutionConfig.cs` (D12)** — `TimeoutSeconds=30`, `MaxMemoryBytes=64MB`, `AllowNetwork=false`, `AllowFileSystem=false`, `AllowedPaths=[]`, `MaxHttpRequests=10`; `Clamp(hostCeilings)` helper so node-level config can never exceed host limits
- [ ] **`Abstractions/ScriptExecutionResult.cs`** — `Success`, `ReturnValue` (object?/JsonElement), `VariableUpdates` (staged, D7), `Logs` (captured api log calls), `Error`/`Diagnostics`, `Duration`
- [ ] **`Abstractions/IScriptExecutorFactory.cs`** + keyed-DI implementation (`GetExecutor(languageId)`, `GetRegisteredLanguages()`) — mirrors `KeyedExpressionEvaluatorFactory`
- [ ] **`Executors/JavaScriptScriptExecutor.cs` (D2)** 🟨
  - [ ] Jint engine per execution: strict mode, `MemoryLimit`/`TimeoutInterval`/recursion from config, linked-CTS interrupt (same pattern as `JintExpressionEvaluator`)
  - [ ] Inject `workflow` API object + `input` global; marshal .NET↔JS via Jint interop + `JsonValueConverter` fallback for complex graphs
  - [ ] Promise support: a returned promise is awaited (Q7); script errors → structured `ScriptExecutionResult.Error` (line info when available)
- [ ] **DI:** `AddWorkflowScripting()` (factory + JS executor); host ceilings bound from `Scripting:*` config

### Tests (target ~12): → `Workflow.Tests/Scripting/JavaScriptScriptExecutorTests.cs`

- [ ] `Execute_SimpleScript_ReturnsValue` · `Execute_InputData_Accessible` · `Execute_ObjectReturn_MarshalsToNet`
- [ ] `Execute_SyntaxError_StructuredError` · `Execute_RuntimeError_StructuredError`
- [ ] `Execute_Timeout_Enforced` · `Execute_MemoryLimit_Enforced` · `Execute_CancellationToken_Honoured`
- [ ] `Execute_Promise_Awaited` *(Q7)* · `Execute_StrictMode_UndeclaredThrows`
- [ ] `Config_Clamp_NodeCannotExceedHostCeilings` · `Factory_ResolvesRegisteredLanguages`

---

## 3.1.1 Unified Workflow Script API 🔧 (`Workflow.Scripting/Api/*`)

> **Purpose:** The `workflow` object scripts see — variables, logging, utilities always-on; HTTP/file capability-gated; no raw database access (D6/D7/Q2)~ ✨

**Complexity:** 🟠 Medium-High *(this is the sandbox boundary — every method is attack surface)*

### Tasks

- [ ] **`Api/IWorkflowScriptApi.cs` + `WorkflowScriptApi.cs`** 🔧
  - [ ] **Variables (D7):** `GetVariable(name)` · `SetVariable(name, value)` (staged) · `DeleteVariable(name)` (staged tombstone) · `VariableExists(name)`
  - [ ] **Logging:** `LogDebug/LogInfo/LogWarning/LogError` — forwarded to the module logger **and** captured into `ScriptExecutionResult.Logs` (for the test endpoint)
  - [ ] **Utilities:** `NewGuid()` · `Now()`/`UtcNow()` · `FormatDateTime(date, format)` · `Base64Encode/Decode` · `Hash(data, algorithm)` (SHA-256/SHA-512/MD5) · `ParseJson`/`ToJson` · `ParseCsv`/`ToCsv` (reuse the 2.5.a CsvHelper internals)
  - [ ] **Workflow context:** `GetExecutionId()` · `GetWorkflowId()` · `GetNodeId()` · `WaitAsync(ms)` (CT-linked, capped by remaining timeout)
- [ ] **Gated HTTP (D6/Q3)** 🌐 — `HttpGetAsync/HttpPostAsync/HttpPutAsync/HttpDeleteAsync(url, body?, headers?)`; throws a clear `ScriptSecurityException` when `AllowNetwork=false`; per-execution request counter enforcing `MaxHttpRequests`; uses the named `dotflow.http` client; responses surfaced as `{ status, headers, body }`
- [ ] **Gated file system (D6)** 📁 — `ReadFileAsync/WriteFileAsync/FileExists/DeleteFile(path)`; throws when `AllowFileSystem=false`; every path validated against `AllowedPaths` via the 2.5.a path-security sandbox (traversal-proof)
- [ ] **No database API** — document the decision + the node-composition alternative (Q2)
- [ ] **Language bridging:** JS interop shapes verified (camelCase aliases where idiomatic); marshalling helpers shared for 3.1.2/3.1.3

### Tests (target ~16): → `Workflow.Tests/Scripting/WorkflowScriptApiTests.cs`

- [ ] Variables: `SetVariable_StagedInResult` · `GetVariable_ReadsSnapshot` · `DeleteVariable_StagedTombstone` · `VariableExists_Works`
- [ ] Logging: `Logs_CapturedAndForwarded` (all four levels)
- [ ] Utilities: `Guid_Now_Format_Base64_Hash_Json_Csv_RoundTrips` *(theory-style batch)*
- [ ] HTTP: `Http_Blocked_WhenNetworkDisallowed` · `Http_Allowed_MakesRequest` *(local test server)* · `Http_RequestCount_CapEnforced`
- [ ] Files: `File_Blocked_WhenFileSystemDisallowed` · `File_AllowedPath_ReadsWrites` · `File_PathTraversal_Rejected` · `File_OutsideAllowedPaths_Rejected`
- [ ] `Wait_RespectsCancellation` · `Api_FromJavaScript_EndToEnd` *(script calls each API family)*

---

## 3.1.2 C# Executor (Roslyn adapter) 🟪 (`Workflow.Scripting.Roslyn/Executors/*`)

> **Purpose:** C# joins the language set by adapting the existing 2.6.b core to `IScriptExecutor` — staying inside the Roslyn-quarantined project (D3)~ ✨

**Complexity:** 🟡 Medium *(the hard parts — compiler, forbidden-syntax, caching — already exist)*

### Tasks

- [ ] **`Executors/CSharpScriptExecutor.cs`** — `LanguageId="csharp"`; compiles via `IRoslynScriptCompiler` (globals type exposing `Input`, `Workflow` api, `Variables`), executes via `CollectibleScriptRunner`, caches via `ICompiledScriptCache` (keyed by code hash)
- [ ] **`ForbiddenSyntaxWalker` reuse** — same deny-list (no `System.IO` unless api-gated, no reflection, no P/Invoke, no `Environment`); extend with script-API-specific allowances
- [ ] **Async:** scripts may `await` (Q7) — natively supported by Roslyn scripting
- [ ] **DI:** `AddRoslynScripting()` gains the keyed executor registration; hosts without the Roslyn project simply lack the `"csharp"` key
- [ ] Marshalling: `Input` as `IReadOnlyDictionary<string, object?>`; return value materialized via the existing `ScriptResultMaterializer`

### Tests (target ~8): → `Workflow.Tests/Scripting/CSharpScriptExecutorTests.cs`

- [ ] `Execute_SimpleScript_ReturnsValue` · `Execute_InputAccessible` · `Execute_WorkflowApi_Callable`
- [ ] `Execute_ForbiddenSyntax_Rejected` · `Execute_CompileError_StructuredDiagnostics`
- [ ] `Execute_Async_Awaited` · `Execute_Timeout_Enforced` · `Cache_SecondRun_UsesCompiled`

---

## 3.1.3 Lua Executor (MoonSharp) 🌙 (`Workflow.Scripting.Lua/*`)

> **Purpose:** The one genuinely-new engine — MoonSharp in its own quarantined project (D4)~ ✨

**Complexity:** 🟠 Medium *(new dependency + table marshalling)*

### Tasks

- [ ] **New project `Workflow.Scripting.Lua`** — MoonSharp package added to `Directory.Packages.props`; references `Workflow.Scripting`
- [ ] **`LuaScriptExecutor.cs`** — `LanguageId="lua"`; `CoreModules.Preset_SoftSandbox` (no `io`/`os`/`load`); per-execution `Script` instance; instruction-count-based abort + linked-CTS timeout; memory observed via instruction budget (MoonSharp has no direct memory cap — document)
- [ ] **API bridging:** register `IWorkflowScriptApi` via `UserData.RegisterType`; `workflow` global + `input` table; Lua tables ↔ .NET dictionaries/lists (recursive marshaller with depth cap, reusing `JsonValueConverter` shapes)
- [ ] **Sync model (Q7):** Lua scripts are synchronous; `workflow.wait(ms)` provided; async HTTP api methods exposed as blocking wrappers with CT propagation
- [ ] **DI:** `AddLuaScripting()` keyed registration
- [ ] **Docs:** language nuances (1-based arrays, table semantics) in the scripting guide

### Tests (target ~10): → `Workflow.Tests/Scripting/LuaScriptExecutorTests.cs`

- [ ] `Execute_SimpleScript_ReturnsValue` · `Execute_InputTable_Accessible` · `Execute_TableReturn_MarshalsToNet` · `Execute_NestedTables_DepthCapped`
- [ ] `Execute_ApiCalls_Work` *(variables + logging + utilities from Lua)*
- [ ] `Execute_SyntaxError_StructuredError` · `Execute_RuntimeError_StructuredError`
- [ ] `Execute_Timeout_Enforced` · `Sandbox_NoIoOsLoad_Modules` · `Http_Gated_FromLua`

---

## 3.1.4 `builtin.script` Module 🧩 (`Workflow.Modules/Builtin/Script/*`)

> **Purpose:** The workflow-facing node: pick a language, write code, run it in the sandbox with staged variable writes (D8)~ ✨

**Complexity:** 🟡 Medium

### Tasks

- [ ] **`ScriptModule.cs` (`builtin.script`)** 🧩
  - [ ] Properties: `language` (Dropdown, `AllowedValues` = registered executor keys) · `code` (Code editor) · `timeoutSeconds` (Number) · `allowNetwork`/`allowFileSystem` (Boolean) · `allowedPaths` (Json)
  - [ ] Inputs: `input` (object, optional — flows to the script's `input` global); Outputs: `result` (object)
  - [ ] `ExecuteAsync`: resolve executor via `IScriptExecutorFactory` (from `context.Services`), build `ScriptExecutionContext` (variables snapshot from `context.Variables`, config clamped to host ceilings), run, map `ScriptExecutionResult` → `ModuleResult` (`Ok(outputs, variableUpdates)` on success, `Fail` with diagnostics otherwise)
  - [ ] `ValidateConfiguration`: language key registered, code non-empty, timeout within ceiling
- [ ] **Registration:** in `BuiltinModules.GetAll()`/host wiring so the API host + engine both resolve it; language dropdown reflects registered executors at schema-build time
- [ ] **No `ActivePorts` from scripts (Q6)** — document; scripts return data only
- [ ] **Docs:** `docs/scripting.md` — the node, per-language examples, API reference, sandbox flags

### Tests (target ~10): → `Workflow.Tests/Modules/Script/ScriptModuleTests.cs`

- [ ] `Execute_JavaScript_EndToEnd` · `Execute_Lua_EndToEnd` · `Execute_CSharp_EndToEnd` *(engine E2E via BuiltinModuleEndToEnd harness)*
- [ ] `VariableUpdates_AppliedByEngine_DownstreamSees` · `Inputs_FlowIntoScript` · `Result_FlowsToOutputPort`
- [ ] `UnknownLanguage_FailsValidation` · `EmptyCode_FailsValidation`
- [ ] `NodeTimeout_OverHostCeiling_Clamped` · `ScriptFailure_ProducesModuleFailure`

---

## 3.1.5 Script Library System 📚 (`Workflow.Scripting/Libraries/*`)

> **Purpose:** Reusable, per-language code snippets that scripts can import (D9)~ ✨

**Complexity:** 🟡 Medium

### Tasks

- [ ] **`Libraries/ScriptLibraryDefinition.cs`** — `LibraryId`, `Name`, `Description`, `Language`, `Code`, `ExportedFunctions` (documentation metadata), `Dependencies` (other library ids, same language)
- [ ] **`Libraries/IScriptLibraryStore.cs`** — `SaveAsync`/`GetAsync`/`GetAllAsync(language?)`/`DeleteAsync`; validation (id format, language key registered, dependency cycles via the 2.8.1-style resolver)
- [ ] **`Libraries/BlobScriptLibraryStore.cs`** (default when a blob store exists) + `InMemoryScriptLibraryStore` fallback — mirrors the 2.8 state-store fallback pattern
- [ ] **Import mechanics (per language):**
  - [ ] JS: libraries injected as pre-evaluated module objects (`const lib = workflow.require('libraryId')`)
  - [ ] Lua: preloaded into `package.preload` → `local lib = require('libraryId')`
  - [ ] C#: library source prepended in dependency order (compile-time inclusion)
  - [ ] Dependency-ordered load; missing/wrong-language imports → clear structured error
- [ ] **`builtin.script` + test endpoint** accept a `libraries` list (explicit imports — no auto-injection)

### Tests (target ~10): → `Workflow.Tests/Scripting/ScriptLibraryTests.cs`

- [ ] `Store_SaveGetDelete_RoundTrips` · `Store_ListByLanguage_Filters` · `Store_DependencyCycle_Rejected`
- [ ] `Js_Require_CallsLibraryFunction` · `Lua_Require_CallsLibraryFunction` · `CSharp_Prepend_CallsLibraryFunction`
- [ ] `Import_MissingLibrary_ClearError` · `Import_WrongLanguage_ClearError`
- [ ] `Dependencies_LoadInOrder` · `BlobStore_Fallback_InMemoryWhenNoProvider`

---

## 3.1.6 Script Endpoints 🧪🌐 (`/api/v1/scripts`)

> **Purpose:** Test-before-you-save + library management over HTTP, on the 2.7 conventions (D10)~ ✨

**Complexity:** 🟢 Low-Medium

### Tasks

- [ ] **`V1/ScriptEndpoints.cs`** (`MapScriptEndpoints`) 🗺️
  - [ ] `POST /api/v1/scripts/test` — `{ language, code, inputs?, libraries?, config? }` → `{ success, result, logs, variableUpdates, durationMs, error? }`; config clamped to host ceilings; `WorkflowWrite` policy; `422` for unknown language/empty code
  - [ ] `GET /api/v1/scripts/languages` — registered executor keys + display names; `WorkflowRead`
  - [ ] `GET/PUT/DELETE /api/v1/scripts/libraries[/{id}]` — library CRUD over `IScriptLibraryStore`; `?language=` filter on list; `WorkflowRead`/`WorkflowWrite`
- [ ] **Contracts:** `ScriptTestRequest/ResultDto`, `ScriptLibraryDto` in `Contracts/Scripts/`
- [ ] **Swagger:** tagged `Scripts`; request/response examples
- [ ] **Docs:** endpoint section in `docs/rest-api.md` + cross-link from `docs/scripting.md`

### Tests (target ~10): → `Workflow.Tests/Api/V1/ScriptEndpointsTests.cs`

- [ ] `Test_JavaScript_ReturnsResultAndLogs` · `Test_Lua_ReturnsResult` · `Test_CSharp_ReturnsResult`
- [ ] `Test_UnknownLanguage_422` · `Test_EmptyCode_422` · `Test_ScriptError_StructuredErrorIn200Body`
- [ ] `Test_ConfigCeilings_Clamped` · `Languages_ListsRegistered`
- [ ] `Libraries_CrudRoundTrip` · `Test_WithLibrary_ImportsWork`

---

## 3.1.7 PropertyBinder Expression Evaluation 🧮 (deferred from Phase 1.4)

> **Purpose:** `{{Variable.Count > 5}}`, `{{1 + 2}}`, `{{Variable.Name + '!'}}` — inline expressions in property bindings via the existing Jint evaluator (D11)~ ✨

**Complexity:** 🟡 Medium *(compat-sensitive — the binder runs on every node execution)*

### Tasks

- [ ] **Detection (Q5):** inner template that isn't a pure `Variable.X`/`NodeId.Output` reference → expression path; pure references keep the existing fast path untouched
- [ ] **Evaluation:** references inside the expression are resolved to evaluator variables first (`Variable.Count` → injected `Count`-style scope), then evaluated via `IExpressionEvaluator` (Jint — 250 ms/4 MB sandbox is already right-sized); supports arithmetic/comparison/logical/string ops per the checklist (Jint gives them all for free)
- [ ] **Whole-template vs interpolated:** single-expression templates preserve the raw typed result (like references today); mixed text interpolates `ToString`
- [ ] **Caching:** memoize prepared scripts per expression string (Jint `Engine` reuse or parsed-script cache) — the binder is hot-path
- [ ] **Failure semantics:** parse/runtime/timeout → binding error listing the expression (never silent null)
- [ ] **Compat:** `enableExpressions` constructor flag (default true); `PropertyBinder` acquires the evaluator optionally (null → reference-only behavior, existing tests unchanged)
- [ ] **Docs:** expression syntax in the module author guide + `docs/scripting.md`

### Tests (target ~10): → `Workflow.Tests/Modules/Binding/PropertyBinderExpressionTests.cs`

- [ ] `Arithmetic_Evaluates` · `Comparison_Evaluates` · `Logical_Evaluates` · `StringConcat_Evaluates`
- [ ] `VariableReference_InExpression_Resolves` · `NodeOutput_InExpression_Resolves`
- [ ] `WholeTemplate_PreservesType` · `MixedTemplate_Interpolates`
- [ ] `InvalidExpression_BindingError` · `ExpensiveExpression_TimesOut`
- [ ] *(regression)* existing reference-only binder tests stay green with expressions enabled

---

## Proposed File Layout 🗂️

```
Workflow.Scripting/                          ← new project (3.1.0)
  Abstractions/
    IScriptExecutor.cs
    IScriptExecutorFactory.cs
    ScriptExecutionContext.cs
    ScriptExecutionConfig.cs
    ScriptExecutionResult.cs
  Api/
    IWorkflowScriptApi.cs                    ← 3.1.1
    WorkflowScriptApi.cs
    ScriptSecurityException.cs
  Executors/
    JavaScriptScriptExecutor.cs              ← 3.1.0 (Jint)
  Libraries/
    ScriptLibraryDefinition.cs               ← 3.1.5
    IScriptLibraryStore.cs
    BlobScriptLibraryStore.cs / InMemoryScriptLibraryStore.cs
  ScriptingServiceCollectionExtensions.cs    ← AddWorkflowScripting()

Workflow.Scripting.Roslyn/
  Executors/CSharpScriptExecutor.cs          ← 3.1.2 (adapter, stays quarantined)

Workflow.Scripting.Lua/                      ← new quarantined project (3.1.3)
  LuaScriptExecutor.cs
  LuaMarshaller.cs
  LuaScriptingServiceCollectionExtensions.cs ← AddLuaScripting()

Workflow.Modules/
  Builtin/Script/ScriptModule.cs             ← 3.1.4 (builtin.script)
  Binding/PropertyBinder.cs                  ← extended (3.1.7, D11 — expression path)

Workflow.Api/
  V1/ScriptEndpoints.cs                      ← 3.1.6
  Contracts/Scripts/ScriptContracts.cs       ← 3.1.6

Workflow.Tests/
  Scripting/…                                ← 3.1.0–3.1.3, 3.1.5
  Modules/Script/ScriptModuleTests.cs        ← 3.1.4
  Modules/Binding/PropertyBinderExpressionTests.cs ← 3.1.7
  Api/V1/ScriptEndpointsTests.cs             ← 3.1.6

docs/scripting.md                            ← new (node + API reference + sandbox + examples)
```

---

## Post-MVP Slices 🚧 *(deferred — not blocking 3.2+)*

### 3.1.P1 Python executor 🐍 *(Q1)*
Python.NET-based `PythonScriptExecutor` with containerized/native CPython isolation — needs a deployment story (host must provide CPython) and a sandbox review before it joins the language set. ~10 tests.

### 3.1.P2 Named-connection database script API 🗄️ *(Q2)*
`workflow.queryAsync(connectionName, sql, params)` over the 2.4 named-connection registry (never raw connection strings), capability-gated like HTTP. Only if node-composition proves insufficient.

### 3.1.P3 Script library repository + versioning 📚 *(Q4)*
Promote the blob-backed store to a persistence repository with library versioning, usage tracking, and per-workflow pinning.

### 3.1.P4 Script-driven routing 🎯 *(Q6)*
`ActivePorts` returns from `builtin.script` so scripts can participate in control flow — needs designer support (Phase 3.3) to be usable.

---

## Success Criteria ✅

- [ ] A workflow with a `builtin.script` node runs JavaScript, Lua, and C# scripts end-to-end through the engine, with inputs in and results out
- [ ] Scripts read variables and stage writes that downstream nodes observe (via `VariableUpdates`)
- [ ] The sandbox holds: timeout/memory enforced per engine; network and file access **denied by default** and gated by config under host ceilings; path traversal impossible; no raw database access from scripts
- [ ] Script libraries round-trip through the store and import correctly in all three languages
- [ ] `POST /api/v1/scripts/test` executes all registered languages and returns outputs + captured logs + duration
- [ ] `{{Variable.Count > 5}}`-style expressions evaluate in property bindings with the documented failure semantics — and every pre-existing reference-only binding behaves identically
- [ ] All existing tests stay green — `IExpressionEvaluator`, transform-script, and binder behavior are provably unchanged
