# 04 — Future: Reading SnapLogic `.slp` Pipeline JSON 🔮

> **Deferred** — feasibility notes only, per the analysis plan. Do not implement yet.

## What we know about the format

`.slp` exports are JSON. Reconstructed structure (⚠️ the authoritative legacy doc page is
login-gated — **verify against real exports before coding**):

| Key | Purpose | Maps to DotFlow |
| --- | --- | --- |
| `class_fqid` | pipeline class id | — |
| `snap_map` | snap instance id → `{class_fqid, property_map, label, …}` | `NodeDefinition` (ModuleId via mapping table, Properties via per-snap translators) |
| `link_map` | source snap+view → target snap+view | `ConnectionDefinition` (view index → port name) |
| `render_map` | canvas x/y per snap | `NodeDefinition.Position` |
| `property_map` | label, description, **parameters**, error-pipeline ref, expression libs | `Name`/`Description`, `Variables`, (error pipeline: unsupported) |

## Recommended fidelity target: **structural conversion**

Convert the graph shape, positions, labels, parameters→variables, and known Snap→module
mappings; emit `builtin.passthrough` placeholder nodes (with original snap config stashed in
`Metadata`) for unknown Snaps, plus a conversion report. Do **not** attempt automatic
expression translation in v1 — the SnapLogic expression subset differs enough (`$` roots,
`_params`, `lib.*`, `eval`) that silent mistranslation is worse than a flagged TODO.

## Prerequisites (from artifact 03 §6)

1. Obtain real `.slp` samples (several, incl. Router/Join/Pipeline Execute/error views) and
   pin the actual schema. **Licensing note:** use only pipelines we author/own.
2. `builtin.workflow.execute` module (Pipeline Execute has no target otherwise).
3. Snap→module mapping table (03 §2) encoded as data, with per-snap property translators.

## Sketch

- New project `Workflow.Import.SnapLogic` (parser + mapping registry + report generator),
  surfaced via `POST /api/v1/workflows/import/slp` and the existing import/export UI
  (`docs/import-export.md`).
- Per-document streaming semantics: importer wraps mapped per-document segments in a
  `builtin.loop.foreach` when the source snap is a known collection producer — behind a
  user-confirmed option, since intent can't always be inferred.

## Open questions

- [ ] Structural vs semantic expression translation for v2? (v1 = structural, flagged TODOs)
- [ ] How to represent unmapped Accounts — auto-create secret variable stubs?
- [ ] Should the importer emit a side-by-side validation harness (same input → compare)?
