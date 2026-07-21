# Real-Time Hub 📡

> Phase 3.2 — stream workflow execution events to clients in real time over SignalR,
> instead of polling. Made with 💖 by Ami-Chan~ ✨

> 📡 The **[Execution Monitor](execution-monitor.md)** (`/monitor`) is the main consumer of this
> hub — a live dashboard + per-execution detail built on the firehose described here.

The `WorkflowHub` pushes execution and node lifecycle events to subscribed clients as they
happen. It's a thin real-time layer over the engine's existing observability: the engine
publishes lifecycle events to the Akka `EventStream`, and a hosted `ExecutionEventBridge`
translates and broadcasts them to SignalR groups — so **`Workflow.Engine` has no SignalR
dependency**.

- [Connecting](#connecting)
- [Authentication](#authentication)
- [Subscriptions](#subscriptions)
- [Event catalog](#event-catalog)
- [Reconnection](#reconnection)
- [CORS](#cors)
- [Metrics](#metrics)
- [Scaling (post-MVP)](#scaling-post-mvp)

---

## Connecting

- **Hub URL:** `/hubs/workflow`
- **Transport:** WebSockets (falls back to Server-Sent Events / Long Polling).

```javascript
import { HubConnectionBuilder } from "@microsoft/signalr";

const connection = new HubConnectionBuilder()
  .withUrl("/hubs/workflow", { accessTokenFactory: () => myJwt })
  .withAutomaticReconnect()
  .build();

connection.on("ExecutionStarted", e => console.log("started", e.executionId));
connection.on("ExecutionCompleted", e => console.log("done", e.durationMs));
connection.on("NodeStarted", e => console.log("node", e.nodeId));

await connection.start();
await connection.invoke("SubscribeToExecution", executionId);
```

---

## Authentication

The hub is protected by the same policies as the REST API and requires the
**`WorkflowRead`** policy (roles `Admin`, `Developer`, or `Viewer`). When
`Api:Auth:Require` is `false` (dev default) the hub is anonymous-friendly.

- **Non-browser clients** send `Authorization: Bearer <jwt>` (or the `X-API-Key` header).
- **Browser clients** can't set headers on the WebSocket handshake, so the JWT is read from
  the **`access_token` query string** — but only for the `/hubs/*` path. The SignalR JS
  client's `accessTokenFactory` handles this automatically.

The admin firehose (`SubscribeToAll`) additionally requires the **`Admin`** policy.

---

## Subscriptions

Events are only delivered to clients subscribed to the relevant group. Hub methods:

| Method | Argument | Effect |
|--------|----------|--------|
| `SubscribeToExecution` | `Guid executionId` | Receive one execution's events (+ an immediate snapshot) |
| `UnsubscribeFromExecution` | `Guid executionId` | Stop receiving them |
| `SubscribeToWorkflow` | `Guid workflowId` | Receive events for **all** executions of a workflow |
| `UnsubscribeFromWorkflow` | `Guid workflowId` | Stop receiving them |
| `SubscribeToAll` | *(none)* | Firehose — **admin only** |
| `UnsubscribeFromAll` | *(none)* | Leave the firehose |

> **Note:** Workflow-level delivery resolves the execution's workflow id from execution
> history; if no persistence provider is configured, `workflow:*` grouping is best-effort
> while `execution:*` always works.

Immediately after `SubscribeToExecution`, the server pushes an **`ExecutionSnapshot`** with
the current state, so late subscribers aren't blank until the next live event.

---

## Event catalog

All payloads are plain camelCase JSON. They are **summaries** — large outputs/variables are
omitted; fetch them via `GET /api/v1/executions/{id}` when needed.

| Client method | Payload fields |
|---------------|----------------|
| `ExecutionStarted` | `executionId`, `workflowId?`, `timestamp` |
| `ExecutionCompleted` | `executionId`, `workflowId?`, `durationMs`, `timestamp` |
| `ExecutionFailed` | `executionId`, `workflowId?`, `error`, `durationMs`, `timestamp` |
| `NodeStarted` | `executionId`, `nodeId`, `timestamp` |
| `NodeCompleted` | `executionId`, `nodeId`, `durationMs`, `timestamp` |
| `NodeFailed` | `executionId`, `nodeId`, `error`, `durationMs`, `timestamp` |
| `ExecutionProgress` | `executionId`, `percentage`, `currentNode?`, `completedNodes`, `totalNodes`, `timestamp` |
| `ExecutionSnapshot` | `executionId`, `state`, `progress`, `nodeStates`, `endTime?`, `error?` |

---

## Reconnection

Use the client's built-in `withAutomaticReconnect()` (exponential backoff). The server
holds **no durable per-connection state**, so after a reconnect the client must
**re-invoke its `Subscribe*` calls** (the reconnect gets a new connection id):

```javascript
connection.onreconnected(() => connection.invoke("SubscribeToExecution", executionId));
```

An expired token causes the (re)connect to fail cleanly with an auth error.

> **⏪ Missed-event replay (post-MVP — 3.2.P2):** events that occur **during** a disconnect
> window are **not** replayed in the MVP. Live events resume immediately on reconnect; if
> you need the exact state you missed, call `GET /api/v1/executions/{id}`. Durable replay
> from a bounded event log with a client cursor is planned as a post-MVP feature.

---

## CORS

Browser clients on another origin need CORS. The hub uses a dedicated, **deny-by-default**
policy: configure allowed origins under `Api:RealTime:AllowedOrigins`. Credentials are
allowed (required for SignalR auth), so a wildcard origin is not permitted.

```jsonc
{
  "Api": {
    "RealTime": {
      "AllowedOrigins": [ "https://app.example.com" ]
    }
  }
}
```

With no configured origins, cross-origin access is denied (same-origin still works).

---

## Metrics

Active real-time connections and subscriptions are surfaced through the existing
monitoring endpoints:

- `GET /api/v1/metrics` → `realtime_connections_active`, `realtime_subscriptions_active`
- `GET /api/v1/status` → `activeConnections`, `activeSubscriptions`

---

## Scaling (post-MVP)

The MVP is single-instance. Sharing groups/broadcasts across multiple API instances needs
a SignalR backplane — **3.2.P1** will evaluate options (Redis backplane, Azure SignalR
Service, or an event-broker approach) and choose per deployment target. The `IHubContext`
design keeps a backplane purely additive.

> **Reference client:** the [visual workflow designer](designer.md) consumes this hub for its live execution overlay (Phase 3.3.c).
