# Phase 2.4: Database Modules (Weeks 11-14) 🗄️

Made with 💖 by Ami-Chan! UwU ✨

[Back to Phase 2](Phase2-CoreFeatures.md) | [All Phases](README.md) | [Design Doc](../new-feature-design/Phase2-4-DatabaseModules-Design.md)

---

## Overview

Phase 2.4 ships DotFlow's **database family** with a **typed-first authoring experience** — the primary surface is `builtin.database.linq`: strongly-typed linq2db queries authored against a UI-selected table catalog, **Roslyn-compiled at publish time**, validated in an in-memory SQLite sandbox, and executed in a collectible `AssemblyLoadContext`. Users should **never have to hand-write raw SQL unless absolutely necessary** — the raw-SQL modules (query, execute, transaction, bulk-insert) still ship, but explicitly as the **escape hatch** for vendor-specific DDL, stored-proc-ish things, and perf-critical bulk paths that typed linq can't express yet~ 🌷

Both families sit on **shared connection/provider infrastructure** (`IDbConnectionFactory`, `IDbProviderRegistry`, `IDbConnectionRegistry`, `IDbTransactionScope`) so neither is a second-class citizen.

The design is the outcome of the [Phase 2.4 design exploration](../new-feature-design/Phase2-4-DatabaseModules-Design.md) where we weighed:
- **Option A** — raw SQL only (familiar but stringly-typed)
- **Option B** — Roslyn + typed linq2db only (gorgeous UX, but doesn't cover DDL/bulk edge cases)
- **Option C** — layered: ship A as 2.4.a MVP, slot B in as post-MVP *(previously chosen)*
- **Option D** — *typed-first layered: **B is the primary MVP surface**, A ships alongside as the escape hatch* ⭐ **chosen (July 2026 re-plan)**

> **Re-plan note (July 2026):** Per product direction — "users should not have to do raw SQL unless absolutely necessary, even for MVP" — Option C's sequencing is superseded. The former post-MVP slice **2.4.b is promoted into the MVP** with a full task breakdown below (2.4.b.0–2.4.b.6). The raw-SQL family remains in scope because the typed family *cannot* cover everything (bulk insert, vendor DDL, hand-tuned SQL), but docs/UI must present typed linq as the default path~ ✨

**Timeline:** 4 weeks (Weeks 11-14) — 2.4.a (infra + escape-hatch SQL family) Weeks 11-12 · 2.4.b (typed linq family) Weeks 13-14
**Complexity:** 🔴 High — the shared infrastructure (`IDbConnectionFactory`, `IDbProviderRegistry`, `IDbConnectionRegistry`, `IDbTransactionScope`) shapes both families, and 2.4.b adds a Roslyn compile-cache-execute pipeline + ALC lifecycle + sandbox preview

> **CopilotNote:** Two hot paths now: `Workflow.Modules.Database/*` (shared infra + 4 escape-hatch modules) and `Workflow.Modules.Database.Linq/*` (Roslyn compiler + `builtin.database.linq` + previewer). Roslyn (~30MB transitive) stays quarantined in the `.Linq` project so raw-SQL-only deployments don't pay for it. `Workflow.Api` changes for named-connection CRUD (2.4.a.5) **and** the linq validate/preview/compile endpoints (2.4.b.5). Tests stay Docker-free in `Workflow.Tests` for SQLite scenarios; Postgres E2E lives in `Workflow.Tests.Integration` via Testcontainers~ 🌸

### Confirmed Design Decisions ✅

| # | Decision |
|---|----------|
| **D1 linq2db is the only DB access library** | All four modules go through `LinqToDB.Data.DataConnection`. No raw `IDbConnection`/`DbCommand` direct use in modules — keeps connection-pooling, provider-quirks handling, and bulk-copy routing in one place. |
| **D2 Shared infra extracted from day one** | `IDbConnectionFactory`, `IDbProviderRegistry`, `IDbConnectionRegistry`, `IDbTransactionScope` live in a new `Workflow.Modules.Database` project that 2.4.b (`Workflow.Modules.Database.Linq`) will reference. |
| **D3 Named connections preferred over inline `connectionString`** | Modules accept `connectionId` (string, references DI/config-bound registration) **or** raw `connectionString` (escape hatch). Hides credentials from workflow definitions; matches HTTP module's named-credential pattern from 2.3.4. |
| **D4 Named connections exposed via runtime API + appsettings** | Per Q9 resolution — runtime CRUD is the default (opt-out via config), credentials encrypted at rest via ASP.NET Data Protection (see 2.4.a.5). |
| **D5 Postgres + SQLite only in MVP** | Per Q6 resolution — MySQL + SQL Server deferred to **2.4.a.P3** (Testcontainers-driven). Two providers prove the abstraction without doubling the test matrix. |
| **D6 Provider key strings, not enums** | `provider: "postgres"` / `"sqlite"` as strings — `IDbProviderRegistry` maps to `linq2db`'s `ProviderName` constants. Allows plugins to register new providers without touching core enums. |
| **D7 Parameterisation is mandatory** | The parameter-binding code never concatenates SQL strings. Properties carrying `{{ }}` template syntax bind via `PropertyBinder`; SQL body itself is verbatim (no template expansion on `query`/`command` text). |
| **D8 Outputs always materialised** | `query` returns `IReadOnlyList<IReadOnlyDictionary<string, object?>>` (rows) — never `IQueryable<T>` or open data readers. Forward-compat with 2.4.b's "no ALC-rooted references" invariant. |
| **D9 IBlobStore `compiled-modules/` namespace owned by 2.4.b** | Reserved during 2.4.a (no writes); **actively written by 2.4.b.2's assembly cache within the MVP** (July 2026 re-plan). |
| **D10 Workflow table catalog stays manual** | Per Q4 resolution — `IWorkflowTableCatalog` ships with manual registration only. Auto-discovery from registered databases tracked in **2.4.b.P3**. |
| **D11 Transaction module: sequential ops, no DSL; `parameterSets` for batch performance** | Per Q11 resolution — `operations` is a plain list of `DbOperationSpec`. Each op is either single-mode (`parameters`) or batch-mode (`parameterSets`). Conditional abort at the workflow-graph level via `builtin.condition` + `builtin.throw` + `builtin.trycatch`; inline SQL guards (WHERE clauses) handle per-row no-ops in batch mode. `parameters` and `parameterSets` are mutually exclusive. See §2.4.a.3 Diagrams A–C. |
| **D12 Typed linq is the primary authoring surface (MVP)** | Per July 2026 re-plan — `builtin.database.linq` ships in the MVP (2.4.b, Weeks 13-14). Docs, UI defaults, and examples lead with typed linq; raw-SQL modules are documented as the escape hatch. Users should not have to hand-write SQL unless the typed surface can't express the operation. |
| **D13 Raw SQL family retained as escape hatch** | The 4 raw-SQL modules (2.4.a.1–4) still ship — bulk insert, vendor-specific DDL, hand-tuned SQL, and stored procs (post-MVP) are legitimately outside typed linq's V1 coverage. They also serve as the fallback while the table catalog for a given connection is unpopulated. |
| **D14 Roslyn stays quarantined in `Workflow.Modules.Database.Linq`** | Even though 2.4.b is now MVP, the ~30MB Roslyn transitive dep lives in its own project/package so raw-SQL-only deployments don't pay for it. `AddDatabaseLinqModules()` is a separate opt-in DI call (called by default from `Workflow.Api`). |
| **D15 Compile at publish time, never per-execution** | Roslyn compile (~200–800ms) happens when the workflow definition is published (or via `POST /api/database/linq/compile`); emitted assembly bytes cached in `IBlobStore` under `compiled-modules/{definitionId}/{nodeId}/{hash}.dll`. Execution loads from cache into a collectible ALC. (Mitigates design-doc concern C7.) |
| **D16 4-week serial timeline signed off** | Per Q14 resolution — Weeks 11-14, Phase 2.5+ shifts accordingly. Parallel-track (2.4.b starting after 2.4.a.0/2.4.a.5 land) stays an opportunistic optimisation if staffing allows. |
| **D17 Trusted-author gate + whitelists for V1** | Per Q15 resolution — compile/save of linq modules gated to trusted authors; reference/usings/syntax whitelists enforced at compile. Fuller sandbox (process isolation / WASM) revisited in Phase 3. |
| **D18 UI panel is a separate tracked slice (2.4.b.P4)** | Per Q16 resolution — 2.4.b MVP is API-only; the Monaco editor panel MVP is scoped in [Phase2-4-LinqEditorPanel-Design.md](../new-feature-design/Phase2-4-LinqEditorPanel-Design.md). |
| **D19 One-shot catalog import is MVP** | Per Q17 resolution — `POST /api/database/catalog/{connectionId}/import` ships in 2.4.b.4 so the typed path is usable without hand-registering tables. Versioned auto-discovery remains 2.4.b.P3. |

### TO RESOLVE 🤔

> All Q1–Q10 from the design exploration have been resolved — see [Phase2-4-DatabaseModules-Design.md §7](../new-feature-design/Phase2-4-DatabaseModules-Design.md#7-open-questions-for-clarification-).

**Newly raised during task breakdown:**

- [x] **Q11 Transaction module batch-op shape:** should each op in the `operations` array carry its own `sql` + `parameters`, or should we support a richer DSL (e.g. "if op N returns 0 rows, abort")?
  - **Resolved (D11):** Simple sequential ops — no DSL. "Conditional abort" is composed at the workflow level using the existing `builtin.condition` + `builtin.throw` + `builtin.trycatch` nodes from 2.2.4. See the block diagram in §2.4.a.3 "Batch-op design" for the full pattern.
- [x] **Q12 Postgres `RETURNING` clause:** `execute` returns `lastInsertId` (long, nullable) — for Postgres `INSERT … RETURNING id` is the idiomatic way. Should we auto-rewrite simple `INSERT` to add `RETURNING id` when the user reads `lastInsertId`, or document "users must write `RETURNING` themselves on Postgres"? V1 recommend: **document only**, no rewriting (avoids parser surprises).
  - **RESOLVED:** Document only for now.
- [x] **Q13 SQLite `ATTACH DATABASE`:** out-of-scope for V1 (keep one connection = one file/path). Document as a non-goal.
  - **RESOLVED:** Agreed — non-goal.

**Newly raised during the July 2026 typed-first re-plan — all resolved ✅:**

- [X] **Q14 Timeline extension sign-off:** promoting 2.4.b into the MVP pushes Phase 2.4 from 2 weeks to **4 weeks (Weeks 11-14)**, which shifts Phase 2.5+ (File System Modules) accordingly. Alternative: run 2.4.a and 2.4.b with two engineers in parallel (Weeks 11-13, 2.4.b starts once 2.4.a.0 shared infra lands ~day 3). Which do we want?
  - **RESOLVED (D16):** 4-week serial plan (Weeks 11-14) signed off. The parallel-track option remains available as an opportunistic optimisation if staffing allows — 2.4.b only hard-depends on 2.4.a.0 + 2.4.a.5.
- [X] **Q15 Trusted-author gate still acceptable for an MVP feature?** Q2 accepted "only trusted authors can save linq modules" while 2.4.b was post-MVP. Now that typed linq is the *default* authoring path, is that gate still OK for GA, or do we need the fuller sandbox story (process isolation / WASM) sooner? V1 recommendation: keep the gate + reference/syntax whitelists; revisit in Phase 3.
  - **RESOLVED (D17):** Keep the trusted-author gate + reference/usings/syntax whitelists for V1. Fuller sandbox (process isolation / WASM) revisited in Phase 3.
- [X] **Q16 UI editor scope for MVP:** typed-first authoring really shines with an editor (Monaco + diagnostics squigglies from `/validate`). Is a `Workflow.UI` code-editor panel in scope for 2.4.b.5, or is API-only (validate/preview/compile endpoints consumable by the UI later) acceptable for MVP? V1 recommendation: **API-only**, UI panel tracked as 2.4.b.P4.
  - **RESOLVED (D18):** API-only for the 2.4.b MVP. The UI panel MVP is mapped out in its own design doc — [**Phase2-4-LinqEditorPanel-Design.md**](../new-feature-design/Phase2-4-LinqEditorPanel-Design.md) — and tracked as slice **2.4.b.P4** (~1 week, single-PR sized, has its own UQ1–UQ4 clarification items).
- [X] **Q17 Table catalog bootstrap friction:** typed linq needs `IWorkflowTableCatalog` entries (manual per Q4/D10) before anyone can author a typed query. Manual-only registration makes the *default* path high-friction. Should we pull a minimal one-shot schema introspection ("import tables from connection X" API) forward into 2.4.b.4, ahead of the full versioned auto-discovery in 2.4.b.P3?
  - **RESOLVED (D19):** Yes — the one-shot import (`POST /api/database/catalog/{connectionId}/import`) is **confirmed in-scope for 2.4.b.4** (already spec'd there). Versioned auto-discovery stays in 2.4.b.P3.


---

## Pre-Existing Work (from earlier phases) ✅

| Component | File | Status |
|-----------|------|--------|
| `IWorkflowModule` contract | `Workflow.Modules/Abstractions/IWorkflowModule.cs` | ✅ Reused as-is |
| `ModuleResult` + `Ok`/`Fail` | `Workflow.Modules/Abstractions/IWorkflowModule.cs` | ✅ Used for query/execute results |
| `ModuleExecutionContext.Services` | `Workflow.Modules/Abstractions/IWorkflowModule.cs` (line ~135) | ✅ Per-call DI resolution pattern (mirrors `HttpRequestModule`) |
| `PropertyBinder` (`{{ }}` templating) | `Workflow.Engine/Services/PropertyBinder.cs` | ✅ Reused for parameter values (NOT for SQL body — D7) |
| `IBlobStore` (Phase 2.1.4) | `Workflow.Persistence/Abstractions/IBlobStore.cs` | ✅ `compiled-modules/` namespace reserved (no writes in 2.4.a) |
| `IPersistenceProvider` | `Workflow.Persistence/Abstractions/IPersistenceProvider.cs` | ✅ For 2.4.a.5 named-connection storage when persisted-CRUD enabled |
| `linq2db` + `Npgsql` packages | `Directory.Packages.props` | ✅ Already used by `Workflow.Persistence.Postgres` |
| `Microsoft.Data.Sqlite` | `Directory.Packages.props` | ✅ Used by `Workflow.Persistence.Sqlite` |
| `WorkflowDataConnection` pattern | `Workflow.Persistence.Sqlite/Data/WorkflowDataConnection.cs` | ✅ Pattern reference for module-side `DataConnection` lifecycle |
| `IWebhookRegistrationRepository` | `Workflow.Persistence/Abstractions/IWebhookRegistrationRepository.cs` | ✅ Pattern reference for `IDbConnectionRegistry` (named-record CRUD) |
| `BuiltinModuleRegistration` | `Workflow.Modules/Builtin/BuiltinModuleRegistration.cs` | ✅ Append new module IDs here |
| `AddWorkflowModules` (2.3.0) | `Workflow.Modules/WorkflowModulesServiceCollectionExtensions.cs` | ✅ Extend to call `AddDatabaseModules()` |

> **CopilotNote:** The repository pattern from `Workflow.Persistence.Sqlite/Repositories/SqliteWebhookRegistrationRepository.cs` is the closest existing analog for `SqliteDbConnectionRegistry` (named-record CRUD with optional persistence). Mirror its lifecycle (factory + lazy connection) and migration shape (`Migration_NNN_*.cs` auto-discovered by `SqliteMigrationRunner`)~ 💖

---

## 2.4.a.0 Shared Infrastructure 🛠️ (foundation)

> **Purpose:** Land the shared project + the four abstractions (`IDbConnectionFactory`, `IDbProviderRegistry`, `IDbConnectionRegistry`, `IDbTransactionScope`) + the `IWorkflowTableCatalog` stub. Every other 2.4.a slice consumes these~ ✨

**Complexity:** 🟡 Low-Medium *(no real logic, but the contracts shape everything downstream)*

### Tasks

- [x] **`Workflow.Modules.Database` project layout** 🌷
  - [x] New project: `Workflow.Modules.Database/Workflow.Modules.Database.csproj`
  - [x] References: `Workflow.Core`, `Workflow.Modules` (for `IWorkflowModule`), `linq2db` (**5.4.1** — repo's pinned version, not 8.0 as originally guessed; `ProviderName.PostgreSQL15`/`SQLiteMS` both available), `linq2db.PostgreSQL`, `Npgsql`, `Microsoft.Data.Sqlite`, `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Options`, `Microsoft.Extensions.Logging.Abstractions`
  - [x] Add to `Workflow.sln`
  - [x] Folder layout:
    - [x] `Abstractions/` — interfaces only
    - [x] `Providers/` — registry + default provider mappings
    - [x] `Connections/` — `IDbConnectionFactory` impl + named-connection registry
    - [x] `Transactions/` — `IDbTransactionScope` impl
    - [x] `Catalog/` — `IWorkflowTableCatalog` stub
    - [x] `Configuration/` — `DatabaseConnectionsOptions` (pulled forward from 2.4.a.5 — registry hydration needs it)
    - [ ] `Builtin/` — the four built-in modules (filled in by 2.4.a.1–4)
  - [x] `DatabaseModuleServiceCollectionExtensions.AddDatabaseModules(this IServiceCollection)` — single entry point (uses `TryAdd` so hosts/plugins can pre-register overrides per D6)
  - [x] ~~`AddWorkflowModules` updated to also call `AddDatabaseModules()`~~ → **CORRECTED during implementation:** `Workflow.Modules.Database` references `Workflow.Modules`, so the reverse call would be circular. **The host (`Workflow.Api`) wires `AddDatabaseModules()` explicitly** — same pattern D14 prescribes for the linq family. `Program.cs` wiring lands in 2.4.a.5.

- [x] **`IDbProviderRegistry`** 🗂️ — landed as specced (`DefaultDbProviderRegistry`, case-insensitive keys, `UnknownProviderException`, DI-replaceable per D6)

- [x] **`IDbConnectionRegistry`** 📇 — landed as specced (`DbConnectionDescriptor` record, `InMemoryDbConnectionRegistry` hydrating from `DatabaseConnectionsOptions`, case-insensitive lookup, LanguageExt `Option<>` returns)

- [x] **`IDbConnectionFactory`** 🔌 — landed as specced (`DefaultDbConnectionFactory` via `DataOptions().UseConnectionString(...)`, caller-owns-disposal, disabled connections resolve as `ConnectionNotFoundException`)

- [x] **`IDbTransactionScope`** 💼 — landed as specced (`DefaultDbTransactionScope`, auto-rollback-on-dispose, terminal commit/rollback state machine, scope owns the connection; `CreateTransactionAsync` extensions for both named and raw connections)

- [x] **`IWorkflowTableCatalog` (stub)** 📚 — landed as specced (`InMemoryWorkflowTableCatalog`, manual upsert only per Q4/D10; `compiled-modules/` blob namespace remains reserved-not-written)

- [x] **Common exception types** 🚨 — `DatabaseModuleException` base + `UnknownProviderException` + `ConnectionNotFoundException` + `SqlParameterBindingException` (with structured `ProviderKey`/`ConnectionId`/`ParamName` properties)

### Tests (target ~10 — **17 shipped ✅**): → `Workflow.Tests/Modules/Database/SharedInfrastructureTests.cs`

- [x] `ProviderRegistry_KnownPostgres_ResolvesToPostgreSQL15`
- [x] `ProviderRegistry_KnownSqlite_ResolvesToSQLiteMS`
- [x] `ProviderRegistry_UnknownKey_ThrowsUnknownProviderException`
- [x] `ProviderRegistry_KnownProviders_ContainsPostgresAndSqlite` *(bonus)*
- [x] `ConnectionRegistry_ConfigBoundEntry_LookupByIdReturnsDescriptor`
- [x] `ConnectionRegistry_UpsertThenGet_RoundTrips`
- [x] `ConnectionRegistry_DeleteUnknown_ReturnsFalse`
- [x] `ConnectionRegistry_LookupCaseInsensitive`
- [x] `ConnectionRegistry_List_ReturnsAllEntries` *(bonus)*
- [x] `ConnectionFactory_NamedConnection_CreatesDataConnection` *(SQLite temp-file, real round-trip)*
- [x] `ConnectionFactory_UnknownConnectionId_ThrowsConnectionNotFound`
- [x] `ConnectionFactory_DisabledConnection_ThrowsConnectionNotFound` *(bonus)*
- [x] `ConnectionFactory_RawProviderAndConnectionString_CreatesDataConnection` *(bonus)*
- [x] `ConnectionFactory_RawUnknownProvider_ThrowsUnknownProviderException` *(bonus)*
- [x] `TransactionScope_AutoRollbackOnDispose_NoCommit` *(SQLite, INSERT not visible after dispose)*
- [x] `TransactionScope_Commit_PersistsWork` *(bonus)*
- [x] `TransactionScope_DoubleCommit_ThrowsInvalidOperation` *(bonus)*

---

## 2.4.a.1 Database Query Module 🔍 (`builtin.database.query`)

> **Escape-hatch family note (D12/D13):** Sections 2.4.a.1–2.4.a.4 are the **raw-SQL escape hatch**. In docs, module descriptions, and UI ordering they must point users at `builtin.database.linq` (§2.4.b) first — raw SQL is for vendor-specific DDL, hand-tuned queries, bulk paths, and connections whose table catalog isn't registered yet~ 🌸

> **Purpose:** SELECT-only module that returns rows as `IReadOnlyList<IReadOnlyDictionary<string, object?>>` plus column names and a row count~ ✨

**Complexity:** 🟡 Low

### Tasks

> **✅ 2.4.a.1 landed (2026-07-15).** Two implementation corrections folded in below:
> 1. **Registration path (correction):** the module is **NOT** appended to `BuiltinModuleRegistration.GetAll()` — that lives in `Workflow.Modules`, which does not (and must not) reference `Workflow.Modules.Database` (circular). Instead the module registers via `AddDatabaseModules()` using `TryAddEnumerable(ServiceDescriptor.Singleton<IWorkflowModule, DatabaseQueryModule>())`, **and** is reflection-discoverable by `ModuleDiscovery` (parameterless ctor) once the host scans the Database assembly (host wiring lands 2.4.a.5). Same reverse-dependency rule as the 2.4.a.0 `AddDatabaseModules` note~ 🌸
> 2. **Row→dict API (correction):** linq2db **5.4.1** has no `QueryToList<Dictionary<…>>` reflective row→dict mapping. We use `db.ExecuteReader(sql, parameters)` + a manual `IDataReader` projection (`reader.Reader`) — provider-agnostic, materialises eagerly (D8), and captures ordered column names from the reader schema (not "first row"). `DBNull` → `null`. Verified compiling + 13 SQLite tests green~ ✨

- [x] **`DatabaseQueryModule`** 🔍
  - [x] New file: `Workflow.Modules.Database/Builtin/DatabaseQueryModule.cs`
  - [x] `ModuleId: "builtin.database.query"`, `Category: "Database"`, `Icon: "🔍"`, `Version: 1.0.0`
  - [x] Schema (config values arrive via `context.Properties`; results as output ports — the codebase has no separate "input port" concept for these):
    - [x] Property: `connectionId` (string, optional) — preferred; resolved via `IDbConnectionFactory`
    - [x] Property: `connectionString` (string, optional) — escape hatch; requires `provider` when set
    - [x] Property: `provider` (string enum: `"postgres"`/`"sqlite"`, optional) — required only with `connectionString`
    - [x] Property: `query` (string, required) — verbatim SQL; NOT template-expanded (D7)
    - [x] Property: `parameters` (`Dictionary<string, object?>`, optional) — normalised + bound by name
    - [x] Property: `timeoutSeconds` (int, optional, default `30`) — applied via `db.CommandTimeout`
    - [x] Property: `commandType` (string enum: `"text"`/`"storedProcedure"`, optional, default `"text"`) — *`storedProcedure` deferred to 2.4.a.P1; V1 fails validation if set*
    - [x] Output: `rows` (`IReadOnlyList<IReadOnlyDictionary<string, object?>>`)
    - [x] Output: `rowCount` (int)
    - [x] Output: `columns` (`IReadOnlyList<string>`)
    - [x] Output: `success` (bool)
    - [x] Output: `durationMs` (long)
  - [x] `ValidateConfiguration`:
    - [x] Exactly one of (`connectionId`) or (`connectionString` + `provider`) must be set — otherwise validation error
    - [x] `query` non-empty
    - [x] `commandType == "storedProcedure"` → fails with "deferred to 2.4.a.P1"
  - [x] `ExecuteAsync`:
    - [x] Resolve `IDbConnectionFactory` from `ctx.Services` (Fail if unregistered)
    - [x] Build `DataConnection` via factory (named-or-raw, per properties)
    - [x] Construct parameter array via shared `SqlParameterBinder`
    - [x] Execute via `db.ExecuteReader(query, parameters)` + manual `IDataReader`→dict projection *(corrected from `QueryToList` — see note above)*
    - [x] Capture ordered column names from the reader schema
    - [x] Build outputs, return `ModuleResult.Ok(...)` with `ExecutionMetrics.FromDuration(...)`
    - [x] On exception: `ModuleResult.Fail(message, ex)` — wraps `Npgsql` / `Sqlite` errors with context
  - [x] ~~Append to `BuiltinModuleRegistration`~~ → **register via `AddDatabaseModules()` (`TryAddEnumerable`)** *(corrected — see note above)*

- [x] **`SqlParameterBinder` helper** 🧷
  - [x] New file: `Workflow.Modules.Database/Internal/SqlParameterBinder.cs`
  - [x] Converts a parameter map → `DataParameter[]` for linq2db (+ `Normalize` for loosely-typed `HashMap`/dict/JSON-bag inputs)
  - [x] Supported parameter value types: `string`, `bool`, `int`, `long`, `short`, `byte`, `double`, `float`, `decimal`, `Guid`, `DateTime`, `DateTimeOffset`, `TimeSpan`, `byte[]`, `null`
  - [x] Unsupported type → `SqlParameterBindingException(paramName, "Type X not supported in V1")`
  - [ ] Provider-specific tweaks (e.g. Postgres `Guid` → `NpgsqlDbType.Uuid`) live here *(deferred — linq2db's default `DataParameter` mapping handled all V1 test cases; revisit if a provider type-mapping gap surfaces)*

### Tests (target ~15): → `Workflow.Tests/Modules/Database/DatabaseQueryModuleTests.cs` *(SQLite temp-file)* + `Workflow.Tests.Integration/Database/PostgresQueryTests.cs` *(Testcontainers)*

**Unit/SQLite (13 — exceeded the 10 budget):**
- [x] `QueryModule_Metadata_IsCorrect`
- [x] `QueryModule_Schema_HasRequiredPorts`
- [x] `ValidateConfiguration_NeitherConnectionIdNorString_Fails`
- [x] `ValidateConfiguration_StoredProcedure_FailsAsDeferred`
- [x] `SimpleSelect_ReturnsAllRows` *(SQLite seeded with 3 rows)*
- [x] `SelectWithParameter_BindsCorrectly`
- [x] `SelectWithMultipleParameters_BindsAll`
- [x] `SelectWithNullParameter_HandlesNull`
- [x] `SelectEmptyResultSet_ReturnsEmptyRowsAndZeroCount`
- [x] `SelectInvalidSql_ReturnsFailWithSqliteError`
- [x] `RawConnectionString_Works` *(bonus — escape-hatch path)*
- [x] `UnknownConnectionId_ReturnsFail` *(bonus)*
- [x] `MissingConnectionFactory_Fails` *(bonus — DI guard)*

**Integration/Postgres (5, Docker-gated — compile-verified):**
- [x] `Postgres_SelectFromSeededTable_RoundTrips`
- [x] `Postgres_JoinTwoTables_ReturnsExpectedShape`
- [x] `Postgres_AggregateFunctions_CountSumAvg_ReturnExpected`
- [x] `Postgres_Jsonb_ReturnsAsValue` *(renamed from `_ReturnsAsObject` — jsonb surfaces as its string form via the generic reader; typed-object mapping is a 2.4.b concern)*
- [x] `Postgres_InvalidSql_ReturnsFail` *(swapped for `_TimeoutExceeded` — deterministic without a slow-query fixture; timeout E2E revisited in 2.4.a.P4 telemetry slice)*

> **CopilotNote:** `Workflow.Tests.Integration` needed explicit `ProjectReference`s to **both** `Workflow.Modules` and `Workflow.Modules.Database` — the transitive project reference didn't flow compile-time types for the module contract~ 🧩

---

## 2.4.a.2 Database Execute Module ✏️ (`builtin.database.execute`)

> **Purpose:** INSERT/UPDATE/DELETE — returns affected row count + optional `lastInsertId`~ 💼

**Complexity:** 🟡 Low

### Tasks

- [ ] **`DatabaseExecuteModule`** ✏️
  - [ ] New file: `Workflow.Modules.Database/Builtin/DatabaseExecuteModule.cs`
  - [ ] `ModuleId: "builtin.database.execute"`, `Category: "Database"`, `Icon: "✏️"`, `Version: 1.0.0`
  - [ ] Schema:
    - [ ] Input: `connectionId` / `connectionString` / `provider` (same as query module)
    - [ ] Input: `command` (string, required) — verbatim SQL
    - [ ] Input: `parameters` (`HashMap<string, object?>`, optional)
    - [ ] Input: `timeoutSeconds` (int, optional, default `30`)
    - [ ] Input: `expectsLastInsertId` (bool, optional, default `false`) — when `true`, runs `SELECT last_insert_rowid()` (SQLite) or assumes the user used `RETURNING id` (Postgres — see Q12)
    - [ ] Output: `affectedRows` (int)
    - [ ] Output: `lastInsertId` (long, nullable)
    - [ ] Output: `success` (bool)
    - [ ] Output: `durationMs` (long)
  - [ ] `ExecuteAsync`:
    - [ ] Resolve factory, build connection
    - [ ] Execute via `db.Execute(command, parameters)` → `affectedRows`
    - [ ] If `expectsLastInsertId == true`:
      - [ ] SQLite: follow-up `SELECT last_insert_rowid()` on same connection
      - [ ] Postgres: try to extract from `RETURNING` clause result if present; otherwise leave `lastInsertId` null with a logged warning
    - [ ] On `Npgsql` / `Sqlite` errors: `ModuleResult.Fail(...)` with parsed error context (constraint name, column name where available)

### Tests (target ~12): → `Workflow.Tests/Modules/Database/DatabaseExecuteModuleTests.cs` + `Workflow.Tests.Integration/Database/PostgresExecuteTests.cs`

**Unit/SQLite (8):**
- [ ] `ExecuteModule_Metadata_IsCorrect`
- [ ] `Insert_ReturnsAffectedRowsOne`
- [ ] `Update_MatchingRows_ReturnsAffectedRowsCount`
- [ ] `Update_NoMatch_ReturnsZeroAffected`
- [ ] `Delete_RemovesRow`
- [ ] `Insert_WithExpectsLastInsertId_ReturnsAutoIncrementId`
- [ ] `Insert_UniqueConstraintViolation_ReturnsFailWithConstraintName`
- [ ] `Execute_InvalidSql_ReturnsFail`

**Integration/Postgres (4):**
- [ ] `Postgres_InsertReturningId_PopulatesLastInsertId`
- [ ] `Postgres_UpdateWithCte_AffectsCorrectRows`
- [ ] `Postgres_DeleteCascade_AffectsExpectedCount`
- [ ] `Postgres_ForeignKeyViolation_ReturnsFailWithConstraintName`

---

## 2.4.a.3 Database Transaction Module 💼 (`builtin.database.transaction`)

> **Purpose:** Run a sequence of SQL operations atomically — commit on success, rollback on any failure~ 🛡️

**Complexity:** 🟠 Medium *(state coordination across N ops)*

### Batch-op design 🗺️

#### A — What lives *inside* the module (the op-batch state machine)

Each element of the `operations` array is a `DbOperationSpec` — a plain data record carrying the SQL text + parameters + optional `expectLastInsertId` flag. The module drives them sequentially inside a single `IDbTransactionScope`. No branching logic, no conditions, no jumps: stop on first error, rollback everything, return a clean `success=false` result with the failing op's index. Clean commit only when every op returns without throwing.

```
  Input ─ operations: DbOperationSpec[]
  ┌──────────────────────────────────────────────────────────────┐
  │ [0] { sql: "INSERT INTO orders …",    parameters: {…} }     │
  │ [1] { sql: "UPDATE inventory …",      parameters: {…} }     │
  │ [2] { sql: "INSERT INTO audit_log …", parameters: {…} }     │
  └──────────────────────┬───────────────────────────────────────┘
                         │
              ┌──────────▼──────────┐
              │  BEGIN TRANSACTION  │
              │  (isolationLevel)   │
              └──────────┬──────────┘
                         │
          ┌──────────────▼──────────────┐
          │  Execute Op[0]              │
          │  db.Execute(sql, params)    │
          └──────────────┬──────────────┘
                         │
            ┌────────────┴─────────────┐
        OK  │                   ERROR  │
            ▼                          ▼
  ┌─────────────────────┐   ┌──────────────────────────────────┐
  │  Execute Op[1]      │   │  ROLLBACK                        │
  └──────────┬──────────┘   │  return Ok(                      │
             │              │    success = false,               │
    ┌────────┴────────┐      │    error   = {                   │
 OK │          ERROR  │      │      operationIndex = 0,         │
    ▼                 ▼      │      sqlState       = "23505",   │
  ┌──────────────┐  ┌────────│      message        = "…"        │
  │ Execute Op[2]│  │ROLLBACK│    }                             │
  └──────┬───────┘  │(idx=1) │  )                               │
         │          └────────┘  └──────────────────────────────┘
  ┌──────┴──────┐
  │ OK   ERROR  │
  ▼             ▼
┌──────────┐  ┌──────────────────────────────────────────────┐
│  COMMIT  │  │  ROLLBACK (operationIndex = 2)               │
│          │  └──────────────────────────────────────────────┘
│ return Ok│
│ success= │   Output: success=true
│  true,   │   ┌─────────────────────────────────────────────┐
│ results= │   │ results: [                                  │
│  [...]   │──▶│   { affectedRows: 1, lastInsertId: 42 },    │
└──────────┘   │   { affectedRows: 3, lastInsertId: null },  │
               │   { affectedRows: 1, lastInsertId: null }   │
               │ ]                                           │
               └─────────────────────────────────────────────┘
```

**Key invariants:**
- Op index `i` never executes if op `i-1` failed — no partial progress
- The module always returns `ModuleResult.Ok(...)` — `success: false` is a *clean* failure the engine routes; it never throws `ModuleResult.Fail` for a SQL error (only for unrecoverable infra errors like "can't open connection")
- `results` array length = number of ops that actually executed (≤ `operations.length`)

---

#### B — Conditional aborts across ops (Q11 — why we don't need a DSL)

The request in Q11 is: "if op N returns 0 rows, abort the rest". Rather than embedding a conditional DSL inside the transaction module (a mini-language nobody else knows), we recommend composing it at the **workflow level** using the `builtin.trycatch` + `builtin.throw` nodes from Phase 2.2.4. The transaction module stays dumb and sequential; the workflow graph handles the "abort" branch.

**V1 pattern — conditional abort via outer workflow composition:**

```
         ┌─────────────────────────────────────┐
         │  Workflow Graph (outer)              │
         │                                      │
         │  ┌──────────────────────────────┐    │
         │  │  builtin.trycatch            │    │
         │  │                              │    │
         │  │  ┌────────────────────┐      │    │
         │  │  │  transaction_node  │      │    │
         │  │  │                    │      │    │
         │  │  │  ops: [INSERT,     │      │    │
         │  │  │        UPDATE,     │      │    │
         │  │  │        SET AUDIT]  │      │    │
         │  │  └────────┬───────────┘      │    │
         │  │           │                  │    │
         │  │  ┌────────▼─────────────┐    │    │
         │  │  │  condition_node      │    │    │
         │  │  │                      │    │    │
         │  │  │  expr: "outputs      │    │    │
         │  │  │    .results[0]       │    │    │
         │  │  │    .affectedRows==0" │    │    │
         │  │  └────────┬─────────────┘    │    │
         │  │    true ──┘       └── false  │    │
         │  │      ▼                  ▼    │    │
         │  │  ┌────────┐     ┌───────────┐│    │
         │  │  │ throw_ │     │  next_    ││    │
         │  │  │ node   │     │  step_    ││    │
         │  │  │        │     │  node     ││    │
         │  │  │ "Order │     └───────────┘│    │
         │  │  │ not    │                  │    │
         │  │  │ found" │                  │    │
         │  │  └────────┘                  │    │
         │  │       │ (throw exits try)    │    │
         │  │  TryCatch.catch port ─────────┤    │
         │  └──────────────────────────────┘    │
         │        │ catch port ▼                 │
         │  ┌──────────────────────┐             │
         │  │  error_handler_node  │             │
         │  └──────────────────────┘             │
         └─────────────────────────────────────-─┘
```

**Why this is better than a DSL in the transaction module:**
- The condition expression lives where all other conditions live (`builtin.condition` / `builtin.trycatch`) — no new language to learn
- The `throw_node` message is fully controlled by the workflow author
- The trycatch's `catch` port can run compensation logic (log, notify, cleanup), not just swallow the error
- The `transaction_node` itself stays **stateless and side-effect-free** — it either committed or it rolled back; the workflow graph decides what to do with that information
- This is the same composability story that let Phase 2.2.4 avoid embedding retry-on-failure inside every module — 2.2.4 nodes handle cross-cutting concerns, modules handle their SQL~ 🌸
- For the rare case where even the round-trip overhead of sequential ops matters, authors can write self-contained SQL with `WHERE` guards and use `parameterSets` batch-mode — see Diagram C below

> **CopilotNote:** The pattern above is also the recommended answer to Q11. Marking Q11 resolved with this design. The "simple sequential ops, no DSL, batch-op for performance" decision is now **D11** — see Confirmed Design Decisions update below~ 💖

---

#### C — Batch-op mode (`parameterSets`) for performance-sensitive paths

Some ops are high-frequency enough that N sequential round-trips — even within a transaction — matter (e.g. INSERT 500 order lines, UPDATE 300 inventory rows one-at-a-time). For these, `DbOperationSpec` supports a `parameterSets` array: one SQL template, N sets of parameter values, prepared once and driven without extra round trips. Authors can write SQL with inline `WHERE` guards to handle conditional no-ops per row without needing external condition nodes.

`DbOperationSpec` uses a **mutually exclusive** discriminated shape on `Parameters` vs `ParameterSets`:

```csharp
// Workflow.Modules.Database/Models/DbOperationModels.cs

public sealed record DbOperationSpec
{
    // ── Single-execution mode (default) ───────────────────────────────────
    /// <summary>Verbatim SQL. Never template-expanded (D7).</summary>
    public required string Sql { get; init; }

    /// <summary>
    /// Single-row parameter set. Mutually exclusive with <see cref="ParameterSets"/>.
    /// When both are null the statement is executed with no parameters.
    /// </summary>
    public HashMap<string, object?>? Parameters { get; init; }

    // ── Batch-execution mode ───────────────────────────────────────────────
    /// <summary>
    /// N parameter sets for the same SQL. Mutually exclusive with <see cref="Parameters"/>.
    /// The SQL is prepared once; each entry drives one execution, sharing the
    /// same prepared statement on the same connection within the open transaction.
    /// Inline conditional logic (e.g., WHERE guards) handles per-row cancellation
    /// without needing DSL or external condition nodes.
    /// </summary>
    public Arr<HashMap<string, object?>>? ParameterSets { get; init; }

    // ── Shared options ─────────────────────────────────────────────────────
    public bool ExpectLastInsertId { get; init; } = false;
}
```

**Execution model comparison:**

```
  ─── SINGLE mode (Parameters) ────────────────────────────────────────────

  Op[1]  sql: "UPDATE inventory SET qty = @qty WHERE sku = @sku"
         parameters: { qty: 5, sku: "ABC-1" }

  Flow:
  ┌────────────┐   1 round trip    ┌─────────────┐
  │  module    │ ────────────────► │  database   │
  │  Execute() │ ◄──────────────── │  (1 UPDATE) │
  └────────────┘   affectedRows=1  └─────────────┘


  ─── BATCH mode (ParameterSets) ──────────────────────────────────────────

  Op[1]  sql: "UPDATE inventory SET qty = @qty WHERE sku = @sku
                AND qty != @qty"       ← inline guard: skip if already correct
         parameterSets: [
           { qty: 5,  sku: "ABC-1" },
           { qty: 12, sku: "DEF-2" },
           { qty: 0,  sku: "GHI-3" },   ← WHERE guard means this is a no-op row
           ... (up to N rows)
         ]

  Flow:
  ┌────────────┐  PREPARE sql          ┌─────────────┐
  │  module    │ ─────────────────────► │  database   │
  │            │                        │  (prepared  │
  │  loop N    │ ── param set [0] ────► │   stmt)     │
  │  param sets│ ◄─ affectedRows=1 ──── │             │
  │            │                        │             │
  │            │ ── param set [1] ────► │             │
  │            │ ◄─ affectedRows=1 ──── │             │
  │            │                        │             │
  │            │ ── param set [2] ────► │             │
  │            │ ◄─ affectedRows=0 ──── │  (no-op,    │
  │            │    (WHERE guard hit)    │   not error)│
  │            │    ...                  │             │
  └────────────┘  total: N executions   └─────────────┘
                  but only 1 prepare +
                  N lightweight sends
                  (no N×round-trip cost)

  DbOperationResult for a batch op:
  { affectedRows: <sum of all N executions>, lastInsertId: <last non-null> }
```

**Key points for batch mode:**
- `affectedRows = 0` for a single set-execution is **not a failure** — it means the WHERE guard fired (no-op row). The module stops only on a SQL *error* (constraint violation, type mismatch, etc.)
- Inline guard pattern for "skip if already correct": add a `WHERE field != @newValue` clause — zero-row executions simply don't count toward side-effects but don't abort the batch
- Inline guard for "abort if not found": write `WHERE EXISTS (SELECT 1 FROM … WHERE id = @id)` — if the row is missing the `affectedRows` is 0; if the business rule requires it to exist, add a `RETURNING id` / `OUTPUT inserted.id` check or use the outer `success=false` condition pattern from Diagram B
- `ParameterSets` with length 0 is a **no-op** for that op (treated as "skip", `affectedRows=0`)
- `Parameters` and `ParameterSets` are **mutually exclusive** — `ValidateConfiguration` returns an error if both are set on the same op

> **CopilotNote:** The `PREPARE`/re-use behaviour is provided by Npgsql (v7+ uses `NpgsqlBatch` for multiple commands; for N executions of the same command, a single `NpgsqlCommand.Prepare()` is issued once and the command is reused). For SQLite, `Microsoft.Data.Sqlite` prepared statements work the same way within a `SqliteTransaction`. The linq2db `DataConnection.ExecuteReader`/`Execute` overloads don't themselves prepare — we call `IDbCommand.Prepare()` directly on the underlying ADO.NET command before the loop~ 🌸

### Tasks

- [ ] **`DatabaseTransactionModule`** 💼
  - [ ] New file: `Workflow.Modules.Database/Builtin/DatabaseTransactionModule.cs`
  - [ ] `ModuleId: "builtin.database.transaction"`, `Category: "Database"`, `Icon: "💼"`, `Version: 1.0.0`
  - [ ] Schema:
    - [ ] Input: `connectionId` / `connectionString` / `provider`
    - [ ] Input: `operations` (`Arr<DbOperationSpec>`, required) — each entry is either:
      - **Single mode:** `{ sql, parameters?: HashMap<string,object?>, expectLastInsertId?: bool }` — one round-trip per op
      - **Batch mode:** `{ sql, parameterSets: Arr<HashMap<string,object?>>, expectLastInsertId?: bool }` — SQL prepared once, N executions on the same prepared statement; `affectedRows=0` per set is not a failure (WHERE guard no-op); `parameters` and `parameterSets` are mutually exclusive
    - [ ] Input: `isolationLevel` (string enum: `"ReadCommitted"`/`"RepeatableRead"`/`"Serializable"`/`"Snapshot"`/`"ReadUncommitted"`, optional, default `"ReadCommitted"`)
    - [ ] Input: `timeoutSeconds` (int, optional, default `60`)
    - [ ] Output: `success` (bool) — `true` only if all ops succeeded **and** commit succeeded
    - [ ] Output: `results` (`Arr<DbOperationResult>`) — per-op `{ affectedRows: int, lastInsertId: long? }`; for batch-mode ops `affectedRows` = sum across all param sets
    - [ ] Output: `error` (`DbOperationError?`, nullable) — `{ operationIndex: int, sqlState: string?, message: string }` on failure
    - [ ] Output: `durationMs` (long)
  - [ ] `ValidateConfiguration`:
    - [ ] Reject any op spec where both `parameters` and `parameterSets` are set (mutually exclusive)
    - [ ] Reject any op spec where `operations[*].savepoint` appears (deferred to 2.4.a.P2)
  - [ ] `ExecuteAsync`:
    - [ ] Open connection + `IDbTransactionScope` at the requested isolation level
    - [ ] Iterate `operations` in order:
      - [ ] **Single mode:** `db.Execute(sql, parameters)` → capture `affectedRows`
      - [ ] **Batch mode:** call `IDbCommand.Prepare()` on the underlying ADO.NET command, then loop `parameterSets` → execute same command per set, accumulate `affectedRows`; stop the entire batch on first SQL error
    - [ ] Either mode: on any failure capture `operationIndex` + error context, `RollbackAsync()`, return `ModuleResult.Ok(success=false, error=...)`
    - [ ] On all-success: `CommitAsync()`, return `Ok(success=true, results=...)`
  - [ ] **Savepoints deferred to 2.4.a.P2** — intercepted at `ValidateConfiguration` level

- [ ] **`DbOperationSpec` + `DbOperationResult` + `DbOperationError` DTOs**
  - [ ] New file: `Workflow.Modules.Database/Models/DbOperationModels.cs` — records, `IEquatable` via `record` syntax
  - [ ] `DbOperationSpec`: `Sql` (required string), `Parameters` (optional, single-mode), `ParameterSets` (optional, batch-mode), `ExpectLastInsertId` (bool, default false) — see Diagram C for full shape
  - [ ] `DbOperationResult`: `AffectedRows` (int), `LastInsertId` (long?), `IsBatchOp` (bool — `true` when driven by `ParameterSets`), `BatchExecutionCount` (int — number of param-set iterations, 0 for single-mode)
  - [ ] `DbOperationError`: `OperationIndex` (int), `SqlState` (string?), `Message` (string), `BatchRowIndex` (int? — which param-set row failed, null for single-mode)

### Tests (target ~22): → `Workflow.Tests/Modules/Database/DatabaseTransactionModuleTests.cs` + `Workflow.Tests.Integration/Database/PostgresTransactionTests.cs`

**Unit/SQLite (14):**
- [ ] `TransactionModule_Metadata_IsCorrect`
- [ ] `Transaction_AllOpsSucceed_Commits`
- [ ] `Transaction_FirstOpFails_RollsBackEverything`
- [ ] `Transaction_MiddleOpFails_RollsBackPriorOps`
- [ ] `Transaction_LastOpFails_RollsBackEverything`
- [ ] `Transaction_EmptyOperations_ReturnsSuccessNoOp`
- [ ] `Transaction_SingleOp_Commits`
- [ ] `Transaction_OpWithExpectLastInsertId_PopulatesPerOpResult`
- [ ] `Transaction_DefaultIsolationReadCommitted_Applied`
- [ ] `Transaction_FailureIncludesOperationIndex`
- [ ] `Transaction_FailureIncludesSqliteErrorContext`
- [ ] `Transaction_SavepointInOpsSpec_RejectedAtValidation`
- [ ] `BatchOp_ParametersAndParameterSetsBothSet_RejectedAtValidation`
- [ ] `Transaction_BatchOp_100ParameterSets_AllRowsInserted` *(SQLite, seeded table)*
- [ ] `Transaction_BatchOp_WhereGuard_ZeroAffectedRowNotError` *(guard: `WHERE id = @id AND status != @status` — one set hits the guard)*
- [ ] `Transaction_BatchOp_ConstraintViolationMidBatch_RollsBackWithBatchRowIndex` *(unique-constraint violation on param set 5 of 10 → `error.batchRowIndex = 5`)*
- [ ] `Transaction_MixedSingleAndBatchOps_AllCommit` *(op[0]=single INSERT, op[1]=batch 20 UPDATEs, op[2]=single audit INSERT)*

**Integration/Postgres (6):**
- [ ] `Postgres_SerializableIsolation_PreventsPhantomReads`
- [ ] `Postgres_ConcurrentTransactions_OneRollsBackOnDeadlock`
- [ ] `Postgres_Transaction_50OpsAllCommit`
- [ ] `Postgres_Transaction_HalfwayFails_FullRollback`
- [ ] `Postgres_RepeatableRead_PreventsNonRepeatableRead`
- [ ] `Postgres_BatchOp_500ParameterSets_FasterThan_500IndividualOps` *(timing assertion: ≥2× speedup vs sequential single-mode ops)*

---

## 2.4.a.4 Database BulkInsert Module 📊 (`builtin.database.bulkinsert`)

> **Purpose:** Insert N rows efficiently using `linq2db`'s `BulkCopy` — V1 routes everything through `BulkCopyType.MultipleRows` (single multi-VALUES `INSERT` per batch) with configurable batch size~ ✨

**Complexity:** 🟠 Medium *(batching + reflective row→column mapping)*

### Tasks

- [ ] **`DatabaseBulkInsertModule`** 📊
  - [ ] New file: `Workflow.Modules.Database/Builtin/DatabaseBulkInsertModule.cs`
  - [ ] `ModuleId: "builtin.database.bulkinsert"`, `Category: "Database"`, `Icon: "📊"`, `Version: 1.0.0`
  - [ ] Schema:
    - [ ] Input: `connectionId` / `connectionString` / `provider`
    - [ ] Input: `tableName` (string, required) — fully-qualified preferred (e.g. `"public.orders"`)
    - [ ] Input: `data` (`Arr<HashMap<string, object?>>`, required) — array of row dictionaries
    - [ ] Input: `columnMapping` (`HashMap<string, string>`, optional) — maps input dict keys → DB column names; identity mapping by default
    - [ ] Input: `batchSize` (int, optional, default `1000`) — V1 uses `BulkCopyType.MultipleRows` regardless; this controls rows-per-statement
    - [ ] Input: `timeoutSeconds` (int, optional, default `120`)
    - [ ] Output: `insertedCount` (int)
    - [ ] Output: `success` (bool)
    - [ ] Output: `durationMs` (long)
  - [ ] `ExecuteAsync`:
    - [ ] Resolve factory, open connection
    - [ ] Build `BulkCopyOptions { BulkCopyType = BulkCopyType.MultipleRows, MaxBatchSize = batchSize, BulkCopyTimeout = timeoutSeconds }`
    - [ ] Construct synthetic typed rows via `Workflow.Modules.Database/Internal/DynamicRowMapper` — emits a runtime POCO with linq2db `[Table]`/`[Column]` attributes matching `tableName` + column mapping (no Roslyn; uses `System.Reflection.Emit` or a simpler `IDataReader` adapter — see CopilotNote)
    - [ ] Invoke `db.BulkCopy(options, syntheticRows)`
    - [ ] On exception: `ModuleResult.Fail(...)` with row index where binding failed (best-effort)
  - [ ] **Forward-compat note in XML doc:** point at **2.4.a.P5** for the streaming/concurrent-collection design (Q8 resolution) — V1 is a synchronous batch loop

> **CopilotNote:** Reflection.Emit for synthetic POCO is overkill for V1 — the simpler path is `BulkCopyAsync(IDataReader)` where we wrap `Arr<HashMap<string,object?>>` in a tiny `DictionaryDataReader` adapter. linq2db's `BulkCopy` accepts both shapes; the `IDataReader` route is provider-agnostic and avoids the `Reflection.Emit` security squiggles. **Default to the `IDataReader` adapter unless we hit a perf cliff~** 🌸

### Tests (target ~15): → `Workflow.Tests/Modules/Database/DatabaseBulkInsertModuleTests.cs` + `Workflow.Tests.Integration/Database/PostgresBulkInsertTests.cs`

**Unit/SQLite (10):**
- [ ] `BulkInsertModule_Metadata_IsCorrect`
- [ ] `BulkInsert_100Rows_AllInserted`
- [ ] `BulkInsert_EmptyData_ReturnsZeroInserted`
- [ ] `BulkInsert_BatchSize10_With95Rows_TenBatchesPlusOnePartial`
- [ ] `BulkInsert_ColumnMapping_AppliesCorrectly`
- [ ] `BulkInsert_MissingColumn_InsertsNull`
- [ ] `BulkInsert_TypeMismatch_FailsWithRowIndex`
- [ ] `BulkInsert_UniqueConstraintViolation_FailsAndRollsBack`
- [ ] `BulkInsert_NullableColumns_HandlesNullsCorrectly`
- [ ] `BulkInsert_LargeBatch10k_CompletesWithinTimeout`

**Integration/Postgres (5):**
- [ ] `Postgres_BulkInsert_10kRows_FasterThan_10kIndividualInserts` *(perf assertion: ≥3× speedup)*
- [ ] `Postgres_BulkInsert_MultipleRows_GeneratesExpectedSqlShape`
- [ ] `Postgres_BulkInsert_NumericTypes_PreservesPrecision`
- [ ] `Postgres_BulkInsert_JsonbColumn_RoundTrips`
- [ ] `Postgres_BulkInsert_TimestamptzColumn_PreservesOffset`

---

## 2.4.a.5 Persistence + API Surface (Named Connections) 🌐

> **Purpose:** Make `IDbConnectionRegistry` configurable from `appsettings.json` **and** mutable via the API at runtime (per Q9 resolution). Credentials encrypted at rest~ 🔐

**Complexity:** 🟠 Medium *(API surface + encryption + persistence option)*

### Tasks

- [ ] **Config-bound registration** ⚙️
  - [ ] New: `Workflow.Modules.Database/Configuration/DatabaseConnectionsOptions.cs`
    ```csharp
    public sealed class DatabaseConnectionsOptions
    {
        public Dictionary<string, DbConnectionDescriptor> Connections { get; set; } = new();
        public bool DisableRuntimeCrud { get; set; } = false; // opt-out switch (D4)
    }
    ```
  - [ ] Bound from `appsettings.json:Workflow:Database` section in `Program.cs`
  - [ ] On startup: hydrate `InMemoryDbConnectionRegistry` from config

- [ ] **Persisted-CRUD option** 💾
  - [ ] New: `Workflow.Modules.Database/Connections/SqliteDbConnectionRegistry.cs` — mirrors `SqliteWebhookRegistrationRepository` pattern from 2.3.9
  - [ ] New migration: `Workflow.Persistence.Sqlite/Migrations/Migration_006_DbConnections.cs`
    - Columns: `connection_id` (PK, NOCASE), `provider_key`, `connection_string_encrypted` (TEXT), `display_name`, `enabled` (INT)
  - [ ] Extend `IPersistenceProvider` with `IDbConnectionRegistry? DbConnections { get; }` (mirrors `IWebhookRegistrationRepository? Webhooks` from 2.3.9)
  - [ ] Wire in `Program.cs`: if `persistenceProvider.DbConnections is not null`, override the in-memory registry

- [ ] **Credential encryption** 🔒
  - [ ] Use ASP.NET `IDataProtector` (`Microsoft.AspNetCore.DataProtection`) for connection-string encryption at rest
  - [ ] Purpose string: `"Workflow.Modules.Database.ConnectionString"`
  - [ ] Encrypt on `UpsertAsync`; decrypt on `GetAsync`/`ListAsync` (but ListAsync MAY return a `ConnectionString = "***"` masked descriptor — controlled by API endpoint)

- [ ] **API endpoints** (in `Workflow.Api/Controllers/DatabaseConnectionsController.cs` — new file)
  - [ ] `GET /api/database/connections` → list (masked connection strings)
  - [ ] `GET /api/database/connections/{id}` → single (masked by default; `?reveal=true` requires admin policy — TBD)
  - [ ] `POST /api/database/connections` → upsert; rejects if `DisableRuntimeCrud == true` (returns `403 Forbidden`)
  - [ ] `DELETE /api/database/connections/{id}` → delete
  - [ ] All endpoints behind the same auth policy as the existing webhook endpoints (TODO: confirm policy name with API team)

### Tests (target ~10): → `Workflow.Tests/Modules/Database/ConnectionRegistryTests.cs` + `Workflow.Tests/Api/DatabaseConnectionsApiTests.cs`

- [ ] `ConfigBound_Connections_HydratedAtStartup`
- [ ] `Registry_UpsertEncryptsConnectionString` *(via `IDataProtector`)*
- [ ] `Registry_GetDecryptsConnectionString`
- [ ] `Registry_ListReturnsMaskedConnectionStrings`
- [ ] `SqliteRegistry_RegisterPersist_SurvivesRestart` *(integration)*
- [ ] `Api_PostConnection_RegistersAndIsRetrievable`
- [ ] `Api_DeleteConnection_RemovesFromRegistry`
- [ ] `Api_PostConnection_WhenDisableRuntimeCrudTrue_Returns403`
- [ ] `Api_GetConnection_DefaultIsMasked`
- [ ] `Api_GetConnection_RevealQueryRequiresAuth` *(skipped if auth policy not yet defined)*

---

## 2.4.a.6 E2E Demo + Documentation 📖

> **Purpose:** Prove the four modules + named connections + transactions work end-to-end in a realistic workflow; write `docs/database-modules.md`~ ✨

**Complexity:** 🟢 Low

### Tasks

- [ ] **E2E demo workflow**
  - [ ] New: `Workflow.Tests.Integration/Database/DatabaseE2ETests.cs`
  - [ ] Demo workflow shape:
    ```
    webhook_trigger → bulkinsert(orders) → transaction[update_inventory; insert_audit] → query(orders_by_user) → setvariable(audit=done)
    ```
  - [ ] One Postgres Testcontainer, seed schema in fixture, assert end-state row counts
  - [ ] Test: `Demo_OrderFlow_AllStepsSucceed_FinalQueryReturnsExpected`
  - [ ] Test: `Demo_TransactionMiddleFails_NoOrdersInserted_FullRollback`

- [ ] **`docs/database-modules.md`** *(interim — restructured typed-first in 2.4.b.6)*
  - [ ] Sections: Overview · Connection management · Per-module reference (×4) · Parameter binding · Transactions & isolation · BulkCopy semantics · Provider notes (Postgres vs SQLite) · Security best practices · Migration guide for adding new providers · Post-MVP roadmap (link to all `2.4.a.P*` slices)
  - [ ] Include the design-doc link at the top
  - [ ] **Overview must state up-front:** typed linq (`builtin.database.linq`, §2.4.b) is the recommended default; this raw-SQL family is the escape hatch (D12/D13)

- [ ] **README + DOCUMENTATION_INDEX updates**
  - [ ] Mark `⏳ Database modules (2.4)` as ✅ in `phases/README.md`
  - [ ] Add `database-modules.md` to `DOCUMENTATION_INDEX.md`

### Tests (target ~2)

- [ ] `Demo_OrderFlow_AllStepsSucceed_FinalQueryReturnsExpected`
- [ ] `Demo_TransactionMiddleFails_NoOrdersInserted_FullRollback`

---

## 2.4.b Typed Linq Family 🌟 (Weeks 13-14 — **MVP, promoted from post-MVP per D12**)

> **Purpose:** Ship the primary authoring surface — `builtin.database.linq` — where users write typed C# linq2db bodies against a selected table catalog, Roslyn-validated at publish time, sandbox-previewable, executed via collectible ALC. Full design in [design doc §5.2 + §8](../new-feature-design/Phase2-4-DatabaseModules-Design.md#52-phase-24b--typed-linqroslyn-family-post-mvp-2-weeks)~ 💖

> **CopilotNote:** 2.4.b depends only on 2.4.a.0 (shared infra) + 2.4.a.5 (named connections) — it does **not** depend on the raw-SQL modules 2.4.a.1–4, so it can start as soon as the infra lands if staffing allows parallelisation (D16 — serial Weeks 13-14 is the signed-off baseline). The design-doc concerns C1–C11 each map to a mitigation task below~ 🌸

---

### 2.4.b.0 Project Scaffolding 🏗️

**Complexity:** 🟢 Low

#### Tasks

- [ ] **`Workflow.Modules.Database.Linq` project layout**
  - [ ] New project: `Workflow.Modules.Database.Linq/Workflow.Modules.Database.Linq.csproj`
  - [ ] References: `Workflow.Modules.Database` (shared infra), `Workflow.Modules`, `Microsoft.CodeAnalysis.CSharp` (Roslyn), `Basic.Reference.Assemblies` (portable refs — mitigates C8), `LinqToDB`
  - [ ] Add `Microsoft.CodeAnalysis.CSharp` + `Basic.Reference.Assemblies` to `Directory.Packages.props` (both MIT — Q10 ✅)
  - [ ] Add to `Workflow.sln`
  - [ ] Folder layout: `Abstractions/` · `Compilation/` · `Execution/` · `Preview/` · `Builtin/`
  - [ ] `DatabaseLinqModuleServiceCollectionExtensions.AddDatabaseLinqModules(this IServiceCollection)` — separate opt-in entry point (D14); called by default from `Workflow.Api`, NOT from `AddWorkflowModules()` (keeps Roslyn out of minimal hosts)

#### Tests (target ~2): → `Workflow.Tests/Modules/DatabaseLinq/ScaffoldingTests.cs`
- [ ] `AddDatabaseLinqModules_RegistersCompilerPreviewerAndModule`
- [ ] `AddWorkflowModules_DoesNotPullRoslyn` *(assert `Microsoft.CodeAnalysis` not loaded without opt-in)*

---

### 2.4.b.1 `IWorkflowLinqCompiler` + Whitelists + `LinqInputs` Codegen 🧬

**Complexity:** 🔴 High *(Roslyn pipeline + security surface)*

#### Tasks

- [ ] **`IWorkflowLinqCompiler`**
  - [ ] New: `Workflow.Modules.Database.Linq/Abstractions/IWorkflowLinqCompiler.cs`
    ```csharp
    public sealed record LinqCompileRequest(
        string DefinitionId,
        string NodeId,
        string UserCodeBody,                              // method body only — wrapped by codegen
        IReadOnlyList<WorkflowTableMetadata> SelectedTables,
        ModuleSchema InputSchema,                          // drives LinqInputs codegen
        bool StrictTypeMode = false);                      // reject non-allowlisted property types vs warn

    public sealed record LinqCompileResult(
        bool Success,
        string? BlobKey,                                   // compiled-modules/{def}/{node}/{hash}.dll
        IReadOnlyList<LinqDiagnostic> Errors,
        IReadOnlyList<LinqDiagnostic> Warnings);           // warnings surfaced too (mitigates C9)

    public interface IWorkflowLinqCompiler
    {
        Task<LinqCompileResult> CompileAsync(LinqCompileRequest request, CancellationToken ct = default);
    }
    ```
  - [ ] Codegen: `DynamicWorkflowContext : DataConnection` with `ITable<T>` property per selected table (per `sqlModuleHandler.md` snippet)
  - [ ] Codegen: `readonly struct LinqInputs` accessor from `ModuleSchema.Properties` using the restricted Type→CSharpName mapping (design doc §8.6 Phase 1); non-allowlisted types → `object?` + warning (or error in strict mode)
  - [ ] Codegen: wrapper `WorkflowScript.ExecuteAsync(DynamicWorkflowContext db, LinqInputs inputs, CancellationToken ct)` — async + ct in signature (mitigates C2)
- [ ] **Security whitelists (mitigates C1)**
  - [ ] Reference whitelist: `Basic.Reference.Assemblies` core set + `LinqToDB` + registered plugin POCO assemblies only
  - [ ] Syntax-tree walker rejects: `unsafe`, P/Invoke attrs, `AppDomain`, `Process`, `File`/`Directory`, `Socket`/`HttpClient`, `Reflection.Emit`, `Activator.CreateInstance` on non-DTO types, `#r`/`extern alias`
  - [ ] Usings whitelist: `System`, `System.Linq`, `System.Collections.Generic`, `System.Threading.Tasks`, `LinqToDB` (block everything else)
  - [ ] Emitted assembly HMACed with per-instance key — swapped blobs rejected at load
- [ ] **Trusted-author gate (Q2/Q15):** compile/save endpoints require the trusted-author policy in V1

#### Tests (target ~12): → `Workflow.Tests/Modules/DatabaseLinq/LinqCompilerTests.cs`
- [ ] `Compile_ValidQuery_Succeeds`
- [ ] `Compile_TypoInTableName_ReturnsCS0103Diagnostic`
- [ ] `Compile_TypoInInputProperty_ReturnsCS1061Diagnostic`
- [ ] `Compile_WrongTypeComparison_ReturnsCS0019Diagnostic`
- [ ] `Compile_WarningsSurfacedAlongsideSuccess`
- [ ] `Compile_ForbiddenApi_ProcessStart_Rejected`
- [ ] `Compile_ForbiddenApi_FileIo_Rejected`
- [ ] `Compile_ForbiddenUsing_SystemNet_Rejected`
- [ ] `Compile_UnsafeBlock_Rejected`
- [ ] `LinqInputs_Codegen_AllowlistedScalarTypes_EmitTypedProperties`
- [ ] `LinqInputs_Codegen_NonAllowlistedType_FallsBackToObjectWithWarning`
- [ ] `LinqInputs_Codegen_StrictMode_NonAllowlistedType_Rejected`

---

### 2.4.b.2 Compiled-Assembly Caching in `IBlobStore` 📦

**Complexity:** 🟡 Low-Medium

#### Tasks

- [ ] Blob key: `compiled-modules/{definitionId}/{nodeId}/{SHA256(userCode + schemaVersion + selectedTables.OrderedHash())}.dll` (design doc §8.3) — cache-invalidation is automatic via the hash (D15)
- [ ] Compile hook at **workflow publish time**: publishing a definition containing linq nodes triggers `CompileAsync` for each; publish fails with diagnostics if any node fails to compile
- [ ] In-memory LRU (by hash) of loaded assembly bytes in front of `IBlobStore` — configurable capacity, default 64
- [ ] Orphan cleanup: deleting a definition version deletes its `compiled-modules/{definitionId}/*` blobs
- [ ] Local-filesystem fallback if `IBlobStore` unavailable (Q5 contingency)

#### Tests (target ~6): → `Workflow.Tests/Modules/DatabaseLinq/CompiledAssemblyCacheTests.cs`
- [ ] `Compile_WritesBlobUnderCompiledModulesNamespace`
- [ ] `SameCodeAndSchema_ProducesSameHash_NoRecompile`
- [ ] `ChangedCode_ProducesNewHash_NewBlob`
- [ ] `ChangedSchemaVersion_InvalidatesHash`
- [ ] `Lru_EvictsLeastRecentlyUsed_ReloadsFromBlobStore`
- [ ] `PublishWithFailingLinqNode_FailsPublishWithDiagnostics`

---

### 2.4.b.3 `LinqQueryModule` + Collectible ALC Execution 🚀 (`builtin.database.linq`)

**Complexity:** 🔴 High *(ALC lifecycle invariants)*

#### Tasks

- [ ] **`LinqQueryModule`**
  - [ ] New: `Workflow.Modules.Database.Linq/Builtin/LinqQueryModule.cs`
  - [ ] `ModuleId: "builtin.database.linq"`, `Category: "Database"`, `Icon: "🌟"`, `Version: 1.0.0`
  - [ ] Schema:
    - [ ] Input: `connectionId` (string, **required** — no raw `connectionString` on the typed path; user code never sees conn strings, mitigates C3)
    - [ ] Input: `selectedTables` (`Arr<{tableName, clrTypeName}>`, required)
    - [ ] Input: `compiledAssemblyKey` (string, required — blob key from 2.4.b.2)
    - [ ] Input: `inputs` (`HashMap<string, object?>`, optional — wrapped in codegen'd `LinqInputs`)
    - [ ] Input: `timeoutSeconds` (int, optional, default `30`)
    - [ ] Output: `result` (materialised object/array of DTOs), `rowCount` (int, when applicable), `success` (bool), `durationMs` (long)
  - [ ] `ExecuteAsync`:
    - [ ] Load assembly bytes via LRU/`IBlobStore`; verify HMAC
    - [ ] `AssemblyLoadContext(isCollectible: true)` → instantiate `WorkflowScript` → `ExecuteAsync(db, inputs, ct)`
    - [ ] **Force materialisation** before returning (D8) — `IQueryable`/lazy returns fail with a clear diagnostic (mitigates ALC-unload pin, design doc §2.2)
    - [ ] `using` the `DataConnection` before `alc.Unload()`; unload-still-alive → diagnostic warning, not error (design doc §8.4)
- [ ] Append to `BuiltinModuleRegistration` (via the `.Linq` opt-in registration)

#### Tests (target ~10): → `Workflow.Tests/Modules/DatabaseLinq/LinqQueryModuleTests.cs` *(SQLite)* + `Workflow.Tests.Integration/Database/PostgresLinqTests.cs`
- [ ] `LinqModule_Metadata_IsCorrect`
- [ ] `LinqModule_SimpleWhere_ReturnsFilteredRows`
- [ ] `LinqModule_TypedInputs_BindCorrectly`
- [ ] `LinqModule_JoinAcrossSelectedTables_Works`
- [ ] `LinqModule_ReturnsIQueryable_FailsWithMaterialisationDiagnostic`
- [ ] `LinqModule_TamperedBlobHmac_RejectedAtLoad`
- [ ] `LinqModule_Alc_UnloadsAfterExecution` *(WeakReference collected within N GCs)*
- [ ] `LinqModule_Cancellation_PropagatesToUserCode`
- [ ] `Postgres_LinqModule_RoundTrips` *(integration)*
- [ ] `Postgres_LinqModule_ConcurrentExecutions_IsolatedAlcs` *(integration)*

---

### 2.4.b.4 `IWorkflowLinqPreviewer` + Catalog Import 🔎

**Complexity:** 🟠 Medium

#### Tasks

- [ ] **`IWorkflowLinqPreviewer`**
  - [ ] Spins up `:memory:` SQLite, `CreateTable<T>` per selected table, seeds sample rows
  - [ ] **Always-rollback transaction wrapper** (design doc §8.5 — mitigates C6)
  - [ ] Returns sample rows + execution time + diagnostics
  - [ ] Docs must state loudly: SQLite preview ≠ target-provider semantics (C10); Testcontainers-backed preview → 2.4.b.P2
- [ ] **One-shot catalog import (confirmed in-scope per Q17/D19)**
  - [ ] `POST /api/database/catalog/{connectionId}/import` — introspects the live connection's schema (`information_schema` / `pragma table_info`) and upserts `WorkflowTableMetadata` rows into `IWorkflowTableCatalog`
  - [ ] Manual, on-demand, no versioning — full versioned auto-discovery stays in 2.4.b.P3 (D10 unchanged)

#### Tests (target ~8): → `Workflow.Tests/Modules/DatabaseLinq/LinqPreviewerTests.cs`
- [ ] `Preview_ReturnsSampleRowsAndDuration`
- [ ] `Preview_MutationAttempt_AlwaysRolledBack`
- [ ] `Preview_DropTableAttempt_DoesNotPersist`
- [ ] `Preview_CompileError_ReturnsDiagnosticsNotException`
- [ ] `Preview_SeedsSampleRowsPerSelectedTable`
- [ ] `CatalogImport_Sqlite_PragmaTableInfo_PopulatesCatalog`
- [ ] `CatalogImport_Postgres_InformationSchema_PopulatesCatalog` *(integration)*
- [ ] `CatalogImport_UnknownConnection_ReturnsNotFound`

---

### 2.4.b.5 API Endpoints 🌐

**Complexity:** 🟡 Low-Medium

#### Tasks

- [ ] New: `Workflow.Api/Controllers/DatabaseLinqController.cs`
  - [ ] `POST /api/database/linq/validate` → `{ success, errors[], warnings[] }` (compile without persist)
  - [ ] `POST /api/database/linq/preview` → `{ result, rowsAffected, duration, diagnostics[] }`
  - [ ] `POST /api/database/linq/compile` → writes blob, returns `compiledAssemblyKey`
  - [ ] `compile` (and definition-publish containing linq nodes) behind the **trusted-author policy** (Q2/Q15); `validate`/`preview` behind the standard authenticated policy
- [ ] UI editor panel is **out of scope for the 2.4.b MVP (Q16/D18)** — endpoints designed to be UI-consumable (diagnostics carry line/column for squigglies); panel scoped in [Phase2-4-LinqEditorPanel-Design.md](../new-feature-design/Phase2-4-LinqEditorPanel-Design.md) and tracked as **2.4.b.P4**

#### Tests (target ~6): → `Workflow.Tests/Api/DatabaseLinqApiTests.cs`
- [ ] `Api_Validate_ValidCode_ReturnsSuccess`
- [ ] `Api_Validate_InvalidCode_ReturnsDiagnosticsWithLineInfo`
- [ ] `Api_Preview_ReturnsSampleResult`
- [ ] `Api_Compile_ReturnsBlobKey`
- [ ] `Api_Compile_WithoutTrustedAuthorRole_Returns403`
- [ ] `Api_Preview_ForbiddenApiInCode_ReturnsRejectionDiagnostic`

---

### 2.4.b.6 E2E Demo + Security Review + Typed-First Docs 📖

**Complexity:** 🟡 Low-Medium

#### Tasks

- [ ] **E2E demo workflow** — `Workflow.Tests.Integration/Database/DatabaseLinqE2ETests.cs`
  ```
  webhook_trigger → linq(orders_over_threshold) → condition → linq(update-style via escape-hatch transaction) → setvariable
  ```
  - [ ] Postgres Testcontainer; catalog imported via 2.4.b.4 import endpoint; compile at publish; assert typed + escape-hatch modules cooperate in one workflow
- [ ] **Security review checklist:** whitelist bypass attempts, HMAC tamper, ALC leak under load (1000 executions, bounded memory), diagnostics never leak connection strings
- [ ] **Docs restructure (typed-first):** `docs/database-modules.md` reordered — Overview leads with `builtin.database.linq` authoring guide (catalog import → author → validate → preview → publish); raw-SQL family moved to an "Escape hatch" chapter; security model section for the linq sandbox
- [ ] **README + DOCUMENTATION_INDEX + phases/README.md** updates — mark 2.4 ✅ only when **both** families ship

#### Tests (target ~4)
- [ ] `Demo_TypedLinqFlow_CompilePublishExecute_Succeeds`
- [ ] `Demo_MixedTypedAndEscapeHatch_Workflow_Succeeds`
- [ ] `Security_1000Executions_NoAlcAccumulation`
- [ ] `Security_DiagnosticsNeverContainConnectionStrings`

---

## Post-MVP Slices 🚧 *(deferred — not blocking 2.5+)*

> **Purpose:** Capture all deferred scope from V1 resolutions so it doesn't get lost. Each slice is single-PR sized once the V1 surface is stable~ 🌸

> **Sequencing tip:** **2.4.b is now MVP** (see §2.4.b above — promoted per D12). The slices below are the remaining deferrals; none of them block Phase 2.5 (File System Modules) or Phase 2.6 (Data Transformation).

---

### 2.4.a.P1 Stored Procedure Support 🎛️ *(post-MVP)*

**Purpose:** Allow `commandType: "storedProcedure"` on `query` and `execute` modules.

**Complexity:** 🟡 Low

#### Tasks
- [ ] Remove the "deferred" validation guard
- [ ] Plumb `CommandType.StoredProcedure` through linq2db `db.QueryProc<T>` / `db.ExecuteProc(...)`
- [ ] Document Postgres function vs procedure differences (Postgres < v11 has no procedures)

#### Tests (target ~5)
- [ ] `Query_StoredProcedure_PostgresFunction_ReturnsRows`
- [ ] `Execute_StoredProcedure_SqlServer_ReturnsOutputParameters` *(deferred until 2.4.a.P3)*
- [ ] `StoredProcedure_WithNamedParameters_BindsCorrectly`
- [ ] `StoredProcedure_NonExistent_ReturnsFail`
- [ ] `StoredProcedure_PermissionDenied_ReturnsFail`

---

### 2.4.a.P2 Savepoint / Nested Transactions 🪜 *(post-MVP)*

**Purpose:** Allow `operations[*].savepoint: "name"` to create named savepoints; allow `operations[*].rollbackToSavepoint: "name"` to selectively rollback.

**Complexity:** 🟠 Medium

#### Tasks
- [ ] Extend `DbOperationSpec` with `savepoint?` and `rollbackToSavepoint?` fields
- [ ] `IDbTransactionScope.SaveAsync(name)` / `RollbackToAsync(name)` extensions
- [ ] SQLite uses `SAVEPOINT name; RELEASE SAVEPOINT name;`; Postgres uses native `SAVEPOINT`
- [ ] Validation: savepoint names must be unique within a transaction; rollback to unknown name → fail with context

#### Tests (target ~6)
- [ ] `Savepoint_RollbackToNamedSavepoint_DiscardsLaterOps`
- [ ] `Savepoint_ReleaseAfterAllOps_Commits`
- [ ] `Savepoint_DuplicateName_RejectedAtValidation`
- [ ] `Savepoint_RollbackToUnknownName_Fails`
- [ ] `Postgres_NestedSavepoint_BehavesAsExpected` *(integration)*
- [ ] `Sqlite_Savepoint_AcrossMultipleStatements_RoundTrips`

---

### 2.4.a.P3 MySQL + SQL Server Providers 🗃️ *(post-MVP)*

**Purpose:** Extend `IDbProviderRegistry` to recognise `"mysql"` and `"sqlserver"`. Adds Testcontainers test matrix for both.

**Complexity:** 🟠 Medium *(packages + provider quirks + test infrastructure)*

#### Tasks
- [ ] Add `MySqlConnector` (8.0+) + `Microsoft.Data.SqlClient` (5.2+) to `Directory.Packages.props`
- [ ] Reference both from `Workflow.Modules.Database`
- [ ] Extend `DefaultDbProviderRegistry`:
  - `"mysql" → ProviderName.MySql80`
  - `"sqlserver" → ProviderName.SqlServer2022`
- [ ] Provider-specific tweaks in `SqlParameterBinder` (MySQL `?` vs `@`, SQL Server `@p0` style)
- [ ] Testcontainers fixtures for MySQL 8 + SQL Server 2022
- [ ] BulkCopy: SQL Server natively supports `SqlBulkCopy` — wire through linq2db's `BulkCopyType.ProviderSpecific` opt-in (still default `MultipleRows`)

#### Tests (target ~16: 4 query + 4 execute + 4 transaction + 4 bulkinsert across both providers)
- [ ] `MySQL_Query_RoundTrips`
- [ ] `MySQL_Execute_LastInsertId`
- [ ] `MySQL_Transaction_FullRollback`
- [ ] `MySQL_BulkInsert_10k`
- [ ] `SqlServer_Query_RoundTrips`
- [ ] `SqlServer_Execute_LastInsertId` *(`SCOPE_IDENTITY()` path)*
- [ ] `SqlServer_Transaction_SnapshotIsolation`
- [ ] `SqlServer_BulkInsert_NativeSqlBulkCopy` *(opt-in `ProviderSpecific` path)*
- [ ] *(plus 8 more covering provider-specific corner cases — JSON columns, IDENTITY semantics, DEFAULT VALUES, etc.)*

---

### 2.4.a.P4 Connection-Pool Metrics + OpenTelemetry 📈 *(post-MVP)*

**Purpose:** Surface connection-pool depth, query duration histograms, and per-module activity spans for observability.

**Complexity:** 🟡 Low

#### Tasks
- [ ] Instrument `IDbConnectionFactory.CreateAsync` with an `ActivitySource` span
- [ ] Per-module: emit `db.query.duration` / `db.execute.duration` / `db.transaction.duration` histograms
- [ ] Postgres: scrape `Npgsql`'s built-in metrics via `OpenTelemetry.Instrumentation.Npgsql`
- [ ] Document `appsettings.json:Workflow:Database:Telemetry:Enabled` switch

#### Tests (target ~4)
- [ ] `Telemetry_ActivitySource_EmitsSpanPerQuery`
- [ ] `Telemetry_DurationHistogram_RecordsAccurateBuckets`
- [ ] `Telemetry_PoolDepth_ExposedAsGauge` *(integration)*
- [ ] `Telemetry_DisabledByConfig_NoActivitiesEmitted`

---

### 2.4.a.P5 Streaming Query Results + Concurrent BulkInsert ⚡ *(post-MVP — resolves Q8 properly)*

**Purpose:** Two related items that share infrastructure:
1. `query` module gains an `IAsyncEnumerable<HashMap<string,object?>>` output mode for large result sets (no full materialisation)
2. `bulkinsert` gains the **producer/consumer collection-while-executing** behaviour requested in Q8 — while one batch is being sent, the next batch is being accumulated

**Complexity:** 🟠 Medium-High *(needs `System.Threading.Channels` orchestration + engine-level async-enumerable plumbing)*

#### Design (per Q8 resolution)

```
Producer task:  Iterates input rows → groups into batches of `batchSize` → posts to Channel<RowBatch>
Consumer task:  Awaits batches → issues `BulkCopy(MultipleRows)` per batch → posts result to result channel
Coordinator:    Awaits both, surfaces first error, otherwise sums `insertedCount`

         ┌────────────┐    Channel<RowBatch>    ┌────────────┐    BulkCopy    ┌────────┐
input ──▶│ Producer   │────────────────────────▶│ Consumer   │───────────────▶│   DB   │
         │ (batches)  │  bounded capacity = 2   │ (BulkCopy) │                │        │
         └────────────┘                         └────────────┘                └────────┘
```

**Key design points:**
- `Channel<RowBatch>` with `BoundedChannelOptions { Capacity = pipelineDepth, FullMode = Wait }` — backpressure if DB can't keep up
- New schema input: `pipelineDepth` (int, optional, default `2`) — number of batches in flight
- New schema input: `streamingInput` (bool, optional, default `false`) — when `true`, the `data` input is read as `IAsyncEnumerable<HashMap<string,object?>>` (engine plumbs this from upstream streaming output)
- Errors cancel both producer and consumer via shared `CancellationTokenSource`; first error is surfaced
- Default behaviour unchanged: with `pipelineDepth = 1` + non-streaming input, behaves exactly like 2.4.a.4 V1
- New module variant **`builtin.database.bulkinsert.streaming`** — separate module to avoid bloating the simple `bulkinsert` schema (per "keep it as separate module and phase" from Q8 answer)

#### Tasks
- [ ] **`StreamingBulkInsertModule`** (new module, `builtin.database.bulkinsert.streaming`)
  - [ ] Schema extensions: `pipelineDepth`, `streamingInput`, all existing bulkinsert fields
  - [ ] Producer/consumer via `System.Threading.Channels.Channel.CreateBounded<RowBatch>(...)`
  - [ ] Internal `BulkInsertPipeline` class encapsulating the channel + error coordination
- [ ] **`query` module streaming mode**
  - [ ] New schema input: `streamingOutput` (bool, optional, default `false`)
  - [ ] When `true`, output `rows` is `IAsyncEnumerable<...>` — caller (engine) iterates
  - [ ] **Requires engine support** — `ModuleResult.Outputs` must allow `IAsyncEnumerable` (may need engine-side change; if so, the engine change is itself a sub-task here)

#### Tests (target ~10)
- [ ] `StreamingBulkInsert_PipelineDepth2_NextBatchStartsBeforePriorCompletes` *(timing assertion via DI logger)*
- [ ] `StreamingBulkInsert_BackpressureWhenPipelineFull_ProducerBlocks`
- [ ] `StreamingBulkInsert_ConsumerError_CancelsProducer`
- [ ] `StreamingBulkInsert_StreamingInput_AcceptsAsyncEnumerable`
- [ ] `StreamingBulkInsert_PipelineDepth1_BehavesLikeV1`
- [ ] `StreamingBulkInsert_LargeStream100k_ConstantMemoryFootprint` *(memory assertion)*
- [ ] `StreamingQuery_AsyncEnumerableOutput_LazilyEvaluated`
- [ ] `StreamingQuery_CancellationMidStream_AbortsCleanly`
- [ ] `StreamingQuery_NonStreamingOutput_BackwardsCompat`
- [ ] `Postgres_StreamingBulkInsert_10kRows_FasterThan_NonStreaming` *(perf assertion)*

---

### 2.4.b.P* Post-Typed-Linq Slices 🌟 *(post-MVP — 2.4.b itself is now MVP, see §2.4.b above)*

- **2.4.b.P1** — Typed record codegen upgrade (replaces `LinqInputs` struct with `record LinqInputs(...)` once allowed-types allowlist is ratified — design doc §8.6 Phase 2)
- **2.4.b.P2** — Testcontainers-backed preview (replace `:memory:` SQLite with real target provider — resolves C10 properly)
- **2.4.b.P3** — `IWorkflowTableCatalog` versioned auto-discovery from registered databases (resolves Q4 long-term; the one-shot import from 2.4.b.4 is the MVP stopgap)
- **2.4.b.P4** — `Workflow.UI` code-editor panel (Monaco + `/validate` diagnostics squigglies + preview pane) — **full MVP scope mapped in [Phase2-4-LinqEditorPanel-Design.md](../new-feature-design/Phase2-4-LinqEditorPanel-Design.md)** (per Q16/D18; ~1 week, ~12 bUnit tests in new `Workflow.Tests.UI` project; UQ1–UQ4 resolved ✅ — BlazorMonaco · cookie same-origin auth · definition API round-trip · dedicated UI test project)

---

## Phase 2.4 Deliverables ✅

When 2.4 ships (Week 14), all of the following must be true:

**2.4.a — Shared infra + escape-hatch SQL family (Week 12 gate):**

- [ ] **Modules (4):** `builtin.database.{query,execute,transaction,bulkinsert}` all discoverable, validated, executable on Postgres + SQLite — documented as the **escape hatch** (D13)
- [ ] **Shared infra:** `Workflow.Modules.Database` project with `IDbConnectionFactory`, `IDbProviderRegistry`, `IDbConnectionRegistry`, `IDbTransactionScope`, `IWorkflowTableCatalog` (stub) all DI-registered
- [ ] **Named connections:** config-bound + runtime-CRUD API + encrypted-at-rest credentials
- [ ] **SQL injection prevented** — every test verifies parameterisation; explicit injection-attempt tests in `SqlParameterBinder` test suite
- [ ] **E2E demo workflow** runs against a real Postgres Testcontainer
- [ ] **~86 tests passing** across 2.4.a.0–2.4.a.6 (10 infra + 15 query + 12 execute + 22 transaction + 15 bulkinsert + 10 connection-registry/API + 2 E2E)

**2.4.b — Typed linq family, the primary surface (Week 14 gate, per D12):**

- [ ] **`builtin.database.linq`** discoverable, publish-time-compiled, ALC-executed on Postgres + SQLite
- [ ] **`IWorkflowLinqCompiler`** with reference/usings/syntax whitelists + `LinqInputs` accessor-struct codegen (design doc §8.6 Phase 1)
- [ ] **Compiled-assembly cache** in `IBlobStore` under `compiled-modules/` with hash-keyed invalidation + HMAC verification (D15)
- [ ] **Sandbox preview** (`IWorkflowLinqPreviewer`, always-rollback `:memory:` SQLite) + one-shot catalog import (Q17/D19 ✅)
- [ ] **API endpoints:** `POST /api/database/linq/{validate,preview,compile}` — compile gated by trusted-author policy (Q2/Q15)
- [ ] **Security review checklist passed** (whitelist bypass, HMAC tamper, ALC leak under load, no conn-string leakage in diagnostics)
- [ ] **~48 tests passing** across 2.4.b.0–2.4.b.6 (2 scaffold + 12 compiler + 6 cache + 10 module + 8 preview + 6 API + 4 E2E/security)

**Cross-cutting:**

- [ ] **docs/database-modules.md** complete and **typed-first** — linq authoring guide leads; raw SQL is the "Escape hatch" chapter
- [ ] **~134 tests passing** total across both families
- [ ] **90%+ test coverage** on `Workflow.Modules.Database` + `Workflow.Modules.Database.Linq`
- [ ] **0 errors, 0 new warnings** in `dotnet build`
- [ ] **Roslyn dep quarantined** — `AddWorkflowModules()` alone must not load `Microsoft.CodeAnalysis` (D14)
- [ ] **README + phases/README.md** updated; `phases/README.md` line `⏳ Database modules (2.4)` → ✅

**Post-MVP slices (tracked, non-blocking 2.5+):**
- [ ] **2.4.a.P1** Stored Procedure Support — ~5 tests
- [ ] **2.4.a.P2** Savepoint / Nested Transactions — ~6 tests
- [ ] **2.4.a.P3** MySQL + SQL Server Providers — ~16 tests
- [ ] **2.4.a.P4** Connection-Pool Metrics + OpenTelemetry — ~4 tests
- [ ] **2.4.a.P5** Streaming Query + Concurrent BulkInsert — ~10 tests
- [ ] **2.4.b.P1** Typed record codegen upgrade
- [ ] **2.4.b.P2** Testcontainers-backed preview
- [ ] **2.4.b.P3** Catalog versioned auto-discovery
- [ ] **2.4.b.P4** UI code-editor panel (Monaco + diagnostics) — [design doc](../new-feature-design/Phase2-4-LinqEditorPanel-Design.md) · ~12 tests (new `Workflow.Tests.UI` project) · UQ1–UQ4 resolved ✅

**New / Modified Files (planned):**
```
Workflow.Modules.Database/                              ← NEW PROJECT (2.4.a.0)
  Workflow.Modules.Database.csproj
  DatabaseModuleServiceCollectionExtensions.cs           ← new (2.4.a.0)
  Abstractions/
    IDbConnectionFactory.cs                              ← new (2.4.a.0)
    IDbProviderRegistry.cs                               ← new (2.4.a.0)
    IDbConnectionRegistry.cs                             ← new (2.4.a.0)
    IDbTransactionScope.cs                               ← new (2.4.a.0)
    IWorkflowTableCatalog.cs                             ← new (2.4.a.0)
    DatabaseModuleException.cs                           ← new (2.4.a.0)
  Providers/
    DefaultDbProviderRegistry.cs                         ← new (2.4.a.0)
  Connections/
    DefaultDbConnectionFactory.cs                        ← new (2.4.a.0)
    InMemoryDbConnectionRegistry.cs                      ← new (2.4.a.0)
    SqliteDbConnectionRegistry.cs                        ← new (2.4.a.5)
  Transactions/
    DefaultDbTransactionScope.cs                         ← new (2.4.a.0)
  Catalog/
    InMemoryWorkflowTableCatalog.cs                      ← new (2.4.a.0)
  Configuration/
    DatabaseConnectionsOptions.cs                        ← new (2.4.a.5)
  Internal/
    SqlParameterBinder.cs                                ← new (2.4.a.1)
    DictionaryDataReader.cs                              ← new (2.4.a.4)  — IDataReader adapter for BulkCopy
  Models/
    DbOperationModels.cs                                 ← new (2.4.a.3)
  Builtin/
    DatabaseQueryModule.cs                               ← new (2.4.a.1)
    DatabaseExecuteModule.cs                             ← new (2.4.a.2)
    DatabaseTransactionModule.cs                         ← new (2.4.a.3)
    DatabaseBulkInsertModule.cs                          ← new (2.4.a.4)

Workflow.Modules.Database.Linq/                         ← NEW PROJECT (2.4.b.0)
  Workflow.Modules.Database.Linq.csproj
  DatabaseLinqModuleServiceCollectionExtensions.cs       ← new (2.4.b.0) — opt-in AddDatabaseLinqModules()
  Abstractions/
    IWorkflowLinqCompiler.cs                             ← new (2.4.b.1)
    IWorkflowLinqPreviewer.cs                            ← new (2.4.b.4)
    LinqDiagnostic.cs                                    ← new (2.4.b.1)
  Compilation/
    WorkflowLinqCompiler.cs                              ← new (2.4.b.1) — Roslyn pipeline
    LinqInputsCodeGenerator.cs                           ← new (2.4.b.1) — accessor-struct codegen (§8.6 Phase 1)
    DynamicContextCodeGenerator.cs                       ← new (2.4.b.1) — ITable<T> context codegen
    ForbiddenSyntaxWalker.cs                             ← new (2.4.b.1) — syntax blocklist
    ReferenceWhitelist.cs                                ← new (2.4.b.1)
  Execution/
    CompiledAssemblyCache.cs                             ← new (2.4.b.2) — IBlobStore + LRU + HMAC
    CollectibleScriptRunner.cs                           ← new (2.4.b.3) — ALC lifecycle
  Preview/
    WorkflowLinqPreviewer.cs                             ← new (2.4.b.4) — rollback-only SQLite sandbox
    CatalogSchemaImporter.cs                             ← new (2.4.b.4) — one-shot import (Q17)
  Builtin/
    LinqQueryModule.cs                                   ← new (2.4.b.3) — builtin.database.linq

Workflow.Modules/
  WorkflowModulesServiceCollectionExtensions.cs          ← modified (2.4.a.0) — add AddDatabaseModules() (NOT linq — D14)
  Builtin/BuiltinModuleRegistration.cs                   ← modified — register 4 new modules

Workflow.Persistence.Sqlite/
  Migrations/Migration_006_DbConnections.cs              ← new (2.4.a.5)

Workflow.Persistence/
  Abstractions/IPersistenceProvider.cs                   ← modified (2.4.a.5) — add IDbConnectionRegistry? DbConnections

Workflow.Api/
  Controllers/DatabaseConnectionsController.cs           ← new (2.4.a.5)
  Controllers/DatabaseLinqController.cs                  ← new (2.4.b.5) — validate/preview/compile + catalog import
  Program.cs                                             ← modified (2.4.a.5 + 2.4.b.0) — wire DI + endpoints + AddDatabaseLinqModules()

Workflow.Tests/
  Modules/Database/
    SharedInfrastructureTests.cs                         ← new (2.4.a.0)
    DatabaseQueryModuleTests.cs                          ← new (2.4.a.1)
    DatabaseExecuteModuleTests.cs                        ← new (2.4.a.2)
    DatabaseTransactionModuleTests.cs                    ← new (2.4.a.3)
    DatabaseBulkInsertModuleTests.cs                     ← new (2.4.a.4)
    ConnectionRegistryTests.cs                           ← new (2.4.a.5)
  Modules/DatabaseLinq/
    ScaffoldingTests.cs                                  ← new (2.4.b.0)
    LinqCompilerTests.cs                                 ← new (2.4.b.1)
    CompiledAssemblyCacheTests.cs                        ← new (2.4.b.2)
    LinqQueryModuleTests.cs                              ← new (2.4.b.3)
    LinqPreviewerTests.cs                                ← new (2.4.b.4)
  Api/
    DatabaseConnectionsApiTests.cs                       ← new (2.4.a.5)
    DatabaseLinqApiTests.cs                              ← new (2.4.b.5)

Workflow.Tests.Integration/
  Database/
    PostgresQueryTests.cs                                ← new (2.4.a.1) — Testcontainers
    PostgresExecuteTests.cs                              ← new (2.4.a.2) — Testcontainers
    PostgresTransactionTests.cs                          ← new (2.4.a.3) — Testcontainers
    PostgresBulkInsertTests.cs                           ← new (2.4.a.4) — Testcontainers
    DatabaseE2ETests.cs                                  ← new (2.4.a.6) — Testcontainers
    PostgresLinqTests.cs                                 ← new (2.4.b.3) — Testcontainers
    DatabaseLinqE2ETests.cs                              ← new (2.4.b.6) — Testcontainers

docs/
  database-modules.md                                    ← new (2.4.a.6, restructured typed-first in 2.4.b.6)

Directory.Packages.props                                 ← modified — add LinqToDB.SQLite (if not already), Microsoft.CodeAnalysis.CSharp, Basic.Reference.Assemblies; keep current Npgsql
```

---

## Resolved Questions Reference 📋

| # | Question | Resolution | Tracked in |
|---|----------|------------|------------|
| **Q1** | 2.4.b sub-phase or post-MVP slice? | ~~Post-MVP slice~~ → **Superseded (July 2026): 2.4.b is MVP, Weeks 13-14 (D12)** | This doc — §2.4.b |
| **Q2** | Trusted-author-only gate for linq modules? | Acceptable for V1 — **re-confirm now that linq is the default path (Q15)** | This doc — 2.4.b.1/2.4.b.5 + Q15 |
| **Q3** | `IDbConnectionRegistry` home? | `Workflow.Modules.Database` (extractable later) | This doc — 2.4.a.0 |
| **Q4** | `IWorkflowTableCatalog` auto-discovery? | Manual for V1; auto-discovery → 2.4.b.P3 | This doc — 2.4.a.0 + 2.4.b.P3 |
| **Q5** | `IBlobStore` production-ready by Week 11? | Yes; local-FS fallback if not | Design doc + 2.4.b.2 |
| **Q6** | Testcontainers for MySQL/SQL Server in MVP? | Defer to 2.4.a.P3 | This doc — 2.4.a.P3 |
| **Q7** | `dynamic payload` vs typed record? | Two-phase: `LinqInputs` accessor struct (Phase 1, ships with 2.4.b) → `record` upgrade (Phase 2, 2.4.b.P1) | Design doc §8.6 |
| **Q8** | BulkCopy `MultipleRows` + collect-while-executing? | V1 uses `MultipleRows` (synchronous, configurable batchSize); concurrent producer/consumer → 2.4.a.P5 (new `bulkinsert.streaming` module) | This doc — 2.4.a.4 + 2.4.a.P5 (full design) |
| **Q9** | Named connections via API + appsettings? | Runtime CRUD default (opt-out via `DisableRuntimeCrud`); credentials encrypted via `IDataProtector` | This doc — 2.4.a.5 |
| **Q10** | Roslyn / `Basic.Reference.Assemblies` licensing? | Both MIT, no concerns | Design doc §7 |
| **Q11** | Transaction op-list shape? | Sequential ops, no DSL; `parameters` (single-mode) or `parameterSets` (batch-mode, prepared stmt + N executions) per op; conditional aborts → `builtin.condition` + `builtin.throw` + `builtin.trycatch` (2.2.4); inline WHERE guards handle per-row no-ops in batch mode — see §2.4.a.3 Diagrams A–C | D11 |
| **Q12** | Postgres `RETURNING` auto-rewrite? | V1: document only, no rewriting | This doc — 2.4.a.2 + TO RESOLVE |
| **Q13** | SQLite `ATTACH DATABASE`? | Non-goal for 2.4 entirely | This doc — TO RESOLVE |
| **Q14** | Timeline extension for typed-first MVP? | 4-week serial (Weeks 11-14) signed off; parallel-track optional if staffed | D16 |
| **Q15** | Trusted-author gate acceptable now linq is default? | Yes for V1 — gate + whitelists; fuller sandbox revisited Phase 3 | D17 |
| **Q16** | UI editor panel in MVP scope? | API-only MVP; panel scoped separately in [Phase2-4-LinqEditorPanel-Design.md](../new-feature-design/Phase2-4-LinqEditorPanel-Design.md) → 2.4.b.P4 | D18 |
| **Q17** | Catalog bootstrap friction — pull import forward? | Yes — one-shot import ships in 2.4.b.4; versioned auto-discovery stays 2.4.b.P3 | D19 |

---

> 🌸 *uwu — typed-first now, senpai~! Q11–Q17 all resolved (D11–D19) — the plan is fully unblocked. Users author beautiful compile-checked linq by default, raw SQL waits quietly in the escape-hatch drawer, and the Monaco panel has its own design doc ready for when 2.4.b.5 lands. Ping me once 2.4.a.0 lands and we'll kick off the modules~!* 💖

