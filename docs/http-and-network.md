# HTTP & Network Modules 🌐🔌

> Phase 2.3 — DotFlow's outbound HTTP client + inbound webhook trigger primitives~ ✨💖
>
> Made with 💖 by Ami-Chan! UwU ✨

---

## Table of Contents

1. [Overview](#overview)
2. [Setup](#setup)
3. [HttpRequest Module (`builtin.http.request`)](#httprequest-module)
   - [Basic Usage](#basic-usage)
   - [Authentication](#authentication)
   - [Retry, Timeout & Circuit Breaker](#retry-timeout--circuit-breaker)
   - [Response Extraction](#response-extraction)
4. [Webhook Trigger Module (`builtin.http.webhook`)](#webhook-trigger-module)
   - [Registering a Webhook](#registering-a-webhook)
   - [Signature Validation](#signature-validation)
   - [Triggered Workflow Inputs](#triggered-workflow-inputs)
5. [Common Patterns](#common-patterns)
6. [Module Reference](#module-reference)

---

## Overview

Phase 2.3 ships two HTTP primitives:

| Module | Direction | Purpose |
|--------|-----------|---------|
| `builtin.http.request` | Outbound ↑ | Make HTTP calls to external APIs with auth, retry, and response extraction~ 🌐 |
| `builtin.http.webhook` | Inbound ↓ | Trigger a workflow from an external HTTP POST (webhook)~ 🪝 |

Both modules are registered automatically when you call `services.AddWorkflowModules()` in your DI setup~ 🎀

---

## Setup

### DI Registration

```csharp
// Program.cs (or wherever you configure DI)
builder.Services.AddWorkflowModules();                              // ← registers IHttpClientFactory + all modules
builder.Services.AddSingleton<IWebhookRegistrationRepository,
    InMemoryWebhookRegistrationRepository>();                       // ← in-memory webhook store (default)
builder.Services.AddSingleton<IWorkflowLauncher, NullWorkflowLauncher>();  // ← replaced with ActorWorkflowLauncher in production
builder.Services.AddSingleton<IWebhookResponseStrategy, Async202ResponseStrategy>();
builder.Services.AddSingleton<WebhookDispatcher>();
```

Then map the webhook endpoints:

```csharp
app.MapWebhookEndpoints();  // registers /webhooks/{id} + /api/webhooks CRUD
```

---

## HttpRequest Module

**Module ID:** `builtin.http.request`  
**Category:** Network  
**Icon:** 🌐

### Basic Usage

Minimal GET request:

```json
{
  "id": "fetch_user",
  "moduleId": "builtin.http.request",
  "properties": {
    "url": "https://api.example.com/users/{{userId}}",
    "method": "GET",
    "timeoutSeconds": 30
  }
}
```

POST with JSON body:

```json
{
  "id": "create_order",
  "moduleId": "builtin.http.request",
  "properties": {
    "url": "https://api.example.com/orders",
    "method": "POST",
    "body": { "productId": "{{productId}}", "quantity": 1 },
    "contentType": "application/json"
  }
}
```

### Output Ports

| Port | Type | Description |
|------|------|-------------|
| `statusCode` | `int` | HTTP response status code (e.g. 200, 404)~ |
| `body` | `object` | Decoded response body — `Dictionary<string,object?>` for JSON, `string` otherwise~ |
| `headers` | `Dictionary<string,string>` | Flattened response headers~ |
| `success` | `bool` | `true` when `statusCode` is 200-299~ |
| `durationMs` | `long` | Round-trip elapsed time in milliseconds~ |
| `contentType` | `string` | Response `Content-Type` media type~ |
| `attemptCount` | `int` | Total send attempts (1 = no retry)~ |
| `circuitState` | `string` | `closed` / `open` / `halfopen` (when circuit breaker configured)~ |

### Body Formats

| `contentType` | Body input type | Notes |
|---------------|----------------|-------|
| `application/json` (default) | Any object | Serialized with `System.Text.Json`~ |
| `application/x-www-form-urlencoded` | `Dictionary<string,string>` | Form-encoded key/value pairs~ |
| `multipart/form-data` | `List<object>` of part dicts | Each part: `{ name, value, filename?, contentType? }`~ |
| `application/xml` / `text/xml` | `string` | Validated for well-formedness before send~ |
| `text/*` | `string` | Passed through as-is~ |
| `application/octet-stream` | `byte[]` | Binary passthrough~ |

### Authentication

Set `authType` plus the corresponding sub-properties:

```json
"properties": {
  "url": "https://api.example.com/data",
  "authType": "bearer",
  "bearerToken": "{{myToken}}"
}
```

| `authType` | Required Properties | Notes |
|------------|---------------------|-------|
| `none` (default) | — | No auth header added~ |
| `basic` | `username`, `password` | `Authorization: Basic {base64}`~ |
| `bearer` | `bearerToken` | `Authorization: Bearer {token}`~ |
| `apikey` | `apiKey`, `apiKeyHeader` (default `X-API-Key`), `apiKeyLocation` (`header` or `query`) | Added to header or query string~ |
| `oauth2` | `oauth2TokenUrl`, `oauth2ClientId`, `oauth2ClientSecret`, `oauth2Scope`, `oauth2Audience` | Client credentials flow + token cache~ |

#### OAuth2 Token Cache Scope

```json
"oauth2TokenCacheScope": "module"    // default — fresh cache per module instance
"oauth2TokenCacheScope": "pipeline"  // shared within one workflow execution
```

> **CopilotNote:** Cross-workflow singleton + persisted cache deferred to 2.3.P3~ 🧠

### Retry, Timeout & Circuit Breaker

Powered by **Polly v8**. All policies are opt-in (`retryCount` defaults to 0):

```json
"properties": {
  "url": "https://api.example.com/data",
  "retryCount": 3,
  "retryBackoff": "exponential",
  "retryDelaySeconds": 1.0,
  "maxRetryBackoffSeconds": 60.0,
  "retryOnStatusCodes": [408, 429, 500, 502, 503, 504],
  "circuitBreakerFailureThreshold": 5,
  "circuitBreakerSamplingDurationSeconds": 30
}
```

| Property | Default | Description |
|----------|---------|-------------|
| `retryCount` | `0` | Number of retries after first attempt~ |
| `retryBackoff` | `exponential` | `linear` / `exponential` / `constant`~ |
| `retryDelaySeconds` | `1.0` | Base delay between retries (seconds)~ |
| `maxRetryBackoffSeconds` | `60.0` | Hard cap on per-attempt sleep (including Retry-After header values)~ |
| `retryOnStatusCodes` | `[408,429,500,502,503,504]` | HTTP status codes that trigger retry~ |
| `circuitBreakerFailureThreshold` | `0` (disabled) | Consecutive failures to trip the breaker~ |
| `circuitBreakerSamplingDurationSeconds` | `30.0` | Sampling window for the circuit breaker~ |
| `timeoutSeconds` | `30` | Per-request timeout (applies to each attempt independently)~ |

#### Retry-After Header

When the server returns a `Retry-After` header with a `429` or `503`, the module honours it **up to `maxRetryBackoffSeconds`**. If the header value exceeds the cap, the configured backoff is used instead~ 🛡️

### Response Extraction

#### JSONPath

Extract specific fields from a JSON response without a downstream `builtin.setvariable`:

```json
"properties": {
  "url": "https://api.example.com/users/1",
  "responseExtract": {
    "userId": "$.id",
    "userName": "$.name",
    "tags": "$.tags[*]"
  },
  "responseExtractRequired": true
}
```

Output ports `userId`, `userName`, `tags` are added dynamically~ ✨

| JSONPath result | Output value |
|----------------|-------------|
| Single scalar | Unwrapped value (not wrapped in array)~ |
| Multiple nodes | JSON array~ |
| No match | `null` (or error if `responseExtractRequired: true`)~ |

#### Regex

Extract text from non-JSON responses (HTML, CSV, plain text):

```json
"responseRegex": {
  "orderId": "Order #(?<value>\\d+)"
}
```

The named capture group `(?<value>...)` is required~ 🔍

#### Header Extraction

```json
"headerExtract": {
  "location": "Location",
  "etag": "ETag"
}
```

Useful for `201 Created` → `Location` header pattern~ 🏷️

---

## Webhook Trigger Module

**Module ID:** `builtin.http.webhook`  
**Category:** Triggers  
**Icon:** 🪝

The webhook trigger enables **external HTTP calls to start workflow executions**~ 🪝

### Registering a Webhook

Register via the management API:

```http
POST /api/webhooks
Content-Type: application/json

{
  "webhookId": "order-placed",
  "workflowDefinitionId": "a1b2c3d4-...",
  "allowedMethods": ["POST"],
  "enabled": true
}
```

Response (`201 Created`):

```json
{
  "webhookId": "order-placed",
  "workflowDefinitionId": "a1b2c3d4-...",
  "allowedMethods": ["POST"],
  "enabled": true,
  "createdAt": "2026-05-21T..."
}
```

### Triggering

Once registered, external systems POST to:

```http
POST /webhooks/order-placed
Content-Type: application/json

{"orderId":"12345","event":"order.placed"}
```

Response (`202 Accepted`):

```json
{ "executionId": "b5beff12-3ae7-..." }
```

### Management Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/api/webhooks` | Register a new webhook~ |
| `GET` | `/api/webhooks` | List all registered webhooks~ |
| `GET` | `/api/webhooks/{webhookId}` | Get a specific webhook~ |
| `PUT` | `/api/webhooks/{webhookId}` | Update an existing webhook~ |
| `DELETE` | `/api/webhooks/{webhookId}` | Delete a webhook~ |

### Signature Validation

Protect webhook endpoints from forged requests using HMAC signatures~ 🔒

Register with a secret + scheme:

```http
POST /api/webhooks
{
  "webhookId": "github-push",
  "workflowDefinitionId": "...",
  "secretKey": "my-super-secret",
  "signatureScheme": "github"
}
```

| Scheme | Header | Format | Notes |
|--------|--------|--------|-------|
| `hmac-sha256` | `X-Signature` | `{lowercase-hex}` | Generic HMAC-SHA256~ |
| `github` | `X-Hub-Signature-256` | `sha256={hex}` | GitHub webhook format~ 🐙 |
| `stripe` | `Stripe-Signature` | `t={unix},v1={hex}` | Includes replay-attack protection (5-min window)~ 💳 |

Requests with invalid / missing signatures → `401 Unauthorized` (failure reason is logged internally, not echoed)~ 🛡️

### Triggered Workflow Inputs

When a webhook fires, the dispatcher pre-seeds the workflow inputs with:

```json
{
  "__webhook__": {
    "body": { ...parsed JSON body... },
    "headers": { "Content-Type": "application/json", ... },
    "query": { "source": "github", ... },
    "method": "POST",
    "receivedAt": "2026-05-21T12:00:00Z"
  }
}
```

### Workflow Node Definition

Add a `builtin.http.webhook` node at the **start** of your workflow:

```json
{
  "id": "trigger",
  "moduleId": "builtin.http.webhook",
  "properties": {
    "webhookId": "order-placed"
  }
}
```

Output ports:

| Port | Type | Description |
|------|------|-------------|
| `body` | `object` | Parsed request body~ |
| `headers` | `Dictionary<string,string>` | Inbound request headers~ |
| `query` | `Dictionary<string,string>` | URL query parameters~ |
| `method` | `string` | HTTP method (e.g. `"POST"`)~ |
| `receivedAt` | `DateTimeOffset` | UTC timestamp when the webhook was received~ |

Connect `trigger.body` → downstream nodes to pass the webhook payload forward~ 🌊

---

## Common Patterns

### Pattern 1 — Webhook → API Call → Persist

```
WebhookTrigger (order-placed)
  └→ HttpRequest (GET /inventory/{{body.orderId}})
       └→ SetVariable (stock = body.available)
```

### Pattern 2 — Parallel API Fan-out with TryCatch

```
WebhookTrigger
  └→ TryCatch
        ├─ try  → Parallel
        │          ├─ api_call_1 (GET, with retry)
        │          └─ api_call_2 (POST, bearer auth)
        ├─ catch → Log("failed: {{error.message}}")
        └─ finally → SetVariable(audit = "done")
```

See `examples/definitions/http-integration-demo.json` for a complete working example~ 🌈

### Pattern 3 — Paginated API via ForEach

```
HttpRequest (GET /items?page=1)
  └→ ForEach (items in body.data)
       └→ HttpRequest (POST /process/{{item.id}})
```

### Pattern 4 — OAuth2 with Retry

```json
{
  "url": "https://api.example.com/protected",
  "authType": "oauth2",
  "oauth2TokenUrl": "https://auth.example.com/token",
  "oauth2ClientId": "{{clientId}}",
  "oauth2ClientSecret": "{{clientSecret}}",
  "oauth2Scope": "read:data",
  "oauth2TokenCacheScope": "pipeline",
  "retryCount": 2,
  "retryBackoff": "exponential"
}
```

On `401 Unauthorized`, the module automatically invalidates the cached token and retries once with a fresh token~ 🔄

---

## Module Reference

### `builtin.http.request` Properties

| Property | Type | Default | Required | Description |
|----------|------|---------|----------|-------------|
| `url` | `string` | — | ✅ | Absolute request URL. Supports `{{Variable}}` references~ |
| `method` | `string` | `"GET"` | | HTTP verb: GET/POST/PUT/DELETE/PATCH/HEAD/OPTIONS~ |
| `headers` | `Dictionary<string,string>` | — | | Custom request headers~ |
| `body` | `object` | — | | Request body (serialisation controlled by `contentType`)~ |
| `contentType` | `string` | auto | | Request body media type~ |
| `timeoutSeconds` | `int` | `30` | | Per-request timeout~ |
| `authType` | `string` | `"none"` | | Auth strategy: `none`/`basic`/`bearer`/`apikey`/`oauth2`~ |
| `username` | `string` | — | | Basic auth username~ |
| `password` | `string` | — | | Basic auth password~ |
| `bearerToken` | `string` | — | | Bearer token~ |
| `apiKey` | `string` | — | | API key value~ |
| `apiKeyHeader` | `string` | `X-API-Key` | | Header name for API key~ |
| `apiKeyLocation` | `string` | `header` | | `header` or `query`~ |
| `oauth2TokenUrl` | `string` | — | | OAuth2 token endpoint~ |
| `oauth2ClientId` | `string` | — | | OAuth2 client ID~ |
| `oauth2ClientSecret` | `string` | — | | OAuth2 client secret~ |
| `oauth2Scope` | `string` | — | | OAuth2 requested scope(s)~ |
| `oauth2Audience` | `string` | — | | Auth0-style audience claim~ |
| `oauth2TokenCacheScope` | `string` | `module` | | `module` or `pipeline`~ |
| `retryCount` | `int` | `0` | | Number of retries (0 = disabled)~ |
| `retryBackoff` | `string` | `exponential` | | `linear`/`exponential`/`constant`~ |
| `retryDelaySeconds` | `double` | `1.0` | | Base retry delay (seconds)~ |
| `maxRetryBackoffSeconds` | `double` | `60.0` | | Max per-attempt sleep (inc. Retry-After)~ |
| `retryOnStatusCodes` | `int[]` | `[408,429,500,502,503,504]` | | Status codes that trigger retry~ |
| `circuitBreakerFailureThreshold` | `int` | `0` (off) | | Consecutive failures to trip the breaker~ |
| `circuitBreakerSamplingDurationSeconds` | `double` | `30.0` | | Breaker sampling window~ |
| `responseExtract` | `Dictionary<string,string>` | — | | JSONPath extractions → named outputs~ |
| `responseExtractRequired` | `bool` | `false` | | Fail module when required paths miss~ |
| `responseRegex` | `Dictionary<string,string>` | — | | Regex extractions from text/HTML body~ |
| `headerExtract` | `Dictionary<string,string>` | — | | Response header extractions~ |

### `builtin.http.webhook` Properties

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `webhookId` | `string` | ✅ | Must match a registered webhook slug~ |

---

> 💖 **Ami-Chan's Tips~**
>
> - Always set `retryCount: 0` unless you explicitly want retries — silent retries make debugging a nightmare~ 🛡️
> - Use `pipeline` cache scope for OAuth2 when multiple nodes in the same workflow call the same API~ ⚡
> - The `__webhook__` input key is a stable contract — do NOT use it for any other purpose or custom modules~ 🌸
> - Signature validation failure reasons are logged internally but NOT echoed to callers — this prevents oracle attacks~ 🔒
> - `builtin.http.request` returns `ModuleResult.Ok` (with `success=false`) for HTTP 4xx/5xx — it does NOT throw. Use `builtin.trycatch` to catch *network* failures only (connection refused, timeout, etc.)~ 🧠 UwU 💖

