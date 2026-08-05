# SnapLogic vs DotFlow — Analysis Plan 📋

> Task plan for comparing GlutenFree.DotFlow's workflow composition architecture against
> SnapLogic (commercial iPaaS), and producing porting guidance.

## Goals

1. **Explain our architecture** from a composition standpoint (artifact `01`).
2. **Compare** against SnapLogic's composition model (artifact `02`).
3. **Advise on porting** SnapLogic pipelines to DotFlow (artifact `03`).
4. *(Future step — deferred)* Read SnapLogic `.slp` pipeline JSON and convert to DotFlow
   workflow definitions (feasibility notes only, artifact `04`).

## Scope (confirmed)

- **Audience:** both DotFlow developers (feature gaps) and users migrating from SnapLogic.
- **SnapLogic coverage:** core composition (pipelines, Snaps, views, expression language,
  Mapper/Router/Copy/Union/Join/Pipeline Execute) **plus** Ultra pipelines, error pipelines,
  and Snaplex runtime topology. Tasks/scheduling and Accounts covered where relevant to porting.
- **Out of scope for now:** the `.slp` importer implementation itself.

## Artifacts

| File | Contents | Status |
| --- | --- | --- |
| `00-PLAN.md` | this plan | ✅ |
| `01-dotflow-architecture.md` | DotFlow composition architecture explainer | ✅ |
| `02-snaplogic-comparison.md` | side-by-side architectural differences | ✅ |
| `03-porting-guide.md` | Snap→module mapping, gap analysis, porting advice | ✅ |
| `04-future-slp-import.md` | feasibility outline for a future `.slp` importer | ✅ |

## Comparison axes

- Graph model: nodes + connections vs Snaps + input/output/error **views**
- Data semantics: execution context/payload vs **streaming JSON documents**
- Control flow: DotFlow flow-control modules vs Router/Filter/Join/Gate Snaps
- Reuse: sub-workflows vs **Pipeline Execute** (child pipelines) + pipeline parameters
- Expressions: DotFlow templating & JS/C#/Lua scripting vs SnapLogic JS-like expression language
- Error handling: error boundaries/retries vs error views + error pipelines
- Runtime: single-process Akka.NET actor system vs Snaplex control-plane/data-plane split
- Low-latency: DotFlow trigger model vs **Ultra pipelines**
- Credentials: DotFlow variables/config vs SnapLogic **Accounts**

## Open questions / clarifications

- [x] Artifact location → `new-feature-design/snaplogic-analysis/`
- [x] Audience → both developers and migrators
- [x] SnapLogic scope → include Ultra, error pipelines, Snaplex topology
- [ ] Should identified DotFlow gaps be filed as phase docs (`phases/`) after review?
- [ ] For the future importer: target fidelity — structural conversion (graph + config
      placeholders) vs semantic conversion (expression translation)? Structural is far cheaper.

## Method

1. Fact-gathering on DotFlow internals (`Workflow.Core` models, `Workflow.Engine` actors,
   module system, docs/).
2. Fact-gathering on SnapLogic from public documentation.
3. Write artifacts `01`–`04`; cross-link from `DOCUMENTATION_INDEX.md` if appropriate.
