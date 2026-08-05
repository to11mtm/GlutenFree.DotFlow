# 03 — Porting from SnapLogic to DotFlow 🧳

> Practical guidance for translating SnapLogic pipelines into DotFlow workflows.
> Audience: both DotFlow developers (what to build) and users migrating pipelines.
> Read [`02-snaplogic-comparison.md`](02-snaplogic-comparison.md) first for the *why*.

## 1. The mental-model shifts (read these before porting anything)

1. **Documents → one run.** A SnapLogic pipeline streams N documents; a DotFlow run carries one
   payload. Decide per pipeline: does it process *a batch as a whole* (port the stream as a
   collection flowing through one run) or *each document independently* (port to a workflow
   whose body is a `builtin.loop.foreach` over the fetched collection — or one run per document
   via webhook/trigger)?
2. **Views → ports, unlinked views → Start/End.** Circle/diamond view links become explicit
   `ConnectionDefinition`s. An unlinked input view (pipeline entry) becomes `builtin.start` +
   run inputs, or `builtin.http.webhook` for Triggered/Ultra tasks. Unlinked outputs → terminal
   node outputs / `builtin.end`.
3. **Everything-is-a-Snap routing → control-flow nodes.** Router/Filter/Copy logic moves into
   `builtin.condition` / `builtin.switch` / `builtin.fanout` (+ per-connection `Condition`).
4. **String parameters → typed variables.** `_param` values (always strings) become declared,
   typed `Variables`; drop the `parseInt(_x)` idioms. Environment-wide values → *global* scope;
   per-pipeline persistent state (high-water marks) → *workflow* scope; per-run → run inputs.
5. **Accounts → secret variables (for now).** Connection config lives in secret-flagged
   variables referenced from node properties (`{{Variable.dbConnection}}`). OAuth2 flows need a
   custom module or an external token broker until DotFlow grows a credential asset type.

## 2. Snap → module mapping table

| SnapLogic Snap | DotFlow module(s) | Notes |
| --- | --- | --- |
| Mapper | `builtin.transform.map` (+ `builtin.transform.json`) | expression rows → mapping config; **Pass through** ≈ merge with input; Mapping Root → path prefix |
| Router | `builtin.switch` (first-match) / `builtin.condition`; multi-match fan-out → `builtin.fanout` + connection `Condition`s | Router "all matching views" needs fanout+conditions, not switch |
| Filter | `builtin.condition` (false branch unwired) or a `where` in `builtin.transform.query` | per-document drop only makes sense inside a ForEach or against a collection |
| Copy | `builtin.fanout` (or simply multiple connections from one output port) | |
| Union | `builtin.fanin` | fan-in `named` mode preserves branch identity |
| Join | `builtin.transform.join` | DotFlow join is collection-based — no sorted-stream concerns |
| Aggregate | `builtin.transform.aggregate` | no upstream Sort requirement |
| Gate | `builtin.fanin` (+ `builtin.parallel` WaitForAll) | Gate's wait-for-all-then-one-document ≈ parallel join semantics |
| Sort | `builtin.transform.query` / script | only needed where ordering matters for output — not for joins |
| JSON Splitter | `builtin.split` (or ForEach directly) | |
| Group By N / Fixed batching | `builtin.partition` | |
| Sequence / Generator | script node or run inputs | |
| Pipeline Execute | ⚠️ **no equivalent yet** | inline the child graph, or wait for a `workflow.execute` module (gap #1) |
| Script Snap (Jython/JS) | `builtin.script` (JS/C#/Lua) | DotFlow sandbox is deny-by-default — grant capabilities explicitly |
| REST Get/Post etc. | `builtin.http.request` | headers/auth from secret variables |
| DB Select / Execute / Insert | `builtin.database.query` / `.execute` / `.bulkinsert` | SQL text never template-expands; use bound parameters (values *do* expand) |
| DB Stored Procedure | `builtin.database.execute` | |
| File Reader/Writer, CSV/JSON/XML Parser/Formatter | `builtin.file.read`/`.write`, `.csv.*`, `.json.*`, `.xml.*` | parse+format pairs often collapse into one read/write node |
| ZipFile Read/Write | `builtin.file.compress` / `.decompress` | |
| S3 / Azure Blob Snaps | `builtin.cloud.s3` / `builtin.cloud.azureblob` | GCS: not yet built-in |
| Email/Slack/SaaS Snaps (Salesforce, SAP, …) | custom `.wfmod` modules or `builtin.http.request` | biggest catalog gap — expect to build connectors |
| Error view routing | wrap in `builtin.trycatch`; catch port = error path | |
| Exit / Ignore | `builtin.throw` / `builtin.passthrough` | |

## 3. Expression translation cheat-sheet

| SnapLogic | DotFlow | Caveats |
| --- | --- | --- |
| `$field`, `$a.b[2]` | `{{input.field}}`, `{{input.a.b[2]}}` | only in `SupportsTemplates` fields; inside script nodes use the `workflow.*` API |
| `_param` | `{{Variable.param}}` | now typed — remove `parseInt`/`parseFloat` shims |
| `pipe.args["x"]` | `{{Variable.x}}` | |
| `$x == null ? "d" : $x` | same JS in `{{…}}` | Jint is real JS: `===`, assignment etc. all work |
| `jsonPath($, "$.a[*].b")` | `builtin.transform.jsonquery` node, or JS | |
| `lib.helpers.fn($x)` | script node with shared snippet | no expression-library equivalent yet |
| `eval(_expr)` | avoid; restructure | dynamic eval is a smell — DotFlow conditions/switch usually remove the need |
| `pipe.ruuid`, `snap.label` etc. | execution metadata not exposed to templates | log/monitoring concerns move to SignalR/monitor |

Remember DotFlow's two hard rules: unresolvable `{{…}}` **fails the node** (good — it surfaces
translation mistakes immediately), and only opted-in fields expand at all.

## 4. Porting workflow (recommended process)

1. **Inventory.** Export pipelines (`.slp`), list Snap types used, accounts referenced, tasks,
   error pipelines, child-pipeline (Pipeline Execute) topology, Ultra usage.
2. **Classify each pipeline:**
   - *Request/response* (Triggered/Ultra low-latency) → webhook-triggered workflow.
   - *Scheduled batch* → scheduled workflow; stream → ForEach/partition decision.
   - *Headless Ultra (listener)* → no direct equivalent; needs an event trigger + queue
     integration (e.g., NATS) or a polling scheduled workflow.
3. **Port leaf/child pipelines first** (same rule as SnapLogic import ordering). Until a
   sub-workflow module exists, either inline children into the parent graph or expose the child
   as its own webhook workflow and call it via `builtin.http.request` (works, but loses
   in-process pooling and typed handoff).
4. **Translate the graph** with the tables above; put per-document sections inside
   `builtin.loop.foreach` regions.
5. **Recreate parameters as variables**, mark credentials `IsSecret`, choose seed modes
   (`AlwaysOverride` for counters/flags; default `SeedOnly` for high-water marks).
6. **Recreate error handling**: per-Snap error views → trycatch scopes; per-node retries via
   `RetryPolicy` (SnapLogic has no per-Snap retry declaration — you may *simplify* here).
7. **Validate side-by-side**: run both against the same input; compare outputs. DotFlow's
   execution monitor + SignalR events give per-node visibility comparable to SnapLogic's
   pipeline statistics.

## 5. What doesn't port (plan around these)

| SnapLogic feature | Status | Workaround |
| --- | --- | --- |
| Pipeline Execute (child pipelines, pooling) | ❌ no module yet | inline, or HTTP call to a webhook workflow |
| Ultra low-latency (FeedMaster queue, warm instances) | ❌ | webhook trigger (cold per run); acceptable unless latency-critical |
| Reusable error pipelines | ❌ | trycatch per workflow; centralize logic in a script module |
| Resumable pipelines | ❌ | idempotent design + re-run; high-water-mark variables |
| Premium connector Snaps | ❌ | `builtin.http.request` or custom `.wfmod` |
| Accounts w/ OAuth2 lifecycle | ⚠️ partial | secret variables + custom token-refresh module |
| Expression libraries (`.expr`) | ⚠️ partial | shared script snippets |
| Very large streamed datasets | ⚠️ | chunk via pagination/partition; or keep such pipelines in place until DotFlow gets streaming ports |

## 6. Advice to DotFlow developers (build order for migration readiness)

1. **`builtin.workflow.execute`** (sub-workflow node) — unblocks the dominant SnapLogic reuse
   pattern and enables reusable error workflows for free.
2. **Credential/connection asset type** — scoped, secret, with OAuth2 client-credentials +
   refresh support; referenced from node properties like Accounts.
3. **Queue-fed trigger** (NATS is already in the persistence stable) — approximates
   Ultra/FeedMaster and headless listener pipelines.
4. **Importer** (see [`04-future-slp-import.md`](04-future-slp-import.md)) — structural `.slp`
   conversion becomes tractable once 1–2 exist, since Pipeline Execute and Accounts appear in
   almost every real estate.
5. Later: streaming/chunked ports for large ETL; mid-run checkpoint/resume.
