# Execution Monitor 📡

> Phase 3.5 — "Mission Control": watch running workflows live, browse history, drill into a single
> run's node-by-node progress/timings/IO/logs, and replay a finished run. Made with 💖 by Ami-Chan~ ✨

The monitor lives at **`/monitor`** in the DotFlow app (top bar → **📡 Monitor**). It's a thin
front-end over the shipped execution pipeline: it reuses the Phase 3.2 real-time hub and the Phase
3.3 run-state/overlay primitives, adding only **two read-only endpoints** to expose data the engine
already persists.

- [The dashboard](#the-dashboard)
- [Live vs polling](#live-vs-polling)
- [Filters & sort](#filters--sort)
- [Execution detail](#execution-detail)
- [The log viewer](#the-log-viewer)
- [Replay](#replay)
- [From the designer](#from-the-designer)
- [API surface](#api-surface)

## The dashboard

```text
┌──────────────────────────────────────────────────────────────────────────────┐
│ 🌊 DotFlow · 📡 Monitor   [workflow id] [Status ▾] [⏱ ≥ n s] [Apply]           │
├──────────────────────────────────────────────────────────────────────────────┤
│ ● LIVE  2 running                                                              │
│   ⏱ order-pipeline  ▶ 63% ▓▓▓▓▓▓▓░░░  node: enrich   exec 8f3a…  [open][cancel] │
│ RECENT                                            [Started ▾][Duration]         │
│   ✅ order-pipeline   16:31:02   1.4s   7c2e…   [open →]                        │
│   ❌ payment-sync     16:29:50   0.9s   55a1…  error  [open →]                  │
├──────────────────────────────────────────────────────────────────────────────┤
│ 🟢 hub connected · live firehose      |      🟡 polling (when not admin/hub)    │
└──────────────────────────────────────────────────────────────────────────────┘
```

The **LIVE** section shows in-flight executions; the **RECENT** section shows finished ones. Each
row's **open →** opens the [execution detail](#execution-detail); running rows offer **cancel**.

## Live vs polling

The dashboard connects to the real-time hub and subscribes to the **global firehose**
(`SubscribeToAll`), merging `ExecutionStarted/Progress/Completed/Failed` events into the rows in
place — a new row appears when a run starts, its progress updates live, and it moves to RECENT when
it finishes. The firehose is **admin-gated**; when you aren't an admin (or the hub is unreachable),
the footer shows **🟡 polling** and the dashboard degrades gracefully — you can still load history
by workflow (see below).

## Filters & sort

Enter a **workflow id** + **Apply** to load that workflow's recent executions (status + date-range
filters map to the server query; the **min-duration** filter and **column sort** apply client-side).
Sort the RECENT list by **Started** or **Duration** (click again to flip direction).

## Execution detail

`/monitor/{executionId}` shows a run's header (workflow, state, duration, cancel-if-running) and a
**node inspector**: the list of nodes with per-node state + timing, and — for the selected node —
its **inputs**, **outputs**, **error**, and loop iteration (from the persisted node records). For a
**running** execution it seeds from the live snapshot and updates as node events arrive; on
completion it fetches the final records to fill in I/O.

## The log viewer

Below the nodes, the **run log** renders the event-derived log (node/execution events) with a
**level filter** (Debug/Info/Warning/Error), **search**, and **copy** / **download .txt**. *(Real
per-node module/script log streaming is a later phase.)*

## Replay

For a **finished** run, the **replay** scrubber steps through the recorded nodes in order
(⏮ ◀ ▶ ⏭, a clickable track, and ←/→/Home/End keys) — revealing the run "as of" each step. Replay
is a read-only view of the persisted records (not a re-execution); per-step variable snapshots are a
later phase.

## From the designer

The designer's inline **🕘 Executions** list has a **📡** button per row that deep-links to
`/monitor/{id}`; the monitor's detail view offers **"open workflow in designer →"**.

## API surface

The monitor uses the existing execution endpoints plus two read-only additions (Phase 3.5.0):

| Endpoint | Purpose |
|----------|---------|
| `GET /api/v1/executions?workflowId=…` | Workflow-scoped history (status/date filters) |
| `GET /api/v1/executions/{id}` | Live status |
| `GET /api/v1/executions/{id}/detail` 🆕 | Persisted execution record (renders after the run leaves memory) |
| `GET /api/v1/executions/{id}/nodes` 🆕 | Persisted node records (state, timing, inputs, outputs, error, loop) |
| `POST /api/v1/executions/{id}/cancel` | Cancel a running execution |
| hub `SubscribeToAll` (admin) / `SubscribeToExecution` | Live event streams |

---

See also: [Real-Time Hub](realtime.md) · [Designer](designer.md) ·
[Designer Architecture & React-Port Guide](designer-architecture.md).
