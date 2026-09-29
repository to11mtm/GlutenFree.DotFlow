# Phase 6.1: AI Assistant — Workflow Analysis & Authoring 🤖

Made with 💖 by Ami-Chan! UwU ✨

[All Phases](README.md) | [Designer](Phase3-3-WorkflowDesigner.md) | [Scripting Engine](Phase3-1-ScriptingEngine.md) | [Module System](Phase2-8-ModuleSystem.md) | [Streaming](Phase5-1-StreamingDataPlane.md)

> **Numbering note:** provisional. Phase 6 is proposed as a **new post-Phase-4 track** ("AI-assisted
> authoring"), sibling to the Phase 5 SnapLogic gap-closure track. If the maintainer would rather
> file this under the UI track it becomes **3.8** — see **Q1**. Nothing else in the plan depends on
> which number wins.

---

## Overview

Phase 6.1 ships an **AI assistant** that can:

1. **Analyze** an existing workflow — explain what it does in plain language, review it for
   anti-patterns (missing error handling, secrets in plaintext properties, unbounded accumulators,
   orphaned nodes, unreachable branches), and **diagnose a failed execution** from its persisted
   `ExecutionRecord` / `NodeExecutionRecord` history.
2. **Create** a new workflow from a natural-language specification ("poll this REST endpoint every
   5 minutes, flatten the JSON, upsert into the `orders` table, alert on failure").
3. **Modify** an existing workflow from an instruction ("add retry with backoff to the HTTP node and
   route failures to a dead-letter file").

…across **three provider families**, all first-class and all optional:

| Family | Examples | Why it's in scope |
|---|---|---|
| **Anthropic Claude** | `claude-*` via the Messages API | Best-in-class tool use; explicitly requested |
| **OpenAI / ChatGPT** | OpenAI platform + **Azure OpenAI** | Explicitly requested; Azure matters for enterprise deployments |
| **Local / self-hosted** | Ollama, LM Studio, vLLM, llama.cpp server (all OpenAI-compatible), optional in-process LLamaSharp | **Air-gapped and data-residency deployments must be able to use every feature in this phase.** No feature may be cloud-only. |

**Complexity:** 🔴 High — new project family, new external-egress surface (a first for this repo),
new API area, new designer surface, and a security/governance story that has to be right the first
time because it involves shipping user data off-box.

> **CopilotNote:** Hot paths will be `Workflow.AI/Abstractions/*` (provider + tool seams),
> `Workflow.AI/Grounding/*` (workflow digest + module catalog retrieval),
> `Workflow.AI/Patch/*` (the `WorkflowPatch` model and its deterministic applier),
> `Workflow.AI.{Anthropic,OpenAI,Local}/*` (quarantined adapters, exactly like
> `Workflow.Scripting.{Roslyn,Lua}`), `Workflow.Api/V1/AssistantEndpoints.cs`, and
> `Workflow.UI.Client/Assistant/*`. The assistant **never writes a `WorkflowDefinition` directly** —
> see **D4**.

---

## Grounding: what already exists (and must be reused, not re-invented)

| Need | Existing asset | Path |
|---|---|---|
| Workflow model to read/emit | `WorkflowDefinition` / `NodeDefinition` / `ConnectionDefinition` / `PortDefinition` / `VariableDefinition` (LanguageExt `Arr`/`HashMap`, System.Text.Json + custom converters) | `Workflow.Core/Models/*` |
| Ground truth for "what modules exist" | `IModuleRegistry` (`GetAllModules`, `SearchModules`, `GetModulesByCategory`) + `ModuleSchema` (`Inputs`/`Outputs`/`Properties`, `PropertyEditorType`, `ValidationRule`) | `Workflow.Modules/Abstractions/IModuleRegistry.cs`, `Workflow.Core/Models/ModuleSchema.cs` |
| "Is this proposal legal?" | `WorkflowValidator` (structure, cycles, orphans, variable refs) + `ModuleAwareWorkflowValidator` (MA001 module exists, MA002 property schema, MA003/4 port names) | `Workflow.Core/Abstractions/`, `Workflow.Modules/Validation/` |
| Existing validate endpoint | `POST /api/v1/workflows/validate` | `Workflow.Api/V1/WorkflowEndpoints.cs` |
| Quarantine precedent for heavy optional deps | `Workflow.Scripting.Roslyn` / `.Lua` behind `IScriptExecutor` + `IScriptExecutorFactory` | `Workflow.Scripting/Abstractions/` |
| Capability-gating / ceilings precedent | `ScriptExecutionConfig.ClampTo(ScriptHostCeilings)`, deny-by-default network | `Workflow.Scripting/Abstractions/ScriptExecutionConfig.cs` |
| Secret storage precedent | `IConnectionStringProtector` → `DataProtectionConnectionStringProtector` (ASP.NET Data Protection), `ISpillProtector` | `Workflow.Api/Database/`, `Workflow.Modules/Streaming/` |
| Secret **marking** | `VariableDefinition.IsSecret` (already drives designer masking + execution-record redaction) | `Workflow.Core/Models/VariableDefinition.cs` |
| Token/event streaming to the browser | `WorkflowHub` + `ExecutionEventBridge` over the Akka `EventStream` | `Workflow.Api/Hubs/`, `Workflow.Api/RealTime/` |
| Undo/redo for applying changes | `CommandStack` + `IDesignerCommand` | `Workflow.UI.Client/Designer/State/` |
| Framework-free UI boundary | `Workflow.UI.Client` has **no** `Workflow.Core` reference (Phase 3.3 D2, keeps a React port additive) — DTO mirrors only | `Workflow.UI.Client/Api/Dtos/` |
| Network-free test precedent | WireMock.Net for HTTP modules; Docker-gated integration tests | `Workflow.Tests`, `Workflow.Tests.Integration` |

**Nothing AI-related exists today.** `Directory.Packages.props` has no `Microsoft.Extensions.AI`,
Semantic Kernel, OpenAI, Azure.AI or LLamaSharp entry, and the solution targets **net8.0**.

---

## Design Decisions ✅ (proposed — not yet ratified, see TO RESOLVE)

| # | Decision |
|---|----------|
| **D1 New quarantined project family** | `Workflow.AI` (core, no vendor SDKs) + `Workflow.AI.Anthropic`, `Workflow.AI.OpenAI`, `Workflow.AI.Local` adapters, host-wired in `Program.cs` exactly like the database/cloud/transform module families. A host that never enables the assistant pays **zero** new dependency weight, and an air-gapped host can reference `Workflow.AI.Local` alone. |
| **D2 One provider seam, `IChatProvider`** | Mirrors `IScriptExecutor`/`IScriptExecutorFactory`: `ProviderId`, `DisplayName`, `Capabilities` (tool use, streaming, JSON mode, context window), `Task<ChatResponse> CompleteAsync(ChatRequest, ct)`, `IAsyncEnumerable<ChatDelta> StreamAsync(...)`. `IChatProviderFactory.GetProvider(id)` + `GetRegisteredProviders()`. **DotFlow owns its own abstraction** so a vendor SDK churn never reaches `Workflow.AI` core. Whether adapters wrap `Microsoft.Extensions.AI`'s `IChatClient` or hand-rolled HTTP is **Q2**. |
| **D3 Tool calling, not blob generation** | The model is given read-only **tools** (`list_module_categories`, `search_modules`, `get_module_schema`, `get_workflow_digest`, `get_execution_trace`, `validate_draft`) and must *look up* real module ids and property schemas. Free-form "emit a whole workflow JSON" hallucinates module ids that don't exist; tool grounding against the live `IModuleRegistry` is the single highest-leverage correctness decision in this phase. |
| **D4 Patch, never overwrite** | The authoring output is a **`WorkflowPatch`** — an ordered list of typed operations (`AddNode`, `RemoveNode`, `SetNodeProperty`, `AddConnection`, `RemoveConnection`, `UpsertVariable`, `SetErrorHandling`, `SetTrigger`) applied by a **deterministic C# applier** in `Workflow.AI/Patch/WorkflowPatchApplier.cs`. Benefits: reviewable diff, per-operation accept/reject, natural mapping onto `IDesignerCommand` for undo, no risk of the model silently dropping half a graph it wasn't asked to touch. "Generate from scratch" is just a patch against an empty definition. |
| **D5 Validate-and-repair loop** | Every patch is applied to a **draft copy**, run through `ModuleAwareWorkflowValidator`, and — if invalid — the errors are fed back to the model for up to **N attempts (default 2)**. Only a validated draft is ever surfaced to the user. Unrepairable drafts are returned **with** their validation errors, not silently discarded. |
| **D6 Human-in-the-loop, always** | The assistant never persists a workflow and never triggers an execution. It returns a proposal; a human accepts it in the designer; acceptance goes through the ordinary save + `POST /workflows/validate` path. No "auto-apply" mode ships in 6.1 (**Q7** asks whether an opt-in one should ever exist). |
| **D7 Deny-by-default egress + redaction** | No provider is reachable unless explicitly configured **and** enabled. Before any prompt is assembled, the digest builder strips: values of `IsSecret` variables, connection strings / `PropertyEditorType.ConnectionString` properties, `Authorization`/`X-API-Key` header values, and execution-record payloads unless the caller opts in per-request. Redaction is a **unit-tested pure function** with a golden-file test, not a best-effort filter, and it runs on the same side of the wire as the provider call. |
| **D8 Credentials via Data Protection** | `IAiCredentialProtector` + `DataProtectionAiCredentialProtector`, purpose string `"Workflow.AI.ProviderCredential"` — the exact shape of `DataProtectionConnectionStringProtector`. Keys are configurable via `AI:Providers:*:ApiKey` **or** an env var reference; they are never returned by any GET (always `***`), never logged, and never written into a `WorkflowDefinition`. |
| **D9 Governance is a slice, not a follow-up** | `IAiUsageMeter` records tokens in/out, provider, model, feature, caller, and cost estimate per call; budgets (per-request max tokens, per-caller daily cap) are enforced **before** the call; usage surfaces in `/api/v1/metrics` and an admin-only `/api/v1/assistant/usage`. Assistant write-ish endpoints sit behind the existing developer policy; provider configuration sits behind `AdminPolicy`. |
| **D10 Deterministic fake provider for tests** | `ScriptedChatProvider` (in `Workflow.AI`, shipped, not test-only) replays a recorded transcript keyed by request hash. **CI runs zero network calls and zero Docker for this phase's unit tests**; real-provider tests are opt-in and env-var-gated, matching the Docker-gated Postgres/MinIO precedent. Transcripts double as regression fixtures for prompt changes. |
| **D11 Context assembly is deterministic and testable** | `IWorkflowDigestBuilder` renders a compact, stable, **token-budgeted** textual digest of a workflow (nodes + module ids + ports + non-secret properties + connections + variables). The full module catalog is far too large for a prompt, so the catalog slice is **retrieved**, not dumped: keyword/BM25 over `SearchModules` in 6.1; embeddings are a P-item (**Q6**). Digest output is snapshot-tested so prompt drift is visible in code review. |
| **D12 UI boundary preserved** | `Workflow.UI.Client` gains `Assistant/` (state + components) and `Api/AssistantClient.cs` with **DTO mirrors only** — no `Workflow.Core` reference, no vendor SDK in the WASM payload. All model calls go through `Workflow.Api`; **provider API keys never reach the browser.** |
| **D13 The in-workflow `builtin.ai.*` module is a different feature** | Letting a *running workflow* call an LLM (`builtin.ai.complete`, `builtin.ai.classify`, embeddings) is adjacent but separate: different threat model, different cost model, different error semantics. It reuses `IChatProvider` for free and is proposed as **Phase 6.2**. Confirm the split in **Q3** before anyone builds it here. |
| **D14 Streaming reuses the existing real-time story** | Token streaming rides SignalR rather than introducing SSE, so auth, reconnect and the browser client are already solved (**Q5** covers the alternative). |

---

## TO RESOLVE 🤔 — needs maintainer input before slice work starts

These are the genuinely open decisions. Slices below are written assuming the **proposed** answer;
where a different answer changes the shape of a slice, that's called out inline.

| # | Question | Proposed answer | Why it matters |
|---|---|---|---|
| **Q1** | **Phase numbering** — new **Phase 6** track, or **3.8** under the UI/advanced-features track? | New **Phase 6**, because this is a cross-cutting capability with backend, API *and* UI, not a UI feature. | Only affects file naming and `phases/README.md`. |
| **Q2** | **Adapter substrate** — wrap **`Microsoft.Extensions.AI`** (`IChatClient`, first-party, net8.0-compatible, unifies OpenAI/Azure/Ollama and has a community Anthropic client) or **hand-roll thin HTTP adapters** against each vendor's REST API? | `Microsoft.Extensions.AI` for OpenAI/Azure/local, **hand-rolled for Anthropic** if the community `IChatClient` implementation doesn't meet the licence/maintenance bar. | Hand-rolling is ~3 adapters of real work but zero dependency risk; wrapping is fast but adds preview-package churn to a repo that currently pins everything conservatively. **Licence review is mandatory either way** (repo has been strict: "both MIT (Q10)" in 2.4.b). |
| **Q3** | **Scope** — is `builtin.ai.*` (LLM calls *inside* a running workflow) in this phase, a Phase 6.2, or out of scope entirely? | **Phase 6.2**, per **D13**. | Doubles the phase if folded in. Also changes the security review (a workflow author could exfiltrate data via a prompt). |
| **Q4** | **Local provider baseline** — is **OpenAI-compatible HTTP (Ollama / LM Studio / vLLM)** sufficient, or is **in-process LLamaSharp** (no server, GGUF from disk, ~large native deps) required for a true air-gapped single-binary deployment? | OpenAI-compatible HTTP in 6.1.1; LLamaSharp as **6.1.P1**. | LLamaSharp drags in per-RID native binaries and a model-file distribution story — a phase-sized problem on its own. |
| **Q5** | **Streaming transport** — SignalR (`WorkflowHub` or a new `AssistantHub`) or plain **SSE** on the API? | New **`AssistantHub`**: the assistant's event vocabulary (token deltas, tool-call notices, patch-ready) has nothing to do with execution lifecycle, and `WorkflowHub`'s admin `SubscribeToAll` firehose shouldn't carry prompt content. | Affects 6.1.5 and the auth story. SSE is simpler but the browser client, reconnect and token auth already exist for SignalR. |
| **Q6** | **Retrieval** — is keyword search over `IModuleRegistry.SearchModules` enough to pick candidate modules, or do we need **embeddings + a vector index** over module docs and example workflows? | Keyword in 6.1.2; embeddings as **6.1.P2**. | Embeddings need an embedding provider *and* a store for every deployment mode including air-gapped — a real cost. Keyword may genuinely suffice at the current module count (~40). |
| **Q7** | **Autonomy ceiling** — should an opt-in "apply without review" mode ever exist (e.g. for bulk refactors across many workflows)? | **No** in 6.1. Revisit only after the diff UX has field usage. | Governs whether the applier needs its own authorization + audit path. |
| **Q8** | **Prompt/response retention** — do we persist prompts and responses (for audit, debugging, and eval datasets)? For how long? Redacted or raw? | **Opt-in, off by default**, redacted-only, with a TTL. | Prompts contain workflow content; some deployments will consider that regulated data. Default-on would be a mistake. |
| **Q9** | **Which repo persistence backs assistant state** (conversations, usage counters, transcripts)? A new repository on `IPersistenceProvider`, or in-memory only in 6.1? | **In-memory + optional file** in 6.1; a persisted repository only if Q8 lands as "yes, retain". | Adding a repository touches every provider (Postgres, SQLite, NATS, composite) — that's a slice by itself. |
| **Q10** | **Is a "spec" a free-text prompt, or a structured document** (a `.md`/YAML spec file with inputs, outputs, steps, SLAs) that can live in source control next to the workflow? | Free text in 6.1, with a **spec-file input as 6.1.P3** — the phrase "based on specifications" in the request suggests the structured form is wanted eventually. | Changes the API contract (`spec` becomes a first-class typed object) and enables round-tripping / regeneration. Cheap to design for now, expensive to retrofit. |
| **Q11** | **Cost controls** — what are the default per-request and per-caller daily token budgets, and what happens on exhaustion (hard 429, or degrade to the local provider)? | Hard fail with a `ProblemDetails` 429; **no silent provider substitution** (a quietly-swapped model produces quietly-worse workflows). | Needs a number from the maintainer, not a guess. |
| **Q12** | **Evaluation** — do we build a small **golden-set eval harness** (N specs → expected module sets / validator-clean output) so prompt and model changes are measurable? | **Yes**, folded into 6.1.7, using `ScriptedChatProvider` transcripts + a handful of real-provider opt-in runs. | Without it, every prompt tweak is a vibe. With it, provider swaps become a table of numbers. |
| **Q13** | **Anthropic/OpenAI model pinning** — do we pin exact model ids in config with a documented "known-good" set, or accept whatever the operator types? | Config-driven with a documented known-good set per provider and a startup capability probe. | Tool-calling behaviour differs sharply across model tiers; a too-small model will fail **D3** silently. |

---

## Slices

Each slice lands with its tests and, where user-visible, its UI — per the Phase 5.1 D15 rule
(no "UI later" backlog).

### 6.1.0 — Provider seam, config, credential protection ⏳
- New `Workflow.AI` project (references `Workflow.Core`, `Workflow.Modules` abstractions; **no** vendor SDKs, **no** ASP.NET).
- `IChatProvider`, `IChatProviderFactory`, `ChatRequest`/`ChatResponse`/`ChatDelta`/`ChatMessage`/`ToolCall`, `ProviderCapabilities`.
- `AiOptions` bound from an `AI:` config section: enabled flag, default provider, per-provider blocks (endpoint, model, api key ref, timeouts), `AiHostCeilings` with a `ClampTo` in the `ScriptExecutionConfig` style.
- `IAiCredentialProtector` + Data Protection implementation (**D8**).
- `ScriptedChatProvider` + transcript format (**D10**).
- **Tests:** options binding/clamping, protector round-trip, factory resolution, scripted provider replay.

### 6.1.1 — Provider adapters: Claude · OpenAI/Azure · Local ⏳
- `Workflow.AI.Anthropic` — Messages API, tool use, streaming, system prompt handling.
- `Workflow.AI.OpenAI` — OpenAI + **Azure OpenAI** (deployment-name vs model-name difference is the whole trick), tool calling, streaming.
- `Workflow.AI.Local` — OpenAI-compatible client targeting **Ollama / LM Studio / vLLM / llama.cpp server**, with a documented "no API key required" path and capability probing (many local models have weak or no tool support → **D3** needs a degraded prompt-only fallback path, and the UI must say so).
- Host wiring in `Workflow.Api/Program.cs` + `appsettings.json` samples for all three families **including a fully offline sample**.
- **Tests:** WireMock.Net-backed request/response shape tests per adapter (**no live calls in CI**); opt-in env-gated smoke tests.
- ⚠️ Shape depends on **Q2** and **Q4**.

### 6.1.2 — Grounding layer (digest + catalog retrieval + tools) ⏳
- `IWorkflowDigestBuilder` + token-budgeted renderer; **redaction** as a separately-tested pure function (**D7**).
- Module catalog retrieval over `IModuleRegistry.SearchModules` / `GetModulesByCategory`, rendering `ModuleSchema` (ports, properties, editor types, validation rules, allowed values) as compact tool-readable schema.
- Tool registry + dispatcher; all 6.1.2 tools are **read-only**.
- **Tests:** golden-file digest snapshots, redaction golden files (secrets, connection strings, auth headers), retrieval relevance on a fixture registry, tool dispatch round-trip.

### 6.1.3 — Analysis: explain · review · diagnose ⏳
- `POST /api/v1/assistant/analyze` in `Workflow.Api/V1/AssistantEndpoints.cs` — modes `explain` | `review` | `diagnose`.
- `diagnose` reads the persisted `ExecutionRecord`/`NodeExecutionRecord` via the read-only 3.5 endpoints' data path; payload inclusion is **opt-in per request** (**D7**).
- `review` findings are structured (`code`, `severity`, `nodeId`, `message`, `suggestedFix`) so they can render as designer annotations rather than a wall of prose — and so deterministic rules can be promoted out of the model into `ModuleAwareWorkflowValidator` over time.
- **Tests:** scripted-provider end-to-end per mode, redaction assertions on the outbound prompt, `ProblemDetails` shape when no provider is configured (503, matching the existing scripting/persistence precedent).

### 6.1.4 — Authoring: patch model, applier, repair loop ⏳
- `WorkflowPatch` + typed operations + `WorkflowPatchApplier` (**D4**) — pure, deterministic, exhaustively unit-tested, including hostile input (unknown node ids, duplicate ids, dangling connections, port type mismatch).
- Write-side tools: `propose_patch`, `validate_draft`.
- Validate-and-repair loop with attempt cap (**D5**).
- `POST /api/v1/assistant/generate` (spec → proposal) and `POST /api/v1/assistant/modify` (workflow + instruction → proposal). Response = patch + resulting draft + validation result + rationale + token usage. **Nothing is persisted.**
- **Tests:** applier property/edge cases, repair-loop convergence with a scripted broken-then-fixed transcript, "proposal always validates or reports why", golden specs (**Q12**).

### 6.1.5 — Designer UX: assistant panel + diff review ⏳
- `Workflow.UI.Client/Assistant/` — panel with mode switch (Explain / Review / Generate / Modify), provider + model picker (populated from a read-only `GET /assistant/providers`; **keys never leave the server**), conversation view, streaming tokens (**Q5**), and a visible "this model has no tool support / is running locally" badge.
- **Diff review**: proposed additions render as ghost nodes on the canvas, removals as struck-through, property changes in a side-by-side list; per-operation accept/reject; accepting emits ordinary `IDesignerCommand`s onto the existing `CommandStack` so **undo just works**.
- Review findings render as node badges linked to the panel.
- Graceful degradation when the assistant is disabled or unconfigured (panel hidden, no errors).
- **Tests:** bUnit component tests (`Workflow.Tests.UI`) for panel state, diff rendering, accept/reject → command emission → undo.

```
┌─ Designer ──────────────────────────────────┬─ ✨ Assistant ─────────────┐
│                                             │ [Explain][Review][Build]  │
│   ┌────────┐      ┌──────────┐              │ provider: Local (Ollama)  │
│   │ Start  │─────▶│ HTTP Get │              │ ⚠ no tool support         │
│   └────────┘      └────┬─────┘              │───────────────────────────│
│                        │                    │ ▸ "add retry + DLQ to     │
│                   ┌────▼─────┐              │    the HTTP node"         │
│                   │ Transform│              │───────────────────────────│
│                   └────┬─────┘              │ Proposed changes (3)      │
│                        ┆ (ghost)            │  + node  dlq.write   [✓✗] │
│                   ┌ ─ ─▼─ ─ ─┐              │  ~ prop  retry=3     [✓✗] │
│                   ╎ DLQ Write╎ ← proposed   │  + conn  http.err→dlq[✓✗] │
│                   └ ─ ─ ─ ─ ─┘              │ ✅ draft validates         │
│                                             │ [Accept all] [Discard]    │
└─────────────────────────────────────────────┴───────────────────────────┘
```

### 6.1.6 — Governance, observability, admin ⏳
- `IAiUsageMeter` + budget enforcement (**D9**, numbers from **Q11**), admin-only `GET /api/v1/assistant/usage`, counters in `/api/v1/metrics`, a `ready`-tagged health check that reports provider reachability **without** making a billable call.
- Audit trail per **Q8** (opt-in, redacted, TTL).
- Admin-gated provider configuration surface in `Settings.razor`; keys always masked.
- **Tests:** budget exhaustion → 429 `ProblemDetails`, admin gating, metrics shape, health check with an unreachable provider.

### 6.1.7 — Docs, examples, eval harness ⏳
- `docs/ai-assistant.md`: architecture, provider setup for **all three families** (including a step-by-step **fully offline Ollama walkthrough**), the data-egress/redaction contract in plain language, cost controls, and limitations.
- Example transcripts + a golden-set eval harness (**Q12**).
- Update `phases/README.md`, `DOCUMENTATION_INDEX.md`, `docs/README.md`.

### Post-MVP (P-items)
- **6.1.P1** In-process **LLamaSharp** local provider (**Q4**).
- **6.1.P2** Embeddings + vector retrieval over module docs and example workflows (**Q6**).
- **6.1.P3** Structured **spec documents** as input, with regenerate/round-trip (**Q10**).
- **6.1.P4** Script Studio integration — generate/explain/fix scripts using the existing `/api/v1/scripts/test` loop as an automatic correctness check (a very strong fit: the assistant can *run* what it wrote).
- **6.1.P5** LINQ Studio integration — natural language → typed LINQ query, validated by the existing preview path.
- **6.1.P6** Expose the grounding tools as an **MCP server** so external agents can author DotFlow workflows.
- **6.1.P7** Fine-tuning / few-shot corpus built from accepted proposals (depends on **Q8**).

---

## Success Criteria

- [ ] All three provider families work end-to-end, and **every 6.1 feature is usable with a local model only** (no cloud account required to exercise the phase).
- [ ] The assistant never proposes a module id or property name that isn't in the live registry (**D3** verified by test).
- [ ] Every surfaced proposal passes `ModuleAwareWorkflowValidator`, or is returned with explicit validation errors (**D5**).
- [ ] Redaction has golden-file coverage; no secret variable value, connection string or auth header can reach a provider (**D7**).
- [ ] Provider API keys never appear in a GET response, a log line, a workflow definition, or the WASM payload (**D8**, **D12**).
- [ ] Accept/reject of a proposal is fully undoable via the existing `CommandStack`.
- [ ] CI runs the whole phase's unit tests with **no network and no Docker** (**D10**).
- [ ] A host with the assistant disabled sees zero behaviour change and takes zero new dependencies.

## Demo Workflow

> "Every 15 minutes, fetch `https://api.example.com/orders?since={{lastRun}}`, keep orders over
> $500, write them to `orders.csv`, and if the fetch fails three times in a row, write the error to
> a dead-letter file."

The assistant searches the registry, proposes a patch (schedule trigger + `builtin.http.request` +
a transform filter + CSV write + error handling + a `lastRun` variable), the draft validates clean,
the user rejects the CSV node and accepts the rest, then undoes the whole thing with Ctrl+Z —
**using a local model, with no data leaving the machine.**

---

**Status:** 📝 Plan drafted — **blocked on Q1–Q13** before slice work begins
**Estimated:** ~4–5 weeks for 6.1.0–6.1.7, assuming Q2/Q4 land on the lighter option
**Last Updated:** August 23, 2026
