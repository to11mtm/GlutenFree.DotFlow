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
| Security | ~~JavaScript executor enables Jint `AllowClr()`. Scripts may reach `System.IO` / `System.Net` directly and bypass `allowNetwork` / `allowFileSystem`.~~ **Fixed (§9.1)** — `AllowClr()` removed and `Interop.AllowGetType = false`; `workflow.*` unaffected. | — | [04 §9.1](04-modules-plugins-and-scripting.md#9-findings-to-verify) |
| Security | ~~The C# script / LINQ "sandbox" is an identifier blocklist over the full BCL. Reflection (`Type.GetType(...).GetMethod(...).Invoke`) gets past it.~~ **Fixed (§9.2)** — a semantic check flags every sensitive API the code binds to (reflection, IO, network, process, interop, …) in both compilers; flagged code only compiles once a `ScriptApprover` (not the author) approves that exact code. See [plan 01](plans/01-security-sandbox-remediation.md). | — | [04 §9.2](04-modules-plugins-and-scripting.md#9-findings-to-verify) |
| Engine | ~~`WorkflowCompleted` / `Failed` and node completion events are never published to the `EventStream`, so SignalR clients don't get completion events.~~ **Fixed (§12.1)** — the executor publishes slim copies (no outputs); cancel is reported as `ExecutionFailed` for now. | — | [01 §12.1](01-execution-lifecycle.md#12-findings-to-verify) |
| Engine | ~~`WorkflowExecutor` actors are never stopped. The actor tree and memory grow with every run.~~ **Fixed (§12.2)** — a finished executor stops once its persistence writes are acked and a grace period (`ExecutorLifecycleOptions`, default 60s) passes; paused runs never stop. | — | [01 §12.2](01-execution-lifecycle.md#12-findings-to-verify) |
| Engine | ~~Nodes inside construct bodies ignore `Retry`/`Continue`.~~ **Fixed (§12.4)** — bodies share the executor's failure handling (`NodeFailureCore`). Branching works in bodies; constructs *nested* in a body work too since §12.10 (below). | — | [01 §12.4](01-execution-lifecycle.md#12-findings-to-verify) |
| Engine | ~~A construct nested inside a body (loop in a loop, try/catch in a loop, …) runs as a **plain node**: `SubGraphExecutor` ignores `Node*ExecutionRequested`, so every outgoing edge fires once.~~ **Fixed (§12.10)**: `SubGraphExecutor` spawns nested constructs through the shared `ConstructSpawnCore`. Break/continue bind to the innermost loop. Break/continue inside a try/catch, parallel or transaction in a loop body reach that loop. | — | [plan 02 §G](plans/02-engine-lifecycle-remediation.md#finding-g-high--a-construct-nested-inside-a-body-runs-as-a-plain-node-1210) |
| Engine | ~~An unregistered module id runs a stub that *succeeds*.~~ **Fixed (§12.5)** — the node fails, and `WorkflowSupervisor` rejects unknown module ids at submit time. | — | [01 §12.5](01-execution-lifecycle.md#12-findings-to-verify) |
| Engine | ~~A node wired to a loop's `done` port is swallowed into the body scope and **skipped** — post-loop nodes never run.~~ **Fixed (§12.9)** — `HandleLoopCompleted` scopes the body from body-port targets only; guarded by `Engine/LoopCompletionTests`. | — | [02 remediation](plans/02-engine-lifecycle-remediation.md) |
| Engine | ~~User cancel doesn't cancel `_executionCts`. Running loops and parallel blocks continue.~~ **Fixed (§12.3)** — cancel cancels `_executionCts` (constructs stop cooperatively), and late node/construct replies after a terminal state are ignored. | — | [01 §12.3](01-execution-lifecycle.md#12-findings-to-verify) |
| Binding | ~~Variables inside loop and branch bodies are a start-of-construct snapshot. `SetVariable` writes there are dropped.~~ **Fixed (§10.3)** — body writes propagate: sequential bodies see earlier writes, parallel branches merge in branch order, writes before a failure are kept. | — | [plan 03](plans/03-binding-and-variables-remediation.md) |
| Binding | ~~`{{nodeId.port}}` only resolves direct predecessors. `../variables.md` says it reaches further upstream.~~ **Fixed (§10.1)**: resolves for any ancestor that has run, across construct bodies too. The designer and save-time validator flag references to non-ancestors, with "wire it in" / "via a variable" fixes. | — | [plan 03](plans/03-binding-and-variables-remediation.md) |
| Ops | ~~The active-execution count is only decremented for sync runs, so `/metrics` is wrong and every hot reload times out.~~ **Fixed (§9.3)** — `ExecutionMetricsRecorder` records every run's terminal outcome from the engine's EventStream; webhook launches count as started. | — | [plan 05](plans/05-modules-and-plugins-remediation.md) |
| Security | ~~Secrets (run inputs) are persisted in plain text in every node's history `Inputs`. Variable values are logged.~~ **Fixed (§10.6)**: secret variables are hidden by name and value in history, run variable writes, logs and live failure events. Storage at rest is still plain text (by decision). | — | [plan 03](plans/03-binding-and-variables-remediation.md) |
| Security | Strong-name token checks don't prove authorship. `RequireSigned` can be satisfied by copying a public key. | Medium | [04 §9.5](04-modules-plugins-and-scripting.md#9-findings-to-verify) |
| Security | Script HTTP has no host allowlist (SSRF). | Medium | [04 §9.6](04-modules-plugins-and-scripting.md#9-findings-to-verify) |
| Streaming | ~~`Blob` spill can write to an in-memory store, which gives no memory relief.~~ **Fixed (Stage 7.1)**: a blob store that lives in memory (the dev fallback, or SQLite `:memory:`) is refused, so the host won't start with `Store=Blob` on it. | — | [plan 04](plans/04-streaming-and-spill-remediation.md) |
| Streaming | ~~Retrying a failed mid-pipeline stage can leave the run stuck in `Retrying`.~~ Re-checked: no stall. A region failure blames the source, so a retry re-runs the whole region. Only stage-level blame is missing. | Low | [plan 04](plans/04-streaming-and-spill-remediation.md) |
| Binding | ~~Plain input keys (incl. port values) shadow real variables of the same name.~~ **Fixed (§10.2)** — port values aren't variables; loop/branch/catch bodies get `item`/`index`/`error` as body variables; the designer and save-time validator flag `{{Variable.x}}` vs a wired port `x`, with fixes. | — | [plan 03](plans/03-binding-and-variables-remediation.md) |
| Engine | ~~A join can be skipped despite a completed input.~~ **Refuted** — did not reproduce. | — | [01 §12.6](01-execution-lifecycle.md#12-findings-to-verify) |
| Modules | ~~`IWorkflowModule.ValidateConfiguration` is implemented by many modules but never called.~~ **Fixed (§9.4)** — warnings on save, errors when a run is submitted (`ModuleConfigurationChecker`). | — | [plan 05](plans/05-modules-and-plugins-remediation.md) |
| Modules | ~~An uploaded assembly can register more module ids than its manifest declares.~~ **Fixed (§9.7)** — a package registers exactly its declared modules (manifest `id`, or the new `modules` list); others are skipped with a warning. | — | [plan 05](plans/05-modules-and-plugins-remediation.md) |
| Streaming | No timeout for regions. ~~Pinned versions are ignored.~~ **Fixed (§9.4)** — regions run (and are detected from) the pinned version, via the shared `ModuleResolution`. No regions inside construct bodies. | Low | [plan 04](plans/04-streaming-and-spill-remediation.md) |
| Streaming | `StreamRegionProgress` isn't forwarded to SignalR. ~~Spill is kept forever when no history is registered.~~ **Fixed (§9.6)**: abandoned spill is reclaimed by age, with a startup warning. | Low | [plan 04](plans/04-streaming-and-spill-remediation.md) |
| Binding | `ConnectionDefinition.Condition` / `Priority` are never read. Expression evaluation blocks the node actor. | Low | [plan 03](plans/03-binding-and-variables-remediation.md) |
| Engine | ~~`UseErrorHandler` isn't implemented.~~ **Fixed (§12.7)**, see [error handling](../error-handling.md). Scheduled and Event triggers have no runtime. | Info | [01 §12.7–8](01-execution-lifecycle.md#12-findings-to-verify) |

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
