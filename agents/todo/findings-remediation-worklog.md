# Findings remediation — session worklog

> A standalone runbook for a fresh session to work through the confirmed architecture findings and
> track progress. You can start here with **no prior context**. Edit this file as you go: tick the
> checkboxes and append to the Progress log at the bottom.

**Created:** 2026-09-29 · **Owner:** rotating (whoever picks up the next item)

---

## 0. What this is

A set of real defects was found while documenting the engine, reproduced with tests, and planned.
Everything you need is already in the repo:

| Where | What |
| --- | --- |
| `docs/architecture/README.md` | the **findings table** (status, severity, link to each plan) — read first |
| `docs/architecture/plans/README.md` | the **plan index** + cross-cutting threads + suggested order |
| `docs/architecture/plans/0X-*.md` | one plan per subsystem: evidence, root cause, options, recommendation, acceptance, risk, effort |
| `docs/architecture/00`–`04` | the deep-dive docs each plan references (how the subsystem actually works) |
| `Workflow.Tests/Findings/` | the tests that **reproduce** each finding (`[Trait("Category","Finding")]`), plus its own README |

Each finding has a **guard test** that is **red today** and should go **green** when the finding is
fixed. That is the definition of done for the code change.

---

## 1. Orientation (do this once, at the start of a session)

- [ ] Read `docs/architecture/plans/README.md` (5 min) — the index and the four cross-cutting threads.
- [ ] Skim `docs/architecture/README.md#findings-at-a-glance` — the severity + status table.
- [ ] Run the finding tests to see the current baseline (expect mostly red):
  ```bash
  dotnet test Workflow.Tests --filter Category=Finding --logger "trx;LogFileName=findings.trx" --results-directory TestResults
  ```
  The `.trx` lands in `TestResults/findings.trx` — a clean per-test pass/fail list (ignore the Akka
  console noise).
- [ ] Confirm the **rest** of the suite is green before you change anything, so you have a clean base:
  ```bash
  dotnet test Workflow.Tests --filter Category!=Finding
  ```

---

## 2. The standard working loop (per finding)

Pick the next unchecked item from the Work queue (§3) and:

1. **Read its plan section** (`docs/architecture/plans/0X-…#finding-…`). Note the recommended option
   and any **decision to settle first** — if the plan says a design choice is needed, raise it before
   coding, don't guess.
2. **Run its guard test alone** and confirm it's red for the reason the plan describes:
   ```bash
   dotnet test Workflow.Tests --filter "FullyQualifiedName~<TestName>"
   ```
3. **Implement the fix** in the smallest change that matches the recommendation.
4. **Make the guard test green.**
5. **Run the full suite** (`--filter Category!=Finding`) — nothing may regress. Pay attention to the
   suites the plan's *Acceptance* section names.
6. **Retire the finding test**: either delete it, or drop its `[Trait("Category","Finding")]` so it
   becomes a permanent regression test (preferred when it's a good ongoing guard). Where the trait is
   class-level (e.g. `EngineFindingTests`), retiring means **moving** the test to a permanent class.
   - **Prove the new/moved tests bite.** Stash only the production change
     (`git stash push -- <prod files>`), run the new tests and confirm they go **red**, then
     `git stash pop` and confirm green. If the fix has several parts (e.g. a guard + an API change),
     stash each part separately so every test is shown to guard the part it claims to. A test that
     stays green without the fix isn't a regression test — rework it. Restore with `git stash pop`,
     not by copying a backup file: a copy keeps the old mtime and MSBuild silently reuses the stale
     build (touch the file and rebuild if you must copy).
7. **Update the status**: tick the boxes here, and remove/strike the row in
   `docs/architecture/README.md#findings-at-a-glance`.
8. **Commit** following the repo's existing commit conventions (look at recent `git log`). One finding
   per commit where practical; reference the finding id (e.g. `§12.9`) in the message.
9. **Append a Progress log entry** (§4).

> Guardrails: never weaken an existing passing test to make a change fit. If the fix needs a design
> decision the plan flagged, stop and get it decided. Keep each fix behind its guard test + the full
> suite.

---

## 3. Work queue (recommended order)

Dependencies: **§12.9 first** (blocks §10.3). **§12.1 before §9.3** (§9.3 reads the terminal events
§12.1 adds). **§12.4 + §10.3 + §9.5 share one "sub-graph parity" change** — do them together.

### Stage 1 — unblockers & high-visibility engine fixes

- [x] **§12.9 — loop `done`-port node is skipped** · plan 02 · guard `EngineFindingTests.LoopDonePort_ShouldRunPostLoopNode`
  - [x] plan read · [x] fix (`HandleLoopCompleted`: scope from `loopBody` targets only) · [x] guard green · [x] `WhileModule` path re-checked (by code review — same handler/port; no test yet, see log) · [x] suite green · [x] finding retired (→ `Engine/LoopCompletionTests`) · [x] README updated
- [x] **§12.5 — unregistered module id "succeeds" via stub** · plan 02 · guard `EngineFindingTests.UnregisteredModule_ShouldFailNode_NotSucceedViaStub`
  - [x] plan read · [x] fix (fail the node; reject unknown ids at validation) · [x] guard green · [x] stub-dependent tests updated · [x] suite green · [x] finding retired (→ `WorkflowExecutorTests.StartExecution_UnregisteredModule_ShouldFailNodeAndWorkflow`) · [x] README updated
- [x] **§12.1 — completion/node events never reach SignalR** · plan 02 · guards `RealtimeCompletionFindingTests.*` (2)
  - [x] plan read · [x] fix (publish terminal messages to EventStream; mind secret outputs re §10.6) · [x] guards green · [x] bridge/subscription tests green · [x] suite green · [x] findings retired (→ `Api/RealTime/RealtimeCompletionTests`) · [x] README updated

### Stage 2 — lifecycle & cancellation (same code area)

- [x] **§12.2 — executor actors never stop** · plan 02 · guard `EngineFindingTests.CompletedRun_ShouldStopExecutorActor`
  - [x] plan read · [x] fix (self-stop on terminal, after persistence acks; never reap paused) · [x] guard green · [x] status-after-stop falls back to history (add test) · [x] suite green · [x] finding retired (→ `Engine/ExecutorLifecycleTests`) · [x] README updated
- [x] **§12.3 — cancel doesn't stop running constructs** · plan 02 · guard `EngineFindingTests.Cancel_ShouldStopRunningLoopIterations`
  - [x] plan read · [x] fix (`_executionCts.Cancel()` on cancel path — `CooperativeCancel*` not needed, see log) · [x] guard green · [x] suite green · [x] finding retired (→ `HierarchicalCancellationTests`) · [x] README updated

### Stage 3 — sub-graph parity (do as ONE change)

> `SubGraphExecutor` lacks parity with `WorkflowExecutor`. Extract the shared behaviour (like
> `DispatchCore` was) so bodies get error handling AND variable propagation at once.

- [x] **§12.4 — body nodes ignore retry/continue** · plan 02 · guard `EngineFindingTests.RetryInsideLoopBody_ShouldBeHonoured`
- [x] **§10.3 — body variable writes not visible after loop** · plan 03 · guard `BindingFindingTests.VariableWriteInsideLoopBody_…` (needs §12.9 done first)
  - [x] decide scoping (visible after construct? iteration-local? per plan 03 open question) — decided, recorded in plan 03
- [ ] (optional) **§9.5 — no streaming regions inside bodies** · plan 04 (documented-only; add a test if tackled)
  - [x] plan read (02 §12.4 + 03 §10.3) · [x] extraction done · [x] both guards green · [x] `SubGraphExecutorTests`/`LoopScopeTests`/`ErrorBoundaryTests` green · [x] suite green · [x] findings retired (→ `Engine/SubGraphParityTests`) · [x] README updated · §9.5 not tackled (optional)

### Stage 4 — security (script sandbox)

- [x] **§9.1 — JS CLR interop reachable** · plan 01 · guards `ScriptSandboxFindingTests.JavaScript_*` (2)
  - [x] plan read · [x] fix (drop `AllowClr()`; `Interop.AllowGetType=false`) · [x] guards green · [x] **all** `Workflow.Tests/Scripting/*` still green (proves `workflow.*` unaffected) · [x] suite green · [x] findings retired (→ `JavaScriptScriptExecutorTests.Sandbox_*`) · [x] README updated
- [x] **§9.2 — C# reflection bypasses blocklist** · plan 01 · guard `ScriptSandboxFindingTests.CSharp_ShouldNotAllow_ReflectionTypeResolution`
  - [x] plan read · [x] mitigation in **both** compilers (semantic check + the name list as pre-filter) · [x] real fix — NOT a reference allowlist (can't work, see plan 01 "Decided design"): default-deny semantic check + reviewer approval of exact code · [x] guard green + mirror test for the LINQ compiler · [x] transform/linq suites green · [x] suite green · [x] finding retired (→ `Scripting/ScriptApprovalTests`) · [x] README updated

### Stage 5 — smaller, independent fixes

- [x] **§9.3 — active-exec count only drops for sync runs** · plan 05 · guard `ActiveExecutionCountFindingTests.AsyncRun_WhenComplete_…` (needs §12.1) — fixed, retired → `Api/V1/ExecutionMetricsTests`
- [ ] **§9.4 (modules) — ValidateConfiguration never called** · plan 05 · guard `ValidateConfigurationFindingTests.Validator_ShouldSurface_…`
  - [ ] audit built-ins' `ValidateConfiguration` first (don't start rejecting valid workflows)
- [ ] **§9.7 — package registers undeclared module ids** · plan 05 · guard `PackageExtraModulesFindingTests.Install_ShouldNotRegister_…`
  - [ ] decide single- vs multi-module manifests first
- [ ] **§9.4 (streaming) — pinned version ignored in region** · plan 04 · guard `StreamingFindingTests.PinnedModuleVersion_…`
- [ ] **§10.2 — input keys shadow declared variables** · plan 03 · guard `BindingFindingTests.InputKey_ShouldNotShadow_DeclaredVariable`
- [ ] **§10.1 — `{{nodeId.port}}` only reaches direct predecessors** · plan 03 · guard `BindingFindingTests.NonAdjacentNodeOutputReference_ShouldResolve`
  - [ ] note: recommended fix is **docs/UX** (keep behaviour, correct `docs/variables.md` + designer picker, flip the test to assert a clear error)

### Stage 6 — needs a decision before coding

- [ ] **§10.6 — secrets persisted verbatim** · plan 03 · guard `SecretPersistenceFindingTests.SecretValue_…`
  - [ ] settle the "what is secret" policy (plan 03 Finding D) · then redact at every sink (history in/out, variable store, logs, broadcast — incl. the exception text SignalR now sends in `ExecutionFailed`/`NodeFailed` since §12.1)
- [ ] **§9.6 — spill kept forever with no history** · plan 04 · guard `SpillCleanupFindingTests.Sweep_WithNoHistory_…`
  - [ ] pick B1 (fail-fast wiring) vs B2 (artefact age) vs B3 (accept + fix comment); if B1/B3, **delete** the guard test rather than making it pass
- [ ] **§12.7 — UseErrorHandler not implemented** · plan 02 · guard `EngineFindingTests.UseErrorHandler_…`
  - [ ] this is a **feature spec**, not a bug: agree the semantics (`ErrorNodeId` routing, `MaxConsecutiveErrors`) before implementing

### Follow-ups (future work, not findings)

- [ ] **Dedicated cancel event for real-time clients.** Since §12.1 a user cancel reaches SignalR as
  `ExecutionFailed` with "Workflow execution was cancelled" — clients can only tell it apart by text.
  Add an `ExecutionCancelled` client event (bridge mapping + hub contract), e.g. publish a distinct
  message or map terminal `ExecutionStateChanged → Cancelled`. Update `ExecutionEventBridgeTests`.
- [ ] **Node events from construct bodies.** Loop/branch body nodes run in `SubGraphExecutor`, which
  doesn't publish `NodeExecutionCompleted`/`Failed` (or `NodeStateChanged`/`NodeRetrying` for body
  retries), so clients see no per-node events inside bodies. Left out of Stage 3 to keep that change
  focused; `NodeFailureCore`'s `scheduleRetry` callback is the natural hook for retry events.
- [x] **Top-level run can complete while a retry is pending.** `WorkflowExecutor.IsWorkflowComplete`
  doesn't check `_pendingRetryCancellations`: if node A is in retry backoff (counted in
  `_failedNodes`) and sibling B finishes last, the run completes and A's retry is dropped. One-line
  fix (`&& _pendingRetryCancellations.Count == 0`) + a test. `SubGraphExecutor.IsComplete` already
  has the equivalent guard (added in Stage 3). Pre-existing; found while doing Stage 3.
  *Done 2026-10-10 — see Progress log.*
- [x] **Constructs nested inside a body (loop-in-loop, try/catch in a loop, …) — CONFIRMED, now
  finding §12.10 (High).** *Done 2026-10-11 with G1 — see Progress log.*
  - [x] decision (G1) · [x] step 1 pure refactor (`ConstructSpawnCore`) · [x] step 2 `SubGraphExecutor` spawns all four constructs · [x] guards green · [x] suite green · [x] finding retired (→ `Engine/NestedConstructTests`, 7 tests, all red without the fix) · [x] README updated
  
  Original note: Guard: `Findings/Engine/NestedConstructFindingTests` (2, both red on
  2026-10-10). Loop-in-loop runs the inner body 3× instead of 6× and the run still completes. A
  try/catch in a loop doesn't catch, so the run fails. Cause: `SubGraphExecutor` doesn't handle
  `Node*ExecutionRequested`, so the construct completes as a plain node and every outgoing edge
  fires once. Also (code reading, untested): break/continue inside an inner loop would hit the outer loop.
  Plan: [02 §G](../../docs/architecture/plans/02-engine-lifecycle-remediation.md#finding-g-high--a-construct-nested-inside-a-body-runs-as-a-plain-node-1210).
  **Decision needed** before coding: G1 (shared `ConstructSpawnCore`, `SubGraphExecutor` spawns
  nested constructs) vs G2 (reject at submit time as a stop-gap), plus the history/variable-scoping
  questions listed there. Plan 02's §12.4 acceptance item "try/catch nested in a loop body" is
  covered by this guard.
- [x] **Break/continue inside a try/catch (or parallel/transaction) inside a loop body.** *Done
  2026-10-11 — see Progress log.* Since
  §12.10 the nested try/catch runs its branches in its own sub-graph, and `TryCatchCompleted` /
  `ParallelCompleted` / `TransactionCompleted` carry no `BreakRequested` / `ContinueRequested`, so a
  `builtin.break` in a try branch no longer ends the enclosing loop. (Before §12.10 it did, but only
  because the try/catch ran as plain nodes and didn't catch.) Fix idea: carry the flags on the
  construct's completion and have `SubGraphExecutor` raise them in its own body (never across a
  nested *loop*). Add a test: foreach → trycatch → try: break.
- [x] **Nested constructs: history fidelity + transaction test.** *Done 2026-10-11.*
  - [x] history: body records carry `Metadata["scopePath"]` (every enclosing construct, outermost
    first) and `LoopId` / `LoopIteration` from the innermost loop. *Done 2026-10-11 — see Progress
    log.* Showing the full path in the API/UI is not done (only the innermost loop shows today).
  - [x] transaction nested in a loop body: structural SQLite tests (commit per iteration, rollback
    per iteration) + `loop#k/txn` history path. *Done 2026-10-11 — see Progress log.*
- [ ] **API test flake: SQLite "table VersionInfo already exists".** Seen in `RateLimitTests`,
  `ModuleManagementEndpointsTests`, `WebhookApiTests` (2 of 4 full runs on 2026-10-10, green on
  rerun). DB names are unique per factory (`TestSqlite.InMemory`), so suspect one host running
  migrations twice concurrently. Investigate the migration runner / hosted-service startup.

---

## 4. Refuted / keep as-is (do not "fix")

- **§12.6 join-skip** — refuted; the guard passed. `JoinSkipFindingTests` can be deleted (it was a
  false alarm).
- **Port-routing inside a loop body** — works; `EngineFindingTests.PortRoutingInsideLoopBody_…`
  passed. Keep it as a regression test (drop the `Finding` trait).

---

## 5. Done criteria for the whole effort

- [ ] Every Stage 1–5 finding fixed, its guard green or retired, and its README row removed.
- [ ] Stage 6 items either resolved or converted to tracked tickets with their decision recorded.
- [ ] `dotnet test Workflow.Tests` fully green.
- [ ] `docs/architecture/README.md#findings-at-a-glance` reflects reality (ideally empty or only
  parked design-decisions remain).

---

## 6. Progress log

Append one entry per working session. Keep it short: what you touched, test status, open decisions.

```
### 2026-11-08 — §9.3 metrics count every run's outcome
- Picked back up after a break: full suite green first (Workflow.Tests 1814/1814, Workflow.Tests.UI
  718/718), pushed feature/FSL (c18b134..83f55cc).
- Fix (plan 05 A1): hosted `ExecutionMetricsRecorder` listens to `WorkflowCompleted` /
  `WorkflowFailed` on the EventStream (published since §12.1) and records completed / failed /
  cancelled for every run. Started stays at submission; `ActorWorkflowLauncher` (webhooks) now
  records it too (it recorded nothing). Removed the wait path's own terminal recording — keeping
  it would double-count sync runs.
- Tests: `Api/V1/ExecutionMetricsTests` (6): async completes → active 0; sync counted once; webhook
  started + completed; failed; cancelled (not failed); hot-reload drain gate clears. 5/6 RED with
  the production files stashed; the sync-once test (green either way, old code counted sync once)
  was shown to catch a re-added wait-path count (completed = 2). Retired
  `Findings/Modules/ActiveExecutionCountFindingTests`.
- First full run caught a shutdown bug in the new recorder: hosted services stop in reverse
  registration order, so Akka can be gone before `StopAsync` → NRE in 8 API test teardowns.
  Now idempotent and skips stopping once the actor system has terminated.
- `--filter Category!=Finding`: 1820 passed, 0 failed. Next: §9.4 (modules) — audit built-ins'
  ValidateConfiguration before wiring it in.

### 2026-10-11 — §12.10 follow-up: break/continue through try/catch, parallel, transaction
- `TryCatchCompleted` / `ParallelCompleted` / `TransactionCompleted` gained optional
  `BreakRequested` / `ContinueRequested`. Each actor ORs the flags of its completed bodies (try/catch:
  any branch; parallel: any completed branch; transaction: the committed body, a rolled-back body has
  none). `SubGraphExecutor.OnConstructCompleted` raises them in its own body, so they reach the
  nearest enclosing loop; `LoopCompleted` carries none, so a break never crosses a nested loop.
  Semantics unchanged otherwise: flags only, the body still runs to the end. Top-level constructs
  (no enclosing loop) ignore them, as before.
- Tests: `Engine/NestedConstructTests` +5 (break in try, in catch, in a parallel branch, in a
  transaction body — each inside a loop — and break in a try inside an inner loop ends only the inner
  loop). All 5 RED before the change. Stashed per part: try/catch actor → the 3 try/catch tests red;
  parallel → its test red; transaction → its test red; `SubGraphExecutor` → all 5 red.
- Continue has no test: the loop treats `ContinueRequested` the same as a normal iteration end
  today, so it can't be observed. The flag is carried for when it can.
- Suite: (`Category!=Finding`) 1814 passed, 0 failed.

### 2026-10-11 — §12.10 follow-up: nested transaction tests
- `DatabaseTransactionStructuralEngineTests`: `TransactionNestedInLoopBody_CommitsPerIteration`
  (3 rows, committed ×3, rolledBack ×0) and `…_RollsBackPerIteration_AndRunCompletes` (0 rows,
  rolledBack ×2, run completes). Both RED with `SubGraphExecutor`'s nested-construct start disabled
  (`TryStart` short-circuited), green with it. The first version of the commit test stayed green
  without the fix (autocommit + every edge firing looks like a commit), hence the rolledBack ×0 check.
- `NestedConstructHistoryTests.TransactionInLoop_BodyRowsCarryLoopIterationAndTransaction`: RED
  without the transaction actor's scope step. No production changes.
- Suite: (`Category!=Finding`) 1809 passed, 0 failed.

### 2026-10-11 — §12.10 follow-up: scope path on nested body history
- New `Workflow.Engine/Models/ScopeStep` (`nodeId`, `kind`, `iteration` / `branchIndex` / `branch`).
  `ConstructSpawnCore` takes a `scopePath` delegate (`WorkflowExecutor`: empty; `SubGraphExecutor`:
  its own). The four construct actors take the parent path as an optional Props param, add their
  step and pass it to `SubGraphExecutor.Props` (new optional `scopePath`).
- `SubGraphExecutor.QueuePersistNode` writes `Metadata["scopePath"]` and `LoopId` / `LoopIteration`
  from the innermost loop step. `subGraphId` unchanged; no migration.
- Tests: `Engine/NestedConstructHistoryTests` (4: loop, loop-in-loop 6 pairs, try/catch-in-loop catch
  path, parallel-in-loop branch path). All 4 RED with the production files stashed, green with them.
  Suite (`Category!=Finding`): 1806 passed, 0 failed.
- Open: API/UI for the full path; body node events (separate follow-up); nested transaction test.

### 2026-10-11 — §12.10 nested constructs (G1)
- Step 1 (commit b8a8143): `ConstructSpawnCore` extracted from `WorkflowExecutor` (request maps,
  Spawn*/On*Completed, running-construct tracking; state stays in the actor via callbacks, like
  `NodeFailureCore`). Pure refactor: `Category!=Finding` 1795 passed, 0 failed, no test changes.
- Step 2 (commit 3ed7deb): `SubGraphExecutor` handles `Node*ExecutionRequested`, starts the construct
  in `HandleNodeCompleted` (`TryStart`), handles `*Completed` (apply `VariableUpdates`, done-port
  routing) and `*Failed` / unexpected `Terminated` (through `NodeFailureCore` / `ReportFailure`).
  `IsComplete` waits for running constructs, and a `_finished` guard ignores late reports. All four
  constructs are covered (plan stages 2+3 together). Suite: 1795 passed, 0 failed.
- Guards retired → `Engine/NestedConstructTests` (7: loop-in-loop, try/catch-in-loop,
  parallel-in-loop with done-after-branches ordering, loop-in-try, break-in-inner, failing inner
  loop caught by outer try/catch, recursive variable propagation). All 7 RED with
  `SubGraphExecutor` reverted to pre-fix, green with it.
- History fidelity / variable scoping: took the plan's defaults (same fidelity as one body level;
  Stage 3 rules recursively), recorded in plan 02 §G Status. Follow-ups added: break/continue in a
  try/catch inside a loop (regressed by construction), history fidelity confirmation, nested
  transaction test.

### 2026-10-11 — §9.2 C# sensitive-API check + reviewer approval
- Plan 01's real fix (reference allowlist) can't work: Type, MethodInfo, Assembly, Activator and
  File are all in System.Runtime.dll. Decided with the user instead: default-deny semantic check +
  approval of exact code by a new ScriptApprover role (author can't self-approve), everything
  reviewable, every flag explained in the UI. Design recorded in plan 01 "Decided design".
- Server: `Workflow.Scripting.Roslyn/Security` (SensitiveApiCatalog: 8 categories with danger text,
  symbol classifier; ScriptSecurityAnalyzer: walks the user body's bound symbols, name list as
  pre-filter, P/Invoke attributes, `dynamic`; CodeHash = SHA-256(kind + normalized code)).
  Both RoslynScriptCompiler (scripts + transforms) and WorkflowLinqCompiler deny flagged code
  unless `IScriptApprovalGate.IsApproved(hash)`; denied compiles record a Candidate. Fail closed
  when no gate. Old `WFSCRIPT100`/`WFLINQ100` ids kept for denials, so existing tests unchanged;
  unsafe/pointer/stackalloc stay hard errors. `Workflow.Modules.Database.Linq` now references
  `Workflow.Scripting.Roslyn` (shared checker).
- Store: `Workflow.Scripting/Approvals` (ScriptApprovalStore, JSON catalog over the blob store like
  script libraries; candidates capped at 500). API: role `ScriptApprover`, policies
  `ScriptApprove` / `ScriptApprovalRead`, `/api/v1/script-approvals` (list/categories/get/request/
  approve/reject); flags + hash on scripts/test, transform validate/compile, LINQ validate/compile.
- UI: SecurityReviewPanel (Script Studio test runner, LINQ Studio validate), Script Approvals page,
  🛡️ Approvals nav button.
- Tests: `Scripting/ScriptApprovalTests` (29: spellings incl. global::/typeof/dynamic, IO/Net/
  Process via reflection, categories, positions, exact-code approval, kind in hash, self-approval,
  state machine, persistence, LINQ mirror), `Api/V1/ScriptApprovalApiTests` (4, real HTTP + roles),
  `Tests.UI/.../ScriptApprovalUiTests` (6). Mutation check (gate forced open): 26/34 new tests RED.
  Retired `Findings/Modules/ScriptSandboxFindingTests` (its probe is in ScriptApprovalTests).
- Built in a separate worktree (the uncommitted G1 work in the main checkout untouched).
  `--filter Category!=Finding`: 1795 passed, 0 failed; Workflow.Tests.UI: 718 passed, 0 failed.
- Follow-ups: approvals can't be revoked (compiled-bytes cache); designer has no inline compile,
  so node scripts are reviewed via the Studios / by running. Next: Stage 5, or resume G1.

### 2026-10-11 — §9.1 JS CLR interop off
- `JavaScriptScriptExecutor`: removed `opts.AllowClr()`, added `opts.Interop.AllowGetType = false`
  (plan 01 Option A + belt). `__api` (SetValue'd) still binds — governed by Interop, not AllowClr.
- Guards moved to `Scripting/JavaScriptScriptExecutorTests` (`Sandbox_ClrNamespaceRoot_IsNotInScope`,
  `Sandbox_ClrStaticCall_Fails`) + positive `Sandbox_WorkflowApi_StillWorks_WithoutClrInterop`
  (workflow.newGuid; get/setVariable already covered). Both guards RED with the fix stashed.
  All 68 `Workflow.Tests/Scripting/*` tests green.
- Decision recorded (memorizer d39ba449): §12.10 nested constructs → **G1** (ConstructSpawnCore).
  Uncommitted G1 work-in-progress (ConstructSpawnCore.cs, WorkflowExecutor.cs, SubGraphExecutor.cs)
  was in the shared checkout and NOT part of this commit; full suite therefore run in a clean
  worktree (HEAD + §9.1 only): 1762 passed, 0 failed.
- Next: §9.2 (C# reflection bypasses blocklist), or resume the G1 work in progress.

### 2026-10-10 — test helper: ConfigurableFailureModule honours failCount
- `ConfigurableFailureModule` now reads `failCount` when it arrives as a numeric `JsonElement`
  (previously always fell back to the default of 2).
- Reviewed every `test.configurable-failure` user (all in `SupervisionStrategyTests`):
  `WithRetryPolicy_ShouldRetryAndSucceed` (2 of 3 attempts fail) and `RetryPending_…` (2, expects 3
  calls) unchanged; `RetryExhausted_ShouldFailWorkflow` (999, MaxAttempts 2) still exhausts;
  `NodeRetry_ShouldPublishNodeRetryingEvent` (1) still fails once, so a retry event still fires.
  No assertions changed.
- `--filter Category!=Finding`: 1759 passed, 0 failed.

### 2026-10-10 — follow-up: run completing while a retry is pending
- `WorkflowExecutor.IsWorkflowComplete` now also requires `_pendingRetryCancellations.Count == 0`
  (mirrors `SubGraphExecutor.IsComplete`). `HandleRetryNode` removes the entry before re-executing,
  so the retried node's completion still ends the run.
- Test: `SupervisionStrategyTests.RetryPending_WhenSiblingFinishesDuringBackoff_ShouldNotCompleteUntilRetryRuns`
  (A retries with 300ms backoff, sibling B finishes at ~100ms). Red without the fix (completed
  after 1 call to A), green with it.
- Side note: `ConfigurableFailureModule` ignores the `failCount` property (it arrives as a
  `JsonElement`, so the default of 2 is always used); the new test sets 2 explicitly to match.
- `--filter Category!=Finding`: 1759 passed, 0 failed (one earlier run had 2 API-test failures,
  green in isolation and on rerun — likely the known API flake).

### 2026-10-10 — Stage 3: sub-graph parity (§12.4 + §10.3)
- Scoping decided (asked): sequential bodies see earlier writes and report them after the
  construct; parallel branches isolated, merged in branch order; writes before a failure kept.
  Plan 03's "iteration-local" recommendation was revised — it broke WhileModule's documented
  variable-driven while. Recorded in plan 03's open question.
- §12.4: extracted `NodeFailureCore` (boundary → retry / continue / fail) from
  `WorkflowExecutor.HandleNodeFailure`, DispatchCore-style (state stays in the actor). Pure
  refactor first: full suite green before touching sub-graphs. `SubGraphExecutor` now uses it
  (no boundary stack → its owning try/catch still sees a failure), with its own retry scheduling;
  `IsComplete` waits for pending retries.
- §10.3: new `BodyVariables` helper. `SubGraphExecutor` passes variables to body nodes
  (`Execute.Variables`), applies their writes, reports them on `SubGraphCompleted/Failed`
  (new optional `VariableUpdates` on all 10 construct messages). Loop carries writes into later
  iterations + the while condition context; parallel merges per-branch writes by index;
  try/catch and transaction run phases sequentially. `WorkflowExecutor` hands constructs the
  current variables and applies their reported writes via the same path as a node
  (`ApplyVariableUpdates` → context + `QueuePersistVariableUpdates`).
- Tests: `Engine/SubGraphParityTests` (8): retry honoured / exhausted, continue, write visible
  after loop, later iterations see earlier writes (0,1,2 → 3), variable-driven `while` terminates
  (the test §12.9 deferred), parallel isolation + branch-order merge, try/catch writes before
  failure. All 8 RED with the engine changes stashed. Retired both guards.
- Follow-ups added: node events from bodies (not folded in after all), top-level
  complete-while-retry-pending race (pre-existing), nested constructs in bodies (unverified).
- Flake, unrelated to the engine: API tests intermittently hit SQLite "table VersionInfo already
  exists" (2 of the last 4 runs, different tests; green on rerun). First seen after §12.2 added
  another WebApplicationFactory class — likely more parallel load exposing an existing migration
  race. Added as a follow-up.
- `--filter Category!=Finding`: 1758 passed, 0 failed. Next: Stage 4 (§9.1 JS CLR interop).

### 2026-10-10 — §12.3 cancel stops running constructs
- Cancel path now calls `_executionCts.Cancel()` (like Complete/FailWorkflow). Every construct
  actor links its token to it, so loops/parallels/try-catch/transactions/stream regions stop
  cooperatively. Did NOT send `CooperativeCancel*` (they only re-cancel the same linked token) and
  did NOT `Context.Stop` constructs (a transaction must still roll back).
- Found while fixing: after a terminal state, late construct/node replies still mutated the run —
  a cancelled loop's `LoopFailed` turned Cancelled → Failed (and a late `LoopCompleted` could start
  post-loop nodes). Added `DropIfTerminal` at the top of the node/construct result handlers,
  `HandleNodeFailure` and `HandleRetryNode` (applies to Completed/Failed/Cancelled).
- Tests in `Engine/HierarchicalCancellationTests`: cancel stops loop iterations (retired guard),
  cancelled run stays Cancelled with one terminal message, done-port node doesn't run after cancel.
  Iteration + done-port tests RED with the fix stashed; the stays-Cancelled test RED with only the
  terminal guard disabled.
- `--filter Category!=Finding`: 1750 passed, 0 failed. One unrelated flake seen once:
  `HttpRetryTests.Retry_ExponentialBackoff_DelaysIncrease` measured exactly 150ms vs `> 150` (module-only
  timing test, pre-existing). Stage 2 done. Next: Stage 3 sub-graph parity
  (§12.4 + §10.3 [+ §9.5]) — §10.3 needs the scoping decision first.

### 2026-10-10 — §12.2 finished executors stop
- First cut (stop as soon as persistence drains) broke 69 actor-level tests that query a live
  executor's status after the run ends. Asked → agreed: drain + grace period, and fill node states
  in the API history fallback.
- `WorkflowExecutor`: counts in-flight persistence writes (create record, node record, variable
  updates, status update, snapshot); on Completed/Failed/Cancelled `StopWhenPersisted()` stops Self
  once writes are acked AND `ExecutorLifecycleOptions.TerminalGracePeriod` (new, optional in DI,
  default 60s) has passed; fallback stop after grace + `PersistenceAckTimeout` (30s). Paused never
  stops. Supervisor already removed entries on `Terminated`.
- API `GetStatusAsync` history fallback now fills `nodeStates` from `GetNodeExecutionsAsync`
  (latest record per node).
- Tests: `Engine/ExecutorLifecycleTests` (zero-grace completed/failed stop = retired guard; live
  status during grace then stop; paused not stopped), `Api/V1/ExecutionStatusAfterStopTests`
  (zero-grace API: status after stop = history + node states). Stop tests RED with fix stashed;
  API test RED with only the API change stashed. Existing 69 untouched.
- Directory.Build.props (trailing blank line) included in this commit, per request.
- `--filter Category!=Finding`: 1747 passed, 0 failed. Next: §12.3 (cancel doesn't stop running constructs).

### 2026-10-10 — §12.1 completion events reach SignalR
- Decisions (asked + agreed): publish SLIM copies to the EventStream (outputs / partial outputs /
  variable updates stripped) so nothing possibly-secret goes on the stream; forward exception text
  to clients as-is (bridge's existing contract) and track it under §10.6.
- `WorkflowExecutor.PublishTerminal`: `WorkflowCompleted` (CompleteWorkflow), `WorkflowFailed`
  (FailWorkflow + cancel path), `NodeExecutionCompleted` (HandleNodeExecutionCompleted),
  `NodeExecutionFailed` (HandleNodeFailure — the shared node→Failed choke point, so every failed
  attempt is reported, matching history + NodeStateChanged). Bridge unchanged. Only the supervisor
  spawns executors, so no nested-run double events.
- Guards moved to permanent `Api/RealTime/RealtimeCompletionTests`; new
  `Engine/EventStreamPublishTests` pins the slim payloads (parent still gets full outputs). All 4
  verified RED with the fix stashed.
- Cancel shows as `ExecutionFailed` for now → follow-up added (dedicated cancel event).
- `--filter Category!=Finding`: 1742 passed, 0 failed. Next: §12.2 (executor actors never stop).

### 2026-10-10 — §12.5 unregistered module id fails
- D1: `NodeExecutor` sends `NodeExecutionFailed` ("Module 'x' is not registered.") instead of
  `ExecuteStubFallback` (removed). D2: `WorkflowSupervisor` rejects unknown module ids at submit
  (`Status.Failure`, "validation failed"), only when an `IModuleRegistry` is registered. Deliberately
  ONLY the MA001 unknown-id check — not the full `ModuleAwareWorkflowValidator` (MA002–004 would
  start rejecting unknown property keys / target ports at run time; out of scope).
- Stub-dependent tests (18): fixtures in WorkflowExecutorTests, ExecutionStateTrackingTests,
  WorkflowSupervisorTests now register `StubModule("test.module")`; PersistenceIntegrationTests'
  `missing.module` → registered `test.pass`. Assertions unchanged. `NodeExecutorTests.
  Execute_WithUnregisteredModule_ShouldUseFallbackStub` asserted the bug itself → inverted to
  `..._ShouldFailNode`. Note: WorkflowSupervisorTests.GetWorkflowStatus_ForExistingWorkflow only
  failed on some runs (raced Running vs Failed) — same root cause, same fixture fix.
- New: `WorkflowExecutorTests.StartExecution_UnregisteredModule_ShouldFailNodeAndWorkflow` (retired
  guard), `WorkflowSupervisorTests.CreateWorkflowInstance_WithUnknownModuleId_ShouldReturnFailure`.
  All 3 verified RED with the fix stashed.
- `--filter Category!=Finding`: 1738 passed, 0 failed (green on 3 consecutive runs).
- Next: §12.1 (completion/node events → SignalR; mind secret outputs re §10.6).

### 2026-10-10 — §12.9 closed out
- Baseline commit 4953dc2 (plans, finding tests, worklog, README table, MessagePack 3.1.11 bump).
- Guard moved out of `EngineFindingTests` into permanent `Workflow.Tests/Engine/LoopCompletionTests.cs`
  (own stubs, no FindingDefs dependency) + plan 02 C2 acceptance e2e test
  `ForEachWithBodyAndDoneNode_DoneNodeContributesToWorkflowOutputs` (body runs 3×, done node's
  `after.summary` lands in `WorkflowCompleted.Outputs`). Both verified RED with the fix stashed.
- README §12.9 row struck. `--filter Category!=Finding`: 1736 passed, 0 failed.
- Still open: `while` + done-port e2e test → after §10.3 (needs body variable writes).
- Next: §12.5.

### 2026-10-10 — §12.9 loop done-port (fix in, not yet committed)
- Fix: `WorkflowExecutor.HandleLoopCompleted` now seeds `ComputeBodyScope` from body-port targets
  only (matches `SpawnLoopExecutor`). `LoopCompleted` gained `LoopBodyPort` (default "loopBody"),
  filled by `LoopExecutorActor` from `_loop.LoopBodyPort`, so custom body ports keep working.
  Files: WorkflowExecutor.cs, LoopExecutorActor.cs, Messages/LoopMessages.cs.
- Tests: guard `LoopDonePort_ShouldRunPostLoopNode` green; `--filter Category!=Finding` all green.
- WhileModule: same completion path, covered by code review. A `while` + done-port end-to-end test
  needs body variable writes to terminate the loop → add it after §10.3.
- Remaining for §12.9: move the guard out of `EngineFindingTests` (class-level Finding trait) into a
  permanent loop test; add the plan's acceptance e2e test (foreach body + done node contributing to
  workflow outputs); strike the §12.9 row in docs/architecture/README.md; commit referencing §12.9.
- Open: commit plans/finding tests/worklog (+ README edit) as a baseline commit first, then §12.9.
- Next after §12.9: §12.5 (unregistered module id succeeds via stub).

### 2026-09-29 — baseline
- Findings reproduced: 17 confirmed red, 2 refuted (join-skip, body port-routing).
- Plans written (docs/architecture/plans/01–05). No production code changed yet.
- Next: §12.9 (loop done-port) — it blocks §10.3 and is a small localized fix.
```

<!-- add new entries above this line, newest first -->
