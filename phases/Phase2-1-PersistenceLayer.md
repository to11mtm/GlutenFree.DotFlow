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

- [ ] **Create `Workflow.Persistence` project** 📁
  - [ ] Add `Workflow.Persistence.csproj` to solution
  - [ ] Reference `Workflow.Core`
  - [ ] Add to `Directory.Build.props` / `Directory.Packages.props`

- [ ] **Define `IPersistenceProvider`** 🔌
  - [ ] New file: `Workflow.Persistence/Abstractions/IPersistenceProvider.cs`
  - [ ] `Task InitializeAsync(CancellationToken ct = default)` — run migrations, create buckets
  - [ ] `Task<HealthCheckResult> HealthCheckAsync(CancellationToken ct = default)`
  - [ ] `ValueTask DisposeAsync()` (implements `IAsyncDisposable`)
  - [ ] `string ProviderName { get; }` — e.g. `"postgres"`, `"nats"`, `"s3"`
  - [ ] `bool IsInitialized { get; }`
  - [ ] XML documentation

- [ ] **Define `IWorkflowRepository`** 📋
  - [ ] New file: `Workflow.Persistence/Abstractions/IWorkflowRepository.cs`
  - [ ] `Task<Guid> CreateAsync(WorkflowDefinition definition, CancellationToken ct = default)`
  - [ ] `Task UpdateAsync(Guid id, WorkflowDefinition definition, CancellationToken ct = default)`
  - [ ] `Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)` — **soft delete** (`is_active = false`)
  - [ ] `Task<bool> PurgeAsync(Guid id, CancellationToken ct = default)` — **hard delete**, removes all FK records
  - [ ] `Task<WorkflowDefinition?> GetByIdAsync(Guid id, CancellationToken ct = default)` — only returns active workflows by default
  - [ ] `Task<WorkflowDefinition?> GetByIdAsync(Guid id, bool includeDeleted, CancellationToken ct = default)` — overload for admin use
  - [ ] `Task<PagedResult<WorkflowDefinition>> GetAllAsync(WorkflowFilter filter, Pagination pagination, CancellationToken ct = default)`
  - [ ] `Task<IReadOnlyList<WorkflowDefinition>> SearchAsync(string query, CancellationToken ct = default)`
  - [ ] `Task<bool> ExistsAsync(Guid id, CancellationToken ct = default)`
  - [ ] `Task<bool> RestoreAsync(Guid id, CancellationToken ct = default)` — un-delete a soft-deleted workflow
  - [ ] XML documentation

- [ ] **Define `IExecutionHistoryRepository`** 📊
  - [ ] New file: `Workflow.Persistence/Abstractions/IExecutionHistoryRepository.cs`
  - [ ] `Task<Guid> CreateExecutionAsync(ExecutionRecord record, CancellationToken ct = default)`
  - [ ] `Task UpdateExecutionStatusAsync(Guid executionId, ExecutionState state, DateTimeOffset? endTime = null, string? error = null, CancellationToken ct = default)`
  - [ ] `Task<ExecutionRecord?> GetExecutionAsync(Guid executionId, CancellationToken ct = default)`
  - [ ] `Task<PagedResult<ExecutionRecord>> GetExecutionsForWorkflowAsync(Guid workflowId, ExecutionFilter filter, Pagination pagination, CancellationToken ct = default)`
  - [ ] `Task RecordNodeExecutionAsync(NodeExecutionRecord nodeRecord, CancellationToken ct = default)`
  - [ ] `Task<IReadOnlyList<NodeExecutionRecord>> GetNodeExecutionsAsync(Guid executionId, CancellationToken ct = default)`
  - [ ] XML documentation

- [ ] **Define `IVariableStore`** 💾
  - [ ] New file: `Workflow.Persistence/Abstractions/IVariableStore.cs`
  - [ ] `Task SetVariableAsync(VariableScope scope, string name, object? value, CancellationToken ct = default)`
    - [ ] **`null` value is a valid entry** — persisted as a null-valued version, NOT a delete
    - [ ] Use `DeleteVariableAsync` to explicitly remove a variable
  - [ ] `Task<VariableEntry?> GetVariableAsync(VariableScope scope, string name, int? version = null, CancellationToken ct = default)`
    - [ ] Returns `null` (not found) vs `VariableEntry { Value = null }` (found, value is null) — distinct!
  - [ ] `Task<IReadOnlyList<VariableEntry>> GetVariableHistoryAsync(VariableScope scope, string name, CancellationToken ct = default)`
    - [ ] Includes null-valued versions in history
  - [ ] `Task<bool> DeleteVariableAsync(VariableScope scope, string name, CancellationToken ct = default)`
    - [ ] **Hard removes** the variable and all its history from scope
    - [ ] Returns `false` if variable didn't exist
  - [ ] `Task<IReadOnlyDictionary<string, object?>> GetAllVariablesAsync(VariableScope scope, CancellationToken ct = default)`
    - [ ] Includes variables whose current value is `null`
  - [ ] XML documentation

- [ ] **Define `IBlobStore`** 🗃️
  - [ ] New file: `Workflow.Persistence/Abstractions/IBlobStore.cs`
  - [ ] `Task<string> PutAsync(string key, Stream data, string? contentType = null, CancellationToken ct = default)` → returns ETag/version
  - [ ] `Task<Stream?> GetAsync(string key, CancellationToken ct = default)`
  - [ ] `Task<bool> DeleteAsync(string key, CancellationToken ct = default)`
  - [ ] `Task<bool> ExistsAsync(string key, CancellationToken ct = default)`
  - [ ] `Task<string> GeneratePresignedUrlAsync(string key, TimeSpan expiry, CancellationToken ct = default)`
  - [ ] XML documentation

- [ ] **Define supporting DTOs and value objects** 📦
  - [ ] New file: `Workflow.Persistence/Models/ExecutionRecord.cs`
    - [ ] `Guid ExecutionId`, `Guid WorkflowId`, `ExecutionState State`
    - [ ] `DateTimeOffset StartedAt`, `DateTimeOffset? CompletedAt`
    - [ ] `IReadOnlyDictionary<string, object?> Inputs`, `Outputs`
    - [ ] `string? Error`, `string? TriggeredBy`
  - [ ] New file: `Workflow.Persistence/Models/NodeExecutionRecord.cs`
    - [ ] `Guid ExecutionId`, `string NodeId`, `NodeExecutionState State`
    - [ ] `DateTimeOffset StartedAt`, `DateTimeOffset? CompletedAt`
    - [ ] `IReadOnlyDictionary<string, object?> Inputs`, `Outputs`
    - [ ] `string? Error`, `TimeSpan Duration`
  - [ ] New file: `Workflow.Persistence/Models/VariableScope.cs`
    - [ ] `record VariableScope(VariableScopeKind Kind, Guid? WorkflowId = null, Guid? ExecutionId = null)`
    - [ ] `enum VariableScopeKind { Global, Workflow, Execution }`
    - [ ] Static factory: `VariableScope.Global`, `VariableScope.ForWorkflow(id)`, `VariableScope.ForExecution(id)`
  - [ ] New file: `Workflow.Persistence/Models/VariableEntry.cs`
    - [ ] `VariableScope Scope`, `string Name`, `object? Value`
    - [ ] `string ValueTypeName`, `int Version`
    - [ ] `DateTimeOffset CreatedAt`, `DateTimeOffset UpdatedAt`
  - [ ] New file: `Workflow.Persistence/Models/PagedResult.cs`
    - [ ] `IReadOnlyList<T> Items`, `int TotalCount`, `int Page`, `int PageSize`
    - [ ] `bool HasNextPage { get; }`, `bool HasPreviousPage { get; }`
  - [ ] New file: `Workflow.Persistence/Models/Pagination.cs`
    - [ ] `int Page`, `int PageSize` (default 50, max 200)
  - [ ] New file: `Workflow.Persistence/Models/WorkflowFilter.cs`
    - [ ] `string? NameContains`, `bool? IsActive`, `string[]? Tags`
    - [ ] `DateTimeOffset? CreatedAfter`, `DateTimeOffset? CreatedBefore`
  - [ ] New file: `Workflow.Persistence/Models/ExecutionFilter.cs`
    - [ ] `ExecutionState[]? States`, `DateTimeOffset? StartedAfter`, `DateTimeOffset? StartedBefore`
  - [ ] New file: `Workflow.Persistence/Models/HealthCheckResult.cs`
    - [ ] `bool IsHealthy`, `string ProviderName`, `TimeSpan Latency`
    - [ ] `string? ErrorMessage`, `IReadOnlyDictionary<string, object?> Details`

- [ ] **Define `IPersistenceProviderFactory`** 🏭
  - [ ] New file: `Workflow.Persistence/Abstractions/IPersistenceProviderFactory.cs`
  - [ ] `IPersistenceProvider Create(PersistenceConfiguration config)`
  - [ ] `bool CanHandle(string providerName)`
  - [ ] New file: `Workflow.Persistence/PersistenceConfiguration.cs`
    - [ ] `string ProviderName`, `string ConnectionString`
    - [ ] `IReadOnlyDictionary<string, string> Options`

- [ ] **Define `CompositePersistenceConfiguration`** 🔀
  - [ ] New file: `Workflow.Persistence/Composite/CompositePersistenceConfiguration.cs`
  - [ ] `PersistenceConfiguration WorkflowsProvider` — which provider handles `IWorkflowRepository`
  - [ ] `PersistenceConfiguration ExecutionHistoryProvider` — which provider handles `IExecutionHistoryRepository`
  - [ ] `PersistenceConfiguration VariablesProvider` — which provider handles `IVariableStore`
  - [ ] `PersistenceConfiguration? BlobsProvider` — nullable, falls back to `WorkflowsProvider`
  - [ ] Allows mixing providers (e.g. Postgres workflows + NATS variables)

- [ ] **Implement `CompositePersistenceProvider`** 🔀
  - [ ] New file: `Workflow.Persistence/Composite/CompositePersistenceProvider.cs`
  - [ ] Implements `IPersistenceProvider`
  - [ ] `ProviderName = "composite"`
  - [ ] Holds a configured sub-provider per interface
  - [ ] `InitializeAsync` — initialises all sub-providers in parallel
  - [ ] `HealthCheckAsync` — aggregates health from all sub-providers
  - [ ] Exposes each sub-provider's repository via the correct interface

- [ ] **DI extension methods** 💉
  - [ ] New file: `Workflow.Persistence/ServiceCollectionExtensions.cs`
  - [ ] `AddWorkflowPersistence(this IServiceCollection, PersistenceConfiguration config)` — single provider
  - [ ] `AddWorkflowPersistence(this IServiceCollection, CompositePersistenceConfiguration config)` — composite
  - [ ] Registers all interfaces against the selected provider(s)

**Tests (~18):** → `Workflow.Tests/Persistence/AbstractionTests.cs`
- [ ] `PagedResult` computed properties (`HasNextPage`, `HasPreviousPage`) correct
- [ ] `VariableScope` factory methods produce correct values
- [ ] `VariableScope.Global` is singleton-like
- [ ] `VariableScope.ForWorkflow(id)` carries WorkflowId
- [ ] `VariableScope.ForExecution(id)` carries ExecutionId
- [ ] `Pagination` clamps PageSize to max (200)
- [ ] `PersistenceConfiguration` validates ProviderName not empty
- [ ] `HealthCheckResult.IsHealthy = true` when no error
- [ ] `ExecutionRecord` maps from `WorkflowExecutionContext` correctly
- [ ] `VariableEntry` round-trip serialization (JSON)
- [ ] `CompositePersistenceProvider` routes `IWorkflowRepository` to configured sub-provider
- [ ] `CompositePersistenceProvider` routes `IVariableStore` to a different sub-provider
- [ ] `CompositePersistenceProvider.HealthCheckAsync` aggregates healthy/unhealthy from all sub-providers
- [ ] `CompositePersistenceProvider.InitializeAsync` initialises all sub-providers
- [ ] Null variable: `VariableEntry { Value = null }` is distinct from `null` (not found)

---

## 2.1.1 In-Memory Persistence Provider 🧪

**Purpose:** A full in-memory implementation of all persistence interfaces — used for unit tests and local dev without Docker. Replaces the existing `InMemoryExecutionStateStore`~ ✨

**Complexity:** 🟢 Low

**New Project:** stays in `Workflow.Persistence` (no DB dependency)

### Tasks:

- [ ] **Create `InMemoryPersistenceProvider`** 🧪
  - [ ] New file: `Workflow.Persistence/InMemory/InMemoryPersistenceProvider.cs`
  - [ ] Implements `IPersistenceProvider`
  - [ ] `ProviderName = "memory"`
  - [ ] Wires up all in-memory repositories

- [ ] **Create `InMemoryWorkflowRepository`** 📋
  - [ ] New file: `Workflow.Persistence/InMemory/InMemoryWorkflowRepository.cs`
  - [ ] `ConcurrentDictionary<Guid, (WorkflowDefinition Definition, bool IsActive)>` as backing store
  - [ ] `DeleteAsync` sets `IsActive = false` — soft delete only
  - [ ] `PurgeAsync` physically removes entry from dictionary
  - [ ] `RestoreAsync` sets `IsActive = true`
  - [ ] `GetByIdAsync(id)` — returns null if soft-deleted; `GetByIdAsync(id, includeDeleted: true)` returns it
  - [ ] All other `IWorkflowRepository` operations
  - [ ] Search: substring match on name/description (active only by default)
  - [ ] Pagination: in-memory paging via LINQ Skip/Take

- [ ] **Create `InMemoryExecutionHistoryRepository`** 📊
  - [ ] New file: `Workflow.Persistence/InMemory/InMemoryExecutionHistoryRepository.cs`
  - [ ] Separate `ConcurrentDictionary` for executions and node executions
  - [ ] All `IExecutionHistoryRepository` operations
  - [ ] Filter/pagination via LINQ

- [ ] **Create `InMemoryVariableStore`** 💾
  - [ ] New file: `Workflow.Persistence/InMemory/InMemoryVariableStore.cs`
  - [ ] `ConcurrentDictionary<(VariableScope, string), List<VariableEntry>>` for version history
  - [ ] `SetVariableAsync(null)` — appends a new `VariableEntry` with `Value = null` (valid, versioned entry)
  - [ ] `DeleteVariableAsync` — removes the key entirely from the dictionary (hard removes all history)
  - [ ] `GetVariableAsync` — returns `null` if key missing; returns `VariableEntry { Value = null }` if latest version has null value
  - [ ] `GetAllVariablesAsync` — includes entries with null current value
  - [ ] Thread-safe via `lock` on mutation per key

- [ ] **Create `InMemoryBlobStore`** 🗃️
  - [ ] New file: `Workflow.Persistence/InMemory/InMemoryBlobStore.cs`
  - [ ] `ConcurrentDictionary<string, byte[]>` backing store
  - [ ] PresignedUrl: returns fake URL with expiry embedded

**Tests (~25):** → `Workflow.Tests/Persistence/InMemoryProviderTests.cs`
- [ ] Workflow CRUD round-trip
- [ ] `DeleteAsync` soft-deletes — `GetByIdAsync` returns null, `GetByIdAsync(includeDeleted: true)` returns workflow
- [ ] `PurgeAsync` hard-deletes — no longer returned even with `includeDeleted: true`
- [ ] `RestoreAsync` brings back soft-deleted workflow
- [ ] `GetAllAsync` with `IsActive = true` filter excludes soft-deleted
- [ ] Search by name substring
- [ ] Pagination returns correct page
- [ ] Execution record create → get round-trip
- [ ] Node execution records attached to correct execution
- [ ] Execution filter by state
- [ ] Variable `Set("x", "hello")` creates version 1
- [ ] Variable `Set("x", "world")` creates version 2, `Get` returns `"world"`
- [ ] Variable `Set("x", null)` creates version 3 — `Get` returns `VariableEntry { Value = null }` (not null!)
- [ ] Variable `Get(version: 1)` returns original value `"hello"`
- [ ] Variable `GetHistory` returns all 3 versions in order
- [ ] Variable `DeleteVariableAsync` removes key; subsequent `Get` returns `null` (not found)
- [ ] `GetAllVariablesAsync` includes variables with `null` current value
- [ ] `GetAllVariablesAsync` scoped — global, workflow, execution scopes isolated
- [ ] Blob `Put` → `Get` round-trip
- [ ] Blob `Exists` returns true after Put
- [ ] Blob `Delete` removes blob
- [ ] Concurrent writes don't corrupt variable version list (parallel writes, then read)
- [ ] `HealthCheck` always returns healthy for in-memory

---

## 2.1.2 PostgreSQL Persistence Provider 🐘

**Purpose:** Production-grade PostgreSQL persistence using Linq2Db with FluentMigrator migrations. This is the primary storage provider for production deployments~ 🗄️

**Complexity:** 🔴 High

**Target:** PostgreSQL 15+ (uses `jsonb` operators, `text[]` arrays, `gen_random_uuid()`, expression indexes)

**New Project:** `Workflow.Persistence.Postgres`

> CopilotNote: Use TestContainers for integration tests — no manual Postgres setup needed.
> Add `Testcontainers.PostgreSql` NuGet package to the test project.
> Use `postgres:15-alpine` image for speed~ 🐳

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
  - [ ] Support `"memory"`, `"postgres"`, `"nats"`, `"composite"` values
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
- [ ] Engine with in-memory provider: workflow runs + execution record created
- [ ] Engine with in-memory provider: node completions recorded
- [ ] Engine with in-memory provider: failed workflow records error
- [ ] Engine without provider: runs correctly (null provider fallback)
- [ ] Execution history captures correct node states
- [ ] Execution history captures variable updates per node

---

## Phase 2.1 Deliverables ✅

**Completion Criteria:**
- [ ] `IPersistenceProvider` + all sub-interfaces defined and documented
- [ ] `InMemoryPersistenceProvider` — full implementation for tests/dev
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

## New Projects Layout (updated for composite)
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
  InMemory/
    InMemoryPersistenceProvider.cs
    InMemoryWorkflowRepository.cs   ← + soft delete, PurgeAsync, RestoreAsync
    InMemoryExecutionHistoryRepository.cs
    InMemoryVariableStore.cs        ← + null-is-valid semantics
    InMemoryBlobStore.cs
  PersistenceConfiguration.cs
  ServiceCollectionExtensions.cs    ← + composite overload

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
    PostgresWorkflowRepository.cs   ← + soft delete, PurgeAsync, RestoreAsync
    PostgresExecutionHistoryRepository.cs
    PostgresVariableStore.cs        ← + null-is-valid, hard DeleteVariableAsync
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
Workflow.Engine/Actors/WorkflowExecutor.cs    ← wire IExecutionHistoryRepository (PipeToSelf, awaited)
Workflow.Engine/Actors/WorkflowSupervisor.cs  ← optionally use IWorkflowRepository
Workflow.Api/Program.cs                       ← DI registration incl. composite config
Workflow.Api/appsettings.json                 ← add Persistence section
Workflow.sln                                  ← add new projects
Directory.Packages.props                      ← add new NuGet packages
```

---

## ✅ All Clarifications Resolved

| # | Question | Answer | Impact |
|---|----------|--------|--------|
| **Q1** | Soft vs hard delete | Soft delete (`is_active`) default + `PurgeAsync` hard delete | Added `PurgeAsync`, `RestoreAsync`, `GetByIdAsync(includeDeleted)` to `IWorkflowRepository` + all implementations |
| **Q2** | `null` value = delete or valid? | Valid null-valued versioned entry; `DeleteVariableAsync` is the only delete | Updated `IVariableStore` docs, null semantics in Postgres (`SQL NULL`) and in-memory |
| **Q3** | Mutually exclusive or composable? | Composable via `CompositePersistenceProvider` | Added `CompositePersistenceProvider`, `CompositePersistenceConfiguration`, composite DI overload |
| **Q4** | Postgres target version | Postgres 15+ | Added to Postgres section header; use `jsonb` operators, `text[]`, `gen_random_uuid()` freely |
| **Q5** | Fire-and-forget or awaited? | **Awaited** for reliability | Added Akka `PipeToSelf` pattern to `WorkflowExecutor` wiring tasks |

---

> 💖 **Ami's Phase 2.1 Tips:**
> - Build 2.1.0 (abstractions) first — everything else depends on those interfaces~ ✨
> - Build 2.1.1 (in-memory) second — gives you a way to test 2.1.5 (engine integration) without needing Docker~ 🧪
> - Tackle 2.1.2 (Postgres) before 2.1.3 (NATS) — Postgres is the primary provider~ 🐘
> - S3 (2.1.4) can be done in parallel with NATS (2.1.3) since they're independent~ ☁️
> - Test composite routing with in-memory providers before touching real infra~ 🔀 UwU 💖

