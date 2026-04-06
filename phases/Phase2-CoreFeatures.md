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
- [2.4 Database Modules (Week 11-12)](#24-database-modules-week-11-12)
- [2.5 File System Modules (Week 12-13)](#25-file-system-modules-week-12-13)
- [2.6 Data Transformation Modules (Week 13)](#26-data-transformation-modules-week-13)
- [2.7 REST API Implementation (Week 13-14)](#27-rest-api-implementation-week-13-14)
- [2.8 Module System Enhancements (Deferred from Phase 1.4)](#28-module-system-enhancements-deferred-from-phase-14-)
- [Phase 2 Success Criteria](#phase-2-success-criteria-)

---

## 🚀 Phase 2: Core Features (Weeks 7-14)

**Goal:** Implement essential workflow features and expand module library! 💫

### 2.1 Persistence Layer (Week 7-9)

**Tasks:**
- [ ] **Implement pluggable persistence interface** 🔌
    - [ ] Define `IPersistenceProvider` base interface
        - [ ] Add `InitializeAsync()` method
        - [ ] Add `HealthCheckAsync()` method
        - [ ] Add `DisposeAsync()` method
        - [ ] Add configuration properties
    - [ ] Define persistence operations interfaces
        - [ ] `IWorkflowRepository` - Workflow CRUD operations
        - [ ] `IExecutionHistoryRepository` - Execution tracking
        - [ ] `IVariableStore` - Variable storage with history
        - [ ] `IBlobStore` - Large object storage
    - [ ] Create provider factory pattern
        - [ ] `IPersistenceProviderFactory`
        - [ ] Registration mechanism for providers
        - [ ] Configuration-based provider selection
    - [ ] Add provider lifecycle management
    - [ ] Implement provider health monitoring

- [ ] **Create PostgreSQL persistence provider (Linq2Db)** 🐘
    - [ ] Install required NuGet packages
        - [ ] `linq2db`
        - [ ] `Npgsql`
        - [ ] `FluentMigrator` for migrations
    - [ ] Design database schema
        - [ ] Create `workflows` table
            - [ ] `id` (uuid, primary key)
            - [ ] `name` (varchar, indexed)
            - [ ] `description` (text)
            - [ ] `definition` (jsonb) - Full workflow definition
            - [ ] `version` (varchar)
            - [ ] `is_active` (boolean)
            - [ ] `created_at` (timestamptz)
            - [ ] `updated_at` (timestamptz)
            - [ ] `tags` (text array)
            - [ ] `metadata` (jsonb)
        - [ ] Create `executions` table
            - [ ] `id` (uuid, primary key)
            - [ ] `workflow_id` (uuid, foreign key)
            - [ ] `status` (enum: pending, running, completed, failed, cancelled)
            - [ ] `started_at` (timestamptz)
            - [ ] `completed_at` (timestamptz, nullable)
            - [ ] `inputs` (jsonb)
            - [ ] `outputs` (jsonb)
            - [ ] `error` (jsonb, nullable)
            - [ ] `triggered_by` (varchar)
            - [ ] Create indexes on workflow_id, status, started_at
        - [ ] Create `execution_nodes` table (node-level tracking)
            - [ ] `id` (bigserial, primary key)
            - [ ] `execution_id` (uuid, foreign key)
            - [ ] `node_id` (varchar)
            - [ ] `status` (enum)
            - [ ] `started_at` (timestamptz)
            - [ ] `completed_at` (timestamptz, nullable)
            - [ ] `inputs` (jsonb)
            - [ ] `outputs` (jsonb)
            - [ ] `error` (jsonb, nullable)
            - [ ] `duration_ms` (int)
            - [ ] Create index on execution_id
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
    - [ ] Implement FluentMigrator migrations
        - [ ] Migration_001_InitialSchema
        - [ ] Migration_002_AddIndexes
        - [ ] Add migration runner
        - [ ] Test rollback functionality
    - [ ] Implement Linq2Db data context
        - [ ] Create `WorkflowDataConnection` class
        - [ ] Map tables to entities
        - [ ] Configure connection string
        - [ ] Add connection pooling
    - [ ] Implement `PostgresWorkflowRepository`
        - [ ] Implement `CreateAsync(WorkflowDefinition)`
        - [ ] Implement `UpdateAsync(Guid id, WorkflowDefinition)`
        - [ ] Implement `DeleteAsync(Guid id)`
        - [ ] Implement `GetByIdAsync(Guid id)`
        - [ ] Implement `GetAllAsync(filter, pagination)`
        - [ ] Implement `SearchAsync(query)`
        - [ ] Add optimistic concurrency handling
    - [ ] Implement `PostgresExecutionHistoryRepository`
        - [ ] Implement `CreateExecutionAsync(Execution)`
        - [ ] Implement `UpdateExecutionStatusAsync(Guid id, status)`
        - [ ] Implement `GetExecutionAsync(Guid id)`
        - [ ] Implement `GetExecutionsForWorkflowAsync(Guid workflowId)`
        - [ ] Implement `RecordNodeExecutionAsync(NodeExecution)`
        - [ ] Implement query methods with filtering
        - [ ] Add pagination support
    - [ ] Implement `PostgresVariableStore`
        - [ ] Implement `SetVariableAsync(scope, name, value)`
        - [ ] Implement `GetVariableAsync(scope, name, version?)`
        - [ ] Implement `GetVariableHistoryAsync(scope, name)`
        - [ ] Implement `DeleteVariableAsync(scope, name)`
        - [ ] Support versioned reads (time-travel queries)
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
    - [ ] Store execution start/end times
    - [ ] Store execution inputs/outputs
    - [ ] Store node-level execution details
    - [ ] Store error information with stack traces
    - [ ] Implement execution log aggregation
    - [ ] Add retention policies
        - [ ] Archive old executions
        - [ ] Delete very old executions
    - [ ] Implement execution replay capability
        - [ ] Store enough data to replay
        - [ ] Create replay functionality

- [ ] **Add variable persistence with historical tracking** 🕰️
    - [ ] Implement versioned variable storage
    - [ ] Track all changes with timestamps
    - [ ] Support point-in-time queries
    - [ ] Implement variable scopes
        - [ ] Global scope (across all workflows)
        - [ ] Workflow scope (shared in workflow)
        - [ ] Execution scope (single execution)
    - [ ] Add variable expiration/TTL
    - [ ] Implement variable change notifications
    - [ ] Add audit trail for variable changes

- [ ] **Implement NATS KV persistence provider** 🚀
    - [ ] Install `NATS.Client` NuGet package
    - [ ] Implement `NatsKVPersistenceProvider`
    - [ ] Configure NATS connection
        - [ ] Connection string
        - [ ] Authentication
        - [ ] TLS configuration
    - [ ] Implement key-value operations
        - [ ] Put (create/update)
        - [ ] Get (with optional revision)
        - [ ] Delete
        - [ ] Watch (for changes)
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
    - [ ] Install `AWSSDK.S3` NuGet package
    - [ ] Implement `S3PersistenceProvider`
    - [ ] Configure S3 client
        - [ ] Access key / Secret key
        - [ ] Region
        - [ ] Bucket name
        - [ ] Endpoint URL (for S3-compatible services)
    - [ ] Implement blob storage operations
        - [ ] `PutObjectAsync` - Upload large data
        - [ ] `GetObjectAsync` - Download data
        - [ ] `DeleteObjectAsync` - Remove data
        - [ ] `GeneratePresignedUrlAsync` - Temporary access URLs
    - [ ] Define key patterns
        - [ ] Workflows: `workflows/{id}/definition.json`
        - [ ] Executions: `executions/{id}/data.json`
        - [ ] Large outputs: `executions/{id}/nodes/{nodeId}/output.bin`
        - [ ] Logs: `executions/{id}/logs/{timestamp}.log`
    - [ ] Implement multipart upload for large files
    - [ ] Add content-type detection
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
    - [ ] Setup PostgreSQL container for tests
    - [ ] Test workflow CRUD operations
        - [ ] Create workflow
        - [ ] Read workflow by ID
        - [ ] Update workflow
        - [ ] Delete workflow
        - [ ] List all workflows
        - [ ] Search workflows
    - [ ] Test execution history operations
        - [ ] Create execution record
        - [ ] Update execution status
        - [ ] Record node executions
        - [ ] Query execution history
        - [ ] Filter by date range
        - [ ] Pagination
    - [ ] Test variable operations
        - [ ] Set variable (create)
        - [ ] Get variable (latest version)
        - [ ] Get variable (specific version)
        - [ ] Get variable history
        - [ ] Delete variable
    - [ ] Test concurrent operations
    - [ ] Test transaction rollback
    - [ ] Test connection pool exhaustion

- [ ] **NATS KV integration tests** 🚀
    - [ ] Setup NATS container for tests
    - [ ] Test KV bucket operations
    - [ ] Test workflow storage/retrieval
    - [ ] Test variable versioning
    - [ ] Test watch functionality
    - [ ] Test connection resilience

- [ ] **S3 integration tests** ☁️
    - [ ] Setup MinIO container for tests (S3-compatible)
    - [ ] Test object upload/download
    - [ ] Test presigned URL generation
    - [ ] Test large file upload (multipart)
    - [ ] Test object deletion
    - [ ] Test lifecycle policies

- [ ] **Historical tracking tests** 🕰️
    - [ ] Test variable version tracking
    - [ ] Test point-in-time queries
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

**Tasks:**
- [ ] **Implement `HttpRequestModule` - Full HTTP client** 🌐
    - [ ] Create `HttpRequestModule` class
        - [ ] ModuleId: `builtin.http.request`
        - [ ] DisplayName: `HTTP Request`
        - [ ] Category: `Network`
    - [ ] Define module schema
        - [ ] Input: `url` (string, required) - Target URL
        - [ ] Input: `method` (enum, required) - GET, POST, PUT, DELETE, PATCH, HEAD, OPTIONS
        - [ ] Input: `headers` (dictionary, optional) - Custom headers
        - [ ] Input: `body` (object, optional) - Request body
        - [ ] Input: `contentType` (string, optional) - Content-Type header
        - [ ] Input: `timeout` (TimeSpan, optional, default=30s)
        - [ ] Input: `followRedirects` (bool, optional, default=true)
        - [ ] Input: `maxRedirects` (int, optional, default=10)
        - [ ] Input: `validateCertificate` (bool, optional, default=true)
        - [ ] Output: `statusCode` (int) - HTTP status code
        - [ ] Output: `headers` (dictionary) - Response headers
        - [ ] Output: `body` (object) - Response body
        - [ ] Output: `success` (bool) - Status code 200-299
        - [ ] Output: `duration` (TimeSpan) - Request duration
    - [ ] Implement ExecuteAsync method
        - [ ] Create HttpClient instance (or use injected)
        - [ ] Build HTTP request message
        - [ ] Add all custom headers
        - [ ] Serialize body based on content type
        - [ ] Set timeout
        - [ ] Send request
        - [ ] Parse response
        - [ ] Deserialize response body
        - [ ] Return all outputs
    - [ ] Add request body serialization
        - [ ] JSON serialization
        - [ ] XML serialization
        - [ ] Form URL encoded
        - [ ] Multipart form data
        - [ ] Raw string/bytes
    - [ ] Add response body deserialization
        - [ ] Auto-detect content type
        - [ ] JSON deserialization
        - [ ] XML deserialization
        - [ ] Text content
        - [ ] Binary content
    - [ ] Add comprehensive tests
        - [ ] Test all HTTP methods
        - [ ] Test custom headers
        - [ ] Test request body serialization
        - [ ] Test response parsing
        - [ ] Test error handling (404, 500, etc.)

- [ ] **Add authentication support** 🔐
    - [ ] Implement Basic Authentication
        - [ ] Add `authType` input (enum)
        - [ ] Add `username` and `password` inputs
        - [ ] Generate Authorization header
    - [ ] Implement Bearer Token Authentication
        - [ ] Add `bearerToken` input
        - [ ] Add token to Authorization header
    - [ ] Implement API Key Authentication
        - [ ] Add `apiKey` and `apiKeyHeader` inputs
        - [ ] Support query parameter API keys
    - [ ] Implement OAuth2 Support
        - [ ] Add OAuth2 client credentials flow
        - [ ] Token caching
        - [ ] Automatic token refresh
    - [ ] Add comprehensive tests
        - [ ] Test each auth type
        - [ ] Test auth failures
        - [ ] Test token refresh

- [ ] **Implement retry logic and timeouts** 🔄
    - [ ] Add retry configuration
        - [ ] Input: `retryCount` (int, optional, default=3)
        - [ ] Input: `retryDelay` (TimeSpan, optional, default=1s)
        - [ ] Input: `retryBackoff` (enum: Linear, Exponential, Fibonacci)
        - [ ] Input: `retryOnStatusCodes` (array, optional) - Which codes to retry
    - [ ] Implement retry logic with Polly
        - [ ] Install Polly NuGet package
        - [ ] Create retry policy
        - [ ] Handle transient failures (408, 429, 500-599)
        - [ ] Exponential backoff implementation
        - [ ] Jitter for retry delays
    - [ ] Implement circuit breaker pattern
        - [ ] Open circuit after N failures
        - [ ] Half-open state for testing recovery
        - [ ] Close circuit when stable
    - [ ] Add timeout handling
        - [ ] Request-level timeout
        - [ ] Operation-level timeout
        - [ ] Cancellation token support
    - [ ] Add comprehensive tests
        - [ ] Test retry on 500 error
        - [ ] Test exponential backoff timing
        - [ ] Test max retry limit
        - [ ] Test circuit breaker opening
        - [ ] Test timeout cancellation

- [ ] **Add request/response transformation** 🔄
    - [ ] Implement request transformation
        - [ ] Template strings in URL
        - [ ] Variable interpolation in body
        - [ ] Dynamic header generation
        - [ ] Request middleware pipeline
    - [ ] Implement response transformation
        - [ ] JSONPath queries on response
        - [ ] XPath queries on XML response
        - [ ] Regex extraction from text
        - [ ] Response mapping to outputs
    - [ ] Add data extraction helpers
        - [ ] Extract specific fields
        - [ ] Flatten nested objects
        - [ ] Array manipulation
    - [ ] Add comprehensive tests
        - [ ] Test URL templating
        - [ ] Test JSONPath extraction
        - [ ] Test response mapping

- [ ] **Implement webhook trigger module** 🪝
    - [ ] Create `WebhookTriggerModule` class
        - [ ] ModuleId: `builtin.http.webhook`
        - [ ] DisplayName: `Webhook Trigger`
        - [ ] Category: `Triggers`
    - [ ] Define module schema
        - [ ] Configuration: `webhookId` (string, unique)
        - [ ] Configuration: `path` (string) - URL path
        - [ ] Configuration: `method` (enum) - Allowed HTTP methods
        - [ ] Configuration: `secretKey` (string, optional) - For signature validation
        - [ ] Output: `headers` (dictionary)
        - [ ] Output: `body` (object)
        - [ ] Output: `query` (dictionary)
    - [ ] Implement webhook endpoint in API
        - [ ] POST /api/webhooks/{webhookId}
        - [ ] Parse incoming request
        - [ ] Validate signature (if configured)
        - [ ] Trigger workflow execution
        - [ ] Return response to caller
    - [ ] Add signature validation
        - [ ] HMAC-SHA256 validation
        - [ ] Support for common webhook signatures (GitHub, Stripe, etc.)
    - [ ] Add webhook management endpoints
        - [ ] Register webhook
        - [ ] Update webhook
        - [ ] Delete webhook
        - [ ] List webhooks
    - [ ] Add comprehensive tests
        - [ ] Test webhook trigger
        - [ ] Test signature validation
        - [ ] Test invalid signatures
        - [ ] Test different HTTP methods

**Modules:**
```
✅ builtin.http.request - All HTTP methods
✅ builtin.http.webhook - Webhook triggers
✅ builtin.http.graphql - GraphQL queries
✅ builtin.http.soap - SOAP client (optional)
```

**Features:**
- [ ] **All HTTP methods** (GET, POST, PUT, DELETE, PATCH, HEAD, OPTIONS)
    - [ ] Implement each method
    - [ ] Test each method

- [ ] **Custom headers**
    - [ ] Accept any header key-value pair
    - [ ] Validate header names
    - [ ] Support multiple values for same header

- [ ] **Multiple auth types**
    - [ ] Basic, Bearer, API Key, OAuth2
    - [ ] Test all combinations

- [ ] **Retry with exponential backoff**
    - [ ] Implement using Polly
    - [ ] Test backoff timing

- [ ] **Request/response body transformation**
    - [ ] JSONPath, XPath, Regex
    - [ ] Test transformations

- [ ] **SSL/TLS configuration**
    - [ ] Certificate validation toggle
    - [ ] Custom certificate support
    - [ ] Test with self-signed certs

- [ ] **Proxy support**
    - [ ] HTTP proxy configuration
    - [ ] SOCKS proxy support
    - [ ] Proxy authentication

**Tests:**
- [ ] **HTTP request tests (with WireMock/MockServer)** 🧪
    - [ ] Setup WireMock container
    - [ ] Test GET request
    - [ ] Test POST with JSON body
    - [ ] Test PUT with XML body
    - [ ] Test DELETE request
    - [ ] Test response parsing
    - [ ] Test error responses (404, 500)

- [ ] **Authentication flow tests** 🔐
    - [ ] Test Basic auth success
    - [ ] Test Basic auth failure
    - [ ] Test Bearer token
    - [ ] Test API key in header
    - [ ] Test API key in query
    - [ ] Test OAuth2 token flow

- [ ] **Retry logic tests** 🔄
    - [ ] Test retry on 500 error
    - [ ] Test retry on 429 (rate limit)
    - [ ] Test exponential backoff
    - [ ] Test max retries exceeded
    - [ ] Test retry gives up on 404

- [ ] **Timeout tests** ⏱️
    - [ ] Test request timeout
    - [ ] Test connection timeout
    - [ ] Test cancellation

- [ ] **Webhook trigger tests** 🪝
    - [ ] Test webhook receives request
    - [ ] Test workflow triggered
    - [ ] Test signature validation
    - [ ] Test invalid webhook ID

**Deliverables:**
- ✅ Can make authenticated HTTP requests to any API
- ✅ Workflows triggered via webhooks reliably
- ✅ Retry logic works correctly with backoff
- ✅ All HTTP methods supported
- ✅ Response transformation working
- ✅ 90%+ test coverage on HTTP modules

---

### 2.4 Database Modules (Week 11-12)

**Tasks:**
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

### 2.5 File System Modules (Week 12-13)

**Tasks:**
- [ ] **Implement file read/write modules** 📁
    - [ ] Create `FileReadModule` class
        - [ ] ModuleId: `builtin.file.read`
        - [ ] DisplayName: `Read File`
        - [ ] Category: `File System`
    - [ ] Define FileReadModule schema
        - [ ] Input: `path` (string, required) - File path to read
        - [ ] Input: `encoding` (enum, optional, default=UTF8) - Text encoding
        - [ ] Input: `readAs` (enum, optional, default=Text) - Text, Binary, Lines
        - [ ] Input: `maxSize` (long, optional) - Max file size in bytes
        - [ ] Output: `content` (string or byte[]) - File content
        - [ ] Output: `size` (long) - File size in bytes
        - [ ] Output: `lastModified` (DateTime) - Last modified time
    - [ ] Implement FileReadModule ExecuteAsync
        - [ ] Validate file path (security check)
        - [ ] Check file exists
        - [ ] Check file size against limit
        - [ ] Read file based on readAs option
            - [ ] Text: Read all text with encoding
            - [ ] Binary: Read all bytes
            - [ ] Lines: Read lines into array
        - [ ] Get file metadata
        - [ ] Return content and metadata
    - [ ] Add path security validation
        - [ ] Prevent directory traversal attacks (../)
        - [ ] Validate against allowed paths
        - [ ] Check file extension whitelist
    - [ ] Create `FileWriteModule` class
        - [ ] ModuleId: `builtin.file.write`
        - [ ] DisplayName: `Write File`
        - [ ] Category: `File System`
    - [ ] Define FileWriteModule schema
        - [ ] Input: `path` (string, required) - File path to write
        - [ ] Input: `content` (string or byte[], required) - Content to write
        - [ ] Input: `encoding` (enum, optional) - Text encoding
        - [ ] Input: `mode` (enum, optional, default=Overwrite) - Overwrite, Append, CreateNew
        - [ ] Input: `createDirectory` (bool, optional, default=true)
        - [ ] Output: `bytesWritten` (long)
        - [ ] Output: `success` (bool)
    - [ ] Implement FileWriteModule ExecuteAsync
        - [ ] Validate file path
        - [ ] Create directory if needed
        - [ ] Write content based on mode
        - [ ] Handle file locking
        - [ ] Return bytes written
    - [ ] Add comprehensive tests
        - [ ] Test read text file
        - [ ] Test read binary file
        - [ ] Test write text file
        - [ ] Test append mode
        - [ ] Test path traversal prevention
        - [ ] Test file not found error
        - [ ] Test insufficient permissions error

- [ ] **Add CSV parsing and generation** 📊
    - [ ] Install `CsvHelper` NuGet package
    - [ ] Create `CsvReadModule` class
        - [ ] ModuleId: `builtin.file.csv.read`
        - [ ] DisplayName: `Read CSV`
        - [ ] Category: `File System`
    - [ ] Define CsvReadModule schema
        - [ ] Input: `path` (string, required) - CSV file path
        - [ ] Input: `hasHeader` (bool, optional, default=true)
        - [ ] Input: `delimiter` (string, optional, default=",")
        - [ ] Input: `encoding` (enum, optional)
        - [ ] Input: `skipEmptyRows` (bool, optional, default=true)
        - [ ] Output: `rows` (array) - Array of objects/dictionaries
        - [ ] Output: `rowCount` (int)
        - [ ] Output: `columns` (array) - Column names
    - [ ] Implement CsvReadModule ExecuteAsync
        - [ ] Read CSV file with CsvHelper
        - [ ] Parse with configuration
        - [ ] Map to dictionary/object array
        - [ ] Handle quoted fields
        - [ ] Handle escaped delimiters
        - [ ] Return structured data
    - [ ] Create `CsvWriteModule` class
        - [ ] ModuleId: `builtin.file.csv.write`
        - [ ] DisplayName: `Write CSV`
        - [ ] Category: `File System`
    - [ ] Define CsvWriteModule schema
        - [ ] Input: `path` (string, required)
        - [ ] Input: `data` (array, required) - Array of objects
        - [ ] Input: `includeHeader` (bool, optional, default=true)
        - [ ] Input: `delimiter` (string, optional)
        - [ ] Input: `encoding` (enum, optional)
        - [ ] Output: `rowsWritten` (int)
        - [ ] Output: `success` (bool)
    - [ ] Implement CsvWriteModule ExecuteAsync
        - [ ] Generate CSV with CsvHelper
        - [ ] Write to file
        - [ ] Handle special characters
        - [ ] Quote fields as needed
    - [ ] Add comprehensive tests
        - [ ] Test read CSV with header
        - [ ] Test read CSV without header
        - [ ] Test custom delimiter (tab, semicolon)
        - [ ] Test quoted fields
        - [ ] Test write CSV from objects
        - [ ] Test empty data

- [ ] **Add JSON processing** 📝
    - [ ] Create `JsonReadModule` class
        - [ ] ModuleId: `builtin.file.json.read`
        - [ ] DisplayName: `Read JSON`
        - [ ] Category: `File System`
    - [ ] Define JsonReadModule schema
        - [ ] Input: `path` (string, required)
        - [ ] Input: `encoding` (enum, optional)
        - [ ] Output: `data` (object) - Parsed JSON
        - [ ] Output: `isArray` (bool) - Whether root is array
    - [ ] Implement JsonReadModule ExecuteAsync
        - [ ] Read file content
        - [ ] Parse JSON with System.Text.Json
        - [ ] Handle parse errors
        - [ ] Return deserialized object
    - [ ] Create `JsonWriteModule` class
        - [ ] ModuleId: `builtin.file.json.write`
        - [ ] DisplayName: `Write JSON`
        - [ ] Category: `File System`
    - [ ] Define JsonWriteModule schema
        - [ ] Input: `path` (string, required)
        - [ ] Input: `data` (object, required)
        - [ ] Input: `indented` (bool, optional, default=true)
        - [ ] Input: `encoding` (enum, optional)
        - [ ] Output: `success` (bool)
    - [ ] Implement JsonWriteModule ExecuteAsync
        - [ ] Serialize object to JSON
        - [ ] Format with indentation if requested
        - [ ] Write to file
    - [ ] Create `JsonQueryModule` class (JSONPath queries)
        - [ ] ModuleId: `builtin.transform.jsonquery`
        - [ ] Input: JSONPath expression
        - [ ] Output: Matching elements
    - [ ] Add comprehensive tests
        - [ ] Test read simple JSON object
        - [ ] Test read JSON array
        - [ ] Test write JSON with indentation
        - [ ] Test write JSON compact
        - [ ] Test JSONPath queries
        - [ ] Test invalid JSON error

- [ ] **Add XML processing** 🏷️
    - [ ] Create `XmlReadModule` class
        - [ ] ModuleId: `builtin.file.xml.read`
        - [ ] DisplayName: `Read XML`
        - [ ] Category: `File System`
    - [ ] Define XmlReadModule schema
        - [ ] Input: `path` (string, required)
        - [ ] Input: `encoding` (enum, optional)
        - [ ] Input: `validateSchema` (bool, optional, default=false)
        - [ ] Input: `schemaPath` (string, optional) - XSD schema file
        - [ ] Output: `data` (object) - Parsed XML
        - [ ] Output: `rootElement` (string)
    - [ ] Implement XmlReadModule ExecuteAsync
        - [ ] Read XML file
        - [ ] Parse with XDocument
        - [ ] Optionally validate against schema
        - [ ] Convert to dictionary/object
        - [ ] Return structured data
    - [ ] Create `XmlWriteModule` class
        - [ ] ModuleId: `builtin.file.xml.write`
        - [ ] DisplayName: `Write XML`
        - [ ] Category: `File System`
    - [ ] Define XmlWriteModule schema
        - [ ] Input: `path` (string, required)
        - [ ] Input: `data` (object, required)
        - [ ] Input: `rootElement` (string, optional, default="root")
        - [ ] Input: `indented` (bool, optional, default=true)
        - [ ] Output: `success` (bool)
    - [ ] Implement XmlWriteModule ExecuteAsync
        - [ ] Convert object to XML
        - [ ] Format with indentation
        - [ ] Write to file
    - [ ] Create `XmlQueryModule` class (XPath queries)
        - [ ] ModuleId: `builtin.transform.xmlquery`
        - [ ] Input: XPath expression
        - [ ] Output: Matching nodes
    - [ ] Add comprehensive tests
        - [ ] Test read XML document
        - [ ] Test write XML document
        - [ ] Test XPath queries
        - [ ] Test schema validation
        - [ ] Test namespaces handling
        - [ ] Test invalid XML error

- [ ] **Implement file compression/decompression** 🗜️
    - [ ] Create `CompressModule` class
        - [ ] ModuleId: `builtin.file.compress`
        - [ ] DisplayName: `Compress Files`
        - [ ] Category: `File System`
    - [ ] Define CompressModule schema
        - [ ] Input: `sourcePath` (string or array, required) - Files to compress
        - [ ] Input: `outputPath` (string, required) - Output archive path
        - [ ] Input: `format` (enum, required) - Zip, GZip, Tar, TarGz
        - [ ] Input: `compressionLevel` (enum, optional) - Optimal, Fastest, NoCompression
        - [ ] Input: `includeBaseDirectory` (bool, optional)
        - [ ] Output: `archivePath` (string)
        - [ ] Output: `compressedSize` (long)
        - [ ] Output: `originalSize` (long)
        - [ ] Output: `compressionRatio` (decimal)
    - [ ] Implement CompressModule ExecuteAsync
        - [ ] Create archive based on format
            - [ ] Zip: Use System.IO.Compression.ZipFile
            - [ ] GZip: Use System.IO.Compression.GZipStream
            - [ ] Tar: Use SharpZipLib
            - [ ] TarGz: Combine Tar + GZip
        - [ ] Add files/directories to archive
        - [ ] Apply compression level
        - [ ] Calculate compression ratio
        - [ ] Return archive info
    - [ ] Create `DecompressModule` class
        - [ ] ModuleId: `builtin.file.decompress`
        - [ ] DisplayName: `Decompress Files`
        - [ ] Category: `File System`
    - [ ] Define DecompressModule schema
        - [ ] Input: `archivePath` (string, required)
        - [ ] Input: `outputDirectory` (string, required)
        - [ ] Input: `overwrite` (bool, optional, default=false)
        - [ ] Output: `extractedFiles` (array)
        - [ ] Output: `fileCount` (int)
    - [ ] Implement DecompressModule ExecuteAsync
        - [ ] Detect archive format
        - [ ] Extract files to directory
        - [ ] Handle existing files
        - [ ] Return extraction info
    - [ ] Add comprehensive tests
        - [ ] Test Zip compression/decompression
        - [ ] Test GZip compression/decompression
        - [ ] Test Tar compression/decompression
        - [ ] Test compression levels
        - [ ] Test multiple files
        - [ ] Test directory structure preservation

- [ ] **Add cloud storage support (S3, Azure Blob)** ☁️
    - [ ] Create `S3Module` class
        - [ ] ModuleId: `builtin.cloud.s3`
        - [ ] DisplayName: `Amazon S3 Operations`
        - [ ] Category: `Cloud Storage`
    - [ ] Define S3Module schema
        - [ ] Input: `operation` (enum) - Upload, Download, Delete, List
        - [ ] Input: `bucket` (string, required)
        - [ ] Input: `key` (string, required for Upload/Download/Delete)
        - [ ] Input: `localPath` (string, for Upload/Download)
        - [ ] Input: `prefix` (string, for List)
        - [ ] Input: `accessKey` (string, required)
        - [ ] Input: `secretKey` (string, required)
        - [ ] Input: `region` (string, optional)
        - [ ] Input: `contentType` (string, optional)
        - [ ] Output: `success` (bool)
        - [ ] Output: `objects` (array, for List)
        - [ ] Output: `url` (string, for Upload)
    - [ ] Implement S3Module ExecuteAsync
        - [ ] Initialize S3 client
        - [ ] Execute operation
            - [ ] Upload: PutObjectAsync
            - [ ] Download: GetObjectAsync
            - [ ] Delete: DeleteObjectAsync
            - [ ] List: ListObjectsV2Async
        - [ ] Handle errors
        - [ ] Return operation result
    - [ ] Create `AzureBlobModule` class
        - [ ] ModuleId: `builtin.cloud.azure`
        - [ ] DisplayName: `Azure Blob Storage`
        - [ ] Category: `Cloud Storage`
    - [ ] Define AzureBlobModule schema
        - [ ] Input: `operation` (enum)
        - [ ] Input: `connectionString` (string, required)
        - [ ] Input: `containerName` (string, required)
        - [ ] Input: `blobName` (string, required)
        - [ ] Input: `localPath` (string, optional)
        - [ ] Output: `success` (bool)
        - [ ] Output: `blobs` (array, for List)
    - [ ] Implement AzureBlobModule ExecuteAsync
        - [ ] Initialize blob client
        - [ ] Execute operation
        - [ ] Handle errors
        - [ ] Return result
    - [ ] Add comprehensive tests
        - [ ] Test S3 upload (with LocalStack)
        - [ ] Test S3 download
        - [ ] Test S3 list objects
        - [ ] Test Azure Blob upload (with Azurite)
        - [ ] Test Azure Blob download
        - [ ] Test error handling (invalid credentials)

**Modules:**
```
✅ builtin.file.read - Read file content
✅ builtin.file.write - Write file content
✅ builtin.file.csv.read - Read CSV
✅ builtin.file.csv.write - Write CSV
✅ builtin.file.json.read - Read JSON
✅ builtin.file.json.write - Write JSON
✅ builtin.file.xml.read - Read XML
✅ builtin.file.xml.write - Write XML
✅ builtin.file.compress - Compress files
✅ builtin.file.decompress - Decompress files
✅ builtin.cloud.s3 - Amazon S3 operations
✅ builtin.cloud.azure - Azure Blob Storage
```

**Tests:**
- [ ] **File I/O tests** 📁
    - [ ] Test read text file (UTF-8)
    - [ ] Test read text file (other encodings)
    - [ ] Test read binary file
    - [ ] Test write text file
    - [ ] Test write binary file
    - [ ] Test append mode
    - [ ] Test file not found
    - [ ] Test permission denied
    - [ ] Test path traversal prevention

- [ ] **CSV parsing tests** 📊
    - [ ] Test parse CSV with header
    - [ ] Test parse CSV without header
    - [ ] Test custom delimiter (tab, semicolon)
    - [ ] Test quoted fields with commas
    - [ ] Test escaped quotes
    - [ ] Test empty fields
    - [ ] Test write CSV from objects

- [ ] **JSON/XML processing tests** 📝
    - [ ] Test read JSON object
    - [ ] Test read JSON array
    - [ ] Test write JSON indented
    - [ ] Test JSONPath queries
    - [ ] Test read XML document
    - [ ] Test write XML document
    - [ ] Test XPath queries
    - [ ] Test XML schema validation

- [ ] **Compression tests** 🗜️
    - [ ] Test Zip compression
    - [ ] Test Zip decompression
    - [ ] Test GZip compression
    - [ ] Test preserve directory structure
    - [ ] Test compression levels
    - [ ] Test multiple files in archive

- [ ] **Cloud storage integration tests** ☁️
    - [ ] Test S3 upload (LocalStack container)
    - [ ] Test S3 download
    - [ ] Test S3 list objects with prefix
    - [ ] Test S3 delete object
    - [ ] Test Azure Blob upload (Azurite container)
    - [ ] Test Azure Blob download
    - [ ] Test authentication failures

**Deliverables:**
- ✅ Can read/write local files with encoding support
- ✅ Can parse and generate CSV/JSON/XML formats
- ✅ Can interact with cloud storage (S3, Azure Blob)
- ✅ Can compress/decompress files in multiple formats
- ✅ Path security prevents directory traversal
- ✅ 90%+ test coverage on file system modules
  ✅ builtin.file.json - JSON operations
  ✅ builtin.file.xml - XML operations
  ✅ builtin.file.compress - Zip/GZip
  ✅ builtin.cloud.s3 - Amazon S3
  ✅ builtin.cloud.azure - Azure Blob Storage

**Tests:**
- [ ] File I/O tests
- [ ] CSV parsing tests
- [ ] JSON/XML processing tests
- [ ] Compression tests
- [ ] Cloud storage integration tests

**Deliverables:**
- ✅ Can read/write local files
- ✅ Can parse and generate CSV/JSON/XML
- ✅ Can interact with cloud storage

---

### 2.6 Data Transformation Modules (Week 13)

**Tasks:**
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
        - [ ] Test sync execution
        - [ ] Test status query
        - [ ] Test cancel execution
        - [ ] Test list executions

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
        - [ ] POST /api/v1/auth/logout
            - [ ] Invalidate tokens
    - [ ] Implement authorization policies
        - [ ] Create `[Authorize]` attribute usage
        - [ ] Define roles (Admin, Developer, Viewer)
        - [ ] Define permissions (WorkflowCreate, WorkflowExecute, etc.)
    - [ ] Add comprehensive tests
        - [ ] Test API key authentication
        - [ ] Test JWT authentication
        - [ ] Test login endpoint
        - [ ] Test token refresh
        - [ ] Test authorization policies

- [ ] **Implement API versioning** 🔢
    - [ ] Install `Microsoft.AspNetCore.Mvc.Versioning`
    - [ ] Configure API versioning
        - [ ] URL-based versioning (/api/v1/, /api/v2/)
        - [ ] Header-based versioning (api-version header)
        - [ ] Query string versioning (?api-version=1.0)
    - [ ] Mark controllers with version
        - [ ] [ApiVersion("1.0")]
    - [ ] Implement version deprecation
        - [ ] Mark deprecated versions
        - [ ] Return deprecation warning in response header
    - [ ] Add comprehensive tests
        - [ ] Test v1 endpoints
        - [ ] Test version negotiation
        - [ ] Test deprecated version warnings

- [ ] **Add Swagger/OpenAPI documentation** 📚
    - [ ] Install `Swashbuckle.AspNetCore`
    - [ ] Configure Swagger generation
        - [ ] Add XML documentation
        - [ ] Configure schema generation
        - [ ] Add authentication schemes
    - [ ] Configure Swagger UI
        - [ ] Enable at /swagger
        - [ ] Add API key input
        - [ ] Add JWT bearer token input
        - [ ] Customize branding
    - [ ] Generate OpenAPI spec
        - [ ] Export as swagger.json
        - [ ] Version the API specification
    - [ ] Add API examples
        - [ ] Request examples
        - [ ] Response examples
        - [ ] Error examples
    - [ ] Add comprehensive tests
        - [ ] Test Swagger generation
        - [ ] Test UI accessibility
        - [ ] Validate OpenAPI spec

**Controllers:**
```csharp
✅ WorkflowsController - CRUD + Execute
✅ ModulesController - Module management
✅ VariablesController - Variable management
✅ MonitoringController - Health + Metrics
✅ WebhooksController - Webhook handling
✅ ExecutionsController - Execution management
✅ AuthController - Authentication
```

**Authentication:**
```csharp
✅ API Key authentication
✅ JWT token authentication
✅ Role-based authorization
✅ Rate limiting (per user/key)
```

**Tests:**
- [ ] **API endpoint tests** 🧪
    - [ ] Test all CRUD operations
    - [ ] Test execution endpoints
    - [ ] Test module endpoints
    - [ ] Test variable endpoints
    - [ ] Test monitoring endpoints
    - [ ] Test webhook endpoints
    - [ ] Test request/response formats

- [ ] **Authentication tests** 🔐
    - [ ] Test API key auth success
    - [ ] Test API key auth failure
    - [ ] Test JWT auth success
    - [ ] Test JWT auth failure
    - [ ] Test login with valid credentials
    - [ ] Test login with invalid credentials
    - [ ] Test token refresh
    - [ ] Test expired token

- [ ] **Authorization tests** 🛡️
    - [ ] Test role-based access
    - [ ] Test permission-based access
    - [ ] Test unauthorized access (403)
    - [ ] Test unauthenticated access (401)

- [ ] **Rate limiting tests** 🚦
    - [ ] Test rate limit enforcement
    - [ ] Test rate limit per user
    - [ ] Test rate limit per API key
    - [ ] Test rate limit headers
    - [ ] Test rate limit exceeded (429)

- [ ] **API versioning tests** 🔢
    - [ ] Test v1 endpoints
    - [ ] Test version routing
    - [ ] Test deprecated version warnings
    - [ ] Test unsupported version (404)

**Deliverables:**
- ✅ Full REST API operational with all endpoints
- ✅ Swagger documentation available at /swagger
- ✅ Authentication working (API Key + JWT)
- ✅ Authorization with roles and permissions
- ✅ Rate limiting in place per user/key
- ✅ API versioning implemented
- ✅ 90%+ test coverage on API controllers
- ✅ OpenAPI specification exported

---

### 2.8 Module System Enhancements (Deferred from Phase 1.4) 📦

> **CopilotNote:** These items were deferred from Phase 1.4 (Module System Foundation) because they go beyond foundational work. Phase 1.4 establishes the core module contracts, registry, validation, property binding, discovery, and basic dynamic loading. These items build on top of that foundation~ 💖

**Tasks:**
- [ ] **Define `.wfmod` package format** 📦
  - [ ] Define package structure (ZIP archive):
    - [ ] `module.json` — manifest with metadata, version, dependencies
    - [ ] `lib/` — module DLL(s) and dependency assemblies
    - [ ] `docs/` — README, changelog, examples
    - [ ] `assets/` — icons, screenshots
  - [ ] Create `ModuleManifest` class (deserialized from `module.json`)
    - [ ] `Id`, `Version`, `DisplayName`, `Description`, `Author`
    - [ ] `MinEngineVersion` — minimum DotFlow engine version required
    - [ ] `Dependencies` — list of other module IDs + version ranges
    - [ ] `EntryAssembly` — path to main DLL within package
  - [ ] Create `ModulePackageReader` class
    - [ ] Read and validate `.wfmod` ZIP structure
    - [ ] Deserialize manifest
    - [ ] Extract DLLs to isolated directory
    - [ ] Validate dependencies are available
  - [ ] Integrate with `AssemblyModuleLoader` (from Phase 1.4.6) for loading extracted DLLs
  - [ ] Add comprehensive tests
    - [ ] Test valid package loads correctly
    - [ ] Test invalid ZIP fails gracefully
    - [ ] Test missing manifest fails
    - [ ] Test missing DLL fails
    - [ ] Test dependency validation

- [ ] **Implement module hot-reload** 🔄
  - [ ] Create `IModuleWatcher` interface
    - [ ] `Watch(string directory)` — start watching for changes
    - [ ] `Stop()` — stop watching
    - [ ] `event Action<string> ModuleChanged` — fires on DLL/package changes
  - [ ] Implement `FileSystemModuleWatcher` using `FileSystemWatcher`
    - [ ] Monitor configured module directories
    - [ ] Debounce rapid changes (e.g., 500ms delay)
    - [ ] On change: unload old → load new via `AssemblyModuleLoader`
  - [ ] Handle running workflows gracefully
    - [ ] Don't unload modules with active executions
    - [ ] Queue reload until current executions complete
    - [ ] Publish `ModuleReloaded` event to Akka EventStream
  - [ ] Add comprehensive tests

- [ ] **Implement module versioning (side-by-side)** 🔢
  - [ ] Extend `IModuleRegistry` to support versioned lookups
    - [ ] `GetModule(string moduleId, Version? version = null)` — null = latest
    - [ ] `GetModuleVersions(string moduleId) → IReadOnlyList<Version>`
  - [ ] Store multiple versions per module ID in registry
  - [ ] Allow workflows to pin to specific module versions
    - [ ] Add `ModuleVersion` to `NodeDefinition` (optional)
    - [ ] Resolve at execution time: pinned version > latest
  - [ ] Handle breaking changes:
    - [ ] Validate schema compatibility between versions
    - [ ] Warn on incompatible upgrades
  - [ ] Add comprehensive tests

- [ ] **Implement full module dependency resolution** 🔗
  - [ ] Build on `IWorkflowModule.Dependencies` stub (from Phase 1.4.1)
  - [ ] Create `ModuleDependencyResolver` class
    - [ ] Topological sort of modules by dependency order
    - [ ] Detect circular dependencies
    - [ ] Validate all declared dependencies are registered
    - [ ] Report missing dependencies with clear messages
  - [ ] Wire into module loading: load/register in dependency order
  - [ ] Add comprehensive tests

- [ ] **Assembly signature verification** 🔏
  - [ ] Optionally verify assembly strong-name signatures on load
  - [ ] Create `IAssemblyVerifier` interface
    - [ ] `Verify(string assemblyPath) → bool`
  - [ ] Implement `StrongNameVerifier`
  - [ ] Allow trusted publisher list in configuration
  - [ ] Log warnings for unsigned assemblies (don't block by default)
  - [ ] Add comprehensive tests

**Tests:**
- [ ] Package format tests (valid/invalid packages)
- [ ] Hot-reload tests (watch, reload, running-workflow safety)
- [ ] Version management tests (side-by-side, pin, resolve)
- [ ] Dependency resolution tests (sort, circular detection)
- [ ] Assembly verification tests (signed, unsigned, trusted)

**Deliverables:**
- ✅ `.wfmod` packages can be loaded and validated
- ✅ Module hot-reload works with file watching
- ✅ Multiple module versions can coexist
- ✅ Module dependencies resolved automatically
- ✅ Assembly signatures optionally verified

---

### Phase 2 Success Criteria ✨

**Must Have:**
- [ ] All 3 persistence providers working (PostgreSQL, NATS KV, S3)
- [ ] Conditionals, loops, and parallel execution working
- [ ] 20+ built-in modules operational
- [ ] Complete REST API with auth
- [ ] `.wfmod` package format defined and loadable
- [ ] Module versioning (side-by-side) operational
- [ ] 80%+ code coverage maintained

**Demo Workflow:**
```
Webhook Trigger → HTTP GET API → Transform JSON → 
Condition (if valid) → True: Database INSERT → Log Success
                    → False: Log Error
```

---

*Made with 💖 by Ami-Chan! UwU* ✨

**This is a COMPLETE self-contained Phase 2 roadmap!** Everything you need to implement Phase 2 is right here! 🎀

