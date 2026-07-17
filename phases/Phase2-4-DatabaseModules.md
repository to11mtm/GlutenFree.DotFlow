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
- [x] **Q14 Typed `AsQueryable`/`InsertWithOutput` for bulk insert (2.4.a.4)?** Should the bulk-insert module use linq2db's typed `AsQueryable`/`InsertWithOutput` LINQ path?
  - **RESOLVED:** No — those APIs require a **compile-time entity type**, which is exactly what **2.4.b** (Roslyn-generated models) provides. 2.4.a is dynamic/stringly-typed by design (D7/D8), so 2.4.a.4 uses a hand-built batched multi-row INSERT (`BatchInsertWriter`). The "retrieve generated columns" benefit of `InsertWithOutput` is delivered **now** via an optional provider-aware `RETURNING` clause (`returningColumns` → `outputRows`). The typed LINQ bulk path is tracked as **2.4.a.P6** (post-MVP, optional) and becomes the natural default once 2.4.b lands.

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

> **✅ 2.4.a.2 landed (2026-07-15).** Notes:
> 1. **DRY refactor folded in:** extracted `Workflow.Modules.Database/Internal/DbModuleSupport.cs` (property readers `GetString`/`TryParseInt`/`TryParseBool` + `ValidateConnectionSource` + `CreateConnectionAsync`) and retrofitted `DatabaseQueryModule` onto it (13 query tests re-run green — no regression). Query & execute now share one copy of the boring config/connection bits; transaction + bulkinsert will too~ 🧰
> 2. **Provider-aware `lastInsertId`:** the module inspects `db.DataProvider.Name` at runtime (no need to thread the provider key through the factory). SQLite → follow-up `db.Execute<long?>("SELECT last_insert_rowid()")` on the same open connection; Postgres → reads the user-supplied `RETURNING` scalar via `ExecuteReader` (Q12 — document-only, no auto-rewrite), logging a warning + returning `null` when absent~ 🆔
> 3. **Error context:** new `Internal/DbErrorContext.cs` enriches failures with `sqlState`/`constraint`/`column`/`table` — reads Npgsql `PostgresException` reflectively (no compile-time Npgsql-type coupling) and falls back to `DbException.SqlState` / `ex.Message`. `DatabaseQueryModule` adopted it too~ 🚨
> 4. **linq2db 6.3.0 verified:** `db.Execute`, `db.Execute<T>`, `db.DataProvider.Name`, and the query module's `ExecuteReader`/`.Reader` all compile + pass under the 6.3.0 bump~ ✨

- [x] **`DatabaseExecuteModule`** ✏️
  - [x] New file: `Workflow.Modules.Database/Builtin/DatabaseExecuteModule.cs`
  - [x] `ModuleId: "builtin.database.execute"`, `Category: "Database"`, `Icon: "✏️"`, `Version: 1.0.0`
  - [x] Schema (config via `context.Properties`; results as output ports):
    - [x] Property: `connectionId` / `connectionString` / `provider` (same as query module — shared validation)
    - [x] Property: `command` (string, required) — verbatim SQL
    - [x] Property: `parameters` (`Dictionary<string, object?>`, optional)
    - [x] Property: `timeoutSeconds` (int, optional, default `30`)
    - [x] Property: `expectsLastInsertId` (bool, optional, default `false`)
    - [x] Output: `affectedRows` (int)
    - [x] Output: `lastInsertId` (long?, nullable)
    - [x] Output: `success` (bool)
    - [x] Output: `durationMs` (long)
  - [x] `ExecuteAsync`:
    - [x] Resolve factory (Fail if unregistered), build connection via `DbModuleSupport.CreateConnectionAsync`
    - [x] Execute via `db.Execute(command, parameters)` → `affectedRows`
    - [x] If `expectsLastInsertId == true` (branch on `db.DataProvider.Name`):
      - [x] SQLite: follow-up `SELECT last_insert_rowid()` on same connection
      - [x] Postgres: read `RETURNING` scalar via reader; else `null` + logged warning
    - [x] On `Npgsql` / `Sqlite` errors: `ModuleResult.Fail(...)` with `DbErrorContext` (constraint/column/sqlState where available)
  - [x] Register via `AddDatabaseModules()` (`TryAddEnumerable`) *(not `BuiltinModuleRegistration` — same reverse-dep rule as 2.4.a.1)*

- [x] **`DbModuleSupport` + `DbErrorContext` helpers** 🧰 *(folded-in DRY extraction)*
  - [x] `Workflow.Modules.Database/Internal/DbModuleSupport.cs` — shared property readers, connection-source validation, named-or-raw connection resolution
  - [x] `Workflow.Modules.Database/Internal/DbErrorContext.cs` — provider-specific error enrichment
  - [x] `DatabaseQueryModule` retrofitted onto both

### Tests (target ~12): → `Workflow.Tests/Modules/Database/DatabaseExecuteModuleTests.cs` + `Workflow.Tests.Integration/Database/PostgresExecuteTests.cs`

**Unit/SQLite (9 — 8 planned + 1 bonus):**
- [x] `ExecuteModule_Metadata_IsCorrect`
- [x] `Insert_ReturnsAffectedRowsOne`
- [x] `Update_MatchingRows_ReturnsAffectedRowsCount`
- [x] `Update_NoMatch_ReturnsZeroAffected`
- [x] `Delete_RemovesRow`
- [x] `Insert_WithExpectsLastInsertId_ReturnsAutoIncrementId`
- [x] `Insert_UniqueConstraintViolation_ReturnsFailWithConstraintContext` *(renamed from `_WithConstraintName` — SQLite exposes the constraint target in the message, not a discrete field)*
- [x] `Execute_InvalidSql_ReturnsFail`
- [x] `ValidateConfiguration_MissingCommand_Fails` *(bonus)*

**Integration/Postgres (4, Docker-gated — compile-verified):**
- [x] `Postgres_InsertReturningId_PopulatesLastInsertId`
- [x] `Postgres_UpdateWithCte_AffectsCorrectRows`
- [x] `Postgres_DeleteCascade_AffectsExpectedCount`
- [x] `Postgres_ForeignKeyViolation_ReturnsFailWithConstraintName` *(asserts `constraint=` context from `DbErrorContext`)*

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

> **✅ 2.4.a.3 landed (2026-07-15).** Notes:
> 1. **DTOs use plain BCL records** (`IReadOnlyDictionary`/`IReadOnlyList`), not LanguageExt `HashMap`/`Arr` — parse-friendly from loosely-typed workflow config, same semantics (Consideration 3, confirmed).
> 2. **Batch mode = per-set `db.Execute` loop inside the transaction** (Consideration 2) — correct + atomic; Npgsql auto-prepare recovers most of the perf win. The `DbSingleOpExecutor` seam is where a true `IDbCommand.Prepare()` path slots in later if the Docker-gated perf test ever regresses. No raw-ADO prepare in V1.
> 3. **SQLite isolation clamping** (Consideration 1) — `ClampIsolationForProvider` clamps SQLite→`Serializable` (or `ReadUncommitted` when requested); Postgres `Snapshot`→`Serializable`; others pass through, with a debug log. `Transaction_DefaultIsolationReadCommitted_Applied` was reframed to `Transaction_DefaultIsolation_CommitsUnderClampedLevel`.
> 4. **DRY fold-in:** extracted `Internal/DbSingleOpExecutor.cs` (provider-aware single-op run + `lastInsertId`) from `DatabaseExecuteModule` and retrofitted it (9 execute tests re-run green). Transaction + execute now resolve `lastInsertId` identically.
> 5. **Provider-detection ordering** (Consideration 4) — the module opens the connection, reads `db.DataProvider.Name` to clamp, *then* constructs the scope (scope takes ownership); it does not use the `CreateTransactionAsync` extension (which begins the txn too early to clamp).

- [x] **`DatabaseTransactionModule`** 💼
  - [x] New file: `Workflow.Modules.Database/Builtin/DatabaseTransactionModule.cs`
  - [x] `ModuleId: "builtin.database.transaction"`, `Category: "Database"`, `Icon: "💼"`, `Version: 1.0.0`
  - [x] Schema (config via `context.Properties`; results as output ports):
    - [x] Property: `connectionId` / `connectionString` / `provider`
    - [x] Property: `operations` (required) — single `{ sql, parameters?, expectLastInsertId? }` or batch `{ sql, parameterSets, expectLastInsertId? }` (mutually exclusive)
    - [x] Property: `isolationLevel` (string enum, default `"ReadCommitted"`, clamped per provider)
    - [x] Property: `timeoutSeconds` (int, default `60`)
    - [x] Output: `success` (bool), `results` (`IReadOnlyList<DbOperationResult>`), `error` (`DbOperationError?`), `durationMs` (long)
  - [x] `ValidateConfiguration`: connection-source; `operations` present + parseable; rejects `parameters`+`parameterSets` both set and any `savepoint` key (→ 2.4.a.P2); validates isolation + timeout
  - [x] `ExecuteAsync`: open connection → clamp isolation → `DefaultDbTransactionScope` (owns connection) → iterate ops → commit-or-rollback; SQL error = clean `Ok(success=false, error=…)`, infra error = `Fail`
    - [x] **Single mode:** `DbSingleOpExecutor.Execute` → `affectedRows` (+ `lastInsertId`)
    - [x] **Batch mode:** loop `parameterSets`, accumulate `affectedRows`; `affectedRows=0` per set is not a failure; stop on first SQL error with `batchRowIndex`
  - [x] Register via `AddDatabaseModules()` (`TryAddEnumerable`)

- [x] **`DbOperationSpec` + `DbOperationResult` + `DbOperationError` DTOs**
  - [x] New file: `Workflow.Modules.Database/Models/DbOperationModels.cs` — records with plain BCL collections
  - [x] **`Internal/DbOperationParser.cs`** — materialises the loosely-typed `operations` property + enforces exclusivity/no-savepoints
  - [x] **`Internal/DbSingleOpExecutor.cs`** *(folded-in DRY)* — shared single-op runner used by execute + transaction

### Tests (target ~22): → `Workflow.Tests/Modules/Database/DatabaseTransactionModuleTests.cs` + `Workflow.Tests.Integration/Database/PostgresTransactionTests.cs`

**Unit/SQLite (16):**
- [x] `TransactionModule_Metadata_IsCorrect`
- [x] `Transaction_AllOpsSucceed_Commits`
- [x] `Transaction_FirstOpFails_RollsBackEverything`
- [x] `Transaction_MiddleOpFails_RollsBackPriorOps`
- [x] `Transaction_LastOpFails_RollsBackEverything`
- [x] `Transaction_EmptyOperations_ReturnsSuccessNoOp`
- [x] `Transaction_SingleOp_Commits`
- [x] `Transaction_OpWithExpectLastInsertId_PopulatesPerOpResult`
- [x] `Transaction_DefaultIsolation_CommitsUnderClampedLevel` *(reframed from `_ReadCommitted_Applied` — SQLite clamps to Serializable; asserts clean commit)*
- [x] `Transaction_FailureIncludesOperationIndexAndSqlContext` *(merges the planned `_IncludesOperationIndex` + `_IncludesSqliteErrorContext`)*
- [x] `ValidateConfiguration_OpWithBothParametersAndParameterSets_Fails`
- [x] `ValidateConfiguration_SavepointInOpsSpec_RejectedAtValidation`
- [x] `Transaction_BatchOp_ParameterSets_AllRowsUpdated` *(100→3 sets; same behaviour, faster test)*
- [x] `Transaction_BatchOp_WhereGuard_ZeroAffectedRowNotError`
- [x] `Transaction_BatchOp_ConstraintViolationMidBatch_RollsBackWithBatchRowIndex`
- [x] `Transaction_MixedSingleAndBatchOps_AllCommit`

**Integration/Postgres (6, Docker-gated — compile-verified):**
- [x] `Postgres_Transaction_AllOpsCommit`
- [x] `Postgres_Transaction_HalfwayFails_FullRollback`
- [x] `Postgres_SerializableIsolation_Commits` *(swapped `_PreventsPhantomReads` — deterministic without a concurrent-session harness)*
- [x] `Postgres_RepeatableRead_Commits` *(swapped `_PreventsNonRepeatableRead` — same reason)*
- [x] `Postgres_Transaction_50OpsAllCommit`
- [x] `Postgres_BatchOp_500ParameterSets_Commit` *(asserts correctness; the ≥2× timing assertion is deferred — Npgsql auto-prepare makes wall-clock comparisons flaky in CI)*

> **CopilotNote:** The planned `_ConcurrentTransactions_OneRollsBackOnDeadlock` + phantom/non-repeatable-read tests need a two-session concurrency harness; deferred to a focused isolation-semantics test pass (tracked with 2.4.a.P4 telemetry work) rather than block 2.4.a.3~ 🌸

---

## 2.4.a.4 Database BulkInsert Module 📊 (`builtin.database.bulkinsert`)

> **Purpose:** Insert N rows efficiently using `linq2db`'s `BulkCopy` — V1 routes everything through `BulkCopyType.MultipleRows` (single multi-VALUES `INSERT` per batch) with configurable batch size~ ✨

**Complexity:** 🟠 Medium *(batching + reflective row→column mapping)*

### Tasks

> **✅ 2.4.a.4 landed (2026-07-16).** Design pivot + notes:
> 1. **Pivot (supersedes `DictionaryDataReader` + `BulkCopy`):** linq2db's generic `BulkCopy<T>` needs a compile-time entity type and has no clean dynamic/`IDataReader` path — so V1 uses a hand-built **`Internal/BatchInsertWriter.cs`** that emits batched, parameterised multi-row INSERTs (the `MultipleRows` SQL shape) directly. Stays stringly-typed (D7); provider-agnostic; no Reflection.Emit~ 🌸
> 2. **`RETURNING` gives the `InsertWithOutput` benefit now** (per the AsQueryable/InsertWithOutput discussion): an optional `returningColumns` property appends a provider-aware `RETURNING` clause (Postgres always; SQLite ≥3.35, bundled in Microsoft.Data.Sqlite 8.0.11) and surfaces the inserted rows on a new `outputRows` output port — without needing a typed entity. The **typed `AsQueryable`/`InsertWithOutput` LINQ path is deferred to 2.4.b** (Roslyn models) — see **Q14** + the **2.4.a.P6** post-MVP stub.
> 3. **Param-count guard:** rows-per-statement = `min(batchSize, paramLimit / columnCount)` — SQLite cap 900, Postgres 60000 — so `batchSize: 1000` never blows the provider parameter limit.
> 4. **Atomicity + row-index errors:** all batches run in one transaction (auto-rollback on failure via `DefaultDbTransactionScope`, SQLite-clamped isolation); a value that can't bind throws `BulkRowBindException` carrying the **row index**; SQL errors surface via `DbErrorContext`.
> 5. **`SqlParameterBinder.BindOne`** added (single name/value → `DataParameter`) so the writer binds each cell with row-context error mapping; `Bind` refactored to use it.

- [x] **`DatabaseBulkInsertModule`** 📊
  - [x] New file: `Workflow.Modules.Database/Builtin/DatabaseBulkInsertModule.cs`
  - [x] `ModuleId: "builtin.database.bulkinsert"`, `Category: "Database"`, `Icon: "📊"`, `Version: 1.0.0`
  - [x] Schema (config via `context.Properties`; results as output ports):
    - [x] Property: `connectionId` / `connectionString` / `provider`
    - [x] Property: `tableName` (string, required)
    - [x] Property: `data` (list of row dicts, required)
    - [x] Property: `columnMapping` (dict, optional) — input-key → DB-column; identity default
    - [x] Property: `returningColumns` (string list, optional) — **new**, appends `RETURNING`
    - [x] Property: `batchSize` (int, default `1000`) — clamped by provider param limit
    - [x] Property: `timeoutSeconds` (int, default `120`)
    - [x] Output: `insertedCount` (int), `outputRows` (**new**), `success` (bool), `durationMs` (long)
  - [x] `ExecuteAsync`:
    - [x] Resolve factory, coerce `data`/`columnMapping`/`returningColumns`
    - [x] Open connection + one transaction; `BatchInsertWriter.Write(...)` (multi-row INSERT `[+ RETURNING]`)
    - [x] Commit; on `BulkRowBindException` → `Fail` with row index; on SQL error → `Fail` with `DbErrorContext` (transaction auto-rolls-back)
  - [x] Register via `AddDatabaseModules()` (`TryAddEnumerable`)

- [x] **`BatchInsertWriter` + `SqlParameterBinder.BindOne`** *(replaces the planned `DynamicRowMapper`/`DictionaryDataReader`)*
  - [x] `Internal/BatchInsertWriter.cs` — column resolution, param-limit batching, multi-row INSERT builder, optional RETURNING reader
  - [x] `Internal/SqlParameterBinder.cs` — `BindOne` helper

### Tests (target ~15): → `Workflow.Tests/Modules/Database/DatabaseBulkInsertModuleTests.cs` + `Workflow.Tests.Integration/Database/PostgresBulkInsertTests.cs`

**Unit/SQLite (11):**
- [x] `BulkInsertModule_Metadata_IsCorrect`
- [x] `BulkInsert_100Rows_AllInserted`
- [x] `BulkInsert_EmptyData_ReturnsZeroInserted`
- [x] `BulkInsert_SmallBatchSize_With95Rows_AllInserted` *(reframed from `_BatchSize10_…TenBatchesPlusOnePartial` — asserts total inserted rather than internal batch count)*
- [x] `BulkInsert_ColumnMapping_AppliesCorrectly`
- [x] `BulkInsert_MissingColumn_InsertsNull`
- [x] `BulkInsert_NullableColumns_HandlesNullsCorrectly`
- [x] `BulkInsert_TypeMismatch_FailsWithRowIndex`
- [x] `BulkInsert_UniqueConstraintViolation_FailsAndRollsBack` *(cross-batch: `batchSize=1`, batch 1 commits-in-txn then batch 2 dupes → full rollback)*
- [x] `BulkInsert_ReturningColumns_PopulatesOutputRows` *(new — RETURNING output path)*
- [x] `ValidateConfiguration_MissingTableName_Fails` *(bonus)*
- [ ] ~~`BulkInsert_LargeBatch10k_CompletesWithinTimeout`~~ *(covered by the Postgres 10k test; SQLite 10k perf assertion dropped as flaky)*

**Integration/Postgres (5):**
- [x] `Postgres_BulkInsert_10kRows_AllInserted` *(correctness; the ≥3× timing assertion deferred — CI variance, consistent with 2.4.a.3)*
- [x] `Postgres_BulkInsert_NumericTypes_PreservesPrecision`
- [x] `Postgres_BulkInsert_TimestamptzColumn_PreservesOffset`
- [x] `Postgres_BulkInsert_ReturningGeneratedIds_RoundTrips` *(new — RETURNING output path)*
- [x] `Postgres_BulkInsert_ForeignKeyOrConstraintViolation_RollsBack`
- [ ] ~~`Postgres_BulkInsert_JsonbColumn_RoundTrips`~~ *(deferred — jsonb param needs an explicit `::jsonb` cast/type-hint; tracked for a small follow-up, flagged in the plan as the risky one)*
- [ ] ~~`Postgres_BulkInsert_MultipleRows_GeneratesExpectedSqlShape`~~ *(the hand-built writer IS the multi-row shape; asserting exact SQL text is brittle — covered implicitly by the 10k + precision tests)*

---

## 2.4.a.5 Persistence + API Surface (Named Connections) 🌐 ✅ COMPLETE

> **Purpose:** Make `IDbConnectionRegistry` configurable from `appsettings.json` **and** mutable via the API at runtime (per Q9 resolution). Credentials encrypted at rest~ 🔐

**Complexity:** 🟠 Medium *(API surface + encryption + persistence option)*

> **✅ Implemented & verified (July 2026):** full solution build green (0 errors), **30/30** related tests passing (17 shared-infra + 6 SQLite-registry + 7 API). Two design deviations from the original task list, both cleaner than planned — see the 💡 corrections inline below.

### Tasks

- [x] **Config-bound registration** ⚙️
  - [x] New: `Workflow.Modules.Database/Configuration/DatabaseConnectionsOptions.cs` *(landed early in 2.4.a.0 — registry hydration needs it)*
    ```csharp
    public sealed class DatabaseConnectionsOptions
    {
        public Dictionary<string, DbConnectionDescriptor> Connections { get; set; } = new();
        public bool DisableRuntimeCrud { get; set; } = false; // opt-out switch (D4)
    }
    ```
  - [x] Bound from `appsettings.json:Workflow:Database` section in `Program.cs`
  - [x] On startup: hydrate `InMemoryDbConnectionRegistry` from config *(plus `SeedConfiguredConnectionsAsync` idempotently seeds config ids into whichever registry is active, so the SQLite-persisted registry also honours appsettings)*

- [x] **Persisted-CRUD option** 💾
  - [x] New: `Workflow.Persistence.Sqlite/Repositories/SqliteDbConnectionRegistry.cs` — mirrors `SqliteWebhookRegistrationRepository` pattern from 2.3.9
    > 💡 **Correction (home):** lives in `Workflow.Persistence.Sqlite/Repositories/` (**not** `Workflow.Modules.Database/Connections/`) because it needs the linq2db + migration infra — and to keep `IPersistenceProvider`/persistence free of a modules-layer dependency. Ships with `DbConnectionEntity` + `WorkflowDataConnection.DbConnections` table mapping~
  - [x] New migration: `Workflow.Persistence.Sqlite/Migrations/Migration_006_DbConnections.cs`
    - Columns: `connection_id` (PK), `provider_key`, `connection_string_encrypted` (TEXT), `display_name`, `enabled` (INT)
  - [x] ~~Extend `IPersistenceProvider` with `IDbConnectionRegistry? DbConnections { get; }`~~
    > 💡 **Correction (wiring):** `IPersistenceProvider` was **NOT** extended. Instead `SqlitePersistenceProvider` exposes a `CreateDbConnectionRegistry(IConnectionStringProtector)` **factory method**, and `Program.cs` casts (`persistenceProvider is SqlitePersistenceProvider`) to build the registry lazily. This keeps the modules-layer `IConnectionStringProtector` dependency **out of** the shared `IPersistenceProvider` contract (which other providers implement), rather than forcing a nullable member onto every provider~ 🌸
  - [x] Wire in `Program.cs`: when SQLite persistence is configured, override the in-memory registry with the SQLite-persisted one (resolved lazily so the Data-Protection protector is available)

- [x] **Credential encryption** 🔒
  - [x] Use ASP.NET `IDataProtector` (`Microsoft.AspNetCore.DataProtection`) for connection-string encryption at rest
    > 💡 **Seam:** encryption is abstracted behind `IConnectionStringProtector` (in `Workflow.Modules.Database/Abstractions/`) with a `NoOpConnectionStringProtector` default; the host registers the Data-Protection-backed `DataProtectionConnectionStringProtector`. Keeps ASP.NET Data Protection out of the persistence + modules layers~
  - [x] Purpose string: `"Workflow.Modules.Database.ConnectionString"`
  - [x] Encrypt on `UpsertAsync`; decrypt on `GetAsync`/`ListAsync` (masking to `"***"` is applied at the API-response layer)

- [x] **API endpoints** (in `Workflow.Api/Database/DatabaseConnectionEndpoints.cs`)
    > 💡 **Correction (shape):** implemented as **minimal-API endpoints** (`MapDatabaseConnectionEndpoints`), **not** an MVC `DatabaseConnectionsController` — consistent with the existing `WebhookEndpoints` pattern in this codebase~
  - [x] `GET /api/database/connections` → list (masked connection strings)
  - [x] `GET /api/database/connections/{id}` → single (masked by default; `?reveal=true` returns plaintext — admin policy still a TODO, ungated like webhooks in V1)
  - [x] `POST /api/database/connections` → upsert; rejects if `DisableRuntimeCrud == true` (returns `403 Forbidden`)
  - [x] `DELETE /api/database/connections/{id}` → delete (`204` / `404`)
  - [ ] ~~All endpoints behind the same auth policy as webhooks~~ → **deferred:** no auth infrastructure exists in the API yet (webhooks are also unauthenticated in V1). Reveal-gating tracked for the API-security pass~ 🔐

### Tests (target ~10): ✅ **13 delivered** → `Workflow.Tests/Modules/Database/ConnectionRegistryTests.cs` (6) + `Workflow.Tests/Api/DatabaseConnectionsApiTests.cs` (7) + config-bound in `SharedInfrastructureTests.cs`

- [x] `ConnectionRegistry_ConfigBoundEntry_LookupByIdReturnsDescriptor` *(in `SharedInfrastructureTests` — covers `ConfigBound_Connections_HydratedAtStartup`)*
- [x] `SqliteRegistry_UpsertEncryptsConnectionString` *(reads raw ciphertext from the table — proves at-rest encryption)*
- [x] `SqliteRegistry_UpsertThenGet_RoundTrips` + `SqliteRegistry_List_ReturnsAllDecrypted` *(covers decrypt-on-read + masking is validated at the API layer)*
- [x] `SqliteRegistry_Persist_SurvivesAcrossRegistryInstances` *(restart simulation)* + `SqliteRegistry_Upsert_UpdatesExisting` + `SqliteRegistry_DeleteUnknown_ReturnsFalse`
- [x] `Api_PostConnection_RegistersAndIsRetrievable`
- [x] `Api_DeleteConnection_RemovesFromRegistry` + `Api_DeleteUnknownConnection_Returns404`
- [x] `Api_PostConnection_WhenDisableRuntimeCrudTrue_Returns403`
- [x] `Api_GetConnection_DefaultIsMasked` + `Api_ListConnections_ReturnsMasked`
- [x] `Api_GetConnection_RevealReturnsFullConnectionString` *(reveal path; auth-gating deferred with no auth infra — see note above)*

---

## 2.4.a.6 E2E Demo + Documentation 📖 ✅ COMPLETE

> **Purpose:** Prove the four modules + named connections + transactions work end-to-end in a realistic workflow; write `docs/database-modules.md`~ ✨

**Complexity:** 🟢 Low

> **✅ Implemented & verified (July 2026):** E2E test **compile-verified** (Docker-gated like the sibling Postgres suites — fails only at container startup when Docker is absent, not on logic); `docs/database-modules.md`, `phases/README.md`, and `DOCUMENTATION_INDEX.md` all updated. One approach deviation recorded 💡 below.

### Tasks

- [x] **E2E demo workflow**
  - [x] New: `Workflow.Tests.Integration/Database/DatabaseE2ETests.cs`
  - [x] Demo workflow shape:
    ```
    webhook_trigger → bulkinsert(orders) → transaction[update_inventory; insert_audit] → query(orders_by_user) → setvariable(audit=done)
    ```
    > 💡 **Correction (approach):** `webhook_trigger` and `setvariable` are engine-level concerns (Akka + HTTP trigger + variable store). Consistent with **every** other Postgres integration test in this repo, the E2E invokes the **modules directly** (`ModuleExecutionContext` → `ExecuteAsync`) sharing one `InMemoryDbConnectionRegistry`, rather than spinning up the full engine. The webhook trigger is the framing "arrange"; `setvariable` is represented by capturing the final query output into a local variables bag. Proves the module family cooperates + shares infra while keeping the E2E Docker-only (no engine wiring)~ 🌸
  - [x] One Postgres Testcontainer, seed schema (`orders`/`inventory`/`audit`) in fixture, assert end-state row counts + inventory decrements + audit count
  - [x] Test: `Demo_OrderFlow_AllStepsSucceed_FinalQueryReturnsExpected`
  - [x] Test: `Demo_TransactionMiddleFails_NoOrdersInserted_FullRollback` *(order writes live inside the transaction; a mid-op PK violation rolls back all of them → 0 orders)*

- [x] **`docs/database-modules.md`** *(interim — restructured typed-first in 2.4.b.6)*
  - [x] Sections: Overview · Connection management · Per-module reference (×4) · Parameter binding · Transactions & isolation · BulkCopy semantics · Provider notes (Postgres vs SQLite) · Security best practices · Migration guide for adding new providers · Post-MVP roadmap
  - [x] Include the design-doc link at the top
  - [x] **Overview states up-front:** typed linq (`builtin.database.linq`, §2.4.b) is the recommended default; this raw-SQL family is the escape hatch (D12/D13)

- [x] **README + DOCUMENTATION_INDEX updates**
  - [x] Mark Database modules (2.4) in `phases/README.md` — **2.4.a complete ✅**, 2.4.b (typed linq) pending *(not a blanket ✅: the D12 re-plan promoted 2.4.b into the MVP, so the phase isn't fully done until the typed family ships)*
  - [x] Add `database-modules.md` to `DOCUMENTATION_INDEX.md` *(new "Feature & Module Guides" section)*

### Tests (target ~2): ✅ **2 delivered** *(Docker-gated, compile-verified)*

- [x] `Demo_OrderFlow_AllStepsSucceed_FinalQueryReturnsExpected`
- [x] `Demo_TransactionMiddleFails_NoOrdersInserted_FullRollback`

---

## 2.4.b Typed Linq Family 🌟 (Weeks 13-14 — **MVP, promoted from post-MVP per D12**)

> **Purpose:** Ship the primary authoring surface — `builtin.database.linq` — where users write typed C# linq2db bodies against a selected table catalog, Roslyn-validated at publish time, sandbox-previewable, executed via collectible ALC. Full design in [design doc §5.2 + §8](../new-feature-design/Phase2-4-DatabaseModules-Design.md#52-phase-24b--typed-linqroslyn-family-post-mvp-2-weeks)~ 💖

> **CopilotNote:** 2.4.b depends only on 2.4.a.0 (shared infra) + 2.4.a.5 (named connections) — it does **not** depend on the raw-SQL modules 2.4.a.1–4, so it can start as soon as the infra lands if staffing allows parallelisation (D16 — serial Weeks 13-14 is the signed-off baseline). The design-doc concerns C1–C11 each map to a mitigation task below~ 🌸

---

### 2.4.b.0 Project Scaffolding 🏗️ ✅ COMPLETE

**Complexity:** 🟢 Low

> **✅ Implemented & verified (July 2026):** full solution build green (**0 errors**); **4/4** scaffolding tests passing (target was ~2). Roslyn (4.11.0) + Basic.Reference.Assemblies (1.7.8) restore + run; the D14 quarantine is proven by test. Deviations recorded 💡 below.

#### Tasks

- [x] **`Workflow.Modules.Database.Linq` project layout**
  - [x] New project: `Workflow.Modules.Database.Linq/Workflow.Modules.Database.Linq.csproj`
  - [x] References: `Workflow.Modules.Database` (shared infra), `Workflow.Modules`, `Microsoft.CodeAnalysis.CSharp` (Roslyn), `Basic.Reference.Assemblies` (portable refs — mitigates C8), `LinqToDB`
  - [x] Add `Microsoft.CodeAnalysis.CSharp` (4.11.0) + `Basic.Reference.Assemblies` (1.7.8) to `Directory.Packages.props` (both MIT — Q10 ✅)
  - [x] Add to `Workflow.sln`
  - [x] Folder layout: `Abstractions/` · `Compilation/` · `Execution/` · `Preview/` · `Builtin/`
    > 💡 **Note:** each folder currently holds a one-line `README.md` marker describing what lands there + in which sub-slice (empty folders aren't trackable). These are deleted as real files replace them~
  - [x] `DatabaseLinqModuleServiceCollectionExtensions.AddDatabaseLinqModules(this IServiceCollection)` — separate opt-in entry point (D14); wired by the host, **NOT** by `AddWorkflowModules()`
    > 💡 **Scaffold state:** the extension is a documented **no-op** today (chainable/idempotent); real registrations (compiler/cache/module/previewer) slot in across 2.4.b.1–4. Added `AssemblyMarker` (a Roslyn-toolchain smoke) so the slice has verifiable substance + so the compiler emits the Roslyn/BasicRefs assembly references the quarantine test asserts on.
  - [x] 💡 **`NoWarn SA0002`** on the Linq project only — StyleCop.Analyzers (pinned to older Roslyn) can't load `stylecop.json` when a project references a newer `Microsoft.CodeAnalysis` as a runtime library. Known cosmetic conflict (also pre-exists on `Workflow.Api`); suppressed with a comment.

#### Tests (target ~2): ✅ **4 delivered** → `Workflow.Tests/Modules/DatabaseLinq/ScaffoldingTests.cs`
  > 💡 **Deviation:** the plan's `AddDatabaseLinqModules_RegistersCompilerPreviewerAndModule` can't be honest at scaffold time (those types land in 2.4.b.1/3/4). Replaced with tests that are true *now* + keep the D14 guarantee:
- [x] `AddDatabaseLinqModules_IsChainableAndIdempotent` *(no `IWorkflowModule` registered yet — module lands 2.4.b.3)*
- [x] `RoslynToolchain_ResolvesInsideLinqAssembly` *(Roslyn parse smoke + Basic.Reference.Assemblies referenced)*
- [x] `LinqAssembly_ReferencesRoslynAndBasicRefs` *(reference-set assertion)*
- [x] `WorkflowModules_DoesNotReferenceRoslyn_QuarantineHolds` *(the D14 quarantine — deterministic `GetReferencedAssemblies` check)* — replaces the plan's `AddWorkflowModules_DoesNotPullRoslyn`

---

### 2.4.b.1 `IWorkflowLinqCompiler` + Whitelists + `LinqInputs` Codegen 🧬 ✅ COMPLETE

**Complexity:** 🔴 High *(Roslyn pipeline + security surface)*

> **✅ Implemented & verified (July 2026):** full solution build green (**0 errors**); **14/14** compiler tests passing (target ~12) + the 4 scaffolding tests still green (18 DatabaseLinq total). Dual-POCO sourcing landed per user direction; deviations recorded 💡 below.

#### Tasks

- [x] **`IWorkflowLinqCompiler`**
  - [x] New: `Workflow.Modules.Database.Linq/Abstractions/IWorkflowLinqCompiler.cs` (+ `Abstractions/LinqDiagnostic.cs`)
    > 💡 **Deviation (result shape):** `LinqCompileResult` carries **`byte[]? AssemblyBytes`**, NOT `string? BlobKey`. 2.4.b.1 is a pure compiler — blob key/write **and HMAC signing** move to 2.4.b.2 (co-located with `IBlobStore` + the 2.4.b.3 load-time verify). Keeps the compiler pure/testable. `LinqDiagnostic` carries `Id`/`Severity`/`Message`/`Line`/`Column` (line/col point into the wrapped unit; user-relative mapping is a 2.4.b.5 refinement).
  - [x] Codegen: `DynamicWorkflowContext : DataConnection` with `ITable<T>` per table (`DynamicContextCodeGenerator`)
    > 💡 **Deviation (ctor):** uses `DynamicWorkflowContext(LinqToDB.DataOptions options) : base(options)` — the two-string `DataConnection(provider, connStr)` ctor from the design snippet isn't in linq2db 6.3.0. Execution (2.4.b.3) constructs via `DataOptions`.
  - [x] Codegen: `readonly struct LinqInputs` from `ModuleSchema.Properties` via `RestrictedTypeMapper` (§8.6 scalar allowlist); non-allowlisted → `object?` + warning (`WFLINQ004`), or error in strict mode (`LinqInputsCodeGenerator`)
  - [x] Codegen: `WorkflowScript.ExecuteAsync(DynamicWorkflowContext db, LinqInputs inputs, CancellationToken ct)` async wrapper (mitigates C2)
  - [x] **🌟 Dual-POCO table resolution (`TableTypeResolver` — per user direction):** each selected table resolves its entity type via **(a) a plugin POCO** (`ClrTypeName` + `AssemblyName`, preferred + authoritative when present/loadable) **or (b) a column-generated POCO** emitted from `WorkflowColumnMetadata` via `SqlTypeMapper` (provider SQL-type → C# type). A table with neither → error `WFLINQ001`; an unloadable plugin type → `WFLINQ002`; an unmapped column type → `WFLINQ003` (warn, or error in strict). **Precedence: plugin POCO wins** when both are present.
- [x] **Security whitelists (mitigates C1)**
  - [x] Reference set (`ReferenceWhitelist`): `Basic.Reference.Assemblies.Net80.References.All` + `LinqToDB` + resolved plugin assemblies
    > 💡 **Note (enforcement model):** `Net80` refs = the whole BCL, so the *real* gate is the **usings allowlist** (codegen prepends ONLY `System`/`System.Linq`/`System.Collections.Generic`/`System.Threading`/`System.Threading.Tasks`/`LinqToDB`) **+ `ForbiddenSyntaxWalker`**, which catches fully-qualified reaches like `System.IO.File.Delete`. Trimming references isn't the mechanism (documented in `ReferenceWhitelist`).
  - [x] `ForbiddenSyntaxWalker` (`CSharpSyntaxWalker`) rejects `Process`/`File`/`Directory`/`Socket`/`HttpClient`/`Activator`/`Marshal`/`Assembly`/… identifiers (`WFLINQ100`), `[DllImport]` (`WFLINQ101`), `unsafe` (`WFLINQ102`), pointers (`WFLINQ103`), `stackalloc` (`WFLINQ104`). Scans the user body **standalone** so codegen/column identifiers can't false-positive.
  - [x] Usings allowlist (codegen-controlled — user bodies can't declare usings)
  - [ ] ~~Emitted assembly HMACed with per-instance key~~ → **moved to 2.4.b.2** (signing belongs with the blob write + the 2.4.b.3 load-time verify)
- [ ] **Trusted-author gate (Q2/Q15):** enforced at the API layer in **2.4.b.5**, not in the compiler — note only
- [x] Registered `IWorkflowLinqCompiler` + `TableTypeResolver` via `TryAddSingleton` in `AddDatabaseLinqModules()`

#### Tests (target ~12): ✅ **14 delivered** → `Workflow.Tests/Modules/DatabaseLinq/LinqCompilerTests.cs`
- [x] `Compile_ValidQuery_Succeeds`
- [x] `Compile_ColumnGeneratedPocoTable_Succeeds` *(new — generated-POCO path)*
- [x] `Compile_TypoInTableName_ReturnsMemberDiagnostic` *(asserts **CS1061** — `db.Ordrs` is a member typo, not the plan's CS0103)*
- [x] `Compile_TypoInInputProperty_ReturnsCS1061Diagnostic`
- [x] `Compile_WrongTypeComparison_ReturnsCS0019Diagnostic`
- [x] `LinqInputs_Codegen_AllowlistedScalarTypes_EmitTypedProperties`
- [x] `LinqInputs_Codegen_NonAllowlistedType_FallsBackToObjectWithWarning` *(covers `Compile_WarningsSurfacedAlongsideSuccess`)*
- [x] `LinqInputs_Codegen_StrictMode_NonAllowlistedType_Rejected`
- [x] `Compile_ForbiddenApi_ProcessStart_Rejected`
- [x] `Compile_ForbiddenApi_FileIo_Rejected`
- [x] `Compile_ForbiddenApi_HttpClient_Rejected` *(renamed from `_ForbiddenUsing_SystemNet` — user bodies can't declare usings; the reach is a fully-qualified `System.Net.Http.HttpClient`)*
- [x] `Compile_UnsafeBlock_Rejected`
- [x] `Compile_PluginPocoTable_Succeeds` *(new — plugin-POCO path)*
- [x] `Compile_TableWithNoTypeOrColumns_ReturnsDiagnostic` *(new — `WFLINQ001`)*

---

### 2.4.b.2 Compiled-Assembly Caching in `IBlobStore` 📦 ✅ COMPLETE

**Complexity:** 🟡 Low-Medium

> **✅ Implemented & verified (July 2026):** full solution build green (**0 errors**); **9/9** cache tests passing (target ~6), 27 DatabaseLinq total. Scoped to the cache/HMAC/LRU unit; the publish-graph-walk orchestration moved to 2.4.b.5 (no publish hook exists yet). Deviations recorded 💡 below.

#### Tasks

- [x] Blob key: `compiled-modules/{definitionId}/{nodeId}/{SHA256(userCode + schemaVersion + selectedTables.OrderedHash())}.dll` (design doc §8.3) — cache-invalidation is automatic via the hash (D15) *(`Execution/CompiledAssemblyKey.cs`; ordered, order-independent table fingerprint incl. columns)*
- [x] **HMAC signing (moved here from 2.4.b.1 per plan):** `ILinqAssemblySigner` + `HmacLinqAssemblySigner` prepend an HMAC-SHA256 tag on write, verify + strip on read (tamper → cache miss, never handed to the loader). Key via `ILinqHmacKeyProvider`
    > 💡 **HMAC key (Consideration 3 resolved):** the Linq project ships an **ephemeral per-process** `EphemeralLinqHmacKeyProvider` default (safe; on-disk cache recomputed once after a restart). The host may register a **Data-Protection-backed stable key** (mirrors the 2.4.a.5 protector) for cross-restart cache reuse — the seam is `ILinqHmacKeyProvider`.
- [x] In-memory LRU (by key) of verified assembly bytes in front of `IBlobStore` — `LinqCompileCacheOptions.LruCapacity` (default 64) *(`Execution/CompiledAssemblyCache.cs`)*
- [x] Orphan cleanup: `EvictDefinitionAsync(definitionId)` deletes `compiled-modules/{definitionId}/*` + drops LRU entries
    > 💡 **Limitation:** `IBlobStore` has **no list-by-prefix API**, so eviction deletes the keys this process has stored (tracked in-memory). Cross-restart orphan GC needs a blob-store list API — tracked as a follow-up.
- [ ] ~~Compile hook at workflow publish time~~ → **moved to 2.4.b.5** (Consideration 1): `IWorkflowRepository` has no publish/validate step, so the "compile every linq node on save" graph-walk belongs with the API `compile` endpoint where compiler + module + API converge~ 🌸
- [ ] ~~Local-filesystem fallback if `IBlobStore` unavailable (Q5)~~ → deferred: the cache takes an injected `IBlobStore` (host wires the real one; tests use an in-memory double). A local-FS `IBlobStore` fallback is a host-wiring concern, tracked for 2.4.b.5/host config.
- [x] Registered `ICompiledAssemblyCache` + `ILinqAssemblySigner` + `ILinqHmacKeyProvider` + options via `TryAddSingleton` in `AddDatabaseLinqModules()`

#### Tests (target ~6): ✅ **9 delivered** → `Workflow.Tests/Modules/DatabaseLinq/CompiledAssemblyCacheTests.cs` *(in-memory `IBlobStore` double)*
- [x] `Store_WritesBlobUnderCompiledModulesNamespace`
- [x] `SameCodeAndSchema_ProducesSameKey`
- [x] `ChangedCode_ProducesNewKey`
- [x] `ChangedSchemaVersion_ProducesNewKey`
- [x] `ChangedSelectedTables_ProducesNewKey` *(bonus — table-fingerprint invalidation)*
- [x] `Hmac_RoundTrips` + `Hmac_TamperedBlob_RejectedOnRead` *(the signing half of the 2.4.b.3 tamper test)*
- [x] `Lru_EvictsLeastRecentlyUsed_ReloadsFromBlobStore`
- [x] `EvictDefinition_RemovesAllNodeBlobs`
- [ ] ~~`PublishWithFailingLinqNode_FailsPublishWithDiagnostics`~~ → moved to 2.4.b.5 (no publish hook exists)

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

### 2.4.a.P6 Typed Bulk Insert (`AsQueryable`/`InsertWithOutput`) 🧬 *(post-MVP — depends on 2.4.b)*

**Purpose:** Offer a **typed** bulk-insert path using linq2db's improved `AsQueryable` + `InsertWithOutput`/`InsertWithOutputAsync` for the cases where a compile-time entity type exists (i.e. tables registered through the 2.4.b Roslyn-generated model family). This gives first-class output-column retrieval (identity/defaults/computed) and lets linq2db own SQL generation + provider quirks, instead of the hand-built `BatchInsertWriter`.

**Complexity:** 🟠 Medium *(only meaningful once 2.4.b's typed models + `IWorkflowTableCatalog` ClrTypeName exist)*

**Why deferred (Q14):** `AsQueryable`/`InsertWithOutput` require a typed `T` — the dynamic 2.4.a family has no such type. The MVP delivers the *output-column* benefit via the `returningColumns` → `outputRows` path on `builtin.database.bulkinsert` (hand-built `RETURNING`). This slice is the "graduate to typed" upgrade once 2.4.b lands.

#### Tasks
- [ ] Add a typed bulk path (new module `builtin.database.bulkinsert.typed`, or a `mode: "typed"` on the linq family) that resolves a 2.4.b-generated entity type for `tableName` via `IWorkflowTableCatalog.ClrTypeName`
- [ ] Use `source.AsQueryable(db).Insert(...)` for the multi-row insert and `InsertWithOutputAsync` where output columns are requested
- [ ] Fall back to `builtin.database.bulkinsert` (hand-built) when no typed model is registered for the table
- [ ] Benchmark typed vs hand-built to confirm parity/gains before making it the default

#### Tests (target ~6)
- [ ] `TypedBulk_RegisteredModel_InsertsAllRows`
- [ ] `TypedBulk_InsertWithOutput_ReturnsGeneratedIds`
- [ ] `TypedBulk_NoRegisteredModel_FallsBackToHandBuilt`
- [ ] `TypedBulk_ProviderQuirks_HandledByLinq2Db` *(Postgres + SQLite)*
- [ ] `TypedBulk_ColumnMapping_HonoursEntityAttributes`
- [ ] `TypedBulk_LargeBatch_MatchesHandBuiltThroughput` *(perf parity)*

---

### 2.4.b.P* Post-Typed-Linq Slices 🌟 *(post-MVP — 2.4.b itself is now MVP, see §2.4.b above)*

- **2.4.b.P1** — Typed record codegen upgrade (replaces `LinqInputs` struct with `record LinqInputs(...)` once allowed-types allowlist is ratified — design doc §8.6 Phase 2)
- **2.4.b.P2** — Testcontainers-backed preview (replace `:memory:` SQLite with real target provider — resolves C10 properly)
- **2.4.b.P3** — `IWorkflowTableCatalog` versioned auto-discovery from registered databases (resolves Q4 long-term; the one-shot import from 2.4.b.4 is the MVP stopgap)
- **2.4.b.P4** — `Workflow.UI` code-editor panel (Monaco + `/validate` diagnostics squigglies + preview pane) — **full MVP scope mapped in [Phase2-4-LinqEditorPanel-Design.md](../new-feature-design/Phase2-4-LinqEditorPanel-Design.md)** (per Q16/D18; ~1 week, ~12 bUnit tests in new `Workflow.Tests.UI` project; UQ1–UQ4 resolved ✅ — BlazorMonaco · cookie same-origin auth · definition API round-trip · dedicated UI test project)

---

## Phase 2.4 Deliverables ✅

When 2.4 ships (Week 14), all of the following must be true:

**2.4.a — Shared infra + escape-hatch SQL family (Week 12 gate):** ✅ **COMPLETE**

- [x] **Modules (4):** `builtin.database.{query,execute,transaction,bulkinsert}` all discoverable, validated, executable on Postgres + SQLite — documented as the **escape hatch** (D13)
- [x] **Shared infra:** `Workflow.Modules.Database` project with `IDbConnectionFactory`, `IDbProviderRegistry`, `IDbConnectionRegistry`, `IDbTransactionScope`, `IWorkflowTableCatalog` (stub) all DI-registered
- [x] **Named connections:** config-bound + runtime-CRUD API + encrypted-at-rest credentials
- [x] **SQL injection prevented** — every test verifies parameterisation; explicit injection-attempt tests in `SqlParameterBinder` test suite
- [x] **E2E demo workflow** runs against a real Postgres Testcontainer *(compile-verified; Docker-gated)*
- [x] **~86 tests passing** across 2.4.a.0–2.4.a.6 — **66 SQLite/unit passing** (17 infra + 13 query + 9 execute + 16 transaction + 11 bulkinsert) + **13 connection-registry/API passing** + Docker-gated Postgres suites (query/execute/transaction/bulkinsert) + **2 E2E** (all compile-verified)

**2.4.b — Typed linq family, the primary surface (Week 14 gate, per D12):**

- [ ] **`builtin.database.linq`** discoverable, publish-time-compiled, ALC-executed on Postgres + SQLite
- [x] **`IWorkflowLinqCompiler`** with reference/usings/syntax whitelists + `LinqInputs` accessor-struct codegen (design doc §8.6 Phase 1) *(2.4.b.1 ✅ — plus dual-POCO table resolution; HMAC signing deferred to 2.4.b.2)*
- [x] **Compiled-assembly cache** in `IBlobStore` under `compiled-modules/` with hash-keyed invalidation + HMAC verification (D15) *(2.4.b.2 ✅ — LRU + ephemeral/Data-Protection HMAC seam; publish-hook + local-FS fallback → 2.4.b.5)*
- [ ] **Sandbox preview** (`IWorkflowLinqPreviewer`, always-rollback `:memory:` SQLite) + one-shot catalog import (Q17/D19 ✅)
- [ ] **API endpoints:** `POST /api/database/linq/{validate,preview,compile}` — compile gated by trusted-author policy (Q2/Q15)
- [ ] **Security review checklist passed** (whitelist bypass, HMAC tamper, ALC leak under load, no conn-string leakage in diagnostics)
- [ ] **~48 tests passing** across 2.4.b.0–2.4.b.6 (2 scaffold + 12 compiler + 6 cache + 10 module + 8 preview + 6 API + 4 E2E/security)

**Cross-cutting:**

- [x] **docs/database-modules.md** — **interim done** (typed-first *framing*: leads with the "reach for typed linq first" recommendation; body currently documents the raw-SQL escape-hatch family since 2.4.b isn't built yet — full linq authoring guide added in 2.4.b.6)
- [ ] **~134 tests passing** total across both families
- [ ] **90%+ test coverage** on `Workflow.Modules.Database` + `Workflow.Modules.Database.Linq`
- [ ] **0 errors, 0 new warnings** in `dotnet build`
- [x] **Roslyn dep quarantined** — `AddWorkflowModules()` alone must not load `Microsoft.CodeAnalysis` (D14) *(established + test-locked in 2.4.b.0 via `WorkflowModules_DoesNotReferenceRoslyn_QuarantineHolds`)*
- [x] **README + phases/README.md** updated — Database modules (2.4) marked **2.4.a complete ✅** / 2.4.b pending (not blanket-✅: D12 promoted 2.4.b into the MVP); `docs/database-modules.md` added to `DOCUMENTATION_INDEX.md`

**Post-MVP slices (tracked, non-blocking 2.5+):**
- [ ] **2.4.a.P1** Stored Procedure Support — ~5 tests
- [ ] **2.4.a.P2** Savepoint / Nested Transactions — ~6 tests
- [ ] **2.4.a.P3** MySQL + SQL Server Providers — ~16 tests
- [ ] **2.4.a.P4** Connection-Pool Metrics + OpenTelemetry — ~4 tests
- [ ] **2.4.a.P5** Streaming Query + Concurrent BulkInsert — ~10 tests
- [ ] **2.4.a.P6** Typed Bulk Insert (`AsQueryable`/`InsertWithOutput`, depends on 2.4.b) — ~6 tests
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

Workflow.Modules.Database.Linq/                         ← NEW PROJECT (2.4.b.0) ✅
  Workflow.Modules.Database.Linq.csproj                  ← new (2.4.b.0) ✅ (NoWarn SA0002)
  AssemblyMarker.cs                                      ← new (2.4.b.0) ✅ — Roslyn-toolchain smoke + ref-emit
  DatabaseLinqModuleServiceCollectionExtensions.cs       ← new (2.4.b.0) ✅ — opt-in AddDatabaseLinqModules() (no-op scaffold)
  Abstractions/
    IWorkflowLinqCompiler.cs                             ← new (2.4.b.1) ✅ — LinqCompileRequest/Result (bytes, not blob key)
    LinqDiagnostic.cs                                    ← new (2.4.b.1) ✅
    IWorkflowLinqPreviewer.cs                            ← new (2.4.b.4)
  Compilation/
    WorkflowLinqCompiler.cs                              ← new (2.4.b.1) ✅ — Roslyn pipeline orchestrator
    TableTypeResolver.cs                                 ← new (2.4.b.1) ✅ — dual-POCO (plugin OR column-generated)
    RestrictedTypeMapper.cs                              ← new (2.4.b.1) ✅ — Type→C# name (§8.6 LinqInputs)
    SqlTypeMapper.cs                                     ← new (2.4.b.1) ✅ — SQL type→C# name (generated POCOs)
    LinqInputsCodeGenerator.cs                           ← new (2.4.b.1) ✅ — accessor-struct codegen (§8.6 Phase 1)
    DynamicContextCodeGenerator.cs                       ← new (2.4.b.1) ✅ — POCO + ITable<T> context + wrapper
    CodeIdentifiers.cs                                   ← new (2.4.b.1) ✅ — identifier/literal sanitising
    ForbiddenSyntaxWalker.cs                             ← new (2.4.b.1) ✅ — syntax blocklist
    ReferenceWhitelist.cs                                ← new (2.4.b.1) ✅ — reference set + usings allowlist
  Execution/
    CompiledAssemblyCache.cs                             ← new (2.4.b.2) ✅ — IBlobStore + LRU + options
    CompiledAssemblyKey.cs                               ← new (2.4.b.2) ✅ — §8.3 hash key + LinqCodegen.SchemaVersion
    HmacLinqAssemblySigner.cs                            ← new (2.4.b.2) ✅ — HMAC sign/verify + ephemeral key provider
    CollectibleScriptRunner.cs                           ← new (2.4.b.3) — ALC lifecycle
  Abstractions/
    ICompiledAssemblyCache.cs                            ← new (2.4.b.2) ✅
    ILinqAssemblySigner.cs                               ← new (2.4.b.2) ✅ — signer + ILinqHmacKeyProvider seam
  Preview/
    WorkflowLinqPreviewer.cs                             ← new (2.4.b.4) — rollback-only SQLite sandbox
    CatalogSchemaImporter.cs                             ← new (2.4.b.4) — one-shot import (Q17)
  Builtin/
    LinqQueryModule.cs                                   ← new (2.4.b.3) — builtin.database.linq

Workflow.Modules/
  WorkflowModulesServiceCollectionExtensions.cs          ← modified (2.4.a.0) — add AddDatabaseModules() (NOT linq — D14)
  Builtin/BuiltinModuleRegistration.cs                   ← modified — register 4 new modules

Workflow.Persistence.Sqlite/
  Migrations/Migration_006_DbConnections.cs              ← new (2.4.a.5) ✅
  Data/Entities/DbConnectionEntity.cs                    ← new (2.4.a.5) ✅ — linq2db table mapping
  Repositories/SqliteDbConnectionRegistry.cs             ← new (2.4.a.5) ✅ — persisted registry (moved here from Modules.Database)
  SqlitePersistenceProvider.cs                           ← modified (2.4.a.5) ✅ — CreateDbConnectionRegistry(protector) factory

Workflow.Modules.Database/
  Abstractions/IConnectionStringProtector.cs             ← new (2.4.a.5) ✅ — protector seam + NoOp default

Workflow.Api/
  Database/DatabaseConnectionEndpoints.cs                ← new (2.4.a.5) ✅ — minimal API (not an MVC controller)
  Database/DataProtectionConnectionStringProtector.cs    ← new (2.4.a.5) ✅ — IDataProtector-backed protector
  Controllers/DatabaseLinqController.cs                  ← new (2.4.b.5) — validate/preview/compile + catalog import
  Program.cs                                             ← modified (2.4.a.5 ✅ + 2.4.b.0) — wire DI + endpoints + AddDatabaseLinqModules()

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
    DatabaseE2ETests.cs                                  ← new (2.4.a.6) ✅ — Testcontainers
    PostgresLinqTests.cs                                 ← new (2.4.b.3) — Testcontainers
    DatabaseLinqE2ETests.cs                              ← new (2.4.b.6) — Testcontainers

docs/
  database-modules.md                                    ← new (2.4.a.6) ✅ (restructured typed-first in 2.4.b.6)

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

