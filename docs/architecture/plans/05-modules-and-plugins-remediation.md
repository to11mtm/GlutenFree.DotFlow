# Plan 05 — Modules & plugins remediation

> Fix plan for the confirmed module/plugin findings (non-security) from
> `Workflow.Tests/Findings/Modules/*` (2026-09-29). The two **security** module findings (JS interop,
> C# reflection) are in [plan 01](01-security-sandbox-remediation.md). Source:
> `Workflow.Api/Execution/ActorWorkflowExecutionService.cs`,
> `Workflow.Api/Observability/*`, `Workflow.Api/Modules/ModuleHostAdapters.cs`,
> `Workflow.Modules/Validation/ModuleAwareWorkflowValidator.cs`,
> `Workflow.Modules/{Loading/AssemblyModuleLoader,Discovery/ModuleDiscovery,Packaging/ModulePackageInstaller}.cs`.
>
> Up: [architecture findings](../README.md#findings-at-a-glance) ·
> [04 — Modules, plugins & scripting §9](../04-modules-plugins-and-scripting.md#9-findings-to-verify)

**Status:** proposed. Nothing built yet.

| # | Finding | Sev | Guard test |
| --- | --- | :-: | --- |
| A | Active-execution counter only drops for sync runs (§9.3) | Med | `ActiveExecutionCountFindingTests.AsyncRun_WhenComplete_…` |
| B | `IWorkflowModule.ValidateConfiguration` never called (§9.4) | Low | `ValidateConfigurationFindingTests.Validator_ShouldSurface_…` |
| C | A package registers modules its manifest didn't declare (§9.7) | Low | `PackageExtraModulesFindingTests.Install_ShouldNotRegister_…` |

---

## Finding A — active-execution counter only drops for sync runs (§9.3)

### Evidence
- `ActorWorkflowExecutionService.StartAsync` calls `metrics.RecordStarted()` for every run, but
  `RecordCompleted/Failed/Cancelled` only happen in `StartAndWaitAsync` (the sync path). Webhook
  launches (`ActorWorkflowLauncher`) never record at all.
- `AsyncRun_WhenComplete_ShouldReturnActiveCountToZero` → `executions_active` stays `1` after a
  completed async run.
- Fallout: `/metrics` `executions_active` is wrong, and `MetricsActiveExecutionTracker.HasActiveExecutions`
  (the hot-reload drain gate) reports activity forever, so **every hot reload times out** after the
  first async run.

### Root cause
Terminal accounting lives only on the synchronous wait path. There is no single place that observes
"a run reached a terminal state" for all entry points.

### Options
- **A1 (recommended): record terminal from engine events.** Once [01 §12.1](02-engine-lifecycle-remediation.md)
  publishes terminal events to the `EventStream`, add a small hosted listener (or extend
  `ExecutionEventBridge`) that calls `RecordCompleted/Failed/Cancelled` on `IWorkflowMetrics` for
  every run, regardless of entry point. One source of truth; fixes `/metrics` and the reload gate.
- A2: have the supervisor/executor notify metrics directly on terminal. Works without §12.1 but
  couples the engine to the metrics seam.

### Recommendation
**A1**, sequenced after §12.1 (it needs the same terminal events). Until then, note the hot-reload
drain gate is effectively broken after the first async run.

### Acceptance
`AsyncRun_WhenComplete_…` green; a webhook-launched run also returns the count to 0; a hot reload
succeeds after an async run has completed (ties to the reload path).

**Effort:** ~1 day (on top of §12.1).

**Status — fixed 2026-11-08 (A1).** `Workflow.Api/Observability/ExecutionMetricsRecorder` (hosted)
subscribes to `WorkflowCompleted` / `WorkflowFailed` on the EventStream and records completed /
failed / cancelled (a cancel is `WorkflowFailed` + `OperationCanceledException`) for every run.
"Started" stays at submission — `ActorWorkflowExecutionService.StartAsync` and now
`ActorWorkflowLauncher` (webhooks, which recorded nothing before). The wait-for-result path no
longer records terminal outcomes, so sync runs aren't double-counted. Tests:
`Api/V1/ExecutionMetricsTests` (async, sync-once, webhook, failed, cancelled, drain gate).

---

## Finding B — `ValidateConfiguration` is never called (§9.4)

### Evidence
- Many built-ins implement `IWorkflowModule.ValidateConfiguration`, but neither the validators nor
  the engine invoke it.
- `Validator_ShouldSurface_ModuleValidateConfigurationErrors` → a module that rejects its config still
  validates clean.

### Root cause
The hook exists on the interface but has no caller.

### Options
- **B1 (recommended): call it in `ModuleAwareWorkflowValidator`.** For each node, after the existing
  structural + schema checks, call `module.ValidateConfiguration(nodeProperties)` and merge its
  `ValidationResult`. Errors then surface at **save/import**, where the validator already runs.
- B2: also call it in `NodeExecutor` before execution (defence in depth for definitions that bypassed
  save-time validation, e.g. direct supervisor submission).

### Recommendation
**B1 now, B2 optionally.** Audit the built-ins' `ValidateConfiguration` implementations first — some
may be stricter than real usage and would start rejecting currently-valid workflows. Fix or relax
those in the same change.

### Acceptance
`Validator_ShouldSurface_…` green; existing `ModuleAwareWorkflowValidatorTests`,
`ModuleValidatorTests`, and the built-in module suites green (i.e. no legitimate config starts
failing); add a couple of per-module config-rejection tests for modules that have real rules.

### Risk / blast radius
Medium — turning on a dormant validation hook can reject workflows that were silently accepted. The
audit is the de-risking step.

**Effort:** ~1–2 days including the built-in audit.

**Audit (2026-11-08), 44 implementations:**
- *Value checks only when present* (Delay, End, Log, Start, Get/SetVariable, FanIn, FanOut, Throw,
  HTTP, DB timeouts, File read/write options, Compress format) — low risk; only a `{{template}}` in
  an enum-style field would be misjudged.
- *Required properties* (File/CSV/JSON/XML read+write, Compress/Decompress, S3, Azure Blob, DB
  query/execute/bulk/transaction, Script, Switch, Partition, Parallel, Split, DataMap, DataJoin,
  Aggregate/JSON/String transforms, JsonQuery, XmlQuery, JsonValue, ValidateData, WebhookTrigger)
  — they match each schema's `IsRequired`, and modules read these only from properties (never from
  input ports), so they'd fail at run time anyway. Risk: as save-time *errors* they'd block saving
  half-built workflows (create/update return 422 on errors).
- *Set after save*: LINQ query + Transform Script require `compiledAssemblyKey` ("compile at
  publish first") — wrong as a save-time check.
- *Bugs*: FanIn, Parallel, Partition, Switch, Split passed the property name in the `NodeId` slot;
  ValidateData set no property.
- Probe (B1 wired in temporarily, run-time-shaped config): 0 rejections across the 1820-test
  suite. `examples/definitions/*.json` couldn't be checked — they're stale (non-GUID ids, retired
  module ids).

**Decided + fixed 2026-11-08.** Policy (asked): **warn on save, block on run**; `compiledAssemblyKey`
checked **only at run**. `Workflow.Modules/Validation/ModuleConfigurationChecker` builds the config
like run time (JSON → CLR as NodeExecutor does, schema defaults as the binder applies them), ignores
errors on `{{…}}`-valued properties, and skips `RunOnlyProperties` on save.
`ModuleAwareWorkflowValidator` adds its findings as **warnings**; `WorkflowSupervisor` adds them as
**errors** at submit (API → 400 with the module's message; webhook → launch error). The five
NodeId-slot bugs and ValidateData's missing property are fixed. B2 (NodeExecutor) not done — the
submit check covers the API and webhooks. Tests: `Modules/ModuleConfigurationValidationTests` (10).

---

## Finding C — a package registers modules its manifest didn't declare (§9.7)

### Evidence
- `AssemblyModuleLoader.LoadFromAssembly` calls `ModuleDiscovery.DiscoverAndRegister(assembly, registry)`,
  which registers **every** valid `IWorkflowModule` in the assembly.
- `ModulePackageInstaller.InstallAsync` then only checks that the manifest's `Id` is **among** the
  loaded modules — it doesn't restrict registration to it.
- `Install_ShouldNotRegister_ModulesNotDeclaredInManifest` → installing a package declaring only
  `sample.log` also registered `sample.delay`.

### Root cause
Discovery is "register all," and the installer treats the manifest id as a presence check, not an
allow-list.

### Options
- **C1 (recommended): register only declared modules.** After discovery, register only the id(s) the
  manifest declares; drop the rest. Requires deciding multi-module packaging:
  `ModuleManifest.Id` is **singular** today — either keep packages single-module (register exactly
  that id, ignore/reject extras) or extend the manifest to a declared **list** of ids.
- C2: **reject** a package whose assembly exposes undeclared ids (fail the install). Strictest; good
  for a trusted-publisher model.

### Recommendation
**C1 with a decision on multi-module manifests.** If single-module packaging is the intent, register
only the declared id and **log** (or reject, per policy) extras. If multi-module is wanted, add
`ModuleManifest.Modules` (a declared id list) and register exactly that set. Pair with the
trusted-publisher direction in [plan 01](01-security-sandbox-remediation.md) (strong-name / Authenticode)
— "which ids a package may register" is part of the same supply-chain story.

### Acceptance
`Install_ShouldNotRegister_…` green; a declared single-module package registers exactly its id;
(if multi-module adopted) a package declaring several registers exactly those; `ModulePackageTests`
green.

### Risk / blast radius
Low-medium. Existing multi-module packages (if any) would need their manifest updated; surface that in
release notes.

**Effort:** ~1–2 days depending on the single- vs multi-module decision.

**Decided + fixed 2026-11-08 — multi-module manifests (C1).** Decisions (asked):
- **Manifest:** optional `modules: [ids]`. Absent/empty → the package provides exactly `id` (the
  original format, unchanged). When present, `id` is the *package* id (install directory, uninstall
  key) and needn't be a module id. Duplicates / blank entries rejected (MP010, MP011).
- **Registration:** `AssemblyModuleLoader.LoadFromAssembly(path, onlyModuleIds)` →
  `ModuleDiscovery.DiscoverAndRegister(..., include, excludedModuleIds)` registers only declared
  ids — at install and at rehydrate. Undeclared modules in the assembly are skipped and listed in a
  warning. The `IModuleLoader` overload fails closed (throws) for loaders that can't filter.
- **All declared modules must exist and report the manifest version** (one release = one version;
  side-by-side installs and pinning stay keyed by id + version). Otherwise the install fails and
  rolls back.
- **Disabling one module:** the existing `POST /modules/{id}/disable[?version=]` works per module,
  so a module inside a package can be switched off on its own. Found + fixed while testing this:
  disabled state was never actually persisted for a module whose only version was disabled
  (`GetAllModules` skips those), so it came back enabled after a restart — persistence now uses the
  new `IModuleRegistry.GetAllModuleIds()`.
- **Uninstall:** by package id removes all its modules (the "others depend on it" check covers every
  module in the package). Uninstalling a module that's one of several in a package → **409** pointing
  to `/modules/{id}/disable` or the package.
Tests: `Api/V1/MultiModulePackageTests` (9). Docs: module author guide → `modules`.

---

## Related (in other plans)
- Script sandbox escapes (04 §9.1 JS, §9.2 C#) → [plan 01](01-security-sandbox-remediation.md).
- Script HTTP host allow-list / SSRF (04 §9.6) and strong-name-tokens-aren't-authorship (04 §9.5) →
  listed as "related" in [plan 01](01-security-sandbox-remediation.md#related-not-in-this-plan);
  fold into a "script/plugin trust" plan with Finding C above.

## Sequencing
B and C are independent and small — schedule anytime. A depends on the §12.1 terminal-events fix, so
it rides with [plan 02](02-engine-lifecycle-remediation.md).
