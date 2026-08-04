# Designer Split Preview — Plan

> 📋 Response to user feedback (2026-08-04): *"somewhat related to the [input hinting ask] as far
> as the split module, is that it would be nice to have a preview of what items would be included
> in the output split."*
>
> **Revision 1 — proposal.** Q1–Q2 below shape the scope; recommended answers are baked into the
> items. Related plans from the same feedback round:
> [Designer-Http-Input-Port-Plan.md](Designer-Http-Input-Port-Plan.md) and
> [Designer-Input-Shape-Hinting-Plan.md](Designer-Input-Shape-Hinting-Plan.md) — the hinting plan
> is the general mechanism; this one is a module-specific, sample-driven refinement of it.

## The ask

| # | Feedback | Reading |
| --- | --- | --- |
| 1 | "Preview of what items would be included in the output split" | Given a sample object, show **which keys land on which port** — including what falls into `restPort` and which keys would be `null` — *before* running the workflow |

## Findings 🔬

### F1 — Split's semantics are trivially replayable client-side

`SplitModule.ExecuteAsync` is pure data-shaping: resolve object (input port wins over the `value`
property), emit `outputs[key] = obj[key] ?? null` per key, remainder to `restPort`
(`SplitModule.cs:105-149`). No I/O, no engine services. **A preview does not need the server** —
the designer can replay the same rules on a sample JSON object in a few lines of client code, with
zero drift risk as long as it mirrors the published semantics (missing key → `null`, rest =
unlisted properties).

### F2 — Preview precedent exists, but it's heavyweight by comparison

Script Studio and Linq Studio both have real preview machinery (`TransformScriptPreviewer`,
`WorkflowLinqPreviewer`, `POST /api/transform/script/preview`, `POST /api/database/linq/preview`) —
compile-and-execute against sample inputs, sandboxed. That pattern is right for *code*; it's
overkill for Split, whose entire behaviour is a dictionary comprehension. A server round-trip per
keystroke would make the feature feel worse, not better.

### F3 — Where does the sample object come from?

Three candidate sources, in decreasing availability:

1. **The `value` property** — when the user configured a static object, the preview input already
   exists in the node.
2. **A pasted sample** — when `value` comes from an upstream connection (the common case), the
   designer cannot know the runtime shape; a "sample input" scratch box (not persisted to the
   workflow… or persisted as a designer-only metadata entry?) lets the user paste one
   representative object.
3. **Last-run outputs** — `GET /api/v1/executions/{id}/nodes` returns per-node `Inputs`/`Outputs`
   (`NodeExecutionRecord`), so after at least one run the *actual* upstream value is retrievable.
   Strictly better data, but requires an execution to exist and plumbing to fetch/select it.

### F4 — There is no per-module UI slot yet, but there are precedents for special-casing

`PropertiesPanel` renders generic editors per `EditorType`, with per-module extras already
precedented: Script nodes get an "Edit in Script Studio →" button, SQL modules get a
"🛡️ SQL parameters" modal. A "Split preview" section that appears only for `builtin.split` follows
the existing pattern — no new architecture needed.

### F5 — `DesignerNode.Metadata` can hold a designer-only sample

Nodes carry a `Metadata` dictionary (already used for `moduleVersion`, `ui.*` keys). A pasted
sample can persist as e.g. `ui.sampleInput` without touching engine behaviour — it round-trips
with the workflow file, which is exactly what you want for a team sharing a draft. (Q2 decides.)

## Proposed design 🎨

A **preview section in the PropertiesPanel**, shown only for `builtin.split`:

```
┌─ Split preview ────────────────────────────┐
│ Sample input   [ paste JSON… / from value ]│
│                                            │
│  Foo   → 1                                 │
│  Bar   → "hello"                           │
│  Qux   → null  ⚠ not present in sample     │
│  rest  → { "Baz": 3, "Extra": true }       │
└────────────────────────────────────────────┘
```

- Sample source resolution: the `value` property when it parses as an object → otherwise the
  `ui.sampleInput` metadata (paste box) → otherwise an empty-state hint ("paste a sample object to
  preview the split").
- Recomputes live as `keys` / `restPort` / sample change — pure client-side (F1).
- Keys missing from the sample render with a soft warning (they'll emit `null` at runtime) —
  that's the "what would be included" question answered at a glance.
- Values render truncated (single line, expandable is out of scope).

## Open questions ❓

- [ ] **Q1 — Client-side replay (recommended) or server preview endpoint?** Client-side: zero
      latency, no new API, trivially testable; risk is semantic drift if SplitModule changes —
      mitigated by a shared spec test asserting the same fixtures against both implementations.
      Server endpoint (`POST /api/builtin/split/preview`): single source of truth, but new API
      surface + round-trips for a dictionary lookup. Recommended: **client-side**, revisit only if
      more modules grow previews (then a generic module-preview endpoint is the right investment).
  - Client side but leave notes in the server code for a future endpoint, so that if we ever do add one, we can ensure the semantics are consistent.
  - But just as important, When we are getting a merged output from a prior node, we should be able to handle at least that without any replay/etc because it's all at designer level.
  - Users want to be able to use the previewed output as the input to the next node in the graph.
- [ ] **Q2 — Should the pasted sample persist?** Recommended: yes, as `ui.sampleInput` node
      metadata (F5) — survives reload, shares with the team, engine ignores it. Alternative:
      session-only scratch state.
  - Yes we should have the option for persisting the saved simple input, but we should also have the option to not persist it or refresh it. This could be a toggle in the UI.
- [ ] **Q3 — Also pull "last run" values as the sample (F3.3)?** Recommended: defer to a follow-up
      — it's the best data but needs execution-history plumbing, and it's the natural second step
      of the *input-shape hinting* plan (which wants the same data for every module, not just
      Split). Ship the paste-box version first.
  - Defer and make sure we add notes. 

## Items

| # | Item | Kind | Size | Status |
| --- | --- | --- | --- | --- |
| V1 | `SplitPreview` state helper — pure function: (sample JSON, keys, restPort) → per-port preview rows | Infra | S | ⬜ |
| V2 | PropertiesPanel section for `builtin.split` (sample box + live result table, empty state) | UX | M | ⬜ |
| V3 | Persist pasted sample as `ui.sampleInput` metadata (Q2) | UX | S | ⬜ |
| V4 | Drift guard: fixture tests asserting `SplitPreview` matches `SplitModule` outputs for the same inputs | Tests | S | ⬜ |
| V5 | UI tests (bUnit: section renders per config, null-key warning, rest bucket, malformed sample) | Tests | S | ⬜ |
| V6 | Docs note under `builtin.split` | Docs | S | ⬜ |

### V1 — Preview computation 🧮

- [ ] V1.1 `SplitPreview.Compute(sampleJson, keys, restPort)` → list of `(port, valuePreview,
      isMissing)` + rest row; mirrors SplitModule: missing key → `null` + flag, rest = unlisted
      properties, invalid/non-object sample → error result.
- [ ] V1.2 Value rendering: compact single-line JSON, truncated at ~60 chars.

### V2 — Panel section 🖥️

- [ ] V2.1 Rendered only when `SelectedNode.ModuleId == "builtin.split"` (F4 precedent).
- [ ] V2.2 Sample source: `value` property when object-parseable, else metadata sample, else empty
      state; a small "sample" text area with JSON validation.
- [ ] V2.3 Live recompute on property buffer changes (same change pipeline the panel already uses).

### V4 — Drift guard 🧪

- [ ] V4.1 Shared fixtures (sample object + keys + restPort → expected port map) asserted against
      `SplitModule.ExecuteAsync` in `Workflow.Tests` and `SplitPreview.Compute` in
      `Workflow.Tests.UI`. A change to either side that diverges breaks a test by construction.

---

*Created 2026-08-04. Findings verified against `SplitModule.cs`, `PropertiesPanel.razor`,
`TransformScriptPreviewer.cs` / `WorkflowLinqPreviewer.cs` (preview precedents), execution-history
endpoints (`ExecutionEndpoints.cs`), and `DesignerNode.Metadata`.*
