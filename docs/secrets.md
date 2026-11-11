# Secrets

DotFlow keeps a few kinds of secret:
- **secret variables**: variables marked secret in a workflow, or stored as global / workflow
  variables;
- **database connection strings**;
- **spill files**, when spill encryption is on.

This page covers how they're protected at rest. For how secret values are kept out of history,
logs and live events, see [variables](variables.md); plan 03 §10.6 has the design notes.

> Design and decisions: [plan 08](architecture/plans/08-secrets-at-rest.md) (implemented
> 2026-11-11).

---

## The encryption keys

Everything above is encrypted with **ASP.NET Data Protection**. Its keys decrypt every stored
secret, so where they live matters: **lose the keys and those secrets can't be read any more.**

Choose with `DataProtection:KeyStorage`:

| Option | Where the keys live | Safe for |
| --- | --- | --- |
| **`PersistenceStore`** *(default)* | One key ring in the configured persistence store's blobs (`settings/dataprotection-keys.xml`), shared by every server | restarts, containers, several servers, as long as the persistence provider is durable |
| **`Folder`** | `DataProtection:KeysPath`, e.g. a mounted volume | restarts, and several servers if they share the folder |
| **`Default`** ⚠️ | ASP.NET's own default: the user profile, or `~/.aspnet/DataProtection-Keys` | a single machine whose profile survives. **Dangerous** in containers or with more than one server: keys can vanish on a restart, or differ between servers |

```jsonc
"DataProtection": { "KeyStorage": "PersistenceStore" }
// or
"DataProtection": { "KeyStorage": "Folder", "KeysPath": "/var/dotflow/keys" }
```

- The server logs a warning at startup, and `GET /api/v1/status` reports
  `dataProtection: { keyStorage, location, warning }`, when the setup can lose keys. That happens
  with `Default`, or with `PersistenceStore` when the persistence store keeps blobs in memory.
- `Folder` without `KeysPath` refuses to start.
- **Upgrading:** the first time `PersistenceStore` is used, DotFlow imports the keys from ASP.NET's
  default folder on that machine. Connection strings encrypted before the switch stay readable.
- **Back up the key ring** (the blob, or the folder) along with the database. A database backup
  without its keys can't decrypt its secrets.
- Keys are created about every 90 days. If two servers create one at the same instant, one can be
  lost; that window is small and it's rare.

---

## Secret variables at rest

Global and workflow variables live in the variable store; "Save for future runs" writes there too.
Each stored variable has a **secret mode**:

| Mode | Meaning |
| --- | --- |
| **Not secret** | Stored and shown as is, with its version history. |
| **Secret** | Stored **encrypted**. **Write-only**: the API and UI never show the value again, only that it's set, by whom and when. Keeps **no earlier values**. |
| **Follow workflow declarations** | Secret as soon as a workflow declares a variable of that name secret (for a workflow variable, that workflow), and then **stays secret**, even if the workflow later stops declaring it. ⚠️ It can therefore become secret when someone edits a workflow. |

- **Runs get the real value.** Their history, logs and live events hide it, just like a variable
  the workflow declares secret.
- **"Save for future runs"** of a variable the workflow declares secret stores it as **Secret**
  automatically.
- **Marking a variable secret erases its earlier values**, which can't be undone. The API refuses
  the change, saying how many versions would go, unless you confirm (`confirmPurge=true`).
- **Switching a secret back to Not secret** needs the value re-entered, because the old one can't be
  read back.
- **History:** a secret's history lists who changed it and when ("marked secret", "set", "earlier
  values erased (2 versions)"), never values.
- **Saving a workflow** that declares a variable secret turns any stored "follow" variable of that
  name secret. The designer can ask the server first which ones that would be
  (`POST /api/v1/variables/secret-impact`).

### In the UI

- **Settings → Global variables:**
  - each variable has a **Secret** setting (Not secret / Secret / Follow workflow declarations),
    with an explanation of each option and a warning on Follow;
  - a secret's row shows 🔒 and who set it when, never the value. Editing a secret leaves the value
    box empty (type a new one to replace it);
  - marking a variable secret opens a **confirmation dialog** saying how many earlier values will
    be erased, and nothing is erased until you confirm;
  - a secret's history lists who and when, never values.
- **Designer:** saving a workflow that declares a variable secret first asks, in a dialog listing
  them, if that would turn stored "follow" variables secret.
- **Settings → Encryption keys** shows where the keys live, and the warning when they could be lost.

### API

| Method | Path | What |
| --- | --- | --- |
| PUT | `/api/v1/variables/{name}?scope=…` | set a value; optional `secretMode` creates it secret in one call (`&confirmPurge=true` if that erases earlier values) |
| PUT | `/api/v1/variables/{name}/secret?scope=…[&confirmPurge=true]` | change the mode: `{ "mode": "Secret" \| "FollowWorkflows" \| "None", "value": … }` (the value is required when a secret becomes None) |
| GET | `/api/v1/variables?scope=…`, `/{name}`, `/{name}/history` | secret values show as `••••••` or `null`, with `isSecret`, `secretMode`, `lastSetBy` and `lastSetAt`; a secret's history is who/when only |
| POST | `/api/v1/variables/secret-impact` | `{ workflowId, secretNames }` → the stored follow-mode variables that saving would turn secret |

### How it's stored

- The value is encrypted with the Data Protection keys above (`dp:v1:` + ciphertext), and each
  write replaces the previous version.
- Each variable's mode and its who/when trail are kept in the persistence store
  (`settings/secret-variables.json`). The variable tables themselves didn't change.

### Later: secret providers

A pluggable source for secret values (environment variables, a key vault, …) is planned: a stored
variable would hold a reference such as `env:NAME`, resolved when a run starts. Until then, secrets
live encrypted in the variable store.
