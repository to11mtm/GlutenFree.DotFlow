# 🌸 Workflow Engine Design Template 🌸
## Powered by C# & Akka.NET ✨

> *"Building workflows should be as kawaii as coding them!"* - Ami-Chan 💖

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

```csharp
namespace Workflow.Modules.Abstractions;

/// <summary>
/// 🌸 The base interface all workflow modules must implement!
/// This is the contract for creating new modules, uwu~
/// </summary>
/// <remarks>
/// CopilotNote: Module authors implement this interface to create
/// custom workflow nodes. Keep it simple and stateless!
/// </remarks>
public interface IWorkflowModule
{
    /// <summary>
    /// Unique identifier for this module type ✨
    /// </summary>
    string ModuleId { get; }
    
    /// <summary>
    /// Display name shown in the UI 🎨
    /// </summary>
    string DisplayName { get; }
    
    /// <summary>
    /// Category for organizing in the module palette 📁
    /// </summary>
    string Category { get; }
    
    /// <summary>
    /// Description of what this module does 📝
    /// </summary>
    string Description { get; }
    
    /// <summary>
    /// Icon identifier (emoji or icon name) 🖼️
    /// </summary>
    string Icon { get; }
    
    /// <summary>
    /// Defines the input/output schema for this module 📋
    /// </summary>
    ModuleSchema Schema { get; }
    
    /// <summary>
    /// Execute the module's logic! This is where the magic happens~ ✨
    /// </summary>
    /// <param name="context">Execution context with inputs and services</param>
    /// <param name="cancellationToken">Cancellation token for graceful shutdown</param>
    /// <returns>The execution result with outputs</returns>
    Task<ModuleResult> ExecuteAsync(
        ModuleExecutionContext context, 
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 🎁 Defines the input/output schema for a module
/// </summary>
public record ModuleSchema
{
    /// <summary>
    /// Input port definitions 📥
    /// </summary>
    public IReadOnlyList<PortDefinition> Inputs { get; init; } = [];
    
    /// <summary>
    /// Output port definitions 📤
    /// </summary>
    public IReadOnlyList<PortDefinition> Outputs { get; init; } = [];
    
    /// <summary>
    /// Configuration properties for the module ⚙️
    /// </summary>
    public IReadOnlyList<PropertyDefinition> Properties { get; init; } = [];
}

/// <summary>
/// 🔌 Defines an input or output port
/// </summary>
public record PortDefinition
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required Type DataType { get; init; }
    public string? Description { get; init; }
    public bool IsRequired { get; init; } = true;
    public object? DefaultValue { get; init; }
}

/// <summary>
/// ⚙️ Defines a configurable property
/// </summary>
public record PropertyDefinition
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required Type DataType { get; init; }
    public PropertyEditorType EditorType { get; init; } = PropertyEditorType.Text;
    public string? Description { get; init; }
    public bool IsRequired { get; init; } = true;
    public object? DefaultValue { get; init; }
    public IReadOnlyList<object>? AllowedValues { get; init; }
}

/// <summary>
/// 🖊️ Types of property editors for the UI
/// </summary>
public enum PropertyEditorType
{
    Text,
    MultilineText,
    Number,
    Boolean,
    Dropdown,
    FilePath,
    DirectoryPath,
    ConnectionString,
    Expression,
    Json,
    Code
}

/// <summary>
/// 📦 Context provided to modules during execution
/// </summary>
public record ModuleExecutionContext
{
    /// <summary>
    /// Input values from connected ports 📥
    /// </summary>
    public required IReadOnlyDictionary<string, object?> Inputs { get; init; }
    
    /// <summary>
    /// Configured property values ⚙️
    /// </summary>
    public required IReadOnlyDictionary<string, object?> Properties { get; init; }
    
    /// <summary>
    /// Workflow-level variables 🔧
    /// </summary>
    public required IReadOnlyDictionary<string, object?> Variables { get; init; }
    
    /// <summary>
    /// Logger for this module execution 📝
    /// </summary>
    public required ILogger Logger { get; init; }
    
    /// <summary>
    /// Service provider for dependency injection 💉
    /// </summary>
    public required IServiceProvider Services { get; init; }
    
    /// <summary>
    /// Unique execution ID for tracing 🔍
    /// </summary>
    public required Guid ExecutionId { get; init; }
    
    /// <summary>
    /// Node instance ID within the workflow 🆔
    /// </summary>
    public required string NodeId { get; init; }
}

/// <summary>
/// 🎯 Result of module execution
/// </summary>
public record ModuleResult
{
    public bool Success { get; init; }
    public IReadOnlyDictionary<string, object?> Outputs { get; init; } = new Dictionary<string, object?>();
    public string? ErrorMessage { get; init; }
    public Exception? Exception { get; init; }
    
    public static ModuleResult Ok(Dictionary<string, object?> outputs) 
        => new() { Success = true, Outputs = outputs };
    
    public static ModuleResult Fail(string message, Exception? ex = null) 
        => new() { Success = false, ErrorMessage = message, Exception = ex };
}
```

### Module Discovery & Loading

```csharp
/// <summary>
/// 🔍 Handles discovering and loading modules from uploaded assemblies
/// </summary>
/// <remarks>
/// CopilotNote: Uses AssemblyLoadContext for isolation!
/// Each module package gets its own context for clean unloading.
/// </remarks>
public interface IModuleRegistry
{
    /// <summary>
    /// Get all registered modules 📋
    /// </summary>
    IReadOnlyList<ModuleInfo> GetAllModules();
    
    /// <summary>
    /// Get a specific module by ID 🔍
    /// </summary>
    IWorkflowModule? GetModule(string moduleId);
    
    /// <summary>
    /// Load modules from an assembly package 📦
    /// </summary>
    Task<ModuleLoadResult> LoadModulePackageAsync(
        Stream assemblyStream, 
        ModulePackageMetadata metadata);
    
    /// <summary>
    /// Unload a module package (for updates/removal) 🗑️
    /// </summary>
    Task<bool> UnloadModulePackageAsync(string packageId);
}

/// <summary>
/// 📦 Metadata for a module package
/// </summary>
public record ModulePackageMetadata
{
    public required string PackageId { get; init; }
    public required string Name { get; init; }
    public required Version Version { get; init; }
    public string? Author { get; init; }
    public string? Description { get; init; }
}
```

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

```csharp
/// <summary>
/// 🌐 Makes HTTP requests to external APIs!
/// Supports GET, POST, PUT, PATCH, DELETE with full configuration.
/// </summary>
[WorkflowModule("builtin.http")]
public class HttpModule : IWorkflowModule
{
    public string ModuleId => "builtin.http";
    public string DisplayName => "HTTP Request";
    public string Category => "Network";
    public string Description => "Make HTTP requests to external APIs";
    public string Icon => "🌐";
    
    public ModuleSchema Schema => new()
    {
        Inputs =
        [
            new() { Name = "body", DisplayName = "Request Body", DataType = typeof(object), IsRequired = false }
        ],
        Outputs =
        [
            new() { Name = "response", DisplayName = "Response Body", DataType = typeof(object) },
            new() { Name = "statusCode", DisplayName = "Status Code", DataType = typeof(int) },
            new() { Name = "headers", DisplayName = "Response Headers", DataType = typeof(Dictionary<string, string>) }
        ],
        Properties =
        [
            new() { Name = "url", DisplayName = "URL", DataType = typeof(string), IsRequired = true, EditorType = PropertyEditorType.Text },
            new() { Name = "method", DisplayName = "Method", DataType = typeof(string), EditorType = PropertyEditorType.Dropdown, 
                    AllowedValues = ["GET", "POST", "PUT", "PATCH", "DELETE"], DefaultValue = "GET" },
            new() { Name = "headers", DisplayName = "Headers", DataType = typeof(Dictionary<string, string>), EditorType = PropertyEditorType.Json },
            new() { Name = "timeout", DisplayName = "Timeout (seconds)", DataType = typeof(int), DefaultValue = 30 },
            new() { Name = "authentication", DisplayName = "Authentication", DataType = typeof(AuthConfig), EditorType = PropertyEditorType.Json }
        ]
    };
    
    // ExecuteAsync implementation...
}
```

### 2. Database Module 🗄️

```csharp
/// <summary>
/// 🗄️ Interact with databases using SQL!
/// Supports SQL Server, PostgreSQL, MySQL, SQLite.
/// </summary>
[WorkflowModule("builtin.database")]
public class DatabaseModule : IWorkflowModule
{
    public string ModuleId => "builtin.database";
    public string DisplayName => "Database Query";
    public string Category => "Data";
    public string Description => "Execute SQL queries against databases";
    public string Icon => "🗄️";
    
    public ModuleSchema Schema => new()
    {
        Inputs =
        [
            new() { Name = "parameters", DisplayName = "Query Parameters", DataType = typeof(Dictionary<string, object>), IsRequired = false }
        ],
        Outputs =
        [
            new() { Name = "results", DisplayName = "Query Results", DataType = typeof(List<Dictionary<string, object>>) },
            new() { Name = "rowsAffected", DisplayName = "Rows Affected", DataType = typeof(int) }
        ],
        Properties =
        [
            new() { Name = "connectionString", DisplayName = "Connection String", DataType = typeof(string), 
                    IsRequired = true, EditorType = PropertyEditorType.ConnectionString },
            new() { Name = "provider", DisplayName = "Database Provider", DataType = typeof(string), 
                    EditorType = PropertyEditorType.Dropdown, AllowedValues = ["SqlServer", "PostgreSQL", "MySQL", "SQLite"] },
            new() { Name = "query", DisplayName = "SQL Query", DataType = typeof(string), 
                    IsRequired = true, EditorType = PropertyEditorType.Code },
            new() { Name = "queryType", DisplayName = "Query Type", DataType = typeof(string), 
                    EditorType = PropertyEditorType.Dropdown, AllowedValues = ["Query", "NonQuery", "Scalar"] }
        ]
    };
    
    // ExecuteAsync implementation...
}
```

### 3. File Module 📁

```csharp
/// <summary>
/// 📁 Read and write files from the filesystem!
/// Supports text, JSON, CSV, and binary files.
/// </summary>
[WorkflowModule("builtin.file")]
public class FileModule : IWorkflowModule
{
    public string ModuleId => "builtin.file";
    public string DisplayName => "File Operations";
    public string Category => "I/O";
    public string Description => "Read and write files";
    public string Icon => "📁";
    
    public ModuleSchema Schema => new()
    {
        Inputs =
        [
            new() { Name = "content", DisplayName = "Content to Write", DataType = typeof(object), IsRequired = false }
        ],
        Outputs =
        [
            new() { Name = "content", DisplayName = "File Content", DataType = typeof(object) },
            new() { Name = "exists", DisplayName = "File Exists", DataType = typeof(bool) },
            new() { Name = "metadata", DisplayName = "File Metadata", DataType = typeof(FileMetadata) }
        ],
        Properties =
        [
            new() { Name = "path", DisplayName = "File Path", DataType = typeof(string), 
                    IsRequired = true, EditorType = PropertyEditorType.FilePath },
            new() { Name = "operation", DisplayName = "Operation", DataType = typeof(string), 
                    EditorType = PropertyEditorType.Dropdown, AllowedValues = ["Read", "Write", "Append", "Delete", "Exists", "Copy", "Move"] },
            new() { Name = "format", DisplayName = "Format", DataType = typeof(string), 
                    EditorType = PropertyEditorType.Dropdown, AllowedValues = ["Text", "Json", "Csv", "Binary"], DefaultValue = "Text" },
            new() { Name = "encoding", DisplayName = "Encoding", DataType = typeof(string), DefaultValue = "UTF-8" }
        ]
    };
    
    // ExecuteAsync implementation...
}
```

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
| `builtin.script` | Advanced | Execute C# or JavaScript code |

---

## 📝 Workflow Definition

### Workflow Model

```csharp
/// <summary>
/// 🌸 Represents a complete workflow definition
/// </summary>
public record WorkflowDefinition
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required Version Version { get; init; }
    
    /// <summary>
    /// All nodes in this workflow 🧩
    /// </summary>
    public required IReadOnlyList<NodeDefinition> Nodes { get; init; }
    
    /// <summary>
    /// Connections between nodes 🔗
    /// </summary>
    public required IReadOnlyList<ConnectionDefinition> Connections { get; init; }
    
    /// <summary>
    /// Workflow-level variables 🔧
    /// </summary>
    public IReadOnlyDictionary<string, VariableDefinition> Variables { get; init; } = new Dictionary<string, VariableDefinition>();
    
    /// <summary>
    /// Trigger configuration (how the workflow starts) ⚡
    /// </summary>
    public TriggerDefinition? Trigger { get; init; }
    
    /// <summary>
    /// Error handling configuration 🛡️
    /// </summary>
    public ErrorHandlingConfig ErrorHandling { get; init; } = new();
}

/// <summary>
/// 🧩 A single node in the workflow
/// </summary>
public record NodeDefinition
{
    public required string Id { get; init; }
    public required string ModuleId { get; init; }
    public required string Name { get; init; }
    
    /// <summary>
    /// Configured property values ⚙️
    /// </summary>
    public IReadOnlyDictionary<string, object?> Properties { get; init; } = new Dictionary<string, object?>();
    
    /// <summary>
    /// Position in the visual designer 📍
    /// </summary>
    public Position Position { get; init; } = new(0, 0);
    
    /// <summary>
    /// Node-specific error handling 🛡️
    /// </summary>
    public NodeErrorHandling? ErrorHandling { get; init; }
}

/// <summary>
/// 🔗 Connection between two nodes
/// </summary>
public record ConnectionDefinition
{
    public required string SourceNodeId { get; init; }
    public required string SourcePortName { get; init; }
    public required string TargetNodeId { get; init; }
    public required string TargetPortName { get; init; }
}
```

### Example Workflow JSON

```json
{
  "id": "550e8400-e29b-41d4-a716-446655440000",
  "name": "API Data Sync",
  "description": "Fetches data from API and saves to database",
  "version": "1.0.0",
  "nodes": [
    {
      "id": "node-1",
      "moduleId": "builtin.http",
      "name": "Fetch Users",
      "properties": {
        "url": "https://api.example.com/users",
        "method": "GET"
      },
      "position": { "x": 100, "y": 100 }
    },
    {
      "id": "node-2",
      "moduleId": "builtin.transform",
      "name": "Transform Data",
      "properties": {
        "expression": "$.response.data"
      },
      "position": { "x": 300, "y": 100 }
    },
    {
      "id": "node-3",
      "moduleId": "builtin.database",
      "name": "Save to DB",
      "properties": {
        "connectionString": "{{Variables.DbConnection}}",
        "provider": "PostgreSQL",
        "query": "INSERT INTO users (data) VALUES (@data)",
        "queryType": "NonQuery"
      },
      "position": { "x": 500, "y": 100 }
    }
  ],
  "connections": [
    {
      "sourceNodeId": "node-1",
      "sourcePortName": "response",
      "targetNodeId": "node-2",
      "targetPortName": "input"
    },
    {
      "sourceNodeId": "node-2",
      "sourcePortName": "output",
      "targetNodeId": "node-3",
      "targetPortName": "parameters"
    }
  ],
  "variables": {
    "DbConnection": {
      "type": "string",
      "isSecret": true
    }
  }
}
```

---

## 🎨 UI Requirements

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

```csharp
/// <summary>
/// 🔌 Interface for workflow engine plugins
/// </summary>
public interface IWorkflowPlugin
{
    string PluginId { get; }
    string Name { get; }
    Version Version { get; }
    
    /// <summary>
    /// Called when the plugin is loaded 📦
    /// </summary>
    Task InitializeAsync(IWorkflowPluginContext context);
    
    /// <summary>
    /// Called when the plugin is unloaded 🗑️
    /// </summary>
    Task ShutdownAsync();
}

/// <summary>
/// 🎁 Context provided to plugins
/// </summary>
public interface IWorkflowPluginContext
{
    IModuleRegistry ModuleRegistry { get; }
    IServiceCollection Services { get; }
    IConfiguration Configuration { get; }
}
```

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

### Phase 3: API & Persistence 🌐 (Weeks 9-12)
- [ ] REST API
- [ ] SignalR real-time updates
- [ ] Workflow persistence
- [ ] Execution history

### Phase 4: UI 🎨 (Weeks 13-20)
- [ ] Visual designer canvas
- [ ] Module palette
- [ ] Property editors
- [ ] Execution monitoring

### Phase 5: Advanced Features ✨ (Weeks 21-26)
- [ ] Custom module upload
- [ ] Scheduling & triggers
- [ ] Error handling & retry
- [ ] Security & RBAC

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

