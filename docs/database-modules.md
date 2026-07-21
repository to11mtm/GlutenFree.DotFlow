# 🗄️ Database Modules (Phase 2.4)

Made with 💖 by Ami-Chan! UwU ✨

> **Design docs:** [Phase 2.4 plan](../phases/Phase2-4-DatabaseModules.md) · [Design exploration](../new-feature-design/Phase2-4-DatabaseModules-Design.md)

---

## Overview

DotFlow's database family lets workflows talk to relational databases (Postgres + SQLite in V1). There are **two authoring surfaces**, and picking the right one matters:

| Surface | Module(s) | When to use |
|---------|-----------|-------------|
| 🌟 **Typed linq** *(recommended default)* | `builtin.database.linq` *(Phase 2.4.b)* | The everyday path — write typed C# linq2db against a selected table catalog, Roslyn-validated, sandbox-previewable. **Reach for this first.** |
| 🧰 **Raw SQL** *(escape hatch)* | `builtin.database.{query,execute,transaction,bulkinsert}` | When typed linq can't express it: vendor-specific DDL, hand-tuned bulk paths, ad-hoc SQL against unregistered catalogs. |

> **Typed-first (D12/D13):** you should **not** have to hand-write SQL for routine work. The raw-SQL modules are the deliberate escape hatch — powerful, parameterised, and always available, but not the first tool you reach for~ 🌸

This document leads with the **typed linq surface** (2.4.b), then documents the **raw-SQL escape-hatch family** (2.4.a) as its own chapter.

---

## 🌟 Typed linq (`builtin.database.linq`) — the default surface

Write ordinary, strongly-typed C# linq2db against your tables. Roslyn validates it, an in-memory SQLite sandbox previews it, and it's compiled once at publish time and executed in a collectible `AssemblyLoadContext`. No SQL strings, real compile errors, IDE-grade feedback~ ✨

### Authoring flow: import → author → validate → preview → publish

1. **Import the table catalog** — one-shot introspection so you can author against real tables:
   ```http
   POST /api/database/catalog/{connectionId}/import
   ```
   This reads the live schema (`information_schema` on Postgres, `PRAGMA table_info` on SQLite) and upserts `WorkflowTableMetadata` (table + column names/types/nullability) into the catalog. Manual + on-demand in V1 (versioned auto-discovery is 2.4.b.P3).

2. **Author** the query body — plain C# returning a **materialised** result (a `List<T>`, not an open `IQueryable`). `db` exposes an `ITable<T>` per selected table; `inputs` exposes your typed node inputs:
   ```csharp
   return db.orders
       .Where(o => o.total >= inputs.MinTotal)
       .OrderBy(o => o.id)
       .ToList();
   ```

3. **Validate** — compile without persisting; diagnostics carry line/column for editor squigglies:
   ```http
   POST /api/database/linq/validate   → { success, errors[], warnings[] }
   ```

4. **Preview** — run against a throwaway `:memory:` SQLite sandbox seeded with sample rows, inside an **always-rollback** transaction (nothing you write can persist):
   ```http
   POST /api/database/linq/preview    → { rows, rowCount, durationMs, diagnostics[] }
   ```
   > ⚠️ **Preview ≠ target semantics (C10):** preview runs on SQLite, so provider-specific behaviour (Postgres types, collations, functions) is *not* reproduced. Testcontainers-backed preview against the real provider is tracked as 2.4.b.P2.

5. **Publish / compile** — compile + cache the assembly, returning the `compiledAssemblyKey` the node stores:
   ```http
   POST /api/database/linq/compile    → { compiledAssemblyKey }
   ```
   Gated to trusted authors (see security model below).

### Table typing: generated **or** plugin POCOs

Each selected table resolves an entity type via one of two paths:

- **Column-generated POCO** *(default)* — emitted from the imported `WorkflowColumnMetadata` (provider SQL type → C# type). Zero setup beyond catalog import.
- **Plugin POCO** *(preferred when present)* — a pre-registered CLR type (`ClrTypeName` + `AssemblyName`), authoritative for attributes/relations. Wins over the generated POCO when both exist.

### Typed inputs (`LinqInputs`)

Node input properties become a typed `readonly struct LinqInputs` (§8.6 Phase 1). Allow-listed scalar types (`string`/`int`/`long`/`decimal`/`double`/`bool`/`Guid`/`DateTime`/…) become strongly-typed properties; non-allow-listed types fall back to `object?` with a warning (or a hard error in strict mode).

---

## 🔒 Typed linq — security model

The typed surface runs author-supplied C#, so it ships a defence-in-depth sandbox (V1, D17). **Trusted-author gate + whitelists**; fuller isolation (process/WASM) is revisited in Phase 3.

| Control | What it does |
|---------|--------------|
| **Trusted-author gate** | `POST /compile` (and publish of linq nodes) requires the trusted-author policy. `validate`/`preview` use the standard authenticated policy. |
| **Usings allowlist** | Codegen prepends **only** `System`, `System.Linq`, `System.Collections.Generic`, `System.Threading`, `System.Threading.Tasks`, `LinqToDB`. User bodies can't declare their own usings. |
| **Forbidden-syntax walker** | Rejects `Process`/`File`/`Directory`/`Socket`/`HttpClient`/`Activator`/`Marshal`/`Assembly`, `[DllImport]`, `unsafe`, pointers, `stackalloc` — including fully-qualified reaches like `System.IO.File.Delete` (`WFLINQ1xx` diagnostics). |
| **HMAC-signed blobs** | Compiled assemblies are HMAC-SHA256 signed on write and verified on read; a tampered/swapped blob is treated as absent and never loaded. |
| **No connection strings in code** | The typed path takes only a `connectionId` — user code never sees a raw connection string, and failure surfaces never echo one. |
| **Collectible ALC, bounded** | Each distinct compiled assembly loads into **one** reused collectible `AssemblyLoadContext`; ALC count is bounded by the LRU capacity, **not** by execution count (no leak under sustained load — design §8.4.4). |
| **Forced materialisation** | Results are copied out to BCL types before the context is disposed; returning an open `IQueryable`/lazy sequence fails with a clear diagnostic (D8). |

---

## Connection management

Every module accepts **one of two** connection sources (mutually exclusive — D3):

1. **Named connection (preferred)** — `connectionId: "OrdersDb"` references a registration held by the connection registry. Credentials never appear in the workflow definition.
2. **Raw connection string (escape hatch)** — `connectionString: "..."` **plus** `provider: "postgres" | "sqlite"`.

### Registering named connections

Named connections come from two places:

- **`appsettings.json`** — bound from the `Workflow:Database` section at startup:
  ```json
  {
    "Workflow": {
      "Database": {
        "DisableRuntimeCrud": false,
        "Connections": {
          "OrdersDb": {
            "Id": "OrdersDb",
            "ProviderKey": "postgres",
            "ConnectionString": "Host=localhost;Database=orders;Username=app;Password=***",
            "DisplayName": "Orders database",
            "Enabled": true
          }
        }
      }
    }
  }
  ```

- **Runtime CRUD API** — mutable at runtime (opt-out via `DisableRuntimeCrud: true`, which makes writes return `403`):

  | Method | Route | Notes |
  |--------|-------|-------|
  | `GET` | `/api/database/connections` | Lists connections; strings **masked** (`***`) |
  | `GET` | `/api/database/connections/{id}` | Masked by default; `?reveal=true` returns plaintext |
  | `POST` | `/api/database/connections` | Upsert (`403` when `DisableRuntimeCrud`) |
  | `DELETE` | `/api/database/connections/{id}` | Delete (`204`/`404`) |

### Credentials at rest 🔒

When a persistence provider (SQLite) is configured, connection strings are **encrypted at rest** via ASP.NET Data Protection (`IConnectionStringProtector`, purpose `Workflow.Modules.Database.ConnectionString`). The registry never persists plaintext. Without a persistence provider the in-memory registry is used (config values are plain by design).

---

---

## 🧰 Raw SQL — the escape-hatch family

The four raw-SQL modules below (2.4.a) are the deliberate escape hatch (D13): vendor-specific DDL, hand-tuned queries, perf-critical bulk paths, and connections whose table catalog isn't registered yet. They're always parameterised — the binder never concatenates SQL (D7).

## Per-module reference

### 🔍 `builtin.database.query` — SELECT

Runs a parameterised SELECT and returns fully-materialised rows (D8 — no open readers escape).

| Property | Type | Default | Notes |
|----------|------|---------|-------|
| `connectionId` / `connectionString` + `provider` | string | — | Connection source (one of) |
| `query` | string | — | **Required.** Verbatim SQL — never template-expanded (D7) |
| `parameters` | map | — | Named params (name→value) |
| `timeoutSeconds` | int | `30` | Command timeout |
| `commandType` | string | `"text"` | `"storedProcedure"` deferred to 2.4.a.P1 |

**Outputs:** `rows` (`IReadOnlyList<IReadOnlyDictionary<string, object?>>`), `rowCount` (int), `columns` (`IReadOnlyList<string>`), `success` (bool), `durationMs` (long).

### ✏️ `builtin.database.execute` — INSERT/UPDATE/DELETE

| Property | Type | Default | Notes |
|----------|------|---------|-------|
| connection source | — | — | as above |
| `command` | string | — | **Required.** Verbatim SQL |
| `parameters` | map | — | Named params |
| `timeoutSeconds` | int | `30` | |
| `expectsLastInsertId` | bool | `false` | See below |

**Outputs:** `affectedRows` (int), `lastInsertId` (long?), `success` (bool), `durationMs` (long).

**`lastInsertId` is provider-aware:**
- **SQLite** — a follow-up `SELECT last_insert_rowid()` on the same connection.
- **Postgres** — read from a user-supplied `RETURNING id` clause (Q12: document-only, no auto-rewrite). If you set `expectsLastInsertId` but don't write `RETURNING`, `lastInsertId` is `null` with a logged warning.

### 💼 `builtin.database.transaction` — atomic op sequence

Runs an ordered list of operations in one transaction: commit iff **all** succeed, else rollback with the failing op's index. A SQL error returns a *clean* `success:false` (the engine routes it) — only infra failures (can't open connection) throw.

| Property | Type | Default | Notes |
|----------|------|---------|-------|
| connection source | — | — | as above |
| `operations` | array | — | **Required.** See op shapes below |
| `isolationLevel` | string | `"ReadCommitted"` | `ReadCommitted`/`RepeatableRead`/`Serializable`/`Snapshot`/`ReadUncommitted` |
| `timeoutSeconds` | int | `60` | |

**Operation shapes** (per op, `parameters` and `parameterSets` are mutually exclusive):
- **Single mode:** `{ "sql": "...", "parameters": { ... }, "expectLastInsertId": false }` — one round-trip.
- **Batch mode:** `{ "sql": "...", "parameterSets": [ { ... }, { ... } ] }` — one SQL, N parameter sets. `affectedRows = 0` for a set is **not** a failure (a `WHERE`-guard no-op); only a SQL error aborts.

**Outputs:** `success` (bool), `results` (`IReadOnlyList<DbOperationResult>` — per-op `affectedRows`/`lastInsertId`/`isBatchOp`/`batchExecutionCount`), `error` (`DbOperationError?` — `operationIndex`/`sqlState`/`message`/`batchRowIndex`), `durationMs` (long).

> **Conditional aborts:** there's no in-module DSL. Compose "abort if op N returns 0 rows" at the **workflow level** with `builtin.condition` + `builtin.throw` + `builtin.trycatch` (Phase 2.2.4), or use inline `WHERE` guards in batch mode. See the plan doc §2.4.a.3 Diagrams A–C.

### 📊 `builtin.database.bulkinsert` — efficient multi-row insert

Inserts N row-dictionaries via a hand-built batched multi-row parameterised INSERT (the `MultipleRows` shape), all wrapped in one transaction for atomicity.

| Property | Type | Default | Notes |
|----------|------|---------|-------|
| connection source | — | — | as above |
| `tableName` | string | — | **Required.** Fully-qualified preferred (`"public.orders"`) |
| `data` | array | — | **Required.** Array of row dictionaries |
| `columnMapping` | map | identity | input key → DB column |
| `batchSize` | int | `1000` | rows/statement (clamped by the provider param limit) |
| `returningColumns` | list | — | When set, collects generated columns (e.g. `["id"]`) |
| `timeoutSeconds` | int | `120` | |

**Outputs:** `insertedCount` (int), `outputRows` (rows, when `returningColumns` set), `success` (bool), `durationMs` (long).

> **Why not linq2db `BulkCopy`?** The typed `BulkCopy<T>`/`AsQueryable`/`InsertWithOutput` APIs need a compile-time entity type — which is exactly what the typed 2.4.b family generates. The dynamic escape-hatch family uses a parameterised multi-row INSERT instead, and delivers "return generated columns" via a provider-aware `RETURNING` clause (`returningColumns` → `outputRows`). The fully-typed bulk path is tracked as **2.4.a.P6**.

---

## Parameter binding

All parameter values bind through `SqlParameterBinder` — **SQL text is never string-concatenated** (D7). Supported value types:

`string`, `int`, `long`, `double`, `decimal`, `bool`, `Guid`, `DateTime`, `DateTimeOffset`, `byte[]`, and `null`.

Unsupported types throw `SqlParameterBindingException(paramName, reason)`. This is the SQL-injection firewall — every module test suite includes injection-attempt coverage.

---

## Transactions & isolation

- The transaction module opens one `IDbTransactionScope`; the scope **owns and disposes** the connection and **auto-rolls-back** on dispose if not committed.
- **SQLite isolation clamping:** `Microsoft.Data.Sqlite` only accepts `Serializable` and `ReadUncommitted`. Requested levels are clamped for SQLite (`ReadCommitted`/`RepeatableRead`/`Snapshot` → `Serializable`) with a debug log. Postgres passes levels through (`Snapshot` → `Serializable`).

---

## Provider notes (Postgres vs SQLite)

| Concern | Postgres | SQLite |
|---------|----------|--------|
| Provider key | `"postgres"` → `ProviderName.PostgreSQL15` | `"sqlite"` → `ProviderName.SQLiteMS` |
| `lastInsertId` | user writes `RETURNING id` | `SELECT last_insert_rowid()` |
| `RETURNING` clause | always available | requires SQLite ≥ 3.35 (bundled with `Microsoft.Data.Sqlite` 8.x) |
| Isolation | all levels | clamped (see above) |
| Param limit (bulk) | 65535 | ~999 / 32766 |
| `ATTACH DATABASE` | n/a | non-goal for V1 (Q13) |

---

## Security best practices 🔐

1. **Prefer named connections** over inline `connectionString` — keeps secrets out of workflow definitions.
2. **Always parameterise** — pass values via `parameters`/`parameterSets`, never interpolate into `query`/`command`.
3. **Enable persistence** so credentials are encrypted at rest via Data Protection.
4. **Set `DisableRuntimeCrud: true`** in locked-down environments to make the connection registry read-only at runtime.
5. **Mask by default** — the API masks connection strings unless `?reveal=true` (gate this behind admin auth once the API-security pass lands).

---

## Migration guide — adding a new provider

The provider set is intentionally small in V1. To add one (e.g. MySQL, SQL Server — tracked as 2.4.a.P3):

1. Add the ADO provider package to `Directory.Packages.props` and reference it from `Workflow.Modules.Database`.
2. Register the key→`ProviderName` mapping in `DefaultDbProviderRegistry` (or ship your own `IDbProviderRegistry` via DI — the registry is override-friendly, D6).
3. Add any provider-specific quirks to `SqlParameterBinder` (param marker style, type hints).
4. Add a Testcontainers fixture + the query/execute/transaction/bulkinsert test matrix.

No core enums to touch — provider keys are strings by design.

---

## Post-MVP roadmap

| Slice | What |
|-------|------|
| **2.4.a.P1** | Stored-procedure support (`commandType: "storedProcedure"`) |
| **2.4.a.P2** | Savepoints / nested transactions |
| **2.4.a.P3** | MySQL + SQL Server providers |
| **2.4.a.P4** | Connection-pool metrics + OpenTelemetry |
| **2.4.a.P5** | Streaming query results + concurrent bulk-insert pipeline |
| **2.4.a.P6** | Typed bulk insert via `AsQueryable`/`InsertWithOutput` (depends on 2.4.b) |
| **2.4.b** | ✅ **Shipped** — 🌟 Typed linq family (Roslyn) — the recommended default surface |
| **2.4.b.P1** | Typed `record LinqInputs` codegen upgrade |
| **2.4.b.P2** | Testcontainers-backed preview (real target provider) |
| **2.4.b.P3** | Catalog versioned auto-discovery |
| **2.4.b.P4** | `Workflow.UI` Monaco editor panel |

---

> 🌸 *uwu — reach for typed linq first; keep the raw-SQL family in your back pocket for the gnarly vendor-specific bits~* 💖

