# Designer Split Preview â€” Plan

> ðŸ“‹ Response to user feedback (2026-08-04): *"somewhat related to the [input hinting ask] as far
> as the split module, is that it would be nice to have a preview of what items would be included
> in the output split."*
>
> **Revision 1 â€” proposal.** Q1â€“Q2 below shape the scope; recommended answers are baked into the
> items. Related plans from the same feedback round:
> [Designer-Http-Input-Port-Plan.md](Designer-Http-Input-Port-Plan.md) and
> [Designer-Input-Shape-Hinting-Plan.md](Designer-Input-Shape-Hinting-Plan.md) â€” the hinting plan
> is the general mechanism; this one is a module-specific, sample-driven refinement of it.

## The ask

| # | Feedback | Reading |
| --- | --- | --- |
| 1 | "Preview of what items would be included in the output split" | Given a sample object, show **which keys land on which port** â€” including what falls into `restPort` and which keys would be `null` â€” *before* running the workflow |

## Findings ðŸ”¬

### F1 â€” Split's semantics are trivially replayable client-side

`SplitModule.ExecuteAsync` is pure data-shaping: resolve object (input port wins over the `value`
property), emit `outputs[key] = obj[key] ?? null` per key, remainder to `restPort`
(`SplitModule.cs:105-149`). No I/O, no engine services. **A preview does not need the server** â€”
the designer can replay the same rules on a sample JSON object in a few lines of client code, with
zero drift risk as long as it mirrors the published semantics (missing key â†’ `null`, rest =
unlisted properties).

### F2 â€” Preview precedent exists, but it's heavyweight by comparison

Script Studio and Linq Studio both have real preview machinery (`TransformScriptPreviewer`,
`WorkflowLinqPreviewer`, `POST /api/transform/script/preview`, `POST /api/database/linq/preview`) â€”
compile-and-execute against sample inputs, sandboxed. That pattern is right for *code*; it's
overkill for Split, whose entire behaviour is a dictionary comprehension. A server round-trip per
keystroke would make the feature feel worse, not better.

### F3 â€” Where does the sample object come from?

Three candidate sources, in decreasing availability:

1. **The `value` property** â€” when the user configured a static object, the preview input already
   exists in the node.
2. **A pasted sample** â€” when `value` comes from an upstream connection (the common case), the
   designer cannot know the runtime shape; a "sample input" scratch box (not persisted to the
   workflowâ€¦ or persisted as a designer-only metadata entry?) lets the user paste one
   representative object.
3. **Last-run outputs** â€” `GET /api/v1/executions/{id}/nodes` returns per-node `Inputs`/`Outputs`
   (`NodeExecutionRecord`), so after at least one run the *actual* upstream value is retrievable.
   Strictly better data, but requires an execution to exist and plumbing to fetch/select it.

### F4 â€” There is no per-module UI slot yet, but there are precedents for special-casing

`PropertiesPanel` renders generic editors per `EditorType`, with per-module extras already
precedented: Script nodes get an "Edit in Script Studio â†’" button, SQL modules get a
"ðŸ›¡ï¸ SQL parameters" modal. A "Split preview" section that appears only for `builtin.split` follows
the existing pattern â€” no new architecture needed.

### F5 â€” `DesignerNode.Metadata` can hold a designer-only sample

Nodes carry a `Metadata` dictionary (already used for `moduleVersion`, `ui.*` keys). A pasted
sample can persist as e.g. `ui.sampleInput` without touching engine behaviour â€” it round-trips
with the workflow file, which is exactly what you want for a team sharing a draft. (Q2 decides.)

## Proposed design ðŸŽ¨

A **preview section in the PropertiesPanel**, shown only for `builtin.split`:

```
â”Œâ”€ Split preview â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”
â”‚ Sample input   [ paste JSONâ€¦ / from value ]â”‚
â”‚                                            â”‚
â”‚  Foo   â†’ 1                                 â”‚
â”‚  Bar   â†’ "hello"                           â”‚
â”‚  Qux   â†’ null  âš  not present in sample     â”‚
â”‚  rest  â†’ { "Baz": 3, "Extra": true }       â”‚
â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜
```

- Sample source resolution: the `value` property when it parses as an object â†’ otherwise the
  `ui.sampleInput` metadata (paste box) â†’ otherwise an empty-state hint ("paste a sample object to
  preview the split").
- Recomputes live as `keys` / `restPort` / sample change â€” pure client-side (F1).
- Keys missing from the sample render with a soft warning (they'll emit `null` at runtime) â€”
  that's the "what would be included" question answered at a glance.
- Values render truncated (single line, expandable is out of scope).

## Open questions â“ â€” all decided 2026-08-04 âœ…

- [x] **Q1 â€” Client-side replay or server endpoint?** âœ… **Client-side**, with a semantics note
      left in `SplitModule.cs` so a future preview endpoint keeps the same rules. Two additions
      from review: **(a)** when Split's `value` input is wired from a **merged** upstream node,
      the preview derives the sample shape from schema alone â€” no paste, no replay (the merged
      object's keys are the upstream node's schema ports); **(b)** the previewed output must be
      usable downstream â€” Split's key/rest ports already flow into wiring and the token picker
      (P2 of the fanout round + T2 of the hinting round), and the rest port now expands into its
      remaining keys when the upstream shape is derivable.
- [x] **Q2 â€” Should the pasted sample persist?** âœ… **Toggle in the UI.** Persist ON â†’ sample
      stored as `ui.sampleInput` node metadata (undoable edit, survives reload, shares with the
      team). Turning the toggle OFF while a sample is stored asks via a **confirm**: primary =
      *remove the stored sample from the workflow* (undoable); secondary = *keep it* (stored value
      stays in the file; further edits are session-only).
- [x] **Q3 â€” "Last run" values as the sample?** âœ… Deferred, with notes here and in the hinting
      plan â€” both features want the same execution-history plumbing
      (`GET /api/v1/executions/{id}/nodes` per-node `Outputs`); do them together as the follow-up
      round.

## Items

| # | Item | Kind | Size | Status |
| --- | --- | --- | --- | --- |
| V1 | `SplitPreview` state helper â€” pure function: (sample JSON, keys, restPort) â†’ per-port preview rows | Infra | S | â¬œ |
| V2 | PropertiesPanel section for `builtin.split` (sample box + live result table, empty state) | UX | M | â¬œ |
| V3 | Persist pasted sample as `ui.sampleInput` metadata (Q2) | UX | S | â¬œ |
| V4 | Drift guard: fixture tests asserting `SplitPreview` matches `SplitModule` outputs for the same inputs | Tests | S | â¬œ |
| V5 | UI tests (bUnit: section renders per config, null-key warning, rest bucket, malformed sample) | Tests | S | â¬œ |
| V6 | Docs note under `builtin.split` | Docs | S | â¬œ |

### V1 â€” Preview computation ðŸ§®

- [x] V1.1 `SplitPreview.Compute(sampleJson, keys, restPort)` â†’ list of `(port, valuePreview,
      isMissing)` + rest row; mirrors SplitModule: missing key â†’ `null` + flag, rest = unlisted
      properties, invalid/non-object sample â†’ error result.
- [x] V1.2 Value rendering: compact single-line JSON, truncated at ~60 chars.

### V2 â€” Panel section ðŸ–¥ï¸

- [x] V2.1 Rendered only when `SelectedNode.ModuleId == "builtin.split"` (F4 precedent).
- [x] V2.2 Sample source: `value` property when object-parseable, else metadata sample, else empty
      state; a small "sample" text area with JSON validation.
- [x] V2.3 Live recompute on property buffer changes (same change pipeline the panel already uses).

### V4 â€” Drift guard ðŸ§ª

- [x] V4.1 Shared fixtures (sample object + keys + restPort â†’ expected port map) asserted against
      `SplitModule.ExecuteAsync` in `Workflow.Tests` and `SplitPreview.Compute` in
      `Workflow.Tests.UI`. A change to either side that diverges breaks a test by construction.

---

*Created 2026-08-04. Findings verified against `SplitModule.cs`, `PropertiesPanel.razor`,
`TransformScriptPreviewer.cs` / `WorkflowLinqPreviewer.cs` (preview precedents), execution-history
endpoints (`ExecutionEndpoints.cs`), and `DesignerNode.Metadata`.*


---

## What shipped 📦

| File | Change |
| --- | --- |
| `Workflow.UI.Client/Designer/State/SplitPreview.cs` | New (V1) — client replay of SplitModule (missing → null flagged, rest bucket, truncation); `ComputeFromShape` for the merged-upstream no-sample case (Q1a); keys/restPort/sample parsing; `ui.sampleInput` metadata accessor. |
| `Workflow.UI.Client/Designer/Components/PropertiesPanel.razor` | V2/V3 — "Split preview" section for `builtin.split`: shape mode when the value input is wired from a merged node; otherwise sample textarea + live rows; persist toggle with confirm-on-off (primary: remove stored sample — undoable; secondary: keep, session-only edits). Sample seeds from metadata on selection. |
| `Workflow.UI.Client/Designer/State/Commands/Commands.cs` | V3 — `EditNodeMetadataCommand` (set/remove one metadata entry, undoable). |
| `Workflow.UI.Client/Designer/State/InputShape.cs` | Q1b — Split's rest port expands into the remaining upstream-shape keys for downstream tokens/hints. |
| `Workflow.Modules/Builtin/Transform/SplitModule.cs` | Q1 note — remarks documenting the client mirror, the drift-guard fixtures, and how a future preview endpoint must be built (call the module). |
| `Workflow.Tests/Modules/Transform/SplitPreviewDriftGuardTests.cs` | New — server half of the shared drift fixture. |
| `Workflow.Tests.UI/State/SplitPreviewTests.cs` | New — 12 tests: client half of the drift fixture, shape mode, parsing, upstream-shape derivation, metadata command, rest expansion. |
| `Workflow.Tests.UI/Components/SplitPreviewPanelTests.cs` | New — 8 bUnit tests: rows/warnings/rest, persist toggle + confirm both paths, metadata seeding, merged shape mode. |
| `docs/advanced-flow-control.md` | Preview note under `builtin.split`, incl. the Q3 deferral (last-run samples follow-up). |

**Verification.** `Workflow.Tests.UI` 659/659 green. Server Split suites 16/16 green (incl. drift
guard). Solution build: 0 errors.

*Implemented and verified 2026-08-04. Q3 (last-run samples) deferred jointly with the
input-shape-hinting plan's Q3 — both need the execution-history plumbing
(`GET /api/v1/executions/{id}/nodes` per-node `Outputs`).*