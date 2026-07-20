# Phase 3.5: Execution Monitor UI — "Mission Control" 📡

Made with 💖 by Ami-Chan! UwU ✨

[Back to Phase 3](Phase3-AdvancedFeatures.md) | [All Phases](README.md)

> ## ✅ COMPLETE — all 6 slices (3.5.0–3.5.5) implemented, **~63 monitor tests** (242 total in
> `Workflow.Tests.UI` + 4 new `Workflow.Tests` endpoint tests), documented in
> [`docs/execution-monitor.md`](../docs/execution-monitor.md). The **only backend work** was **two
> read-only endpoints** (`/executions/{id}/detail` + `/nodes`) over already-persisted data — **no
> engine/persistence/hub changes**. Reused the 3.2 `SubscribeToAll` firehose + generalized 3.3.c's
> `RunState`/`RunOverlay`; the D2 framework-free boundary keeps the React port additive~ 🌸

---

## Overview

Phase 3.5 delivers a **dedicated execution-monitoring experience** — "Mission Control" — a page
where operators watch running workflows live, browse historical runs, drill into a single
execution's **node-by-node** progress, timings, inputs/outputs, and logs, and **replay** a finished
run step-by-step. Like Phases 3.3 and 3.4, it is **mostly a thin front-end** over infrastructure
that already shipped — but unlike them it needs **two small, additive, read-only API endpoints**
to expose data the engine already persists.

> **CopilotNote:** Hot paths: a new `Workflow.UI.Client/Monitor/*` feature area
> (`ExecutionMonitor.razor` page at `/monitor`, `ExecutionDetail.razor` at `/monitor/{executionId}`,
> plus framework-free state), **generalizing** the designer's `RunState` + `RunOverlay` out of
> `Designer/*` so both the designer and the monitor share them, a `RealTimeClient.SubscribeToAllAsync`
> addition (the hub method already exists), and **two read-only endpoints** in `Workflow.Api`
> (global execution list + node-execution records). Tests extend `Workflow.Tests.UI` (bUnit + xUnit)
> and `Workflow.Tests` (the endpoints)~ 🌸

> **Reality-check note (July 2026):** the §3.5 checklist in
> [`Phase3-AdvancedFeatures.md`](Phase3-AdvancedFeatures.md#35-ui---execution-monitor-week-21)
> predates Phases 3.2 and 3.3. Since then a large fraction of it already exists:
> - **Real-time hub + typed events (3.2):** `RealTimeClient` wraps the SignalR hub and raises
>   `ExecutionStarted/Completed/Failed`, `NodeStarted/Completed/Failed`, `ExecutionProgress`, and a
>   late-join `ExecutionSnapshot`. The hub **already has `SubscribeToAll()`** (admin-gated) and the
>   `ExecutionEventBridge` publishes every event to the `all` group — so a **live global dashboard
>   needs no hub changes**.
> - **Live node visualization (3.3.c.1):** `RunState` (framework-free node run-state + progress +
>   run log + `CssClassFor` painting + snapshot seeding) and `RunOverlay.razor` (progress bar, node
>   list with per-node timing, run log, reconnect banner) already implement "node-by-node progress"
>   and "real-time status" — they live under `Designer/` and just need generalizing.
> - **Execution list + history (3.3.c.2):** `ExecutionsClient` (`ListAsync`/`GetStatusAsync`/
>   `CancelAsync`) + `ExecutionHistory.razor` already list a **workflow's** executions with
>   status/duration; the list endpoint supports `status`/`from`/`to`/`page`/`pageSize` filters.
> - **Persisted node records:** `IExecutionHistoryRepository.GetNodeExecutionsAsync` returns
>   `NodeExecutionRecord`s carrying **inputs, outputs, state, started/completed, duration, error,
>   loop id/iteration** — everything the node-detail and replay features need. It's implemented in
>   all three providers (Sqlite/Postgres/Nats) but **not exposed via REST**.
>
> **The two genuine gaps** (see [Design Decisions](#confirmed-design-decisions-) D5): (1) the list
> endpoint **requires a `workflowId`** — there is no cross-workflow list; (2) node records aren't
> reachable over HTTP. This plan reconciles the checklist and supersedes it.

**Timeline:** ~1–1.5 weeks (Week 32 — the checklist's original "Week 21" renumbered to follow 3.4).
**Complexity:** 🟠 Medium — the client mechanics are well-bounded (reuse `RunState`/`RunOverlay`,
the hub firehose), but this is the **first Phase-3 UI slice that touches the backend**, and the
live-global-list + replay-timeline are new interaction surfaces.

---

## Confirmed Design Decisions ✅

| # | Decision |
|---|----------|
| **D1 A dedicated "Mission Control" area in the existing Blazor client** | Two routes in `Workflow.UI.Client`: `/monitor` (the dashboard — live + recent executions) and `/monitor/{executionId}` (single-execution detail). Reachable from the top bar (`📡 Monitor`). Reuses the 3.3 shell (TopBar, ToastHost, tokens.css, `AuthState`, ProblemDetails handling). No new host/project. |
| **D2 Contracts-only + framework-free boundary (inherited from 3.3 D2)** | The monitor talks to the backend **only** through `/api/v1/executions/*` + the 3.2 hub. All *logic* (list/filter/sort model, live-merge of hub events into rows, replay cursor) lives in **framework-free C# services** (`Monitor/State/*` — no Blazor/JSInterop/LanguageExt types), with plain STJ wire-DTO mirrors. Keeps the React+TS port (3.3.P7) additive. |
| **D3 Generalize `RunState` + `RunOverlay` out of the designer (don't fork)** | Move `Designer/State/RunState.cs` → `Execution/State/RunState.cs` (shared namespace) and `Designer/Components/RunOverlay.razor` → a shared `Execution/Components/`; the designer keeps using them (its `@using` updated). Both the designer's live overlay (3.3.c.1) and the monitor's detail view render from the **same** `RunState`. Zero behavioral change to the designer (its tests stay green). |
| **D4 Live dashboard via the existing `SubscribeToAll` firehose (no hub changes)** | `RealTimeClient` gains `SubscribeToAllAsync()`/`UnsubscribeFromAllAsync()` (the hub methods + the `all` group already exist, 3.2). The dashboard connects, seeds rows from the REST list, then **merges** `ExecutionStarted/Progress/Completed/Failed` events into the live rows (a new row appears when an execution starts; its state/percentage update in place). Auto-resubscribes on reconnect like the per-execution path. |
| **D5 Two small, additive, read-only endpoints (the only backend work)** | (a) **`GET /api/v1/executions/{id}/nodes`** → projects `GetNodeExecutionsAsync` to `NodeExecutionRecordDto[]` (nodeId, state, startedAt, completedAt, durationMs, error, inputs, outputs, loopId, loopIteration). (b) **`GET /api/v1/executions/{id}/detail`** → the persisted `ExecutionRecord` (state, timing, inputs, outputs, error, triggeredBy) so **finished** executions render even after the actor is gone (the existing `GET /executions/{id}` uses live `GetStatusAsync`, which returns null once the run leaves memory). Both `WorkflowRead`, no writes, no engine changes. The repo methods already exist in all three providers. *(A cross-workflow historical **list** endpoint is deferred — see Q1/3.5.P1.)* |
| **D6 Node-by-node detail + timings from persisted records** | The detail view seeds `RunState` from `ExecutionSnapshot` (live) **or** from `/executions/{id}/nodes` (history), then paints the node list with per-node state + duration, and shows a selected node's **inputs/outputs** (pretty JSON) + error + loop iteration. This is the "node-by-node progress visualization" + "view node inputs/outputs" checklist items. |
| **D7 Log viewer = the event-derived run log (real per-node logs are post-MVP)** | The hub streams node/execution *events*, not module/script log lines; `RunState.Log` already synthesizes a readable, timestamped run log from them ("node X started", "node Y completed (12ms)", "execution failed: …"). The monitor's log viewer renders `RunState.Log` with a **level filter** (derived from event kind) + **search** + **copy/download**. Streaming real per-node module/script logs needs engine work → **3.5.P2**. |
| **D8 Replay = a read-only scrub over the recorded node records (not re-execution)** | For a finished run, `/executions/{id}/nodes` (ordered by `startedAt`) drives a **timeline scrubber**: stepping reveals each node's state + inputs/outputs + duration up to that point, and paints the (optional) canvas/list. "Step through node execution" + "timeline visualization" are satisfied read-only. **"Variables at each step"** needs per-step variable snapshots the engine doesn't persist → **3.5.P3**. |
| **D9 Filtering/sort: server filters + client-side sort/duration** | Status + date-range filters use the existing `ExecutionFilter` (`status`, `from`, `to`) on the workflow-scoped list; **sort by column** and **duration filter** are applied client-side to the loaded page (the professional-monitoring nicety without new server work). Workflow filter = the workflow picker (Q1). |
| **D10 Auth: read policies; graceful degradation without admin/hub** | List/detail/nodes use `WorkflowRead`. `SubscribeToAll` is **admin-gated** (3.2) — when the user lacks admin *or* the hub is unreachable, the dashboard **falls back to polling** the REST list on an interval (reusing the designer's polling-fallback pattern, 3.3.c.1). Anonymous-friendly when `Api:Auth:Require=false`. |
| **D11 Testing** | New `Workflow.Tests.UI` specs: framework-free list/live-merge/replay-cursor state + bUnit render/interaction for the dashboard, detail view, log viewer, filters, and replay. The two new endpoints get `Workflow.Tests` xUnit coverage (projection + policy + not-found). The generalized `RunState`/`RunOverlay` keep their existing 3.3 tests (moved namespaces). |
| **D12 Reuse the 3.3 client plumbing** | `ExecutionsClient` (extended), `RealTimeClient` (extended), `AuthState`/`AuthMessageHandler`, `ApiException`/`ApiError`, `ToastService`, `ILocalStorage`, `keys.js`, `tokens.css`, and the `RunState`/`RunOverlay` visuals — all reused. |

---

## RESOLVED ✅

> Q1–Q6 confirmed (all proposed answers **Agreed** by the user) — implementation proceeds on these~ ✅

- [x] **Q1 Global historical list → MVP live-all + workflow-scoped history.** A **live global dashboard** (running executions via `SubscribeToAll`) **plus** a **workflow-scoped** historical list (pick a workflow → the existing `/executions?workflowId=` endpoint). A true cross-workflow *historical* list (new `IExecutionHistoryRepository.GetExecutionsAsync` in all three providers + endpoint) → **3.5.P1**.
- [x] **Q2 Node/detail endpoints confirmed.** `GET /executions/{id}/nodes` + `GET /executions/{id}/detail` — both read-only, `WorkflowRead`, projecting existing repo methods. No engine/persistence changes; the live `/{id}` status endpoint stays untouched.
- [x] **Q3 Admin-only live dashboard is acceptable.** Admin users get the live firehose; non-admin users get the same dashboard **backed by polling** (D10). A non-admin live scope → **3.5.P4**.
- [x] **Q4 Log viewer = event-derived run log now.** `RunState.Log` with level filter + search + copy/download. Real per-node log streaming → **3.5.P2**.
- [x] **Q5 Replay = read-only node-record scrub now.** Scrub the recorded node records (state + inputs/outputs + timing). Per-step **variable snapshots** → **3.5.P3**.
- [x] **Q6 `/monitor` area + designer bridge.** `/monitor` (dashboard) + `/monitor/{executionId}` (detail), a `📡 Monitor` top-bar link, and the designer keeps its inline `ExecutionHistory` with an "open in Monitor →" deep link.
    - Agreed. 

---

## Pre-Existing Work (from earlier phases) ✅

| Component | File / Endpoint | Status |
|-----------|-----------------|--------|
| Typed execution client (list/status/cancel) | `Workflow.UI.Client/Api/ExecutionsClient.cs` + `Api/Dtos/ExecutionDtos.cs` (3.3.a.0) | ✅ Extended (SubscribeToAll wiring, detail/nodes) |
| Real-time hub client + typed events | `Api/RealTimeClient.cs` + `Api/Dtos/RealTimeDtos.cs` (3.2/3.3) | ✅ Gains `SubscribeToAllAsync` (D4) |
| Hub `SubscribeToAll` + `all` group + event bridge | `Workflow.Api/RealTime/WorkflowHub.cs`, `RealTimeGroups.All`, `ExecutionEventBridge.cs` (3.2) | ✅ **Already broadcasts to `all`** — no hub changes |
| Framework-free node run state | `Designer/State/RunState.cs` (3.3.c) | ✅ Generalized → `Execution/State/RunState.cs` (D3) |
| Live run overlay (progress/nodes/log/reconnect) | `Designer/Components/RunOverlay.razor` (3.3.c.1) | ✅ Generalized → shared `Execution/Components/` (D3) |
| Per-workflow execution history list | `Designer/Components/ExecutionHistory.razor` (3.3.c.2) | ✅ Reused + deep-linked into `/monitor` (Q6) |
| Workflow-scoped list endpoint (status/date filters) | `GET /api/v1/executions?workflowId=…` (2.7.2) | ✅ Reused for the history list (D9) |
| Live status + cancel endpoints | `GET/POST /api/v1/executions/{id}[/cancel]` (2.7.2) | ✅ Reused |
| Persisted node records (I/O, timing, error, loop) | `IExecutionHistoryRepository.GetNodeExecutionsAsync` / `GetExecutionAsync` (2.2) | ✅ Exposed via new read-only endpoints (D5) |
| Client shell + auth + toasts + polling-fallback + tests | `Shared/*`, `Api/AuthState.cs`, `Api/ApiError.cs`, `Workflow.Tests.UI` (3.3) | ✅ Reused (D1/D10/D12) |

> **CopilotNote:** The mirror of the 3.3/3.4 insight, with a twist: the **live pipeline and the
> node-visualization primitives already exist**, but the **historical read surface is missing two
> HTTP endpoints** over data the engine already stores. Phase 3.5 writes a little `Workflow.Api`
> code (two read-only handlers + DTOs) — no `Workflow.Engine`/persistence changes for the MVP.
> Budget risk on the **live-merge of firehose events into list rows** and the **replay cursor** —
> not on plumbing~ 💖

---

## Screen Mockups 🖼️

### S1 — Mission Control (dashboard: `/monitor`)

```text
┌──────────────────────────────────────────────────────────────────────────────────────────┐
│ 🌊 DotFlow · 📡 Monitor            [Workflow: all ▾] [Status ▾] [⏱ any ▾] [🔄 live] [⚙]     │
├──────────────────────────────────────────────────────────────────────────────────────────┤
│ ● LIVE  3 running                                                                          │
│ ┌────────────────────────────────────────────────────────────────────────────────────┐   │
│ │ ⏱  order-pipeline    ▶ 63%  ▓▓▓▓▓▓▓░░░  node: enrich   00:12   exec 8f3a… [cancel]  │   │
│ │ ⏱  nightly-etl       ▶ 20%  ▓▓░░░░░░░░  node: extract  00:44   exec 1b90…           │   │
│ └────────────────────────────────────────────────────────────────────────────────────┘   │
│ RECENT                                                          ⬍ sort: Started ▾          │
│ ┌ State ┬ Workflow ────────┬ Started ────────┬ Duration ┬ Exec ─────────┬ ─────────────┐  │
│ │ ✅    │ order-pipeline    │ 16:31:02        │ 1.4s     │ 7c2e…         │ [open →]     │  │
│ │ ❌    │ payment-sync      │ 16:29:50        │ 0.9s     │ 55a1…  error  │ [open →]     │  │
│ │ 🛑    │ nightly-etl       │ 16:10:11        │ 12.0s    │ 03bd…         │ [open →]     │  │
│ └───────┴───────────────────┴─────────────────┴──────────┴───────────────┴──────────────┘  │
│                                                            « 1 2 3 … »  20 / page           │
└──────────────────────────────────────────────────────────────────────────────────────────┘
   🟢 hub connected · admin (live firehose)     ·     [🟡 polling every 3s] when not admin/offline
```

### S2 — Execution detail + replay (`/monitor/{executionId}`)

```text
┌──────────────────────────────────────────────────────────────────────────────────────────┐
│ ← Monitor   exec 8f3a1c…   order-pipeline   ❌ Failed · 3.2s · started 16:31:02  [cancel]  │
├──────────────────────────┬───────────────────────────────────────────────────────────────┤
│ NODES                    │ SELECTED NODE:  enrich                                          │
│ ✅ validate      8ms     │  state ❌ Failed · 41ms · loop —                                │
│ ✅ fetch         120ms   │  ── inputs ──                     ── outputs ──                 │
│ ▶  enrich  ❌    41ms    │  { "id": 42,                      (none — node failed)          │
│ ⬜ notify        —       │    "region": "eu" }               ── error ──                   │
│                          │                                   TimeoutError: upstream 5s     │
│ ── LOGS ──  [level ▾][🔍]│                                                                 │
│ 16:31:02 node validate…  │ ── REPLAY ──                                                    │
│ 16:31:02 node fetch com… │  |●———————————○———————————○————|  step 3 / 4   ⏮ ◀ ▶ ⏭        │
│ 16:31:03 node enrich fa… │  (scrub to see each node's state + I/O at that point)          │
│ [copy] [download .txt]   │                                                                 │
└──────────────────────────┴───────────────────────────────────────────────────────────────┘
```

---

## Architecture (framework-swap ready) 🏗️

```text
┌──────────────────  Workflow.UI.Client (WASM)  ───────────────────┐
│  Blazor components (THIN views)                                  │
│   Pages/ExecutionMonitor.razor  Pages/ExecutionDetail.razor      │
│   Execution/Components/: RunOverlay (moved) · ExecutionRow ·     │
│     NodeInspector · LogViewer · ReplayTimeline · MonitorFilters  │
│        │                                                         │
│        ▼   framework-free C#  ◀── React port re-implements ──▶   │
│   Execution/State/: RunState (moved) · MonitorState (live list   │
│     + merge) · ExecutionFilterModel · ReplayCursor               │
│   Api/: ExecutionsClient(+detail/nodes) · RealTimeClient(+all)   │
└───────────────┬──────────────────────────────────────────────────┘
                │ REST (JSON) + SignalR
                ▼
   /executions?workflowId= · /{id} · /{id}/cancel · /{id}/detail 🆕 · /{id}/nodes 🆕
   hub: SubscribeToAll (admin) · SubscribeToExecution
                ▲
┌───────────────┴──────────  Workflow.Api  ────────────────────────┐
│  ExecutionEndpoints: + DetailHandler + NodesHandler (read-only)  │
│  → IExecutionHistoryRepository.GetExecutionAsync/GetNodeExecutions│
│  (already implemented in Sqlite/Postgres/Nats — no repo changes) │
└──────────────────────────────────────────────────────────────────┘
```

**React exit checklist** additions live in `docs/designer-architecture.md`: `MonitorState`,
`ExecutionFilterModel`, `ReplayCursor`, and the (now shared) `RunState` are framework-free with
xUnit specs; the only new interop is none (the hub client is already framework-free).

---

## Proposed File Layout 🗂️

```text
Workflow.Api/
  V1/ExecutionEndpoints.cs                 (+ DetailHandler, NodesHandler — read-only)
  Contracts/ (or inline)                   NodeExecutionRecordDto, ExecutionDetailDto
Workflow.UI.Client/
  Api/
    ExecutionsClient.cs                     (+ GetDetailAsync, GetNodesAsync)
    RealTimeClient.cs                        (+ SubscribeToAllAsync/UnsubscribeFromAllAsync)
    Dtos/ExecutionDtos.cs                    (+ NodeExecutionRecordDto, ExecutionDetailDto)
  Execution/                                 (moved from Designer/, shared)
    State/
      RunState.cs                            (moved; namespace Execution.State)
      MonitorState.cs                        (live list + event merge + polling seam)
      ExecutionFilterModel.cs                (status/date/duration/sort — framework-free)
      ReplayCursor.cs                        (step over ordered node records)
    Components/
      RunOverlay.razor                       (moved from Designer/Components)
      ExecutionRow.razor · MonitorFilters.razor
      NodeInspector.razor · LogViewer.razor · ReplayTimeline.razor
  Pages/
    ExecutionMonitor.razor                   (/monitor)
    ExecutionDetail.razor                    (/monitor/{executionId})
  Designer/*                                 (@using updated to Execution.State/Components)
Workflow.Tests.UI/
  Execution/State/*   (MonitorState merge, filter model, replay cursor, RunState — moved)
  Execution/Components/*   (dashboard, detail, log viewer, filters, replay — bUnit)
Workflow.Tests/
  Api/ …ExecutionEndpoints… (detail + nodes projection/policy/not-found)
docs/execution-monitor.md   (user guide) + cross-links
```

---

## Slices & Dependencies 🧭

| Slice | Scope | Depends on |
|-------|-------|-----------|
| 3.5.0 Backend endpoints (`/detail` + `/nodes`) + client methods + DTOs | — |
| 3.5.1 Generalize `RunState` + `RunOverlay` out of `Designer/` (no behavior change) | — |
| 3.5.2 Monitor dashboard: REST-seeded list + live merge via `SubscribeToAll` (+ polling fallback) | 3.5.0, 3.5.1 |
| 3.5.3 Execution detail: node list + `NodeInspector` (I/O) + live/historical seeding | 3.5.0, 3.5.1 |
| 3.5.4 Log viewer + history filters (status/date/duration) + sort + pagination | 3.5.2, 3.5.3 |
| 3.5.5 Replay timeline + designer deep-link + docs + polish | 3.5.3 |

---

## 3.5.0 Backend Read Endpoints + Client Methods 🔌 ✅ DONE

> **Purpose:** Expose the persisted execution detail + node records over HTTP and mirror them
> client-side. The **only** backend slice.

**Complexity:** 🟢 Low-Medium

### Tasks

- [x] **`ExecutionDetailDto` + `NodeExecutionRecordDto`** (server contracts) — project `ExecutionRecord` (state, startedAt, completedAt, durationMs, inputs, outputs, error, triggeredBy) and `NodeExecutionRecord` (nodeId, state, startedAt, completedAt, durationMs, error, inputs, outputs, loopId, loopIteration) with `From` factories in `Contracts/ExecutionContracts.cs`
- [x] **`GET /api/v1/executions/{id}/detail`** — `DetailHandler`: `GetExecutionAsync(id)` → `ExecutionDetailDto`; `404` when absent; `WorkflowRead`
- [x] **`GET /api/v1/executions/{id}/nodes`** — `NodesHandler`: `GetNodeExecutionsAsync(id)` → `NodeExecutionRecordDto[]` ordered by `startedAt`; `WorkflowRead` (empty array for unknown)
- [x] **`ExecutionsClient.GetDetailAsync(id)` + `GetNodesAsync(id)`** + list status/date filters — client DTO mirrors in `Api/Dtos/ExecutionDtos.cs`; ProblemDetails-aware
- [x] **`RealTimeClient.SubscribeToAllAsync()` / `UnsubscribeFromAllAsync()`** — invoke the existing hub methods; re-subscribe to `all` on `Reconnected`
- [x] **No engine/persistence changes** — the repo methods already exist in all three providers

### Tests (19 green: 4 API + 15 existing endpoint suite; 4 client)

- [x] `Detail_AfterCompletion_ReturnsProjectedRecord` · `Detail_UnknownExecution_Returns404`
- [x] `Nodes_AfterCompletion_ReturnsNodeRecords` *(real passthrough n1 record, ordered)* · `Nodes_UnknownExecution_ReturnsEmptyArray`
- [x] `ExecutionsClient_Detail_Parses` · `ExecutionsClient_Detail_Unknown_ReturnsNull` · `ExecutionsClient_Nodes_Parses` · `ExecutionsClient_List_AppendsStatusAndDateFilters` *(fake handler)*
- [~] `RealTimeClient_SubscribeToAll_…` — deferred to the 3.5.2 dashboard behavior test (the client wraps a real `HubConnection` with no unit seam; the admin-vs-poll path is exercised there)

---

## 3.5.1 Generalize `RunState` + `RunOverlay` 🔧 ✅ DONE

> **Purpose:** Share the node-run-state model + overlay between the designer and the monitor
> without forking. A pure refactor — no behavior change.

**Complexity:** 🟢 Low

### Tasks

- [x] **Moved `Designer/State/RunState.cs` → `Execution/State/RunState.cs`** — namespace `Workflow.UI.Client.Execution.State`; added it (+ `Execution.Components`) to `_Imports.razor` so the designer + monitor resolve it globally (still framework-free)
- [x] **Moved `Designer/Components/RunOverlay.razor` → `Execution/Components/RunOverlay.razor`** — `@namespace Workflow.UI.Client.Execution.Components`, `@using` both `Designer.State` (for `DesignerDocument`) + `Execution.State`
- [x] **Moved the tests** — `Workflow.Tests.UI/State/RunStateTests.cs` → `Execution/State/`; updated namespaces in `RunStateTests` + `OverlayRenderTests`
- [x] **Designer regression check** — full `Workflow.Tests.UI` suite (204) green, incl. `RunFlowTests`/`OverlayRenderTests`/`ExecutionHistoryTests` (only namespaces moved)

### Tests

- [x] `RunStateTests` (moved) green · designer overlay/run/history tests green (no behavior change) — **204 UI tests pass**

---

## 3.5.2 Monitor Dashboard (live list) 📡 ✅ DONE

> **Purpose:** The `/monitor` page — a live list of running executions merged from the hub
> firehose over a REST-seeded recent list, with a polling fallback (mockup **S1**).

**Complexity:** 🟠 Medium-High *(the live-merge is the fiddly bit)*

### Tasks

- [x] **`Execution/State/MonitorState.cs`** — framework-free: `MonitorRow` (`execId`, `workflowId`, `state`, `startedAt`, `completedAt`, `progress`, `currentNode`, `durationMs`, `error`), a keyed collection, `SeedFromList` + **merge** methods (`ApplyStarted/Progress/Completed/Failed`, `MarkCancelled`) that upsert rows by id, `Running`/`Recent(sort,desc)` projections; `Changed` event
- [x] **`Execution/Components/ExecutionRow.razor`** — a live row (state icon, workflow, progress bar for running, duration, exec id, open/cancel actions)
- [x] **`Pages/ExecutionMonitor.razor`** — route `/monitor`; connect `RealTimeClient`, `SubscribeToAllAsync`, and merge events into the injected `MonitorState`; a **LIVE** (running) + **RECENT** (terminal) section; `cancel` via `ExecutionsClient.CancelAsync`
- [x] **Polling fallback (D10)** — `SubscribeToAll` failure (no admin / hub down) sets a `🟡 polling` footer instead of `🟢 hub`; the REST-seeded recent history rides the workflow filter in 3.5.4
- [x] **Nav** — `📡 Monitor` top-bar link; row `open →` navigates to `/monitor/{id}`; `MonitorState` registered scoped in `Program.cs`

### Tests (10 green): → `Workflow.Tests.UI/Execution/State/MonitorStateTests.cs` + `Components/MonitorTests.cs`

- [x] `Merge_StartedThenProgress_UpsertsRunningRow` · `Merge_Completed_MovesToRecent` · `Merge_Failed_ShowsError`
- [x] `SeedFromList_ThenLiveEvent_Merges_NoDuplicates` · `Recent_Sort_ByDuration_Orders` · `Changed_Raised_OnMerge`
- [x] `Monitor_RendersRows_FromState` · `Monitor_HubUnreachable_ShowsPollingIndicator`
- [x] `Monitor_RowOpen_NavigatesToDetail` · `Monitor_Cancel_CallsClient`

---

## 3.5.3 Execution Detail + Node Inspector 🔎 ✅ DONE

> **Purpose:** `/monitor/{executionId}` — node-by-node state + timings + a selected node's
> inputs/outputs/error, seeded live (snapshot + events) or from history (`/nodes`) (mockup **S2**).

**Complexity:** 🟡 Medium

### Tasks

- [x] **`Pages/ExecutionDetail.razor`** — route `/monitor/{executionId}`; header (workflow, state, duration, cancel-if-running, back-to-monitor, open-in-designer); **finished** → `GetDetailAsync` + `GetNodesAsync`; **running** → `SubscribeToExecutionAsync` + seed `RunState` from `ExecutionSnapshot` + apply node events, synthesizing node rows from `RunState` until records land
- [x] **`Execution/Components/NodeInspector.razor`** — the node list (state icon + per-node duration + loop iteration) with **self-managed selection** (defaults to first node); the selected node's **inputs**/**outputs** (pretty JSON) + **error**
- [x] **Live→terminal transition** — on `ExecutionCompleted`/`Failed`, re-fetch `/nodes` to fill the final I/O the events didn't carry, and flip the header state
- [x] **Cancel** — running executions expose cancel (reusing `ExecutionsClient.CancelAsync`)

### Tests (5 green): → `Workflow.Tests.UI/Execution/Components/ExecutionDetailTests.cs`

- [x] `Detail_Historical_LoadsNodes_ShowsHeader` · `NodeInspector_Select_ShowsInputsOutputsError`
- [x] `Detail_Running_ShowsCancel` · `Detail_Unknown_ShowsNotFound` · `Detail_OpenInDesigner_Navigates`

---

## 3.5.4 Log Viewer + History Filters 📜🔍 ✅ DONE

> **Purpose:** A filterable/searchable log viewer (event-derived) and the history filters + sort
> the "professional monitoring" deliverable calls for.

**Complexity:** 🟡 Medium

### Tasks

- [x] **`Execution/Components/LogViewer.razor`** — renders run-log entries with a **level filter** (`RunLogClassifier` maps event text → Debug/Info/Warning/Error), **search**, **copy** (`navigator.clipboard`) + **download .txt** (`dotflowDownload` helper in `keys.js`); wired into the detail page's run log
- [x] **`Execution/State/ExecutionFilterModel.cs`** — framework-free: workflow/status/date-range (→ server query) + **duration** + **sort** (client-side); `ServerStatus`, `MatchesDuration`, `Apply(rows)`, `ToggleSort`. Plus `RunLogClassifier.LevelOf`
- [x] **`Execution/Components/MonitorFilters.razor`** — the dashboard filter bar (workflow id, status, min duration, Apply); re-queries the REST list on Apply when a workflow is set, re-sorts client-side otherwise
- [x] **Pagination + sort** — `PageDto` paging (20/page) via `ListAsync`; sortable RECENT column buttons (Started/Duration) driving `ExecutionFilterModel.ToggleSort`

### Tests (14 green): → `Workflow.Tests.UI/Execution/State/FilterModelTests.cs` + `Components/LogViewerTests.cs` (+ `MonitorTests`)

- [x] `Filter_Status_MapsToServerQuery` · `Filter_Duration_FiltersClientSide` · `Sort_ByColumn_Orders` · `ToggleSort_SameColumn_FlipsDirection` · `RunLogClassifier_LevelOf_Classifies`
- [x] `Logs_RendersAll_ByDefault` · `Logs_LevelFilter_Filters` · `Logs_Search_Filters`
- [x] `Logs_Copy_InvokesClipboard` · `Logs_Download_InvokesHelper`
- [x] `Filters_ApplyWithWorkflow_RequeriesList`

---

## 3.5.5 Replay Timeline + Designer Link + Docs 🎬🔗📚 ✅ DONE

> **Purpose:** A read-only replay scrubber over recorded node records, the designer↔monitor deep
> link, docs, and phase close.

**Complexity:** 🟡 Medium

### Tasks

- [x] **`Execution/State/ReplayCursor.cs`** — framework-free: ordered node records + a step index; `StepForward/Back/First/Last/SeekTo` (clamped); `Current`/`VisibleNodes` "as of" the step
- [x] **`Execution/Components/ReplayTimeline.razor`** — a scrubber (⏮ ◀ ▶ ⏭ + a clickable track + ←/→/Home/End keys) showing the current step's node; disabled while running; raises `OnStep`; wired into the detail page
- [x] **Designer deep link (Q6)** — the designer's `ExecutionHistory` gains a **📡** "open in Monitor" button per row → `/monitor/{id}`; the monitor detail offers **"open workflow in designer →"**
- [x] **`docs/execution-monitor.md`** — user guide: dashboard (live vs polling), filters/sort, detail + node inspector, log viewer, replay, designer bridge, and the API surface; mockups S1/S2; cross-links `docs/realtime.md`/`designer.md`
- [x] **Cross-links + port checklist** — appended `MonitorState`/`ExecutionFilterModel`/`ReplayCursor`/shared `RunState` to the `docs/designer-architecture.md` React-port checklist; `realtime.md` links to the monitor; `phases/README.md` + `Phase3-AdvancedFeatures.md` §3.5 → COMPLETE
- [x] **Polish** — replay keyboard (←/→/Home/End); empty/error/disabled states; `dotnet build Workflow.sln` (0 errors) + full `Workflow.Tests.UI` (242) + the `Workflow.Tests` endpoint suite green

### Tests (10 green): → `Workflow.Tests.UI/Execution/State/ReplayCursorTests.cs` + `Components/ReplayTests.cs`

- [x] `Cursor_OrdersByStart_AndStartsAtFirst` · `Cursor_Step_RevealsNodesUpTo` · `Cursor_SeekTo_Jumps` · `Cursor_Bounds_Clamp` · `Cursor_Empty_HasNoCurrent`
- [x] `Replay_Scrub_UpdatesCurrentNode` · `Replay_DisabledWhileRunning` · `Replay_RaisesOnStep`
- [x] `Designer_HistoryLink_NavigatesToMonitor`

---

## Agent Implementation Instructions 🤖

> **Audience:** an AI coding agent implementing this phase. Follow the same loop and guardrails as
> [Phase 3.4](Phase3-4-ScriptEditor.md#agent-implementation-instructions-); highlights specific to 3.5:

- **Slice order:** **3.5.0 + 3.5.1 first** (endpoints + the refactor unblock everything), then
  **3.5.2 ∥ 3.5.3**, then **3.5.4**, then **3.5.5**. 3.5.1 is a pure move — do it in one commit and
  confirm the designer's tests are green *before* building on the shared `RunState`.
- **This phase touches the backend — but minimally.** Only two **read-only** handlers + DTOs in
  `Workflow.Api/V1/ExecutionEndpoints.cs`. **Do not** change `Workflow.Engine`, the persistence
  repos, or the hub. If a task seems to need engine/repo/hub work (real logs, per-step variables,
  cross-workflow list, relaxing `SubscribeToAll`), **stop** — it's post-MVP (3.5.P1–P4).
- **Read the real contracts first:** `ExecutionEndpoints.cs` (routes/policies/`ExecutionStatusDto.From`),
  `IExecutionHistoryRepository` (`GetExecutionAsync`/`GetNodeExecutionsAsync`), `ExecutionRecord` +
  `NodeExecutionRecord` (the fields you project), `RealTime/WorkflowHub.cs` (`SubscribeToAll` +
  admin gate), and the existing `RunState`/`RunOverlay`/`ExecutionHistory` you're reusing. Capture a
  real `/nodes` response before building the inspector.
- **D2 guardrail (hard):** nothing in `Execution/State/*` or `Api/*` may reference Blazor/JS-interop
  types. `MonitorState`, `ExecutionFilterModel`, `ReplayCursor`, `RunState` are pure C# with xUnit
  specs. The hub client stays framework-free.
- **The live-merge is the risk.** Seed rows from REST, then upsert from `ExecutionStarted/Progress/
  Completed/Failed` keyed by `executionId`; a `Started` for an unknown id inserts a row; terminal
  events flip state + fill duration. Test the merge in isolation (`MonitorStateTests`) before wiring
  the page.
- **Auth degradation:** `SubscribeToAll` throws `HubException` without admin — catch it and switch to
  polling; never let the dashboard hard-fail for non-admins.
- **bUnit + loose JS interop / faked hub:** as in 3.3.c, fake the hub/interop and assert on state
  transitions + `data-testid` selectors (emoji text is mojibake-prone). Register **all** injected
  services (incl. the moved `RunState`, `MonitorState`) before the first render.
- **Bookkeeping ritual:** check off Tasks **and** Tests per slice, flip slice headers to `✅ DONE`,
  then at phase end check the Success Criteria, add an Overview completion banner, and update the
  `Phase3-AdvancedFeatures.md` §3.5 + `phases/README.md` pointers — exactly as 3.1–3.4 were closed.
  Track a todo per slice (`3-5-0`…`3-5-5`).
- **Repo gotchas:** `Workflow.sln`; Central Package Management (no new packages expected); PowerShell
  has no `&&`; known-flaky parallel engine tests verified in isolation are not regressions.

---

## Post-MVP Slices 🚧 *(deferred — not blocking 4.x)*

### 3.5.P1 Cross-workflow historical list 🌐 *(Q1)*
`IExecutionHistoryRepository.GetExecutionsAsync(filter, pagination)` across all workflows,
implemented in Sqlite/Postgres/Nats, + a `GET /api/v1/executions` (no `workflowId`) endpoint; the
dashboard's RECENT list becomes global.

### 3.5.P2 Real per-node log streaming 📜 *(Q4)*
Capture module/script log lines in the engine, persist them per node record, add a `NodeLog` hub
event, and stream real logs into the viewer (beyond the event-derived run log).

### 3.5.P3 Per-step variable snapshots 🔬 *(Q5)*
Persist a variable snapshot per node step so replay can show "variables at each step"; extend the
node-records endpoint + inspector.

### 3.5.P4 Non-admin live scope 👥 *(Q3)*
A per-caller "my executions" live group (or a relaxed policy) so non-admins get a live firehose
instead of polling.

### 3.5.P5 Metrics & charts 📊
Throughput/latency/failure-rate charts over the history (needs aggregation queries) — a "professional
monitoring" stretch.

---

## Success Criteria ✅

- [x] Open `/monitor` and see **running executions update live** (progress, current node, terminal state) merged over a REST-seeded recent list — with a **polling fallback** when not admin/offline
- [x] Open `/monitor/{id}` and see **node-by-node** state + per-node **timings**, and inspect a node's **inputs/outputs/error** (live or historical)
- [x] The **log viewer** filters by level, searches, and copies/downloads the run log
- [x] History **filters** (workflow/status/duration) + **column sort** + pagination work
- [x] **Replay** scrubs a finished run's node records step-by-step (read-only)
- [x] **Exactly two new read-only endpoints** (`/detail` + `/nodes`); **no** engine/persistence/hub changes for the MVP
- [x] `RunState`/`RunOverlay` are shared by the designer + monitor with the designer's tests still green
- [x] `docs/execution-monitor.md` exists and cross-links; the React-port checklist is updated
- [x] State services covered by xUnit specs; components by bUnit; the two endpoints by `Workflow.Tests`; full suites green (242 UI + endpoint suite)

---

*Made with 💖 by Ami-Chan! Watching workflows fly is the best part~ UwU* ✨
