# Import & Export 📦

> Phase 3.6 — saving a workflow to a file and loading it back.
> Made with 💖 by Ami-Chan~ ✨

Workflows can be written to a `.dotflow.json` file and read back: to move one between environments,
keep it in source control, share it, or take a backup.

- [Exporting](#exporting)
- [Importing](#importing)
- [The file format](#the-file-format)
- [What travels — and what doesn't](#what-travels--and-what-doesnt)
- [Credentials](#credentials)

---

## Exporting

**⬇ Export** in the designer toolbar downloads the workflow you're looking at.

Export uses the **in-memory document**, so what's on the canvas is what lands in the file —
including unsaved edits. The dialog says so when the document is dirty, so "I exported before
saving" is never a silent surprise.

Node **positions travel**, so a workflow imported elsewhere looks the way its author arranged it.

## Importing

**📥 Import** on the workflow list takes a `.dotflow.json` file.

Import deliberately does **not** write anything straight away. The file is parsed in the browser,
checked against this environment, and then opened in the designer **unsaved** — so you can look at
it, fix anything the report flagged, and commit with **💾 Save**. A bad import costs nothing.

### The portability report

A workflow is portable; the things it *references* are environment configuration. Before opening,
import tells you what won't line up here:

| Finding | Severity | Why |
| --- | --- | --- |
| A node's module isn't installed | ❌ Error | Saving would be refused. Install the module, or delete the node |
| A node pins a module **version** that isn't installed | ⚠️ Warning | It imports and saves fine, then fails when the workflow runs |
| A database `connectionId` isn't registered here | ⚠️ Warning | Connections are environment config by design — add it in Settings |
| A `{{Variable.x}}` reference is neither declared nor a known global | ⚠️ Warning | It'll resolve to nothing unless supplied as a run input |
| The workflow declares secret variables | ⚠️ Warning | Secrets never travel in a file — supply them here |

Errors block *saving*, not *opening*: you can still open the workflow to look around.

### Creating a copy vs. overwriting

By default import **creates a new workflow** — importing the same file twice gives you two.

If the file's workflow already exists here, import offers to **overwrite** it instead. That
replaces the stored definition and can't be undone, so it's opt-in, clearly warned, and identifies
the target **by name** rather than by id.

## The file format

JSON, indented, in a small versioned envelope:

```json
{
  "dotflowFormat": 1,
  "exportedAt": "2026-08-02T14:00:00+00:00",
  "engineVersion": "1.4.0",
  "workflow": {
    "id": "…",
    "name": "Order sync",
    "version": "1.2.0",
    "nodes": [
      {
        "id": "http-1",
        "moduleId": "builtin.http.request",
        "name": "Fetch orders",
        "properties": { "url": "{{Variable.apiBaseUrl}}/orders" },
        "position": { "x": 120, "y": 340 }
      }
    ],
    "connections": [],
    "variables": {
      "orderId": { "name": "orderId", "type": "String", "seed": "SeedOnly", "isSecret": false }
    }
  }
}
```

`dotflowFormat` lets DotFlow refuse a file from a newer version rather than half-reading it. A file
with **no** envelope — a hand-crafted one, or a pasted API response — is still accepted, with a
warning suggesting you re-export it.

Enums are written as **names** (`"type": "String"`, not `"type": 0`), so a diff is reviewable.
Numbers are still accepted when reading, so files and definitions written before this change keep
working.

## What travels — and what doesn't

**Travels:** nodes (with positions, properties, metadata, timeouts, error handling, retry policy,
region grouping), connections (with conditions and priorities), variable declarations, tags,
trigger and error-handling config, and the workflow's `id`, `version`, `createdAt` and `updatedAt`.

**Doesn't travel — by design:**

- **Execution history** — belongs to the environment that ran it
- **Stored variable values** — global and workflow-scoped values live in the variable store; only
  the *declarations* are part of the workflow (see [Workflow Variables](variables.md))
- **Database connection registrations** — a workflow references a `connectionId`; the connection
  itself is environment config
- **Installed modules** — a workflow names the modules it needs; it doesn't carry them

That last point is what the portability report exists to make visible.

## Credentials

Secret **variables** are already safe: a secret declaration can't carry a value, precisely because
definitions get exported and version-controlled.

Node **properties** have no such guard — an HTTP node's `apiKey`, `bearerToken` or `password` is an
ordinary string. Export checks for credential-shaped property names holding literal values and, if
it finds any, lists them and offers to **blank them in the exported file** (on by default).

The check is name-based, so treat it as a prompt rather than a guarantee — it can't recognise a
credential stored under an unusual name.

> 💡 The durable fix is to reference a variable instead: `{{Variable.apiKey}}` in the property, with
> the value supplied per environment. Bindings export safely, and export won't flag them.

## See also

- [Visual Designer](designer.md) — the canvas, toolbar and properties panel
- [Workflow Variables](variables.md) — scopes, precedence, and how bindings resolve
- [REST API › Workflows](rest-api.md) — the underlying create/update endpoints
