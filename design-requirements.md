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

📄 **Example:** [WorkflowCoordinatorActor.cs](examples/actors/WorkflowCoordinatorActor.cs)

### 2. Workflow Instance Actor
Represents a single workflow execution.

📄 **Example:** [WorkflowInstanceActor.cs](examples/actors/WorkflowInstanceActor.cs)

### 3. Node Actor
Executes individual workflow nodes.

📄 **Example:** [NodeActor.cs](examples/actors/NodeActor.cs)

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

Execute custom scripts in various languages! Perfect for quick transformations and custom logic, uwu~

📄 **Example:** [ScriptModule.cs](examples/scripting/ScriptModule.cs)

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

**📄 Complete Controller Implementations:**
- **[WorkflowsController.cs](examples/api/WorkflowsController.cs)** - Workflow management & execution
- **[ModulesController.cs](examples/api/ModulesController.cs)** - Module management
- **[VariablesController.cs](examples/api/VariablesController.cs)** - Variables & secrets
- **[MonitoringController.cs](examples/api/MonitoringController.cs)** - Health & metrics
- **[WebhooksController.cs](examples/api/WebhooksController.cs)** - Webhook triggers

**Key API Endpoints:**
```
# Workflow Management
GET    /api/v1/workflows              # List workflows
GET    /api/v1/workflows/{id}         # Get workflow
POST   /api/v1/workflows              # Create workflow
PUT    /api/v1/workflows/{id}         # Update workflow
DELETE /api/v1/workflows/{id}         # Delete workflow

# Execution
POST   /api/v1/workflows/{id}/execute       # Execute workflow
POST   /api/v1/workflows/execute/{name}     # Execute by name
GET    /api/v1/workflows/executions/{id}    # Get status
POST   /api/v1/workflows/executions/{id}/cancel  # Cancel
POST   /api/v1/workflows/executions/{id}/pause   # Pause
POST   /api/v1/workflows/executions/{id}/resume  # Resume
```

### API Request/Response Models

📄 **[ApiModels.cs](examples/api/ApiModels.cs)** - Complete request/response models including:
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
### SignalR Real-Time Hub

📄 **[WorkflowHub.cs](examples/api/WorkflowHub.cs)** - Real-time workflow monitoring

Provides real-time updates via SignalR:
- Subscribe to specific execution updates
- Subscribe to all workflow executions
- Events: ExecutionStarted, ExecutionCompleted, ExecutionFailed, NodeStarted, NodeCompleted, ExecutionProgress

### Webhook Integration

📄 **[WebhooksController.cs](examples/api/WebhooksController.cs)** - Webhook triggers

Trigger workflows via HTTP webhooks with configurable:
- HTTP methods (GET, POST, PUT, DELETE)
- Secret validation
- Header validation
- Signature verification

### Client SDK Examples

All client SDKs are available in the examples directory with full implementations and usage examples:

📄 **[WorkflowClient.cs](examples/clients/WorkflowClient.cs)** - C# client SDK with async/sync execution, status polling, and error handling

📄 **[WorkflowClient.ts](examples/clients/WorkflowClient.ts)** - TypeScript/JavaScript SDK with SignalR real-time support

📄 **[WorkflowClient.py](examples/clients/WorkflowClient.py)** - Python client SDK with synchronous and asynchronous execution
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

