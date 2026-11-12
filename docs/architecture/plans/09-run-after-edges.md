# Plan 09 — "Run after" edges (sequencing without data)

**Status:** decided 2026-11-11 (asked in two rounds). **Implemented** 2026-11-11 in three commits: model/engine/validators;
designer; samples + docs. User docs: `docs/advanced-flow-control.md` §6, `docs/designer.md`. A **separate edge kind** (not an
implicit `input` port on every module, and not "keep strict").

## Decisions

| Question | Decision | Not chosen |
| --- | --- | --- |
| Approach | A separate run-after edge kind | implicit `input` port on every module; keep strict and reshape samples |
| Q1 Representation | `"kind": "RunAfter"` on the connection (`Data` by default, omitted in JSON; names read case-insensitively, numbers too); no target port | reserved port name `@after` |
| Q2 Legacy `input` edges into a module that doesn't declare `input` | **Read as run-after** (normalised on load and in the validators; the designer shows them as run-after) | keep `7c97e9d` only; reject |
| Q3 FanIn | Waits for a run-after edge, adds **no branch** to the result | an empty branch |
| Q4 Designer | Drop a wire on the node body → dashed "after" edge; the edge inspector toggles Data / Run after | a dedicated "after" handle |

## Problem

A connection today is always *data*: `source.port → target.port`, and the engine copies the source
output into `inputs[targetPort]`. About 30 built-in modules declare only data inputs and no `input`
port (condition, foreach, while, switch, setvariable, throw, transforms, file writers…), and 7
declare none at all (database execute/query/linq/bulkinsert, S3, Azure Blob, stream.collect).

| Layer | An edge into a node "just to run after" |
| --- | --- |
| Engine (`WorkflowExecutor` / `SubGraphExecutor`) | Works with any port name: readiness and skip use predecessors, not ports; an undeclared port's value is ignored by the module |
| API validator (`ModuleAwareWorkflowValidator`, MA004) | Rejected unless the port is declared. Since `7c97e9d` an `input` edge is accepted on modules that declare **no** inputs |
| Designer (`NodePorts.Inputs`) | Offers only the declared ports (or `input` when none) |

So "Try → Condition" can only be drawn into the `condition` port, and the incoming value replaces
the condition's expression. The flow-control sample and the wiring example in
`docs/advanced-flow-control.md` rely on "run after", and engine tests use `→ input` freely.

## Proposal

- **Model:** `ConnectionDefinition` gains `Kind` = `Data` (default; today's behaviour) or
  `RunAfter`. A run-after edge keeps its **source port**, so port routing still applies (condition
  `true`/`false`, switch cases, try/catch/finally, loop bodies), as do edge conditions and priority.
  It has **no target port** (empty). Old JSON without `kind` reads as `Data`. (Q1)
- **Engine:** a run-after edge counts for readiness, skip propagation, body membership and joins
  like any edge, but **copies no data**: nothing lands in `inputs`, and `{{input}}` / `{{src.port}}`
  are unaffected (`{{nodeId.port}}` already reaches any finished ancestor). It never forms a
  streaming edge.
- **Validators:** core `WF009` (empty target port) and `MA004` skip run-after edges. A data edge
  into an undeclared port stays an error, apart from legacy `input` edges (Q2).
- **API / import-export / persistence:** `kind` travels in the JSON (string enum, omitted when
  `Data`). SQLite, Postgres and NATS store definitions as JSON, so no schema change is needed.
- **Designer:** run-after edges are drawn dashed and labelled "after". They are created by dropping a
  wire on the node body rather than on a port, and the edge inspector can switch an edge between
  Data and Run after (Q4).

## Questions as asked

- **Q1 — Representation.** `"kind": "RunAfter"` on the connection (recommended), or a reserved target
  port name (e.g. `"@after"`), which needs no model change but is a magic string.
- **Q2 — Existing `input` edges into modules that don't declare `input`** (stored workflows, engine
  tests, the `7c97e9d` rule). Options:
  - read them as run-after (normalise on load and in the validator; recommended, so nothing that
    runs today breaks);
  - keep `7c97e9d` only;
  - reject them.
- **Q3 — FanIn.** A run-after edge into a FanIn: the FanIn waits for it, but should it add an
  (empty) branch to the result? Recommended: it waits, adds no branch.
- **Q4 — Designer gesture.** Recommended: drop on the node body plus the inspector toggle. The
  alternative is a dedicated "after" handle on every node.

## As built (part 1)

- `ConnectionDefinition.Kind` (`ConnectionKind`, `[JsonConverter(JsonStringEnumConverter)]` on the
  enum so the persistence options — which have no global enum converter — also write names) and
  `IsRunAfter`. `Data` is omitted (`JsonIgnore WhenWritingDefault`).
- Engine: `WorkflowExecutor.GatherInputs` and `SubGraphExecutor` skip run-after edges entirely (no
  port value, no `src.*` copies, no FanIn branch). Readiness/skip still go through predecessors.
  `StreamRegionDetector.IsStreamingEdge` is false for run-after.
- Validators: core `WF009` and `MA004` skip run-after edges; MA004 also accepts a legacy data edge
  into an undeclared `input` port on **any** module (Q2). The engine keeps passing a legacy edge's
  value to `inputs["input"]` (nothing that ran before changes); the designer will show it as run-after.
- UI: `ConnectionDto.Kind` (string, omitted when null), `DesignerConnection.Kind`/`IsRunAfter`,
  carried through load/save, clone and paste (paste also kept dropping `BufferCapacity`; fixed).
- Tests: `Engine/RunAfterEdgeTests` (5), `Api/V1/RunAfterEdgeApiTests` (5), validator tests (core 1,
  module-aware 4), `StreamRegionDetectorTests` (1), UI `State/RunAfterConnectionTests` (3). 11/11
  mutants killed.

## As built (part 2 — designer)

- `Designer/State/RunAfterEdges`: `Create`, `HitTestNodeBody`, `CanAdd` (no self, duplicate or
  cycle), `IsUndeclaredInput`, `NormaliseLegacy` (run by `DesignerDocument.FromDto`; only when the
  target's schema is known), `Anchor` (middle of the node's top edge).
- Canvas: releasing a wire on a node's body creates a run-after edge. A wire into the default
  `input` handle of a module with no inputs is run-after too. Both are undoable.
- `EdgeLayer`: run-after edges are dotted (`2 4`), `data-edge-kind="RunAfter"`, labelled "after"
  ("after · <condition>" with a condition).
- Inspector: "To" shows "after (no data)". A **Carries** select (`conn-kind`) offers
  "Data → <port>" for each declared input and "Run after (no data)". Switching is
  `SetConnectionTargetCommand` (undoable); the edge stays selected under its new key
  (`DesignerConnection.Key` ends `@after` for run-after).
- `GraphValidator`: MA004 skips run-after edges and legacy `input` edges; the streaming shape rule
  skips run-after. `StreamGraph.IsStreamingEdge` (the UI's mirror of the engine detector) is false
  for run-after.
- **Spec change (flagged):** two `PortValidationTests` asserted that a wire into an undeclared
  `input` port is an error. Under Q2 that edge is a legacy run-after edge, so they now use a typo
  port (`inptu`). Their assertions are otherwise unchanged.
- Tests: UI `State/RunAfterEdgesTests` (10), `Components/RunAfterDesignerTests` (4). 14/14 mutants
  killed.

## As built (part 3 — samples, docs)

- `examples/definitions/*.json` load and are correct:
  - GUID ids; `PropertyType` names; `name` + `initialValue` (was the ignored `defaultValue`);
  - current module ids and ports, with run-after edges where only ordering was meant;
  - `{{Variable.x}}` templates;
  - the notify token is a secret with no value; the dead `$schema` link is removed.
- `Api/V1/SampleWorkflowDefinitionTests`:
  - every sample passes `POST /api/v1/workflows`;
  - every key maps to a model property (`metadata` and connection `comment` are allowed as
    documentation);
  - the flow-control demo runs to completion with both branches taken.
- Docs: "Data vs run-after edges" (advanced-flow-control §6), designer Connect bullet.

## Parts

1. Model + engine + validators + API/JSON round-trip (tests: a run-after edge sequences without
   data in top-level, loop and try bodies; skip propagates; condition routing; FanIn per Q3; legacy
   per Q2).
2. Designer: drawing, inspector toggle, validation, `{{x}}` picker unaffected.
3. Samples (`examples/definitions/*.json`) and `docs/advanced-flow-control.md` use run-after edges.
   `SampleWorkflowDefinitionTests` loads every sample through `POST /api/v1/workflows` and runs the
   flow-control demo end to end.

## Blast radius

`ConnectionDefinition` is used across the engine, API, UI DTOs and persistence. The default `Data`
keeps every existing definition and test unchanged except where Q2 changes legacy `input` edges.
