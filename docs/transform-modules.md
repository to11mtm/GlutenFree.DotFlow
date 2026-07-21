# Data Transformation Modules 🔄

*Phase 2.6 — Made with 💖 by Ami-Chan! UwU ✨*

DotFlow's **transformation family** reshapes, queries, aggregates, joins, and validates in-memory
workflow data. Like the database family (2.4), it's **expressions-first**: declarative,
config-driven modules cover the common cases, and a typed C# **script** module
(`builtin.transform.script`, Phase 2.6.b) is the power tool for anything the declarative surface
can't express~ 🌷

---

## 🧮 Expressions & the item context

Modules that take predicates/projections (`map`, `query`, `aggregate`, `join`, `validate`'s
`custom` rule) evaluate expressions through the shared **`IExpressionEvaluator`** seam (Phase 2.2.5):

- **Default language: JavaScript** (Jint) — `item.price > 10 && item.name.startsWith("A")`
- **Opt-in C#** (DynamicExpresso) — set `language: "csharp"` on the module: `item.price > 10`

Every per-item expression sees:

| Variable | Meaning |
|----------|---------|
| `item` | the current record (object) |
| `index` | the current item's zero-based index |
| *workflow variables* | all `Variables` in scope, by name |
| `group` / `items` | (aggregate contexts) the group key + its members |
| `left` / `right` | (join contexts) the two sides being joined |
| `value` | (validate `custom` rule) the field value under test |

Expressions are sandboxed (no I/O, no reflection), timeout-bounded, and cancellation-aware.

---

## Module reference

| Module ID | Purpose | Key properties |
|-----------|---------|----------------|
| `builtin.transform.map` | Reshape records: rename, nested access, defaults, convert, compute | `mapping`, `flatten`, `ignoreNulls`, `language` |
| `builtin.transform.query` | Filter → project → sort → paginate | `where`, `select`, `orderBy`, `descending`, `skip`, `take` |
| `builtin.transform.aggregate` | Sum/count/avg/min/max/first/last/distinct/median/mode + grouping | `operation`, `property`, `groupBy` |
| `builtin.transform.join` | Hash-join two collections | `leftKey`, `rightKey`, `joinType`, `select` |
| `builtin.transform.jsonquery` | JSONPath query | `path`, `required` |
| `builtin.transform.xmlquery` | XPath query (XXE-safe) | `xpath`, `required` |
| `builtin.transform.json` | Merge / patch / diff / flatten / unflatten | `operation`, `other`, `separator` |
| `builtin.transform.validate` | Declarative rules or JSON Schema | `rules`, `schema`, `failOnInvalid` |
| `builtin.transform.string` | String utility belt | `operation`, `parameters` |
| `builtin.transform.script` | Typed C# transform (Phase 2.6.b) | `compiledAssemblyKey`, `inputs` |

### Mapping spec (`builtin.transform.map`)

`mapping` is `targetField → spec`, where a spec is either a **source path string** or an object:

```jsonc
{
  "name": "firstName",                                   // rename via dot-path
  "city": "user.address.city",                           // nested access
  "country": { "path": "user.country", "default": "?" }, // default when missing
  "age": { "path": "ageStr", "convert": "int" },         // type conversion
  "full": { "expression": "item.first + ' ' + item.last" } // computed
}
```

Convert types: `string`, `int`, `long`, `double`, `decimal`, `bool`, `dateTime`, `guid`.

### Join semantics (`builtin.transform.join`)

Hash-join (`O(n+m)`) two collections on `leftKey`/`rightKey` (dot-path or expression). `joinType`:
`inner` (default), `left` (unmatched left rows emit `right: null`), `full` (also emits unmatched
right rows with `left: null`). Duplicate right keys produce one output row per match pair. The
default output merges left fields and nests the right record under `right`; supply `select` (a
mapping over `left`/`right`) to shape it.

### Validation (`builtin.transform.validate`)

Two mutually-exclusive modes:

**Declarative rules** — `rules` is an array of `{ field, rule, value?, message? }`:

```jsonc
{ "rules": [
  { "field": "email", "rule": "required" },
  { "field": "email", "rule": "email" },
  { "field": "age", "rule": "min", "value": 18 },
  { "field": "code", "rule": "pattern", "value": "^[A-Z]{3}$" },
  { "field": "role", "rule": "enum", "value": ["admin", "user"] },
  { "field": "age", "rule": "custom", "value": "value >= 18 && value < 120" }
] }
```

Rule kinds: `required`, `type`, `minLength`/`maxLength`, `min`/`max`, `pattern`, `email`, `url`,
`enum`, `minItems`/`maxItems`, `custom`.

**JSON Schema** — set `schema` to a standard JSON Schema (validated via `JsonSchema.Net`).

Array inputs are split into `validItems` / `invalidItems`. Set `failOnInvalid: true` to return a
module failure (composes with `builtin.trycatch`).

### String operations (`builtin.transform.string`)

`operation` ∈ `upper` · `lower` · `trim`/`trimStart`/`trimEnd` · `substring` · `replace` · `split` ·
`join` · `padLeft`/`padRight` · `truncate` · `format` · `regexMatch`/`regexReplace`/`regexExtract` ·
`base64Encode`/`base64Decode` · `urlEncode`/`urlDecode` · `htmlEncode`/`htmlDecode` · `hash` · `newGuid`.
Per-op args go in `parameters`. Array inputs apply per element (except `join`).

> **Hashing:** `sha256` (default) and `sha512` are recommended. `md5` is available for **legacy
> interop only — it is not cryptographically secure**.

---

## 🛡️ Security

- **Expressions** run in the sandboxed 2.2.5 evaluator — no file/network/reflection access.
- **Regex** (string ops + validate `pattern`) uses `RegexOptions.NonBacktracking` where possible,
  always with a 1s match timeout — ReDoS-safe.
- **`xmlquery`** parses raw XML with DTD processing prohibited and no external resolver — XXE-safe.

| Threat | Mitigation | Test |
|--------|-----------|------|
| ReDoS | NonBacktracking / match timeout | `Pattern_CatastrophicBacktracking_TimesOutSafely`, `Regex_Timeout_SafeFail` |
| XXE | DTD prohibited, resolver disabled | `XmlQuery_RawString_XxeRefused` |
| Expression sandbox escape | 2.2.5 evaluator (no CLR reach) | inherited from 2.2.5 |

---

## 🌟 Typed C# script (`builtin.transform.script`)

When declarative modules can't express a transform (multi-stage regroups, complex joins, bespoke
logic), the **script module** runs a typed C# body — the same compile→cache→execute pipeline as
`builtin.database.linq`, generalised into a shared `Workflow.Scripting.Roslyn` core.

The user body receives:

```csharp
// rows:   IReadOnlyList<IReadOnlyDictionary<string, object?>>
// inputs: IReadOnlyDictionary<string, object?>
// ct:     CancellationToken
// return: object?  (materialised out of the sandbox)
return rows
    .GroupBy(r => (string)r["dept"]!)
    .Select(g => (object)new Dictionary<string, object?>
    {
        ["dept"] = g.Key,
        ["total"] = g.Sum(r => Convert.ToInt64(r["age"])),
    })
    .ToList();
```

> **Numeric tip:** JSON integers normalise to `long`, decimals to `double`. Prefer `Convert.ToInt64`/
> `Convert.ToDouble` over hard casts so scripts are robust to either.

### Authoring flow (API)

1. `POST /api/transform/script/validate` — compile-check, returns diagnostics
2. `POST /api/transform/script/preview` — compile **and run** against caller-supplied `sampleRows`
3. `POST /api/transform/script/compile` — compile + cache, returns a `compiledAssemblyKey`
   (**trusted-author gated** via the `X-Trusted-Author: true` header)

The `compiledAssemblyKey` is then set on the `builtin.transform.script` node.

### Security model

- **Compile-time whitelist:** only a curated set of usings is available (`System`, `System.Linq`,
  `System.Collections.Generic`, `System.Text`, `System.Text.RegularExpressions`, `System.Globalization`,
  threading/tasks). No `System.IO`, `System.Net`, or reflection.
- **`ForbiddenSyntaxWalker`:** rejects fully-qualified reaches to `File`/`Process`/`HttpClient`/…,
  `unsafe`, pointers, `stackalloc`, and P/Invoke attributes.
- **HMAC-signed cache:** compiled assemblies are HMAC-tagged in `IBlobStore` under
  `compiled-modules/transform/`; a tampered blob fails verification and never reaches the loader.
- **Collectible ALC:** execution loads into a collectible `AssemblyLoadContext` reused per assembly
  (bounded by LRU), with results materialised into plain BCL types.
- **Trusted-author gate:** compile/save is gated at the API (placeholder header until full auth).

> **Architecture note (Phase 2.6.b):** the compile→whitelist→HMAC-cache→collectible-ALC machinery
> lives in the shared **`Workflow.Scripting.Roslyn`** project — a domain-agnostic generalisation of the
> 2.4.b typed-linq pipeline. The transform-script module (`Workflow.Modules.Transform.Script`) and the
> Roslyn dependency are **quarantined** out of `Workflow.Modules`, so SDK-free hosts never pay for
> Roslyn (test-locked by `ScriptingQuarantineTests`). Retargeting the 2.4.b linq family onto the same
> shared core is a tracked follow-up (its linq-specific runner/cache stay linq-shaped for now).
