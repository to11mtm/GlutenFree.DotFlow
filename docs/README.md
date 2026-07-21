# GlutenFree.DotFlow 🌊

> A **workflow automation & orchestration engine** for .NET — design workflows visually, run them
> on a resilient actor-based engine, script the tricky bits in JavaScript / C# / Lua, and watch it
> all happen live. Made with 💖 by Ami-Chan~ ✨

This is the documentation home. Start here to understand **what the project is**, **how to run it**,
and **where to read more**.

---

## What is it?

DotFlow lets you build **workflows** — directed graphs of **nodes** (each backed by a *module*) with
**connections** between them — then execute, monitor, and manage them.

- **⚙️ Execution engine** — an [Akka.NET](https://getakka.net/) actor-based engine that runs
  workflows node-by-node with retries, timeouts, error boundaries, loops, fan-out/parallel branches,
  and staged variable writes.
- **🧩 Modules** — the built-in library (HTTP, database, transforms, file, flow-control, logging, and
  a sandboxed **script** node) plus **custom modules** you package as `.wfmod` files and upload at
  runtime.
- **📜 Scripting** — a capability-gated, sandboxed scripting engine (**JavaScript** via Jint, **C#**
  via Roslyn, **Lua** via MoonSharp) with a unified `workflow.*` API (variables, logging, HTTP,
  files, utilities) — network/file access is **deny-by-default**.
- **🌐 REST API** — versioned `/api/v1/*` endpoints for workflows, executions, modules, variables,
  scripts, and monitoring, with **OpenAPI/Swagger** and API-key **or** JWT auth.
- **📡 Real-time hub** — a SignalR hub streaming execution & node lifecycle events to clients.
- **🖥️ Web UI** (Blazor WebAssembly) — a **visual designer**, a **Script Studio**, an **execution
  monitor**, and a **module manager**.
- **💾 Pluggable persistence** — SQLite, PostgreSQL, NATS, and S3 providers, individually or
  **composed** (e.g. workflows in Postgres, variables in NATS).

> **Status:** the engine, REST API, real-time hub, scripting, and the four web-UI areas are
> implemented and tested. **Client SDKs (C#/TS/Python)** are planned — see
> [`../phases/Phase3-7-ClientSDKs.md`](../phases/Phase3-7-ClientSDKs.md).

---

## Prerequisites

- **.NET 8 SDK** (the whole solution targets `net8.0`).
- Optional, only for non-default persistence: **PostgreSQL**, a **NATS** server, or an **S3**-compatible
  store. The default configuration uses **in-memory SQLite** and needs nothing extra.

---

## Quick start

### 1. Build & test

```bash
dotnet build Workflow.sln
dotnet test                     # runs the full test suite
```

### 2. Run the API

```bash
dotnet run --project Workflow.Api
```

- HTTP: **http://localhost:5213** · HTTPS: **https://localhost:7018**
- **Swagger UI:** `/swagger` · **OpenAPI doc:** `/swagger/v1/swagger.json`
- Default persistence is **SQLite `:memory:`** (fresh each run); auth is **off in Development**
  (`Api:Auth:Require=false`), so every endpoint is anonymous-friendly out of the box.

Try it:

```bash
# create a workflow, then execute it (see docs/rest-api.md for full payloads)
curl http://localhost:5213/api/v1/workflows
curl http://localhost:5213/api/v1/modules          # browse available modules
```

### 3. Run the Web UI (optional)

```bash
dotnet run --project Workflow.UI/Workflow.UI
```

- HTTP: **http://localhost:5277** · HTTPS: **https://localhost:7188**
- Point the UI at the API by setting `Api:BaseUrl` in
  `Workflow.UI/Workflow.UI.Client/wwwroot/appsettings.json` (e.g. `"https://localhost:7018"`), and
  allow the UI origin for the real-time hub via `Api:RealTime:AllowedOrigins` in the API's config.
- UI routes: `/` (workflow list) · `/designer/{id}` (visual designer) · `/scripts` (Script Studio) ·
  `/monitor` (execution monitor) · `/modules` (module manager) · `/settings`.

### Run both together (recommended) 🚀

The API and UI are **two processes** that must run at the same time **on the same scheme** (both
`http`, or both `https`) — otherwise the browser blocks the UI's cross-origin/mixed-content calls to
the API. Convenience launchers start both and stop both on a single **Ctrl-C**:

| Platform | Command |
| --- | --- |
| Windows (PowerShell) | `./run-dev.ps1` |
| Windows (cmd) | `run-dev.cmd` |
| Linux / macOS | `./run-dev.sh` |

```bash
./run-dev.sh              # http  → API :5213, UI :5277  (open http://localhost:5277)
./run-dev.sh https        # https → API :7018, UI :7188
```

The repo ships pre-wired for the **http** pairing (`Api:BaseUrl = http://localhost:5213`, and the UI
origins are in the API's `Api:RealTime:AllowedOrigins`). For **https**, also set `Api:BaseUrl` to
`https://localhost:7018` in `Workflow.UI/Workflow.UI.Client/wwwroot/appsettings.json`.

> **Seeing `Failed to fetch ... blazor-hotreload.js`?** That's a stale browser tab cached from a
> previous IDE "Run" or `dotnet watch` (hot-reload) session. Hard-refresh (**Ctrl-Shift-R**) or open a
> new tab. The launcher scripts strip hot-reload env vars so a plain run never injects it.

> **Running from Visual Studio / Rider:** set **both** projects' launch profile to **http** (not the
> default `https`) and start them together, or just use the launcher scripts above.

---

## Configuration

Configured via `Workflow.Api/appsettings*.json` (and environment variables). The main sections:

| Section | Purpose |
|---------|---------|
| `Persistence:Provider` | `sqlite` (default), `postgres`, `nats`, or `composite` |
| `Persistence:ConnectionString` | provider connection string (default `:memory:`) |
| `Persistence:Composite` | per-concern providers (Workflows / ExecutionHistory / Variables) |
| `Api:Auth:Require` | `false` in dev (anonymous); set `true` to enforce policies |
| `Api:Auth:ApiKeys` | hashed API keys for the `X-API-Key` header |
| `Api:Auth:Jwt` | JWT bearer validation settings |
| `Api:RealTime:AllowedOrigins` | CORS origins allowed to connect to the SignalR hub |
| `Scripting` | host ceilings for the script sandbox (timeout/memory/network/file) |

See [`rest-api.md`](rest-api.md) for auth details and [`scripting.md`](scripting.md) for the sandbox.

---

## Project layout

```text
Workflow.Core            domain models (workflows, nodes, connections, variables)
Workflow.Engine          Akka.NET execution engine (executors, error boundaries, loops)
Workflow.Modules*        module abstractions + built-ins (HTTP, database, transform, …)
Workflow.Scripting*      scripting engine + JS/C#/Lua executors + libraries
Workflow.Persistence*    persistence abstractions + SQLite/Postgres/NATS/S3 providers
Workflow.Api             the REST API + SignalR hub + OpenAPI (the app you run)
Workflow.UI              Blazor Web App host  ─┐  the web UI
Workflow.UI.Client       Blazor WebAssembly    ─┘  (designer, script studio, monitor, modules)
Workflow.Tests*          xUnit test suites (unit, integration, UI/bUnit)
docs/                    this documentation
phases/                  the phased build roadmap (design breakdowns + status)
```

---

## Documentation index

**Using the API & real-time**
- [REST API](rest-api.md) — endpoints, auth, pagination, errors, OpenAPI
- [Real-Time Hub](realtime.md) — SignalR connection, subscriptions, events

**The web UI**
- [Visual Designer](designer.md) — building workflows on the canvas
- [Designer Architecture & React-Port Guide](designer-architecture.md) — the framework-free boundary
- [Script Studio](script-studio.md) — writing/testing/managing scripts
- [Execution Monitor](execution-monitor.md) — watching runs live + replay
- [Module Manager](module-manager.md) — browse/upload/enable/uninstall modules

**Scripting & modules**
- [Scripting Engine](scripting.md) — languages, the `workflow.*` API, the sandbox, libraries
- [Module Author Guide](module-author-guide.md) — building & packaging a `.wfmod` module
- [Database Modules](database-modules.md) · [HTTP & Network](http-and-network.md) · [File Modules](file-modules.md) · [Transform Modules](transform-modules.md) · [Advanced Flow Control](advanced-flow-control.md)

**Roadmap**
- [`../phases/README.md`](../phases/README.md) — the phased build plan and per-phase status

---

*Made with 💖 by Ami-Chan! Happy orchestrating~ UwU* ✨
