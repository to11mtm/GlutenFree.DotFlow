# 🗺️ GlutenFree.DotFlow - Documentation Index

Welcome to GlutenFree.DotFlow! This is your complete guide to understanding and building this kawaii workflow engine~ ✨

## 📚 Main Documentation

### 🌸 [design-requirements.md](design-requirements.md)
The master design document containing:
- Architecture overview
- Technology stack
- Component descriptions
- UI mockups and requirements
- Security considerations
- Implementation roadmap overview

> 💡 **Note:** Code examples have been extracted to the `examples/` directory (see below)

## 🗂️ Phase Roadmaps

Detailed implementation phases with week-by-week checklists:

### 📁 [phases/](phases/)
- **[Phase 1: Foundation](phases/Phase1-Foundation.md)** (Weeks 1-6)
  - Core architecture & basic modules
  - Akka.NET actor system
  - Module loading system
  
- **[Phase 2: Core Features](phases/Phase2-CoreFeatures.md)** (Weeks 7-14)
  - Persistence providers
  - 20+ built-in modules
  - REST API & webhooks
  
- **[Phase 3: Advanced Features](phases/Phase3-AdvancedFeatures.md)** (Weeks 15-22)
  - Multi-language scripting (JS/Lua/Python)
  - Visual workflow designer
  - Real-time monitoring
  - Client SDKs
  
- **[Phase 4: Production](phases/Phase4-Production.md)** (Weeks 23-28)
  - Performance optimization
  - Security hardening
  - High availability
  - Launch prep! 🚀

## 📦 Code Examples

All code snippets organized by category for easy reference:

### 📁 [examples/](examples/)

#### 🎭 Actors
- [WorkflowCoordinatorActor.cs](examples/actors/WorkflowCoordinatorActor.cs) - Main orchestrator
- [WorkflowInstanceActor.cs](examples/actors/WorkflowInstanceActor.cs) - Single workflow execution
- [NodeActor.cs](examples/actors/NodeActor.cs) - Individual node executor

#### 📦 Modules
- [IWorkflowModule.cs](examples/modules/IWorkflowModule.cs) - Module interface & schema
- [IModuleRegistry.cs](examples/modules/IModuleRegistry.cs) - Module loading system
- [HttpModule.cs](examples/modules/HttpModule.cs) - HTTP requests module
- [DatabaseModule.cs](examples/modules/DatabaseModule.cs) - Database operations
- [FileModule.cs](examples/modules/FileModule.cs) - File I/O operations
- [ModuleSecurityConfig.cs](examples/modules/ModuleSecurityConfig.cs) - Security settings
- [IWorkflowPlugin.cs](examples/modules/IWorkflowPlugin.cs) - Plugin architecture

#### 📜 Scripting
- [ScriptModule.cs](examples/scripting/ScriptModule.cs) - Script execution module
- [IWorkflowScriptApi.cs](examples/scripting/IWorkflowScriptApi.cs) - API for scripts
- [ScriptExecutors.cs](examples/scripting/ScriptExecutors.cs) - Language executors
- [ScriptExecutionConfig.cs](examples/scripting/ScriptExecutionConfig.cs) - Security config
- [IScriptLibrary.cs](examples/scripting/IScriptLibrary.cs) - Reusable script libraries

**Script Examples:**
- JavaScript: [Data Transformation](examples/scripting/javascript-data-transformation.js), [API Integration](examples/scripting/javascript-api-integration.js)
- Lua: [Data Processing](examples/scripting/lua-data-processing.lua), [CSV Processing](examples/scripting/lua-csv-processing.lua)
- Python: [Data Analysis](examples/scripting/python-data-analysis.py), [Database ETL](examples/scripting/python-database-etl.py)

#### 🌐 REST API
- [WorkflowsController.cs](examples/api/WorkflowsController.cs) - Workflow management endpoints
- [ModulesController.cs](examples/api/ModulesController.cs) - Module management
- [ScriptTestingController.cs](examples/api/ScriptTestingController.cs) - Script testing
- [ApiModels.cs](examples/api/ApiModels.cs) - Request/response models
- [WorkflowHub.cs](examples/api/WorkflowHub.cs) - SignalR real-time hub

#### 💎 Client SDKs
- [WorkflowClient.cs](examples/clients/WorkflowClient.cs) - C# client SDK
- [WorkflowClient.ts](examples/clients/WorkflowClient.ts) - TypeScript/JavaScript SDK
- [WorkflowClient.py](examples/clients/WorkflowClient.py) - Python client SDK

#### 📋 Definitions
- [WorkflowDefinition.cs](examples/definitions/WorkflowDefinition.cs) - Workflow model
- [example-workflow.json](examples/definitions/example-workflow.json) - Sample workflow
- [module-manifest.json](examples/definitions/module-manifest.json) - Module package manifest

## 🎯 Quick Start Guides

### For Developers Building the Engine
1. Read [design-requirements.md](design-requirements.md) for architecture overview
2. Follow [Phase 1: Foundation](phases/Phase1-Foundation.md) to start implementation
3. Reference code examples in [examples/](examples/) as you build

### For Module Authors
1. Study [IWorkflowModule.cs](examples/modules/IWorkflowModule.cs) interface
2. Look at built-in modules for examples:
   - [HttpModule.cs](examples/modules/HttpModule.cs)
   - [DatabaseModule.cs](examples/modules/DatabaseModule.cs)
   - [FileModule.cs](examples/modules/FileModule.cs)
3. Review [module-manifest.json](examples/definitions/module-manifest.json) for packaging

### For Script Writers
1. Check the [IWorkflowScriptApi.cs](examples/scripting/IWorkflowScriptApi.cs) for available functions
2. Browse script examples by language:
   - **JavaScript:** [Data Transformation](examples/scripting/javascript-data-transformation.js)
   - **Lua:** [Data Processing](examples/scripting/lua-data-processing.lua)
   - **Python:** [Data Analysis](examples/scripting/python-data-analysis.py)

### For API Consumers
1. Review API controllers:
   - [WorkflowsController.cs](examples/api/WorkflowsController.cs)
   - [ApiModels.cs](examples/api/ApiModels.cs)
2. Use client SDKs:
   - [C# SDK](examples/clients/WorkflowClient.cs)
   - [TypeScript SDK](examples/clients/WorkflowClient.ts)
   - [Python SDK](examples/clients/WorkflowClient.py)

## 📊 Project Status

```
Current Phase: Planning & Design ✨
Timeline: 23-28 weeks (~6 months)
Team Size: 3-4 developers recommended

Phase 1: Foundation        [░░░░░░░░░░] 0% - Not started
Phase 2: Core Features     [░░░░░░░░░░] 0% - Not started
Phase 3: Advanced Features [░░░░░░░░░░] 0% - Not started
Phase 4: Production        [░░░░░░░░░░] 0% - Not started
```

## 🛠️ Technology Stack

| Component | Technology |
|-----------|------------|
| Runtime | .NET 8+ |
| Actor Framework | Akka.NET |
| UI Framework | Blazor (Server/WASM) or MAUI |
| Database | PostgreSQL / SQL Server |
| Module Loading | AssemblyLoadContext |
| API | ASP.NET Core / gRPC |
| Scripting | Jint (JS), MoonSharp (Lua), IronPython/Python.NET |
| Real-time | SignalR |

## 📝 Contributing

When adding to this project:
1. Follow the phase roadmaps for feature implementation
2. Add code examples to the `examples/` directory
3. Update relevant documentation
4. Include cute comments and emojis! ✨

## 🎀 Additional Resources

- **[DOCUMENTATION_INDEX.md](DOCUMENTATION_INDEX.md)** - This file!
- **[README.md](README.md)** - Project overview
- **[examples/README.md](examples/README.md)** - Detailed examples guide

---

*Made with 💖 by Ami-Chan for the most kawaii workflow engine ever! UwU* ✨

**Happy coding, senpai~!** 🌸

