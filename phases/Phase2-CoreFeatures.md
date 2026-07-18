# 🚀 Phase 2: Core Features (Weeks 7-14)

**Goal:** Implement essential workflow features and expand module library! 💫

[Back to Main Design Requirements](../design-requirements.md) | [All Phases](README.md)

---

## Overview

Phase 2 builds upon the foundation with critical production features:
- Pluggable persistence layer (PostgreSQL, NATS KV, S3)
- Advanced flow control (conditionals, loops, parallel execution)
- HTTP & Network modules with authentication
- Database modules for major providers
- File system & cloud storage modules
- Data transformation modules
- Complete REST API with authentication
- Module system enhancements (`.wfmod` packages, hot-reload, versioning, dependency resolution) *(deferred from Phase 1.4)*

**Timeline:** 8 weeks  
**Team Size:** 3-4 developers  
**Target Coverage:** 80%+

---

> **💡 Note to AI (Ami-Chan):** This file contains the COMPLETE Phase 2 implementation roadmap with ALL detailed tasks, tests, and deliverables. You can work directly from this file without needing to reference design-requirements.md! Everything you need is right here, uwu~! 💖

---

## Quick Navigation

- [2.1 Persistence Layer (Week 7-9)](#21-persistence-layer-week-7-9)
- [2.2 Advanced Flow Control (Week 9-10)](#22-advanced-flow-control-week-9-10)
- [2.3 HTTP & Network Modules (Week 10-11)](#23-http--network-modules-week-10-11)
- [2.4 Database Modules (Week 11-14) ✅](#24-database-modules-week-11-12)
- [2.5 File System Modules (Week 12-13)](#25-file-system-modules-week-12-13)
- [2.6 Data Transformation Modules (Week 13)](#26-data-transformation-modules-week-13)
- [2.7 REST API Implementation (Week 13-14)](#27-rest-api-implementation-week-13-14)
- [2.8 Module System Enhancements (Deferred from Phase 1.4)](#28-module-system-enhancements-deferred-from-phase-14-)
- [Phase 2 Success Criteria](#phase-2-success-criteria-)

---

## 🚀 Phase 2: Core Features (Weeks 7-14)

**Goal:** Implement essential workflow features and expand module library! 💫

### 2.1 Persistence Layer (Week 7-9)

> 📋 **See detailed sub-phases:** [Phase2-1-PersistenceLayer.md](./Phase2-1-PersistenceLayer.md)

> **Sub-phases:** 2.1.0 Abstractions · 2.1.1 SQLite · 2.1.2 PostgreSQL · 2.1.3 NATS KV · 2.1.4 S3 Blob · 2.1.5 Engine Integration
> **Estimated tests:** ~118 | **New projects:** Workflow.Persistence, Workflow.Persistence.Sqlite, Workflow.Persistence.Postgres, Workflow.Persistence.Nats, Workflow.Persistence.S3

**Tasks:**
- [x] **Implement pluggable persistence interface** 🔌
    - [x] Define `IPersistenceProvider` base interface
        - [x] Add `InitializeAsync()` method
        - [x] Add `HealthCheckAsync()` method
        - [x] Add `DisposeAsync()` method
        - [x] Add configuration properties
    - [x] Define persistence operations interfaces
        - [x] `IWorkflowRepository` - Workflow CRUD operations
        - [x] `IExecutionHistoryRepository` - Execution tracking
        - [x] `IVariableStore` - Variable storage with history
        - [x] `IBlobStore` - Large object storage
    - [x] Create provider factory pattern
        - [x] `IPersistenceProviderFactory`
        - [x] Registration mechanism for providers
        - [x] Configuration-based provider selection
    - [x] Add provider lifecycle management
    - [x] Implement provider health monitoring

- [ ] **Create PostgreSQL persistence provider (Linq2Db)** 🐘
    - [x] Install required NuGet packages
        - [x] `linq2db`
        - [x] `Npgsql`
        - [x] `FluentMigrator` for migrations
    - [ ] Design database schema
        - [x] Create `workflows` table
            - [x] `id` (uuid, primary key)
            - [x] `name` (varchar, indexed)
            - [x] `description` (text)
            - [x] `definition` (jsonb) - Full workflow definition
            - [x] `version` (varchar)
            - [x] `is_active` (boolean)
            - [x] `created_at` (timestamptz)
            - [x] `updated_at` (timestamptz)
            - [x] `tags` (text array)
            - [x] `metadata` (jsonb)
        - [x] Create `executions` table
            - [x] `id` (uuid, primary key)
            - [x] `workflow_id` (uuid, foreign key)
            - [x] `status` (enum: pending, running, completed, failed, cancelled)
            - [x] `started_at` (timestamptz)
            - [x] `completed_at` (timestamptz, nullable)
            - [x] `inputs` (jsonb)
            - [x] `outputs` (jsonb)
            - [x] `error` (jsonb, nullable)
            - [x] `triggered_by` (varchar)
            - [x] Create indexes on workflow_id, status, started_at
        - [x] Create `execution_nodes` table (node-level tracking)
            - [x] `id` (bigserial, primary key)
            - [x] `execution_id` (uuid, foreign key)
            - [x] `node_id` (varchar)
            - [x] `status` (enum)
            - [x] `started_at` (timestamptz)
            - [x] `completed_at` (timestamptz, nullable)
            - [x] `inputs` (jsonb)
            - [x] `outputs` (jsonb)
            - [x] `error` (jsonb, nullable)
            - [x] `duration_ms` (int)
            - [x] Create index on execution_id
        - [ ] Create `variables` table
            - [ ] `id` (bigserial, primary key)
            - [ ] `workflow_id` (uuid, nullable) - null for global
            - [ ] `execution_id` (uuid, nullable) - null for workflow-scope
            - [ ] `name` (varchar)
            - [ ] `value` (jsonb)
            - [ ] `value_type` (varchar)
            - [ ] `version` (int) - for historical tracking
            - [ ] `created_at` (timestamptz)
            - [ ] `updated_at` (timestamptz)
            - [ ] Create unique index on (execution_id, name, version)
        - [ ] Create `variable_history` table
            - [ ] Track all changes to variables over time
            - [ ] Include old value, new value, changed_at, changed_by
    - [x] Implement FluentMigrator migrations
        - [x] Migration_001_InitialSchema
        - [x] Migration_002_AddIndexes
        - [x] Add migration runner
        - [ ] Test rollback functionality
    - [x] Implement Linq2Db data context
        - [x] Create `WorkflowDataConnection` class
        - [x] Map tables to entities
        - [x] Configure connection string
        - [ ] Add connection pooling
    - [x] Implement `PostgresWorkflowRepository`
        - [x] Implement `CreateAsync(WorkflowDefinition)`
        - [x] Implement `UpdateAsync(Guid id, WorkflowDefinition)`
        - [x] Implement `DeleteAsync(Guid id)`
        - [x] Implement `GetByIdAsync(Guid id)`
        - [x] Implement `GetAllAsync(filter, pagination)`
        - [x] Implement `SearchAsync(query)`
        - [ ] Add optimistic concurrency handling
    - [x] Implement `PostgresExecutionHistoryRepository`
        - [x] Implement `CreateExecutionAsync(Execution)`
        - [x] Implement `UpdateExecutionStatusAsync(Guid id, status)`
        - [x] Implement `GetExecutionAsync(Guid id)`
        - [x] Implement `GetExecutionsForWorkflowAsync(Guid workflowId)`
        - [x] Implement `RecordNodeExecutionAsync(NodeExecution)`
        - [x] Implement query methods with filtering
        - [x] Add pagination support
    - [x] Implement `PostgresVariableStore`
        - [x] Implement `SetVariableAsync(scope, name, value)`
        - [x] Implement `GetVariableAsync(scope, name, version?)`
        - [x] Implement `GetVariableHistoryAsync(scope, name)`
        - [x] Implement `DeleteVariableAsync(scope, name)`
        - [x] Support versioned reads (time-travel queries)
    - [ ] Add transaction support
    - [ ] Implement retry logic for transient failures
    - [ ] Add query performance optimizations

- [ ] **Implement workflow definition storage** 📋
    - [ ] Add JSON serialization for WorkflowDefinition
    - [ ] Implement schema versioning
    - [ ] Add validation before storage
    - [ ] Implement workflow tagging system
    - [ ] Add search/filter by tags
    - [ ] Implement workflow versioning
        - [ ] Support multiple versions of same workflow
        - [ ] Track version history
        - [ ] Allow rollback to previous version

- [ ] **Implement execution history storage** 📊
    - [x] Store execution start/end times
    - [x] Store execution inputs/outputs
    - [x] Store node-level execution details
    - [x] Store error information with stack traces
    - [ ] Implement execution log aggregation
    - [ ] Add retention policies
        - [ ] Archive old executions
        - [ ] Delete very old executions
    - [ ] Implement execution replay capability
        - [ ] Store enough data to replay
        - [ ] Create replay functionality

- [ ] **Add variable persistence with historical tracking** 🕰️
    - [x] Implement versioned variable storage
    - [x] Track all changes with timestamps
    - [x] Support point-in-time queries
    - [x] Implement variable scopes
        - [x] Global scope (across all workflows)
        - [x] Workflow scope (shared in workflow)
        - [x] Execution scope (single execution)
    - [ ] Add variable expiration/TTL
    - [ ] Implement variable change notifications
    - [ ] Add audit trail for variable changes

- [ ] **Implement NATS KV persistence provider** 🚀
    - [x] Install `NATS.Net` NuGet package
    - [x] Implement `NatsPersistenceProvider`
    - [x] Configure NATS connection
        - [x] Connection string
        - [x] Authentication
        - [x] TLS configuration
    - [x] Implement key-value operations
        - [x] Put (create/update)
        - [x] Get (with optional revision)
        - [x] Delete
        - [x] Watch (for changes)
    - [ ] Implement workflow storage in KV buckets
        - [ ] Create bucket: `workflows`
        - [ ] Store as JSON with key pattern: `workflow:{id}`
    - [ ] Implement execution history in streams
        - [ ] Create stream: `executions`
        - [ ] Publish execution events
        - [ ] Query by workflow ID
    - [ ] Implement variable storage with history
        - [ ] Use NATS KV built-in history feature
        - [ ] Key pattern: `var:{scope}:{name}`
    - [ ] Add pub/sub for real-time updates
    - [ ] Implement connection resilience
    - [ ] Add retry logic

- [ ] **Implement S3 persistence provider (for large blobs)** ☁️
    - [x] Install `AWSSDK.S3` NuGet package
    - [x] Implement `S3PersistenceProvider`
    - [x] Configure S3 client
        - [x] Access key / Secret key
        - [x] Region
        - [x] Bucket name
        - [x] Endpoint URL (for S3-compatible services)
    - [x] Implement blob storage operations
        - [x] `PutObjectAsync` - Upload large data
        - [x] `GetObjectAsync` - Download data
        - [x] `DeleteObjectAsync` - Remove data
        - [x] `GeneratePresignedUrlAsync` - Temporary access URLs
    - [ ] Define key patterns
        - [ ] Workflows: `workflows/{id}/definition.json`
        - [ ] Executions: `executions/{id}/data.json`
        - [ ] Large outputs: `executions/{id}/nodes/{nodeId}/output.bin`
        - [ ] Logs: `executions/{id}/logs/{timestamp}.log`
    - [x] Implement multipart upload for large files
    - [x] Add content-type detection
    - [ ] Implement server-side encryption
    - [ ] Add lifecycle policies
        - [ ] Transition to Glacier after 90 days
        - [ ] Delete after 1 year
    - [ ] Implement object tagging for organization
    - [ ] Add CloudFront integration (optional)

**Key Interfaces:**
```csharp
✅ IPersistenceProvider - Base interface
✅ IWorkflowRepository - Workflow CRUD
✅ IExecutionHistoryRepository - History tracking
✅ IVariableStore - Variable storage
✅ IHistoricalTracker - Historical versioning
```

**Providers:**
```csharp
✅ PostgresPersistenceProvider (primary)
  - Workflow definitions table
  - Execution history table
  - Variable history table
  - Migration scripts

✅ NatsKVPersistenceProvider (optional)
  - Key-value storage
  - Stream-based history
  - Pub/sub integration

✅ S3PersistenceProvider (optional)
  - Large blob storage
  - Presigned URLs
  - Lifecycle policies
```

**Tests:**
- [ ] **Persistence provider interface tests** 🧪
    - [ ] Test provider initialization
    - [ ] Test provider disposal
    - [ ] Test health checks
    - [ ] Test configuration validation

- [ ] **PostgreSQL integration tests (with TestContainers)** 🐘
    - [x] Setup PostgreSQL container for tests
    - [x] Test workflow CRUD operations
        - [x] Create workflow
        - [x] Read workflow by ID
        - [x] Update workflow
        - [x] Delete workflow
        - [x] List all workflows
        - [x] Search workflows
    - [x] Test execution history operations
        - [x] Create execution record
        - [x] Update execution status
        - [x] Record node executions
        - [x] Query execution history
        - [x] Filter by date range
        - [x] Pagination
    - [x] Test variable operations
        - [x] Set variable (create)
        - [x] Get variable (latest version)
        - [x] Get variable (specific version)
        - [x] Get variable history
        - [x] Delete variable
    - [x] Test concurrent operations
    - [ ] Test transaction rollback
    - [ ] Test connection pool exhaustion

- [ ] **NATS KV integration tests** 🚀
    - [x] Setup NATS container for tests
    - [x] Test KV bucket operations
    - [x] Test workflow storage/retrieval
    - [x] Test variable versioning
    - [x] Test watch functionality
    - [ ] Test connection resilience

- [ ] **S3 integration tests** ☁️
    - [x] Setup MinIO container for tests (S3-compatible)
    - [x] Test object upload/download
    - [x] Test presigned URL generation
    - [x] Test large file upload (multipart)
    - [x] Test object deletion
    - [ ] Test lifecycle policies

- [ ] **Historical tracking tests** 🕰️
    - [x] Test variable version tracking
    - [x] Test point-in-time queries
    - [ ] Test history retention
    - [ ] Test audit trail completeness

- [ ] **Migration tests** 🔄
    - [ ] Test migration from v1 to v2 schema
    - [ ] Test rollback functionality
    - [ ] Test migration with existing data
    - [ ] Test idempotent migrations

**Deliverables:**
- ✅ Workflows persist to database correctly
- ✅ Execution history tracked with full details
- ✅ Variables support historical versioning
- ✅ Can switch providers via configuration
- ✅ All 3 persistence providers working
- ✅ Database migrations tested
- ✅ 85%+ test coverage on persistence layer

---

### 2.2 Advanced Flow Control (Week 9-10)

> 📋 **See detailed sub-phases:** [Phase2-2-AdvancedFlowControl.md](./Phase2-2-AdvancedFlowControl.md)

> **Sub-phases:** 2.2.0 Engine Prep · 2.2.1 Conditional/Switch · 2.2.2 Loops · 2.2.3 Parallel + Fan-out/in · 2.2.4 Try/Catch · 2.2.5 Expression Evaluator · 2.2.6 E2E Demo
> **Estimated tests:** ~76 | **New modules:** condition, switch, foreach, while, break, continue, parallel, fanout, fanin, trycatch, throw

**Tasks:**
- [ ] **Implement conditional branching (if/else)** 🔀
    - [ ] Create `ConditionalModule` class
        - [ ] ModuleId: `builtin.condition`
        - [ ] DisplayName: `Conditional Branch`
        - [ ] Category: `Flow Control`
    - [ ] Define module schema
        - [ ] Input: `condition` (boolean or expression string, required)
        - [ ] Input: `ifTrue` (connection port)
        - [ ] Input: `ifFalse` (connection port)
        - [ ] Output: `result` (boolean) - Which path was taken
    - [ ] Implement expression evaluation
        - [ ] Support boolean expressions (>, <, ==, !=, &&, ||)
        - [ ] Support variable references in expressions
        - [ ] Support node output references
        - [ ] Add expression parser/evaluator
    - [ ] Update engine to support multiple output ports
        - [ ] Modify node execution to handle conditional routing
        - [ ] Only activate connections matching the condition result
    - [ ] Add comprehensive tests
        - [ ] Test true path execution
        - [ ] Test false path execution
        - [ ] Test complex expressions
        - [ ] Test expression evaluation errors

- [ ] **Implement loop support (for-each, while)** 🔁
    - [ ] Create `ForEachModule` class
        - [ ] ModuleId: `builtin.loop.foreach`
        - [ ] DisplayName: `For Each`
        - [ ] Category: `Flow Control`
    - [ ] Define ForEach schema
        - [ ] Input: `collection` (array, required) - Items to iterate
        - [ ] Input: `loopBody` (connection to loop content)
        - [ ] Input: `maxIterations` (int, optional, default=1000) - Safety limit
        - [ ] Input: `continueOnError` (bool, optional, default=false)
        - [ ] Output: `results` (array) - Collected outputs from each iteration
        - [ ] Output: `count` (int) - Number of iterations
        - [ ] Output: `errors` (array) - Any errors encountered
    - [ ] Implement loop execution logic
        - [ ] Iterate over collection
        - [ ] For each item, execute loop body subgraph
        - [ ] Pass current item as input to loop body
        - [ ] Collect outputs from each iteration
        - [ ] Support break conditions
        - [ ] Support continue/skip logic
    - [ ] Create `WhileModule` class
        - [ ] ModuleId: `builtin.loop.while`
        - [ ] DisplayName: `While Loop`
    - [ ] Define While schema
        - [ ] Input: `condition` (expression or boolean)
        - [ ] Input: `loopBody` (connection)
        - [ ] Input: `maxIterations` (safety limit)
        - [ ] Output: `iterations` (int)
    - [ ] Implement while execution logic
        - [ ] Evaluate condition before each iteration
        - [ ] Execute loop body while condition is true
        - [ ] Enforce max iteration limit
    - [ ] Add loop state management to engine
        - [ ] Track current iteration
        - [ ] Track loop variables
        - [ ] Support nested loops
    - [ ] Add loop control flow
        - [ ] Break statement support
        - [ ] Continue statement support
    - [ ] Add comprehensive tests
        - [ ] Test simple foreach over array
        - [ ] Test while loop execution
        - [ ] Test break condition
        - [ ] Test max iteration limit
        - [ ] Test nested loops
        - [ ] Test empty collection

- [ ] **Implement parallel execution branches** ⚡
    - [ ] Create `ParallelModule` class
        - [ ] ModuleId: `builtin.parallel`
        - [ ] DisplayName: `Parallel Execution`
        - [ ] Category: `Flow Control`
    - [ ] Define module schema
        - [ ] Input: `branches` (array of connections)
        - [ ] Input: `maxDegreeOfParallelism` (int, optional)
        - [ ] Input: `waitForAll` (bool, optional, default=true)
        - [ ] Input: `failFast` (bool, optional, default=true)
        - [ ] Output: `results` (array) - Results from each branch
        - [ ] Output: `completedCount` (int)
        - [ ] Output: `failedCount` (int)
    - [ ] Implement parallel execution logic
        - [ ] Start all branches concurrently
        - [ ] Use Task.WhenAll for coordination
        - [ ] Respect maxDegreeOfParallelism
        - [ ] Collect results from all branches
        - [ ] Handle partial failures (if not failFast)
    - [ ] Update engine for parallel node execution
        - [ ] Create multiple NodeExecutor actors simultaneously
        - [ ] Coordinate completion using parent actor
        - [ ] Aggregate results
    - [ ] Add comprehensive tests
        - [ ] Test 3-way parallel split
        - [ ] Test with different parallelism limits
        - [ ] Test partial failure handling
        - [ ] Test fail-fast behavior
        - [ ] Test result aggregation

- [ ] **Implement fan-out/fan-in patterns** 🌟
    - [ ] Implement fan-out logic
        - [ ] Split execution into multiple paths
        - [ ] Pass same input to all branches
        - [ ] Track all spawned branches
    - [ ] Implement fan-in logic
        - [ ] Wait for all branches to complete
        - [ ] Merge results from all branches
        - [ ] Handle branch failures
    - [ ] Create `FanOutModule` class
        - [ ] ModuleId: `builtin.fanout`
        - [ ] Distribute inputs to multiple branches
    - [ ] Create `FanInModule` class
        - [ ] ModuleId: `builtin.fanin`
        - [ ] Collect and merge branch outputs
    - [ ] Add synchronization primitives
        - [ ] Barrier for fan-in coordination
        - [ ] Semaphore for controlled parallelism
    - [ ] Add comprehensive tests
        - [ ] Test fan-out to 5 branches
        - [ ] Test fan-in aggregation
        - [ ] Test combined fan-out/fan-in
        - [ ] Test unbalanced execution times

- [ ] **Add error handling nodes (try-catch)** 🛡️
    - [ ] Create `TryCatchModule` class
        - [ ] ModuleId: `builtin.trycatch`
        - [ ] DisplayName: `Try-Catch`
        - [ ] Category: `Error Handling`
    - [ ] Define module schema
        - [ ] Input: `tryBlock` (connection) - Nodes to try
        - [ ] Input: `catchBlock` (connection) - Error handler
        - [ ] Input: `finallyBlock` (connection, optional)
        - [ ] Output: `error` (object, nullable) - Caught exception
        - [ ] Output: `success` (boolean) - Whether try succeeded
    - [ ] Implement try-catch execution logic
        - [ ] Execute try block
        - [ ] On error, execute catch block
        - [ ] Pass error details to catch block
        - [ ] Always execute finally block (if present)
        - [ ] Propagate or suppress error based on config
    - [ ] Update engine for error boundaries
        - [ ] Implement error containment
        - [ ] Allow recovery from errors
        - [ ] Support error transformation
    - [ ] Create `ThrowModule` class
        - [ ] ModuleId: `builtin.throw`
        - [ ] Manually throw errors
    - [ ] Add comprehensive tests
        - [ ] Test successful try block
        - [ ] Test error caught and handled
        - [ ] Test finally block execution
        - [ ] Test nested try-catch
        - [ ] Test error re-throwing

- [ ] **Engine enhancements for flow control** ⚙️
    - [ ] Support for multiple output ports
        - [ ] Update NodeDefinition to support multiple outputs
        - [ ] Update connection logic to reference specific output ports
        - [ ] Update execution engine to handle multi-port routing
    - [ ] Parallel node execution coordinator
        - [ ] Create ParallelExecutionCoordinator actor
        - [ ] Implement barrier synchronization
        - [ ] Track completion of parallel branches
        - [ ] Aggregate results
    - [ ] Loop state management
        - [ ] Create LoopContext class
        - [ ] Track iteration variables
        - [ ] Support loop control flow (break/continue)
        - [ ] Handle nested loops
    - [ ] Conditional expression evaluator
        - [ ] Parse expression strings
        - [ ] Evaluate with variable context
        - [ ] Support common operators
        - [ ] Add type coercion
    - [ ] Error boundary handling
        - [ ] Implement error containment zones
        - [ ] Support error recovery
        - [ ] Allow selective error propagation

**Modules:**
```
✅ builtin.condition - If/else branching
✅ builtin.loop.foreach - Iteration over collections
✅ builtin.loop.while - While loop
✅ builtin.parallel - Parallel execution
✅ builtin.fanout - Fan-out pattern
✅ builtin.fanin - Fan-in pattern
✅ builtin.trycatch - Error handling
✅ builtin.switch - Multi-way branching
✅ builtin.throw - Throw errors
```

**Engine Updates:**
```csharp
✅ Support for multiple output ports
✅ Parallel node execution coordinator
✅ Loop state management
✅ Conditional expression evaluator
✅ Error boundary handling
```

**Tests:**
- [ ] **Conditional branching tests (true/false paths)** 🔀
    - [ ] Test simple if-else
    - [ ] Test nested conditionals
    - [ ] Test complex expressions
    - [ ] Test with variable references
    - [ ] Test both paths in same workflow

- [ ] **Loop execution tests (arrays, ranges)** 🔁
    - [ ] Test foreach over array of 10 items
    - [ ] Test foreach with early break
    - [ ] Test while loop with counter
    - [ ] Test nested foreach loops
    - [ ] Test empty collection
    - [ ] Test max iteration limit

- [ ] **Parallel execution tests (fan-out/fan-in)** ⚡
    - [ ] Test 3-way parallel execution
    - [ ] Test parallel with different durations
    - [ ] Test parallel with one failure
    - [ ] Test fan-out followed by fan-in
    - [ ] Test result aggregation
    - [ ] Test degree of parallelism limiting

- [ ] **Error handling flow tests** 🛡️
    - [ ] Test try-catch-finally execution
    - [ ] Test error caught and handled
    - [ ] Test finally always executes
    - [ ] Test error re-throwing
    - [ ] Test nested try-catch
    - [ ] Test multiple errors in parallel

- [ ] **Complex nested flow tests** 🌀
    - [ ] Test loop with conditionals inside
    - [ ] Test parallel branches with loops
    - [ ] Test try-catch around parallel execution
    - [ ] Test conditional with parallel branches
    - [ ] Test deeply nested structures (5+ levels)

**Deliverables:**
- ✅ Can execute workflows with conditionals
- ✅ Can iterate over collections
- ✅ Can execute branches in parallel
- ✅ Errors can be caught and handled

---

### 2.3 HTTP & Network Modules (Week 10-11)

> 📖 **Detailed sub-phase breakdown:** See [Phase2-3-HttpAndNetworkModules.md](Phase2-3-HttpAndNetworkModules.md) for the full slice plan (2.3.0–2.3.8 + Post-MVP P1–P7), design decisions, file lists, and per-slice test targets~ 🌷
>
> **Sub-phases:** 2.3.0 Infra + core `HttpRequestModule` · 2.3.1 Body/Response formats · 2.3.2 Basic/Bearer/API Key auth · 2.3.3 OAuth2 client credentials · 2.3.4 Polly retry/timeout/circuit · 2.3.5 Request/response transformation · 2.3.6 Webhook trigger + API · 2.3.7 Webhook signature validation · 2.3.8 E2E demo + docs · *(GraphQL deferred → 2.3.P6)*

**Tasks:**
- [x] **HTTP infrastructure & DI setup** 🏗️ ✅ **(May 19, 2026)**
    - [x] New folder: `Workflow.Modules/Builtin/Http/`
    - [x] Add `Microsoft.Extensions.Http` to `Directory.Packages.props`
    - [x] `HttpModuleServiceCollectionExtensions` — registers `IHttpClientFactory` + named client `"dotflow.http"` (`SocketsHttpHandler`, `PooledConnectionLifetime=2min`, `MaxConnectionsPerServer=256`, `ConnectTimeout=30s`)
    - [x] `AddWorkflowModules(this IServiceCollection)` aggregate DI registration — single call for all module families; `Workflow.Api/Program.cs` updated
    - [x] `BuiltinModuleRegistration.GetAll()` updated (count 16 → 18: `HttpRequestModule` + `WebhookTriggerModule`)

- [x] **Implement `HttpRequestModule` - Full HTTP client** 🌐 ✅ **(May 19–20, 2026)**
    - [x] Create `HttpRequestModule` class
        - [x] ModuleId: `builtin.http.request`
        - [x] DisplayName: `HTTP Request`
        - [x] Category: `Network`
    - [x] Define module schema
        - [x] Input: `url` (string, required) - Target URL
        - [x] Input: `method` (enum, required) - GET, POST, PUT, DELETE, PATCH, HEAD, OPTIONS
        - [x] Input: `headers` (dictionary, optional) - Custom headers
        - [x] Input: `body` (object, optional) - Request body
        - [x] Input: `contentType` (string, optional) - Content-Type header
        - [x] Input: `timeout` (TimeSpan, optional, default=30s) — implemented as `timeoutSeconds` (int)
        - [ ] Input: `followRedirects` (bool, optional, default=true)
        - [ ] Input: `maxRedirects` (int, optional, default=10)
        - [ ] Input: `validateCertificate` (bool, optional, default=true)
        - [x] Input: `retryCount` (int, default=0 opt-in)
        - [x] Input: `retryDelaySeconds` (double, default=1.0)
        - [x] Input: `retryBackoff` (string: `linear`/`exponential`/`constant`, default=`exponential`)
        - [x] Input: `maxRetryBackoffSeconds` (double, default=60.0) — Retry-After header cap
        - [x] Input: `retryOnStatusCodes` (int[], default=[408,429,500,502,503,504])
        - [x] Input: `circuitBreakerFailureThreshold` (int, default=0 disabled)
        - [x] Input: `circuitBreakerSamplingDurationSeconds` (double, default=30)
        - [x] Input: `authType` (string: none/basic/bearer/apikey/oauth2, default=none)
        - [x] Input: `username`, `password` (Basic auth)
        - [x] Input: `bearerToken` (Bearer auth)
        - [x] Input: `apiKey`, `apiKeyHeader`, `apiKeyLocation` (API Key auth)
        - [x] Input: `oauth2TokenUrl`, `oauth2ClientId`, `oauth2ClientSecret`, `oauth2Scope`, `oauth2Audience`, `oauth2TokenCacheScope` (OAuth2)
        - [x] Input: `responseExtract` (HashMap<string,string>, optional) — JSONPath extraction map
        - [x] Input: `responseExtractRequired` (bool, optional, default=false)
        - [x] Input: `responseRegex` (HashMap<string,string>, optional) — regex named-capture extraction
        - [x] Input: `headerExtract` (HashMap<string,string>, optional) — response header extraction
        - [x] Output: `statusCode` (int) - HTTP status code
        - [x] Output: `headers` (dictionary) - Response headers
        - [x] Output: `body` (object) - Response body
        - [x] Output: `success` (bool) - Status code 200-299
        - [x] Output: `duration` (TimeSpan) - Request duration (as `durationMs` long)
        - [x] Output: `contentType` (string) - Response Content-Type
        - [x] Output: `attemptCount` (int) - Total send attempts (1 = no retry)
        - [x] Output: `circuitState` (string) - `closed`/`open`/`halfopen`
    - [x] Implement ExecuteAsync method
        - [x] Create HttpClient instance (or use injected) — via `IHttpClientFactory` named client
        - [x] Build HTTP request message
        - [x] Add all custom headers
        - [x] Serialize body based on content type
        - [x] Set timeout
        - [x] Send request
        - [x] Parse response
        - [x] Deserialize response body
        - [x] Return all outputs
    - [x] Add request body serialization (`RequestBodyEncoder`)
        - [x] JSON serialization
        - [x] XML serialization — string passthrough (well-formedness validated via XmlDocument.LoadXml)
        - [x] Form URL encoded
        - [x] Multipart form data — V1: `byte[]` + string parts only (Stream/file-path deferred → 2.3.P4/P5)
        - [x] Raw string/bytes
    - [x] Add response body deserialization (`ResponseBodyDecoder`)
        - [x] Auto-detect content type
        - [x] JSON deserialization → `Dictionary<string,object?>` POCO graph
        - [x] XML deserialization — string passthrough (full XML→object map deferred)
        - [x] Text content
        - [x] Binary content
    - [x] Add auth header redaction in logs 🔒
        - [x] Redacts `Authorization`, `X-API-Key`, `X-Auth-Token`, `Cookie`, etc. in debug logs
    - [x] Add comprehensive tests ✅ **13/13 + 9/9 HttpBodyFormat**
        - [x] Test all HTTP methods
        - [x] Test custom headers
        - [x] Test request body serialization (JSON, form, multipart, XML, bytes)
        - [x] Test response parsing (JSON dict, text, binary)
        - [x] Test error handling (404, 500, etc.)

- [x] **Add authentication support** 🔐 ✅ **(May 19, 2026)**
    - [x] Implement Basic Authentication
        - [x] Add `authType` input (enum)
        - [x] Add `username` and `password` inputs
        - [x] Generate Authorization header
    - [x] Implement Bearer Token Authentication
        - [x] Add `bearerToken` input
        - [x] Add token to Authorization header
    - [x] Implement API Key Authentication
        - [x] Add `apiKey` and `apiKeyHeader` inputs
        - [x] Support query parameter API keys
    - [x] Implement OAuth2 Support
        - [x] Add OAuth2 client credentials flow
        - [x] Token caching — `PerModuleOAuth2TokenCache` + `PerPipelineOAuth2TokenCache`
        - [x] Automatic token refresh — refresh-on-401 retry (once)
        - [x] Selectable `oauth2TokenCacheScope`: `module` (per-instance) or `pipeline` (per-execution)
        - [ ] Singleton/persisted cross-workflow token cache — deferred → **2.3.P3**
    - [x] Add comprehensive tests ✅ **9/9 HttpAuth + 10/10 OAuth2**
        - [x] Test each auth type
        - [x] Test auth failures
        - [x] Test token refresh

- [x] **Implement retry logic and timeouts** 🔄 ✅ **(May 19, 2026)**
    - [x] Add retry configuration
        - [x] Input: `retryCount` (int, optional, default=0 — opt-in)
        - [x] Input: `retryDelaySeconds` (double, optional, default=1.0)
        - [x] Input: `retryBackoff` (string: linear/exponential/constant — Fibonacci not in Polly v8, falls back to exponential)
        - [x] Input: `retryOnStatusCodes` (array, optional, default=[408,429,500,502,503,504])
    - [x] Implement retry logic with Polly
        - [x] Install Polly.Core v8 NuGet package
        - [x] Create retry policy (`HttpResiliencePipelineFactory` — cached per-config-hash per module instance)
        - [x] Handle transient failures (408, 429, 500-599) + `HttpRequestException`
        - [x] Exponential backoff implementation
        - [x] Jitter for retry delays (`UseJitter = true`)
        - [x] Retry-After header honouring for 429/503 (capped by `maxRetryBackoffSeconds`)
        - [x] Requests rebuilt per attempt (HTTP messages aren't resendable)
    - [x] Implement circuit breaker pattern
        - [x] Open circuit after N failures (`circuitBreakerFailureThreshold`)
        - [x] Half-open state for testing recovery
        - [x] Close circuit when stable
    - [x] Add timeout handling
        - [x] Request-level timeout — `CancellationTokenSource.CancelAfter(timeoutSeconds)`
        - [x] Operation-level timeout
        - [x] Cancellation token support (hierarchical CT from 2.2.0b)
    - [x] Add comprehensive tests ✅ **11/11 HttpRetry**
        - [x] Test retry on 500 error
        - [x] Test exponential backoff timing
        - [x] Test max retry limit
        - [x] Test circuit breaker opening
        - [x] Test timeout cancellation

- [x] **Add request/response transformation** 🔄 ✅ **(May 20, 2026)**
    - [x] Implement request transformation
        - [x] Template strings in URL — `{{variable.name}}` double-brace syntax via `PropertyBinder`
        - [x] Variable interpolation in body
        - [x] Dynamic header generation
        - [ ] Request middleware pipeline
    - [x] Implement response transformation
        - [x] JSONPath queries on response — `JsonPathExtractor` using `JsonPath.Net` (v0.8.1, MIT)
        - [ ] XPath queries on XML response — deferred (low demand)
        - [x] Regex extraction from text — named-capture `(?<value>...)` with 5s match timeout
        - [x] Response mapping to outputs
    - [x] Add data extraction helpers
        - [x] Extract specific fields (JSONPath single, multi-value → array)
        - [ ] Flatten nested objects
        - [ ] Array manipulation
        - [ ] Composite JSONPath extract (multiple paths → single object port) — deferred → **2.3.P7**
    - [x] Add comprehensive tests ✅ **8/8 HttpTransformation**
        - [x] Test URL templating
        - [x] Test JSONPath extraction (single, nested, array, missing)
        - [x] Test regex named-capture extraction
        - [x] Test response header extraction
        - [x] Test response mapping

- [x] **Implement webhook trigger module** 🪝 ✅ **(May 21, 2026)**
    - [x] Create `WebhookTriggerModule` class
        - [x] ModuleId: `builtin.http.webhook`
        - [x] DisplayName: `Webhook Trigger`
        - [x] Category: `Triggers`
    - [x] Create `WebhookRegistration` model (`Workflow.Core/Models/WebhookRegistration.cs`)
        - [x] Fields: `WebhookId`, `WorkflowDefinitionId`, `AllowedMethods`, `SecretKey`, `SignatureScheme`, `CreatedAt`, `Enabled`
        - [x] `WebhookRegistration.Validate()` — non-empty ID, Guid, methods; scheme+secret consistency
    - [x] Create `IWebhookRegistrationRepository` (`Workflow.Persistence/Abstractions/`)
        - [x] CRUD: `RegisterAsync`, `UpdateAsync`, `DeleteAsync`, `GetAsync`, `ListAsync`
        - [x] `InMemoryWebhookRegistrationRepository` — default impl (ConcurrentDictionary, OrdinalIgnoreCase)
        - [ ] SQLite impl / IPersistenceProvider slot — deferred
    - [x] Define module schema
        - [x] Configuration: `webhookId` (string, required — must match a registration)
        - [ ] Configuration: `path` (string) - Arbitrary URL path routing — deferred → **2.3.P1**
        - [x] Configuration: `method` (`AllowedMethods`, normalised to UPPER-CASE)
        - [x] Configuration: `secretKey` (string, optional) - For signature validation
        - [x] Output: `headers` (dictionary)
        - [x] Output: `body` (object)
        - [x] Output: `query` (dictionary)
        - [x] Output: `method` (string) — HTTP method of triggering request
        - [x] Output: `receivedAt` (DateTimeOffset) — timestamp of trigger
    - [x] Implement webhook endpoint in API (`WebhookEndpoints.cs` — minimal-API)
        - [x] `ANY /webhooks/{webhookId}` — trigger endpoint (all HTTP methods; method check in dispatcher)
        - [x] Parse incoming request body as raw bytes (before JSON — required for HMAC hashing)
        - [x] Validate signature (if configured) via `WebhookDispatcher`
        - [x] Trigger workflow execution via `IWorkflowLauncher` (stub → `NullWorkflowLauncher`; `ActorWorkflowLauncher` wired in 2.3.8)
        - [x] Return `202 Accepted + { executionId }` via `IWebhookResponseStrategy` (Async202, forward-compat for sync → **2.3.P2**)
        - [x] `WebhookDispatcher` service — encapsulates lookup+dispatch (forward-compat for path router → **2.3.P1**)
    - [x] Add signature validation (`Workflow.Api/Webhooks/IWebhookSignatureValidator.cs`) 🔒
        - [x] HMAC-SHA256 validation (`HmacSha256SignatureValidator` — `X-Signature: {hex}`)
        - [x] GitHub support (`GitHubSignatureValidator` — `X-Hub-Signature-256: sha256={hex}`)
        - [x] Stripe support (`StripeSignatureValidator` — `Stripe-Signature: t={ts},v1={hex}` with 5-min tolerance)
        - [x] Replay protection — Stripe validator rejects events outside timestamp tolerance
        - [x] `WebhookSignatureValidatorRegistry` — `Resolve(scheme)` + `IsKnownScheme(scheme)`
        - [x] Fail-safe `401` + internal-only error log (no oracle exposure)
    - [x] Add webhook management endpoints (`POST/GET/PUT/DELETE /api/webhooks`)
        - [x] Register webhook
        - [x] Update webhook
        - [x] Delete webhook
        - [x] List webhooks
        - [x] `ProgramPublic.cs` — exposes `Program` type for `WebApplicationFactory<Program>`
    - [x] Add comprehensive tests ✅ **12/12 WebhookTrigger + 7/7 WebhookSignature**
        - [x] Test webhook trigger (unit + API integration via WebApplicationFactory)
        - [x] Test signature validation (HMAC, GitHub, Stripe)
        - [x] Test invalid signatures (wrong hash, missing header, unknown scheme)
        - [x] Test different HTTP methods (allowed vs. disallowed → 405)
        - [x] Test invalid webhook ID (→ 404)

- [x] **End-to-end demo & persistence integration** 🎯 ✅ **(May 21, 2026)**
    - [x] `examples/definitions/http-integration-demo.json` — WebhookTrigger → TryCatch → Parallel[GET w/ retry | POST w/ bearer] → catch→Log → finally→SetVariable
    - [x] Persistence tests — WireMock + `SqlitePersistenceProvider(:memory:)` + `WorkflowSupervisor` (Akka TestKit)
    - [x] E2E tests — full demo shape validates both APIs called + TryCatch recovery path ✅ **4/4**
    - [x] `docs/http-and-network.md` — full module reference docs

**Modules:**
```
✅ builtin.http.request - All HTTP methods
✅ builtin.http.webhook - Webhook triggers
[ ] builtin.http.graphql - GraphQL queries (deferred → 2.3.P6)
✅ builtin.http.soap - SOAP client — deferred indefinitely (D7)
```

**Features:**
- [x] **All HTTP methods** (GET, POST, PUT, DELETE, PATCH, HEAD, OPTIONS) ✅
    - [x] Implement each method
    - [x] Test each method

- [x] **Custom headers** ✅
    - [x] Accept any header key-value pair
    - [x] Validate header names (redact sensitive headers in logs)
    - [ ] Support multiple values for same header

- [x] **Multiple auth types** ✅
    - [x] Basic, Bearer, API Key, OAuth2
    - [x] Test all combinations

- [x] **Retry with exponential backoff** ✅
    - [x] Implement using Polly v8 (ResiliencePipeline API)
    - [x] Test backoff timing

- [x] **Request/response body transformation** ✅
    - [x] JSONPath, Regex (XPath deferred)
    - [x] Test transformations

- [ ] **SSL/TLS configuration**
    - [ ] Certificate validation toggle
    - [ ] Custom certificate support
    - [ ] Test with self-signed certs

- [ ] **Proxy support**
    - [ ] HTTP proxy configuration
    - [ ] SOCKS proxy support
    - [ ] Proxy authentication

**Post-MVP Slices** *(tracked, non-blocking 2.4+)* 🚧
- [ ] **2.3.P1** Arbitrary-Path Webhook Routing — `Path`/`PathPattern` fields + `WebhookPathRouter` + new catch-all endpoint (~6 tests)
- [ ] **2.3.P2** Sync Webhook Responses — `WaitForFirstOutputStrategy`, `WaitForCompletionStrategy`, `IWebhookResponseStrategy` impls + `VariableUpdated` engine event (~8 tests)
- [ ] **2.3.P3** OAuth2 Singleton/Persisted Token Cache — `SingletonOAuth2TokenCache` DI singleton + optional `IOAuth2TokenStore` over `IPersistenceProvider` (~5 tests)
- [ ] **2.3.P4** Multipart Stream Support — `RequestBodyEncoder` recognises `Stream` parts (~4 tests)
- [ ] **2.3.P5** Multipart File-Path Support — `allowFileUpload` capability flag + path allowlist (~6 tests)
- [ ] **2.3.P6** GraphQL Module — `builtin.http.graphql`, partial-success detection, auth reuse (~6 tests)
- [ ] **2.3.P7** Composite JSONPath Extract — `responseExtractComposite` schema key, multi-path → single object port (~5 tests)

**Tests:**
- [x] **HTTP request tests (with WireMock.Net in-process)** 🧪 ✅ **13/13**
    - [x] Setup WireMock container (in-process, Docker-free)
    - [x] Test GET request
    - [x] Test POST with JSON body
    - [x] Test PUT with XML body
    - [x] Test DELETE request
    - [x] Test response parsing
    - [x] Test error responses (404, 500)

- [x] **Authentication flow tests** 🔐 ✅ **9/9 + 10/10 OAuth2**
    - [x] Test Basic auth success
    - [x] Test Basic auth failure
    - [x] Test Bearer token
    - [x] Test API key in header
    - [x] Test API key in query
    - [x] Test OAuth2 token flow (fetch, cache, expire, 401-retry)

- [x] **Retry logic tests** 🔄 ✅ **11/11**
    - [x] Test retry on 500 error
    - [x] Test retry on 429 (rate limit) — Retry-After header honoured up to cap
    - [x] Test exponential backoff
    - [x] Test max retries exceeded
    - [x] Test retry gives up on 404

- [x] **Timeout tests** ⏱️ ✅
    - [x] Test request timeout
    - [x] Test connection timeout
    - [x] Test cancellation token honoured mid-flight

- [x] **Webhook trigger tests** 🪝 ✅ **12/12 + 7/7 signature**
    - [x] Test webhook receives request
    - [x] Test workflow triggered (SpyWorkflowLauncher DI override)
    - [x] Test signature validation (HMAC, GitHub, Stripe)
    - [x] Test invalid webhook ID (→ 404)

- [x] **Persistence & E2E tests** 💾 ✅ **4/4**
    - [x] `HttpNodeExecution_PersistedWithStatusCodeMetadata`
    - [x] `WebhookTriggeredExecution_PersistedWithWebhookIdMetadata`
    - [x] `Demo_TriggeredByWebhook_BothApisCalled_AuditPersisted`
    - [x] `Demo_OneApiFails_TryCatchRecovers_WorkflowCompletes`

**Deliverables:**
- ✅ Can make authenticated HTTP requests to any API
- ✅ Workflows triggered via webhooks reliably
- ✅ Retry logic works correctly with backoff
- ✅ All HTTP methods supported
- ✅ Response transformation working (JSONPath, regex, header extraction)
- ✅ Webhook signature validation (HMAC, GitHub, Stripe) with replay protection
- ✅ E2E demo workflow runs on persistence + API stack
- ✅ `docs/http-and-network.md` documentation complete
- ✅ 83 unit + integration tests passing across 2.3.0–2.3.8
- ✅ 90%+ test coverage on HTTP modules

---

### 2.4 Database Modules (Week 11-14) ✅ COMPLETE

> 📋 **See detailed sub-phases:** [Phase2-4-DatabaseModules.md](./Phase2-4-DatabaseModules.md)
> 🧭 **Design exploration:** [new-feature-design/Phase2-4-DatabaseModules-Design.md](../new-feature-design/Phase2-4-DatabaseModules-Design.md)
> 📖 **Feature guide:** [docs/database-modules.md](../docs/database-modules.md)

> **Sub-phases:** 2.4.a.0 Shared Infra · 2.4.a.1 Query · 2.4.a.2 Execute · 2.4.a.3 Transaction · 2.4.a.4 BulkInsert · 2.4.a.5 Named Connections + API · 2.4.a.6 E2E + Docs · **2.4.b.0–6 Typed Linq/Roslyn family**
> **Estimated tests:** ~70 (MVP) + ~41 (post-MVP slices) | **New projects:** Workflow.Modules.Database + Workflow.Modules.Database.Linq

> **Note:** This section preserves the *original* Phase 2.4 task list below as a reference. The **authoritative, sliced plan lives in [`Phase2-4-DatabaseModules.md`](./Phase2-4-DatabaseModules.md)** — it incorporates the design-doc resolutions (named connections, deferred MySQL/SQL Server, shared infra extraction, and the typed linq/Roslyn family) that are not reflected in the legacy list below~ 🌸

---

#### ✅ Completion summary (July 2026 — typed-first re-plan, Option D)

Per product direction ("users should not have to hand-write raw SQL unless absolutely necessary"), Phase 2.4 shipped **both** families across Weeks 11-14:

- [x] **🌟 Typed linq — `builtin.database.linq` (the recommended default surface, 2.4.b):** author strongly-typed C# linq2db against an imported table catalog; Roslyn-validated with a security allowlist (usings + forbidden-syntax walker); compiled at publish time; HMAC-signed assemblies cached in `IBlobStore`; executed in a **reused collectible ALC** (bounded, no leak under load); in-memory SQLite **always-rollback** sandbox preview; one-shot catalog import; `POST /api/database/linq/{validate,preview,compile}` + `POST /api/database/catalog/{id}/import` (trusted-author gate). Dual-POCO table typing (column-generated **or** plugin POCOs).
- [x] **🧰 Raw SQL escape-hatch family (2.4.a):** `builtin.database.{query,execute,transaction,bulkinsert}` — parameterised (never string-concatenated), Postgres + SQLite, single + batch (`parameterSets`) transactions, hand-built multi-row bulk insert with optional `RETURNING`.
- [x] **Shared infra:** `IDbConnectionFactory` · `IDbProviderRegistry` · `IDbConnectionRegistry` · `IDbTransactionScope` · `IWorkflowTableCatalog`, plus named connections (config + runtime CRUD API + encrypted-at-rest credentials).
- [x] **Roslyn quarantined** in `Workflow.Modules.Database.Linq` (opt-in `AddDatabaseLinqModules()`, D14) — raw-SQL-only deployments don't pay the ~30MB.
- [x] **Tests:** ~132 passing (2.4.a: 66 unit + 13 registry/API; 2.4.b: 53 unit + 3 security) + Docker-gated Postgres & E2E suites (compile-verified); **0 build errors**.
- [x] **Docs:** [`docs/database-modules.md`](../docs/database-modules.md) restructured typed-first (authoring guide + security model), added to `DOCUMENTATION_INDEX.md`.

> Post-MVP slices (2.4.a.P1–P6, 2.4.b.P1–P4) remain tracked in the sliced doc — non-blocking for Phase 2.5+.

---

**Tasks:** *(legacy reference list — superseded by the sliced plan above)*
- [ ] **Implement generic SQL query module** 🗄️
    - [ ] Create `DatabaseQueryModule` class
        - [ ] ModuleId: `builtin.database.query`
        - [ ] DisplayName: `Database Query`
        - [ ] Category: `Database`
    - [ ] Define module schema
        - [ ] Input: `connectionString` (string, required) - DB connection
        - [ ] Input: `provider` (enum, required) - PostgreSQL, MySQL, SQL Server, SQLite
        - [ ] Input: `query` (string, required) - SELECT query
        - [ ] Input: `parameters` (dictionary, optional) - Query parameters
        - [ ] Input: `timeout` (TimeSpan, optional, default=30s)
        - [ ] Input: `commandType` (enum, optional) - Text or StoredProcedure
        - [ ] Output: `rows` (array) - Result rows
        - [ ] Output: `rowCount` (int) - Number of rows returned
        - [ ] Output: `columns` (array) - Column names
    - [ ] Implement ExecuteAsync method
        - [ ] Create database connection using Linq2Db
        - [ ] Prepare parameterized query
        - [ ] Execute query
        - [ ] Map results to dictionaries
        - [ ] Handle null values
        - [ ] Return structured output
    - [ ] Add support for stored procedures
    - [ ] Add comprehensive tests
        - [ ] Test simple SELECT
        - [ ] Test with parameters
        - [ ] Test with stored procedure
        - [ ] Test empty result set

- [ ] **Implement SQL command execution (INSERT/UPDATE/DELETE)** ✏️
    - [ ] Create `DatabaseExecuteModule` class
        - [ ] ModuleId: `builtin.database.execute`
        - [ ] DisplayName: `Execute SQL`
        - [ ] Category: `Database`
    - [ ] Define module schema
        - [ ] Input: `connectionString` (string, required)
        - [ ] Input: `provider` (enum, required)
        - [ ] Input: `command` (string, required) - SQL command
        - [ ] Input: `parameters` (dictionary, optional)
        - [ ] Input: `timeout` (TimeSpan, optional)
        - [ ] Output: `affectedRows` (int) - Rows affected
        - [ ] Output: `lastInsertId` (long, nullable) - For INSERT operations
        - [ ] Output: `success` (bool)
    - [ ] Implement ExecuteAsync method
        - [ ] Create connection
        - [ ] Prepare command with parameters
        - [ ] Execute non-query
        - [ ] Return affected rows
        - [ ] Get last insert ID (if applicable)
    - [ ] Add comprehensive tests
        - [ ] Test INSERT
        - [ ] Test UPDATE
        - [ ] Test DELETE
        - [ ] Test with parameters
        - [ ] Test SQL error handling

- [ ] **Add parameter binding (prevent SQL injection)** 🔒
    - [ ] Implement parameterized query builder
        - [ ] Convert dictionary to parameters
        - [ ] Support named parameters (@param, :param, ?)
        - [ ] Type-safe parameter mapping
    - [ ] Validate query safety
        - [ ] Warn on string concatenation
        - [ ] Enforce parameterization
    - [ ] Add comprehensive tests
        - [ ] Test parameter injection attempt
        - [ ] Test various parameter types
        - [ ] Test null parameters
        - [ ] Verify SQL injection prevention

- [ ] **Support multiple database providers** 🗃️
    - [ ] Install provider packages
        - [ ] `Npgsql` for PostgreSQL
        - [ ] `MySqlConnector` for MySQL
        - [ ] `Microsoft.Data.SqlClient` for SQL Server
        - [ ] `Microsoft.Data.Sqlite` for SQLite
    - [ ] Create provider factory
        - [ ] Map provider enum to connection type
        - [ ] Create appropriate connection
        - [ ] Handle provider-specific SQL dialects
    - [ ] Test query differences between providers
        - [ ] Date formatting
        - [ ] String concatenation
        - [ ] Limit/Offset syntax
    - [ ] Add comprehensive tests
        - [ ] Test each provider (using TestContainers)
        - [ ] Test provider-specific features
        - [ ] Test cross-provider queries

- [ ] **Implement transaction support** 💼
    - [ ] Create `DatabaseTransactionModule` class
        - [ ] ModuleId: `builtin.database.transaction`
        - [ ] DisplayName: `Database Transaction`
        - [ ] Category: `Database`
    - [ ] Define module schema
        - [ ] Input: `connectionString` (string, required)
        - [ ] Input: `provider` (enum, required)
        - [ ] Input: `operations` (array, required) - List of SQL operations
        - [ ] Input: `isolationLevel` (enum, optional) - ReadCommitted, Serializable, etc.
        - [ ] Output: `success` (bool)
        - [ ] Output: `results` (array) - Results from each operation
        - [ ] Output: `error` (object, nullable)
    - [ ] Implement transaction logic
        - [ ] Begin transaction
        - [ ] Execute all operations in order
        - [ ] Commit if all succeed
        - [ ] Rollback on any failure
        - [ ] Support nested transactions (savepoints)
    - [ ] Add comprehensive tests
        - [ ] Test successful transaction
        - [ ] Test rollback on failure
        - [ ] Test isolation levels
        - [ ] Test concurrent transactions

- [ ] **Add bulk insert capabilities** 📊
    - [ ] Create `BulkInsertModule` class
        - [ ] ModuleId: `builtin.database.bulkinsert`
        - [ ] DisplayName: `Bulk Insert`
        - [ ] Category: `Database`
    - [ ] Define module schema
        - [ ] Input: `connectionString` (string, required)
        - [ ] Input: `provider` (enum, required)
        - [ ] Input: `tableName` (string, required)
        - [ ] Input: `data` (array, required) - Array of objects to insert
        - [ ] Input: `batchSize` (int, optional, default=1000)
        - [ ] Input: `columnMapping` (dictionary, optional)
        - [ ] Output: `insertedCount` (int)
        - [ ] Output: `duration` (TimeSpan)
    - [ ] Implement bulk insert logic
        - [ ] Use provider-specific bulk insert
            - [ ] PostgreSQL: COPY command
            - [ ] SQL Server: SqlBulkCopy
            - [ ] MySQL: LOAD DATA or batch INSERT
            - [ ] SQLite: Batch INSERT with transaction
        - [ ] Split into batches if needed
        - [ ] Handle data type mapping
        - [ ] Report progress
    - [ ] Add comprehensive tests
        - [ ] Test bulk insert 10,000 rows
        - [ ] Test batching logic
        - [ ] Test performance vs individual inserts
        - [ ] Test with mixed data types

**Modules:**
```
✅ builtin.database.query - Execute SELECT
✅ builtin.database.execute - Execute commands
✅ builtin.database.transaction - Transaction scope
✅ builtin.database.bulkinsert - Bulk operations
```

**Database Support:**
```csharp
✅ PostgreSQL (Npgsql + Linq2Db)
✅ MySQL (MySqlConnector + Linq2Db)
✅ SQL Server (Microsoft.Data.SqlClient + Linq2Db)
✅ SQLite (for testing)
```

**Tests:**
- [ ] **Query execution tests** 🧪
    - [ ] Test simple SELECT *
    - [ ] Test SELECT with WHERE
    - [ ] Test SELECT with JOIN
    - [ ] Test aggregate functions (COUNT, SUM, AVG)
    - [ ] Test with parameters

- [ ] **Command execution tests** ✏️
    - [ ] Test INSERT single row
    - [ ] Test UPDATE multiple rows
    - [ ] Test DELETE with WHERE
    - [ ] Test last insert ID retrieval

- [ ] **Transaction commit/rollback tests** 💼
    - [ ] Test successful commit
    - [ ] Test rollback on error
    - [ ] Test partial rollback (savepoint)
    - [ ] Test concurrent transactions

- [ ] **Parameter binding tests** 🔒
    - [ ] Test string parameters
    - [ ] Test numeric parameters
    - [ ] Test date parameters
    - [ ] Test null parameters
    - [ ] Test array parameters (PostgreSQL)

- [ ] **SQL injection prevention tests** 🛡️
    - [ ] Attempt SQL injection via parameters
    - [ ] Verify parameterization prevents injection
    - [ ] Test with malicious input strings

- [ ] **Multi-provider tests** 🗃️
    - [ ] Test same query on PostgreSQL
    - [ ] Test same query on MySQL
    - [ ] Test same query on SQL Server
    - [ ] Test same query on SQLite
    - [ ] Verify consistent results

**Deliverables:**
- ✅ Can query databases safely with parameters
- ✅ Can insert/update/delete data reliably
- ✅ Transactions work correctly with commit/rollback
- ✅ Supports major database providers (PostgreSQL, MySQL, SQL Server, SQLite)
- ✅ SQL injection prevented through parameterization
- ✅ Bulk insert handles large datasets efficiently
- ✅ 90%+ test coverage on database modules

---

### 2.5 File System Modules (Week 12-13) ✅ COMPLETE

> **✅ COMPLETE — full detail in the sliced plan:** [Phase2-5-FileSystemModules.md](Phase2-5-FileSystemModules.md) · module reference: [`docs/file-modules.md`](../docs/file-modules.md)
>
> Shipped as two families mirroring the 2.4 layout: **2.5.a** local file family (in `Workflow.Modules/Builtin/File/`) and **2.5.b** cloud storage (in the quarantined **`Workflow.Modules.Cloud`** project, opt-in `AddCloudStorageModules()` — SDK weight stays out of SDK-free deployments, same rule as 2.4's D14). **80 Docker-free unit tests green** + Docker-gated MinIO/Azurite/E2E suites (compile-verified).

**Key corrections vs. the original checklist** *(the plan below was written pre-implementation)*:
- Config values (`path`, `encoding`, `mode`, …) are **module Properties** (template-expandable via `PropertyBinder`), not a separate "input port" concept — the codebase has no config input ports (same correction as 2.4.a.1).
- Cloud credentials use **named storage connections** (`storageConnectionId` → `IStorageConnectionRegistry`, config-bound, secrets redacted) — the checklist's inline `accessKey`/`secretKey` remain only as a documented dev escape hatch (D5).
- `builtin.transform.jsonquery` / `builtin.transform.xmlquery` **moved to Phase 2.6** (pure transforms, no file I/O); `XmlReadModule` still ships an optional `xpath` property so 2.5 is self-sufficient (D9/Q2).
- Compression uses **.NET in-box APIs only** (`System.IO.Compression` + `System.Formats.Tar`) — no SharpZipLib (D7).
- Azure module id is **`builtin.cloud.azureblob`** (not `builtin.cloud.azure`), leaving room for other Azure services.

**Foundation — path-security sandbox (2.5.a.0):**
- [x] `IWorkflowPathValidator` — the single gate every file-touching module passes paths through (canonicalise, reject `..` traversal + sibling-prefix escapes, symlink re-check, write-side blocked-extension policy)
- [x] `FileSystemModuleOptions` (`Workflow:FileSystem`) — `AllowedRoots`, `UnrestrictedIfNoRoots` (+ startup warning), `BlockedExtensions`, `DefaultMaxReadBytes` (16 MB), `ResolveSymlinks`
- [x] `EncodingResolver` (utf-8 no-BOM / utf-16 / ascii / latin1) · `FileModuleSupport` (shared readers + validate-then-resolve) · `FileModuleException`/`PathSecurityException`/`FileTooLargeException`
- [x] Registered via `AddFileSystemModules()`, aggregated by `AddWorkflowModules()`

**Modules (2.5.a — local, 10):**
- [x] `builtin.file.read` — text / binary / lines, encoding + `maxSize` cap (no partial read)
- [x] `builtin.file.write` — overwrite / append / createNew, `createDirectory`, port-or-property content
- [x] `builtin.file.csv.read` / `builtin.file.csv.write` — CsvHelper; headerless → `column0..N`; quotes/escapes/embedded newlines handled
- [x] `builtin.file.json.read` / `builtin.file.json.write` — System.Text.Json; `JsonNode` → CLR dict/list/scalar; `isArray`
- [x] `builtin.file.xml.read` / `builtin.file.xml.write` — XDocument; **XXE-safe** (DTD prohibited, resolver disabled); `@attr`/`#text`/auto-list convention; optional XSD `validateSchema` + `xpath`
- [x] `builtin.file.compress` / `builtin.file.decompress` — Zip / GZip / Tar / TarGz; **zip-slip protection** (pre-scan, whole-extraction fail); format inference on decompress

**Modules (2.5.b — cloud, 2):**
- [x] `builtin.cloud.s3` — upload / download / delete / list / exists; named connection or inline; default AWS credential chain fallback; MinIO/on-prem `serviceUrl`
- [x] `builtin.cloud.azureblob` — upload / download / delete / list / exists; named connection or inline connection string; `createContainer`
- [x] `IStorageConnectionRegistry` + `IStorageClientFactory` + `CloudStorageOptions` (`Workflow:CloudStorage`); credentials never logged (redaction test-locked)

**Modules:**
```
✅ builtin.file.read           - Read file (text/binary/lines)
✅ builtin.file.write          - Write file (overwrite/append/createNew)
✅ builtin.file.csv.read       - Parse CSV → row dictionaries
✅ builtin.file.csv.write      - Write row dictionaries → CSV
✅ builtin.file.json.read      - Parse JSON → object graph
✅ builtin.file.json.write     - Serialise object graph → JSON
✅ builtin.file.xml.read       - Parse XML → dictionary graph (XXE-safe)
✅ builtin.file.xml.write      - Write dictionary graph → XML
✅ builtin.file.compress       - Zip/GZip/Tar/TarGz archive
✅ builtin.file.decompress     - Extract archive (zip-slip safe)
✅ builtin.cloud.s3            - Amazon S3 (+ S3-compatible: MinIO)
✅ builtin.cloud.azureblob     - Azure Blob Storage
```

**Tests:**
- [x] Path-security tests (traversal, sibling-prefix, symlink, blocked-extension, encoding) — `PathValidatorTests`
- [x] File I/O tests (utf-8/latin1, binary, lines, not-found, max-size, round-trip) — `FileReadWriteModuleTests`
- [x] CSV parsing tests (header/no-header, delimiters, quoted fields, round-trip) — `CsvModuleTests`
- [x] JSON/XML processing tests (object/array, nested round-trip, `@attr`/`#text`, **XXE refused**) — `JsonXmlModuleTests`
- [x] Compression tests (Zip/GZip/TarGz round-trip, dir structure, **zip-slip blocked**, corrupt/overwrite) — `CompressionModuleTests`
- [x] Cloud infra + module unit tests (registry, factory, validation, DI-guard, redaction) — `StorageInfrastructureTests`, `CloudModuleUnitTests`
- [x] Cloud storage integration tests (Docker-gated, compile-verified) — `MinioS3ModuleTests`, `AzuriteBlobModuleTests`, `FileCloudE2ETests`

**Deliverables:**
- ✅ Read/write local files with encoding support
- ✅ Parse and generate CSV / JSON / XML formats
- ✅ Interact with cloud storage (S3, Azure Blob) via named connections
- ✅ Compress/decompress in Zip / GZip / Tar / TarGz
- ✅ Path security prevents directory traversal, symlink escape, zip-slip, and XXE
- ✅ 80 unit tests green; cloud SDK weight quarantined; `docs/file-modules.md` published

---

### 2.6 Data Transformation Modules (Week 13)

> **📋 Detailed sliced plan available:** [Phase2-6-DataTransformationModules.md](Phase2-6-DataTransformationModules.md) — 2.6.a expression family (map/query/aggregate/**join**/validate/string/json + the `jsonquery`/`xmlquery` modules deferred from 2.5) riding the existing 2.2.5 `IExpressionEvaluator` seam, plus 2.6.b typed C# script family (`builtin.transform.script`) that **extracts and reuses 2.4.b's Roslyn compile→whitelist→HMAC-cache→collectible-ALC pipeline** into a shared `Workflow.Scripting.Roslyn` core. **Timeline shifted to Weeks 17-18.** The checklist below is the legacy reference list — the sliced doc supersedes it (notably: no System.Linq.Dynamic.Core, no FluentValidation — expressions + declarative rules + JSON Schema instead; Q1–Q8 all resolved — 2.6.b is MVP, joins ship as `builtin.transform.join`).

**Tasks:** *(legacy reference list — superseded by the sliced plan above)*
- [ ] **Implement data mapping module** 🔄
    - [ ] Create `DataMapModule` class
        - [ ] ModuleId: `builtin.transform.map`
        - [ ] DisplayName: `Map Data`
        - [ ] Category: `Transformation`
    - [ ] Define DataMapModule schema
        - [ ] Input: `source` (object or array, required) - Source data
        - [ ] Input: `mapping` (object, required) - Mapping configuration
        - [ ] Input: `flatten` (bool, optional, default=false) - Flatten nested objects
        - [ ] Input: `ignoreNulls` (bool, optional, default=false)
        - [ ] Output: `result` (object or array) - Transformed data
    - [ ] Implement mapping engine
        - [ ] Support property renaming (source.oldName → target.newName)
        - [ ] Support nested property access (user.address.city)
        - [ ] Support array mapping
        - [ ] Support conditional mapping
        - [ ] Support default values
        - [ ] Support type conversion
        - [ ] Support computed properties (expressions)
    - [ ] Create mapping configuration DSL
        - [ ] JSON-based mapping definitions
        - [ ] Template expressions
        - [ ] Function calls
    - [ ] Add comprehensive tests
        - [ ] Test simple property mapping
        - [ ] Test nested object mapping
        - [ ] Test array mapping
        - [ ] Test conditional mapping
        - [ ] Test type conversion
        - [ ] Test null handling

- [ ] **Add LINQ-style query support** 🔍
    - [ ] Install `System.Linq.Dynamic.Core` NuGet package
    - [ ] Create `DataQueryModule` class
        - [ ] ModuleId: `builtin.transform.query`
        - [ ] DisplayName: `Query Data`
        - [ ] Category: `Transformation`
    - [ ] Define DataQueryModule schema
        - [ ] Input: `data` (array, required) - Source data collection
        - [ ] Input: `query` (string, required) - LINQ query expression
        - [ ] Input: `parameters` (dictionary, optional) - Query parameters
        - [ ] Output: `result` (array) - Query results
        - [ ] Output: `count` (int) - Result count
    - [ ] Implement query execution
        - [ ] Parse dynamic LINQ expressions
        - [ ] Support Where filtering
        - [ ] Support Select projection
        - [ ] Support OrderBy/OrderByDescending
        - [ ] Support GroupBy aggregation
        - [ ] Support Skip/Take pagination
        - [ ] Support Join operations
    - [ ] Add query helpers
        - [ ] Query builder API
        - [ ] Common query templates
        - [ ] Query validation
    - [ ] Add comprehensive tests
        - [ ] Test Where filtering
        - [ ] Test Select projection
        - [ ] Test OrderBy sorting
        - [ ] Test GroupBy aggregation
        - [ ] Test Skip/Take pagination
        - [ ] Test complex queries
        - [ ] Test query parameter binding

- [ ] **Implement aggregation operations** 📊
    - [ ] Create `AggregateModule` class
        - [ ] ModuleId: `builtin.transform.aggregate`
        - [ ] DisplayName: `Aggregate Data`
        - [ ] Category: `Transformation`
    - [ ] Define AggregateModule schema
        - [ ] Input: `data` (array, required) - Source data
        - [ ] Input: `operation` (enum, required) - Sum, Count, Average, Min, Max, First, Last
        - [ ] Input: `property` (string, optional) - Property to aggregate
        - [ ] Input: `groupBy` (string, optional) - Group by property
        - [ ] Output: `result` (varies) - Aggregation result
        - [ ] Output: `groups` (array, optional) - Grouped results
    - [ ] Implement aggregation operations
        - [ ] Sum - Calculate total
        - [ ] Count - Count items
        - [ ] Average - Calculate mean
        - [ ] Min - Find minimum
        - [ ] Max - Find maximum
        - [ ] First - Get first item
        - [ ] Last - Get last item
        - [ ] Distinct - Get unique values
        - [ ] Median - Calculate median (custom)
        - [ ] Mode - Find most common value (custom)
    - [ ] Support grouped aggregation
        - [ ] Group by property
        - [ ] Aggregate within groups
        - [ ] Return grouped results
    - [ ] Add comprehensive tests
        - [ ] Test Sum aggregation
        - [ ] Test Count aggregation
        - [ ] Test Average aggregation
        - [ ] Test Min/Max aggregation
        - [ ] Test grouped aggregation
        - [ ] Test empty collection
        - [ ] Test null value handling

- [ ] **Add data validation module** ✅
    - [ ] Install `FluentValidation` NuGet package
    - [ ] Create `ValidateDataModule` class
        - [ ] ModuleId: `builtin.transform.validate`
        - [ ] DisplayName: `Validate Data`
        - [ ] Category: `Transformation`
    - [ ] Define ValidateDataModule schema
        - [ ] Input: `data` (object or array, required) - Data to validate
        - [ ] Input: `schema` (object, required) - Validation schema
        - [ ] Input: `throwOnError` (bool, optional, default=false)
        - [ ] Output: `isValid` (bool) - Validation result
        - [ ] Output: `errors` (array) - Validation errors
        - [ ] Output: `validItems` (array, optional) - Valid items
        - [ ] Output: `invalidItems` (array, optional) - Invalid items
    - [ ] Implement validation rules
        - [ ] Required field validation
        - [ ] Type validation (string, number, boolean, date)
        - [ ] String length validation (min, max)
        - [ ] Number range validation (min, max)
        - [ ] Regex pattern validation
        - [ ] Email validation
        - [ ] URL validation
        - [ ] Date range validation
        - [ ] Custom validation (expressions)
        - [ ] Nested object validation
        - [ ] Array validation (min/max items)
    - [ ] Create validation schema format
        - [ ] JSON Schema support
        - [ ] Custom validation DSL
        - [ ] Reusable validation rules
    - [ ] Add comprehensive tests
        - [ ] Test required field validation
        - [ ] Test type validation
        - [ ] Test length/range validation
        - [ ] Test pattern validation
        - [ ] Test email/URL validation
        - [ ] Test nested validation
        - [ ] Test array validation
        - [ ] Test custom rules

- [ ] **Implement string manipulation** 📝
    - [ ] Create `StringTransformModule` class
        - [ ] ModuleId: `builtin.transform.string`
        - [ ] DisplayName: `String Operations`
        - [ ] Category: `Transformation`
    - [ ] Define StringTransformModule schema
        - [ ] Input: `input` (string or array, required) - String(s) to transform
        - [ ] Input: `operation` (enum, required) - Transformation operation
        - [ ] Input: `parameters` (dictionary, optional) - Operation parameters
        - [ ] Output: `result` (string or array) - Transformed string(s)
    - [ ] Implement string operations
        - [ ] ToUpper - Convert to uppercase
        - [ ] ToLower - Convert to lowercase
        - [ ] Trim - Remove whitespace
        - [ ] TrimStart/TrimEnd - Remove from start/end
        - [ ] Substring - Extract substring
        - [ ] Replace - Replace text
        - [ ] Split - Split into array
        - [ ] Join - Join array into string
        - [ ] PadLeft/PadRight - Add padding
        - [ ] Truncate - Limit length
        - [ ] Format - String formatting
        - [ ] Regex operations (Match, Replace, Extract)
        - [ ] Base64 encode/decode
        - [ ] URL encode/decode
        - [ ] HTML encode/decode
        - [ ] Hash (MD5, SHA256, SHA512)
        - [ ] GUID generation
    - [ ] Add comprehensive tests
        - [ ] Test all string operations
        - [ ] Test with null/empty strings
        - [ ] Test with arrays of strings
        - [ ] Test regex operations
        - [ ] Test encoding operations
        - [ ] Test hash generation

- [ ] **Implement JSON transformation** 📝
    - [ ] Create `JsonTransformModule` class
        - [ ] ModuleId: `builtin.transform.json`
        - [ ] DisplayName: `JSON Transform`
        - [ ] Category: `Transformation`
    - [ ] Define JsonTransformModule schema
        - [ ] Input: `data` (object, required)
        - [ ] Input: `operation` (enum, required) - Select, Filter, Transform, Merge
        - [ ] Input: `path` (string, optional) - JSONPath expression
        - [ ] Input: `template` (object, optional) - Transformation template
        - [ ] Output: `result` (object)
    - [ ] Implement JSON operations
        - [ ] JSONPath queries ($.items[?(@.price > 10)])
        - [ ] JSON merge/patch
        - [ ] JSON diff
        - [ ] JSON schema validation
        - [ ] Flatten nested JSON
        - [ ] Unflatten flat JSON
    - [ ] Add comprehensive tests
        - [ ] Test JSONPath queries
        - [ ] Test merge operations
        - [ ] Test diff operations
        - [ ] Test flatten/unflatten

**Modules:**
```
✅ builtin.transform.map - Object mapping
✅ builtin.transform.query - LINQ queries
✅ builtin.transform.aggregate - Sum, count, avg, etc.
✅ builtin.transform.validate - Data validation
✅ builtin.transform.string - String operations
✅ builtin.transform.json - JSON transformations
```

**Tests:**
- [ ] **Data mapping tests** 🔄
    - [ ] Test simple property mapping
    - [ ] Test nested object mapping
    - [ ] Test array mapping
    - [ ] Test conditional mapping
    - [ ] Test type conversion in mapping
    - [ ] Test default values

- [ ] **Query execution tests** 🔍
    - [ ] Test Where clause filtering
    - [ ] Test Select projection
    - [ ] Test OrderBy sorting (ascending/descending)
    - [ ] Test GroupBy aggregation
    - [ ] Test Skip/Take pagination
    - [ ] Test combined operations

- [ ] **Aggregation tests** 📊
    - [ ] Test Sum on numeric array
    - [ ] Test Count on collection
    - [ ] Test Average calculation
    - [ ] Test Min/Max on collection
    - [ ] Test grouped aggregation
    - [ ] Test empty collection handling

- [ ] **Validation tests** ✅
    - [ ] Test required field validation
    - [ ] Test type validation errors
    - [ ] Test string length validation
    - [ ] Test number range validation
    - [ ] Test regex pattern matching
    - [ ] Test email format validation
    - [ ] Test nested object validation
    - [ ] Test array validation rules

---

**Deliverables:**
- ✅ Can transform data between formats reliably
- ✅ Can validate data schemas with detailed errors
- ✅ Can perform aggregations (sum, avg, count, etc.)
- ✅ Can query collections with LINQ expressions
- ✅ Can manipulate strings with various operations
- ✅ JSON transformations working (JSONPath, merge, diff)
- ✅ 90%+ test coverage on transformation modules
  ✅ builtin.transform.validate - Data validation
  ✅ builtin.transform.string - String operations
  ✅ builtin.transform.json - JSON transformations

**Tests:**
- [ ] Data mapping tests
- [ ] Query execution tests
- [ ] Aggregation tests
- [ ] Validation tests

**Deliverables:**
- ✅ Can transform data between formats
- ✅ Can validate data schemas
- ✅ Can perform aggregations

---

### 2.7 REST API Implementation (Week 13-14)

**Tasks:**
- [ ] **Implement workflow CRUD endpoints** 📋
    - [ ] Create `WorkflowsController` class
    - [ ] Implement GET /api/v1/workflows
        - [ ] List all workflows with pagination
        - [ ] Support filtering (by name, tags, status)
        - [ ] Support sorting (by name, created date)
        - [ ] Return workflow summaries
    - [ ] Implement GET /api/v1/workflows/{id}
        - [ ] Get single workflow by ID
        - [ ] Return full workflow definition
        - [ ] Handle not found (404)
    - [ ] Implement POST /api/v1/workflows
        - [ ] Create new workflow
        - [ ] Validate workflow definition
        - [ ] Return created workflow with ID
        - [ ] Return 201 Created
    - [ ] Implement PUT /api/v1/workflows/{id}
        - [ ] Update existing workflow
        - [ ] Validate workflow definition
        - [ ] Handle version conflicts
        - [ ] Return updated workflow
    - [ ] Implement DELETE /api/v1/workflows/{id}
        - [ ] Delete workflow
        - [ ] Check for running executions
        - [ ] Return 204 No Content
    - [ ] Add comprehensive tests
        - [ ] Test list with pagination
        - [ ] Test create workflow
        - [ ] Test update workflow
        - [ ] Test delete workflow
        - [ ] Test validation errors

- [ ] **Implement execution endpoints** ⚡
    - [ ] Create `ExecutionsController` class
    - [ ] Implement POST /api/v1/workflows/{id}/execute
        - [ ] Start workflow execution
        - [ ] Accept input parameters
        - [ ] Resolve caller identity using `X-Caller-Id` override → claims (`NameIdentifier`/`sub`) → `"system"` fallback
        - [ ] Pass `ExecutionStartOptions` (`CallerId`, `VariableWriteMode`) to `CreateWorkflowInstance`
        - [ ] Return execution ID
        - [ ] Return 202 Accepted
    - [ ] Implement POST /api/v1/workflows/execute/{name}
        - [ ] Execute by workflow name
        - [ ] Handle multiple versions
    - [ ] Implement GET /api/v1/executions/{executionId}
        - [ ] Get execution status
        - [ ] Return execution details
        - [ ] Include node statuses
        - [ ] Include outputs (if complete)
    - [ ] Implement POST /api/v1/executions/{executionId}/cancel
        - [ ] Cancel running execution
        - [ ] Return cancellation status
    - [ ] Implement GET /api/v1/executions
        - [ ] List executions with filters
        - [ ] Filter by workflow, status, date range
        - [ ] Support pagination
    - [ ] Implement POST /api/v1/workflows/{id}/execute/sync
        - [ ] Execute and wait for completion
        - [ ] Support timeout parameter
        - [ ] Return execution result
    - [ ] Add comprehensive tests
        - [ ] Test async execution
        - [ ] Test execution start persists `TriggeredBy` from caller identity resolution
        - [ ] Test runtime `VariableWriteMode` maps to expected variable scope writes
        - [ ] Test sync execution
        - [ ] Test status query
        - [ ] Test cancel execution
        - [ ] Test list executions
    - [ ] Reuse Phase 2.1.5 persistence wiring (`IExecutionHistoryRepository` / `IVariableStore`) for execution creation and status reads

- [ ] **Create module schema DTO layer** 📐
  - [ ] `ModuleSchemaDto` record — serializable version of `ModuleSchema`
  - [ ] `PortDefinitionDto` record:
    - [ ] `Name` (string)
    - [ ] `DisplayName` (string)
    - [ ] `DataType` (string) — type name, e.g. `"System.String"`, `"System.Int32"`
    - [ ] `Description` (string?)
    - [ ] `IsRequired` (bool)
    - [ ] `DefaultValue` (JsonElement?) — serializable default
  - [ ] `ModulePropertyDefinitionDto` — mirrors `ModulePropertyDefinition` with serializable types
  - [ ] `ModuleSummaryDto` — lightweight list view (no schema, just id/name/category/description/version)
  - [ ] `ModuleDetailsDto` — full view including `ModuleSchemaDto`
  - [ ] Projection mapping: `IWorkflowModule → ModuleDetailsDto`
  - [ ] Tests for DTO projection and round-trip JSON serialization

- [ ] **Add module management endpoints** 📦
    - [ ] Create `ModulesController` class
    - [ ] Implement GET /api/v1/modules
        - [ ] List all registered modules
        - [ ] Group by category
        - [ ] Include module metadata
    - [ ] Implement GET /api/v1/modules/{moduleId}
        - [ ] Get module details
        - [ ] Return schema information
        - [ ] Include documentation
    - [ ] Implement POST /api/v1/modules/upload
        - [ ] Upload module package (.wfmod)
        - [ ] Validate module package
        - [ ] Install module
        - [ ] Return module info
    - [ ] Implement DELETE /api/v1/modules/{moduleId}
        - [ ] Uninstall module
        - [ ] Check for dependencies
        - [ ] Return status
    - [ ] Implement POST /api/v1/modules/{moduleId}/enable
        - [ ] Enable disabled module
    - [ ] Implement POST /api/v1/modules/{moduleId}/disable
        - [ ] Disable module
    - [ ] Add comprehensive tests
        - [ ] Test list modules
        - [ ] Test get module details
        - [ ] Test upload module
        - [ ] Test enable/disable

- [ ] **Implement variable management endpoints** 🔧
    - [ ] Create `VariablesController` class
    - [ ] Implement GET /api/v1/variables
        - [ ] List all variables
        - [ ] Filter by scope
        - [ ] Support pagination
    - [ ] Implement GET /api/v1/variables/{name}
        - [ ] Get variable value
        - [ ] Support scopes (global, workflow, execution)
        - [ ] Return version information
    - [ ] Implement PUT /api/v1/variables/{name}
        - [ ] Set/update variable
        - [ ] Support different scopes
        - [ ] Return new version
    - [ ] Implement DELETE /api/v1/variables/{name}
        - [ ] Delete variable
        - [ ] Return status
    - [ ] Implement GET /api/v1/variables/{name}/history
        - [ ] Get variable change history
        - [ ] Return all versions
    - [ ] Add comprehensive tests
        - [ ] Test get variable
        - [ ] Test set variable
        - [ ] Test delete variable
        - [ ] Test get history

- [ ] **Add monitoring endpoints** 📊
    - [ ] Create `MonitoringController` class
    - [ ] Implement GET /api/v1/health
        - [ ] Return health status
        - [ ] Check database connectivity
        - [ ] Check actor system status
        - [ ] Return 200 if healthy, 503 if unhealthy
    - [ ] Implement GET /api/v1/health/ready
        - [ ] Readiness probe for Kubernetes
        - [ ] Check all dependencies
    - [ ] Implement GET /api/v1/health/live
        - [ ] Liveness probe for Kubernetes
        - [ ] Basic process check
    - [ ] Implement GET /api/v1/metrics
        - [ ] Return Prometheus metrics
        - [ ] Workflow execution metrics
        - [ ] Performance metrics
    - [ ] Implement GET /api/v1/status
        - [ ] System status overview
        - [ ] Active workflows count
        - [ ] Active executions count
        - [ ] Resource usage
    - [ ] Add comprehensive tests
        - [ ] Test health endpoint
        - [ ] Test metrics endpoint
        - [ ] Test status endpoint

- [ ] **Implement webhook endpoints** 🪝
    - [ ] Create `WebhooksController` class
    - [ ] Implement POST /api/v1/webhooks/{webhookId}
        - [ ] Receive webhook call
        - [ ] Validate signature
        - [ ] Trigger workflow
        - [ ] Return response
    - [ ] Implement GET /api/v1/webhooks
        - [ ] List registered webhooks
    - [ ] Implement POST /api/v1/webhooks
        - [ ] Register new webhook
        - [ ] Generate webhook ID
        - [ ] Return webhook URL
    - [ ] Implement DELETE /api/v1/webhooks/{webhookId}
        - [ ] Unregister webhook
    - [ ] Add comprehensive tests
        - [ ] Test webhook trigger
        - [ ] Test signature validation
        - [ ] Test register webhook
        - [ ] Test unregister webhook

- [ ] **Add authentication (API Key + JWT)** 🔐
    - [ ] Implement API Key authentication
        - [ ] Create `ApiKeyAuthenticationHandler`
        - [ ] Validate API key from header (X-API-Key)
        - [ ] Load user/permissions from API key
        - [ ] Set user identity
    - [ ] Implement JWT token authentication
        - [ ] Create `JwtAuthenticationHandler`
        - [ ] Validate JWT token
        - [ ] Extract claims
        - [ ] Set user identity
    - [ ] Create authentication endpoints
        - [ ] POST /api/v1/auth/login
            - [ ] Accept username/password
            - [ ] Validate credentials
            - [ ] Generate JWT token
            - [ ] Return access token + refresh token
        - [ ] POST /api/v1/auth/refresh
            - [ ] Accept refresh token
            - [ ] Validate refresh token
            - [ ] Generate new access token
        - [ ] POST /api/v1/auth
