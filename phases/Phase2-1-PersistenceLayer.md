# Phase 2.1: Persistence Layer (Weeks 7-9) ️

Made with  by Ami-Chan! UwU ✨

[Back to Phase 2](Phase2-CoreFeatures.md) | [All Phases](README.md)

---

## Overview

Phase 2.1 introduces the pluggable persistence layer — the backbone that lets workflows survive crashes, tracks every execution, and preserves variable history. This phase has three persistence providers (PostgreSQL, NATS KV, S3) wired behind clean interfaces so the engine never talks to storage directly~ 

Providers are **composable**: you can use PostgreSQL for workflows + execution history while routing variables to NATS KV. A `CompositePersistenceProvider` handles the routing.

**Timeline:** 3 weeks (Weeks 7-9)
**Complexity:**  High — external infrastructure, migrations, concurrent access

> **CopilotNote:** Phase 2.1 has real infrastructure dependencies (Postgres, NATS, MinIO). Tests use
> TestContainers to spin up Docker instances automatically. Make sure Docker Desktop is running before
> running the integration tests! The unit tests (interface contract tests) work without Docker~ 

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

## 2.1.0 Persistence Abstractions & Contracts 

**Purpose:** Define the clean interfaces that all providers implement. The rest of the engine talks ONLY to these interfaces — no provider-specific code leaks into business logic~ ✨

**Complexity:**  Low

**New Project:** `Workflow.Persistence` (class library)
> CopilotNote: Create a new project `Workflow.Persistence.csproj`. Providers live in their own sub-projects:
> `Workflow.Persistence.Postgres`, `Workflow.Persistence.Nats`, `Workflow.Persistence.S3`~ 

### Tasks:

- [x] **Create `Workflow.Persistence` project**  ✅
  - [x] Add `Workflow.Persistence.csproj` to solution
  - [x] Reference `Workflow.Core`
  - [x] StyleCop configured, builds cleanly

- [x] **Move `ExecutionState`/`NodeExecutionState` enums to `Workflow.Core`**  ✅
  - [x] New file: `Workflow.Core/Models/ExecutionState.cs`
  - [x] Removed from `Workflow.Engine/Messages/WorkflowMessages.cs`
  - [x] Updated `using` in `WorkflowExecutionContext.cs`
  - [x] Full solution builds cleanly, 395 tests passing

- [x] **Define `IPersistenceProvider`**  ✅
  - [x] New file: `Workflow.Persistence/Abstractions/IPersistenceProvider.cs`
  - [x] `InitializeAsync`, `HealthCheckAsync`, `DisposeAsync`, `ProviderName`, `IsInitialized`
  - [x] Exposes `Workflows`, `ExecutionHistory`, `Variables`, `Blobs` repository properties
  - [x] XML documentation

- [x] **Define `IWorkflowRepository`**  ✅
  - [x] New file: `Workflow.Persistence/Abstractions/IWorkflowRepository.cs`
  - [x] CRUD + soft delete + `PurgeAsync` + `RestoreAsync` + `GetByIdAsync(includeDeleted)`
  - [x] Pagination, search, exists
  - [x] XML documentation

- [x] **Define `IExecutionHistoryRepository`**  ✅
  - [x] New file: `Workflow.Persistence/Abstractions/IExecutionHistoryRepository.cs`
  - [x] `CreateExecutionAsync`, `UpdateExecutionStatusAsync`, `GetExecutionAsync`
  - [x] `GetExecutionsForWorkflowAsync`, `RecordNodeExecutionAsync`, `GetNodeExecutionsAsync`
  - [x] XML documentation

- [x] **Define `IVariableStore`**  ✅
  - [x] New file: `Workflow.Persistence/Abstractions/IVariableStore.cs`
  - [x] `null` value = valid entry semantics documented in XML docs
  - [x] `SetVariableAsync`, `GetVariableAsync`, `GetVariableHistoryAsync`
  - [x] `DeleteVariableAsync` (hard remove), `GetAllVariablesAsync`
  - [x] XML documentation

- [x] **Define `IBlobStore`** ️ ✅
  - [x] New file: `Workflow.Persistence/Abstractions/IBlobStore.cs`
  - [x] `PutAsync`, `GetAsync`, `DeleteAsync`, `ExistsAsync`, `GeneratePresignedUrlAsync`
  - [x] XML documentation

- [x] **Define supporting DTOs and value objects**  ✅
  - [x] `ExecutionRecord.cs` — full record with state, timestamps, inputs/outputs, error
  - [x] `NodeExecutionRecord.cs` — per-node execution tracking
  - [x] `VariableScope.cs` — `Global`/`ForWorkflow`/`ForExecution` factories + `VariableScopeKind` enum
  - [x] `VariableEntry.cs` — versioned entry with null-is-valid semantics
  - [x] `PagedResult.cs` — generic paged result with `HasNextPage`/`HasPreviousPage`/`TotalPages`
  - [x] `Pagination.cs` — clamped to max 200, `Skip` computed property
  - [x] `WorkflowFilter.cs` — name, active, tags, date range filter
  - [x] `ExecutionFilter.cs` — states, date range filter
  - [x] `HealthCheckResult.cs` — healthy/unhealthy with latency and details

- [x] **Define `IPersistenceProviderFactory`**  ✅
  - [x] New file: `Workflow.Persistence/Abstractions/IPersistenceProviderFactory.cs`
  - [x] `PersistenceConfiguration.cs` — `ProviderName`, `ConnectionString`, `Options`, `Validate()`

- [x] **Define `CompositePersistenceConfiguration`**  ✅
  - [x] New file: `Workflow.Persistence/Composite/CompositePersistenceConfiguration.cs`
  - [x] Per-interface provider routing with `Effective*` fallback properties

- [x] **Implement `CompositePersistenceProvider`**  ✅
  - [x] New file: `Workflow.Persistence/Composite/CompositePersistenceProvider.cs`
  - [x] Routes each interface to configured sub-provider
  - [x] `InitializeAsync` — parallel init of unique providers (deduped by reference)
  - [x] `HealthCheckAsync` — aggregates health from all sub-providers
  - [x] `DisposeAsync` — disposes all unique providers

- [x] **DI extension methods**  ✅
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

## 2.1.1 SQLite Persistence Provider 

**Purpose:** A lightweight SQLite-backed implementation of all persistence interfaces. By using SQLite's **`:memory:`** mode for tests and a file path for local dev, we get full SQL coverage (real migrations, real queries) with zero infrastructure setup. The repository and migration patterns established here are then **directly reused** by the PostgreSQL provider (2.1.2) — it becomes a simple swap of the connection provider and a few SQL dialect differences~ ✨

**Complexity:**  Low-Medium

**New Project:** `Workflow.Persistence.Sqlite`

> **CopilotNote:** The SQLite schema intentionally mirrors the Postgres schema (same table names,
> same column names) but drops Postgres-specific types (`jsonb` → `TEXT`, `text[]` → `TEXT`,
> `bigserial` → `INTEGER PRIMARY KEY AUTOINCREMENT`). This keeps the FluentMigrator migrations
> and Linq2Db entity classes copy-paste-upgradeable to Postgres in 2.1.2~ 
>
> For tests use `"Data Source=:memory:"` — SQLite in-memory databases are fast and isolated
> per-connection. No Docker, no temp files, no cleanup needed~ 

### Design Decisions 

| Concern | SQLite choice | Postgres equivalent (2.1.2) |
|---------|--------------|---------------------------|
| JSON columns | `TEXT` with `System.Text.Json` serialization | `jsonb` |
| Arrays (tags) | `TEXT` comma-joined | `text[]` |
| Auto-increment PK | `INTEGER PRIMARY KEY AUTOINCREMENT` | `BIGSERIAL` |
| UUID PK | `TEXT` (stored as string) | `UUID` |
| Concurrent access | WAL mode (`PRAGMA journal_mode=WAL`) | native |
| In-memory testing | `"Data Source=:memory:;Cache=Shared;Mode=Memory"` | Testcontainers |

### Tasks:

- [x] **Create `Workflow.Persistence.Sqlite` project** 
  - [x] Add project to solution
  - [x] Reference `Workflow.Persistence`
  - [x] NuGet packages:
    - [x] `linq2db` (4.x)
    - [x] `linq2db.SQLite`
    - [x] `Microsoft.Data.Sqlite` (8.x)
    - [x] `FluentMigrator`
    - [x] `FluentMigrator.Runner`
    - [x] `FluentMigrator.Runner.SQLite`
  - [x] Add to `Directory.Packages.props`

- [x] **Design and implement database schema migrations** 
  - [x] New file: `Workflow.Persistence.Sqlite/Migrations/Migration_001_InitialSchema.cs`
    - [x] `workflows` table
    - [x] `executions` table
    - [x] `execution_nodes` table
    - [x] `variables` table
  - [x] New file: `Workflow.Persistence.Sqlite/Migrations/Migration_002_AddIndexes.cs`
    - [x] Index: `executions(workflow_id)`
    - [x] Index: `executions(state)`
    - [x] Index: `executions(started_at)`
    - [x] Index: `execution_nodes(execution_id)`
    - [x] Unique index: `variables(scope_kind, scope_id, name, version)`
    - [x] Index: `workflows(name)`
  - [x] New file: `Workflow.Persistence.Sqlite/SqliteMigrationRunner.cs`
    - [x] `RunMigrationsAsync(string connectionString)`
    - [x] `RollbackLastMigrationAsync(string connectionString)`
    - [x] Enable WAL mode after migration (`PRAGMA journal_mode=WAL`)

- [x] **Shared base for SQL providers** 
  - [x] New file: `Workflow.Persistence.Sqlite/Data/WorkflowDataConnection.cs`
  - [x] New file: `Workflow.Persistence.Sqlite/Data/Entities/WorkflowEntity.cs`
  - [x] New file: `Workflow.Persistence.Sqlite/Data/Entities/ExecutionEntity.cs`
  - [x] New file: `Workflow.Persistence.Sqlite/Data/Entities/ExecutionNodeEntity.cs`
  - [x] New file: `Workflow.Persistence.Sqlite/Data/Entities/VariableEntity.cs`
  - [x] New file: `Workflow.Persistence.Sqlite/Data/WorkflowDataConnectionFactory.cs`

- [x] **Implement `SqliteWorkflowRepository`** 
  - [x] `CreateAsync`, `UpdateAsync`, `DeleteAsync`, `PurgeAsync`, `RestoreAsync`
  - [x] `GetByIdAsync(id)`, `GetByIdAsync(id, includeDeleted)`
  - [x] `GetAllAsync`, `SearchAsync`, `ExistsAsync`
  - [x] JSON helpers with LanguageExt converters (`Arr<T>`, `HashMap<string,V>`)

- [x] **Implement `SqliteExecutionHistoryRepository`** 
  - [x] `CreateExecutionAsync`, `UpdateExecutionStatusAsync`, `GetExecutionAsync`
  - [x] `GetExecutionsForWorkflowAsync`, `RecordNodeExecutionAsync`, `GetNodeExecutionsAsync`

- [x] **Implement `SqliteVariableStore`** 
  - [x] `SetVariableAsync` — versioned inserts, null-is-valid semantics
  - [x] `GetVariableAsync(version: null)` — latest version
  - [x] `GetVariableAsync(version: n)` — specific version
  - [x] `GetVariableHistoryAsync`, `DeleteVariableAsync`, `GetAllVariablesAsync`

- [x] **Implement `SqlitePersistenceProvider`** 
  - [x] `ProviderName = "sqlite"`, `InitializeAsync`, `HealthCheckAsync`
  - [x] Exposes all repositories + `SqliteBlobStore`
  - [x] DI helpers: `AddSqlitePersistence`, `AddSqlitePersistenceInMemory`

**Tests (25/25 passing):** → `Workflow.Tests/Persistence/SqliteProviderTests.cs` ✅
> CopilotNote: Tests use a temp-file SQLite DB. FluentMigrator.Runner.SQLite uses System.Data.SQLite
> while linq2db uses Microsoft.Data.Sqlite — two separate native runtimes that cannot share
> in-memory databases. A temp file is visible to both~ ️
- [x] Provider initialises (migrations run) without error
- [x] Provider health check returns healthy
- [x] **Workflow CRUD:**
  - [x] `CreateAsync` → `GetByIdAsync` round-trip preserves all fields
  - [x] `UpdateAsync` changes definition
  - [x] `DeleteAsync` soft-deletes — `GetByIdAsync` returns null
  - [x] `GetByIdAsync(id, includeDeleted: true)` returns soft-deleted workflow
  - [x] `PurgeAsync` removes completely — `GetByIdAsync(includeDeleted: true)` returns null
  - [x] `RestoreAsync` brings back soft-deleted workflow
  - [x] `GetAllAsync` with `IsActive = true` excludes soft-deleted
  - [x] `SearchAsync` case-insensitive substring match
  - [x] Pagination: page 1 and page 2 return correct items
  - [x] `ExistsAsync` true/false
- [x] **Execution history:**
  - [x] `CreateExecutionAsync` → `GetExecutionAsync` round-trip
  - [x] `UpdateExecutionStatusAsync` Running → Completed sets completed_at
  - [x] `RecordNodeExecutionAsync` then `GetNodeExecutionsAsync`
  - [x] Execution filter by state
- [x] **Variable store:**
  - [x] `Set("x", "hello")` creates version 1
  - [x] `Set("x", "world")` creates version 2; `Get` returns `"world"`
  - [x] `Set("x", null)` creates version 3; `Get` returns `VariableEntry { Value = null }` (not null!)
  - [x] `Get(version: 1)` returns `"hello"`
  - [x] `GetHistory` returns 3 entries ordered
  - [x] `DeleteVariableAsync` removes all; subsequent `Get` returns null (not found)
  - [x] `GetAllVariablesAsync` includes null-valued variable
  - [x] Global, Workflow, Execution scopes isolated

---

## 2.1.2 PostgreSQL Persistence Provider 

**Purpose:** Production-grade PostgreSQL persistence. Inherits the same migration structure, entity mappings, and repository pattern from the SQLite provider (2.1.1) — the key additions are Postgres-specific SQL types (`jsonb`, `text[]`, `UUID`), connection pooling via Npgsql, and Postgres-optimised index strategies~ ️

**Complexity:**  Medium *(reduced from  High — SQLite provider establishes the pattern)*

**Target:** PostgreSQL 15+ (uses `jsonb` operators, `text[]` arrays, `gen_random_uuid()`, expression indexes)

**New Project:** `Workflow.Persistence.Postgres`

> **CopilotNote:** Most of the hard work is done in 2.1.1. For Postgres, the main changes are:
> - Swap `Microsoft.Data.Sqlite` → `Npgsql` + `linq2db.PostgreSQL`
> - Replace `TEXT` JSON columns with `jsonb` (enables GIN indexes and `@>` operator)
> - Replace `TEXT` tag columns with `text[]` (enables `@>` array containment queries)
> - Replace `INTEGER AUTOINCREMENT` with `BIGSERIAL`
> - Replace `TEXT` UUID columns with native `UUID` type
> - Use `Testcontainers.PostgreSql` for integration tests (requires Docker)~ 

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

- [x] **Create `Workflow.Persistence.Postgres` project** 
  - [x] Add project to solution
  - [x] NuGet packages: `linq2db`, `linq2db.PostgreSQL`, `Npgsql`, `FluentMigrator`, `FluentMigrator.Runner`, `FluentMigrator.Runner.Postgres`

- [x] **Design and implement database schema migrations** 
  - [x] New file: `Workflow.Persistence.Postgres/Migrations/Migration_001_InitialSchema.cs`
    - [x] `workflows` table — id (UUID), name, description, definition (jsonb), version, is_active, created_at (timestamptz), updated_at, tags (text[]), metadata (jsonb)
    - [x] `executions` table — id (UUID), workflow_id, state, started_at, completed_at, inputs (jsonb), outputs (jsonb), error, triggered_by
    - [x] `execution_nodes` table — id (bigserial), execution_id, node_id, state, started_at, completed_at, inputs (jsonb), outputs (jsonb), error, duration_ms
    - [x] `variables` table — id (bigserial), scope_kind, scope_id (UUID), name, value (jsonb), value_type, version, created_at, updated_at
  - [x] New file: `Workflow.Persistence.Postgres/Migrations/Migration_002_AddIndexes.cs`
    - [x] BTREE indexes: `executions(workflow_id)`, `executions(state)`, `executions(started_at)`, `execution_nodes(execution_id)`, `workflows(name)`, `workflows(is_active)`
    - [x] Unique constraint: `variables(scope_kind, scope_id, name, version)`
    - [x] GIN index on `workflows(tags)` for `@>` array containment queries
    - [x] GIN index on `workflows(definition)` for jsonb path queries
  - [x] New file: `Workflow.Persistence.Postgres/PostgresMigrationRunner.cs`
    - [x] `RunMigrationsAsync(string connectionString)`
    - [x] `RollbackLastMigrationAsync(string connectionString)`

- [x] **Implement Linq2Db data context and entity mappings** ️
  - [x] `WorkflowDataConnection.cs` — extends `DataConnection`, exposes typed tables
  - [x] `WorkflowEntity.cs` — UUID PK, `definition` as `DataType.BinaryJson`, `tags` as `string[]`
  - [x] `ExecutionEntity.cs` — UUID PK/FK, `DateTimeOffset` timestamps, jsonb inputs/outputs
  - [x] `ExecutionNodeEntity.cs` — bigserial PK, jsonb inputs/outputs
  - [x] `VariableEntity.cs` — bigserial PK, UUID scope_id, jsonb value
  - [x] `WorkflowDataConnectionFactory.cs` — `UsePostgreSQL` connection config

- [x] **Implement `PostgresWorkflowRepository`** 
  - [x] `CreateAsync`, `UpdateAsync`, `DeleteAsync` (soft), `PurgeAsync` (hard+cascade), `RestoreAsync`
  - [x] `GetByIdAsync`, `GetByIdAsync(includeDeleted)`, `GetAllAsync`, `SearchAsync`, `ExistsAsync`
  - [x] LanguageExt JSON converters for `Arr<T>` and `HashMap<string,V>`

- [x] **Implement `PostgresExecutionHistoryRepository`** 
  - [x] `CreateExecutionAsync`, `UpdateExecutionStatusAsync`, `GetExecutionAsync`
  - [x] `GetExecutionsForWorkflowAsync` (filtered + paginated), `RecordNodeExecutionAsync` (upsert), `GetNodeExecutionsAsync`

- [x] **Implement `PostgresVariableStore`** 
  - [x] `SetVariableAsync` — atomic `INSERT ... SELECT MAX(version)+1` (race-condition safe)
  - [x] `GetVariableAsync` (latest or specific version), `GetVariableHistoryAsync`
  - [x] `DeleteVariableAsync`, `GetAllVariablesAsync`
  - [x] Null value semantics preserved — `null` value stored as SQL NULL with `value_type="null"`

- [x] **Implement `PostgresPersistenceProvider`** 
  - [x] `ProviderName = "postgres"`, `InitializeAsync`, `HealthCheckAsync` (SELECT 1 with latency)
  - [x] `Blobs → null` (Postgres not suitable for large blobs — use S3)
  - [x] DI helper: `AddPostgresPersistence(string connectionString)`

**Tests (30/30 passing):** → `Workflow.Tests/Persistence/PostgresProviderTests.cs` ✅
> Uses `Testcontainers.PostgreSql` (postgres:15-alpine). Tests marked `[Trait("Category", "Integration")]`.
> Requires Docker~ 
- [x] Provider initializes (migrations run) without error
- [x] Provider health check returns healthy on live DB
- [x] Provider health check returns unhealthy on bad connection string
- [x] **Workflow CRUD:** Create→GetById, Update, Delete (soft), GetById(includeDeleted), Purge (hard), Restore, GetAll by name filter, GetAll active only, Pagination, SearchAsync, ExistsAsync
- [x] **Execution history:** Create→Get round-trip, Update Pending→Running→Completed, RecordNode→GetNodes, Filter by state, Date range filter, Pagination
- [x] **Variable store:** Set creates v1, Set again creates v2, Set null creates null entry (not missing!), Get specific version, GetHistory ordered, Delete removes all, GetAll includes null-valued, Scopes isolated
- [x] Concurrent writes (5 parallel Set calls) don't corrupt versions

---

## 2.1.3 NATS KV Persistence Provider 

**Purpose:** Lightweight, fast persistence using NATS JetStream Key-Value store. Best for distributed/cloud scenarios where latency matters~ ⚡

**Complexity:**  Medium

**New Project:** `Workflow.Persistence.Nats`

### Tasks:

- [x] **Create `Workflow.Persistence.Nats` project** 
  - [x] NuGet: `NATS.Net` (2.x), `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging.Abstractions`

- [x] **Implement NATS connection management** 
  - [x] New file: `Workflow.Persistence.Nats/NatsConnectionManager.cs`
    - [x] Connection string parsing (nats://user:pass@host:4222)
    - [x] TLS configuration (via `NatsOpts` overload)
    - [x] Reconnect logic handled by NATS.Net internally
    - [x] `PingAsync` for health checks

- [x] **Implement `NatsWorkflowRepository`** 
  - [x] New file: `Workflow.Persistence.Nats/Repositories/NatsWorkflowRepository.cs`
  - [x] KV bucket: `WF_WORKFLOWS`
  - [x] Key pattern: `{id}` (UUID string)
  - [x] Value: JSON-serialised `NatsWorkflowDocument` (wraps `WorkflowDefinition` + `IsActive` + timestamps)
  - [x] `CreateAsync` — Put with new UUID
  - [x] `UpdateAsync` — Put with updated document
  - [x] `DeleteAsync` — Update document with `IsActive = false` (soft delete)
  - [x] `PurgeAsync` — NATS KV `PurgeAsync` (hard delete, removes all revisions)
  - [x] `RestoreAsync` — Update document with `IsActive = true`
  - [x] `GetByIdAsync` — Get + deserialise; respects `includeDeleted`
  - [x] `GetAllAsync` — Keys() + batch Get, in-memory filter + pagination
  - [x] `SearchAsync` — in-memory substring filter
  - [x] `ExistsAsync` — checks `IsActive == true`

- [x] **Implement `NatsExecutionHistoryRepository`** 
  - [x] New file: `Workflow.Persistence.Nats/Repositories/NatsExecutionHistoryRepository.cs`
  - [x] KV bucket: `WF_EXECUTIONS` for current execution state
  - [x] KV bucket: `WF_EXEC_NODES` for node records
  - [x] Key pattern: `{executionId}` / `{executionId}:{nodeId}`
  - [x] `CreateExecutionAsync`, `UpdateExecutionStatusAsync`, `GetExecutionAsync`
  - [x] `GetExecutionsForWorkflowAsync` — in-memory scan + filter + pagination
  - [x] `RecordNodeExecutionAsync` — upsert via Put
  - [x] `GetNodeExecutionsAsync` — prefix scan on `{executionId}:`

- [x] **Implement `NatsVariableStore`** 
  - [x] New file: `Workflow.Persistence.Nats/Repositories/NatsVariableStore.cs`
  - [x] KV bucket: `WF_VARIABLES` with `History = 100`
  - [x] Key pattern: `global::{name}` / `workflow.{id}.{name}` / `execution.{id}.{name}`
  - [x] NATS KV built-in history via `HistoryAsync` with `NatsVariableDocument` wrappers
  - [x] `SetVariableAsync` — increments explicit version counter stored in document
  - [x] `GetVariableAsync(version: null)` — latest revision
  - [x] `GetVariableAsync(version: n)` — scans history for matching version
  - [x] `GetVariableHistoryAsync` — full ordered history
  - [x] `DeleteVariableAsync` — NATS KV `PurgeAsync` (removes all revisions)
  - [x] `GetAllVariablesAsync` — prefix scan for scope

- [x] **Implement `NatsPersistenceProvider`** 
  - [x] New file: `Workflow.Persistence.Nats/NatsPersistenceProvider.cs`
  - [x] `ProviderName = "nats"`, `InitializeAsync` — creates all KV buckets via `CreateOrUpdateStoreAsync`
  - [x] `HealthCheckAsync` — ping connection status
  - [x] `Blobs → null` (NATS KV not suitable for large blobs — use S3)
  - [x] DI registration: `AddNatsPersistence(string natsUrl)`

**Tests (17/17 passing):** → `Workflow.Tests/Persistence/NatsProviderTests.cs` ✅
> Uses `Testcontainers.Nats` with `nats:2.10-alpine -js` (JetStream enabled). Tests marked `[Trait("Category", "Integration")]`.
> Requires Docker~ 
- [x] Provider initialises (creates buckets)
- [x] Provider name is "nats"
- [x] Provider health check returns healthy on live server
- [x] Provider health check returns unhealthy on bad connection string
- [x] Provider Blobs is null
- [x] **Workflow CRUD:** Create→GetById, Update, Delete (soft), GetById(includeDeleted), Purge (hard), Restore, Exists true/false, Search, GetAll active only, Concurrent sequential updates
- [x] **Execution history:** Create→Get round-trip, Update Pending→Running→Completed, RecordNode→GetNodes, Filter by state
- [x] **Variable store:** Set creates v1, Set again creates v2+Get latest, Set null creates null entry (not missing!), Get specific version, GetHistory ordered, Delete removes all+subsequent Get=null, Delete returns false when not found, GetAll includes null-valued, Scopes isolated, Watch fires on change

---

## 2.1.4 S3 Blob Store ☁️

**Purpose:** Large-object storage for workflow outputs, execution logs, and binary data that shouldn't be in a relational DB or KV store~ ️

**Complexity:**  Medium

**New Project:** `Workflow.Persistence.S3`

### Tasks:

- [x] **Create `Workflow.Persistence.S3` project**  ✅
  - [x] NuGet: `AWSSDK.S3` (3.x)
  - [x] (Optional) `Minio` — package version added centrally; implementation uses AWSSDK.S3 with `UsePathStyle` for MinIO compatibility (single SDK keeps things tidy~ )

- [x] **Implement S3 client configuration** ⚙️ ✅
  - [x] New file: `Workflow.Persistence.S3/S3Configuration.cs`
    - [x] `AccessKey`, `SecretKey`, `Region`
    - [x] `BucketName`, `EndpointUrl` (for MinIO/local)
    - [x] `UsePathStyle` flag (needed for MinIO)
    - [x] `ServerSideEncryption` flag
    - [x] `MultipartThresholdBytes`, `MaxRetryAttempts`, `RetryBaseDelay` + `Validate()`

- [x] **Implement `S3BlobStore`** ☁️ ✅
  - [x] New file: `Workflow.Persistence.S3/S3BlobStore.cs`
  - [x] Implements `IBlobStore`
  - [x] `PutAsync` — single PUT for ≤ threshold, `TransferUtility` multipart for larger
  - [x] `GetAsync` — streaming download (returns `null` on 404)
  - [x] `DeleteAsync` — `ExistsAsync` → `DeleteObjectAsync`; returns true only if it existed
  - [x] `ExistsAsync` — HeadObject (`GetObjectMetadataAsync`) check
  - [x] `GeneratePresignedUrlAsync` — signed URL with expiry
  - [ ] Key patterns *(documented for future engine wiring in 2.1.5 — not enforced by the store itself)*:
    - [ ] Workflows: `workflows/{id}/definition.json`
    - [ ] Executions: `executions/{id}/data.json`
    - [ ] Large node outputs: `executions/{id}/nodes/{nodeId}/output.bin`
    - [ ] Logs: `executions/{id}/logs/{timestamp}.log`
  - [x] Retry on transient S3 errors (503, 429, `SlowDown`, `RequestTimeout`) with exponential backoff
  - [x] Content-type detection from key extension

- [x] **Implement `S3PersistenceProvider`** ☁️ ✅
  - [x] New file: `Workflow.Persistence.S3/S3PersistenceProvider.cs`
  - [x] `InitializeAsync` — verify bucket exists, create if missing
  - [x] `HealthCheckAsync` — `GetACL` (HEAD-equivalent) check + latency
  - [x] DI registration: `AddS3BlobStore(S3Configuration config)` in `ServiceCollectionExtensions.cs`
  - [x] Non-blob repository properties throw `NotSupportedException` (compose with SQL/NATS for full stack)

**Tests (14/14 written):** → `Workflow.Tests/Persistence/S3BlobStoreTests.cs` ✅
> Uses MinIO via Testcontainers (`minio/minio:latest`) — marked `[Trait("Category", "Integration")]`. Requires Docker~ 
- [x] Provider initialises (bucket created) and `Blobs` is wired
- [x] Provider name is `"s3"`
- [x] Non-blob repositories (`Workflows`, `ExecutionHistory`, `Variables`) throw `NotSupportedException`
- [x] Put small file → Get → content matches
- [x] Put large file (>5MiB) via multipart → Get → content matches byte-for-byte
- [x] Exists returns true after Put
- [x] Exists returns false before Put
- [x] Get returns `null` when object missing
- [x] Delete removes object (Exists returns false) and returns `true`
- [x] Delete returns `false` when object missing
- [x] GeneratePresignedUrl returns non-empty URL and is fetchable without auth
- [x] Presigned URL expires (expired URL returns non-success)
- [x] Put with explicit content-type preserved on Get
- [x] Put auto-detects content-type from key extension
- [x] HealthCheck returns healthy on live MinIO
- [x] HealthCheck returns unhealthy on bad endpoint

---

## 2.1.5 Engine Integration & Snapshot Migration 

**Purpose:** Wire the persistence layer into the Akka actor system. `WorkflowExecutor` currently uses `InMemoryExecutionStateStore` — replace it with the pluggable `IExecutionHistoryRepository`~ ⚡

**Complexity:**  Medium

### Tasks:

- [x] **Update `WorkflowExecutor` to use `IExecutionHistoryRepository`** ⚡
  - [x] Resolve `IExecutionHistoryRepository` from `IServiceProvider`
  - [x] **All repository calls are awaited** (not fire-and-forget) to guarantee reliability
  - [x] Use Akka `PipeToSelf` pattern: wrap each async repo call in `Task.Run(...).PipeTo(Self)` to avoid blocking the actor mailbox
  - [x] On `StartExecution`: call `CreateExecutionAsync` (PipeTo self → handle `ExecutionCreated` internal message)
  - [x] On `NodeExecutionCompleted`: call `RecordNodeExecutionAsync`
  - [x] On `NodeExecutionFailed`: call `RecordNodeExecutionAsync` with error
  - [x] On `CompleteWorkflow`: call `UpdateExecutionStatusAsync(Completed)`
  - [x] On `FailWorkflow`: call `UpdateExecutionStatusAsync(Failed)` with error
  - [x] Fall back gracefully if repository is null (backwards compat)

- [x] **Update `WorkflowSupervisor` to use `IWorkflowRepository`** ️
  - [x] On `CreateWorkflowInstance`: optionally validate against stored definition
  - [x] Fall back to provided definition if repository is null

- [x] **Update `SaveSnapshotAsync` in `WorkflowExecutor`** 
  - [x] Currently writes to `InMemoryExecutionStateStore`
  - [x] Bridge to `IExecutionHistoryRepository` when available
  - [x] Keep `InMemoryExecutionStateStore` as fallback
  - [ ] Follow-up hardening: avoid duplicate terminal status persistence when completion/failure already queued a status update
  - [ ] Follow-up hardening: treat benign/idempotent terminal update conflicts (including null-result provider responses) as debug/info, not warning
  - [ ] Follow-up hardening: keep warning/error logs only for actionable data-loss paths, with structured fields (`ExecutionId`, `State`, provider type, exception type)

- [x] **Update DI registration in `Workflow.Api`** 
  - [x] Wire `IPersistenceProvider` from appsettings `Persistence:Provider`
  - [x] Support `"sqlite"`, `"postgres"`, `"nats"`, `"composite"` values
  - [x] For `"sqlite"`: use `"Data Source=:memory:;Cache=Shared;Mode=Memory"` when `ConnectionString` is `":memory:"`
  - [x] For `"composite"`: read `Persistence:Composite:Workflows`, `Persistence:Composite:Variables`, etc.
  - [x] Register all repositories from the selected provider(s)
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

- [ ] **Follow-up note closure (docs + API handoff)** 🔁
  - [ ] Promote the composite `Persistence` example into API docs/config guidance, then mark the DI follow-up example item complete.
  - [ ] Add explicit API identity flow for execution start: `X-Caller-Id` override → claims (`NameIdentifier`/`sub`) → `"system"` fallback.
  - [ ] Pass `ExecutionStartOptions` from execution start endpoints into `CreateWorkflowInstance` (`CallerId` + runtime `VariableWriteMode`).
  - [ ] Ensure `POST /api/v1/workflows/{id}/execute` returns the persisted execution ID from the repository-backed flow.
  - [ ] Cross-reference endpoint delivery ownership to Phase 2.7 so 2.1.5 remains persistence integration focused.
  - [ ] Add a short runbook note for snapshot-bridge outcomes: success vs benign duplicate terminal update vs true persistence regression.

**Tests (6/6 passing):** → `Workflow.Tests/Engine/PersistenceIntegrationTests.cs` ✅
> Uses `SqlitePersistenceProvider` with `:memory:` connection — **no Docker required!**
- [x] Engine with SQLite `:memory:` provider: workflow runs + execution record created
- [x] Engine with SQLite `:memory:` provider: node completions recorded
- [x] Engine with SQLite `:memory:` provider: failed workflow records error
- [x] Engine without provider: runs correctly (null provider fallback)
- [x] Execution history captures correct node states
- [x] Execution history captures variable updates per node
- [ ] Follow-up hardening test: completed execution does not emit snapshot-bridge warning when terminal status already persisted
- [ ] Follow-up hardening test: null-result/not-found terminal update path is handled without warning spam
- [ ] Follow-up hardening test: terminal persisted state remains correct when snapshot fallback is used

---

## Phase 2.1 Deliverables ✅

**Completion Criteria:**
- [ ] `IPersistenceProvider` + all sub-interfaces defined and documented
- [ ] `InMemoryPersistenceProvider` — ~~full implementation for tests/dev~~ **replaced by SQLite `:memory:`**
- [ ] `SqlitePersistenceProvider` — lightweight file/in-memory provider; establishes SQL pattern for Postgres
- [ ] `PostgresPersistenceProvider` — production-grade with Linq2Db + migrations
- [ ] `NatsPersistenceProvider` — lightweight cloud-native option
- [ ] `S3BlobStore` — large object storage
- [x] Engine (`WorkflowExecutor`) wired to `IExecutionHistoryRepository`
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

## New Projects Layout (updated for SQLite + composite) ️
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
| **Revised** | In-Memory → SQLite | SQLite `:memory:` replaces pure in-memory; same SQL pattern feeds into Postgres | 2.1.1 rewritten as `Workflow.Persistence.Sqlite`; Postgres complexity reduced → |

---

>  **Ami's Phase 2.1 Tips (revised):**
> - 2.1.0 Abstractions ✅ Done! 22 tests passing~
> - Build 2.1.1 (SQLite) next — use `:memory:` mode for fast zero-infra testing~ 
> - Build 2.1.5 (engine integration) after 2.1.1 — SQLite `:memory:` tests the full actor stack without Docker~ 
> - 2.1.2 (Postgres) will be fast since 2.1.1 establishes the pattern — just swap the types~ 
> - 2.1.3 (NATS) and 2.1.4 (S3) are independent — can be done in parallel~ ☁️ UwU 
> - Test composite routing with in-memory providers before touching real infra~  UwU 
