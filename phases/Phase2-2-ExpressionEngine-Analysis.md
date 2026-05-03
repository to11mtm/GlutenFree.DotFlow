# Phase 2.2: Expression Engine Options Analysis 🧮✨

Made with 💖 by Ami-Chan! UwU ✨

[Back to Phase 2.2](Phase2-2-AdvancedFlowControl.md) | [Back to Phase 2](Phase2-CoreFeatures.md) | [All Phases](README.md)

---

## TL;DR 🎀

| Engine | Syntax Familiarity | Sandboxing | Deps | Perf | Verdict |
|--------|-------------------|------------|------|------|---------|
| **DynamicExpresso** | C# / LINQ-ish | ✅ Built-in whitelist | `DynamicExpresso.Core` (MIT) | 🟡 Medium | 🔄 Opt-in `"csharp"` fallback |
| **JavaScript (Jint)** | JS / JSON | ⚠️ Manual lockdown required | `Jint` (BSD-2) | 🟡 Medium | ✅ **Default for v1** — native `EvaluateAsync` + CT + `async/await` |
| **Lua (NLua / MoonSharp)** | Lua 5.x | ⚠️ Manual lockdown required | `MoonSharp` (MIT) | 🟢 Fast | 🔄 Good for scripting-heavy flows |

### Async & Threading Summary ⚡

| Feature | DynamicExpresso | Jint (JS) | MoonSharp (Lua) |
|---------|----------------|-----------|-----------------|
| Async in expression syntax | ❌ None | ✅ `async/await` + `Promise` (ES2020) | ⚠️ Coroutines only (`coroutine.yield`) |
| .NET-side `async` wrapper | ✅ `ValueTask.FromResult` (zero alloc) | ✅ Native `EvaluateAsync(script, ct)` — no wrapper needed | ✅ `Task.Run` + `WaitAsync` |
| Native `CancellationToken` during eval | ❌ Entry-only, no mid-eval cancel | ✅ First-class CT parameter on `EvaluateAsync` | ⚠️ Via coroutine resume loop polling |
| Hard wall-clock timeout | ⚠️ `Task.WhenAny` workaround needed | ✅ `TimeoutInterval` config + CT (belt + suspenders) | ⚠️ `Task.WaitAsync` workaround needed |
| Thread safety (shared instance) | ✅ Safe for read (eval only) | ❌ Not safe — pool or create-per-call | ❌ Not safe — create-per-call |
| Recommended instance strategy | Singleton (read-only after init) | `ObjectPool<Engine>` | New `Script` per call |
| Async expression → .NET Task bridge | ❌ Not applicable | ✅ Promise resolves to .NET Task | ❌ Not applicable |

> **CopilotNote:** All three are hidden behind `IExpressionEvaluator`, so the choice is swappable at any time.
> Pick DynamicExpresso for v1 because it requires the least sandboxing ceremony, then evaluate swapping
> based on real-world author feedback~ 💡

---

## Context — Where Expressions Appear in DotFlow 🔀

Expressions are small *predicates or transforms* embedded in workflow definitions. They run inside the engine, sandboxed, with access to current workflow variables. They must be:

- **deterministic** — same inputs → same output
- **side-effect-free** — no I/O, no state mutation outside the variable store
- **bounded** — hard wall-clock + allocation limits
- **serializable** — stored as plain strings in `WorkflowDefinition` JSON

Typical call sites:

| Module | Expression role | Example |
|--------|----------------|---------|
| `builtin.condition` | boolean predicate | `order.total > 100 && order.priority == "high"` |
| `builtin.switch` | match key | `order.status` (then matched against case strings) |
| `builtin.loop.while` | loop condition | `retryCount < 3 && lastError != null` |
| `builtin.trycatch` | `catchTypes` filter | `"ValidationError"` (static string, but could be expression) |
| Future `builtin.transform` | value mapping | `items.Select(x => x.name).ToArray()` *(C#-ish)* |

---

## Option 1 — DynamicExpresso (C# Expression Syntax) 🔷

**NuGet:** `DynamicExpresso.Core` ≥ 2.15 (MIT)
**Docs:** https://github.com/dynamicexpresso/DynamicExpresso

### Syntax Overview

DynamicExpresso parses a **subset of C# expression syntax** — no statements, no `new`, no `var`, no loops. Operators map 1:1 to C# semantics. Variables are registered as typed parameters.

```csharp
// Registration
var interpreter = new Interpreter();
interpreter.SetVariable("order", orderObj);
interpreter.SetVariable("retryCount", 2);

// Evaluate to bool
bool result = interpreter.Eval<bool>("order.total > 100 && retryCount < 3");

// Evaluate to string
string key = interpreter.Eval<string>("order.status");
```

### Expression Examples

#### Conditions (`builtin.condition`)

```
// Simple numeric compare
order.total > 100

// Compound
order.total > 100 && order.priority == "high"

// Null-safe negation (DynamicExpresso supports null-conditional from v2.x)
order.customer?.tier != null && order.customer.tier == "gold"

// Collection membership — via helper registration
Contains(tags, "urgent")

// String helpers — via helper registration  
Lower(order.status) == "pending"

// Ternary
order.total > 500 ? "premium" : "standard"
```

#### Switch keys (`builtin.switch`)

```
// direct property access
order.status

// computed
order.total >= 1000 ? "large" : "small"
```

#### While condition (`builtin.loop.while`)

```
retryCount < 3 && lastResult == null
```

#### Transform expression (future `builtin.transform`)

```
// Member projection — only if we register IEnumerable extensions
items.Count()

// Conditional value
score >= 90 ? "A" : score >= 80 ? "B" : "C"
```

### Sandboxing

DynamicExpresso only resolves types that are **explicitly registered** — unknown type names throw `ParseException`. To harden:

```csharp
// Restrict to only the types we register
var interpreter = new Interpreter(InterpreterOptions.Default)
    .DisableReflection();  // available post v2.14

// Whitelist only safe .NET primitives + registered input objects
interpreter.Reference(typeof(Math));
interpreter.Reference(typeof(string));
// Do NOT reference System.IO, System.Diagnostics, etc.
```

### Async Support ⚡

DynamicExpresso is **entirely synchronous** — `Eval<T>()` and `Parse()` both block the calling thread. There is no concept of `async/await` inside an expression, and no native `CancellationToken` hookpoint during evaluation.

```csharp
// ✅ Wrapping pattern used in our DynamicExpressoEvaluator:
public ValueTask<T> EvaluateAsync<T>(
    string expression,
    IReadOnlyDictionary<string, object?> variables,
    CancellationToken cancellationToken = default)
{
    // Pre-check before entering blocking eval — ct is tested here but NOT during eval
    cancellationToken.ThrowIfCancellationRequested();

    // Synchronous eval — offload to thread pool only if caller needs true async
    var parameters = variables
        .Select(kv => new Parameter(kv.Key, kv.Value?.GetType() ?? typeof(object), kv.Value))
        .ToArray();

    var result = _interpreter.Eval<T>(expression, parameters);
    return ValueTask.FromResult(result);
}

// 🐢 For long/untrusted expressions — enforce hard timeout via Task racing:
public async ValueTask<T> EvaluateWithTimeoutAsync<T>(
    string expression,
    IReadOnlyDictionary<string, object?> variables,
    TimeSpan timeout,
    CancellationToken ct = default)
{
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    cts.CancelAfter(timeout);

    // DynamicExpresso can't observe the token mid-eval, so we race the task
    var evalTask = Task.Run(() => _interpreter.Eval<T>(expression,
        variables.Select(kv => new Parameter(kv.Key, kv.Value?.GetType() ?? typeof(object), kv.Value)).ToArray()), cts.Token);

    try { return await evalTask.WaitAsync(timeout, ct); }
    catch (TimeoutException) { throw new ExpressionRuntimeException(expression, "Evaluation timed out", isTimeout: true); }
}
```

**Key async characteristics:**
- ✅ Thread-safe for **read** (compilation/evaluation of different expressions): `Interpreter` can be shared as singleton if only `Eval` is called
- ⚠️ **Not** thread-safe for `SetVariable` / `SetFunction` — configure once at startup, then treat as immutable
- ⚠️ No mid-expression cancellation — token is only checked at call entry; a runaway expression holds the thread until completion or process kill
- ✅ `ValueTask.FromResult` (zero allocation) for the hot path since eval is synchronous

### Pros ✅
- Near-zero learning curve for C# workflow authors
- Strongly typed — catches type mismatches at parse time (fast feedback)
- `DisableReflection()` reduces attack surface cleanly
- Compile-and-cache: parse once, evaluate many (fast loop iterations)
- No extra runtime (runs in CLR)

### Cons ⚠️
- Subset of C#, not full C# — authors will hit walls (no `foreach`, no `var`, no multi-line)
- Non-C# authors (JS devs, low-code users) find the syntax surprising
- LINQ-style extensions require explicit registration
- Less popular than Lua/JS in visual workflow tools

---

## Option 2 — JavaScript (Jint Engine) 🟡

**NuGet:** `Jint` ≥ 4.1 (BSD-2)
**Docs:** https://github.com/sebastienros/jint

### Syntax Overview

Jint runs a full **ES2020 interpreter** in .NET. Expressions can be multi-statement JS. Variables are injected as JS values. Return value is the last expression or explicit `return`.

```csharp
var engine = new Engine(options => options
    .LimitMemory(4_000_000)           // 4 MB
    .TimeoutInterval(TimeSpan.FromMilliseconds(250))
    .LimitRecursion(64)
    .Strict());

engine.SetValue("order", order);
engine.SetValue("retryCount", retryCount);

var result = engine.Evaluate("order.total > 100 && retryCount < 3").AsBoolean();
```

### Expression Examples

#### Conditions (`builtin.condition`)

```javascript
// Simple
order.total > 100

// Compound
order.total > 100 && order.priority === "high"

// Null-safe (JS optional chaining)
order.customer?.tier !== undefined && order.customer.tier === "gold"

// Array helpers — native JS
tags.includes("urgent")

// String helpers — native JS
order.status.toLowerCase() === "pending"

// Ternary
order.total > 500 ? "premium" : "standard"
```

#### Switch keys (`builtin.switch`)

```javascript
// Direct access
order.status

// Computed
order.total >= 1000 ? "large" : "small"
```

#### While condition

```javascript
retryCount < 3 && lastResult === null
```

#### Transform expression (future)

```javascript
// Native array methods
items.map(x => x.name)

// Filter + map chain
items.filter(x => x.active).map(x => x.id)

// Reduce
items.reduce((acc, x) => acc + x.price, 0)
```

#### Multi-line (optional, if we allow statement mode)

```javascript
const discount = order.tier === "gold" ? 0.2 : 0.05;
order.total * (1 - discount)
```

### Sandboxing

Jint requires **manual lockdown** — by default it can access .NET types via `engine.SetValue`. Hardening:

```csharp
var engine = new Engine(options => options
    .LimitMemory(4_000_000)
    .TimeoutInterval(TimeSpan.FromMilliseconds(250))
    .LimitRecursion(64)
    .Strict()
    // Prevent accessing CLR types not explicitly registered
    .CatchClrExceptions());

// Do NOT setvalue for dangerous types
// Do NOT enable AllowClrWrite
// Only inject plain POCOs / anonymous objects (auto-serialised to JS)
```

> ⚠️ **CopilotNote:** Jint doesn't have a "whitelist only" mode — you must be careful about what you inject.
> If you inject a POCO that has a property returning `IServiceProvider` or similar, that's exposed. Always
> clone/project to safe DTO shapes before injection~ 🛡️

### Async Support ⚡

Jint 4.x exposes a **first-class `Engine.EvaluateAsync(script, ct)`** on the .NET side — it is a true `Task<JsValue>` that can be `await`ed and accepts a `CancellationToken` directly. No `Task.Run` wrapping is needed. JS `async/await` and `Promise` are also fully supported *inside* expressions; Jint resolves them as part of the async evaluation.

> **CopilotNote:** Earlier versions of this doc incorrectly stated that `.NET`-side evaluation was synchronous.
> The GitHub source confirms `EvaluateAsync` was added in Jint 4.x. Use it directly — no `Task.Run` wrapper
> required~ ✅

```csharp
// ✅ Jint 4.x — EvaluateAsync is built-in, CancellationToken is natively supported
public async ValueTask<T> EvaluateAsync<T>(
    string expression,
    IReadOnlyDictionary<string, object?> variables,
    CancellationToken cancellationToken = default)
{
    // Pool engines — instances are still NOT thread-safe (concurrent calls need separate instances)
    var engine = _enginePool.Get();
    try
    {
        foreach (var (key, value) in variables)
            engine.SetValue(key, value);

        // ✅ True async — no Task.Run, no WaitAsync; ct is respected natively
        // TimeoutInterval on engine config acts as a secondary hard-cap (belt + suspenders)
        var jsResult = await engine.EvaluateAsync(expression, cancellationToken);
        return (T)jsResult.ToObject()!;
    }
    catch (OperationCanceledException) { throw; }
    catch (JavaScriptException ex)     { throw new ExpressionRuntimeException(expression, ex.Message, ex); }
    catch (ParserException ex)         { throw new ExpressionParseException(expression, ex.Description, ex); }
    finally { _enginePool.Return(engine); }
}

// ✅ async JS expression — Promise is awaited as part of EvaluateAsync
var result = await engine.EvaluateAsync("Promise.resolve(order.total > 100)", ct);
// result.AsBoolean() == true
```

**Key async characteristics:**
- ✅ `Engine.EvaluateAsync(script, ct)` — built-in true async, no `Task.Run` workaround needed
- ✅ `CancellationToken` is a first-class parameter — cancellation works mid-evaluation (not just at entry)
- ✅ JS `async/await` and `Promise` are resolved as part of `EvaluateAsync`'s task lifecycle
- ✅ `TimeoutInterval` on engine config still applies as a safety cap alongside the cancellation token
- ⚠️ `Engine` instances are still **not thread-safe** — pool or create-per-call; `EvaluateAsync` is async but not concurrent-safe on the same instance
- ⚠️ JS `async` expressions that don't `await` their result return a `Promise` object, not the resolved value — author must use `await` in expression body for async sub-calls (uncommon for simple predicates)

### Pros ✅
- **Most familiar** to web/low-code authors (JS is everywhere)
- Native `?.`, `??`, `Array.map/filter/reduce` — very expressive for data transforms
- Multi-line statement mode possible for power users
- Huge community; lots of example workflows available
- Good fit for future "scripting" phase (Phase 3)

### Cons ⚠️
- Manual sandboxing ceremony — easy to accidentally expose a CLR surface
- `===` vs `==` JS semantics may surprise C# workflow authors
- Larger dependency footprint than DynamicExpresso
- Type coercion (`"5" + 3 === "53"`) can surprise strongly-typed C# devs
- Slightly heavier startup per-engine instance (mitigated by pooling)

---

## Option 3 — Lua (MoonSharp) 🌙

**NuGet:** `MoonSharp.Interpreter` ≥ 2.0 (MIT)
**Docs:** https://www.moonsharp.org / https://github.com/moonsharp-devs/moonsharp

> **Alternative:** `NLua` (wraps native Lua 5.4 via P/Invoke) is faster but harder to sandbox on all platforms. Recommend `MoonSharp` for .NET-native sandboxing.

### Syntax Overview

MoonSharp runs **Lua 5.x** fully in managed .NET. Variables are set as globals or passed in a table. Expressions are Lua expressions or small scripts.

```csharp
var script = new Script(CoreModules.Preset_HardSandbox);
script.Globals["order"] = UserData.Create(orderDto);  // must be registered
script.Globals["retryCount"] = retryCount;

// Single expression (wrap in return for Lua)
var result = script.DoString("return order.total > 100 and retryCount < 3");
bool boolResult = result.CastToBool();
```

### Expression Examples

Note: Lua uses `and` / `or` / `not` instead of `&&` / `||` / `!`, `~=` for not-equal, and `#` for length.

#### Conditions (`builtin.condition`)

```lua
-- Simple
order.total > 100

-- Compound
order.total > 100 and order.priority == "high"

-- Null-safe (Lua: nil check)
order.customer ~= nil and order.customer.tier == "gold"

-- Table membership helper (register as global)
contains(tags, "urgent")

-- String helpers — Lua string library
string.lower(order.status) == "pending"

-- Ternary equivalent (Lua idiom)
order.total > 500 and "premium" or "standard"
```

#### Switch keys (`builtin.switch`)

```lua
-- Direct access
order.status

-- Computed
order.total >= 1000 and "large" or "small"
```

#### While condition

```lua
retryCount < 3 and lastResult == nil
```

#### Transform expression (future)

```lua
-- Lua tables / array helpers need custom registration
-- Length
#items

-- Map via registered helper
map(items, function(x) return x.name end)

-- Sum via registered helper
reduce(items, 0, function(acc, x) return acc + x.price end)
```

#### Multi-line (Lua is very natural for inline scripts)

```lua
local discount = 0.05
if order.tier == "gold" then
  discount = 0.2
end
return order.total * (1 - discount)
```

### Sandboxing

MoonSharp has a `CoreModules.Preset_HardSandbox` preset that removes all I/O, OS, debug modules:

```csharp
var script = new Script(CoreModules.Preset_HardSandbox);
// HardSandbox removes: io, os, file, dofile, loadfile, load, dostring
// Keeps: math, string, table, basic math ops

// Register only safe POCOs with [MoonSharpUserData] attribute
UserData.RegisterType<OrderDto>();
UserData.RegisterType<CustomerDto>();

// Timeout via CancellationToken or Jit instruction count hook
script.Options.DebugInput = (ctx) => {
    if (ctx.EllapsedTime > TimeSpan.FromMilliseconds(250))
        throw new ScriptRuntimeException("Expression timeout");
};
```

> ⚠️ **CopilotNote:** MoonSharp's timeout hook is best-effort (fires every ~100 instructions by default).
> For strict hard timeouts, wrap the evaluation in a `Task` with a `CancellationToken` + `Task.WhenAny`~ ⏱️

### Async Support ⚡

MoonSharp has **Lua coroutines** as its native concurrency primitive — not `async/await`. Coroutines are cooperative, single-threaded, and require explicit `coroutine.yield()` / `coroutine.resume()` calls from expression code. From the **.NET side**, `script.DoString()` is synchronous. MoonSharp also exposes a dedicated `Coroutine` API for interleaved execution.

```csharp
// ✅ Standard pattern — synchronous DoString, offloaded to thread pool in our wrapper:
public async ValueTask<T> EvaluateAsync<T>(
    string expression,
    IReadOnlyDictionary<string, object?> variables,
    CancellationToken cancellationToken = default)
{
    cancellationToken.ThrowIfCancellationRequested();

    // Each Script instance is NOT thread-safe — create per call or pool
    var script = new Script(CoreModules.Preset_HardSandbox);
    foreach (var (key, value) in variables)
        script.Globals[key] = value;

    var wrapped = expression.TrimStart().StartsWith("return ") ? expression : $"return ({expression})";

    // Offload blocking eval + enforce hard wall-clock timeout via WaitAsync
    var evalTask = Task.Run(() => script.DoString(wrapped), cancellationToken);
    try
    {
        var dynResult = await evalTask.WaitAsync(_options.Timeout, cancellationToken);
        return (T)dynResult.ToObject()!;
    }
    catch (TimeoutException) { throw new ExpressionRuntimeException(expression, "Evaluation timed out", isTimeout: true); }
}

// ✅ Coroutine-based approach — for long iterative Lua logic that yields mid-execution:
public ValueTask<T> EvaluateAsCoroutineAsync<T>(Script script, string expression, CancellationToken ct)
{
    var fn = script.LoadString(expression);
    var coroutine = script.CreateCoroutine(fn);

    // Resume the coroutine in a loop; each Resume returns until yield or completion
    DynValue result;
    do
    {
        ct.ThrowIfCancellationRequested();
        result = coroutine.Coroutine.Resume();
    }
    while (result.Type == DataType.YieldRequest);

    return ValueTask.FromResult((T)result.ToObject()!);
}
```

**Key async characteristics:**
- ✅ Lua **coroutines** enable cooperative multi-step execution — useful if a Lua expression needs to "pause" between phases (though rare for simple predicates)
- ✅ Hard timeout enforced via `Task.WaitAsync` wrapping — more reliable than MoonSharp's best-effort `DebugInput` hook
- ⚠️ `Script` instances are **not thread-safe** — must create-per-call (relatively cheap) or pool carefully
- ✅ Coroutine-style evaluation is the only native "yield" primitive — no `async/await` syntax in Lua itself
- ⚠️ Mid-expression `CancellationToken` requires polling in the coroutine resume loop (as shown above) — can't inject the token into the Lua runtime directly
- ✅ Each `Script` creation is cheap enough for per-call use with `HardSandbox` preset; no JIT warm-up costs

### Pros ✅
- `CoreModules.Preset_HardSandbox` is the strongest out-of-the-box sandbox of the three options
- Lua is designed as an *embedded* scripting language — this is its native role
- Very fast interpreter; low allocation pressure
- Lua tables are naturally JSON-compatible for workflow variables
- Excellent for power-user scripting (full multi-line, functions, closures)
- MIT license; fully managed (MoonSharp) — no P/Invoke, no native deps

### Cons ⚠️
- `and`/`or`/`not` syntax surprises most developers (C#, JS, Python background)
- `~=` for inequality is unusual
- Arrays are 1-indexed — catches authors off-guard
- Smaller community than JS relative to the "workflow automation" audience
- Requires explicit `UserData.RegisterType<T>()` for every POCO — more setup per new variable type
- `and/or` for ternary is a common Lua footgun (`false and "x" or "y"` returns `"y"` if middle is false)

---

## Side-by-Side Expression Cheat Sheet 📋

The same expression in all three engines, using a representative `order` object:

| Intent | DynamicExpresso (C#) | JavaScript (Jint) | Lua (MoonSharp) |
|--------|---------------------|-------------------|----------------|
| Simple compare | `order.total > 100` | `order.total > 100` | `order.total > 100` |
| Not equal | `order.status != "done"` | `order.status !== "done"` | `order.status ~= "done"` |
| Compound AND | `a > 0 && b < 10` | `a > 0 && b < 10` | `a > 0 and b < 10` |
| Compound OR | `a == 1 \|\| b == 2` | `a === 1 \|\| b === 2` | `a == 1 or b == 2` |
| Logical NOT | `!flag` | `!flag` | `not flag` |
| Null check | `value != null` | `value !== null && value !== undefined` | `value ~= nil` |
| Null-safe access | `obj?.name` | `obj?.name` | `obj ~= nil and obj.name or nil` |
| Null coalesce | `value ?? "default"` | `value ?? "default"` | `value ~= nil and value or "default"` |
| String lower | `Lower(s)` *(helper)* | `s.toLowerCase()` | `string.lower(s)` |
| String contains | `Contains(s, "x")` *(helper)* | `s.includes("x")` | `string.find(s, "x") ~= nil` |
| Array length | `len(items)` *(helper)* | `items.length` | `#items` |
| Array contains | `Contains(items, x)` *(helper)* | `items.includes(x)` | *(helper needed)* |
| Array map | *(LINQ helper needed)* | `items.map(x => x.id)` | `map(items, function(x) return x.id end)` |
| Ternary | `a > 0 ? "yes" : "no"` | `a > 0 ? "yes" : "no"` | `a > 0 and "yes" or "no"` ⚠️ |
| Arithmetic | `(a + b) * 2` | `(a + b) * 2` | `(a + b) * 2` |
| Modulo | `a % 2 == 0` | `a % 2 === 0` | `a % 2 == 0` |
| Math.Abs | `Math.Abs(x)` | `Math.abs(x)` | `math.abs(x)` |
| String interpolation | *(not supported)* | `` `Hello ${name}` `` | `"Hello " .. name` |
| Multi-line logic | ❌ Not supported | ✅ Allowed (statement mode) | ✅ Allowed |
| Built-in reduce | *(LINQ helper)* | `items.reduce(fn, 0)` | *(helper needed)* |

> ⚠️ **Lua ternary footgun:** `cond and "x" or "y"` returns `"y"` when `cond` is `true` but the true-branch value is `false` or `nil`. Use a helper `ternary(cond, "x", "y")` registered on the sandbox for safety~ 🛡️

---

## Expression Testing & Error Diagnostics 🔍

This section covers how each engine supports **pre-validation**, **structured error output**, and **test-driving expressions with sample inputs** — critical for both author tooling and unit tests~ 🧪

---

### Pre-Validation (Parse Without Executing)

Checking an expression for syntax errors *before* attaching it to a workflow node or running it in a loop is a must-have for any authoring UI~ ✅

| Feature | DynamicExpresso | Jint (JS) | MoonSharp (Lua) |
|---------|----------------|-----------|-----------------|
| Parse-only (no exec) | ✅ `interpreter.Parse(expr)` | ✅ `Engine.Parse(script)` | ✅ `script.LoadString(src)` |
| Returns typed lambda | ✅ `Lambda` object, re-usable | ❌ (returns `Script` AST node) | ✅ `DynValue` function, re-usable |
| Reports error position | ✅ `ParseException.Position` (row+col) | ✅ `ParserException.Position` | ✅ `SyntaxErrorException` (line) |
| Type-checks at parse time | ✅ Yes (strongly typed parameters needed) | ❌ Dynamic — runtime only | ❌ Dynamic — runtime only |
| Detects unknown identifiers | ✅ Yes (if parameters pre-registered) | ❌ `undefined` at runtime | ❌ `nil` at runtime |

---

### DynamicExpresso — Validation & Error Details 🔷

#### Parse-only validation

```csharp
var interpreter = new Interpreter().DisableReflection();

// Register parameter types (no values needed for parse)
interpreter.SetVariable("order", default(OrderDto)); // null value, correct type

try
{
    var lambda = interpreter.Parse("order.total > 100 && order.priority == \"high\"",
        new Parameter("order", typeof(OrderDto)));
    // ✅ Lambda is cached and re-invokable — zero re-parse cost in loops
    Console.WriteLine($"✅ OK — return type: {lambda.ReturnType.Name}");
}
catch (ParseException ex)
{
    Console.WriteLine($"❌ Parse error at {ex.Position}: {ex.Message}");
}
```

#### Error message examples

```
// Unknown identifier
expression: "ordr.total > 100"
❌ ParseException at (1,1): Unknown identifier 'ordr'

// Wrong operator
expression: "order.total >> 100"
❌ ParseException at (1,13): Unexpected character '>'

// Type mismatch
expression: "order.total + order.status"   // int + string
❌ ParseException at (1,14): Operator '+' is not defined for types 'Double' and 'String'

// Disallowed reflection (with DisableReflection)
expression: "order.GetType().Name"
❌ ParseException: Method 'GetType' is not accessible
```

#### Sample evaluation output

```csharp
var interpreter = new Interpreter().DisableReflection();
var order = new { total = 250.0, priority = "high", status = "pending" };

var testCases = new[]
{
    "order.total > 100",
    "order.total > 100 && order.priority == \"high\"",
    "order.total >= 1000 ? \"large\" : \"small\"",
    "Lower(order.status) == \"pending\"",
};

foreach (var expr in testCases)
{
    var result = interpreter.Eval(expr, new Parameter("order", order));
    Console.WriteLine($"  {expr,-55} → {result}");
}
```

```
  order.total > 100                                       → True
  order.total > 100 && order.priority == "high"          → True
  order.total >= 1000 ? "large" : "small"                → small
  Lower(order.status) == "pending"                       → True
```

#### ExpressionParseException / ExpressionRuntimeException shape (our wrapper)

```csharp
// ExpressionParseException carries:
ex.Expression   // "ordr.total > 100"
ex.Message      // "Unknown identifier 'ordr'"
ex.Position     // (Line: 1, Column: 1)   ← from ParseException.Position

// ExpressionRuntimeException carries:
ex.Expression   // "order.total / denominator"
ex.Message      // "Division by zero"
ex.InnerException.GetType().Name  // "DivisionByZeroException" (wrapped)
```

---

### JavaScript (Jint) — Validation & Error Details 🟡

#### Parse-only validation

```csharp
// Jint 4.x — parse via Esprima (bundled)
using Esprima;

try
{
    var parser = new JavaScriptParser();
    var ast = parser.ParseExpression("order.total > 100 && order.priority === \"high\"");
    Console.WriteLine("✅ Syntax OK");
}
catch (ParserException ex)
{
    Console.WriteLine($"❌ Syntax error at line {ex.LineNumber} col {ex.Column}: {ex.Description}");
}

// Note: Jint parse does NOT check identifier resolution — that's runtime only.
// Unknown variables silently resolve to `undefined` unless you use strict mode.
```

#### Error message examples

```
// Syntax error — unmatched bracket
expression: "order.total > 100 &&"
❌ ParserException at line 1 col 20: Unexpected end of input

// Strict mode — unknown identifier
expression: "ordr.total > 100"  (strict mode + no 'ordr' set)
❌ JavaScriptException at line 1: ReferenceError: ordr is not defined

// Type error at runtime
expression: "order.total.toUpperCase()"   // number has no toUpperCase
❌ JavaScriptException at line 1: TypeError: order.total.toUpperCase is not a function

// Timeout exceeded
expression: "while(true){}"
❌ TimeoutException: Script execution timed out (250ms)

// Memory exceeded
expression: "new Array(999999999)"
❌ MemoryLimitExceededException: 4194304 bytes memory limit exceeded
```

#### Sample evaluation output

```csharp
var engine = new Engine(cfg => cfg
    .LimitMemory(4_000_000)
    .TimeoutInterval(TimeSpan.FromMilliseconds(250))
    .Strict());

var order = new { total = 250.0, priority = "high", status = "pending",
                  tags = new[] { "urgent", "paid" } };
engine.SetValue("order", order);

var testCases = new[]
{
    "order.total > 100",
    "order.total > 100 && order.priority === \"high\"",
    "order.total >= 1000 ? \"large\" : \"small\"",
    "order.status.toLowerCase() === \"pending\"",
    "order.tags.includes(\"urgent\")",
    "order.tags.filter(x => x.length > 4).map(x => x.toUpperCase())",
};

foreach (var expr in testCases)
{
    var result = engine.Evaluate(expr);
    Console.WriteLine($"  {expr,-60} → {result}");
}
```

```
  order.total > 100                                           → True
  order.total > 100 && order.priority === "high"             → True
  order.total >= 1000 ? "large" : "small"                    → small
  order.status.toLowerCase() === "pending"                   → True
  order.tags.includes("urgent")                              → True
  order.tags.filter(x => x.length > 4).map(x => x.toUpperCase()) → ["URGENT"]
```

#### ExpressionParseException / ExpressionRuntimeException shape (our wrapper)

```csharp
// ExpressionParseException wraps ParserException:
ex.Expression   // "order.total > 100 &&"
ex.Message      // "Unexpected end of input"
ex.Line         // 1
ex.Column       // 20

// ExpressionRuntimeException wraps JavaScriptException:
ex.Expression   // "order.total.toUpperCase()"
ex.Message      // "TypeError: order.total.toUpperCase is not a function"
ex.JsStack      // " at <anonymous>:1:12"  ← JS stack trace string
```

---

### Lua (MoonSharp) — Validation & Error Details 🌙

#### Parse-only validation (load without execute)

```csharp
var script = new Script(CoreModules.Preset_HardSandbox);

try
{
    // LoadString compiles to bytecode but does NOT execute — safe for validation
    DynValue fn = script.LoadString("return order.total > 100 and order.priority == \"high\"");
    Console.WriteLine($"✅ Syntax OK — loaded as {fn.Type}");
    // fn is a DynValue(Function) — can be called later with script.Call(fn)
}
catch (SyntaxErrorException ex)
{
    Console.WriteLine($"❌ Syntax error at line {ex.DecoratedMessage}: {ex.Message}");
}
```

#### Error message examples

```
// Syntax error — missing `then`
expression: "if order.total > 100 return true end"
❌ SyntaxErrorException: [string "..."]:1: 'then' expected near 'return'
   DecoratedMessage: chunk_1:1: 'then' expected near 'return'

// Runtime nil access
expression: "return order.missing.value"  (order.missing is nil)
❌ ScriptRuntimeException: [string "..."]:1: attempt to index a nil value (field 'missing')

// Attempt to call unsafe removed module
expression: "return os.time()"   (os removed by HardSandbox)
❌ ScriptRuntimeException: [string "..."]:1: attempt to index a nil value (global 'os')

// Stack overflow (recursion limit)
expression: "local function f() return f() end return f()"
❌ ScriptRuntimeException: [string "..."]:1: stack overflow
```

#### Sample evaluation output

```csharp
var script = new Script(CoreModules.Preset_HardSandbox);

// Register a safe DTO
UserData.RegisterType<OrderDto>();
var order = new OrderDto { Total = 250.0, Priority = "high", Status = "pending" };
script.Globals["order"] = UserData.Create(order);

var testCases = new[]
{
    "return order.Total > 100",
    "return order.Total > 100 and order.Priority == \"high\"",
    "return order.Total >= 1000 and \"large\" or \"small\"",
    "return string.lower(order.Status) == \"pending\"",
};

foreach (var expr in testCases)
{
    var result = script.DoString(expr);
    Console.WriteLine($"  {expr,-55} → {result}");
}
```

```
  return order.Total > 100                                → true
  return order.Total > 100 and order.Priority == "high"  → true
  return order.Total >= 1000 and "large" or "small"      → small
  return string.lower(order.Status) == "pending"         → true
```

> ⚠️ Note Lua property names are **case-sensitive** and must match the registered `[MoonSharpUserData]` member names exactly (PascalCase by default from C# unless remapped). This is a common author trip-up~ 🛡️

#### ExpressionParseException / ExpressionRuntimeException shape (our wrapper)

```csharp
// ExpressionParseException wraps SyntaxErrorException:
ex.Expression        // "if order.total > 100 return true end"
ex.Message           // "'then' expected near 'return'"
ex.LuaSourceLine     // 1
ex.DecoratedMessage  // "chunk_1:1: 'then' expected near 'return'"

// ExpressionRuntimeException wraps ScriptRuntimeException:
ex.Expression        // "return order.missing.value"
ex.Message           // "attempt to index a nil value (field 'missing')"
ex.LuaCallStack      // "[string \"...\"]:1" — Lua decorates with source location
```

---

### Test-Harness Utility Sketch (shared across all three) 🧪

For unit tests and authoring tooling we want a simple `ExpressionTestResult` wrapper:

```csharp
/// <summary>
/// Describes the outcome of a single expression test run~ 🧪
/// </summary>
public sealed record ExpressionTestResult
{
    /// <summary>The raw expression string that was tested.</summary>
    public required string Expression { get; init; }

    /// <summary>The variable bindings used during the test.</summary>
    public required IReadOnlyDictionary<string, object?> Inputs { get; init; }

    /// <summary>The evaluated result value, or null if evaluation failed.</summary>
    public object? Output { get; init; }

    /// <summary>True when the expression parsed and evaluated without error.</summary>
    public bool Success { get; init; }

    /// <summary>Structured error, if any. Null on success.</summary>
    public ExpressionError? Error { get; init; }
}

public sealed record ExpressionError
{
    public required string Kind { get; init; }   // "ParseError" | "RuntimeError" | "Timeout"
    public required string Message { get; init; }
    public int? Line { get; init; }
    public int? Column { get; init; }
    public string? EngineStack { get; init; }    // raw engine stack string for power users
}
```

```csharp
// Extension on IExpressionEvaluator — used by authoring UI + unit tests
public static async Task<ExpressionTestResult> TestAsync(
    this IExpressionEvaluator evaluator,
    string expression,
    IReadOnlyDictionary<string, object?> inputs,
    CancellationToken ct = default)
{
    try
    {
        var output = await evaluator.EvaluateAsync<object?>(expression, inputs, ct);
        return new ExpressionTestResult
        {
            Expression = expression, Inputs = inputs,
            Output = output, Success = true
        };
    }
    catch (ExpressionParseException ex)
    {
        return new ExpressionTestResult
        {
            Expression = expression, Inputs = inputs, Success = false,
            Error = new ExpressionError
            {
                Kind = "ParseError", Message = ex.Message,
                Line = ex.Line, Column = ex.Column
            }
        };
    }
    catch (ExpressionRuntimeException ex)
    {
        return new ExpressionTestResult
        {
            Expression = expression, Inputs = inputs, Success = false,
            Error = new ExpressionError
            {
                Kind = ex.IsTimeout ? "Timeout" : "RuntimeError",
                Message = ex.Message, EngineStack = ex.EngineStack
            }
        };
    }
}
```

Sample unit test usage:

```csharp
[Theory]
[InlineData("order.total > 100",                   true)]
[InlineData("order.total > 100 && order.priority == \"vip\"", false)]
[InlineData("order.total >= 1000 ? \"large\" : \"small\"",     "small")]
public async Task Evaluator_CorrectlyEvaluates_CommonExpressions(string expr, object expected)
{
    var inputs = new Dictionary<string, object?>
    {
        ["order"] = new { total = 250.0, priority = "standard" }
    };

    var result = await _evaluator.TestAsync(expr, inputs);

    result.Success.Should().BeTrue(because: result.Error?.Message ?? "no error");
    result.Output.Should().Be(expected);
}

[Fact]
public async Task Evaluator_Returns_ParseError_For_Unknown_Identifier()
{
    var result = await _evaluator.TestAsync("ordr.total > 0", new Dictionary<string, object?>
    {
        ["order"] = new { total = 100.0 }
    });

    result.Success.Should().BeFalse();
    result.Error!.Kind.Should().Be("ParseError");
    result.Error.Message.Should().Contain("ordr");
}
```

---



## Integration with `IExpressionEvaluator` 🔌

All three map cleanly to the same interface. The async contract is `ValueTask<T>` — synchronous engines return `ValueTask.FromResult(result)` (zero allocation), while engines requiring thread-pool offload return a true `Task`-backed `ValueTask`~ 🔌

```csharp
/// <summary>
/// Safe, sandboxed evaluation of a single expression string.
/// The active implementation is registered via DI and can be swapped without touching modules~ 🔌
/// </summary>
public interface IExpressionEvaluator
{
    /// <summary>Evaluate expression and cast result to <typeparamref name="T"/>.</summary>
    /// <exception cref="ExpressionParseException">Syntax or type error detected at parse time.</exception>
    /// <exception cref="ExpressionRuntimeException">Runtime error, timeout, or access violation.</exception>
    ValueTask<T> EvaluateAsync<T>(
        string expression,
        IReadOnlyDictionary<string, object?> variables,
        CancellationToken cancellationToken = default);
}
```

### DynamicExpresso adapter sketch

```csharp
// Workflow.Engine/Services/DynamicExpressoEvaluator.cs
public sealed class DynamicExpressoEvaluator : IExpressionEvaluator
{
    private readonly Interpreter _interpreter = new Interpreter()
        .DisableReflection()
        .SetFunction("len",      (IEnumerable<object> x) => x.Count())
        .SetFunction("contains", (IEnumerable<object> x, object v) => x.Contains(v))
        .SetFunction("lower",    (string s) => s.ToLowerInvariant())
        .SetFunction("upper",    (string s) => s.ToUpperInvariant())
        .SetFunction("now",      () => DateTimeOffset.UtcNow);

    public ValueTask<T> EvaluateAsync<T>(
        string expression,
        IReadOnlyDictionary<string, object?> variables,
        CancellationToken cancellationToken = default)
    {
        var parameters = variables
            .Select(kv => new Parameter(kv.Key, kv.Value?.GetType() ?? typeof(object), kv.Value))
            .ToArray();
        try
        {
            var result = _interpreter.Eval<T>(expression, parameters);
            return new ValueTask<T>(result);
        }
        catch (ParseException ex)  { throw new ExpressionParseException(expression, ex.Message, ex); }
        catch (Exception ex)       { throw new ExpressionRuntimeException(expression, ex.Message, ex); }
    }
}
```

### JavaScript (Jint) adapter sketch

```csharp
// Workflow.Engine/Services/JintExpressionEvaluator.cs
public sealed class JintExpressionEvaluator : IExpressionEvaluator
{
    private readonly ObjectPool<Engine> _enginePool;

    private static Engine CreateEngine() => new Engine(cfg => cfg
        .LimitMemory(4_000_000)
        .TimeoutInterval(TimeSpan.FromMilliseconds(250))  // secondary hard-cap
        .LimitRecursion(64)
        .Strict()
        .CatchClrExceptions());

    public async ValueTask<T> EvaluateAsync<T>(
        string expression,
        IReadOnlyDictionary<string, object?> variables,
        CancellationToken cancellationToken = default)
    {
        var engine = _enginePool.Get();
        try
        {
            foreach (var (key, value) in variables)
                engine.SetValue(key, value);   // ⚠️ project to safe DTOs first!

            // ✅ EvaluateAsync is built-in — CancellationToken supported natively, no Task.Run needed
            var jsResult = await engine.EvaluateAsync(expression, cancellationToken);
            return (T)jsResult.ToObject()!;
        }
        catch (JavaScriptException ex) { throw new ExpressionRuntimeException(expression, ex.Message, ex); }
        catch (ParserException ex)     { throw new ExpressionParseException(expression, ex.Description, ex); }
        finally { _enginePool.Return(engine); }
    }
}
```

### Lua (MoonSharp) adapter sketch

```csharp
// Workflow.Engine/Services/MoonSharpExpressionEvaluator.cs
[MoonSharpModule]
public sealed class MoonSharpExpressionEvaluator : IExpressionEvaluator
{
    public ValueTask<T> EvaluateAsync<T>(
        string expression,
        IReadOnlyDictionary<string, object?> variables,
        CancellationToken cancellationToken = default)
    {
        var script = new Script(CoreModules.Preset_HardSandbox);
        foreach (var (key, value) in variables)
            script.Globals[key] = value;

        // Lua needs `return` for expression result
        var wrapped = expression.TrimStart().StartsWith("return ")
            ? expression
            : $"return ({expression})";
        try
        {
            var dynResult = script.DoString(wrapped);
            return new ValueTask<T>((T)dynResult.ToObject()!);
        }
        catch (SyntaxErrorException ex) { throw new ExpressionParseException(expression, ex.Message, ex); }
        catch (ScriptRuntimeException ex) { throw new ExpressionRuntimeException(expression, ex.Message, ex); }
    }
}
```

---

## Decision Factors by Audience 👥

| Audience / Priority | Best Pick | Runner-up |
|---------------------|-----------|-----------|
| C# backend engineers (internal tool) | DynamicExpresso | Lua |
| Web / low-code UI workflow authors | JavaScript (Jint) | DynamicExpresso |
| Scripting power users | Lua (MoonSharp) | JavaScript |
| Strictest on-prem sandboxing requirements | Lua (HardSandbox) | DynamicExpresso |
| Minimum external dependency footprint | DynamicExpresso | Lua |
| Future `builtin.transform` data pipeline | JavaScript | Lua |
| Replay determinism / no date/rand surprises | DynamicExpresso | Lua |

---

## Recommended Adoption Plan 🗺️

```
Phase 2.2.5 (v1)
  └─ Ship: JintExpressionEvaluator as default IExpressionEvaluator ✅
       Reason: native EvaluateAsync + CancellationToken, full async/await + Promise inside expressions,
               rich built-in JS array/string helpers, no manual helper registration needed
  └─ Ship: DynamicExpressoEvaluator as opt-in "csharp" keyed fallback
       Reason: zero async overhead for simple sync predicates; C#-syntax familiarity for backend devs

Phase 3.x (scripting)
  └─ Promote Jint to statement mode (multi-line scripts, import-style helpers)
       Reason: multi-line transforms, JS familiarity for UI-authored flows already using v1 Jint

Phase 3.x+ (optional)
  └─ Add: MoonSharpExpressionEvaluator for scripting-heavy or strict isolation deployments
       Reason: HardSandbox preset + Lua's design-for-embedding story
```

All three can coexist — the engine asks DI for `IExpressionEvaluator`, and each `WorkflowDefinition` could optionally declare a `"expressionEngine": "csharp"|"javascript"|"lua"` property to select per-workflow (or per-node) in a future iteration~ 🌈

---

## Custom Return Types & Serialization Wiring 📦

When an expression needs to return something richer than a scalar (`bool`, `string`, `int`) — e.g. a projected object, a filtered array, or a computed summary record — each engine has a distinct handoff story. The rules are the same for all three:

1. **Never return live CLR objects** (services, EF entities, actors) — project to safe shapes first
2. **Prefer `JsonElement` / `JsonNode` as the universal transport** between engine and the rest of the workflow runtime
3. **Fail fast with a clear error** if the engine returns something that can't be safely serialized

---

### Shared: `IExpressionEvaluator` for complex return types

Extend the interface contract to support object returns cleanly:

```csharp
public interface IExpressionEvaluator
{
    // Scalar / strongly-typed path (existing)
    ValueTask<T> EvaluateAsync<T>(
        string expression,
        IReadOnlyDictionary<string, object?> variables,
        CancellationToken ct = default);

    // ✅ Object path — returns a JsonElement so callers get a universal, safe, serializable value.
    // Use this when the expression returns { key: value } shapes, arrays, or computed records~ 📦
    ValueTask<JsonElement> EvaluateObjectAsync(
        string expression,
        IReadOnlyDictionary<string, object?> variables,
        CancellationToken ct = default);
}
```

> **CopilotNote:** `JsonElement` is the right transport here — it's immutable, stack-friendly, zero extra deps,
> and can be re-serialized, stored in `NodeExecutionRecord.Outputs`, or bound to a module input without any
> type gymnastics. Never return `dynamic` or `object` across the engine boundary~ 💡

---

### DynamicExpresso — Custom Return Wiring 🔷

DynamicExpresso returns a **real .NET object** — the CLR type you declared. For custom return shapes, you have two options:

#### Option A — Return a registered DTO directly (recommended)

```csharp
// DTO is registered as a parameter type — no reflection needed
public record OrderSummary(string Status, double Total, bool IsPremium);

interpreter.SetVariable("order", orderObj);
interpreter.Reference(typeof(OrderSummary)); // whitelist the return type

// Expression returns a constructed record — needs new keyword support (DE v2.15+)
// ⚠️ If `new` is disallowed, use a registered factory function instead (see Option B)
var summary = interpreter.Eval<OrderSummary>(
    "new OrderSummary(order.status, order.total, order.total > 500)");
```

#### Option B — Return an anonymous shape via a registered factory function (safer)

```csharp
// Register a builder function — expression never gets `new` or direct type access
interpreter.SetFunction("summary",
    (string status, double total) => new OrderSummary(status, total, total > 500));

var result = interpreter.Eval<OrderSummary>(
    "summary(order.status, order.total)");
```

#### Serialization to `JsonElement`

```csharp
// Workflow.Engine/Services/DynamicExpressoEvaluator.cs
public async ValueTask<JsonElement> EvaluateObjectAsync(
    string expression,
    IReadOnlyDictionary<string, object?> variables,
    CancellationToken ct = default)
{
    ct.ThrowIfCancellationRequested();

    var parameters = variables
        .Select(kv => new Parameter(kv.Key, kv.Value?.GetType() ?? typeof(object), kv.Value))
        .ToArray();

    object? raw;
    try   { raw = _interpreter.Eval(expression, parameters); }
    catch (ParseException ex) { throw new ExpressionParseException(expression, ex.Message, ex); }

    // Safe round-trip through STJ — only serializable shapes survive~ 🔒
    try
    {
        var json = JsonSerializer.Serialize(raw, _safeSerializerOptions);
        return JsonDocument.Parse(json).RootElement.Clone(); // Clone owns its own memory
    }
    catch (JsonException ex)
    {
        throw new ExpressionRuntimeException(expression,
            $"Expression returned a type that cannot be safely serialized: {raw?.GetType().Name}", ex);
    }
}

// Safe options: no reference loops, no private members, no dangerous converters
private static readonly JsonSerializerOptions _safeSerializerOptions = new()
{
    ReferenceHandler      = ReferenceHandler.IgnoreCycles,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    MaxDepth              = 16,   // prevent deeply nested attack objects
};
```

**Author expression examples:**
```
// Return a projected object via registered factory
summary(order.status, order.total)

// Return a filtered array (if LINQ helpers registered)
Where(items, x => x.active)

// Return a computed scalar packed by registered helper
pack("count", len(items), "total", Sum(items, x => x.price))
```

---

### JavaScript (Jint) — Custom Return Wiring 🟡

JS expressions can return **any JS value** — plain objects `{}`, arrays `[]`, nested structures. Jint converts them to .NET via `JsValue.ToObject()`, which returns `IDictionary<string, object>` for objects and `object[]` for arrays. Use `JSON.stringify` inside the expression + `System.Text.Json` on the .NET side for the cleanest round-trip.

#### Option A — `JSON.stringify` in the expression (most reliable)

```javascript
// Author writes the expression — stringify is always available in JS scope
JSON.stringify({ status: order.status, total: order.total, premium: order.total > 500 })
```

```csharp
// .NET side receives a string → parse directly
public async ValueTask<JsonElement> EvaluateObjectAsync(
    string expression,
    IReadOnlyDictionary<string, object?> variables,
    CancellationToken ct = default)
{
    var engine = _enginePool.Get();
    try
    {
        foreach (var (key, value) in variables)
            engine.SetValue(key, value);

        // ✅ EvaluateAsync — true async, CT natively supported, no Task.Run needed
        var jsResult = await engine.EvaluateAsync(expression, ct);

        // If author used JSON.stringify → result is a JS string
        if (jsResult.IsString())
            return JsonDocument.Parse(jsResult.AsString()).RootElement.Clone();

        // If author returned a plain object → safe-convert via JsValueToJsonNode + STJ
        var node = JsValueToJsonNode(jsResult);
        var json = node?.ToJsonString(_safeWriterOptions) ?? "null";
        return JsonDocument.Parse(json).RootElement.Clone();
    }
    finally { _enginePool.Return(engine); }
}
```

#### Option B — Jint object graph → `JsonElement` via STJ (no author changes needed)

```csharp
// Helper: walk a JsValue and produce a JsonNode tree — avoids CLR interop surprises
private static JsonNode? JsValueToJsonNode(JsValue value) => value switch
{
    { IsNull: true }      => null,
    { IsUndefined: true } => null,
    { IsBoolean: true }   => JsonValue.Create(value.AsBoolean()),
    { IsNumber: true }    => JsonValue.Create(value.AsNumber()),
    { IsString: true }    => JsonValue.Create(value.AsString()),
    { IsArray: true }     => new JsonArray(
        value.AsArray().Select(v => JsValueToJsonNode(v)).ToArray<JsonNode?>()),
    { IsObject: true }    => new JsonObject(
        value.AsObject().GetOwnProperties()
             .Where(p => !p.Value.Value.IsUndefined())
             .Select(p => new KeyValuePair<string, JsonNode?>(
                 p.Key.AsString(), JsValueToJsonNode(p.Value.Value)))),
    _ => throw new ExpressionRuntimeException("", $"Unsupported JS value type: {value.Type}")
};
```

**Author expression examples:**
```javascript
// Inline object literal
({ status: order.status, total: order.total, premium: order.total > 500 })

// Array of projected items
order.items.map(x => ({ id: x.id, name: x.name }))

// Nested structure
({
  summary: { count: order.items.length, total: order.items.reduce((a,x) => a + x.price, 0) },
  flags: { isPremium: order.total > 500, isGold: order.tier === "gold" }
})
```

> ⚠️ **CopilotNote:** Jint's `.ToObject()` on a plain JS object returns `ExpandoObject` or a custom Jint
> internal type — always marshal through STJ or the `JsValueToJsonNode` helper rather than casting directly
> to a C# type. Casting to `IDictionary<string, object>` works but loses type info (numbers become `double`,
> booleans become `bool` — watch out when feeding into a typed module input)~ 🛡️

---

### Lua (MoonSharp) — Custom Return Wiring 🌙

Lua returns a `DynValue` whose `.Type` is `Table` for structured data. Lua **tables** are the universal data structure — they serve as both arrays and dictionaries. Converting them to `JsonElement` requires a recursive walk since MoonSharp doesn't have a built-in JSON serializer in `HardSandbox` mode.

#### Author-side: returning a table from an expression

```lua
-- Lua table literal → becomes a MoonSharp Table DynValue
return {
    status  = order.Status,
    total   = order.Total,
    premium = order.Total > 500
}

-- Array table (1-indexed in Lua, converts to 0-indexed JSON array)
return { order.Item1, order.Item2, order.Item3 }

-- Mixed nested table
return {
    summary = { count = #order.Items, total = 0 },
    flags   = { isPremium = order.Total > 500 }
}
```

#### .NET side: `DynValue` table → `JsonElement`

```csharp
// Workflow.Engine/Services/MoonSharpExpressionEvaluator.cs
public async ValueTask<JsonElement> EvaluateObjectAsync(
    string expression,
    IReadOnlyDictionary<string, object?> variables,
    CancellationToken ct = default)
{
    ct.ThrowIfCancellationRequested();

    var script = new Script(CoreModules.Preset_HardSandbox);
    foreach (var (key, value) in variables)
        script.Globals[key] = value;

    var wrapped = expression.TrimStart().StartsWith("return ") ? expression : $"return ({expression})";

    var dynResult = await Task.Run(() => script.DoString(wrapped), ct)
        .WaitAsync(_options.Timeout, ct);

    var node = DynValueToJsonNode(dynResult);
    var json = node?.ToJsonString(_safeWriterOptions) ?? "null";
    return JsonDocument.Parse(json).RootElement.Clone();
}

/// <summary>
/// Recursively converts a MoonSharp DynValue to a JsonNode tree~ 🌙
/// Handles: nil, bool, number, string, Table (array and dict), UserData (via STJ).
/// CopilotNote: Tables with mixed integer/string keys are treated as dicts (string keys win).
/// </summary>
private static JsonNode? DynValueToJsonNode(DynValue value) => value.Type switch
{
    DataType.Nil       => null,
    DataType.Void      => null,
    DataType.Boolean   => JsonValue.Create(value.Boolean),
    DataType.Number    => JsonValue.Create(value.Number),
    DataType.String    => JsonValue.Create(value.String),

    DataType.Table     => ConvertTable(value.Table),

    // UserData = a registered C# POCO — round-trip through STJ for safety
    DataType.UserData  => JsonSerializer.SerializeToNode(
                              value.UserData.Object, _safeSerializerOptions),

    _ => throw new ExpressionRuntimeException("",
             $"Lua expression returned an unsupported type: {value.Type}. " +
             "Only nil, bool, number, string, and table are allowed~ 🛡️")
};

private static JsonNode ConvertTable(Table table)
{
    // Detect array table: all keys are sequential integers starting at 1 (Lua convention)
    var pairs = table.Pairs.ToList();
    bool isArray = pairs.Count > 0
        && pairs.All(p => p.Key.Type == DataType.Number)
        && pairs.Select((p, i) => (int)p.Key.Number == i + 1).All(x => x);

    if (isArray)
    {
        var arr = new JsonArray();
        foreach (var pair in pairs)
            arr.Add(DynValueToJsonNode(pair.Value));
        return arr;
    }

    var obj = new JsonObject();
    foreach (var pair in pairs)
    {
        var key = pair.Key.Type == DataType.String
            ? pair.Key.String
            : pair.Key.ToString();   // numeric keys become string keys in JSON
        obj[key] = DynValueToJsonNode(pair.Value);
    }
    return obj;
}
```

> ⚠️ **CopilotNote:** Lua's `json` library is **removed** by `HardSandbox` (`dostring`/`load` gone too).
> Do NOT try to call `json.encode()` inside the expression — it won't be available. Always do the
> serialization on the .NET side using `DynValueToJsonNode` above~ 🌙

---

### Wiring It All Together — `IExpressionEvaluator` Full Registration 🔌

```csharp
// Workflow.Api/Program.cs (or engine bootstrap)

// Safe STJ options shared by all evaluators — registered as singleton
builder.Services.AddSingleton(new JsonSerializerOptions
{
    ReferenceHandler       = ReferenceHandler.IgnoreCycles,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    MaxDepth               = 16,
    PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
});

// v1 — DynamicExpresso (default)
builder.Services.AddSingleton<IExpressionEvaluator, DynamicExpressoEvaluator>();

// Phase 3 opt-in — Jint (register under a named key or feature flag)
builder.Services.AddKeyedSingleton<IExpressionEvaluator, JintExpressionEvaluator>("javascript");

// Phase 3+ opt-in — MoonSharp
builder.Services.AddKeyedSingleton<IExpressionEvaluator, MoonSharpExpressionEvaluator>("lua");

// IExpressionEvaluatorFactory — resolves by engine name from WorkflowDefinition metadata
builder.Services.AddSingleton<IExpressionEvaluatorFactory, KeyedExpressionEvaluatorFactory>();
```

```csharp
/// <summary>
/// Resolves the correct IExpressionEvaluator based on the engine name declared on
/// the workflow definition or individual node metadata~ 🔌
/// CopilotNote: defaults to "csharp" if no engine is declared (backwards compat).
/// </summary>
public sealed class KeyedExpressionEvaluatorFactory(IServiceProvider sp) : IExpressionEvaluatorFactory
{
    private static readonly string[] KnownEngines = ["csharp", "javascript", "lua"];

    public IExpressionEvaluator Resolve(string? engineName)
    {
        engineName = engineName?.ToLowerInvariant() ?? "csharp";
        if (!KnownEngines.Contains(engineName))
            throw new ArgumentException($"Unknown expression engine '{engineName}'. " +
                $"Valid options: {string.Join(", ", KnownEngines)}~ 🛡️");

        return engineName == "csharp"
            ? sp.GetRequiredService<IExpressionEvaluator>()                    // default singleton
            : sp.GetRequiredKeyedService<IExpressionEvaluator>(engineName);    // named registration
    }
}
```

---

### Custom Return Type Safety Comparison 📊

| Concern | DynamicExpresso | Jint (JS) | MoonSharp (Lua) |
|---------|----------------|-----------|-----------------|
| Return a plain object | ✅ Registered DTO or factory func | ✅ JS object literal `{}` | ✅ Lua table `{}` |
| Return an array | ✅ `IEnumerable<T>` via LINQ helper | ✅ `[]` native | ✅ Lua array table |
| Nested objects | ✅ Nested DTOs (via factory func) | ✅ Native JS nesting | ✅ Nested Lua tables |
| Safe serialization path | ✅ STJ round-trip on returned CLR type | ✅ `JsValueToJsonNode` walker or `JSON.stringify` | ✅ `DynValueToJsonNode` recursive walker |
| Author must know serialization? | ❌ Transparent (returns .NET objects) | ⚠️ Aware of `JSON.stringify` option | ✅ Transparent (table is universal) |
| Return type checked at parse time | ✅ Yes (if typed parameters used) | ❌ Runtime only | ❌ Runtime only |
| Private CLR member exposure risk | ✅ Low (only whitelisted types) | ⚠️ Medium (`.ToObject()` can expose) | ✅ Low (`UserData` explicit registration) |
| Max safe depth enforced | ✅ Via STJ `MaxDepth = 16` | ✅ Via STJ `MaxDepth = 16` | ✅ Via STJ `MaxDepth = 16` |

---



- [ ] **AE1**: Should `expressionEngine` be a per-workflow or per-node setting? Per-workflow is simpler for v1; per-node enables mixing (e.g. a JS transform inside a C# condition flow).
- [ ] **AE2**: Should we ship `JintExpressionEvaluator` as a separate NuGet (`Workflow.Engine.Scripting`) to keep Jint out of the core engine bundle for minimal install footprint?
- [ ] **AE3**: Lua ternary footgun (`cond and x or y`) — worth registering a `ternary(cond, t, f)` built-in from day one if we ever enable Lua, to avoid subtle author bugs?
- [ ] **AE4**: For the UI designer (Phase 3-UI), which expression engine would give the best autocomplete / IntelliSense story in a Monaco editor? (JS/Jint wins easily; TS support is free there.)

---

> 💖 **Ami's Expression Engine Tips:**
> - Hide all three behind `IExpressionEvaluator` from day one — swapping later is painless, and you'll want to once the UI team starts asking for JS autocomplete~ 🔌
> - **Always project POCO inputs to safe DTO shapes** before handing to any evaluator — don't inject services, repositories, or anything with side effects into the expression context~ 🛡️
> - Cache parsed expressions (DynamicExpresso compiled lambdas, Jint parsed ASTs) keyed by expression string — re-parsing on every loop iteration will hurt~ 🐇
> - Log the expression source string + input variables on every `ExpressionRuntimeException` — expression debugging without that context is a nightmare~ 🔍 UwU


