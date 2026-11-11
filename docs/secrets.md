# Secrets

DotFlow keeps a few kinds of secret:
- **secret variables**: variables marked secret in a workflow, or stored as global / workflow
  variables;
- **database connection strings**;
- **spill files**, when spill encryption is on.

This page covers how they're protected at rest. For how secret values are kept out of history,
logs and live events, see [variables](variables.md); plan 03 §10.6 has the design notes.

> **Status:** part 1 of [plan 08](architecture/plans/08-secrets-at-rest.md) (key storage) is in
> place. Encrypted, write-only secret variables (parts 2–3) are next.

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

*Coming in plan 08 parts 2–3.*
- Stored global and workflow variables get a per-variable mode: **Not secret**, **Secret**, or
  **Follow workflow declarations**.
- Secret values are stored encrypted and are **write-only**: never shown again, only replaced.
- Their history keeps who and when, not values.
- Marking a variable secret erases its earlier plain-text values, after you confirm.
