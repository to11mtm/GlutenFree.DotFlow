# Universal "Merged Output" Option (Design & Plan)

> 📋 Follow-up to FanIn's `meta` option ([`Designer-UX-Feedback-Plan.md`](Designer-UX-Feedback-Plan.md)):
> users want the same "one merged output object" choice on **every module where it makes sense**
> (data modules), while control-flow modules (try/catch, fan-out, switch…) keep routing ports.

## Goal

Any **data module** (≥2 output ports, none of them control-flow routes) can be switched, per
node, to emit **one `output` port** whose value is an object of all its outputs — e.g.
`builtin.http.request` in merged mode emits
`output = { statusCode, headers, body, success, durationMs, … }`.

## Survey (which modules qualify)

- **Eligible (data modules, ≥2 outputs):** `http.request` (8 ports), `script` (3),
  `transform.aggregate/join/json/string/…` (2–5), all `file.*` readers/writers (2–6),
  `database.query/execute/bulkinsert/transaction` (4–5), `cloud.s3/azureblob` (8),
  `getvariable` (3), `setvariable` (2), `delay` (2), `log` (2), `csv.*`, `json.*`, `xml.*`.
- **Excluded (routing/structural ports):** `condition`, `switch`, `fanout`, `parallel`,
  `loop.foreach`, `loop.while`, `break`, `continue`, `throw`, `trycatch`, and `passthrough`
  (single output). **`fanin` keeps its richer `meta` option** and is excluded here.

## Design decisions

- **D1 — One reserved property, zero per-module edits.** A well-known node property
  **`outputMode`** (`ports` default | `merged`) handled **centrally by the engine**, not by each
  module's `ExecuteAsync`. Every current and future data module gets the feature for free;
  module code is untouched.
- **D2 — Engine-side shaping.** `WorkflowExecutor` (NodeCompleted path) replaces the module's
  outputs with `{ output: { …all outputs… } }` when the node's definition has
  `outputMode=merged`. History records, real-time events, and downstream input routing all see
  the shaped outputs — one consistent story. Structural executors (loop/parallel/trycatch
  completion paths) are untouched.
- **D3 — Validation treats it as reserved.** `outputMode` is whitelisted in
  `ValidateNodeProperties` (no MA002 "unknown property"), and both port validators
  (`ModuleAwareWorkflowValidator.ValidateConnectionPorts` MA003 + the engine's load-time
  `ValidateConnectionPorts`) accept source port **`output`** when the source node is merged.
- **D4 — Designer surfaces it as a synthetic selector.** The properties panel shows an
  **"Output mode"** dropdown (ports / merged) for **eligible** nodes — eligibility computed
  client-side: ≥2 schema output ports, module not in the control-flow exclusion list, not
  `fanin`. On `merged`, the canvas collapses the node's output ports to a single **`output`**
  port (mirrors `NodePorts`' FanIn-meta behaviour); existing edges from old ports keep rendering
  (validation flags them on save so the user re-wires deliberately).
- **D5 — UI stays contracts-only.** The UI duplicates the tiny string constants
  (`outputMode`/`merged`/`output`) in a framework-free state class rather than referencing
  `Workflow.Core` (D2 boundary).

## Slices

- [x] **U0 — Core + engine:** `Workflow.Core.Models.OutputShaping` (constants + `Merge`);
      engine shaping at NodeCompleted; engine load-time port validation accepts `output` for
      merged nodes. Tests: merged E2E (downstream receives the merged object), default
      unchanged, load-time validation passes for `output` connections.
      *(Done — E2E test in `FanInModuleTests.cs` proves both shaping and load-time validation.)*
- [x] **U1 — Server validation:** `ModuleAwareWorkflowValidator` — reserve `outputMode`
      (MA002) + accept merged `output` source port (MA003). Tests for both.
      *(3 tests, incl. the negative case: non-merged `output` connection still fails MA003.)*
- [x] **U2 — Designer:** `OutputShapingUx` (framework-free eligibility + constants);
      `NodePorts.Outputs` collapse to `output` when merged; PropertiesPanel "Output mode"
      selector for eligible nodes (writes the property via the normal undoable edit path).
      bUnit tests: selector visibility (eligible vs control-flow vs fanin), port collapse.
      *(4 tests; selecting "ports" removes the property entirely.)*
- [x] **U3 — Docs + plan sync:** `docs/designer.md` + `docs/advanced-flow-control.md` note;
      tick this doc's boxes.

## Out of scope

Per-port include/exclude picking (merged always includes all outputs); FanIn (has `meta`);
changing any module's default behaviour (`ports` remains default everywhere).
