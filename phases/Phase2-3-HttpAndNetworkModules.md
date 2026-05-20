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

- [ ] **Q1 OAuth2 token store scope:** per-module-instance (simple) vs DI singleton keyed by `(authority, clientId)` (more reuse across workflows). Recommend **singleton-keyed** for token reuse but needs decision on cache eviction (TTL based on `expires_in`).
- [ ] **Q2 Webhook routing strategy:** `POST /webhooks/{webhookId}` lookup-then-fire (synchronous trigger response) vs `POST /webhooks/{path}` arbitrary-path matching (more flexible URL design). Recommend **`{webhookId}`** for v1 (simpler validation, no path conflicts with existing API routes).
- [ ] **Q3 Webhook trigger module return semantics:** does the webhook trigger module *return* a response to the caller (sync) or always 202-Accepted (async)? Recommend **always 202-Accepted with execution ID** for v1 — callers poll `/executions/{id}/status` from Phase 2.2.6 (when those endpoints land).
- [ ] **Q4 Retry-after header honouring:** when server responds `429` + `Retry-After`, should Polly honour the header value over the configured backoff? Recommend **yes** for `429` and `503`.
- [ ] **Q5 Multipart/form-data file source:** authors can pass `byte[]`, `Stream`, or file path. File path opens an FS-read surface — should this require an opt-in `allowFileUpload` capability flag? Recommend **yes** (security default-deny).
- [ ] **Q6 GraphQL as sub-phase or separate?** Listed in original 2.3 scope. Recommend **separate Phase 2.3.x** slice (low-priority; can ship after webhook trigger).

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

- [ ] **`Workflow.Modules.Http` project layout** 📁
  - [ ] New folder: `Workflow.Modules/Builtin/Http/`
  - [ ] Add `System.Net.Http.Json`, `Microsoft.Extensions.Http` to `Directory.Packages.props`
  - [ ] DI registration helper: `Workflow.Modules/Builtin/Http/HttpModuleServiceCollectionExtensions.cs`
    - [ ] `AddHttpModules(this IServiceCollection)` → registers `IHttpClientFactory`, named client `"dotflow.http"`, default `SocketsHttpHandler` with sensible timeouts
  - [ ] Update `Workflow.Api/Program.cs` to call `services.AddHttpModules()`

- [ ] **`HttpRequestModule` v1** 🌐
  - [ ] New file: `Workflow.Modules/Builtin/Http/HttpRequestModule.cs`
  - [ ] `ModuleId: "builtin.http.request"`, `Category: "Network"`, `Icon: "🌐"`, `Version: 1.0.0`
  - [ ] Schema (v1 — minimal):
    - [ ] Input: `url` (string, required)
    - [ ] Input: `method` (string enum: GET/POST/PUT/DELETE/PATCH/HEAD/OPTIONS, default `"GET"`)
    - [ ] Input: `headers` (`HashMap<string,string>`, optional)
    - [ ] Input: `body` (object, optional — JSON-serialised by default)
    - [ ] Input: `timeoutSeconds` (int, optional, default `30`)
    - [ ] Output: `statusCode` (int), `headers` (`HashMap<string,string>`), `body` (object), `success` (bool — true when 200-299), `durationMs` (long)
  - [ ] `ExecuteAsync`:
    - [ ] Resolve named `HttpClient` from `IHttpClientFactory` (constructor-injected)
    - [ ] Build `HttpRequestMessage` with method, URL, headers, JSON body
    - [ ] Use `CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken)` with `timeoutSeconds`
    - [ ] Send + parse status, headers, response body (JSON if `Content-Type: application/json`, else string)
    - [ ] Return `ModuleResult.Ok(outputs)`
    - [ ] On `HttpRequestException` / `TaskCanceledException` (timeout) → `ModuleResult.Fail(ex.Message, ex)`
  - [ ] `ValidateConfiguration`:
    - [ ] Validate `method` is a known HTTP verb
    - [ ] Validate `url` is a well-formed URI (when statically provided)
    - [ ] Validate `timeoutSeconds > 0`

- [ ] **Registration**
  - [ ] Append `new HttpRequestModule()` to `BuiltinModuleRegistration.GetAll()` (count 16 → 17)

### Tests (target ~10): → `Workflow.Tests/Modules/Http/HttpRequestModuleTests.cs`

- [ ] `HttpRequestModule_Metadata_IsCorrect`
- [ ] `HttpRequestModule_Schema_HasRequiredPorts`
- [ ] `ValidateConfiguration_InvalidMethod_Fails`
- [ ] `ValidateConfiguration_InvalidUrl_Fails`
- [ ] `Get_ReturnsStatus200AndJsonBody` *(WireMock)*
- [ ] `Post_WithJsonBody_SendsCorrectContentType` *(WireMock)*
- [ ] `Headers_ArePassedThrough` *(WireMock)*
- [ ] `Timeout_ExceededReturnsFail` *(WireMock with delay)*
- [ ] `CancellationToken_HonouredMidFlight` *(WireMock)*
- [ ] `NonJsonResponse_ReturnsStringBody` *(WireMock returning `text/plain`)*

---

## 2.3.1 Body & Response Format Support 📦

> **Purpose:** Expand the request module to handle the four content types real APIs actually use (beyond JSON): form-encoded, multipart, XML, raw bytes/string. Smart response deserialization via `Content-Type` detection~ 🎀

**Complexity:** 🟡 Medium

### Tasks

- [ ] **Request body strategies** 📤
  - [ ] New file: `Workflow.Modules/Builtin/Http/Internal/RequestBodyEncoder.cs`
  - [ ] Strategy interface: `Encode(object body, string contentType) -> HttpContent`
  - [ ] Built-in strategies:
    - [ ] `application/json` → `JsonContent.Create(body)` *(default)*
    - [ ] `application/x-www-form-urlencoded` → `FormUrlEncodedContent` from `HashMap<string,string>`
    - [ ] `multipart/form-data` → `MultipartFormDataContent` (supports `byte[]`, `Stream`, opt-in file path with `allowFileUpload`)
    - [ ] `application/xml` / `text/xml` → string-passthrough (validate XML well-formedness)
    - [ ] `text/plain` → string-passthrough
    - [ ] `application/octet-stream` → byte-array passthrough
  - [ ] Schema additions on `HttpRequestModule`:
    - [ ] Input: `contentType` (string, optional — falls back to JSON for object bodies)
    - [ ] Input: `allowFileUpload` (bool, optional, default `false`) — Q5 opt-in
  - [ ] Update `ValidateConfiguration` to recognise known content types (warning, not error, for unknown)

- [ ] **Response body decoding** 📥
  - [ ] New file: `Workflow.Modules/Builtin/Http/Internal/ResponseBodyDecoder.cs`
  - [ ] Decision tree by `Content-Type`:
    - [ ] `application/json*` → `JsonDocument` round-trip → `Dictionary<string,object?>`
    - [ ] `application/xml` / `text/xml` → string (deferred: XML→object map)
    - [ ] `text/*` → string
    - [ ] anything binary → `byte[]`
  - [ ] Output port additions: none (existing `body` port carries decoded value)
  - [ ] Output port addition: `contentType` (string) — for downstream switching

### Tests (target ~10): → `Workflow.Tests/Modules/Http/HttpBodyFormatTests.cs`

- [ ] `Post_FormUrlEncoded_SendsCorrectContentType_AndBody` *(WireMock)*
- [ ] `Post_MultipartFormData_WithByteArrayPart_RoundTrips` *(WireMock)*
- [ ] `Post_MultipartFormData_WithFilePath_FailsWithoutAllowFileUpload`
- [ ] `Post_MultipartFormData_WithFilePath_SucceedsWithAllowFileUpload`
- [ ] `Post_XmlBody_PassedThroughAsString` *(WireMock)*
- [ ] `Post_RawBytes_OctetStream_RoundTrips` *(WireMock)*
- [ ] `Response_ApplicationJson_DeserialisedToDictionary` *(WireMock)*
- [ ] `Response_TextPlain_ReturnedAsString` *(WireMock)*
- [ ] `Response_OctetStream_ReturnedAsByteArray` *(WireMock)*
- [ ] `ContentType_OutputPort_Populated` *(WireMock)*

---

## 2.3.2 Authentication: Basic + Bearer + API Key 🔐

> **Purpose:** Land the three simple, synchronous auth strategies — no token refresh, no OAuth dance. These cover ~80% of real API integrations. OAuth2 lives in its own slice (2.3.3)~ 🌸

**Complexity:** 🟢 Low-Medium

### Tasks

- [ ] **Auth strategy interface** 🔧
  - [ ] New file: `Workflow.Modules/Builtin/Http/Auth/IHttpAuthStrategy.cs`
  - [ ] `Task ApplyAsync(HttpRequestMessage request, ModuleExecutionContext ctx, CancellationToken ct)`
  - [ ] Built-in implementations co-located:
    - [ ] `BasicAuthStrategy` — `username`, `password` → `Authorization: Basic {base64}`
    - [ ] `BearerAuthStrategy` — `bearerToken` → `Authorization: Bearer {token}`
    - [ ] `ApiKeyAuthStrategy` — `apiKey`, `apiKeyHeader` (default `X-API-Key`), `apiKeyLocation` (`header` or `query`)

- [ ] **`HttpRequestModule` schema additions** 🎀
  - [ ] Input: `authType` (string enum: `none`/`basic`/`bearer`/`apikey`/`oauth2`, default `none`)
  - [ ] Inputs grouped by auth type (only relevant ones are read):
    - Basic: `username`, `password`
    - Bearer: `bearerToken`
    - API Key: `apiKey`, `apiKeyHeader`, `apiKeyLocation`
  - [ ] Auth strategy resolved via simple `switch` (no DI factory needed for these three)

- [ ] **Header redaction in logs** 🔒
  - [ ] `Authorization`, `X-API-Key`, and other auth-related headers must be redacted in `ctx.Logger.LogDebug` output

### Tests (target ~9): → `Workflow.Tests/Modules/Http/HttpAuthTests.cs`

- [ ] `BasicAuth_Base64EncodedCorrectly` *(WireMock verify header)*
- [ ] `BasicAuth_MissingCredentials_Fails`
- [ ] `BearerAuth_AddsAuthorizationHeader` *(WireMock)*
- [ ] `BearerAuth_MissingToken_Fails`
- [ ] `ApiKeyAuth_InHeader_AddedToHeaders` *(WireMock)*
- [ ] `ApiKeyAuth_InQuery_AppendedToUrl` *(WireMock)*
- [ ] `ApiKeyAuth_CustomHeaderName_Honoured` *(WireMock)*
- [ ] `AuthType_None_NoAuthHeaderAdded` *(WireMock)*
- [ ] `AuthHeaders_RedactedInDebugLog`

---

## 2.3.3 OAuth2 Client Credentials Flow 🔑

> **Purpose:** Implement the most common machine-to-machine OAuth2 flow (client credentials grant). Token caching + automatic refresh. Other flows (auth code, device flow) deferred~ 🌷

**Complexity:** 🟡 Medium

### Tasks

- [ ] **OAuth2 token cache** 💾
  - [ ] New file: `Workflow.Modules/Builtin/Http/Auth/IOAuth2TokenCache.cs`
  - [ ] Default impl: `InMemoryOAuth2TokenCache` — keyed on `(authority, clientId, scope)`, TTL = `expires_in - 30s` safety margin
  - [ ] Register as `Singleton<IOAuth2TokenCache>` in `AddHttpModules`
  - [ ] Resolves **Q1**: singleton-keyed (token reused across workflows)

- [ ] **OAuth2 strategy** 🔧
  - [ ] New file: `Workflow.Modules/Builtin/Http/Auth/OAuth2ClientCredentialsStrategy.cs`
  - [ ] Inputs (on `HttpRequestModule`): `oauth2TokenUrl`, `oauth2ClientId`, `oauth2ClientSecret`, `oauth2Scope`, optional `oauth2Audience`
  - [ ] Flow:
    - [ ] Check cache for unexpired token
    - [ ] If miss/expired → `POST {tokenUrl}` with `grant_type=client_credentials` (`application/x-www-form-urlencoded`)
    - [ ] Parse response: `access_token`, `expires_in`, `token_type` (must be `Bearer`)
    - [ ] Cache + apply as `Authorization: Bearer {access_token}`
  - [ ] Failures: structured error mapping (`invalid_client`, `invalid_scope`, etc. → `ModuleResult.Fail` with code)

- [ ] **Refresh-on-401 retry** 🔄
  - [ ] If a request fails with `401 Unauthorized` and `authType == oauth2`, invalidate cache + retry once
  - [ ] Hard fail on second `401`

### Tests (target ~8): → `Workflow.Tests/Modules/Http/OAuth2Tests.cs`

- [ ] `OAuth2_FirstCall_FetchesTokenFromAuthority` *(WireMock for token endpoint + protected endpoint)*
- [ ] `OAuth2_SecondCall_UsesCachedToken_NoTokenFetch`
- [ ] `OAuth2_TokenExpired_RefetchesToken`
- [ ] `OAuth2_401Response_InvalidatesCacheAndRetries`
- [ ] `OAuth2_DoubleAuth401_Fails`
- [ ] `OAuth2_InvalidClient_ReturnsFail`
- [ ] `OAuth2_DifferentScopes_CachedSeparately`
- [ ] `OAuth2_TokenCache_EvictionTimingRespectsExpiresIn`

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
  - [ ] Input: `retryOnStatusCodes` (`Arr<int>`, optional, default `[408, 429, 500, 502, 503, 504]`)
  - [ ] Input: `circuitBreakerFailureThreshold` (int, optional, default `0` — disabled)
  - [ ] Input: `circuitBreakerSamplingDurationSeconds` (double, optional, default `30`)

- [ ] **Retry-after header support** *(Q4 resolved: yes)* 🎀
  - [ ] When response is `429`/`503` and `Retry-After` header present → honour it (overrides configured backoff)
  - [ ] Add jitter to non-Retry-After delays (Polly's `Jitter` setting)

- [ ] **Outputs on retry** 📊
  - [ ] Add output: `attemptCount` (int) — actual attempts made before success/failure
  - [ ] Add output: `circuitState` (string) — `closed`/`open`/`halfopen` (if circuit configured)

### Tests (target ~10): → `Workflow.Tests/Modules/Http/HttpRetryTests.cs`

- [ ] `Retry_OnTransient500_RetriesAndSucceeds` *(WireMock with response sequence)*
- [ ] `Retry_OnPermanent404_DoesNotRetry`
- [ ] `Retry_MaxAttemptsExceeded_FailsWithLastError`
- [ ] `Retry_ExponentialBackoff_DelaysIncrease` *(time-based assertion with tolerance)*
- [ ] `Retry_RetryAfterHeader_OverridesBackoff`
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
  - [ ] New file: `Workflow.Api/Controllers/WebhooksController.cs` *(or minimal-API mapping in `Program.cs`)*
  - [ ] `POST /webhooks/{webhookId}` — trigger endpoint *(see 2.3.7 for signature validation)*
    - [ ] Lookup registration → check method allowed → kick off execution via `WorkflowSupervisor.CreateWorkflowInstance`
    - [ ] Return `202 Accepted` with `{ executionId }`
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

## 2.3.8 GraphQL Module *(optional, low priority)* 🔍

> **Purpose:** Specialised client for GraphQL APIs. Thin wrapper around `HttpRequestModule` — POST a query to a single endpoint, handle GraphQL-specific error shape~ 🌷

**Complexity:** 🟢 Low

### Tasks

- [ ] **`GraphQLQueryModule`** 🔍
  - [ ] New file: `Workflow.Modules/Builtin/Http/GraphQLQueryModule.cs`
  - [ ] `ModuleId: "builtin.http.graphql"`, `Category: "Network"`
  - [ ] Schema:
    - [ ] Inputs: `endpoint` (string, required), `query` (string, required), `variables` (object, optional), `operationName` (string, optional), `headers` (`HashMap<string,string>`, optional), `authType`/auth props (reused from 2.3.2)
    - [ ] Outputs: `data` (object), `errors` (`Arr<object>`), `extensions` (object, optional), `success` (bool)
  - [ ] Executes via internal `HttpRequestModule` invocation (or shared low-level helper)
  - [ ] Detects partial-success responses: GraphQL spec allows `200 OK` with both `data` and `errors` populated → `success = data != null && errors.Length == 0`

### Tests (target ~6): → `Workflow.Tests/Modules/Http/GraphQLQueryModuleTests.cs`

- [ ] `GraphQLQuery_SimpleQuery_ReturnsData` *(WireMock)*
- [ ] `GraphQLQuery_WithVariables_SerialisesCorrectly`
- [ ] `GraphQLQuery_GraphQLErrors_PopulatesErrorsOutput`
- [ ] `GraphQLQuery_PartialData_SuccessFalse_DataAvailable`
- [ ] `GraphQLQuery_AuthHeaders_Passed`
- [ ] `GraphQLQuery_NetworkFailure_Fails`

---

## 2.3.9 Engine Integration & End-to-End Demo 🎯

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

## Phase 2.3 Deliverables ✅

**Completion Criteria:**
- [ ] 2.3.0 shipped: `HttpRequestModule` core (GET/POST/JSON minimum) + DI infra
- [ ] 2.3.1 shipped: form/multipart/XML/raw body + content-type-aware response decoding
- [ ] 2.3.2 shipped: Basic, Bearer, API Key auth + header redaction
- [ ] 2.3.3 shipped: OAuth2 client credentials + token cache + refresh-on-401
- [ ] 2.3.4 shipped: Polly retry + timeout + circuit breaker + Retry-After honouring
- [ ] 2.3.5 shipped: URL templating + JSONPath/regex/header response extraction
- [ ] 2.3.6 shipped: `WebhookTriggerModule` + `IWebhookRegistrationRepository` + API endpoints
- [ ] 2.3.7 shipped: HMAC/GitHub/Stripe signature validation + replay protection
- [ ] 2.3.8 *(optional)* shipped: `GraphQLQueryModule`
- [ ] 2.3.9 shipped: end-to-end demo + persistence test + `docs/http-and-network.md`
- [ ] Modules: `builtin.http.request`, `builtin.http.webhook`, *(optional)* `builtin.http.graphql`
- [ ] ~84 unit + integration tests passing across 2.3.0–2.3.9 (2.3.0 ~10 + 2.3.1 ~10 + 2.3.2 ~9 + 2.3.3 ~8 + 2.3.4 ~10 + 2.3.5 ~8 + 2.3.6 ~12 + 2.3.7 ~7 + 2.3.8 ~6 + 2.3.9 ~4)
- [ ] XML docs + `docs/http-and-network.md`
- [ ] Sample workflow runs end-to-end on persistence + API stack

**New / Modified Files (planned):**
```
Workflow.Core/
  Models/WebhookRegistration.cs                         ← new (2.3.6)

Workflow.Modules/Builtin/Http/
  HttpRequestModule.cs                                  ← new (2.3.0, extended in 2.3.1–2.3.5)
  WebhookTriggerModule.cs                               ← new (2.3.6)
  GraphQLQueryModule.cs                                 ← new (2.3.8, optional)
  HttpModuleServiceCollectionExtensions.cs              ← new (2.3.0)
  Internal/RequestBodyEncoder.cs                        ← new (2.3.1)
  Internal/ResponseBodyDecoder.cs                       ← new (2.3.1)
  Internal/JsonPathExtractor.cs                         ← new (2.3.5)
  Auth/IHttpAuthStrategy.cs (+ Basic/Bearer/ApiKey)     ← new (2.3.2)
  Auth/OAuth2ClientCredentialsStrategy.cs               ← new (2.3.3)
  Auth/IOAuth2TokenCache.cs (+ InMemory impl)           ← new (2.3.3)
  Resilience/HttpResiliencePipelineFactory.cs           ← new (2.3.4)

Workflow.Persistence/
  Abstractions/IWebhookRegistrationRepository.cs        ← new (2.3.6)
  InMemoryWebhookRegistrationRepository.cs              ← new (2.3.6)

Workflow.Persistence.Sqlite/
  SqliteWebhookRegistrationRepository.cs                ← new (2.3.6)
  Migrations/Migration_005_Webhooks.cs                  ← new (2.3.6)

Workflow.Api/
  Controllers/WebhooksController.cs                     ← new (2.3.6)
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
  GraphQLQueryModuleTests.cs                            ← new (2.3.8, optional)
  HttpPersistenceTests.cs                               ← new (2.3.9)
  HttpE2ETests.cs                                       ← new (2.3.9)

Workflow.Tests/Api/
  WebhookApiTests.cs                                    ← new (2.3.6)
  WebhookSignatureTests.cs                              ← new (2.3.7)

docs/http-and-network.md                                ← new (2.3.9)
examples/definitions/http-integration-demo.json         ← new (2.3.9)

Directory.Packages.props
  + WireMock.Net                                        (test dependency)
  + Microsoft.Extensions.Http                           (2.3.0)
  + Polly.Core                                          (2.3.4)
  + JsonPath.Net                                        (2.3.5)
```

---

## ✅ Resolved vs ❓ Open

| # | Question | Status | Note |
|---|----------|--------|------|
| **D1** | HttpClient lifetime | ✅ `IHttpClientFactory` named client | Avoids socket exhaustion |
| **D2** | Test strategy | ✅ WireMock.Net in-process + `WebApplicationFactory` | Docker-free; all tests in `Workflow.Tests` |
| **D3** | Auth shape | ✅ Single module + `authType` property | Avoids module fan-out |
| **D4** | Webhook persistence | ✅ Via `IWebhookRegistrationRepository` | In-memory default, SQLite impl |
| **D5** | Transformation engine | ✅ JSONPath.NET + IExpressionEvaluator (Jint) | XPath deferred |
| **D6** | Cancellation | ✅ Native via 2.2.0b hierarchical CTS | No new surface |
| **D7** | SOAP | ✅ Deferred indefinitely | Out of scope |
| **Q1** | OAuth2 token store scope | ❓ Recommend **singleton-keyed** | Cache eviction via `expires_in` |
| **Q2** | Webhook URL design | ❓ Recommend `/webhooks/{webhookId}` | Simpler validation |
| **Q3** | Webhook trigger response | ❓ Recommend `202 Accepted + executionId` | Async by default |
| **Q4** | Retry-After header | ✅ Yes, honour over backoff for `429`/`503` | Polly built-in support |
| **Q5** | Multipart file path | ❓ Recommend opt-in `allowFileUpload` flag | Security default-deny |
| **Q6** | GraphQL as sub-phase or separate | ❓ Recommend **separate slice (2.3.8)** | Low priority |

---

> 💖 **Ami's Phase 2.3 Tips:**
> - Build **2.3.0 first** — every other slice extends `HttpRequestModule`. Don't try to land auth + retry in the same PR; they touch different concerns and tests live in separate files anyway~ 🧠
> - Use **WireMock.Net in-process** — it spins up a tiny in-memory server per test, no Docker needed. Way faster than `HttpMessageHandler` mocks because you exercise the *real* `HttpClient` socket stack~ ⚡
> - The **webhook trigger** (2.3.6) is the only slice that touches `Workflow.Api`. Land it after 2.3.5 so the request module is stable; otherwise you'll be debugging two things at once~ 🌸
> - **Don't reinvent OAuth2** — copy the client-credentials shape from any reference impl (e.g. `IdentityModel.OidcClient`); just wrap the token-fetch + cache. We don't need full OIDC for v1~ 🔐
> - When in doubt about retry config, **default to `retryCount: 0`** (opt-in). Silent retries are a debugging nightmare; authors should explicitly choose resiliency~ 🛡️ UwU 💖

