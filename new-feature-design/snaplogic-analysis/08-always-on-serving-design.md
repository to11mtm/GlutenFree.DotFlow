# 08 — Design: Always-On / Low-Latency Serving ("Ultra-style") ⚡

> Closes the "Ultra-style always-on serving" gap from [`02`](02-snaplogic-comparison.md) §6/§7.
> SnapLogic parity targets: **Ultra low-latency tasks** (FeedMaster queue + warm instances)
> and **headless Ultra** (continuous listeners). Design-level.

## 1. What "cold" costs us today, and what actually needs building

Per the engine exploration: a webhook request already flows
`WebhookEndpoints → WebhookDispatcher → ActorWorkflowLauncher → supervisor Ask → WorkflowExecutor`
in-process — actor spawn is cheap (no container/JVM start like SnapLogic avoids with Ultra).
The real gaps are different from SnapLogic's:

| Concern | SnapLogic answer | DotFlow gap |
| --- | --- | --- |
| Ingest buffering, burst absorption, at-least-once | FeedMaster queue | none — webhook is invoke-only |
| Continuous consumption of external streams | headless Ultra w/ listener Snaps | `TriggerType.Event` is **declared but unimplemented** |
| Scheduled execution | Scheduled Tasks | `TriggerType.Scheduled` is **declared but unimplemented** |
| Instance supervision/autoscale | Ultra monitor | n/a in-process; needed only for multi-node later |
| Request/response over a queue | FeedMaster correlation | none |

So this design = **trigger runtime** (scheduled + event/queue) + **queue-fed execution feed**,
not literal "warm pipeline instances".

## 2. Architecture

### 2.1 Trigger runtime host (new, `Workflow.Engine` or `Workflow.Api` hosted service)

A `TriggerRuntimeService` (IHostedService) that, on startup and on workflow save/delete,
reconciles active triggers:

- **Scheduled:** cron evaluation (Cronos), per-workflow schedule actor or a single timer
  wheel; misfire policy (`skip` | `runOnce`) in `TriggerDefinition.Configuration`; overlap
  policy (`allow` | `skip` | `queue`) — SnapLogic Scheduled Task parity.
- **Event:** pluggable `ITriggerListener` providers registered per source type. First
  provider: **NATS** (JetStream is already a dependency of `Workflow.Persistence.Nats`) —
  subject subscription → each message becomes a run (`{{input}}` = message payload).
  Contract mirrors headless Ultra: always-on consumer, restart-on-fault with backoff,
  concurrency cap per trigger.

### 2.2 Execution feed queue (FeedMaster analog) — optional layer

For webhook workloads needing burst absorption / at-least-once:

- Webhook registration gains `Mode: Direct (default) | Queued`.
- `Queued`: the HTTP handler validates (auth/signature — existing Phase 2.3.7 machinery),
  enqueues to a JetStream work queue stream (`WF_FEED_<webhookId>`), replies `202` +
  `requestId`. A consumer group (the Event trigger runtime, same machinery as 2.1) drains it
  with configurable max in-flight executions.
- **Request/response ("low-latency Ultra") variant:** `QueuedSync` mode — handler enqueues,
  awaits a completion signal (JetStream reply subject or in-proc correlation when the
  consumer runs in the same host) up to `timeoutMs`, returns the execution's terminal outputs.
  This is FeedMaster's one-in-one-out contract.
- Redelivery: JetStream ack on terminal state; `maxDeliver` → dead-letter subject. Executions
  must be idempotent or workflows use high-water variables (documented, as SnapLogic does).

### 2.3 Warm capacity (deliberately minimal)

No pre-spawned executor pools in v1 — measure first. If spawn+prepare latency ever matters,
the narrow fix is caching prepared/validated `WorkflowDefinition` + bound module schemas per
workflow id (a "prepared workflow" cache), not warm actors.

## 3. Surfaces

- `TriggerDefinition.Configuration` keys documented per type (cron, subject, mode, caps) —
  no model change needed (`HashMap<string,string>` already there).
- API: `GET /api/v1/triggers` (active trigger states: running/paused/faulted, last fire,
  next fire), `POST .../pause|resume`. SignalR events for trigger lifecycle.
- Monitor UI: trigger health panel; queued-webhook depth gauge (JetStream consumer info).

## 4. Key decisions

1. Build on **NATS JetStream** rather than an internal queue — already in the stack, gives
   persistence/redelivery/consumer groups for free. In-memory fallback (`Direct` semantics)
   when NATS isn't configured.
2. Scheduled + Event triggers are the *same runtime*; queued webhooks are Event triggers with
   an HTTP producer. One implementation, three features.
3. Multi-node scale-out stays out of scope; but consumer groups mean a second API instance
   pointed at the same NATS naturally shares the feed — cheap horizontal path later.

## 5. Open questions

- [ ] Cron library and timezone handling (org-level default TZ?).
- [ ] `QueuedSync` in v1, or ship async `202` first? (Recommend async first.)
- [ ] Backfill semantics for scheduled triggers after downtime (run missed? cap?).
- [ ] Should Event trigger providers be pluggable via `.wfmod` like modules (Kafka, SQS…)?
- [ ] Per-trigger rate limiting / max concurrent executions defaults.

## 6. Phasing

1. Trigger runtime host + **Scheduled** (highest general value, zero new deps).
2. **Event/NATS** listener provider.
3. **Queued webhooks** (async), dead-letter, monitor surfaces.
4. `QueuedSync` request/response mode if demanded.
