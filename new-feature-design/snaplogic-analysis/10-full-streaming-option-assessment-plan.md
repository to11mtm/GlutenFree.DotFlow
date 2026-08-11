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

> ### 📊 Calibrated against actuals — see [Appendix A](#appendix-a-calibration-against-phase-51-actuals-)
>
> Phase 5.1's estimates missed by **~24× on effort** (35 estimated engineer-days delivered in ~11.8
> working hours). But the useful finding is different: **93% of the calendar time was spent waiting
> for decisions, not implementing them.**
>
> Calibrated calendar figures, which are the ones to plan against:
>
> | | Conventional estimate | Calibrated calendar | Decision-latency floor |
> | --- | --- | --- | --- |
> | **C** full rewrite | 11–20 engineer-months | **~11–26 weeks** | ~4–7 weeks |
> | **M** dual mode ⭐ | 5–9 engineer-months | **~4–9 weeks** | ~2–3 weeks |
>
> For Option M the latency floor is roughly *half* the total — implementation is no longer the
> dominant cost. The schedule is set by how fast §7's open questions get answered.

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

> 📊 **Calibrated equivalent: ~11–26 weeks calendar** with agent-assisted implementation, of which
> a **~4–7 week decision-latency floor** cannot be compressed by working faster. See
> [Appendix A.5](#a5-4-and-5-re-stated-with-calibrated-units). Appendix A also confirms the warning
> above empirically: the slices with the most unresolved design compressed ~4× *less* than the
> slices with a settled spec.

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

> 📊 **Calibrated equivalent: ~4–9 weeks calendar**, with a **~2–3 week decision-latency floor** —
> roughly half the total. Seven gating decisions drive that floor and are enumerated in
> [Appendix A.5](#the-open-decisions-enumerated); three of them are serial. See
> [Appendix A](#appendix-a-calibration-against-phase-51-actuals-).

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

> 📊 [Appendix A.3](#a3-the-real-constraint-is-decision-latency-not-implementation) found that
> **93% of Phase 5.1's calendar time was decision latency**, and that serial decisions cost far more
> than parallel ones. So these are batched deliberately — answering them in one pass is worth more
> than any implementation speedup.

**Independent — answerable now, in any order:**

- [ ] ❗ **§4.6 — which auditability model?** (a) aggregate-only, (b) sampled *(recommended, already
      designed as 5.1.5)*, or (c) full per-document. This gates the monitor design and is the item
      you flagged for clarification.
- [ ] **Is >50% of the target workload large-volume per-document ETL?** Still unanswered from doc 09,
      and it's the strongest single signal for how far down §6's sequencing to go.
- [ ] **Do you agree batch is worth keeping on merit** (§5.1), independent of the
      no-released-users argument?
- [ ] **Confirm the "no semantic mode flag" position** (§5.4) — mode inferred from graph shape, with
      only lifecycle declared.
- [ ] **Module coverage: exhaustive or on-demand?** (§5.3-6) — the difference between a fixed
      1.5–2.5 month block and an incremental trickle.
- [ ] Any appetite constraint I should estimate against? The §5.3 ranges assume a small team fluent
      in this codebase and no hard deadline.

**Serial — each needs the previous one answered first (so worth starting early):**

- [ ] **Branching semantics inside a region** — what fan-out/fan-in means when D22's linear-chain
      restriction lifts. Gates everything else in §5.3.
- [ ] **Per-document error-boundary scoping** — the streaming `trycatch` shape. Needs branching first.
- [ ] **Stateful operator semantics** — window/join memory bounds. Needs both of the above.

---

# Appendix A — Calibration against Phase 5.1 actuals 📊

> Added after the fact. Phase 5.1 is the only project in this repo where an estimate was written
> **before** implementation and the implementation then happened in a measurable window — so it is
> the only calibration data available. This appendix compares the two and re-states §4/§5 in light
> of it.
>
> **The headline is not the one I expected.** The estimates were wrong by ~24× on effort — but that
> turns out to be nearly irrelevant, because **93% of the calendar time was spent waiting for
> decisions, not implementing them.**

## A.1 What was measured, and how

**Sources:** the session's turn timestamps (23 turns) and the repo's git history, which align
closely — the user committed immediately before nearly every message, so commits bracket the work.

**Method:** turns were clustered into working blocks, splitting on any gap > 2 hours (treated as a
break rather than work). Block duration = first turn of the cluster → last commit of the cluster.

| Block | Covers | Duration |
| --- | --- | --- |
| B1 | Analysis docs `01`–`09` | 48 min |
| B2 | Design review ×4 rounds, phase plan, 5.1.0, 5.1.1, 5.1.2, start of 5.1.3 | 116 min |
| B3 | Rest of 5.1.3, all of 5.1.4, flaky-test remediation | 446 min |
| B4 | 5.1.6 spill (incl. two blob stores) | 79 min |
| B5 | Doc refresh + this document | 16 min |
| | **Total working** | **705 min ≈ 11.8 h** |
| | **Calendar span** (08-05 → 08-11) | **167 h ≈ 7.0 days** |
| | **Waiting** (the five breaks: 22 h, 16.5 h, 4.9 h, 47.9 h, 72 h) | **155 h — 93.0% of calendar** |

### ⚠️ Limits of this data — read before using any number below

1. **n = 1.** One project, one codebase, one agent, one user. Treat everything here as a
   directional signal, not a coefficient.
2. **Block time includes user reading and response time**, so it *overstates* agent working time.
   The effort ratio below is therefore a **floor**, not a ceiling.
3. **Not everything estimated was delivered.** The Docker-gated 1M-row scale run and the spill
   integration run were never executed, guard defaults are still an uncalibrated placeholder,
   `file.xml.*` was deferred, and **5.1.5 was skipped entirely**. So 35 estimated engineer-days
   bought somewhat less than 35 days of scope — inflating the ratio further.
4. **Self-estimation conflict.** The same author wrote the estimates *and* the implementation, and
   wrote the estimates in conventional human-team units without ever calibrating them. Expect
   anchoring on team norms rather than on observed throughput.
5. **Favourable conditions.** The codebase was already well-factored with directly reusable
   precedents (`LoopExecutorActor` as a sibling for the region executor; `IConnectionStringProtector`
   as the exact shape for `ISpillProtector`). Greenfield or tangled code would compress less.
6. **Speed is not quality.** 2,446 tests pass, but the Docker-gated paths are unexercised and the
   scale claims are unverified. Some of the compression is real; some is deferred verification.

## A.2 Estimate vs actual, per slice

Phase-plan estimates were: 5.1.0 ~3 d · 5.1.1 ~2 d · 5.1.2 ~1 wk · 5.1.3 ~2 wk · 5.1.4 ~2 wk ·
5.1.5 ~1 wk · 5.1.6 ~1 wk.

| Slice group | Estimated | Actual working | Ratio | Character of the work |
| --- | --- | --- | --- | --- |
| 5.1.0 + 5.1.1 + 5.1.2 | 10 d (80 h) | ~1.0 h | **~78×** | Contracts, bridges, designer rendering — well-understood patterns, clear precedents |
| 5.1.3 + 5.1.4 | 20 d (160 h) | ~7.9 h | **~20×** | Novel design: Akka spike, D25/D26, per-item contract, 9 module conversions |
| 5.1.6 | 5 d (40 h) | ~1.3 h | **~30×** | Built to a written plan, but discovered two missing prerequisites mid-flight |
| **Delivered total** | **35 d (280 h)** | **~11.8 h** | **~24×** | |
| 5.1.5 | 5 d | **not started** | — | |

**Calendar comparison:** 35 engineer-days is ~7 calendar weeks for one engineer. Actual calendar
span was **7 days** — a **7× calendar compression**, against a **24× effort compression**.

That gap between the two ratios is the whole finding.

## A.3 🔑 The real constraint is decision latency, not implementation

**93% of elapsed calendar time was waiting.** Implementation was never the bottleneck; it was
essentially free relative to everything else.

What filled the waiting time was **decisions requiring human judgement**, and this session had a
lot of them:

- 4 rounds of review on design doc `06` before a line of code was written (~20 decisions)
- **Q6** — needed a spike to answer, and its answer (D25) *deleted* a designed subsystem
- **Q7** — followed from Q6 and deleted `StreamCardinality`
- **Q-A…Q-E** (spill), then a second round for **Q-C1** and **Q-E1**
- Plus the two decisions still open today (§4.6 auditability, workload mix)

Roughly **10 decision round-trips over 7 calendar days**, with gaps of 22 h, 16.5 h, 4.9 h, 47.9 h
and 72 h between working blocks — mean ~1.4 days per round-trip.

Three consequences that matter more than any effort estimate:

1. **Compressing implementation further buys almost nothing.** Halving the 11.8 h saves ~3% of
   calendar. Removing one decision round-trip saves ~20%.
2. **Decisions that cascade cost more than decisions that don't.** Q6 → Q7 → the D26 contract change
   were serial: none could be answered before the previous one landed. Front-loading independent
   decisions into a single round is worth more than any implementation speedup.
3. **Deleting designed work was the single highest-leverage act.** D25 removed a resequencer,
   an overflow policy and a tombstone mechanism — work that had a real estimate attached and would
   otherwise have been built, tested, documented and maintained. It was found by *checking a
   suspicion with a spike* rather than by implementing the plan as written.

## A.4 Compression is not uniform — and the pattern is legible

Ordering the slices by ratio: **78× (contracts/designer) → 30× (spill) → 20× (region executor +
scale-out)**.

The slices that compressed *least* were the ones with the most unresolved design. That is consistent
with A.3, and it is directly actionable for §4/§5, because **doc 10's own estimate already flags
which workstreams are which** — workstreams C-4 (bindings) and C-6 (auditability) were marked
🔴 low confidence precisely because they are specification work.

> **The multiplier applies to typing, not to thinking.** Any workstream whose cost is dominated by
> *deciding what the semantics should be* will compress roughly like 5.1.3/5.1.4 did (~20×), and its
> calendar cost will be set by how fast a human answers, not by how fast code appears.

## A.5 §4 and §5 re-stated with calibrated units

**Method:** scale by relative size against the one delivered data point (35 engineer-days → 7
calendar days, 11.8 h working), then apply a **design-heaviness penalty** — because Phase 5.1 began
with a *fully specified* design (doc `06`, 20 decisions resolved before coding), which neither
option below enjoys.

| | Option C (full rewrite) | Option M (dual mode, realistic scope) |
| --- | --- | --- |
| §4/§5 estimate (conventional) | 11–20 engineer-months | 5–9 engineer-months |
| = engineer-days (22 d/mo) | 242–440 d | 110–198 d |
| Size relative to delivered 5.1 | 6.9× – 12.6× | 3.1× – 5.7× |
| Naive scaled calendar | 48–88 days (7–13 wks) | 22–40 days (3–6 wks) |
| Naive scaled working time | ~81–149 h | ~37–67 h |
| Design-heaviness penalty | **×1.5–2** — binding language and audit model must be designed from scratch; no spec exists | **×1.25–1.5** — regions and the mode boundary exist, but branching, stateful-operator and audit semantics are undecided |
| **Calibrated calendar** | **~11–26 weeks** | **~4–9 weeks** |
| Decision round-trips (est.) | ~15–25 | ~7–10 |
| **Decision-latency floor** (at observed ~1.4 d/round, partly serial) | **~4–7 weeks** | **~2–3 weeks** |

**Read the last two rows first.** For Option M, the decision-latency floor (2–3 weeks) is roughly
half the calibrated calendar (4–9 weeks). **Implementation is no longer the dominant cost — the
schedule is set by how quickly the open questions get answered.**

For Option C the floor is 4–7 weeks and the total 11–26 weeks, and the spread is wide precisely
because a binding-language redesign has no spec to estimate against. That uncertainty is not
reducible by working faster.

### The open decisions, enumerated

Option M's ~7 gating decisions, which is where its schedule actually lives:

| # | Decision | Blocks | Status |
| --- | --- | --- | --- |
| 1 | Auditability model (§4.6: aggregate / sampled / full) | 5.1.5 monitor, all later observability | ❗ **open — asked in §7** |
| 2 | Branching semantics inside a region (fan-out/fan-in shape) | everything in §5.3 | not raised yet |
| 3 | Per-document error-boundary scoping | §5.3-3 | not raised yet |
| 4 | Lifecycle flag shape (§5.4) | §5.3-4,5 | proposed, unconfirmed |
| 5 | Stateful operator semantics (window/join memory bounds) | §5.3-7 | not raised yet |
| 6 | Module coverage: exhaustive or on-demand? | §5.3-6 | not raised yet |
| 7 | Mode indication in the designer | §5.3-9 | not raised yet |

Decisions 2 → 3 → 5 are **serial** (you cannot scope a per-document error boundary before deciding
what a branch inside a region means). Decisions 1, 4, 6, 7 are independent and could be batched into
a single round.

## A.6 What I'd actually do with this

1. **Optimise the decision pipeline, not the build.** Batch the four independent decisions above
   into one review round instead of discovering them one slice at a time. On the observed numbers
   that is worth more than any implementation improvement.
2. **Keep writing the spec first.** The 48 minutes spent on docs `01`–`09` and the ~54 minutes of
   design review produced the specification that let 5.1.0–5.1.2 run at 78×. Doc 06's twenty
   pre-resolved decisions are visibly *why* the implementation was fast.
3. **Keep spiking suspicions.** Two spikes (Akka viability, Q6 ordering) cost well under an hour
   combined. One chose the executor technology; the other **deleted an entire designed subsystem**.
   Both had better returns than any hour spent implementing.
4. **Don't trust the 24×.** Use the *calendar* figures in A.5 for planning and treat effort ratios
   as a curiosity. Effort was never the binding constraint, and planning against it would optimise
   the wrong thing.
5. **Re-calibrate after the next slice.** 5.1.5 is scoped (~1 week estimated) and unstarted — it is
   a clean second data point, and specifically a test of the A.4 prediction that a
   *design-light-but-undecided* slice behaves like 5.1.3/5.1.4 rather than like 5.1.0–5.1.2.
