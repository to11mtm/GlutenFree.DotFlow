# GlutenFree.DotFlow 🌊

A **workflow automation & orchestration engine** for .NET 8 — design workflows visually, run them on
a resilient actor-based engine, script the tricky bits in JavaScript / C# / Lua, and watch it all
happen live.

## Highlights

- **⚙️ Actor-based execution engine** ([Akka.NET](https://getakka.net/)) — runs workflows node-by-node
  with retries, timeouts, error boundaries, loops, and parallel/fan-out branches.
- **🧩 Modules** — built-ins (HTTP, database, transforms, file, flow control, logging, a sandboxed
  script node) plus **custom `.wfmod` packages** you upload at runtime.
- **📜 Sandboxed scripting** — **JavaScript** (Jint), **C#** (Roslyn), and **Lua** (MoonSharp) with a
  unified `workflow.*` API; network/file access is **deny-by-default**.
- **🌐 REST API + OpenAPI** — versioned `/api/v1/*` endpoints with Swagger and API-key **or** JWT auth.
- **📡 Real-time hub** — SignalR streaming of execution & node lifecycle events.
- **🖥️ Blazor WebAssembly UI** — a visual **designer**, **Script Studio**, **execution monitor**, and
  **module manager**.
- **💾 Pluggable persistence** — SQLite, PostgreSQL, NATS, and S3, individually or **composed**.

## Quick start

Requires the **.NET 8 SDK**. The default config uses in-memory SQLite, so nothing else is needed.

```bash
dotnet build Workflow.sln
dotnet test                        # full test suite
```

### Running locally

The app is **two processes** — the REST API (`Workflow.Api`) and the Blazor web UI
(`Workflow.UI/Workflow.UI`). They must run at the same time **on the same scheme** (both `http` or
both `https`) or the browser will block the UI's calls to the API. The launcher scripts start both
and stop both on a single **Ctrl-C**:

| Platform | Command | |
| --- | --- | --- |
| Windows (PowerShell) | `./run-dev.ps1` | `./run-dev.ps1 https` |
| Windows (cmd) | `run-dev.cmd` | `run-dev.cmd https` |
| Linux / macOS | `./run-dev.sh` | `./run-dev.sh https` |

Then open **http://localhost:5277** (UI); the API is on **http://localhost:5213** (Swagger at
`/swagger`). See the [docs quick start](docs/README.md#quick-start) for the https pairing, IDE tips,
and configuration.

Prefer to run them by hand (two terminals)? Use the **`http`** launch profile for both so the schemes
match:

```bash
dotnet run --project Workflow.Api --launch-profile http                 # → http://localhost:5213
dotnet run --project Workflow.UI/Workflow.UI --launch-profile http      # → http://localhost:5277
```

## Documentation

Full docs live in **[`docs/`](docs/README.md)** — start with the
**[documentation home](docs/README.md)** for what it is, how to run it, and configuration, then dive
into the [REST API](docs/rest-api.md), [Scripting Engine](docs/scripting.md),
[Visual Designer](docs/designer.md), [Real-Time Hub](docs/realtime.md), and
[Module Author Guide](docs/module-author-guide.md).

The phased build plan and per-phase status are in **[`phases/`](phases/README.md)**.

## Project layout

```text
Workflow.Core          domain models (workflows, nodes, connections, variables)
Workflow.Engine        Akka.NET execution engine
Workflow.Modules*      module abstractions + built-ins
Workflow.Scripting*    scripting engine + JS/C#/Lua executors
Workflow.Persistence*  persistence abstractions + SQLite/Postgres/NATS/S3
Workflow.Api           the REST API + SignalR hub + OpenAPI (the app you run)
Workflow.UI(.Client)   the Blazor WebAssembly web UI
Workflow.Tests*        xUnit + bUnit test suites
```

## License

See the repository for license details.

---

*Made with 💖 by Ami-Chan~ ✨*