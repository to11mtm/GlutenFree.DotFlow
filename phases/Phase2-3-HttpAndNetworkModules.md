# Phase 2.3: HTTP & Network Modules (Weeks 10-11) 🌐🔌

Made with 💖 by Ami-Chan! UwU ✨

[Back to Phase 2](Phase2-CoreFeatures.md) | [All Phases](README.md)

---

## Overview

Phase 2.3 ships DotFlow's **outbound HTTP client** and **inbound webhook trigger** primitives. Outbound: a single, well-rounded `builtin.http.request` module supporting all common methods, content types, auth strategies, retries, and response transformations. Inbound: a `builtin.http.webhook` trigger module + the API surface to receive external POSTs and kick off workflow executions~ 🌷

Built on top of Phase 2.2's flow control: an HTTP call wrapped in `builtin.trycatch` with a `builtin.loop.foreach` retry pattern is *already* expressible — Polly's job is to make this ergonomic and battle-tested at the **module level** so authors don't need to assemble it by hand every time~ 🎀

**Timeline:** 2 weeks (Weeks 10-11)
**Complexity:** 🟡 Medium — many small slices, two non-trivial pieces (OAuth2 token refresh, webhook trigger plumbing)

> **CopilotNote:** The hot path here is `Workflow.Modules/Builtin/Http/*` — keep modules thin and lean on shared infra
> (`IHttpClientFactory`, Polly registry, `IExpressionEvaluator`). The webhook trigger is the only piece that touches
> `Workflow.Api` — every other slice is module-only and Docker-free testable~ 🧠

### Confirmed Design Decisions ✅

| # | Decision |
|---|----------|
| **D1 HttpClient lifetime** | Use `IHttpClientFactory` (singleton via DI) with named clients per module instance. Avoids socket exhaustion and DNS-cache staleness vs `new HttpClient()`~ |
| **D2 Test strategy** | Use **WireMock.Net in-process** (no Docker) for outbound request assertions; use `WebApplicationFactory<Program>` for webhook trigger tests. Keep all Phase 2.3 tests in `Workflow.Tests` (Docker-free), not `Workflow.Tests.Integration`~ |
| **D3 Auth as properties on the request module** | Single `builtin.http.request` module handles all auth types via an `authType` property + auth-specific sub-properties. Avoids module fan-out (no separate `http.request.basic` / `http.request.bearer` etc.). |
| **D4 Webhook registrations persisted** | Webhook registrations stored via `IWebhookRegistrationRepository` (default: in-memory; production: existing persistence provider). Survives restarts on persisted providers~ |
| **D5 Body/response transformation** | Use JSONPath.NET for JSON path queries (well-maintained, MIT); use `IExpressionEvaluator` from 2.2.5 for inline JS-style transforms; defer XPath to optional sub-phase. |
| **D6 Cancellation** | `HttpRequestMessage` honours the module's `CancellationToken` natively (already plumbed via 2.2.0b hierarchical cancellation)~ |
| **D7 No SOAP** | SOAP module deferred indefinitely — virtually no greenfield demand; if needed, ship as a separate package later. |

### TO RESOLVE 🙏

- [x] **Q1 OAuth2 token store scope:** ~~per-module-instance vs DI singleton keyed by `(authority, clientId)`~~ **Resolved: selectable via `oauth2TokenCacheScope` property** — supports `module` (per-module-instance, simple, no cross-workflow reuse) and `pipeline` (per-pipeline-instance, scoped to a single execution) for V1. A future `singleton` scope (cross-workflow reuse) is tracked in **2.3.P3 OAuth2 Singleton/Persisted Token Cache** post-MVP slice~
- [x] **Q2 Webhook routing strategy:** ~~`POST /webhooks/{webhookId}` vs `POST /webhooks/{path}` arbitrary-path matching~~ **Resolved: `POST /webhooks/{webhookId}` for V1.** Arbitrary path routing tracked in **2.3.P1 Arbitrary-Path Webhook Routing** post-MVP slice — V1 implementation must avoid hard-wiring `webhookId` into core types so the path-router can be added without breaking schemas~
- [x] **Q3 Webhook trigger module return semantics:** ~~sync response vs always 202-Accepted~~ **Resolved: always `202 Accepted` with `{ executionId }` for V1.** Synchronous-response support tracked in **2.3.P2 Sync Webhook Responses** post-MVP slice — V1 must keep the trigger->execution kickoff pluggable so a "wait for first completion / first output" response strategy can be added later~
- [x] **Q4 Retry-after header honouring:** ~~always honour vs ignore~~ **Resolved: honour `Retry-After` for `429`/`503` *up to the configured backoff cap*.** If the header value exceeds `maxRetryBackoffSeconds` (new property, default `60s`), fall back to the configured backoff. Prevents adversarial / misconfigured servers from forcing long sleeps~
- [x] **Q5 Multipart/form-data file source:** ~~`byte[]` vs `Stream` vs file path~~ **Resolved: `byte[]` only for V1.** `Stream` support tracked in **2.3.P4 Multipart Stream Support** post-MVP slice; file-path source (with `allowFileUpload` opt-in) tracked in **2.3.P5 Multipart File-Path Support** post-MVP slice. Keeps V1 free of FS-read surface area~
- [x] **Q6 GraphQL as sub-phase or separate?** ~~include in 2.3 vs separate~~ **Resolved: moved to post-MVP** — tracked in **2.3.P6 GraphQL Module** slice. Phase 2.3 V1 ships outbound HTTP + webhook inbound only; GraphQL is a nice-to-have that doesn't block 2.4+~

---

## Pre-Existing Work (from earlier phases) ✅

| Component | File | Status |
|-----------|------|--------|
| `IWorkflowModule` contract | `Workflow.Modules/Abstractions/IWorkflowModule.cs` | ✅ Existing — reused as-is |
| `ModuleResult` + `WithActivePorts` | `Workflow.Modules/Abstractions/IWorkflowModule.cs` | ✅ Existing — used for retry/exhaustion outcomes |
| Hierarchical cancellation | `WorkflowExecutor._executionCts` (2.2.0b) | ✅ Tokens already flow into modules |
| `IExpressionEvaluator` (Jint) | `Workflow.Engine/Services/JintExpressionEvaluator.cs` | ✅ 2.2.5 — used for response/header templating |
| `BuiltinModuleRegistration` | `Workflow.Modules/Builtin/BuiltinModuleRegistration.cs` | ✅ Existing — new modules appended here |
| Logging (`ILogger`) | `ctx.Logger` on `ModuleExecutionContext` | ✅ Existing |

> **CopilotNote:** Don't roll your own HttpClient pool — wire everything through `IHttpClientFactory`. The `Workflow.Api/Program.cs`
> already has DI; we just need to add `services.AddHttpClient("dotflow.http")` and resolve from the registry inside modules~ 🌸

---

## 2.3.0 HTTP Infrastructure & Core `builtin.http.request` 🌐 (foundation)

> **Purpose:** Land the shared HTTP infrastructure (DI registration, `IHttpClientFactory`, common helpers) + a minimal-but-useful `HttpRequestModule` supporting all standard methods with JSON body/response. Everything later builds on this~ ✨

**Complexity:** 🟡 Medium

### Tasks

- [x] **`Workflow.Modules.Http` project layout** 📁 ✅ **(May 19, 2026)**
  - [x] New folder: `Workflow.Modules/Builtin/Http/`
  - [x] Add `Microsoft.Extensions.Http` (v8.0.1) to `Directory.Packages.props`
  - [x] Add `Microsoft.Extensions.Http` reference to `Workflow.Modules/Workflow.Modules.csproj`
  - [x] DI registration helper: `Workflow.Modules/Builtin/Http/HttpModuleServiceCollectionExtensions.cs`
    - [x] `AddHttpModules(this IServiceCollection)` → registers `IHttpClientFactory` + named client `"dotflow.http"` with `SocketsHttpHandler` (`PooledConnectionLifetime=2min`, `MaxConnectionsPerServer=256`, `ConnectTimeout=30s`, auto-redirect on)
    - [x] `HttpClientName = "dotflow.http"` public const for modules to reference
  - [x] Update `Workflow.Api/Program.cs` to call `services.AddHttpModules()` (with phase note comment)
  - [x] Both `Workflow.Modules` + `Workflow.Api` build with 0 errors ✅

- [x] **`AddWorkflowModules` aggregate DI registration** 🗂️ ✅ **(May 19, 2026)**
  - [x] New file: `Workflow.Modules/WorkflowModulesServiceCollectionExtensions.cs`
  - [x] `AddWorkflowModules(this IServiceCollection)` — single call that aggregates *all* built-in module family registrations at the `Workflow.Modules` layer:
    - [x] Calls `AddHttpModules()` (2.3.0) — more families added here as they land (2.4 Database, etc.)
    - [ ] ~~Calls `BuiltinModules.RegisterAll(registry)` (or accepts an `IModuleRegistry` overload)~~ — **deferred**: module-registry registration is a separate concern from DI-service registration (registry is populated via `ModuleDiscovery` in the engine startup path, not in `IServiceCollection`). The aggregate method stays focused on DI services only~ 🧠
  - [x] Update `Workflow.Api/Program.cs` to replace the individual `AddHttpModules()` call with `AddWorkflowModules()` (kept old call as comment for reference)
  - [x] `Workflow.Modules.csproj` did **not** gain any new transitive deps — already references `Microsoft.Extensions.Http` and `Microsoft.Extensions.DependencyInjection.Abstractions`
  - [x] XML doc explains the "one-call" contract: future module families extend this method, hosts never need to know about family-specific extensions
  - [x] `Workflow.Api` + `Workflow.Modules` build with 0 errors ✅
  > **CopilotNote:** This is the "smart layer" — put it in `Workflow.Modules` not in any sub-namespace so it's the natural top-level entry point. Individual family extensions (like `AddHttpModules`) stay as public API for advanced hosts that want fine-grained control, but `AddWorkflowModules` is the blessed happy-path~ 🧠💖

- [x] **`HttpRequestModule` v1** 🌐 ✅ **(May 19, 2026)**
  - [x] New file: `Workflow.Modules/Builtin/Http/HttpRequestModule.cs`
  - [x] `ModuleId: "builtin.http.request"`, `Category: "Network"`, `Icon: "🌐"`, `Version: 1.0.0`
  - [x] Schema (v1 — minimal):
    - [x] Input: `url` (string, required)
    - [x] Input: `method` (string enum: GET/POST/PUT/DELETE/PATCH/HEAD/OPTIONS, default `"GET"`)
    - [x] Input: `headers` (`HashMap<string,string>`, optional) — implemented as `Dictionary<string,string>` for v1, encoder accepts both shapes
    - [x] Input: `body` (object, optional — JSON-serialised by default)
    - [x] Input: `timeoutSeconds` (int, optional, default `30`)
    - [x] Output: `statusCode` (int), `headers` (`Dictionary<string,string>`), `body` (object), `success` (bool — true when 200-299), `durationMs` (long)
  - [x] `ExecuteAsync`:
    - [x] Resolve named `HttpClient` from `IHttpClientFactory` (resolved lazily from `ctx.Services` — keeps module parameterless-constructable)
    - [x] Build `HttpRequestMessage` with method, URL, headers, JSON body
    - [x] Use `CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken)` with `timeoutSeconds`
    - [x] Send + parse status, headers, response body (JSON if `Content-Type: application/json`, else string)
    - [x] Return `ModuleResult.Ok(outputs)`
    - [x] On `HttpRequestException` / `TaskCanceledException` (timeout) → `ModuleResult.Fail(ex.Message, ex)`
  - [x] `ValidateConfiguration`:
    - [x] Validate `method` is a known HTTP verb
    - [x] Validate `url` is a well-formed URI (when statically provided — skips `{{ }}` templates)
    - [x] Validate `timeoutSeconds > 0`

- [x] **Registration** ✅ **(May 19, 2026)**
  - [x] Appended `new HttpRequestModule()` to `BuiltinModuleRegistration.GetAll()` (count 16 → 17)

### Tests (target ~10): → `Workflow.Tests/Modules/Http/HttpRequestModuleTests.cs` ✅ **13/13 passing**

- [x] `HttpRequestModule_Metadata_IsCorrect` ✅
- [x] `HttpRequestModule_Schema_HasRequiredPorts` ✅
- [x] `HttpRequestModule_IsDiscoverableInAssembly` ✅ *(bonus)*
- [x] `ValidateConfiguration_InvalidMethod_Fails` ✅
- [x] `ValidateConfiguration_InvalidUrl_Fails` ✅
- [x] `ValidateConfiguration_TemplateUrl_IsSkipped` ✅ *(bonus — `{{ }}` placeholders bypass static validation)*
- [x] `Get_ReturnsStatus200AndJsonBody` ✅ *(WireMock)*
- [x] `Post_WithJsonBody_SendsCorrectContentType` ✅ *(WireMock)*
- [x] `Headers_ArePassedThrough` ✅ *(WireMock)*
- [x] `Timeout_ExceededReturnsFail` ✅ *(WireMock with delay + parent CT cancellation)*
- [x] `CancellationToken_HonouredMidFlight` ✅ *(WireMock)*
- [x] `NonJsonResponse_ReturnsStringBody` ✅ *(WireMock returning `text/plain`)*
- [x] `MissingDI_IHttpClientFactory_Fails` ✅ *(bonus — empty service provider → clear error)*

---

## 2.3.1 Body & Response Format Support 📦 ✅ **(May 19, 2026)**

> **Purpose:** Expand the request module to handle the four content types real APIs actually use (beyond JSON): form-encoded, multipart, XML, raw bytes/string. Smart response deserialization via `Content-Type` detection~ 🎀

**Complexity:** 🟡 Medium

### Tasks

- [x] **Request body strategies** 📤 ✅
  - [x] New file: `Workflow.Modules/Builtin/Http/Internal/RequestBodyEncoder.cs`
  - [x] Strategy dispatch: `Encode(object body, string? contentType) -> EncodeResult`
  - [x] Built-in strategies:
    - [x] `application/json` → `StringContent` w/ `JsonSerializer` *(default for objects)*
    - [x] `application/x-www-form-urlencoded` → `FormUrlEncodedContent` from string/object dictionary
    - [x] `multipart/form-data` → `MultipartFormDataContent` (V1: `byte[]` + string parts only — see Q5; `Stream` + file-path support deferred to **2.3.P4** / **2.3.P5**)
    - [x] `application/xml` / `text/xml` → string-passthrough (validate XML well-formedness via `XmlDocument.LoadXml`)
    - [x] `text/*` → string-passthrough
    - [x] `application/octet-stream` → byte-array passthrough
    - [x] Unknown content type → best-effort stringify + stamp the requested Content-Type
  - [x] Schema additions on `HttpRequestModule`:
    - [x] Input: `contentType` (string, optional — falls back to JSON for object bodies, text/plain for strings, octet-stream for byte[])
  - [ ] ~~Update `ValidateConfiguration` to recognise known content types (warning, not error, for unknown)~~ — **deferred**: `ValidationResult` is binary (pass/fail) today; warning-level validation is a cross-cutting concern best added when the warning infrastructure lands. Unknown content types are accepted at runtime + best-effort encoded~ 🧠

- [x] **Response body decoding** 📥 ✅
  - [x] New file: `Workflow.Modules/Builtin/Http/Internal/ResponseBodyDecoder.cs`
  - [x] Decision tree by `Content-Type`:
    - [x] `application/json*` / `*+json` → `JsonDocument` round-trip → `Dictionary<string,object?>` (POCO graph; lists for arrays; primitives unboxed)
    - [x] `application/xml` / `text/xml` → string (deferred: XML→object map)
    - [x] `text/*` → string
    - [x] anything binary → `byte[]`
    - [x] Missing content type → string (best guess — most untyped responses are text)
  - [x] Output port additions: `contentType` (string) — for downstream switching
  - [x] `HttpRequestModule.ExecuteAsync` refactored to delegate body build → `RequestBodyEncoder`, response decode → `ResponseBodyDecoder` (removes ~80 lines of inline helpers)

### Tests (target ~8): → `Workflow.Tests/Modules/Http/HttpBodyFormatTests.cs` ✅ **9/9 passing**

- [x] `Post_FormUrlEncoded_SendsCorrectContentType_AndBody` ✅ *(WireMock)*
- [x] `Post_MultipartFormData_WithByteArrayPart_RoundTrips` ✅ *(WireMock)*
- [x] `Post_XmlBody_PassedThroughAsString` ✅ *(WireMock)*
- [x] `Post_XmlBody_Malformed_Fails` ✅ *(bonus — pre-flight well-formedness check)*
- [x] `Post_RawBytes_OctetStream_RoundTrips` ✅ *(WireMock)*
- [x] `Response_ApplicationJson_DeserialisedToDictionary` ✅ *(WireMock)*
- [x] `Response_TextPlain_ReturnedAsString` ✅ *(WireMock)*
- [x] `Response_OctetStream_ReturnedAsByteArray` ✅ *(WireMock)*
- [x] `ContentType_OutputPort_Populated` ✅ *(WireMock)*

---

## 2.3.2 Authentication: Basic + Bearer + API Key 🔐 ✅ **(May 19, 2026)**

> **Purpose:** Land the three simple, synchronous auth strategies — no token refresh, no OAuth dance. These cover ~80% of real API integrations. OAuth2 lives in its own slice (2.3.3)~ 🌸

**Complexity:** 🟢 Low-Medium

### Tasks

- [x] **Auth strategy interface** 🔧 ✅
  - [x] New file: `Workflow.Modules/Builtin/Http/Auth/IHttpAuthStrategy.cs`
  - [x] `Task ApplyAsync(HttpRequestMessage request, ModuleExecutionContext ctx, CancellationToken ct)`
  - [x] Built-in implementations co-located (single file by design — V1 strategies are tiny + stateless):
    - [x] `BasicAuthStrategy` — `username`, `password` → `Authorization: Basic {base64}`
    - [x] `BearerAuthStrategy` — `bearerToken` → `Authorization: Bearer {token}`
    - [x] `ApiKeyAuthStrategy` — `apiKey`, `apiKeyHeader` (default `X-API-Key`), `apiKeyLocation` (`header` or `query`)
    - [x] `NoAuthStrategy` (singleton) — used when `authType=none`
    - [x] `HttpAuthStrategyFactory.FromProperties(...)` — switch-based selector with explicit error returns (no exceptions on user-misconfig)

- [x] **`HttpRequestModule` schema additions** 🎀 ✅
  - [x] Input: `authType` (string enum: `none`/`basic`/`bearer`/`apikey`/`oauth2`, default `none`)
  - [x] Inputs grouped by auth type (only relevant ones are read):
    - [x] Basic: `username`, `password`
    - [x] Bearer: `bearerToken`
    - [x] API Key: `apiKey`, `apiKeyHeader`, `apiKeyLocation`
  - [x] Auth strategy resolved via `HttpAuthStrategyFactory.FromProperties` switch (no DI factory needed for these three; `oauth2` returns a clear "ships in 2.3.3" error until that slice lands)
  - [x] Strategy `ApplyAsync` invoked inside `ExecuteAsync` after body/headers built and before send — receives the linked timeout CT so OAuth2 token-fetch in 2.3.3 honours the same deadline~

- [x] **Header redaction in logs** 🔒 ✅
  - [x] `HttpAuthStrategyFactory.IsRedactedHeader` / `RedactForLog` / `RedactHeaders` helpers
  - [x] Default redacted set: `Authorization`, `Proxy-Authorization`, `X-API-Key`, `X-Api-Key`, `X-Auth-Token`, `X-Access-Token`, `Cookie`, `Set-Cookie`
  - [x] `HttpRequestModule.ExecuteAsync` emits a debug log line with redacted header snapshot when `LogLevel.Debug` is enabled

### Tests (target ~9): → `Workflow.Tests/Modules/Http/HttpAuthTests.cs` ✅ **9/9 passing**

- [x] `BasicAuth_Base64EncodedCorrectly` ✅ *(WireMock verify header)*
- [x] `BasicAuth_MissingCredentials_Fails` ✅
- [x] `BearerAuth_AddsAuthorizationHeader` ✅ *(WireMock)*
- [x] `BearerAuth_MissingToken_Fails` ✅
- [x] `ApiKeyAuth_InHeader_AddedToHeaders` ✅ *(WireMock)*
- [x] `ApiKeyAuth_InQuery_AppendedToUrl` ✅ *(WireMock — pre-existing query preserved)*
- [x] `ApiKeyAuth_CustomHeaderName_Honoured` ✅ *(WireMock)*
- [x] `AuthType_None_NoAuthHeaderAdded` ✅ *(WireMock)*
- [x] `AuthHeaders_RedactedInDebugLog` ✅ *(redaction helper unit tests — bulk + single)*

---

## 2.3.3 OAuth2 Client Credentials Flow 🔑

> **Purpose:** Implement the most common machine-to-machine OAuth2 flow (client credentials grant). Token caching + automatic refresh. Other flows (auth code, device flow) deferred~ 🌷

**Complexity:** 🟡 Medium

### Tasks

- [ ] **OAuth2 token cache** 💾
  - [ ] New file: `Workflow.Modules/Builtin/Http/Auth/IOAuth2TokenCache.cs`
  - [ ] Two built-in implementations (selectable via `oauth2TokenCacheScope` property — see schema additions below):
    - [ ] `PerModuleOAuth2TokenCache` — fresh cache per `HttpRequestModule` instance (no cross-call reuse); simplest, safest default
    - [ ] `PerPipelineOAuth2TokenCache` — scoped to a single `WorkflowExecution`; tokens reused across all HTTP calls inside the same execution. Implementation: resolved per-execution via `ctx.ExecutionId` keyed dictionary on a scoped DI service
  - [ ] Both keyed on `(authority, clientId, scope)`, TTL = `expires_in - 30s` safety margin
  - [ ] Cross-workflow singleton scope **deferred to 2.3.P3** (post-MVP)
  - [ ] Resolves **Q1**: selectable scope, no cross-workflow leakage in V1

- [ ] **OAuth2 strategy** 🔧
  - [ ] New file: `Workflow.Modules/Builtin/Http/Auth/OAuth2ClientCredentialsStrategy.cs`
  - [ ] Inputs (on `HttpRequestModule`): `oauth2TokenUrl`, `oauth2ClientId`, `oauth2ClientSecret`, `oauth2Scope`, optional `oauth2Audience`
  - [ ] Input: `oauth2TokenCacheScope` (string enum: `module`/`pipeline`, default `module`) — selects cache implementation
  - [ ] Flow:
    - [ ] Check cache for unexpired token
    - [ ] If miss/expired → `POST {tokenUrl}` with `grant_type=client_credentials` (`application/x-www-form-urlencoded`)
    - [ ] Parse response: `access_token`, `expires_in`, `token_type` (must be `Bearer`)
    - [ ] Cache + apply as `Authorization: Bearer {access_token}`
  - [ ] Failures: structured error mapping (`invalid_client`, `invalid_scope`, etc. → `ModuleResult.Fail` with code)

- [ ] **Refresh-on-401 retry** 🔄
  - [ ] If a request fails with `401 Unauthorized` and `authType == oauth2`, invalidate cache + retry once
  - [ ] Hard fail on second `401`

### Tests (target ~10): → `Workflow.Tests/Modules/Http/OAuth2Tests.cs`

- [ ] `OAuth2_FirstCall_FetchesTokenFromAuthority` *(WireMock for token endpoint + protected endpoint)*
- [ ] `OAuth2_SecondCall_UsesCachedToken_NoTokenFetch`
- [ ] `OAuth2_TokenExpired_RefetchesToken`
- [ ] `OAuth2_401Response_InvalidatesCacheAndRetries`
- [ ] `OAuth2_DoubleAuth401_Fails`
- [ ] `OAuth2_InvalidClient_ReturnsFail`
- [ ] `OAuth2_DifferentScopes_CachedSeparately`
- [ ] `OAuth2_TokenCache_EvictionTimingRespectsExpiresIn`
- [ ] `OAuth2_ModuleScope_FreshCachePerModuleInstance` — two modules → two token fetches
- [ ] `OAuth2_PipelineScope_SharesCacheAcrossModulesInSameExecution` — two modules in same workflow → one token fetch

---

## 2.3.4 Retry, Timeout & Circuit Breaker via Polly 🔄

> **Purpose:** Add resiliency policies to the request module without forcing authors to assemble them via flow-control modules. Powered by **Polly v8** (the new Resilience pipeline API)~ ⚡

**Complexity:** 🟡 Medium

### Tasks

- [ ] **Polly integration** 📦
  - [ ] Add `Polly.Core` (v8+) to `Directory.Packages.props`
  - [ ] New file: `Workflow.Modules/Builtin/Http/Resilience/HttpResiliencePipelineFactory.cs`
  - [ ] Build per-request pipeline from input properties (cached when properties are stable):
    - [ ] Retry policy
    - [ ] Circuit breaker policy
    - [ ] Timeout policy

- [ ] **`HttpRequestModule` schema additions** 🎀
  - [ ] Input: `retryCount` (int, optional, default `0` — opt-in)
  - [ ] Input: `retryBackoff` (string enum: `linear`/`exponential`/`fibonacci`, default `exponential`)
  - [ ] Input: `retryDelaySeconds` (double, optional, default `1.0`)
  - [ ] Input: `maxRetryBackoffSeconds` (double, optional, default `60.0`) — hard cap on per-attempt sleep (also caps `Retry-After`)
  - [ ] Input: `retryOnStatusCodes` (`Arr<int>`, optional, default `[408, 429, 500, 502, 503, 504]`)
  - [ ] Input: `circuitBreakerFailureThreshold` (int, optional, default `0` — disabled)
  - [ ] Input: `circuitBreakerSamplingDurationSeconds` (double, optional, default `30`)

- [ ] **Retry-after header support** *(Q4 resolved: honour up to `maxRetryBackoffSeconds` cap)* 🎀
  - [ ] When response is `429`/`503` and `Retry-After` header present → use `min(headerValue, maxRetryBackoffSeconds)` as the delay
  - [ ] If `headerValue > maxRetryBackoffSeconds` → log a warning + fall back to configured backoff strategy
  - [ ] Supports both seconds-form (`Retry-After: 120`) and HTTP-date form (`Retry-After: Wed, 21 Oct 2026 07:28:00 GMT`)
  - [ ] Add jitter to non-Retry-After delays (Polly's `Jitter` setting)

- [ ] **Outputs on retry** 📊
  - [ ] Add output: `attemptCount` (int) — actual attempts made before success/failure
  - [ ] Add output: `circuitState` (string) — `closed`/`open`/`halfopen` (if circuit configured)

### Tests (target ~11): → `Workflow.Tests/Modules/Http/HttpRetryTests.cs`

- [ ] `Retry_OnTransient500_RetriesAndSucceeds` *(WireMock with response sequence)*
- [ ] `Retry_OnPermanent404_DoesNotRetry`
- [ ] `Retry_MaxAttemptsExceeded_FailsWithLastError`
- [ ] `Retry_ExponentialBackoff_DelaysIncrease` *(time-based assertion with tolerance)*
- [ ] `Retry_RetryAfterHeader_WithinCap_HonouredOverBackoff`
- [ ] `Retry_RetryAfterHeader_ExceedsCap_FallsBackToConfiguredBackoff` — header says 600s, cap is 60s → use 60s + log warning
- [ ] `Timeout_AbortsRequestAfterDuration`
- [ ] `Timeout_CancellationToken_Honoured`
- [ ] `CircuitBreaker_OpensAfterThresholdFailures`
- [ ] `CircuitBreaker_HalfOpenAllowsTestRequest`
- [ ] `AttemptCount_OutputReflectsActualAttempts`

---

## 2.3.5 Request/Response Transformation 🔀

> **Purpose:** Authors shouldn't need a downstream `builtin.condition` + `builtin.setvariable` just to extract a single field from a JSON response. Add lightweight URL templating + JSONPath response extraction~ 🌟

**Complexity:** 🟢 Low-Medium

### Tasks

- [ ] **URL & body templating** 🪄
  - [ ] Reuse existing `PropertyBinder` `{{variable.name}}` syntax for `url`, `headers`, string `body` properties
  - [ ] *(Already shipped — just confirm and document)*
  - [ ] Add explicit test: `Url_WithDoubleBraceVariable_Resolved`

- [ ] **JSONPath response extraction** 🎯
  - [ ] Add `JsonPath.Net` package to `Directory.Packages.props` *(MIT, well-maintained)*
  - [ ] New file: `Workflow.Modules/Builtin/Http/Internal/JsonPathExtractor.cs`
  - [ ] Schema addition: `responseExtract` (`HashMap<string,string>`, optional) — keys = output port names, values = JSONPath expressions
  - [ ] On JSON response, evaluate each path → add to outputs under the keyed name
  - [ ] Missing paths → `null` (don't fail unless `responseExtractRequired: true`)

- [ ] **Regex extraction** *(for text/HTML responses)* 🔍
  - [ ] Schema addition: `responseRegex` (`HashMap<string,string>`, optional) — keys = output names, values = regex patterns with named capture group `(?<value>...)`
  - [ ] Run against `body` when it's a string; surface captured value(s) as outputs

- [ ] **Header extraction** 🏷️
  - [ ] Schema addition: `headerExtract` (`HashMap<string,string>`, optional) — keys = output names, values = response header names
  - [ ] Common case: extract `Location` from `201 Created`, ETag from `200`, etc.

> **Deferred to a follow-up slice:** XPath for XML responses (low demand; can be added later under the same interface)~

### Tests (target ~8): → `Workflow.Tests/Modules/Http/HttpTransformationTests.cs`

- [ ] `Url_WithDoubleBraceVariable_Resolved` *(WireMock)*
- [ ] `JsonPath_ExtractSingleField_PopulatesOutput` *(WireMock)*
- [ ] `JsonPath_ExtractNestedField_PopulatesOutput`
- [ ] `JsonPath_MissingPath_OutputIsNull`
- [ ] `JsonPath_ArrayQuery_ReturnsList`
- [ ] `Regex_NamedCapture_PopulatesOutput`
- [ ] `Regex_NoMatch_OutputIsNull`
- [ ] `HeaderExtract_LocationFrom201_Populated` *(WireMock)*

---

## 2.3.6 Webhook Trigger Module + API Surface 🪝

> **Purpose:** Inbound side — let workflows be **triggered** by external HTTP POSTs. New module + API endpoint + registration repository~ 💖

**Complexity:** 🟡 Medium-High *(API surface + repository + actor message handling)*

> **V1 forward-compat notes** *(per Q2 / Q3 resolutions)*:
> - **Route shape:** `POST /webhooks/{webhookId}` is the V1 surface. The lookup-and-dispatch logic must be encapsulated in a `WebhookDispatcher` service that takes `(webhookId, HttpRequest)` so a future router (path-based, header-based) can sit in front without touching the dispatcher (**2.3.P1**).
> - **Response strategy:** V1 always returns `202 Accepted + { executionId }`. The dispatcher must accept an `IWebhookResponseStrategy` (default: `Async202ResponseStrategy`) — future sync strategies (`WaitForFirstOutput`, `WaitForCompletion`) plug in here without breaking the V1 controller (**2.3.P2**).

### Tasks

- [ ] **`WebhookRegistration` model** 📋
  - [ ] New file: `Workflow.Core/Models/WebhookRegistration.cs`
  - [ ] Fields: `WebhookId` (string), `WorkflowDefinitionId` (Guid), `AllowedMethods` (`Arr<string>`), `SecretKey` (Option<string>), `SignatureScheme` (`Option<string>`), `CreatedAt`, `Enabled`

- [ ] **`IWebhookRegistrationRepository`** 💾
  - [ ] New file: `Workflow.Persistence/Abstractions/IWebhookRegistrationRepository.cs`
  - [ ] CRUD: `RegisterAsync`, `UpdateAsync`, `DeleteAsync`, `GetAsync(webhookId)`, `ListAsync()`
  - [ ] Default impl: `InMemoryWebhookRegistrationRepository`
  - [ ] Add to `IPersistenceProvider` interface as optional `Webhooks` property *(or new provider slot)*
  - [ ] SQLite impl: `Workflow.Persistence.Sqlite/SqliteWebhookRegistrationRepository.cs` + migration

- [ ] **Webhook API endpoints** 🌐
  - [ ] New file: `Workflow.Api/Webhooks/WebhookDispatcher.cs` — encapsulates lookup + dispatch (forward-compat for 2.3.P1)
  - [ ] New file: `Workflow.Api/Webhooks/IWebhookResponseStrategy.cs` + `Async202ResponseStrategy` (default impl, forward-compat for 2.3.P2)
  - [ ] New file: `Workflow.Api/Controllers/WebhooksController.cs` *(or minimal-API mapping in `Program.cs`)*
  - [ ] `POST /webhooks/{webhookId}` — trigger endpoint *(see 2.3.7 for signature validation)*
    - [ ] Delegate to `WebhookDispatcher.DispatchAsync(webhookId, HttpRequest, responseStrategy)`
    - [ ] Dispatcher: lookup registration → check method allowed → kick off execution via `WorkflowSupervisor.CreateWorkflowInstance` → hand off to response strategy
    - [ ] Default strategy returns `202 Accepted` with `{ executionId }`
  - [ ] `POST /api/webhooks` — register
  - [ ] `GET /api/webhooks` — list
  - [ ] `GET /api/webhooks/{webhookId}` — fetch one
  - [ ] `PUT /api/webhooks/{webhookId}` — update
  - [ ] `DELETE /api/webhooks/{webhookId}` — delete

- [ ] **`WebhookTriggerModule`** 🪝
  - [ ] New file: `Workflow.Modules/Builtin/Http/WebhookTriggerModule.cs`
  - [ ] `ModuleId: "builtin.http.webhook"`, `Category: "Triggers"`, `Icon: "🪝"`
  - [ ] Schema:
    - [ ] Properties: `webhookId` (string, required) — must match a registration
    - [ ] Outputs: `body` (object), `headers` (`HashMap<string,string>`), `query` (`HashMap<string,string>`), `method` (string), `receivedAt` (`DateTimeOffset`)
  - [ ] Execution: this module is a **trigger node** — when the workflow is started by the webhook controller, the inputs are pre-populated; the module just passes them through to outputs

- [ ] **Triggered workflow inputs convention** 📨
  - [ ] Webhook controller calls `CreateWorkflowInstance` with `inputs = { "__webhook__": { body, headers, query, method, receivedAt } }`
  - [ ] `WebhookTriggerModule.ExecuteAsync` reads `ctx.Inputs["__webhook__"]` → unpacks to outputs

### Tests (target ~12): → `Workflow.Tests/Modules/Http/WebhookTriggerModuleTests.cs`, `Workflow.Tests/Api/WebhookApiTests.cs`

**Unit (5):**
- [ ] `WebhookTriggerModule_Metadata_IsCorrect`
- [ ] `WebhookTriggerModule_Schema_HasCorrectPorts`
- [ ] `WebhookTriggerModule_WithWebhookInputs_PopulatesOutputs`
- [ ] `WebhookTriggerModule_WithoutWebhookInputs_OutputsAreEmpty`
- [ ] `WebhookRegistration_ValidationRules`

**Registration repository (3):**
- [ ] `InMemoryRepository_RegisterAndGet_RoundTrips`
- [ ] `InMemoryRepository_DuplicateId_ReturnsConflictError`
- [ ] `InMemoryRepository_Delete_RemovesEntry`

**API integration (4 — via `WebAplicationFactory<Program>`):**
- [ ] `RegisterWebhook_ReturnsCreated`
- [ ] `PostToRegisteredWebhook_TriggersWorkflow_Returns202`
- [ ] `PostToUnknownWebhook_Returns404`
- [ ] `PostWithDisallowedMethod_Returns405`

---

## 2.3.7 Webhook Signature Validation 🔒

> **Purpose:** Secure webhook endpoints against forged requests. Support generic HMAC + named provider shapes (GitHub, Stripe)~ 🛡️

**Complexity:** 🟢 Low-Medium

### Tasks

- [ ] **Signature scheme registry** 🔧
  - [ ] New file: `Workflow.Api/Webhooks/IWebhookSignatureValidator.cs`
  - [ ] Built-in implementations:
    - [ ] `HmacSha256SignatureValidator` — generic: header name + secret + hex/base64 encoding
    - [ ] `GitHubSignatureValidator` — `X-Hub-Signature-256: sha256={hex}`
    - [ ] `StripeSignatureValidator` — `Stripe-Signature: t={timestamp},v1={hex}` *(includes timestamp tolerance check)*
  - [ ] Resolved by `signatureScheme` string on `WebhookRegistration` (`"hmac-sha256"`, `"github"`, `"stripe"`)

- [ ] **Validation in controller** 🛡️
  - [ ] Before triggering workflow, validator runs against raw request body + headers
  - [ ] Mismatch → `401 Unauthorized` + log + don't trigger
  - [ ] Missing `SecretKey` on registration but `SignatureScheme` set → registration-time validation failure

- [ ] **Replay protection** ⏰
  - [ ] Stripe-style timestamp tolerance (default 5 minutes — configurable per registration)

### Tests (target ~7): → `Workflow.Tests/Api/WebhookSignatureTests.cs`

- [ ] `HmacSha256_ValidSignature_Passes`
- [ ] `HmacSha256_InvalidSignature_Rejected`
- [ ] `HmacSha256_MissingHeader_Rejected`
- [ ] `GitHub_ValidSignature_Passes`
- [ ] `Stripe_ValidSignatureWithinTolerance_Passes`
- [ ] `Stripe_ExpiredTimestamp_Rejected`
- [ ] `UnknownScheme_RejectedAtRegistration`

---

## 2.3.8 Engine Integration & End-to-End Demo 🎯

> **Purpose:** Prove the new HTTP primitives compose cleanly with Phase 2.2 flow control. Sample workflow + persistence integration tests + docs~ 💖

**Complexity:** 🟢 Low-Medium

### Tasks

- [ ] **End-to-end sample workflow** 🌈
  - [ ] New file: `examples/definitions/http-integration-demo.json`
  - [ ] Shape: a webhook trigger fans into a parallel block that calls two external APIs (one with retry), aggregates via `builtin.fanin`, persists via `builtin.setvariable`:
    ```
    WebhookTrigger
      └→ TryCatch
            ├─ try → Parallel
            │          ├─ api_call_1 → HttpRequest(GET, with retry)
            │          └─ api_call_2 → HttpRequest(POST, with auth)
            ├─ catch → Log("api call failed: {{error.message}}")
            └─ finally → SetVariable("audit", "done")
    ```
  - [ ] Uses **only** modules from Phases 2.2 + 2.3 — no fictional dependencies

- [ ] **Persistence integration test** 💾
  - [ ] New file: `Workflow.Tests/Modules/Http/HttpPersistenceTests.cs`
  - [ ] Spin up WireMock + `SqlitePersistenceProvider(:memory:)` + WorkflowExecutor
  - [ ] Run demo workflow → verify HTTP node executions persisted with `statusCode`, `durationMs` in metadata

- [ ] **Docs** 📚
  - [ ] New file: `docs/http-and-network.md`
  - [ ] Cover: HttpRequest module (all options), auth strategies, retry/timeout/circuit-breaker, response extraction, webhook trigger + registration + signature validation, GraphQL, common patterns

### Tests (target ~4): → `Workflow.Tests/Modules/Http/HttpPersistenceTests.cs`, `Workflow.Tests/Modules/Http/HttpE2ETests.cs`

- [ ] `Demo_TriggeredByWebhook_BothApisCalled_AuditPersisted`
- [ ] `Demo_OneApiFails_TryCatchRecovers_WorkflowCompletes`
- [ ] `HttpNodeExecution_PersistedWithStatusCodeMetadata`
- [ ] `WebhookTriggeredExecution_PersistedWithWebhookIdMetadata`

---

## Post-MVP Slices 🚧 (deferred — not blocking 2.4+)

> **Purpose:** Capture all deferred-but-tracked scope from the V1 resolutions (Q1, Q2, Q3, Q5, Q6) as discrete post-MVP slices so they don't get lost. Each slice is small enough to ship as a single PR once the V1 surface is stable~ 🌷
>
> **Sequencing tip:** None of these block 2.4 (Database Modules). They can be picked up opportunistically — e.g. P6 (GraphQL) when a real consumer needs it; P1/P2 (webhook router + sync responses) together once external integrators ask for richer webhook UX~ 💖

---

### 2.3.P1 Arbitrary-Path Webhook Routing 🛣️ *(post-MVP, expands Q2)*

**Purpose:** Allow webhook registrations to claim arbitrary URL paths instead of being keyed only by `{webhookId}`. Useful for mirroring third-party expected paths (`/integrations/github/push`, `/billing/stripe`).

**Complexity:** 🟢 Low-Medium

#### Tasks

- [ ] **`WebhookRegistration` schema additions**
  - [ ] Add optional `Path` field (string, e.g. `/integrations/github/push`); when set, takes precedence over `WebhookId` lookup
  - [ ] Add `PathPattern` (string, optional) — simple `*` glob support (e.g. `/integrations/*/push`)
  - [ ] Migration to extend SQLite schema
- [ ] **`WebhookPathRouter`**
  - [ ] New service `Workflow.Api/Webhooks/WebhookPathRouter.cs` — resolves an incoming request to a registration by (in order): exact path match → pattern match → fallback `{webhookId}` route
  - [ ] Plug into existing `WebhookDispatcher` (V1's forward-compat hook)
- [ ] **New endpoint:** `POST /webhooks/{**catchAll}` (ASP.NET route prefix) — alongside `POST /webhooks/{webhookId}`
- [ ] **Validation:** registration time check for path conflicts (two registrations claiming the same path)

#### Tests (target ~6)

- [ ] `PathRouter_ExactPath_ResolvesRegistration`
- [ ] `PathRouter_GlobPattern_MatchesWildcardSegment`
- [ ] `PathRouter_NoMatch_FallsBackToWebhookIdRoute`
- [ ] `PathRouter_ConflictAtRegistration_Rejected`
- [ ] `Api_PostArbitraryPath_TriggersWorkflow`
- [ ] `Api_PostAmbiguousPath_Returns409`

---

### 2.3.P2 Sync Webhook Responses ⏱️ *(post-MVP, expands Q3)*

**Purpose:** Allow webhooks to return a synchronous response (workflow output) instead of always returning `202 Accepted`. Useful for chatbot integrations, OAuth callbacks, healthchecks, etc.

**Complexity:** 🟡 Medium

#### Tasks

- [ ] **`IWebhookResponseStrategy` implementations**
  - [ ] `WaitForFirstOutputStrategy` — blocks until the trigger workflow produces a named output variable (e.g. `webhookResponse`), returns it as the HTTP response. Configurable timeout (default 30s) → `504 Gateway Timeout` on miss.
  - [ ] `WaitForCompletionStrategy` — blocks until the workflow reaches a terminal state. Returns workflow status + final variables. Hard cap (default 60s).
- [ ] **`WebhookRegistration` schema additions**
  - [ ] Add `ResponseStrategy` (string enum: `async202` / `waitForFirstOutput` / `waitForCompletion`, default `async202`)
  - [ ] Add `ResponseTimeoutSeconds` (int, optional, default `30`)
  - [ ] Add `ResponseVariableName` (string, optional — for `waitForFirstOutput`)
- [ ] **Output-completion notification**
  - [ ] New engine message: `VariableUpdated(executionId, name, value)` published on `EventStream` when `SetVariableModule` or `ModuleResult.VariableUpdates` writes
  - [ ] Strategy subscribes to event stream + filters by `executionId`
- [ ] **Timeout cancellation**
  - [ ] On timeout, dispatcher does **not** cancel the workflow — only the HTTP response is abandoned. Workflow keeps running.

#### Tests (target ~8)

- [ ] `WaitForFirstOutput_VariableSet_ReturnsValue`
- [ ] `WaitForFirstOutput_TimeoutBeforeVariable_Returns504`
- [ ] `WaitForFirstOutput_WorkflowFailsFirst_ReturnsErrorPayload`
- [ ] `WaitForCompletion_WorkflowCompletes_ReturnsFinalVariables`
- [ ] `WaitForCompletion_WorkflowFails_ReturnsErrorPayload`
- [ ] `WaitForCompletion_Timeout_Returns504_WorkflowKeepsRunning`
- [ ] `Async202Strategy_StillDefault_BackwardsCompat`
- [ ] `VariableUpdated_EventPublished_StrategyPicksUp`

---

### 2.3.P3 OAuth2 Singleton/Persisted Token Cache 💾 *(post-MVP, expands Q1)*

**Purpose:** Add a cross-workflow token cache scope — useful in high-throughput scenarios where many workflows hit the same authority and want to share a single token.

**Complexity:** 🟢 Low-Medium

#### Tasks

- [ ] **`SingletonOAuth2TokenCache`**
  - [ ] DI singleton; survives execution boundaries; shared across all workflows in the host process
  - [ ] Thread-safe via `ConcurrentDictionary<TokenCacheKey, CachedToken>`
- [ ] **Persisted cache option** *(stretch)*
  - [ ] `IOAuth2TokenStore` over `IPersistenceProvider` (NATS KV ideal); survives restarts
  - [ ] Encryption-at-rest for cached tokens (using ASP.NET Data Protection)
- [ ] **Schema addition**
  - [ ] Extend `oauth2TokenCacheScope` enum: `module` / `pipeline` / `singleton` / `persisted`
- [ ] **Security review checklist**
  - [ ] Document threat model: process compromise → all cached tokens exposed
  - [ ] Encryption at rest mandatory for `persisted`

#### Tests (target ~5)

- [ ] `SingletonScope_SharesCacheAcrossExecutions`
- [ ] `SingletonScope_DifferentAuthorities_CachedSeparately`
- [ ] `PersistedScope_SurvivesHostRestart` *(integration)*
- [ ] `PersistedScope_TokensEncryptedAtRest`
- [ ] `PersistedScope_FallsBackToFetchOnDecryptionFailure`

---

### 2.3.P4 Multipart Stream Support 🌊 *(post-MVP, expands Q5)*

**Purpose:** Allow `Stream` objects (e.g. from another module's output) as multipart parts. Avoids materialising large payloads into `byte[]`.

**Complexity:** 🟢 Low

#### Tasks

- [ ] **Encoder additions** — `RequestBodyEncoder` recognises `Stream` (or `Func<Stream>`) as a part value → uses `StreamContent`
- [ ] **Lifetime management** — encoder takes ownership of the stream; disposes after request completes (success or failure)
- [ ] **Schema update** — `multipart/form-data` documentation lists `Stream` as supported part type

#### Tests (target ~4)

- [ ] `Multipart_StreamPart_RoundTrips`
- [ ] `Multipart_StreamDisposedAfterSend`
- [ ] `Multipart_StreamDisposedOnFailure`
- [ ] `Multipart_LargeStream_StreamedNotBuffered` *(memory assertion)*

---

### 2.3.P5 Multipart File-Path Support 📂 *(post-MVP, expands Q5)*

**Purpose:** Allow file paths as multipart parts — the encoder reads the file as a stream. Security-sensitive: opt-in via capability flag.

**Complexity:** 🟢 Low

#### Tasks

- [ ] **`allowFileUpload` capability flag** — new module property (bool, default `false`); without it, file-path parts fail validation
- [ ] **Path allowlist** *(stretch)* — `fileUploadAllowedDirectories` (`Arr<string>`, optional) — when set, only paths under these roots are accepted (prevents directory traversal)
- [ ] **Encoder integration** — `RequestBodyEncoder` recognises `FilePart` value type (DTO with `path`, `contentType`, `fileName`)
- [ ] **Security defaults** — symlinks resolved + checked; absolute paths required (no relative paths)

#### Tests (target ~6)

- [ ] `Multipart_FilePart_WithoutAllowFileUpload_Fails`
- [ ] `Multipart_FilePart_WithAllowFileUpload_Succeeds`
- [ ] `Multipart_FilePart_OutsideAllowlist_Fails`
- [ ] `Multipart_FilePart_RelativePath_Rejected`
- [ ] `Multipart_FilePart_SymlinkEscape_Rejected`
- [ ] `Multipart_FilePart_RoundTripsContent`

---

### 2.3.P6 GraphQL Module 🔍 *(post-MVP, resolves Q6)*

**Purpose:** Specialised client for GraphQL APIs. Thin wrapper around `HttpRequestModule` — POST a query to a single endpoint, handle GraphQL-specific error shape~ 🌷

**Complexity:** 🟢 Low

#### Tasks

- [ ] **`GraphQLQueryModule`** 🔍
  - [ ] New file: `Workflow.Modules/Builtin/Http/GraphQLQueryModule.cs`
  - [ ] `ModuleId: "builtin.http.graphql"`, `Category: "Network"`
  - [ ] Schema:
    - [ ] Inputs: `endpoint` (string, required), `query` (string, required), `variables` (object, optional), `operationName` (string, optional), `headers` (`HashMap<string,string>`, optional), `authType`/auth props (reused from 2.3.2)
    - [ ] Outputs: `data` (object), `errors` (`Arr<object>`), `extensions` (object, optional), `success` (bool)
  - [ ] Executes via internal `HttpRequestModule` invocation (or shared low-level helper)
  - [ ] Detects partial-success responses: GraphQL spec allows `200 OK` with both `data` and `errors` populated → `success = data != null && errors.Length == 0`

#### Tests (target ~6)

- [ ] `GraphQLQuery_SimpleQuery_ReturnsData` *(WireMock)*
- [ ] `GraphQLQuery_WithVariables_SerialisesCorrectly`
- [ ] `GraphQLQuery_GraphQLErrors_PopulatesErrorsOutput`
- [ ] `GraphQLQuery_PartialData_SuccessFalse_DataAvailable`
- [ ] `GraphQLQuery_AuthHeaders_Passed`
- [ ] `GraphQLQuery_NetworkFailure_Fails`

---

## Phase 2.3 Deliverables ✅

**V1 (MVP) Completion Criteria:**
- [ ] 2.3.0 shipped: `HttpRequestModule` core (GET/POST/JSON minimum) + DI infra + `AddWorkflowModules()` aggregate registration
- [x] 2.3.1 shipped: form/multipart-byte[]/XML/raw body + content-type-aware response decoding ✅ **(May 19, 2026)**
- [x] 2.3.2 shipped: Basic, Bearer, API Key auth + header redaction ✅ **(May 19, 2026)**
- [ ] 2.3.3 shipped: OAuth2 client credentials + selectable `module`/`pipeline` token cache scope + refresh-on-401
- [ ] 2.3.4 shipped: Polly retry + timeout + circuit breaker + Retry-After honouring (capped by `maxRetryBackoffSeconds`)
- [ ] 2.3.5 shipped: URL templating + JSONPath/regex/header response extraction
- [ ] 2.3.6 shipped: `WebhookTriggerModule` + `IWebhookRegistrationRepository` + `WebhookDispatcher` + `IWebhookResponseStrategy` (default async-202) + API endpoints
- [ ] 2.3.7 shipped: HMAC/GitHub/Stripe signature validation + replay protection
- [ ] 2.3.8 shipped: end-to-end demo + persistence test + `docs/http-and-network.md`
- [ ] Modules: `builtin.http.request`, `builtin.http.webhook`
- [ ] ~82 unit + integration tests passing across 2.3.0–2.3.8 (2.3.0 ~10 + 2.3.1 ~8 + 2.3.2 ~9 + 2.3.3 ~10 + 2.3.4 ~11 + 2.3.5 ~8 + 2.3.6 ~12 + 2.3.7 ~7 + 2.3.8 ~4)
- [ ] XML docs + `docs/http-and-network.md`
- [ ] Sample workflow runs end-to-end on persistence + API stack

**Post-MVP Tracked Slices** *(non-blocking — see Post-MVP Slices section above):*
- [ ] **2.3.P1** Arbitrary-Path Webhook Routing (expands Q2) — ~6 tests
- [ ] **2.3.P2** Sync Webhook Responses (expands Q3) — ~8 tests
- [ ] **2.3.P3** OAuth2 Singleton/Persisted Token Cache (expands Q1) — ~5 tests
- [ ] **2.3.P4** Multipart Stream Support (expands Q5) — ~4 tests
- [ ] **2.3.P5** Multipart File-Path Support (expands Q5) — ~6 tests
- [ ] **2.3.P6** GraphQL Module (resolves Q6) — ~6 tests

**New / Modified Files (planned):**
```
Workflow.Core/
  Models/WebhookRegistration.cs                         ← new (2.3.6)

Workflow.Modules/
  WorkflowModulesServiceCollectionExtensions.cs         ← new (2.3.0) — aggregate AddWorkflowModules()

Workflow.Modules/Builtin/Http/
  HttpRequestModule.cs                                  ← new (2.3.0, extended in 2.3.1–2.3.5)
  WebhookTriggerModule.cs                               ← new (2.3.6)
  HttpModuleServiceCollectionExtensions.cs              ← new (2.3.0)
  Internal/RequestBodyEncoder.cs                        ← new (2.3.1)
  Internal/ResponseBodyDecoder.cs                       ← new (2.3.1)
  Internal/JsonPathExtractor.cs                         ← new (2.3.5)
  Auth/IHttpAuthStrategy.cs (+ Basic/Bearer/ApiKey)     ← new (2.3.2)
  Auth/OAuth2ClientCredentialsStrategy.cs               ← new (2.3.3)
  Auth/IOAuth2TokenCache.cs                             ← new (2.3.3)
  Auth/PerModuleOAuth2TokenCache.cs                     ← new (2.3.3)
  Auth/PerPipelineOAuth2TokenCache.cs                   ← new (2.3.3)
  Resilience/HttpResiliencePipelineFactory.cs           ← new (2.3.4)

Workflow.Persistence/
  Abstractions/IWebhookRegistrationRepository.cs        ← new (2.3.6)
  InMemoryWebhookRegistrationRepository.cs              ← new (2.3.6)

Workflow.Persistence.Sqlite/
  SqliteWebhookRegistrationRepository.cs                ← new (2.3.6)
  Migrations/Migration_005_Webhooks.cs                  ← new (2.3.6)

Workflow.Api/
  Controllers/WebhooksController.cs                     ← new (2.3.6)
  Webhooks/WebhookDispatcher.cs                         ← new (2.3.6) — forward-compat for 2.3.P1
  Webhooks/IWebhookResponseStrategy.cs                  ← new (2.3.6) — forward-compat for 2.3.P2
  Webhooks/Async202ResponseStrategy.cs                  ← new (2.3.6)
  Webhooks/IWebhookSignatureValidator.cs (+ impls)      ← new (2.3.7)
  Program.cs                                            ← + AddHttpModules (2.3.0)

Workflow.Tests/Modules/Http/
  HttpRequestModuleTests.cs                             ← new (2.3.0)
  HttpBodyFormatTests.cs                                ← new (2.3.1)
  HttpAuthTests.cs                                      ← new (2.3.2)
  OAuth2Tests.cs                                        ← new (2.3.3)
  HttpRetryTests.cs                                     ← new (2.3.4)
  HttpTransformationTests.cs                            ← new (2.3.5)
  WebhookTriggerModuleTests.cs                          ← new (2.3.6)
  HttpPersistenceTests.cs                               ← new (2.3.8)
  HttpE2ETests.cs                                       ← new (2.3.8)

Workflow.Tests/Api/
  WebhookApiTests.cs                                    ← new (2.3.6)
  WebhookSignatureTests.cs                              ← new (2.3.7)

docs/http-and-network.md                                ← new (2.3.8)
examples/definitions/http-integration-demo.json         ← new (2.3.8)

Directory.Packages.props
  + WireMock.Net                                        (test dependency)
  + Microsoft.Extensions.Http                           (2.3.0)
  + Polly.Core                                          (2.3.4)
  + JsonPath.Net                                        (2.3.5)

Post-MVP additions (deferred):
  Workflow.Modules/Builtin/Http/GraphQLQueryModule.cs   ← new (2.3.P6)
  Workflow.Modules/Builtin/Http/Auth/SingletonOAuth2TokenCache.cs   ← new (2.3.P3)
  Workflow.Modules/Builtin/Http/Auth/PersistedOAuth2TokenStore.cs   ← new (2.3.P3, stretch)
  Workflow.Api/Webhooks/WebhookPathRouter.cs            ← new (2.3.P1)
  Workflow.Api/Webhooks/WaitForFirstOutputStrategy.cs   ← new (2.3.P2)
  Workflow.Api/Webhooks/WaitForCompletionStrategy.cs    ← new (2.3.P2)
```

---

## ✅ Resolved Questions

| # | Question | Status | Note |
|---|----------|--------|------|
| **D1** | HttpClient lifetime | ✅ `IHttpClientFactory` named client | Avoids socket exhaustion |
| **D2** | Test strategy | ✅ WireMock.Net in-process + `WebApplicationFactory` | Docker-free; all tests in `Workflow.Tests` |
| **D3** | Auth shape | ✅ Single module + `authType` property | Avoids module fan-out |
| **D4** | Webhook persistence | ✅ Via `IWebhookRegistrationRepository` | In-memory default, SQLite impl |
| **D5** | Transformation engine | ✅ JSONPath.NET + IExpressionEvaluator (Jint) | XPath deferred |
| **D6** | Cancellation | ✅ Native via 2.2.0b hierarchical CTS | No new surface |
| **D7** | SOAP | ✅ Deferred indefinitely | Out of scope |
| **Q1** | OAuth2 token store scope | ✅ Selectable `module`/`pipeline` for V1; `singleton`/`persisted` deferred to **2.3.P3** | `oauth2TokenCacheScope` property |
| **Q2** | Webhook URL design | ✅ `/webhooks/{webhookId}` for V1; arbitrary paths deferred to **2.3.P1** | V1 uses `WebhookDispatcher` (forward-compat for path router) |
| **Q3** | Webhook trigger response | ✅ Always `202 Accepted + executionId` for V1; sync responses deferred to **2.3.P2** | V1 uses `IWebhookResponseStrategy` (forward-compat) |
| **Q4** | Retry-After header | ✅ Honour up to `maxRetryBackoffSeconds` cap (default 60s) | `429`/`503` |
| **Q5** | Multipart parts | ✅ `byte[]` only for V1; `Stream` deferred to **2.3.P4**; file-path deferred to **2.3.P5** | Security default-deny |
| **Q6** | GraphQL as sub-phase or separate | ✅ Deferred to **2.3.P6** (post-MVP) | Not blocking 2.4+ |

---

> 💖 **Ami's Phase 2.3 Tips:**
> - Build **2.3.0 first** — every other slice extends `HttpRequestModule`. Don't try to land auth + retry in the same PR; they touch different concerns and tests live in separate files anyway~ 🧠
> - Use **WireMock.Net in-process** — it spins up a tiny in-memory server per test, no Docker needed. Way faster than `HttpMessageHandler` mocks because you exercise the *real* `HttpClient` socket stack~ ⚡
> - The **webhook trigger** (2.3.6) is the only slice that touches `Workflow.Api`. Land it after 2.3.5 so the request module is stable; otherwise you'll be debugging two things at once~ 🌸
> - **Don't reinvent OAuth2** — copy the client-credentials shape from any reference impl (e.g. `IdentityModel.OidcClient`); just wrap the token-fetch + cache. We don't need full OIDC for v1~ 🔐
> - When in doubt about retry config, **default to `retryCount: 0`** (opt-in). Silent retries are a debugging nightmare; authors should explicitly choose resiliency~ 🛡️ UwU 💖

