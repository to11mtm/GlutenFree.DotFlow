# Phase 3.2: SignalR Real-Time Hub (Week 26) 📡

Made with 💖 by Ami-Chan! UwU ✨

[Back to Phase 3](Phase3-AdvancedFeatures.md) | [All Phases](README.md)

---

## Overview

> **Progress (2026-07-19):** Phase 3.2 is **COMPLETE ✅**. All 6 slices (3.2.0–3.2.5) are implemented, tested, and documented — `WorkflowHub` with query-string-token auth + CORS, the `ExecutionEventBridge` (subscribes to the Akka `EventStream`, zero engine changes), typed client contracts, group-based subscriptions (`SubscribeToAll` admin-gated), connection/subscription metrics on `/api/v1/metrics` + `/status`, and reconnection-via-re-subscribe. The legacy `Microsoft.AspNetCore.SignalR` 1.1.0 package was removed (server SignalR ships in the Web SDK); the client library is referenced by tests only. **27 new real-time tests pass** via the `HubConnection` harness; the full suite is green apart from pre-existing parallel-timing flakes. Docs: [`docs/realtime.md`](../docs/realtime.md) (new) + `docs/rest-api.md`.

Phase 3.2 gives clients a **real-time window into workflow execution** — a SignalR hub
(`WorkflowHub`) that streams execution/node lifecycle events to subscribed browsers and
services as they happen, instead of polling `GET /api/v1/executions/{id}`. The engine
**already publishes** every lifecycle transition to the Akka.NET `EventStream`
(`ExecutionStateChanged`, `NodeStateChanged`, `ProgressUpdate`, `WorkflowCompleted`,
`WorkflowFailed`, `NodeExecutionCompleted/Failed`); the genuinely-new work is a **bridge**
that subscribes to those events, maps them to serializable client contracts, and
broadcasts them to the right SignalR groups — plus subscription management, connection
metrics, auth on the hub, and a client test harness. This is a **thin real-time seam over
existing observability**, not a new event system~ 🌷

> **CopilotNote:** Hot paths: `Workflow.Api/RealTime/WorkflowHub.cs` (the hub),
> `Workflow.Api/RealTime/ExecutionEventBridge.cs` (EventStream → `IHubContext` translator,
> hosted as a background service so **`Workflow.Engine` gains no ASP.NET/SignalR
> dependency**), `Workflow.Api/Contracts/RealTime/*` (plain client event records),
> `Workflow.Api/RealTime/IConnectionTracker.cs` (thread-safe connection/subscription
> bookkeeping for metrics + reconnection), and small edits to `Program.cs` (map the hub,
> CORS, query-string-token auth). Tests use `HubConnectionBuilder` from
> `Microsoft.AspNetCore.SignalR.Client` against `WebApplicationFactory<Program>`~ 🌸

> **Reality-check note (July 2026):** The §3.2 checklist in
> [`Phase3-AdvancedFeatures.md`](Phase3-AdvancedFeatures.md#32-signalr-real-time-hub-week-17)
> predates Phase 2's observability work. Since then: (a) the engine **already emits**
> execution + node lifecycle events to the Akka `EventStream`
> (`Workflow.Engine/Messages/WorkflowMessages.cs`) — so "add execution event
> broadcasting" becomes "subscribe + translate", not "instrument the engine"; (b) auth
> policies (`WorkflowRead/Write/Execute`, `Admin`) + JWT/API-key schemes already exist
> (`Workflow.Api/Auth/*`) and the hub reuses them verbatim; (c) a metrics seam
> (`IWorkflowMetrics` + `/api/v1/metrics`) already exists and is extended for connection
> counts; (d) the standalone `Microsoft.AspNetCore.SignalR` **1.1.0** package reference is
> a **legacy ASP.NET Core 2.1-era artifact** — under `Microsoft.NET.Sdk.Web` server-side
> SignalR ships **in the shared framework**, so that reference is removed (see D1). This
> plan reconciles the checklist against the existing engine and supersedes it.

**Timeline:** ~1 week (Week 26 — the checklist's original "Week 17" renumbered to follow
3.1's Weeks 23-25).
**Complexity:** 🟠 Medium — the mechanics are well-bounded (SignalR is mature); the risky
parts are **the EventStream→hub bridge lifecycle** (subscribe/unsubscribe cleanly, no
leaked Akka subscriptions), **auth over WebSockets** (token-from-query-string), and
**LanguageExt→plain-record marshalling** of event payloads.

### Confirmed Design Decisions ✅

| # | Decision |
|---|----------|
| **D1 Use the in-framework SignalR; drop the legacy package** | `Workflow.Api` targets `Microsoft.NET.Sdk.Web`, so **server-side SignalR is part of the ASP.NET Core shared framework** — no package reference needed. The stray `Microsoft.AspNetCore.SignalR` **1.1.0** entry (`Directory.Packages.props` + `Workflow.Api.csproj`) is an ASP.NET Core 2.1-era artifact and is **removed**. The **client** package `Microsoft.AspNetCore.SignalR.Client` is added to **`Workflow.Tests` only** for the HubConnection test harness. |
| **D2 Bridge engine events → hub via a hosted background subscriber (engine stays SignalR-free)** | `Workflow.Engine` has **no** ASP.NET/SignalR dependency and must keep it that way. An `ExecutionEventBridge` (an `IHostedService` in `Workflow.Api`) subscribes to the Akka `EventStream` on startup and forwards translated events to `IHubContext<WorkflowHub, IWorkflowHubClient>`. The engine keeps publishing to `EventStream` exactly as today — **zero engine changes** for the core event flow. |
| **D3 Reuse the existing Akka lifecycle events — do not invent a parallel event system** | The bridge consumes the events the engine **already** publishes: `ExecutionStateChanged`, `NodeStateChanged`, `ProgressUpdate`, `WorkflowCompleted`, `WorkflowFailed`, `NodeExecutionCompleted`, `NodeExecutionFailed` (`Workflow.Engine/Messages/WorkflowMessages.cs`). Semantic client events are **derived** from state transitions (e.g. `Pending→Running` ⇒ `ExecutionStarted`; a terminal `WorkflowCompleted` ⇒ `ExecutionCompleted`). A mapping table lives in the bridge. |
| **D4 Client event contracts are plain, serializable records (camelCase JSON)** | `Workflow.Api/Contracts/RealTime/*` defines DTOs matching the checklist's execution/node event names — `ExecutionStartedEvent`, `ExecutionCompletedEvent`, `ExecutionFailedEvent`, `NodeStartedEvent`, `NodeCompletedEvent`, `NodeFailedEvent`, `ExecutionProgressEvent`. (**`WorkflowUpdatedEvent` is deferred to 3.2.P4** per Q2 — the engine emits no definition-change event today.) LanguageExt types (`HashMap`/`Arr`/`Option`) are flattened to plain `IReadOnlyDictionary`/`IReadOnlyList`/nullable via the existing `JsonValueConverter` conventions. A strongly-typed hub client interface `IWorkflowHubClient` names each method for compile-safe broadcasts. |
| **D5 Hub auth reuses existing policies + query-string token for WebSockets** | `WorkflowHub` is `[Authorize(AuthConstants.WorkflowReadPolicy)]`. Because browsers can't set `Authorization` headers on the WebSocket handshake, JWT bearer is configured to also read the token from `?access_token=` **for the hub path only** (`JwtBearerEvents.OnMessageReceived`, gated on `HttpContext.Request.Path` starting with the hub route). API-key auth continues to work via header for non-browser clients. |
| **D6 Group-based broadcasting; `SubscribeToAll` is admin-only** | Subscriptions map to SignalR **groups**: `workflow:{workflowId}` and `execution:{executionId}`. The bridge broadcasts each event to the relevant group(s) so **only subscribed clients** receive it. `SubscribeToAll()` (firehose) requires `AuthConstants.AdminPolicy`. |
| **D7 SignalR Groups are the broadcast source of truth; a tracker mirrors them for metrics/restore** | A thread-safe `IConnectionTracker` (`ConcurrentDictionary<connectionId, HashSet<subscriptionKey>>`) records who's subscribed to what — used only for **metrics** and **reconnection restore**, never for the broadcast fan-out itself (SignalR groups handle that). Cleaned up in `OnDisconnectedAsync`. |
| **D8 Connection metrics ride the existing metrics seam** | `IWorkflowMetrics` (or a sibling `IRealtimeMetrics`) gains `activeConnections` + `activeSubscriptions` gauges, surfaced through the existing `/api/v1/metrics` + `/api/v1/status` endpoints — no new monitoring surface. |
| **D9 Reconnection = SignalR auto-reconnect + client-driven re-subscribe; missed-event replay deferred** | The client uses SignalR's built-in `WithAutomaticReconnect()` (exponential backoff). On `OnConnectedAsync` the server holds no durable per-user state, so the **client re-invokes its `Subscribe*` calls** after reconnect (documented pattern). Replaying events missed *during* a disconnect requires an event store → **post-MVP (3.2.P2)**. |
| **D10 CORS: a dedicated, config-driven policy with credentials** | SignalR from a browser needs CORS with `AllowCredentials()` + explicit origins (wildcard is illegal with credentials). A named policy `dotflow.realtime` reads allowed origins from `Api:RealTime:AllowedOrigins` (**deny-by-default** — empty ⇒ no cross-origin). Applied to the hub endpoint only. |
| **D11 Single-node/in-memory for MVP; Redis backplane deferred** | Connection/subscription state and the bridge are per-process. Horizontal scale-out (multiple API instances sharing subscriptions) needs a SignalR backplane (Redis) → **post-MVP (3.2.P1)**. The `IHubContext`-based broadcast means adding a backplane later is purely additive. |
| **D12 Chatty-event posture: broadcast all lifecycle events in MVP; coalescing deferred** | Every `NodeStateChanged`/`ProgressUpdate` is forwarded as-is for MVP. Throttling/coalescing high-frequency node events for very large workflows → **post-MVP (3.2.P3)** behind a config flag. |

### TO RESOLVE 🤔 → RESOLVED ✅

> All Q1–Q7 resolved (July 2026) — user answers folded into the design decisions + slices
> below. Mirror of the 3.1 process~ ✅

- [x] **Q1 Hub route: `/hubs/workflow` (checklist) or `/api/v1/hub` (versioned convention)?**
  - **RESOLVED (OK):** `/hubs/workflow` — SignalR hubs live outside the versioned REST tree; the query-string-token auth exemption scopes to this stable path. Event-contract versioning rides the DTO records, not the URL.
- [x] **Q2 `WorkflowUpdated` event: in scope for MVP?** The engine emits **execution** events but nothing on workflow-definition **CRUD** today.
  - **RESOLVED (defer):** Deferred to **3.2.P4** — MVP stays focused on execution/node streaming, no touching the workflow write path. `WorkflowUpdatedEvent` is **removed from MVP contracts** (D4).
- [x] **Q3 Permission model for subscriptions.** There is **no per-workflow ownership/ACL** in the system yet — authz is role/policy-based.
  - **RESOLVED (OK):** Policy-only for MVP — `WorkflowReadPolicy` to subscribe to any workflow/execution; `AdminPolicy` for `SubscribeToAll`. Resource-level checks land with an ownership model → **3.2.P5**.
- [x] **Q4 Missed-event replay on reconnect.**
  - **RESOLVED (defer):** Deferred — **explicitly documented as a post-MVP feature (3.2.P2)** in both this plan and `docs/realtime.md`. MVP restores subscriptions via client re-invoke (D9); live events resume immediately, the disconnect-window gap is accepted and called out in the docs.
- [x] **Q5 Multi-node backplane (Redis).**
  - **RESOLVED (defer):** Deferred to **3.2.P1** — and we want to **explore backplane options** (Redis vs Azure SignalR Service vs other) as part of that slice rather than committing to Redis now. MVP is single-instance; the `IHubContext` design keeps the backplane additive.
- [x] **Q6 Event payload richness — include full outputs/variables, or references only?** Large outputs over a broadcast channel can be heavy.
  - **RESOLVED (agreed):** **Summary** fields inline (ids, timings, status, error, progress); large output blobs **omitted** — clients fetch full outputs via `GET /api/v1/executions/{id}`. Configurable cap on inlined payload size.
- [x] **Q7 Node-event volume.** Should MVP throttle node/progress events?
  - **RESOLVED (agreed):** No throttling in MVP (D12); revisit under **3.2.P3** if profiling shows it's needed.

---

## Pre-Existing Work (from earlier phases) ✅

| Component | File | Status |
|-----------|------|--------|
| Execution + node lifecycle events published to Akka `EventStream` | `Workflow.Engine/Messages/WorkflowMessages.cs` (`ExecutionStateChanged`, `NodeStateChanged`, `ProgressUpdate`, `WorkflowCompleted`, `WorkflowFailed`, `NodeExecutionCompleted/Failed`) | ✅ The bridge subscribes to these (D2/D3) — **no engine changes** |
| Event publishing site | `Workflow.Engine/Actors/WorkflowExecutor.cs` (`Context.System.EventStream.Publish(...)`) | ✅ Source of the real-time feed |
| `ActorSystem` singleton in DI | `Workflow.Api/Program.cs` (`ActorSystem.Create("dotflow")`) | ✅ Bridge resolves it to `EventStream.Subscribe<T>` |
| Execution status query + DTO | `Workflow.Api/Execution/IWorkflowExecutionService.cs` (`GetStatusAsync`, `ExecutionStatusResult`) | ✅ Reused for on-connect snapshot + full-output fetch (Q6) |
| Execution state model | `Workflow.Core/Models/ExecutionState.cs` (`Pending/Running/Completed/Failed/Cancelled/Paused/Resuming`) | ✅ Drives the state→semantic-event mapping (D3) |
| Auth policies + schemes | `Workflow.Api/Auth/AuthConfiguration.cs` (`WorkflowRead/Write/Execute`, `Admin`), `ConfigureJwtBearerOptions.cs`, `AuthServiceCollectionExtensions.cs` | ✅ Hub `[Authorize]` + query-string token (D5) |
| Metrics seam + endpoints | `Workflow.Api/Observability/IWorkflowMetrics.cs`, `Workflow.Api/V1/MonitoringEndpoints.cs` (`/metrics`, `/status`) | ✅ Extended with connection/subscription gauges (D8) |
| Minimal-API + endpoint-mapping conventions | `Workflow.Api/V1/ApiVersioning.cs` (`MapV1Group`), `Workflow.Api/Program.cs` map block | ✅ `MapHub<WorkflowHub>` slots into the same wiring |
| `WebApplicationFactory<Program>` integration-test pattern | `Workflow.Tests/Api/V1/ApiE2ETests.cs`, `ApiFoundationTests.cs` | ✅ Base for the SignalR client harness |
| `JsonValueConverter` (iterative CLR↔JSON) | `Workflow.Modules/Internal/JsonValueConverter.cs` | ✅ Flattens LanguageExt payloads to plain contracts (D4) |
| Example hub skeleton (reference only) | `examples/api/WorkflowHub.cs` | ✅ Group-naming + event-name reference |
| Webhook dispatcher (parallel push channel) | `Workflow.Api/Webhooks/WebhookDispatcher.cs` | ⚠️ Not reused directly; the bridge is the SignalR-specific analog. A shared "execution-event fan-out" refactor is possible but **out of scope** (noted for later). |

> **CopilotNote:** The insight mirroring 3.1: **the events already exist**. `WorkflowExecutor`
> publishes to the Akka `EventStream` on every transition, so Phase 3.2 is *subscribe →
> translate → group-broadcast*. Budget risk on **bridge lifecycle** (clean subscribe/dispose,
> no actor leaks) and **WebSocket auth** — not on wiring SignalR itself~ 💖

---

## 3.2.0 Hub Foundation + Auth + Wiring ✅ DONE (`Workflow.Api/RealTime/*`)

> **Purpose:** A `WorkflowHub` clients can connect to, authenticated, with clean
> connection lifecycle, CORS, and a client test harness — the skeleton everything else
> hangs off.

**Complexity:** 🟡 Medium *(WebSocket auth is the fiddly bit)*

### Tasks

- [x] **Remove the legacy SignalR package (D1)** — delete `Microsoft.AspNetCore.SignalR` 1.1.0 from `Directory.Packages.props` + `Workflow.Api.csproj`; confirm the hub compiles under the Web SDK's shared framework. Add `Microsoft.AspNetCore.SignalR.Client` to `Directory.Packages.props` + `Workflow.Tests.csproj` (test-only).
- [x] **`RealTime/IWorkflowHubClient.cs`** — strongly-typed client interface (one method per event, `Task`-returning) for compile-safe `IHubContext<WorkflowHub, IWorkflowHubClient>` broadcasts.
- [x] **`RealTime/WorkflowHub.cs`** — `Hub<IWorkflowHubClient>`, `[Authorize(WorkflowReadPolicy)]`; `OnConnectedAsync` (register in tracker, log), `OnDisconnectedAsync` (clean up tracker + groups).
- [x] **Query-string-token auth (D5)** — extend JWT bearer `OnMessageReceived` to read `?access_token=` when `Request.Path` starts with the hub route; leave header/API-key paths untouched.
- [x] **CORS policy (D10)** — named `dotflow.realtime` policy from `Api:RealTime:AllowedOrigins` (deny-by-default), `AllowCredentials()`, applied to the hub endpoint only.
- [x] **Program.cs wiring** — `AddSignalR()`, register `IConnectionTracker`, `MapHub<WorkflowHub>("/hubs/workflow")` (Q1) with the CORS policy; add `AddRealTime()` extension to keep `Program.cs` tidy.
- [x] **Test harness** — a `SignalRTestHarness` building a `HubConnection` against `WebApplicationFactory<Program>`'s `TestServer` (`options.HttpMessageHandlerFactory`), with a helper to issue a valid token.

### Tests (target ~6): → `Workflow.Tests/Api/RealTime/WorkflowHubConnectionTests.cs`
- [x] `Client_CanConnect_WithValidToken` · `Connect_WithoutToken_Rejected` · `Connect_WithInvalidToken_Rejected`
- [x] `Connect_ViaQueryStringToken_Succeeds` · `Disconnect_CleansUpTracker`
- [x] `Cors_DisallowedOrigin_Blocked` *(or documented as manual if TestServer bypasses CORS)*

---

## 3.2.1 Execution Event Bridge + Contracts ✅ DONE (`Workflow.Api/RealTime/ExecutionEventBridge.cs`)

> **Purpose:** Translate Akka `EventStream` lifecycle events into client event DTOs and
> broadcast them to the right groups. The heart of the phase.

**Complexity:** 🟠 Medium-High *(lifecycle + marshalling)*

### Tasks

- [x] **`Contracts/RealTime/*` (D4)** — records: `ExecutionStartedEvent`, `ExecutionCompletedEvent`, `ExecutionFailedEvent`, `NodeStartedEvent`, `NodeCompletedEvent`, `NodeFailedEvent`, `ExecutionProgressEvent`. camelCase; summary payloads (Q6). (`WorkflowUpdatedEvent` deferred to 3.2.P4 per Q2.)
- [x] **`RealTime/ExecutionEventBridge.cs` (`IHostedService`, D2)** — on `StartAsync`, resolve `ActorSystem` + subscribe a bridging actor/handler to `ExecutionStateChanged`, `NodeStateChanged`, `ProgressUpdate`, `WorkflowCompleted`, `WorkflowFailed`, `NodeExecutionCompleted/Failed`; on `StopAsync`, unsubscribe + dispose (no leaked subscriptions).
- [x] **State→semantic-event mapping (D3)** — `Pending→Running` ⇒ `ExecutionStarted`; `WorkflowCompleted` ⇒ `ExecutionCompleted`; `WorkflowFailed` ⇒ `ExecutionFailed`; node `→Running` ⇒ `NodeStarted`; `NodeExecutionCompleted` ⇒ `NodeCompleted`; `NodeExecutionFailed` ⇒ `NodeFailed`; `ProgressUpdate` ⇒ `ExecutionProgress`.
- [x] **Payload marshalling** — flatten `HashMap`/`Arr`/`Option`/`Exception` to plain contract fields via `JsonValueConverter` conventions; cap inlined output size (Q6).
- [x] **Group broadcast (D6)** — each event goes to `execution:{executionId}` **and** `workflow:{workflowId}` groups via `IHubContext<WorkflowHub, IWorkflowHubClient>`.
- [x] **Resilience** — a slow/faulted client must not break the bridge; broadcast failures are logged, not thrown; the subscription survives individual send errors.

### Tests (target ~9): → `Workflow.Tests/Api/RealTime/ExecutionEventBridgeTests.cs`
- [x] `ExecutionStarted_Broadcast` · `ExecutionCompleted_Broadcast` · `ExecutionFailed_Broadcast`
- [x] `NodeStarted_Broadcast` · `NodeCompleted_Broadcast` · `NodeFailed_Broadcast`
- [x] `ExecutionProgress_Broadcast` · `Payload_LanguageExtTypes_FlattenToPlainJson`
- [x] `Bridge_Unsubscribes_OnStop` *(no leaked EventStream subscription)*

---

## 3.2.2 Subscription Management ✅ DONE (`Workflow.Api/RealTime/*`)

> **Purpose:** Clients subscribe/unsubscribe to specific workflows/executions (or all, if
> admin); only subscribed clients receive events; state is tracked thread-safely.

**Complexity:** 🟡 Medium

### Tasks

- [x] **Hub methods** — `SubscribeToWorkflow(Guid)`, `UnsubscribeFromWorkflow(Guid)`, `SubscribeToExecution(Guid)`, `UnsubscribeFromExecution(Guid)`, `SubscribeToAll()` (**AdminPolicy**, D6). Each manages the matching SignalR group + the tracker entry.
- [x] **`RealTime/IConnectionTracker.cs` + `ConnectionTracker.cs` (D7)** — thread-safe `ConcurrentDictionary<string, HashSet<string>>` (connectionId → subscription keys); add/remove/list; snapshot counts for metrics.
- [x] **Permission checks (Q3)** — policy-based gate; `SubscribeToAll` requires admin; return a structured hub error on denial (no silent no-op).
- [x] **On-connect snapshot (optional/nice-to-have)** — when subscribing to an execution, immediately push the current `ExecutionStatusResult` so late subscribers aren't blank until the next event.

### Tests (target ~8): → `Workflow.Tests/Api/RealTime/SubscriptionTests.cs`
- [x] `Subscribe_ToExecution_ReceivesOnlyThatExecutionsEvents` · `Subscribe_ToWorkflow_ReceivesItsExecutions`
- [x] `Unsubscribe_StopsEvents` · `NonSubscriber_ReceivesNothing`
- [x] `SubscribeToAll_AsAdmin_ReceivesEverything` · `SubscribeToAll_AsNonAdmin_Denied`
- [x] `Tracker_AddRemove_Threadsafe` *(concurrent sub/unsub)* · `Subscribe_PushesInitialSnapshot`

---

## 3.2.3 Connection State + Metrics ✅ DONE (`Workflow.Api/Observability/*`)

> **Purpose:** Observe the real-time layer — active connections and subscriptions surfaced
> through the existing monitoring endpoints.

**Complexity:** 🟢 Low-Medium

### Tasks

- [x] **Metrics extension (D8)** — add `activeConnections` + `activeSubscriptions` gauges to `IWorkflowMetrics` (or a sibling `IRealtimeMetrics`) fed from `IConnectionTracker`; increment/decrement in hub connect/disconnect + sub/unsub.
- [x] **Surface via endpoints** — include the gauges in `GET /api/v1/metrics` + `GET /api/v1/status`.
- [x] **Multiple connections per user** — confirm the tracker keys by connectionId (not user), so a user with several tabs is handled correctly.
- [x] **Heartbeat** — rely on SignalR's built-in keep-alive/ping (document the configured intervals); no custom ping needed unless a gap is found.

### Tests (target ~5): → `Workflow.Tests/Api/RealTime/ConnectionMetricsTests.cs`
- [x] `ActiveConnections_IncrementsOnConnect_DecrementsOnDisconnect`
- [x] `ActiveSubscriptions_TracksSubUnsub` · `MultipleConnections_PerUser_CountedIndependently`
- [x] `Metrics_Endpoint_IncludesRealtimeGauges` · `Status_Endpoint_IncludesConnectionCount`

---

## 3.2.4 Reconnection + Resilience ✅ DONE (client guidance + server restore)

> **Purpose:** Survive transient network drops gracefully; document the reconnect contract.

**Complexity:** 🟡 Medium

### Tasks

- [x] **Server-side re-subscribe support (D9)** — `OnConnectedAsync` is idempotent; the client re-invokes its `Subscribe*` calls after a reconnect (server holds no durable per-connection subscription across a *new* connectionId).
- [x] **Auth on reconnect** — the reconnect handshake re-validates the token (expired token ⇒ reconnect fails cleanly with an auth error).
- [x] **Graceful degradation** — bridge broadcast failures to a dropped client are swallowed; the server never crashes on client disconnect mid-send.
- [x] **Missed-event note (Q4)** — document that events during the disconnect window are **not** replayed in MVP (post-MVP 3.2.P2); live events resume on reconnect.
- [x] **Client guidance** — `WithAutomaticReconnect()` + re-subscribe snippet in the docs.

### Tests (target ~4): → `Workflow.Tests/Api/RealTime/ReconnectionTests.cs`
- [x] `Reconnect_AfterDrop_ReceivesLiveEventsAgain` · `Reconnect_RestoresSubscriptions_ViaClientReinvoke`
- [x] `Reconnect_WithExpiredToken_FailsCleanly` · `ServerSend_ToDroppedClient_DoesNotThrow`

---

## 3.2.5 Docs + Example Client ✅ DONE

> **Purpose:** Make the hub usable — event catalog, auth, subscribe/reconnect patterns, a
> runnable client snippet.

**Complexity:** 🟢 Low

### Tasks

- [x] **`docs/realtime.md`** — hub URL, auth (header + query-string token), the full event catalog with payload shapes, subscribe/unsubscribe methods, admin firehose, reconnect + re-subscribe pattern, CORS config keys, metrics.
- [x] **REST/docs cross-links** — add a "Real-time" section pointer to `docs/rest-api.md`; link from the README doc index.
- [x] **Example client** — a small JS/TS (or `HubConnection` C#) snippet in `docs/realtime.md` (or refresh `examples/api/WorkflowHub.cs`) showing connect → subscribe → handle events → reconnect.
- [x] **Config reference** — document `Api:RealTime:AllowedOrigins` and any keep-alive settings.

### Tests
- [x] *(docs slice — no automated tests; verified by the 3.2.0–3.2.4 suites)*

---

## Proposed File Layout 🗂️

```
Workflow.Api/
  RealTime/
    WorkflowHub.cs                 ← 3.2.0 (hub + connection lifecycle)
    IWorkflowHubClient.cs          ← 3.2.0 (typed client interface)
    IConnectionTracker.cs          ← 3.2.2
    ConnectionTracker.cs           ← 3.2.2
    ExecutionEventBridge.cs        ← 3.2.1 (EventStream → IHubContext, IHostedService)
    RealTimeServiceCollectionExtensions.cs  ← AddRealTime()
  Contracts/RealTime/
    RealTimeEvents.cs              ← 3.2.1 (ExecutionStarted/Completed/Failed, Node*, Progress)
  Observability/
    IWorkflowMetrics.cs            ← extended (3.2.3, connection/subscription gauges)
  Program.cs                       ← MapHub + CORS + query-string-token auth (3.2.0)

Directory.Packages.props           ← remove legacy SignalR 1.1.0; add SignalR.Client (test) (3.2.0)

Workflow.Tests/
  Api/RealTime/
    SignalRTestHarness.cs          ← 3.2.0 (HubConnection over TestServer)
    WorkflowHubConnectionTests.cs  ← 3.2.0
    ExecutionEventBridgeTests.cs   ← 3.2.1
    SubscriptionTests.cs           ← 3.2.2
    ConnectionMetricsTests.cs      ← 3.2.3
    ReconnectionTests.cs           ← 3.2.4

docs/realtime.md                   ← new (3.2.5 — hub + event catalog + auth + reconnect)
```

> **Note:** `Workflow.Engine` gains **no** files and **no** new dependency — the bridge
> lives entirely in `Workflow.Api` and consumes the existing `EventStream` (D2).

---

## Post-MVP Slices 🚧 *(deferred — not blocking 3.3+)*

### 3.2.P1 Backplane / horizontal scale-out 🌐 *(D11/Q5)*
Add a SignalR backplane so multiple API instances share groups + broadcasts. **Explore
options as part of this slice** — Redis backplane, Azure SignalR Service, or an
event-broker-based approach — and pick per the deployment target rather than committing to
Redis up front. Purely additive given the `IHubContext` design; needs the chosen
dependency + a deployment story.

### 3.2.P2 Missed-event replay on reconnect ⏪ *(D9/Q4)*
Persist a bounded per-execution event log; on reconnect, replay events since the client's
last-seen sequence number. Requires an event store + client cursor protocol.

### 3.2.P3 Event coalescing / throttling 🚦 *(D12/Q7)*
Batch or debounce high-frequency `NodeStateChanged`/`ProgressUpdate` events for very large
workflows, behind a config flag, to protect chatty clients.

### 3.2.P4 `WorkflowUpdated` definition-change events ✏️ *(Q2)*
Emit `WorkflowUpdatedEvent` from the workflow write endpoints via an
`IWorkflowChangeNotifier` seam so designers see live edits from collaborators.

### 3.2.P5 Resource-level subscription authorization 🔐 *(Q3)*
Once a workflow ownership/ACL model exists, gate `SubscribeToWorkflow/Execution` on
per-resource access (not just role), rejecting subscriptions to workflows the user can't see.

---

## Success Criteria ✅

- [x] A client connects to `WorkflowHub` with a valid token (header **or** query-string) and is rejected without one
- [x] Executing a workflow streams `ExecutionStarted/Progress/Completed` (or `Failed`) and `NodeStarted/Completed/Failed` events to subscribed clients in real time — no polling
- [x] Subscriptions are honored: a client subscribed to execution A never receives execution B's events; `SubscribeToAll` is admin-only
- [x] Event payloads are plain, camelCase JSON (no LanguageExt leakage) with summary fields; large outputs are fetched via REST (Q6)
- [x] Active connection + subscription counts appear in `/api/v1/metrics` and `/api/v1/status`
- [x] The bridge subscribes/unsubscribes to the Akka `EventStream` cleanly (no leaked subscriptions) and never crashes on a dropped client
- [x] Automatic reconnect works; the client re-subscribes and resumes receiving live events (missed-window replay explicitly out of MVP scope)
- [x] **`Workflow.Engine` is unchanged and gains no ASP.NET/SignalR dependency**
- [x] `docs/realtime.md` documents the hub, event catalog, auth, and reconnect pattern
- [x] All existing tests stay green; new real-time tests pass via the `HubConnection` harness

---

*Made with 💖 by Ami-Chan! Real-time is just events with good manners~ UwU* ✨
