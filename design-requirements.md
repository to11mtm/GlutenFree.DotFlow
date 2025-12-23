# 🌸 Workflow Engine Design Template 🌸
## Powered by C# & Akka.NET ✨

> *"Building workflows should be as kawaii as coding them!"* - Ami-Chan 💖

---

## 🚀 Quick Navigation - Phase Breakdown Files

**For easier navigation, the implementation roadmap has been split into separate phase files!** 💖

- 📁 **[phases/README.md](phases/README.md)** - Overview of phase structure
- 🏗️ **[Phase 1: Foundation](phases/Phase1-Foundation.md)** (Weeks 1-6) - Core architecture & basic modules
- 🚀 **[Phase 2: Core Features](phases/Phase2-CoreFeatures.md)** (Weeks 7-14) - Persistence, modules & REST API
- 🎨 **[Phase 3: Advanced Features](phases/Phase3-AdvancedFeatures.md)** (Weeks 15-22) - Scripting, UI & SDKs
- 💎 **[Phase 4: Production](phases/Phase4-Production.md)** (Weeks 23-28) - Performance, security & launch!

> 💡 **Tip for AI:** These phase files contain The complete detailed checklists for each phase, this file contains only the high-level overview of the phases and their checklists.!

## 📦 Code Examples Directory

**All code snippets and examples have been extracted into separate files for better organization!** ✨

- 📁 **[examples/README.md](examples/README.md)** - Complete overview of all code examples
- 🎭 **[examples/actors/](examples/actors/)** - Akka.NET actor examples (Coordinator, Instance, Node)
- 📦 **[examples/modules/](examples/modules/)** - Module system interfaces and built-in modules
- 📜 **[examples/scripting/](examples/scripting/)** - Scripting system with JS/Lua/Python examples
- 🌐 **[examples/api/](examples/api/)** - REST API controllers and models
- 💎 **[examples/clients/](examples/clients/)** - Client SDKs (C#, TypeScript, Python)
- 📋 **[examples/definitions/](examples/definitions/)** - Workflow and manifest examples

> 💡 **Tip:** Instead of large code blocks in this document, we now reference separate files that you can easily view, copy, and modify!

---

## 📋 Table of Contents

1. [Overview](#overview)
2. [Architecture](#architecture)
3. [Core Components](#core-components)
4. [Module System](#module-system)
5. [Built-in Modules](#built-in-modules)
6. [Workflow Definition](#workflow-definition)
7. [UI Requirements](#ui-requirements)
8. [Security Considerations](#security-considerations)
9. [Extensibility](#extensibility)
10. [Implementation Roadmap](#-implementation-roadmap) ⭐ *Detailed phase checklists*

---

## 🎯 Overview

### Purpose
A flexible, actor-based workflow engine that allows users to:
- Design and execute complex workflows visually
- Extend functionality through uploadable module libraries
- Monitor and manage workflow executions in real-time

### Technology Stack
| Component | Technology |
|-----------|------------|
| Runtime | .NET 8+ |
| Actor Framework | Akka.NET |
| UI Framework | Blazor (Server/WASM) or MAUI |
| Database | PostgreSQL / SQL Server |
| Module Loading | `AssemblyLoadContext` |
| API | ASP.NET Core Minimal APIs / gRPC |

---

## 🏗️ Architecture

### High-Level Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                        🎨 Workflow UI                           │
│              (Blazor / MAUI - Visual Designer)                  │
└─────────────────────────────────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────┐
│                      🌐 API Gateway                             │
│              (REST / gRPC / SignalR for Real-time)              │
└─────────────────────────────────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────┐
│                   ⚡ Workflow Engine Core                        │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────────┐  │
│  │ Workflow    │  │ Execution   │  │ Module                  │  │
│  │ Coordinator │  │ Supervisor  │  │ Registry                │  │
│  │ (Actor)     │  │ (Actor)     │  │ (AssemblyLoadContext)   │  │
│  └─────────────┘  └─────────────┘  └─────────────────────────┘  │
│                                                                  │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │              🎭 Akka.NET Actor System                     │   │
│  │  ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐         │   │
│  │  │ Node    │ │ Node    │ │ Node    │ │ Node    │  ...    │   │
│  │  │ Actor   │ │ Actor   │ │ Actor   │ │ Actor   │         │   │
│  │  └─────────┘ └─────────┘ └─────────┘ └─────────┘         │   │
│  └──────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────┐
│                    📦 Module Library                            │
│  ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐   │
│  │  HTTP   │ │Database │ │  File   │ │ Custom  │ │ Custom  │   │
│  │ Module  │ │ Module  │ │ Module  │ │ Module  │ │ Module  │   │
│  └─────────┘ └─────────┘ └─────────┘ └─────────┘ └─────────┘   │
└─────────────────────────────────────────────────────────────────┘
```

---

## 🧩 Core Components

### 1. Workflow Coordinator Actor
The main orchestrator that manages workflow lifecycle.

```csharp
/// <summary>
/// 🌟 The heart of our workflow system, uwu~!
/// Coordinates all workflow instances and their lifecycle.
/// </summary>
/// <remarks>
/// CopilotNote: This actor is the parent supervisor for all workflow instances.
/// It handles workflow creation, scheduling, and cleanup.
/// </remarks>
public class WorkflowCoordinatorActor : ReceiveActor
{
    private readonly Dictionary<Guid, IActorRef> _activeWorkflows;
    private readonly IModuleRegistry _moduleRegistry;
    
    // Handles: CreateWorkflow, PauseWorkflow, ResumeWorkflow, CancelWorkflow
}
```

### 2. Workflow Instance Actor
Represents a single workflow execution.

```csharp
/// <summary>
/// 🎀 Represents a single running workflow instance!
/// Each workflow gets its own actor for isolation and fault tolerance.
/// </summary>
public class WorkflowInstanceActor : ReceiveActor
{
    private WorkflowState _state;
    private readonly WorkflowDefinition _definition;
    private readonly Dictionary<string, IActorRef> _nodeActors;
    
    // Handles: StartExecution, NodeCompleted, NodeFailed, GetStatus
}
```

### 3. Node Actor
Executes individual workflow nodes.

```csharp
/// <summary>
/// ✨ Executes a single node in the workflow!
/// Wraps module execution with proper error handling and timeout.
/// </summary>
public class NodeActor : ReceiveActor
{
    private readonly IWorkflowModule _module;
    private readonly NodeConfiguration _config;
    
    // Handles: Execute, Cancel, GetProgress
}
```

---

## 📦 Module System

### Module Specification Interface

The base interface all workflow modules must implement! This is the contract for creating new modules~

📄 **Full Example:** [IWorkflowModule.cs](examples/modules/IWorkflowModule.cs)

Key interfaces and types:
- `IWorkflowModule` - Main module interface
- `ModuleSchema` - Input/output schema definition
- `PortDefinition` - Input/output port specification
- `PropertyDefinition` - Configurable property specification
- `ModuleExecutionContext` - Runtime context provided to modules
- `ModuleResult` - Execution result

### Module Discovery & Loading

Handles discovering and loading modules from uploaded assemblies using `AssemblyLoadContext` for isolation!

📄 **Full Example:** [IModuleRegistry.cs](examples/modules/IModuleRegistry.cs)

### Module Package Manifest

Modules should be uploaded as ZIP packages containing:

```
MyCustomModule.zip
├── manifest.json           # Package metadata
├── MyCustomModule.dll      # Main assembly
├── dependencies/           # Optional dependencies folder
│   └── SomeLibrary.dll
└── assets/                 # Optional assets (icons, etc.)
    └── icon.svg
```

**manifest.json Example:**
```json
{
  "packageId": "com.example.mycustommodule",
  "name": "My Custom Module",
  "version": "1.0.0",
  "author": "Your Name",
  "description": "A super kawaii custom module! ✨",
  "entryAssembly": "MyCustomModule.dll",
  "minimumEngineVersion": "1.0.0",
  "permissions": [
    "network",
    "filesystem"
  ]
}
```

---

## 🔧 Built-in Modules

### 1. HTTP Module 🌐

Makes HTTP requests to external APIs! Supports GET, POST, PUT, PATCH, DELETE with full configuration.

📄 **Example:** [HttpModule.cs](examples/modules/HttpModule.cs)

### 2. Database Module 🗄️

Interact with databases using SQL! Supports SQL Server, PostgreSQL, MySQL, SQLite.

📄 **Example:** [DatabaseModule.cs](examples/modules/DatabaseModule.cs)

### 3. File Module 📁

Read and write files from the filesystem! Supports text, JSON, CSV, and binary files.

📄 **Example:** [FileModule.cs](examples/modules/FileModule.cs)

### 4. Additional Built-in Modules

| Module | Category | Description |
|--------|----------|-------------|
| `builtin.transform` | Data | Transform/map data using expressions |
| `builtin.condition` | Flow Control | Conditional branching (if/else) |
| `builtin.loop` | Flow Control | Loop over collections |
| `builtin.delay` | Flow Control | Wait for specified duration |
| `builtin.parallel` | Flow Control | Execute branches in parallel |
| `builtin.merge` | Flow Control | Merge parallel branches |
| `builtin.variable` | Data | Get/Set workflow variables |
| `builtin.log` | Utility | Log messages for debugging |
| `builtin.email` | Communication | Send emails via SMTP |
| `builtin.script` | Advanced | Execute scripts in multiple languages (C#, JavaScript, Lua, Python) |

---

## 📜 Scripting Support

### Overview

The workflow engine supports embedded scripting in multiple languages! This allows for quick prototyping and custom logic without creating full modules~ ✨

**Supported Languages:**
- 🟨 **JavaScript** - Via Jint (pure .NET implementation)
- 🌙 **Lua** - Via NLua or MoonSharp
- 🐍 **Python** - Via IronPython or Python.NET

### Script Module

```csharp
/// <summary>
/// 📜 Execute custom scripts in various languages!
/// Perfect for quick transformations and custom logic, uwu~
/// </summary>
[WorkflowModule("builtin.script")]
public class ScriptModule : IWorkflowModule
{
    public string ModuleId => "builtin.script";
    public string DisplayName => "Script";
    public string Category => "Advanced";
    public string Description => "Execute custom scripts in JavaScript, Lua, or Python";
    public string Icon => "📜";
    
    public ModuleSchema Schema => new()
    {
        Inputs =
        [
            new() { Name = "input", DisplayName = "Input Data", DataType = typeof(object), IsRequired = false }
        ],
        Outputs =
        [
            new() { Name = "output", DisplayName = "Output Data", DataType = typeof(object) },
            new() { Name = "logs", DisplayName = "Script Logs", DataType = typeof(List<string>) }
        ],
        Properties =
        [
            new() { Name = "language", DisplayName = "Language", DataType = typeof(string), 
                    EditorType = PropertyEditorType.Dropdown, 
                    AllowedValues = ["JavaScript", "Lua", "Python"], 
                    DefaultValue = "JavaScript" },
            new() { Name = "script", DisplayName = "Script Code", DataType = typeof(string), 
                    IsRequired = true, EditorType = PropertyEditorType.Code },
            new() { Name = "timeout", DisplayName = "Timeout (seconds)", DataType = typeof(int), DefaultValue = 30 }
        ]
    };
    
    // ExecuteAsync implementation with language-specific execution...
}
```

### Scripting API

All scripting languages have access to a unified API for interacting with the workflow engine! 🎯

📄 **Full Example:** [IWorkflowScriptApi.cs](examples/scripting/IWorkflowScriptApi.cs)

The API provides:
- **Variable Management** - Get/Set/Check workflow variables
- **Logging** - Info, Warning, Error, Debug logging
- **HTTP Operations** - Make HTTP requests
- **Data Operations** - JSON/CSV parsing and serialization
- **Database Operations** - Query and execute SQL
- **File Operations** - Read/write files, check existence
- **Utility Functions** - GUID generation, datetime formatting, encoding, hashing
- **Workflow Control** - Trigger other workflows

---

### JavaScript Examples

📄 **Data Transformation:** [javascript-data-transformation.js](examples/scripting/javascript-data-transformation.js)

📄 **API Integration:** [javascript-api-integration.js](examples/scripting/javascript-api-integration.js)

---

### Lua Examples

📄 **Simple Data Processing:** [lua-data-processing.lua](examples/scripting/lua-data-processing.lua)

📄 **CSV Processing:** [lua-csv-processing.lua](examples/scripting/lua-csv-processing.lua)

---

### Python Examples

📄 **Data Analysis:** [python-data-analysis.py](examples/scripting/python-data-analysis.py)

📄 **Database ETL:** [python-database-etl.py](examples/scripting/python-database-etl.py)

---

### Script Execution Environment

📄 **Configuration:** [ScriptExecutionConfig.cs](examples/scripting/ScriptExecutionConfig.cs)

Configures security and performance settings for script execution including:
- Timeout limits
- Memory allocation limits
- Network/filesystem/database access permissions
- Allowed file system paths
- HTTP request limits

### Language-Specific Implementations

📄 **Executors:** [ScriptExecutors.cs](examples/scripting/ScriptExecutors.cs)

Contains implementations for:
- `JavaScriptExecutor` - Using Jint
- `LuaExecutor` - Using MoonSharp
- `PythonExecutor` - Using IronPython or Python.NET

### Script Library System

📄 **Library Interface:** [IScriptLibrary.cs](examples/scripting/IScriptLibrary.cs)

Allows creating shared functions across workflows! Register reusable script libraries that can be imported by workflow scripts.

---

## 📝 Workflow Definition

### Workflow Model

📄 **Model Classes:** [WorkflowDefinition.cs](examples/definitions/WorkflowDefinition.cs)

Includes:
- `WorkflowDefinition` - Complete workflow definition
- `NodeDefinition` - Single node in the workflow
- `ConnectionDefinition` - Connection between two nodes

### Example Workflow JSON

📄 **Sample Workflow:** [example-workflow.json](examples/definitions/example-workflow.json)

Shows a complete workflow that fetches data from an API, transforms it, and saves to a database.

---

## 🌐 External API Specification

### 📦 Code Examples Reference

All API code examples have been extracted into separate files for easier maintenance and reference:

- **Controllers:**
  - 📄 [WorkflowsController.cs](examples/api/WorkflowsController.cs) - Workflow management and execution endpoints
  - 📄 [ModulesController.cs](examples/api/ModulesController.cs) - Module management endpoints
  - 📄 [ScriptTestingController.cs](examples/api/ScriptTestingController.cs) - Script testing endpoint

- **Models:**
  - 📄 [ApiModels.cs](examples/api/ApiModels.cs) - Request/response models and enums

- **Real-time:**
  - 📄 [WorkflowHub.cs](examples/api/WorkflowHub.cs) - SignalR hub for real-time monitoring

- **Client SDKs:**
  - 📄 [WorkflowClient.cs](examples/clients/WorkflowClient.cs) - C# client SDK
  - 📄 [WorkflowClient.ts](examples/clients/WorkflowClient.ts) - TypeScript/JavaScript client SDK
  - 📄 [WorkflowClient.py](examples/clients/WorkflowClient.py) - Python client SDK

> 💡 **Tip:** See [examples/README.md](examples/README.md) for a complete overview of all code examples!

### API Gateway Architecture

The workflow engine exposes comprehensive REST and gRPC APIs for external integration! Perfect for calling workflows from other systems~ 🎯

```csharp
/// <summary>
/// 🌸 Main API endpoints for workflow management and execution
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public class WorkflowsController : ControllerBase
{
    // === Workflow Management === 📋
    
    /// <summary>
    /// Get all workflows 📚
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<WorkflowSummary>), 200)]
    public async Task<IActionResult> GetWorkflows([FromQuery] WorkflowFilter? filter = null);
    
    /// <summary>
    /// Get a specific workflow by ID 🔍
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(WorkflowDefinition), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetWorkflow(Guid id);
    
    /// <summary>
    /// Create a new workflow ✨
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(WorkflowDefinition), 201)]
    [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
    public async Task<IActionResult> CreateWorkflow([FromBody] CreateWorkflowRequest request);
    
    /// <summary>
    /// Update an existing workflow ✏️
    /// </summary>
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(WorkflowDefinition), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> UpdateWorkflow(Guid id, [FromBody] UpdateWorkflowRequest request);
    
    /// <summary>
    /// Delete a workflow 🗑️
    /// </summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> DeleteWorkflow(Guid id);
    
    // === Workflow Execution === ▶️
    
    /// <summary>
    /// Execute a workflow 🚀
    /// </summary>
    [HttpPost("{id}/execute")]
    [ProducesResponseType(typeof(ExecutionResponse), 202)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> ExecuteWorkflow(
        Guid id, 
        [FromBody] ExecuteWorkflowRequest request);
    
    /// <summary>
    /// Execute a workflow by name 🎯
    /// </summary>
    [HttpPost("execute/{name}")]
    [ProducesResponseType(typeof(ExecutionResponse), 202)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> ExecuteWorkflowByName(
        string name,
        [FromBody] ExecuteWorkflowRequest request);
    
    /// <summary>
    /// Get execution status 📊
    /// </summary>
    [HttpGet("executions/{executionId}")]
    [ProducesResponseType(typeof(ExecutionStatus), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetExecutionStatus(Guid executionId);
    
    /// <summary>
    /// Cancel a running execution ⏹️
    /// </summary>
    [HttpPost("executions/{executionId}/cancel")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> CancelExecution(Guid executionId);
    
    /// <summary>
    /// Pause a running execution ⏸️
    /// </summary>
    [HttpPost("executions/{executionId}/pause")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> PauseExecution(Guid executionId);
    
    /// <summary>
    /// Resume a paused execution ▶️
    /// </summary>
    [HttpPost("executions/{executionId}/resume")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> ResumeExecution(Guid executionId);
    
    /// <summary>
    /// Get execution history 📜
    /// </summary>
    [HttpGet("{id}/executions")]
    [ProducesResponseType(typeof(PagedResult<ExecutionSummary>), 200)]
    public async Task<IActionResult> GetExecutionHistory(
        Guid id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50);
}

/// <summary>
/// 📦 Module management endpoints
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public class ModulesController : ControllerBase
{
    /// <summary>
    /// Get all available modules 📋
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<ModuleInfo>), 200)]
    public async Task<IActionResult> GetModules([FromQuery] string? category = null);
    
    /// <summary>
    /// Get module details 🔍
    /// </summary>
    [HttpGet("{moduleId}")]
    [ProducesResponseType(typeof(ModuleDetails), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetModule(string moduleId);
    
    /// <summary>
    /// Upload a custom module package 📤
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ModuleUploadResponse), 201)]
    [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
    public async Task<IActionResult> UploadModule([FromForm] IFormFile package);
    
    /// <summary>
    /// Delete a custom module 🗑️
    /// </summary>
    [HttpDelete("{packageId}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> DeleteModule(string packageId);
}

/// <summary>
/// 🔧 Variables and secrets management
/// </summary>
[ApiController]
[Route("api/v1/workflows/{workflowId}/[controller]")]
public class VariablesController : ControllerBase
{
    /// <summary>
    /// Get all workflow variables 📋
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(Dictionary<string, VariableValue>), 200)]
    public async Task<IActionResult> GetVariables(Guid workflowId);
    
    /// <summary>
    /// Set a workflow variable 💾
    /// </summary>
    [HttpPut("{name}")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> SetVariable(
        Guid workflowId,
        string name,
        [FromBody] SetVariableRequest request);
    
    /// <summary>
    /// Delete a workflow variable 🗑️
    /// </summary>
    [HttpDelete("{name}")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> DeleteVariable(Guid workflowId, string name);
}

/// <summary>
/// 📊 Monitoring and metrics endpoints
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public class MonitoringController : ControllerBase
{
    /// <summary>
    /// Get system health status 💚
    /// </summary>
    [HttpGet("health")]
    [ProducesResponseType(typeof(HealthStatus), 200)]
    public async Task<IActionResult> GetHealth();
    
    /// <summary>
    /// Get workflow engine metrics 📈
    /// </summary>
    [HttpGet("metrics")]
    [ProducesResponseType(typeof(EngineMetrics), 200)]
    public async Task<IActionResult> GetMetrics();
    
    /// <summary>
    /// Get active executions 🔄
    /// </summary>
    [HttpGet("active-executions")]
    [ProducesResponseType(typeof(List<ExecutionSummary>), 200)]
    public async Task<IActionResult> GetActiveExecutions();
}
```

### API Request/Response Models

```csharp
/// <summary>
/// 🚀 Request to execute a workflow
/// </summary>
public record ExecuteWorkflowRequest
{
    /// <summary>
    /// Input data for the workflow 📥
    /// </summary>
    public Dictionary<string, object?>? Inputs { get; init; }
    
    /// <summary>
    /// Override workflow variables for this execution 🔧
    /// </summary>
    public Dictionary<string, object?>? Variables { get; init; }
    
    /// <summary>
    /// Wait for execution to complete (synchronous mode) ⏳
    /// </summary>
    public bool WaitForCompletion { get; init; } = false;
    
    /// <summary>
    /// Timeout for synchronous execution (seconds) ⏱️
    /// </summary>
    public int? TimeoutSeconds { get; init; }
    
    /// <summary>
    /// Correlation ID for tracking 🔍
    /// </summary>
    public string? CorrelationId { get; init; }
}

/// <summary>
/// 📬 Response from workflow execution
/// </summary>
public record ExecutionResponse
{
    public Guid ExecutionId { get; init; }
    public ExecutionStatus Status { get; init; } = ExecutionStatus.Queued;
    public Dictionary<string, object?>? Outputs { get; init; }
    public DateTime QueuedAt { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// 📊 Detailed execution status
/// </summary>
public record ExecutionStatusDetail
{
    public Guid ExecutionId { get; init; }
    public Guid WorkflowId { get; init; }
    public string WorkflowName { get; init; } = string.Empty;
    public ExecutionStatus Status { get; init; }
    public DateTime QueuedAt { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public TimeSpan? Duration { get; init; }
    public Dictionary<string, object?>? Inputs { get; init; }
    public Dictionary<string, object?>? Outputs { get; init; }
    public List<NodeExecutionStatus> NodeStatuses { get; init; } = [];
    public string? Error { get; init; }
    public string? CorrelationId { get; init; }
}

/// <summary>
/// 🧩 Individual node execution status
/// </summary>
public record NodeExecutionStatus
{
    public string NodeId { get; init; } = string.Empty;
    public string NodeName { get; init; } = string.Empty;
    public NodeStatus Status { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public Dictionary<string, object?>? Outputs { get; init; }
    public string? Error { get; init; }
}

public enum ExecutionStatus
{
    Queued,
    Running,
    Paused,
    Completed,
    Failed,
    Cancelled
}

public enum NodeStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Skipped
}
```

### SignalR Real-Time Hub

```csharp
/// <summary>
/// 📡 Real-time updates hub for workflow execution monitoring
/// </summary>
/// <remarks>
/// CopilotNote: Use SignalR for live updates to connected clients!
/// Perfect for UI real-time monitoring~ ✨
/// </remarks>
public class WorkflowHub : Hub
{
    /// <summary>
    /// Subscribe to workflow execution updates 📻
    /// </summary>
    public async Task SubscribeToExecution(Guid executionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"execution-{executionId}");
    }
    
    /// <summary>
    /// Unsubscribe from execution updates 🔇
    /// </summary>
    public async Task UnsubscribeFromExecution(Guid executionId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"execution-{executionId}");
    }
    
    /// <summary>
    /// Subscribe to all workflow executions 📻
    /// </summary>
    public async Task SubscribeToAllExecutions()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "all-executions");
    }
}

// Events sent to clients:
// - ExecutionStarted(ExecutionStartedEvent)
// - ExecutionCompleted(ExecutionCompletedEvent)
// - ExecutionFailed(ExecutionFailedEvent)
// - NodeStarted(NodeStartedEvent)
// - NodeCompleted(NodeCompletedEvent)
// - NodeFailed(NodeFailedEvent)
// - ExecutionProgress(ExecutionProgressEvent)
```

### Webhook Integration

```csharp
/// <summary>
/// 🪝 Webhook trigger for workflows
/// </summary>
[ApiController]
[Route("api/v1/webhooks")]
public class WebhooksController : ControllerBase
{
    /// <summary>
    /// Trigger workflow via webhook 🎣
    /// </summary>
    [HttpPost("{webhookId}")]
    [HttpGet("{webhookId}")]
    [HttpPut("{webhookId}")]
    [HttpDelete("{webhookId}")]
    [ProducesResponseType(typeof(WebhookResponse), 200)]
    public async Task<IActionResult> HandleWebhook(
        string webhookId,
        [FromBody] object? body = null)
    {
        // Webhook logic...
    }
}

/// <summary>
/// 🪝 Webhook configuration
/// </summary>
public record WebhookDefinition
{
    public required string WebhookId { get; init; }
    public required Guid WorkflowId { get; init; }
    public string? Secret { get; init; }
    public List<string> AllowedMethods { get; init; } = ["POST"];
    public Dictionary<string, string>? HeaderValidation { get; init; }
    public bool ValidateSignature { get; init; } = false;
}
```

### Client SDK Examples

**C# Client SDK:**

```csharp
/// <summary>
/// 💎 Official C# client SDK for the workflow engine
/// </summary>
public class WorkflowClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    
    public WorkflowClient(string baseUrl, string? apiKey = null)
    {
        _baseUrl = baseUrl;
        _httpClient = new HttpClient();
        if (apiKey != null)
            _httpClient.DefaultRequestHeaders.Add("X-API-Key", apiKey);
    }
    
    /// <summary>
    /// Execute a workflow and get the execution ID ✨
    /// </summary>
    public async Task<ExecutionResponse> ExecuteWorkflowAsync(
        string workflowName,
        Dictionary<string, object?>? inputs = null,
        CancellationToken cancellationToken = default)
    {
        var request = new ExecuteWorkflowRequest { Inputs = inputs };
        var response = await _httpClient.PostAsJsonAsync(
            $"{_baseUrl}/api/v1/workflows/execute/{workflowName}",
            request,
            cancellationToken);
        
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ExecutionResponse>(cancellationToken: cancellationToken);
    }
    
    /// <summary>
    /// Execute and wait for completion (synchronous) ⏳
    /// </summary>
    public async Task<ExecutionResponse> ExecuteWorkflowSyncAsync(
        string workflowName,
        Dictionary<string, object?>? inputs = null,
        int timeoutSeconds = 300,
        CancellationToken cancellationToken = default)
    {
        var request = new ExecuteWorkflowRequest 
        { 
            Inputs = inputs,
            WaitForCompletion = true,
            TimeoutSeconds = timeoutSeconds
        };
        
        var response = await _httpClient.PostAsJsonAsync(
            $"{_baseUrl}/api/v1/workflows/execute/{workflowName}",
            request,
            cancellationToken);
        
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ExecutionResponse>(cancellationToken: cancellationToken);
    }
    
    /// <summary>
    /// Get execution status 📊
    /// </summary>
    public async Task<ExecutionStatusDetail> GetExecutionStatusAsync(
        Guid executionId,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync(
            $"{_baseUrl}/api/v1/workflows/executions/{executionId}",
            cancellationToken);
        
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ExecutionStatusDetail>(cancellationToken: cancellationToken);
    }
    
    /// <summary>
    /// Wait for execution to complete 🎯
    /// </summary>
    public async Task<ExecutionStatusDetail> WaitForCompletionAsync(
        Guid executionId,
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default)
    {
        pollInterval ??= TimeSpan.FromSeconds(2);
        
        while (!cancellationToken.IsCancellationRequested)
        {
            var status = await GetExecutionStatusAsync(executionId, cancellationToken);
            
            if (status.Status is ExecutionStatus.Completed or ExecutionStatus.Failed or ExecutionStatus.Cancelled)
                return status;
            
            await Task.Delay(pollInterval.Value, cancellationToken);
        }
        
        throw new OperationCanceledException();
    }
}

// Usage example:
var client = new WorkflowClient("https://workflow-engine.example.com", apiKey: "your-api-key");

// Async execution
var execution = await client.ExecuteWorkflowAsync("data-sync", new Dictionary<string, object?>
{
    ["source"] = "api",
    ["destination"] = "database"
});

Console.WriteLine($"Execution started: {execution.ExecutionId}");

// Wait for completion
var result = await client.WaitForCompletionAsync(execution.ExecutionId);
Console.WriteLine($"Status: {result.Status}, Duration: {result.Duration}");
```

**JavaScript/TypeScript Client:**

```typescript
// 🟨 JavaScript/TypeScript client SDK

class WorkflowClient {
    constructor(
        private baseUrl: string,
        private apiKey?: string
    ) {}
    
    /**
     * Execute a workflow ✨
     */
    async executeWorkflow(
        workflowName: string,
        inputs?: Record<string, any>
    ): Promise<ExecutionResponse> {
        const response = await fetch(
            `${this.baseUrl}/api/v1/workflows/execute/${workflowName}`,
            {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    ...(this.apiKey && { 'X-API-Key': this.apiKey })
                },
                body: JSON.stringify({ inputs })
            }
        );
        
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        return response.json();
    }
    
    /**
     * Execute and wait for completion ⏳
     */
    async executeWorkflowSync(
        workflowName: string,
        inputs?: Record<string, any>,
        timeoutSeconds: number = 300
    ): Promise<ExecutionResponse> {
        const response = await fetch(
            `${this.baseUrl}/api/v1/workflows/execute/${workflowName}`,
            {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    ...(this.apiKey && { 'X-API-Key': this.apiKey })
                },
                body: JSON.stringify({
                    inputs,
                    waitForCompletion: true,
                    timeoutSeconds
                })
            }
        );
        
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        return response.json();
    }
    
    /**
     * Get execution status 📊
     */
    async getExecutionStatus(executionId: string): Promise<ExecutionStatusDetail> {
        const response = await fetch(
            `${this.baseUrl}/api/v1/workflows/executions/${executionId}`,
            {
                headers: {
                    ...(this.apiKey && { 'X-API-Key': this.apiKey })
                }
            }
        );
        
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        return response.json();
    }
    
    /**
     * Connect to real-time updates 📡
     */
    connectToRealtime(): WorkflowHubConnection {
        return new WorkflowHubConnection(this.baseUrl, this.apiKey);
    }
}

// SignalR connection for real-time updates
class WorkflowHubConnection {
    private connection: signalR.HubConnection;
    
    constructor(baseUrl: string, apiKey?: string) {
        this.connection = new signalR.HubConnectionBuilder()
            .withUrl(`${baseUrl}/hubs/workflow`, {
                accessTokenFactory: () => apiKey || ''
            })
            .withAutomaticReconnect()
            .build();
    }
    
    async start() {
        await this.connection.start();
    }
    
    async subscribeToExecution(executionId: string, callbacks: {
        onNodeStarted?: (event: NodeStartedEvent) => void;
        onNodeCompleted?: (event: NodeCompletedEvent) => void;
        onExecutionCompleted?: (event: ExecutionCompletedEvent) => void;
    }) {
        await this.connection.invoke('SubscribeToExecution', executionId);
        
        if (callbacks.onNodeStarted)
            this.connection.on('NodeStarted', callbacks.onNodeStarted);
        if (callbacks.onNodeCompleted)
            this.connection.on('NodeCompleted', callbacks.onNodeCompleted);
        if (callbacks.onExecutionCompleted)
            this.connection.on('ExecutionCompleted', callbacks.onExecutionCompleted);
    }
}

// Usage:
const client = new WorkflowClient('https://workflow-engine.example.com', 'your-api-key');

// Execute workflow
const execution = await client.executeWorkflow('data-processing', {
    source: 'api',
    format: 'json'
});

console.log(`Started execution: ${execution.executionId}`);

// Real-time monitoring
const hub = client.connectToRealtime();
await hub.start();
await hub.subscribeToExecution(execution.executionId, {
    onNodeStarted: (event) => console.log(`Node started: ${event.nodeName}`),
    onNodeCompleted: (event) => console.log(`Node completed: ${event.nodeName}`),
    onExecutionCompleted: (event) => console.log(`Execution completed! Status: ${event.status}`)
});
```

**Python Client:**

```python
# 🐍 Python client SDK

import requests
from typing import Dict, Any, Optional
import time

class WorkflowClient:
    def __init__(self, base_url: str, api_key: Optional[str] = None):
        self.base_url = base_url
        self.api_key = api_key
        self.session = requests.Session()
        if api_key:
            self.session.headers['X-API-Key'] = api_key
    
    def execute_workflow(
        self, 
        workflow_name: str, 
        inputs: Optional[Dict[str, Any]] = None
    ) -> Dict[str, Any]:
        """Execute a workflow ✨"""
        response = self.session.post(
            f"{self.base_url}/api/v1/workflows/execute/{workflow_name}",
            json={"inputs": inputs or {}}
        )
        response.raise_for_status()
        return response.json()
    
    def execute_workflow_sync(
        self,
        workflow_name: str,
        inputs: Optional[Dict[str, Any]] = None,
        timeout_seconds: int = 300
    ) -> Dict[str, Any]:
        """Execute and wait for completion ⏳"""
        response = self.session.post(
            f"{self.base_url}/api/v1/workflows/execute/{workflow_name}",
            json={
                "inputs": inputs or {},
                "waitForCompletion": True,
                "timeoutSeconds": timeout_seconds
            }
        )
        response.raise_for_status()
        return response.json()
    
    def get_execution_status(self, execution_id: str) -> Dict[str, Any]:
        """Get execution status 📊"""
        response = self.session.get(
            f"{self.base_url}/api/v1/workflows/executions/{execution_id}"
        )
        response.raise_for_status()
        return response.json()
    
    def wait_for_completion(
        self,
        execution_id: str,
        poll_interval: float = 2.0,
        timeout: Optional[float] = None
    ) -> Dict[str, Any]:
        """Wait for execution to complete 🎯"""
        start_time = time.time()
        
        while True:
            status = self.get_execution_status(execution_id)
            
            if status['status'] in ['Completed', 'Failed', 'Cancelled']:
                return status
            
            if timeout and (time.time() - start_time) > timeout:
                raise TimeoutError(f"Execution timed out after {timeout} seconds")
            
            time.sleep(poll_interval)

# Usage:
client = WorkflowClient('https://workflow-engine.example.com', api_key='your-api-key')

# Execute workflow
execution = client.execute_workflow('data-sync', {
    'source': 'api',
    'destination': 'database'
})

print(f"Execution started: {execution['executionId']}")

# Wait for completion
result = client.wait_for_completion(execution['executionId'])
print(f"Status: {result['status']}, Duration: {result['duration']}")
```

---

## 🎨 UI Requirements

> 💡 **Note:** This section contains implementation guidance and UI mockups. For backend code examples (actors, modules, API), see the [examples/](examples/) directory!

### Visual Designer Features

```
┌─────────────────────────────────────────────────────────────────────────┐
│  📁 File   ✏️ Edit   ▶️ Run   🔧 Settings   ❓ Help                      │
├─────────────────────────────────────────────────────────────────────────┤
│ ┌──────────────┐ ┌────────────────────────────────────────┐ ┌─────────┐ │
│ │  📦 Modules  │ │         🎨 Canvas (Drag & Drop)        │ │⚙️Props  │ │
│ │              │ │                                        │ │         │ │
│ │ 🔍 Search... │ │     ┌─────────┐      ┌─────────┐       │ │ Node:   │ │
│ │              │ │     │ 🌐 HTTP │ ───► │ 🗄️ DB   │       │ │ HTTP    │ │
│ │ ▼ Network    │ │     │ Request │      │ Insert  │       │ │         │ │
│ │   🌐 HTTP    │ │     └─────────┘      └─────────┘       │ │ URL:    │ │
│ │              │ │                                        │ │ [____]  │ │
│ │ ▼ Data       │ │                                        │ │         │ │
│ │   🗄️ Database│ │                                        │ │ Method: │ │
│ │   📊 Transform│ │                                        │ │ [GET▼] │ │
│ │              │ │                                        │ │         │ │
│ │ ▼ I/O        │ │                                        │ │         │ │
│ │   📁 File    │ │                                        │ │         │ │
│ │              │ │                                        │ │         │ │
│ │ ▼ Flow       │ │                                        │ │         │ │
│ │   🔀 Condition│ │                                        │ │         │ │
│ │   🔄 Loop    │ │                                        │ │         │ │
│ └──────────────┘ └────────────────────────────────────────┘ └─────────┘ │
├─────────────────────────────────────────────────────────────────────────┤
│  📋 Output  │  🐛 Debug  │  📊 Variables  │  📜 History                  │
│  ✅ Workflow saved successfully!                                        │
└─────────────────────────────────────────────────────────────────────────┘
```

### Key UI Components

1. **Module Palette** 📦
   - Searchable list of available modules
   - Categorized organization
   - Drag-and-drop to canvas

2. **Visual Canvas** 🎨
   - Node-based visual editor
   - Connection drawing between ports
   - Zoom and pan support
   - Multi-select and bulk operations
   - Copy/paste support

3. **Properties Panel** ⚙️
   - Dynamic property editors based on module schema
   - Expression builder for dynamic values
   - Variable reference picker

4. **Execution Monitor** 📊
   - Real-time execution status via SignalR
   - Node-by-node progress
   - Input/output inspection
   - Error highlighting

5. **Module Manager** 📦
   - Upload custom module packages
   - Enable/disable modules
   - Version management

6. **Script Editor** 📜
   - Monaco Editor integration (VSCode editor component)
   - Language-specific syntax highlighting
   - IntelliSense/autocomplete for workflow API
   - Script template library
   - Live syntax validation
   - Script testing/debugging capabilities

### Script Editor Component

```typescript
/// <summary>
/// 📝 Monaco-based script editor for JavaScript, Lua, and Python
/// UwU~ This gives users a professional coding experience! ✨
/// </summary>

interface ScriptEditorProps {
    language: 'javascript' | 'lua' | 'python';
    code: string;
    onChange: (code: string) => void;
    readOnly?: boolean;
    theme?: 'vs-light' | 'vs-dark';
}

// Monaco editor configuration with workflow API intellisense
const configureMonacoEditor = (monaco: typeof import('monaco-editor')) => {
    // JavaScript/TypeScript API definitions
    monaco.languages.typescript.javascriptDefaults.addExtraLib(`
        declare namespace api {
            // Variable Management
            function GetVariable(name: string): any;
            function SetVariable(name: string, value: any): void;
            function HasVariable(name: string): boolean;
            
            // Logging
            function LogInfo(message: string): void;
            function LogWarning(message: string): void;
            function LogError(message: string): void;
            function LogDebug(message: string): void;
            
            // HTTP Operations
            function HttpGetAsync(url: string, headers?: Record<string, string>): Promise<HttpScriptResponse>;
            function HttpPostAsync(url: string, body?: any, headers?: Record<string, string>): Promise<HttpScriptResponse>;
            function HttpRequestAsync(request: HttpScriptRequest): Promise<HttpScriptResponse>;
            
            // Data Operations
            function ParseJson(json: string): any;
            function ToJson(obj: any, pretty?: boolean): string;
            function ParseCsv(csv: string, hasHeaders?: boolean): Array<Record<string, any>>;
            function ToCsv(data: Array<Record<string, any>>, includeHeaders?: boolean): string;
            
            // Database Operations
            function QueryDatabaseAsync(connectionString: string, query: string, parameters?: Record<string, any>): Promise<Array<Record<string, any>>>;
            function ExecuteDatabaseAsync(connectionString: string, command: string, parameters?: Record<string, any>): Promise<number>;
            
            // File Operations
            function ReadFileAsync(path: string): Promise<string>;
            function WriteFileAsync(path: string, content: string): Promise<void>;
            function FileExists(path: string): boolean;
            function ListFiles(path: string, pattern?: string): string[];
            
            // Utility Functions
            function DelayAsync(milliseconds: number): Promise<void>;
            function NewGuid(): string;
            function Now(): Date;
            function FormatDateTime(dateTime: Date, format: string): string;
            function Base64Encode(text: string): string;
            function Base64Decode(base64: string): string;
            function Hash(text: string, algorithm?: string): string;
            
            // Workflow Control
            function TriggerWorkflowAsync(workflowName: string, inputs?: Record<string, any>): Promise<string>;
            function GetContext(): ScriptExecutionContext;
        }
        
        interface HttpScriptRequest {
            Url: string;
            Method?: string;
            Headers?: Record<string, string>;
            Body?: any;
            TimeoutSeconds?: number;
        }
        
        interface HttpScriptResponse {
            StatusCode: number;
            Body: string;
            Headers: Record<string, string>;
            IsSuccess: boolean;
        }
        
        interface ScriptExecutionContext {
            ExecutionId: string;
            NodeId: string;
            WorkflowName: string;
            StartTime: Date;
            Inputs: Record<string, any>;
        }
        
        declare const $input: any;
        declare const workflow: {
            api: typeof api;
            input: any;
        };
    `, 'workflow-api.d.ts');
    
    // Configure Python language support
    monaco.languages.registerCompletionItemProvider('python', {
        provideCompletionItems: () => ({
            suggestions: [
                {
                    label: 'api.LogInfo',
                    kind: monaco.languages.CompletionItemKind.Method,
                    insertText: 'api.LogInfo("${1:message}")',
                    insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
                    documentation: 'Log an informational message'
                },
                {
                    label: 'api.GetVariable',
                    kind: monaco.languages.CompletionItemKind.Method,
                    insertText: 'api.GetVariable("${1:name}")',
                    insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
                    documentation: 'Get a workflow variable value'
                },
                {
                    label: 'api.SetVariable',
                    kind: monaco.languages.CompletionItemKind.Method,
                    insertText: 'api.SetVariable("${1:name}", ${2:value})',
                    insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
                    documentation: 'Set a workflow variable value'
                },
                {
                    label: 'api.HttpGetAsync',
                    kind: monaco.languages.CompletionItemKind.Method,
                    insertText: 'await api.HttpGetAsync("${1:url}")',
                    insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
                    documentation: 'Make an HTTP GET request'
                },
                // Add more Python completions...
            ]
        })
    });
    
    // Configure Lua language support
    monaco.languages.registerCompletionItemProvider('lua', {
        provideCompletionItems: () => ({
            suggestions: [
                {
                    label: 'api:LogInfo',
                    kind: monaco.languages.CompletionItemKind.Method,
                    insertText: 'api:LogInfo("${1:message}")',
                    insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
                    documentation: 'Log an informational message'
                },
                {
                    label: 'api:GetVariable',
                    kind: monaco.languages.CompletionItemKind.Method,
                    insertText: 'api:GetVariable("${1:name}")',
                    insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
                    documentation: 'Get a workflow variable value'
                },
                // Add more Lua completions...
            ]
        })
    });
};

// Script template library
const ScriptTemplates = {
    javascript: {
        basic: `// 🟨 Basic JavaScript template
const input = $input;

api.LogInfo("Starting script execution! ✨");

// Your code here

return { output: input };`,
        
        httpRequest: `// 🌐 HTTP Request template
const url = "https://api.example.com/data";

api.LogInfo(\`Fetching data from \${url}\`);

const response = await api.HttpGetAsync(url);

if (response.IsSuccess) {
    const data = api.ParseJson(response.Body);
    api.LogInfo(\`Retrieved \${data.length} items\`);
    return { output: data };
} else {
    api.LogError(\`Request failed: \${response.StatusCode}\`);
    throw new Error(\`HTTP \${response.StatusCode}\`);
}`,
        
        dataTransform: `// 📊 Data transformation template
const input = $input;

api.LogInfo(\`Processing \${input.length} items\`);

const transformed = input.map(item => ({
    id: item.id,
    name: item.name.toUpperCase(),
    processed: true,
    timestamp: api.Now()
}));

api.SetVariable("processedCount", transformed.length);

return { output: transformed };`,
        
        databaseQuery: `// 🗄️ Database query template
const connString = api.GetVariable("dbConnection");

const query = \`
    SELECT * FROM users 
    WHERE status = @status
    AND created_date > @date
\`;

const results = await api.QueryDatabaseAsync(connString, query, {
    status: "active",
    date: "2024-01-01"
});

api.LogInfo(\`Found \${results.length} records\`);

return { output: results };`
    },
    
    lua: {
        basic: `-- 🌙 Basic Lua template
local api = workflow.api
local input = workflow.input

api:LogInfo("Starting Lua script! 🌙")

-- Your code here

return { output = input }`,
        
        dataProcessing: `-- 📊 Data processing template
local api = workflow.api
local input = workflow.input

local result = {}
for i, item in ipairs(input) do
    if item.value > 100 then
        table.insert(result, {
            id = item.id,
            value = item.value * 2,
            processed = true
        })
    end
end

api:LogInfo(string.format("Processed %d items", #result))

return { output = result }`
    },
    
    python: {
        basic: `# 🐍 Basic Python template
api = workflow.api
input_data = workflow.input

api.LogInfo("Starting Python script! 🐍")

# Your code here

return {'output': input_data}`,
        
        dataAnalysis: `# 📊 Data analysis template
api = workflow.api
input_data = workflow.input

# Filter data
filtered = [
    item for item in input_data 
    if item['score'] > 75
]

# Calculate statistics
total = len(filtered)
avg_score = sum(item['score'] for item in filtered) / total if total > 0 else 0

api.LogInfo(f"Processed {total} items with average {avg_score:.2f}")

return {
    'output': filtered,
    'stats': {'total': total, 'average': avg_score}
}`,
        
        etl: `# 🗄️ ETL template
api = workflow.api

# Extract
conn_string = api.GetVariable("dbConnection")
source_data = await api.QueryDatabaseAsync(
    conn_string,
    "SELECT * FROM source WHERE status = @status",
    {"status": "pending"}
)

api.LogInfo(f"Extracted {len(source_data)} records")

# Transform
transformed = []
for row in source_data:
    transformed.append({
        'id': row['id'],
        'name': row['name'].upper(),
        'processed_date': api.FormatDateTime(api.Now(), "yyyy-MM-dd")
    })

# Load
for item in transformed:
    await api.ExecuteDatabaseAsync(
        conn_string,
        "INSERT INTO destination (id, name, date) VALUES (@id, @name, @date)",
        item
    )

api.LogInfo(f"Loaded {len(transformed)} records")

return {'output': transformed, 'count': len(transformed)}`
    }
};

// Script Editor Component
const ScriptEditor: React.FC<ScriptEditorProps> = ({
    language,
    code,
    onChange,
    readOnly = false,
    theme = 'vs-dark'
}) => {
    const editorRef = useRef<monaco.editor.IStandaloneCodeEditor | null>(null);
    
    useEffect(() => {
        // Configure Monaco on mount
        if (window.monaco) {
            configureMonacoEditor(window.monaco);
        }
    }, []);
    
    return (
        <div className="script-editor">
            <div className="editor-toolbar">
                <select 
                    value={language}
                    onChange={(e) => onLanguageChange(e.target.value)}
                >
                    <option value="javascript">🟨 JavaScript</option>
                    <option value="lua">🌙 Lua</option>
                    <option value="python">🐍 Python</option>
                </select>
                
                <button onClick={() => insertTemplate('basic')}>
                    📝 Basic Template
                </button>
                <button onClick={() => insertTemplate('httpRequest')}>
                    🌐 HTTP Template
                </button>
                <button onClick={() => insertTemplate('dataTransform')}>
                    📊 Transform Template
                </button>
                
                <button onClick={testScript}>
                    ▶️ Test Script
                </button>
                <button onClick={showApiDocs}>
                    📚 API Docs
                </button>
            </div>
            
            <MonacoEditor
                language={language}
                value={code}
                onChange={onChange}
                theme={theme}
                options={{
                    readOnly,
                    minimap: { enabled: true },
                    fontSize: 14,
                    lineNumbers: 'on',
                    automaticLayout: true,
                    scrollBeyondLastLine: false,
                    suggestOnTriggerCharacters: true,
                    quickSuggestions: true,
                    tabSize: language === 'python' ? 4 : 2
                }}
                editorDidMount={(editor) => {
                    editorRef.current = editor;
                }}
            />
            
            <div className="editor-status">
                <span>Line {currentLine}, Column {currentColumn}</span>
                <span>{language}</span>
            </div>
        </div>
    );
};
```

### Script Testing Interface

```csharp
/// <summary>
/// 🧪 Script testing endpoint for the UI
/// Allows testing scripts before adding them to workflows!
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public class ScriptTestingController : ControllerBase
{
    /// <summary>
    /// Test a script with sample data 🧪
    /// </summary>
    [HttpPost("test")]
    [ProducesResponseType(typeof(ScriptTestResult), 200)]
    [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
    public async Task<IActionResult> TestScript([FromBody] ScriptTestRequest request)
    {
        try
        {
            var executor = GetExecutor(request.Language);
            var api = new TestWorkflowScriptApi(); // Sandboxed API for testing
            
            var startTime = DateTime.UtcNow;
            var result = await executor.ExecuteAsync(
                request.Script,
                request.Inputs ?? new(),
                api,
                new ScriptExecutionConfig
                {
                    Timeout = TimeSpan.FromSeconds(30),
                    AllowNetwork = request.AllowNetwork,
                    AllowFileSystem = false,
                    AllowDatabase = false
                }
            );
            var duration = DateTime.UtcNow - startTime;
            
            return Ok(new ScriptTestResult
            {
                Success = true,
                Output = result,
                Logs = api.GetLogs(),
                Duration = duration,
                Variables = api.GetVariables()
            });
        }
        catch (Exception ex)
        {
            return Ok(new ScriptTestResult
            {
                Success = false,
                Error = ex.Message,
                Logs = new[] { ex.ToString() }
            });
        }
    }
}

/// <summary>
/// 🧪 Script test request
/// </summary>
public record ScriptTestRequest
{
    public required string Language { get; init; }
    public required string Script { get; init; }
    public Dictionary<string, object?>? Inputs { get; init; }
    public bool AllowNetwork { get; init; } = false;
}

/// <summary>
/// 📊 Script test result
/// </summary>
public record ScriptTestResult
{
    public bool Success { get; init; }
    public object? Output { get; init; }
    public string? Error { get; init; }
    public IReadOnlyList<string> Logs { get; init; } = [];
    public TimeSpan Duration { get; init; }
    public Dictionary<string, object?> Variables { get; init; } = new();
}
```

---

## 🔒 Security Considerations

### Module Sandboxing

```csharp
/// <summary>
/// 🛡️ Security configuration for module execution
/// </summary>
public record ModuleSecurityConfig
{
    /// <summary>
    /// Maximum execution time per node ⏱️
    /// </summary>
    public TimeSpan MaxExecutionTime { get; init; } = TimeSpan.FromMinutes(5);
    
    /// <summary>
    /// Maximum memory per execution 💾
    /// </summary>
    public long MaxMemoryBytes { get; init; } = 512 * 1024 * 1024; // 512 MB
    
    /// <summary>
    /// Allowed permissions for the module 🔐
    /// </summary>
    public ModulePermissions Permissions { get; init; } = ModulePermissions.None;
}

[Flags]
public enum ModulePermissions
{
    None = 0,
    Network = 1,
    FileSystem = 2,
    Database = 4,
    ProcessExecution = 8,
    EnvironmentVariables = 16,
    All = Network | FileSystem | Database | ProcessExecution | EnvironmentVariables
}
```

### Security Checklist

- [ ] 🔐 Module assembly validation and signing
- [ ] 🛡️ Sandboxed execution via `AssemblyLoadContext`
- [ ] ⏱️ Execution timeouts per node
- [ ] 💾 Memory limits per execution
- [ ] 🔒 Secrets management (encrypted storage)
- [ ] 📝 Audit logging for all operations
- [ ] 👤 Role-based access control (RBAC)
- [ ] 🌐 Network policy enforcement

---

## 🚀 Extensibility

### Extension Points

1. **Custom Modules** - Implement `IWorkflowModule`
2. **Custom Triggers** - Implement `IWorkflowTrigger`
3. **Custom Property Editors** - Blazor components
4. **Middleware** - Pipeline for pre/post execution
5. **Storage Providers** - Custom workflow persistence

### Plugin Architecture

📄 **Plugin Interface:** [IWorkflowPlugin.cs](examples/modules/IWorkflowPlugin.cs)

Allows extending the workflow engine with custom plugins that can:
- Register custom modules
- Add services to the DI container
- Access configuration
- Initialize and cleanup resources

---

## 📚 Project Structure

```
📁 Workflow/
├── 📁 src/
│   ├── 📁 Workflow.Abstractions/          # 🔌 Core interfaces & models
│   │   ├── IWorkflowModule.cs
│   │   ├── ModuleSchema.cs
│   │   └── ...
│   │
│   ├── 📁 Workflow.Engine/                 # ⚡ Akka.NET based engine
│   │   ├── Actors/
│   │   │   ├── WorkflowCoordinatorActor.cs
│   │   │   ├── WorkflowInstanceActor.cs
│   │   │   └── NodeActor.cs
│   │   ├── ModuleRegistry.cs
│   │   └── ...
│   │
│   ├── 📁 Workflow.Modules.Builtin/        # 📦 Built-in modules
│   │   ├── HttpModule.cs
│   │   ├── DatabaseModule.cs
│   │   ├── FileModule.cs
│   │   └── ...
│   │
│   ├── 📁 Workflow.Api/                    # 🌐 REST/gRPC API
│   │   ├── Controllers/
│   │   ├── Hubs/
│   │   └── ...
│   │
│   ├── 📁 Workflow.UI/                     # 🎨 Blazor UI
│   │   ├── Components/
│   │   │   ├── Designer/
│   │   │   ├── ModulePalette/
│   │   │   └── PropertyEditor/
│   │   └── ...
│   │
│   └── 📁 Workflow.Persistence/            # 💾 Data persistence
│       ├── Repositories/
│       └── ...
│
├── 📁 tests/
│   ├── 📁 Workflow.Engine.Tests/
│   ├── 📁 Workflow.Modules.Tests/
│   └── ...
│
├── 📁 samples/
│   └── 📁 SampleCustomModule/              # 📘 Example custom module
│
└── 📁 docs/
    ├── getting-started.md
    ├── module-development.md
    └── ...
```

---

## 🛤️ Implementation Roadmap

### Phase 1: Foundation 🏗️ (Weeks 1-4)
- [ ] Core abstractions and interfaces
- [ ] Basic Akka.NET actor hierarchy
- [ ] Module loading system
- [ ] Simple workflow execution

### Phase 2: Built-in Modules 📦 (Weeks 5-8)
- [ ] HTTP module
- [ ] Database module
- [ ] File module
- [ ] Flow control modules

Refer to the phase roadmaps for more details:

 - Phase 1 - [Foundation Roadmap](./phases/Phase1-Foundation.md)
 - Phase 2 - [Built-in Modules Roadmap](./phases/Phase2-CoreFeatures.md)
 - Phase 3 - [Advanced Features Roadmap](./phases/Phase3-AdvancedFeatures.md)
 - Phase 4 - [Production Roadmap](./phases/Phase4-Production.md)

## 📊 Progress Tracking Dashboard

### Overall Timeline
```
Duration: 23-28 weeks (~6 months)
Team Size: 3-4 full-time developers

Phase 1: Foundation        [████████░░] 4-6 weeks  (Weeks 1-6)
Phase 2: Core Features     [░░░░░░░░░░] 6-8 weeks  (Weeks 7-14)
Phase 3: Advanced Features [░░░░░░░░░░] 6-8 weeks  (Weeks 15-22)
Phase 4: Polish & Prod     [░░░░░░░░░░] 4-6 weeks  (Weeks 23-28)
```

### Module Development Targets
```
Phase 1:  4 modules   (Log, Delay, Set/GetVariable)
Phase 2: 20+ modules  (HTTP, DB, Files, Transform, etc.)
Phase 3: 25+ modules  (+ Scripting module)
Phase 4: 30+ modules  (+ Advanced modules)
```

### Test Coverage Goals
```
Phase 1: 80%+ coverage
Phase 2: 80%+ coverage
Phase 3: 75%+ coverage (UI brings down average)
Phase 4: 85%+ coverage
```

---

## 🎯 Key Milestones

### ✅ Phase 1 Complete
- [ ] Basic workflow execution working
- [ ] Module system operational
- [ ] 4 basic modules implemented
- [ ] Architecture validated

### ✅ Phase 2 Complete
- [ ] Persistence providers implemented
- [ ] 20+ modules operational
- [ ] REST API complete
- [ ] Complex workflows executable

### ✅ Phase 3 Complete
- [ ] Scripting in 3 languages working
- [ ] Visual designer operational
- [ ] Real-time monitoring working
- [ ] Client SDKs published

### ✅ Phase 4 Complete
- [ ] Performance targets met
- [ ] Security audit passed
- [ ] HA clustering working
- [ ] Documentation complete
- [ ] **PRODUCTION READY! 🎉✨**

---

## 🎊 Launch Readiness Checklist

**Pre-Launch:**
- [ ] All phases complete
- [ ] Beta testing successful
- [ ] Performance benchmarks passed
- [ ] Security audit passed
- [ ] Documentation complete
- [ ] Support infrastructure ready
- [ ] Deployment tested
- [ ] Monitoring operational
- [ ] Backup/DR tested
- [ ] Team trained
- [ ] Go/No-Go decision made

**Launch Day:**
- [ ] Deploy to production
- [ ] Monitor for issues
- [ ] Respond to support requests
- [ ] Collect user feedback
- [ ] **CELEBRATE! 🎉🎀✨**

---

## 🎀 Conclusion

This template provides a solid foundation for building a powerful, extensible workflow engine with Akka.NET! The actor-based architecture ensures:

- ✅ **Fault tolerance** - Actors supervise and restart on failure
- ✅ **Scalability** - Distribute workload across nodes
- ✅ **Isolation** - Each workflow runs independently
- ✅ **Real-time** - Event-driven updates via message passing

Remember, the key to a kawaii workflow engine is keeping things simple, extensible, and fun to use! ✨

---

*Made with 💖 by Ami-Chan for senpai's awesome project! UwU*

