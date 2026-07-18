# GlutenFree.DotFlow REST API 🌐

The DotFlow host exposes a versioned HTTP API for managing workflow definitions, running
executions, discovering modules, managing variables, and monitoring the engine. All resource
endpoints live under `/api/v1`; feature tooling (webhooks) keeps its established path.

- **Base URL:** `/api/v1`
- **Version header:** every v1 response carries `api-supported-versions: 1.0`
- **Content type:** `application/json`
- **Errors:** RFC 7807 `application/problem+json` (see [Errors](#errors))

---

## Authentication 🔐

Auth is **off by default** (`Api:Auth:Require=false`) so local/dev hosts are anonymous-friendly.
Set `Api:Auth:Require=true` to enforce it. Two schemes are supported.

### API key (`X-API-Key`)

Send your key in the `X-API-Key` header. Keys are configured **hashed at rest** (SHA-256, base64):

```jsonc
// appsettings.json
"Api": {
  "Auth": {
    "Require": true,
    "ApiKeys": [
      { "KeyHash": "<base64 sha-256 of the raw key>", "CallerId": "ci-bot", "Roles": [ "Developer" ] }
    ]
  }
}
```

Generate a hash with the built-in helper (`Workflow.Api.Auth.ApiKeyHasher.Hash("my-raw-key")`).

### JWT bearer

Send `Authorization: Bearer <token>`. Configure validation under `Api:Auth:Jwt`:

```jsonc
"Api": { "Auth": { "Jwt": {
  "Authority": "https://login.example.com",   // OIDC issuer (optional)
  "Issuer":   "https://login.example.com",
  "Audience": "dotflow-api",
  "SigningKey": "<symmetric key for self/test-issued tokens>"
} } }
```

### Roles & policies

| Policy             | Roles allowed                     | Applied to                                   |
| ------------------ | --------------------------------- | -------------------------------------------- |
| `WorkflowRead`     | Admin, Developer, Viewer          | GET workflows/executions/modules/variables   |
| `WorkflowWrite`    | Admin, Developer                  | POST/PUT workflows, PUT/DELETE variables     |
| `WorkflowExecute`  | Admin, Developer                  | execute / cancel                             |
| `Admin`            | Admin                             | DELETE workflow                              |

When auth is disabled every policy is a no-op. The authenticated caller id flows into execution
audit (`TriggeredBy`); an unauthenticated request may still set `X-Caller-Id` for dev.

---

## Pagination & filtering 📄

List endpoints accept `?page=` (1-based) and `?pageSize=` (default 50, max 200) and return:

```json
{ "items": [ ... ], "totalCount": 42, "page": 1, "pageSize": 50, "totalPages": 1 }
```

---

## Errors

All failures return RFC 7807 ProblemDetails, e.g.:

```json
{ "type": "about:blank", "title": "Not Found", "status": 404, "detail": "Workflow '…' was not found." }
```

Validation failures use `422 Unprocessable Entity` with a `errors` map.

---

## Workflows 📋 (`/api/v1/workflows`)

| Method & path                         | Description                                   |
| ------------------------------------- | --------------------------------------------- |
| `GET /workflows`                      | List (filter `?name=`, `?tag=`, paginated)    |
| `GET /workflows/{id}`                 | Get a definition                              |
| `POST /workflows`                     | Create (server assigns id; `422` on invalid)  |
| `PUT /workflows/{id}`                 | Update (optimistic version guard → `409`)     |
| `DELETE /workflows/{id}`              | Soft-delete; `?purge=true` hard-deletes       |
| `POST /workflows/{id}/restore`        | Restore a soft-deleted workflow               |

```bash
curl -X POST http://localhost:5000/api/v1/workflows \
  -H 'Content-Type: application/json' -H 'X-API-Key: my-key' \
  -d '{ "name": "hello", "version": "1.0.0", "nodes": [ ... ], "connections": [], "variables": {} }'
```

## Executions ⚡ (`/api/v1/workflows/{id}/execute`, `/api/v1/executions`)

| Method & path                                   | Description                                                |
| ----------------------------------------------- | ---------------------------------------------------------- |
| `POST /workflows/{id}/execute`                  | Start; `202` + `{ executionId }` + `Location`              |
| `POST /workflows/{id}/execute/sync?timeoutSeconds=` | Start & wait; `200` final status, `202` + poll URL on timeout |
| `POST /workflows/execute/{name}?version=`       | Start by name (newest active, optional version pin)        |
| `GET /executions/{executionId}`                 | Status (state/progress/node states/error/outputs)          |
| `POST /executions/{executionId}/cancel`         | Cancel a running execution                                 |
| `GET /executions?workflowId=&status=&from=&to=` | List execution history for a workflow (paginated)          |

The execution lifecycle states are `Pending → Running → (Completed | Failed | Cancelled)`.

## Modules 📦 (`/api/v1/modules`) — read-only

| Method & path                | Description                                            |
| ---------------------------- | ----------------------------------------------------- |
| `GET /modules`               | List summaries; `?category=`, `?q=`, `?groupByCategory=true` |
| `GET /modules/{moduleId}`    | Full details incl. schema (ports/properties)          |

Module upload / enable / disable arrive with the `.wfmod` package format in **Phase 2.8**.

## Variables 🔧 (`/api/v1/variables`)

Scoped by `?scope=global|workflow|execution` (+ `?scopeId=` for workflow/execution).

| Method & path                          | Description                              |
| -------------------------------------- | ---------------------------------------- |
| `GET /variables`                       | All variables in the scope               |
| `GET /variables/{name}?version=`       | One variable (specific version optional) |
| `PUT /variables/{name}`               | Set value (creates a new version)        |
| `DELETE /variables/{name}`            | Hard-delete the variable + history       |
| `GET /variables/{name}/history`       | All versions                             |

A `null` value is stored as a *present null* entry, distinct from a missing variable (`404`).

## Monitoring 📊 (`/api/v1/health`, `/status`, `/metrics`)

| Method & path            | Description                                            |
| ------------------------ | ----------------------------------------------------- |
| `GET /health`            | Component health report (`200` healthy / `503` unhealthy) |
| `GET /health/ready`      | Readiness (persistence)                               |
| `GET /health/live`       | Liveness (actor system)                               |
| `GET /status`            | Provider + health, module count, active executions, uptime, version |
| `GET /metrics`           | Execution counters (Prometheus exporter → Phase 2.7.P2) |

---

## Webhooks 🪝 (`/api/webhooks`) — feature tooling

Webhook **management** intentionally lives at `/api/webhooks` (not `/api/v1/...`): it is feature
tooling for external triggers rather than a versioned resource. The trigger route is
`ANY /webhooks/{webhookId}`; management is `POST/GET/GET{id}/PUT/DELETE /api/webhooks`. Signature
validation happens inside the webhook trigger dispatcher. A `/api/v1/webhooks` alias may be added
later (2.7.P5) if a consumer needs it.

---

## Rate limiting 🚦

Off by default. Enable a fixed-window limiter (keyed by API key / caller id) via config:

```jsonc
"Api": { "RateLimit": { "Enabled": true, "PermitLimit": 100, "WindowSeconds": 60 } }
```

Over-limit requests receive `429 Too Many Requests` with a `Retry-After` header.

---

## OpenAPI 📖

The generated OpenAPI document is served at `/swagger/v1/swagger.json` (Swagger UI at `/swagger`
in Development). Both the `ApiKey` and `Bearer` security schemes are registered so the
**Authorize** button works.
