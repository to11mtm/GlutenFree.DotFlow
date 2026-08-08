# Phase 5.1.6 — Spill Mode: Storage & Encryption Plan 💾

> **Status: implemented** ✅ — all thirteen work items in §9 have shipped. This document is kept as
> the decision record; §14 tracks what remains.
>
> Written before implementation because two prerequisites turned out to be **missing**, and one
> recorded decision turned out to be **unimplementable as written**.

Related: [Phase 5.1 plan](Phase5-1-StreamingDataPlane.md) · [design doc 06 §4.5](../new-feature-design/snaplogic-analysis/06-streaming-data-plane-design.md)

---

## 1. What spill is for (scope check)

Streaming keeps memory bounded — except at stages that **must** buffer: `stream.collect`,
`transform.aggregate`, and later `stream.sort` (5.1.P5). Those carry the bounded-accumulator guard
and currently **fail loudly** past `maxItems` (D10).

**Spill** is the opt-in escape valve: instead of failing, the stage offloads accumulated items to
storage and reads them back when the consumer drains. It's *scratch* data — written once, read
once, deleted at the end of the region.

That framing drives every decision below: spill is **ephemeral, sequential, and local-first**. It is
not archival storage.

---

## 2. Findings that change the plan 🔎

### 2.1 ⚠️ The recorded storage decision is not implementable

Design doc 06 §4.5 says spill "prefers **database or NATS** over S3". Investigation:

| Provider | `IBlobStore` today |
| --- | --- |
| `PostgresPersistenceProvider.Blobs` | **`null`** — not implemented |
| `NatsPersistenceProvider.Blobs` | **`null`** — not implemented (comment says "use S3") |
| `S3BlobStore` | ✅ implemented, streaming, optional SSE-AES256 |
| `SqliteBlobStore` | ✅ implemented (MemoryStream, dev/test) |
| DI fallback | `InMemoryBlobStore` ⚠️ |

So the preferred targets **don't exist**. This plan implements them (§4, §5).

### 2.2 🚨 The in-memory fallback is a trap

`Program.cs` registers `InMemoryBlobStore` as a `TryAddSingleton` fallback. If spill naively
resolves `IBlobStore` from DI, a misconfigured host would **spill memory into memory** — a memory
leak wearing the costume of a memory fix, and silent. Spill must **refuse** an in-memory blob store
with a clear error rather than "work".

### 2.3 🔓 Q1 resolved: there is no secret-variable encryption to reuse

`IsSecret` is a **designer-only flag** today. `docs/variables.md` says plainly that globals are
"stored and returned in **plaintext**". So Q1's premise ("verify the secrets mechanism suits
spill") resolves to: **nothing to reuse — build it**.

The right precedent exists elsewhere: **`IConnectionStringProtector`** (Phase 2.4.a.5) — an
abstraction in the modules layer, with `DataProtectionConnectionStringProtector` (ASP.NET Data
Protection, purpose-scoped) supplied by the host and a `NoOp` default. Spill copies that shape
exactly (§6).

### 2.4 ✅ Both new blob primitives are already available — no new packages

| Need | Primitive | Package |
| --- | --- | --- |
| NATS blobs | `NATS.Client.ObjectStore` (`INatsObjContext`/`INatsObjStore`: `PutAsync`, `GetAsync`, `DeleteAsync`, `ListAsync`) | **already in `NATS.Net` 2.7.3** |
| Postgres blobs | `NpgsqlLargeObjectManager` + `NpgsqlLargeObjectStream`, **or** a `BYTEA` table | **already in `Npgsql` 8.0.5** |

---

## 3. Storage model — three targets, one abstraction

```
ISpillStore  (Workflow.Modules/Streaming)
   ├── TempFileSpillStore     ← DEFAULT. local scratch, no config, no network
   └── BlobStoreSpillStore    ← opt-in, wraps IBlobStore (S3 / NATS / Postgres / SQLite)
                                 ⛔ rejects InMemoryBlobStore at construction
```

**Why temp-file is the default**, not blob storage:

- Spill is a *local scratch* concern. Spark and Flink both spill to local disk for the same reason:
  a network round-trip per batch turns a memory problem into a latency problem.
- It's always available — no provider configuration, so `spill: true` works out of the box.
- It genuinely removes bytes from the managed heap, which is the entire point.

**When to choose blob spill instead:** ephemeral/read-only container filesystems, or a host that
wants spill visible to operators. Documented as the exception, not the default.

---

## 4. 🐘 Postgres blob store — **BYTEA table, not Large Objects**

Both work; recommending **BYTEA** deliberately:

| | `BYTEA` table ✅ recommended | Large Objects (`lo_*`) |
| --- | --- | --- |
| Size ceiling | 1 GB / value | 4 TB |
| Streaming | whole value | true seek/stream |
| Transactions | ordinary | **`lo_*` must run inside a transaction** |
| `IBlobStore.GetAsync` returns `Stream?` | trivial | stream is bound to a live txn — must own & dispose a connection+txn, easy to leak |
| Orphans | rows, deleted by SQL | **orphan OIDs need explicit `lo_unlink`** |
| Fits existing stack | linq2db + FluentMigrator, like every other table | bypasses both |

Our blobs are module packages (MBs), compiled assemblies (KBs) and spill batches (bounded by
`maxItems`) — all far under 1 GB. Large Objects buy a ceiling we don't need in exchange for
transaction-lifetime bugs and orphan OIDs, which is a bad trade for a v1.

**Shape:** new FluentMigrator migration adding `blobs (key TEXT PK, content BYTEA, content_type TEXT,
etag TEXT, created_at TIMESTAMPTZ, size_bytes BIGINT)`; `PostgresBlobStore : IBlobStore`;
`PostgresPersistenceProvider.Blobs` returns it. `GeneratePresignedUrlAsync` → `NotSupportedException`
with a clear message (same as SQLite does today).

> Revisit Large Objects only if a real >1 GB single-blob need appears — noted as a follow-up rather
> than pre-built.

## 5. 🚀 NATS blob store — **JetStream Object Store**

Not KV. The existing provider comments "KV unsuitable for large binary" and it's right — KV values
are whole-value with history limits. **Object Store** is JetStream's chunked large-object API and is
already in the referenced package.

**Shape:** `NatsBlobStore : IBlobStore` over `INatsObjContext.CreateObjectStoreAsync(new NatsObjConfig(bucket))`;
`PutAsync`/`GetAsync` stream via `PutAsync(string, Stream)` / `GetAsync(string, Stream)`;
`DeleteAsync` → `store.DeleteAsync`; `ExistsAsync` → `GetInfoAsync` + not-found catch;
`NatsPersistenceProvider.Blobs` returns it; bucket `WF_BLOBS` created in `InitializeAsync` beside
the existing KV buckets. Presigned URLs → `NotSupportedException`.

---

## 6. 🔒 Encryption at rest — mirror the connection-string precedent

```
ISpillProtector           (Workflow.Modules/Streaming) — Protect(byte[]) / Unprotect(byte[])
   ├── NoOpSpillProtector          default; plaintext; for dev/tests
   └── DataProtectionSpillProtector (Workflow.Api) — IDataProtectionProvider,
                                     purpose "Workflow.Streaming.Spill"
```

Identical layering to `IConnectionStringProtector`: the abstraction lives where the modules can see
it, the ASP.NET dependency stays in the host, `AddDataProtection()` is **already called** in
`Program.cs`.

**Byte-oriented, not string** — spill payloads are serialized JSON batches; base64-round-tripping
them through the string API would inflate them ~33% for no reason.

**Decision — encryption is ON by default when a protector is registered.** Spill can contain any
item flowing through a region, including data derived from secret variables. Since the API host
registers a real protector, production spill is encrypted unless a host opts out.

---

## 7. Batch format

One spill session per buffering stage. Batches appended sequentially:

```
[4-byte LE length][protected batch bytes][4-byte LE length][protected batch bytes]…
```

Each batch is a JSON array of `{ i: index, o: {t,s}?, p: payload }`. Length-prefixing is what makes
the encrypted form readable back — ciphertext is opaque, so we can't scan for delimiters.

---

## 8. Lifecycle & cleanup 🧹

| Moment | Action |
| --- | --- |
| Stage completes / fails / region cancelled | `ISpillSession.DisposeAsync()` deletes its data (`try/finally` around the stage) |
| Process crash | Data is orphaned — hence the sweep ↓ |
| Host startup | `SpillCleanupHostedService` deletes spill artefacts older than a threshold (default 24 h) |

**Orphan-sweep caveat, stated honestly:** `IBlobStore` has **no list-by-prefix API** (already a
recorded follow-up in `CompiledAssemblyCache`). So the startup sweep can be complete for
**temp-file** spill (enumerate the directory) but **not** for blob spill without adding a list API.
Options: (a) ship sweep for temp-file only in v1 and document it; (b) add `ListAsync(prefix)` to
`IBlobStore`. **Recommend (b)** — it's small, it unblocks the compiled-assembly GC follow-up too,
and NATS/S3/Postgres all support prefix listing natively.

---

## 9. Work breakdown

| # | Item | Status | Landed as |
| --- | --- | --- | --- |
| 1 | `IBlobStore.ListAsync(prefix)` + implement in S3/SQLite/InMemory | ✅ | `IBlobStore.cs` (+`BlobInfo`), `S3BlobStore` (paged via continuation token), `SqliteBlobStore`, both `InMemoryBlobStore`s |
| 2 | `PostgresBlobStore` (BYTEA) + migration + provider wiring | ✅ | `BlobEntity`, `Migration_003_AddBlobs`, `PostgresBlobStore`, provider `Blobs` no longer `null` |
| 3 | `NatsBlobStore` (JetStream Object Store) + provider wiring | ✅ | `NatsBlobStore` (bucket `WF_BLOBS`), `NatsConnectionManager.ObjectStore`, bucket created in `InitializeAsync` |
| 4 | `ISpillStore`/`ISpillSession` abstraction | ✅ | `Workflow.Modules/Streaming/ISpillStore.cs` (+ `SpillSessionKey`, `SpillNotConfiguredException`) |
| 5 | `SqliteSpillStore` (single configured file, `journal_mode=MEMORY`, `synchronous=OFF`) | ✅ | `Workflow.Api/Streaming/SqliteSpillStore.cs` |
| 6 | `BlobStoreSpillStore` (⛔ refuses `InMemoryBlobStore`) + `DirectorySpillStore` (opt-in) | ✅ | both in `Workflow.Api/Streaming/`; blob spill batches records into length-prefixed segments |
| 7 | `ISpillProtector` + `NoOp` + `DataProtectionSpillProtector` + DI | ✅ | purpose `Workflow.Streaming.Spill`; on by default via `AddStreamingSpill` |
| 8 | Encryption-disabled warnings: host startup, per-execution log, API flag for the designer | ✅ | `LogSpillConfiguration`, `SpillableItemBuffer.AddAsync`, `spill` block on `GET /api/v1/status` |
| 9 | `SpillableItemBuffer` — buffer→spill→replay; wire into `BoundedAccumulator` | ✅ | memory head / spilled tail; `AccumulatorLimitBehavior.Spill` no longer throws |
| 10 | `stream.collect` + `aggregate` honour `spill: true` | ✅ | both gained a `spill` property; ceiling still enforced |
| 11 | `SpillCleanupHostedService` — execution-completion retention + abandoned threshold | ✅ | timer + startup sweep; `SweepAsync` exposed for deterministic tests |
| 12 | Designer: spill toggle + "prefer limits" helper text + encryption-off warning | ✅ | `PropertiesPanel` streaming section; `HostSpill` fed from `/status` via `SystemClient` |
| 13 | Tests | ✅ | 31 in `Workflow.Tests/Streaming/`, 6 designer tests, Docker-gated Postgres/NATS/S3 blob tests |

Items 1–3 are really **persistence** work that spill depends on; they landed as their own
reviewable step before the spill machinery.

---

## 10. Resolved decisions ✅

- [x] **Q-A — Postgres uses `BYTEA`.** Agreed: MBs not GBs; Large Objects are a trap
      (transaction-lifetime streams, orphan OIDs, a ceiling we don't need). Revisit only if a real
      >1 GB single-blob need appears.
- [x] **Q-B — add `IBlobStore.ListAsync(prefix)`.** Small, natively supported by S3/NATS/Postgres,
      and it unblocks the existing compiled-assembly GC follow-up as well as the spill sweep.
- [x] **Q-D — encryption on by default, opt-out per host, and *loud* about it.** When a protector
      is registered, spill is encrypted. A host may opt out, but that must never be quiet:
      1. **host startup** — a warning log naming the setting that disabled it;
      2. **designer** — a warning on guard-bearing stages where spill could engage;
      3. **every execution** that actually spills — one logged warning.
      *Design consequence:* the designer can't know a host-side setting today, so this needs the
      flag surfaced on an existing API response (capabilities/settings) for the client to read —
      folded into work item 10.

## 11. Revised: spill storage targets (was Q-C) 🎯

**Rejected:** an implicit `Path.GetTempPath()` default. Your point stands — on cloud nodes the
local/overlay filesystem is often slow and, more importantly, it's not somewhere an operator thinks
to provision. A default that silently lands there is a default that silently underperforms.

**New model — no implicit default; spill must be configured:**

| Target | For | Notes |
| --- | --- | --- |
| 🗄️ **SQLite spill store** | the simple, local, *configured* option | one file at a configured path (provisionable volume), not thousands of scratch files. Scratch data needs no durability, so `journal_mode=MEMORY` + `synchronous=OFF` |
| ☁️ **Blob spill store** (S3 / NATS / Postgres) | nodes with slow local disk but provisioned infrastructure | reuses `IBlobStore`; ⛔ still refuses `InMemoryBlobStore` |
| 📁 **Directory spill store** | explicit opt-in only | documented as "only when you know local disk is fast" |

`spill: true` with **no** spill store configured → **fails loudly** with a message naming the
setting, exactly as the codebase does elsewhere (non-array JSON root, `orderBy` on a stream,
`Spill` before it existed). Better a clear configuration error than a silent slow path.

> 📌 **Resolved in §13 (Q-C1):** SQLite is the recommended **local-only** option; blob spill
> (Postgres / NATS / S3) is the recommended option for **low-IOPS** nodes. A SQLite file still lives
> on local disk and still consumes local IOPS — it fixes *bloat* and *configurability*, not the
> underlying device. That distinction must be stated explicitly in the docs and the designer helper
> text, not left to inference.

## 12. Revised: retention & cleanup (was Q-E) 🧹

Retention keys off **execution completion, not file age** — your phrasing: *"spill clears X after
the execution has completed, whether successful or not."* That's better than my age-based proposal
and it's cluster-safe by construction: any host can evaluate the rule, because it depends on shared
execution state rather than "did I write this?".

Implementation consequences:

1. Every spill artefact is **tagged with its execution id** (and region index) — in the key for
   blob/SQLite targets.
2. The sweeper consults the execution history repository. Rules (final form in §13, Q-E1):
   - execution **terminal** (Completed/Failed/Cancelled) and terminal-time older than
     `Spill:RetentionAfterCompletion` (**global config**, cluster-wide) → delete;
   - execution **non-terminal** and record updated recently → **keep**;
   - execution **non-terminal** but record stale beyond `Spill:AbandonedThreshold` → delete
     (abandoned host), logged at warning;
   - execution **unknown** (record purged) → treat as terminal, using artefact age.
3. Sweep runs on a timer, not only at startup, so long-lived hosts reclaim too.

---

## 13. Resolved: Q-C1 and Q-E1 ✅

- [x] **Q-C1 — SQLite is the recommended *local-only* option; blob (Postgres/NATS/S3) is the
      recommended option for low-IOPS nodes.** Both are first-class; neither is "the" default. The
      documentation must state the distinction explicitly rather than leaving an operator to infer
      it, because the failure mode (slow local disk) is invisible until it hurts.

      Concretely, the docs and the designer helper text say:

      | If your host has… | Use | Because |
      | --- | --- | --- |
      | fast local disk (bare metal, local NVMe, generous provisioned IOPS) | 🗄️ **SQLite spill** | lowest latency, no network hop, one provisionable file |
      | slow/throttled local disk (small cloud nodes, overlay/ephemeral FS) | ☁️ **Blob spill** (Postgres / NATS / S3) | spill leaves the node entirely for infrastructure an operator already sizes |

      This makes the earlier caveat a documented *selection criterion* rather than a wart: SQLite is
      not a fix for a slow device, and we say so where the choice is made.

- [x] **Q-E1 — option (b): a second, much longer *abandoned* threshold.** Non-terminal executions
      normally keep their spill forever, but if the execution record hasn't been updated within
      `Spill:AbandonedThreshold` (default conservative — days), the sweeper treats it as abandoned
      and reclaims. No heartbeat machinery, no new runtime state, and the crash-leak is bounded.

      Sweeper rules, final:

      | Execution state | Age check | Action |
      | --- | --- | --- |
      | terminal (Completed/Failed/Cancelled) | terminal-time > `Spill:RetentionAfterCompletion` | 🗑️ delete |
      | terminal | within retention | keep |
      | non-terminal | record updated within `Spill:AbandonedThreshold` | keep |
      | non-terminal | record stale beyond `Spill:AbandonedThreshold` | 🗑️ delete (abandoned) |
      | unknown (record purged) | artefact age > `Spill:RetentionAfterCompletion` | 🗑️ delete |

      Deleting an abandoned execution's spill is logged at warning level with the execution id, so a
      reclaimed leak is also a visible signal that a host died.

## 14. Remaining questions ❓

None — the plan is fully specified and implemented.

## 15. Notes from implementation 📝

Things that only became clear while building, recorded so the next reader doesn't rediscover them:

1. **NATS object *names* are permissive; only *bucket* names are restricted.** `NatsObjContext`
   validates buckets against `\A[a-zA-Z0-9_-]+\z`, but object names only have to be non-empty —
   nats.net base64url-encodes the name into the meta subject. A probe confirmed `a/b/c.json`,
   `spill:exec/1` and `x=y` all pass. So spill keys need **no** escaping, and the key-mangling
   scheme originally sketched here was dropped.

2. **The NATS Object Store keeps a tombstone after delete.** `ListAsync` therefore filters
   `meta.Deleted`; a sweep that didn't would loop forever trying to delete what's already gone.
   There's a test for exactly this.

3. **Blob spill batches records into segments.** One blob per item would mean one network round
   trip per item, which would make spill slower than the OOM it prevents. Records are
   length-prefixed rather than delimited, because once encryption is on a record is arbitrary bytes
   and there is no byte value safe to use as a separator.

4. **`ExecutionRecord` has no "last updated" column**, so the abandoned-execution check measures
   staleness from `StartedAt`. That's conservative in the right direction: it can only ever *delay*
   reclaiming a genuinely long-running job, never hasten it.

5. **`SystemClient` is resolved from the container, not `@inject`ed, in `Designer.razor`.** The
   spill-status fetch is genuinely optional — the designer works fine without it — and a hard
   injection made every existing designer-page test fail on a service it had no reason to register.
   `GetService` is the accurate expression of "optional".

6. **Spill did not become "unlimited".** `BoundedAccumulator` still enforces the ceiling when
   `OnLimit == Spill`; spill changes *where* buffered items live, not whether there's a bound. A
   guard test pins this.

