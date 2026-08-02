# Phase 4.2: Observability & Monitoring (Week 24) 📊

Made with 💖 by Ami-Chan! UwU ✨

[Back to Phase 4](Phase4-Production.md) | [All Phases](README.md)

---

## Overview

Phase 4.2 turns DotFlow from "it has some counters and a health endpoint" into a system you can
**operate**: structured logs you can search, traces that follow a request across the actor
boundary, metrics a scrape can consume, and alerts that fire before a user notices.

> **Reality-check note (August 2026).** The §4.2 checklist in
> [`Phase4-Production.md`](Phase4-Production.md#42-observability--monitoring-week-24) reads as
> greenfield, but a surprising amount already exists — and one item is a trap. Verified against the
> code:
>
> | Claim in §4.2 | Reality |
> | --- | --- |
> | "Implement structured logging (Serilog)" | **Serilog is already a `PackageReference`** in `Workflow.Api` and `Workflow.Engine` (4.1.0 + Extensions.Hosting + Sinks.Console, with Sinks.File and Formatting.Compact pinned in `Directory.Packages.props`) — and has **0 usages**. Nothing calls `UseSerilog`. The dependency is paid for but unwired. |
> | "Implement Prometheus metrics" | `IWorkflowMetrics` + `InMemoryWorkflowMetrics` already exist from Phase 2.7.5 with five counters, and the interface's own comment says *"A Prometheus text exporter can be layered over this later"*. This is a seam to extend, not a thing to build. |
> | "Add health check endpoints" | `/api/v1/health`, `/health/ready`, `/health/live`, `/status`, `/metrics` already exist (`MonitoringEndpoints.cs`), backed by `PersistenceHealthCheck` and `ActorSystemHealthCheck`. |
> | "Correlation IDs" | **Nothing.** Zero matches for `CorrelationId`, `X-Correlation`, or `Activity.Current` across the API. `CallerIdentity` exists and already flows to execution audit (`TriggeredBy`) — that's the hook to build on. |
> | "Add OpenTelemetry tracing" | No OTel packages anywhere. And see **D3** — the naive approach silently produces broken traces. |

**Timeline:** 1 week (Week 24) — 4.2.0–4.2.2 (logging, correlation, metrics) days 1–3 ·
4.2.3–4.2.5 (tracing, health, dashboards/alerts) days 3–5
**Complexity:** 🟠 Medium-High — the individual integrations are routine; the risk is concentrated in
**trace context crossing the Akka message boundary** (D3) and **metric cardinality** (D5), both of
which are cheap to get wrong and expensive to undo.

> **CopilotNote:** Hot paths: `Workflow.Api/Program.cs` (host wiring), a new
> `Workflow.Api/Observability/` expansion (`Telemetry.cs`, `WorkflowMeter.cs`,
> `CorrelationMiddleware.cs`), `Workflow.Engine/Actors/WorkflowExecutor.cs` +
> `NodeExecutor.cs` (span + metric emission, trace-context propagation in messages),
> `Workflow.Engine/Messages/WorkflowMessages.cs` (carrying trace context), and
> `Workflow.Api/V1/MonitoringEndpoints.cs`. Tests follow the established xUnit + FluentAssertions +
> `WebApplicationFactory<Program>` patterns; metric/trace assertions use `MeterListener` and
> `ActivityListener` rather than scraping text~ 🌸

---

## Confirmed Design Decisions ✅

| # | Decision |
|---|----------|
| **D1 Wire the Serilog that's already paid for** | Replace the default host logger with Serilog via `UseSerilog`, with Console (compact JSON in production, human-readable in development) + File sinks. No new package selection needed — everything is already pinned in `Directory.Packages.props`. The aggregation sink (Seq vs Elasticsearch) is **Q3**. |
| **D2 One logging pipeline, not two** | The engine logs through **Akka's** `_log` adapter (163 call sites); the API and modules log through `ILogger<T>` (44 injections). These currently go to different places. Route Akka's logging into `Microsoft.Extensions.Logging` (`Akka.Logger.Serilog` or the MEL adapter) so a single Serilog pipeline sees both, and one correlation id appears on every line regardless of origin. |
| **D3 Trace context travels *in the message*, never in `AsyncLocal`** | ⚠️ **The load-bearing decision.** `Activity.Current` flows on `AsyncLocal`, which does **not** survive `actorRef.Tell(...)` — the send returns immediately and the receive runs on a different thread from the actor's mailbox. Naive OTel instrumentation therefore produces traces that stop dead at `WorkflowExecutor` and never reach `NodeExecutor`, while *looking* like it works in a single-node smoke test. Trace context is carried explicitly on the message envelope (the same way `ExecutionStartOptions.CallerId` already crosses that boundary), and each actor re-establishes an `Activity` from the incoming context. This also survives Phase 4.4 clustering, where the receiving actor may be on another node. |
| **D4 Extend `IWorkflowMetrics`, don't replace it** | Back it with a `System.Diagnostics.Metrics.Meter` so OTel/Prometheus exporters pick it up for free, while keeping `Snapshot()` — the designer's status page and `MonitoringEndpointsTests` depend on it. `RecordStarted/Completed/Failed/Cancelled` stay as the call sites; the implementation gains instruments plus new duration histograms. |
| **D5 Cardinality is a budget, and node id is over it** | Metric tags are restricted to **bounded** dimensions: `workflow_name`, `module_id`, `outcome`, `node_type`. **Never** `node_id`, `execution_id`, or a user-supplied variable value — a 1000-execution burst would mint a time series per node and take the scrape down. Per-execution and per-node detail belongs in **traces and the execution history**, which are already built for it. A unit test asserts the tag key allow-list so this can't drift. |
| **D6 Instrument at the lifecycle points that already exist** | `ExecutionStarted/Completed/Failed`, `NodeStarted/Completed/Failed`, and `ExecutionProgress` already exist as typed realtime events, and `ExecutionRecord`/`NodeExecutionRecord` already carry durations. Emit metrics and close spans at those same points rather than scattering new instrumentation — one set of definitions for "what happened", already proven correct. |
| **D7 Two `/metrics`, deliberately** | `GET /api/v1/metrics` keeps returning the JSON snapshot (the UI consumes it; changing it is a breaking API change for no benefit). Prometheus exposition is served at the conventional **unversioned `/metrics`** in the text format. They read the same meter. |
| **D8 Health semantics: liveness must not fail on a dependency** | `/health/live` answers "is the process wedged?" and must **not** consult persistence — a database blip that fails liveness makes Kubernetes kill an otherwise-healthy pod and turns an outage into a crash loop. `/health/ready` is where dependency checks belong. Audit the existing two checks against this split and correct as needed. |
| **D9 Observability must not eat the 4.1 budget** | Phase 4.1 targets < 50 ms workflow overhead and < 100 ms p95 API. Instrumentation is measured, not assumed: the benchmark from 4.1 runs with telemetry on and off, and the delta is recorded. Metric recording stays allocation-free on the hot path (pre-allocated `TagList`, no boxing), and tracing is sampled (**Q5**). |
| **D10 Logs are a redaction surface** | Structured logging will happily serialise a whole property bag, which after Phase 3.5's V4 can contain resolved variable values — including credentials. Log enrichment routes through the same redactor the secrets workstream defines (see [`Designer-Workflow-Variables-Secrets-Plan.md`](../use-testing-feedback/Designer-Workflow-Variables-Secrets-Plan.md) S4), and **no log statement serialises a raw property/variable map**. Sequencing is **Q8**. |

---

## TO RESOLVE 🤔

| # | Question | Proposal |
|---|---|---|
| **Q1** | **Scrape or push?** Prometheus pull (`/metrics` scrape) or OTLP push to a collector — or both? Pull is simplest for Kubernetes; push suits serverless (Phase 4.7 lists Azure Container Apps and ECS/Fargate, where scraping is awkward). | **Both, via one OTel pipeline** — the meter is the same, only exporters differ. Default to pull; enable OTLP by configuration. |
| **Q2** | **Cardinality allow-list.** D5 proposes `workflow_name`, `module_id`, `outcome`, `node_type`. Is `workflow_name` acceptable, or should it be `workflow_id` hashed / omitted? A tenant with 10 000 workflows makes even the name unbounded. | Ship the allow-list with `workflow_name`, plus a configurable cap that falls back to `other` past N distinct values. |
| **Q3** | **Log aggregation sink.** §4.2 lists "Elasticsearch/Seq". Seq is far lighter for self-hosting and understands Serilog's structure natively; Elasticsearch suits existing ELK shops. | **Seq by default, sink pluggable by configuration.** Don't pick one exclusively. |
| **Q4** | **Does anything already consume `/api/v1/metrics`?** D7 assumes the designer does. Worth confirming before treating its shape as frozen. | Verify during 4.2.2; if nothing consumes it, collapse to one endpoint. |
| **Q5** | **Trace sampling default, and are node-level spans on by default?** A 1000-execution burst with a span per node is a lot of spans. | Parent-based + 10 % head sampling by default; **always sample failures**. Node spans on, since a workflow trace without node spans is nearly useless. |
| **Q6** | **Is the CA1848 cleanup in scope?** There are **272** `CA1848` warnings (logging via non-source-generated extension methods). It's the difference between allocating on every log call and not — material once logs are structured and shipped. | Convert the **hot paths only** (`WorkflowExecutor`, `NodeExecutor`, binder) in 4.2.0; track the rest as a follow-up so it doesn't swallow the week. |
| **Q7** | **Where do alerts go?** Alertmanager, a webhook, email? Note DotFlow has a webhook module — routing its own alerts through itself is cute but circular, and fails exactly when the engine is unhealthy. | Alertmanager rules shipped as YAML; notification routing left to the operator. **Explicitly not** self-hosted through DotFlow. |
| **Q8** | **Sequencing against the secrets plan (D10).** The secrets redactor may not exist yet when 4.2 lands. Ship a name-based redactor now and swap later, or block? | Ship a **field allow-list** now (log ids and counts, never values) — safe regardless, and it makes the later value-scanner a defence-in-depth addition rather than the only guard. |

---

## Slices

### 4.2.0 — Structured logging foundation 📝

**Tasks:**
- [ ] Wire Serilog via `UseSerilog` in `Workflow.Api/Program.cs` (D1); compact JSON in production,
      human-readable console in development; File sink with rolling policy
- [ ] Route Akka logging into `Microsoft.Extensions.Logging` so engine + API share one pipeline (D2)
- [ ] Configure minimum levels per source (`Akka` noise down, `Workflow.*` at Information)
- [ ] Enrichers: machine name, environment, application version (feeds the Phase 4.7 SemVer work)
- [ ] Convert hot-path logging to `LoggerMessage` source generators (Q6): `WorkflowExecutor`,
      `NodeExecutor`, `PropertyBinder`
- [ ] Field allow-list enrichment — never serialise raw property/variable maps (D10, Q8)

**Tests:**
- [ ] Log output is valid compact JSON with expected fields
- [ ] An Akka `_log` call and an `ILogger<T>` call land in the same sink with the same shape
- [ ] A resolved secret variable value never appears in emitted logs

### 4.2.1 — Correlation & context propagation 🧵

**Tasks:**
- [ ] `CorrelationMiddleware`: honour inbound `traceparent` (W3C) and `X-Correlation-Id`, else mint
      one; echo it on the response
- [ ] Push correlation id + `CallerIdentity` into the Serilog `LogContext` for the request scope
- [ ] Carry correlation id **on the message envelope** into the engine (D3) — extend
      `ExecutionStartOptions` / the execute messages, mirroring how `CallerId` already travels
- [ ] Re-establish the logging scope inside `WorkflowExecutor` and `NodeExecutor` from the message
- [ ] Persist the correlation id on `ExecutionRecord` so history rows join to logs

**Tests:**
- [ ] Inbound `traceparent` is honoured; absent, one is generated
- [ ] A single execution's API log lines and engine log lines share one correlation id
- [ ] The correlation id survives the `Tell` boundary (the regression guard for D3)

### 4.2.2 — Metrics 📈

**Tasks:**
- [ ] Back `InMemoryWorkflowMetrics` with a `Meter` (D4), preserving `Snapshot()`
- [ ] Counters: `executions_started_total`, `_completed_total`, `_failed_total`, `_cancelled_total`
- [ ] Gauges: `executions_active`, queue depth, active module executions (an
      `IActiveExecutionTracker` already exists)
- [ ] **Histograms** (the gap — §4.2 asks for p50/p95/p99 and nothing measures duration today):
      `execution_duration_ms`, `node_duration_ms`, `api_request_duration_ms`
- [ ] Tag allow-list + cardinality cap (D5, Q2), with a test asserting the allow-list
- [ ] Prometheus exposition at unversioned `/metrics`; keep `/api/v1/metrics` JSON (D7)
- [ ] Optional OTLP exporter behind configuration (Q1)

**Tests:**
- [ ] `MeterListener` sees each instrument with expected tags
- [ ] Duration histograms record on completion, failure **and** cancellation
- [ ] High-cardinality tags are rejected/capped
- [ ] `/api/v1/metrics` JSON shape is unchanged (regression guard for existing consumers)

### 4.2.3 — Distributed tracing 🔭

**Tasks:**
- [ ] `ActivitySource` per component (`DotFlow.Api`, `DotFlow.Engine`, `DotFlow.Modules`)
- [ ] Span per HTTP request (ASP.NET Core instrumentation) and per workflow execution
- [ ] Span per node execution, parented via the message-borne context (D3)
- [ ] Tag spans with `workflow_id`, `execution_id`, `node_id`, `module_id` — **unbounded values are
      fine here**, which is precisely why they're excluded from metrics (D5)
- [ ] Record exceptions on spans; set status from execution outcome
- [ ] Sampling policy (Q5), always-sample-on-error
- [ ] Exporter: OTLP → Jaeger/Tempo

**Tests:**
- [ ] `ActivityListener` sees a parent execution span with child node spans
- [ ] **Child spans share the parent's trace id across the actor boundary** — the D3 guard
- [ ] A failed node marks the span with error status and the exception
- [ ] Sampling honours the configured rate; failures always sampled

### 4.2.4 — Health checks 🩺

**Tasks:**
- [ ] Audit `/health/live` for dependency checks and remove any (D8)
- [ ] `/health/ready`: persistence, actor system, module registry populated
- [ ] Add a SignalR hub health check (the designer degrades to polling without it — worth knowing)
- [ ] Per-provider detail in the readiness payload (which store is unhealthy, not just "unhealthy")
- [ ] Document expected Kubernetes probe configuration for Phase 4.7

**Tests:**
- [ ] Liveness stays healthy when persistence is down; readiness does not
- [ ] Readiness names the failing dependency
- [ ] Probe payload shape is stable

### 4.2.5 — Dashboards & alerting 📺

**Tasks:**
- [ ] Grafana dashboard JSON committed to the repo (`ops/grafana/`), not hand-built in a UI —
      dashboards are code or they drift
- [ ] Panels: execution rate, success/failure ratio, duration percentiles, active executions,
      queue depth, API latency, error rate, resource utilisation
- [ ] Alertmanager rules (`ops/alerts/`): error-rate spike, p95 latency breach, executions stuck
      active beyond a threshold, node down, persistence unhealthy
- [ ] A **runbook** entry per alert — an alert without a documented response is a pager that gets ignored
- [ ] `docker-compose.observability.yml` for local Prometheus + Grafana + Jaeger + Seq

**Tests:**
- [ ] Dashboard JSON parses and references only metrics that exist (a test that greps metric names
      out of the JSON and asserts each is registered — catches the classic "renamed the metric,
      broke the dashboard silently")
- [ ] Alert rules are valid `promtool` syntax

### 4.2.6 — Overhead validation ⏱️

**Tasks:**
- [ ] Re-run the Phase 4.1 benchmark with telemetry on and off; record the delta (D9)
- [ ] Confirm the < 50 ms workflow-overhead and < 100 ms p95 targets still hold with telemetry on
- [ ] Document the measured cost per instrumentation layer

**Tests:**
- [ ] Benchmark comparison committed with results

---

## Deliverables

- ✅ One structured log pipeline covering API + engine, with correlation ids that survive the actor
      boundary
- ✅ Metrics with duration histograms and a defended cardinality budget, exposed for scrape and/or OTLP
- ✅ End-to-end traces from HTTP request → workflow → node
- ✅ Liveness/readiness split that won't crash-loop a pod on a database blip
- ✅ Dashboards and alert rules committed as code, each alert with a runbook
- ✅ Measured proof that observability didn't eat the Phase 4.1 performance budget

## Success criteria

- [ ] A single execution can be followed end-to-end by one correlation id across API and engine logs
- [ ] A trace shows the execution span with child node spans, on one node **and** across a cluster
      (re-verified in Phase 4.4)
- [ ] p50/p95/p99 execution duration visible in Grafana
- [ ] No metric series count explosion under a 1000-execution burst
- [ ] No credential or variable value appears in any log sink
- [ ] Telemetry overhead measured and within budget

---

*Made with 💖 by Ami-Chan! UwU* ✨
