# Phase 3.1: Scripting Engine (Weeks 23-25) 📜

Made with 💖 by Ami-Chan! UwU ✨

[Back to Phase 3](Phase3-AdvancedFeatures.md) | [All Phases](README.md)

---

## Overview

> **Progress (2026-07-19):** Phase 3.1 is **COMPLETE ✅**. All 8 slices (3.1.0–3.1.7) are implemented, tested, and documented — abstractions + JS/C#/Lua executors, unified script API, `builtin.script`, library system, `/api/v1/scripts` endpoints, and PropertyBinder inline expressions. ~1,285 tests green (the only intermittent failures are pre-existing parallel-timing flakes that pass in isolation). A timing race in the JavaScript executor's timeout reporting was fixed as part of this work, and the builtin-module count tests were updated for `builtin.script`. Docs: `docs/scripting.md` (new) + `docs/rest-api.md` + `docs/module-author-guide.md`.

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
| **D4 Lua = MoonSharp in a quarantined `Workflow.Scripting.Lua` project** | MoonSharp (MIT, pure managed, no native deps) implements `LuaScriptExecutor`. New dependency → new quarantined project per the cloud/Roslyn convention, wired via `AddLuaScripting()`. Lua tables ↔ .NET marshalling via MoonSharp `DynValue` conversion + a JSON-round-trip fallback for complex object graphs. The executor loads the script body as a **function invoked via `DynValue`** (not top-level `DoString`) so async coroutine bridging (3.1.P5) is purely additive — pure-Lua `coroutine.*` works in MVP; only .NET-async bridging is deferred (Q7). |
| **D5 Python is deferred to post-MVP (3.1.P1)** | IronPython lags CPython (3.4 dialect), and Python.NET requires a native CPython install on the host — both are poor fits for a sandboxed, portable engine. The MVP language set is **JavaScript + Lua + C#** (all pure-managed, all sandboxable). The `IScriptExecutor` seam makes Python purely additive later; the checklist's Python items map to **3.1.P1**. |
| **D6 The script API is capability-gated, not all-powerful** | `IWorkflowScriptApi` exposes: **always-on** — variables (get/set/delete/exists, staged as `VariableUpdates` per the ModuleResult convention), logging (debug/info/warn/error), utilities (guid/now/format/base64/hash/json/csv), workflow context (executionId/workflowId), `WaitAsync`; **gated by `ScriptExecutionConfig`** — HTTP (`AllowNetwork`, via `IHttpClientFactory`'s existing `dotflow.http` client, request-count capped) and file system (`AllowFileSystem` **default false** + `AllowedPaths`, reusing the Phase 2.5 path-security sandbox). **No raw database API** — the checklist's `QueryDatabase(connectionString, …)` is rejected: scripts that need data go through workflow nodes (2.4 database modules + named connections) instead of carrying raw connection strings (Q2). |
| **D7 Variable writes are staged, not direct** | Scripts mutate variables through the API, but writes are **collected and returned** as `ModuleResult.VariableUpdates` (the same mechanism `builtin.setvariable` uses) so the engine applies them transactionally after the node completes. Reads see execution-scope variables passed into the `ModuleExecutionContext`. No direct `IVariableStore` access from inside a running script. |
| **D8 `builtin.script` is a first-class module in the builtin family** | `ScriptModule` (`builtin.script`) — properties: `language` (dropdown: registered executor keys), `code` (Code editor), `timeoutSeconds`, `allowNetwork`, `allowFileSystem`, `allowedPaths`; inputs: `input` (object, optional); outputs: `result` + whatever the script returns as an object. Registered in `BuiltinModules.GetAll()` only when at least one executor is registered — schema's language `AllowedValues` reflects the registered set. `builtin.transform.script` stays as-is (typed, cached, transform-shaped); `builtin.script` is the general-purpose sibling (Q6). |
| **D9 Script libraries persist via the existing blob-store seam** | `ScriptLibraryDefinition` (id/name/description/language/code/exported functions) + `IScriptLibraryStore` with a blob-store-backed default (key `scripts/libraries/{id}.json`, using the same `IBlobStore` seam as 2.8's package archive) and in-memory fallback for provider-less hosts. Libraries are **prepended** to script code at execution (JS: injected as a module-object; Lua: preloaded via `require`; C#: prepended `#load`-style source) — no cross-language imports. |
| **D10 Script test endpoint = Minimal-API group under `/api/v1/scripts`** | `ScriptEndpoints.MapScriptEndpoints()`: `POST /api/v1/scripts/test` (code + language + inputs + config → outputs/logs/duration/errors), `GET /api/v1/scripts/languages` (registered executor keys), plus library CRUD (`GET/PUT/DELETE /api/v1/scripts/libraries[/{id}]`). ProblemDetails errors, `WorkflowWrite` policy on test + library writes, `WorkflowRead` on reads — all 2.7 conventions verbatim. |
| **D11 PropertyBinder expressions ride the existing `IExpressionEvaluator`** | The deferred Phase 1.4 item: `PropertyBinder` gains an **expression path** using the already-registered Jint evaluator (250 ms sandbox — right-sized for binding). Detection: an inner template that is **not** a pure `Variable.X`/`NodeId.Output` reference is treated as an expression (e.g. `{{Variable.Count > 5}}`, `{{1 + 2}}`), with references inside expressions resolved to evaluator variables first. Behavior is **opt-in via constructor flag** (`enableExpressions`, default true in hosts, false-able for compat) so the existing binder tests stay meaningful. Evaluation failures produce binding errors, never silent nulls. |
| **D12 Sandbox posture: deny-by-default for anything that touches the outside world** | `ScriptExecutionConfig` defaults: `TimeoutSeconds=30`, `MaxMemoryBytes=64 MB`, `AllowNetwork=false`, `AllowFileSystem=false`, `AllowedPaths=[]`, `MaxHttpRequests=10`. The node's properties can loosen these **up to host-configured ceilings** (`Scripting:MaxTimeoutSeconds` etc.) — a workflow author can never exceed what the host operator allows. Engines get no CLR access beyond the injected API object (Jint: no `SetTypeResolver`; MoonSharp: `CoreModules.Preset_SoftSandbox`; C#: `ForbiddenSyntaxWalker`). |

### TO RESOLVE 🤔

> All Q1–Q7 resolved (July 2026) — answers folded into the design decisions + slices below~ ✅

- [x] **Q1 MVP language set: is JavaScript + Lua + C# (Python deferred) acceptable?**
  - **RESOLVED:** Yes — Python deferred to **3.1.P1** (IronPython dialect lag; Python.NET needs native CPython). Revisit with Python.NET + containerized isolation.
- [x] **Q2 Script Database API: OK to reject the raw `QueryDatabase(connectionString, …)` checklist item?**
  - **RESOLVED:** Yes — no database API in scripts for MVP; workflows compose `builtin.database.*` nodes with script nodes instead. A named-connection-based script API (`QueryAsync(connectionName, sql, params)`) → **3.1.P2** if demanded.
- [x] **Q3 Script HTTP API default: `AllowNetwork` default false (deny-by-default)?**
  - **RESOLVED:** Yes — deny-by-default with per-node opt-in under host ceilings (D12), superseding the checklist's default-true.
- [x] **Q4 Library storage: blob-store seam (D9) or a dedicated `IScriptLibraryRepository` in persistence?**
  - **RESOLVED:** Blob-backed for MVP with an in-memory fallback (mirrors the 2.8 state-store pattern); repository promotion → **3.1.P3**.
- [x] **Q5 PropertyBinder expression detection: implicit (any non-reference template = expression, D11) or explicit marker?**
  - **RESOLVED:** Implicit, with the constructor opt-out — today non-reference templates resolve to null/unchanged, so semantics only improve.
- [x] **Q6 Should `builtin.script` support returning `ActivePorts` (control-flow from scripts)?**
  - **RESOLVED:** Not in MVP — scripts return data; routing stays with `builtin.condition`/`builtin.switch`. Revisit post-MVP (**3.1.P4**).
- [x] **Q7 Script async support: JS `async/await` + `WaitAsync` only, or full task-bridging for Lua/C# too?**
  - **RESOLVED:** **JS and C# support async natively** (Jint promises awaited; Roslyn scripts may `await`). **Lua is synchronous in MVP** (with `workflow.wait(ms)`), but the executor is built so coroutine bridging is purely additive: MoonSharp coroutines land post-MVP as **3.1.P5** with a concrete plan (see the P-slice) — the MVP `LuaScriptExecutor` must not preclude it (script function entry point + `DynValue`-based invocation kept coroutine-compatible).

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

## 3.1.0 Scripting Abstractions + JavaScript Executor ✅ DONE (`Workflow.Scripting/*`)

> **Purpose:** The seam everything else plugs into — `IScriptExecutor`, config/result contracts, and the first executor (Jint/JS), generalized from the proven evaluator posture~ ✨

**Complexity:** 🟡 Medium

### Tasks

- [x] **New project `Workflow.Scripting`** 📦 — references `Workflow.Core` only (+ Jint); wired into the solution + `Directory.Packages.props`
- [x] **`Abstractions/IScriptExecutor.cs`** — `LanguageId` (e.g. `"javascript"`), `DisplayName`, `ExecuteAsync(string code, ScriptExecutionContext context, CancellationToken ct) → ScriptExecutionResult`
- [x] **`Abstractions/ScriptExecutionContext.cs`** — `Inputs` (dict), `Variables` (read snapshot), `Api` (the bridge object, D6), `Config` (`ScriptExecutionConfig`), `ExecutionId`/`WorkflowId`/`NodeId`, `Logger`
- [x] **`Abstractions/ScriptExecutionConfig.cs` (D12)** — `TimeoutSeconds=30`, `MaxMemoryBytes=64MB`, `AllowNetwork=false`, `AllowFileSystem=false`, `AllowedPaths=[]`, `MaxHttpRequests=10`; `Clamp(hostCeilings)` helper so node-level config can never exceed host limits
- [x] **`Abstractions/ScriptExecutionResult.cs`** — `Success`, `ReturnValue` (object?/JsonElement), `VariableUpdates` (staged, D7), `Logs` (captured api log calls), `Error`/`Diagnostics`, `Duration`
- [x] **`Abstractions/IScriptExecutorFactory.cs`** + keyed-DI implementation (`GetExecutor(languageId)`, `GetRegisteredLanguages()`) — mirrors `KeyedExpressionEvaluatorFactory`
- [x] **`Executors/JavaScriptScriptExecutor.cs` (D2)** 🟨
  - [x] Jint engine per execution: strict mode, `MemoryLimit`/`TimeoutInterval`/recursion from config, linked-CTS interrupt (same pattern as `JintExpressionEvaluator`)
  - [x] Inject `workflow` API object + `input` global; marshal .NET↔JS via Jint interop + `JsonValueConverter` fallback for complex graphs
  - [x] Promise support: a returned promise is awaited (Q7); script errors → structured `ScriptExecutionResult.Error` (line info when available)
- [x] **DI:** `AddWorkflowScripting()` (factory + JS executor); host ceilings bound from `Scripting:*` config

### Tests (target ~12): → `Workflow.Tests/Scripting/JavaScriptScriptExecutorTests.cs`

- [x] `Execute_SimpleScript_ReturnsValue` · `Execute_InputData_Accessible` · `Execute_ObjectReturn_MarshalsToNet`
- [x] `Execute_SyntaxError_StructuredError` · `Execute_RuntimeError_StructuredError`
- [x] `Execute_Timeout_Enforced` · `Execute_MemoryLimit_Enforced` · `Execute_CancellationToken_Honoured`
- [x] `Execute_Promise_Awaited` *(Q7)* · `Execute_StrictMode_UndeclaredThrows`
- [x] `Config_Clamp_NodeCannotExceedHostCeilings` · `Factory_ResolvesRegisteredLanguages`

---

## 3.1.1 Unified Workflow Script API ✅ DONE (`Workflow.Scripting/Api/*`)

> **Purpose:** The `workflow` object scripts see — variables, logging, utilities always-on; HTTP/file capability-gated; no raw database access (D6/D7/Q2)~ ✨

**Complexity:** 🟠 Medium-High *(this is the sandbox boundary — every method is attack surface)*

### Tasks

- [x] **`Api/IWorkflowScriptApi.cs` + `WorkflowScriptApi.cs`** 🔧
  - [x] **Variables (D7):** `GetVariable(name)` · `SetVariable(name, value)` (staged) · `DeleteVariable(name)` (staged tombstone) · `VariableExists(name)`
  - [x] **Logging:** `LogDebug/LogInfo/LogWarning/LogError` — forwarded to the module logger **and** captured into `ScriptExecutionResult.Logs` (for the test endpoint)
  - [x] **Utilities:** `NewGuid()` · `Now()`/`UtcNow()` · `FormatDateTime(date, format)` · `Base64Encode/Decode` · `Hash(data, algorithm)` (SHA-256/SHA-512/MD5) · `ParseJson`/`ToJson` · `ParseCsv`/`ToCsv` (reuse the 2.5.a CsvHelper internals)
  - [x] **Workflow context:** `GetExecutionId()` · `GetWorkflowId()` · `GetNodeId()` · `WaitAsync(ms)` (CT-linked, capped by remaining timeout)
- [x] **Gated HTTP (D6/Q3)** 🌐 — `HttpGetAsync/HttpPostAsync/HttpPutAsync/HttpDeleteAsync(url, body?, headers?)`; throws a clear `ScriptSecurityException` when `AllowNetwork=false`; per-execution request counter enforcing `MaxHttpRequests`; uses the named `dotflow.http` client; responses surfaced as `{ status, headers, body }`
- [x] **Gated file system (D6)** 📁 — `ReadFileAsync/WriteFileAsync/FileExists/DeleteFile(path)`; throws when `AllowFileSystem=false`; every path validated against `AllowedPaths` via the 2.5.a path-security sandbox (traversal-proof)
- [x] **No database API** — document the decision + the node-composition alternative (Q2)
- [x] **Language bridging:** JS interop shapes verified (camelCase aliases where idiomatic); marshalling helpers shared for 3.1.2/3.1.3

### Tests (target ~16): → `Workflow.Tests/Scripting/WorkflowScriptApiTests.cs`

- [x] Variables: `SetVariable_StagedInResult` · `GetVariable_ReadsSnapshot` · `DeleteVariable_StagedTombstone` · `VariableExists_Works`
- [x] Logging: `Logs_CapturedAndForwarded` (all four levels)
- [x] Utilities: `Guid_Now_Format_Base64_Hash_Json_Csv_RoundTrips` *(theory-style batch)*
- [x] HTTP: `Http_Blocked_WhenNetworkDisallowed` · `Http_Allowed_MakesRequest` *(local test server)* · `Http_RequestCount_CapEnforced`
- [x] Files: `File_Blocked_WhenFileSystemDisallowed` · `File_AllowedPath_ReadsWrites` · `File_PathTraversal_Rejected` · `File_OutsideAllowedPaths_Rejected`
- [x] `Wait_RespectsCancellation` · `Api_FromJavaScript_EndToEnd` *(script calls each API family)*

---

## 3.1.2 C# Executor (Roslyn adapter) ✅ DONE (`Workflow.Scripting.Roslyn/Executors/*`)

> **Purpose:** C# joins the language set by adapting the existing 2.6.b core to `IScriptExecutor` — staying inside the Roslyn-quarantined project (D3)~ ✨

**Complexity:** 🟡 Medium *(the hard parts — compiler, forbidden-syntax, caching — already exist)*

### Tasks

- [x] **`Executors/CSharpScriptExecutor.cs`** — `LanguageId="csharp"`; compiles via `IRoslynScriptCompiler` (globals type exposing `Input`, `Workflow` api, `Variables`), executes via `CollectibleScriptRunner`, caches via `ICompiledScriptCache` (keyed by code hash)
- [x] **`ForbiddenSyntaxWalker` reuse** — same deny-list (no `System.IO` unless api-gated, no reflection, no P/Invoke, no `Environment`); extend with script-API-specific allowances
- [x] **Async:** scripts may `await` (Q7) — natively supported by Roslyn scripting
- [x] **DI:** `AddRoslynScripting()` gains the keyed executor registration; hosts without the Roslyn project simply lack the `"csharp"` key
- [x] Marshalling: `Input` as `IReadOnlyDictionary<string, object?>`; return value materialized via the existing `ScriptResultMaterializer`

### Tests (target ~8): → `Workflow.Tests/Scripting/CSharpScriptExecutorTests.cs`

- [x] `Execute_SimpleScript_ReturnsValue` · `Execute_InputAccessible` · `Execute_WorkflowApi_Callable`
- [x] `Execute_ForbiddenSyntax_Rejected` · `Execute_CompileError_StructuredDiagnostics`
- [x] `Execute_Async_Awaited` · `Execute_Timeout_Enforced` · `Cache_SecondRun_UsesCompiled`

---

## 3.1.3 Lua Executor (MoonSharp) ✅ DONE (`Workflow.Scripting.Lua/*`)

> **Purpose:** The one genuinely-new engine — MoonSharp in its own quarantined project (D4)~ ✨

**Complexity:** 🟠 Medium *(new dependency + table marshalling)*

### Tasks

- [x] **New project `Workflow.Scripting.Lua`** — MoonSharp package added to `Directory.Packages.props`; references `Workflow.Scripting`
- [x] **`LuaScriptExecutor.cs`** — `LanguageId="lua"`; `CoreModules.Preset_SoftSandbox` (no `io`/`os`/`load`); per-execution `Script` instance; instruction-count-based abort + linked-CTS timeout; memory observed via instruction budget (MoonSharp has no direct memory cap — document)
- [x] **Coroutine-ready execution shape (Q7):** the script body is loaded as a **Lua function and invoked via `DynValue`** (not `Script.DoString` top-level statements) so 3.1.P5 can later wrap the same entry point in `coroutine.create` + resume-loop without changing the executor contract; `coroutine.*` core module stays available inside scripts (pure-Lua coroutines work today — only .NET async *bridging* is deferred)
- [x] **API bridging:** register `IWorkflowScriptApi` via `UserData.RegisterType`; `workflow` global + `input` table; Lua tables ↔ .NET dictionaries/lists (recursive marshaller with depth cap, reusing `JsonValueConverter` shapes)
- [x] **Sync model (Q7):** Lua scripts are synchronous; `workflow.wait(ms)` provided; async HTTP api methods exposed as blocking wrappers with CT propagation
- [x] **DI:** `AddLuaScripting()` keyed registration
- [x] **Docs:** language nuances (1-based arrays, table semantics, coroutine status/roadmap) in the scripting guide

### Tests (target ~11): → `Workflow.Tests/Scripting/LuaScriptExecutorTests.cs`

- [x] `Execute_SimpleScript_ReturnsValue` · `Execute_InputTable_Accessible` · `Execute_TableReturn_MarshalsToNet` · `Execute_NestedTables_DepthCapped`
- [x] `Execute_ApiCalls_Work` *(variables + logging + utilities from Lua)*
- [x] `Execute_SyntaxError_StructuredError` · `Execute_RuntimeError_StructuredError`
- [x] `Execute_Timeout_Enforced` · `Sandbox_NoIoOsLoad_Modules` · `Http_Gated_FromLua`
- [x] `Execute_PureLuaCoroutines_Work` *(coroutine.create/resume/yield inside a script — guards the 3.1.P5-ready shape)*

---

## 3.1.4 `builtin.script` Module ✅ DONE (`Workflow.Modules/Builtin/Script/*`)

> **Purpose:** The workflow-facing node: pick a language, write code, run it in the sandbox with staged variable writes (D8)~ ✨

**Complexity:** 🟡 Medium

### Tasks

- [x] **`ScriptModule.cs` (`builtin.script`)** 🧩
  - [x] Properties: `language` (Dropdown, `AllowedValues` = registered executor keys) · `code` (Code editor) · `timeoutSeconds` (Number) · `allowNetwork`/`allowFileSystem` (Boolean) · `allowedPaths` (Json)
  - [x] Inputs: `input` (object, optional — flows to the script's `input` global); Outputs: `result` (object)
  - [x] `ExecuteAsync`: resolve executor via `IScriptExecutorFactory` (from `context.Services`), build `ScriptExecutionContext` (variables snapshot from `context.Variables`, config clamped to host ceilings), run, map `ScriptExecutionResult` → `ModuleResult` (`Ok(outputs, variableUpdates)` on success, `Fail` with diagnostics otherwise)
  - [x] `ValidateConfiguration`: language key registered, code non-empty, timeout within ceiling
- [x] **Registration:** in `BuiltinModules.GetAll()`/host wiring so the API host + engine both resolve it; language dropdown reflects registered executors at schema-build time
- [x] **No `ActivePorts` from scripts (Q6)** — document; scripts return data only
- [x] **Docs:** `docs/scripting.md` — the node, per-language examples, API reference, sandbox flags

### Tests (target ~10): → `Workflow.Tests/Modules/Script/ScriptModuleTests.cs`

- [x] `Execute_JavaScript_EndToEnd` · `Execute_Lua_EndToEnd` · `Execute_CSharp_EndToEnd` *(engine E2E via BuiltinModuleEndToEnd harness)*
- [x] `VariableUpdates_AppliedByEngine_DownstreamSees` · `Inputs_FlowIntoScript` · `Result_FlowsToOutputPort`
- [x] `UnknownLanguage_FailsValidation` · `EmptyCode_FailsValidation`
- [x] `NodeTimeout_OverHostCeiling_Clamped` · `ScriptFailure_ProducesModuleFailure`

---

## 3.1.5 Script Library System ✅ DONE (`Workflow.Scripting/Libraries/*`)

> **Purpose:** Reusable, per-language code snippets that scripts can import (D9)~ ✨

**Complexity:** 🟡 Medium

### Tasks

- [x] **`Libraries/ScriptLibraryDefinition.cs`** — `LibraryId`, `Name`, `Description`, `Language`, `Code`, `ExportedFunctions` (documentation metadata), `Dependencies` (other library ids, same language)
- [x] **`Libraries/IScriptLibraryStore.cs`** — `SaveAsync`/`GetAsync`/`GetAllAsync(language?)`/`DeleteAsync`; validation (id format, language key registered, dependency cycles via the 2.8.1-style resolver)
- [x] **`Libraries/BlobScriptLibraryStore.cs`** (default when a blob store exists) + `InMemoryScriptLibraryStore` fallback — mirrors the 2.8 state-store fallback pattern
- [x] **Import mechanics (per language):**
  - [x] JS: libraries injected as pre-evaluated module objects (`const lib = workflow.require('libraryId')`)
  - [x] Lua: preloaded into `package.preload` → `local lib = require('libraryId')`
  - [x] C#: library source prepended in dependency order (compile-time inclusion)
  - [x] Dependency-ordered load; missing/wrong-language imports → clear structured error
- [x] **`builtin.script` + test endpoint** accept a `libraries` list (explicit imports — no auto-injection)

### Tests (target ~10): → `Workflow.Tests/Scripting/ScriptLibraryTests.cs`

- [x] `Store_SaveGetDelete_RoundTrips` · `Store_ListByLanguage_Filters` · `Store_DependencyCycle_Rejected`
- [x] `Js_Require_CallsLibraryFunction` · `Lua_Require_CallsLibraryFunction` · `CSharp_Prepend_CallsLibraryFunction`
- [x] `Import_MissingLibrary_ClearError` · `Import_WrongLanguage_ClearError`
- [x] `Dependencies_LoadInOrder` · `BlobStore_Fallback_InMemoryWhenNoProvider`

---

## 3.1.6 Script Endpoints ✅ DONE (`/api/v1/scripts`)

> **Purpose:** Test-before-you-save + library management over HTTP, on the 2.7 conventions (D10)~ ✨

**Complexity:** 🟢 Low-Medium

### Tasks

- [x] **`V1/ScriptEndpoints.cs`** (`MapScriptEndpoints`) 🗺️
  - [x] `POST /api/v1/scripts/test` — `{ language, code, inputs?, libraries?, config? }` → `{ success, result, logs, variableUpdates, durationMs, error? }`; config clamped to host ceilings; `WorkflowWrite` policy; `422` for unknown language/empty code
  - [x] `GET /api/v1/scripts/languages` — registered executor keys + display names; `WorkflowRead`
  - [x] `GET/PUT/DELETE /api/v1/scripts/libraries[/{id}]` — library CRUD over `IScriptLibraryStore`; `?language=` filter on list; `WorkflowRead`/`WorkflowWrite`
- [x] **Contracts:** `ScriptTestRequest/ResultDto`, `ScriptLibraryDto` in `Contracts/Scripts/`
- [x] **Swagger:** tagged `Scripts`; request/response examples
- [x] **Docs:** endpoint section in `docs/rest-api.md` + cross-link from `docs/scripting.md`

### Tests (target ~10): → `Workflow.Tests/Api/V1/ScriptEndpointsTests.cs`

- [x] `Test_JavaScript_ReturnsResultAndLogs` · `Test_Lua_ReturnsResult` · `Test_CSharp_ReturnsResult`
- [x] `Test_UnknownLanguage_422` · `Test_EmptyCode_422` · `Test_ScriptError_StructuredErrorIn200Body`
- [x] `Test_ConfigCeilings_Clamped` · `Languages_ListsRegistered`
- [x] `Libraries_CrudRoundTrip` · `Test_WithLibrary_ImportsWork`

---

## 3.1.7 PropertyBinder Expression Evaluation ✅ DONE (deferred from Phase 1.4)

> **Purpose:** `{{Variable.Count > 5}}`, `{{1 + 2}}`, `{{Variable.Name + '!'}}` — inline expressions in property bindings via the existing Jint evaluator (D11)~ ✨

**Complexity:** 🟡 Medium *(compat-sensitive — the binder runs on every node execution)*

### Tasks

> **Status (2026-07-19):** ✅ Complete. `PropertyBinder` gains an optional `IExpressionEvaluator` + `enableExpressions` flag; non-pure-reference templates are evaluated as sandboxed JS with reference tokens resolved into scope (built-ins like `Math`/`JSON` left intact), whole-template results keep their type, mixed templates interpolate, failures become binding errors, and token spans are memoized. `IPropertyBinder` is registered in DI with the default evaluator so `NodeExecutor` gets expression support. 12 new tests in `PropertyBinderExpressionTests.cs` pass; the 23 existing binder tests stay green. Docs added to `docs/scripting.md` + `docs/module-author-guide.md`.

- [x] **Detection (Q5):** inner template that isn't a pure `Variable.X`/`NodeId.Output` reference → expression path; pure references keep the existing fast path untouched
- [x] **Evaluation:** references inside the expression are resolved to evaluator variables first (`Variable.Count` → injected `Count`-style scope), then evaluated via `IExpressionEvaluator` (Jint — 250 ms/4 MB sandbox is already right-sized); supports arithmetic/comparison/logical/string ops per the checklist (Jint gives them all for free)
- [x] **Whole-template vs interpolated:** single-expression templates preserve the raw typed result (like references today); mixed text interpolates `ToString`
- [x] **Caching:** memoize prepared scripts per expression string (Jint `Engine` reuse or parsed-script cache) — the binder is hot-path
- [x] **Failure semantics:** parse/runtime/timeout → binding error listing the expression (never silent null)
- [x] **Compat:** `enableExpressions` constructor flag (default true); `PropertyBinder` acquires the evaluator optionally (null → reference-only behavior, existing tests unchanged)
- [x] **Docs:** expression syntax in the module author guide + `docs/scripting.md`

### Tests (target ~10): → `Workflow.Tests/Modules/Binding/PropertyBinderExpressionTests.cs`

- [x] `Arithmetic_Evaluates` · `Comparison_Evaluates` · `Logical_Evaluates` · `StringConcat_Evaluates`
- [x] `VariableReference_InExpression_Resolves` · `NodeOutput_InExpression_Resolves`
- [x] `WholeTemplate_PreservesType` · `MixedTemplate_Interpolates`
- [x] `InvalidExpression_BindingError` · `ExpensiveExpression_TimesOut`
- [x] *(regression)* existing reference-only binder tests stay green with expressions enabled

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

### 3.1.P5 Lua coroutine bridging 🌙 *(Q7 — plan agreed, bridging deferred)*
Bridge .NET async into Lua coroutines so `workflow.httpGet(...)` and friends **yield instead of block**. The concrete plan:

1. **Entry point (already MVP-ready):** the 3.1.3 executor loads the script body as a Lua function invoked via `DynValue` — 3.1.P5 wraps that same function in `Script.CreateCoroutine(fn)` without contract changes.
2. **Async API surface:** each gated async API method gets a coroutine-aware variant registered as a **yielding callback** (`DynValue.NewYieldReq`): the Lua side calls `workflow.httpGet(url)`, the callback starts the .NET `Task` and yields; the C# resume-loop (`coroutine.Resume()`) awaits the task off-thread and resumes the coroutine with the marshalled result.
3. **Resume loop:** `while (coroutine.State != CoroutineState.Dead) { result = coroutine.Resume(pendingValue); if (result is yield-request) pendingValue = await RunPendingTask(ct); }` — CT-linked so timeout/cancellation aborts between resumes (MoonSharp's `AutoYieldCounter` keeps CPU-bound segments interruptible too).
4. **Compat:** synchronous blocking wrappers stay for scripts that don't opt in; a `workflow.async = true` script pragma (or config flag) selects the coroutine execution path.
5. **Tests (~6):** yield-resume round-trip, HTTP-yield end-to-end, timeout across a suspended coroutine, cancellation between resumes, error propagation from a faulted task into a Lua `error()`, mixed sync+async API usage.

---

## Success Criteria ✅

- [x] A workflow with a `builtin.script` node runs JavaScript, Lua, and C# scripts end-to-end through the engine, with inputs in and results out
- [x] Scripts read variables and stage writes that downstream nodes observe (via `VariableUpdates`)
- [x] The sandbox holds: timeout/memory enforced per engine; network and file access **denied by default** and gated by config under host ceilings; path traversal impossible; no raw database access from scripts
- [x] Script libraries round-trip through the store and import correctly in all three languages
- [x] `POST /api/v1/scripts/test` executes all registered languages and returns outputs + captured logs + duration
- [x] `{{Variable.Count > 5}}`-style expressions evaluate in property bindings with the documented failure semantics — and every pre-existing reference-only binding behaves identically
- [x] All existing tests stay green — `IExpressionEvaluator`, transform-script, and binder behavior are provably unchanged
