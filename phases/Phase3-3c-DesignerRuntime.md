# Phase 3.3.c: Designer Runtime (Week 30) ▶️

Made with 💖 by Ami-Chan! UwU ✨

[Master plan: Phase3-3-WorkflowDesigner.md](Phase3-3-WorkflowDesigner.md) | [Prev: 3.3.b Editing](Phase3-3b-DesignerEditing.md)

> **Scope:** Running workflows from the designer and watching them live: the ▶ Run flow,
> the real-time execution overlay driven by the Phase 3.2 hub (mockup **S3**), a run
> log/history side pane, plus the docs + polish pass that closes the phase. **No new
> backend** — everything rides `POST /execute`, `GET /executions/{id}`, and
> `/hubs/workflow` as shipped (D8).

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

## 3.3.c.2 Docs + Polish + A11y/Perf Pass 📚✨

> **Purpose:** Close the phase: user + architecture docs (incl. the React port checklist,
> D2), UX polish debt from a/b, and a basic accessibility/performance sweep.

**Complexity:** 🟢 Low-Medium

### Tasks

- [ ] **`docs/designer.md` (user guide)** — getting started (run API + UI, auth token), screen tour matching mockups S1–S4, edit walkthrough (palette → connect → configure → save), run walkthrough (S3), keyboard shortcut table, troubleshooting (CORS, auth, hub blocked → polling fallback)
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
- [ ] `docs/designer.md` + `docs/designer-architecture.md` shipped with the React port checklist
- [ ] Full solution + test suite green; Phase 3.3 Success Criteria in the [master plan](Phase3-3-WorkflowDesigner.md#success-criteria-) all check off

---

*Made with 💖 by Ami-Chan! Watching the little boxes light up is the whole point~ UwU* ✨
