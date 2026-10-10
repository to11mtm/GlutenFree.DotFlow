# Remediation plans

Fix plans for the findings verified in
[`../README.md#findings-at-a-glance`](../README.md#findings-at-a-glance). Each finding was reproduced
by a red test in [`Workflow.Tests/Findings/`](../../../Workflow.Tests/Findings/README.md) on
2026-09-29 before any plan was written.

Each plan states, per finding: the evidence (with the guard test), the root cause, the options with
trade-offs, a recommendation, acceptance checks, and blast radius. They are proposals — nothing is
built yet.

| Plan | Covers | Severity |
| --- | --- | --- |
| [01 — Script sandbox](01-security-sandbox-remediation.md) | JS CLR interop (§9.1), C# reflection bypass (§9.2) | Security / High |
| [02 — Engine lifecycle](02-engine-lifecycle-remediation.md) | loop done-port skip (§12.9), SignalR completion events (§12.1), executor never stops (§12.2), body retry (§12.4), stub module (§12.5), cancel (§12.3), UseErrorHandler (§12.7) | High / Medium |
| [03 — Binding & variables](03-binding-and-variables-remediation.md) | non-adjacent refs (§10.1), variable shadowing (§10.2), loop-body writes (§10.3), secrets in history (§10.6) | Medium / Low |
| [04 — Streaming & spill](04-streaming-and-spill-remediation.md) | pinned version in region (§9.4), spill cleanup w/o history (§9.6), + documented-only streaming items | Medium / Low |
| [05 — Modules & plugins](05-modules-and-plugins-remediation.md) | active-exec count (§9.3), ValidateConfiguration (§9.4), package extra modules (§9.7) | Medium / Low |

## Coverage

Every **confirmed** finding now has a plan. Cross-cutting threads to keep in mind while scheduling:

- **Sub-graph parity** — [§12.4 body error handling](02-engine-lifecycle-remediation.md) and
  [§10.3 body variable writes](03-binding-and-variables-remediation.md) (and
  [§9.5 regions in bodies](04-streaming-and-spill-remediation.md)) are all "`SubGraphExecutor` should
  behave like `WorkflowExecutor`." Do them as one extraction, not three.
- **Terminal events** — [§12.1](02-engine-lifecycle-remediation.md) unblocks
  [§9.3 active-exec count](05-modules-and-plugins-remediation.md); and the broadcast must redact
  [§10.6 secrets](03-binding-and-variables-remediation.md).
- **Supply chain** — [§9.1/§9.2 sandbox](01-security-sandbox-remediation.md),
  [§9.5 strong-name, §9.6 SSRF](01-security-sandbox-remediation.md#related-not-in-this-plan), and
  [§9.7 package module ids](05-modules-and-plugins-remediation.md) form one "script/plugin trust"
  story.
- **Loop done-port (§12.9)** blocks a clean §10.3 test — do it first.

## Workflow

1. Reproduce with a `Category=Finding` test (done).
2. Write / update the plan here.
3. Implement behind that test plus the existing suite.
4. On landing: delete the finding test (or drop its `Finding` trait to keep it as a regression test)
   and remove the row from `../README.md`.
