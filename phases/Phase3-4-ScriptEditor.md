# Phase 3.4: Script Editor UI — "Script Studio" (Week 31) 💻

Made with 💖 by Ami-Chan! UwU ✨

[Back to Phase 3](Phase3-AdvancedFeatures.md) | [All Phases](README.md)

> ## ✅ COMPLETE — all 6 slices (3.4.0–3.4.5) implemented, **58 Script Studio tests** (200 total
> in `Workflow.Tests.UI`), documented in [`docs/script-studio.md`](../docs/script-studio.md).
> **Zero new backend code** — a pure client feature over the shipped `/scripts/*` endpoints +
> the existing Monaco wrapper, honoring the D2 framework-free boundary so the React port stays
> additive~ 🌸

---

## Overview

Phase 3.4 delivers a **dedicated in-browser script editor** — "Script Studio" — where
authors write, test, and manage the scripts that power `builtin.script` nodes and script
libraries. It is a **thin front-end consumer** of infrastructure that already shipped:
Monaco is already wrapped as a lazy-loaded `CodeEditor` component (Phase 3.3.b.3 / D13),
the `POST /api/v1/scripts/test` sandbox-run endpoint + `GET /api/v1/scripts/languages` +
the `GET/PUT/DELETE /api/v1/scripts/libraries` CRUD all exist (Phase 3.1.6), and the
`workflow` API surface is fully defined in `IWorkflowScriptApi` (Phase 3.1.1). Phase 3.4 is
about **assembling these into a first-class authoring experience** — syntax highlighting,
IntelliSense for the `workflow.*` API, a template catalog, an inline test runner, an API
documentation panel, and library management — not building new engines~ 🌷

> **CopilotNote:** Hot paths: a new `Workflow.UI.Client/Scripts/*` feature area
> (`ScriptStudio.razor` page at `/scripts`, plus components + framework-free state), a new
> `Api/ScriptsClient.cs` (the `/scripts/*` REST surface), a **generalized `ScriptEditor`
> component** wrapping the existing `CodeEditor` with language/theme/IntelliSense wiring, and
> a static **workflow-API descriptor** driving both completions and the docs panel. Tests
> extend the existing `Workflow.Tests.UI` (bUnit + xUnit) project. **Zero new backend code
> for the MVP** — Script Studio speaks the existing `/scripts/*` endpoints verbatim~ 🌸

> **Reality-check note (July 2026):** The §3.4 checklist in
> [`Phase3-AdvancedFeatures.md`](Phase3-AdvancedFeatures.md#34-ui---script-editor-week-20)
> predates Phases 3.1 and 3.3. Since then: (a) **Monaco is already integrated** as the
> lazy-loaded `CodeEditor` (3.3 D13, with a plain-textarea fallback) — the "Integrate
> Monaco" task is a generalization, not a green-field install; (b) the **script test
> interface already has a backend** — `POST /api/v1/scripts/test` runs code in the sandbox
> and returns `{ success, result, logs, variableUpdates, durationMs, error }` (3.1.6); (c)
> the **IntelliSense target is a real, stable contract** — `IWorkflowScriptApi` (3.1.1) +
> the camelCase JS prelude in `JavaScriptScriptExecutor`; (d) **Monaco bundles JS/TS, Lua,
> and Python** grammars, so "language-specific syntax highlighting" is configuration, not
> new grammar authoring; (e) the checklist's Python support maps to the deferred Python
> executor (3.1.P1) — Script Studio highlights Python but only *runs* the registered MVP
> languages (JS/C#/Lua). This plan reconciles all five and supersedes the checklist.

**Timeline:** ~1 week (Week 31 — the checklist's original "Week 20" renumbered to follow
3.3's Weeks 27-30).
**Complexity:** 🟡 Medium — the mechanics are well-bounded (one page, existing endpoints);
the fiddly parts are **Monaco completion/hover providers via JS interop** and keeping the
API descriptor from drifting out of sync with `IWorkflowScriptApi`.

---

## Confirmed Design Decisions ✅

| # | Decision |
|---|----------|
| **D1 A dedicated "Script Studio" page in the existing Blazor client** | Script Studio lives at `/scripts` (and `/scripts/{libraryId}`) in `Workflow.UI.Client`, reachable from the top bar and from `builtin.script` nodes in the designer. It reuses the 3.3 shell (TopBar, ToastHost, tokens.css, `AuthState`, ProblemDetails handling). No new host/project. |
| **D2 Contracts-only + framework-free boundary (inherited from 3.3 D2)** | Script Studio talks to the backend **only** through `/api/v1/scripts/*` (test/languages/libraries). All *logic* (template catalog, API-descriptor model, test-run state, editor options) lives in **framework-free C# services** (`Scripts/State/*` — no Blazor types), and the wire DTOs are plain STJ mirrors (`Api/Dtos/ScriptDtos.cs`). Monaco lives behind the same view-layer interop seam as `CodeEditor`. This keeps the React+TS port (3.3.P7) additive. |
| **D3 A generalized `ScriptEditor` over the existing `CodeEditor`** | The 3.3 `CodeEditor` (lazy Monaco + textarea fallback) is extended, not replaced: a richer `ScriptEditor` component adds language selection, theme/options (line numbers, minimap, word-wrap, font size), and a registration hook for completion/hover providers. The textarea fallback path is preserved (Monaco-unavailable environments still edit). |
| **D4 IntelliSense from a static, drift-guarded API descriptor** | A hand-authored `WorkflowApiDescriptor` (framework-free C#: method name, JS/camelCase name, params, return, one-line doc, category, gated flag) is the single source for **both** Monaco completions/hover **and** the API docs panel. A `Workflow.Tests.UI` guard test asserts the descriptor's method set matches `IWorkflowScriptApi`'s public methods (fails loudly on drift). *(A reflection-backed `GET /scripts/api-descriptor` endpoint is the post-MVP alternative — 3.4.P1.)* |
| **D5 Monaco's bundled grammars for highlighting; run only registered languages** | JavaScript/TypeScript, Lua, and Python highlighting come from Monaco's own `basic-languages` bundle — no grammar authoring. The **language dropdown for *running*** is populated from `GET /api/v1/scripts/languages` (the registered executors: JS/C#/Lua for MVP). Python can be *edited/highlighted* but shows a "not runnable yet" hint until 3.1.P1. |
| **D6 The test runner reuses `POST /api/v1/scripts/test`** | The inline **▶ Test** action sends `{ language, code, inputs?, libraries?, config? }` and renders `{ success, result, logs, variableUpdates, durationMs, error }` in a results pane (result JSON, level-filtered logs, staged variable updates, duration, structured error). Config toggles (timeout, allowNetwork, allowFileSystem) map to the request `config`; the server clamps to host ceilings. `WorkflowWrite` policy. |
| **D7 A static template catalog, insertable into the editor** | `ScriptTemplateCatalog` (framework-free) ships ≥10 templates keyed by `(language, category)` — HTTP request, database-via-node-composition, data transform, file processing, variables, logging, JSON/CSV, hashing, HTTP-with-headers, error-handling. Insertion drops the snippet at the cursor (or replaces an empty editor). Templates are client-side data, not an API. |
| **D8 Library management over the existing CRUD** | Script Studio lists libraries (`GET /scripts/libraries?language=`), opens one for edit, **Save**s (`PUT`), and **Delete**s — including "Save current editor as a library" (id/name/description/language + `exportedFunctions` metadata). Dependency-cycle/validation errors surface from the server's ProblemDetails. |
| **D9 Designer integration is additive** | `builtin.script` nodes in the designer (Phase 3.3) gain an **"Edit in Script Studio"** affordance on their `code`/`language` properties: it opens Script Studio seeded with the node's code+language; **Apply back** returns the edited code to the node's property (via the existing `EditNodePropertiesCommand`). No change to the node model — just a deep link + a return channel. |
| **D10 Auth + errors reuse the 3.3 client plumbing** | The `AuthMessageHandler` stamps the credential; `ApiException`/`ApiError` surface ProblemDetails; toasts report success/failure. Anonymous-friendly when `Api:Auth:Require=false`. |
| **D11 Testing: bUnit for components, xUnit for state, descriptor-drift guard** | New tests extend `Workflow.Tests.UI`: framework-free specs for the template catalog, API descriptor, and test-run state; bUnit render/interaction tests for `ScriptEditor`, the test runner, the docs panel, and library management. The Monaco interop path is faked (loose JS interop) — the textarea fallback is the tested edit surface, mirroring 3.3.b.3. |

---

## RESOLVED ✅

> Q1–Q6 confirmed (all proposed answers **Agreed** by the user) — implementation proceeds on these~ ✅

- [x] **Q1 API descriptor: static hand-authored (D4) or a reflection-backed `GET /scripts/api-descriptor` endpoint?**
  - **Resolved → Static + a drift-guard test (D4).** Reflection can't recover the camelCase JS names or the one-line docs cleanly (they live in the JS prelude + XML comments), and a static descriptor keeps Script Studio backend-free and React-portable. Promote to an endpoint only if the guard proves annoying → **3.4.P1**.
- [x] **Q2 Python: highlight-only, or wait for the executor (3.1.P1)?**
  - **Resolved → Highlight + edit Python now** (Monaco bundles it), but the **Run** button is disabled for Python with a "runtime coming in a later phase" hint — the language dropdown for *running* reflects `GET /scripts/languages` (JS/C#/Lua). Full Python authoring lands with **3.1.P1**.
- [x] **Q3 Templates: static client catalog (D7), or persist templates as script libraries?**
  - **Resolved → Static client catalog for MVP** (zero API, instantly available, React-portable). User-defined/persisted templates ride the library system later → **3.4.P2**.
- [x] **Q4 Standalone Script Studio *and* an inline editor in the designer's properties panel — both, or just the page + deep link?**
  - **Resolved → The standalone page is the full experience;** the designer keeps its **inline** `CodeEditor` (from 3.3) for quick edits and adds an **"Edit in Script Studio →"** deep link for the full experience (D9). Avoids embedding the whole studio in the properties rail.
- [x] **Q5 IntelliSense depth: completions + hover only, or also signature help + diagnostics?**
  - **Resolved → Completions + hover docs** for `workflow.*` in MVP (highest value, lowest interop risk). Signature help + squiggly diagnostics (beyond Monaco's built-in JS parse errors) → **3.4.P3**.
- [x] **Q6 Run inputs/config UX: a simple JSON textarea + toggles, or a structured form?**
  - **Resolved → JSON inputs textarea** (Monaco `json`) + a small toggle row (timeout number, allowNetwork, allowFileSystem) for MVP — mirrors the designer's run dialog. A schema-driven form is unnecessary since script inputs are free-form.

---

## Pre-Existing Work (from earlier phases) ✅

| Component | File / Endpoint | Status |
|-----------|-----------------|--------|
| Lazy Monaco editor + textarea fallback | `Workflow.UI.Client/Designer/Components/CodeEditor.razor`, `wwwroot/js/monaco-interop.js` (3.3 D13) | ✅ Generalized into `ScriptEditor` (D3) |
| Script sandbox-run endpoint | `POST /api/v1/scripts/test` → `{ success, result, logs, variableUpdates, durationMs, error }` (3.1.6) | ✅ The test runner rides it (D6) |
| Registered-language discovery | `GET /api/v1/scripts/languages` (3.1.6) | ✅ Populates the runnable-language dropdown (D5) |
| Script library CRUD | `GET/PUT/DELETE /api/v1/scripts/libraries[/{id}]` (3.1.6) | ✅ Library management UI (D8) |
| The `workflow` API surface (IntelliSense target) | `Workflow.Scripting/Abstractions/IWorkflowScriptApi.cs` (3.1.1) + camelCase JS prelude | ✅ Source of the descriptor + docs (D4) |
| Contract DTOs | `Workflow.Api/Contracts/Scripts/ScriptContracts.cs` (`ScriptTestRequest/ResultDto`, `ScriptLibraryDto`, `ScriptLogEntryDto`) | ✅ Mirrored client-side (D2) |
| Client shell + auth + toasts + tests | `Workflow.UI.Client/Shared/*`, `Api/AuthState.cs`, `Api/ApiError.cs`, `Workflow.Tests.UI` (3.3) | ✅ Reused verbatim (D1/D10/D11) |
| Designer `builtin.script` node + properties panel | `Workflow.UI.Client/Designer/Components/PropertyEditor.razor` (Code editor case, 3.3.b.3) | ✅ Gains the "Edit in Script Studio" deep link (D9) |
| Scripting reference docs | `docs/scripting.md` (3.1) | ✅ Cross-linked from the API docs panel |

> **CopilotNote:** The mirror of the 3.2/3.3 insight: **the backend and the editor primitive
> already exist**. Phase 3.4 writes **no** C# in `Workflow.Api`/`Workflow.Engine`/
> `Workflow.Scripting` for the MVP — it's a client feature over the shipped `/scripts/*`
> endpoints + the existing Monaco wrapper. Budget risk on **Monaco completion-provider
> interop** and **descriptor drift** — not on plumbing~ 💖

---

## Screen Mockup 🖼️

### S1 — Script Studio

```text
┌────────────────────────────────────────────────────────────────────────────────────────┐
│ 🌊 Script Studio   [Language JavaScript ▾] [Templates ▾] [📚 Libraries ▾]  [▶ Test] [💾]  │
├───────────────┬──────────────────────────────────────────────────┬─────────────────────┤
│ API REFERENCE │  EDITOR (Monaco · JS/TS · Lua · Python)           │ TEST                │
│ 🔍 filter…    │  1  const orders = input.orders;                  │ Inputs (json)       │
│ ▾ Variables   │  2  let total = 0;                                │ ┌─────────────────┐ │
│  getVariable  │  3  for (const o of orders) {                     │ │ { "orders": [   │ │
│  setVariable  │  4    total += o.amount;      ┌───────────────┐   │ │   {"amount":10} │ │
│  variableEx…  │  5    workflow.log(o.id);     │ workflow.      │   │ │ ]}              │ │
│ ▾ Logging     │  6  }                         │  ● logInfo(m)  │   │ └─────────────────┘ │
│  logInfo      │  7  workflow.setVariable('t', │  ● logWarning  │   │ ☑ network ☐ files   │
│  logWarning   │  8    total);                 │  ● logError    │   │ timeout [30]s       │
│ ▾ HTTP 🌐     │  9  return { total };         │  ● log         │   │ ───── result ─────  │
│  httpGet      │                               └───────────────┘   │ ✅ 12ms             │
│  httpPost     │                                 (IntelliSense)    │ { "total": 10 }     │
│ ▾ Utilities   │                                                   │ logs: [info] o-1    │
│  newGuid …    │                                                   │ vars: t = 10        │
├───────────────┴──────────────────────────────────────────────────┴─────────────────────┤
│ ✓ JavaScript · library: order-utils · 🟢 API connected                                  │
└──────────────────────────────────────────────────────────────────────────────────────────┘
   API panel item → click inserts `workflow.method()` · hover shows signature + doc
```

---

## Architecture (framework-swap ready) 🏗️

```text
┌──────────────────  Workflow.UI.Client (WASM)  ───────────────────┐
│  Blazor components (THIN views)                                   │
│   Pages/ScriptStudio.razor                                        │
│   Scripts/Components/: ScriptEditor · ApiReferencePanel ·         │
│     TemplatePicker · TestRunnerPanel · LibraryBar · SaveLibraryDialog │
│        │                                                          │
│        ▼   framework-free C#  ◀── React port re-implements ──▶    │
│   Scripts/State/: WorkflowApiDescriptor · ApiMethodInfo ·         │
│     ScriptTemplateCatalog · ScriptTemplate · TestRunState ·       │
│     ScriptEditorOptions                                           │
│   Api/: ScriptsClient · Dtos/ScriptDtos.cs                        │
│   (Monaco completion/hover registered via monaco-interop.js seam) │
└───────────────┬──────────────────────────────────────────────────┘
                │ REST (JSON)
                ▼
     /api/v1/scripts/test · /languages · /libraries[/{id}]
```

**React exit checklist** additions live in `docs/designer-architecture.md`: the descriptor +
template catalog + test-run state are framework-free with xUnit specs (porting specs); the
Monaco provider registration is the only JS-interop surface (swap for `@monaco-editor/react`).

---

## Proposed File Layout 🗂️

```text
Workflow.UI.Client/
  Api/
    ScriptsClient.cs
    Dtos/ScriptDtos.cs   (ScriptTestRequestDto · ScriptTestResultDto · ScriptLogEntryDto · ScriptLibraryDto · ScriptLanguageDto)
  Scripts/
    State/
      WorkflowApiDescriptor.cs · ApiMethodInfo.cs
      ScriptTemplateCatalog.cs · ScriptTemplate.cs
      TestRunState.cs · ScriptEditorOptions.cs
    Components/
      ScriptEditor.razor            (generalizes CodeEditor: language/theme/options + provider hook)
      ApiReferencePanel.razor        (searchable workflow.* method list + details + insert)
      TemplatePicker.razor
      TestRunnerPanel.razor
      LibraryBar.razor · SaveLibraryDialog.razor
  Pages/ScriptStudio.razor           (route /scripts and /scripts/{libraryId})
  wwwroot/js/monaco-interop.js       (extended: registerCompletions/registerHover/setLanguage/setOptions)
Workflow.Tests.UI/
  Scripts/State/*   (descriptor drift-guard, template catalog, test-run state)
  Scripts/Components/*   (editor, api panel, test runner, library management — bUnit)
docs/script-studio.md   (user guide) + docs/scripting.md cross-links
```

---

## Slices & Dependencies 🧭

| Slice | Scope | Depends on |
|-------|-------|-----------|
| 3.4.0 Script Studio shell + `ScriptsClient` + generalized `ScriptEditor` | — |
| 3.4.1 Workflow-API descriptor + IntelliSense (completions/hover) + API reference panel | 3.4.0 |
| 3.4.2 Template catalog + insertion | 3.4.0 |
| 3.4.3 Test runner (inputs + `/scripts/test` + results/logs/errors) | 3.4.0 |
| 3.4.4 Library management (list/open/save/delete + save-as-library) | 3.4.0 |
| 3.4.5 Designer integration (deep link + return channel) + docs + polish | 3.4.1–3.4.4 |

---

## 3.4.0 Studio Shell + Scripts Client + `ScriptEditor` 🧰 ✅ DONE

> **Purpose:** The page skeleton, the typed `/scripts/*` client, and the generalized Monaco
> editor everything else hangs off. At the end, a user can open `/scripts`, pick a runnable
> language, and edit code (Monaco or textarea fallback).

**Complexity:** 🟢 Low-Medium

### Tasks

- [x] **`Api/Dtos/ScriptDtos.cs`** — plain STJ mirrors of the 3.1.6 contracts: `ScriptTestRequestDto` (`language`, `code`, `inputs?`, `libraries?`, `config?`), `ScriptTestConfigDto` (`timeoutSeconds?`, `allowNetwork?`, `allowFileSystem?`, `allowedPaths?`), `ScriptTestResultDto` (`success`, `result`, `logs`, `variableUpdates`, `durationMs`, `error`), `ScriptLogEntryDto` (`level`, `message`), `ScriptLibraryDto`, `ScriptLanguageDto` (`languageId`, `displayName`)
- [x] **`Api/ScriptsClient.cs`** — `TestAsync(request)`, `GetLanguagesAsync()`, `ListLibrariesAsync(language?)`, `GetLibraryAsync(id)`, `SaveLibraryAsync(dto)`, `DeleteLibraryAsync(id)`; ProblemDetails-aware via the shared `ApiHttp`; registered in `Program.cs`
- [x] **`Scripts/State/ScriptEditorOptions.cs`** — framework-free: theme (`vs-dark`/`vs`), font size, line numbers, minimap, word-wrap; language↔Monaco-mode map (`javascript`/`csharp`/`lua`/`python`/`json`) + `IsHighlightable`/`IsKnownRunnable`
- [x] **`Scripts/Components/ScriptEditor.razor` (D3)** — wraps the existing lazy-Monaco+textarea seam; adds `Language`, `Options`, an `OnEditorReady` hook (called after Monaco loads) for 3.4.1, and `InsertTextAsync`/`ApplyOptionsAsync`; two-way `Value`
- [x] **`monaco-interop.js` extension** — `createEditor`, `setLanguage(id, mode)`, `setOptions(id, opts)`, `insertText(id, text)`, and `registerCompletions`/`registerHover` (wired in 3.4.1); all no-op-safe when Monaco is absent (textarea fallback)
- [x] **`Pages/ScriptStudio.razor`** — route `/scripts` + `/scripts/{libraryId}`; top bar (language dropdown from `GetLanguagesAsync`, placeholders for Templates/Libraries/Test/Save), the `ScriptEditor`, a status bar; nav entry from the app shell (TopBar `💻 Scripts`)
- [x] **Python non-runnable hint (Q2)** — Python selectable for editing/highlighting; a banner + disabled Test button note it isn't runnable yet

### Tests (14 green): → `Workflow.Tests.UI/Scripts/State/ScriptsClientTests.cs` + `Scripts/Components/ScriptEditorTests.cs`

- [x] `ScriptsClient_Test_SendsRequestShape` · `ScriptsClient_Languages_Parses` · `ScriptsClient_Libraries_CrudRoundTrips` *(fake handler)*
- [x] `ScriptsClient_ServerError_SurfacesProblemDetails`
- [x] `EditorOptions_LanguageMap_CoversRegisteredLanguages` · `EditorOptions_UnknownLanguage_FallsBackToPlaintext` · `ScriptEditor_FallsBackToTextarea_WhenMonacoUnavailable`
- [x] `ScriptStudio_LoadsLanguages_PopulatesDropdown` · `ScriptStudio_PythonSelected_ShowsNonRunnableHint` · `ScriptStudio_LanguagesError_ShownInStatus`

---

## 3.4.1 Workflow-API Descriptor + IntelliSense + Reference Panel 💡 ✅ DONE

> **Purpose:** Make `workflow.*` discoverable: Monaco completions + hover docs, and a
> searchable API reference panel that inserts calls at the cursor.

**Complexity:** 🟠 Medium-High *(the Monaco provider interop is the risky bit)*

### Tasks

- [x] **`Scripts/State/ApiMethodInfo.cs` + `WorkflowApiDescriptor.cs` (D4)** — framework-free catalog of the `workflow.*` surface (30 methods): each has `JsName`, `ClrName`, `Parameters`, `ReturnType`, `Summary`, `Category` (Variables/Logging/Utilities/Context/HTTP/Files), `Gated`. Hand-authored from `IWorkflowScriptApi` + the JS prelude; `Signature`/`TypedSignature`/`CallSnippet` computed
- [x] **Descriptor drift-guard test (D4)** — `Descriptor_CoversWorkflowApi_NoDrift` reflects `IWorkflowScriptApi`'s public methods (minus the two executor-only accessors) and asserts the descriptor covers exactly that set. Needed a `Workflow.Scripting` project reference on `Workflow.Tests.UI`
- [x] **Monaco completion provider** — `monaco-interop.js` `registerCompletions(language, items)` (kind=Method, insertText, detail, doc); registered by the page on `OnEditorReady`. JS-only for MVP
- [x] **Monaco hover provider** — `registerHover(language, items)`: hovering a `workflow.method` shows signature + `Summary`
- [x] **`Scripts/Components/ApiReferencePanel.razor`** — searchable, category-grouped list from the descriptor; click → **insert `workflow.jsName(...)`** at the cursor + shows signature/doc/gated note; a "full reference →" link to `docs/scripting.md`
- [x] **Graceful degradation** — when Monaco is unavailable, completions/hover are absent but the reference panel + insert-at-cursor (append into the textarea) still work

### Tests (10 green): → `Workflow.Tests.UI/Scripts/State/DescriptorTests.cs` + `Scripts/Components/ApiReferencePanelTests.cs`

- [x] `Descriptor_CoversWorkflowApi_NoDrift` *(reflection guard)* · `Descriptor_MethodsHaveJsName_Summary_Category`
- [x] `Descriptor_GatedMethods_FlaggedHttpAndFile` · `Descriptor_CallSnippet_IsWorkflowPrefixed` · `Descriptor_Search_Filters`
- [x] `ApiPanel_RendersGroups_FromDescriptor` · `ApiPanel_Search_Filters`
- [x] `ApiPanel_ClickMethod_InsertsCallAtCursor` · `ApiPanel_ShowsSignatureAndDoc`
- [x] `Completions_ProviderRegistered_ForJavaScript` *(bUnit + faked interop asserts the register call)*

---

## 3.4.2 Template Catalog + Insertion 📚 ✅ DONE

> **Purpose:** Jump-start scripts from a curated catalog (≥10 templates), inserted into the
> editor.

**Complexity:** 🟢 Low

### Tasks

- [x] **`Scripts/State/ScriptTemplate.cs` + `ScriptTemplateCatalog.cs` (D7)** — framework-free: 14 templates across languages/categories — HTTP GET, HTTP-with-headers, transform (map/filter), JSON parse+build, CSV round-trip, variable read/write, logging, hashing, try/catch error handling, "database via node composition" (per 3.1 Q2), file read/write, plus Lua + C# starters
- [x] **`Scripts/Components/TemplatePicker.razor`** — dropdown grouped by category, filtered to the current language (with an "all languages" toggle); **Insert** replaces an empty editor, else confirms (insert-at-cursor vs replace-all) before touching non-empty code
- [x] **Language-awareness** — templates tagged per language; switching the studio language re-filters the catalog (external replace/insert syncs into Monaco via `setValue`)

### Tests (8 green): → `Workflow.Tests.UI/Scripts/State/TemplateCatalogTests.cs` + `Scripts/Components/TemplatePickerTests.cs`

- [x] `Catalog_HasAtLeastTenTemplates` · `Catalog_FilterByLanguage_Works` · `Catalog_GroupByCategory_Works` · `Catalog_EveryTemplate_HasCode`
- [x] `Picker_Insert_IntoEmptyEditor_Replaces` · `Picker_Insert_IntoNonEmpty_ConfirmsThenInserts` · `Picker_ConfirmReplace_ReplacesAll`
- [x] `Picker_FiltersToCurrentLanguage`

---

## 3.4.3 Test Runner 🧪 ✅ DONE

> **Purpose:** Run the current script in the sandbox and show results — the heart of the
> studio (mockup **S1** right rail).

**Complexity:** 🟡 Medium

### Tasks

- [x] **`Scripts/State/TestRunState.cs`** — framework-free: `Inputs` (JSON string), config (timeout/allowNetwork/allowFileSystem), `Running`, last `Result` + distinct `RequestError`; `Changed` event; `ParseInputs`/`ToConfig`/`FilteredLogs` helpers
- [x] **`Scripts/Components/TestRunnerPanel.razor`** — JSON inputs textarea + config toggles (Q6), **▶ Test** → `ScriptsClient.TestAsync` with the current code+language+inputs+config+libraries; disabled + spinner while running
- [x] **Results rendering** — success/fail banner + duration; **result** (pretty JSON, collapsible `<details>`); **logs** with a level filter (Debug/Info/Warning/Error) + search; **variable updates** list; a script *error* (200 + `success:false`) rendered inline (not a toast)
- [x] **Run guards** — non-runnable language + empty code disable the button with a hint; invalid inputs JSON blocks the request with an inline error; server `422` surfaces in the panel
- [x] **Remembered inputs** — inputs remembered per language in `localStorage`

### Tests (15 green): → `Workflow.Tests.UI/Scripts/State/TestRunStateTests.cs` + `Scripts/Components/TestRunnerTests.cs`

- [x] `ParseInputs_Empty/Valid/Invalid` · `FilteredLogs_LevelFilter_Filters` · `FilteredLogs_Search_Filters` · `ToConfig_Maps_Toggles` · `BeginRun_ClearsPrevious_AndRaisesChanged`
- [x] `Test_Success_ShowsResult_Logs_Duration` · `Test_Failure_ShowsStructuredError_InPanel` · `VariableUpdates_Rendered`
- [x] `Test_SendsCodeLanguageInputsConfig` · `Test_InvalidInputs_ShownInPanel_NoRequest` · `Test_Unknown_422_ShownInPanel`
- [x] `Run_PythonOrEmpty_DisabledWithHint` · `Inputs_RememberedPerLanguage`

---

## 3.4.4 Library Management 📚🔧 ✅ DONE

> **Purpose:** Browse, open, save, and delete script libraries; save the current editor as a
> reusable library.

**Complexity:** 🟢 Low-Medium

### Tasks

- [x] **`Scripts/Components/LibraryBar.razor`** — **📚 Libraries** dropdown: list from `ListLibrariesAsync(currentLanguage)`; **Open** raises to the page (loads into the editor + shows the active library in the status bar); per-item **delete** with confirm
- [x] **`Scripts/Components/SaveLibraryDialog.razor`** — **💾 Save**: for an open library → `PUT` update; **Save as new…** → id (slug) / name / description / language / `exportedFunctions` → `PUT` new; validation + dependency-cycle errors surface from ProblemDetails in the dialog
- [x] **Delete** — with confirm → `DELETE`; clears the editor if the deleted library was open
- [x] **Dirty tracking** — a `●unsaved` marker in the status bar when the editor differs from the loaded library
- [x] **Deep-link load** — `/scripts/{libraryId}` opens the studio with that library loaded

### Tests (7 green): → `Workflow.Tests.UI/Scripts/Components/LibraryManagementTests.cs`

- [x] `Libraries_List_RendersFromClient` · `Library_Open_LoadsIntoEditor`
- [x] `SaveExisting_CallsPut` · `SaveAsNew_Posts_WithMetadata`
- [x] `Save_ServerValidationError_ShownInDialog` · `Delete_ConfirmsThenCalls_ClearsIfOpen`
- [x] `DeepLink_LibraryId_LoadsLibrary`

---

## 3.4.5 Designer Integration + Docs + Polish 🔗📚✨ ✅ DONE

> **Purpose:** Connect Script Studio to the designer's `builtin.script` nodes, document it,
> and close the phase.

**Complexity:** 🟢 Low-Medium

### Tasks

- [x] **"Edit in Script Studio" (D9)** — the designer's `PropertiesPanel` shows the button on `builtin.script` nodes; it stages a request on the framework-free `ScriptStudioHandoff` and navigates to `/scripts`. Script Studio seeds from it and **✅ Apply to node** stages the edited code back; the panel consumes it and applies one `EditNodePropertiesCommand`. No node-model change
- [x] **`docs/script-studio.md`** — user guide: opening, language selection, IntelliSense + the API reference panel, templates, the test runner, library management, the designer round-trip, and shortcuts; screen tour matching **S1**
- [x] **Cross-links** — `docs/scripting.md` ↔ `docs/script-studio.md`; append the descriptor/template/test-state/handoff services to the `docs/designer-architecture.md` port checklist; `phases/README.md` + `Phase3-AdvancedFeatures.md` §3.4 pointers updated to COMPLETE
- [x] **Polish** — keyboard: Ctrl+Enter runs the test, Ctrl+S opens save (via the 3.3 `keys.js` register/unregister + `beforeunload` guard); `ScriptEditorOptions` theme/options plumbed through `ScriptEditor`; a11y labels on the API filter/inputs
- [x] **Perf/hygiene** — Monaco loads once per editor; full `Workflow.Tests.UI` green (200 tests)

### Tests (4 green): → `Workflow.Tests.UI/Scripts/Components/DesignerIntegrationTests.cs`

- [x] `ScriptNode_EditButton_OpensStudio_Seeded` · `Studio_ApplyBack_ReturnsCodeToNode` *(EditNodePropertiesCommand)*
- [x] `Shortcut_CtrlEnter_RunsTest` · `Shortcut_CtrlS_Saves`

---

## Agent Implementation Instructions 🤖

> **Audience:** an AI coding agent implementing this phase. Follow the same loop and
> guardrails as [Phase 3.3](Phase3-3-WorkflowDesigner.md#agent-implementation-instructions-);
> the highlights specific to 3.4:

- **Slice order:** **3.4.0 → (3.4.1 ∥ 3.4.2 ∥ 3.4.3 ∥ 3.4.4) → 3.4.5.** After the shell,
  the four feature slices are largely independent; do them in the order above but they can
  interleave. 3.4.5 closes the phase.
- **Read the real contracts first:** `Workflow.Api/Contracts/Scripts/ScriptContracts.cs`
  (wire shapes), `Workflow.Api/V1/ScriptEndpoints.cs` (routes/policies),
  `Workflow.Scripting/Abstractions/IWorkflowScriptApi.cs` (the descriptor's source of
  truth), and the existing `CodeEditor.razor` + `monaco-interop.js` (the seam you extend).
  Capture a real `POST /scripts/test` response before building the results pane.
- **D2 guardrail (hard):** nothing in `Scripts/State/*` or `Api/*` may reference Blazor/JS
  interop types. The descriptor, template catalog, and test-run state are pure C# with
  xUnit specs. Monaco interop lives only in `ScriptEditor` + `monaco-interop.js`.
- **No new backend for the MVP.** If a slice seems to need a server change (e.g. the
  descriptor endpoint, Q1), stop and ask — it's a post-MVP (3.4.P1), not an MVP task.
- **Descriptor drift-guard is mandatory (D4):** write `Descriptor_CoversWorkflowApi_NoDrift`
  early; it's the safety net that keeps IntelliSense honest.
- **bUnit + loose JS interop:** the Monaco path is faked in tests (as in 3.3.b.3) — assert
  on the *register/insert* interop calls and on the **textarea fallback** edit surface, not
  on real Monaco behavior. Use `data-testid` selectors (emoji text is mojibake-prone).
- **Bookkeeping ritual:** check off Tasks **and** Tests per slice, flip slice headers to
  `✅ DONE`, then at phase end check the Success Criteria, add an Overview completion banner,
  and update the `Phase3-AdvancedFeatures.md` §3.4 + `phases/README.md` pointers — exactly
  as 3.1/3.2/3.3 were closed. Track a todo per slice (`3-4-0`…`3-4-5`).
- **Repo gotchas:** `Workflow.sln`; Central Package Management (no new packages expected —
  Monaco is static JS, bunit already added); PowerShell has no `&&`; known-flaky parallel
  tests verified in isolation are not regressions.

---

## Post-MVP Slices 🚧 *(deferred — not blocking 4.x)*

### 3.4.P1 Reflection-backed API descriptor endpoint 🔍 *(Q1)*
`GET /api/v1/scripts/api-descriptor` reflecting `IWorkflowScriptApi` (+ XML docs) so the
descriptor can't drift; the client consumes it instead of the static catalog.

### 3.4.P2 User-defined / persisted templates 🧩 *(Q3)*
Persist custom templates (via the library system or a new store) with team sharing.

### 3.4.P3 Deeper IntelliSense 💡 *(Q5)*
Signature help, parameter hints, and richer diagnostics (e.g. linting the `workflow.*`
usage, flagging gated calls when the config disallows them).

### 3.4.P4 Python authoring end-to-end 🐍 *(Q2 / 3.1.P1)*
Enable running Python once the Python executor ships (3.1.P1); wire completions for the
Python `workflow` binding.

### 3.4.P5 C#/Lua live completions 🟪🌙
Extend completions/hover beyond JavaScript to the C# and Lua `workflow` bindings (needs
language-server-ish support in Monaco or a lighter custom provider).

---

## Success Criteria ✅

- [x] Open `/scripts`, pick a runnable language, and edit code in Monaco (with a working textarea fallback)
- [x] Typing `workflow.` offers `workflow.*` completions with hover docs; the API reference panel is searchable and inserts calls at the cursor (S1)
- [x] The template catalog offers ≥10 templates and inserts them into the editor
- [x] ▶ Test runs the current script via `POST /api/v1/scripts/test` and shows result, level-filtered logs, staged variable updates, duration, and structured errors — in-panel
- [x] Libraries list/open/save/delete work over `/api/v1/scripts/libraries`, including "save current editor as a library"
- [x] A `builtin.script` node in the designer opens its code in Script Studio and applies edits back as one undoable change (D9)
- [x] The API descriptor provably matches `IWorkflowScriptApi` (drift-guard test green)
- [x] **Zero new API/engine/scripting code required for the MVP**; the client touches only the shipped `/scripts/*` endpoints (D2)
- [x] `docs/script-studio.md` exists and cross-links `docs/scripting.md`; the React-port checklist is updated
- [x] State services covered by xUnit specs; components have bUnit tests; full `Workflow.Tests.UI` suite green (200 tests)

---

*Made with 💖 by Ami-Chan! A cozy editor makes for happy scripts~ UwU* ✨
