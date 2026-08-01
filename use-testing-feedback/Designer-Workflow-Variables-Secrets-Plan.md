# Workflow Variable Secrets — Plan

> 🔒 Split out of [`Designer-Workflow-Variables-Plan.md`](Designer-Workflow-Variables-Plan.md) per
> **Q13** ("we really do need to implement IsSecret and redact the values… if we need a separate
> plan for handling this due to complexity then let's make that plan and handle as a separate
> step"). Created 2026-08-01.
>
> The parent plan keeps only the **declaration** (`IsSecret` on `VariableDefinition`, parent V1.2).
> Everything that makes the flag *mean* something lives here.
>
> ⚠️ **This plan has one load-bearing open question (S-Q1).** The answer decides whether this is a
> ~1-week job or a ~3-week one, because it determines whether we can redact by *name* or must
> redact by *value*.

## Summary

| # | Item | Kind | Size | Status |
| --- | --- | --- | --- | --- |
| S1 | Model: `IsSecret` on definition **and** persisted entry | Feature | S | ☐ |
| S2 | Encryption at rest for secret values | Security | M | ☐ |
| S3 | Taint tracking through the binder | Security | M | ☐ |
| S4 | Redaction at the persistence boundary | Security | M | ☐ |
| S5 | API never returns secret values | Security | S | ☐ |
| S6 | UI masking (designer, run dialog, monitor, admin screen) | UX | M | ☐ |
| S7 | Docs — including the honest limits | Docs | S | ☐ |
| S8 | Retire the interim "not for credentials" warning (parent Q16) | Cleanup | S | ☐ |

Recommended order: **S1 → S2 → S5 → S6 → S3 → S4 → S7 → S8**. S5/S6 are cheap and close the most
obvious holes; S3/S4 are the hard part and depend on **S-Q1**.

> ⏱️ **Start S1/S2/S5 alongside the parent plan's V3, not after it.** Q16 ships functional globals
> ahead of this plan behind an advisory warning, so every day this plan lags is a day credentials
> could be sitting in plaintext (parent plan, "Accepted risk from Q16").

---

## Why this is harder than it looks

The instinct is "add a bool, don't print it". That covers the *variable*, but not the **value once
it has been used**, which is where the real exposure is.

### The interpolation problem

Once the parent plan's **V4** lands, a property like an HTTP URL can be authored as
`https://api.example.com/orders?key={{Variable.apiKey}}`. The binder resolves that to a plain
string. From that moment the secret is **indistinguishable from ordinary data** — it is a substring
of a value the persistence layer has no reason to treat specially.

So there are two fundamentally different redaction strategies:

| | **Name-based** | **Value-based** |
| --- | --- | --- |
| How | Drop values we *know* came from a secret variable | Scan outgoing payloads for known secret values and replace them |
| Cost | Low | A scan per persisted record |
| Catches whole-token use (`{{Variable.apiKey}}` alone) | ✅ | ✅ |
| Catches interpolated use (inside a URL/body) | ❌ | ✅ |
| False positives | None | Possible on short/common values |

**Interpolated use is the common case**, which is why name-based alone would ship a flag that
mostly doesn't work. **S-Q1** asks which trade-off you want.

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

Surface 6 being clean is a genuine simplification — the original estimate assumed five streaming
surfaces; it's really **four persisted ones plus a defensive pass on error strings**.

### A precedent already exists

DotFlow already encrypts secrets at rest for database connection strings:
`IConnectionStringProtector` (`Workflow.Modules.Database/Abstractions/IConnectionStringProtector.cs`),
used by `SqliteDbConnectionRegistry` (`:113,:122`) against a
`connection_string_encrypted` column (`Migration_006_DbConnections.cs`, `DbConnectionEntity.cs:31-34`).
**S2 should reuse this abstraction rather than invent a second one.**

---

## S1 — Model

- [ ] S1.1 `bool IsSecret = false` on `VariableDefinition` *(this is the parent plan's V1.2 — listed
      here for completeness)*.
- [ ] S1.2 `IsSecret` on the persisted `VariableEntry` + a migration per store (Sqlite, Postgres,
      NATS), so a value set purely through the API can be marked secret without any workflow
      declaring it. This matters for globals, which are the main credential use case.
- [ ] S1.3 `PUT /api/v1/variables/{name}` accepts `isSecret`; once set, a variable cannot be
      silently un-secreted (require an explicit delete + recreate, so nobody downgrades a
      credential by accident).
- [ ] S1.4 **Forbid `InitialValue` on a secret `VariableDefinition`.** The workflow definition JSON
      is exported, diffed and version-controlled — it must never carry a credential. Validation
      error at save time, plus a designer-side guard.

## S2 — Encryption at rest

- [ ] S2.1 Reuse the `IConnectionStringProtector` pattern (or generalise it to an
      `ISecretProtector`) so secret variable values are stored as ciphertext, mirroring
      `SqliteDbConnectionRegistry`.
- [ ] S2.2 Apply in each `IVariableStore` implementation; decrypt only on the engine's hydration
      path (parent V3.1) and never in a list/read API response (S5).
- [ ] S2.3 **Migrate values written during the interim window.** Per the parent plan's Q16, globals
      ship *before* this plan, so plaintext global values will already exist by the time S2 lands.
      Protect existing rows on migration rather than only new writes — otherwise the first
      credentials anyone stored stay in the clear indefinitely.
- [ ] S2.4 Tests: a secret value never appears in plaintext in the underlying table; round-trip
      through set → hydrate → use works; pre-existing plaintext rows are encrypted by the migration.

## S3 — Taint tracking *(depends on S-Q1)*

Only needed if S-Q1 chooses a name/taint-based approach, or a hybrid.

- [ ] S3.1 When `PropertyBinder` resolves a reference to a secret variable, mark the resulting bound
      value as tainted (a wrapper type, or a per-node set of tainted keys carried on
      `ModuleExecutionContext`).
- [ ] S3.2 Propagate taint through whole-token resolution (easy — the value *is* the secret) and
      flag interpolated results as tainted-by-containment (the string contains a secret).
- [ ] S3.3 Decide whether taint survives a module boundary — a module that reads a tainted input and
      emits a derived output cannot be tracked without real dataflow analysis. **This is the
      inherent limit of the approach** and must be documented in S7 rather than papered over.

## S4 — Redaction at the persistence boundary *(depends on S-Q1)*

- [ ] S4.1 A single choke point that every persisted payload passes through, so redaction can't be
      forgotten at one call site — surfaces 1, 2 and 4 above.
- [ ] S4.2 Implement the strategy chosen in S-Q1. If value-based: maintain the run's known secret
      values in memory, apply a minimum-length threshold (proposed: 8 characters) to avoid
      false-positive redaction of values like `"1"` or `"true"`, and replace with a stable marker
      (`«redacted:apiKey»`) so the monitor can still explain *why* a value is missing.
- [ ] S4.3 Defensive pass on error strings (surface 7) — binding and module exception messages run
      through the same redactor before persistence or realtime emission.
- [ ] S4.4 Measure the cost: a scan per persisted record on a large execution is the main
      performance risk. Benchmark before/after on a workflow with a big payload.

## S5 — API never returns secret values

- [ ] S5.1 `VariableDto` omits `Value` for secrets on list/get; returns name, type, version and
      timestamps only.
- [ ] S5.2 `GET /variables/{name}/history` returns versions without values for secrets.
- [ ] S5.3 Decide whether an **admin reveal** endpoint exists at all. *Proposed: no.* A write-only
      credential store is simpler to reason about, and "I forgot the value" is solved by setting a
      new one. If we do add reveal, it needs `AdminPolicy` **and** an audit log entry.
- [ ] S5.4 Tests: secret value absent from every read endpoint.

## S6 — UI masking

- [ ] S6.1 Designer variables panel (parent V1.4): render `••••••`, never the value; 🔒 badge.
- [ ] S6.2 Token picker Globals group (parent V6.2): name only.
- [ ] S6.3 Run dialog (parent V5.1): password-style input for secret variables; never pre-fill from
      a stored value.
- [ ] S6.4 Execution monitor node inspector: show the redaction marker from S4.2 with a tooltip
      explaining it, rather than a blank.
- [ ] S6.5 Global admin screen (parent V9.2): set-only field; no reveal unless S5.3 says otherwise.
- [ ] S6.6 Tests: no UI surface renders a secret value.

## S7 — Docs

- [ ] S7.1 Add a secrets section to `docs/variables.md` (parent V8.1): how to mark a variable
      secret, where secrets may live (store only, never `InitialValue`), and how they're protected
      at rest.
- [ ] S7.2 **Document the limits honestly.** Depending on S-Q1 this will include some of: taint is
      lost once a module derives a new value from a secret; short secrets below the length threshold
      aren't value-redacted; a module that deliberately writes a secret to an external system is
      not something DotFlow can prevent. A security feature that overstates its guarantees is worse
      than one that states them plainly.
- [ ] S7.3 Note the interaction with **F9** (parent plan): with input templates off by default,
      untrusted upstream data can no longer reference `{{Variable.apiKey}}` at all — that's a
      meaningful part of the secret story and worth stating.

## S8 — Retire the interim credential warning 🧹

The parent plan's **Q16** ships globals ahead of this plan behind an advisory "not for credentials
yet" notice (parent V3.5). It must be removed here, or it goes stale and trains users to ignore
warnings.

- [ ] S8.1 Remove the notice from `docs/variables.md`, the designer's Globals picker group
      (parent V6.2) and the global admin screen (parent V9.2); replace it with the real guidance
      from S7.
- [ ] S8.2 Confirm S2.3's migration has run before the notice comes down — the warning is only
      honestly retractable once pre-existing plaintext values are protected.

---

## Questions — OPEN ❓

- [ ] **S-Q1: name/taint-based or value-based redaction?** This decides the size of S3 + S4.
      - **Name/taint-based** — cheap, no scanning, no false positives; **but** a secret interpolated
        into a larger string (the common case after parent V4) is only caught while taint survives,
        and taint cannot survive a module transforming the value (S3.3).
      - **Value-based** — catches interpolation regardless of provenance; **but** costs a scan per
        persisted record and needs a length threshold to avoid absurd false positives.
      - **Hybrid (proposed)** — taint for the direct cases, plus a value scan at the single
        persistence choke point (S4.1) with a minimum-length threshold. Highest confidence; roughly
        the sum of both costs.
      Which do you want?
  - Hybrid I guess, but we need to understand the performance implications of scanning every persisted record. If the scan is too expensive, we may have to go with name/taint-based and document the limitations.

- [ ] **S-Q2: is an admin "reveal" needed (S5.3)?** *Proposed: no — write-only.* Confirm, because it
      changes whether we need an audit-log concept (DotFlow has none today).
  - Yes, we want a reveal ability — requires an audit log and a new `AdminPolicy` permission.

- [ ] **S-Q3: scope of "secret" for this round.** Is it enough to cover **global and workflow-scoped
      stored variables** (the credential use case), or must a value passed as a **run input** also be
      markable secret? The latter needs the run-start API to carry per-input secrecy, since the
      declaration may not exist for ad-hoc inputs. *Proposed: stored scopes only for round one;
      run-input secrecy follows if asked for.*
  - Having a run input be a markable secret is important because of testing scenarios where a user may want to pass in a secret value that is not stored in the system. We would like this for MVP.

---

*Created 2026-08-01. Surfaces and the `IConnectionStringProtector` precedent verified against the
code at that date. Blocked on S-Q1 for S3/S4; S1, S2, S5, S6 can start immediately.*
