# Plan 04 — Streaming & spill remediation

> Fix plan for the confirmed streaming findings from `Workflow.Tests/Findings/Streaming/*`
> (2026-09-29), plus the streaming items that are documented-only (not yet reproduced). Source:
> `Workflow.Engine/Actors/StreamRegionExecutor.cs`, `Workflow.Engine/Streaming/*`,
> `Workflow.Api/Streaming/*`.
>
> Up: [architecture findings](../README.md#findings-at-a-glance) ·
> [03 — Streaming & spill §9](../03-streaming-and-spill.md#9-findings-to-verify)

**Status:** proposed. Nothing built yet.

| # | Finding | Sev | Guard test |
| --- | --- | :-: | --- |
| A | Pinned module version ignored inside a region (§9.4) | Low | `StreamingFindingTests.PinnedModuleVersion_ShouldBeHonoured_InsideRegion` |
| B | Spill kept forever when no history is registered (§9.6) | Med | `SpillCleanupFindingTests.Sweep_WithNoHistory_ShouldReclaimAbandonedSpill` |

Plus **documented-only** items (no test yet) covered at the end: §9.1 progress→SignalR, §9.2 region
timeout, §9.3 mid-pipeline retry, §9.5 regions inside bodies, §9.7 blob spill in an in-memory store.

---

## Finding A — pinned module version ignored inside a region (§9.4)

### Evidence
- `StreamRegionExecutor.HandleExecute` resolves each stage with
  `registry.GetModule(nodeDefinition.ModuleId)` — the **latest** enabled version — ignoring any pin.
- `NodeExecutor` honours a pin via `ResolvePinnedVersion(node)` reading `Metadata["moduleVersion"]`.
- `PinnedModuleVersion_ShouldBeHonoured_InsideRegion` → a node pinned to `1.0` ran `2.0`.

### Root cause
The region executor duplicates the module-resolution step but omits the version pin that `NodeExecutor`
applies.

### Options
- **A1 (recommended): honour the pin.** Reuse the same resolution as `NodeExecutor`: read
  `ResolvePinnedVersion(nodeDefinition)` and call `GetModule(id, pinnedVersion)`, with the same
  "pinned-but-missing → fail clearly" behaviour. Extract the resolution into one shared helper so the
  two call sites can't drift again (same pattern as `DispatchCore`).

### Recommendation
**A1**, extracting a shared `ResolveModule(nodeDefinition, registry)` helper.

### Acceptance
`PinnedModuleVersion_…` green; a pinned-but-missing version inside a region fails with the same clear
error as the batch path; existing `StreamRegionExecutionTests` green.

**Effort:** ~0.5 day.

**Fixed 2026-11-08 (A1).** New `Workflow.Modules.ModuleResolution` (`PinnedVersion(node)`,
`Resolve(registry, node)`) is the one resolution path, used by `NodeExecutor`, `StreamRegionExecutor`,
`WorkflowExecutor` region detection, `WorkflowSupervisor`'s run-time config check and
`ModuleAwareWorkflowValidator`. Found while fixing: region **detection** also used the latest version's
ports, so a pin to a non-streaming version could still build a region and then fail ("isn't a
streaming module") — detection now uses the pinned version's schema too. Tests:
`Engine/Streaming/StreamRegionPinnedVersionTests` (4); guard retired.

---

## Finding B — spill kept forever with no history (§9.6)

### Evidence
- `SpillCleanupHostedService.DecideAsync` returns `SweepDecision.Keep` when `history is null`, with a
  comment that says the opposite ("fall back to the abandoned threshold … keeping forever would be a
  guaranteed leak").
- `Sweep_WithNoHistory_ShouldReclaimAbandonedSpill` → nothing reclaimed.

### Root cause
The comment and the code disagree. And the "fall back to age" the comment wants isn't actually
possible today: retention is keyed to the **execution record's** timestamps, and the spill store keeps
no artefact-creation time of its own, so with no history there is no age to fall back to.

### Options
- **B1: fail fast at wiring.** If a spill store is configured but no `IExecutionHistoryRepository` is
  registered, refuse at startup (or log a prominent warning). Spill retention is meaningless without
  history, so don't run in that state. Then fix the misleading comment.
- **B2: give the store its own age.** Record an artefact-creation timestamp in each `ISpillSession`
  so the sweeper can apply `AbandonedThreshold` even with no history. Enables orphan cleanup in a
  history-less deployment.
- **B3: accept "Keep", fix the comment.** Lowest effort; leaves a latent leak if anyone ever runs
  spill without history.

### Recommendation
**B1** as the default (spill without history is a misconfiguration), and **B2** only if a
history-less spill deployment is a real supported target. Either way, **fix the contradictory
comment**. This finding's test encodes the comment's *stated intent* — if you choose B1/B3, delete or
rewrite the test rather than "fixing the code" to match it.

### Acceptance
- B1: a startup test that spill-store-without-history is rejected/warned; `Sweep_WithNoHistory_…`
  **deleted** (no longer a valid expectation).
- B2: `Sweep_WithNoHistory_…` green via artefact-age; existing `SpillCleanupTests` green.

**Effort:** B1 ≈ 0.5 day. B2 ≈ 1–2 days (store schema + sweeper change).

### Decided 2026-11-08 (asked) — B2, plus a clear warning
- **Chosen: B2.** Each spill artefact gets its own creation time, so with no history store the
  sweeper reclaims artefacts older than `AbandonedThreshold`. That makes `Sweep_WithNoHistory_…`
  pass.
- **Also warn clearly:** at startup, when spill is on and no history store is configured, log a
  prominent warning. Cleanup then falls back to file age, so a run longer than the threshold could
  lose its spill.
- Fix the contradictory comment.
- **Fixed 2026-11-08:**
  - `ISpillStore.GetLastWrittenAsync`: a default interface method that returns null ("unknown",
    so the spill is kept).
  - Directory store: file times. Blob store: newest `LastModified`. SQLite store: a new
    `written_at` column, added with `ALTER TABLE` to older scratch files, whose old rows count as
    unknown.
  - The sweeper uses the last write, not the first, so a long run that is still spilling is never
    reclaimed.
  - Startup warning in `SpillCleanupHostedService.ExecuteAsync`.
  - Tests: `Streaming/SpillAgeRetentionTests` (10). The guard is retired: its fake store had no
    age, which is now kept by design.
- **Pivot options kept on file** (user asked to keep them documented):
  - **B1 refuse:** fail at startup when spill is on without history.
  - **B1-lite warn:** start, warn, keep files.
  - **B3 accept:** keep forever and fix the comment.

  For B1 / B1-lite / B3, delete the finding test rather than making it pass.

---

## Documented-only streaming items (no test yet)

These are in [03 §9](../03-streaming-and-spill.md#9-findings-to-verify) but weren't reproduced, mostly
because they need a hanging module, a client contract that doesn't exist, or DI-shape inspection.
Planned lightly here; promote to a tested finding when scheduled.

- **§9.1 `StreamRegionProgress` not forwarded to SignalR.** No client event contract exists for
  per-stage progress. Fix belongs with Phase 5.1.5 (observability): define the client event, add the
  type to `ExecutionEventBridge.SubscribedTypes`, map it. Until 5.1.5, document as "not wired."
  **Decided 2026-11-09 (asked, Stage 7.6):**
  - **Live + final:** while a region runs, per-stage item counts are sent every interval (only when
    they changed), plus a final event when it finishes, carrying the duration and a `completed`
    flag.
  - **UI:** run mode and the execution detail page show per-stage counters. The monitor list is
    unchanged.
  - **Rate:** 1 s by default, configurable via `Streaming:ProgressIntervalMs` (0 = final summary
    only).
  - Alternatives: final summary only; detail page only; API only; a fixed 1 s; 250 ms.
  - **Implemented 2026-11-09 (Stage 7.6):**
    - `StreamRegionCounters` lets the runner's counts be read while it runs.
    - `StreamRegionExecutor` sends a `ProgressTick` on a timer.
    - `StreamingProgressOptions` is registered from `Streaming:ProgressIntervalMs`.
    - `StreamRegionProgress.Completed` was added.
    - The bridge maps it to `StreamProgress`; `RunState.StreamProgress` and `ItemBadgeFor` show it
      in the UI.
- **§9.2 regions have no timeout.** Batch nodes get `NodeExecutor`'s receive timeout; a region has
  none. Fix: a per-region timeout knob (default off or generous for long ETL) enforced around
  `StreamRegionRunner.RunAsync` via the linked CTS. Needs a design decision on the default.
  **Decided 2026-11-09 (asked, Stage 7.5):**
  - **Kind:** a **stall** timeout (fail when no item moves through any stage for N) plus an
    optional **overall** wall-clock limit.
  - **Default: everything off** (opt-in). No behaviour change unless configured.
  - **Where:** the server defaults are `Streaming:RegionStallTimeoutMs` / `Streaming:RegionTimeoutMs`.
    A workflow overrides them per region on the region's **source node**: metadata
    `regionStallTimeoutMs` / `regionTimeoutMs`, where `0` means off for that region. The designer
    has a field for them.
  - **On timeout:** handled like any region failure. The source node fails with a
    `TimeoutException` naming the limit and the per-stage counts, so retry, continue, the on-error
    handler and try/catch apply.
  - Alternatives: stall only; overall only; a default stall of 5 min (with or without a 1 h overall
    limit); server-only or per-region-only settings; timeouts never retried.
  - **Implemented 2026-11-09 (Stage 7.5):**
    - `StreamingTimeoutOptions` (`For(sourceMetadata)` resolves the per-region override).
    - `StreamRegionExecutor` runs a stall watchdog (`StallCheck`, every stall/4 clamped to
      25 ms–1 s, comparing the live counters) and an overall timer (`TotalElapsed`). It cancels a
      linked run token and reports `TimeoutException`.
    - Designer: "Streaming region limits" on a region's source node (`RegionTimeouts`).
- **§9.3 retrying a mid-pipeline stage.** `StreamRegionFailed` currently always blames the **source**
  (`Fail()` uses `SourceNodeIds.First()`), so a retry re-runs the whole region rather than stalling —
  the originally-feared stall doesn't occur, but stage-level blame is missing. Revisit if/when
  per-stage blame is added; not urgent.
- **§9.5 no regions inside construct bodies.** `SubGraphExecutor` doesn't run region detection, so
  streaming edges inside a loop/branch body run as batch. Ties to the "sub-graph parity" theme
  ([plan 02 §12.4](02-engine-lifecycle-remediation.md), [plan 03 §10.3](03-binding-and-variables-remediation.md)).
  Low priority; most streaming is top-level.
  **Fixed 2026-11-09 (Stage 7.7, no decision needed):**
  - `SubGraphExecutor` detects regions over the whole definition (so the indices match the top
    level's) and keeps those wholly inside its scope.
  - It runs them through `StreamRegionExecutor`, exactly like `WorkflowExecutor`. A failure goes
    through `HandleNodeFailed`, so retry, continue, an error handler and the enclosing try/catch
    all apply.
  - Tests: `StreamRegionExecutionTests.Region_InsideALoopBody_*` (3) and
    `Regions_InParallelBranches_EachRunAsAPipeline`.
- **§9.7 `Blob` spill can land in an in-memory store.** `Program.cs` always registers an
  `InMemoryBlobStore` fallback (`TryAddSingleton`), and the default provider is SQLite `:memory:`, so
  `Streaming:Spill:Store=Blob` can resolve to an in-memory backend that gives no memory relief. Fix:
  the `Blob` spill wiring should reject an in-memory `IBlobStore` (fail fast), or require a durable
  persistence provider. Checkable by resolving `ISpillStore` from a host with Blob spill + default
  persistence and asserting it isn't in-memory-backed.
  **Fixed 2026-11-08 (Stage 7.1), fail fast.**
  - The real gap was the default SQLite `:memory:` provider. Its `SqliteBlobStore` passed the old
    `is InMemoryBlobStore` check.
  - New `IBlobStore.IsInMemory` (a default interface member, false), overridden by
    `InMemoryBlobStore` and by `SqliteBlobStore` from the connection string.
  - `BlobStoreSpillStore` refuses either, and the spill cleanup service resolves it at startup, so
    the host fails to start.
  - "Require a durable provider" turned out to be the same change, so no decision was needed.
  - Tests: `Streaming/BlobSpillDurabilityTests` (8).

---

## Sequencing
A (pin) is a trivial, isolated fix — do it anytime. B (spill cleanup) needs the B1-vs-B2 decision
first. The documented-only items mostly ride along with Phase 5.1.5 (progress, timeout) or the
sub-graph parity work (§9.5); §9.7 is a small DI hardening worth doing with B.
