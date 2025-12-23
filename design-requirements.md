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

> 💡 **Tip for AI:** These phase files contain summaries for quick reference. The complete detailed checklists are below in the Implementation Roadmap section of this file!

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

```csharp
/// <summary>
/// 🌸 API exposed to scripts for workflow interaction
/// </summary>
/// <remarks>
/// CopilotNote: This is the bridge between scripts and the workflow engine!
/// Keep methods simple and well-documented for script authors.
/// </remarks>
public interface IWorkflowScriptApi
{
    // === Variable Management === 🔧
    
    /// <summary>
    /// Get a workflow variable value 📥
    /// </summary>
    object? GetVariable(string name);
    
    /// <summary>
    /// Set a workflow variable value 📤
    /// </summary>
    void SetVariable(string name, object? value);
    
    /// <summary>
    /// Check if a variable exists 🔍
    /// </summary>
    bool HasVariable(string name);
    
    // === Logging === 📝
    
    /// <summary>
    /// Log an informational message ℹ️
    /// </summary>
    void LogInfo(string message);
    
    /// <summary>
    /// Log a warning message ⚠️
    /// </summary>
    void LogWarning(string message);
    
    /// <summary>
    /// Log an error message ❌
    /// </summary>
    void LogError(string message);
    
    /// <summary>
    /// Log a debug message 🐛
    /// </summary>
    void LogDebug(string message);
    
    // === HTTP Operations === 🌐
    
    /// <summary>
    /// Make an HTTP GET request 🌐
    /// </summary>
    Task<HttpScriptResponse> HttpGetAsync(string url, Dictionary<string, string>? headers = null);
    
    /// <summary>
    /// Make an HTTP POST request 📤
    /// </summary>
    Task<HttpScriptResponse> HttpPostAsync(string url, object? body = null, Dictionary<string, string>? headers = null);
    
    /// <summary>
    /// Make an HTTP request with full control 🎯
    /// </summary>
    Task<HttpScriptResponse> HttpRequestAsync(HttpScriptRequest request);
    
    // === Data Operations === 💾
    
    /// <summary>
    /// Parse JSON string to object 📦
    /// </summary>
    object? ParseJson(string json);
    
    /// <summary>
    /// Convert object to JSON string 📄
    /// </summary>
    string ToJson(object? obj, bool pretty = false);
    
    /// <summary>
    /// Parse CSV string to table 📊
    /// </summary>
    List<Dictionary<string, object>> ParseCsv(string csv, bool hasHeaders = true);
    
    /// <summary>
    /// Convert table to CSV string 📋
    /// </summary>
    string ToCsv(List<Dictionary<string, object>> data, bool includeHeaders = true);
    
    // === Database Operations === 🗄️
    
    /// <summary>
    /// Execute a database query 🔍
    /// </summary>
    Task<List<Dictionary<string, object>>> QueryDatabaseAsync(string connectionString, string query, Dictionary<string, object>? parameters = null);
    
    /// <summary>
    /// Execute a database command (INSERT, UPDATE, DELETE) ✏️
    /// </summary>
    Task<int> ExecuteDatabaseAsync(string connectionString, string command, Dictionary<string, object>? parameters = null);
    
    // === File Operations === 📁
    
    /// <summary>
    /// Read text file content 📖
    /// </summary>
    Task<string> ReadFileAsync(string path);
    
    /// <summary>
    /// Write text to file 📝
    /// </summary>
    Task WriteFileAsync(string path, string content);
    
    /// <summary>
    /// Check if file exists 🔍
    /// </summary>
    bool FileExists(string path);
    
    /// <summary>
    /// List files in directory 📂
    /// </summary>
    List<string> ListFiles(string path, string pattern = "*");
    
    // === Utility Functions === 🛠️
    
    /// <summary>
    /// Sleep/delay execution ⏱️
    /// </summary>
    Task DelayAsync(int milliseconds);
    
    /// <summary>
    /// Generate a new GUID 🆔
    /// </summary>
    string NewGuid();
    
    /// <summary>
    /// Get current timestamp 📅
    /// </summary>
    DateTime Now();
    
    /// <summary>
    /// Format a date/time string 🕐
    /// </summary>
    string FormatDateTime(DateTime dateTime, string format);
    
    /// <summary>
    /// Encode string to Base64 🔐
    /// </summary>
    string Base64Encode(string text);
    
    /// <summary>
    /// Decode Base64 to string 🔓
    /// </summary>
    string Base64Decode(string base64);
    
    /// <summary>
    /// Hash string with specified algorithm 🔒
    /// </summary>
    string Hash(string text, string algorithm = "SHA256");
    
    // === Workflow Control === ⚡
    
    /// <summary>
    /// Trigger another workflow 🚀
    /// </summary>
    Task<Guid> TriggerWorkflowAsync(string workflowName, Dictionary<string, object>? inputs = null);
    
    /// <summary>
    /// Get the current execution context 🎯
    /// </summary>
    ScriptExecutionContext GetContext();
}

/// <summary>
/// 📦 HTTP request builder for scripts
/// </summary>
public record HttpScriptRequest
{
    public required string Url { get; init; }
    public string Method { get; init; } = "GET";
    public Dictionary<string, string>? Headers { get; init; }
    public object? Body { get; init; }
    public int TimeoutSeconds { get; init; } = 30;
}

/// <summary>
/// 📬 HTTP response for scripts
/// </summary>
public record HttpScriptResponse
{
    public int StatusCode { get; init; }
    public string Body { get; init; } = string.Empty;
    public Dictionary<string, string> Headers { get; init; } = new();
    public bool IsSuccess => StatusCode >= 200 && StatusCode < 300;
}

/// <summary>
/// 🎯 Execution context available to scripts
/// </summary>
public record ScriptExecutionContext
{
    public Guid ExecutionId { get; init; }
    public string NodeId { get; init; } = string.Empty;
    public string WorkflowName { get; init; } = string.Empty;
    public DateTime StartTime { get; init; }
    public Dictionary<string, object?> Inputs { get; init; } = new();
}
```

---

### JavaScript Examples

```javascript
// 🟨 JavaScript Example - Data Transformation

// Access input data
const input = $input;

// Use the workflow API
api.LogInfo("Starting JavaScript transformation! ✨");

// HTTP request
const response = await api.HttpGetAsync("https://api.example.com/data");
const data = api.ParseJson(response.Body);

// Transform data
const transformed = data.items.map(item => ({
    id: item.id,
    name: item.name.toUpperCase(),
    timestamp: api.Now()
}));

// Set workflow variable
api.SetVariable("processedCount", transformed.length);

// Log result
api.LogInfo(`Processed ${transformed.length} items!`);

// Return output
return {
    output: transformed,
    summary: `Transformed ${transformed.length} items`
};
```

```javascript
// 🌐 JavaScript Example - API Integration

// Get configuration from variables
const apiKey = api.GetVariable("apiKey");
const endpoint = api.GetVariable("endpoint");

// Make authenticated request
const response = await api.HttpRequestAsync({
    Url: endpoint,
    Method: "POST",
    Headers: {
        "Authorization": `Bearer ${apiKey}`,
        "Content-Type": "application/json"
    },
    Body: $input,
    TimeoutSeconds: 60
});

if (response.IsSuccess) {
    api.LogInfo("✅ Request successful!");
    return { output: api.ParseJson(response.Body) };
} else {
    api.LogError(`❌ Request failed with status ${response.StatusCode}`);
    throw new Error(`HTTP ${response.StatusCode}`);
}
```

---

### Lua Examples

```lua
-- 🌙 Lua Example - Simple Data Processing

-- Access the workflow API
local api = workflow.api

api:LogInfo("Starting Lua script execution! 🌙")

-- Get input data
local input = workflow.input

-- Process data
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

-- Set variable
api:SetVariable("filteredCount", #result)

-- Return output
return {
    output = result,
    count = #result
}
```

```lua
-- 📊 Lua Example - CSV Processing

local api = workflow.api

-- Read CSV file
local csvContent = api:ReadFileAsync("data/input.csv")
local data = api:ParseCsv(csvContent, true)

api:LogInfo(string.format("Read %d rows from CSV", #data))

-- Transform data
local transformed = {}
for i, row in ipairs(data) do
    if row.status == "active" then
        row.processed_date = api:FormatDateTime(api:Now(), "yyyy-MM-dd")
        table.insert(transformed, row)
    end
end

-- Write result
local outputCsv = api:ToCsv(transformed, true)
api:WriteFileAsync("data/output.csv", outputCsv)

api:LogInfo(string.format("✅ Wrote %d rows to output", #transformed))

return { output = transformed }
```

---

### Python Examples

```python
# 🐍 Python Example - Data Analysis

# Access the workflow API
api = workflow.api

api.LogInfo("Starting Python script execution! 🐍")

# Get input data
input_data = workflow.input

# Process with list comprehension
filtered = [
    item for item in input_data 
    if item['score'] > 75
]

# Calculate statistics
total = len(filtered)
avg_score = sum(item['score'] for item in filtered) / total if total > 0 else 0

api.LogInfo(f"Processed {total} items with average score {avg_score:.2f}")

# Set workflow variables
api.SetVariable("total_processed", total)
api.SetVariable("average_score", avg_score)

# Return result
return {
    'output': filtered,
    'stats': {
        'total': total,
        'average': avg_score
    }
}
```

```python
# 🗄️ Python Example - Database ETL

import json

api = workflow.api

# Get connection string from variable
conn_string = api.GetVariable("dbConnection")

# Extract data from source
api.LogInfo("📥 Extracting data from database...")
source_data = await api.QueryDatabaseAsync(
    conn_string,
    "SELECT * FROM source_table WHERE status = @status",
    {"status": "pending"}
)

api.LogInfo(f"Found {len(source_data)} records to process")

# Transform data
transformed = []
for row in source_data:
    transformed.append({
        'id': row['id'],
        'name': row['name'].upper(),
        'processed_date': api.FormatDateTime(api.Now(), "yyyy-MM-dd HH:mm:ss"),
        'metadata': json.dumps(row.get('metadata', {}))
    })

# Load into destination
api.LogInfo("📤 Loading data into destination...")
for item in transformed:
    await api.ExecuteDatabaseAsync(
        conn_string,
        """INSERT INTO destination_table (id, name, processed_date, metadata) 
           VALUES (@id, @name, @processed_date, @metadata)""",
        item
    )

api.LogInfo(f"✅ Successfully processed {len(transformed)} records")

return {
    'output': transformed,
    'count': len(transformed)
}
```

---

### Script Execution Environment

```csharp
/// <summary>
/// 🎯 Configuration for script execution environments
/// </summary>
/// <remarks>
/// CopilotNote: Each language has different security and performance characteristics!
/// Configure appropriately for your use case.
/// </remarks>
public record ScriptExecutionConfig
{
    /// <summary>
    /// Maximum execution time ⏱️
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
    
    /// <summary>
    /// Maximum memory allocation 💾
    /// </summary>
    public long MaxMemoryBytes { get; init; } = 256 * 1024 * 1024; // 256 MB
    
    /// <summary>
    /// Allow network access 🌐
    /// </summary>
    public bool AllowNetwork { get; init; } = true;
    
    /// <summary>
    /// Allow file system access 📁
    /// </summary>
    public bool AllowFileSystem { get; init; } = false;
    
    /// <summary>
    /// Allow database access 🗄️
    /// </summary>
    public bool AllowDatabase { get; init; } = true;
    
    /// <summary>
    /// Allowed file system paths (when filesystem access is enabled) 🔒
    /// </summary>
    public IReadOnlyList<string> AllowedPaths { get; init; } = [];
    
    /// <summary>
    /// Maximum number of HTTP requests per execution 🌐
    /// </summary>
    public int MaxHttpRequests { get; init; } = 10;
}
```

### Language-Specific Implementations

```csharp
/// <summary>
/// 🟨 JavaScript executor using Jint
/// </summary>
public class JavaScriptExecutor : IScriptExecutor
{
    public string Language => "JavaScript";
    
    public async Task<object?> ExecuteAsync(
        string script, 
        Dictionary<string, object?> inputs,
        IWorkflowScriptApi api,
        ScriptExecutionConfig config)
    {
        var engine = new Engine(options =>
        {
            options.TimeoutInterval(config.Timeout);
            options.LimitMemory(config.MaxMemoryBytes);
        });
        
        // Inject API
        engine.SetValue("api", api);
        engine.SetValue("$input", inputs.GetValueOrDefault("input"));
        
        // Execute script
        var result = engine.Evaluate(script);
        return result.ToObject();
    }
}

/// <summary>
/// 🌙 Lua executor using MoonSharp
/// </summary>
public class LuaExecutor : IScriptExecutor
{
    public string Language => "Lua";
    
    public async Task<object?> ExecuteAsync(
        string script,
        Dictionary<string, object?> inputs,
        IWorkflowScriptApi api,
        ScriptExecutionConfig config)
    {
        var luaScript = new Script();
        
        // Register API
        UserData.RegisterType<IWorkflowScriptApi>();
        luaScript.Globals["workflow"] = new
        {
            api = api,
            input = inputs.GetValueOrDefault("input")
        };
        
        // Execute with timeout
        using var cts = new CancellationTokenSource(config.Timeout);
        var result = luaScript.DoString(script);
        
        return result.ToObject();
    }
}

/// <summary>
/// 🐍 Python executor using IronPython or Python.NET
/// </summary>
public class PythonExecutor : IScriptExecutor
{
    public string Language => "Python";
    
    public async Task<object?> ExecuteAsync(
        string script,
        Dictionary<string, object?> inputs,
        IWorkflowScriptApi api,
        ScriptExecutionConfig config)
    {
        var engine = Python.CreateEngine();
        var scope = engine.CreateScope();
        
        // Inject API and input
        scope.SetVariable("workflow", new
        {
            api = api,
            input = inputs.GetValueOrDefault("input")
        });
        
        // Execute with timeout
        using var cts = new CancellationTokenSource(config.Timeout);
        var result = engine.Execute(script, scope);
        
        return result;
    }
}
```

### Script Library System

```csharp
/// <summary>
/// 📚 Reusable script library system
/// Allows creating shared functions across workflows! ✨
/// </summary>
public interface IScriptLibrary
{
    /// <summary>
    /// Register a script library 📦
    /// </summary>
    Task RegisterLibraryAsync(ScriptLibraryDefinition library);
    
    /// <summary>
    /// Get a registered library 🔍
    /// </summary>
    ScriptLibraryDefinition? GetLibrary(string libraryId);
    
    /// <summary>
    /// List all available libraries 📋
    /// </summary>
    IReadOnlyList<ScriptLibraryDefinition> GetAllLibraries();
}

/// <summary>
/// 📖 Script library definition
/// </summary>
public record ScriptLibraryDefinition
{
    public required string LibraryId { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string Language { get; init; }
    public required string Code { get; init; }
    public IReadOnlyList<string> ExportedFunctions { get; init; } = [];
}
```

**Example Library Usage:**

```javascript
// 📚 Register a utility library
{
    "libraryId": "utils.datehelpers",
    "name": "Date Helpers",
    "language": "JavaScript",
    "code": `
        function formatDate(date, format) {
            // Format implementation
        }
        
        function addDays(date, days) {
            const result = new Date(date);
            result.setDate(result.getDate() + days);
            return result;
        }
        
        function isWeekend(date) {
            const day = date.getDay();
            return day === 0 || day === 6;
        }
    `,
    "exportedFunctions": ["formatDate", "addDays", "isWeekend"]
}

// 📝 Use in a workflow script
import * as dateHelpers from 'utils.datehelpers';

const tomorrow = dateHelpers.addDays(api.Now(), 1);
api.LogInfo(`Tomorrow is: ${dateHelpers.formatDate(tomorrow, 'yyyy-MM-dd')}`);
```

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

## 🌐 External API Specification

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

---

## 🏗️ Phase 1: Foundation (Weeks 1-6)

**Goal:** Establish the core architecture and basic workflow execution engine! 🎯

### 1.1 Project Structure & Setup (Week 1)

**Tasks:**
- [ ] **Create solution structure with projects:** 📁
  - [ ] Create blank solution file (`Workflow.sln`)
  - [ ] Create `Workflow.Core` class library project (.NET 8)
    - [ ] Add folder structure (Models, Interfaces, Abstractions)
    - [ ] Configure project settings (nullable enabled, implicit usings)
  - [ ] Create `Workflow.Engine` class library project (.NET 8)
    - [ ] Add folder structure (Actors, Services, Messages)
    - [ ] Add reference to `Workflow.Core`
  - [ ] Create `Workflow.Modules` class library project (.NET 8)
    - [ ] Add folder structure (Builtin, Abstractions)
    - [ ] Add reference to `Workflow.Core`
  - [ ] Create `Workflow.Api` web project (ASP.NET Core)
    - [ ] Add folder structure (Controllers, Hubs, Middleware)
    - [ ] Add references to Engine and Modules
  - [ ] Create `Workflow.UI` project (Blazor WebAssembly or React)
    - [ ] Configure frontend build pipeline
    - [ ] Add folder structure (Components, Pages, Services)
  - [ ] Create `Workflow.Tests` test project (xUnit)
    - [ ] Add test project references
    - [ ] Configure test coverage tools
    
- [ ] **Set up CI/CD pipeline** 🚀
  - [ ] Choose platform (GitHub Actions or Azure DevOps)
  - [ ] Create build workflow/pipeline
    - [ ] Configure dotnet restore
    - [ ] Configure dotnet build
    - [ ] Configure code linting
    - [ ] Configure static analysis
  - [ ] Create test workflow/pipeline
    - [ ] Configure dotnet test
    - [ ] Configure test result reporting
    - [ ] Configure code coverage collection
    - [ ] Set coverage thresholds (e.g., 80%)
  - [ ] Create package workflow/pipeline
    - [ ] Configure NuGet package creation
    - [ ] Configure container image build
    - [ ] Configure artifact publishing
  - [ ] Create deployment workflow/pipeline
    - [ ] Configure environment stages (dev, staging, prod)
    - [ ] Configure approval gates
    - [ ] Configure rollback procedures
    
- [ ] **Configure code standards and linting rules** 📏
  - [ ] Add `.editorconfig` file
    - [ ] Configure C# formatting rules
    - [ ] Configure naming conventions
    - [ ] Configure indentation (tabs vs spaces)
    - [ ] Configure line ending preferences
  - [ ] Add `Directory.Build.props` for common properties
    - [ ] Set common NuGet package versions
    - [ ] Configure nullable reference types
    - [ ] Configure implicit usings
    - [ ] Configure warning levels
  - [ ] Configure StyleCop analyzers
    - [ ] Install StyleCop.Analyzers NuGet package
    - [ ] Create `stylecop.json` configuration
    - [ ] Configure documentation rules
  - [ ] Configure Roslyn analyzers
    - [ ] Enable CA (Code Analysis) rules
    - [ ] Configure security rules
    - [ ] Configure performance rules
  - [ ] Add pre-commit hooks
    - [ ] Install Husky.NET
    - [ ] Configure format check on commit
    - [ ] Configure build check on commit
    
- [ ] **Set up Git branching strategy (GitFlow)** 🌳
  - [ ] Document branching strategy in README
    - [ ] Define `main` branch purpose (production)
    - [ ] Define `develop` branch purpose (integration)
    - [ ] Define `feature/*` branch pattern
    - [ ] Define `release/*` branch pattern
    - [ ] Define `hotfix/*` branch pattern
  - [ ] Configure branch protection rules
    - [ ] Require pull request reviews
    - [ ] Require status checks to pass
    - [ ] Require linear history
    - [ ] Restrict direct pushes to main
  - [ ] Create PR templates
    - [ ] Add checklist for PRs
    - [ ] Add sections for description, testing, screenshots
  - [ ] Create issue templates
    - [ ] Bug report template
    - [ ] Feature request template
    - [ ] Documentation improvement template

**Dependencies:**
```xml
<PackageReference Include="Akka" Version="1.5.*" />
<PackageReference Include="Akka.Persistence" Version="1.5.*" />
<PackageReference Include="Akka.Cluster" Version="1.5.*" />
<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.0.*" />
<PackageReference Include="Serilog" Version="3.1.*" />
```

**Deliverables:**
- ✅ Solution builds successfully without warnings
- ✅ CI/CD pipeline runs on every commit
- ✅ Code standards documented and enforced
- ✅ All project references correct
- ✅ Git workflow documented

---

### 1.2 Core Domain Models (Week 1-2)

**Tasks:**
- [ ] **Implement `WorkflowDefinition` and related models** 🌸
  - [ ] Create `WorkflowDefinition` record class
    - [ ] Add `Id` property (Guid)
    - [ ] Add `Name` property (string)
    - [ ] Add `Description` property (string?)
    - [ ] Add `Version` property (Version)
    - [ ] Add `Nodes` collection property
    - [ ] Add `Connections` collection property
    - [ ] Add `Variables` dictionary property
    - [ ] Add `Trigger` property (optional)
    - [ ] Add `ErrorHandling` configuration
    - [ ] Add `CreatedAt` and `UpdatedAt` timestamps
    - [ ] Add `Tags` for categorization
  - [ ] Add XML documentation to all properties
  - [ ] Implement `IEquatable<WorkflowDefinition>`
  - [ ] Implement custom `ToString()` override
  
- [ ] **Implement `NodeDefinition` and `ConnectionDefinition`** 🧩
  - [ ] Create `NodeDefinition` record class
    - [ ] Add `Id` property (string - unique within workflow)
    - [ ] Add `ModuleId` property (string)
    - [ ] Add `Name` property (string - display name)
    - [ ] Add `Properties` dictionary (configuration values)
    - [ ] Add `Position` for UI (X, Y coordinates)
    - [ ] Add `ErrorHandling` (node-specific overrides)
    - [ ] Add `Timeout` configuration
    - [ ] Add `RetryPolicy` configuration
    - [ ] Add `Metadata` for extensibility
  - [ ] Create `ConnectionDefinition` record class
    - [ ] Add `SourceNodeId` property
    - [ ] Add `SourcePortName` property
    - [ ] Add `TargetNodeId` property
    - [ ] Add `TargetPortName` property
    - [ ] Add `Condition` property (optional - for conditional routing)
    - [ ] Add `Priority` property (for parallel execution)
  - [ ] Add validation methods
  - [ ] Add XML documentation
  
- [ ] **Implement `ModuleSchema` and property system** 📋
  - [ ] Create `ModuleSchema` record class
    - [ ] Add `Inputs` collection (PropertyDefinition)
    - [ ] Add `Outputs` collection (PropertyDefinition)
    - [ ] Add `Configuration` collection (PropertyDefinition)
  - [ ] Create `PropertyDefinition` record class
    - [ ] Add `Name` property
    - [ ] Add `Type` property (PropertyType enum)
    - [ ] Add `Description` property
    - [ ] Add `DefaultValue` property
    - [ ] Add `IsRequired` property
    - [ ] Add `ValidationRules` collection
    - [ ] Add `DisplayMetadata` (UI hints)
  - [ ] Create `PropertyType` enum
    - [ ] String, Int, Long, Decimal, Boolean
    - [ ] DateTime, TimeSpan, Guid
    - [ ] Object, Array
    - [ ] Connection (reference to another node's output)
    - [ ] Variable (reference to workflow variable)
  - [ ] Create validation rule types
    - [ ] MinLength, MaxLength (strings)
    - [ ] Min, Max (numbers)
    - [ ] Regex (pattern matching)
    - [ ] Enum (allowed values)
    - [ ] Custom (lambda expression)
  
- [ ] **Create validation logic for workflow definitions** ✅
  - [ ] Implement `WorkflowValidator` class
    - [ ] Validate workflow has at least one node
    - [ ] Validate all node IDs are unique
    - [ ] Validate all module IDs exist in registry
    - [ ] Validate node properties match module schema
    - [ ] Validate no cycles in connections (detect infinite loops)
    - [ ] Validate all connections reference valid nodes
    - [ ] Validate all connections reference valid ports
    - [ ] Validate at least one start node (no incoming connections)
    - [ ] Validate no orphaned nodes (disconnected subgraphs)
    - [ ] Validate property types match schema
    - [ ] Validate required properties are provided
    - [ ] Validate variable references exist
  - [ ] Implement `ValidationResult` class
    - [ ] Add `IsValid` property
    - [ ] Add `Errors` collection
    - [ ] Add `Warnings` collection
    - [ ] Add error codes and messages
  - [ ] Create validation rule attributes
  - [ ] Add fluent validation integration
  
- [ ] **Implement JSON serialization/deserialization** 📝
  - [ ] Configure System.Text.Json settings
    - [ ] Add custom converters for complex types
    - [ ] Configure naming policy (camelCase)
    - [ ] Configure null handling
    - [ ] Configure enum serialization
    - [ ] Configure indentation for readability
  - [ ] Create JSON converter for `Version` type
  - [ ] Create JSON converter for `PropertyValue` type
  - [ ] Test serialization roundtrip (serialize → deserialize → equals)
  - [ ] Add support for schema versioning
  - [ ] Implement migration logic for old schema versions
  - [ ] Add JSON schema generation for validation

**Key Classes:**
```csharp
✅ WorkflowDefinition
✅ NodeDefinition
✅ ConnectionDefinition
✅ ModuleSchema
✅ PropertyDefinition
✅ VariableDefinition
✅ TriggerDefinition
✅ ValidationResult
✅ WorkflowValidator
```

**Tests:**
- [ ] **Workflow definition validation tests** 🧪
  - [ ] Test cycle detection (A → B → C → A)
  - [ ] Test orphaned node detection
  - [ ] Test missing node references in connections
  - [ ] Test invalid port names
  - [ ] Test duplicate node IDs
  - [ ] Test missing required properties
  - [ ] Test invalid property types
  - [ ] Test variable reference validation
  - [ ] Test start node detection
  - [ ] Test complex workflow graphs
  
- [ ] **Serialization/deserialization tests** 💾
  - [ ] Test simple workflow serialization
  - [ ] Test complex workflow with all features
  - [ ] Test null/empty property handling
  - [ ] Test special characters in strings
  - [ ] Test large workflows (performance)
  - [ ] Test schema version migration
  - [ ] Test backwards compatibility
  
- [ ] **Connection validation tests** 🔗
  - [ ] Test valid connections
  - [ ] Test invalid source node
  - [ ] Test invalid target node
  - [ ] Test invalid port names
  - [ ] Test self-connections (node to itself)
  - [ ] Test multiple connections to same input port

**Deliverables:**
- ✅ Core models fully implemented with all properties
- ✅ 90%+ test coverage on domain models
- ✅ XML documentation on all public APIs
- ✅ Validation prevents invalid workflows
- ✅ JSON serialization works flawlessly

---

### 1.3 Basic Akka.NET Engine (Week 2-4)

**Tasks:**
- [ ] **Implement `WorkflowSupervisor` actor** 🎭
  - [ ] Create actor class inheriting from `ReceiveActor`
  - [ ] Add private field for tracking active workflows (Dictionary)
  - [ ] Implement constructor with dependency injection
  - [ ] Define message handlers
    - [ ] Handle `CreateWorkflowInstance` message
      - [ ] Validate workflow definition
      - [ ] Generate unique execution ID
      - [ ] Create child `WorkflowExecutor` actor
      - [ ] Store actor reference in dictionary
      - [ ] Reply with execution ID
    - [ ] Handle `GetWorkflowStatus` message
      - [ ] Look up executor actor
      - [ ] Forward status request
      - [ ] Return status to sender
    - [ ] Handle `CancelWorkflow` message
      - [ ] Look up executor actor
      - [ ] Send cancellation message
      - [ ] Clean up if needed
    - [ ] Handle `Terminated` message (child death watch)
      - [ ] Remove actor from tracking dictionary
      - [ ] Log termination reason
      - [ ] Notify subscribers
  - [ ] Configure supervision strategy
    - [ ] Define restart directive for recoverable errors
    - [ ] Define stop directive for unrecoverable errors
    - [ ] Set max retry limits (e.g., 3 retries in 1 minute)
  - [ ] Add structured logging with context
  - [ ] Implement health check endpoint integration
  
- [ ] **Implement `WorkflowExecutor` actor** 🎀
  - [ ] Create actor class inheriting from `ReceiveActor`
  - [ ] Add private fields for state management
    - [ ] Workflow definition
    - [ ] Execution context
    - [ ] Node actor references (Dictionary)
    - [ ] Execution graph/topology
    - [ ] Completed nodes tracking (HashSet)
    - [ ] Failed nodes tracking
  - [ ] Define message handlers
    - [ ] Handle `StartExecution` message
      - [ ] Initialize execution context
      - [ ] Parse workflow graph
      - [ ] Identify start nodes (no dependencies)
      - [ ] Create NodeExecutor actors for start nodes
      - [ ] Send `Execute` messages to start nodes
      - [ ] Update state to `Running`
    - [ ] Handle `NodeExecutionCompleted` message
      - [ ] Mark node as completed
      - [ ] Store node outputs
      - [ ] Determine next nodes to execute
      - [ ] Check if outputs satisfy connection conditions
      - [ ] Create NodeExecutor actors for next nodes
      - [ ] Pass input data from previous node outputs
      - [ ] Check if workflow is complete (all nodes done)
      - [ ] If complete, send `WorkflowCompleted` to parent
    - [ ] Handle `NodeExecutionFailed` message
      - [ ] Mark node as failed
      - [ ] Log error details
      - [ ] Check error handling configuration
      - [ ] If retry configured, schedule retry
      - [ ] If continue-on-error, proceed to next nodes
      - [ ] If fail-fast, cancel all other nodes
      - [ ] Send `WorkflowFailed` to parent
    - [ ] Handle `CancelExecution` message
      - [ ] Send cancel to all running node actors
      - [ ] Update state to `Cancelled`
      - [ ] Clean up resources
      - [ ] Notify parent
    - [ ] Handle `GetProgress` message
      - [ ] Calculate completion percentage
      - [ ] Gather status from all nodes
      - [ ] Return progress details
  - [ ] Implement execution graph traversal
    - [ ] Topological sort for dependency order
    - [ ] Handle parallel execution paths
    - [ ] Detect and handle fan-out/fan-in patterns
  - [ ] Add execution timing and metrics
  - [ ] Implement state persistence (for resumability)
  
- [ ] **Implement `NodeExecutor` actor** ✨
  - [ ] Create actor class inheriting from `ReceiveActor`
  - [ ] Add private fields
    - [ ] Module instance reference
    - [ ] Node configuration
    - [ ] Execution context
    - [ ] Cancellation token source
  - [ ] Define message handlers
    - [ ] Handle `Execute` message
      - [ ] Log execution start
      - [ ] Validate input data against schema
      - [ ] Bind properties from configuration
      - [ ] Create module execution context
      - [ ] Call module's `ExecuteAsync` method
      - [ ] Handle success case
        - [ ] Validate outputs against schema
        - [ ] Send `NodeExecutionCompleted` to parent
        - [ ] Include output data
      - [ ] Handle failure case (try-catch)
        - [ ] Log exception details
        - [ ] Send `NodeExecutionFailed` to parent
        - [ ] Include error information
      - [ ] Handle timeout case
        - [ ] Cancel execution token
        - [ ] Log timeout
        - [ ] Send failure message
    - [ ] Handle `Cancel` message
      - [ ] Trigger cancellation token
      - [ ] Interrupt module execution
      - [ ] Send cancellation acknowledgment
    - [ ] Handle `GetProgress` message (if module supports it)
      - [ ] Query module progress
      - [ ] Return progress percentage
  - [ ] Implement timeout management
    - [ ] Use `Context.SetReceiveTimeout`
    - [ ] Configure from node configuration
    - [ ] Default to reasonable timeout (e.g., 30 seconds)
  - [ ] Add detailed execution logging
  - [ ] Implement input/output data validation
  - [ ] Add execution metrics (duration, memory, etc.)
  
- [ ] **Create actor messaging protocol** 📬
  - [ ] Define message classes (use records for immutability)
    - [ ] `CreateWorkflowInstance(Guid workflowId, WorkflowDefinition definition, Dictionary<string, object?> inputs)`
    - [ ] `StartExecution(Guid executionId)`
    - [ ] `CancelExecution(Guid executionId)`
    - [ ] `GetWorkflowStatus(Guid executionId)`
    - [ ] `Execute(string nodeId, Dictionary<string, object?> inputs)`
    - [ ] `NodeExecutionCompleted(string nodeId, Dictionary<string, object?> outputs)`
    - [ ] `NodeExecutionFailed(string nodeId, Exception error)`
    - [ ] `WorkflowCompleted(Guid executionId, Dictionary<string, object?> outputs)`
    - [ ] `WorkflowFailed(Guid executionId, Exception error)`
    - [ ] `GetProgress()`
    - [ ] `ProgressUpdate(int percentage, string currentNode)`
  - [ ] Add message serialization attributes
  - [ ] Document message flow diagrams
  - [ ] Add message validation
  
- [ ] **Implement basic execution flow (sequential nodes only)** ➡️
  - [ ] Implement linear execution logic (A → B → C)
  - [ ] Add proper data flow between nodes
  - [ ] Implement output-to-input mapping
  - [ ] Handle missing required inputs
  - [ ] Validate data types match
  - [ ] Add flow control logging
  
- [ ] **Add execution state tracking** 📊
  - [ ] Create `ExecutionState` enum
    - [ ] `Pending` - Not started
    - [ ] `Running` - Currently executing
    - [ ] `Completed` - Finished successfully
    - [ ] `Failed` - Finished with error
    - [ ] `Cancelled` - Cancelled by user
    - [ ] `Paused` - Temporarily paused
  - [ ] Create `ExecutionContext` class
    - [ ] Add `ExecutionId` property
    - [ ] Add `WorkflowId` property
    - [ ] Add `State` property
    - [ ] Add `StartTime` property
    - [ ] Add `EndTime` property
    - [ ] Add `Variables` dictionary (workflow variables)
    - [ ] Add `NodeStates` dictionary (per-node status)
    - [ ] Add `Outputs` dictionary (final outputs)
    - [ ] Add `Error` property (if failed)
  - [ ] Implement state persistence snapshots
  - [ ] Add state change events/notifications
  
- [ ] **Implement supervisor strategy for error handling** 🛡️
  - [ ] Define supervision directives per actor type
    - [ ] WorkflowSupervisor directives
      - [ ] Restart on transient failures
      - [ ] Stop on critical failures
      - [ ] Escalate on unknown failures
    - [ ] WorkflowExecutor directives
      - [ ] Resume for node failures (if continue-on-error)
      - [ ] Restart for recoverable state corruption
      - [ ] Stop for unrecoverable errors
    - [ ] NodeExecutor directives
      - [ ] Restart with backoff for transient errors
      - [ ] Stop after max retries
  - [ ] Configure restart limits
    - [ ] Max restarts: 3
    - [ ] Time window: 1 minute
  - [ ] Implement custom supervision logic
  - [ ] Add supervision event logging
  - [ ] Test supervision with failure injection

**Key Actors:**
```csharp
✅ WorkflowSupervisor - Manages workflow lifecycle
✅ WorkflowExecutor - Executes a single workflow instance
✅ NodeExecutor - Executes a single node
✅ ExecutionMonitor - Tracks execution progress (optional)
```

**Actor Messages:**
```csharp
✅ CreateWorkflowInstance(workflowId, definition, inputs)
✅ StartExecution(executionId)
✅ Execute(nodeId, inputs)
✅ NodeExecutionCompleted(nodeId, outputs)
✅ NodeExecutionFailed(nodeId, error)
✅ WorkflowCompleted(workflowId, outputs)
✅ WorkflowFailed(workflowId, error)
✅ CancelExecution(executionId)
✅ GetWorkflowStatus(executionId)
✅ GetProgress()
```

**Tests:**
- [ ] **Actor lifecycle tests** 🔄
  - [ ] Test actor creation
  - [ ] Test actor initialization
  - [ ] Test actor termination
  - [ ] Test graceful shutdown
  - [ ] Test resource cleanup
  
- [ ] **Message passing tests** 📨
  - [ ] Test Tell (fire-and-forget) messaging
  - [ ] Test Ask (request-response) messaging
  - [ ] Test message ordering guarantees
  - [ ] Test message delivery under load
  - [ ] Test dead letter handling
  - [ ] Test message serialization
  
- [ ] **Basic workflow execution tests (A → B → C)** ✅
  - [ ] Test 3-node linear workflow
  - [ ] Test data passing between nodes
  - [ ] Test workflow completion detection
  - [ ] Test output collection
  - [ ] Test empty workflow (no nodes)
  - [ ] Test single-node workflow
  
- [ ] **Error handling and supervision tests** 🛡️
  - [ ] Test node failure handling
  - [ ] Test workflow failure propagation
  - [ ] Test continue-on-error behavior
  - [ ] Test fail-fast behavior
  - [ ] Test retry logic
  - [ ] Test timeout handling
  - [ ] Test supervision restart
  - [ ] Test supervision stop
  - [ ] Test escalation
  
- [ ] **Actor restart behavior tests** 🔁
  - [ ] Test restart preserves state (where appropriate)
  - [ ] Test restart limits enforced
  - [ ] Test restart backoff timing
  - [ ] Test restart after transient failure
  - [ ] Test stop after max retries

**Deliverables:**
- ✅ Can execute a simple linear workflow (sequential nodes)
- ✅ Execution state properly tracked at all levels
- ✅ Errors handled gracefully with supervision strategies
- ✅ All actors communicate correctly via messages
- ✅ Complete message flow documented
- ✅ 85%+ test coverage on actor code

---

### 1.4 Module System Foundation (Week 4-5)

**Tasks:**
- [ ] **Implement `IWorkflowModule` interface** 📦
  - [ ] Define interface in `Workflow.Core.Abstractions`
  - [ ] Add `ModuleId` property (string - unique identifier)
  - [ ] Add `DisplayName` property (string - human-readable name)
  - [ ] Add `Category` property (string - for UI grouping)
  - [ ] Add `Description` property (string - help text)
  - [ ] Add `Icon` property (string - emoji or icon identifier)
  - [ ] Add `Version` property (Version - module version)
  - [ ] Add `Schema` property (ModuleSchema - inputs/outputs definition)
  - [ ] Add `ExecuteAsync` method signature
    - [ ] Parameter: `ModuleExecutionContext context`
    - [ ] Parameter: `CancellationToken cancellationToken`
    - [ ] Return type: `Task<ModuleResult>`
  - [ ] Add `ValidateConfiguration` method (optional)
  - [ ] Add XML documentation with examples
  - [ ] Create `ModuleExecutionContext` class
    - [ ] Add `Inputs` dictionary
    - [ ] Add `Configuration` dictionary
    - [ ] Add `Variables` access (workflow-level)
    - [ ] Add `Logger` instance
    - [ ] Add `ExecutionId` for correlation
    - [ ] Add `ServiceProvider` for DI
  - [ ] Create `ModuleResult` class
    - [ ] Add `IsSuccess` property
    - [ ] Add `Outputs` dictionary
    - [ ] Add `Error` property (optional)
    - [ ] Add `Metrics` (duration, resource usage, etc.)
  
- [ ] **Create module registry and discovery** 📚
  - [ ] Implement `IModuleRegistry` interface
    - [ ] Add `RegisterModule(Type moduleType)` method
    - [ ] Add `RegisterModule(IWorkflowModule instance)` method
    - [ ] Add `UnregisterModule(string moduleId)` method
    - [ ] Add `GetModule(string moduleId)` method
    - [ ] Add `GetAllModules()` method
    - [ ] Add `GetModulesByCategory(string category)` method
    - [ ] Add `ModuleRegistered` event
    - [ ] Add `ModuleUnregistered` event
  - [ ] Implement `ModuleRegistry` class
    - [ ] Use ConcurrentDictionary for thread-safety
    - [ ] Implement module instance caching
    - [ ] Add module metadata indexing
    - [ ] Implement category-based lookup
    - [ ] Add search functionality (by name, tags)
  - [ ] Implement automatic discovery
    - [ ] Scan assemblies for types implementing `IWorkflowModule`
    - [ ] Use reflection to find modules
    - [ ] Apply module attributes for metadata
    - [ ] Auto-register discovered modules
    - [ ] Handle duplicate registrations gracefully
  - [ ] Add module dependency resolution
    - [ ] Track module dependencies
    - [ ] Validate dependencies are registered
    - [ ] Load modules in dependency order
  
- [ ] **Implement module validation** ✅
  - [ ] Create `ModuleValidator` class
  - [ ] Validate module ID is unique
  - [ ] Validate module ID follows naming conventions
  - [ ] Validate schema is properly defined
    - [ ] All input properties have types
    - [ ] All output properties have types
    - [ ] No conflicting property names
  - [ ] Validate module implements interface correctly
  - [ ] Validate module has parameterless constructor or DI constructor
  - [ ] Validate module metadata completeness
    - [ ] DisplayName is not empty
    - [ ] Description is provided
    - [ ] Category is valid
  - [ ] Run validation on registration
  - [ ] Return detailed validation errors
  - [ ] Add optional strict mode vs. lenient mode
  
- [ ] **Create module property binding system** 🔗
  - [ ] Implement `IPropertyBinder` interface
    - [ ] Add `BindProperties(Dictionary config, ModuleSchema schema)` method
    - [ ] Return bound values with type safety
  - [ ] Implement `PropertyBinder` class
    - [ ] Handle primitive type binding (string, int, bool, etc.)
    - [ ] Handle complex type binding (objects, arrays)
    - [ ] Handle variable references ({{Variable.Name}})
    - [ ] Handle node output references ({{NodeId.OutputName}})
    - [ ] Handle expression evaluation (simple expressions)
    - [ ] Implement type conversion
      - [ ] String to int/long/decimal
      - [ ] String to DateTime
      - [ ] String to Guid
      - [ ] JSON to objects
    - [ ] Validate bindings against schema
      - [ ] Check required properties present
      - [ ] Check types match
      - [ ] Check values meet validation rules
    - [ ] Implement default value assignment
    - [ ] Add detailed binding error messages
  - [ ] Create property value resolvers
    - [ ] Variable resolver
    - [ ] Node output resolver
    - [ ] Expression resolver
    - [ ] Static value resolver
  - [ ] Add caching for expensive bindings
  
- [ ] **Add support for dynamic module loading from assemblies** 🚀
  - [ ] Implement `IModuleLoader` interface
    - [ ] Add `LoadFromAssembly(string path)` method
    - [ ] Add `LoadFromDirectory(string path)` method
    - [ ] Add `UnloadModule(string moduleId)` method
  - [ ] Implement `ModuleLoader` class using `AssemblyLoadContext`
    - [ ] Create isolated AssemblyLoadContext per module
    - [ ] Load assembly from file path
    - [ ] Scan assembly for module types
    - [ ] Instantiate modules safely
    - [ ] Handle dependency resolution
    - [ ] Support assembly unloading
    - [ ] Implement assembly version checking
  - [ ] Add security validation
    - [ ] Verify assembly signature (optional)
    - [ ] Check for malicious code patterns
    - [ ] Sandbox module execution (future)
  - [ ] Add module package format
    - [ ] Define `.wfmod` package structure (ZIP)
    - [ ] Include module DLL
    - [ ] Include `module.json` manifest
    - [ ] Include dependencies folder
    - [ ] Include documentation/README
  - [ ] Implement package validation
    - [ ] Validate manifest schema
    - [ ] Check dependencies are available
    - [ ] Verify module compatibility
  - [ ] Add module hot-reload capability
    - [ ] Detect file changes
    - [ ] Unload old version
    - [ ] Load new version
    - [ ] Notify running workflows
  - [ ] Implement module versioning
    - [ ] Support multiple versions side-by-side
    - [ ] Allow workflows to pin versions
    - [ ] Handle breaking changes gracefully

**Key Interfaces:**
```csharp
✅ IWorkflowModule - Base module interface
✅ IModuleRegistry - Module registration and lookup
✅ IModuleLoader - Dynamic assembly loading
✅ IPropertyBinder - Property value binding
✅ ModuleExecutionContext - Runtime context
✅ ModuleResult - Execution result
```

**Tests:**
- [ ] **Module registration tests** 📝
  - [ ] Test register single module
  - [ ] Test register multiple modules
  - [ ] Test duplicate registration handling
  - [ ] Test unregister module
  - [ ] Test module lookup by ID
  - [ ] Test category-based filtering
  - [ ] Test module instance caching
  
- [ ] **Module discovery tests** 🔍
  - [ ] Test auto-discovery in assembly
  - [ ] Test discovery with no modules present
  - [ ] Test discovery with multiple modules
  - [ ] Test discovery excludes abstract classes
  - [ ] Test discovery excludes internal classes
  
- [ ] **Property binding tests** 🔗
  - [ ] Test bind primitive types (string, int, bool)
  - [ ] Test bind complex types (objects, arrays)
  - [ ] Test bind with type conversion
  - [ ] Test bind variable references
  - [ ] Test bind node output references
  - [ ] Test bind with default values
  - [ ] Test bind with missing required property (error)
  - [ ] Test bind with type mismatch (error)
  - [ ] Test bind with validation rules
  
- [ ] **Module validation tests** ✅
  - [ ] Test valid module passes
  - [ ] Test module with missing schema (error)
  - [ ] Test module with duplicate ID (error)
  - [ ] Test module with invalid characters in ID (error)
  - [ ] Test module without constructor (error)
  - [ ] Test validation error messages are clear
  
- [ ] **Dynamic loading tests** 🚀
  - [ ] Test load module from DLL
  - [ ] Test load from directory (multiple DLLs)
  - [ ] Test unload module
  - [ ] Test load invalid assembly (error)
  - [ ] Test load assembly with missing dependencies (error)
  - [ ] Test assembly isolation (separate contexts)
  - [ ] Test package format validation
  - [ ] Test hot-reload functionality

**Deliverables:**
- ✅ Module system can register and discover modules successfully
- ✅ Modules can be loaded dynamically from external assemblies
- ✅ Property values properly bound to module inputs with type safety
- ✅ Module validation prevents broken modules from loading
- ✅ 90%+ test coverage on module system
- ✅ Clear documentation for module developers

---

### 1.5 Basic Built-in Modules (Week 5-6)

**Tasks:**
- [ ] **Implement `LogModule` - Simple logging** 📝
  - [ ] Create `LogModule` class implementing `IWorkflowModule`
  - [ ] Configure module metadata
    - [ ] ModuleId: `builtin.log`
    - [ ] DisplayName: `Log Message`
    - [ ] Category: `Utilities`
    - [ ] Icon: `📝`
  - [ ] Define module schema
    - [ ] Input: `message` (string, required) - The message to log
    - [ ] Input: `level` (LogLevel enum, optional, default=Info) - Log level
    - [ ] Input: `includeContext` (bool, optional, default=false) - Include execution context
    - [ ] Output: `timestamp` (DateTime) - When the log was written
  - [ ] Implement ExecuteAsync method
    - [ ] Extract message from inputs
    - [ ] Extract log level from inputs
    - [ ] Resolve any variable references in message
    - [ ] Log to configured logger with level
    - [ ] Include execution ID in log context
    - [ ] Optionally include full context data
    - [ ] Return timestamp in outputs
  - [ ] Add template string support (e.g., "User {userId} logged in")
  - [ ] Support structured logging properties
  - [ ] Add XML documentation with examples
  - [ ] Write comprehensive unit tests
    - [ ] Test logging at each level (Debug, Info, Warning, Error)
    - [ ] Test variable interpolation in messages
    - [ ] Test template string formatting
    - [ ] Test context inclusion
  
- [ ] **Implement `DelayModule` - Pause execution** ⏱️
  - [ ] Create `DelayModule` class implementing `IWorkflowModule`
  - [ ] Configure module metadata
    - [ ] ModuleId: `builtin.delay`
    - [ ] DisplayName: `Delay`
    - [ ] Category: `Flow Control`
    - [ ] Icon: `⏱️`
  - [ ] Define module schema
    - [ ] Input: `duration` (TimeSpan or int milliseconds, required) - Delay duration
    - [ ] Input: `allowCancellation` (bool, optional, default=true)
    - [ ] Output: `actualDuration` (TimeSpan) - Actual time delayed
    - [ ] Output: `wasCancelled` (bool) - Whether delay was interrupted
  - [ ] Implement ExecuteAsync method
    - [ ] Parse duration from inputs (support ms, seconds, TimeSpan)
    - [ ] Validate duration is reasonable (e.g., < 1 hour)
    - [ ] Use `Task.Delay` with cancellation token
    - [ ] Track actual start/end time
    - [ ] Handle cancellation gracefully
    - [ ] Return actual duration in outputs
  - [ ] Add convenience duration parsing
    - [ ] Support "5s", "1m", "30s" format
    - [ ] Support ISO 8601 duration format
  - [ ] Add XML documentation with examples
  - [ ] Write comprehensive unit tests
    - [ ] Test short delay (100ms)
    - [ ] Test cancellation handling
    - [ ] Test duration parsing (various formats)
    - [ ] Test invalid duration (error)
    - [ ] Test timeout interaction
  
- [ ] **Implement `SetVariableModule` - Variable management** 💾
  - [ ] Create `SetVariableModule` class implementing `IWorkflowModule`
  - [ ] Configure module metadata
    - [ ] ModuleId: `builtin.setvariable`
    - [ ] DisplayName: `Set Variable`
    - [ ] Category: `Variables`
    - [ ] Icon: `💾`
  - [ ] Define module schema
    - [ ] Input: `name` (string, required) - Variable name
    - [ ] Input: `value` (object, required) - Value to set
    - [ ] Input: `scope` (enum, optional, default=Workflow) - Variable scope
    - [ ] Output: `previousValue` (object, nullable) - Previous value if existed
    - [ ] Output: `wasCreated` (bool) - True if new, false if updated
  - [ ] Implement ExecuteAsync method
    - [ ] Extract variable name from inputs
    - [ ] Extract value from inputs
    - [ ] Validate variable name (no special characters)
    - [ ] Get previous value from context (if exists)
    - [ ] Set variable in execution context
    - [ ] Determine if this is create or update
    - [ ] Return previous value and created flag
  - [ ] Support variable scopes
    - [ ] Workflow scope (shared across all nodes)
    - [ ] Execution scope (current execution only)
    - [ ] Global scope (shared across executions - optional)
  - [ ] Add type validation (optional)
  - [ ] Add XML documentation with examples
  - [ ] Write comprehensive unit tests
    - [ ] Test create new variable
    - [ ] Test update existing variable
    - [ ] Test variable name validation
    - [ ] Test different value types (string, int, object, array)
    - [ ] Test null value handling
    - [ ] Test scopes
  
- [ ] **Implement `GetVariableModule` - Variable access** 🔍
  - [ ] Create `GetVariableModule` class implementing `IWorkflowModule`
  - [ ] Configure module metadata
    - [ ] ModuleId: `builtin.getvariable`
    - [ ] DisplayName: `Get Variable`
    - [ ] Category: `Variables`
    - [ ] Icon: `🔍`
  - [ ] Define module schema
    - [ ] Input: `name` (string, required) - Variable name to retrieve
    - [ ] Input: `defaultValue` (object, optional) - Value if not found
    - [ ] Input: `throwIfMissing` (bool, optional, default=false)
    - [ ] Output: `value` (object) - Variable value
    - [ ] Output: `exists` (bool) - Whether variable exists
    - [ ] Output: `type` (string) - Type name of the value
  - [ ] Implement ExecuteAsync method
    - [ ] Extract variable name from inputs
    - [ ] Try to get variable from context
    - [ ] If not found and throwIfMissing is true, throw exception
    - [ ] If not found and default provided, return default
    - [ ] If not found, return null
    - [ ] Return value, exists flag, and type info
  - [ ] Support dot notation for nested properties
    - [ ] E.g., `user.address.city`
  - [ ] Add XML documentation with examples
  - [ ] Write comprehensive unit tests
    - [ ] Test get existing variable
    - [ ] Test get missing variable (with default)
    - [ ] Test get missing variable (without default)
    - [ ] Test throwIfMissing behavior
    - [ ] Test nested property access
    - [ ] Test type reporting

**Modules:**
```
✅ builtin.log - Log messages at various levels
✅ builtin.delay - Pause workflow execution
✅ builtin.setvariable - Set workflow variable
✅ builtin.getvariable - Get workflow variable
```

**Tests:**
- [ ] **Each module has unit tests** 🧪
  - [ ] Test module initialization
  - [ ] Test schema validation
  - [ ] Test ExecuteAsync with valid inputs
  - [ ] Test ExecuteAsync with invalid inputs
  - [ ] Test ExecuteAsync with missing inputs
  - [ ] Test error handling
  - [ ] Test cancellation handling
  - [ ] Test output generation
  
- [ ] **Integration tests combining multiple modules** 🔗
  - [ ] Test SetVariable → GetVariable → Log
  - [ ] Test SetVariable → Delay → GetVariable (verify persistence)
  - [ ] Test variable scoping across nodes
  - [ ] Test error propagation between modules
  
- [ ] **End-to-end workflow tests using these modules** 🎯
  - [ ] Create workflow: Log start → SetVariable → Delay → GetVariable → Log end
  - [ ] Execute workflow and validate outputs
  - [ ] Verify logs are written correctly
  - [ ] Verify variables are accessible
  - [ ] Verify timing is correct
  - [ ] Test workflow cancellation during delay

**Deliverables:**
- ✅ 4 basic modules working correctly
- ✅ Can execute a workflow using these modules
- ✅ Variables can be set and retrieved reliably
- ✅ Logging works at all levels
- ✅ Delay respects cancellation
- ✅ 95%+ test coverage on module implementations
- ✅ Module documentation complete with examples

---

### Phase 1 Success Criteria ✨

**Must Have:**
- [ ] Akka.NET actors properly structured and communicating
- [ ] Can execute simple sequential workflows (no branching yet)
- [ ] Module system working with 4 basic modules
- [ ] 80%+ code coverage on Phase 1 components
- [ ] Architecture documentation complete

**Demo Workflow:**
```
Start → Log "Hello" → Delay 1s → Set Variable "count"=1 → Get Variable "count" → Log Variable → End
```

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
```

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
```

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

### Phase 2 Success Criteria ✨

**Must Have:**
- [ ] All 3 persistence providers working (PostgreSQL, NATS KV, S3)
- [ ] Conditionals, loops, and parallel execution working
- [ ] 20+ built-in modules operational
- [ ] Complete REST API with auth
- [ ] 80%+ code coverage maintained

**Demo Workflow:**
```
Webhook Trigger → HTTP GET API → Transform JSON → 
Condition (if valid) → True: Database INSERT → Log Success
                    → False: Log Error
```

---

## 🎨 Phase 3: Advanced Features (Weeks 15-22)

**Goal:** Add scripting, UI, and advanced capabilities! 🌟

### 3.1 Scripting Engine (Week 15-17)

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

---

### 3.2 SignalR Real-Time Hub (Week 17)

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
✅ Real-time updates (SignalR)
✅ Workflow management
✅ Variable management
✅ Module browsing


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

## Phase 4: Polish & Production (Weeks 23-28) 💎 

**Goal:** Production readiness, performance, and quality! 🚀

### 4.1 Performance Optimization (Week 23-24)

**Tasks:**
- [ ] Profile and optimize hot paths
- [ ] Implement execution plan caching
- [ ] Optimize database queries
- [ ] Add connection pooling
- [ ] Implement result caching
- [ ] Add batch execution support
- [ ] Optimize actor message passing

**Performance Targets:**
```
✅ Workflow execution: < 50ms overhead
✅ API response time: < 100ms (p95)
✅ UI load time: < 2s
✅ Concurrent executions: 1000+
✅ Memory: < 500MB for 100 workflows
```

**Tests:**
- [ ] Load testing (k6/JMeter)
- [ ] Stress testing
- [ ] Memory profiling
- [ ] Performance benchmarks

**Deliverables:**
- ✅ Performance targets met
- ✅ Bottlenecks fixed
- ✅ Benchmark results documented

---

### 4.2 Observability & Monitoring (Week 24)

**Tasks:**
- [ ] Implement structured logging (Serilog)
- [ ] Add OpenTelemetry tracing
- [ ] Implement Prometheus metrics
- [ ] Create Grafana dashboards
- [ ] Add health check endpoints
- [ ] Implement alerting

**Metrics:**
```
✅ Workflow execution count
✅ Execution duration (p50, p95, p99)
✅ Error rate
✅ Active executions
✅ Queue depth
✅ Resource utilization
```

**Logging:**
```csharp
✅ Serilog structured logging
✅ Log levels properly configured
✅ Correlation IDs
✅ Log aggregation (Elasticsearch/Seq)
```

**Tests:**
- [ ] Metric collection tests
- [ ] Logging tests
- [ ] Health check tests

**Deliverables:**
- ✅ Full observability operational
- ✅ Grafana dashboards created
- ✅ Alerting configured

---

### 4.3 Security Hardening (Week 25)

**Tasks:**
- [ ] Conduct security audit
- [ ] Implement rate limiting
- [ ] Add input validation everywhere
- [ ] Implement secret management (Azure Key Vault/AWS Secrets Manager)
- [ ] Add audit logging
- [ ] Implement CORS properly
- [ ] Add CSP headers
- [ ] Implement JWT with refresh tokens

**Security Features:**
```csharp
✅ API authentication (JWT + API keys)
✅ Role-based access control (RBAC)
✅ Secret encryption at rest
✅ TLS/HTTPS enforcement
✅ Rate limiting per user
✅ SQL injection prevention
✅ XSS prevention
✅ CSRF protection
```

**Tests:**
- [ ] Security tests
- [ ] Penetration testing
- [ ] Authentication tests
- [ ] Authorization tests

**Deliverables:**
- ✅ Security audit passed
- ✅ Sensitive data encrypted
- ✅ RBAC implemented

---

### 4.4 High Availability & Clustering (Week 25-26)

**Tasks:**
- [ ] Implement Akka.NET clustering
- [ ] Add cluster sharding for workflows
- [ ] Implement cluster singleton for scheduling
- [ ] Add distributed locking
- [ ] Implement graceful shutdown
- [ ] Add health-based routing

**Clustering:**
```csharp
✅ Akka.Cluster setup
✅ Cluster sharding
✅ Cluster singleton
✅ Split-brain resolver
✅ Cluster monitoring
```

**Tests:**
- [ ] Cluster formation tests
- [ ] Failover tests
- [ ] Split-brain tests
- [ ] Load distribution tests

**Deliverables:**
- ✅ Multiple nodes in cluster
- ✅ Workflows distributed
- ✅ Failover works automatically

---

### 4.5 Advanced Scheduling (Week 26)

**Tasks:**
- [ ] Implement cron-based scheduling (Quartz.NET)
- [ ] Add event-based triggers
- [ ] Implement workflow chaining
- [ ] Add calendar-based scheduling
- [ ] Implement priority queues
- [ ] Add workflow dependencies

**Scheduling:**
```csharp
✅ Cron expressions
✅ Event triggers
✅ Webhook triggers
✅ Schedule triggers
✅ Dependency triggers
✅ Manual triggers
```

**Tests:**
- [ ] Cron scheduling tests
- [ ] Event trigger tests
- [ ] Priority queue tests
- [ ] Dependency resolution tests

**Deliverables:**
- ✅ Cron scheduling working
- ✅ Event triggers working
- ✅ Dependencies resolved

---

### 4.6 Documentation & Training (Week 27)

**Tasks:**
- [ ] Write user documentation
- [ ] Create developer documentation
- [ ] Write module development guide
- [ ] Create video tutorials
- [ ] Write best practices guide
- [ ] Create sample workflow library
- [ ] Write deployment guide
- [ ] Create troubleshooting guide

**Documentation:**
```
✅ User Guide
  - Getting started
  - Creating workflows
  - Using modules
  - Writing scripts
  - Monitoring

✅ Developer Guide
  - Architecture
  - Creating modules
  - API reference
  - SDK usage
  - Contributing

✅ Operations Guide
  - Deployment
  - Configuration
  - Monitoring
  - Backup/restore
  - Troubleshooting
```

**Deliverables:**
- ✅ Complete documentation site
- ✅ Video tutorials published
- ✅ Sample workflow library

---

### 4.7 Deployment & DevOps (Week 27-28)

**Tasks:**
- [ ] Create Docker images
- [ ] Create Kubernetes manifests
- [ ] Create Helm charts
- [ ] Add database migration scripts
- [ ] Create deployment automation
- [ ] Implement blue-green deployment
- [ ] Add rollback procedures

**Deployment Options:**
```
✅ Docker Compose (dev)
✅ Kubernetes (production)
✅ Standalone (single server)
✅ Azure Container Apps
✅ AWS ECS/Fargate
```

**Tests:**
- [ ] Deployment tests
- [ ] Migration tests
- [ ] Rollback tests

**Deliverables:**
- ✅ Docker images published
- ✅ Kubernetes tested
- ✅ Deployment automation working

---

### 4.8 Testing & Quality Assurance (Week 28)

**Tasks:**
- [ ] Achieve 85%+ code coverage
- [ ] Implement integration test suite
- [ ] Add end-to-end test suite
- [ ] Implement load testing
- [ ] Add chaos testing
- [ ] Create test data generators

**Test Types:**
```
✅ Unit tests (85%+ coverage)
✅ Integration tests
✅ End-to-end tests
✅ Performance tests
✅ Load tests
✅ Chaos tests
✅ Security tests
```

**Tools:**
```
✅ xUnit for unit tests
✅ TestContainers for integration
✅ Playwright for E2E
✅ k6 for load testing
✅ Chaos Mesh for chaos testing
```

**Deliverables:**
- ✅ Comprehensive test suite
- ✅ All tests passing
- ✅ Coverage targets met

---

### 4.9 Launch Preparation (Week 28)

**Tasks:**
- [ ] Conduct beta testing
- [ ] Fix critical bugs
- [ ] Optimize performance
- [ ] Complete documentation
- [ ] Create marketing materials
- [ ] Set up support channels
- [ ] Prepare launch announcement

**Beta Testing:**
```
✅ 10+ beta users
✅ Feedback collected
✅ Critical issues fixed
✅ Performance validated
```

**Launch Checklist:**
```
✅ All features complete
✅ Documentation complete
✅ Performance targets met
✅ Security audit passed
✅ Load testing passed
✅ Support ready
✅ Monitoring operational
✅ Backup/DR tested
```

**Deliverables:**
- ✅ Production-ready release
- ✅ Documentation complete
- ✅ Support operational

---

### Phase 4 Success Criteria ✨

**Must Have:**
- [ ] Performance targets met
- [ ] Security audit passed
- [ ] HA clustering working
- [ ] Complete documentation
- [ ] 85%+ code coverage
- [ ] Production deployment ready
- [ ] **LAUNCH READY! 🎉**

---

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

