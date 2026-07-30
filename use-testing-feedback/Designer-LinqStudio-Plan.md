# Linq Studio — Typed LINQ Query Authoring (Design & Plan)

> 📋 Response to stakeholder confusion (2026-07-29): *"for the linq query functionality, it is
> unclear whether the existing script editor is sufficient or a new one is required… users may
> want to define classes with attribute mappings for tables… make it easy to discover or
> manually define tables for a connection and expose those in the script editor, or create a
> superset of the script editor for linq query module composition."*

## What exists today (survey — this is why users are confused)

The **backend authoring pipeline is already complete** (Phase 2.4.b), but has **zero UI**:

- `builtin.database.linq` runs a **publish-time-compiled** assembly: its designer properties are
  just `connectionId`, **`compiledAssemblyKey`** ("from POST /api/database/linq/compile" — i.e.
  today users must curl the API and paste a blob key!), `inputs`, `timeoutSeconds`.
- Authoring endpoints exist: `POST /api/database/linq/validate | preview | compile`
  (`LinqAuthoringRequest`: user code + tables inline **or** `tableNames` resolved from the
  catalog + typed inputs). Preview compiles and runs against **generated sample data** — no DB
  writes.
- **Table catalog** exists (`IWorkflowTableCatalog`: List/Upsert/Remove +
  `CatalogSchemaImporter`): `POST /api/database/catalog/{connectionId}/import` introspects a
  live connection. But there are **no list/upsert/remove endpoints** and no UI.
- User code sees tables as **typed properties on `DynamicWorkflowContext`**
  (`db.Users` → `IQueryable<Users>`, POCOs generated from catalog column metadata) plus a typed
  `LinqInputs` struct. **Users never need to hand-write attribute-mapped classes** — that's
  exactly what the codegen does from table metadata (plugin CLR types are supported for
  advanced cases via `ClrTypeName`/`AssemblyName`).
- **Script Studio is unrelated**: it edits sandbox *workflow scripts* (JS/C#/Lua via
  `/api/v1/scripts`), knows nothing of connections/tables/compiled keys. Reusing it directly
  would conflate two different backends and mental models.

**Conclusion:** the right answer to "script editor or new one?" is a **dedicated Linq Studio**
that *reuses Script Studio's building blocks* (Monaco `CodeEditor`, diagnostics/test-runner
layout patterns, the handoff mechanism) but drives the `/api/database/linq/*` pipeline.

## Design decisions (proposed)

- **D1 — Dedicated surface, shared parts.** A **Linq Studio page** (`/linq-studio`), opened
  from a linq node's properties panel via **"Open in Linq Studio →"** (same handoff pattern as
  Script Studio's D9 round-trip: edit → return to designer with results applied to the node).
- **D2 — The key disappears.** Studio's **Publish** button runs `compile` and writes
  `compiledAssemblyKey` + the authoring state back onto the node in one undoable step. Users
  never see or paste a key again (it remains visible read-only for debugging).
- **D3 — Authoring state lives on the node.** Add optional schema properties to
  `LinqQueryModule`: `userCode` (Code editor), `tableNames` (Json) — so the Studio can
  round-trip and MA002 unknown-property validation stays quiet. Runtime ignores them (compiled
  assembly is authoritative); validate warns when code changed since last compile (post-MVP).
- **D4 — Catalog management API + UI.** New endpoints over the existing catalog:
  `GET /api/database/catalog/{connectionId}` (list), `PUT …/{tableName}` (manual upsert),
  `DELETE …/{tableName}` — plus the existing `import`. Studio's **Tables panel** lists the
  connection's catalog with per-table columns, an **Import from connection** button
  (introspection), and a **manual table editor** (name/schema/columns grid) for
  define-by-hand cases.
- **D5 — Tables exposed in the editor.** A **Context reference panel** (like Script Studio's
  API reference): selected tables render as `db.TableName` entries with their generated POCO
  property list (name + CLR type + nullability) — click to insert. Monaco completions for
  `db.` are post-MVP.
- **D6 — Validate / Preview loop in-Studio.** Buttons wired to the existing endpoints:
  diagnostics list with line/column (click → Monaco cursor), preview grid of sample rows +
  duration. Inputs panel defines typed inputs (name/type/required) and sample values for
  preview — mirrors `LinqInputDto`.

## Slices

- [x] **L0 — Catalog management API**: list/upsert/remove endpoints + DTO mirrors, tests.
- [x] **L1 — Node schema + client plumbing**: `userCode`/`tableNames` optional properties on
      `LinqQueryModule`; UI `DatabaseLinqClient` (validate/preview/compile/catalog) + DTOs;
      `LinqStudioHandoff` state (framework-free).
- [x] **L2 — Studio shell**: `/linq-studio` page — Monaco editor, connection picker (from
      named connections API), Tables panel (catalog list + import + manual editor), Inputs
      panel, "Open in Linq Studio →" on linq nodes + round-trip apply.
- [x] **L3 — Validate/Preview/Publish**: diagnostics panel, preview grid, publish writing
      `compiledAssemblyKey`+state to the node (single undoable apply on return).
- [x] **L4 — Context reference panel + docs**: `db.Table` reference with column insert;
      `docs/linq-studio.md` + designer.md pointer; plan sync.

## Questions — RESOLVED ✅ (2026-07-29)

- [x] **Q1 (D1):** Full page (`/linq-studio`, like Script Studio) or an in-designer modal
      (like the ƒx/🛡️ builders)? *Proposed: full page — the tables/inputs/preview surfaces are
      too much for a modal, and the Script Studio handoff pattern is proven.*
  - **RESOLVED: Full page.**
- [x] **Q2 (D4):** Manual table editor scope — a structured columns grid (name/type/nullable
      rows), or JSON-paste-only for MVP? *Proposed: structured grid; it's the discoverability
      ask.*
  - **RESOLVED: Structured grid.**
- [x] **Q3 (D3):** Store `userCode` on the node (workflow JSON grows, but self-contained
      round-trip) vs a server-side authoring store? *Proposed: on the node — simplest,
      versioned with the workflow, matches how script nodes store code.*
  - **RESOLVED: On the node.**
- [x] **Q4 (D5):** Monaco completions for `db.`/columns in MVP, or reference-panel-only with
      completions post-MVP? *Proposed: panel-only MVP (completions need a Monaco language
      service against generated types — meaningful extra work).*
  - **RESOLVED: Panel-only MVP. Post-MVP slice L.P1 (below) tracks Monaco completion support.**

## L5 — Standalone sandbox + connection management (stakeholder follow-up, 2026-07-29)

Users asked: (a) can connections be defined from the UI for Linq Studio, and (b) can the
studio be reached as a sandbox separate from the workflow composer (like Script Studio)?
The backend already has full connection CRUD (`/api/database/connections`), so both are
UI-only slices.

- [x] **L5a — Connections via UI**: ➕ New connection modal in the Linq Studio toolbar
      (id / provider (postgres/sqlite) / connection string / display name) → `POST
      /api/database/connections/`; on save the new connection is selected and its catalog
      loads. Client `UpsertConnectionAsync`/`DeleteConnectionAsync` + `DbConnectionUpsertDto`.
- [x] **L5b — Standalone sandbox exposure**: 🧬 Linq nav button in the TopBar (alongside
      💻 Scripts) → `/linq-studio`; a sandbox-mode hint when opened without a node context
      (validate/preview work freely, Publish stays gated); docs update.

## L6 — Provider-guided connection setup (stakeholder follow-up, 2026-07-29)

Raw connection strings were still the hard part of L5a. The modal now composes the string
from provider-specific fields.

- [x] **L6a — `ConnectionStringComposer`** (framework-free, `Linq/State/`): per-provider field
      definitions (key/label/kind/required/default/options/help), `Defaults`, `MissingRequired`,
      `Build` (quotes separators, omits values left at defaults), `Parse` (round-trips back into
      fields), `ProviderLabel`. Postgres: Host/Port/Database/Username/Password/SSL mode/Pooling/
      Timeout. SQLite: Data Source/Mode/Cache/Foreign Keys/Password.
- [x] **L6b — Guided modal**: fields swap with the provider, secrets render masked, required
      markers + per-field validation messages, a masked live **Preview** of the composed string,
      and an **Advanced — raw connection string** toggle that carries values across both ways.

## L7 — Row types usable by table name (stakeholder bug, 2026-07-29)

`db.FooBar.Select(a => a.A)` worked but `new FooBar { A = "1" }` failed with CS0246: generated
POCOs are emitted as `WorkflowRuntime.Gen_FooBar`, a name users never see.

- [x] **L7a — Entity aliases in codegen**: `DynamicContextCodeGenerator.GenerateEntityAliases`
      emits `using FooBar = global::WorkflowRuntime.Gen_FooBar;` (and the plugin FQN for plugin
      tables) inside the runtime namespace, skipping reserved/duplicate names. So the table's
      own name is the row type — no namespace prefix, works in `new`, generics, and locals.
- [x] **L7b — `WFLINQ010` scope hint**: any CS0246/CS0103 now also yields a hint naming the
      tables in scope (`db.X (row type 'X')`), or "no tables are selected" when none are.
- [x] **L7c — UI/doc surfacing**: editor help text + a `new FooBar { }` reference chip;
      `docs/linq-studio.md` documents the naming rule and the hint.

## L8 — Editing a table definition in place (stakeholder request, 2026-07-29)

- [x] **L8 — Edit an existing catalogue entry**: ✏️ per table loads name/schema/columns into
      the (now dual-purpose) definition form — "Edit table 'x'" title, Save changes, Cancel.
      Same-name saves are a plain upsert; renames upsert the new name, remove the old entry,
      and carry the table's selection across. Removing or switching connections while editing
      resets the form. A schema field was added to the form (previously always null).

## L9 — SQL preview, live preview, and key/identity columns (stakeholder request, 2026-07-29)

- [x] **L9a — Primary key / identity in the table designer**: `WorkflowColumnMetadata` gains
      `IsPrimaryKey`/`IsIdentity`; generated POCOs emit `[PrimaryKey]`/`[Identity]` so
      `InsertWithIdentity`, `Update`, and `Delete` by key work; catalog DTOs + the designer's
      column grid get 🔑/⚡ toggles; the schema importer fills them where the provider reports
      them (SQLite `pk`/rowid-alias, Postgres key + `is_identity`/serial default).
- [x] **L9b — SQL preview**: linq2db tracing captures every statement the body executes during
      preview (dialect-correct), and a body that returns an unmaterialised `IQueryable` is no
      longer an error — its SQL is rendered via `ToSqlQuery()` with a "add `.ToList()` to run
      it" hint. Surfaced as a **SQL** panel in the studio.
- [x] **L9c — Preview against the real connection**: `POST /api/database/linq/preview-live`
      resolves the named connection, runs the body inside a transaction that is **always rolled
      back**, and returns rows + the captured SQL. Trusted-author gated like compile; the studio
      gets a **🗄 Run on connection** button with a clear rollback notice.

## L10 — Persisted connection store (stakeholder request, 2026-07-29)

Connections defined in the UI vanished on restart (the registry was in-memory only; the
"persisted registry" was a long-standing 2.4.a.5 TODO).

- [x] **L10 — SQLite-backed `IDbConnectionRegistry`**: `SqliteDbConnectionRegistry` +
      `ConnectionStoreOptions` (`Workflow:Database:ConnectionStore` — `Enabled`, `Path`);
      registered via a resolve-time factory so the in-memory registry stays the default and
      hosts opt in (enabled in `appsettings.Development.json`). Config-declared connections are
      re-seeded each start as `origin=config`; UI-created ones persist as `origin=user`.
      Connection strings go through the existing `IConnectionStringProtector` seam (the API's
      Data-Protection protector encrypts at rest; undecryptable rows surface disabled instead of
      breaking the listing). Studio gained a 🗑 to forget a saved connection.

## L11 — Oracle connections (stakeholder request, 2026-07-29)

Scope is deliberately narrow: the **database module family + Linq Studio authoring UX** learn
Oracle. `Workflow.Persistence` is untouched (DotFlow's own state never lives in Oracle).

- [x] **L11a — Provider plumbing**: `linq2db.Oracle` package; `"oracle"` →
      `ProviderName.OracleManaged` in `DefaultDbProviderRegistry`; Oracle SQL-type map for
      generated POCOs (NUMBER/VARCHAR2/CLOB/TIMESTAMP WITH TIME ZONE/…), including a
      `Normalise` fix so `TIMESTAMP(6) WITH TIME ZONE` keeps its suffix.
- [x] **L11b — Catalog import**: `ALL_TAB_COLS` + PK constraint / identity introspection for
      Oracle in `CatalogSchemaImporter`, dispatched off the resolved provider.
- [x] **L11c — Studio UX**: guided Oracle connection fields (Host/Port/Service name/User Id/
      Password composing an EZConnect `Data Source`), plus a provider-aware column-type list in
      the table designer so Oracle users pick Oracle types.

## L12 — Structural Database Transaction (stakeholder request, 2026-07-29)

> **Ask:** make `builtin.database.transaction` behave like For Each / While / Try Catch in the
> designer — drop-scaffolded skeleton, dashed body edges, and a region "box" around the nodes
> that run inside the transaction.

### What exists today (survey)

| Piece | Today |
| --- | --- |
| `builtin.database.transaction` | **Not structural.** It has no body port; it executes an `operations` JSON array (`[{ sql, parameters?, parameterSets?, expectLastInsertId? }]`) itself, inside one scope, and returns `success/results/error/durationMs`. |
| Loops / Try-Catch | **Engine-backed structural nodes**: `LoopExecutorActor` / `TryCatchExecutorActor` own a *sub-graph scope* (`ComputeBodyScope`/`ComputeScope`), `WorkflowExecutor` pre-marks those nodes skipped and spawns the actor, which runs them. |
| Designer | `NodePorts.StructuralPorts` + `StructuralRegions` + `DropZones` drive dashed edges, region halos, and drop-scaffolding — all keyed off structural **port names** (`loopBody`/`try`/`catch`/`finally`). |
| DB modules | `builtin.database.query/execute/bulkinsert` each open their **own** connection per execution; `IDbTransactionScope`/`DefaultDbTransactionScope` exist but are used only *within* the transaction module. |

So the visual layer is generic and cheap to extend — the semantics are the real work: for a body
sub-graph to be transactional, the nodes inside it must run on the **same open connection and
transaction** as the owner node.

### Options

- **Option A — real structural transaction (recommended).**
  Add `transactionBody` + `committed` / `rolledBack` ports; a `TransactionExecutorActor` (mirroring
  `TryCatchExecutorActor`) opens an `IDbTransactionScope`, runs the body scope, then commits — or
  rolls back on the first failure and routes to `rolledBack`. DB modules inside the body enlist via
  a new ambient registry keyed on `(ExecutionId, connectionId)` instead of opening their own
  connection. Designer gets scaffolding + region boxing for free once the ports exist.
  *Touches:* engine (new actor + executor wiring), `Workflow.Modules.Database` (ambient scope
  registry + enlistment in 3 modules), module schema, designer state/UI, validator, docs.
  The existing `operations` property stays supported (declarative mode) so nothing breaks.
- **Option B — designer-only boxing.** Cheap, but there are no body nodes to box: it would draw a
  transaction "region" that has no runtime meaning. **Not recommended — actively misleading.**
- **Option C — designer compiles the body into `operations` at save time.** No engine change, but
  body nodes wouldn't really execute (no outputs, no per-node status, no non-SQL nodes allowed).
  Leaky; only worth it as a stop-gap.

### Questions — RESOLVED ✅ (2026-07-29)

- [x] **Q1:** **Option A** — engine-backed, a real transaction spanning the body sub-graph.
- [x] **Q2:** **Auto-enlist by matching `connectionId`.** A body node on a different connection (or
      using a raw connection string) runs on its own connection; the designer warns that it won't
      be rolled back.
- [x] **Q3:** Any body failure → **rollback + route to `rolledBack`** (mirrors `catch`; the
      workflow continues rather than failing). `TransactionFailed` is reserved for infrastructure
      failures (opening/committing the transaction itself).
- [x] **Q4:** **Nested transactions rejected** — blocking designer validation; loops and try/catch
      inside a transaction body are allowed.

### Implementation ✅

- [x] **L12a — Core/engine contracts**: `TransactionRequest` (Core), `ModuleResult.Transaction` +
      `WithTransaction`, `TransactionMessages` (`TransactionCompleted`/`TransactionFailed`/
      `NodeTransactionExecutionRequested`), `NodeExecutor` forwarding (FIFO before completion).
- [x] **L12b — `TransactionExecutorActor`**: opens the scope, registers it ambiently, runs the body
      via `SubGraphExecutor`, commits → `committed` / rolls back → `rolledBack`, always cleans up.
      `WorkflowExecutor` mirrors every try/catch touch-point (pending maps, body-scope pre-skip,
      completion routing, `IsWorkflowComplete`).
- [x] **L12c — Layering-safe enlistment**: `IAmbientDbTransactions` + `IWorkflowTransactionScopeFactory`
      live in `Workflow.Modules.Abstractions` (object-typed, provider-neutral) so **no new
      `Workflow.Engine` → `Workflow.Modules.Database` reference**; the database project implements
      both. `query`/`execute`/`bulkinsert` reuse the ambient connection (and don't dispose it) when
      their `connectionId` matches.
- [x] **L12d — Designer**: `transactionBody`/`committed`/`rolledBack` + `input` ports, structural
      (dashed) edges, amber 💼 region box, palette drop-scaffolding + "💼 transaction from here"
      drop zone, canvas menu skeleton, properties hint, nested-transaction error + cross-connection
      warning in `GraphValidator`.
- [x] **L12e — Compatibility**: declarative `operations` mode is unchanged; the structural request
      is emitted only when `operations` is empty.
- [x] **L12f — LINQ steps in the body** (follow-up): `LinqQueryModule` enlists in the ambient
      transaction via `DataOptions.UseTransaction(provider, ambient.Transaction)`, so the compiled
      query's generated context shares the open connection + transaction (never disposing it).
      Canvas menu gained **Insert transaction skeleton (Linq step)** to scaffold the body starting
      with a `builtin.database.linq` node.

## Post-MVP (tracked, not in scope now)

- [ ] **L.P1 — Monaco completions for `db.` / columns**: a completion provider fed by the
      selected tables' generated POCO shapes (table properties on `db.`, column properties on
      entity variables). Requires extending `monaco-interop.js` with a registerable completion
      source keyed to the Linq Studio editor instance.

---

*Created 2026-07-29. Update statuses/checkboxes as items land.*
