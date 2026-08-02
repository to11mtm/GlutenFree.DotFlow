# Designer Export & Import — Resolution Plan

> 📋 Response to [`Designer-Save-And-Import.md`](Designer-Save-And-Import.md) (user feedback,
> 2026-08-02). Same format as
> [`Designer-Workflow-Variables-Plan.md`](Designer-Workflow-Variables-Plan.md): findings first, then
> items with slices, then the questions that change the shape of the work.
>
> **Revision 2 (2026-08-02)** — Q1–Q6 answered; decisions folded in. Two answers changed the shape:
> **overwrite-if-existing is wanted** (Q3, was "create-new only") and **`createdAt`/`updatedAt`/
> `version` do travel** (Q5, was "drop them").

## The ask

| # | Feedback | Reading |
| --- | --- | --- |
| 1 | "a way to export and import workflows" | Round-trip a workflow to a file and back — between environments, into source control, and as a sharing/backup mechanism |
| 2 | "semi-human readable format… JSON / YAML (not preferred) / ???" | JSON. See **F4** — the current JSON is *machine* readable but has one genuinely unreadable wart |
| 3 | "maintain the layout of the modules as shown in the Designer" | Already works — see **F1**. Good news |
| 4 | "on import we should zoom to the full view" | Already exists as `CanvasView.FitAsync()` — see **F2**. Just needs calling |

## Summary

| # | Item | Kind | Size | Status |
| --- | --- | --- | --- | --- |
| E1 | Export a workflow to a `.json` file from the designer + list | Feature | M | ✅ done |
| E2 | Import a file → new workflow, with pre-flight validation | Feature | M–L | ✅ done |
| E3 | Human-readable enums in the exported format (**F4**) | **Bug-ish** | M | ✅ done |
| E4 | A stable, versioned envelope for the file format | Design | S | ✅ done |
| E5 | Portability report — what won't survive the trip | UX | M | ✅ done |
| E6 | Secret & credential safety on export | Security | S–M | ✅ done |
| E7 | Docs | Docs | S | ✅ done |

Recommended order: **E4 → E3 → E1 → E5 → E2 → E6 → E7**. Format decisions (E4/E3) come first because
every exported file is a compatibility commitment the moment a user saves one.

---

## Findings 🔬

### F1 — Layout already round-trips ✅

Node position is persisted end-to-end, not designer-only:

```csharp
// Workflow.Core/Models/NodeDefinition.cs:41
Position? Position = null,

// Workflow.UI/.../Designer/State/DesignerNode.cs — FromDto
X = dto.Position?.X ?? 0,
Y = dto.Position?.Y ?? 0,
```

`Position` is documented as *"used purely for UI layout and doesn't affect workflow execution"*, and
`DocumentAndGeometryTests.Document_FromDto_ToDto_RoundTripsLosslessly` already asserts the
round-trip. **Feedback item 3 needs no engine work** — it falls out of exporting the DTO.

`RegionId` (visual grouping) is also persisted. What is *not* in the DTO is `DesignerNode.Schema`,
which is resolved from the module registry at load time — correctly so, since a schema belongs to the
installed module, not the workflow.

### F2 — "Zoom to full view" already exists ✅

`CanvasGeometry.FitToContent(bounds, viewportWidth, viewportHeight, padding)` exists and is already
wired to a **⤢ Fit** button via `CanvasView.FitAsync()` (`CanvasView.razor:75,254`), which is
**public**. Import just calls it after load. **Feedback item 4 is a one-line integration.**

### F3 — The wire format is export-ready, and provably lossless

`WorkflowDto` carries everything: nodes (with position, properties, metadata, timeout, error
handling, retry policy, region), connections (with condition + priority), variables, trigger, error
handling, tags. JSON is camelCase with nulls omitted, and LanguageExt converters (`Arr`, `HashMap`,
`Option`) are registered on both sides.

Critically, `ApiClientTests.Dtos_RoundTrip_NoDataLoss` already asserts that domain → wire → client
DTO → domain produces **byte-identical** JSON. That test is the foundation the export format can
stand on.

### F4 — ⚠️ The current JSON is not "semi-human readable" in one specific, fixable way

**No `JsonStringEnumConverter` is registered anywhere in the solution.** Every enum crosses the wire
as an integer. A variable declaration exports as:

```json
{ "name": "orderId", "type": 0, "seed": 0, "isSecret": false }
```

`"type": 0` is `PropertyType.String`. `"seed": 0` is `VariableSeedMode.SeedOnly`. Nobody reading a
diff can tell — and worse, **the ordinals are the contract**: Phase 3.5 needed a drift-guard test
(`VariableEnumDriftTests`) precisely because reordering `PropertyType` would silently re-type every
variable.

For a format that goes into source control and gets reviewed in pull requests, this is the single
biggest readability gap, and it's the one place the feedback's "semi-human readable" requirement
isn't already met. It also *improves* robustness: `"type": "String"` doesn't break when an enum is
reordered.

**This is a wire-format change with blast radius** — see **Q2**.

### F5 — Import must reconcile against installed modules, and today that's all-or-nothing

`ModuleAwareWorkflowValidator` runs on POST/PUT and returns **422** when a node references a module
that isn't installed or is disabled (`WorkflowEndpoints.cs`). So importing a workflow that uses a
module this environment lacks fails wholesale, with a validation error rather than an explanation.

The designer already models this more gracefully: `DesignerNode.Schema` is null for unknown modules
and `IsModuleKnown` is false — nodes are **preserved, not dropped**. So the designer can *open*
something the API would refuse to *save*. Import should exploit that gap deliberately: load,
show what's wrong, let the user fix it, then save. See **E5**.

Note also `Metadata["moduleVersion"]` pinning (`NodeExecutor.ResolvePinnedVersion`): a pinned
version that doesn't exist in the target environment fails at *execution*, not at import — a
portability trap worth surfacing up front.

### F6 — Ids: workflow is server-assigned, nodes are workflow-local

```csharp
// WorkflowEndpoints.cs:122 — create handler
var toCreate = definition with { Id = Guid.Empty };  // client-supplied id is ignored
```

The server always assigns the workflow id, so import can't collide on it — importing the same file
twice yields two workflows. Node ids are unique *per workflow only*, generated as `{stem}-{n}`
(`NodeIdGenerator`), so they need no remapping on import.

That means **the natural import semantic is "create new", not "restore over"** — which is the safe
default, but makes round-tripping edits back into the *same* workflow a separate question (**Q3**).

### F7 — No file download or upload exists for workflows

There's no download helper anywhere in the client — no JS `saveAs`/blob helper, and `ILocalStorage`
is string-only and unsuitable. **Upload** has an established pattern to copy: `UploadDialog.razor`
uses `InputFile` + drag-drop + `OpenReadStream()` for `.wfmod` module packages.

### F8 — Definitions have no format version, and secrets are only half-guarded

There is **no `formatVersion`** on the definition and no migration logic. Backward compatibility has
so far been maintained by adding trailing optional record parameters (e.g. Phase 3.5's
`VariableDefinition.Seed`/`IsSecret`). That works within a running system; it is much weaker once
files exist on disk that outlive the deployment that wrote them. Hence **E4**.

On secrets: `VariableDefinition.IsSecret` exists and its own doc comment already anticipates this
feature —

> *"A secret variable must not carry an `InitialValue` — workflow definitions are exported and
> version-controlled, so they must never contain a credential."*

— and the designer enforces it (V1.4). Database modules reference a `connectionId` rather than a raw
connection string, so connections don't leak either. **But node properties are arbitrary
`JsonElement`** with no redaction: an HTTP node's `apiKey`/`bearerToken`/`password` properties
(which exist, and are template-enabled) are exported verbatim. See **E6**.

---

## Proposed items

### E1 — Export 📤

- [x] E1.1 A JS download helper (`wwwroot/js/download.js`) — blob + object URL + revoke. This is the
      one piece of new plumbing; `IJSRuntime` is already used throughout.
- [x] E1.2 **⬇ Export** in the designer toolbar → downloads the current document.
      Export the **saved** state or the **in-memory buffer**? See **Q4**.
- [x] E1.3 **Export** per row in the workflow list (fetches the DTO, no designer round-trip needed).
- [x] E1.4 Filename: `{slugified-name}-{version}.dotflow.json`; content **indented**, since
      readability is the point (`WriteIndented = true` — the API's compact wire form is wrong here).
- [x] E1.5 Deterministic key ordering so re-exporting an unchanged workflow produces an identical
      file — otherwise every export is a spurious diff in source control.
- [x] E1.6 Tests: exported bytes parse back to an equal `WorkflowDto`; export is byte-stable across
      repeated calls.

### E2 — Import 📥

- [x] E2.1 **⬆ Import** on the workflow list, reusing the `UploadDialog` pattern (F7):
      `InputFile` + drag-drop, `.json` accept filter, size cap.
- [x] E2.2 Parse + envelope validation (E4) with legible errors — "this isn't a DotFlow workflow
      file" beats a raw `JsonException`.
- [x] E2.3 Open the imported document **in the designer, unsaved**, rather than POSTing it straight
      to the API. This is the key decision (**Q1**): it lets the user see the portability report
      (E5), fix problems, and choose the name — and it sidesteps F5's all-or-nothing 422.
- [x] E2.4 Call `CanvasView.FitAsync()` after load (F2) — feedback item 4, done.
- [x] E2.5 Default is **create new**: the incoming id is dropped (the server assigns one anyway,
      F6) and the document opens dirty/unsaved so nothing is written until the user saves.
- [x] E2.8 **Overwrite-if-existing** *(Q3)*: when the file's workflow id matches one that exists
      here, offer to overwrite instead of creating a copy. Needs a hard-to-miss warning — overwrite
      discards the stored definition. Show the match **by name**, so the user confirms something
      legible rather than a GUID. `PUT`'s existing 409-on-older-version is a second safety net.
- [x] E2.6 Name collision handling — offer `"{name} (imported)"` when the name already exists.
- [x] E2.7 Tests: round-trip export → import → identical document; malformed file surfaces a clear
      error; imported document is dirty and has no id until saved.

### E3 — Readable enums 🔤

- [x] E3.1 Register `JsonStringEnumConverter` so enums serialise as names (F4).
      **Where** it applies is **Q2** — export-only vs the whole wire format.
- [x] E3.2 Deserialisation must accept **both** names and numbers, so existing stored definitions
      and any file exported before this change keep loading.
- [x] E3.3 Audit every enum that reaches the wire: `PropertyType`, `VariableSeedMode`,
      `PropertyEditorType`, `ExecutionState`, `NodeExecutionState`, `IssueSeverity`.
- [x] E3.4 If applied wire-wide, the designer's mirrored enums (`VariableValueType`,
      `VariableSeed`) and their drift-guard tests need updating in lockstep — those tests currently
      assert **ordinals are the contract** (`VariableEnumDriftTests`), which stops being true.
- [x] E3.5 Tests: enums round-trip by name; numeric input still parses; drift guard updated.

### E4 — A versioned envelope 🧾

- [x] E4.1 Wrap the payload rather than exporting a bare `WorkflowDto`:

      ```json
      {
        "dotflowFormat": 1,
        "exportedAt": "2026-08-02T14:00:00Z",
        "exportedBy": "…",
        "engineVersion": "1.4.0",
        "workflow": { … }
      }
      ```

- [x] E4.2 Reject unknown **major** format versions with a clear message rather than half-parsing.
- [x] E4.3 Record `engineVersion` for diagnostics and to power the E5 report — the same idea as
      `.wfmod`'s `MinEngineVersion` gate (Phase 2.8 D2), so the convention is consistent.
- [x] E4.4 Export `id`, `createdAt`, `updatedAt` and `version` *(Q5 — they carry the workflow's
      history, and the id is what makes overwrite-if-existing possible)*. Still excluded: execution
      history, stored variable values, connection registrations.
- [x] E4.5 Tests: envelope round-trips; unknown major version is rejected; a bare `WorkflowDto`
      (no envelope) is either rejected clearly or accepted as format 0 per **Q6**.

### E5 — Portability report 🧭

The bit that turns "it failed" into "here's what to do". Shown after parsing, before saving:

- [x] E5.1 **Missing modules** — node references a module id this environment doesn't have.
      *Error* (the API will 422 on save anyway); list the ids so the user can install them.
- [x] E5.2 **Pinned module version unavailable** — `Metadata["moduleVersion"]` names a version that
      isn't installed. *Warning*: it imports and saves fine and fails only at execution (F5), which
      is exactly the kind of late failure worth pulling forward.
- [x] E5.3 **Unknown `connectionId`** — database nodes reference named connections that don't exist
      here. *Warning* with the list; connections are environment config by design.
- [x] E5.4 **Referenced globals not present** — reuse `VariableLint` (Phase 3.5 V7), which already
      computes exactly this. *Warning*.
- [x] E5.5 **Secrets** — declared secret variables have no value by design; say so plainly so the
      user knows to supply them.
- [x] E5.6 Tests: each condition detected and correctly classified error vs warning.

### E6 — Secret safety on export 🔒

- [x] E6.1 Confirm and test that a secret variable never exports a value (V1.4 already prevents an
      `InitialValue`; this is the regression guard now that files leave the building).
- [x] E6.2 **Node properties are the real gap** (F8). HTTP nodes have `apiKey`, `bearerToken`,
      `password`, `oauth2ClientSecret` properties that export verbatim. Options in **Q6**:
      redact on export, warn on export, or leave it and document. My proposal: **warn loudly**,
      listing the properties, and offer "export with these redacted".
- [x] E6.3 Reuse the secrets plan's redactor if it exists by then
      ([`Designer-Workflow-Variables-Secrets-Plan.md`](Designer-Workflow-Variables-Secrets-Plan.md) S4)
      rather than inventing a second one.
- [x] E6.4 Tests: secret variable value never appears in exported bytes; credential-shaped
      properties trigger the warning.

### E7 — Docs 📚

- [x] E7.1 New `docs/import-export.md`: the format, the envelope, what does and doesn't travel,
      the portability report, and the source-control story.
- [x] E7.2 Cross-link from `docs/designer.md` and `docs/README.md`.
- [x] E7.3 Document the overwrite semantics (Q3) and the deliberate exclusions (Q5: no execution
      history, stored variable values or connection registrations).

---

## Questions — RESOLVED ✅ (2026-08-02)

- **Q1 (E2) Import path** → **Designer-first.** Parse client-side, open unsaved, show the
  portability report, user saves. No new endpoint; a headless `POST /import` can follow later if
  automation needs it.
- **Q2 (E3) String-enum scope** → **Fix the whole wire format.** Pre-release, so take the breaking
  change now rather than shipping two formats that drift. Reads stay number-tolerant.
- **Q3 (E2.5) Overwrite** → **Wanted.** Import offers an *overwrite-if-existing* option with a
  clear UX warning. (Changed from my "create-new only" proposal — folded into E2.5/E2.8.)
- **Q4 (E1.2) Export source** → **The in-memory document** — what you see is what you get.
- **Q5 (E4.4) What travels** → **Keep `createdAt`, `updatedAt` and `version`** — they're part of the
  workflow's history and versioning story. (Changed from my "drop them" proposal.) Still excluded:
  execution history, stored variable values, connection registrations.
- **Q6 (E6.2/E4.5)** → (a) Credential-shaped node properties: **warn, with an opt-in "redact these"
  checkbox**. (b) Bare `WorkflowDto` with no envelope: **accept as format 0 with a warning**.
---

## What's cheap, and what isn't 💰

Worth stating plainly, because the feedback's two explicit requirements are the *easy* half:

| Ask | Cost |
| --- | --- |
| "maintain the layout" | **Free** — already round-trips (F1) |
| "zoom to full view on import" | **~1 line** — `FitAsync()` already exists and is public (F2) |
| Export a file | **Small** — one JS download helper + a button (F7) |
| Import a file | **Medium** — upload pattern exists, but reconciliation (E5) is the real work |
| "semi-human readable" | **Medium** — the format is *nearly* there; enums are the wart, and fixing them properly has wire-format blast radius (F4, Q2) |
| Not leaking credentials | **Medium** — variables are handled, node properties are not (F8, E6) |

---

*Created 2026-08-02. Findings F1–F8 verified against the code at that date. No implementation has
started; E1–E7 are blocked on Q1/Q2.*
