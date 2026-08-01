# Workflow Variable Secrets — Plan

> 🔒 Split out of [`Designer-Workflow-Variables-Plan.md`](Designer-Workflow-Variables-Plan.md) per
> **Q13**. The parent plan keeps only the **declaration** (`IsSecret` on `VariableDefinition`,
> parent V1.2); everything that makes the flag *mean* something lives here.
>
> **Revision 3 (2026-08-01)** — S-Q1–S-Q5 all answered and folded in; **no open questions remain**.
> Two answers grew the scope materially: a **reveal capability** brings an **audit log** with it
> (S9 — a subsystem DotFlow does not have today), and **secret run inputs** are MVP (S10).

## Summary

| # | Item | Kind | Size | Status |
| --- | --- | --- | --- | --- |
| S1 | Model: `IsSecret` on definition **and** persisted entry | Feature | S | ☐ |
| S2 | Encryption at rest for secret values | Security | M | ☐ |
| S3 | Taint tracking through the binder | Security | M | ☐ |
| S4 | Redaction at the persistence boundary (**benchmark-gated**) | Security | M–L | ☐ |
| S5 | API never returns secret values, except audited reveal | Security | S | ☐ |
| S6 | UI masking (designer, run dialog, monitor, admin screen) | UX | M | ☐ |
| S7 | Docs — including the honest limits | Docs | S | ☐ |
| S8 | Retire the interim "not for credentials" warning (parent Q16) | Cleanup | S | ☐ |
| S9 | **Audit log subsystem** (required by S-Q2 reveal) | Feature | **L** | ☐ |
| S10 | **Secret run inputs** (S-Q3, MVP) | Feature | M | ☐ |

Recommended order: **S1 → S2 → S10 → S6 → S5 → S3 → S4.0 (benchmark) → S4 → S9 → S7 → S8**.

> ⏱️ **Start S1/S2 alongside the parent plan's V3, not after it.** Q16 ships functional globals
> ahead of this plan behind an advisory warning, so every day this plan lags is a day credentials
> could be sitting in plaintext (parent plan, "Accepted risk from Q16").

> ⚠️ **S9 is the schedule risk.** An audit log is a new subsystem — store, entity, per-provider
> migrations, read API, retention. Per S-Q4 the *schema* is general-purpose from day one but only
> the **variables** surface is wired now, which keeps it bounded. Reveal (S5.3) must not ship before
> it, because an unaudited reveal is strictly worse than no reveal.

---

## Decisions — S-Q1–S-Q5 RESOLVED ✅ (2026-08-01)

- **S-Q1 Redaction strategy** → **Hybrid** (taint + value scan), **conditional on performance**:
  *"if the scan is too expensive, we may have to go with name/taint-based and document the
  limitations."* Turned into an explicit benchmark gate with pre-agreed criteria — **S4.0**.
- **S-Q2 Admin reveal** → **Yes**, and it requires an **audit log** and a dedicated admin
  permission. → **S9**, and S5.3 flips from "no reveal" to "audited reveal".
- **S-Q3 Scope of "secret"** → **Run inputs must be markable secret for MVP**, for the testing
  scenario of passing a secret that is deliberately *not* stored in the system. → **S10**.
- **S-Q4 Audit log breadth** → **General-purpose schema, variables surface only for now.**
  `AuditEntry` and `IAuditStore` are designed so other subsystems (workflow delete, module
  install/uninstall, auth failures) can adopt them later without a schema rewrite, but **no other
  actions are wired in this round**. **Retention: configurable, defaulting to keep indefinitely.**
- **S-Q5 Reveal permission** → A dedicated **`SecretRevealPolicy`**, with **`AdminRole` also
  satisfying it** so admins and ops engineers who already hold admin need no extra grant. Because
  policies are plain role lists (`AuthServiceCollectionExtensions.cs:59-64`), this is
  `BuildPolicy(p, AdminRole, SecretOperatorRole)` — which means a **new `SecretOperator` role**
  is needed, otherwise the separate policy would be indistinguishable from `AdminPolicy` and
  couldn't be granted to a non-admin.

---

## Why this is harder than it looks

The instinct is "add a bool, don't print it". That covers the *variable*, but not the **value once
it has been used**, which is where the exposure is.

### The interpolation problem

Once the parent plan's **V4** lands, a property can be authored as
`https://api.example.com/orders?key={{Variable.apiKey}}`. The binder resolves that to a plain
string, and from that moment the secret is **indistinguishable from ordinary data** — a substring of
a value the persistence layer has no reason to treat specially.

| | **Name/taint-based** | **Value-based** |
| --- | --- | --- |
| How | Drop values we *know* came from a secret | Scan outgoing payloads for known secret values |
| Cost | Low | A scan per persisted record |
| Whole-token use (`{{Variable.apiKey}}` alone) | ✅ | ✅ |
| Interpolated use (inside a URL/body) | ❌ | ✅ |
| Survives a module transforming the value | ❌ | ✅ (if the substring survives) |
| False positives | None | Possible on short/common values |

**S-Q1 chose the hybrid, gated on S4.0's benchmark.**

### The surfaces

Verified in code:

| # | Surface | Evidence | Leaks? |
| --- | --- | --- | --- |
| 1 | `ExecutionRecord.Inputs` / `Outputs` | `Workflow.Persistence/Models/ExecutionRecord.cs:18-19` | ✅ persisted verbatim |
| 2 | `NodeExecutionRecord.Inputs` / `Outputs` | `NodeExecutionRecord.cs:23-24` | ✅ persisted verbatim |
| 3 | API projections of 1 & 2 | `ExecutionContracts.cs:88-89,130-131` → monitor node inspector | ✅ |
| 4 | Execution-scoped variable store writes | `WorkflowExecutor.cs:2734-2770` | ✅ every `SetVariable` write |
| 5 | `VariableDto.Value` on the variables API | `Workflow.Api/Contracts/VariableContracts.cs:23-66` | ✅ |
| 6 | Realtime hub events | `Workflow.Api/Contracts/RealTime/RealTimeEvents.cs` | ❌ **safe** — ids/timings/percentages only |
| 7 | `NodeFailedEvent.Error` / binding error strings | `RealTimeEvents.cs:59` | ⚠️ only if an exception message embeds a value |

Surface 6 being clean is a genuine simplification — four persisted surfaces plus a defensive pass on
error strings, not five streaming ones.

### A precedent already exists

DotFlow already encrypts secrets at rest for database connection strings —
`IConnectionStringProtector` (`Workflow.Modules.Database/Abstractions/IConnectionStringProtector.cs`),
used by `SqliteDbConnectionRegistry` (`:113,:122`) against a `connection_string_encrypted` column
(`Migration_006_DbConnections.cs`, `DbConnectionEntity.cs:31-34`). **S2 reuses this abstraction
rather than inventing a second one.**

---

## S1 — Model

- [ ] S1.1 `bool IsSecret = false` on `VariableDefinition` *(this is the parent plan's V1.2, listed
      here for completeness)*.
- [ ] S1.2 `IsSecret` on the persisted `VariableEntry` + a migration per store (Sqlite, Postgres,
      NATS), so a value set purely through the API can be marked secret without any workflow
      declaring it. This matters for globals, the main credential use case.
- [ ] S1.3 `PUT /api/v1/variables/{name}` accepts `isSecret`; once set, a variable cannot be
      silently un-secreted (require explicit delete + recreate, so nobody downgrades a credential by
      accident).
- [ ] S1.4 **Forbid `InitialValue` on a secret `VariableDefinition`.** The workflow definition JSON
      is exported, diffed and version-controlled — it must never carry a credential. Validation
      error at save time plus a designer-side guard.

## S2 — Encryption at rest

- [ ] S2.1 Reuse the `IConnectionStringProtector` pattern (or generalise it to an `ISecretProtector`)
      so secret values are stored as ciphertext, mirroring `SqliteDbConnectionRegistry`.
- [ ] S2.2 Apply in each `IVariableStore` implementation; decrypt only on the engine's hydration
      path (parent V3.1), never in a list/read API response (S5).
- [ ] S2.3 **Migrate values written during the interim window.** Per the parent plan's Q16, globals
      ship *before* this plan, so plaintext global values will already exist. Protect existing rows
      on migration, not just new writes — otherwise the first credentials anyone stored stay in the
      clear indefinitely.
- [ ] S2.4 Tests: a secret value never appears in plaintext in the underlying table; set → hydrate →
      use round-trips; pre-existing plaintext rows are encrypted by the migration.

## S3 — Taint tracking

The cheap half of the hybrid, and the fallback if S4.0 fails.

- [ ] S3.1 When `PropertyBinder` resolves a reference to a secret variable, mark the resulting bound
      value as tainted (a per-node set of tainted keys carried on `ModuleExecutionContext`, rather
      than a wrapper type — less invasive to module authors).
- [ ] S3.2 Seed taint from three origins: secret variables hydrated from the store (S2), secret run
      inputs (S10), and whole-token resolutions of either.
- [ ] S3.3 Propagate through whole-token resolution (trivial — the value *is* the secret) and mark
      interpolated results as tainted-by-containment.
- [ ] S3.4 **Taint does not survive a module transforming the value.** A module that reads a tainted
      input and emits a derived output cannot be tracked without real dataflow analysis. This is the
      inherent limit of the approach; the S4 value scan is precisely what covers it, and if S4.0
      forces the taint-only fallback this limit **must** be documented (S7.2) rather than papered
      over.

## S4 — Redaction at the persistence boundary

- [ ] **S4.0 Benchmark spike — decision gate for S-Q1.** Run *before* committing to the value scan.
      Pre-agreed criteria so the decision is mechanical rather than a judgement call:
      - **Zero-secret fast path must cost effectively nothing.** The overwhelming majority of
        executions have no secrets at all; if the run's secret set is empty the redactor must return
        the payload untouched with no scan and no allocation. Measure this first — it likely makes
        the whole concern moot for normal traffic.
      - **With secrets present:** redaction adds **≤ 5% to node-record persist time at p95**, and
        **≤ 2 ms per record** for payloads up to 256 KB.
      - Optimisations to apply before declaring failure: scan only string leaves; ordinal
        comparison; a single Aho–Corasick pass for multiple secrets rather than N× `Replace`;
        minimum-length threshold (proposed **8 characters**) which also avoids absurd false
        positives on values like `"1"` or `"true"`.
      - **If the criteria still can't be met** → fall back to **taint-only** (S3) and document the
        interpolation limit prominently (S7.2). Record the benchmark numbers in this plan either way.
- [ ] S4.1 A single choke point that every persisted payload passes through, so redaction can't be
      forgotten at one call site — surfaces 1, 2 and 4.
- [ ] S4.2 Replace with a stable, explanatory marker (`«redacted:apiKey»` where the name is known
      from taint, `«redacted»` where only the value matched) so the monitor can explain *why* a
      value is missing rather than showing a confusing blank.
- [ ] S4.3 Defensive pass on error strings (surface 7) — binding and module exception messages go
      through the same redactor before persistence or realtime emission.
- [ ] S4.4 Tests: whole-token secret redacted; **interpolated** secret redacted (the case taint
      alone misses); short secret below threshold behaves per the documented rule; zero-secret run
      is byte-identical to today.

## S5 — API secret handling

- [ ] S5.1 `VariableDto` omits `Value` for secrets on list/get — name, type, version and timestamps
      only.
- [ ] S5.2 `GET /variables/{name}/history` returns versions without values for secrets.
- [ ] S5.3 **Audited reveal** *(S-Q2)* — a dedicated endpoint
      (`POST /api/v1/variables/{name}/reveal`, deliberately not a `GET`, so it never lands in
      browser history, proxy logs or a shared link). Requires **`SecretRevealPolicy`** *(S-Q5)*
      **and** writes an audit entry (S9) *before* returning the value. **Must not ship before S9.**
- [ ] S5.4 Auth wiring for S-Q5: add a `SecretOperator` role and a `SecretRevealPolicy` composed as
      `BuildPolicy(p, AdminRole, SecretOperatorRole)` so **Admin satisfies it automatically**
      (`AuthConfiguration.cs:31-40`, `AuthServiceCollectionExtensions.cs:44-64`). Update the policy
      table in `docs/rest-api.md:54-55`.
- [ ] S5.5 Rate-limit reveal and make failures auditable too — a denied reveal attempt is exactly
      the event a security review will ask about.
- [ ] S5.6 Tests: secret value absent from every ordinary read endpoint; reveal requires the policy;
      an Admin can reveal without an explicit `SecretOperator` grant; reveal writes an audit entry;
      a reveal that fails authorisation is still audited.

## S6 — UI masking

- [ ] S6.1 Designer variables panel (parent V1.4): render `••••••`, never the value; 🔒 badge.
- [ ] S6.2 Token picker Globals group (parent V6.2): name only.
- [ ] S6.3 Run dialog (parent V5.1): password-style input for secret variables; never pre-fill from
      a stored value; plus the ad-hoc secret toggle from S10.3.
- [ ] S6.4 Execution monitor node inspector: show S4.2's marker with a tooltip explaining it.
- [ ] S6.5 Global admin screen (parent V9.2): set-only field, with a **Reveal** action for holders
      of the reveal policy that states plainly that the action is logged.
- [ ] S6.6 Tests: no UI surface renders a secret value except the explicit reveal flow.

## S7 — Docs

- [ ] S7.1 Secrets section in `docs/variables.md` (parent V8.1): how to mark a variable secret,
      where secrets may live (store or run input, never `InitialValue`), how they're protected at
      rest, and how reveal works and that it is audited.
- [ ] S7.2 **Document the limits honestly.** Depending on S4.0's outcome this includes some of:
      taint is lost once a module derives a new value from a secret (S3.4); secrets below the length
      threshold aren't value-redacted; a module that deliberately sends a secret to an external
      system is not something DotFlow can prevent. A security feature that overstates its guarantees
      is worse than one that states them plainly.
- [ ] S7.3 Note the interaction with parent **F9**: with input templates off by default, untrusted
      upstream data can no longer reference `{{Variable.apiKey}}` at all — a meaningful part of the
      secret story.
- [ ] S7.4 Document the audit log (S9): what is recorded, who can read it, and retention.

## S8 — Retire the interim credential warning 🧹

The parent plan's **Q16** ships globals ahead of this plan behind an advisory "not for credentials
yet" notice (parent V3.5). It must be removed here, or it goes stale and trains users to ignore
warnings.

- [ ] S8.1 Remove the notice from `docs/variables.md`, the designer's Globals picker group
      (parent V6.2) and the global admin screen (parent V9.2); replace with the real guidance from S7.
- [ ] S8.2 Confirm S2.3's migration has run first — the warning is only honestly retractable once
      pre-existing plaintext values are protected.

## S9 — Audit log subsystem 📜 *(required by S-Q2)*

**Finding.** DotFlow has **no audit concept today** — no audit store, entity, endpoint or retention
policy. Reveal (S5.3) cannot ship responsibly without one.

Per **S-Q4** the split is: **general-purpose schema and store now, variables actions only wired
now.** That keeps this round bounded while avoiding the painful retrofit of an audit schema later.

- [ ] S9.1 `AuditEntry` — deliberately **generic**: actor (subject + claims), action, resource
      (type + scope + name), timestamp, source IP / user agent, and outcome (allowed / denied) with
      a reason. Nothing variable-specific in the shape, so a later `workflow.delete` or
      `module.install` entry needs no migration.
- [ ] S9.2 **The audit log never records secret values.** It records that a reveal happened, by
      whom, and against which name — never the value revealed, and never the value on a set. An
      audit log that stores the credentials it is auditing defeats the purpose.
- [ ] S9.3 `IAuditStore` + implementations per persistence provider (Sqlite, Postgres, NATS) with
      migrations, mirroring how `IVariableStore` is structured.
- [ ] S9.4 **Append-only semantics** — no update or delete path on the interface (pruning in S9.6 is
      the sole exception and is retention-driven, not caller-driven). An audit log that callers can
      rewrite is not an audit log.
- [ ] S9.5 Wire the **variables surface only**: reveal, set, delete across all scopes. Leave hooks
      documented but unwired for other subsystems — explicitly *not* in this round.
- [ ] S9.6 Retention: **configurable, default keep indefinitely** *(S-Q4)*. Pruning is opt-in and
      logs its own activity.
- [ ] S9.7 `GET /api/v1/audit` with filtering (actor, action, resource, time range), behind
      `AdminPolicy`.
- [ ] S9.8 Tests: reveal writes an entry; denied reveal writes an entry; **no entry ever contains a
      secret value**; entries cannot be mutated through the public interface; retention prunes only
      when configured.

## S10 — Secret run inputs 🎫 *(new — S-Q3, MVP)*

**Finding.** The testing scenario is passing a secret that is deliberately **not** stored in the
system. Today `StartExecutionRequest(Dictionary<string, object?>? Inputs, string? VariableWriteMode)`
(`Workflow.Api/Contracts/ExecutionContracts.cs:16-18`) has no way to say "this one is sensitive", and
run inputs are persisted verbatim into `ExecutionRecord.Inputs` (surface 1) — so an ad-hoc secret
leaks immediately.

- [ ] S10.1 Extend `StartExecutionRequest` with the set of input names to treat as secret
      (proposed: `IReadOnlyList<string>? SecretInputs` — additive and trivial to ignore for existing
      callers, versus reshaping `Inputs` into objects which would break every client).
- [ ] S10.2 Engine: seed S3's taint set from these names at execution start, and **exclude them from
      `ExecutionRecord.Inputs`** at persist time — a run input the user explicitly called ephemeral
      should not be written to history at all.
- [ ] S10.3 Run dialog (parent V5.1/V5.2): declared secret variables render as password fields
      automatically; **undeclared** ad-hoc inputs get a per-row 🔒 toggle, which is the actual
      testing scenario from S-Q3. The JSON escape hatch needs a companion way to mark names secret.
- [ ] S10.4 **Secret run inputs are never persisted by `VariableWriteMode`.** If the run uses
      `workflow` or `dual`, a secret input must not be written to workflow scope — otherwise "not
      stored in the system" quietly becomes "stored permanently, in a broader scope".
      *(Design decision inferred from the S-Q3 wording rather than asked — flag if you disagree.)*
- [ ] S10.5 Tests: secret run input never appears in the execution record; is usable via
      `{{Variable.x}}` during the run; is not persisted under any `VariableWriteMode`; the JSON
      toggle path marks secrecy correctly.

---

## Questions — S-Q4 / S-Q5 RESOLVED ✅ (round 2)

- **S-Q4 Audit log breadth** → **General-purpose schema, variables surface only for now**; other
  actions land later without a rewrite. **Retention configurable, default indefinite.** Folded into
  S9.1, S9.5, S9.6.
- **S-Q5 Reveal permission** → **`SecretRevealPolicy`**, with **`AdminRole` also satisfying it**.
  Folded into S5.3 and S5.4 — note this requires a new `SecretOperator` role, since a policy that
  only Admin can satisfy would be indistinguishable from `AdminPolicy`.

**No open questions remain across either plan. Every item is ready to start.**

### Planning summary 📌

Both documents are now decision-complete:

| | Parent plan | Secrets plan |
| --- | --- | --- |
| Items | V1–V9 | S1–S10 |
| Questions asked / answered | 16 / 16 | 5 / 5 |
| Blocked on a decision | none | none |
| Blocked on a measurement | none | S4 (on the S4.0 benchmark) |
| Sequencing constraints | V3 ships ahead of secrets under Q16, carrying the V3.5 interim warning | S5.3 reveal gated on S9; S8 gated on S2.3 |

The only remaining gate is **S4.0**, which is a measurement rather than a decision — and its
outcome is pre-agreed either way (hybrid if it meets the criteria, taint-only with documented
limits if it doesn't).

---

*Revision 3, 2026-08-01. Surfaces, the `IConnectionStringProtector` precedent and the auth policy
mechanism verified against the code at that date. **All questions (S-Q1–S-Q5) are answered.**
S1, S2, S6 and S10 can start immediately; S4's strategy is gated on the S4.0 benchmark; S5.3
(reveal) is gated on S9.*
