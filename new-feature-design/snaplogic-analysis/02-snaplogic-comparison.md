# 02 — DotFlow vs SnapLogic: Architectural Comparison 🔍

> Companion to [`01-dotflow-architecture.md`](01-dotflow-architecture.md). SnapLogic facts are
> from `docs.snaplogic.com` (verified 2026-08); DotFlow facts from this repository. Focus:
> **composition** — how integrations are built — plus Ultra pipelines, error pipelines, and
> Snaplex runtime topology per confirmed scope.

## 0. TL;DR

Both systems compose **directed graphs of single-purpose processing units** with named,
typed connection points and declarative configuration — the mental model ports well. The one
**deep semantic difference** is data flow: SnapLogic **streams many JSON documents** through a
pipeline per execution; DotFlow passes **one payload through the graph per execution** (batch-per-
trigger). Everything else — expressions, error handling, reuse, runtime — is a mappable
difference of degree.

## 1. Side-by-side overview

| Dimension | SnapLogic | GlutenFree.DotFlow |
| --- | --- | --- |
| Composition unit | **Pipeline** of **Snaps** | **WorkflowDefinition** of **nodes** (module instances) |
| Connection points | **Views** — circle=document, diamond=binary; min/max counts | **Ports** — named, typed by .NET `Type` via `PortDefinition` |
| Edges | Linking views on the canvas (`link_map`) | Explicit `ConnectionDefinition` (source/target node+port, optional condition, priority) |
| Data flow | **Stream of JSON documents**, one at a time, push-pull with partial backpressure | **One execution context per run**; per-node/port outputs bound to downstream inputs |
| Config surface | Snap Settings tab; expression-enabled fields | Node `Properties` (JSON); `SupportsTemplates` opt-in fields |
| Expressions | SnapLogic Expression Language (JS-like subset; `$field`, `_param`, `lib.*`) | `{{…}}` templates + sandboxed JavaScript (Jint); full JS/C#/Lua in script nodes |
| Parameters/state | Pipeline parameters (string-only, `_name`) + expression libraries | Typed **variables** in 3 scopes (global/workflow/execution) with versioned history |
| Reuse | **Pipeline Execute** (child pipelines, pooling, reuse mode) | ⚠️ no sub-workflow module yet (see gaps) |
| Error handling | Per-Snap error views; reusable **error pipelines** | Per-node `ErrorHandling`/`RetryPolicy`/`Timeout`; `builtin.trycatch` boundaries |
| Extensibility | Java Snaps in Snap Packs (core/premium/private) | .NET `IWorkflowModule` in `.wfmod` packages |
| Credentials | **Accounts** — separate scoped assets, injected at runtime | Secret-flagged variables (no separate credential asset type) |
| Runtime | Control plane (SaaS) + data plane (**Snaplex**: JCC nodes, load balancer, FeedMaster) | Single-process Akka.NET actor system behind one REST API |
| Triggers | Tasks: Triggered (HTTP), Scheduled (cron), Ultra (always-on) | `TriggerDefinition`: Manual, Scheduled, Webhook, Event |
| Definition format | `.slp` JSON (`snap_map`, `link_map`, `render_map`, `property_map`)* | Workflow JSON (nodes/connections/variables/trigger) |

\* `.slp` internals reconstructed from ecosystem evidence; the authoritative format page is
login-gated. Verify against a real export before building an importer.

## 2. The big one: streaming documents vs batch-per-trigger

**SnapLogic:** a pipeline execution processes *N* documents — whatever the source Snap emits
(a DB SELECT of 1M rows streams 1M documents). Every downstream Snap runs *per document*.
Synchronizing Snaps (Join, Gate) block or buffer; Gate explicitly warns about memory on large
sets. "One run" is a *stream session*, not a single traversal.

**DotFlow:** a trigger creates one `WorkflowExecutor`; the graph is traversed **once** with one
payload. Multiplicity is *explicit*: `builtin.loop.foreach` iterates a collection through a
`loopBody` sub-graph; `builtin.split`/`builtin.partition`/`builtin.parallel` fan work out;
`builtin.fanin`/`builtin.transform.aggregate` gather it back.

**Consequences:**

- A SnapLogic pipeline's implicit "per-document" semantics must become an **explicit ForEach
  (or partition + parallel) region** in DotFlow. This is the #1 porting transformation.
- DotFlow's model is simpler to reason about (one snapshot of state per run) and fits its
  variable/staged-write model; SnapLogic's scales to large data volumes without loading them
  into a single run context, but leaks operational concerns (sorted-stream Joins, Gate memory
  warnings, blocked-branch hangs) into pipeline design.
- DotFlow currently has no equivalent of *streaming* a large result set node-to-node without
  materializing it in the execution context. This is the most important architectural gap if
  large-volume ETL is a target workload (see §7).

## 3. Graph & port model

Close cousins, with three notable differences:

1. **Typed ports vs shaped views.** SnapLogic types views only as *document* vs *binary*
   (circle/diamond — "only matching shapes connect"). DotFlow ports carry a real .NET
   `DataType`, so the designer/engine can validate more than shape. DotFlow is *stricter*.
2. **Unlinked views are meaningful in SnapLogic** — an unlinked input view is the pipeline's
   entry point (used by Triggered Tasks, Ultra FeedMaster, Pipeline Execute reuse mode); error
   pipelines require exactly one. DotFlow instead uses explicit `builtin.start` /
   `builtin.http.webhook` nodes and run inputs. Porting: unlinked input view → Start/webhook
   node; unlinked output view → `builtin.end` / terminal node outputs.
3. **Conditional edges.** DotFlow connections can carry a `Condition` and `Priority`;
   SnapLogic routes exclusively through Snaps (Router/Filter). Porting direction is easy
   (Router → condition/switch); reverse would be lossy.

## 4. Expressions & configuration

| Aspect | SnapLogic | DotFlow |
| --- | --- | --- |
| Current data | `$field`, `$a.b[2]` | `{{input}}`, `{{input.a.b}}` (reserved root) or `{{nodeId.port}}` |
| Parameters | `_param` (always strings, alphanumeric, 8 MB cap) | `{{Variable.name}}` — typed, scoped, versioned |
| Language | JS-like subset: no assignment, no `===`, no `++`; arrow functions yes | Sandboxed real JavaScript (Jint) in `{{…}}`; full JS/C#/Lua in script nodes |
| Shared functions | `.expr` expression libraries (`lib.foo.bar()`) | no direct equivalent (script modules approximate) |
| Where allowed | any expression-enabled Snap field | only fields with `SupportsTemplates = true` (security boundary) |
| Failure mode | varies per Snap | unresolvable reference **fails the node** |

DotFlow's expression surface is deliberately narrower per-field but *more* powerful where
enabled (real JS, typed variables). SnapLogic's string-only parameters and `eval(_param)`
idioms are a common source of porting friction — DotFlow typed variables remove them.

## 5. Error handling

- **SnapLogic:** each Snap chooses stop / discard / route-to-error-view. A pipeline may
  designate a reusable **error pipeline** (own asset, one unlinked doc input) receiving error
  documents from linked Snaps. Error handling is *stream-shaped*: errors are just documents on
  another view.
- **DotFlow:** resilience is *declared* per node (`RetryPolicy`, `Timeout`, `ErrorHandling`
  with workflow-level default) and *scoped* via `builtin.trycatch` (`try`/`catch`/`finally`/
  `done` ports), executed under dedicated supervising actors.

Mapping: "route error to error view" ≈ wrapping the node in a trycatch whose `catch` branch is
the error path. A **reusable, cross-workflow error pipeline** has no DotFlow equivalent today —
error handling must be composed inside each workflow (gap, §7).

## 6. Runtime topology

- **SnapLogic** separates the **control plane** (SaaS: metadata, scheduling, monitoring — no
  customer data) from the **data plane** (**Snaplex** = JCC/JVM nodes + load balancer +
  optional FeedMaster queue; Cloudplex managed / Groundplex self-hosted). Execution scales out
  across nodes; the least-loaded node wins. **Ultra pipelines** keep always-on instances
  consuming from the FeedMaster queue (low-latency request/response) or run headless with
  listener Snaps; the Ultra monitor restarts failed instances.
- **DotFlow** is a single-process engine (`Workflow.Api` hosting REST + SignalR + Akka.NET),
  with the UI as a second process. Scale-out today = multiple API instances sharing persistence;
  Akka.NET clustering is a plausible future path but not wired up. There is no
  FeedMaster-style ingest queue; webhooks invoke executions directly. NATS persistence hints at
  queueing potential but isn't an execution feed.

Practical read: DotFlow ≈ a *self-hosted Groundplex + built-in control plane* in one process.
The gaps that matter operationally are horizontal execution scale-out and an Ultra-style
always-warm low-latency serving mode.

## 7. Gap analysis (DotFlow features to consider)

| Gap | SnapLogic feature | Severity for porting | Notes |
| --- | --- | --- | --- |
| **Sub-workflow / child execution node** | Pipeline Execute (pooling, reuse, batching, 64-deep) | 🔴 high | Most non-trivial SnapLogic estates use child pipelines heavily. A `builtin.workflow.execute` module is the single highest-value addition. |
| **Streaming data plane** | Per-document streaming between Snaps | 🔴 high for large ETL, 🟡 otherwise | Alternative: document "chunked ForEach" patterns; or add streaming ports later. |
| **Reusable error workflow** | Error pipelines | 🟡 medium | Could compose from sub-workflow node once it exists (trycatch catch → workflow.execute). |
| **Ultra-style always-on serving** | Ultra Tasks + FeedMaster | 🟡 medium | DotFlow webhooks cover request/response but with cold-start per run; no queue/instance pool. |
| **Expression libraries** | `.expr` libs, `lib.*` | 🟢 low | Shared script modules / a variable-of-functions pattern approximates. |
| **Credential asset type** | Accounts (scoped, OAuth2, secrets-manager refs) | 🟡 medium | Secret variables cover storage but not OAuth2 token lifecycle or per-project scoping. |
| **Resumable executions** | Resumable pipeline mode | 🟢 low | DotFlow persists terminal state; resume-from-failure would need mid-run checkpointing. |

DotFlow advantages worth *keeping* (not SnapLogic-isms to copy): typed ports & schema-driven
validation, control-flow-as-nodes with actor supervision, typed scoped variables with history,
the `SupportsTemplates` security boundary, sandboxed multi-language scripting, and a fully
self-hostable open runtime.

## 8. Where each model is stronger

**SnapLogic:** large-volume streaming ETL; huge connector catalog (Snap Packs); managed
elastic data plane; org-level asset governance (accounts, shared folders, versioned assets);
always-on low-latency serving.

**DotFlow:** explicit, analyzable control flow (loops/parallel/trycatch as supervised
sub-graphs); type safety end-to-end; security-by-default expression surface; no JVM/SaaS
dependency; embeddable .NET engine; real scripting languages instead of an expression subset.

→ Porting guidance continues in [`03-porting-guide.md`](03-porting-guide.md).
