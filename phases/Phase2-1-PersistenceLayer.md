# Phase 2.1: Persistence Layer (Weeks 7-9) 🗄️

Made with 💖 by Ami-Chan! UwU ✨

[Back to Phase 2](Phase2-CoreFeatures.md) | [All Phases](README.md)

---

## Overview

Phase 2.1 introduces the pluggable persistence layer — the backbone that lets workflows survive crashes, tracks every execution, and preserves variable history. This phase has three persistence providers (PostgreSQL, NATS KV, S3) wired behind clean interfaces so the engine never talks to storage directly~ 💖

Providers are **composable**: you can use PostgreSQL for workflows + execution history while routing variables to NATS KV. A `CompositePersistenceProvider` handles the routing.

**Timeline:** 3 weeks (Weeks 7-9)
**Complexity:** 🔴 High — external infrastructure, migrations, concurrent access

> **CopilotNote:** Phase 2.1 has real infrastructure dependencies (Postgres, NATS, MinIO). Tests use
> TestContainers to spin up Docker instances automatically. Make sure Docker Desktop is running before
> running the integration tests! The unit tests (interface contract tests) work without Docker~ 🐳

### Confirmed Design Decisions ✅

| # | Decision |
|---|----------|
| **Q1 Soft Delete** | `DeleteAsync` → soft delete (`is_active = false`). `PurgeAsync` added to `IWorkflowRepository` for hard delete. Preserves FK integrity. |
| **Q2 Null Variables** | `SetVariableAsync(null)` = valid null-valued entry. Explicit `DeleteVariableAsync` is the only way to remove a variable. History records null entries. |
| **Q3 Composition** | Providers are **composable**. `CompositePersistenceProvider` routes each interface to a configured sub-provider (e.g. Postgres workflows + NATS variables). |
| **Q4 Postgres Version** | Minimum **PostgreSQL 15**. Use `jsonb` operators, `text[]` arrays, `gen_random_uuid()`, and expression indexes freely. |
| **Q5 Awaited Writes** | All `IExecutionHistoryRepository` calls from `WorkflowExecutor` are **awaited** — reliability over raw throughput. Akka `PipeToSelf` used to avoid blocking the actor mailbox. |

---

## Pre-Existing Work (from Phase 1) ✅

| Component | File | Status |
|-----------|------|--------|
| `InMemoryExecutionStateStore` | `Workflow.Engine/Services/InMemoryExecutionStateStore.cs` | ✅ Used in engine tests |
| `WorkflowExecutionContext` | `Workflow.Engine/Models/WorkflowExecutionContext.cs` | ✅ Full snapshot model |
| `WorkflowDefinition` | `Workflow.Core/Models/WorkflowDefinition.cs` | ✅ Record with Id, Nodes, Connections, Variables |
| `WorkflowValidator` | `Workflow.Core/Abstractions/WorkflowValidator.cs` | ✅ Validates before storage |
| `NodeExecutionState` / `ExecutionState` | `Workflow.Core/Models/` | ✅ State enums |
| Engine actor serialization | `Workflow.Engine/Serialization/` | ✅ MessagePack + JSON options |

---

## 2.1.0 Persistence Abstractions & Contracts 🔌

**Purpose:** Define the clean interfaces that all providers implement. The rest of the engine talks ONLY to these interfaces — no provider-specific code leaks into business logic~ ✨

**Complexity:** 🟢 Low

**New Project:** `Workflow.Persistence` (class library)
> CopilotNote: Create a new project `Workflow.Persistence.csproj`. Providers live in their own sub-projects:
> `Workflow.Persistence.Postgres`, `Workflow.Persistence.Nats`, `Workflow.Persistence.S3`~ 💖

### Tasks:

- [x] **Create `Workflow.Persistence` project** 📁 ✅
  - [x] Add `Workflow.Persistence.csproj` to solution
  - [x] Reference `Workflow.Core`
  - [x] StyleCop configured, builds cleanly

- [x] **Move `ExecutionState`/`NodeExecutionState` enums to `Workflow.Core`** 🔄 ✅
  - [x] New file: `Workflow.Core/Models/ExecutionState.cs`
  - [x] Removed from `Workflow.Engine/Messages/WorkflowMessages.cs`
  - [x] Updated `using` in `WorkflowExecutionContext.cs`
  - [x] Full solution builds cleanly, 395 tests passing

- [x] **Define `IPersistenceProvider`** 🔌 ✅
  - [x] New file: `Workflow.Persistence/Abstractions/IPersistenceProvider.cs`
  - [x] `InitializeAsync`, `HealthCheckAsync`, `DisposeAsync`, `ProviderName`, `IsInitialized`
  - [x] Exposes `Workflows`, `ExecutionHistory`, `Variables`, `Blobs` repository properties
  - [x] XML documentation

- [x] **Define `IWorkflowRepository`** 📋 ✅
  - [x] New file: `Workflow.Persistence/Abstractions/IWorkflowRepository.cs`
  - [x] CRUD + soft delete + `PurgeAsync` + `RestoreAsync` + `GetByIdAsync(includeDeleted)`
  - [x] Pagination, search, exists
  - [x] XML documentation

- [x] **Define `IExecutionHistoryRepository`** 📊 ✅
  - [x] New file: `Workflow.Persistence/Abstractions/IExecutionHistoryRepository.cs`
  - [x] `CreateExecutionAsync`, `UpdateExecutionStatusAsync`, `GetExecutionAsync`
  - [x] `GetExecutionsForWorkflowAsync`, `RecordNodeExecutionAsync`, `GetNodeExecutionsAsync`
  - [x] XML documentation

- [x] **Define `IVariableStore`** 💾 ✅
  - [x] New file: `Workflow.Persistence/Abstractions/IVariableStore.cs`
  - [x] `null` value = valid entry semantics documented in XML docs
  - [x] `SetVariableAsync`, `GetVariableAsync`, `GetVariableHistoryAsync`
  - [x] `DeleteVariableAsync` (hard remove), `GetAllVariablesAsync`
  - [x] XML documentation

- [x] **Define `IBlobStore`** 🗃️ ✅
  - [x] New file: `Workflow.Persistence/Abstractions/IBlobStore.cs`
  - [x] `PutAsync`, `GetAsync`, `DeleteAsync`, `ExistsAsync`, `GeneratePresignedUrlAsync`
  - [x] XML documentation

- [x] **Define supporting DTOs and value objects** 📦 ✅
  - [x] `ExecutionRecord.cs` — full record with state, timestamps, inputs/outputs, error
  - [x] `NodeExecutionRecord.cs` — per-node execution tracking
  - [x] `VariableScope.cs` — `Global`/`ForWorkflow`/`ForExecution` factories + `VariableScopeKind` enum
  - [x] `VariableEntry.cs` — versioned entry with null-is-valid semantics
  - [x] `PagedResult.cs` — generic paged result with `HasNextPage`/`HasPreviousPage`/`TotalPages`
  - [x] `Pagination.cs` — clamped to max 200, `Skip` computed property
  - [x] `WorkflowFilter.cs` — name, active, tags, date range filter
  - [x] `ExecutionFilter.cs` — states, date range filter
  - [x] `HealthCheckResult.cs` — healthy/unhealthy with latency and details

- [x] **Define `IPersistenceProviderFactory`** 🏭 ✅
  - [x] New file: `Workflow.Persistence/Abstractions/IPersistenceProviderFactory.cs`
  - [x] `PersistenceConfiguration.cs` — `ProviderName`, `ConnectionString`, `Options`, `Validate()`

- [x] **Define `CompositePersistenceConfiguration`** 🔀 ✅
  - [x] New file: `Workflow.Persistence/Composite/CompositePersistenceConfiguration.cs`
  - [x] Per-interface provider routing with `Effective*` fallback properties

- [x] **Implement `CompositePersistenceProvider`** 🔀 ✅
  - [x] New file: `Workflow.Persistence/Composite/CompositePersistenceProvider.cs`
  - [x] Routes each interface to configured sub-provider
  - [x] `InitializeAsync` — parallel init of unique providers (deduped by reference)
  - [x] `HealthCheckAsync` — aggregates health from all sub-providers
  - [x] `DisposeAsync` — disposes all unique providers

- [x] **DI extension methods** 💉 ✅
  - [x] New file: `Workflow.Persistence/ServiceCollectionExtensions.cs`
  - [x] `AddWorkflowPersistence(IPersistenceProvider)` — single provider
  - [x] `AddWorkflowPersistence(CompositePersistenceConfiguration, IPersistenceProviderFactory)` — composite

**Tests (22/22 passing):** → `Workflow.Tests/Persistence/AbstractionTests.cs` ✅
- [x] `PagedResult.HasNextPage` true when more items
- [x] `PagedResult.HasNextPage` false on last page
- [x] `PagedResult.TotalPages` computes correctly
- [x] `PagedResult.Empty` has zero items
- [x] `VariableScope.Global` has Global kind
- [x] `VariableScope.Global` is same instance (singleton-like)
- [x] `VariableScope.ForWorkflow(id)` carries WorkflowId
- [x] `VariableScope.ForExecution(id)` carries ExecutionId
- [x] `Pagination` clamps PageSize to max (200)
- [x] `Pagination` clamps PageSize to min (1)
- [x] `Pagination.Skip` computes correctly
- [x] `Pagination.Default` is page 1, size 50
- [x] `PersistenceConfiguration.Validate` throws on empty ProviderName
- [x] `PersistenceConfiguration.Validate` succeeds when valid
- [x] `HealthCheckResult` healthy has null error
- [x] `HealthCheckResult` unhealthy has error message
- [x] `VariableEntry { Value = null }` is distinct from null (not found)
- [x] `CompositePersistenceProvider` routes Workflows to configured provider
- [x] `CompositePersistenceProvider.HealthCheckAsync` aggregates unhealthy
- [x] `CompositePersistenceProvider.InitializeAsync` dedupes same instance
- [x] `CompositePersistenceProvider.ProviderName` is "composite"
- [x] `ExecutionRecord` JSON round-trip preserves fields

---

## 2.1.1 SQLite Persistence Provider 🪶

**Purpose:** A lightweight SQLite-backed implementation of all persistence interfaces. By using SQLite's **`:memory:`** mode for tests and a file path for local dev, we get full SQL coverage (real migrations, real queries) with zero infrastructure setup. The repository and migration patterns established here are then **directly reused** by the PostgreSQL provider (2.1.2) — it becomes a simple swap of the connection provider and a few SQL dialect differences~ 💖✨

**Complexity:** 🟡 Low-Medium

**New Project:** `Workflow.Persistence.Sqlite`

> **CopilotNote:** The SQLite schema intentionally mirrors the Postgres schema (same table names,
> same column names) but drops Postgres-specific types (`jsonb` → `TEXT`, `text[]` → `TEXT`,
> `bigserial` → `INTEGER PRIMARY KEY AUTOINCREMENT`). This keeps the FluentMigrator migrations
> and Linq2Db entity classes copy-paste-upgradeable to Postgres in 2.1.2~ 🐘
>
> For tests use `"Data Source=:memory:"` — SQLite in-memory databases are fast and isolated
> per-connection. No Docker, no temp files, no cleanup needed~ 🧪

### Design Decisions 🔧

| Concern | SQLite choice | Postgres equivalent (2.1.2) |
|---------|--------------|---------------------------|
| JSON columns | `TEXT` with `System.Text.Json` serialization | `jsonb` |
| Arrays (tags) | `TEXT` comma-joined | `text[]` |
| Auto-increment PK | `INTEGER PRIMARY KEY AUTOINCREMENT` | `BIGSERIAL` |
| UUID PK | `TEXT` (stored as string) | `UUID` |
| Concurrent access | WAL mode (`PRAGMA journal_mode=WAL`) | native |
| In-memory testing | `"Data Source=:memory:;Cache=Shared;Mode=Memory"` | Testcontainers |

### Tasks:

- [ ] **Create `Workflow.Persistence.Sqlite` project** 📁
  - [ ] Add project to solution
  - [ ] Reference `Workflow.Persistence`
  - [ ] NuGet packages:
    - [ ] `linq2db` (4.x)
    - [ ] `linq2db.SQLite`
    - [ ] `Microsoft.Data.Sqlite` (8.x)
    - [ ] `FluentMigrator`
    - [ ] `FluentMigrator.Runner`
    - [ ] `FluentMigrator.Runner.SQLite`
  - [ ] Add to `Directory.Packages.props`

- [ ] **Design and implement database schema migrations** 🔄
  - [ ] New file: `Workflow.Persistence.Sqlite/Migrations/Migration_001_InitialSchema.cs`
    - [ ] `workflows` table
      - [ ] `id` (TEXT, primary key) — UUID as string
      - [ ] `name` (TEXT NOT NULL)
      - [ ] `description` (TEXT)
      - [ ] `definition` (TEXT NOT NULL) — JSON blob
      - [ ] `version` (TEXT NOT NULL)
      - [ ] `is_active` (INTEGER NOT NULL DEFAULT 1) — boolean as 0/1
      - [ ] `created_at` (TEXT NOT NULL) — ISO-8601 DateTimeOffset
      - [ ] `updated_at` (TEXT NOT NULL)
      - [ ] `tags` (TEXT) — comma-joined, nullable
      - [ ] `metadata` (TEXT) — JSON blob, nullable
    - [ ] `executions` table
      - [ ] `id` (TEXT, primary key)
      - [ ] `workflow_id` (TEXT NOT NULL)
      - [ ] `state` (TEXT NOT NULL) — `ExecutionState` enum name
      - [ ] `started_at` (TEXT NOT NULL)
      - [ ] `completed_at` (TEXT)
      - [ ] `inputs` (TEXT) — JSON
      - [ ] `outputs` (TEXT) — JSON
      - [ ] `error` (TEXT)
      - [ ] `triggered_by` (TEXT)
    - [ ] `execution_nodes` table
      - [ ] `id` (INTEGER PRIMARY KEY AUTOINCREMENT)
      - [ ] `execution_id` (TEXT NOT NULL)
      - [ ] `node_id` (TEXT NOT NULL)
      - [ ] `state` (TEXT NOT NULL)
      - [ ] `started_at` (TEXT NOT NULL)
      - [ ] `completed_at` (TEXT)
      - [ ] `inputs` (TEXT) — JSON
      - [ ] `outputs` (TEXT) — JSON
      - [ ] `error` (TEXT)
      - [ ] `duration_ms` (INTEGER)
    - [ ] `variables` table
      - [ ] `id` (INTEGER PRIMARY KEY AUTOINCREMENT)
      - [ ] `scope_kind` (TEXT NOT NULL) — `VariableScopeKind` enum name
      - [ ] `scope_id` (TEXT) — WorkflowId or ExecutionId, nullable for Global
      - [ ] `name` (TEXT NOT NULL)
      - [ ] `value` (TEXT) — JSON, null for explicit null entry
      - [ ] `value_type` (TEXT NOT NULL)
      - [ ] `version` (INTEGER NOT NULL)
      - [ ] `created_at` (TEXT NOT NULL)
      - [ ] `updated_at` (TEXT NOT NULL)
  - [ ] New file: `Workflow.Persistence.Sqlite/Migrations/Migration_002_AddIndexes.cs`
    - [ ] Index: `executions(workflow_id)`
    - [ ] Index: `executions(state)`
    - [ ] Index: `executions(started_at)`
    - [ ] Index: `execution_nodes(execution_id)`
    - [ ] Unique index: `variables(scope_kind, scope_id, name, version)`
    - [ ] Index: `workflows(name)`
  - [ ] New file: `Workflow.Persistence.Sqlite/SqliteMigrationRunner.cs`
    - [ ] `RunMigrationsAsync(string connectionString)`
    - [ ] `RollbackLastMigrationAsync(string connectionString)`
    - [ ] Enable WAL mode after migration (`PRAGMA journal_mode=WAL`)

- [ ] **Shared base for SQL providers** 🧱
  - [ ] New file: `Workflow.Persistence.Sqlite/Data/WorkflowDataConnection.cs`
    - [ ] Extends `DataConnection` (`linq2db`)
    - [ ] `ITable<WorkflowEntity> Workflows { get; }`
    - [ ] `ITable<ExecutionEntity> Executions { get; }`
    - [ ] `ITable<ExecutionNodeEntity> ExecutionNodes { get; }`
    - [ ] `ITable<VariableEntity> Variables { get; }`
  - [ ] New file: `Workflow.Persistence.Sqlite/Data/Entities/WorkflowEntity.cs`
    - [ ] `[PrimaryKey] string Id`, `string Name`, `string? Description`
    - [ ] `string Definition` (JSON), `string Version`
    - [ ] `bool IsActive`, `string CreatedAt`, `string UpdatedAt`
    - [ ] `string? Tags`, `string? Metadata`
  - [ ] New file: `Workflow.Persistence.Sqlite/Data/Entities/ExecutionEntity.cs`
  - [ ] New file: `Workflow.Persistence.Sqlite/Data/Entities/ExecutionNodeEntity.cs`
  - [ ] New file: `Workflow.Persistence.Sqlite/Data/Entities/VariableEntity.cs`
  - [ ] New file: `Workflow.Persistence.Sqlite/Data/WorkflowDataConnectionFactory.cs`
    - [ ] Creates `DataConnection` from connection string
    - [ ] Configures `SQLiteTools.ResolveSQLite()`

- [ ] **Implement `SqliteWorkflowRepository`** 📋
  - [ ] New file: `Workflow.Persistence.Sqlite/Repositories/SqliteWorkflowRepository.cs`
  - [ ] `CreateAsync` — insert, ID = `Guid.NewGuid().ToString()`
  - [ ] `UpdateAsync` — update definition + updated_at; throw `InvalidOperationException` if not found
  - [ ] `DeleteAsync` — soft delete (`is_active = 0`)
  - [ ] `PurgeAsync` — hard delete row (no FK cascade in SQLite by default — delete related records first)
  - [ ] `RestoreAsync` — set `is_active = 1`
  - [ ] `GetByIdAsync(id)` — WHERE `is_active = 1`; deserialise definition JSON
  - [ ] `GetByIdAsync(id, includeDeleted)` — no `is_active` filter
  - [ ] `GetAllAsync` — LINQ filter on name (LIKE), is_active, tags (INSTR); Skip/Take for pagination
  - [ ] `SearchAsync` — LIKE `%query%` on name and description
  - [ ] JSON helpers: `WorkflowDefinition ↔ string` via `System.Text.Json`

- [ ] **Implement `SqliteExecutionHistoryRepository`** 📊
  - [ ] New file: `Workflow.Persistence.Sqlite/Repositories/SqliteExecutionHistoryRepository.cs`
  - [ ] `CreateExecutionAsync` — insert execution record
  - [ ] `UpdateExecutionStatusAsync` — update state + completed_at + error
  - [ ] `GetExecutionAsync` — by id
  - [ ] `GetExecutionsForWorkflowAsync` — filter by workflow_id + state + date range; paginate
  - [ ] `RecordNodeExecutionAsync` — upsert by (execution_id, node_id)
  - [ ] `GetNodeExecutionsAsync` — all nodes for execution

- [ ] **Implement `SqliteVariableStore`** 💾
  - [ ] New file: `Workflow.Persistence.Sqlite/Repositories/SqliteVariableStore.cs`
  - [ ] `SetVariableAsync` — INSERT new row with `version = MAX(version) + 1` (or 1 if first)
  - [ ] `GetVariableAsync(version: null)` — SELECT WHERE `version = MAX(version)` for scope+name
  - [ ] `GetVariableAsync(version: n)` — SELECT WHERE `version = n`
  - [ ] `GetVariableHistoryAsync` — all rows for scope+name ORDER BY version ASC
  - [ ] `DeleteVariableAsync` — DELETE all rows WHERE scope+name
  - [ ] `GetAllVariablesAsync` — latest version per name via subquery; includes null-valued rows
  - [ ] JSON serialization for `value` column: `object? ↔ string?` via `System.Text.Json`
  - [ ] Note: `null` value → stored as `NULL` in value column; `value_type = "null"` — distinct from "not found"

- [ ] **Implement `SqlitePersistenceProvider`** 🪶
  - [ ] New file: `Workflow.Persistence.Sqlite/SqlitePersistenceProvider.cs`
  - [ ] `ProviderName = "sqlite"`
  - [ ] `InitializeAsync` — run migrations; enable WAL
  - [ ] `HealthCheckAsync` — execute `SELECT 1`; measure latency
  - [ ] Exposes `IWorkflowRepository`, `IExecutionHistoryRepository`, `IVariableStore`
  - [ ] `Blobs` → `null` (SQLite is not suitable for large blob storage)
  - [ ] DI registration helper: `AddSqlitePersistence(string connectionString)`
  - [ ] In-memory helper: `AddSqlitePersistenceInMemory()` — uses `"Data Source=:memory:;Cache=Shared;Mode=Memory"`

**Tests (~25):** → `Workflow.Tests/Persistence/SqliteProviderTests.cs`
> Uses `":memory:"` connection string — **no Docker required!** Runs on every machine~ 🧪
- [ ] Provider initialises (migrations run) without error
- [ ] Provider health check returns healthy
- [ ] **Workflow CRUD:**
  - [ ] `CreateAsync` → `GetByIdAsync` round-trip preserves all fields
  - [ ] `UpdateAsync` changes definition
  - [ ] `DeleteAsync` soft-deletes — `GetByIdAsync` returns null
  - [ ] `GetByIdAsync(id, includeDeleted: true)` returns soft-deleted workflow
  - [ ] `PurgeAsync` removes completely — `GetByIdAsync(includeDeleted: true)` returns null
  - [ ] `RestoreAsync` brings back soft-deleted workflow
  - [ ] `GetAllAsync` with `IsActive = true` excludes soft-deleted
  - [ ] `SearchAsync` case-insensitive substring match
  - [ ] Pagination: page 1 and page 2 return correct items
- [ ] **Execution history:**
  - [ ] `CreateExecutionAsync` → `GetExecutionAsync` round-trip
  - [ ] `UpdateExecutionStatusAsync` Running → Completed sets completed_at
  - [ ] `RecordNodeExecutionAsync` then `GetNodeExecutionsAsync`
  - [ ] Execution filter by state
- [ ] **Variable store:**
  - [ ] `Set("x", "hello")` creates version 1
  - [ ] `Set("x", "world")` creates version 2; `Get` returns `"world"`
  - [ ] `Set("x", null)` creates version 3; `Get` returns `VariableEntry { Value = null }` (not null!)
  - [ ] `Get(version: 1)` returns `"hello"`
  - [ ] `GetHistory` returns 3 entries ordered
  - [ ] `DeleteVariableAsync` removes all; subsequent `Get` returns null (not found)
  - [ ] `GetAllVariablesAsync` includes null-valued variable
  - [ ] Global, Workflow, Execution scopes isolated

---

## 2.1.2 PostgreSQL Persistence Provider 🐘

**Purpose:** Production-grade PostgreSQL persistence. Inherits the same migration structure, entity mappings, and repository pattern from the SQLite provider (2.1.1) — the key additions are Postgres-specific SQL types (`jsonb`, `text[]`, `UUID`), connection pooling via Npgsql, and Postgres-optimised index strategies~ 🗄️

**Complexity:** 🟡 Medium *(reduced from 🔴 High — SQLite provider establishes the pattern)*

**Target:** PostgreSQL 15+ (uses `jsonb` operators, `text[]` arrays, `gen_random_uuid()`, expression indexes)

**New Project:** `Workflow.Persistence.Postgres`

> **CopilotNote:** Most of the hard work is done in 2.1.1. For Postgres, the main changes are:
> - Swap `Microsoft.Data.Sqlite` → `Npgsql` + `linq2db.PostgreSQL`
> - Replace `TEXT` JSON columns with `jsonb` (enables GIN indexes and `@>` operator)
> - Replace `TEXT` tag columns with `text[]` (enables `@>` array containment queries)
> - Replace `INTEGER AUTOINCREMENT` with `BIGSERIAL`
> - Replace `TEXT` UUID columns with native `UUID` type
> - Use `Testcontainers.PostgreSql` for integration tests (requires Docker)~ 🐳

### What changes vs. SQLite (2.1.1)

| Concern | SQLite (2.1.1) | Postgres (2.1.2) |
|---------|---------------|-----------------|
| JSON columns | `TEXT` | `jsonb` |
| Tag arrays | `TEXT` comma-joined | `text[]` with `@>` |
| UUID type | `TEXT` | `UUID` |
| Auto PK | `AUTOINCREMENT` | `BIGSERIAL` |
| Tag search | `INSTR` | `@>` array operator |
| Concurrent init | WAL pragma | N/A (Postgres native) |
| Tests | `:memory:` (no Docker) | Testcontainers `postgres:15-alpine` |

### Tasks:

- [ ] **Create `Workflow.Persistence.Postgres` project** 📁
  - [ ] Add project to solution
  - [ ] NuGet packages:
    - [ ] `linq2db` (4.x)
    - [ ] `linq2db.PostgreSQL`
    - [ ] `Npgsql` (8.x)
    - [ ] `FluentMigrator`
    - [ ] `FluentMigrator.Runner`
    - [ ] `FluentMigrator.Runner.Postgres`

- [ ] **Design and implement database schema migrations** 🔄
  - [ ] New file: `Workflow.Persistence.Postgres/Migrations/Migration_001_InitialSchema.cs`
    - [ ] `workflows` table — id, name, description, definition (jsonb), version, is_active, created_at, updated_at, tags (text[]), metadata (jsonb)
    - [ ] `executions` table — id, workflow_id (FK), status, started_at, completed_at, inputs (jsonb), outputs (jsonb), error (jsonb), triggered_by
    - [ ] `execution_nodes` table — id (bigserial), execution_id (FK), node_id, status, started_at, completed_at, inputs (jsonb), outputs (jsonb), error (jsonb), duration_ms
    - [ ] `variables` table — id (bigserial), workflow_id, execution_id, name, value (jsonb), value_type, version (int), created_at, updated_at
    - [ ] `variable_history` table — id (bigserial), variable_id (FK), old_value (jsonb), new_value (jsonb), changed_at, changed_by
  - [ ] New file: `Workflow.Persistence.Postgres/Migrations/Migration_002_AddIndexes.cs`
    - [ ] Index: `executions.workflow_id`
    - [ ] Index: `executions.status`
    - [ ] Index: `executions.started_at`
    - [ ] Index: `execution_nodes.execution_id`
    - [ ] Unique index: `variables.(execution_id, name, version)`
    - [ ] Index: `workflows.name` (for search)
  - [ ] New file: `Workflow.Persistence.Postgres/MigrationRunner.cs`
    - [ ] `RunMigrationsAsync(string connectionString)`
    - [ ] `RollbackLastMigrationAsync(string connectionString)`
    - [ ] Called during `IPersistenceProvider.InitializeAsync()`

- [ ] **Implement Linq2Db data context and entity mappings** 🗺️
  - [ ] New file: `Workflow.Persistence.Postgres/Data/WorkflowDataConnection.cs`
    - [ ] Extends `DataConnection`
    - [ ] `ITable<WorkflowEntity> Workflows { get; }`
    - [ ] `ITable<ExecutionEntity> Executions { get; }`
    - [ ] `ITable<ExecutionNodeEntity> ExecutionNodes { get; }`
    - [ ] `ITable<VariableEntity> Variables { get; }`
    - [ ] `ITable<VariableHistoryEntity> VariableHistory { get; }`
  - [ ] New file: `Workflow.Persistence.Postgres/Data/Entities/WorkflowEntity.cs`
    - [ ] Maps 1:1 to `workflows` table
    - [ ] `Definition` stored as `string` (JSON)
  - [ ] New file: `Workflow.Persistence.Postgres/Data/Entities/ExecutionEntity.cs`
  - [ ] New file: `Workflow.Persistence.Postgres/Data/Entities/ExecutionNodeEntity.cs`
  - [ ] New file: `Workflow.Persistence.Postgres/Data/Entities/VariableEntity.cs`
  - [ ] New file: `Workflow.Persistence.Postgres/Data/Entities/VariableHistoryEntity.cs`
  - [ ] New file: `Workflow.Persistence.Postgres/Data/WorkflowDataConnectionFactory.cs`
    - [ ] Creates connections from connection string
    - [ ] Configures connection pooling

- [ ] **Implement `PostgresWorkflowRepository`** 📋
  - [ ] New file: `Workflow.Persistence.Postgres/Repositories/PostgresWorkflowRepository.cs`
  - [ ] `CreateAsync` — serialise `WorkflowDefinition` to JSON, insert
  - [ ] `UpdateAsync` — update definition + updated_at, validate exists first; optimistic concurrency via `updated_at`
  - [ ] `DeleteAsync` — **soft delete**: set `is_active = false`
  - [ ] `PurgeAsync` — **hard delete**: delete row + cascade to FK records
  - [ ] `RestoreAsync` — set `is_active = true`
  - [ ] `GetByIdAsync(id)` — WHERE `is_active = true` by default
  - [ ] `GetByIdAsync(id, includeDeleted: true)` — no `is_active` filter
  - [ ] `GetAllAsync` — with filter (name ILIKE, is_active, tags @>) + pagination
  - [ ] `SearchAsync` — case-insensitive ILIKE on name/description
  - [ ] Add JSON serialization helpers for `WorkflowDefinition`

- [ ] **Implement `PostgresExecutionHistoryRepository`** 📊
  - [ ] New file: `Workflow.Persistence.Postgres/Repositories/PostgresExecutionHistoryRepository.cs`
  - [ ] `CreateExecutionAsync` — insert new execution record
  - [ ] `UpdateExecutionStatusAsync` — update status + timestamps atomically
  - [ ] `GetExecutionAsync` — with node executions eager-loaded
  - [ ] `GetExecutionsForWorkflowAsync` — filtered + paginated
  - [ ] `RecordNodeExecutionAsync` — insert or upsert node record
  - [ ] `GetNodeExecutionsAsync` — all nodes for an execution

- [ ] **Implement `PostgresVariableStore`** 💾
  - [ ] New file: `Workflow.Persistence.Postgres/Repositories/PostgresVariableStore.cs`
  - [ ] `SetVariableAsync` — insert new version row, auto-increment `version` field; **null value is stored as SQL NULL** in `value` column (valid versioned entry)
  - [ ] `GetVariableAsync(version: null)` — SELECT latest (MAX version); returns `VariableEntry { Value = null }` when stored as null — NOT `null`
  - [ ] `GetVariableAsync(version: n)` — get specific version row
  - [ ] `GetVariableHistoryAsync` — all versions ordered by version ASC; includes null-valued rows
  - [ ] `DeleteVariableAsync` — hard delete: remove all rows for `(scope, name)` from both `variables` and `variable_history`
  - [ ] `GetAllVariablesAsync` — latest version per variable in scope via window function `ROW_NUMBER() OVER (PARTITION BY name ORDER BY version DESC)`; includes null-valued entries

- [ ] **Implement `PostgresPersistenceProvider`** 🐘
  - [ ] New file: `Workflow.Persistence.Postgres/PostgresPersistenceProvider.cs`
  - [ ] `InitializeAsync` — run migrations via `MigrationRunner`
  - [ ] `HealthCheckAsync` — ping via `SELECT 1` with timeout
  - [ ] Exposes `IWorkflowRepository`, `IExecutionHistoryRepository`, `IVariableStore`
  - [ ] DI registration: `AddPostgresPersistence(string connectionString)`

**Tests (~35):** → `Workflow.Tests/Persistence/PostgresProviderTests.cs`
> Uses `Testcontainers.PostgreSql` — requires Docker. Tests marked `[Trait("Category", "Integration")]`
- [ ] Provider initializes (migrations run) without error
- [ ] Provider health check returns healthy on live DB
- [ ] Provider health check returns unhealthy on bad connection string
- [ ] **Workflow CRUD:**
  - [ ] Create → GetById round-trip preserves all fields
  - [ ] Update changes definition + updated_at
  - [ ] Delete removes (or soft-deletes) workflow
  - [ ] GetAll with filter: by name
  - [ ] GetAll with filter: is_active = true only
  - [ ] GetAll with filter: by tags
  - [ ] Pagination: page 1 of 2 returns correct items
  - [ ] Pagination: page 2 returns remainder
  - [ ] Search by name substring (case-insensitive)
  - [ ] Exists returns true for created workflow
  - [ ] Exists returns false for random UUID
- [ ] **Execution history:**
  - [ ] Create execution → GetExecution round-trip
  - [ ] Update status Pending → Running → Completed
  - [ ] Record node execution, then query it
  - [ ] GetExecutionsForWorkflow with state filter
  - [ ] Date range filter on GetExecutions
  - [ ] Pagination on execution list
- [ ] **Variable store:**
  - [ ] Set creates version 1
  - [ ] Set again creates version 2
  - [ ] Get latest returns version 2 value
  - [ ] Get version 1 returns original value
  - [ ] Get history returns both versions ordered
  - [ ] Delete sets sentinel, GetVariable returns null
  - [ ] GetAll returns latest version per variable in scope
  - [ ] Global, Workflow, Execution scopes are isolated
- [ ] Concurrent writes (5 parallel Set calls) don't corrupt versions
- [ ] Transaction: failed second insert rolls back first
- [ ] Migration rollback restores previous schema

---

## 2.1.3 NATS KV Persistence Provider 🚀

**Purpose:** Lightweight, fast persistence using NATS JetStream Key-Value store. Best for distributed/cloud scenarios where latency matters~ ⚡

**Complexity:** 🟡 Medium

**New Project:** `Workflow.Persistence.Nats`

### Tasks:

- [ ] **Create `Workflow.Persistence.Nats` project** 📁
  - [ ] NuGet: `NATS.Client.JetStream` (3.x)

- [ ] **Implement NATS connection management** 🔗
  - [ ] New file: `Workflow.Persistence.Nats/NatsConnectionManager.cs`
    - [ ] Connection string parsing (nats://user:pass@host:4222)
    - [ ] TLS configuration
    - [ ] Reconnect logic with exponential backoff
    - [ ] Connection state events

- [ ] **Implement `NatsWorkflowRepository`** 📋
  - [ ] New file: `Workflow.Persistence.Nats/Repositories/NatsWorkflowRepository.cs`
  - [ ] KV bucket: `WF_WORKFLOWS`
  - [ ] Key pattern: `{id}` (UUID)
  - [ ] Value: JSON-serialised `WorkflowDefinition`
  - [ ] `CreateAsync` — Put with revision 0 (create-only)
  - [ ] `UpdateAsync` — Put with expected revision (optimistic concurrency)
  - [ ] `DeleteAsync` — Delete key (NATS KV delete is a tombstone)
  - [ ] `GetByIdAsync` — Get + deserialise
  - [ ] `GetAllAsync` — Keys() + batch Get (filter in-memory)
  - [ ] Search: in-memory substring filter on loaded definitions

- [ ] **Implement `NatsExecutionHistoryRepository`** 📊
  - [ ] New file: `Workflow.Persistence.Nats/Repositories/NatsExecutionHistoryRepository.cs`
  - [ ] KV bucket: `WF_EXECUTIONS` for current state
  - [ ] JetStream stream: `WF_EXECUTION_EVENTS` for history
  - [ ] Key pattern: `exec:{executionId}`
  - [ ] Publish execution events to stream for audit trail
  - [ ] Node records: KV bucket `WF_EXEC_NODES`, key: `{executionId}:{nodeId}`

- [ ] **Implement `NatsVariableStore`** 💾
  - [ ] New file: `Workflow.Persistence.Nats/Repositories/NatsVariableStore.cs`
  - [ ] KV bucket: `WF_VARIABLES` with history enabled (MaxHistory = 100)
  - [ ] Key pattern: `{scope_kind}:{scope_id}:{name}` (or `global:{name}`)
  - [ ] NATS KV built-in history via `GetAllAsync(key)` with revisions
  - [ ] `GetVariableAsync(version: n)` — fetch specific revision

- [ ] **Implement `NatsPersistenceProvider`** 🚀
  - [ ] New file: `Workflow.Persistence.Nats/NatsPersistenceProvider.cs`
  - [ ] `InitializeAsync` — create KV buckets + streams if missing
  - [ ] `HealthCheckAsync` — ping connection status
  - [ ] DI registration: `AddNatsPersistence(string natsUrl)`

**Tests (~15):** → `Workflow.Tests/Persistence/NatsProviderTests.cs`
> Uses `Testcontainers` with NATS image — marked `[Trait("Category", "Integration")]`
- [ ] Provider initialises (creates buckets)
- [ ] Workflow CRUD round-trip
- [ ] Optimistic concurrency: update with stale revision throws
- [ ] Execution create + get round-trip
- [ ] Variable set → get latest
- [ ] Variable history (3 versions) → get each by revision
- [ ] NATS watch fires on variable change
- [ ] Connection drop + reconnect recovers gracefully
- [ ] `GetAllAsync` with name filter returns correct subset
- [ ] Delete marks tombstone, subsequent Get returns null

---

## 2.1.4 S3 Blob Store ☁️

**Purpose:** Large-object storage for workflow outputs, execution logs, and binary data that shouldn't be in a relational DB or KV store~ 🗃️

**Complexity:** 🟡 Medium

**New Project:** `Workflow.Persistence.S3`

### Tasks:

- [ ] **Create `Workflow.Persistence.S3` project** 📁
  - [ ] NuGet: `AWSSDK.S3` (3.x)
  - [ ] (Optional) `Minio` for self-hosted

- [ ] **Implement S3 client configuration** ⚙️
  - [ ] New file: `Workflow.Persistence.S3/S3Configuration.cs`
    - [ ] `AccessKey`, `SecretKey`, `Region`
    - [ ] `BucketName`, `EndpointUrl` (for MinIO/local)
    - [ ] `UsePathStyle` flag (needed for MinIO)
    - [ ] `ServerSideEncryption` flag

- [ ] **Implement `S3BlobStore`** ☁️
  - [ ] New file: `Workflow.Persistence.S3/S3BlobStore.cs`
  - [ ] Implements `IBlobStore`
  - [ ] `PutAsync` — single upload for ≤5MB, multipart for larger
  - [ ] `GetAsync` — streaming download
  - [ ] `DeleteAsync` — remove object
  - [ ] `ExistsAsync` — HeadObject check
  - [ ] `GeneratePresignedUrlAsync` — signed URL with expiry
  - [ ] Key patterns:
    - [ ] Workflows: `workflows/{id}/definition.json`
    - [ ] Executions: `executions/{id}/data.json`
    - [ ] Large node outputs: `executions/{id}/nodes/{nodeId}/output.bin`
    - [ ] Logs: `executions/{id}/logs/{timestamp}.log`
  - [ ] Retry on transient S3 errors (503, 429)
  - [ ] Content-type detection from key extension

- [ ] **Implement `S3PersistenceProvider`** ☁️
  - [ ] New file: `Workflow.Persistence.S3/S3PersistenceProvider.cs`
  - [ ] `InitializeAsync` — verify bucket exists, create if missing
  - [ ] `HealthCheckAsync` — HeadBucket check
  - [ ] DI registration: `AddS3BlobStore(S3Configuration config)`

**Tests (~12):** → `Workflow.Tests/Persistence/S3BlobStoreTests.cs`
> Uses MinIO via Testcontainers — marked `[Trait("Category", "Integration")]`
- [ ] Provider initialises (bucket created)
- [ ] Put small file → Get → content matches
- [ ] Put large file (>5MB) via multipart → Get → content matches
- [ ] Exists returns true after Put
- [ ] Exists returns false before Put
- [ ] Delete removes object (Exists returns false)
- [ ] GeneratePresignedUrl returns non-empty URL
- [ ] Presigned URL expires (expired URL returns 403)
- [ ] Put with content-type preserved on Get
- [ ] HealthCheck returns unhealthy on bad endpoint

---

## 2.1.5 Engine Integration & Snapshot Migration 🔄

**Purpose:** Wire the persistence layer into the Akka actor system. `WorkflowExecutor` currently uses `InMemoryExecutionStateStore` — replace it with the pluggable `IExecutionHistoryRepository`~ ⚡

**Complexity:** 🟡 Medium

### Tasks:

- [ ] **Update `WorkflowExecutor` to use `IExecutionHistoryRepository`** ⚡
  - [ ] Resolve `IExecutionHistoryRepository` from `IServiceProvider`
  - [ ] **All repository calls are awaited** (not fire-and-forget) to guarantee reliability
  - [ ] Use Akka `PipeToSelf` pattern: wrap each async repo call in `Task.Run(...).PipeTo(Self)` to avoid blocking the actor mailbox
  - [ ] On `StartExecution`: call `CreateExecutionAsync` (PipeTo self → handle `ExecutionCreated` internal message)
  - [ ] On `NodeExecutionCompleted`: call `RecordNodeExecutionAsync`
  - [ ] On `NodeExecutionFailed`: call `RecordNodeExecutionAsync` with error
  - [ ] On `CompleteWorkflow`: call `UpdateExecutionStatusAsync(Completed)`
  - [ ] On `FailWorkflow`: call `UpdateExecutionStatusAsync(Failed)` with error
  - [ ] Fall back gracefully if repository is null (backwards compat)

- [ ] **Update `WorkflowSupervisor` to use `IWorkflowRepository`** 🗄️
  - [ ] On `CreateWorkflowInstance`: optionally validate against stored definition
  - [ ] Fall back to provided definition if repository is null

- [ ] **Update `SaveSnapshotAsync` in `WorkflowExecutor`** 💾
  - [ ] Currently writes to `InMemoryExecutionStateStore`
  - [ ] Bridge to `IExecutionHistoryRepository` when available
  - [ ] Keep `InMemoryExecutionStateStore` as fallback

- [ ] **Update DI registration in `Workflow.Api`** 💉
  - [ ] Wire `IPersistenceProvider` from appsettings `Persistence:Provider`
  - [ ] Support `"sqlite"`, `"postgres"`, `"nats"`, `"composite"` values
  - [ ] For `"sqlite"`: use `"Data Source=:memory:;Cache=Shared;Mode=Memory"` when `ConnectionString` is `":memory:"`
  - [ ] For `"composite"`: read `Persistence:Composite:Workflows`, `Persistence:Composite:Variables`, etc.
  - [ ] Register all repositories from the selected provider(s)
  - [ ] Example appsettings for composite:
    ```json
    "Persistence": {
      "Provider": "composite",
      "Composite": {
        "Workflows": { "Provider": "postgres", "ConnectionString": "..." },
        "ExecutionHistory": { "Provider": "postgres", "ConnectionString": "..." },
        "Variables": { "Provider": "nats", "ConnectionString": "nats://localhost:4222" }
      }
    }
    ```

**Tests (~10):** → `Workflow.Tests/Engine/PersistenceIntegrationTests.cs`
> Uses `SqlitePersistenceProvider` with `:memory:` connection — **no Docker required!**
- [ ] Engine with SQLite `:memory:` provider: workflow runs + execution record created
- [ ] Engine with SQLite `:memory:` provider: node completions recorded
- [ ] Engine with SQLite `:memory:` provider: failed workflow records error
- [ ] Engine without provider: runs correctly (null provider fallback)
- [ ] Execution history captures correct node states
- [ ] Execution history captures variable updates per node

---

## Phase 2.1 Deliverables ✅

**Completion Criteria:**
- [ ] `IPersistenceProvider` + all sub-interfaces defined and documented
- [ ] `InMemoryPersistenceProvider` — ~~full implementation for tests/dev~~ **replaced by SQLite `:memory:`**
- [ ] `SqlitePersistenceProvider` — lightweight file/in-memory provider; establishes SQL pattern for Postgres
- [ ] `PostgresPersistenceProvider` — production-grade with Linq2Db + migrations
- [ ] `NatsPersistenceProvider` — lightweight cloud-native option
- [ ] `S3BlobStore` — large object storage
- [ ] Engine (`WorkflowExecutor`) wired to `IExecutionHistoryRepository`
- [ ] ~118 unit + integration tests written and passing (revised up from ~107)
- [ ] All providers implement full interface contract
- [ ] DI registration for each provider
- [ ] XML documentation on all new public APIs

**New Projects:**
```
Workflow.Persistence/
  Abstractions/
    IPersistenceProvider.cs
    IWorkflowRepository.cs
    IExecutionHistoryRepository.cs
    IVariableStore.cs
    IBlobStore.cs
    IPersistenceProviderFactory.cs
  Models/
    ExecutionRecord.cs
    NodeExecutionRecord.cs
    VariableScope.cs
    VariableEntry.cs
    PagedResult.cs
    Pagination.cs
    WorkflowFilter.cs
    ExecutionFilter.cs
    HealthCheckResult.cs
  InMemory/
    InMemoryPersistenceProvider.cs
    InMemoryWorkflowRepository.cs
    InMemoryExecutionHistoryRepository.cs
    InMemoryVariableStore.cs
    InMemoryBlobStore.cs
  PersistenceConfiguration.cs
  ServiceCollectionExtensions.cs

Workflow.Persistence.Postgres/
  Data/
    WorkflowDataConnection.cs
    WorkflowDataConnectionFactory.cs
    Entities/ (5 entity files)
  Migrations/
    Migration_001_InitialSchema.cs
    Migration_002_AddIndexes.cs
  MigrationRunner.cs
  Repositories/
    PostgresWorkflowRepository.cs
    PostgresExecutionHistoryRepository.cs
    PostgresVariableStore.cs
  PostgresPersistenceProvider.cs

Workflow.Persistence.Nats/
  NatsConnectionManager.cs
  Repositories/
    NatsWorkflowRepository.cs
    NatsExecutionHistoryRepository.cs
    NatsVariableStore.cs
  NatsPersistenceProvider.cs

Workflow.Persistence.S3/
  S3Configuration.cs
  S3BlobStore.cs
  S3PersistenceProvider.cs
```

**Modified Files:**
```
Workflow.Engine/Actors/WorkflowExecutor.cs    ← wire IExecutionHistoryRepository
Workflow.Engine/Actors/WorkflowSupervisor.cs  ← optionally use IWorkflowRepository
Workflow.Api/Program.cs                       ← DI registration
Workflow.sln                                  ← add new projects
Directory.Packages.props                      ← add new NuGet packages
```

---

## New Projects Layout (updated for SQLite + composite) 🗂️
```
Workflow.Persistence/
  Abstractions/
    IPersistenceProvider.cs
    IWorkflowRepository.cs          ← + PurgeAsync, RestoreAsync, GetByIdAsync(includeDeleted)
    IExecutionHistoryRepository.cs
    IVariableStore.cs               ← + null semantics documented
    IBlobStore.cs
    IPersistenceProviderFactory.cs
  Composite/
    CompositePersistenceConfiguration.cs
    CompositePersistenceProvider.cs
  Models/
    ExecutionRecord.cs
    NodeExecutionRecord.cs
    VariableScope.cs
    VariableEntry.cs
    PagedResult.cs
    Pagination.cs
    WorkflowFilter.cs
    ExecutionFilter.cs
    HealthCheckResult.cs
  InMemory/ ← REMOVED (replaced by SQLite :memory: in 2.1.1)
  PersistenceConfiguration.cs
  ServiceCollectionExtensions.cs    ← + composite overload

Workflow.Persistence.Sqlite/        ← NEW (replaces in-memory, enables :memory: testing)
  Data/
    WorkflowDataConnection.cs       ← base pattern reused by Postgres
    WorkflowDataConnectionFactory.cs
    Entities/
      WorkflowEntity.cs
      ExecutionEntity.cs
      ExecutionNodeEntity.cs
      VariableEntity.cs
  Migrations/
    Migration_001_InitialSchema.cs  ← mirrored in Postgres (different types)
    Migration_002_AddIndexes.cs
  SqliteMigrationRunner.cs
  Repositories/
    SqliteWorkflowRepository.cs     ← pattern reused by PostgresWorkflowRepository
    SqliteExecutionHistoryRepository.cs
    SqliteVariableStore.cs
  SqlitePersistenceProvider.cs      ← AddSqlitePersistence() + AddSqlitePersistenceInMemory()

Workflow.Persistence.Postgres/
  Data/
    WorkflowDataConnection.cs       ← extends SQLite pattern, swaps provider
    WorkflowDataConnectionFactory.cs
    Entities/ (4 entity files, same structure, native Postgres types)
  Migrations/
    Migration_001_InitialSchema.cs  ← jsonb, text[], UUID, BIGSERIAL
    Migration_002_AddIndexes.cs     ← GIN, @> operator indexes
  PostgresMigrationRunner.cs
  Repositories/
    PostgresWorkflowRepository.cs
    PostgresExecutionHistoryRepository.cs
    PostgresVariableStore.cs
  PostgresPersistenceProvider.cs

Workflow.Persistence.Nats/
  NatsConnectionManager.cs
  Repositories/
    NatsWorkflowRepository.cs
    NatsExecutionHistoryRepository.cs
    NatsVariableStore.cs
  NatsPersistenceProvider.cs

Workflow.Persistence.S3/
  S3Configuration.cs
  S3BlobStore.cs
  S3PersistenceProvider.cs
```

**Modified Files:**
```
Workflow.Core/Models/ExecutionState.cs        ← moved enums here (done ✅)
Workflow.Engine/Actors/WorkflowExecutor.cs    ← wire IExecutionHistoryRepository (PipeToSelf, awaited)
Workflow.Engine/Actors/WorkflowSupervisor.cs  ← optionally use IWorkflowRepository
Workflow.Api/Program.cs                       ← DI registration incl. sqlite/composite config
Workflow.Api/appsettings.json                 ← add Persistence section
Workflow.sln                                  ← add new projects
Directory.Packages.props                      ← add linq2db, Sqlite, FluentMigrator packages
```

---

## ✅ All Clarifications Resolved

| # | Question | Answer | Impact |
|---|----------|--------|--------|
| **Q1** | Soft vs hard delete | Soft delete (`is_active`) default + `PurgeAsync` hard delete | Added `PurgeAsync`, `RestoreAsync`, `GetByIdAsync(includeDeleted)` to `IWorkflowRepository` + all implementations |
| **Q2** | `null` value = delete or valid? | Valid null-valued versioned entry; `DeleteVariableAsync` is the only delete | Updated `IVariableStore` docs, null semantics in both SQLite (`NULL` column + `"null"` type) and Postgres |
| **Q3** | Mutually exclusive or composable? | Composable via `CompositePersistenceProvider` | Added `CompositePersistenceProvider`, `CompositePersistenceConfiguration`, composite DI overload |
| **Q4** | Postgres target version | Postgres 15+ | Added to Postgres section header; use `jsonb` operators, `text[]`, `gen_random_uuid()` freely |
| **Q5** | Fire-and-forget or awaited? | **Awaited** for reliability | Added Akka `PipeToSelf` pattern to `WorkflowExecutor` wiring tasks |
| **Revised** | In-Memory → SQLite | SQLite `:memory:` replaces pure in-memory; same SQL pattern feeds into Postgres | 2.1.1 rewritten as `Workflow.Persistence.Sqlite`; Postgres complexity reduced 🔴→🟡 |

---

> 💖 **Ami's Phase 2.1 Tips (revised):**
> - 2.1.0 Abstractions ✅ Done! 22 tests passing~
> - Build 2.1.1 (SQLite) next — use `:memory:` mode for fast zero-infra testing~ 🪶
> - Build 2.1.5 (engine integration) after 2.1.1 — SQLite `:memory:` tests the full actor stack without Docker~ 🎭
> - 2.1.2 (Postgres) will be fast since 2.1.1 establishes the pattern — just swap the types~ 🐘
> - 2.1.3 (NATS) and 2.1.4 (S3) are independent — can be done in parallel~ ☁️ UwU 💖
> - Test composite routing with in-memory providers before touching real infra~ 🔀 UwU 💖

