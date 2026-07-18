# Phase 2.6: Data Transformation Modules (Weeks 17-18) 🔄

Made with 💖 by Ami-Chan! UwU ✨

[Back to Phase 2](Phase2-CoreFeatures.md) | [All Phases](README.md)

---

## Overview

Phase 2.6 ships DotFlow's **transformation family** — mapping, querying, aggregating, validating, and string/JSON/XML manipulation of in-memory workflow data. Like 2.4, it's **two families with a typed-first spirit**:

- **2.6.a Expression family** (2.6.a.0–2.6.a.6) — declarative, config-driven modules in `Workflow.Modules` under `Builtin/Transform/`. Per-item expressions run through the **existing `IExpressionEvaluator`** (2.2.5 — Jint default, DynamicExpresso keyed `"csharp"`), which is already sandboxed, cancellation-aware, and DI-registered. **Zero new heavyweight dependencies.**
- **2.6.b Typed C# script family** (2.6.b.0–2.6.b.2) — `builtin.transform.script`: users author a typed C# transform body (`IEnumerable<Row> → result`), **Roslyn-compiled at publish time, whitelist-validated, HMAC-cached in `IBlobStore`, executed in a collectible ALC** — a direct generalisation of 2.4.b's `builtin.database.linq` pipeline. The 2.4.b compile-cache-execute machinery is **extracted into a shared `Workflow.Scripting.Roslyn` project** so both families consume one copy (see D3/2.6.b.0).

This slice also **absorbs the query modules deferred from 2.5** (D9/Q2 there): `builtin.transform.jsonquery` (JSONPath, building on 2.3.5's `JsonPathExtractor`) and `builtin.transform.xmlquery` (XPath).

**Timeline:** 2 weeks (Weeks 17-18 — following 2.5's Weeks 15-16) — 2.6.a Week 17 · 2.6.b Week 18
**Complexity:** 🟠 Medium-High — 2.6.a is volume (7 modules) on well-trodden infrastructure; 2.6.b is a **refactor-then-reuse** of the hardest code in the repo (Roslyn pipeline + ALC lifecycle), where the risk is regression in 2.4.b, mitigated by its 53-test suite staying green

> **CopilotNote:** Hot paths: `Workflow.Modules/Builtin/Transform/*` (expression family — same placement rules as the HTTP/File families), **new `Workflow.Scripting.Roslyn`** (extraction target for `ForbiddenSyntaxWalker`, `ReferenceWhitelist`, `CompiledAssemblyCache`, `HmacLinqAssemblySigner`, `CollectibleScriptRunner`), `Workflow.Modules.Transform.Script` (quarantined script module — Roslyn stays out of `Workflow.Modules` per the D14 precedent), and `Workflow.Modules.Database.Linq` (retrofitted onto the shared core — **its 53 tests must stay green unchanged**). Tests stay Docker-free throughout — transforms are pure in-memory~ 🌸

### Confirmed Design Decisions ✅

| # | Decision |
|---|----------|
| **D1 Expressions via `IExpressionEvaluator`, not System.Linq.Dynamic.Core** | The legacy checklist said "Install System.Linq.Dynamic.Core" — superseded. Per-item predicates/projections (`where`, `select`, computed properties, custom validation rules) evaluate through the existing 2.2.5 evaluator seam (`Workflow.Core/Abstractions/IExpressionEvaluator.cs`): Jint (JS syntax, default) or DynamicExpresso (C# syntax, keyed `"csharp"` via `IExpressionEvaluatorFactory`). Already sandboxed (no CLR reach-through), timeout/cancellation-aware, pooled, and DI-wired — Dynamic LINQ would add a new dep *and* a new injection surface for zero capability gain. |
| **D2 Typed C# script is the power surface (2.6.b), mirroring 2.4-D12** | Complex multi-step transforms shouldn't be forced through stringly-typed expression chains. `builtin.transform.script` gives the same authoring experience as `builtin.database.linq`: typed C# body, compile-time diagnostics, publish-time Roslyn compile, ALC execution. Docs present expressions as the default path and script as the power tool. |
| **D3 Shared scripting core extracted to `Workflow.Scripting.Roslyn`** | 2.4.b's `ForbiddenSyntaxWalker`, `ReferenceWhitelist`, `CompiledAssemblyKey`, `CompiledAssemblyCache`, `HmacLinqAssemblySigner`(→`HmacScriptAssemblySigner`), `ILinqHmacKeyProvider`(→`IScriptHmacKeyProvider`), and `CollectibleScriptRunner` are domain-agnostic — nothing in them is database-specific. They move to a new `Workflow.Scripting.Roslyn` project; `Workflow.Modules.Database.Linq` references it and re-exports/aliases as needed. **2.4.b's 53 tests must pass unchanged** (they may re-target moved namespaces only). Linq-specific pieces (linq2db codegen, `TableTypeResolver`, previewer) stay put. |
| **D4 Script module quarantined in `Workflow.Modules.Transform.Script`** | Same rule as 2.4-D14 and 2.5-D4: Roslyn (~30MB) must not enter `Workflow.Modules`. New project + opt-in `AddTransformScriptModules()` wired by the host; a quarantine test asserts `Workflow.Modules` never references Roslyn or the scripting core. |
| **D5 Config via Properties, results as Outputs; `data` arrives via port or property (port wins)** | Same correction as 2.4.a.1/2.5-D3. All transform modules take their working set on a `data`/`source` **input port** (typical: upstream node output) with a property fallback for literals. |
| **D6 One uniform data shape: CLR dict/list/scalar** | Transforms operate on `IReadOnlyDictionary<string, object?>` / `IReadOnlyList<object?>` / scalars — exactly what 2.5's `JsonValueConverter` and the database modules' `rows` already emit. `TransformDataNormalizer` (2.6.a.0) coerces incoming values (incl. `JsonNode`/`JsonElement` stragglers) into this shape once, at module entry. `JsonValueConverter` + `XmlDictionaryConverter` are **promoted from `Builtin/File/Internal` to a shared `Workflow.Modules/Internal/`** namespace (2.5 modules retarget the using — no behaviour change). |
| **D7 Expression item-context convention** | Per-item expressions see `item` (current element), `index` (int), plus workflow `Variables` — flattened into the evaluator's variable map. Aggregate/group expressions additionally see `group` (key) and `items`. Documented once in 2.6.a.0 and shared by map/query/aggregate/validate. |
| **D8 Outputs always materialised** | Same as 2.4-D8/2.5-D8: modules return fully materialised lists/dicts — never lazy enumerables. The script runner reuses the 2.4.b materialisation guard (`ScriptResultMaterializer`): an `IQueryable`/lazy return from user code → failure diagnostic. |
| **D9 Validation is declarative rules, not FluentValidation** | The legacy checklist said "Install FluentValidation" — superseded. FluentValidation is *code-first* (rules authored in C# classes), which can't be driven from a workflow-definition JSON property. `builtin.transform.validate` ships a declarative rule set (required/type/length/range/regex/email/url/enum/nested/array) + a `custom` rule kind that evaluates an `IExpressionEvaluator` expression. Full JSON-Schema support via `JsonSchema.Net` (json-everything, MIT — same family as our `JsonPath.Net`) is **pending Q3**. |
| **D10 `jsonquery`/`xmlquery` land here (from 2.5-D9)** | `builtin.transform.jsonquery` wraps JSONPath (extending 2.3.5's `JsonPathExtractor`; `JsonPath.Net` already pinned). `builtin.transform.xmlquery` wraps `XPathSelectElements` over the D6 dict shape (via `XmlDictionaryConverter` round-trip) — XXE-safe settings identical to 2.5's `XmlReadModule`. |
| **D11 String ops as one operation-style module** | `builtin.transform.string` with `operation` string key (case/trim/substring/replace/split/join/pad/truncate/format/regex/base64/url/html/hash/guid) — same operation-property pattern as `builtin.cloud.s3`. MD5 ships but is documented **non-cryptographic (legacy interop only)**; SHA256/SHA512 are the recommended hashes. |
| **D12 Script inputs reuse the `LinqInputs` accessor-struct codegen** | 2.4.b.1's `RestrictedTypeMapper` + `LinqInputsCodeGenerator` (§8.6 scalar allowlist, `object?` fallback with warning) generalise as-is — the script's `inputs` map binds identically. The script body signature is `Task<object?> ExecuteAsync(ScriptRows rows, ScriptInputs inputs, CancellationToken ct)` where `ScriptRows` wraps the normalised D6 data. |
| **D13 Script cache namespace `compiled-modules/transform/`** | Same `IBlobStore` cache as 2.4.b.2 (shared `CompiledAssemblyCache` post-extraction), keyed `compiled-modules/transform/{definitionId}/{nodeId}/{SHA256(code+schemaVersion+inputsFingerprint)}.dll` — no table fingerprint (no DB), inputs-shape fingerprint instead. |
| **D14 Trusted-author gate + whitelists carry over (2.4-D17)** | Script compile/save gated to trusted authors at the API layer; same `ForbiddenSyntaxWalker` blocklist + usings allowlist (minus `LinqToDB` — transform scripts get `System`/`System.Linq`/`System.Collections.Generic`/`System.Text`/`System.Text.RegularExpressions`/`System.Threading`/`System.Threading.Tasks`). No I/O, no network, no reflection — transforms are pure functions. |

### TO RESOLVE 🤔

> Raised during this task breakdown — each carries a V1 recommendation so 2.6.a.0 can start immediately~ ✨

- [ ] **Q1 Is 2.6.b (typed script) MVP or post-MVP?** The expression family covers the legacy checklist fully; the script family is the power surface. Per the 2.4 typed-first precedent (D12 there) **V1 recommendation: MVP, Week 18** — the extraction (2.6.b.0) also pays down tech debt in `Workflow.Modules.Database.Linq` that 2.7+ would otherwise inherit. Fallback if Week 18 is squeezed: ship 2.6.b.0 (extraction only, regression-tested) and defer 2.6.b.1–2 to a P-slice.
  - A1: Is MVP 
- [ ] **Q2 Default expression language:** Jint/JS (`item.price > 10 && item.name.startsWith("A")`) or DynamicExpresso/C# (`item.price > 10`)? Jint is the 2.2.5 default and handles untyped dict access more gracefully; C# feels closer to 2.6.b. **V1 recommendation: JS default, `"csharp"` opt-in per module via a `language` property** — exactly the `IExpressionEvaluatorFactory` keyed pattern that already exists.
  - A2: JS and lets leverage what we did in 2.4 to support C# as an opt-in language.
- [ ] **Q3 JSON Schema validation:** add `JsonSchema.Net` so `builtin.transform.validate` accepts a standard JSON Schema alongside the declarative rule set? **V1 recommendation: yes, as a `schemaFormat: "jsonSchema"` mode** — it's MIT, ~200KB, same maintainer as JsonPath.Net, and "validate against a real JSON Schema" is table stakes for ETL. If cut, tracked as 2.6.a.P2.
  - A3: Okay 
- [ ] **Q4 `builtin.transform.query` shape:** one module with `where`/`select`/`orderBy`/`skip`/`take` properties applied as a fixed pipeline, or a list-of-operations DSL (`operations: [{op: "where", …}]`)? **V1 recommendation: fixed-pipeline properties** — covers 95% of uses, zero DSL invention (matches 2.4-D11's "no DSL" spirit); chains of query nodes compose the rest. Multi-stage single-node pipelines → 2.6.b script.
  - A4: Okay
- [ ] **Q5 Join support in the query module:** the legacy checklist lists "Join operations". Joining two upstream collections needs a second input port + key expressions — real complexity. **V1 recommendation: defer joins to 2.6.b script (trivial in LINQ) + track a dedicated `builtin.transform.join` as 2.6.a.P1** — don't bloat the query module's V1.
  - A5: I think we need joins for MVP.
- [ ] **Q6 Scripting-core project name/home:** `Workflow.Scripting.Roslyn` as proposed (new top-level project), or fold into `Workflow.Modules.Database.Linq` with the Transform script referencing *it*? **V1 recommendation: new `Workflow.Scripting.Roslyn` project** — a transform module referencing a *database* assembly (dragging linq2db along) is exactly the coupling D14-style quarantines exist to prevent.
  - A6: Okay 
- [ ] **Q7 Script preview:** 2.4.b.4's previewer is SQLite-specific (seeds tables). Transform preview is simpler — run the compiled script against caller-supplied sample rows with a timeout. **V1 recommendation: ship `ITransformScriptPreviewer` in 2.6.b.2** (compile-inclusive, like 2.4.b.4's Consideration 5) — no sandbox DB needed, pure in-memory.
  - A7: Okay 
- [ ] **Q8 Regex safety:** `builtin.transform.string` regex ops + validate's `pattern` rule take user-supplied patterns — ReDoS risk. **V1 recommendation: `RegexOptions.NonBacktracking` where the pattern allows it, else mandatory `Regex` match timeout (1s default, configurable)** — test-locked with a catastrophic-backtracking pattern.
  - A8: Okay  

---

## Pre-Existing Work (from earlier phases) ✅

| Component | File | Status |
|-----------|------|--------|
| `IWorkflowModule` contract + `ModuleResult.Ok/Fail` | `Workflow.Modules/Abstractions/IWorkflowModule.cs` | ✅ Reused as-is |
| `IExpressionEvaluator` + `IExpressionEvaluatorFactory` (2.2.5) | `Workflow.Core/Abstractions/IExpressionEvaluator.cs` + `Workflow.Engine/Services/{Jint,DynamicExpresso}ExpressionEvaluator.cs` + `KeyedExpressionEvaluatorFactory.cs` | ✅ **The** expression seam (D1) — resolved from `ctx.Services`, host-registered |
| `JsonPathExtractor` (JsonPath.Net, 2.3.5) | `Workflow.Modules/Builtin/Http/Internal/JsonPathExtractor.cs` | ✅ Basis for `builtin.transform.jsonquery` (D10) |
| `JsonValueConverter` + `XmlDictionaryConverter` (2.5.a.2) | `Workflow.Modules/Builtin/File/Internal/` | ✅ Promoted to `Workflow.Modules/Internal/` (D6) |
| 2.4.b Roslyn pipeline: `ForbiddenSyntaxWalker`, `ReferenceWhitelist`, `CompiledAssemblyKey`, `CompiledAssemblyCache`, HMAC signer + key provider, `CollectibleScriptRunner`, `LinqResultMaterializer` | `Workflow.Modules.Database.Linq/{Compilation,Execution}/` | ✅ Extraction source for `Workflow.Scripting.Roslyn` (D3) — 53 tests guard the refactor |
| `RestrictedTypeMapper` + `LinqInputsCodeGenerator` (§8.6) | `Workflow.Modules.Database.Linq/Compilation/` | ✅ Generalised for `ScriptInputs` codegen (D12) |
| `IBlobStore` (2.1.4) + `compiled-modules/` namespace | `Workflow.Persistence/Abstractions/IBlobStore.cs` | ✅ Script cache under `compiled-modules/transform/` (D13) |
| `Microsoft.CodeAnalysis.CSharp` + `Basic.Reference.Assemblies` pins | `Directory.Packages.props` | ✅ Already pinned (2.4.b.0) — no new Roslyn pins |
| `JsonPath.Net` pin | `Directory.Packages.props` | ✅ Already pinned (2.3.5) |
| Operation-style module pattern | `Workflow.Modules.Cloud/Builtin/S3Module.cs` | ✅ Pattern reference for `builtin.transform.string` (D11) |
| `BuiltinModuleRegistration` + `AddWorkflowModules()` | `Workflow.Modules/…` | ✅ Expression family appends/wires here; script family host-wired (D4) |
| XXE-safe XML settings (2.5.a.2) | `Workflow.Modules/Builtin/File/XmlReadModule.cs` | ✅ Same settings in `xmlquery` (D10) |

> **CopilotNote:** The 2.6.b.0 extraction is the only genuinely risky work in this phase. Do it **first in the 2.6.b track, as a pure mechanical move** — new project, move files, fix namespaces, alias if needed, run the full 2.4.b suite, and only then start generalising names (`Linq*` → `Script*`) in a second commit. Never mix "move" and "rename+behaviour" in one step~ 💖

---

## 2.6.a.0 Shared Transform Infrastructure 🛠️ (foundation)

> **Purpose:** Land the folder, the data normaliser, and the expression bridge every 2.6.a module consumes. No modules yet~ ✨

**Complexity:** 🟡 Low-Medium *(small surface; the D6/D7 conventions get baked in here)*

### Tasks

- [ ] **`Builtin/Transform/` layout in `Workflow.Modules`** 🌷
  - [ ] `Builtin/Transform/` — the modules (2.6.a.1–5)
  - [ ] `Builtin/Transform/Internal/` — normaliser, expression bridge, shared helpers
  - [ ] No new DI services needed for the family itself (evaluators are host-registered) — `AddTransformModules()` ships anyway as the family's future seam (`TryAdd` semantics; called from `AddWorkflowModules()`)

- [ ] **Promote shared converters (D6)** 📦
  - [ ] Move `JsonValueConverter` + `XmlDictionaryConverter` from `Builtin/File/Internal/` → `Workflow.Modules/Internal/` (namespace `Workflow.Modules.Internal`)
  - [ ] Retarget 2.5 file-module usings; **all 58 file-family tests stay green unchanged**

- [ ] **`TransformDataNormalizer`** 🧹 (D6)
  - [ ] `Builtin/Transform/Internal/TransformDataNormalizer.cs`
  - [ ] Coerces port/property values into dict/list/scalar: passes through CLR shapes, converts `JsonNode`/`JsonElement` (via `JsonValueConverter`), rejects unsupported types with a friendly message
  - [ ] `AsRows(object?)` → `IReadOnlyList<IReadOnlyDictionary<string, object?>>` helper (the common "array of records" case) with a structured error when items aren't records

- [ ] **`ItemExpressionEvaluator`** 🧮 (D7 — the expression bridge)
  - [ ] `Builtin/Transform/Internal/ItemExpressionEvaluator.cs`
  - [ ] Resolves `IExpressionEvaluator` from `ctx.Services` (default) or `IExpressionEvaluatorFactory` when `language` property set (Q2); Fail-fast friendly message when neither registered
  - [ ] Builds the per-item variable map: `item`, `index`, workflow `Variables` (+ `group`/`items` for aggregate contexts)
  - [ ] `EvalPredicateAsync` (bool coercion rules documented: JS-truthiness for Jint, strict bool for csharp) + `EvalValueAsync`
  - [ ] Honors `CancellationToken`; per-expression evaluation errors carry the item index in the failure message

- [ ] **Common exception type** 🚨 — `TransformModuleException` (with `ItemIndex?` context)

### Tests (target ~10): → `Workflow.Tests/Modules/Transform/TransformInfrastructureTests.cs`

- [ ] `Normalizer_ClrDictAndList_PassThrough`
- [ ] `Normalizer_JsonNode_ConvertsToClrShape`
- [ ] `Normalizer_AsRows_NonRecordItem_FriendlyError`
- [ ] `Normalizer_Scalar_PassesThrough`
- [ ] `ItemEvaluator_JsPredicate_SeesItemAndIndex`
- [ ] `ItemEvaluator_CsharpLanguageKey_UsesKeyedEvaluator`
- [ ] `ItemEvaluator_WorkflowVariables_Visible`
- [ ] `ItemEvaluator_MissingEvaluatorRegistration_FriendlyFail`
- [ ] `ItemEvaluator_ExpressionError_CarriesItemIndex`
- [ ] `ItemEvaluator_Cancellation_Propagates`
- [ ] *(regression)* full 2.5 file-family suite green after the converter promotion

---

## 2.6.a.1 Data Map Module 🔄 (`builtin.transform.map`)

> **Purpose:** Declarative per-record reshaping — rename, nested access, defaults, type conversion, computed expressions~ ✨

**Complexity:** 🟠 Medium

### Tasks

- [ ] **`DataMapModule`** 🔄
  - [ ] New: `Workflow.Modules/Builtin/Transform/DataMapModule.cs` — `ModuleId: "builtin.transform.map"`, `DisplayName: "Map Data"`, `Category: "Transformation"`, `Icon: "🔄"`, `Version: 1.0.0`
  - [ ] Schema (D5 — `source` port or property, port wins):
    - [ ] Input port: `source` (object or array)
    - [ ] Property: `mapping` (map, required) — `targetField` → *spec*, where spec is either a **source path string** (`"user.address.city"`, dot-notation nested access) or a **spec object** `{ path?, expression?, default?, convert? }`
    - [ ] Property: `language` (string, optional — Q2) · `flatten` (bool, default `false`) · `ignoreNulls` (bool, default `false`)
    - [ ] Output: `result` (object or array — mirrors input arity) · `count` (int) · `success` (bool)
  - [ ] Mapping engine:
    - [ ] Dot-path resolution over the D6 dict shape (missing segment → `default` if given, else `null`; `ignoreNulls` drops the key)
    - [ ] `expression` spec → `ItemExpressionEvaluator.EvalValueAsync` (sees `item`/`index`/Variables — covers "conditional mapping" + "computed properties")
    - [ ] `convert`: `"string" | "int" | "long" | "double" | "decimal" | "bool" | "dateTime" | "guid"` — invariant-culture, friendly per-item failure
    - [ ] `flatten: true` → nested dicts flattened to `parent.child` keys on the **output**
    - [ ] Array input → map each record; object input → map once
  - [ ] `ValidateConfiguration`: `mapping` non-empty; spec objects must set exactly one of `path`/`expression`

### Tests (target ~11): → `Workflow.Tests/Modules/Transform/DataMapModuleTests.cs`

- [ ] `MapModule_Metadata_IsCorrect`
- [ ] `Map_SimpleRename_Works`
- [ ] `Map_NestedPath_Resolves`
- [ ] `Map_MissingPath_UsesDefault` / `Map_MissingPath_NoDefault_Null`
- [ ] `Map_Expression_ComputesValue` *(e.g. `item.first + " " + item.last`)*
- [ ] `Map_ConditionalExpression_Works` *(ternary in expression)*
- [ ] `Map_TypeConversion_IntFromString` / `Map_TypeConversion_Invalid_FailsWithItemIndex`
- [ ] `Map_ArrayInput_MapsEachRecord`
- [ ] `Map_Flatten_ProducesDottedKeys`
- [ ] `Map_IgnoreNulls_DropsKeys`

---

## 2.6.a.2 Query + Aggregate Modules 🔍📊 (`builtin.transform.query` / `builtin.transform.aggregate`)

> **Purpose:** Filter/project/sort/paginate collections, and compute aggregates with optional grouping — all expression-driven (D1, Q4)~ ✨

**Complexity:** 🟠 Medium

### Tasks

- [ ] **`DataQueryModule`** 🔍 (`builtin.transform.query`)
  - [ ] Fixed pipeline (Q4), applied in order: `where` → `select` → `orderBy`/`descending` → `skip`/`take`
  - [ ] Input port: `data` (array); Properties: `where` (expression, optional) · `select` (expression **or** mapping-style map, optional) · `orderBy` (expression or dot-path, optional) · `descending` (bool) · `skip`/`take` (int, optional) · `language` (Q2)
  - [ ] Outputs: `result` (array) · `count` (int) · `totalCount` (int — pre-skip/take) · `success`
  - [ ] `orderBy` comparison: numeric when both comparands are numeric, ordinal-string otherwise; `null` sorts first (documented)
  - [ ] Joins **not** in V1 (Q5) — module description points at `builtin.transform.script` + 2.6.a.P1
- [ ] **`AggregateModule`** 📊 (`builtin.transform.aggregate`)
  - [ ] Input port: `data` (array); Properties: `operation` (string key: `sum`/`count`/`avg`/`min`/`max`/`first`/`last`/`distinct`/`median`/`mode`, required) · `property` (dot-path, optional — required for numeric ops on record arrays) · `groupBy` (dot-path or expression, optional) · `language`
  - [ ] Outputs: `result` (scalar, or array for `distinct`) · `groups` (array of `{ key, result, count }`, when `groupBy`) · `success`
  - [ ] Numeric coercion via invariant culture; nulls skipped (documented); empty collection → `count: 0`, `sum: 0`, others `null` (no throw)
  - [ ] Grouped mode: group → aggregate within each group → `groups` output

### Tests (target ~18): → `Workflow.Tests/Modules/Transform/DataQueryModuleTests.cs` + `AggregateModuleTests.cs`

**Query (~9):** metadata · `Where_FiltersRows` · `Select_ProjectsShape` *(expression + map forms)* · `OrderBy_NumericAndString` · `OrderByDescending_Works` · `SkipTake_Paginates_TotalCountPreSlice` · `CombinedPipeline_AllStages` · `EmptyData_ReturnsEmptyNotError` · `WhereExpressionError_CarriesItemIndex`

**Aggregate (~9):** metadata · `Sum_Avg_OnNumericProperty` · `MinMax_Works` · `Count_NoPropertyNeeded` · `FirstLast_Work` · `Distinct_ReturnsUniques` · `Median_Mode_Custom` · `GroupBy_AggregatesPerGroup` · `EmptyCollection_NullNotThrow` · `NullValues_Skipped`

---

## 2.6.a.3 JSON + XML Query/Transform 📝 (`builtin.transform.{jsonquery,xmlquery,json}`)

> **Purpose:** The 2.5-deferred query modules (D10) plus structural JSON operations (merge/flatten/diff)~ ✨

**Complexity:** 🟡 Medium

### Tasks

- [ ] **`JsonQueryModule`** 🎯 (`builtin.transform.jsonquery`)
  - [ ] Input port: `data` (object/array — D6 shape or JSON string); Properties: `path` (JSONPath, required) · `required` (bool, default `false`)
  - [ ] Outputs: `result` (unwrap single match to scalar/object, multi → list — same convention as 2.3.5's extractor) · `matchCount` (int) · `success`
  - [ ] Implementation: serialise D6 shape → `JsonNode` → `JsonPath.Net` (reuse/extend `JsonPathExtractor` internals; consider promoting the shared bits to `Workflow.Modules/Internal/`)
  - [ ] No match: `null` + `matchCount: 0` (or Fail when `required`)
- [ ] **`XmlQueryModule`** 🏷️ (`builtin.transform.xmlquery`)
  - [ ] Input port: `data` (D6 dict from `xml.read`, or raw XML string); Properties: `xpath` (required) · `required`
  - [ ] Outputs: `result` (single → dict/scalar, multi → list, via `XmlDictionaryConverter`) · `matchCount` · `success`
  - [ ] Raw-string parsing uses the **same XXE-safe settings** as 2.5's `XmlReadModule` (DTD prohibited, resolver null) — test-locked
- [ ] **`JsonTransformModule`** 📝 (`builtin.transform.json`)
  - [ ] Input port: `data`; Properties: `operation` (string key: `merge`/`patch`/`diff`/`flatten`/`unflatten`, required) · `other` (object — second operand for merge/patch/diff, port or property) · `separator` (string, default `"."`, flatten/unflatten)
  - [ ] `merge`: RFC 7396 merge-patch semantics via `JsonNode` (null removes key — documented); `patch`/`diff`: structural add/remove/replace list (hand-rolled over `JsonNode` — no new package); `flatten`/`unflatten`: dotted-key round-trip (array indices as `items.0`)
  - [ ] Outputs: `result` · `success`
  - [ ] *(JSON-schema validation intentionally NOT here — it's `builtin.transform.validate`'s job, Q3)*

### Tests (target ~14): → `Workflow.Tests/Modules/Transform/JsonXmlQueryModuleTests.cs` + `JsonTransformModuleTests.cs`

- [ ] metadata ×3
- [ ] `JsonQuery_SingleMatch_Unwrapped` · `JsonQuery_FilterExpression_MultiMatch_List` *(`$.items[?(@.price > 10)]`)* · `JsonQuery_NoMatch_NullOrRequiredFail` · `JsonQuery_AcceptsDictShapeAndJsonString`
- [ ] `XmlQuery_XPath_SelectsElements` · `XmlQuery_RawString_XxeRefused` *(security lock)*
- [ ] `JsonTransform_Merge_Rfc7396_NullRemoves` · `JsonTransform_Diff_ReportsAddRemoveReplace` · `JsonTransform_FlattenUnflatten_RoundTrips` · `JsonTransform_Flatten_ArrayIndices` · `JsonTransform_UnknownOperation_Fails`

---

## 2.6.a.4 Validate Data Module ✅ (`builtin.transform.validate`)

> **Purpose:** Declarative record validation with per-field rules + expression escape hatch (D9); optional JSON Schema mode (Q3)~ ✨

**Complexity:** 🟠 Medium

### Tasks

- [ ] **`ValidateDataModule`** ✅
  - [ ] New: `Workflow.Modules/Builtin/Transform/ValidateDataModule.cs` — `ModuleId: "builtin.transform.validate"`, `Category: "Transformation"`, `Icon: "✅"`
  - [ ] Input port: `data` (object or array); Properties: `rules` (array of rule specs, required unless `schema` set) · `schema` (JSON Schema object — Q3 mode) · `failOnInvalid` (bool, default `false` — renamed from the checklist's `throwOnError`; modules Fail, they don't throw) · `language`
  - [ ] Rule spec: `{ field (dot-path), rule, value?, message? }` with `rule` ∈ `required` · `type` (`string`/`number`/`bool`/`date`/`array`/`object`) · `minLength`/`maxLength` · `min`/`max` · `pattern` (regex — Q8 timeout) · `email` · `url` · `enum` · `minItems`/`maxItems` · `custom` (expression seeing `value`/`item`/`index`)
  - [ ] Nested validation via dot-paths into sub-objects; array-of-records input validates each item
  - [ ] Outputs: `isValid` (bool) · `errors` (array of `{ index?, field, rule, message }`) · `validItems` / `invalidItems` (arrays, when input is an array) · `success`
  - [ ] `failOnInvalid: true` → `ModuleResult.Fail` with the error list in the message (composes with `builtin.trycatch`)
  - [ ] Q3 (if confirmed): `schema` property validates via `JsonSchema.Net`; `rules` and `schema` are mutually exclusive (validation error otherwise)

### Tests (target ~14): → `Workflow.Tests/Modules/Transform/ValidateDataModuleTests.cs`

- [ ] metadata · `Required_MissingField_Invalid` · `Type_Mismatch_Invalid` · `MinMaxLength_Enforced` · `NumericRange_Enforced` · `Pattern_Regex_Works` · `Pattern_CatastrophicBacktracking_TimesOutSafely` *(Q8 lock)* · `Email_Url_Formats` · `Enum_Membership` · `NestedField_DotPath_Validated` · `ArrayInput_SplitsValidAndInvalid` · `CustomExpressionRule_Works` · `FailOnInvalid_ReturnsModuleFail` · `JsonSchemaMode_Validates` *(Q3-gated)*

---

## 2.6.a.5 String Transform Module 📝 (`builtin.transform.string`)

> **Purpose:** The utility belt — case/trim/substring/replace/split/join/pad/truncate/format/regex/encodings/hash/guid (D11)~ ✨

**Complexity:** 🟡 Low-Medium *(volume, not difficulty)*

### Tasks

- [ ] **`StringTransformModule`** 📝
  - [ ] New: `Workflow.Modules/Builtin/Transform/StringTransformModule.cs` — `ModuleId: "builtin.transform.string"`, `Category: "Transformation"`, `Icon: "📝"`
  - [ ] Input port: `input` (string or array of strings); Properties: `operation` (string key, required) · `parameters` (map, optional — per-op args)
  - [ ] Operations (string keys): `upper` · `lower` · `trim` · `trimStart` · `trimEnd` · `substring` (`start`,`length?`) · `replace` (`find`,`with`) · `split` (`separator`) → array · `join` (`separator`) ← array · `padLeft`/`padRight` (`width`,`char?`) · `truncate` (`length`,`ellipsis?`) · `format` (`template` with `{0}`/named `{item}` slots) · `regexMatch`/`regexReplace`/`regexExtract` (`pattern`,`with?`,`group?` — Q8 timeout) · `base64Encode`/`base64Decode` · `urlEncode`/`urlDecode` · `htmlEncode`/`htmlDecode` · `hash` (`algorithm`: `sha256` default /`sha512`/`md5`-legacy → lowercase hex) · `newGuid`
  - [ ] Array input → operation applied per element (except `join`); `split` on array input → array of arrays
  - [ ] Outputs: `result` (string or array) · `success`
  - [ ] Null/empty input: pass-through for case/trim ops, friendly Fail for ops needing content (documented per op)
  - [ ] `ValidateConfiguration`: known operation; required parameters present for the chosen op

### Tests (target ~14): → `Workflow.Tests/Modules/Transform/StringTransformModuleTests.cs`

- [ ] metadata · `CaseAndTrim_Ops` *(theory)* · `Substring_Replace_Work` · `Split_ReturnsArray` / `Join_FromArray` · `Pad_Truncate_Format` · `Regex_MatchReplaceExtract` · `Regex_Timeout_SafeFail` *(Q8)* · `Base64_RoundTrips` · `UrlHtml_EncodeDecode_RoundTrip` · `Hash_Sha256_KnownVector` · `Hash_Md5_MarkedLegacyStillWorks` · `NewGuid_ValidFormat` · `ArrayInput_PerElement` · `UnknownOperation_FailsValidation`

---

## 2.6.a.6 E2E + Documentation 📖 (expression family wrap-up)

> **Purpose:** Prove the family composes and document it~ ✨

**Complexity:** 🟢 Low

### Tasks

- [ ] **E2E pipeline test** (Docker-free, `Workflow.Tests/Modules/Transform/TransformE2ETests.cs`): csv.read (2.5) → `validate` (split invalid) → `map` (reshape+convert) → `query` (filter+sort) → `aggregate` (groupBy sum) → `json.write` (2.5) — asserts end shape + invalid-item routing
- [ ] **`docs/transform-modules.md`** — module reference (7 + script pointer), expression-context guide (D7, `item`/`index`/Variables, JS vs csharp), mapping-spec reference, validation-rule reference, "expressions vs typed script" guidance (mirrors the typed-first framing of `docs/database-modules.md`)
- [ ] **Registration housekeeping** — all 7 modules in `BuiltinModuleRegistration.GetAll()` (module-count tests updated); `DOCUMENTATION_INDEX.md` + `phases/README.md` + `Phase2-CoreFeatures.md` §2.6 updated

### Tests (target ~2): the E2E above + `BuiltinModules_CountAndIds_IncludeTransformFamily`

---

## 2.6.b.0 Scripting Core Extraction 🏗️ (`Workflow.Scripting.Roslyn`)

> **Purpose:** Mechanically extract 2.4.b's domain-agnostic compile-cache-execute machinery into a shared project (D3/Q6) with **zero behaviour change** — the 53 DatabaseLinq tests are the regression harness~ ✨

**Complexity:** 🔴 High *(pure refactor risk on the repo's most security-sensitive code)*

### Tasks

- [ ] **New project `Workflow.Scripting.Roslyn`** (net8.0; refs: `Workflow.Core`, `Workflow.Persistence` (IBlobStore), `Microsoft.CodeAnalysis.CSharp`, `Basic.Reference.Assemblies`; NoWarn SA0002 like the Linq project); add to `Workflow.sln`
- [ ] **Step 1 — mechanical move (no renames):** `ForbiddenSyntaxWalker` · `ReferenceWhitelist` · `CodeIdentifiers` · `RestrictedTypeMapper` · `CompiledAssemblyKey` · `CompiledAssemblyCache` (+ options) · `HmacLinqAssemblySigner` + `ILinqAssemblySigner` + `ILinqHmacKeyProvider` + ephemeral provider · `CollectibleScriptRunner` · `LinqResultMaterializer` → namespaces `Workflow.Scripting.Roslyn.{Compilation,Execution}`; `Workflow.Modules.Database.Linq` references the new project; usings retargeted
  - [ ] **Full 2.4.b suite green (53 tests) + Roslyn-quarantine test still green** before anything else
- [ ] **Step 2 — generalising renames (aliased for compat):** `ILinqAssemblySigner`→`IScriptAssemblySigner`, `ILinqHmacKeyProvider`→`IScriptHmacKeyProvider`, `LinqResultMaterializer`→`ScriptResultMaterializer`, cache options gain a configurable blob-namespace prefix (linq keeps `compiled-modules/`, transform uses `compiled-modules/transform/` — D13); linq project keeps thin type-forwarders/aliases so its public DI surface is unchanged
  - [ ] `ReferenceWhitelist` + usings allowlist parameterised (linq passes `+LinqToDB`; transform passes the D14 set) — `ForbiddenSyntaxWalker` blocklist stays **shared and identical**
- [ ] **What does NOT move:** `WorkflowLinqCompiler` orchestration, `DynamicContextCodeGenerator`, `TableTypeResolver`, `SqlTypeMapper`, previewer, `LinqQueryModule` — all linq2db-specific
- [ ] **Quarantine tests:** `WorkflowModules_DoesNotReferenceScriptingCore` (new) + existing Roslyn quarantine

### Tests (target ~6 new + 53 regression): → `Workflow.Tests/Scripting/ScriptingCoreTests.cs`

- [ ] `ForbiddenSyntaxWalker_SharedBlocklist_UnchangedBehaviour` *(golden cases: Process/File/HttpClient/DllImport/unsafe)*
- [ ] `ReferenceWhitelist_Parameterised_LinqAndTransformProfiles`
- [ ] `CompiledAssemblyCache_NamespacePrefix_Configurable`
- [ ] `Hmac_SignVerify_RoundTripsPostMove`
- [ ] `CollectibleRunner_LoadRunUnload_PostMove`
- [ ] `WorkflowModules_DoesNotReferenceScriptingCore_QuarantineHolds`
- [ ] *(regression)* all 53 DatabaseLinq tests green, unchanged

---

## 2.6.b.1 Transform Script Module 🌟 (`builtin.transform.script`)

> **Purpose:** The typed power surface — a C# body over normalised rows, compiled/cached/ALC-executed exactly like `builtin.database.linq` minus the database (D2/D12/D13/D14)~ ✨

**Complexity:** 🟠 Medium-High *(mostly assembly of extracted parts)*

### Tasks

- [ ] **New project `Workflow.Modules.Transform.Script`** (refs: `Workflow.Modules`, `Workflow.Scripting.Roslyn`; quarantined per D4); `AddTransformScriptModules()` opt-in DI entry, wired in `Workflow.Api/Program.cs`
- [ ] **`IWorkflowTransformScriptCompiler`** 🧬
  - [ ] Codegen wrapper: `public static class WorkflowScript { public static async Task<object?> ExecuteAsync(ScriptRows rows, ScriptInputs inputs, CancellationToken ct) { …user body… } }`
  - [ ] `ScriptRows` — typed façade over `IReadOnlyList<IReadOnlyDictionary<string, object?>>` (`rows.All`, `rows.Count`, indexer; LINQ-friendly) — emitted into the compilation like `DynamicWorkflowContext` was
  - [ ] `ScriptInputs` — reuse `RestrictedTypeMapper` + inputs codegen (D12); same `WFLINQ004`-style warnings (new `WFSCRIPT0xx` ids)
  - [ ] Usings allowlist per D14 (no LinqToDB); `ForbiddenSyntaxWalker` unchanged; diagnostics surface with line/col
- [ ] **`TransformScriptModule`** 🌟
  - [ ] `ModuleId: "builtin.transform.script"`, `DisplayName: "Transform Script (C#)"`, `Category: "Transformation"`, `Icon: "🌟"`
  - [ ] Input port: `data` (array — normalised via `TransformDataNormalizer`); Properties: `compiledAssemblyKey` (string, required — D13 blob key) · `inputs` (map, optional) · `timeoutSeconds` (int, default `30`)
  - [ ] Outputs: `result` (materialised — D8) · `rows`/`rowCount` (when the result is a record list — parity with the linq module) · `success` · `durationMs`
  - [ ] `ExecuteAsync`: cache `TryGetAsync` (HMAC-verified; miss → "not compiled / tampered") → `CollectibleScriptRunner` load → invoke → **materialise** (`ScriptResultMaterializer`; lazy return → Fail) → unload; timeout via linked CTS
- [ ] Registered via `AddTransformScriptModules()` (`TryAddEnumerable`) — **not** `BuiltinModuleRegistration` (D4)

### Tests (target ~11): → `Workflow.Tests/Modules/TransformScript/TransformScriptModuleTests.cs`

- [ ] `ScriptModule_Metadata_IsCorrect`
- [ ] `Compile_SimpleProjection_Succeeds` · `Compile_LinqJoinOverTwoRowSets_Succeeds` *(the Q5 join story)*
- [ ] `Compile_ForbiddenApi_FileIo_Rejected` · `Compile_HttpClient_Rejected` *(walker parity on the transform profile)*
- [ ] `Inputs_TypedAccessors_Bind` · `Inputs_NonAllowlistedType_WarnsObjectFallback`
- [ ] `Execute_RoundTrips_RowsInRowsOut` · `Execute_LazyReturn_FailsWithMaterialisationDiagnostic`
- [ ] `Execute_TamperedBlob_RejectedAtLoad` · `Execute_Cancellation_Propagates` · `Execute_Alc_UnloadInvoked_ResultIsAlcFree`

---

## 2.6.b.2 Script API + Preview + Docs 🌐 (wrap-up)

> **Purpose:** validate/preview/compile endpoints (trusted-author gated, D14) + the pure-in-memory previewer (Q7) + typed-first docs~ ✨

**Complexity:** 🟡 Medium

### Tasks

- [ ] **`ITransformScriptPreviewer`** 🔎 (Q7) — compile-inclusive: takes code + sample rows + sample inputs, runs with a short timeout in a collectible ALC, returns result + duration + diagnostics (compile errors → diagnostics, never throws)
- [ ] **API endpoints** (`Workflow.Api/Transform/TransformScriptEndpoints.cs`, minimal API — mirrors `DatabaseLinqEndpoints`): `POST /api/transform/script/{validate,preview,compile}` — compile writes to the D13 cache and returns `compiledAssemblyKey`; same trusted-author header gate as 2.4.b.5 (until real auth)
- [ ] **Docs:** extend `docs/transform-modules.md` with the script authoring guide (author → validate → preview → compile → reference key), security model (walker/allowlist/HMAC/ALC — link to database-modules security section), and the "expressions first, script for power" framing
- [ ] **Housekeeping:** `DOCUMENTATION_INDEX.md`, `phases/README.md`, §2.6 completion summary in `Phase2-CoreFeatures.md`

### Tests (target ~7): → `Workflow.Tests/Api/TransformScriptApiTests.cs` + previewer tests

- [ ] `Validate_ReturnsDiagnostics_NoSideEffects` · `Preview_RunsAgainstSampleRows` · `Preview_CompileError_DiagnosticsNotException` · `Compile_TrustedAuthorGate_Enforced` · `Compile_WritesCacheAndReturnsKey` · `Compile_ThenModuleExecute_RoundTrips` · `Previewer_Timeout_SafeFail`

---

## Post-MVP Slices 🚧 *(deferred — not blocking 2.7+)*

### 2.6.a.P1 Join Module 🔗 *(post-MVP — Q5)*
`builtin.transform.join` — second input port, key expressions, inner/left/full modes. ~8 tests.

### 2.6.a.P2 JSON Schema Validation 📐 *(only if Q3 resolves "cut from MVP")*
`JsonSchema.Net` mode on `builtin.transform.validate`. ~5 tests.

### 2.6.a.P3 Lookup-Table Enrichment Module 🗂️ *(post-MVP)*
`builtin.transform.lookup` — enrich rows from a keyed reference collection (the poor man's join for the common case). ~6 tests.

### 2.6.b.P1 Script Snippet Library 📚 *(post-MVP)*
Named, versioned, reusable script snippets referenced by id across workflows (shared cache entries). ~6 tests.

### 2.6.b.P2 Monaco Editor Panel Reuse 🖥️ *(post-MVP)*
Extend the 2.4.b.P4 editor-panel design to transform scripts (same validate-diagnostics contract). Tracked with 2.4.b.P4.

---

## Phase 2.6 Deliverables ✅

When 2.6 ships (Week 18), all of the following must be true:

**2.6.a — Expression family (Week 17 gate):**

- [ ] **Modules (7):** `builtin.transform.{map,query,aggregate,jsonquery,xmlquery,json,validate,string}` — *(8 ids, 7 slices)* — all discoverable, validated, executable; registered in `BuiltinModuleRegistration`
- [ ] **Shared infra:** `TransformDataNormalizer` + `ItemExpressionEvaluator` (D6/D7); converters promoted to `Workflow.Modules/Internal/` with the 2.5 suite green
- [ ] **Security proven:** XXE lock on xmlquery, regex-timeout locks (Q8), expression sandbox unchanged (no new evaluator surface)
- [ ] **~83 unit tests passing** (10 infra + 11 map + 18 query/aggregate + 14 json/xml + 14 validate + 14 string + 2 E2E) — all Docker-free

**2.6.b — Typed script family (Week 18 gate, pending Q1):**

- [ ] **`Workflow.Scripting.Roslyn` extracted** — 2.4.b's 53 tests green **unchanged**; walker/HMAC/ALC behaviour identical; quarantine tests hold (`Workflow.Modules` references neither Roslyn nor the scripting core)
- [ ] **`builtin.transform.script`** discoverable, publish-time-compiled, HMAC-cached under `compiled-modules/transform/`, ALC-executed with forced materialisation
- [ ] **API:** `POST /api/transform/script/{validate,preview,compile}` behind the trusted-author gate
- [ ] **~24 tests** (6 extraction + 11 module + 7 API/preview) + 53 regression

**Cross-cutting:**

- [ ] **docs/transform-modules.md** published + indexed (expressions-first framing, script security model)
- [ ] **0 errors, 0 new warnings** in `dotnet build`; full unit suite green
- [ ] **Q1–Q8 resolved** and recorded in the Resolved Questions Reference
- [ ] **README + phases/README.md + Phase2-CoreFeatures.md §2.6** updated

**New / Modified Files (planned):**
```
Workflow.Modules/
  Internal/
    JsonValueConverter.cs                          ← moved from Builtin/File/Internal (2.6.a.0)
    XmlDictionaryConverter.cs                      ← moved from Builtin/File/Internal (2.6.a.0)
  Builtin/Transform/
    TransformModuleServiceCollectionExtensions.cs  ← new (2.6.a.0)
    DataMapModule.cs                               ← new (2.6.a.1)
    DataQueryModule.cs / AggregateModule.cs        ← new (2.6.a.2)
    JsonQueryModule.cs / XmlQueryModule.cs / JsonTransformModule.cs ← new (2.6.a.3)
    ValidateDataModule.cs                          ← new (2.6.a.4)
    StringTransformModule.cs                       ← new (2.6.a.5)
    Internal/
      TransformDataNormalizer.cs                   ← new (2.6.a.0)
      ItemExpressionEvaluator.cs                   ← new (2.6.a.0)
      TransformModuleException.cs                  ← new (2.6.a.0)
  Builtin/BuiltinModuleRegistration.cs             ← modified — +8 transform ids
  WorkflowModulesServiceCollectionExtensions.cs    ← modified — AddTransformModules()

Workflow.Scripting.Roslyn/                         ← NEW PROJECT (2.6.b.0 — extraction target)
  Compilation/  (walker, whitelist, identifiers, type mapper)
  Execution/    (key, cache, HMAC signer, collectible runner, materializer)

Workflow.Modules.Database.Linq/                    ← modified (2.6.b.0) — references scripting core;
                                                     linq-specific codegen/previewer/module stay

Workflow.Modules.Transform.Script/                 ← NEW PROJECT (2.6.b.1)
  TransformScriptServiceCollectionExtensions.cs
  Abstractions/IWorkflowTransformScriptCompiler.cs · ITransformScriptPreviewer.cs
  Compilation/TransformScriptCompiler.cs · ScriptRowsCodeGenerator.cs
  Preview/TransformScriptPreviewer.cs
  Builtin/TransformScriptModule.cs

Workflow.Api/
  Transform/TransformScriptEndpoints.cs            ← new (2.6.b.2)
  Program.cs                                       ← modified — AddTransformScriptModules() + endpoints

Workflow.Tests/
  Modules/Transform/  (infra, map, query, aggregate, json/xml, validate, string, E2E)
  Modules/TransformScript/TransformScriptModuleTests.cs
  Scripting/ScriptingCoreTests.cs
  Api/TransformScriptApiTests.cs

docs/transform-modules.md                          ← new (2.6.a.6 + 2.6.b.2)
Directory.Packages.props                           ← + JsonSchema.Net (Q3-gated); no other new pins
```

---

## Resolved Questions Reference 📋

| # | Question | Resolution | Tracked in |
|---|----------|------------|------------|
| **Q1** | 2.6.b typed script MVP or post-MVP? | ⏳ pending — V1 rec: MVP Week 18; fallback ships 2.6.b.0 only | TO RESOLVE + 2.6.b |
| **Q2** | Default expression language? | ⏳ pending — V1 rec: JS (Jint) default, `language: "csharp"` opt-in | TO RESOLVE + 2.6.a.0 |
| **Q3** | JSON Schema validation via JsonSchema.Net? | ⏳ pending — V1 rec: yes, `schema` mode on validate | TO RESOLVE + 2.6.a.4 |
| **Q4** | Query module: fixed pipeline vs operations DSL? | ⏳ pending — V1 rec: fixed pipeline, no DSL | TO RESOLVE + 2.6.a.2 |
| **Q5** | Joins in the query module? | ⏳ pending — V1 rec: no — script (2.6.b) + 2.6.a.P1 | TO RESOLVE + 2.6.a.2 |
| **Q6** | Scripting-core home? | ⏳ pending — V1 rec: new `Workflow.Scripting.Roslyn` project | TO RESOLVE + 2.6.b.0 |
| **Q7** | Script preview shape? | ⏳ pending — V1 rec: in-memory compile-inclusive previewer | TO RESOLVE + 2.6.b.2 |
| **Q8** | Regex/ReDoS safety? | ⏳ pending — V1 rec: NonBacktracking or mandatory match timeout, test-locked | TO RESOLVE + 2.6.a.4/2.6.a.5 |

---

> 🌸 *uwu — the transform family is plan-ready, senpai~! 2.6.a rides entirely on seams that already exist (the 2.2.5 evaluator, JsonPath.Net, the 2.5 converters), and 2.6.b turns 2.4.b's hardest-won machinery into a reusable scripting core — move first, rename second, and let those 53 linq tests stand guard. Nod at Q1–Q8 and we'll start slicing~!* 💖
