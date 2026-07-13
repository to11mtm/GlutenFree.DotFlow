# 🖥️ Phase 2.4.b.P4 — Typed Linq Editor Panel (Workflow.UI) — Design & MVP Scope

> **Status:** ✅ Scoped — UQ1–UQ4 resolved (UD1–UD4); ready to implement once 2.4.b.5 endpoints stabilise · **Author:** Ami-chan uwu
> **Companion to:** [Phase 2.4 Database Modules](../phases/Phase2-4-DatabaseModules.md) · [Database Modules Design](./Phase2-4-DatabaseModules-Design.md)
> **Depends on:** 2.4.b.5 API endpoints (`/api/database/linq/{validate,preview,compile}`), 2.4.b.4 catalog import, 2.4.a.5 named connections

---

## 0. TL;DR ✨

Phase 2.4.b ships the typed-linq authoring surface **API-only** (Q16). This doc maps out the **UI panel MVP** — a Blazor-hosted **Monaco editor panel** for `builtin.database.linq` nodes that turns the validate/preview/compile endpoints into an actual authoring experience: pick a connection, import/pick tables, write typed C#, see Roslyn squigglies live, preview against the sandbox, compile on save. The panel is what makes "typed-first" (D12) *feel* typed-first~ 💖

**Tracked as:** post-MVP slice **2.4.b.P4** (single-PR sized once 2.4.b.5 endpoints are stable; ~1 week).

---

## 1. Current state of `Workflow.UI` 🌱

| Fact | Detail |
|---|---|
| Project | `Workflow.UI/Workflow.UI.Client` — **Blazor WebAssembly**, `net8.0` |
| Contents | Empty scaffold: bare `Program.cs`, empty `Pages/` + `Components/` + `Services/` |
| Implication | No existing node-inspector/panel infrastructure to slot into — **this slice must not block on the full workflow-canvas UI**; the panel ships as a standalone routable page first, embeddable as a component later |

> **CopilotNote:** Because the UI is greenfield, the panel is designed as a **self-contained `<LinqEditorPanel>` Razor component** with all API access behind one injected client service. When the workflow canvas/node-inspector eventually exists, it hosts the same component — zero rework~ 🌸

---

## 2. MVP scope (what ships in 2.4.b.P4) 🎯

### 2.1 The panel — `<LinqEditorPanel>` component

```
┌───────────────────────────────────────────────────────────────────┐
│  Linq Query Editor — node: load_big_orders                        │
├──────────────────────┬────────────────────────────────────────────┤
│ Connection           │  [ OrdersDb (postgres)          ▼ ]        │
│ Tables               │  ☑ public.orders   ☑ public.customers      │
│                      │  ☐ public.audit_log     [⟳ Import tables]  │
├──────────────────────┴────────────────────────────────────────────┤
│  // Monaco editor (C#)                                            │
│  var results = db.Orders                                          │
│      .Where(o => o.CustomerId == inputs.CustomerId                │
│                  && o.Total > inputs.MinTotal)                    │
│      .ToList();                                        ~~~~~~~~   │
│  return results;                        ← live squigglies (CS…)   │
├───────────────────────────────────────────────────────────────────┤
│  [ ✓ Validate ]  [ ▶ Preview ]  [ 💾 Compile & Save ]   ⏱ 213ms   │
├───────────────────────────────────────────────────────────────────┤
│  Diagnostics (2)              │  Preview results (3 rows)         │
│  ⚠ CS8602 possible null …    │  ┌─────┬───────────┬────────┐     │
│  ✖ CS1061 'Ordres' does …    │  │ id  │ customer  │ total  │     │
│                               │  └─────┴───────────┴────────┘     │
└───────────────────────────────────────────────────────────────────┘
```

**MVP features:**

- [ ] **Connection picker** — `GET /api/database/connections` (masked descriptors; id + provider + displayName only)
- [ ] **Table picker** — lists `IWorkflowTableCatalog` entries for the selected connection; multi-select checkboxes drive `selectedTables`
- [ ] **"Import tables" button** — calls the 2.4.b.4 one-shot import (`POST /api/database/catalog/{connectionId}/import`), then refreshes the picker (Q17 flow surfaced in UI)
- [ ] **Monaco editor** via **`BlazorMonaco`** (3.x) — C# syntax highlighting, dark/light theme follows app
- [ ] **Debounced validate-on-idle** (default 800ms after last keystroke) — `POST /api/database/linq/validate`; diagnostics mapped to Monaco **markers** (squigglies) using the line/column info the API contract guarantees (2.4.b.5)
- [ ] **Diagnostics list panel** — errors ✖ / warnings ⚠, click-to-navigate to the offending line
- [ ] **Preview button** — `POST /api/database/linq/preview` → results rendered in a simple grid + duration badge; provider-semantics disclaimer banner ("preview runs on SQLite — see docs", per design-doc C10)
- [ ] **Compile & Save** — `POST /api/database/linq/compile` → stores returned `compiledAssemblyKey` into the node's configuration payload; button **disabled with tooltip when the API returns 403** (trusted-author gate, Q15)
- [ ] **Standalone host page** — `Pages/LinqEditor.razor` (`/linq-editor?definitionId=…&nodeId=…`) so the panel is usable before the canvas exists
- [ ] **`DatabaseLinqApiClient`** service (`Services/`) — typed wrapper over the three linq endpoints + connections + catalog; credentials attached via the `IApiCredentialProvider` seam (cookie same-origin in V1 — UD2)

### 2.2 Explicit non-goals (MVP) 🚫

| Non-goal | Why / where it goes |
|---|---|
| Full IntelliSense / completions | Requires a server-side Roslyn workspace ("completion service") endpoint — post-MVP slice **P4.P1** below |
| Semantic highlighting / hover-docs | Same dependency as completions |
| Diff/versioning UI for compiled blobs | Blob cache is hash-keyed and self-invalidating (D15); no UI needed yet |
| Editing raw-SQL modules in Monaco | Escape-hatch family keeps plain textarea config for now |
| Workflow canvas / node inspector | Separate (larger) UI effort; this panel is designed to embed into it later |
| Sample-data editing for preview seeds | Previewer seeds defaults; custom seed rows → P4.P2 |

---

## 3. Architecture sketch 🛠️

```
Workflow.UI.Client (Blazor WASM)
  Components/
    LinqEditorPanel.razor            ← the panel (composition root)
    LinqDiagnosticsList.razor        ← error/warning list, click-to-line
    LinqPreviewGrid.razor            ← generic rows/columns grid
    ConnectionPicker.razor           ← dropdown, self-loading
    TablePicker.razor                ← checkbox list + import button
  Pages/
    LinqEditor.razor                 ← standalone host page (/linq-editor)
  Services/
    DatabaseLinqApiClient.cs         ← typed HttpClient wrapper (validate/preview/compile/connections/catalog)
    WorkflowDefinitionApiClient.cs   ← definition load/save round-trip for node config (UD3)
    IApiCredentialProvider.cs        ← auth seam — cookie no-op impl for V1 (UD2)
    MonacoMarkerMapper.cs            ← LinqDiagnostic[] → Monaco MarkerData[]

Workflow.Tests.UI                    ← NEW test project (UD4) — bUnit + component tests
```

**Data flow:** `LinqEditorPanel` owns the state record (`connectionId`, `selectedTables`, `code`, `diagnostics`, `previewResult`, `compiledAssemblyKey`) → children are dumb/parameterised. All server access via `DatabaseLinqApiClient`. Debounce implemented with `System.Timers`/`CancellationTokenSource` reset per keystroke — cancel in-flight validate when a newer one starts.

**Auth (UD2 — cookie same-origin for V1):**
- The WASM client is served same-origin with `Workflow.Api`; the browser attaches the auth cookie automatically — `HttpClient` just needs `BrowserRequestCredentials.Include` on requests.
- **Token-provider seam:** all API clients take an injected `IApiCredentialProvider`. The V1 implementation (`CookieCredentialProvider`) is a no-op beyond setting the fetch credentials mode. A future bearer-token move implements `BearerCredentialProvider` (attaches `Authorization` header from a token source) and swaps the DI registration — **no changes to `DatabaseLinqApiClient` call-sites**.
- 403 responses (trusted-author gate, Q15/D17) are surfaced by the credential-agnostic client as a typed `ForbiddenApiResult` so the panel can render the disabled-button tooltip regardless of auth scheme.

**Node-config persistence (UD3 — definition API round-trip, no draft storage):**
- `/linq-editor?definitionId=…&nodeId=…` → `WorkflowDefinitionApiClient.GetDefinitionAsync(definitionId)` → extract the node's configuration → hydrate panel state.
- **Compile & Save** = compile (get `compiledAssemblyKey`) → patch the node config inside the in-memory definition → `PUT` the whole definition back. Standard optimistic-concurrency behaviour of the definition API applies (a 409 surfaces as a "definition changed — reload?" prompt).
- Unsaved-changes guard: simple dirty-flag + `beforeunload`/navigation-lock prompt; no local persistence.

**Packages to add:** `BlazorMonaco` (MIT ✅ — UD1) to `Directory.Packages.props` + `Workflow.UI.Client.csproj`; `bunit` to `Directory.Packages.props` + the new `Workflow.Tests.UI` project (UD4).

---

## 4. Task breakdown (2.4.b.P4, ~1 week) 📋

- [ ] **P4.1 Plumbing** — `BlazorMonaco` package (UD1), `DatabaseLinqApiClient` + `WorkflowDefinitionApiClient` behind `IApiCredentialProvider` (cookie same-origin V1 — UD2), DI registration in `Program.cs`; scaffold **`Workflow.Tests.UI`** project (bUnit, added to `Workflow.sln` — UD4)
- [ ] **P4.2 Pickers** — `ConnectionPicker` + `TablePicker` + import-tables flow
- [ ] **P4.3 Editor core** — Monaco integration, theme, debounced validate, `MonacoMarkerMapper`, diagnostics list with click-to-line
- [ ] **P4.4 Preview + compile** — preview grid + duration + C10 disclaimer banner; compile & save via definition round-trip with 403-gating UX + 409 reload prompt (UD3)
- [ ] **P4.5 Host page + polish** — `/linq-editor` page (definition load/hydrate — UD3), loading/error states, dirty-flag unsaved-changes guard, empty-catalog call-to-action ("no tables yet — import from connection")
- [ ] **P4.6 Docs** — `docs/database-modules.md` authoring guide updated with panel screenshots/flow

### Tests (target ~12) → `Workflow.Tests.UI/LinqEditorPanelTests.cs` *(bUnit — new project per UD4)*

- [ ] `Panel_LoadsConnections_PopulatesPicker`
- [ ] `Panel_ImportTables_CallsImportThenRefreshesCatalog`
- [ ] `Panel_TypingDebounces_SingleValidateCallAfterIdle`
- [ ] `Panel_NewKeystroke_CancelsInFlightValidate`
- [ ] `MarkerMapper_ErrorDiagnostic_MapsToSeverityErrorWithLineCol`
- [ ] `DiagnosticsList_Click_NavigatesEditorToLine`
- [ ] `Preview_RendersRowsAndDuration`
- [ ] `Preview_ShowsSqlitePreviewDisclaimer`
- [ ] `Compile_403_DisablesButtonWithTrustedAuthorTooltip`
- [ ] `Compile_Success_PatchesNodeConfigAndPutsDefinition` *(UD3 round-trip)*
- [ ] `Save_DefinitionConflict409_ShowsReloadPrompt` *(UD3 optimistic concurrency)*
- [ ] `Panel_DirtyState_NavigationGuardPrompts`

> **CopilotNote:** bUnit can't drive real Monaco (JS interop) — wrap the editor in an `IMonacoEditorAdapter` interface so tests fake it. Only the adapter is untested by bUnit; cover it with a Playwright smoke test later if we adopt E2E UI testing~ 🌸

---

## 5. Post-MVP slices for the panel 🚧

- **P4.P1 Completions & hover** — server-side Roslyn completion service (`POST /api/database/linq/completions` with position + code); Monaco `CompletionItemProvider` wiring. This is the big UX unlock (true IntelliSense on `db.` and `inputs.`).
- **P4.P2 Custom preview seed rows** — editable sample-data grid per selected table, sent with the preview request.
- **P4.P3 Canvas embedding** — host `<LinqEditorPanel>` inside the node inspector once the workflow canvas exists.

---

## 6. Open questions — all resolved ✅

- [x] **UQ1 Editor library:** `BlazorMonaco` (Monaco, heavier ~3MB, best C# story) vs `CodeMirror 6` wrapper (lighter, weaker C# tooling)? **Recommendation: BlazorMonaco** — Monaco's marker/completion APIs map 1:1 onto our diagnostics contract and P4.P1.
  - **RESOLVED (UD1):** Go with **BlazorMonaco**; if we hit perf issues we can explore CodeMirror later.
- [x] **UQ2 Auth plumbing:** how does `Workflow.UI.Client` (WASM) obtain/attach tokens today? Nothing exists in the scaffold — do we standardise on cookie-auth same-origin, or bearer tokens via an auth provider? Blocks the 403-gating UX (P4.1).
  - **RESOLVED (UD2):** **Cookie-auth same-origin** for V1 (no new auth provider). `DatabaseLinqApiClient` is built behind an `IApiCredentialProvider` seam so a later switch to bearer tokens is a provider swap, not a client rewrite — see §3 auth notes.
- [x] **UQ3 Where does node config live pre-canvas?** With no canvas, `/linq-editor` needs a way to load/save the node's configuration — direct `PUT` to the definition API, or a local "draft" story? **Recommendation: definition API round-trip** (load definition → edit node → save definition) to avoid inventing draft storage.
  - **RESOLVED (UD3):** No new draft-storage mechanism — **definition API round-trip** (load definition → edit node → save definition). Keeps node management consistent with the rest of the workflow surface — see §3 persistence notes.
- [x] **UQ4 bUnit vs Playwright:** is adding `bunit` to `Workflow.Tests` acceptable, or do we want a dedicated `Workflow.Tests.UI` project? **Recommendation: dedicated `Workflow.Tests.UI`** to keep UI test deps out of the core test project.
  - **RESOLVED (UD4):** Dedicated **`Workflow.Tests.UI`** project for bUnit tests — core test project stays clean and focused on non-UI logic. Project scaffold added to P4.1.

---

> 🌸 *uwu — the panel is a thin, well-behaved shell over the 2.4.b.5 endpoints: all the smarts (Roslyn, sandbox, HMAC) stay server-side where they belong. UQ1–UQ4 all resolved (BlazorMonaco · cookie same-origin behind `IApiCredentialProvider` · definition round-trip · `Workflow.Tests.UI`) — P4 is a clean one-week slice ready to kick off once 2.4.b.5 lands~* 💖

