# Plan 08 — Secret variables at rest (follow-up to §10.6)

**Status:** decided 2026-11-10/11 (asked in three rounds); not implemented yet.

## Where secrets sit at rest today

| Store | Today | After this plan |
| --- | --- | --- |
| Workflow definitions | Safe: a secret variable can't carry an initial value | unchanged |
| Run history, variable-update records, logs, live events | Safe: redacted by name and value (§10.6) | unchanged |
| State snapshots | Memory only (`InMemoryExecutionStateStore`; never written or read back) | unchanged (the worklog note was overstated) |
| **Variable store** (global / workflow scope: Settings, "Save for future runs", `/api/v1/variables`) | **Plain text** in SQLite / Postgres / NATS, with **every past version**. Values are **returned to anyone with read access**. No notion of "secret". | Secret variables encrypted, **write-only**, no past values kept |
| Data Protection keys (also used for DB connection strings) | ASP.NET defaults: user profile / `~/.aspnet` — lost on a container restart, different per server | A clear choice: persistence store / folder / defaults (dangerous) |

## Decisions

| Question | Decision | Not chosen |
| --- | --- | --- |
| How a stored variable becomes secret | **Per variable**, a mode: **Not secret** · **Secret** (the flag; the default for new secrets) · **Follow workflow declarations** (secret while any workflow declares a variable of that name `IsSecret`; for workflow scope, that workflow). The UI warns that "follow" can change when a workflow is edited. | the flag only; workflow declarations only; a server-wide mode |
| "Follow" variable no longer declared secret | **It stays secret** (encrypted, write-only); the UI says so and offers an explicit switch to Not secret (re-enter the value) | decrypt back to plain |
| Protection | **Encrypt with ASP.NET Data Protection now** (like DB connection strings). **Secret providers later**: a pluggable seam (env vars, Key Vault…) designed and documented, not built | external providers only |
| Reading back | **Nobody: write-only.** The API and UI show `••••••` and when / who last set it; values can be replaced or deleted. Runs get the real value | Admin reveal; unchanged |
| History | **Keep who / when, keep no old values** (not even encrypted). Marking a variable secret **purges its earlier plain-text versions**, after a **confirmation dialog** in the UI on the switch / save | keep encrypted history; keep only latest with no who/when |
| Key storage | **Three clear options**: `DataProtection:KeyStorage` = `PersistenceStore` (default — the key ring lives in the configured blob store, shared by every server) · `Folder` (`DataProtection:KeysPath`, a mounted volume) · `Default` (ASP.NET's, **dangerous**: logged loudly). Warn when the persistence store is in-memory. Show the active option in Settings | — |

## Design sketch

- **Secret metadata** (mode, set-by, set-at, and an audit of who/when for each change) is kept per
  scope+name in a small document in the blob store (like trusted publishers). This needs no schema
  change in the three variable stores.
- **`SecretVariableStore`** wraps the registered `IVariableStore`:
  - on set: when the variable is secret, encrypt (`dp:v1:` prefix + ciphertext), then **delete the
    earlier versions** and write the new one (the store keeps one version for a secret); record
    who/when in the metadata audit;
  - on get / get-all (used by runs): decrypt;
  - history for a secret: the audit (who / when), no values.
- **API** (`/api/v1/variables`):
  - list / get / history mask secret values (`••••••`, `isSecret`, `secretMode`, `lastSetBy`,
    `lastSetAt`);
  - PUT takes `secretMode`;
  - switching to secret returns what will be purged unless `confirmPurge=true`, which the UI's
    confirmation dialog sends;
  - switching a secret to Not secret requires a new value.
- **Engine** "Save for future runs": a workflow-scope write of a variable the workflow declares
  `IsSecret` creates a **Secret** variable automatically.
- **Follow workflow declarations:**
  - evaluated against saved workflows (cached; refreshed on workflow save);
  - when a save makes a following variable secret, it's encrypted and purged then. The designer
    asks first (confirmation on save, listing the stored values that will be purged);
  - once secret, it stays secret.
- **Keys:** `DataProtection:KeyStorage` wires `PersistKeysTo…`: a blob-backed `IXmlRepository`, a
  folder, or nothing. The status endpoint and Settings show which, with the warning for
  `Default` / in-memory.
- **Secret providers (later):** `ISecretValueSource` (resolve a reference such as
  `env:NAME` / `vault:…` at run time). Documented only; the encrypted store is the first source.

## Parts

1. Key storage options (+ status, warnings, docs). Benefits connection strings immediately.
2. Encrypted, write-only secret variables (metadata, store wrapper, API, engine auto-mark, follow-mode, purge + confirmation contract).
3. UI: mode selector with warnings, masked values, who/when history, confirmation dialogs (Settings + designer save).

## Tests to write first

- Keys: each option wires the expected key storage; `Default` and an in-memory store warn; a
  second app instance sharing the store can decrypt.
- A secret variable is stored encrypted (the raw store holds no plain text), runs get the real value,
  and the API never returns it.
- Marking a variable secret purges its plain-text versions only with confirmation; history shows
  who/when only.
- "Save for future runs" of a declared secret is stored encrypted.
- Follow mode becomes secret when a workflow declares it, and stays secret when it stops.
- UI: mode selector, warnings, masked display, confirmation dialogs.
