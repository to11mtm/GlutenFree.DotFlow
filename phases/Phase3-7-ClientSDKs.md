# Phase 3.7: Client SDKs — "Bindings" 🔌📦

Made with 💖 by Ami-Chan! UwU ✨

[Back to Phase 3](Phase3-AdvancedFeatures.md) | [All Phases](README.md)

---

## Overview

Phase 3.7 ships **official client SDKs** for GlutenFree.DotFlow in **C#**, **TypeScript/JavaScript**,
and **Python**, so external apps can drive the workflow engine without hand-rolling HTTP calls. Each
SDK wraps the **shipped REST API** (workflows, executions, modules, variables, scripts, monitoring)
plus the **3.2 SignalR real-time hub**, with idiomatic async, typed models, docs, and examples — and
is **package-ready** for NuGet / npm / PyPI.

Unlike a green-field effort, the **C# SDK is largely extractable** from the typed clients already
shipped in `Workflow.UI.Client/Api/*` (`WorkflowsClient`, `ExecutionsClient`, `ModulesClient`,
`ScriptsClient`, `SystemClient`, `RealTimeClient` + DTOs), and **all three SDKs share one contract
source of truth**: the generated **OpenAPI v1** document (`/swagger/v1/swagger.json`) + the
documented [`docs/rest-api.md`](../docs/rest-api.md).

> **CopilotNote:** Hot paths: a new top-level **`clients/`** tree (matching the existing empty
> `clients` solution folder) with `clients/dotnet` (`GlutenFree.DotFlow.Client`), `clients/typescript`
> (`@glutenfree/dotflow-client`), and `clients/python` (`dotflow-client`). The C# SDK **reuses** the
> UI's clean, framework-free API clients + DTOs; TS/Python **generate wire types from OpenAPI** and
> hand-write thin idiomatic facades + real-time. Tests: xUnit (C#), Vitest (TS), pytest (Python).
> **Packaging is in-scope; publishing to public registries is a secret-gated release step** (no
> credentials in the repo / this environment) — CI builds + tests + packs, humans push on a tag~ 🌸

> **Reality-check note (July 2026):** the §3.7 checklist in
> [`Phase3-AdvancedFeatures.md`](Phase3-AdvancedFeatures.md#37-client-sdks-week-22) predates the
> 3.3 UI work and the 2.7/2.8 API maturity. Since then:
> - **A complete, documented REST API exists** (2.7): workflows, executions, modules, variables,
>   scripts, monitoring, webhooks — with **OpenAPI v1** at `/swagger/v1/swagger.json`, XML-comment
>   enrichment, and both **API-key (`X-API-Key`)** and **JWT bearer** security schemes.
> - **A SignalR real-time hub exists** (3.2): typed `Execution*`/`Node*`/`Progress`/`Snapshot`
>   events + `SubscribeToExecution`/`SubscribeToWorkflow`/`SubscribeToAll` methods.
> - **Idiomatic C# clients already exist** (3.3): `Workflow.UI.Client/Api/*` are framework-free,
>   `HttpClient`-based, ProblemDetails-aware typed clients + a `HubConnection` wrapper — i.e. **90%
>   of the C# SDK is already written**, just coupled to the WASM UI project.
>
> **The genuine work:** (1) **extract/generalize** the C# clients into a standalone, packable
> `GlutenFree.DotFlow.Client` with a friendly facade + auth config; (2) **build** the TS + Python
> SDKs (OpenAPI-typed + hand-written facades + real-time); (3) **examples, docs, versioning, and CI
> packaging**. This plan reconciles the checklist and supersedes it.

**Timeline:** ~1.5–2 weeks (Week 34 — the checklist's original "Week 22" renumbered to follow 3.6).
**Complexity:** 🟠 Medium-High — the C# SDK is mostly reuse, but **three languages + real-time +
packaging/CI** is broad, and the TS/Python real-time clients + release plumbing are new surface.

---

## Confirmed Design Decisions ✅

| # | Decision |
|---|----------|
| **D1 A dedicated `clients/` tree (one folder per language)** | `clients/dotnet` (NuGet `GlutenFree.DotFlow.Client`), `clients/typescript` (`@glutenfree/dotflow-client`), `clients/python` (`dotflow-client`). Matches the existing empty **`clients`** solution folder. Each is independently versioned + packaged. |
| **D2 The C# SDK is the reference, extracted from the shipped UI clients** | Create `GlutenFree.DotFlow.Client` (multi-target `netstandard2.0;net8.0`) from `Workflow.UI.Client/Api/*` — the framework-free `HttpClient` clients (`WorkflowsClient`/`ExecutionsClient`/`ModulesClient`/`ScriptsClient`/`SystemClient`), the `RealTimeClient` (`HubConnection`), and the DTOs. The **UI migrates to reference it** (removing its `Api/*` duplication; the UI keeps only its Blazor auth/localStorage wiring) so there's one source of truth (Q2). |
| **D3 OpenAPI is the contract source of truth for TS + Python** | The build exports `openapi/v1.json` (from `/swagger/v1/swagger.json`); TS types are generated with `openapi-typescript`, Python models with `datamodel-code-generator`. The **facades are hand-written** for an idiomatic feel (grouped methods, real-time), over the generated types. A drift-guard regenerates in CI and fails on diff. |
| **D4 A friendly facade + pluggable auth in every SDK** | Each SDK exposes a top-level `DotFlowClient` (C#/TS) / `DotFlowClient` (Python) constructed from a base URL + an **auth option** (API key **or** bearer token, or a token provider), surfacing grouped sub-clients: `.Workflows`, `.Executions`, `.Modules`, `.Variables`, `.Scripts`, `.System`, `.RealTime`. Errors map ProblemDetails → a typed `DotFlowApiError`/exception. |
| **D5 Idiomatic async + real-time per language** | **C#**: `Task`/`async`, `IAsyncEnumerable` where natural, `HubConnection` events. **TS**: Promises, `@microsoft/signalr` for the hub, ESM + CJS builds. **Python**: `httpx` + `asyncio` (async) with a sync convenience wrapper; real-time via `signalrcore` (Q4). |
| **D6 Package-ready, not auto-published (secret-gated release)** | CI **builds, tests, and packs** (`.nupkg` / npm `.tgz` / wheel+sdist) on every push + tag; **actual publishing to NuGet/npm/PyPI is a manual, secret-gated release step** — no registry credentials live in the repo or this environment. A `RELEASING.md` runbook documents the push. (D-firm: never commit secrets.) |
| **D7 SemVer, tracking API v1** | All three SDKs share a coordinated SemVer line (e.g. `0.1.0` → `1.0.0` at API-v1 GA), each with a `CHANGELOG.md`; release tags `sdk/<lang>/vX.Y.Z`. A compatibility note pins each SDK to API `v1`. |
| **D8 Real-time mirrors the 3.2 hub contract** | The hub method names (`SubscribeToExecution`/`Workflow`/`All`) + event payloads (`ExecutionStarted/Completed/Failed`, `NodeStarted/Completed/Failed`, `ExecutionProgress`, `ExecutionSnapshot`) are mirrored verbatim; the C# SDK reuses the 3.3 `RealTimeDtos`, TS/Python mirror them. |
| **D9 Contracts-only wire models** | SDK models mirror the **wire DTOs** only (camelCase JSON), never server internals (no LanguageExt, no engine types). This is the same D2 boundary the UI already honors. |
| **D10 Docs + examples per scenario** | Each SDK ships a `README.md` (install + quickstart) and a shared **examples matrix**: quickstart/auth, create a workflow, execute + poll, monitor via real-time, variable CRUD, browse modules, run a script test. Example code is compiled/type-checked in CI (D7 testing). |
| **D11 Testing per language** | **C#** xUnit over a `FakeHttpMessageHandler` (reuse the 3.3 pattern) + an opt-in integration suite against the `WebApplicationFactory` harness. **TS** Vitest + a fetch/`msw` mock. **Python** pytest + `respx`. Plus a **cross-SDK smoke test** hitting a live in-memory API for parity. |
| **D12 One coordinated release surface** | A `clients/README.md` indexes the three SDKs, their package names, versions, and the release runbook; `docs/sdks.md` is the user-facing landing page. |

---

## TO RESOLVE 🤔

> Proposed answers below so work can proceed; please confirm/override~ ✅

- [ ] **Q1 Scope: all three SDKs in this phase, or C# first with TS + Python as follow-ups?**
  - **Proposed:** **All three in-phase**, but sliced so C# lands first (it's mostly extraction and unblocks the shared OpenAPI/examples), then TS, then Python. If time-boxing is needed, TS + Python can split into `Phase3-7b`/`3-7c` breakouts — the master stays the index.
- [ ] **Q2 C# SDK: extract the UI clients into `GlutenFree.DotFlow.Client` (UI references it), or a fresh mirror (UI keeps its copy)?**
  - **Proposed:** **Extract + reference** (DRY) — the UI's `Api/*` clients are already framework-free and clean; move them to the SDK and have `Workflow.UI.Client` reference it, keeping only Blazor-specific auth/localStorage in the UI. One source of truth; the UI's 283 tests are the safety net.
- [ ] **Q3 TS/Python models: generate from OpenAPI, or hand-write?**
  - **Proposed:** **Generate** wire types from `openapi/v1.json` (`openapi-typescript`, `datamodel-code-generator`) with a CI drift-guard, and **hand-write** the idiomatic facades + real-time over them. Best of both: no drift, nice ergonomics.
- [ ] **Q4 Python real-time: `signalrcore` now, or REST/polling for Python with SignalR deferred?**
  - **Proposed:** Try **`signalrcore`** (community SignalR client) for parity; if it's flaky against ASP.NET Core SignalR, ship Python with **REST + a polling helper** for MVP and mark native SignalR **3.7.P1**. C#/TS get native real-time regardless.
- [ ] **Q5 Publishing posture: CI build/test/pack + secret-gated manual publish (no auto-push, no secrets in repo)?**
  - **Proposed:** **Yes.** CI produces the packages as artifacts on tag; publishing to NuGet/npm/PyPI is a documented manual step gated on registry secrets held **outside** the repo. Nothing here pushes to a registry or commits a token.
- [ ] **Q6 Package/namespace naming: NuGet `GlutenFree.DotFlow.Client`, npm `@glutenfree/dotflow-client`, PyPI `dotflow-client`?**
  - **Proposed:** Those names (C# namespace `GlutenFree.DotFlow.Client`; TS import `@glutenfree/dotflow-client`; Python import `dotflow_client`). Adjust if the org/registry handles differ.

---

## Pre-Existing Work (from earlier phases) ✅

| Component | File / Endpoint | Status |
|-----------|-----------------|--------|
| REST API (workflows/executions/modules/variables/scripts/monitoring) | `Workflow.Api/V1/*` (2.7) | ✅ The SDK surface |
| OpenAPI v1 document + security schemes | `/swagger/v1/swagger.json`, `SwaggerConfiguration.cs` (2.7.8) | ✅ Contract source of truth (D3) |
| SignalR real-time hub + typed events | `Workflow.Api/RealTime/*` (3.2) | ✅ Mirrored real-time surface (D8) |
| Idiomatic C# API clients (HttpClient, ProblemDetails) | `Workflow.UI.Client/Api/{Workflows,Executions,Modules,Scripts,System}Client.cs` (3.3) | ✅ Extracted → C# SDK (D2) |
| C# hub wrapper | `Workflow.UI.Client/Api/RealTimeClient.cs` (3.3) | ✅ Extracted → C# SDK real-time |
| Wire DTOs (workflows/executions/modules/scripts/real-time) | `Workflow.UI.Client/Api/Dtos/*` (3.3) | ✅ SDK models (D9) |
| `ApiHttp`/`ApiError`/`AuthMessageHandler` plumbing | `Workflow.UI.Client/Api/*` (3.3) | ✅ Generalized → SDK core |
| REST API guide | `docs/rest-api.md` (2.7) | ✅ Basis for SDK docs (D10) |
| `clients` solution folder (empty placeholder) | `Workflow.sln` | ✅ Home for `clients/*` (D1) |
| Test harnesses (`FakeHttpMessageHandler`, `WebApplicationFactory`) | `Workflow.Tests.UI/Api/*`, `Workflow.Tests/Api/V1/*` (2.7/3.3) | ✅ Reused for C# SDK tests (D11) |

> **CopilotNote:** The C# SDK is **~90% written already** — the UI's `Api/*` is a clean, framework-free
> HTTP+SignalR client. Phase 3.7's real cost is **(a)** the extraction + a friendly facade + packaging,
> and **(b)** the **TS and Python** SDKs, which are fresh but ride the **OpenAPI** contract. Budget
> risk on **TS/Python real-time** and **release/CI plumbing** — not on the REST surface~ 💖

---

## Architecture 🏗️

```text
                       ┌───────────────────────────────┐
                       │  Workflow.Api (REST + SignalR) │
                       │  OpenAPI v1: /swagger/v1.json  │
                       └───────────────┬───────────────┘
                          contract source of truth (D3)
        ┌───────────────────────────────┼───────────────────────────────┐
        ▼                               ▼                               ▼
┌───────────────┐             ┌───────────────────┐           ┌───────────────────┐
│ clients/dotnet│             │ clients/typescript│           │ clients/python    │
│ GlutenFree.   │             │ @glutenfree/      │           │ dotflow-client    │
│ DotFlow.Client│             │ dotflow-client    │           │ (httpx + asyncio) │
│  (extracted   │             │ (fetch + @microsoft│          │  models: datamodel│
│  from UI Api) │             │  /signalr; types: │           │  -code-generator; │
│  HttpClient + │             │  openapi-typescript)│         │  RT: signalrcore  │
│  HubConnection│             │                   │           │  or polling (Q4)  │
└───────┬───────┘             └─────────┬─────────┘           └─────────┬─────────┘
        │  DotFlowClient facade: .Workflows .Executions .Modules .Variables .Scripts .System .RealTime
        └───────────────────────────────┴───────────────────────────────┘
                    ▲ same wire DTOs (camelCase JSON), same auth (API key / bearer)

  ← Workflow.UI.Client references clients/dotnet (D2), removing its Api/* duplication
  CI: build + test + pack (.nupkg / npm .tgz / wheel) → artifacts; publish = secret-gated manual (D6)
```

---

## Proposed File Layout 🗂️

```text
openapi/
  v1.json                              (exported from /swagger/v1/swagger.json; drift-guarded)
clients/
  README.md                            (index: packages, versions, release runbook)
  RELEASING.md                         (secret-gated publish steps for NuGet/npm/PyPI)
  dotnet/
    GlutenFree.DotFlow.Client/         (netstandard2.0;net8.0)
      DotFlowClient.cs                 (facade + auth + options)
      Http/DotFlowHttp.cs · DotFlowApiError.cs · AuthHandler.cs
      Workflows/… Executions/… Modules/… Variables/… Scripts/… System/…  (extracted clients)
      RealTime/RealTimeClient.cs       (HubConnection)
      Models/*                         (wire DTOs)
    GlutenFree.DotFlow.Client.Tests/   (xUnit; FakeHttpMessageHandler + WebAppFactory integration)
    examples/                          (dotnet console samples)
  typescript/
    src/{client,workflows,executions,modules,variables,scripts,system,realtime,errors}.ts
    src/generated/                     (openapi-typescript output)
    test/*                             (Vitest + fetch mock)
    examples/*  package.json  tsconfig.json  tsup.config.ts
  python/
    dotflow_client/{client,workflows,executions,modules,variables,scripts,system,realtime,errors}.py
    dotflow_client/models/             (datamodel-code-generator output)
    tests/*                            (pytest + respx)
    examples/*  pyproject.toml  README.md
docs/sdks.md                           (landing page + scenario examples index)
.github/workflows/sdk-*.yml            (build/test/pack per language; publish gated on secrets)
```

---

## Slices & Dependencies 🧭

| Slice | Scope | Depends on |
|-------|-------|-----------|
| 3.7.0 Shared foundation: OpenAPI export + `clients/` scaffolding + versioning/CHANGELOG + examples matrix | — |
| 3.7.1 C# SDK: extract `GlutenFree.DotFlow.Client` + facade + auth + real-time + pack + tests | 3.7.0 |
| 3.7.2 TypeScript SDK: `@glutenfree/dotflow-client` (OpenAPI types + facade + `@microsoft/signalr`) + pack + tests | 3.7.0 |
| 3.7.3 Python SDK: `dotflow-client` (OpenAPI models + httpx/asyncio facade + real-time) + wheel + tests | 3.7.0 |
| 3.7.4 Examples + docs + CI/CD packaging + release runbook | 3.7.1–3.7.3 |

---

## 3.7.0 Shared Foundation 🧱

> **Purpose:** The contract export, repo scaffolding, versioning, and the shared examples matrix
> every SDK implements.

**Complexity:** 🟢 Low-Medium

### Tasks

- [ ] **Export the OpenAPI document** — a repeatable step (a `dotnet` run/test or a small tool) that writes `openapi/v1.json` from the API's Swagger generator; a **drift-guard** test/CI job regenerates and fails on diff
- [ ] **`clients/` scaffolding** — folders + `clients/README.md` (index) + `clients/RELEASING.md` (secret-gated publish runbook) + a shared **examples matrix** doc (the seven scenarios each SDK must show)
- [ ] **Versioning + CHANGELOG convention** — SemVer line, `sdk/<lang>/vX.Y.Z` tags, per-SDK `CHANGELOG.md` seeds, API-`v1` compatibility note
- [ ] **Register `clients` in `Workflow.sln`** — add `GlutenFree.DotFlow.Client` (+ its test project) under the `clients` solution folder

### Tests

- [ ] `OpenApi_Export_MatchesLive` *(drift-guard)* · scaffolding present · `Solution_Builds_WithClients`

---

## 3.7.1 C# SDK — `GlutenFree.DotFlow.Client` 💎

> **Purpose:** The reference SDK, extracted from the shipped UI clients, with a friendly facade,
> auth, real-time, XML docs, and a `.nupkg`.

**Complexity:** 🟡 Medium *(mostly extraction + a facade; the risk is decoupling from the UI)*

### Tasks

- [ ] **Create `clients/dotnet/GlutenFree.DotFlow.Client`** (`netstandard2.0;net8.0`) — move the framework-free `Workflow.UI.Client/Api/*` clients + DTOs + `ApiHttp`/`ApiError` here (namespace `GlutenFree.DotFlow.Client`)
- [ ] **`DotFlowClient` facade** — constructed from `baseUrl` + `DotFlowClientOptions` (API key **or** bearer **or** a token provider); exposes `.Workflows`, `.Executions`, `.Modules`, `.Variables`, `.Scripts`, `.System`, `.RealTime`; owns the `HttpClient` + auth handler
- [ ] **Variables client** — add a `VariablesClient` over `/api/v1/variables` (the UI didn't need it) — get/set/list/delete per scope
- [ ] **Real-time** — the extracted `RealTimeClient` (`HubConnection`), with `ConnectAsync` + typed events + `SubscribeTo{Execution,Workflow,All}`
- [ ] **XML docs + packaging** — full XML doc comments, `GeneratePackageOnBuild`, package metadata (id `GlutenFree.DotFlow.Client`, authors, license, repo, README, `PackageReadmeFile`), symbols; `dotnet pack` produces a `.nupkg`
- [ ] **UI migration (Q2)** — repoint `Workflow.UI.Client` at the SDK, delete its duplicated `Api/*`, keep Blazor auth/localStorage; the 283 UI tests stay green
- [ ] **Examples** — a `dotnet` console sample per scenario

### Tests: → `clients/dotnet/GlutenFree.DotFlow.Client.Tests`

- [ ] `Client_Workflows_CrudRoundTrips` · `Client_Executions_StartStatusCancel` · `Client_Variables_Crud` · `Client_Scripts_Test` · `Client_Modules_List`
- [ ] `Auth_ApiKey_StampsHeader` · `Auth_Bearer_StampsHeader` · `Error_ProblemDetails_Typed`
- [ ] `RealTime_Subscribe_ReceivesEvents` *(integration, WebAppFactory)* · `Pack_ProducesNupkg`

---

## 3.7.2 TypeScript SDK — `@glutenfree/dotflow-client` 🟨

> **Purpose:** An idiomatic TS SDK (Promises, ESM+CJS), OpenAPI-typed, with `@microsoft/signalr`
> real-time, and an npm tarball.

**Complexity:** 🟡 Medium

### Tasks

- [ ] **Package scaffolding** — `package.json` (`@glutenfree/dotflow-client`, ESM+CJS via `tsup`), `tsconfig`, `.npmignore`; TS types generated from `openapi/v1.json` (`openapi-typescript`) into `src/generated/`
- [ ] **`DotFlowClient` facade** — `new DotFlowClient({ baseUrl, apiKey | token | getToken })`; grouped sub-clients (`workflows`, `executions`, `modules`, `variables`, `scripts`, `system`, `realtime`); `fetch`-based HTTP with ProblemDetails → `DotFlowApiError`
- [ ] **Real-time** — `@microsoft/signalr` `HubConnection` wrapper with typed event callbacks + `subscribeToExecution/Workflow/All`
- [ ] **Build + pack** — `tsup` build → `dist/`; `npm pack` produces a `.tgz`; typedoc/README
- [ ] **Examples** — a Node + browser snippet per scenario

### Tests: → `clients/typescript/test` (Vitest)

- [ ] `workflows.crud` · `executions.startStatus` · `variables.crud` · `scripts.test` · `modules.list` *(fetch mock)*
- [ ] `auth.apiKey/bearer.header` · `error.problemDetails.typed`
- [ ] `types.match.openapi` *(drift-guard)* · `build.emitsEsmAndCjs`

---

## 3.7.3 Python SDK — `dotflow-client` 🐍

> **Purpose:** An idiomatic Python SDK (`httpx` + `asyncio`, type hints, docstrings), OpenAPI-typed
> models, with real-time, and a wheel.

**Complexity:** 🟡 Medium

### Tasks

- [ ] **Package scaffolding** — `pyproject.toml` (`dotflow-client`, import `dotflow_client`), models generated from `openapi/v1.json` (`datamodel-code-generator`)
- [ ] **`DotFlowClient` facade** — `DotFlowClient(base_url, api_key=… | token=… | token_provider=…)`; async sub-clients (`.workflows`, `.executions`, `.modules`, `.variables`, `.scripts`, `.system`, `.realtime`) over `httpx.AsyncClient`; ProblemDetails → `DotFlowApiError`; a small **sync** convenience wrapper
- [ ] **Real-time (Q4)** — `signalrcore` wrapper with typed callbacks + `subscribe_to_execution/workflow/all`; **or** a REST polling helper if SignalR is flaky (native → 3.7.P1)
- [ ] **Build + wheel** — `python -m build` → wheel + sdist; type hints (`py.typed`); docstrings; README
- [ ] **Examples** — an asyncio + sync snippet per scenario

### Tests: → `clients/python/tests` (pytest + respx)

- [ ] `test_workflows_crud` · `test_executions_start_status` · `test_variables_crud` · `test_scripts_test` · `test_modules_list`
- [ ] `test_auth_apikey/bearer_header` · `test_error_problemdetails`
- [ ] `test_models_match_openapi` *(drift-guard)* · `test_build_wheel`

---

## 3.7.4 Examples + Docs + CI/CD Packaging 📚⚙️

> **Purpose:** The shared examples matrix, user docs, and CI that builds/tests/packs each SDK —
> with publishing left as a secret-gated manual step.

**Complexity:** 🟡 Medium

### Tasks

- [ ] **Examples matrix** — the seven scenarios (quickstart+auth, create workflow, execute+poll, real-time monitor, variable CRUD, browse modules, run script test) implemented in **all three** SDKs, compiled/type-checked in CI
- [ ] **Docs** — `docs/sdks.md` landing page (install per language + scenario links) + per-SDK `README.md`; cross-link `docs/rest-api.md` + `docs/realtime.md`
- [ ] **CHANGELOGs + RELEASING** — seed each `CHANGELOG.md`; `clients/RELEASING.md` documents the secret-gated publish (NuGet `dotnet nuget push`, npm `npm publish`, PyPI `twine upload`) with credentials **outside** the repo
- [ ] **CI/CD** — `.github/workflows/sdk-dotnet.yml` / `sdk-typescript.yml` / `sdk-python.yml`: build + test + **pack** on push/tag, upload artifacts; a **publish job gated on a tag + repository secrets** (does nothing without secrets). No tokens committed
- [ ] **Cross-SDK smoke** — a CI job that boots the in-memory API and runs a minimal parity check per SDK

### Tests

- [ ] Example code compiles/type-checks (all three) · CI `pack` jobs green · `Publish_Job_NoOps_WithoutSecrets`

---

## Agent Implementation Instructions 🤖

> **Audience:** an AI coding agent implementing this phase. Follow the same loop and guardrails as
> [Phase 3.4](Phase3-4-ScriptEditor.md#agent-implementation-instructions-); highlights specific to 3.7:

- **Slice order:** **3.7.0 first** (OpenAPI export + scaffolding), then **3.7.1 (C#)** — it's mostly
  extraction and validates the contract — then **3.7.2 (TS) ∥ 3.7.3 (Python)**, then **3.7.4**.
- **Never commit secrets or publish to a registry.** CI **packs** artifacts; publishing is a
  documented, secret-gated manual step. Do not add NuGet/npm/PyPI tokens anywhere. `Publish` jobs
  must no-op without repository secrets.
- **OpenAPI is the source of truth** for TS/Python types — generate, don't hand-write models; add a
  CI drift-guard that regenerates and fails on diff. Hand-write only the idiomatic facades.
- **C# extraction (Q2):** move the UI's `Api/*` verbatim into the SDK, then repoint the UI at it and
  delete the duplication. The UI's **283 tests are the regression net** — keep them green. The UI
  keeps only its Blazor auth/localStorage.
- **Contracts-only (D9):** SDK models mirror the wire DTOs (camelCase JSON) — no LanguageExt, no
  engine/persistence types. The same D2 boundary the UI honors.
- **Real-time parity:** mirror the 3.2 hub method names + event payloads exactly (see
  `Workflow.Api/RealTime/*` + `RealTimeDtos`). C# reuses `HubConnection`; TS uses `@microsoft/signalr`;
  Python uses `signalrcore` (or ships REST/polling if flaky — 3.7.P1).
- **Test per language** with mocked HTTP (`FakeHttpMessageHandler` / `msw`|fetch-mock / `respx`) +
  an opt-in live-API integration/parity smoke. Every example must compile/type-check in CI.
- **Bookkeeping ritual:** check off Tasks **and** Tests per slice, flip slice headers to `✅ DONE`,
  then at phase end check the Success Criteria, add an Overview completion banner, and update the
  `Phase3-AdvancedFeatures.md` §3.7 + `phases/README.md` pointers — exactly as 3.1–3.6 were closed.
  Track a todo per slice (`3-7-0`…`3-7-4`).
- **Repo gotchas:** `Workflow.sln` (add the C# SDK + test project under the `clients` folder);
  Central Package Management for the C# SDK (add `openapi-typescript`/`datamodel-code-generator` as
  dev-tool deps in their own ecosystems, not CPM); PowerShell has no `&&`.

---

## Post-MVP Slices 🚧 *(deferred — not blocking 4.x)*

### 3.7.P1 Native Python SignalR 🐍📡 *(Q4)*
Replace the Python polling fallback with a robust native SignalR client once `signalrcore` (or an
alternative) proves stable against ASP.NET Core SignalR.

### 3.7.P2 Automated registry publishing 🚀 *(Q5)*
Wire the secret-gated publish jobs to real NuGet/npm/PyPI accounts (org secrets) with provenance
(npm provenance, NuGet signing) — a release-engineering task requiring credentials.

### 3.7.P3 Go / Java / Rust SDKs 🌍
Additional language SDKs off the same OpenAPI contract, if demand warrants.

### 3.7.P4 CLI tool 🖥️
A `dotflow` CLI built on the C# (or Go) SDK for scripting common operations from a shell.

---

## Success Criteria ✅

- [ ] A **C# SDK** (`GlutenFree.DotFlow.Client`) wraps all API groups + real-time, packs to a `.nupkg`, and the UI references it (no duplicated `Api/*`)
- [ ] A **TypeScript SDK** (`@glutenfree/dotflow-client`) wraps the API + real-time, OpenAPI-typed, packs to an npm tarball (ESM+CJS)
- [ ] A **Python SDK** (`dotflow-client`) wraps the API (+ real-time or polling), type-hinted, builds a wheel
- [ ] All three expose the **same facade** (`.workflows/.executions/.modules/.variables/.scripts/.system/.realtime`), the **same auth** (API key / bearer), and typed ProblemDetails errors
- [ ] The **seven example scenarios** exist in all three SDKs and compile/type-check in CI
- [ ] **OpenAPI drift-guards** keep the TS/Python models honest
- [ ] **CI builds + tests + packs** all three; **publishing is a documented, secret-gated manual step** (no secrets in the repo, no auto-push)
- [ ] `docs/sdks.md` + per-SDK READMEs + CHANGELOGs + `clients/RELEASING.md` exist and cross-link
- [ ] Each SDK has its own test suite (xUnit / Vitest / pytest), all green

---

*Made with 💖 by Ami-Chan! Bindings in every language so everyone can play~ UwU* ✨
