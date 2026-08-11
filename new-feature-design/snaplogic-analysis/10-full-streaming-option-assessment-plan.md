# 10 — Option Assessment & Plan: Full Document-Streaming Mode 🌊🔥

> **Type:** option assessment with effort estimates — *not* a commitment or a schedule.
> **Supersedes the sizing in** [`09-full-streaming-rewrite-assessment.md`](09-full-streaming-rewrite-assessment.md),
> which was written **before** Phase 5.1 shipped and therefore priced work that now partly exists.
> Doc 09's *analysis* of what breaks remains correct and is not repeated here — read it first.

**Question being answered:** what would it actually take to make per-document streaming a first-class
way to run a DotFlow workflow, and is a separate **mode** (streaming alongside batch) better than
either a rewrite or the status quo?

**Short answer:** ~**11–20 engineer-months** for a full rewrite, but that is the wrong shopping list.
A **dual-mode** engine costs ~**5–9 engineer-months**, keeps batch semantics for the workloads they
genuinely suit, and — critically — **most of the mode boundary already exists and is already
validated in the designer**. See §6 (recommendation) and §5 (the mode design).

---

## 1. Why revisit now

Two things changed since doc 09.

**1. You answered part of doc 09's stakeholder checklist**, and the answers move the calculus:

| Checklist item | Your answer | Effect on this assessment |
| --- | --- | --- |
| Willing to break every binding + custom module? | *"Yes because we are still testing"* | Removes the migration cost that dominated doc 09's estimate — no field workflows, no third-party `.wfmod`s |
| Fund parallel maintenance of two engines? | *"We don't need to maintain both because this isn't released yet"* | ⚠️ **Reframed in §5.1** — the case for two modes turns out *not* to be legacy support, so this answer doesn't actually close the question |
| Is per-run snapshot auditability expendable? | *"Need clarification"* | ❗ **The one genuinely open question.** §4.6 gives you what you need to decide it |
| >50% of workload large-volume ETL? | *unanswered* | The single biggest driver of whether this is worth doing at all |

**2. Phase 5.1 shipped**, and it de-risked the expensive parts of doc 09's estimate by *building*
them under containment — exactly as doc 09 §7.2 recommended:

| Doc 09 assumed | Reality after 5.1 |
| --- | --- |
| "compile `WorkflowDefinition` → Akka.Streams" is the core unknown | ✅ **Done for linear chains.** `StreamRegionRunner` (253 lines) already does this, in production, with backpressure, bounded buffers, supervision and cancellation |
| Akka.Streams is a bet | ✅ **Proven** by spike *and* by shipping — 5 of 5 criteria passed, now carrying real workloads |
| "All ~40 built-in modules rewritten" | 📉 **56 modules, 11 already streaming.** The per-item contract (`IStreamItemProcessor`) exists and module authors have a documented path |
| Per-document error model is undesigned | ✅ **Shipped** — `onItemError`, per-item error documents (doc 07 schema), streaming `errors` ports |
| Streaming designer UX is unbuilt | ✅ **Shipped** — diamond ports, drag-time refusal, region halos, `{{item}}` scoping, stage knobs |
| Bounded-memory accumulation unsolved | ✅ **Shipped** — `BoundedAccumulator` + spill (5.1.6) |

**The strategic consequence is the important part.** Doc 09 said "if regions grow until a whole
workflow is one region, we arrive at the streaming engine incrementally." That is no longer
hypothetical — it is a **shorter, cheaper, lower-risk path than the rewrite**, and §5 is mostly an
argument that the remaining distance is smaller than it looks.

---

## 2. What we're estimating against (real numbers)

Measured from the current tree, so the estimates below have a basis rather than a vibe.

| Area | Files | Lines | Relevance |
| --- | --- | --- | --- |
| `Workflow.Engine` | 39 | **10,171** | `WorkflowExecutor.cs` alone is **2,696** — the batch traversal loop |
| ↳ actor orchestration retired by a rewrite | 7 | ~5,577 | `WorkflowExecutor`, `NodeExecutor`, `SubGraphExecutor`, `ParallelExecutionCoordinator`, `TryCatchExecutorActor`, `LoopExecutorActor`, `TransactionExecutorActor` |
| ↳ streaming already built | 3 | ~807 | `StreamRegionDetector`, `StreamRegionRunner`, `StreamRegionExecutor` |
| `Workflow.Modules` | 117 | **18,649** | 47 module files here; **56 distinct module ids** across all projects |
| ↳ flow-control modules retired by a rewrite | 11 | ~2,159 | condition, switch, loop×2, parallel, fanin/fanout, partition, split, trycatch, break/continue/throw |
| ↳ already streaming | 11 | — | csv/json read+write, db query/bulkinsert, map, query, aggregate, collect, fromitems |
| `PropertyBinder` (the `{{ }}` engine) | 1 | **900** | The composition break doc 09 §3.1 identifies |
| Variables subsystem (core→persistence→UI) | ~20 | ~2,400 | Semantics change, not just code |
| `Workflow.Tests` | 157 | **35,330** | Engine 9,409 · Modules 18,384 · Core 1,459 |
| `Workflow.Tests.UI` | 83 | 10,909 | Mostly survives — designer is largely kept |
| `docs/` | 18 | ~3,400 | `advanced-flow-control.md` (665) is ~entirely about retired constructs |

**Current suite:** 1,734 + 712 tests green.

---

## 3. Three options on the table

| | Option | One-line shape |
| --- | --- | --- |
| **C** | **Full rewrite** (doc 09's Option C) | Every edge is a stream. Batch semantics deleted. One engine. |
| **M** | **Dual mode** ⭐ | Batch stays exactly as-is. A workflow may *also* be run as one whole-graph stream. Two semantics, one engine, one designer. |
| **S** | **Status quo** (5.1 regions) | Streaming is a sub-graph capability; a workflow is always batch overall. |

Option S is the baseline — it already ships and already delivers bounded-memory ETL. The question is
whether M or C buys enough beyond it.

---

## 4. Effort estimate: Option C (full rewrite)

### 4.1 Basis and honesty about error bars

These are **engineer-months for a small team already fluent in this codebase**. At this size,
estimates are unreliable in a specific, predictable direction: **the code volume is knowable and the
semantic design is not.** Workstreams 4.3 and 4.6 below are *specification* work where the risk
lives; the rest is comparatively mechanical.

Treat the ranges as "low = everything we already understand holds; high = two of the semantic
questions turn out to be genuinely hard." I would not bet on the low end.

### 4.2 Breakdown

| # | Workstream | Est. | Confidence | Notes |
| --- | --- | --- | --- | --- |
| 1 | **Graph compiler**: `WorkflowDefinition` → Akka.Streams `GraphDsl` with branch/merge (not just linear chains) | 1.5–3 mo | 🟡 medium | `StreamRegionRunner` proves the linear case; **branching is the unbuilt part** (deliberately excluded by D22) |
| 2 | **Operator set** replacing flow modules: router, filter, broadcast, merge, partition, window, per-item try/catch | 1.5–3 mo | 🟡 medium | ~2,159 lines of flow modules retired; the replacements are *different*, not ported |
| 3 | **Module conversion**: 45 of 56 modules to per-item/terminal contracts | 1.5–2.5 mo | 🟢 higher | Contract exists, 11 done, pattern established. Mostly grind |
| 4 | **Binding language redesign** (`{{nodeId.port}}` → per-document refs) | 1–2 mo | 🔴 **low** | 900-line `PropertyBinder` + designer lint/picker + **spec work**. See §4.3 |
| 5 | **Variables redesign** → read-only stream config + stateful operators | 0.75–1.5 mo | 🟡 medium | Touches core, 3 persistence providers, API, designer |
| 6 | **Execution model / history / monitor** rebuild | 1.5–2.5 mo | 🔴 **low** | Depends entirely on the auditability answer — see §4.6 |
| 7 | **Transactions** under streaming (per-batch/windowed) | 0.5–1.5 mo | 🔴 **low** | doc 09 §3.3 calls this "genuinely hard" and I agree |
| 8 | **Triggers/lifecycle**: always-on sessions, queue sources | 0.75–1.5 mo | 🟡 medium | Overlaps [`08`](08-always-on-serving-design.md); Akka.Streams `RestartSource`/`Throttle` do much of it |
| 9 | **Test suite** rework (engine + modules ≈ 27,800 lines) | 1.5–2.5 mo | 🟢 higher | Volume, not novelty |
| 10 | **Docs + examples** rewrite | 0.5–1 mo | 🟢 higher | `advanced-flow-control.md` retired wholesale |
| | **Total** | **11–20 mo** | | |

> Doc 09 called this "a v2 of the product — comparable to everything built since Phase 2." That
> characterisation still holds, though **5.1 shaved roughly 2–4 months off** by building the compiler
> core, the module contract, the error model and the streaming designer UX under containment.

### 4.3 🔴 The riskiest line item is not code

Workstream 4 (bindings) is where I'd expect an estimate to break, because it is a **language design
problem wearing a refactor's clothes**. `{{nodeId.port}}` isn't a convenience — it's how DotFlow
expresses composition. Removing it means every workflow that enriches a document from a *different*
branch must be redesigned into an explicit join, and joins over streams are precisely where
SnapLogic's own documentation warns about hangs and memory blowups.

There is no mechanical migration. There isn't even a mechanical *diagnosis* — you can't statically
tell whether a given `{{nodeId.port}}` reference is "the config value I loaded once" (fine, becomes a
stream-config read) or "the row I want to join against" (not fine, becomes a stateful operator).

**This is the single strongest argument for Option M**, because a mode boundary lets both reference
models coexist and lets each workflow keep the one that fits.

### 4.4 What Option C would gain

Unchanged from doc 09 §5 and still true: native bounded-memory ETL everywhere, 1:1 SnapLogic porting
fidelity (a `.slp` importer gets dramatically easier — see [`04`](04-future-slp-import.md)), and
Ultra-style continuous pipelines falling out for free.

### 4.5 What Option C would lose

Also unchanged (doc 09 §6), but one item deserves promotion because your checklist flagged it:

### 4.6 ❗ The open question: per-run snapshot auditability

You marked this *"need clarification"*, so here is what you're actually deciding.

**What exists today.** Every node execution persists a `NodeExecutionRecord` carrying `Inputs`,
`Outputs`, `State`, `Duration`, and loop correlation (`LoopId`/`LoopIteration`). One run produces one
auditable, replayable snapshot per node. The execution monitor, the replay view and the
execution-detail page are all built on this, and it is a genuine differentiator for debugging and
for compliance stories.

**Why streaming breaks it.** In a stream there is no "the output of node X" — there are N documents.
Persisting inputs/outputs per node per document is **O(nodes × documents)**, which for the million-row
ETL that motivates this whole exercise is millions of records per run. That's not a tuning problem;
it's the wrong shape.

**Your three real choices:**

| | Model | Cost | What you give up |
| --- | --- | --- | --- |
| **a** | **Aggregate-only.** Per-stage counters, rates, error counts. No per-document records. | Cheapest; ~what 5.1 already publishes | Cannot answer "what did node X do to document 12345?" — the debugging story becomes logs + sampling |
| **b** | **Sampled.** Aggregate counters + N sampled documents per stage, redacted. | Moderate — **this is already designed as 5.1.5** (D13) and not yet built | Answers "what does node X typically do", not "what did it do to *that* document" |
| **c** | **Full per-document audit.** Every document's path persisted. | Expensive at runtime *and* in storage; needs its own retention design | Nothing semantically — but it may make the streaming mode too slow to be worth having |

**My read:** (b) is the honest answer for a streaming mode, and it's already on the roadmap — which
is a decent sign it's the natural shape rather than a compromise. But note the asymmetry this
creates, because it's the crux of §5: **batch mode can offer (c) cheaply and streaming mode cannot.**
That is not a temporary implementation gap. It's inherent, and it means the two modes are genuinely
good at different things.

---

## 5. Option M — streaming as a *mode* ⭐

### 5.1 Reframing your checklist answer

You wrote: *"We don't need to maintain both engines because this isn't released yet."*

That's correct about **migration cost** — and it removes doc 09's single biggest objection. But it
answers a question about *legacy*, and the case for keeping batch isn't legacy. It's **fitness for
purpose**:

- Batch gives per-node auditability (§4.6c) cheaply; streaming structurally cannot.
- Batch gives `{{nodeId.port}}` cross-graph composition; streaming structurally cannot (§4.3).
- Batch gives supervised control-flow-as-nodes with independent timeouts and retries; streaming
  replaces it with per-document policies that are *different*, not strictly better.
- Streaming gives bounded memory over unbounded data; batch structurally cannot.

Neither is a subset of the other. A 200-row nightly reconciliation with a compliance audit trail and
a 50-million-row file ingest want genuinely different engines. **Deleting batch to get streaming
would trade one capability for another, not upgrade.**

So the question isn't "can we afford two?" — it's "do we want one tool that's mediocre at both, or
one product with two honest modes?"

### 5.2 🔑 The mode boundary already exists

This is the part that makes M far cheaper than it sounds. Phase 5.1 didn't just add streaming
plumbing — it added **a validated semantic boundary**, and every hard question about mixing the two
models was already answered inside it:

| Concern doc 09 raised | How 5.1 already handles it *at the region boundary* |
| --- | --- |
| Bindings mean different things | `{{item}}` is offered **only inside** a region; using it outside is a lint error, and `{{nodeId.port}}` keeps working outside. **Both reference models already coexist, scoped by position in the graph.** |
| Variables can't be written per-document | `SetVariable`-inside-a-region is **already a validation error** |
| Streams mustn't leak into batch constructs | **Already validated** — shape mismatch is refused at drag time, and streams crossing construct boundaries is an error |
| Users can't tell which world they're in | **Already visual** — diamond ports, distinct edge stroke, region halo, 🌊 palette badges |
| Bridging between worlds | **Already exists** — `stream.collect` / `stream.fromitems` |
| Per-document errors | **Already exists** — `onItemError`, per-item error documents, `errors` ports |

**So "streaming mode" is not a new semantic universe.** It is the existing, already-validated,
already-taught region semantics extended until it can cover an entire graph. The user-facing concept
("inside the 🌊 area, you work per document") is *unchanged* — only the size of the area changes.

That is a dramatically smaller conceptual ask than doc 09 was pricing.

### 5.3 What's actually missing to get from regions → whole-graph mode

| # | Gap | Est. | Notes |
| --- | --- | --- | --- |
| 1 | **Branching inside a region** (broadcast / merge / partition) | 1–2 mo | D22 restricts v1 regions to linear chains *by choice*. This is the biggest genuine gap and the prerequisite for everything else |
| 2 | **Per-document routing operators** — streaming `condition` / `switch` | 0.5–1 mo | Depends on (1). The batch modules stay; these are streaming siblings |
| 3 | **Per-document error boundary** — streaming `trycatch` equivalent | 0.5–1 mo | Partly there (`onItemError` + error ports); needs a scoped-subgraph form |
| 4 | **Streaming sources for always-on** (queue/webhook-fed) | 0.75–1.5 mo | Substantially [`08`](08-always-on-serving-design.md); `RestartSource`/`Throttle` do the heavy lifting |
| 5 | **Workflow-level lifecycle flag** (run-to-completion vs always-on) | 0.25–0.5 mo | See §5.4 — the *only* declared flag I'd add |
| 6 | **Remaining module coverage** (~45 modules gaining streaming shapes) | 1.5–2.5 mo | Same grind as C-3, but **incremental and optional** — a module without a streaming shape simply can't sit in a region, which is already the rule |
| 7 | **Stateful operators**: window, streaming join | 1–2 mo | Only needed to replace the `{{nodeId.port}}` enrichment pattern *inside* a stream |
| 8 | **Observability for whole-graph streams** (5.1.5 + mode-aware monitor) | 0.75–1.5 mo | 5.1.5 is already scoped and unstarted |
| 9 | **Designer**: whole-graph region affordances, mode indicator, docs | 0.5–1 mo | Extends existing halo/badge/lint machinery rather than replacing it |
| | **Total** | **~6.75–13 mo** | Nominal |
| | **Realistic scope for a usable mode** (items 1–5, 8, 9) | **~5–9 mo** | Items 6 and 7 are demand-driven and land incrementally |

**And every item ships independently and usefully.** That's the structural advantage over C: there is
no flag day, no unusable intermediate state, and the project can stop at any point with a coherent
product. Option C has a long valley where nothing works.

### 5.4 How the mode should be declared (and mostly, *not* declared)

The instinct is `WorkflowDefinition.ExecutionMode = Batch | Streaming`. I'd argue against most of it:

**Semantics should be inferred from the graph, not declared.** A workflow whose every node sits in
one region *is* a streaming workflow — the engine already computes exactly this
(`StreamRegionDetector`), and the designer already renders and validates it. A declared semantic flag
would create a second source of truth that can disagree with the wiring, and the failure mode is
nasty: a workflow marked `Streaming` whose nodes aren't actually stream-linked, or vice versa. **Two
sources of truth for one fact is how you get bugs nobody can reproduce.**

**Lifecycle genuinely must be declared**, because it *isn't* inferable. "Run to completion and stop"
vs "stay up and consume forever" is a property of intent, not of node wiring — the same graph could
reasonably be either. That belongs on `TriggerDefinition` (where `TriggerType.Scheduled`/`Event`
already exist as unimplemented placeholders) rather than as a new engine mode.

**Auditability may warrant an opt-in**, per §4.6 — an operator running a whole-graph stream should
be able to say "I accept sampled observability" explicitly, so the reduced audit trail is a choice
rather than a surprise. This is the same "loud, never silent" principle 5.1.6 used for spill
encryption.

So: **one small lifecycle flag, one optional observability acknowledgement, and no semantic mode flag
at all.** The mode is what the graph says it is.

---

## 6. Recommendation

**Pursue Option M incrementally. Do not rewrite. Do not stop at S.**

1. **Option C is not justified by the evidence available.** Its dominant cost (§4.3 bindings, §4.6
   auditability) buys capabilities that Option M also delivers, while *destroying* capabilities that
   have no streaming equivalent. The "we're unreleased so breakage is free" argument is true but
   answers the wrong question — the reason to keep batch is that it's better at some jobs, not that
   users depend on it.

2. **Option M's first step is already the right next step regardless.** Branching inside a region
   (§5.3-1) is the top gap for *any* streaming future, is useful on its own, and was deliberately
   deferred rather than discovered late.

3. **Two honest modes beat one compromised engine** — and 5.1 already proved the boundary between
   them is teachable, validatable and visible.

**Revisit Option C only if** all three hold: (a) large-volume ETL becomes the dominant workload,
(b) whole-graph regions prove insufficient *in practice* rather than in theory, and (c) the batch
model's auditability turns out to be unwanted rather than merely unexercised.

### Sequencing if you proceed

| Step | Work | Value on its own |
| --- | --- | --- |
| 0 | **Answer §4.6** (auditability model) | Unblocks 5.1.5 and gates the monitor design for everything after |
| 1 | Finish **5.1.5** (observability/sampling) | The only unbuilt MVP slice; needed by every later step |
| 2 | **Branching inside regions** (§5.3-1) | Removes the D22 linear-chain restriction — immediately useful |
| 3 | **Routing + error-boundary operators** (§5.3-2,3) | Makes non-trivial whole-graph streams expressible |
| 4 | **Lifecycle flag + streaming sources** (§5.3-4,5) | Delivers [`08`](08-always-on-serving-design.md)'s always-on story |
| 5 | Module coverage + stateful operators, **on demand** | Incremental forever |

---

## 7. Decision checklist (what I still need from you)

- [ ] ❗ **§4.6 — which auditability model?** (a) aggregate-only, (b) sampled *(recommended, already
      designed as 5.1.5)*, or (c) full per-document. This gates the monitor design and is the item
      you flagged for clarification.
- [ ] **Is >50% of the target workload large-volume per-document ETL?** Still unanswered from doc 09,
      and it's the strongest single signal for how far down §6's sequencing to go.
- [ ] **Do you agree batch is worth keeping on merit** (§5.1), independent of the
      no-released-users argument?
- [ ] **Confirm the "no semantic mode flag" position** (§5.4) — mode inferred from graph shape, with
      only lifecycle declared.
- [ ] Any appetite constraint I should estimate against? The §5.3 ranges assume a small team fluent
      in this codebase and no hard deadline.
