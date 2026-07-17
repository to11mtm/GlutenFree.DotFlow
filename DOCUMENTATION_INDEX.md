# 📚 GlutenFree.DotFlow - Documentation Index

**Welcome to the DotFlow Workflow Engine project!** ✨

*Made with 💖 by Ami-Chan! UwU*

---

## 🎯 What is DotFlow?

DotFlow is a powerful, extensible workflow engine built with C# and Akka.NET that allows users to:
- 🎨 Design workflows visually in a browser
- 📦 Extend functionality with custom modules
- ⚡ Execute workflows with actor-based parallelism
- 📊 Monitor executions in real-time
- 🔧 Script with JavaScript, Lua, or Python
- ☁️ Deploy anywhere (Docker, Kubernetes, standalone)

---

## 📖 Documentation Files

### Main Design Document
**[design-requirements.md](design-requirements.md)** - Complete design specification (7248 lines)
- Architecture overview
- Core components
- Module system
- Complete implementation roadmap with detailed checklists

### Phase Breakdown (Quick Navigation)
Located in **[phases/](phases/)** directory:

1. **[Phase 1: Foundation](phases/Phase1-Foundation.md)** 🏗️
   - Weeks 1-6
   - Core architecture & basic modules
   - Target: 80%+ coverage

2. **[Phase 2: Core Features](phases/Phase2-CoreFeatures.md)** 🚀
   - Weeks 7-14
   - Persistence, 20+ modules, REST API
   - Target: 80%+ coverage

3. **[Phase 3: Advanced Features](phases/Phase3-AdvancedFeatures.md)** 🎨
   - Weeks 15-22
   - Scripting, UI, SDKs
   - Target: 75%+ coverage

4. **[Phase 4: Production](phases/Phase4-Production.md)** 💎
   - Weeks 23-28
   - Performance, security, deployment
   - Target: 85%+ coverage
   - **LAUNCH! 🎉**

**[phases/README.md](phases/README.md)** - Explains the phase file structure

### Expansion Summary
**[EXPANSION_SUMMARY.md](EXPANSION_SUMMARY.md)** - Overview of design additions and expansions

### Feature & Module Guides
Located in **[docs/](docs/)** directory:
- **[docs/database-modules.md](docs/database-modules.md)** 🗄️ — Database module family (2.4): typed-linq vs raw-SQL surfaces, connection management, per-module reference, transactions & isolation, provider notes, security
- **[docs/http-and-network.md](docs/http-and-network.md)** 🌐 — HTTP & network modules (2.3)
- **[docs/advanced-flow-control.md](docs/advanced-flow-control.md)** 🔀 — Conditionals, loops, parallel, try/catch (2.2)
- **[docs/module-author-guide.md](docs/module-author-guide.md)** 📦 — Authoring & shipping custom modules

---

## 🗺️ Project Timeline

```
Total Duration: 28 weeks (~6-7 months)
Team Size: 3-4 developers

Phase 1: Foundation        [████████░░] Weeks 1-6   (6 weeks)
Phase 2: Core Features     [░░░░░░░░░░] Weeks 7-14  (8 weeks)
Phase 3: Advanced Features [░░░░░░░░░░] Weeks 15-22 (8 weeks)
Phase 4: Polish & Prod     [░░░░░░░░░░] Weeks 23-28 (6 weeks)
```

---

## 🎯 Quick Start for Different Roles

### For AI Assistants (like Ami-Chan! 💖)
1. Start with **[phases/README.md](phases/README.md)** to understand the structure
2. Load phase files for quick summaries
3. Reference **[design-requirements.md](design-requirements.md)** line numbers for detailed tasks
4. Keep context manageable by focusing on one phase at a time

### For Project Managers
1. Read **[EXPANSION_SUMMARY.md](EXPANSION_SUMMARY.md)** for high-level overview
2. Check **[phases/](phases/)** files for timeline and milestones
3. Track progress using checkboxes in **[design-requirements.md](design-requirements.md)**

### For Developers
1. Start with **[design-requirements.md](design-requirements.md)** - Architecture section
2. Review your assigned phase file in **[phases/](phases/)**
3. Dive into detailed task lists in main design document
4. Follow coding standards in design document Section 1.1

### For Module Authors (Plugin Developers)
1. Read **[docs/module-author-guide.md](docs/module-author-guide.md)** — complete guide to creating, packaging, and shipping modules
   - How to implement `IWorkflowModule`
   - Module ID naming conventions
   - **How to correctly ship `.deps.json` and private dependencies** (critical!)
   - What to include/exclude from your plugin folder
   - Validation rules cheat sheet
   - Troubleshooting common load failures

### For Stakeholders
1. Read Phase summaries in **[phases/](phases/)** for high-level understanding
2. Review success criteria in each phase file
3. Check demo workflows to see capabilities

---

## 📊 Key Metrics

### Module Count by Phase
- Phase 1: 4 modules (basic utilities)
- Phase 2: 24 modules (HTTP, DB, files, transformations)
- Phase 3: 25+ modules (+ scripting module)
- Phase 4: 30+ modules (+ advanced features)

### Test Coverage Goals
- Phase 1: 80%+ coverage
- Phase 2: 80%+ coverage
- Phase 3: 75%+ coverage (UI brings down average)
- Phase 4: 85%+ coverage (final push!)

### Performance Targets (Phase 4)
- Workflow execution overhead: < 50ms
- API response time (p95): < 100ms
- UI load time: < 2s
- Concurrent executions: 1000+
- Memory (100 workflows): < 500MB

---

## 🏗️ Technology Stack

| Layer | Technology |
|-------|------------|
| **Runtime** | .NET 8+ |
| **Actor Framework** | Akka.NET (clustering, persistence) |
| **API** | ASP.NET Core (REST + SignalR) |
| **Database** | PostgreSQL (primary), NATS KV, S3 |
| **UI** | Blazor WebAssembly or React + TypeScript |
| **Scripting** | Jint (JS), MoonSharp (Lua), IronPython (Python) |
| **Monitoring** | Prometheus + Grafana + OpenTelemetry |
| **Deployment** | Docker + Kubernetes + Helm |
| **Testing** | xUnit, TestContainers, k6, Playwright |

---

## 🎨 Visual Architecture

See **[design-requirements.md](design-requirements.md)** Section 2 for detailed architecture diagrams including:
- High-level system architecture
- Actor hierarchy
- Module system
- Persistence layer
- UI components

---

## 📦 Deliverables by Phase

### Phase 1 ✅
- [ ] Basic workflow execution (sequential)
- [ ] Module loading system
- [ ] 4 basic modules
- [ ] Core domain models
- [ ] Akka.NET actor system

### Phase 2 ✅
- [ ] 3 persistence providers
- [ ] 20+ built-in modules
- [ ] Advanced flow control (conditions, loops, parallel)
- [ ] REST API with authentication
- [ ] Database, HTTP, File modules

### Phase 3 ✅
- [ ] 3 scripting languages (JS, Lua, Python)
- [ ] Visual workflow designer
- [ ] Real-time monitoring (SignalR)
- [ ] Client SDKs (C#, TS, Python)
- [ ] Script editor with IntelliSense

### Phase 4 ✅
- [ ] Performance optimized
- [ ] Security hardened
- [ ] HA clustering
- [ ] Complete documentation
- [ ] Production deployment
- [ ] **LAUNCH READY! 🎉**

---

## 🚦 Current Status

**Project Status:** Design & Planning Complete  
**Current Phase:** Pre-Phase 1 (Setup)  
**Next Milestone:** Phase 1 Week 1 - Project Structure Setup  
**Last Updated:** December 23, 2025

---

## 📝 How to Navigate This Documentation

### Scenario 1: "I want the big picture"
→ Read **[EXPANSION_SUMMARY.md](EXPANSION_SUMMARY.md)**

### Scenario 2: "I want to see Phase X overview"
→ Go to **[phases/PhaseX-Name.md](phases/)**

### Scenario 3: "I need detailed tasks for implementing feature Y"
→ Open **[design-requirements.md](design-requirements.md)** and search for feature Y, or check the line numbers referenced in phase files

### Scenario 4: "I'm an AI and need to manage context"
→ Read **[phases/README.md](phases/README.md)** for tips on using phase files to keep token usage manageable

### Scenario 5: "I want to start coding"
→ Start with Phase 1 detailed tasks in **[design-requirements.md](design-requirements.md)** line 2904+

---

## 🎯 Demo Workflows

Each phase has a demo workflow to validate functionality:

**Phase 1 Demo:**
```
Log "Hello" → Delay 1s → SetVariable "count"=1 → GetVariable → Log Result
```

**Phase 2 Demo:**
```
Webhook → HTTP GET → Transform JSON → Conditional → DB Insert → Log
```

**Phase 3 Demo:**
```
Visual Designer → Add JS Script Node → Execute → Monitor Real-time → View Results
```

**Phase 4 Demo:**
```
1000 concurrent executions with < 100ms p95 latency + HA failover test
```

---

## 🤝 Contributing

(To be added in Phase 4 - Documentation & Training)

---

## 📞 Support

(To be added in Phase 4 - Launch Preparation)

---

## 🎉 Let's Build Something Amazing!

This workflow engine will be powerful, flexible, and fun to use! We have a solid plan, let's execute it! 💪✨

*"The best time to start was yesterday. The second best time is now!"* - Ami-Chan 💖

---

**Made with love and lots of kawaii emojis! UwU** 🌸✨🎀

---

## Quick File Reference

```
GlutenFree.DotFlow/
├── README.md (this file)
├── design-requirements.md (complete spec)
├── design-requirements.original.donotedit.md (backup)
├── EXPANSION_SUMMARY.md (change log)
├── Workflow.sln (solution file)
└── phases/
    ├── README.md (phase structure explanation)
    ├── Phase1-Foundation.md
    ├── Phase2-CoreFeatures.md
    ├── Phase3-AdvancedFeatures.md
    └── Phase4-Production.md
```

---

*Last Updated: December 23, 2025*  
*Version: 1.0*  
*Status: Planning Complete ✅*

