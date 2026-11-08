# Architecture-finding tests

These tests exist to **evaluate** the concerns recorded in
[`docs/architecture/README.md#findings-at-a-glance`](../../docs/architecture/README.md). They were
written by reading the code, not running it, so each one is a hypothesis that needs confirming.

## How to read a result

Every test asserts the behaviour the code **should** have. So:

- **Red (fails)** → the concern is real. The test now reproduces it.
- **Green (passes)** → the code already does the right thing here; the concern was wrong or already
  handled. Consider deleting the test.

Nothing here is wired into any product path. They are safe to run: in-memory actors, stub modules,
in-memory SQLite hosts, and temp files that are cleaned up. No test touches the network, spawns a
process, or reads or writes outside its own temp directory. The two script-sandbox checks use only
benign probes (`typeof`, `Math.Max`, resolving the `StringBuilder` type) and never perform I/O.

## Running

Every test carries `[Trait("Category", "Finding")]`, so they are opt-in:

```bash
# run ONLY the finding tests
dotnet test Workflow.Tests --filter Category=Finding

# run everything EXCEPT the finding tests (keep CI green while these are open)
dotnet test Workflow.Tests --filter Category!=Finding

# run one area
dotnet test Workflow.Tests --filter "Category=Finding&FullyQualifiedName~Findings.Engine"
```

## What each file covers

| File | Findings | Kind |
| --- | --- | --- |
| `Engine/EngineFindingTests.cs` | 01 §12.2 stop, §12.3 cancel, §12.4 body retry + nesting, §12.5 stub module, §12.7 UseErrorHandler | actor |
| `Engine/RealtimeCompletionFindingTests.cs` | 01 §12.1 terminal/node events to SignalR | API + SignalR |
| `Engine/JoinSkipFindingTests.cs` | 01 §12.6 join skipped with a completed input | actor |
| ~~`Engine/NestedConstructFindingTests.cs`~~ | §12.10 (plan 02 §G) constructs nested in a body run as plain nodes. **Fixed, file retired**: guards now in `Engine/NestedConstructTests` | actor + built-in modules |
| `Binding/BindingFindingTests.cs` | 02 §10.1 non-adjacent refs, §10.3 loop-body variable writes (§10.2 shadowing — **fixed, test retired**: now `Engine/VariableShadowingTests`) | actor + binder |
| `Binding/SecretPersistenceFindingTests.cs` | 02 §10.6 secrets in history | actor + capturing repo |
| ~~`Streaming/StreamingFindingTests.cs`~~ | 03 §9.4 pinned version ignored in a region — **fixed, file retired**: now `Engine/Streaming/StreamRegionPinnedVersionTests` | actor |
| `Streaming/SpillCleanupFindingTests.cs` | 03 §9.6 spill kept forever with no history | unit |
| ~~`Modules/ActiveExecutionCountFindingTests.cs`~~ | 04 §9.3 active count only drops for sync runs — **fixed, file retired**: now `Api/V1/ExecutionMetricsTests` | API |
| ~~`Modules/ValidateConfigurationFindingTests.cs`~~ | 04 §9.4 ValidateConfiguration never called — **fixed, file retired**: now `Modules/ModuleConfigurationValidationTests` (agreed policy: warn on save, block on run) | unit |
| ~~`Modules/PackageExtraModulesFindingTests.cs`~~ | 04 §9.7 package registers undeclared module ids — **fixed, file retired**: now `Api/V1/MultiModulePackageTests` | packaging |
| ~~`Modules/ScriptSandboxFindingTests.cs`~~ | 04 §9.1 JS CLR interop, §9.2 C# reflection bypass — **both fixed, file retired**: guards now in `Scripting/JavaScriptScriptExecutorTests` (`Sandbox_*`) and `Scripting/ScriptApprovalTests` | scripting |

`FindingStubs.cs` holds the shared builders and stub modules; it has no tests of its own.

## Not covered by a test (documented only)

A few findings need a design decision before "correct behaviour" can be asserted, or can't be
exercised without a hanging/heavy setup. These stay in the docs, not here:

- 01 §12.8 — Scheduled / Event triggers have no runtime (nothing to assert against).
- 03 §9.1 — `StreamRegionProgress` isn't forwarded to SignalR (no client event contract exists yet).
- 03 §9.2 — regions have no timeout (a test would need a hanging module).
- 03 §9.3 — retrying a mid-pipeline stage: the current blame-the-source behaviour makes the exact
  repro ambiguous; revisit once stage-level blame exists.
- 03 §9.5 / §9.7 — no regions inside construct bodies; `Blob` spill can land in an in-memory store.
  Both are DI/wiring shapes better checked by inspection than a brittle test.

## When a finding is fixed

Delete its test (or flip it into a normal regression test without the `Finding` trait) and remove
the row from the table in `docs/architecture/README.md`.

## A note on two "correct behaviour" choices

Two tests encode a *stated intent* rather than a settled contract, so if the team decides the
current behaviour is actually right, delete the test instead of "fixing" the code:

- `SecretPersistenceFindingTests` assumes secret-flagged values should be redacted before they reach
  execution history. No redaction policy exists yet.
- `SpillCleanupFindingTests` follows the comment inside `DecideAsync` ("fall back to the abandoned
  threshold … keeping forever would be a guaranteed leak"), which the code contradicts.
