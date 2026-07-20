# Module Manager 📦

> Phase 3.6 — "The Foundry": browse modules, read generated documentation, upload `.wfmod`
> packages, enable/disable + version-manage, and uninstall. Made with 💖 by Ami-Chan~ ✨

The Module Manager lives at **`/modules`** in the DotFlow app (top bar → **📦 Modules**), and from
the designer palette's **"📦 Manage modules →"** link. It's a thin front-end over the shipped module
API — the **read** side (2.7.3) *and* the **write** side (2.8.5, admin/write-gated) both already
exist, so the manager adds **no backend**.

- [Browsing](#browsing)
- [The documentation drawer](#the-documentation-drawer)
- [Uploading a package](#uploading-a-package)
- [Enable / disable & versions](#enable--disable--versions)
- [Uninstalling](#uninstalling)
- [Permissions](#permissions)

## Browsing

```text
┌───────────────────────────────────────────────────────────────────────────┐
│ 🌊 DotFlow · 📦 Modules   [🔍 search…] [Category ▾] [☑ enabled only] [⬆ Upload]│
├───────────────────────────────────────────────────────────────────────────┤
│ ▾ HTTP 🌐                                                                    │
│  ┌ 🌐 HTTP Request ─┐ ┌ 🌐 HTTP Response ┐   click a card → docs drawer ▶    │
│  │ builtin.http.req │ │ builtin.http.res │                                   │
│  │ v1.2.0 🟢 enabled│ │ v1.0.0 🟢 enabled│                                   │
│  └──────────────────┘ └──────────────────┘                                  │
└───────────────────────────────────────────────────────────────────────────┘
```

Modules are grouped by **category**; **search** matches id/name/description, the **category**
dropdown filters, and **enabled only** hides disabled modules (remembered across sessions).

## The documentation drawer

Clicking a module opens a drawer with **generated documentation** derived from its schema:
description, **inputs**/**outputs** (name, type, required, description, default), **properties**
(editor type, required, default, allowed values, description), **dependencies**, and **versions**
(the active one flagged). *(First-class README / usage examples / changelog are a later phase — the
docs are generated from the schema today.)* See the [Module Author Guide](module-author-guide.md)
for how these fields are declared.

## Uploading a package

**⬆ Upload** opens a dialog: drag a `.wfmod` file onto the zone (or choose one), then **Upload**.
The package is validated server-side — on success you see **"Installed {id} v{ver}"** plus any
**warnings** (unsigned package, missing manifest hash, schema-compat notes); on failure the error
shows inline (e.g. `422` invalid package / `409` duplicate version). The grid refreshes
automatically.

## Enable / disable & versions

The docs drawer's action row toggles the module **enabled/disabled** (disabling shows a heads-up
listing any *loaded* modules that depend on it). The **Versions** panel lists every installed
version and enables/disables each one — DotFlow resolves the **newest enabled** version, so:

- **Upgrade** = upload a newer version (it becomes newest + enabled).
- **Rollback** = disable the newer version (or enable an older one).

## Uninstalling

**🗑 Uninstall** (with a confirm) removes a module. The server refuses (`409`) if other modules
depend on it or executions are in flight — the reason is shown clearly. Successful uninstalls close
the drawer and refresh the grid.

## Permissions

Uploading + uninstalling require **admin**; enabling/disabling requires **write** access. In the
demo posture (auth disabled) everything is available; where auth is enabled, unprivileged users get
a clear "requires admin/write" message and keep the read-only browse + docs experience. *(Role-aware
UI that pre-hides actions is a later phase.)*

---

See also: [Module Author Guide](module-author-guide.md) · [Designer](designer.md) ·
[Designer Architecture & React-Port Guide](designer-architecture.md).
