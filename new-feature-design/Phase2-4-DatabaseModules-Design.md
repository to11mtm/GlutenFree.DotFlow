# 🗄️ Phase 2.4 Database Modules — Design Exploration

> **Status:** ✅ All Q1–Q10 resolved · Ready to produce `Phase2-4-DatabaseModules.md` task doc · **Author:** Ami-chan (with input from `sqlModuleHandler.md`)
> **Companion to:** [`phases/Phase2-CoreFeatures.md` §2.4](../phases/Phase2-CoreFeatures.md)
> **Reference notes:** [`new-feature-design/sqlModuleHandler.md`](./sqlModuleHandler.md)

---

## 0. TL;DR — What this doc decides ✨

The original Phase 2.4 plan is a **flat "4-module" slice**: `builtin.database.query`, `builtin.database.execute`, `builtin.database.transaction`, `builtin.database.bulkinsert`, each taking raw SQL strings + a provider enum + a parameter dictionary. That is **correct and shippable**, but the `sqlModuleHandler.md` notes hint at a much spicier capability — **strongly-typed linq2db queries authored against a UI-selected schema, Roslyn-compiled at design-time, validated in an in-memory SQLite sandbox, then executed in a collectible `AssemblyLoadContext`**. UwU 💖

This document proposes to **keep the raw-SQL family as the V1 MVP** *(unchanged scope, faster ship)*, and add a **second, layered family** — `builtin.database.linq` — as an **optional Phase 2.4.b slice** that reuses the same connection/provider/transaction plumbing but swaps the "SQL string + dict" surface for "selected tables + C# expression body". This keeps the MVP boring and shippable while leaving the door open to the much nicer authoring experience the snippet sketches~ 🌸

**Recommendation:** Adopt the **revised sub-phase split** in §6 and the **revised feature-set** in §5. Do **not** block 2.4.a (raw SQL) on 2.4.b (Roslyn/linq2db); 2.4.b becomes a post-MVP slice (sibling to the `2.3.PN` post-MVP slices in 2.3).

> ⚠️ **Superseded (July 2026 re-plan):** Product direction now requires that *users should not have to write raw SQL unless absolutely necessary, even in the MVP*. The chosen composition is effectively **"Option D" — typed-first layered**: **2.4.b (typed linq/Roslyn) is promoted into the MVP** (Weeks 13-14) as the primary authoring surface, with the raw-SQL family (2.4.a.1–4) retained as the escape hatch. All technical content in this doc (§5.2, §8) remains the authoritative design for 2.4.b; only the *sequencing* verdict in §3/§6 and the Q1 resolution are superseded. See the updated [`phases/Phase2-4-DatabaseModules.md`](../phases/Phase2-4-DatabaseModules.md) for the full MVP task breakdown~ 🌟

---

## 1. Analysis of `sqlModuleHandler.md`

### 1.1 What the snippet actually does

The snippet is a **three-piece runtime** (not a workflow module per se):

| Piece | Responsibility | Key tech |
|---|---|---|
| `WorkflowTableMetadata` | Catalog entry: `(TableName, ClrTypeName, Assembly)` — what a UI picker would offer | POCO |
| `WorkflowCompiler` | Generates `DynamicWorkflowContext : DataConnection` with `ITable<T> {TableName}` properties for each selected table; wraps user code in `WorkflowScript.ExecuteAsync(db, payload)`; Roslyn-compiles both syntax trees with `Microsoft.CodeAnalysis.CSharp` and returns `(CSharpCompilation, ValidationResult)` with full diagnostics | Roslyn |
| `WorkflowExecutor` | `compilation.Emit` → MemoryStream → `AssemblyLoadContext(isCollectible: true)` → reflectively `CreateTable<T>()` on an in-memory SQLite → invoke `ExecuteAsync(db, payload)` → `alc.Unload()` | linq2db.SQLite + ALC |

### 1.2 What's nice about it 💖

1. **Strongly-typed authoring** — engineers write `db.Orders.Where(o => o.Total > 100).ToList()` in the UI, not `"SELECT * FROM orders WHERE total > @t"` with a stringly-typed dict.
2. **Design-time validation** — Roslyn surfaces `CS0103: The name 'Ordres' does not exist` *while the user is editing*, instead of at first execution against prod.
3. **Sandbox preview** — in-memory SQLite + `CreateTable<T>` lets the UI render a "Run sample" panel without touching real DBs.
4. **Collectible ALC** — generated assemblies are unloaded after execution → no memory bloat on a long-lived API host.
5. **Plugin-friendly** — `WorkflowTableMetadata.Assembly` means the model types can live in user-supplied `.dll`s (slots neatly into the `.wfmod` story in 2.8).

### 1.3 What's missing / concerning ⚠️

| # | Concern | Severity | Mitigation needed in our design |
|---|---|---|---|
| C1 | **No sandboxing of arbitrary C#** — user code can `File.Delete`, `Process.Start`, call out to network, etc. ALC is *isolation*, not *security* | 🔴 High | Restrict allowed `MetadataReference`s + `usings`; consider `CSharpScript` `ScriptOptions` allowlist; SDLC: only "trusted authors" can save modules in V1 |
| C2 | **No async/`CancellationToken`** in `ExecuteAsync(db, payload)` signature — engine cancellation will be ignored | 🟠 Med | Replace with `Task<object> ExecuteAsync(DynamicWorkflowContext db, IReadOnlyDictionary<string,object?> inputs, CancellationToken ct)` |
| C3 | **No connection-string injection** — preview hard-codes `:memory:`; real execution path not shown | 🟠 Med | Inject `IDbConnectionFactory` keyed by provider; never let user code see raw conn strings |
| C4 | **No parameter typing for `payload`** — `dynamic` defeats Roslyn's whole value-add | 🟠 Med | Generate a typed `Inputs` record from the module's `WorkflowModuleSchema` so user code sees `inputs.OrderId` |
| C5 | **No output schema** — returns `object` | 🟡 Low | Same: generate typed `Outputs` record; engine maps via existing `OutputBindings` |
| C6 | **No mutation guard** — preview against `:memory:` could execute `DROP TABLE` etc. | 🟡 Low | Optional read-only mode for preview (linq2db `DataConnection` doesn't easily forbid mutations — wrap in transaction + always rollback) |
| C7 | **Compile cost** — Roslyn compile is ~200–800ms; not acceptable per-execution | 🔴 High | **Compile at workflow-publish time**, cache `byte[]` of emitted assembly in `IBlobStore` (Phase 2.1), keyed by `(definitionId, nodeId, hash(code+schemaVersion))`; load via ALC at execution |
| C8 | `Assembly.Load("System.Runtime")` style refs | 🟡 Low | Use `Basic.Reference.Assemblies` package for portable refs across runtimes |
| C9 | No diagnostics severity beyond `Error` | 🟡 Low | Surface warnings too (UI can show them as squigglies) |
| C10 | `:memory:` SQLite ≠ Postgres semantics — preview will lie about, e.g., case-sensitivity, `RETURNING`, JSONB ops | 🟠 Med | Document loudly; eventually offer optional Testcontainers-backed preview (post-MVP) |
| C11 | No way for user code to *call other workflow modules* | 🟡 Low | Out of scope for 2.4; revisit in Phase 3 scripting |

---

## 2. How it fits (and doesn't fit) DotFlow's existing patterns

### 2.1 What aligns 🌸

- **Per-call DI via `ModuleExecutionContext.Services`** — both raw SQL modules and linq2db modules can resolve `IDbConnectionFactory`, `IBlobStore` (for cached assemblies), and `ILogger` lazily exactly like `HttpRequestModule` resolves `IHttpClientFactory`. ✅ (see `Workflow.Modules/Builtin/Http/HttpRequestModule.cs` lines ~40–70 — same pattern works here)
- **Provider enum + linq2db `ProviderName`** — already in use in `Workflow.Persistence.Sqlite` / `.Postgres`; the snippet's `ProviderName.SQLite` plugs straight in.
- **`IBlobStore`** (Phase 2.1.4) is **exactly** the right home for cached compiled assemblies — keyed blobs with TTL semantics, already provider-agnostic. 💯
- **Schema-driven module surface** — DotFlow modules declare `WorkflowModuleSchema` with typed inputs/outputs; we can drive both the raw-SQL parameter binder *and* the linq2db typed-record codegen from the same schema source-of-truth.

### 2.2 What clashes 💢

- **`IWorkflowModule` is parameterless-constructable** (see `BuiltinModules.GetAll()` convention). The linq2db family needs **per-module compiled state** (the cached `Assembly`) but must not bake user code into the module class itself — solution: a **template module** `LinqQueryModule` that loads the cached blob by `(definitionId, nodeId)` from inputs/context, exactly like how `WebhookTriggerModule` resolves registrations by id.
- **Roslyn is a *huge* dep** (~30MB transitive) — must live in its own `Workflow.Modules.Database.Linq` project so plain SQL users don't pay for it.
- **`AssemblyLoadContext.Unload` is async-best-effort** — if user code captures references across the boundary (e.g., returns a lazy `IQueryable`), the ALC won't unload. Mitigation: **materialize before return** (force `.ToList()` etc.) or fail with a clear diagnostic.

---

## 3. Three composition options weighed ⚖️

### Option A — "Raw SQL only" (the original Phase 2.4 plan, unchanged)

**Modules:** `builtin.database.{query,execute,transaction,bulkinsert}` with `(connectionString, provider, sql, parameters)` inputs.

| ✅ Pros | ❌ Cons |
|---|---|
| Cheap to ship (~2 weeks as planned) | Stringly-typed; no design-time validation |
| Familiar to anyone who's used n8n / Zapier DB nodes | SQL injection only prevented if users discipline themselves to parameterize |
| No Roslyn dep | No autocomplete / refactor / "rename column" support |
| Works identically across all 4 providers | Easy to ship buggy SQL into prod |

### Option B — "Roslyn + linq2db only" (the `sqlModuleHandler.md` vision, dropped raw-SQL)

**Modules:** Single `builtin.database.linq` template module fed by Roslyn-compiled blobs.

| ✅ Pros | ❌ Cons |
|---|---|
| Best authoring experience — typed, validated, refactor-safe | Huge implementation cost (Roslyn pipeline + ALC + sandbox + UI integration) |
| SQL injection effectively impossible | Doesn't cover stored procs / bulk insert / vendor-specific DDL |
| Catches typos before deploy | Roslyn dep on every DB user |
| Sandbox preview is *amazing* UX | Won't ship in Phase 2 timeline |

### Option C — **Layered: A then B** ⭐ *(RECOMMENDED)*

Ship Option A's 4 modules as **2.4.a** (the MVP), then add **2.4.b** *(post-MVP, non-blocking)*: one `builtin.database.linq` module that **reuses 2.4.a's connection/provider/transaction primitives** but adds the Roslyn compile-cache-execute pipeline.

| ✅ Pros | ❌ Cons |
|---|---|
| MVP ships on schedule | Two families to document |
| Power users get typed authoring; everyone else gets familiar SQL | Roughly +2 weeks for 2.4.b later |
| `Workflow.Modules.Database` (raw) and `Workflow.Modules.Database.Linq` (Roslyn) are cleanly separable | Some refactor needed at 2.4.b time to extract shared `IDbConnectionFactory` |
| Roslyn dep is opt-in (separate NuGet/`.wfmod`) | |
| 2.4.b can borrow from 2.3's "post-MVP slice" tracking convention | |

**Verdict:** Option C — see §5–§6.

---

## 4. Considerations for *how the snippet shapes 2.4.a* (even without doing 2.4.b yet)

Even if we don't build Roslyn now, the snippet's existence should bias 2.4.a's design in these specific ways:

1. **Extract `IDbConnectionFactory` from day one.** The raw SQL modules should *not* `new DataConnection(...)` directly — they should resolve a factory keyed by provider, so 2.4.b's `DynamicWorkflowContext` can share the same connection-pool plumbing.
2. **Reuse a single `IDbProviderRegistry`** that maps `provider: "postgres"` → `(linq2dbProviderName, IDbConnectionFactory)`. Same registry powers both families.
3. **`IDbTransactionScope` abstraction.** The transaction module's "execute N ops" surface should accept either raw-SQL ops *or* a delegate (so 2.4.b's linq queries can join the same transaction without re-implementing isolation/savepoints).
4. **`WorkflowTableMetadata` catalog → start small.** Even 2.4.a benefits from a "known tables" registry for the UI's connection picker / autocomplete on table names in raw SQL. Build the catalog now; linq2b consumes it later for `selectedTables`.
5. **Persistence integration.** Reserve a `compiled_modules` blob namespace in `IBlobStore` *now* (even if unused) so 2.4.b's assembly cache slots in without a migration.
6. **Engine: never serialise live `DataConnection`s.** Outputs returned to the engine must be plain DTOs (`IReadOnlyList<IReadOnlyDictionary<string,object?>>`), never `IQueryable<T>` — same rule will apply to 2.4.b user code.

---

## 5. Revised feature-set 🌟

### 5.1 Phase 2.4.a — Raw SQL Family (MVP, Week 11–12, **was the original 2.4**)

Largely unchanged from the existing Phase 2.4 plan, with these **deltas**:

| Change | Rationale |
|---|---|
| ➕ New shared project `Workflow.Modules.Database` housing `IDbConnectionFactory`, `IDbProviderRegistry`, `IDbTransactionScope` | Foundation reused by 2.4.b |
| ➕ Add `connectionId` (string, optional) input — references a **named connection** registered in DI / config — alongside raw `connectionString` | Hide credentials from definitions; matches HTTP module's named-credential pattern from 2.3.4 |
| ➕ Add `IDbConnectionRegistry` (DI) with config-bound named connections (`appsettings.json: Workflow:Connections:OrdersDb`) | Same as above; ops-friendly |
| ➕ Reserve `WorkflowTableMetadata` catalog API (`IWorkflowTableCatalog`) — empty impl in 2.4.a | UI hook + 2.4.b consumer |
| ➕ `IBlobStore` namespace `compiled-modules/` reserved (no writes yet) | Forward-compat |
| ⚠️ Drop "stored procedure" from MVP, defer to 2.4.a.P1 | Vendor-specific; not blocking |
| ⚠️ Drop "nested transactions / savepoints" from MVP, defer to 2.4.a.P2 | Same |
| ⚠️ Drop full multi-provider Testcontainers matrix from MVP; ship SQLite + Postgres only, others post-MVP | Schedule realism |

**MVP Modules (2.4.a):**
- ✅ `builtin.database.query` — SELECT, returns rows + columns + rowCount
- ✅ `builtin.database.execute` — INSERT/UPDATE/DELETE, returns affectedRows + lastInsertId
- ✅ `builtin.database.transaction` — sequence of ops, atomic commit/rollback
- ✅ `builtin.database.bulkinsert` — provider-specific bulk path with batched fallback

**MVP Providers:** Postgres + SQLite. MySQL + SQL Server → 2.4.a.P3 (Testcontainers-driven).

**Post-MVP slices (tracked, non-blocking 2.4.b+):**
- `2.4.a.P1` — Stored procedure support (`commandType: storedProcedure`)
- `2.4.a.P2` — Savepoint / nested transaction support
- `2.4.a.P3` — MySQL + SQL Server providers (+ Testcontainers test matrix)
- `2.4.a.P4` — Connection-pool metrics + OpenTelemetry instrumentation
- `2.4.a.P5` — Streaming `query` results (`AsyncEnumerable` output for large result sets)

### 5.2 Phase 2.4.b — Typed Linq/Roslyn Family (Post-MVP, ~2 weeks)

**New module:** `builtin.database.linq` — single template module that:

1. **Inputs:** `connectionId` (string, required), `selectedTables` (array of `{tableName, clrTypeName}`, required), `compiledAssemblyKey` (string, required — points into `IBlobStore`), `inputs` (object, optional)
2. **Engine flow:**
   - On module load: pull blob from `IBlobStore` (cache in-memory by hash, LRU)
   - Per execution: spin up collectible `AssemblyLoadContext`, instantiate `WorkflowScript`, invoke `ExecuteAsync(db, inputs, ct)`, **force materialise** outputs, return, then `alc.Unload()`
3. **Outputs:** `result` (object/array of DTOs, materialised), `rowCount` (int, when applicable)

**Companion authoring services** (live in API/UI layer, not in the runtime module):

- `IWorkflowLinqCompiler` — wraps the `WorkflowCompiler` from the snippet, plus:
  - **Reference whitelist** (System, System.Linq, LinqToDB, optionally project-defined POCO assemblies)
  - **Usings whitelist** (block `System.IO`, `System.Net`, `System.Diagnostics.Process`, etc. via syntax walker)
  - **`LinqInputs` accessor struct codegen** from `ModuleSchema.Properties` using restricted Type→CSharpName mapping (see §8.6 — Phase 1); `record` codegen deferred to post-2.4.b slice **2.4.b.P1**
  - Compiles to `byte[]`, persists to `IBlobStore` under `compiled-modules/{definitionId}/{nodeId}/{hash}.dll`
- `IWorkflowLinqPreviewer` — wraps the `WorkflowExecutor` from the snippet:
  - Spins up `:memory:` SQLite, `CreateTable<T>` for each selected type
  - **Wraps the user code in a transaction that is always rolled back** (mitigates C6)
  - Returns sample rows + execution time + diagnostics
- API endpoints:
  - `POST /api/database/linq/validate` → `{ success, errors[], warnings[] }`
  - `POST /api/database/linq/preview` → `{ result, rowsAffected, duration }`
  - `POST /api/database/linq/compile` → writes blob, returns `compiledAssemblyKey`

**Sandbox / security:**
- Reference whitelist enforced at compile time
- Syntax-tree walker rejects: `unsafe`, `P/Invoke` attrs, `AppDomain`, `Process`, `File`, `Directory`, `Socket`, `HttpClient`, `Reflection.Emit`, `Activator.CreateInstance` on non-DTO types
- Compiled assemblies signed/HMACed with a per-instance key so swapped blobs are rejected at load
- "Trusted author" role required to save linq modules in V1

**Out-of-scope for 2.4.b (revisit Phase 3):**
- Cross-module calls from user code
- Async streams / `IAsyncEnumerable` returns
- Custom POCO authoring in the UI (V1: POCOs must come from registered plugin assemblies)
- Multi-DB JOINs (one connection per module instance)

**Post-MVP slices for 2.4.b (tracked, non-blocking):**
- `2.4.b.P1` — **Typed record codegen upgrade**: ratify allowed-types allowlist (including collection types), replace `LinqInputs` struct with `record LinqInputs(...)` primary constructor, extend Type→CSharpName mapping to cover `IReadOnlyList<T>` and other collection types (see §8.6 Phase 2)
- `2.4.b.P2` — Testcontainers-backed preview (replace `:memory:` SQLite with the real target provider for accurate dialect feedback)
- `2.4.b.P3` — `IWorkflowTableCatalog` auto-discovery (scan registered databases, store versioned schema snapshots — see Q4 resolution)

---

## 6. Revised sub-phase split 🌷

```
2.4 Database Modules (Weeks 11–12 + 1 post-MVP slice)
├── 2.4.a Raw SQL Family (MVP)                                       — Weeks 11–12
│   ├── 2.4.a.0 Shared infrastructure                                 (~3 days)
│   │   • IDbConnectionFactory, IDbProviderRegistry, IDbConnectionRegistry
│   │   • IDbTransactionScope, IWorkflowTableCatalog (stub)
│   │   • Workflow.Modules.Database project scaffolding
│   ├── 2.4.a.1 Query module                                          (~2 days)
│   ├── 2.4.a.2 Execute module                                        (~2 days)
│   ├── 2.4.a.3 Transaction module                                    (~2 days)
│   ├── 2.4.a.4 BulkInsert module                                     (~3 days)
│   ├── 2.4.a.5 Persistence + API surface (named connections)         (~2 days)
│   └── 2.4.a.6 E2E demo + docs                                       (~1 day)
│
└── 2.4.b Typed Linq Family (Post-MVP, non-blocking 2.5+)            — ~2 weeks
    ├── 2.4.b.0 Workflow.Modules.Database.Linq scaffolding             (~1 day)
    ├── 2.4.b.1 IWorkflowLinqCompiler + reference/syntax whitelists    (~3 days)
    ├── 2.4.b.2 Compiled-assembly caching in IBlobStore                (~2 days)
    ├── 2.4.b.3 LinqQueryModule + collectible ALC execution            (~2 days)
    ├── 2.4.b.4 IWorkflowLinqPreviewer (rollback-only SQLite sandbox)  (~2 days)
    ├── 2.4.b.5 API endpoints (validate/preview/compile)               (~2 days)
    └── 2.4.b.6 E2E demo + security review + docs                      (~2 days)
```

**Test budget (rough):**
- 2.4.a: ~70 tests (15 query + 12 execute + 18 transaction + 15 bulk + 10 shared infra)
- 2.4.b: ~40 tests (10 compiler + 8 sandbox + 10 ALC lifecycle + 6 API + 6 E2E)

---

## 7. Open questions for clarification 🤔

> Per `.github/copilot-instructions.md`: questions for the planner~

- [x] **Q1:** Should 2.4.b be tracked as a true sub-phase (with its own `Phase2-4-DatabaseModules-Linq.md` like 2.3 has Akka/Expression analysis docs), or as a post-MVP slice `2.4.P*` similar to 2.3.P1–P7?
  - Let's have 2.4.b be a post MVP slice
- [x] **Q2:** Is the "trusted author only can save linq modules in V1" gate acceptable, or do we need a fuller sandboxing story (e.g., ProcessIsolation / WASM-based execution) before *any* user can author them?
  - Initial gate acceptable. 
- [x] **Q3:** Do we want the named-connection registry (`IDbConnectionRegistry`) to live in `Workflow.Modules.Database`, or in `Workflow.Core` so other future families (e.g., a future `builtin.cache.redis`) can reuse it?
  - It can live in Workflow.Modules.Database for now; we can always extract it later if needed by other families.
- [x] **Q4:** Should the catalog `IWorkflowTableCatalog` ship with auto-discovery from registered plugin assemblies (scanning `[Table]`-attributed types), or stay manual / config-driven for V1?
  - No, The workflow table catalog should be manual for now
    - We should make sure we have a post-mvp slice to add discovery from registered databases, where we can grab the structures and store them in a versioned form for mapping.
- [x] **Q5:** Is `IBlobStore` (Phase 2.1.4) considered production-ready enough by Week 11 to host compiled assemblies? If not, a local-filesystem fallback for 2.4.b should be planned.
  - I think it's enough for now, we can always consider a local-filesystem fallback if not.
- [x] **Q6:** What's our position on **Testcontainers** in CI for 2.4.a? The original plan calls for all four providers; deferring MySQL+SQLServer to `2.4.a.P3` is the ship-on-time path but needs sign-off.
  - We can continue to defer MySQL+SQLServer to `2.4.a.P3` for now.
- [x] **Q7:** Should the snippet's `dynamic payload` be replaced with a generated typed record (recommended per §C4) or kept as `IReadOnlyDictionary<string,object?>` to match other DotFlow modules' input shape? Typed record gives nicer Roslyn errors but adds codegen complexity.
  - **Resolved: two-phase approach** — see §8.6 for full design.
    - **Phase 1 (ship with 2.4.b):** Generate a `LinqInputs` accessor struct that *wraps* `IReadOnlyDictionary<string,object?>` but exposes typed property getters derived from `ModuleSchema.Properties`. Roslyn validates field-name usage and cast types at compile time without requiring full type-name emission for arbitrary .NET types.
    - **Phase 2 (post-2.4.b):** Once an agreed restricted set of allowed schema types is locked down, graduate to a proper generated `record LinqInputs(...)` primary-constructor form for full IDE/Roslyn round-trip support.
    - Tracked as post-MVP slice **2.4.b.P1 — Typed record codegen upgrade** once the allowed-types allowlist is agreed.
- [x] **Q8:** Bulk-insert: do we want the V1 to use **linq2db's `BulkCopy`** (which already routes to `COPY` / `SqlBulkCopy` / batched INSERT per provider), or hand-roll provider-specific paths? Linq2db's `BulkCopy` is the lower-effort choice and is probably enough.
  - We want to use BulkCopy for now but we want to have MultipleRows be the default and have round trip size be configurable, also it's implementation ideally needs to be able to collect entries to write while a current one is executing.
  - We should propose a design but again keep it as separate module and phase, also please provide a proposed design. 
- [x] **Q9:** Should 2.4.a.5 expose named connections in **`appsettings.json` only**, or also via the API at runtime (CRUD)? Runtime CRUD = more attack surface; config-only = ops-friendly. Recommend config-only for V1.
  - Exposed via API at runtime as well as appsettings.json, with runtime CRUD being the default (i.e. opt-out)
- [x] **Q10:** Are there licensing concerns with Roslyn (MIT, ✅) or `Basic.Reference.Assemblies` (MIT, ✅) blocking 2.4.b? Spot-check needed before committing to Option C.
  - No licensing concerns with those two packages, both are MIT.

---

## 8. Implementation notes (concrete) 🛠️

### 8.1 Shared connection-factory shape (2.4.a.0)

```csharp
// Workflow.Modules.Database/Abstractions/IDbConnectionFactory.cs
public interface IDbConnectionFactory
{
    /// <summary>Resolve by named registration (preferred) or by raw (provider, connStr) pair.</summary>
    DataConnection Create(string connectionId, CancellationToken ct = default);
    DataConnection Create(string providerName, string connectionString, CancellationToken ct = default);
}

// Workflow.Modules.Database/Abstractions/IDbProviderRegistry.cs
public interface IDbProviderRegistry
{
    /// <summary>Maps "postgres" → ProviderName.PostgreSQL15, etc. Throws on unknown.</summary>
    string ResolveLinq2DbProvider(string moduleProviderKey);
    IReadOnlyCollection<string> KnownProviders { get; }
}
```

### 8.2 Module skeleton (2.4.a.1 — `query`)

```csharp
public sealed class DatabaseQueryModule : IWorkflowModule
{
    public string ModuleId => "builtin.database.query";
    public string DisplayName => "Database Query";
    public string Category => "Database";

    public async Task<ModuleExecutionResult> ExecuteAsync(
        ModuleExecutionContext ctx, CancellationToken ct)
    {
        var factory = ctx.Services.GetRequiredService<IDbConnectionFactory>();
        using var db = ResolveConnection(ctx, factory, ct);
        // parameter-bind from ctx.Inputs["parameters"], execute, map rows → dicts, return
    }
}
```

### 8.3 Compile-cache key (2.4.b.2)

```csharp
// Stable hash → blob key
string key = $"compiled-modules/{definitionId}/{nodeId}/" +
             SHA256(userCode + schemaVersion + selectedTables.OrderedHash()) + ".dll";
```

### 8.4 ALC lifecycle invariants (2.4.b.3)

1. **Never** return a reference rooted in the loaded assembly to engine code — copy to a `List<Dictionary<string,object?>>` first.
2. **Always** `using` the `DataConnection` before `alc.Unload()`.
3. Track ALC `WeakReference` in tests; assert collection within N GCs (per dotnet ALC docs).
4. Treat `alc.Unload()` returning but ALC still alive as a **module-level diagnostic warning**, not an error (user code may have stashed types in a static cache outside our control).

### 8.5 Sandbox preview "always-rollback" pattern (2.4.b.4)

```csharp
using var db = new DynamicWorkflowContext(ProviderName.SQLite, ":memory:");
db.BeginTransaction(IsolationLevel.Serializable);
try
{
    foreach (var t in selectedTables) /* CreateTable<T> + seed sample rows */ ;
    var result = await script.ExecuteAsync(db, inputs, ct);
    return PreviewResult.From(result);
}
finally
{
    db.RollbackTransaction(); // mitigates C6
}
```

### 8.6 Q7 resolution — Two-phase `LinqInputs` approach (2.4.b) 🧩

#### Why not a plain `IReadOnlyDictionary<string,object?>`?

Every existing DotFlow module receives inputs as `IReadOnlyDictionary<string,object?>` via `ModuleExecutionContext.Inputs`. Using that directly inside the user's Roslyn-compiled method body defeats the point of compiling with Roslyn at all — field-name typos silently compile, wrong casts only fail at runtime, autocomplete doesn't work, and the whole "design-time validation" story collapses. For the `builtin.database.linq` module to be worth building, user code must be able to write:

```csharp
// ✅ Roslyn validates "OrderId" exists & is string, catches typos at publish time
var results = db.Orders
    .Where(o => o.CustomerId == inputs.CustomerId && o.Total > inputs.MinTotal)
    .ToList();
```

…not:

```csharp
// ❌ All type errors are runtime; Roslyn doesn't help at all
var results = db.Orders
    .Where(o => o.CustomerId == (Guid)inputs["CustomerId"] && o.Total > (decimal)inputs["MinTotal"])
    .ToList();
```

#### Why not a generated `record` straight away?

The problem is the **`ModulePropertyDefinition.DataType` field is a runtime `System.Type`**, and emitting a C# `record` primary-constructor parameter requires knowing the *fully-qualified C# type name as a string* for each field. That mapping is non-trivial in several cases:

| Runtime `Type` | C# name needed in codegen | Complexity |
|---|---|---|
| `typeof(string)` | `string` | ✅ trivial |
| `typeof(int)` | `int` | ✅ trivial |
| `typeof(Guid)` | `System.Guid` | ✅ easy |
| `typeof(DateTimeOffset)` | `System.DateTimeOffset` | ✅ easy |
| `typeof(HashMap<string,string>)` | `LanguageExt.HashMap<string, string>` | ⚠️ needs LanguageExt ref in compilation |
| `typeof(object?)` / `typeof(IReadOnlyDictionary<string,object?>)` | escape-hatch types in existing schemas | ⚠️ ambiguous intent |
| Custom types from `.wfmod` plugin assemblies | Unknown at framework compile time | 🔴 needs per-plugin resolution |

We also don't yet have a declared **"allowed types for linq module schemas"** list — without that gate, a workflow author could accidentally declare a property of type `IQueryable<T>` or `Stream`, which would create dangerous or non-serialisable patterns inside the user's method body.

#### Phase 1 — `LinqInputs` accessor struct (ships with 2.4.b) ✨

The codegen emits a `readonly struct LinqInputs` alongside `DynamicWorkflowContext` in the same Roslyn compilation. Each property maps to one entry in `ModuleSchema.Properties`:

```csharp
// CODEGEN OUTPUT (emitted by IWorkflowLinqCompiler alongside DynamicWorkflowContext)
// One struct per linq node — derived from the node's ModuleSchema.Properties at publish time.

public readonly struct LinqInputs
{
    private readonly global::System.Collections.Generic.IReadOnlyDictionary<string, object?> _raw;

    public LinqInputs(global::System.Collections.Generic.IReadOnlyDictionary<string, object?> raw)
        => _raw = raw;

    // Each property below is emitted as one line per ModulePropertyDefinition.
    // The cast type is derived via a restricted Type→CSharpName mapping (see below).
    // IsRequired=true → throws KeyNotFoundException if missing (fail-fast)
    // IsRequired=false → returns default(T) if missing

    /// <summary>Customer ID to filter orders by~ 🎯</summary>
    public System.Guid CustomerId => (System.Guid)_raw["CustomerId"]!;

    /// <summary>Minimum order total~ 💰</summary>
    public decimal MinTotal => _raw.TryGetValue("MinTotal", out var v) ? (decimal)v! : 0m;

    /// <summary>Optional search term (nullable)~ 🔍</summary>
    public string? SearchTerm => _raw.TryGetValue("SearchTerm", out var v) ? (string?)v : null;
}
```

And the generated wrapper method uses it:

```csharp
// CODEGEN OUTPUT — wrapper class and method signature
namespace WorkflowRuntime
{
    public class WorkflowScript
    {
        public async System.Threading.Tasks.Task<object?> ExecuteAsync(
            DynamicWorkflowContext db,
            LinqInputs inputs,          // ← typed struct, not dynamic / IDictionary
            System.Threading.CancellationToken ct)
        {
            // --- USER CODE BEGINS ---
            // (user code here — Roslyn validates inputs.CustomerId etc. from LinqInputs)
            // --- USER CODE ENDS ---
            return null;
        }
    }
}
```

**What Roslyn can now validate at publish time:**
- ✅ Typo in property name: `inputs.CustomerrId` → `CS1061: 'LinqInputs' does not contain a definition for 'CustomerrId'`
- ✅ Wrong cast at use site: `o.Total > inputs.MinTotal` when `MinTotal` is `string` → `CS0019: Operator '>' cannot be applied to operands of type 'decimal' and 'string'`
- ✅ Missing `await` on async calls — normal Roslyn async analysis works
- ✅ `inputs.SearchTerm` can be null (nullable reference type annotation is preserved)

**What it still can't validate (Phase 2 problem):**
- ❌ If a property's `DataType` is `typeof(object?)` (the escape hatch), the cast is `(object?)` — no Roslyn help
- ❌ If a workflow author passes a value of the wrong type at runtime the cast will still throw — Phase 1 just moves type errors to module load time (cast in the struct ctor), not to the workflow graph level

#### Restricted Type→CSharpName mapping (enforced in `IWorkflowLinqCompiler`)

The compiler only emits typed properties for the following allowed types. Any `ModulePropertyDefinition` whose `DataType` is **not** in this set either:
- Falls back to `object?` (with a yellow-squiggle warning in the authoring UI: "property type not fully typed — Roslyn validation limited")
- Or is rejected at publish time if the "strict mode" option is enabled

| Allowed `System.Type` | Emitted C# type name |
|---|---|
| `typeof(string)` | `string` |
| `typeof(string?)` | `string?` |
| `typeof(int)` | `int` |
| `typeof(long)` | `long` |
| `typeof(double)` | `double` |
| `typeof(decimal)` | `decimal` |
| `typeof(bool)` | `bool` |
| `typeof(Guid)` | `System.Guid` |
| `typeof(DateTimeOffset)` | `System.DateTimeOffset` |
| `typeof(DateTime)` | `System.DateTime` |
| `typeof(TimeSpan)` | `System.TimeSpan` |
| `typeof(int?)`, etc. (Nullable<T> for the above) | `int?`, etc. |
| `typeof(object?)` or `typeof(object)` | `object?` *(escape hatch, no Roslyn benefit)* |

> 💡 **CopilotNote:** The mapping is intentionally conservative — we start with the scalar types. `List<T>`, `Dictionary<K,V>`, `Arr<T>`, `HashMap<K,V>` are deferred to Phase 2 because they require pulling in additional assembly references and the type-name generation becomes non-trivial (especially LanguageExt generics). In Phase 1 those properties just get `object?` and a warning squiggle.

#### Phase 2 — Proper `record` codegen (post-2.4.b, tracked as 2.4.b.P1) 🌷

Once the allowed-types list is agreed and stable, the upgrade path is:

1. Replace the `readonly struct` with a `record LinqInputs(...)` with a primary constructor
2. Emit each parameter with its fully-qualified type name (using the same mapping, extended to cover collections once those are allowed)
3. The struct → record migration is **non-breaking at the user-code level** — `inputs.CustomerId` still works the same way whether `LinqInputs` is a struct with a property or a record with a parameter
4. Add `IReadOnlyList<T>` support once the allowed-type set includes collections
5. Track and communicate the schema-version hash (part of the blob cache key in §8.3) so published assemblies are automatically invalidated when the allowed-types set changes

**Milestone gate for Phase 2:** The allowed-types allowlist must be ratified and the `2.4.b.P1` post-MVP slice kicked off. Until then, Phase 1 struct approach is the stable path~ 💖

---

## 9. Summary table — what changes vs. original Phase 2.4 plan 📋

| Aspect | Original Phase 2.4 | Revised (this doc) |
|---|---|---|
| Modules in scope | 4 raw-SQL modules | 4 raw-SQL modules (MVP) + 1 linq module (post-MVP) |
| Providers shipped in MVP | PG + MySQL + SQL Server + SQLite | **PG + SQLite** (others → 2.4.a.P3) |
| Stored procs | In MVP | → 2.4.a.P1 |
| Savepoints | In MVP | → 2.4.a.P2 |
| Connection auth | `connectionString` input | `connectionId` (named, preferred) + `connectionString` (raw, optional) |
| Shared infra extracted? | No | **Yes** — `Workflow.Modules.Database` + factory/registry/scope |
| Roslyn / typed authoring | Not mentioned | New 2.4.b post-MVP slice |
| `LinqInputs` payload shape | Not mentioned | Phase 1: generated accessor struct (ships in 2.4.b); Phase 2: record codegen (2.4.b.P1) |
| Sandbox preview | Not mentioned | Part of 2.4.b |
| `IBlobStore` namespace reserved | No | **Yes** (`compiled-modules/`) for forward-compat |
| Estimated MVP duration | 2 weeks | 2 weeks (unchanged) |
| Estimated total (a + b) | 2 weeks | ~4 weeks (b is opportunistic, like 2.3.P*) |

---

## 10. References

- 📄 [`new-feature-design/sqlModuleHandler.md`](./sqlModuleHandler.md) — source notes
- 📄 [`phases/Phase2-CoreFeatures.md` §2.4](../phases/Phase2-CoreFeatures.md) — original plan being revised
- 📄 [`phases/Phase2-1-PersistenceLayer.md`](../phases/Phase2-1-PersistenceLayer.md) — `IBlobStore` (used in 2.4.b.2)
- 📄 [`phases/Phase2-3-HttpAndNetworkModules.md`](../phases/Phase2-3-HttpAndNetworkModules.md) — pattern reference for "post-MVP slices" tracking + named-credential pattern
- 📄 [`Workflow.Modules/Builtin/Http/HttpRequestModule.cs`](../Workflow.Modules/Builtin/Http/HttpRequestModule.cs) — pattern reference for per-call DI resolution
- 📚 Microsoft docs — [Collectible AssemblyLoadContext](https://learn.microsoft.com/dotnet/standard/assembly/unloadability)
- 📦 `Basic.Reference.Assemblies` — portable reference-assembly bundle (recommended over `Assembly.Load("System.Runtime")`)

---

> 🌸 *uwu — this design keeps the MVP boring and shippable while leaving a beautiful "typed authoring" door open. Ping me once Q1–Q10 are answered and I'll fold the resolutions back into a proper `Phase2-4-*.md` task doc to mirror how 2.3 was split~* 💖

