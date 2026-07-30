# 🧬 Linq Studio — Typed LINQ Query Authoring

Linq Studio (`/linq-studio`) is the dedicated authoring surface for the
`builtin.database.linq` module. It answers the "do I use the Script Editor for this?"
question: **no** — Script Studio targets the scripting modules (JS/C#-script/Lua), while
Linq Studio compiles a *typed* C# LINQ method body against your database tables and
publishes the compiled assembly key back onto the node.

## Opening it

- **Standalone (sandbox mode)** — the **🧬 Linq** button in the top bar (or `/linq-studio`
  directly). Explore tables, validate, and preview freely; **Publish is disabled** without a
  node context (a hint banner explains this).
- From the designer: select a `builtin.database.linq` node → the properties panel shows
  **Open in Linq Studio →**. Your node's current code, connection, and table selection are
  carried over; **Publish to node** returns you to the designer and applies everything as a
  single undoable edit.

## Layout

| Area | What it does |
| --- | --- |
| Toolbar | Connection picker, ➕ Connection (define a new named connection), ✅ Validate, 👀 Preview, 🗄 Run on connection, 🚀 Publish to node |
| 📚 Tables (left) | The connection's table catalog: check tables to expose them as `db.TableName`; 📥 Import from connection introspects the live schema; ➕ Define a table adds one manually (name + columns grid) |
| Editor (center) | Monaco (textarea fallback) for the C# query body, diagnostics, preview grid |
| 🧭 Context reference | Selected tables and inputs as click-to-insert chips (`db.orders`, `.total`, `inputs.MinTotal`) |
| 🧬 Inputs (right) | Typed inputs your query reads as `inputs.Name`; sample values feed Preview |

## Writing queries

```csharp
return db.orders.Where(o => o.total > inputs.MinTotal).ToList();
```

- Tables surface as typed `db.TableName` collections; **column identifiers are verbatim**
  (a column `total` is `o.total`, not `o.Total`) — the reference chips show the exact names.
- Each table's **row type is named after the table itself, with no namespace prefix** — so
  `new FooBar { A = "1" }`, `db.GetTable<FooBar>()`, and `List<FooBar>` all work. (Internally
  the generated POCO is `WorkflowRuntime.Gen_FooBar`; an alias makes the friendly name work.)
  The 🧭 reference panel has a **`new FooBar { }`** chip that inserts the right thing.
- Referencing an unknown type produces a `WFLINQ010` hint listing the tables actually in
  scope — the usual cause is a table that isn't checked in the Tables panel.
- End with a `return`.

## Seeing the SQL 🧾

Every preview shows the **SQL** the body produced — captured from linq2db as it executes, so
it's the real thing (including `INSERT`/`UPDATE`/`DELETE`, not just the final `SELECT`).

If you return a query **without materialising it** (`return db.orders.Where(…);`), that's no
longer an error: the query is rendered via `ToSqlQuery()` and shown, with a `WFLINQ021` hint
reminding you to add `.ToList()` (or `.First()`, `.Count()`, …) to actually run it.

## Running against the real connection 🗄

**👀 Preview** runs against generated sample data in an in-memory SQLite sandbox — fast and
safe, but the dialect is SQLite.

**🗄 Run on connection** runs the exact same body against the **real** named connection, so
you get real rows and dialect-correct SQL. Everything happens inside a transaction that is
**always rolled back** — inserts, updates, and (on providers with transactional DDL) schema
changes are discarded, and a `WFLINQ020` notice confirms it on every run. It's gated by the
same trusted-author check as Publish. Read queries still touch live data, so be mindful of
long scans on production.
- **Validate** compiles and shows diagnostics with line/column info; **Preview** runs the
  query against generated sample data; **Publish** compiles, stores the assembly, and writes
  `userCode`, `connectionId`, `tableNames`, `inputDefs`, and `compiledAssemblyKey` to the node.

## Table catalog

The catalog is per-connection and powers the generated POCOs:

- `GET/PUT/DELETE /api/database/catalog/{connectionId}[/{table}]` — list, define, remove.
- `POST /api/database/catalog/{connectionId}/import` — introspect the live connection.

Manually defined tables need a name and at least one column (type + nullability); imported
tables come straight from the provider's schema.

**Keys and generated values:** each column can be marked **🔑 pk** (primary key) and/or
**⚡ auto** (database-generated: identity / serial / SQLite `INTEGER PRIMARY KEY`). Marking a
column auto implies it's a key and not nullable. These become `[PrimaryKey]` / `[Identity]` on
the generated POCO, so `InsertWithIdentity` / `InsertWithInt32Identity`, key-based `Update`,
and `Delete` behave as linq2db expects (identity columns are skipped on insert). Imports fill
both flags automatically where the provider reports them.

**Editing a definition:** ✏️ on any catalogued table loads it into the form below (name,
schema, and every column) — change types, add or drop columns, then **Save changes**. No need
to delete and recreate. Renaming saves under the new name and removes the old entry, and any
selection you had follows the rename. **Cancel** drops back to "define a new table" mode.

## Connections

No connection yet? **➕ Connection** in the toolbar defines one without leaving the studio.
Pick the provider and the form adapts to it — you never have to remember ADO.NET syntax:

| Provider | Guided fields |
| --- | --- |
| **PostgreSQL** | Host\*, Port (5432), Database\*, Username\*, Password, SSL mode, Pooling, Connect timeout |
| **SQLite** | Database file\* (path or `:memory:`), Mode, Cache, Enforce foreign keys, Password (SQLCipher) |

\* required. A live **Preview** shows the composed connection string with secrets masked, and
optional settings left at their defaults are omitted to keep it tidy. Prefer to paste a full
string? Tick **Advanced — enter a raw connection string** (toggling carries your values across
in both directions).

Saving `POST`s to `/api/database/connections/`, selects the new connection, and loads its
(empty) catalog so you can import or define tables right away. Connection strings are stored
server-side (encrypted at rest) and always masked in responses.

## Related

- [Database modules](database-modules.md) — the `builtin.database.*` module family.
- [Designer](designer.md) — node editing, property binding, and the studios' round-trips.
