# Phase 4.4: High Availability & Clustering (Weeks 25-26) 🏗️

Made with 💖 by Ami-Chan! UwU ✨

[Back to Phase 4](Phase4-Production.md) | [All Phases](README.md)

---

## Overview

Phase 4.4 takes DotFlow from a single process to **N nodes with automatic failover**: Akka.NET
clustering, execution distribution, a singleton for scheduling, distributed locking, graceful
shutdown, and health-based routing.

> **Reality-check note (August 2026).** §4.4 reads as six tidy bullets. The code says otherwise —
> clustering touches nearly every assumption the engine currently makes. Verified against the code:
>
> | Claim / assumption | Reality |
> | --- | --- |
> | "Implement Akka.NET clustering" | **`Akka.Cluster` is already a `PackageReference`** in `Workflow.Engine` — with **0 usages**. `Akka.Cluster.Sharding` is pinned in `Directory.Packages.props` but **not referenced by any project**. Same "paid for, unwired" pattern as Serilog in 4.2. |
> | The ActorSystem is ready to cluster | `Program.cs:205` does `ActorSystem.Create("dotflow")` — **no HOCON, no remoting, no serializer bindings**. A `MsgPack2Setup` helper exists (with LanguageExt resolvers!) and documents exactly how to pass that config… and nothing calls it. |
> | Messages can cross a wire | `CreateWorkflowInstance` carries a `WorkflowDefinition` (LanguageExt `Arr<>`/`HashMap<>`) and `HashMap<string, object?> Inputs` whose values are **arbitrary CLR objects**. In-process these pass by reference and nothing serialises. See **F3** — this is the largest hidden cost in the phase. |
> | Failover can resume an execution | **`IExecutionStateStore.LoadSnapshotAsync` is called only from tests.** Snapshots are written on terminal states and `PreRestart`, and never read back in production. There is no rehydrate path today. |
> | "Pin transaction sections to a node" | The constraint is real and **currently fails silently** — see **F5**. It's also solvable much more cheaply than the note implies (**D3**). |

**Timeline:** 2 weeks (Weeks 25-26) — 4.4.0–4.4.2 (config, serialization, snapshot recovery) Week 25 ·
4.4.3–4.4.7 (sharding, singleton, locking, shutdown, routing) Week 26
**Complexity:** 🔴 High — the largest architectural change since the engine itself. The risk is not
"does the cluster form" (that's a day); it's **serialization** (F3), **at-most-once side effects on
failover** (Q2), and **silently non-transactional database work** (F5).

> **CopilotNote:** Hot paths: `Workflow.Api/Program.cs` (ActorSystem config), a new
> `Workflow.Engine/Cluster/` (sharding setup, message extractors, lease), `WorkflowSupervisor.cs`
> (local `ActorOf` → shard region proxy), `WorkflowExecutor.cs` (rehydrate on start, passivation
> rules), `Workflow.Engine/Messages/WorkflowMessages.cs` (serialisable envelopes), and
> `Workflow.Modules.Database/Transactions/AmbientDbTransactions.cs` (locality guard). Tests use
> `Akka.Cluster.TestKit` multi-node specs plus TestContainers for a real 3-node run~ 🌸

---

## Findings 🔬

### F1 — Clustering is referenced, not wired

`Akka.Cluster` sits in `Workflow.Engine.csproj` with zero code usage; `Akka.Cluster.Sharding` isn't
referenced at all. Nothing to undo — but nothing to build on either.

### F2 — The ActorSystem has no configuration

```csharp
// Workflow.Api/Program.cs:205
builder.Services.AddSingleton(sp => ActorSystem.Create("dotflow"));
```

No HOCON, no remoting transport, no serializer bindings, no cluster seed nodes. `MsgPack2Setup`
already prepares MessagePack options **with LanguageExt resolvers** and documents the exact
`ActorSystem.Create(name, config)` call to use — it's simply never invoked. Step zero is config,
not code.

### F3 — Messages are not wire-ready (the expensive one)

In a single process, `Tell` passes references and nothing is serialised. Across nodes, everything
is. Two problems:

1. **LanguageExt types** — `WorkflowDefinition` carries `Arr<NodeDefinition>` and
   `HashMap<string, VariableDefinition>`. `MsgPack2Setup` has resolvers for these, which is
   promising, but they've only ever been exercised by direct-serialisation tests, not by Akka.
2. **`object?` payloads** — `HashMap<string, object?> Inputs`, node outputs, and
   `ModuleResult.Outputs` are unbounded polymorphic values produced by arbitrary modules. A module
   returning a `DataTable`, a `JsonElement`, or its own private type will serialise fine locally and
   explode across the wire.

`WorkflowExecutionContext` is already `[MessagePackObject(keyAsPropertyName: true)]`, which shows
the intent — but the payloads inside it aren't constrained.

### F4 — Snapshots are write-only

`SaveSnapshotAsync` is called on terminal states and in `PreRestart`. `LoadSnapshotAsync` exists on
the interface and is called **only from tests** (`ActorLifecycleTests`,
`ExecutionStateTrackingTests`). So the engine can persist an execution's state but has no path that
rebuilds an executor from it.

This is the same shape as the bug Phase 3.5 found in the variable store (written, never read), and
it's load-bearing here: sharding *exists* to move entities between nodes, and a moved entity that
can't rehydrate is just a lost execution.

### F5 — Transaction locality currently fails **silently**

```csharp
// Workflow.Modules.Database/Transactions/AmbientDbTransactions.cs
private readonly ConcurrentDictionary<Key, object> connections = new();   // Key = (executionId, connectionId)
```

`TransactionExecutorActor` opens an `IWorkflowTransactionScope` (owning a live linq2db
`DataConnection`) and registers it in this **process-local** dictionary. Database nodes inside the
transaction body call `TryGet(executionId, connectionId)` to find it.

If a body node runs on a different cluster node, `TryGet` returns `null` — and the module falls
back to opening its **own** connection. The node succeeds. The work simply happens **outside the
transaction** and isn't rolled back with it. No exception, no warning, no failed test.

A live database connection cannot be serialised or migrated. This is a hard locality constraint,
and the current failure mode is the worst kind: silent and data-corrupting.

### F6 — Execution addressing is local-parent-based

`WorkflowSupervisor` creates executors with `Context.ActorOf(WorkflowExecutor.Props(...))` and
addresses them by `IActorRef`. `GetWorkflowStatus` and `CancelExecution` resolve through local
children. Sharding replaces this with entity ids routed through a shard region proxy — every
call site that assumes a local child needs revisiting.

### F7 — Nothing is cluster-singleton-shaped yet, but something already double-fires

Scheduling arrives in 4.5, so there's no scheduler to make a singleton. But **webhook dispatch**
exists today (`WebhookDispatcher`), and on N nodes every node would dispatch — N× deliveries. Any
"do this once" behaviour needs identifying before the second node exists, not after.

---

## Confirmed Design Decisions ✅

| # | Decision |
|---|----------|
| **D1 Config before code** | Introduce HOCON (remoting transport, serializer bindings via the existing `MsgPack2Setup`, cluster roles/seeds) and pass it to `ActorSystem.Create`. Clustering is **off by default** (`Cluster:Enabled=false`) so single-node deployments — the documented "standalone" option in 4.7 — are unaffected. |
| **D2 The shard entity is an *execution*, keyed by `executionId`** | Not the workflow id: concurrent executions of one popular workflow must spread across nodes, and a workflow-keyed entity would hot-spot. `executionId` is already a `Guid` generated per run and already the key for snapshots, ambient transactions, and history. |
| **D3 An execution's entire actor tree lives on one node — no intra-execution distribution** | §4.4's note asks to "pin transaction sections to a node". Pinning *sections* implies the rest of an execution is distributable, which it isn't: `WorkflowExecutor` holds node outputs, variables and routing state in memory, and its children (`NodeExecutor`, `SubGraphExecutor`, `LoopExecutor`, `TransactionExecutorActor`) are direct children sharing that state. Distributing within an execution would require replacing all of it with distributed state — an enormous change for no throughput benefit, since parallelism across *executions* already saturates a cluster. **Making the execution the atomic unit of placement solves F5 for free** and removes the need for a region-pinning mechanism entirely. |
| **D4 Entities never passivate mid-execution** | Standard sharding passivates idle entities to reclaim memory. An execution waiting on a slow HTTP call looks idle and must not be evicted. Passivation is allowed **only** in terminal states; a long-running execution holds its entity until it finishes. Memory is bounded by the 4.1 target (< 500 MB / 100 workflows) instead. |
| **D5 Rehydrate-or-fail, never silently restart** | Implement the missing snapshot restore (F4): on entity start, load the snapshot and resume. If no usable snapshot exists, mark the execution **Failed** with a clear reason — do **not** re-run it from the beginning. Nodes perform side effects (HTTP POSTs, inserts, file writes); replaying an execution from zero can duplicate them. At-most-once is the safe default; opt-in resume semantics are **Q2**. |
| **D6 Split-brain resolver is mandatory and explicit** | Enable Akka's SBR with **keep-majority**, and require an odd node count in the documented topology. Without SBR, a network partition produces two halves both convinced they own the same execution entities — the same execution running twice, with duplicated side effects. Also sets a minimum cluster size so a 2-node deployment can't split 1-1 (**Q7**). |
| **D7 Distributed locks use the database, not cluster state** | Cross-node mutual exclusion (webhook dedupe now, scheduler triggers in 4.5) uses a **lease table in the existing persistence provider** rather than `DistributedData`. Reasons: persistence is already required and already HA in the target topology, a DB lease survives a full cluster restart, and it doesn't add CRDT semantics to reason about. `IDistributedLock` with a DB-backed implementation and an in-process no-op for single-node. |
| **D8 Graceful shutdown drains, it doesn't dump** | Hook `CoordinatedShutdown`: (1) leave the cluster so no new entities route here, (2) stop accepting new executions, (3) let in-flight executions finish within a bounded drain window, (4) snapshot whatever remains, (5) hand off. Kubernetes `preStop` + `terminationGracePeriodSeconds` must exceed the drain window, documented for 4.7. |
| **D9 Health-based routing reuses 4.2.4** | The readiness endpoint from Phase 4.2 gains cluster membership: a node that is `Up` and drained-of-nothing is ready; a node that is `Leaving`/`Down`/unreachable is not. The load balancer follows readiness, so draining nodes stop receiving HTTP before they stop processing actors. |
| **D10 Locality is asserted, not assumed** | Even with D3, add a defensive guard: `AmbientDbTransactions.TryGet` returning `null` **inside a transaction body** must throw rather than silently opening a fresh connection (F5). A silent fallback to non-transactional work is never the behaviour anyone wants — if D3 is ever weakened, this turns a data-corruption bug into a loud failure. |

---

## TO RESOLVE 🤔

| # | Question | Proposal |
|---|---|---|
| **Q1** | **Do we need sharding in v1, or is sticky routing enough?** Full cluster sharding (rebalancing, handoff, remember-entities) is a big step. A simpler v1: N nodes, route by `executionId` hash, no rebalancing — a node failure fails its in-flight executions rather than migrating them. | **Sharding**, because rebalancing is the reason to cluster. But it hinges on Q2 — if failover can't safely resume, sharding buys much less and sticky routing may be the honest v1. |
| **Q2** | ⚠️ **What happens to an in-flight execution when its node dies?** Nodes perform non-idempotent side effects. Options: (a) fail it and surface it for manual retry; (b) resume from the last snapshot, re-running at most the node that was in flight; (c) per-workflow opt-in. | **(a) fail by default, (c) opt-in resume** via a workflow-level `ResumeOnFailover` flag, since only the author knows whether their nodes are idempotent. Needs your call — it defines the HA guarantee we advertise. |
| **Q3** | **How do `object?` payloads serialise (F3)?** Options: MessagePack typeless (works, embeds CLR type names, fragile across versions); force JSON round-trip at the boundary (lossy for custom types); constrain module outputs to a documented set. | Constrain + validate: document a supported payload set, and **fail loudly at the boundary** when a module returns something unserialisable — with a single-node escape hatch. Needs a survey of what builtin modules actually return. |
| **Q4** | **Node discovery** — static seed nodes (config) or Akka.Management + Kubernetes discovery? | Static seeds for the standalone/Compose story; **k8s discovery** for the Helm chart in 4.7. Both, selected by configuration. |
| **Q5** | **Which operations actually need a distributed lock?** Known: webhook dispatch dedupe (F7), scheduler triggers (4.5). Suspected: global-variable writes, module install/uninstall, workflow definition updates. | Audit in 4.4.5 and produce an explicit list; don't lock speculatively. |
| **Q6** | **Cluster roles.** Should API nodes and execution nodes be separable (`role=api`, `role=worker`), so you can scale execution capacity independently? | **Yes, but ship roles unused** — define them now (cheap), default to every node having both, so the topology can split later without a rewrite. |
| **Q7** | **Minimum cluster size.** SBR keep-majority makes a 2-node cluster dangerous (1-1 split has no majority). Do we require 3 nodes for HA, and what does a 2-node deployment do? | Document **3 nodes minimum for HA**; 2 nodes is explicitly unsupported for HA and should warn loudly at startup. |
| **Q8** | **Do modules need to be identical across nodes?** Phase 2.8 supports hot-reload and side-by-side versions per node. A shard entity could migrate to a node lacking that module version. | Require homogeneous module sets in a cluster for v1; validate at join and refuse membership on mismatch. |

---

## Slices

### 4.4.0 — ActorSystem configuration & serialization 🔌

**Tasks:**
- [ ] Introduce HOCON config; pass it to `ActorSystem.Create` (F2, D1)
- [ ] Wire `MsgPack2Setup.GetSerializationHocon()` — the helper that already exists and is unused
- [ ] Add `Akka.Remote` + reference `Akka.Cluster.Sharding` in `Workflow.Engine.csproj`
- [ ] `Cluster:Enabled` flag — off by default, single-node path unchanged (D1)
- [ ] Audit every `IWorkflowMessage` for serialisability; annotate or reshape (F3)
- [ ] Decide and implement the `object?` payload strategy (Q3), with a loud boundary failure
- [ ] Turn on `akka.actor.serialize-messages = on` in test config — the switch that makes
      single-node tests catch serialization bugs *before* a cluster does

**Tests:**
- [ ] Every message type round-trips through the configured serializer
- [ ] A `WorkflowDefinition` with LanguageExt collections round-trips
- [ ] Each builtin module's outputs round-trip (the F3 survey, as an executable spec)
- [ ] Existing single-node suite passes with `serialize-messages = on`

### 4.4.1 — Snapshot recovery 💾

**Tasks:**
- [ ] Implement the restore path: load snapshot on entity start and rebuild executor state (F4, D5)
- [ ] Version the snapshot payload so a schema change doesn't resurrect garbage
- [ ] Reconstruct node-actor tracking for nodes that were in flight
- [ ] Terminal-state handling: mark Failed with a clear reason when no usable snapshot exists
- [ ] Snapshot on a cadence during long executions, not only at terminal states

**Tests:**
- [ ] Kill an executor mid-run; a fresh one rebuilds the same context from the snapshot
- [ ] An unusable/older-version snapshot fails the execution cleanly rather than half-restoring
- [ ] Snapshot → restore → resume produces the same final outputs as an uninterrupted run

### 4.4.2 — Transaction locality guard 🔒

**Tasks:**
- [ ] Make `AmbientDbTransactions.TryGet` returning `null` inside a transaction body **throw** (D10, F5)
- [ ] Add an assertion that a body node's ambient lookup is on the same process that registered it
- [ ] Document the locality constraint in `docs/database-modules.md`

**Tests:**
- [ ] A body node that can't find its ambient scope fails loudly (the F5 regression guard)
- [ ] Committed/rolled-back transactions still behave identically on a single node

### 4.4.3 — Cluster sharding for executions 🧩

**Tasks:**
- [ ] Shard region for execution entities; `executionId` as entity id (D2)
- [ ] Message extractor + shard resolver (consistent hashing, ~10× shards to nodes)
- [ ] `WorkflowSupervisor` routes through the shard region proxy instead of `Context.ActorOf` (F6)
- [ ] `GetWorkflowStatus` / `CancelExecution` route by entity id, not local child lookup
- [ ] Passivation only in terminal states (D4)
- [ ] `remember-entities` off — D5's rehydrate path is the recovery mechanism, and remembering
      entities would restart executions we've decided to fail
- [ ] Cluster roles defined but unused (Q6)

**Tests:**
- [ ] Multi-node spec: entities distribute across 3 nodes
- [ ] Status/cancel reach an entity hosted on another node
- [ ] An entity is not passivated while its execution is running
- [ ] Rebalance moves an entity and it rehydrates (depends on 4.4.1)

### 4.4.4 — Cluster singleton 👑

**Tasks:**
- [ ] Cluster singleton manager + proxy, ready for 4.5's scheduler
- [ ] Move webhook dispatch behind the singleton or the lock (F7) — whichever the Q5 audit says
- [ ] Lease-based singleton so a partition can't produce two

**Tests:**
- [ ] Exactly one singleton instance across the cluster
- [ ] Singleton migrates on node loss
- [ ] No double-dispatch of a webhook with 3 nodes running

### 4.4.5 — Distributed locking 🔐

**Tasks:**
- [ ] Audit what genuinely needs mutual exclusion (Q5); produce the list before implementing
- [ ] `IDistributedLock` + DB-backed lease implementation (D7) with TTL and fencing tokens
- [ ] In-process no-op implementation for single-node
- [ ] Apply at the audited call sites only

**Tests:**
- [ ] Concurrent acquire across processes — exactly one winner
- [ ] Lease expiry releases a lock held by a dead node
- [ ] Fencing token prevents a stale holder from acting after expiry

### 4.4.6 — Graceful shutdown 🌙

**Tasks:**
- [ ] `CoordinatedShutdown` phases per D8 (leave → stop accepting → drain → snapshot → hand off)
- [ ] Configurable drain window; force-snapshot on expiry
- [ ] Readiness flips to not-ready at the *start* of shutdown (D9)
- [ ] Document `preStop` / `terminationGracePeriodSeconds` for 4.7

**Tests:**
- [ ] In-flight executions complete during the drain window
- [ ] Executions exceeding the window are snapshotted, not dropped
- [ ] No new executions are accepted after shutdown begins

### 4.4.7 — Health-based routing 🚦

**Tasks:**
- [ ] Cluster membership feeds the 4.2.4 readiness check (D9)
- [ ] Unreachable-member handling and SBR configuration (D6, Q7)
- [ ] Startup warning when the cluster is smaller than the HA minimum
- [ ] Cluster status in `/api/v1/status` (members, roles, leader, unreachable)

**Tests:**
- [ ] A `Leaving` node reports not-ready
- [ ] A partition resolves via keep-majority with the minority downed
- [ ] Status endpoint reflects real membership

---

## Deliverables

- ✅ 3-node cluster forming, with executions distributed by `executionId`
- ✅ Node failure handled per the Q2 policy, with no duplicated side effects
- ✅ Transactions provably local — and loudly broken if they ever aren't
- ✅ Exactly-one semantics for singleton work, and locks for the audited call sites
- ✅ Graceful drain with no dropped or silently-restarted executions
- ✅ Single-node deployment unchanged and still supported

## Success criteria

- [ ] 3 nodes form a cluster; executions spread across all three
- [ ] Killing a node triggers the documented failover behaviour (Q2) with no duplicate side effects
- [ ] A transaction body never runs partially outside its transaction — enforced by a test, not a convention
- [ ] Split-brain resolves to one surviving majority; the minority stops processing
- [ ] Rolling restart completes with zero dropped executions
- [ ] `Cluster:Enabled=false` behaves exactly as today (full existing suite green)
- [ ] Phase 4.1 performance targets still met with clustering on

---

*Made with 💖 by Ami-Chan! UwU* ✨
