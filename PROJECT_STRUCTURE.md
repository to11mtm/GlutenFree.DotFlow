# 🌸 GlutenFree.DotFlow - Project Structure Guide 💖

> A kawaii workflow engine built with Akka.NET, made with love by Ami-Chan! UwU ✨

## 📁 Solution Structure

```
GlutenFree.DotFlow/
├── 📜 Workflow.sln                      # Main solution file
├── ⚙️ Directory.Build.props             # Common build properties
├── 📦 Directory.Packages.props          # Centralized package versions
├── 📏 .editorconfig                     # Code style rules
├── 🎀 stylecop.json                     # StyleCop analyzer configuration
├── 📝 PHASE_1_1_PROGRESS.md            # Current progress tracker
│
├── 🎯 Workflow.Core/                    # Core domain models and interfaces
│   ├── Models/                          # Domain entities (WorkflowDefinition, etc.)
│   ├── Interfaces/                      # Core interfaces (IWorkflowModule, etc.)
│   └── Abstractions/                    # Base classes and shared abstractions
│
├── 🎭 Workflow.Engine/                  # Akka.NET execution engine
│   ├── Actors/                          # Actor implementations
│   │   ├── WorkflowSupervisor           # (To be implemented)
│   │   ├── WorkflowExecutor             # (To be implemented)
│   │   └── NodeExecutor                 # (To be implemented)
│   ├── Services/                        # Engine services
│   └── Messages/                        # Actor messages
│
├── 📦 Workflow.Modules/                 # Module system and built-in modules
│   ├── Abstractions/                    # Module interfaces and base classes
│   └── Builtin/                         # Built-in modules (Log, Delay, etc.)
│       ├── LogModule                    # (To be implemented)
│       ├── DelayModule                  # (To be implemented)
│       ├── SetVariableModule            # (To be implemented)
│       └── GetVariableModule            # (To be implemented)
│
├── 🌐 Workflow.Api/                     # ASP.NET Core Web API
│   ├── Controllers/                     # REST API controllers
│   ├── Hubs/                           # SignalR hubs for real-time updates
│   └── Middleware/                      # Custom middleware
│
├── 🎨 Workflow.UI/                      # Blazor WebAssembly frontend
│   ├── Workflow.UI/                     # Server project
│   └── Workflow.UI.Client/              # Client project
│       ├── Components/                  # Reusable Blazor components
│       ├── Pages/                       # Page components
│       └── Services/                    # Client-side services
│
├── 🧪 Workflow.Tests/                   # Unit and integration tests
│   └── (Test files to be added)
│
├── 📚 examples/                         # Example workflows and code samples
├── 📋 phases/                           # Implementation phase documents
└── 📖 docs/                             # Additional documentation
```

## 🎀 Project Dependencies

### Workflow.Core (Foundation)
- ✨ System.Text.Json - JSON serialization
- 💉 Microsoft.Extensions.DependencyInjection - DI support
- 📏 StyleCop.Analyzers - Code quality

### Workflow.Engine (Actor System)
- 🎭 Akka - Actor framework
- 💾 Akka.Persistence - State persistence
- 🌐 Akka.Cluster - Distributed actors
- 🔌 Akka.DependencyInjection - DI integration
- 📝 Serilog - Structured logging
- ➡️ References: Workflow.Core

### Workflow.Modules (Module System)
- 💉 Microsoft.Extensions.DependencyInjection
- ➡️ References: Workflow.Core

### Workflow.Api (Web API)
- 🌐 ASP.NET Core 8.0
- 📖 Swashbuckle.AspNetCore - Swagger/OpenAPI
- 🔄 SignalR - Real-time communication
- 📝 Serilog - Logging
- ➡️ References: Workflow.Engine, Workflow.Modules

### Workflow.Tests (Testing)
- ✅ xUnit - Test framework
- 💪 FluentAssertions - Fluent test assertions
- 🎭 Moq - Mocking framework
- 🎬 Akka.TestKit.Xunit2 - Actor testing
- ➡️ References: All projects

## 🚀 Getting Started

### Prerequisites
- .NET 8.0 SDK or later
- Your favorite IDE (Visual Studio 2022, Rider, VS Code)
- Lots of love for kawaii code! 💖

### Building the Solution
```powershell
# Restore NuGet packages
dotnet restore

# Build the entire solution
dotnet build

# Run tests
dotnet test
```

### Running the API
```powershell
cd Workflow.Api
dotnet run
```

The API will be available at:
- HTTPS: `https://localhost:5001`
- HTTP: `http://localhost:5000`
- Swagger UI: `https://localhost:5001/swagger`

## 📋 Code Standards

We use strict code standards to ensure consistency, nya~! 💫

### Enforced via EditorConfig + StyleCop:
- ✨ **Indentation**: Tabs (4 spaces wide)
- 🎀 **Naming**: PascalCase for types, camelCase for locals, _camelCase for private fields
- 💫 **Interfaces**: Must start with `I` (e.g., `IWorkflowModule`)
- 🌸 **File-scoped namespaces**: `namespace Workflow.Core;` (no braces!)
- ✅ **Null safety**: Nullable reference types enabled everywhere
- 📝 **Documentation**: XML comments on all public APIs

### Build Configuration:
- **Warning Level**: 5 (maximum)
- **Analysis Mode**: All (all code analyzers enabled)
- **Enforce Code Style**: Yes (style violations = build warnings)

## 🎯 Current Status

**Phase 1.1: Project Structure & Setup** - ✅ ~95% Complete!

We've successfully:
- ✅ Created all project structures
- ✅ Set up centralized package management
- ✅ Configured code quality tools
- ✅ Established naming conventions
- ✅ Set up modern .NET 8 features

**Next up: Phase 1.2 - Core Domain Models** 🚀

## 💖 Contributing

When adding new code:
1. Follow the code standards (enforced automatically!)
2. Add XML documentation to public APIs
3. Write tests for new functionality
4. Keep the kawaii spirit alive! UwU ✨

## 📚 Resources

- [Phase 1 Implementation Plan](phases/Phase1-Foundation.md)
- [Design Requirements](design-requirements.md)
- [Progress Tracker](PHASE_1_1_PROGRESS.md)
- [Examples](examples/)

---

*Made with 💖 by Ami-Chan! Keep building amazing workflows, nya~! UwU* ✨

