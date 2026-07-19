# 🎨 Phase 3: Advanced Features (Weeks 15-22)

**Goal:** Add scripting, UI, and advanced capabilities! 🌟

[Back to Main Design Requirements](../design-requirements.md) | [All Phases](README.md)

---

## Overview

Phase 3 adds the most user-facing features:
- Multi-language scripting engine (JavaScript, Lua, Python)
- SignalR real-time hub for live updates
- Visual workflow designer UI
- Script editor with IntelliSense
- Execution monitoring dashboard
- Module manager interface
- Client SDKs (C#, TypeScript, Python)

**Timeline:** 8 weeks  
**Team Size:** 3-4 developers (frontend + backend split)  
**Target Coverage:** 75%+ (UI brings down average)

---

> **💡 Note to AI (Ami-Chan):** This file contains the COMPLETE Phase 3 implementation roadmap with ALL detailed tasks, tests, and deliverables. You can work directly from this file without needing to reference design-requirements.md! Everything you need is right here, uwu~! 💖

---

## Quick Navigation

- [3.1 Scripting Engine (Week 15-17)](#31-scripting-engine-week-15-17)
- [3.2 SignalR Real-Time Hub (Week 17)](#32-signalr-real-time-hub-week-17)
- [3.3 UI - Visual Workflow Designer (Week 18-19)](#33-ui---visual-workflow-designer-week-18-19)
- [3.4 UI - Script Editor (Week 20)](#34-ui---script-editor-week-20)
- [3.5 UI - Execution Monitor (Week 21)](#35-ui---execution-monitor-week-21)
- [3.6 UI - Module Manager (Week 21)](#36-ui---module-manager-week-21)
- [3.7 Client SDKs (Week 22)](#37-client-sdks-week-22)
- [Phase 3 Success Criteria](#phase-3-success-criteria-)

---

## 🎨 Phase 3: Advanced Features (Weeks 15-22)

**Goal:** Add scripting, UI, and advanced capabilities! 🌟

### 3.1 Scripting Engine (Week 15-17) — COMPLETE ✅

> **📋 Detailed sliced plan available:** [Phase3-1-ScriptingEngine.md](Phase3-1-ScriptingEngine.md) — `IScriptExecutor` seam + JavaScript (Jint, already in the tree), C# (adapting the existing `Workflow.Scripting.Roslyn` core), and Lua (MoonSharp, quarantined project); capability-gated `IWorkflowScriptApi` (variables/logging/utilities always-on; HTTP/file gated **deny-by-default**, **no raw database API**); `builtin.script` module; blob-backed script libraries; `/api/v1/scripts/test` Minimal-API endpoints; and the deferred PropertyBinder inline-expression evaluation. **Python is deferred to 3.1.P1**; **Lua coroutine bridging is planned but deferred to 3.1.P5** (the MVP executor is built coroutine-ready). The sliced doc reconciles this checklist against Phase 2's existing scripting infrastructure and supersedes it. **Timeline: Weeks 23-25.** Q1–Q7 resolved ✅. **All 8 slices (3.1.0–3.1.7) implemented, tested, and documented — see the sliced plan for status; docs in [`docs/scripting.md`](../docs/scripting.md).**

**Tasks:**
- [ ] **Implement JavaScript executor (Jint)** 🟨
  - [ ] Install `Jint` NuGet package
  - [ ] Create `JavaScriptExecutor` class implementing `IScriptExecutor`
  - [ ] Configure Jint engine options
    - [ ] Set timeout interval
    - [ ] Set memory limit
    - [ ] Configure strict mode
  - [ ] Implement ExecuteAsync method
    - [ ] Create new Engine instance
    - [ ] Inject workflow API object
    - [ ] Set input data as global variable
    - [ ] Execute script code
    - [ ] Extract return value
    - [ ] Handle script errors
  - [ ] Add API bridging
    - [ ] Expose IWorkflowScriptApi to JavaScript
    - [ ] Convert .NET types to JavaScript types
    - [ ] Convert JavaScript types to .NET types
  - [ ] Add comprehensive tests
    - [ ] Test simple script execution
    - [ ] Test API method calls
    - [ ] Test async operations
    - [ ] Test error handling
    - [ ] Test timeout enforcement
  
- [ ] **Implement Lua executor (MoonSharp)** 🌙
  - [ ] Install `MoonSharp` NuGet package
  - [ ] Create `LuaExecutor` class implementing `IScriptExecutor`
  - [ ] Configure MoonSharp script
    - [ ] Register API type with UserData
    - [ ] Set global timeout
  - [ ] Implement ExecuteAsync method
    - [ ] Create new Script instance
    - [ ] Register workflow API
    - [ ] Set input data as global
    - [ ] Execute Lua code
    - [ ] Extract return value (DynValue)
    - [ ] Convert to .NET objects
  - [ ] Add API bridging
    - [ ] Expose IWorkflowScriptApi to Lua
    - [ ] Handle Lua tables <-> .NET objects
    - [ ] Support coroutines for async
  - [ ] Add comprehensive tests
    - [ ] Test script execution
    - [ ] Test API calls
    - [ ] Test table manipulation
    - [ ] Test error handling
  
- [ ] **Implement Python executor (IronPython/Python.NET)** 🐍
  - [ ] Choose Python engine (IronPython or Python.NET)
  - [ ] Install appropriate NuGet package
  - [ ] Create `PythonExecutor` class implementing `IScriptExecutor`
  - [ ] Configure Python engine
    - [ ] Set up Python runtime
    - [ ] Configure module search paths
    - [ ] Set execution timeout
  - [ ] Implement ExecuteAsync method
    - [ ] Create engine and scope
    - [ ] Inject workflow API
    - [ ] Set input data in scope
    - [ ] Execute Python code
    - [ ] Extract return value
    - [ ] Handle Python exceptions
  - [ ] Add API bridging
    - [ ] Expose IWorkflowScriptApi to Python
    - [ ] Handle Python dicts <-> .NET objects
    - [ ] Support async/await in Python
  - [ ] Add comprehensive tests
    - [ ] Test script execution
    - [ ] Test API calls
    - [ ] Test list/dict operations
    - [ ] Test error handling
    - [ ] Test imports (if allowed)
  
- [ ] **Create unified scripting API** 🔧
  - [ ] Define `IWorkflowScriptApi` interface
  - [ ] Implement Variable Management APIs
    - [ ] `GetVariable(name)` - Get workflow variable
    - [ ] `SetVariable(name, value)` - Set workflow variable
    - [ ] `DeleteVariable(name)` - Delete variable
    - [ ] `VariableExists(name)` - Check if exists
  - [ ] Implement Logging APIs
    - [ ] `LogDebug(message)` - Debug level log
    - [ ] `LogInfo(message)` - Info level log
    - [ ] `LogWarning(message)` - Warning level log
    - [ ] `LogError(message, error)` - Error level log
  - [ ] Implement HTTP Client APIs
    - [ ] `HttpGet(url, headers)` - GET request
    - [ ] `HttpPost(url, body, headers)` - POST request
    - [ ] `HttpPut(url, body, headers)` - PUT request
    - [ ] `HttpDelete(url, headers)` - DELETE request
  - [ ] Implement Database APIs
    - [ ] `QueryDatabase(connectionString, query, params)` - Execute query
    - [ ] `ExecuteDatabase(connectionString, command, params)` - Execute command
  - [ ] Implement File System APIs
    - [ ] `ReadFileAsync(path)` - Read file content
    - [ ] `WriteFileAsync(path, content)` - Write file content
    - [ ] `FileExists(path)` - Check if file exists
    - [ ] `DeleteFile(path)` - Delete file
  - [ ] Implement Utility Functions
    - [ ] `NewGuid()` - Generate GUID
    - [ ] `Now()` - Get current DateTime
    - [ ] `FormatDateTime(date, format)` - Format date
    - [ ] `Base64Encode(data)` - Encode to Base64
    - [ ] `Base64Decode(data)` - Decode from Base64
    - [ ] `Hash(data, algorithm)` - Hash data (MD5, SHA256, etc.)
    - [ ] `ParseJson(json)` - Parse JSON string
    - [ ] `ToJson(object)` - Serialize to JSON
    - [ ] `ParseCsv(csv, hasHeader)` - Parse CSV
    - [ ] `ToCsv(data, includeHeader)` - Generate CSV
  - [ ] Implement Workflow Control APIs
    - [ ] `Wait(milliseconds)` - Pause execution
    - [ ] `GetExecutionId()` - Get current execution ID
    - [ ] `GetWorkflowId()` - Get current workflow ID
  - [ ] Add comprehensive tests for each API method
  
- [ ] **Implement script sandboxing** 🔒
  - [ ] Create `ScriptExecutionConfig` class
    - [ ] Add `Timeout` property (default 30s)
    - [ ] Add `MaxMemoryBytes` property (default 256MB)
    - [ ] Add `AllowNetwork` property (default true)
    - [ ] Add `AllowFileSystem` property (default false)
    - [ ] Add `AllowDatabase` property (default true)
    - [ ] Add `AllowedPaths` list (for file system)
    - [ ] Add `MaxHttpRequests` property
  - [ ] Implement timeout enforcement
    - [ ] Use CancellationToken with timeout
    - [ ] Kill script execution on timeout
    - [ ] Return timeout error
  - [ ] Implement memory limits
    - [ ] Configure engine memory limits
    - [ ] Monitor memory usage
    - [ ] Throw on exceeded limit
  - [ ] Implement network restrictions
    - [ ] Intercept HTTP calls
    - [ ] Block if AllowNetwork is false
    - [ ] Count requests against limit
  - [ ] Implement file system restrictions
    - [ ] Intercept file operations
    - [ ] Validate path against AllowedPaths
    - [ ] Block unauthorized access
  - [ ] Add comprehensive tests
    - [ ] Test timeout enforcement
    - [ ] Test memory limit
    - [ ] Test network blocking
    - [ ] Test file system blocking
    - [ ] Test allowed paths validation
  
- [ ] **Add script library system** 📚
  - [ ] Create `IScriptLibrary` interface
    - [ ] `RegisterLibraryAsync(library)` - Register library
    - [ ] `GetLibrary(libraryId)` - Get library by ID
    - [ ] `GetAllLibraries()` - List all libraries
    - [ ] `DeleteLibrary(libraryId)` - Remove library
  - [ ] Create `ScriptLibraryDefinition` class
    - [ ] Add `LibraryId` property
    - [ ] Add `Name` property
    - [ ] Add `Description` property
    - [ ] Add `Language` property
    - [ ] Add `Code` property
    - [ ] Add `ExportedFunctions` list
    - [ ] Add `Dependencies` list
  - [ ] Implement library loading
    - [ ] Load library code
    - [ ] Parse exported functions
    - [ ] Make available to scripts
  - [ ] Implement library import
    - [ ] JavaScript: `import * as lib from 'libraryId'`
    - [ ] Lua: `local lib = require('libraryId')`
    - [ ] Python: `import libraryId as lib`
  - [ ] Add comprehensive tests
    - [ ] Test library registration
    - [ ] Test library import in script
    - [ ] Test function calls from library
    - [ ] Test library dependencies
  
- [ ] **Create script testing endpoint** 🧪
  - [ ] Create `ScriptTestController`
  - [ ] Implement POST /api/v1/scripts/test
    - [ ] Accept script code and language
    - [ ] Accept test inputs
    - [ ] Execute script in sandbox
    - [ ] Return outputs and logs
    - [ ] Return execution time
  - [ ] Add script validation
    - [ ] Syntax checking
    - [ ] API usage validation
  - [ ] Add comprehensive tests
    - [ ] Test JavaScript execution
    - [ ] Test Lua execution
    - [ ] Test Python execution
    - [ ] Test error responses

**Components:**
```csharp
✅ IScriptExecutor interface
✅ JavaScriptExecutor (Jint)
✅ LuaExecutor (MoonSharp)
✅ PythonExecutor (IronPython/Python.NET)
✅ IWorkflowScriptApi (unified API)
✅ ScriptExecutionConfig (sandboxing)
✅ ScriptLibrary system
```

**Scripting API Categories:**
```csharp
✅ Variable management APIs (4 methods)
✅ Logging APIs (4 methods)
✅ HTTP client APIs (4 methods)
✅ Database APIs (2 methods)
✅ File system APIs (4 methods)
✅ Utility functions (10+ methods)
✅ Workflow control APIs (3 methods)
```

**Tests:**
- [ ] **Script execution tests (all 3 languages)** 🧪
  - [ ] Test JavaScript simple script
  - [ ] Test Lua simple script
  - [ ] Test Python simple script
  - [ ] Test return values
  - [ ] Test input data access
  
- [ ] **API functionality tests (each API method)** 🔧
  - [ ] Test all variable management APIs
  - [ ] Test all logging APIs
  - [ ] Test all HTTP APIs
  - [ ] Test all database APIs
  - [ ] Test all file system APIs
  - [ ] Test all utility functions
  - [ ] Test all workflow control APIs
  
- [ ] **Sandboxing tests (timeout, memory limits)** 🔒
  - [ ] Test timeout enforcement (script runs > timeout)
  - [ ] Test memory limit enforcement
  - [ ] Test network access blocking
  - [ ] Test file system access blocking
  - [ ] Test allowed paths validation
  
- [ ] **Performance tests** ⚡
  - [ ] Test execution speed (simple scripts)
  - [ ] Test overhead per language
  - [ ] Test concurrent script execution
  - [ ] Test memory usage
  
- [ ] **Security tests (escape sandbox attempts)** 🛡️
  - [ ] Test attempts to bypass timeout
  - [ ] Test attempts to access forbidden files
  - [ ] Test attempts to make unauthorized network calls
  - [ ] Test attempts to execute system commands
  - [ ] Test attempts to load unsafe modules

**Deliverables:**
- ✅ All 3 scripting languages working (JavaScript, Lua, Python)
- ✅ Script API fully functional (30+ methods)
- ✅ Sandboxing prevents malicious code effectively
- ✅ Script library system operational
- ✅ Script testing endpoint available
- ✅ 90%+ test coverage on scripting components
- ✅ Comprehensive API documentation

#### Property Binding Expression Evaluation *(Deferred from Phase 1.4)* 🧮

> **CopilotNote:** Phase 1.4 implements `PropertyBinder` with `{{Variable.Name}}` and `{{NodeId.Output}}`
> reference resolution, but defers inline expression evaluation (e.g., `{{1 + 2}}`, `{{Variable.Count * 2}}`)
> to Phase 3 since it overlaps with the scripting engine work~ 💖

- [ ] **Extend `PropertyBinder` with expression evaluation** 📐
  - [ ] Detect expression patterns in property binding templates (e.g., `{{Variable.Count > 5}}`)
  - [ ] Evaluate using one of the scripting engines (Jint/JavaScript recommended for lightweight eval)
  - [ ] Support arithmetic: `+`, `-`, `*`, `/`, `%`
  - [ ] Support comparison: `>`, `<`, `>=`, `<=`, `==`, `!=`
  - [ ] Support logical: `&&`, `||`, `!`
  - [ ] Support string interpolation within expressions
  - [ ] Sandbox expressions with strict timeout (e.g., 100ms)
  - [ ] Cache compiled expressions for performance
  - [ ] Add comprehensive tests
    - [ ] Test arithmetic expressions
    - [ ] Test comparison expressions
    - [ ] Test variable references in expressions
    - [ ] Test invalid expression errors
    - [ ] Test timeout on expensive expressions

---

### 3.2 SignalR Real-Time Hub (Week 17) — COMPLETE ✅

> **📋 Detailed sliced plan available:** [Phase3-2-SignalRRealTime.md](Phase3-2-SignalRRealTime.md) — a `WorkflowHub` that streams execution/node lifecycle events to subscribed clients via a hosted `ExecutionEventBridge` subscribing to the Akka `EventStream` the engine **already publishes** (`ExecutionStateChanged`/`NodeStateChanged`/`ProgressUpdate`/`WorkflowCompleted`/`WorkflowFailed`/`NodeExecution*`), so **`Workflow.Engine` gains no ASP.NET/SignalR dependency**. Typed client contracts (plain camelCase records, no LanguageExt leakage); group-based subscriptions (`workflow:{id}`/`execution:{id}`, `SubscribeToAll` admin-only); auth reuses the existing `WorkflowRead`/`Admin` policies with a query-string-token exemption for WebSockets; connection/subscription counts on `/api/v1/metrics`; reconnect via SignalR auto-reconnect + client re-subscribe. **Removes the legacy `Microsoft.AspNetCore.SignalR` 1.1.0 package** (server SignalR ships in the Web SDK shared framework). **Redis backplane → 3.2.P1; missed-event replay → 3.2.P2; `WorkflowUpdated` → 3.2.P4; resource-level authz → 3.2.P5.** The sliced doc reconciles this checklist against Phase 2's existing observability and supersedes it. **Timeline: Week 26.** Q1–Q7 resolved ✅.

**Tasks:**
- [ ] **Implement `WorkflowHub` SignalR hub** 📡
  - [ ] Install `Microsoft.AspNetCore.SignalR` NuGet package
  - [ ] Create `WorkflowHub` class inheriting from `Hub`
  - [ ] Implement connection management
    - [ ] Override `OnConnectedAsync()` - Track connections
    - [ ] Override `OnDisconnectedAsync()` - Clean up subscriptions
    - [ ] Store connection ID → user mapping
  - [ ] Implement subscription methods
    - [ ] `SubscribeToWorkflow(Guid workflowId)` - Subscribe to workflow events
    - [ ] `UnsubscribeFromWorkflow(Guid workflowId)` - Unsubscribe
    - [ ] `SubscribeToExecution(Guid executionId)` - Subscribe to execution
    - [ ] `UnsubscribeFromExecution(Guid executionId)` - Unsubscribe
    - [ ] `SubscribeToAll()` - Subscribe to all events (admin only)
  - [ ] Configure SignalR in Startup/Program.cs
    - [ ] Add SignalR services
    - [ ] Map hub endpoint (/hubs/workflow)
    - [ ] Configure CORS for SignalR
    - [ ] Configure authentication
  - [ ] Add comprehensive tests
    - [ ] Test hub connection
    - [ ] Test subscription methods
    - [ ] Test connection cleanup
  
- [ ] **Add execution event broadcasting** 📢
  - [ ] Create event broadcaster service
    - [ ] Inject `IHubContext<WorkflowHub>`
    - [ ] Implement broadcast methods for each event type
  - [ ] Integrate with workflow engine
    - [ ] Hook into WorkflowExecutor actor
    - [ ] Emit events on state changes
    - [ ] Include relevant data in events
  - [ ] Implement event types
    - [ ] `ExecutionStarted` - When execution begins
      - [ ] Include: executionId, workflowId, startTime, inputs
    - [ ] `ExecutionCompleted` - When execution finishes
      - [ ] Include: executionId, endTime, outputs, duration
    - [ ] `ExecutionFailed` - When execution fails
      - [ ] Include: executionId, error, failedAt, stackTrace
    - [ ] `NodeStarted` - When node begins execution
      - [ ] Include: executionId, nodeId, nodeName, startTime
    - [ ] `NodeCompleted` - When node finishes
      - [ ] Include: executionId, nodeId, endTime, outputs, duration
    - [ ] `NodeFailed` - When node fails
      - [ ] Include: executionId, nodeId, error
    - [ ] `ExecutionProgress` - Progress updates
      - [ ] Include: executionId, percentage, currentNode, message
    - [ ] `WorkflowUpdated` - When workflow definition changes
      - [ ] Include: workflowId, version, updatedBy, timestamp
  - [ ] Add event filtering
    - [ ] Only send to subscribed clients
    - [ ] Group-based broadcasting
  - [ ] Add comprehensive tests
    - [ ] Test each event type broadcast
    - [ ] Test filtering by subscription
    - [ ] Test multiple clients receiving events
  
- [ ] **Implement subscription management** 📋
  - [ ] Create subscription tracking
    - [ ] Dictionary<connectionId, HashSet<subscriptionKey>>
    - [ ] Thread-safe implementation
  - [ ] Implement group management
    - [ ] Add to group: `workflow:{workflowId}`
    - [ ] Add to group: `execution:{executionId}`
    - [ ] Remove from groups on unsubscribe
  - [ ] Implement permission checks
    - [ ] Verify user can access workflow
    - [ ] Verify user can access execution
    - [ ] Return error if unauthorized
  - [ ] Add comprehensive tests
    - [ ] Test add subscription
    - [ ] Test remove subscription
    - [ ] Test permission checks
    - [ ] Test concurrent subscription changes
  
- [ ] **Add connection state management** 🔌
  - [ ] Track active connections
    - [ ] Store connection metadata
    - [ ] Store user identity per connection
    - [ ] Store subscription list per connection
  - [ ] Implement heartbeat/ping
    - [ ] Send periodic ping from client
    - [ ] Respond with pong
    - [ ] Detect stale connections
  - [ ] Handle concurrent connections
    - [ ] Support multiple connections per user
    - [ ] Sync subscriptions across connections (optional)
  - [ ] Add connection metrics
    - [ ] Track active connection count
    - [ ] Track subscriptions count
    - [ ] Expose via monitoring endpoint
  - [ ] Add comprehensive tests
    - [ ] Test connection tracking
    - [ ] Test heartbeat
    - [ ] Test multiple connections per user
    - [ ] Test metrics collection
  
- [ ] **Implement reconnection logic** 🔄
  - [ ] Configure automatic reconnect on client
    - [ ] Exponential backoff strategy
    - [ ] Max retry attempts
    - [ ] Reconnect on network issues
  - [ ] Implement reconnection handling on server
    - [ ] Restore subscriptions after reconnect
    - [ ] Send missed events (optional - requires event store)
    - [ ] Validate authentication on reconnect
  - [ ] Add connection resilience
    - [ ] Handle temporary network failures
    - [ ] Graceful degradation
    - [ ] Queue messages during disconnect (client-side)
  - [ ] Add comprehensive tests
    - [ ] Test reconnection after disconnect
    - [ ] Test subscription restoration
    - [ ] Test missed event handling

**Events:**
```csharp
✅ ExecutionStarted(executionId, workflowId, startTime, inputs)
✅ ExecutionCompleted(executionId, endTime, outputs, duration)
✅ ExecutionFailed(executionId, error, failedAt, stackTrace)
✅ NodeStarted(executionId, nodeId, nodeName, startTime)
✅ NodeCompleted(executionId, nodeId, endTime, outputs, duration)
✅ NodeFailed(executionId, nodeId, error)
✅ ExecutionProgress(executionId, percentage, currentNode, message)
✅ WorkflowUpdated(workflowId, version, updatedBy, timestamp)
```

**Tests:**
- [ ] **Hub connection tests** 🔌
  - [ ] Test client can connect
  - [ ] Test authentication required
  - [ ] Test connection with valid token
  - [ ] Test connection with invalid token
  - [ ] Test disconnect handling
  
- [ ] **Event broadcasting tests** 📢
  - [ ] Test ExecutionStarted broadcast
  - [ ] Test ExecutionCompleted broadcast
  - [ ] Test ExecutionFailed broadcast
  - [ ] Test NodeStarted broadcast
  - [ ] Test NodeCompleted broadcast
  - [ ] Test NodeFailed broadcast
  - [ ] Test ExecutionProgress broadcast
  - [ ] Test WorkflowUpdated broadcast
  
- [ ] **Subscription tests** 📋
  - [ ] Test subscribe to workflow
  - [ ] Test subscribe to execution
  - [ ] Test unsubscribe from workflow
  - [ ] Test unsubscribe from execution
  - [ ] Test only subscribed clients receive events
  - [ ] Test permission checks
  
- [ ] **Reconnection tests** 🔄
  - [ ] Test automatic reconnection
  - [ ] Test subscription restoration
  - [ ] Test missed events (if implemented)
  - [ ] Test backoff strategy
  
- [ ] **Multiple client tests** 👥
  - [ ] Test multiple clients connected
  - [ ] Test broadcast to all subscribed clients
  - [ ] Test independent subscriptions
  - [ ] Test concurrent subscription changes

**Deliverables:**
- ✅ Real-time updates working via SignalR
- ✅ Multiple clients can subscribe independently
- ✅ Events broadcast correctly to subscribed clients
- ✅ Reconnection logic handles network issues
- ✅ Permission checks prevent unauthorized access
- ✅ 90%+ test coverage on SignalR hub

---

### 3.3 UI - Visual Workflow Designer (Week 18-19)

> **📋 Detailed sliced plan available:** [Phase3-3-WorkflowDesigner.md](Phase3-3-WorkflowDesigner.md) (master) with three implementation breakouts — [3.3.a Foundation](Phase3-3a-DesignerFoundation.md) (app refit, API client layer, workflow list, read-only canvas), [3.3.b Editing](Phase3-3b-DesignerEditing.md) (palette drag-and-drop, connections, schema-driven properties panel with **Monaco code editors**, undo/redo, two-stage save validation), [3.3.c Runtime](Phase3-3c-DesignerRuntime.md) (execute + live 3.2-hub overlay, execution history review, lightweight minimap, docs/polish). **Framework decision (user): Blazor WebAssembly for MVP** on the existing in-tree `Workflow.UI`/`Workflow.UI.Client` skeleton, with a hard "contracts-only + framework-free state services" boundary keeping a **React+TypeScript port additive** (→ 3.3.P7). Custom SVG/HTML canvas (no diagram library); the properties panel is schema-driven from `PropertyEditorType`; the **only new API code is `POST /api/v1/workflows/validate`** (wrapping the existing `ModuleAwareWorkflowValidator`) — everything else consumes the shipped 2.7 REST + 3.2 hub verbatim; `NodeDefinition.Position` already persists layout. ASCII mockups S1–S4 in the master plan. The sliced docs reconcile this checklist against Phases 2.7–3.2 and supersede it. **Timeline: Weeks 27-30.** Q1–Q7 resolved ✅ (Q3 → Monaco in MVP; Q5 → server validate endpoint in MVP; Q6 → lightweight minimap compromise).

> **CopilotNote (from Phase 2.8 Q3):** Module version pinning currently rides `NodeDefinition.Metadata["moduleVersion"]` (zero-migration, see [Phase2-8-ModuleSystem.md](Phase2-8-ModuleSystem.md) D6). **If the designer needs first-class version-pin UI, promote it to a real `NodeDefinition.ModuleVersion` field here** (serializer + migration + designer support — tracked as 2.8.P4). The metadata route remains supported either way~ 🔢 *(3.3 Q7 proposes: read-only display in MVP; pin UI → 3.3.P8/2.8.P4.)*

**Tasks:**
- [ ] **Choose UI framework (Blazor WebAssembly or React)** 🎨
  - [ ] Evaluate Blazor WebAssembly
    - [ ] Pros: C# throughout, MudBlazor, Blazor.Diagrams
    - [ ] Cons: Larger initial download, less ecosystem
  - [ ] Evaluate React + TypeScript
    - [ ] Pros: Rich ecosystem, React Flow, better performance
    - [ ] Cons: Different language, more tooling
  - [ ] Make decision based on team expertise
  - [ ] Document decision and rationale
  - [ ] Set up chosen framework project
    - [ ] Install dependencies
    - [ ] Configure build pipeline
    - [ ] Set up development server
    - [ ] Configure hot reload
  
- [ ] **Implement canvas component with pan/zoom** 🖼️
  - [ ] Choose canvas library
    - [ ] Blazor: Blazor.Diagrams
    - [ ] React: React Flow
  - [ ] Implement canvas initialization
    - [ ] Set up canvas container
    - [ ] Configure default zoom level
    - [ ] Configure initial viewport position
  - [ ] Implement pan functionality
    - [ ] Mouse drag to pan
    - [ ] Touch drag to pan (mobile)
    - [ ] Pan limits (don't pan too far out)
    - [ ] Pan animation/smoothing
  - [ ] Implement zoom functionality
    - [ ] Mouse wheel zoom
    - [ ] Pinch zoom (mobile)
    - [ ] Zoom to fit all nodes
    - [ ] Zoom to selection
    - [ ] Zoom limits (min 10%, max 300%)
  - [ ] Add zoom controls UI
    - [ ] Zoom in button (+)
    - [ ] Zoom out button (-)
    - [ ] Reset zoom button (100%)
    - [ ] Fit to screen button
  - [ ] Add minimap (optional)
    - [ ] Show overall workflow structure
    - [ ] Highlight current viewport
    - [ ] Click to navigate
  - [ ] Add comprehensive tests
    - [ ] Test pan with mouse
    - [ ] Test zoom with wheel
    - [ ] Test zoom controls
    - [ ] Test zoom limits
  
- [ ] **Implement node rendering** 🎯
  - [ ] Create NodeRenderer component
    - [ ] Display node icon
    - [ ] Display node name
    - [ ] Display node status (running, complete, failed)
    - [ ] Display node type/module
  - [ ] Implement node styling
    - [ ] Different colors for different states
    - [ ] Highlight on hover
    - [ ] Selection indicator
    - [ ] Error indicator
    - [ ] Running animation
  - [ ] Implement node ports
    - [ ] Input ports (left side)
    - [ ] Output ports (right side)
    - [ ] Multiple outputs support
    - [ ] Port labels
    - [ ] Port connection points
  - [ ] Add node context menu
    - [ ] Edit node
    - [ ] Delete node
    - [ ] Duplicate node
    - [ ] View outputs
    - [ ] Copy/Paste
  - [ ] Add comprehensive tests
    - [ ] Test node rendering
    - [ ] Test different node states
    - [ ] Test port rendering
    - [ ] Test context menu
  
- [ ] **Implement connection drawing** 🔗
  - [ ] Create ConnectionRenderer component
    - [ ] Draw curved lines (Bezier curves)
    - [ ] Connection start/end points
    - [ ] Connection labels (conditional)
  - [ ] Implement connection creation
    - [ ] Drag from output port
    - [ ] Highlight valid target ports
    - [ ] Snap to target port
    - [ ] Validate connection (no cycles)
    - [ ] Create connection on drop
  - [ ] Implement connection styling
    - [ ] Different colors for different types
    - [ ] Highlight on hover
    - [ ] Selection indicator
    - [ ] Animated flow (optional)
  - [ ] Add connection context menu
    - [ ] Delete connection
    - [ ] Add condition (for conditional)
  - [ ] Add comprehensive tests
    - [ ] Test connection rendering
    - [ ] Test connection creation
    - [ ] Test connection validation
    - [ ] Test connection deletion
  
- [ ] **Add drag-and-drop from module palette** 📦
  - [ ] Create ModulePalette component
    - [ ] Display all available modules
    - [ ] Group by category
    - [ ] Search/filter modules
    - [ ] Module descriptions
  - [ ] Implement drag-and-drop
    - [ ] Drag module from palette
    - [ ] Show drag preview
    - [ ] Drop on canvas at position
    - [ ] Create node from module
    - [ ] Generate unique node ID
  - [ ] Add module details panel
    - [ ] Show module description
    - [ ] Show input/output schema
    - [ ] Show usage examples
  - [ ] Add comprehensive tests
    - [ ] Test palette rendering
    - [ ] Test search/filter
    - [ ] Test drag-and-drop
    - [ ] Test node creation
  
- [ ] **Implement node selection and editing** ✏️
  - [ ] Implement single selection
    - [ ] Click node to select
    - [ ] Deselect on canvas click
    - [ ] Show selection highlight
  - [ ] Implement multi-selection
    - [ ] Ctrl+Click to add to selection
    - [ ] Drag selection rectangle
    - [ ] Select all (Ctrl+A)
  - [ ] Create PropertiesPanel component
    - [ ] Display selected node properties
    - [ ] Property input controls
      - [ ] Text inputs
      - [ ] Number inputs
      - [ ] Checkboxes
      - [ ] Dropdowns
      - [ ] Code editors
    - [ ] Property validation
    - [ ] Apply/Save button
  - [ ] Implement node editing
    - [ ] Edit node name
    - [ ] Edit node properties
    - [ ] Save changes to workflow definition
  - [ ] Add keyboard shortcuts
    - [ ] Delete selected (Delete key)
    - [ ] Copy selected (Ctrl+C)
    - [ ] Paste (Ctrl+V)
    - [ ] Select all (Ctrl+A)
  - [ ] Add comprehensive tests
    - [ ] Test selection
    - [ ] Test multi-selection
    - [ ] Test properties panel
    - [ ] Test property editing
    - [ ] Test keyboard shortcuts
  
- [ ] **Add undo/redo functionality** ↩️
  - [ ] Implement command pattern
    - [ ] Create Command interface
    - [ ] AddNodeCommand
    - [ ] DeleteNodeCommand
    - [ ] MoveNodeCommand
    - [ ] EditNodeCommand
    - [ ] AddConnectionCommand
    - [ ] DeleteConnectionCommand
  - [ ] Implement undo/redo stack
    - [ ] History stack (max 50 commands)
    - [ ] Current position pointer
    - [ ] Execute command
    - [ ] Undo command
    - [ ] Redo command
  - [ ] Add UI controls
    - [ ] Undo button (toolbar)
    - [ ] Redo button (toolbar)
    - [ ] Keyboard shortcuts (Ctrl+Z, Ctrl+Y)
    - [ ] Disable when at limits
  - [ ] Add comprehensive tests
    - [ ] Test undo add node
    - [ ] Test redo add node
    - [ ] Test undo delete node
    - [ ] Test undo/redo connection
    - [ ] Test history limit
  
- [ ] **Implement workflow save/load** 💾
  - [ ] Implement save functionality
    - [ ] Serialize workflow to JSON
    - [ ] Validate workflow before save
    - [ ] Call API to save workflow
    - [ ] Show save success/error
    - [ ] Auto-save (optional - every 30s)
  - [ ] Implement load functionality
    - [ ] Call API to load workflow
    - [ ] Deserialize JSON to workflow
    - [ ] Render nodes on canvas
    - [ ] Render connections
    - [ ] Handle load errors
  - [ ] Add workflow toolbar
    - [ ] New workflow button
    - [ ] Save button
    - [ ] Save as button
    - [ ] Load/Open button
    - [ ] Execute workflow button
  - [ ] Add unsaved changes warning
    - [ ] Track dirty state
    - [ ] Warn before closing
    - [ ] Warn before loading new workflow
  - [ ] Add comprehensive tests
    - [ ] Test save workflow
    - [ ] Test load workflow
    - [ ] Test auto-save
    - [ ] Test unsaved changes warning

**UI Framework Options:**
```
Option A: Blazor WebAssembly ✨
- Blazor WebAssembly (.NET 8)
- MudBlazor for components
- Blazor.Diagrams for canvas
- SignalR client for real-time

Option B: React 🎯
- React 18 + TypeScript
- React Flow for canvas
- Material-UI (MUI) for components
- @microsoft/signalr for real-time
```

**Components:**
```
✅ WorkflowCanvas - Main design surface with pan/zoom
✅ ModulePalette - Searchable module list with categories
✅ NodeRenderer - Individual node display with ports
✅ ConnectionRenderer - Connection lines with styling
✅ PropertiesPanel - Node configuration with validation
✅ Toolbar - Actions and controls (save, execute, etc.)
✅ ZoomControls - Pan/zoom controls
✅ Minimap - Overview map (optional)
```

**Tests:**
- [ ] **Component rendering tests** 🧪
  - [ ] Test canvas renders
  - [ ] Test module palette renders
  - [ ] Test node renderer
    - [ ] Test different node types
    - [ ] Test different node states
  - [ ] Test connection renderer
  - [ ] Test properties panel
  - [ ] Test toolbar
  
- [ ] **Interaction tests (drag, connect, select)** 🖱️
  - [ ] Test drag module from palette
  - [ ] Test drop module on canvas
  - [ ] Test drag to create connection
  - [ ] Test node selection (single)
  - [ ] Test node selection (multiple)
  - [ ] Test drag to move nodes
  - [ ] Test pan canvas
  - [ ] Test zoom canvas
  
- [ ] **Save/load tests** 💾
  - [ ] Test save workflow
  - [ ] Test load workflow
  - [ ] Test save/load preserves all data
  - [ ] Test validation on save
  - [ ] Test error handling
  
- [ ] **Undo/redo tests** ↩️
  - [ ] Test undo add node
  - [ ] Test redo add node
  - [ ] Test undo delete node
  - [ ] Test undo move node
  - [ ] Test undo add connection
  - [ ] Test undo/redo limits

**Deliverables:**
- ✅ Can create workflows visually in browser
- ✅ Drag-and-drop works smoothly from palette
- ✅ Workflows save and load correctly
- ✅ Professional canvas experience with pan/zoom
- ✅ Undo/redo functionality working
- ✅ Properties panel for node configuration
- ✅ Real-time execution visualization
- ✅ 80%+ test coverage on UI components

---

### 3.4 UI - Script Editor (Week 20)

**Tasks:**
- [ ] **Integrate Monaco Editor** 💻
  - [ ] Install Monaco Editor package
    - [ ] Blazor: `BlazorMonaco` NuGet
    - [ ] React: `@monaco-editor/react` npm
  - [ ] Create ScriptEditor component
  - [ ] Configure editor options
    - [ ] Theme (dark/light)
    - [ ] Font size and family
    - [ ] Line numbers
    - [ ] Minimap
    - [ ] Word wrap
  
- [ ] **Implement language-specific syntax highlighting** 🎨
  - [ ] Add JavaScript/TypeScript support
  - [ ] Add Lua language support
  - [ ] Add Python language support
  - [ ] Configure syntax themes
  
- [ ] **Add IntelliSense for workflow API** 💡
  - [ ] Create TypeScript definitions for API
  - [ ] Register custom completions
  - [ ] Add method signatures
  - [ ] Add parameter hints
  - [ ] Add hover documentation
  
- [ ] **Create script template library** 📚
  - [ ] HTTP request template
  - [ ] Database query template
  - [ ] Data transformation template
  - [ ] File processing template
  - [ ] Template insertion UI
  
- [ ] **Implement script testing interface** 🧪
  - [ ] Test button in editor
  - [ ] Input data editor
  - [ ] Execute script via API
  - [ ] Display outputs
  - [ ] Display logs
  - [ ] Display errors
  
- [ ] **Add API documentation viewer** 📖
  - [ ] Side panel with API docs
  - [ ] Searchable method list
  - [ ] Method details and examples
  - [ ] Copy example code

**Deliverables:**
- ✅ Professional code editor with Monaco
- ✅ IntelliSense for workflow API
- ✅ Can test scripts before saving
- ✅ Template library with 10+ templates

---

### 3.5 UI - Execution Monitor (Week 21)

**Tasks:**
- [ ] **Implement execution list view** 📋
  - [ ] Table/grid component
  - [ ] Columns: ID, Workflow, Status, Started, Duration
  - [ ] Pagination (20 per page)
  - [ ] Sort by column
  - [ ] Click row to view details
  
- [ ] **Add real-time execution status display** ⚡
  - [ ] Connect to SignalR hub
  - [ ] Subscribe to execution events
  - [ ] Update status indicators live
  - [ ] Show progress percentage
  - [ ] Highlight active executions
  
- [ ] **Create node-by-node progress visualization** 🎯
  - [ ] Highlight completed nodes (green)
  - [ ] Highlight active node (blue/animated)
  - [ ] Highlight failed nodes (red)
  - [ ] Show node execution times
  
- [ ] **Implement log viewer** 📜
  - [ ] Real-time log streaming
  - [ ] Log levels (Debug, Info, Warning, Error)
  - [ ] Filter by log level
  - [ ] Search logs
  - [ ] Copy/download logs
  
- [ ] **Add execution history with filtering** 🔍
  - [ ] Filter by workflow
  - [ ] Filter by status
  - [ ] Filter by date range
  - [ ] Filter by duration
  
- [ ] **Implement execution replay/debugging** 🐛
  - [ ] Step through node execution
  - [ ] View node inputs/outputs
  - [ ] View variables at each step
  - [ ] Timeline visualization

**Deliverables:**
- ✅ Can monitor executions in real-time
- ✅ Can view historical executions with filters
- ✅ Can debug workflow issues
- ✅ Professional monitoring experience

---

### 3.6 UI - Module Manager (Week 21)

**Tasks:**
- [ ] **Implement module browsing** 📦
  - [ ] Grid/list view of modules
  - [ ] Group by category
  - [ ] Search by name/description
  - [ ] Filter by category
  - [ ] Show module icons
  
- [ ] **Add module upload functionality** ⬆️
  - [ ] File upload component
  - [ ] Drag-and-drop support
  - [ ] Progress indicator
  - [ ] Validation feedback
  
- [ ] **Create module package validation** ✅
  - [ ] Validate .wfmod format
  - [ ] Check manifest.json
  - [ ] Verify module DLL
  - [ ] Check dependencies
  - [ ] Show validation errors
  
- [ ] **Implement module enable/disable** 🔘
  - [ ] Toggle switch per module
  - [ ] Disable dependent workflows warning
  - [ ] Enable/disable confirmation
  
- [ ] **Add module version management** 🔢
  - [ ] Show available versions
  - [ ] Upgrade to newer version
  - [ ] Rollback to older version
  
- [ ] **Create module documentation viewer** 📖
  - [ ] Display module README
  - [ ] Show input/output schema
  - [ ] Show usage examples
  - [ ] Show changelog

**Deliverables:**
- ✅ Can browse all modules with search
- ✅ Can upload custom modules
- ✅ Packages validated on upload
- ✅ Module versioning supported

---

### 3.7 Client SDKs (Week 22)

**Tasks:**
- [ ] **Create C# client SDK** 💎
  - [ ] Create Workflow.Client project
  - [ ] Implement WorkflowClient class
  - [ ] Add all API methods
  - [ ] Add SignalR support
  - [ ] Add async/await patterns
  - [ ] Add XML documentation
  - [ ] Create NuGet package
  - [ ] Publish to NuGet.org
  
- [ ] **Create TypeScript/JavaScript client SDK** 🟨
  - [ ] Create @workflow/client package
  - [ ] Implement WorkflowClient class
  - [ ] Add all API methods
  - [ ] Add SignalR support  
  - [ ] Add Promise-based API
  - [ ] Add TypeScript definitions
  - [ ] Create npm package
  - [ ] Publish to npmjs.com
  
- [ ] **Create Python client SDK** 🐍
  - [ ] Create workflow-client package
  - [ ] Implement WorkflowClient class
  - [ ] Add all API methods
  - [ ] Add async support (asyncio)
  - [ ] Add type hints
  - [ ] Add docstrings
  - [ ] Create PyPI package
  - [ ] Publish to PyPI
  
- [ ] **Add comprehensive examples** 📚
  - [ ] Quick start examples
  - [ ] Execute workflow example
  - [ ] Monitor execution example
  - [ ] Create workflow example
  - [ ] Real-time updates example
  
- [ ] **Publish SDK packages** 📦
  - [ ] Create README files
  - [ ] Write API documentation
  - [ ] Create changelog
  - [ ] Set up CI/CD for SDK builds
  - [ ] Version tagging strategy

**Deliverables:**
- ✅ SDKs published (NuGet, npm, PyPI)
- ✅ Documentation complete with examples
- ✅ Examples for common scenarios
- ✅ CI/CD for SDK releases

**SDK APIs:**
```csharp
✅ Workflow management
✅ Execution control
✅ Real-time updates (SignalR)
✅ Variable management
✅ Module browsing
```

**Tests:**
- [ ] SDK functionality tests
- [ ] Integration tests
- [ ] Example code tests

**Deliverables:**
- ✅ SDKs published
- ✅ Documentation complete
- ✅ Examples available

---

### Phase 3 Success Criteria ✨

**Must Have:**
- [ ] All 3 scripting languages working with full API
- [ ] Complete visual workflow designer operational
- [ ] SignalR broadcasting execution events
- [ ] 3 client SDKs published and documented
- [ ] 75%+ code coverage maintained

**Demo:**
```
Create workflow in UI → Add script node (JavaScript) → 
Execute via API → Monitor in real-time → View results
```

---

## Next Steps → Phase 4 💎

Once Phase 3 is complete, move on to:
**[Phase 4: Production](Phase4-Production.md)** - Performance, security, and launch readiness!

---

*Made with 💖 by Ami-Chan! UwU* ✨

**This is now a COMPLETE self-contained Phase 3 roadmap!** Everything you need to implement Phase 3 is right here! 🎀

