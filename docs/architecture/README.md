# DotFlow architecture docs

Internal architecture reference for GlutenFree.DotFlow: how the system is put together, and why.
These pages sit next to the user guides in [`../`](../README.md). They describe the machinery
behind those guides, not how to use the product.

Everything here was written from the source at commit `7b8011e` (branch `feature/FSL`). Each page
names its source files at the top. When code and docs disagree, trust the code, then fix the page.

| # | Page | Read it when you want to know… |
| --- | --- | --- |
| 00 | [Architecture overview](00-overview.md) · [visual version](overview.html) | what the processes, subsystems and projects are, and how they fit together |
| 01 | [Execution lifecycle](01-execution-lifecycle.md) | what happens between "Run" and "Completed": actors, states, retries, cancel, events |
| 02 | [Data binding & variables](02-data-binding-and-variables.md) | how a value gets from one node into the next module, and where variables come from |
| 03 | [Streaming & spill](03-streaming-and-spill.md) | how streaming regions run with backpressure, and how spill-to-disk works |
| 04 | [Modules, plugins & scripting](04-modules-plugins-and-scripting.md) | the module contract, `.wfmod` packages, loading, hot reload, script sandboxes, Roslyn |

Diagrams are Mermaid, so they render on GitHub and in most IDE Markdown previews. `overview.html`
is a standalone page with hand-drawn SVG. Open it in a browser.

---

## Findings at a glance

> **Verified against tests, 2026-09-29.** The findings below were reproduced with
> `Workflow.Tests/Findings/` (run `dotnet test Workflow.Tests --filter Category=Finding`). Result:
> **17 confirmed (one newly discovered), 2 refuted.**
>
> - **Refuted (removed):** the join-skip case (01 §12.6) did not reproduce — a join with one
>   completed input runs correctly. Port-aware routing *inside* a loop/branch body also works, so
>   01 §12.4 is narrowed to **per-node retry/continue** being dropped in bodies, not branching.
> - **Newly discovered while testing §10.3:** a node on a loop's `done` port is swallowed into the
>   loop body scope and skipped, so **post-loop nodes never run** (`HandleLoopCompleted` passes all
>   outgoing targets to `ComputeBodyScope`). Confirmed with the real `builtin.loop.foreach` in
>   `EngineFindingTests.LoopDonePort_ShouldRunPostLoopNode`. This also blocks a clean §10.3 test.
> - Everything else in the table is **confirmed** by a red test asserting the correct behaviour.

Writing these pages meant reading most of the engine closely, and some things didn't add up. Each
page ends with a **Findings to verify** section that gives the evidence. They came from reading
the code, not running it, so each one should get a failing test before it gets a fix.

| Area | Finding | Severity | Where |
| --- | --- | :-: | --- |
| Security | JavaScript executor enables Jint `AllowClr()`. Scripts may reach `System.IO` / `System.Net` directly and bypass `allowNetwork` / `allowFileSystem`. | **High** | [04 §9.1](04-modules-plugins-and-scripting.md#9-findings-to-verify) |
| Security | The C# script / LINQ "sandbox" is an identifier blocklist over the full BCL. Reflection (`Type.GetType(...).GetMethod(...).Invoke`) gets past it. | **High** | [04 §9.2](04-modules-plugins-and-scripting.md#9-findings-to-verify) |
| Engine | ~~`WorkflowCompleted` / `Failed` and node completion events are never published to the `EventStream`, so SignalR clients don't get completion events.~~ **Fixed (§12.1)** — the executor publishes slim copies (no outputs); cancel is reported as `ExecutionFailed` for now. | — | [01 §12.1](01-execution-lifecycle.md#12-findings-to-verify) |
| Engine | ~~`WorkflowExecutor` actors are never stopped. The actor tree and memory grow with every run.~~ **Fixed (§12.2)** — a finished executor stops once its persistence writes are acked and a grace period (`ExecutorLifecycleOptions`, default 60s) passes; paused runs never stop. | — | [01 §12.2](01-execution-lifecycle.md#12-findings-to-verify) |
| Engine | Nodes inside construct bodies ignore `Retry`/`Continue` (confirmed). Branching *does* work in bodies, so the "flattened" claim is withdrawn. | **High** | [01 §12.4](01-execution-lifecycle.md#12-findings-to-verify) |
| Engine | ~~An unregistered module id runs a stub that *succeeds*.~~ **Fixed (§12.5)** — the node fails, and `WorkflowSupervisor` rejects unknown module ids at submit time. | — | [01 §12.5](01-execution-lifecycle.md#12-findings-to-verify) |
| Engine | ~~A node wired to a loop's `done` port is swallowed into the body scope and **skipped** — post-loop nodes never run.~~ **Fixed (§12.9)** — `HandleLoopCompleted` scopes the body from body-port targets only; guarded by `Engine/LoopCompletionTests`. | — | [02 remediation](plans/02-engine-lifecycle-remediation.md) |
| Engine | User cancel doesn't cancel `_executionCts`. Running loops and parallel blocks continue. | Medium | [01 §12.3](01-execution-lifecycle.md#12-findings-to-verify) |
| Binding | Variables inside loop and branch bodies are a start-of-construct snapshot. `SetVariable` writes there are dropped. | Medium | [plan 03](plans/03-binding-and-variables-remediation.md) |
| Binding | `{{nodeId.port}}` only resolves direct predecessors. `../variables.md` says it reaches further upstream. | Medium | [plan 03](plans/03-binding-and-variables-remediation.md) |
| Ops | The active-execution count is only decremented for sync runs, so `/metrics` is wrong and every hot reload times out. | Medium | [plan 05](plans/05-modules-and-plugins-remediation.md) |
| Security | Secrets (run inputs) are persisted in plain text in every node's history `Inputs`. Variable values are logged. | Medium | [plan 03](plans/03-binding-and-variables-remediation.md) |
| Security | Strong-name token checks don't prove authorship. `RequireSigned` can be satisfied by copying a public key. | Medium | [04 §9.5](04-modules-plugins-and-scripting.md#9-findings-to-verify) |
| Security | Script HTTP has no host allowlist (SSRF). | Medium | [04 §9.6](04-modules-plugins-and-scripting.md#9-findings-to-verify) |
| Streaming | `Blob` spill can write to an in-memory store, which gives no memory relief. | Medium | [plan 04](plans/04-streaming-and-spill-remediation.md) |
| Streaming | Retrying a failed mid-pipeline stage can leave the run stuck in `Retrying`. | Medium | [plan 04](plans/04-streaming-and-spill-remediation.md) |
| Binding | Plain input keys (incl. port values) shadow real variables of the same name. | Low | [plan 03](plans/03-binding-and-variables-remediation.md) |
| Engine | ~~A join can be skipped despite a completed input.~~ **Refuted** — did not reproduce. | — | [01 §12.6](01-execution-lifecycle.md#12-findings-to-verify) |
| Modules | `IWorkflowModule.ValidateConfiguration` is implemented by many modules but never called. | Low | [plan 05](plans/05-modules-and-plugins-remediation.md) |
| Modules | An uploaded assembly can register more module ids than its manifest declares. | Low | [plan 05](plans/05-modules-and-plugins-remediation.md) |
| Streaming | No timeout for regions. Pinned versions are ignored. No regions inside construct bodies. | Low | [plan 04](plans/04-streaming-and-spill-remediation.md) |
| Streaming | `StreamRegionProgress` isn't forwarded to SignalR. Spill is kept forever when no history is registered. | Low | [plan 04](plans/04-streaming-and-spill-remediation.md) |
| Binding | `ConnectionDefinition.Condition` / `Priority` are never read. Expression evaluation blocks the node actor. | Low | [plan 03](plans/03-binding-and-variables-remediation.md) |
| Engine | `UseErrorHandler` isn't implemented. Scheduled and Event triggers have no runtime. | Info | [01 §12.7–8](01-execution-lifecycle.md#12-findings-to-verify) |

---

## Remediation plans

Fix plans for the confirmed findings live in [`plans/`](plans/README.md). Security and High-severity
items are planned first ([01 script sandbox](plans/01-security-sandbox-remediation.md),
[02 engine lifecycle](plans/02-engine-lifecycle-remediation.md)); the rest are listed there awaiting
scheduling.

## Keeping these pages honest

- **When you change a subsystem, update its page in the same PR.** Refresh the commit hash in the
  header too.
- **When you fix a finding, remove it** from both the page and the table above. If the fix changes
  behaviour, add a line to the relevant section instead.
- **Keep the pages mechanism-first.** They explain *how* the code works. Feature usage belongs in
  the user guides one level up.
