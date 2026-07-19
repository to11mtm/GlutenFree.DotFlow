# Phase 3.3.c: Designer Runtime (Week 30) ▶️

Made with 💖 by Ami-Chan! UwU ✨

[Master plan: Phase3-3-WorkflowDesigner.md](Phase3-3-WorkflowDesigner.md) | [Prev: 3.3.b Editing](Phase3-3b-DesignerEditing.md)

> **Scope:** Running workflows from the designer and watching them live: the ▶ Run flow,
> the real-time execution overlay driven by the Phase 3.2 hub (mockup **S3**), an
> **execution history panel** for reviewing past runs (mockup **S5**), plus the docs +
> polish pass that closes the phase. **No new backend** — everything rides
> `POST /execute`, `GET /executions[/{id}]`, and `/hubs/workflow` as shipped (D8).

> **🤖 Agent notes (read [master instructions](Phase3-3-WorkflowDesigner.md#agent-implementation-instructions-) first):**
> - Slice order: **c.0 → c.1 → c.2 → c.3** (strictly sequential — each reuses the previous slice's machinery).
> - Hub contracts: read `Workflow.Api/RealTime/IWorkflowHubClient.cs` + `Contracts/RealTime/RealTimeEvents.cs` for exact method names/payloads, and mirror the client patterns in `Workflow.Tests/Api/RealTime/*` (harness, re-subscribe-on-reconnect). Event *method names* are the SignalR `connection.On("…")` keys.
> - c.1 tests: drive `RunState` mapping with plain xUnit (no hub needed); for component tests, feed events through the `RealTimeClient` seam with a fake — do not spin real SignalR in bUnit. The real-hub path is covered by the manual smoke + the existing 3.2 server tests.
> - c.2 reuse rule: history-mode painting must go through the **same** `RunState`/overlay code path as c.1's snapshot seeding — if you find yourself writing a second painter, stop and refactor.
> - c.3 closes the phase: run the full checklist in the master's Agent Instructions step 4 (headers → DONE, Exit + Success Criteria, completion banner, pointer notes in `Phase3-AdvancedFeatures.md` + `phases/README.md`).

---

## 3.3.c.0 Execute From Designer ▶️

> **Purpose:** The ▶ Run button: collect trigger inputs, start the execution, and hand the
> execution id to the overlay. Also the list-page Run action's full version.

**Complexity:** 🟢 Low-Medium

### Tasks

- [ ] **Run dialog** — ▶ Run (toolbar): if dirty → prompt "Save & run / Run last saved / Cancel"; inputs pane: a JSON editor for the trigger `inputs` object (pre-seeded `{}`; remembered per workflow in `localStorage`); **[Start]** → `ExecutionsClient.ExecuteAsync(workflowId, inputs)`
- [ ] **Start handling** — success → enter **run mode** with the returned `executionId`; failure (400 validation / 404 / 503 no-persistence) → ProblemDetails in the dialog
- [ ] **Run mode state (`RunState`, framework-free)** — `ExecutionId`, `Overall (pending/running/completed/failed/cancelled)`, `Progress (pct, done, total)`, `Dictionary<nodeId, NodeRunStatus (state, durationMs?, error?)>`, ordered `LogEntries`; `Changed` event; **editing disabled while run mode is active** (toolbar + canvas guards; read-only banner)
- [ ] **Cancel** — `[⏹ Cancel]` → `ExecutionsClient.CancelAsync`; confirm dialog
- [ ] **Exit run mode** — `[✖ Close]` returns to edit mode, clears overlay state (document untouched)

### Tests (target ~7): → `Workflow.Tests.UI/State/RunStateTests.cs` + `Components/RunFlowTests.cs`

- [ ] `Run_Dirty_PromptsSaveAndRun` · `Run_StartsExecution_EntersRunMode`
- [ ] `Run_StartFailure_ShowsProblem` · `RunMode_DisablesEditing`
- [ ] `Cancel_CallsApi_AfterConfirm` · `Close_ReturnsToEditMode_DocumentIntact`
- [ ] `Inputs_RememberedPerWorkflow`

---

## 3.3.c.1 Real-Time Execution Overlay 📡 (the 3.2 payoff)

> **Purpose:** Mockup **S3**: nodes repaint live as the engine reports lifecycle events;
> progress bar + run log stream; late joins seed from the snapshot; reconnects self-heal.

**Complexity:** 🟡 Medium

### Tasks

- [ ] **Subscribe** — on entering run mode: `RealTimeClient.ConnectAsync(token)` (idempotent) → `SubscribeToExecutionAsync(executionId)`; the pushed **`ExecutionSnapshot`** seeds `RunState.Nodes` (per-node state strings map to `NodeRunStatus`) so joining mid-run paints correctly
- [ ] **Event → state mapping** — `NodeStarted` → node `running` · `NodeCompleted` → `completed` + duration · `NodeFailed` → `failed` + error · `ExecutionProgress` → progress bar (pct, done/total, current node chip) · `ExecutionCompleted`/`ExecutionFailed` → overall banner (✅/❌ + duration/error) + toast; every event appends a `LogEntry` (timestamp, text) to the run log pane
- [ ] **Canvas painting** — `NodeView` state classes (3.3.a hook): `pending` dimmed · `running` accent border + CSS pulse · `completed` green edge + ✅ chip + duration label · `failed` red + ❌ + error tooltip · `skipped` gray-dashed; edges out of completed nodes tint subtly (flow feel, cheap CSS only)
- [ ] **Node status pane (S3 right rail)** — per-node list (icon, state, duration); clicking an entry pans/zooms the canvas to that node; failed node entry shows the error text; **"View outputs"** = link out to the execution status payload (`GET /api/v1/executions/{id}` outputs pane) — no giant blobs over the hub (3.2 Q6)
- [ ] **Reconnect handling** — `RealTimeClient` re-subscribes on `Reconnected` (a.0); overlay shows a 🔁 "reconnecting…" chip while the connection is down; after re-subscribe the next snapshot/events reconcile state (document the missed-window caveat per 3.2 Q4)
- [ ] **Fallback polling (safety net)** — if the hub connection can't be established at all (e.g. WS blocked), poll `GET /executions/{id}` every 2s for coarse updates; banner notes degraded mode
- [ ] **Run-from-list completion** — the list page's ▶ action now opens the designer directly in run mode (deep-link `/designer/{id}?run={executionId}`)

### Tests (target ~10): → `Workflow.Tests.UI/State/OverlayMappingTests.cs` + `Components/OverlayRenderTests.cs`

- [ ] `Snapshot_SeedsNodeStates` · `NodeStarted_PaintsRunning` · `NodeCompleted_PaintsCompleted_WithDuration`
- [ ] `NodeFailed_PaintsFailed_WithError` · `Progress_UpdatesBarAndCounts`
- [ ] `ExecutionCompleted_ShowsBanner` · `ExecutionFailed_ShowsBanner`
- [ ] `Events_AppendToRunLog_InOrder` · `Reconnected_Resubscribes_AndReconciles`
- [ ] `HubUnavailable_FallsBackToPolling`

---

## 3.3.c.2 Execution History Panel 📜

> **Purpose:** Review past runs, not just live ones: a per-workflow execution history
> list, opening a finished execution to inspect outputs/errors, and painting its final
> node states onto the canvas by **reusing the c.1 overlay machinery**. Backend is fully
> shipped (2.7.2): `GET /api/v1/executions?workflowId=` + `GET /api/v1/executions/{id}`.

**Complexity:** 🟢 Low-Medium *(mostly reuse — list UI + a read-only overlay variant)*

### Mockup S5 — history panel + inspection

```text
┌──────────────────────────────────────────────────────────────────────────────────────┐
│ 🌊 order-pipeline v1.4.0            [💾 Save] [▶ Run] [🕘 History ▾]        [⤢ Fit]    │
├────────────────┬─────────────────────────────────────────────────────┬───────────────┤
│ EXECUTIONS 🕘  │        CANVAS (final states painted, read-only)      │ EXECUTION     │
│ ┌────────────┐ │                                                      │ 7f3a… ❌      │
│ │ ❌ 7f3a…    │ │   ✅ trigger ─▶ ✅ http-1 ─▶ ✅ cond-1 ─▶ ❌ script-1 │ Failed 14:03  │
│ │ 14:03 8.2s │◀┼── selected                                           │ Duration 8.2s │
│ ├────────────┤ │                 (log-fail: ⏭ skipped)                │ ───────────── │
│ │ ✅ 51c0…    │ │                                                      │ Error         │
│ │ 13:40 6.1s │ │                                                      │  script-1:    │
│ ├────────────┤ │                                                      │  "boom …"     │
│ │ ✅ 2ab9…    │ │                                                      │ Outputs       │
│ │ 12:15 5.9s │ │                                                      │  { "total":…} │
│ ├────────────┤ │                                                      │ Node states   │
│ │  ‹ more ›  │ │                                                      │  ✅✅✅❌⏭ list │
│ └────────────┘ │                                                      │ [↻ Re-run]    │
├────────────────┴─────────────────────────────────────────────────────┴───────────────┤
│ Viewing past execution 7f3a… (read-only)  ·  [✖ Back to edit]                          │
└──────────────────────────────────────────────────────────────────────────────────────┘
```

### Tasks

- [ ] **`ExecutionsClient.ListAsync(workflowId, page)`** — wraps `GET /api/v1/executions?workflowId=` (paged); DTO: id, state, startedAt, completedAt, triggeredBy
- [ ] **History panel (`ExecutionHistory.razor`)** — `[🕘 History]` toolbar toggle opens a left-rail list (replaces the palette while open): state icon (✅/❌/🛑/🔄), short id, start time, duration; paging; auto-refresh of the top entry while a run is live; empty state ("no executions yet")
- [ ] **Inspect a past execution** — selecting an entry enters **history mode** (a read-only variant of c.0's run mode — same editing guards): `GetStatusAsync(id)` → paint final per-node states through the existing `RunState`/overlay path (c.1 reuse — the snapshot-seeding code path IS this feature); right rail shows overall state, duration, error, **outputs** (pretty-printed JSON, collapsible), and the per-node state list with click-to-navigate
- [ ] **Live-to-history continuity** — when a live run (c.1) completes, it appears at the top of the history list; "view details" from the completion toast opens it in history mode
- [ ] **Re-run** — `[↻ Re-run]` starts a fresh execution with the same inputs (from the stored run-dialog inputs when available, else the execution record's inputs if present, else `{}` with the dialog shown) → jumps into live run mode
- [ ] **Deep link** — `/designer/{id}?execution={executionId}` opens directly in history mode (shareable failure links)
- [ ] **Exit** — `[✖ Back to edit]` clears the overlay and returns to edit mode, document untouched (same contract as run mode)

### Tests (target ~9): → `Workflow.Tests.UI/Components/ExecutionHistoryTests.cs` + `State/HistoryModeTests.cs`

- [ ] `History_ListsExecutions_PagedWithStates` · `History_Empty_ShowsEmptyState`
- [ ] `SelectExecution_PaintsFinalNodeStates` *(completed/failed/skipped mapping)*
- [ ] `SelectExecution_ShowsOutputsAndError` · `HistoryMode_DisablesEditing`
- [ ] `LiveRunCompletion_AppearsInHistoryList` · `ReRun_StartsExecution_WithRememberedInputs`
- [ ] `DeepLink_OpensHistoryMode` · `BackToEdit_ClearsOverlay_DocumentIntact`

---

## 3.3.c.3 Docs + Polish + Minimap + A11y/Perf Pass 📚✨

> **Purpose:** Close the phase: the lightweight minimap (Q6 compromise), user +
> architecture docs (incl. the React port checklist, D2), UX polish debt from a/b, and a
> basic accessibility/performance sweep.

**Complexity:** 🟢 Low-Medium

### Tasks

- [ ] **Lightweight minimap (Q6 compromise)** — `Minimap.razor`: corner overlay (bottom-right, collapsible) rendering **node bounds as scaled rectangles** + a viewport frame, driven by the same `CanvasGeometry` content-bounds math as Fit; click/drag on the minimap pans the main canvas; run-mode tints rectangles by node state (cheap — reuses the state classes); **no rendered thumbnails** (that's 3.3.P6); ~4 tests (`Minimap_RendersRectPerNode`, `Minimap_ViewportFrame_MatchesTransform`, `Minimap_Click_PansCanvas`, `Minimap_Collapse_Persists`)
- [ ] **`docs/designer.md` (user guide)** — getting started (run API + UI, auth token), screen tour matching mockups S1–S5, edit walkthrough (palette → connect → configure → save), run walkthrough (S3), **execution history review walkthrough (S5)**, keyboard shortcut table, troubleshooting (CORS, auth, hub blocked → polling fallback)
- [ ] **`docs/designer-architecture.md` (D2)** — the layering diagram (thin views / framework-free state / wire DTOs), the state-service catalog with behavioral contracts (pointing at the xUnit specs as the source of truth), **the React+TypeScript port checklist**: DTOs→TS types, state services→TS classes (spec-driven), views→React Flow-or-custom, auth/hub equivalents (`@microsoft/signalr`), what stays untouched (the entire backend)
- [ ] **Cross-links** — README doc index + `phases/README.md` breakout entry; `docs/rest-api.md` gets a "consumed by the designer" note; `docs/realtime.md` links the overlay as a reference client
- [ ] **Polish debt sweep** — pinch-zoom if deferred from a.3; edge hover hit-area widening (invisible fat stroke); node title ellipsis + tooltips; palette drag ghost fidelity; toast timing; empty-canvas onboarding hint ("drag a module here to start")
- [ ] **A11y basics** — keyboard focus order for toolbar/panels; `aria-label`s on icon buttons; visible focus rings from tokens.css; canvas interactions documented as pointer-only (full canvas a11y is out of MVP scope — note it)
- [ ] **Perf sanity** — 100-node synthetic document: initial render < 1s, pan/zoom smooth (transform-only invalidation verified), run-mode repaint per event < 16ms target; note findings in the architecture doc
- [ ] **Solution hygiene** — `dotnet build Workflow.sln` + full test suite green; UI projects build warnings triaged; `.gitignore` covers `Workflow.UI/**/bin|obj` build junk already committed *(clean up stray bin/obj artifacts if present in git)*

### Tests

- [ ] *(docs/polish slice — verified by the full a/b/c suites + a manual S1–S4 walkthrough against the live API)*

---

## Exit Criteria for 3.3.c ✅

- [ ] From the designer: ▶ Run executes the open workflow and the canvas animates live through to ✅/❌, matching mockup S3
- [ ] Late-join and reconnect both repaint correctly from the snapshot; hub-blocked environments degrade to polling with a visible notice
- [ ] Run mode never mutates the document; closing returns to a clean edit mode
- [ ] Past executions are reviewable: history list per workflow, final node states painted on the canvas, outputs/errors inspectable, re-run works (S5)
- [ ] The lightweight minimap navigates the canvas and tints with run state (Q6 compromise)
- [ ] `docs/designer.md` + `docs/designer-architecture.md` shipped with the React port checklist
- [ ] Full solution + test suite green; Phase 3.3 Success Criteria in the [master plan](Phase3-3-WorkflowDesigner.md#success-criteria-) all check off

---

*Made with 💖 by Ami-Chan! Watching the little boxes light up is the whole point~ UwU* ✨
