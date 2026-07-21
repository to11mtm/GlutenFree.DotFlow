# 📦 Code Examples Directory

Nya~ Welcome to the code examples directory! ✨ This folder contains all the code snippets and examples referenced in the design-requirements.md document, organized by category for easy reference~ 💖

## 📁 Directory Structure

```
examples/
├── actors/                    # 🎭 Akka.NET Actor examples
│   ├── WorkflowCoordinatorActor.cs
│   ├── WorkflowInstanceActor.cs
│   ├── NodeActor.cs
│   └── NodeConfiguration.cs
│
├── modules/                   # 📦 Module system examples
│   ├── IWorkflowModule.cs
│   ├── IModuleRegistry.cs
│   ├── HttpModule.cs
│   ├── DatabaseModule.cs
│   ├── FileModule.cs
│   ├── ModuleSecurityConfig.cs
│   └── IWorkflowPlugin.cs
│
├── scripting/                 # 📜 Scripting system examples
│   ├── ScriptModule.cs
│   ├── IWorkflowScriptApi.cs
│   ├── ScriptExecutors.cs
│   ├── ScriptExecutionConfig.cs
│   ├── IScriptLibrary.cs
│   ├── javascript-data-transformation.js
│   ├── javascript-api-integration.js
│   ├── lua-data-processing.lua
│   ├── lua-csv-processing.lua
│   ├── python-data-analysis.py
│   └── python-database-etl.py
│
├── api/                       # 🌐 REST API examples
│   ├── WorkflowsController.cs
│   ├── ModulesController.cs
│   ├── ApiModels.cs
│   ├── WorkflowHub.cs
│   └── ScriptTestingController.cs
│
├── clients/                   # 💎 Client SDK examples
│   ├── WorkflowClient.cs      # C# client
│   ├── WorkflowClient.ts      # TypeScript client
│   └── WorkflowClient.py      # Python client
│
└── definitions/               # 📋 Workflow definition examples
    ├── WorkflowDefinition.cs
    ├── example-workflow.json
    └── module-manifest.json
```

## 🎯 Quick Reference

### Core Actor System
- **[WorkflowCoordinatorActor.cs](actors/WorkflowCoordinatorActor.cs)** - Main orchestrator that manages workflow lifecycle
- **[WorkflowInstanceActor.cs](actors/WorkflowInstanceActor.cs)** - Represents a single workflow execution
- **[NodeActor.cs](actors/NodeActor.cs)** - Executes individual workflow nodes
- **[NodeConfiguration.cs](actors/NodeConfiguration.cs)** - Configuration & messages (ExecuteNode, NodeExecutionResult, etc.)
- **[DESIGN_RATIONALE.md](actors/DESIGN_RATIONALE.md)** - 📚 Explains configuration vs. messages pattern

### Module System
- **[IWorkflowModule.cs](modules/IWorkflowModule.cs)** - Base interface for all workflow modules
- **[IModuleRegistry.cs](modules/IModuleRegistry.cs)** - Module discovery and loading system
- **[HttpModule.cs](modules/HttpModule.cs)** - HTTP request module example
- **[DatabaseModule.cs](modules/DatabaseModule.cs)** - Database query module example
- **[FileModule.cs](modules/FileModule.cs)** - File operations module example
- **[docs/module-author-guide.md](../docs/module-author-guide.md)** - 📖 Complete guide: implementing, packaging, and shipping modules (includes `.deps.json` requirements!)

### Scripting Support
- **[ScriptModule.cs](scripting/ScriptModule.cs)** - Script execution module
- **[IWorkflowScriptApi.cs](scripting/IWorkflowScriptApi.cs)** - API exposed to scripts
- **[ScriptExecutors.cs](scripting/ScriptExecutors.cs)** - Language-specific executors (JS, Lua, Python)

#### Script Examples
- **JavaScript:**
  - [Data Transformation](scripting/javascript-data-transformation.js)
  - [API Integration](scripting/javascript-api-integration.js)
- **Lua:**
  - [Data Processing](scripting/lua-data-processing.lua)
  - [CSV Processing](scripting/lua-csv-processing.lua)
- **Python:**
  - [Data Analysis](scripting/python-data-analysis.py)
  - [Database ETL](scripting/python-database-etl.py)

### REST API
- **[WorkflowsController.cs](api/WorkflowsController.cs)** - Workflow management endpoints
- **[ModulesController.cs](api/ModulesController.cs)** - Module management endpoints
- **[ApiModels.cs](api/ApiModels.cs)** - Request/response models
- **[WorkflowHub.cs](api/WorkflowHub.cs)** - SignalR real-time hub
- **[ScriptTestingController.cs](api/ScriptTestingController.cs)** - Script testing endpoint

### Client SDKs
- **[WorkflowClient.cs](clients/WorkflowClient.cs)** - C# client SDK
- **[WorkflowClient.ts](clients/WorkflowClient.ts)** - TypeScript/JavaScript client SDK
- **[WorkflowClient.py](clients/WorkflowClient.py)** - Python client SDK

### Workflow Definitions
- **[WorkflowDefinition.cs](definitions/WorkflowDefinition.cs)** - Workflow model classes
- **[example-workflow.json](definitions/example-workflow.json)** - Sample workflow JSON
- **[module-manifest.json](definitions/module-manifest.json)** - Module package manifest

## 💡 Usage Tips

### For Developers
1. **Building Modules** - Start with `IWorkflowModule.cs` and refer to the built-in module examples
2. **Shipping Modules** - Read **[docs/module-author-guide.md](../docs/module-author-guide.md)** for the complete packaging guide including `.deps.json` requirements
3. **API Integration** - Check the controller examples for REST API implementation patterns
4. **Scripting** - Use the script examples as templates for your workflows

### For Script Authors
- JavaScript examples show how to use the workflow API for data transformation
- Lua examples demonstrate file and CSV processing
- Python examples showcase data analysis and ETL patterns

### For API Consumers
- Client SDK examples show how to integrate with the workflow engine from external applications
- Available in C#, TypeScript/JavaScript, and Python

## 🌸 Contributing

When adding new examples:
1. Place them in the appropriate category folder
2. Use descriptive filenames
3. Include kawaii comments and documentation! ✨
4. Update this README with references to new examples

## 📚 Related Documentation

- **[design-requirements.md](../design-requirements.md)** - Main design document
- **[Phase 1: Foundation](../phases/Phase1-Foundation.md)** - Implementation roadmap phase 1
- **[Phase 2: Core Features](../phases/Phase2-CoreFeatures.md)** - Implementation roadmap phase 2
- **[Phase 3: Advanced Features](../phases/Phase3-AdvancedFeatures.md)** - Implementation roadmap phase 3
- **[Phase 4: Production](../phases/Phase4-Production.md)** - Implementation roadmap phase 4

---

*Made with 💖 by Ami-Chan! Keep these examples as reference while building the most kawaii workflow engine ever~ UwU* ✨

