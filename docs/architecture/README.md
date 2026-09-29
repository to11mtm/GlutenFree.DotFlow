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

Writing these pages meant reading most of the engine closely, and some things didn't add up. Each
page ends with a **Findings to verify** section that gives the evidence. They came from reading
the code, not running it, so each one should get a failing test before it gets a fix.

| Area | Finding | Severity | Where |
| --- | --- | :-: | --- |
| Security | JavaScript executor enables Jint `AllowClr()`. Scripts may reach `System.IO` / `System.Net` directly and bypass `allowNetwork` / `allowFileSystem`. | **High** | [04 §9.1](04-modules-plugins-and-scripting.md#9-findings-to-verify) |
| Security | The C# script / LINQ "sandbox" is an identifier blocklist over the full BCL. Reflection (`Type.GetType(...).GetMethod(...).Invoke`) gets past it. | **High** | [04 §9.2](04-modules-plugins-and-scripting.md#9-findings-to-verify) |
| Engine | `WorkflowCompleted` / `Failed` and node completion events are never published to the `EventStream`, so SignalR clients don't get completion events. | **High** | [01 §12.1](01-execution-lifecycle.md#12-findings-to-verify) |
| Engine | `WorkflowExecutor` actors are never stopped. The actor tree and memory grow with every run. | **High** | [01 §12.2](01-execution-lifecycle.md#12-findings-to-verify) |
| Engine | Nodes inside construct bodies ignore `Retry`/`Continue`. Constructs nested in a body are silently flattened into plain nodes. | **High** | [01 §12.4](01-execution-lifecycle.md#12-findings-to-verify) |
| Engine | An unregistered module id runs a stub that *succeeds*. | **High** | [01 §12.5](01-execution-lifecycle.md#12-findings-to-verify) |
| Engine | User cancel doesn't cancel `_executionCts`. Running loops and parallel blocks continue. | Medium | [01 §12.3](01-execution-lifecycle.md#12-findings-to-verify) |
| Binding | Variables inside loop and branch bodies are a start-of-construct snapshot. `SetVariable` writes there are dropped. | Medium | [02 §10.3](02-data-binding-and-variables.md#10-findings-to-verify) |
| Binding | `{{nodeId.port}}` only resolves direct predecessors. `../variables.md` says it reaches further upstream. | Medium | [02 §10.1](02-data-binding-and-variables.md#10-findings-to-verify) |
| Ops | The active-execution count is only decremented for sync runs, so `/metrics` is wrong and every hot reload times out. | Medium | [04 §9.3](04-modules-plugins-and-scripting.md#9-findings-to-verify) |
| Security | Secrets (run inputs) are persisted in plain text in every node's history `Inputs`. Variable values are logged. | Medium | [02 §10.6](02-data-binding-and-variables.md#10-findings-to-verify) |
| Security | Strong-name token checks don't prove authorship. `RequireSigned` can be satisfied by copying a public key. | Medium | [04 §9.5](04-modules-plugins-and-scripting.md#9-findings-to-verify) |
| Security | Script HTTP has no host allowlist (SSRF). | Medium | [04 §9.6](04-modules-plugins-and-scripting.md#9-findings-to-verify) |
| Streaming | `Blob` spill can write to an in-memory store, which gives no memory relief. | Medium | [03 §9.7](03-streaming-and-spill.md#9-findings-to-verify) |
| Streaming | Retrying a failed mid-pipeline stage can leave the run stuck in `Retrying`. | Medium | [03 §9.3](03-streaming-and-spill.md#9-findings-to-verify) |
| Binding | Plain input keys (incl. port values) shadow real variables of the same name. | Low | [02 §10.2](02-data-binding-and-variables.md#10-findings-to-verify) |
| Engine | A join can be skipped despite a completed input, if another input's branch is deactivated later. | Low | [01 §12.6](01-execution-lifecycle.md#12-findings-to-verify) |
| Modules | `IWorkflowModule.ValidateConfiguration` is implemented by many modules but never called. | Low | [04 §9.4](04-modules-plugins-and-scripting.md#9-findings-to-verify) |
| Modules | An uploaded assembly can register more module ids than its manifest declares. | Low | [04 §9.7](04-modules-plugins-and-scripting.md#9-findings-to-verify) |
| Streaming | No timeout for regions. Pinned versions are ignored. No regions inside construct bodies. | Low | [03 §9](03-streaming-and-spill.md#9-findings-to-verify) |
| Streaming | `StreamRegionProgress` isn't forwarded to SignalR. Spill is kept forever when no history is registered. | Low | [03 §9](03-streaming-and-spill.md#9-findings-to-verify) |
| Binding | `ConnectionDefinition.Condition` / `Priority` are never read. Expression evaluation blocks the node actor. | Low | [02 §10](02-data-binding-and-variables.md#10-findings-to-verify) |
| Engine | `UseErrorHandler` isn't implemented. Scheduled and Event triggers have no runtime. | Info | [01 §12.7–8](01-execution-lifecycle.md#12-findings-to-verify) |

---

## Keeping these pages honest

- **When you change a subsystem, update its page in the same PR.** Refresh the commit hash in the
  header too.
- **When you fix a finding, remove it** from both the page and the table above. If the fix changes
  behaviour, add a line to the relevant section instead.
- **Keep the pages mechanism-first.** They explain *how* the code works. Feature usage belongs in
  the user guides one level up.
