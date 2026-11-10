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

- [ ] **Search memorizer** for the area you're about to touch (DotFlow workspace — ids and rules in
  the repo-root `CLAUDE.md`, "Long-term memory"). Engine invariants, gotchas and past decisions live
  there, not in this file.
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
10. **Update memorizer** (see `CLAUDE.md` → "Long-term memory"): store or update any rule, gotcha or
    user decision from this finding that a future session would need and couldn't get quickly from
    the code or this log. Search first — edit an existing memory rather than adding a near-duplicate.
    If memorizer is unreachable, list the memories under a "Memories to store" heading in your
    progress-log entry and store them later.

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
- [x] ~~(optional) **§9.5 — no streaming regions inside bodies**~~ · moved to Stage 7 (7.7)
  - [x] plan read (02 §12.4 + 03 §10.3) · [x] extraction done · [x] both guards green · [x] `SubGraphExecutorTests`/`LoopScopeTests`/`ErrorBoundaryTests` green · [x] suite green · [x] findings retired (→ `Engine/SubGraphParityTests`) · [x] README updated · §9.5 not tackled (optional)

### Stage 4 — security (script sandbox)

- [x] **§9.1 — JS CLR interop reachable** · plan 01 · guards `ScriptSandboxFindingTests.JavaScript_*` (2)
  - [x] plan read · [x] fix (drop `AllowClr()`; `Interop.AllowGetType=false`) · [x] guards green · [x] **all** `Workflow.Tests/Scripting/*` still green (proves `workflow.*` unaffected) · [x] suite green · [x] findings retired (→ `JavaScriptScriptExecutorTests.Sandbox_*`) · [x] README updated
- [x] **§9.2 — C# reflection bypasses blocklist** · plan 01 · guard `ScriptSandboxFindingTests.CSharp_ShouldNotAllow_ReflectionTypeResolution`
  - [x] plan read · [x] mitigation in **both** compilers (semantic check + the name list as pre-filter) · [x] real fix — NOT a reference allowlist (can't work, see plan 01 "Decided design"): default-deny semantic check + reviewer approval of exact code · [x] guard green + mirror test for the LINQ compiler · [x] transform/linq suites green · [x] suite green · [x] finding retired (→ `Scripting/ScriptApprovalTests`) · [x] README updated

### Stage 5 — smaller, independent fixes

- [x] **§9.3 — active-exec count only drops for sync runs** · plan 05 · guard `ActiveExecutionCountFindingTests.AsyncRun_WhenComplete_…` (needs §12.1) — fixed, retired → `Api/V1/ExecutionMetricsTests`
- [x] **§9.4 (modules) — ValidateConfiguration never called** · plan 05 · guard `ValidateConfigurationFindingTests.Validator_ShouldSurface_…` — fixed, retired → `Modules/ModuleConfigurationValidationTests`
  - [x] audit built-ins' `ValidateConfiguration` first (don't start rejecting valid workflows) — see plan 05 "Audit"
- [x] **§9.7 — package registers undeclared module ids** · plan 05 · guard `PackageExtraModulesFindingTests.Install_ShouldNotRegister_…` — fixed, retired → `Api/V1/MultiModulePackageTests`
  - [x] decide single- vs multi-module manifests first — multi-module (`modules` list), see plan 05
- [x] **§9.4 (streaming) — pinned version ignored in region** · plan 04 · guard `StreamingFindingTests.PinnedModuleVersion_…` — fixed, retired → `Engine/Streaming/StreamRegionPinnedVersionTests`
- [x] **§10.2 — input keys shadow declared variables** · plan 03 · guard `BindingFindingTests.InputKey_ShouldNotShadow_DeclaredVariable` — fixed, retired → `Engine/VariableShadowingTests` (+ designer clash fixes, see plan 03)
- [x] **§10.1 — `{{nodeId.port}}` only reaches direct predecessors** · plan 03 · guard `BindingFindingTests.NonAdjacentNodeOutputReference_ShouldResolve` — fixed, retired → `Engine/UpstreamReferenceTests`
  - [x] ~~note: recommended fix is **docs/UX**~~ — user chose **A2 across bodies** instead (any finished ancestor, bodies included); see plan 03

### Stage 6 — needs a decision before coding

- [x] **§10.6 — secrets persisted verbatim** · plan 03 · guard `SecretPersistenceFindingTests.SecretValue_…` — fixed, retired → `Engine/SecretRedactionTests` + `Engine/SecretRedactorTests`
  - [x] settle the "what is secret" policy — declared `IsSecret`, redacted by **name and value**; at-rest storage out of scope (pivot options in plan 03)
- [x] **§9.6 — spill kept forever with no history** · plan 04 · guard `SpillCleanupFindingTests.Sweep_WithNoHistory_…` — fixed (B2), retired → `Streaming/SpillAgeRetentionTests`
  - [x] pick B1 / B2 / B3 — **B2** (artefact age) + startup warning; B1/B3 kept on file in plan 04
- [x] **§12.7 — UseErrorHandler not implemented** · plan 02 · guard `EngineFindingTests.UseErrorHandler_…` — fixed, retired → `Engine/ErrorHandlerTests`
  - [x] agree the semantics — catch-style handler, retry first, handler-only nodes, handler path continues, designer support (plan 02, `docs/error-handling.md`)

### Stage 7 — documented-only findings (no guard test yet)

The rest of the [findings table](../../docs/architecture/README.md#findings-at-a-glance). These came
from reading the code and were never reproduced by a finding test, so each one needs a failing
test first (§2 loop). Tracked here so nothing in the table is untracked (§5).

| # | Finding | Sev. | Plan | Decision needed? | Size |
| --- | --- | :-: | --- | --- | --- |
| 7.1 | ✅ ~~**§9.7 (streaming) `Blob` spill can resolve to the in-memory blob store**~~ — **fixed 2026-11-08**, `Streaming/BlobSpillDurabilityTests` | Medium | plan 04 "Documented-only" | Small: fail fast at startup vs. require a durable persistence provider (plan leans fail-fast) | ~0.5 d |
| 7.2 | ✅ **fixed 2026-11-08** (conditional edges, `docs/conditional-edges.md`) — ~~**`ConnectionDefinition.Condition` / `Priority` are never read**~~, but the designer lets you set a condition (edge label), so it looks like it works | Low (UX: Medium) | 02 data-binding §10, item 4 | **Yes**: implement conditional edges (expression on the edge, evaluated after the source completes) vs. remove the field and its designer UI | 1–2 d |
| 7.3 | ✅ **fixed 2026-11-09** (outbound HTTP policy, plan 06, `docs/outbound-http.md`) — ~~**§9.6 (modules) script HTTP has no host allowlist (SSRF)**~~ | Medium | plan 01 "Related" | **Yes**: allowlist shape (`ScriptHostCeilings` hosts / CIDR, default deny vs. allow), private-range blocking | ~1–2 d |
| 7.4 | ✅ **fixed 2026-11-09** (signed packages, plan 07, `docs/module-signing.md`) — ~~**§9.5 (modules) strong-name tokens aren't authorship**: `RequireSigned` is satisfied by copying a public key~~ | Medium | plan 01 "Related" / 2.8.P2 | **Yes**: Authenticode / trusted-publisher design (bigger) vs. docs-only downgrade of what `RequireSigned` means | 2+ d |
| 7.5 | ✅ **fixed 2026-11-09** (opt-in stall + overall limits, server default + per-region override) — ~~**§9.2 (streaming) regions have no timeout**~~ | Low | plan 04 | **Yes**: default (off vs. generous) and where it's set | ~0.5–1 d |
| 7.6 | ✅ **fixed 2026-11-09** (live + final `StreamProgress`, run-mode badges, execution detail table) — ~~**§9.1 (streaming) `StreamRegionProgress` not forwarded to SignalR**~~ | Low | plan 04 / Phase 5.1.5 | Client event contract (ties to the "node events from bodies" follow-up) | ~1 d |
| 7.7 | ✅ **fixed 2026-11-09** (`SubGraphExecutor` runs regions wholly inside a body) — ~~**§9.5 (streaming) no regions inside construct bodies**~~ | Low | plan 04 | No (parity work), but low priority | 1–2 d |
| 7.8 | **§9.3 (streaming) mid-pipeline retry**: the feared stall **doesn't happen** (a failure blames the source, so retry re-runs the region); only stage-level blame is missing | Low | plan 04 | Not urgent | — |
| 7.9 | ✅ **fixed 2026-11-09** (binding runs in the node's execution task) — ~~**Expression evaluation blocks the `NodeExecutor` thread** (`.GetAwaiter().GetResult()`)~~ | Low | 02 data-binding §10, item 5 | No | ~0.5 d |
| 7.10 | **Scheduled and Event triggers have no runtime** | Info | 01 §12.8 | Feature, not a fix: out of scope for this effort | — |

### Follow-ups (future work, not findings)

- [x] ✅ **done 2026-11-09** (see the "Async binding" progress-log entry) —
  **Async binding (`IPropertyBinder` → async).** After Stage 7.9, binding no longer runs on the
  node's actor thread. It still waits synchronously on the expression evaluator, though, now on a
  pool thread.
  - Edge conditions (`EdgeRouting.Fired` → `EvaluateCondition`, Stage 7.2) are evaluated on the
    **WorkflowExecutor / SubGraphExecutor** thread when the source node completes.
  - Both are bounded by Jint's 250 ms timeout.
  - An async binder (`BindPropertiesAsync` / `EvaluateConditionAsync`), plus routing that pipes the
    condition result back to the executor, would remove both waits.
  - It touches the binder interface, modules that call the binder, and both executors' completion
    paths.

- [ ] **Flaky test `StreamRegionExecutionTests.Region_PublishesLiveCounts_WhileItRuns_ThenTheFinalSummary`
  (Stage 7.6).** It failed once in a full `Workflow.Tests` run during the Async binding work, then
  passed in the next full run and 5/5 alone. The failure message wasn't captured. It's timing-based
  (a 25 ms interval against 8 items × 150 ms), so it probably flakes under load. Capture the message
  if it recurs.
  *2026-11-09:* not reproduced in 25 back-to-back runs of the streaming test classes. Left open.
  Capture the message next time.
- [x] ✅ **hardened 2026-11-09** (click and check inside `WaitForAssertion`; 10/10, and still fails when Open does nothing) — **Flaky UI test `MonitorTests.Monitor_RowOpen_NavigatesToDetail`.** Failed once in a full
  UI run during Stage 7.2 ("nav.Uri … is too short") and passed on the re-run and alone. The
  navigation assertion probably races the click handler. Unrelated to 7.2.
- [ ] **Rare test-host crash in `StreamRegionRunnerTests`.** Once during the §12.7 run:
  `ArgumentOutOfRangeException: An unexpected state object was encountered` from
  `CountingSource.ExecuteStreamAsync` (an async-iterator stub), which aborted the test run. It
  didn't reproduce on a re-run. Likely the stub's enumerator is used concurrently. Pre-existing and
  unrelated to §12.7.
  *2026-11-09:* not reproduced in 25 runs.
  - The exception comes from the compiler-generated async iterator's `ManualResetValueTaskSourceCore`
    when `MoveNextAsync` / `DisposeAsync` overlap. The runner wraps each module's
    `IAsyncEnumerable` in Akka's `Source.From(() => …)`, so the likely culprit is Akka.Streams'
    async-enumerable source disposing the enumerator while a `MoveNextAsync` is still pending
    during teardown (cancel or failure).
  - If it recurs, or shows up in production: adapt module streams through a `Channel<T>` pump owned
    by the runner (one reader, awaits the pending move before disposing) instead of
    `Source.From(IAsyncEnumerable)`.
- [x] ✅ **done 2026-11-09** (records written before the inputs are dropped) — **Completed body nodes have no inputs in history.** `SubGraphExecutor.HandleNodeCompleted`
  removes `_nodeInputs[nodeId]` before `QueuePersistNode`, so completed records from loop / branch /
  try bodies are written with `Inputs = null` (failed ones keep theirs). Top-level records keep
  inputs. Pre-existing; found during §10.6.
- [ ] **Secrets at rest.** "Save for future runs" (workflow/global variable store) and the state
  snapshots still hold secret values in plain text. This is pivot option "names + values, plus at
  rest" in plan 03 Finding D, and it overlaps the secrets-storage plan.

- [x] ✅ **done 2026-11-09** (`WorkflowFailed.Cancelled` → `ExecutionCancelled`) — **Dedicated cancel event for real-time clients.** Since §12.1 a user cancel reaches SignalR as
  `ExecutionFailed` with "Workflow execution was cancelled" — clients can only tell it apart by text.
  Add an `ExecutionCancelled` client event (bridge mapping + hub contract), e.g. publish a distinct
  message or map terminal `ExecutionStateChanged → Cancelled`. Update `ExecutionEventBridgeTests`.
- [x] ✅ **done 2026-11-09** (body nodes and region stages publish node events) — **Node events from construct bodies.** Loop/branch body nodes run in `SubGraphExecutor`, which
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
- [ ] **Stale sample workflows.** `examples/definitions/*.json` don't load: `example-workflow.json`
  uses retired module ids (`builtin.http`, `builtin.transform`, `builtin.database`); the other two
  have non-GUID ids. Found during the §9.4 audit.
- [x] **API test flake: SQLite "table VersionInfo already exists".** *Fixed 2026-11-08.* Not one
  host migrating twice: the app's default `:memory:` normalized to a shared-cache `:memory:`, which
  is ONE database per process, so every default-config test host (plain
  `WebApplicationFactory<Program>`, `WithWebHostBuilder`) shared it and raced migrations. Now each
  provider gets a uniquely named in-memory DB (`NormalizeSqliteConnectionString`). Guard:
  `Api/V1/InMemoryPersistenceIsolationTests`.

---

## 4. Refuted / keep as-is (do not "fix")

- **§12.6 join-skip** — refuted; the guard passed. `JoinSkipFindingTests` can be deleted (it was a
  false alarm).
- **Port-routing inside a loop body** — works; `EngineFindingTests.PortRoutingInsideLoopBody_…`
  passed. Keep it as a regression test (drop the `Finding` trait).

---

## 5. Done criteria for the whole effort

- [x] Every Stage 1–5 finding fixed, its guard green or retired, and its README row ~~removed~~
  struck through with a "Fixed (§…)" note (kept as history). Optional §9.5 (streaming) moved to Stage 7.
- [x] Stage 6 items either resolved or converted to tracked tickets with their decision recorded
  (all three resolved, 2026-11-08).
- [x] `dotnet test Workflow.Tests` fully green. *2026-11-08: 1893/1893 unfiltered (finding tests included); UI 760/760.*
- [ ] `docs/architecture/README.md#findings-at-a-glance` reflects reality (ideally empty or only
  parked design-decisions remain). *2026-11-08: every row is now fixed, refuted or tracked in
  Stage 7, and the §9.3 mid-pipeline row is corrected. Open until Stage 7 is fixed or parked.*

---

## 6. Progress log

Append one entry per working session. Keep it short: what you touched, test status, open decisions.

```
### 2026-11-09 — Follow-up: flaky tests
- `MonitorTests.Monitor_RowOpen_NavigatesToDetail`: the handler navigates synchronously, so the
  likely cause was a live re-render between finding the button and clicking it, which swallowed the
  click. The test now clicks and checks inside `WaitForAssertion`, with the same assertion.
  10/10 runs passed; with `Open` turned into a no-op it still fails.
- Streaming flakes (`Region_PublishesLiveCounts_*` and the `StreamRegionRunnerTests` host crash):
  not reproduced in 25 back-to-back runs. Analysis and a fix idea are in the follow-ups; left open.
- UI suite 794/794.

### 2026-11-09 — Follow-up: node events from construct bodies (and region stages)
- `SubGraphExecutor.PublishNodeEvent` puts the same messages on the EventStream as top-level nodes:
  - `NodeStateChanged(Pending → Running)` when a body node or region member starts;
  - `NodeExecutionCompleted`, with outputs and variable updates stripped like the top level;
  - `NodeExecutionFailed`, with the error redacted via `ExecutionSecrets`;
  - `NodeRetrying` when a retry is scheduled.
  These are not told to the construct parent. Only the real-time bridge subscribes, so nothing
  double-counts.
- Found on the way: **streaming region stages never sent `NodeCompleted`**, at the top level too
  (completion only changed their state). Both executors now publish `NodeExecutionCompleted` for
  every stage when a region finishes.
- Tests:
  - `Engine/BodyNodeEventsTests` (2): loop body started/completed ×3 with outputs stripped; a
    failing body node reports retrying and failed ×2.
  - `StreamRegionExecutionTests.EveryStageOfAFinishedRegion_ReportsCompletion` (top level and in a
    loop).
  - With both executors' changes stashed, all 4 fail; restored, they pass.
- Docs: `docs/realtime.md` (body and region events).
- Suites: unfiltered `dotnet test Workflow.Tests` 2009/2009; `Workflow.Tests.UI` 794/794.

### 2026-11-09 — Follow-up: dedicated ExecutionCancelled real-time event
- Engine: `WorkflowFailed` gets an init-only `Cancelled` flag, set by
  `WorkflowExecutor.HandleCancelExecution` on both the message to the parent and the EventStream
  one. It's a property rather than a new message type, so existing subscribers (metrics, the
  completion tracker) see the same terminal message as before.
- API: `IWorkflowHubClient.ExecutionCancelled(ExecutionCancelledEvent(executionId, workflowId?,
  reason, durationMs, timestamp))`. The bridge maps `WorkflowFailed { Cancelled: true }` to it
  **instead of** `ExecutionFailed`.
- UI:
  - `RealTimeClient.ExecutionCancelled`.
  - `MonitorState.ApplyCancelled` (the row shows Cancelled, no error), used by the monitor.
  - Execution detail refreshes as "Cancelled".
  - Designer run mode: `RunState.MarkCancelled`, now idempotent, so the user's own cancel plus the
    event log it once.
- Tests:
  - `Engine/ExecutionCancelledEventTests` (2).
  - `ExecutionEventBridgeTests.ExecutionCancelled_Broadcast_InsteadOfExecutionFailed`.
  - UI: `MonitorStateTests.Merge_Cancelled_*`, `RunStateTests.MarkCancelled_Twice_LogsOnce`.
- Proof: 4/4 mutants killed (engine doesn't mark it, bridge sends it as a failure, monitor shows
  Failed, logged twice).
- Docs: `docs/realtime.md` event catalog.
- Suites: unfiltered `dotnet test Workflow.Tests` 2005/2005; `Workflow.Tests.UI` 794/794.

### 2026-11-09 — Follow-up: completed body nodes keep their inputs in history
- `SubGraphExecutor.HandleNodeCompleted` now calls `QueuePersistNode` **before**
  `_nodeInputs.Remove`, so completed records from loop / branch / try bodies carry their inputs,
  like top-level ones. Failed records already did.
- Test: `NestedConstructHistoryTests.CompletedBodyRows_KeepTheirInputs`. With the fix stashed it
  fails; restored, it passes.
- Suite: unfiltered `dotnet test Workflow.Tests` 2002/2002 (engine-only change; UI not affected).

### 2026-11-09 — Stage 7.5 streaming region timeouts (§9.2 streaming)
- Decided (asked; plan 04):
  - Kind: a stall limit plus an optional overall limit.
  - Default: **everything off** (opt-in).
  - Where: server `Streaming:RegionStallTimeoutMs` / `RegionTimeoutMs`, plus a per-region override
    in the source node's metadata (`regionStallTimeoutMs` / `regionTimeoutMs`; `0` = off), with a
    designer field.
  - On timeout: handled like any region failure (retryable).
- Engine:
  - `StreamingTimeoutOptions` (in `StreamRegionMessages.cs`; `For(metadata)` resolves the
    override).
  - `StreamRegionExecutor`:
    - a linked `runCts` (binding and the runner use `RunToken`);
    - a stall watchdog comparing the live counters (the 7.6 `StreamRegionCounters`);
    - an overall one-shot timer.
    - `TimeOut()` cancels the run; `HandleFinished` turns the cancellation into a `TimeoutException`
      naming the limit, the per-stage counts and where to change it. The timers are cancelled on
      finish and in `PostStop`.
- API: Program.cs registers `StreamingTimeoutOptions` from configuration (0 = off).
- Designer: `Designer/State/RegionTimeouts.cs` (`IsRegionSource`, seconds ↔ ms) and a
  "Streaming region limits" section in `PropertiesPanel` (undoable `EditNodeMetadataCommand`).
- Tests:
  - `StreamRegionExecutionTests` +6: stall fails with the counts; slow but moving is OK; overall
    limit; override `0` disables the server's limit; override sets a limit when the server has
    none; a timeout is retried.
  - `StreamingProgressConfigTests` +2.
  - UI `Components/RegionLimitsPanelTests` (4).
- Proof: 8/8 mutants killed:
  - stall never fires;
  - progress doesn't reset the clock;
  - overall limit never armed;
  - reported as a cancellation;
  - source override ignored;
  - server setting ignored;
  - limits shown on non-source nodes;
  - seconds not converted.
- Docs: 03 §9 item 2, the findings table, plan 04.
- Suites: unfiltered `dotnet test Workflow.Tests` 2001/2001; `Workflow.Tests.UI` 792/792.

### 2026-11-09 — Stage 7.4 part 3/3: dotflow-module CLI, docs
- `Workflow.Modules/Packaging/ModulePackageBuilder.cs` builds a package from a folder:
  - it writes `contentHashes` for every file (except `module.json`, `signature.p7s` and the install
    record);
  - it validates the result like the server would;
  - it signs `module.json` (detached CMS, SHA-256, signer certificate included), then zips.
- `Workflow.Tools.ModulePack` (`dotflow-module`, packable as a dotnet tool; added to `Workflow.sln`):
  - `pack <folder> [--out] [--cert pfx | --cert pem --key pem] [--password-env VAR] [--chain]`. The
    PFX password comes from an environment variable only (default `DOTFLOW_SIGN_PASSWORD`).
  - `verify <wfmod> [--trust cert|thumbprint] [--trust-root root]`: exit 0 only when trusted (if
    asked).
  - On Windows, a PEM key is re-imported as PFX so it can sign.
- End-to-end check with real OpenSSL 3.2 (Git Bash):
  - an OpenSSL-made certificate and key → CLI pack + sign → `verify --trust` passes; checked
    against another certificate → refused;
  - the docs recipe (`openssl cms -sign -binary -md sha256 …` over `module.json`, then re-zip) →
    OpenSSL verifies it, and DotFlow's verifier reports it trusted;
  - a file tampered after signing → refused.
- Tests: `Modules/Security/ModulePackToolTests` (7). `Workflow.Tests` now references the CLI
  project.
- Proof: 5/5 mutants killed (a file left unhashed, never signs, verify passes untrusted, password
  env ignored, unknown options accepted).
- Docs:
  - New `docs/module-signing.md` (two mermaid diagrams, trust table, CLI and OpenSSL recipes,
    config, API, alternatives) and a docs index entry.
  - Updated: 04 §3 step 6 and §9.5, the findings table, the module author guide signing section,
    and the rest-api signing section; plan 07 marked implemented.
- Suites: unfiltered `dotnet test Workflow.Tests` 1993/1993; `Workflow.Tests.UI` 788/788.

### 2026-11-09 — Stage 7.4 part 2/3: trust in the API and UI, Trusted publishers page
- API:
  - `ModuleTrustDto` (status `verified` / `keyPinOnly` / `unsigned` / `untrusted` / `changedOnDisk`,
    publisher, thumbprint, trust source, messages, checked-at).
  - `ModuleSummaryDto.Trust` (installed packages only; built-ins have none) and
    `ModuleInstallResultDto.Trust`.
  - `ModulePackageInstaller.Packages()` lists installed packages for the list endpoint.
- Trusted publishers:
  - `Workflow.Api/Modules/TrustedPublisherService.cs` is the `ITrustedPublisherProvider`. It
    merges the server's `Modules:Security:TrustedPublishers` (locked) with the saved ones (blob
    `settings/trusted-publishers.json`, or in-memory without a blob store), validates and audits
    saves, and reloads every minute (`TrustedPublisherHostedService`).
  - The hosted service warns at startup when `RequireSigned` is on with no publishers.
  - Validation: a thumbprint must be 40 or 64 hex digits; PEM must parse; a thumbprint and PEM
    given together must agree; a CA root needs PEM; a server-set `CheckRevocation` can't be
    changed.
  - Saving normalizes the thumbprint to SHA-256 when a PEM is given, and the name defaults to the
    certificate's common name.
  - Endpoints `/api/v1/settings/module-publishers`: GET (read), PUT (Admin), `/audit` (Admin).
- UI:
  - `ModuleCard` trust badge (`trust-{id}`, tooltip = meaning or reasons).
  - `UploadDialog` shows who signed the package, plus help with a link when it isn't verified.
  - `/settings/module-publishers` page (`TrustedPublishersSettings.razor`, `TrustedPublishersClient`):
    locked server entries, add by thumbprint or PEM, remove, revocation toggle (locked when the
    server sets it), save errors, change list. Linked from Settings.
- Tests:
  - `Api/V1/TrustedPublisherApiTests` (4).
  - UI `Modules/Components/ModuleTrustUiTests` (9).
  - The signing helpers moved to `Modules/Security/SigningTestKit` (no test changed).
- Proof: 9/9 mutants killed (5 API, 4 UI).
- Suites: unfiltered `dotnet test Workflow.Tests` 1986/1986; `Workflow.Tests.UI` 788/788.

### 2026-11-09 — Stage 7.4 part 1/3: signed module packages (verification, install record, re-check on load)
- Decided (asked in two rounds; `docs/architecture/plans/07-module-package-signing.md`):
  - Signed packages: a detached CMS signature (`signature.p7s`) over `module.json`, which must hash
    every file. Must work on Windows and Linux.
  - Trust: pinned certificates plus optional CA roots; revocation opt-in.
  - Every load is re-checked: warn, or refuse under `RequireSigned`.
  - The strong-name token is kept as an accepted option, labelled "key pin only".
  - Tooling: a CLI plus openssl docs.
  - UI: all three pieces (badge, upload result, Trusted publishers page).
- Core (`Workflow.Modules`):
  - `Security/TrustedPublishers.cs`: options, set and provider.
  - `Security/PackageSignatureVerifier.cs`: `SignedCms`; a pinned SHA-256/SHA-1 thumbprint or PEM;
    a CA chain with `CustomRootTrust`; expiry checked at install only.
  - `Security/ModuleTrust.cs`: `ModuleTrust` (Verified / KeyPinOnly / Unsigned / Untrusted /
    ChangedOnDisk), `ModuleTrustInfo`, `ModuleInstallRecord`, `IModuleLoadGate`.
  - New package reference: `System.Security.Cryptography.Pkcs` 8.0.1.
- Reader: keeps the exact `module.json` bytes and `signature.p7s`. A signed package whose
  `contentHashes` miss any file is refused.
- Installer:
  - An invalid signature (tampered `module.json`) is always refused.
  - `Decide()`: a Verified signature, else a trusted strong-name token (KeyPinOnly, with a "not
    proof of authorship" warning), else Unsigned / Untrusted.
  - `RequireSigned` accepts Verified or KeyPinOnly.
  - Writes `.dotflow-install.json` (the trust and every file's hash).
  - `RehydrateAsync` runs `CheckInstalled` first. It compares the files with the record; with the
    same files it re-verifies the signature and the publisher's trust without the time check.
  - `TrustFor(packageId, version)` returns the last result.
  - Implements `IModuleLoadGate` (installed package files, or loose DLLs via the token check).
  - **Behaviour change:** with `RequireSigned` on and no assembly verifier wired, an unsigned
    package is now refused. Before, the check was skipped when no verifier was wired; the API
    always wires one.
- Hot reload: `ModuleReloadService` takes an optional gate, wired from the installer by
  `ModuleHotReloadHostedService`. A refused reload returns the new `ModuleReloadOutcome.Refused`,
  and the current version stays loaded.
- API: `Program.cs` binds `Modules:Security:TrustedPublishers` → `StaticTrustedPublisherProvider`
  (`TryAdd`, so part 2 can replace it) → `PackageSignatureVerifier` → installer.
- Tests: `Modules/Security/PackageSigningTests` (19), using real throwaway certificates (RSA
  `CertificateRequest`, a CA root and an issued leaf) and real CMS signing.
- Proof: 10/10 mutants killed:
  - signature not checked;
  - pinned expiry ignored;
  - included certificates treated as roots;
  - unhashed files allowed;
  - invalid signature not refused;
  - key-pin option dropped;
  - changes on disk ignored;
  - startup re-load ignores refusal;
  - publisher trust not re-checked;
  - hot reload ignores refusal.
- Next: part 2 (API signature status, Trusted publishers store and page, module-list badge,
  upload result), then part 3 (`dotflow-module` CLI, `docs/module-signing.md`, 04 §9.5, findings
  table).
- Suites: unfiltered `dotnet test Workflow.Tests` 1982/1982; `Workflow.Tests.UI` 779/779.

### 2026-11-09 — Async binding follow-up (IPropertyBinder → async, conditions off the executor thread)
- No decision needed. The 7.2 semantics are unchanged: a bad condition fails the source node, and
  in a Priority chain the first true edge per port wins.
- `IPropertyBinder` is async-only: `BindPropertiesAsync`, `BindModulePropertiesAsync` and
  `EvaluateConditionAsync` (each takes a ValueTask and a CancellationToken). The sync methods are
  gone, and every caller has been migrated.
- `PropertyBinder` awaits the evaluator. No `GetAwaiter().GetResult()` is left in binding.
  - Mixed-text interpolation is now a manual loop (the `Regex.Replace` callback can't await).
  - On a cancel it throws `OperationCanceledException`, not "failed to evaluate".
- `NodeExecutor`: `BindAndBuildContextAsync` (it passes the node's token).
- `StreamRegionExecutor`: binds stage properties in `BindAndRunAsync`, inside the region's
  `Task.Run`. It used to bind on the actor thread.
- Edge conditions:
  - `EdgeRouting.ConditionContext` takes a snapshot on the actor thread.
  - `EdgeRouting.DecideInBackground` runs `FiredAsync` in `Task.Run` and pipes `EdgesDecided`
    back.
  - `HandleEdgesDecided` (both executors) then fails the node or calls `CompleteNode`. It drops
    the result if the run is terminal (`DropIfTerminal`) or the body is `_finished`.
  - The node stays Running until then.
- Tests: `Engine/EdgeConditionsOffActorThreadTests` (3), each using a binder that blocks:
  - the status reply arrives while a condition runs;
  - a cancel is handled at once, and the late result is ignored;
  - in a loop body, another branch moves on.
- The binder unit tests and the test binders were migrated to async.
- Proof: stashing doesn't compile (the tests use the new interface). I used a mutant instead:
  without the `Task.Run` in `DecideInBackground`, 3/3 go red (replies ~1.5 s late).
- Docs: 01, 02 (§6 and §10 item 5), 03 and `docs/conditional-edges.md`.
- Suites:
  - unfiltered `dotnet test Workflow.Tests`: 1963/1963 on the re-run. The first full run had
    1 failure, `StreamRegionExecutionTests.Region_PublishesLiveCounts_WhileItRuns_ThenTheFinalSummary`,
    which then passed 5/5 alone. I logged it as flaky in the follow-ups.
  - `Workflow.Tests.UI`: 779/779.
- Memorizer: added 2444cd75 (async binding rule and Task.Run gotcha). Updated 8addec6c (7.9) and
  6ec4ef40 (conditional-edges map).

### 2026-11-09 — Stage 7.9 binding off the NodeExecutor actor thread (02 §10 item 5)
- No decision needed.
- `NodeExecutor.HandleExecute` now only gathers (on the actor thread) the binding context, the raw
  properties, the variables and the logger (`PrepareExecution` → `ExecutionPlan`).
- `BindAndBuildContext` binds the inputs and properties inside the `Task.Run` that runs the module,
  then calls `ThrowIfCancellationRequested` before the module starts.
- Binding failures throw the same `InvalidOperationException` messages.
- `HandleExecutionResult` ignores a result once the node has already reported (timeout or cancel).
  This also fixes a late module result after a timeout being reported a second time.
- Tests: `Engine/NodeBindingOffActorThreadTests` (4):
  - the timeout covers slow binding (with no late second report);
  - a cancel during binding stops the node before the module runs;
  - binding failure messages are unchanged;
  - binding still feeds the module.
- Proof: stashing the production change → 2/4 red. Mutants killed: no late-result guard (after
  widening the test's quiet window to 2 s), no cancel check before the module.
- Remaining sync waits: the binder on the evaluator (pool thread), and edge conditions on the
  executor thread. Logged as the "Async binding" follow-up.
- Docs: 02 §10 item 5, the findings table.
- Suites: unfiltered `dotnet test Workflow.Tests` 1960/1960; `Workflow.Tests.UI` 779/779.

### 2026-11-09 — Stage 7.7 streaming regions inside construct bodies (§9.5 streaming)
- No decision needed (parity work).
- The scope rule is "region wholly inside the body". This matches the designer's rule 3 (a stream
  may not cross a construct boundary); a crossing stream's body nodes still run as ordinary nodes.
- `SubGraphExecutor`:
  - `StreamRegions`: lazy, detected over the whole definition (indices match the top level's),
    filtered to scope, with cached schema lookups and pins honoured.
  - `ExecuteNode`: a region's source starts `ExecuteStreamRegion`; other members return.
  - `ExecuteStreamRegion` un-skips and un-fails the members first, because a retry re-runs the whole
    region.
  - `HandleStreamRegionCompleted`:
    - members complete, with `itemCount` / `errors` outputs;
    - terminal outputs, history records and the final `StreamRegionProgress` (EventStream only;
      the construct parent doesn't want it);
    - then dispatch from the terminal stage.
  - `HandleStreamRegionFailed`: the other members are skipped (recorded), then the blamed node goes
    through `HandleNodeFailed`, so retry, continue, the error handler and try/catch apply.
  - A region actor that stops unexpectedly fails the body. Region actors are stopped once they
    report (top-level region actors still aren't, which is harmless: they die with the executor).
- Tests: `StreamRegionExecutionTests` — loop body each iteration, failure carries the stage's
  error, retry re-runs the region, two parallel branches.
- Proof:
  - Stashing the production change → 4/12 red.
  - Mutants killed: no body regions, retry leaves members skipped, terminal outputs dropped, no
    dispatch after the region, failure bypasses retry.
  - One survivor ("complete while a region runs") showed a redundant `_regionActors.Count` check in
    `IsComplete` (the members are already running), so I removed the check.
- Docs: 03 §9 item 5, the findings table, plan 04, Findings README.
- Suites: unfiltered `dotnet test Workflow.Tests` 1956/1956; `Workflow.Tests.UI` 779/779.

### 2026-11-09 — Stage 7.6 streaming progress to live clients (§9.1 streaming)
- Decided (asked; plan 04 "Low"):
  - Live + final.
  - Shown in designer run mode and on the execution detail page.
  - Every 1 s, configurable via `Streaming:ProgressIntervalMs` (0 = final only).
- Engine:
  - `StreamRegionCounters` (shared, readable mid-run) passed to `StreamRegionRunner.RunAsync`.
  - `StreamRegionExecutor` schedules a repeating `ProgressTick`, publishes
    `StreamRegionProgress(..., Completed: false)` only when the counts changed, and cancels it when
    the region finishes or the actor stops.
  - The final event from `WorkflowExecutor` defaults to `Completed = true`.
  - `StreamingProgressOptions` (default 1000 when not registered).
- API:
  - The bridge subscribes to `StreamRegionProgress` and maps it to `IWorkflowHubClient.StreamProgress(StreamProgressEvent)`.
  - Program.cs registers the interval.
- UI:
  - `RealTimeClient.StreamProgress`.
  - `RunState.StreamProgress` / `StreamCounts` / `ItemBadgeFor`: ignores other executions; a late
    live tick never overwrites the final count.
  - `NodeView` `RunBadge` (`node-items`) via `CanvasView.NodeRunBadge`.
  - An ExecutionDetail "streaming items" table.
- **Test change (not weakened):** `Region_PublishesPerStageItemCounts` now waits for the final
  event (`FishForMessage(p => p.Completed)`), because live events may come first. Its assertions are
  unchanged.
- Tests:
  - Engine: live-then-final, interval 0.
  - Bridge: live and final.
  - `StreamingProgressConfigTests`.
  - UI: RunState ×2, canvas badge, detail table.
  - Mutants killed (7/7): no timer, no "changed" check, live marked final, config ignored, bridge
    not subscribed, late live overwrites final, other executions accepted.
- Docs: `docs/realtime.md` (event and diagram), 03 §9 item 1, findings table, plan 04.
- Suites: unfiltered `dotnet test Workflow.Tests` 1952/1952; `Workflow.Tests.UI` 779/779. Live test repeated 5× green.

### 2026-11-09 — Stage 7.3 outbound HTTP policy (SSRF)
- Decided (asked twice; plan 06 §1 + §8):
  - Separate Scripts / HttpModule channels, and four modes with allow/deny lists (deny wins).
  - **Unconfigured fails closed as PublicOnly**, with warnings.
  - `AllowCloudMetadata` is an explicit switch: blocked in every mode, incl. Unrestricted.
  - Per-node narrowing.
  - **Setup screen in v1**, with host settings locked.
  - Enforcement: connect-time IP check + per-hop host check.
- Core:
  - `Workflow.Core/Http`: the policy model with layered resolve and locked fields, `OutboundHttpRules`,
    `OutboundHttp.CreatePrimaryHandler` (ConnectCallback), `OutboundPolicyHandler` (manual
    redirects, credentials stripped across hosts), and `OutboundHttpBlockedException`.
  - Polly retries skip policy blocks.
- Wiring:
  - Named clients `dotflow.http` / `dotflow.script.http`; a fail-closed fallback provider in
    `AddHttpModules`.
  - Script HTTP never uses a bare `HttpClient` any more.
  - `allowedHosts` on HTTP Request and script nodes.
- API:
  - `OutboundHttpPolicyService` (blob-persisted, audited, reloaded every minute), endpoints under
    `/settings/outbound-http` (Admin save), the `/status` `outboundHttp` section, and a startup
    warning per unconfigured channel.
- UI: the `/settings/outbound-http` page (locked fields, check an address, change list), a link
  from Settings, and the designer banner.
- **Test setup changed (not assertions):** 8 HTTP test classes that call WireMock on localhost now
  register `StaticOutboundHttpPolicyProvider.Unrestricted`. The fail-closed default blocks
  localhost.
- Tests:
  - `Http/OutboundHttpRulesTests` + `OutboundHttpEnforcementTests` (24),
    `Api/V1/OutboundHttpSettingsTests` (6), UI `OutboundHttpUiTests` (6).
  - Mutants killed: connect check, host check, retry exclusion (after adding a policy-call counter),
    metadata rule, script fallback, locked-field validation.
- Docs: `docs/outbound-http.md` (modes, diagrams, upgrade note, alternatives); scripting / http
  docs, the findings table and 04 §9.6 updated.
- Unfiltered `dotnet test Workflow.Tests` 1945/1945; UI 775/775.

### 2026-11-09 — Stage 7.2 conditional edges (Condition + Priority)
- Decided (asked):
  - Syntax: the expression language plus `value` (the edge's value).
  - False = like an inactive port.
  - A bad condition = a failure of the source node (On error applies).
  - Priority ≥ 1 = an if / else-if / else chain per port (first true wins, no condition = else);
    0 = independent.
- Engine:
  - `EdgeRouting.IsConditional` / `Fired`, `EdgeConditionException`, and
    `DispatchCore.ExecuteReadySuccessorsVia`.
  - Both executors decide edges before any state change. A bad condition becomes
    `NodeExecutionFailed`.
  - `_inactiveEdges` keeps a false edge's value out of a join's inputs.
  - `PropertyBinder.EvaluateCondition` (`value` in scope, JSON → plain objects, braces optional).
  - Nodes without conditional edges keep the old code path exactly.
- **Also fixed:** the documented join-skip race (01-execution-lifecycle §12, item 6). A join
  activated by one predecessor was skipped when the last predecessor deactivated its edge. Found
  because the join test was order-dependent. `DispatchCore._activated` now fires such nodes. This
  also applies to module ports. Each race test is RED without it.
- Designer: the Priority field, condition help text, `EditConnectionPriorityCommand`, the
  `n: cond` / `n: else` edge labels, and `EdgeConditions` lint (construct and streaming edges
  ignored, missing node, unreachable chain edges).
- Docs: new `docs/conditional-edges.md` with the Priority decision, diagrams and alternatives
  P1–P6 / C1–C4 (the user asked for this). Docs index, data-binding doc, lifecycle doc and findings
  table updated.
- Also fixed: `SubGraphExecutor.cs` had a literal NUL byte in the §12.7 `NoPortFires` constant
  (from an edit script). It compiled, but made the file "binary" to grep and diffs. Now the
  `\u0000` escape.
- Tests: `Engine/ConditionalEdgeTests` (15), UI `EdgeConditionsTests` (9); routing, priority,
  gather, bad-condition, activated-join and lint mutants all killed.
- Unfiltered `dotnet test Workflow.Tests` 1915/1915; UI 769/769.

### 2026-11-08 — Stage 7.1 Blob spill refuses an in-memory blob store
- Real gap: the default persistence (SQLite `:memory:`) gives a `SqliteBlobStore`, which passed
  `BlobStoreSpillStore`'s `is InMemoryBlobStore` check. Blob spill on a default host gave no memory
  relief.
- Fix: `IBlobStore.IsInMemory` (default false). It is true for `InMemoryBlobStore`, and for
  `SqliteBlobStore` when `WorkflowDataConnectionFactory.IsInMemory` (`:memory:`, `Mode=Memory`, or a
  `file:…?mode=memory` URI). `BlobStoreSpillStore` refuses it, and the host fails at startup
  because the cleanup service resolves the store. Fail-fast and "require durable" were the same
  change, so no decision was needed.
- Kept the existing test's "in-memory" wording in the new message rather than changing that test.
- Tests: `Streaming/BlobSpillDurabilityTests` (8); 2 mutants killed.
- Gotcha: `WebApplicationFactory` `ConfigureAppConfiguration` values are NOT visible to
  `Program.cs` code that reads `builder.Configuration` eagerly (e.g. `AddStreamingSpill`). Use
  `builder.UseSetting(...)`.
- `--filter Category!=Finding`: unfiltered `dotnet test Workflow.Tests` 1901/1901 (UI unchanged).

### 2026-11-08 — done-criteria check; Stage 7 created
- Pushed Stage 6 (`02fac32`, `9cf07bb`, `8ad6ae3`).
- §5 done criteria: 1–3 ticked. Unfiltered `dotnet test Workflow.Tests` 1893/1893 green; UI
  760/760.
- Criterion 4 is open: the findings table's remaining rows (never reproduced by a test) are now
  tracked as **Stage 7** (7.1–7.10), with plan pointers, whether a decision is needed, and a size.
- Corrected the §9.3 (streaming) table row: plan 04 already found there's no stall (the source is
  blamed and the region re-runs), so Medium → Low.
- The optional §9.5 (streaming) item moved to Stage 7.7.
- Spotted while checking: the designer lets you set an edge **condition**, but the engine never
  reads `ConnectionDefinition.Condition` (7.2). User-visible.
- Next: pick from Stage 7. 7.1 is small. 7.2–7.5 need decisions.

### 2026-11-08 — §12.7 UseErrorHandler: catch-style handler (Stage 6 done)
- Decided (asked):
  - Handler nodes run only on error.
  - Retries come first.
  - The handler's own path continues and the failed node's downstream is skipped.
  - Designer support is included.
  - Defaults: workflow-level handler = default; MaxConsecutiveErrors per node per run, reset on
    success.
- Engine:
  - `ErrorHandlerCore`: handler ids, the handler path (the handler + nodes reachable only through
    it), error-only nodes skipped at start, and the handler sub-graph with `error` as a body
    variable.
  - `ErrorHandlerCounters`, the `NodeFailureCore` `UseErrorHandler` branch, and `RunErrorHandler` +
    `SubGraphCompleted`/`SubGraphFailed`/`Terminated` handling in both executors.
  - `ScopeStep.ErrorHandler`.
- Gotchas hit:
  - `GetStartNodes` is called while the graph is being built, so error-only nodes must be computed
    first (a ctor NRE hung every test).
  - Start the handler **before** skipping downstream: skip propagation runs the completion check.
- Designer: `NodeErrorHandling` (read/write, candidates, lint, links), the *On error* section,
  `EditNodeErrorHandlingCommand`, and the dashed "on error" link in `EdgeLayer`.
- Docs: new `docs/error-handling.md` (decision flow, handler path, retry sequence and loop/max
  diagrams, comparison with Try/Catch, designer, alternatives A–G); lifecycle doc and README
  updated.
- Tests:
  - `Engine/ErrorHandlerTests` (8): 6 mutants killed (route, retry-first, max, reset, start-skip,
    downstream).
  - UI `NodeErrorHandlingTests` (9) + `OnErrorPanelTests` (2): lint/links mutants killed.
  - Retired the §12.7 guard method (`EngineFindingTests` keeps its other test). No failing finding
    tests remain.
- `--filter Category!=Finding`: 1891 passed, 0 failed; Workflow.Tests.UI 760/760; `Category=Finding` 2/2 passing (no open finding tests). **Stage 6 complete.**

### 2026-11-08 — §10.6 secrets hidden by name and value
- `SecretRedactor` (name + value; values under 4 chars by name only; longest first; old values stay
  hidden) and the `ExecutionSecrets` per-run registry.
- Hooked in at: execution record inputs, node records (top level + bodies), both status-error
  paths, run-scope variable writes, the variable-update debug log, and published
  `NodeExecutionFailed` / `WorkflowFailed`.
- The workflow-scope store and state snapshots stay verbatim (at rest, by decision).
- Found + fixed: the snapshot path's history status update threw on every successful run
  (LanguageExt `Match` with a null branch) and was swallowed. Moving it out of the task's try block
  turned it into a hang, which is how it was found.
- Tests: `Engine/SecretRedactionTests` (3), RED with both executor changes stashed;
  `Engine/SecretRedactorTests` (7); value and name mutants killed. Retired
  `Findings/Binding/SecretPersistenceFindingTests`.
- New follow-ups: completed body records lack inputs; secrets at rest.
- `--filter Category!=Finding`: 1883 passed, 0 failed (UI unchanged this commit); open finding tests 1 (§12.7).

### 2026-11-08 — Stage 6 decisions + §9.6 spill reclaimed by age without history
- Stage 6 decisions (asked), recorded in the plans with the alternatives kept on file:
  - §10.6: redact secrets by name **and value**.
  - §9.6: **B2** plus a warning.
  - §12.7: **catch-style** handler. Detail questions are to be asked before coding.
- §9.6:
  - `ISpillStore.GetLastWrittenAsync` (default null = unknown, kept), implemented by the Directory,
    Blob and SQLite stores. SQLite gets a `written_at` column, added with ALTER to older scratch
    files.
  - The sweeper falls back to age (last write older than `AbandonedThreshold`) when there's no
    history, and warns at startup.
  - Fixed the contradictory comment, and updated docs `03-streaming-and-spill.md` §7/§8.
- Tests: `Streaming/SpillAgeRetentionTests` (10); 5 mutants killed (sweeper, warning, each store).
  Retired `Findings/Streaming/SpillCleanupFindingTests`: its fake store reported no age, and that
  case is now kept on purpose. The intent is covered with an aged store.
- `--filter Category!=Finding`: 1873 passed, 0 failed (UI unchanged this commit).

### 2026-11-08 — §10.1 {{nodeId.port}} reaches any finished ancestor (across bodies)
- Decided (asked, after an options diagram): **A2 across bodies**, not the plan's A1.
  - `{{nodeId.port}}` resolves for any ancestor (any connection path in) that has run, including
    from inside construct bodies.
  - Non-ancestors and skipped ancestors fail with distinct messages.
  - Designer error defaults on, with optional "wire it in" / "via a variable" fixes. Save warning VB002.
- Engine: `NodeAncestry` + `Execute.Upstream`; `outerOutputs` threaded through
  ConstructSpawnCore → Loop/Parallel/TryCatch/Transaction → SubGraphExecutor (nested bodies too).
  Binding only; node inputs/history unchanged.
- UI: `UpstreamReferences`, a lint error, PropertyEditor buttons, `PropertiesPanel.SchemaFor`. The
  picker already offered ancestors.
- Tests: `Engine/UpstreamReferenceTests` (5), `Modules/NodeReferenceValidationTests` (3), UI
  `UpstreamReferencesTests` (6) + `NonUpstreamReferencePanelTests` (4). All 7 mutants killed
  (merge, body view, nested hand-off, explainer, lint, Find, validator).
- Changed an existing test: `VariableLintTests.KnownNodeReference_IsClean` now connects `up1 → n1`.
  It used to assert "clean" for a reference to an unconnected node, which fails at run time. The
  assertion is unchanged.
- Retired `Findings/Binding/BindingFindingTests.cs` (both guards fixed).
- `--filter Category!=Finding`: 1863 passed, 0 failed; Workflow.Tests.UI 749/749; open finding tests 3 (§9.6, §10.6, §12.7 — expected). **Stage 5 complete.** Stage 6 needs decisions: §10.6,
  §9.6, §12.7.

### 2026-11-08 — §10.2 port values no longer shadow variables
- Decided (asked): B1 — port values aren't variables. Body values seeded as body-scoped variables:
  `item`/`index` (For Each, Fan Out) and `error` (catch). Designer flags `{{Variable.x}}` vs a wired
  port `x` (warning, or error if no variable `x`) with fixes: use incoming value / keep the variable
  (`Metadata["variableClashAck"]`, `name@src.port`) / rename the variable (+ references, undoable).
  Same warning on save (`VariableClashChecker`, VB001). Run-time failure hints at `{{input…}}`.
- Engine: `NodeExecutor.BuildBindingContext` no longer copies input keys into variables;
  `BodyVariables.WithBodyContext` used by loop / parallel / try-catch. Binder hint in `PropertyBinder`.
- UI: `VariableLint.Clashes` / `UseIncomingValue` / `AcknowledgedWith`, `VariableTokens.BodyValuesFor`
  (picker "This body", lint accepts them), `WorkflowVariables.RenameWithReferences` (VariablesPanel now
  uses it too), clash panel in `PropertyEditor`.
- Tests: `Engine/VariableShadowingTests` (6, each production piece stashed alone → RED),
  `Modules/VariableClashValidationTests` (4), UI `VariableClashTests` (15) + `VariableClashPanelTests` (6);
  lint/body/validator mutants all killed. Guard retired.
- Docs: variables.md (ports vs variables, body variables, lint table); advanced-flow-control catch
  example fixed (`{{error.message}}` never resolved → `{{Variable.error.message}}`).
- `--filter Category!=Finding`: 1855 passed, 0 failed; Workflow.Tests.UI 739/739; open finding tests 4 (all expected). Stage 5 left: §10.1 (docs/UX).

### 2026-11-08 — §9.4 (streaming) pinned module version honoured in regions
- New `Workflow.Modules.ModuleResolution` — the single pin-aware module lookup (batch node, region
  executor, region detection, run-time config check, validator). Same clear error for a
  pinned-but-missing version on both paths.
- Found + fixed: region detection read the latest version's ports, so a pin to a non-streaming
  version still formed a region and then failed.
- Tests: `Engine/Streaming/StreamRegionPinnedVersionTests` (4). Bite-proved separately: detection
  change stashed → detection test RED; region-executor change stashed → region test + guard RED.
  Retired `Findings/Streaming/StreamingFindingTests`.
- `--filter Category!=Finding`: 1845 passed, 0 failed; Workflow.Tests.UI 718/718. Stage 5 left: §10.2, §10.1 (docs/UX).

### 2026-11-08 — §9.7 multi-module package manifests
- Decided (asked): multi-module manifests (`modules: [ids]`, absent → just `id`); every declared
  module must exist and report the manifest version; uninstalling one module of a multi-module
  package → 409 pointing to disable; per-module disable must exist (it did — and needed a fix).
- Loader/discovery register only declared ids (install + rehydrate); undeclared modules in the
  assembly are skipped with a warning. Package lookup (`FindPackage` / `FindPackageDeclaring`) for
  uninstall; dependency check covers every module in the package. Upload response gains `modules`.
- Found + fixed: a disabled module wasn't persisted when all its versions were disabled
  (`GetAllModules` skips them) → came back enabled after restart. New
  `IModuleRegistry.GetAllModuleIds()` (default impl for other registries).
- Tests: `Api/V1/MultiModulePackageTests` (9) — all RED with the production files stashed; the
  restart/disable test alone is RED with only the persistence fix stashed. Retired
  `Findings/Modules/PackageExtraModulesFindingTests`. Module author guide documents `modules`.
- Earlier today: SQLite VersionInfo flake fixed (608cfed).
- `--filter Category!=Finding`: 1841 passed, 0 failed; Workflow.Tests.UI 718/718. Stage 5 left: §9.4 (streaming) pinned version in
  region, §10.2, §10.1 (docs/UX).

### 2026-11-08 — SQLite "VersionInfo already exists" test flake fixed
- Root cause: `NormalizeSqliteConnectionString` turned the default `:memory:` into
  `Data Source=:memory:;Cache=Shared;Mode=Memory` — a shared-cache `:memory:` is one database per
  process. Every test host on default settings shared it; parallel hosts raced FluentMigrator
  (check-then-create of VersionInfo; also the "UndeterminableConnection / ArgumentOutOfRange"
  variant) and saw each other's data. Composite providers with several `:memory:` roles had the same
  race (they initialize in parallel).
- Fix: `:memory:` now normalizes to `Data Source=file:dotflow-{guid};Mode=Memory;Cache=Shared` —
  one private in-memory DB per provider; a single-host process behaves as before.
- Tests: `Api/V1/InMemoryPersistenceIsolationTests` — two default hosts don't share data (RED
  without the fix, deterministic), 8 hosts starting concurrently all healthy (smoke; the race
  itself isn't deterministic).
- Before: a DB startup error in 3 of the last 5 full runs. After: 3 consecutive full runs,
  1832/1832 each, 0 DB startup errors. Next: §9.7 (manifest decision first).

### 2026-11-08 — §9.4 module config checks: warn on save, block on run
- Audit of 44 `ValidateConfiguration` implementations (summary in plan 05 "Audit"): value checks
  are low risk; required-property checks would block saving drafts as errors; LINQ/Transform
  Script `compiledAssemblyKey` is set at publish; 5 modules put the property name in the NodeId
  slot. Probe with B1 wired in: 0 rejections across the suite.
- Decided (asked): warn on save, block on run; `compiledAssemblyKey` only at run. Note: the old
  guard asserted save-time *invalid* (plan's B1) — replaced by tests for the agreed policy.
- `ModuleConfigurationChecker` (Workflow.Modules/Validation): run-time-shaped config (JSON → CLR,
  schema defaults), ignores errors on `{{…}}` values, `RunOnlyProperties` skipped on save.
  Validator → warnings; `WorkflowSupervisor` → submit errors (API 400). Fixed the NodeId-slot bugs.
- Tests: `Modules/ModuleConfigurationValidationTests` (10). 6 RED with validator/supervisor/module
  fixes stashed; the 4 "no false alarm" tests RED against a naive checker (no defaults / template
  skip / run-only skip) — one was strengthened (templated `readAs`) after it survived that check.
- Flakes seen once each, green on rerun: FluentMigrator connection at host startup
  (WebhookApiActorTests), HttpRetry 150ms timing.
- Follow-up: `examples/definitions/*.json` are stale (non-GUID ids, retired module ids).
- `--filter Category!=Finding`: 1830 passed, 0 failed (2nd run; 1st hit the SQLite VersionInfo flake
  twice). Workflow.Tests.UI: 718/718. Next: §9.7 (needs a single- vs multi-module
  manifest decision first).

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
