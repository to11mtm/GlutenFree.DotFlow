# Plan 01 — Script sandbox remediation (security)

> Fix plan for the two **High**-severity sandbox findings confirmed by
> `Workflow.Tests/Findings/Modules/ScriptSandboxFindingTests.cs` on 2026-09-29. Both let user
> **script** code reach the .NET runtime past the capability gates, in-process, at the API's trust
> level. Source: `Workflow.Scripting/Executors/JavaScriptScriptExecutor.cs`,
> `Workflow.Scripting.Roslyn/Compilation/ForbiddenSyntaxWalker.cs`,
> `Workflow.Modules.Database.Linq/Compilation/ForbiddenSyntaxWalker.cs`.
>
> Up: [architecture findings](../README.md#findings-at-a-glance) ·
> [04 — Modules, plugins & scripting §9](../04-modules-plugins-and-scripting.md#9-findings-to-verify)

**Status:** proposed. Nothing here is built yet.

**Threat model in one line:** a workflow author (or anyone who can submit a `builtin.script` node or a
typed C# / LINQ node) is treated as *untrusted* by the `allowNetwork` / `allowFileSystem` gates and
the identifier blocklist. Both findings show that boundary is porous, so today those features are
only safe when every script author is already trusted with full process access.

---

## Finding A — JavaScript CLR interop is on (§9.1)

### Evidence
- `JavaScriptScriptExecutor.Run` builds the Jint engine with `opts.AllowClr();` (and
  `opts.CatchClrExceptions();`), `Workflow.Scripting/Executors/JavaScriptScriptExecutor.cs` ~line 95.
- `ScriptSandboxFindingTests`:
  - `JavaScript_ShouldNotExpose_ClrNamespaceRoot` → `typeof System` is `"object"`, not `"undefined"`.
  - `JavaScript_ShouldNotInvoke_ClrStaticMethods` → `System.Math.Max(1, 2)` returns `2`.
- The `allowNetwork` / `allowFileSystem` flags only gate the `workflow.*` API
  (`WorkflowScriptApi.SendAsync`, the file methods). They do nothing about direct CLR reach.

### Root cause
`AllowClr()` enables Jint's CLR interop broadly: the `System` namespace root becomes reachable from
script, along with `GetType` / namespace traversal. Once script can name `System.*`, it can reach
`System.IO`, `System.Net`, `System.Diagnostics.Process`, etc. directly — the `workflow.*` gates are
beside the point.

### Why `AllowClr()` is probably not needed
The only CLR object the script must talk to is `__api` (the `WorkflowScriptApi` facade), and it is
exposed with `engine.SetValue("__api", context.Api)`. In Jint, member calls on an **explicitly
exposed** object (an `ObjectWrapper`) are governed by `Options.Interop`, not by `AllowClr()`.
`AllowClr()` is what additionally exposes arbitrary CLR **type/namespace** access by name. So the
expectation is that `workflow.*` keeps working without it. **This must be verified**, not assumed —
see the acceptance checks.

### Options

| Option | Change | Pro | Con |
| --- | --- | --- | --- |
| **A. Drop `AllowClr()`** (recommended) | remove the call; keep `SetValue("__api", …)` | smallest change; removes the namespace-reach vector | must confirm `workflow.*` still binds under Jint 4.9 |
| B. Keep interop, lock it down | keep `AllowClr()` but set `Interop.AllowGetType = false`, pass **no** assemblies, and set an allow-list `Interop.TypeResolver`/`MemberFilter` | keeps the door open for a future need | larger surface to reason about; easy to mis-configure; still a denylist mindset |
| C. Replace the engine boundary | run scripts out-of-process / in a locked AppDomain-equivalent | strongest isolation | large; .NET has no full in-proc sandbox, so this means process isolation |

### Recommendation
**Option A now**, with a defensive belt: after removing `AllowClr()`, also set
`opts.Interop.AllowGetType = false;` so that even if a wrapped object exposes `GetType()`, it can't be
used to walk to arbitrary types. Keep `CatchClrExceptions()` (it only affects error surfacing).

### Acceptance checks
1. `ScriptSandboxFindingTests.JavaScript_ShouldNotExpose_ClrNamespaceRoot` and
   `…_ShouldNotInvoke_ClrStaticMethods` both go **green**.
2. The whole existing `Workflow.Tests/Scripting/JavaScriptScriptExecutorTests.cs` and
   `WorkflowScriptApiTests.cs` suites still pass — this is the proof that `workflow.*`
   (variables, logging, http-gated, utilities, `require`) is unaffected.
3. Add one positive test: a script calling `workflow.getVariable` / `workflow.setVariable` /
   `workflow.newGuid()` still works. (If it regresses, fall back to Option B.)

### Risk / blast radius
Low code change, but it touches every JS script and every `{{…}}` expression path that shares the
engine. The regression risk is entirely "did we accidentally break `workflow.*`" — covered by check 2.

**Effort:** ~0.5 day including verification.

---

## Finding B — the C# blocklist is bypassable by reflection (§9.2)

### Evidence
- `ForbiddenSyntaxWalker.BannedIdentifiers` (both copies) rejects `File`, `Process`, `Activator`,
  `Assembly`, `Socket`, `HttpClient`, … but **not** `Type`, `GetType`, `GetMethod`, `Invoke`,
  `MethodInfo`.
- The compilation references the full .NET 8 BCL
  (`Basic.Reference.Assemblies.Net80.References.All` in `RoslynScriptCompiler`, and linq2db + BCL in
  `WorkflowLinqCompiler`).
- `ScriptSandboxFindingTests.CSharp_ShouldNotAllow_ReflectionTypeResolution` →
  `System.Type.GetType("System.Text.StringBuilder")` compiles and returns non-null. Nothing named a
  banned identifier, so the walker never sees it.

### Root cause
The walker is a **syntactic denylist over leaf identifiers**. Reflection reaches any type by string,
so it never trips a name-based check, and the reference set makes the whole BCL available at runtime.
A denylist of names cannot bound a language with reflection and a full BCL.

### Options

| Option | Change | Pro | Con |
| --- | --- | --- | --- |
| **A. Extend the denylist** (stop-gap) | add `Type`, `GetType`, `GetMethod`, `GetMethods`, `InvokeMember`, `Invoke`, `MethodInfo`, `PropertyInfo`, `FieldInfo`, `MethodBase`, `Reflection`, `typeof`-of-dynamic patterns to `BannedIdentifiers` in **both** walkers | cheap; closes the demonstrated vector today | still a denylist; other reflection spellings and `dynamic` remain; brittle |
| **B. Reference allowlist** (recommended real fix) | build the `CSharpCompilation` from a **curated** reference set (the types the LINQ/transform surface actually needs: `System.Linq`, `System.Collections.Generic`, primitives, linq2db context) instead of `Net80.References.All`; drop `System.Reflection`, `System.IO`, `System.Net`, `System.Diagnostics` from the metadata references | script can't bind to what isn't referenced; robust against novel spellings | needs care to find the minimal set; some legitimate helpers may need explicit adds |
| C. Trust boundary | declare typed-C# / LINQ authoring an **Admin-only**, trusted-author feature and gate the endpoints behind the Admin policy | honest about what in-proc C# is | removes self-service scripting for non-admins |

### Recommendation
**B as the fix, A as the immediate mitigation** shipped together: extend the denylist now (one commit,
guards the known vector), and in the same plan trim the reference set so reflection has nothing
dangerous to bind to. Apply both to **both** walkers/compilers (Scripting.Roslyn and Database.Linq —
they are separate copies; a fix to one is not a fix to the other). Consider C as policy on top for
defence in depth.

### Acceptance checks
1. `CSharp_ShouldNotAllow_ReflectionTypeResolution` goes green for the shared Roslyn path.
2. Add the mirror test for the **LINQ** compiler (`WorkflowLinqCompiler` / its `ForbiddenSyntaxWalker`)
   — the finding applies there too and currently has no guard.
3. Existing `Workflow.Tests/Scripting/CSharpScriptExecutorTests.cs`,
   `ScriptingCoreTests.cs`, `Modules/TransformScript/*`, and `Modules/DatabaseLinq/*` still pass, so a
   legitimate LINQ query / transform still compiles.
4. A negative test each for `System.IO`, `System.Net`, `System.Diagnostics.Process` reached by
   reflection — all rejected.

### Risk / blast radius
Option A is low-risk (only adds rejections). Option B is the sensitive one: trimming references can
break a legitimate query that used an incidental BCL type. Mitigate by growing the allowlist from the
existing passing tests and any real workflows before enabling.

**Effort:** A ≈ 0.5 day. B ≈ 2–4 days including finding the minimal reference set and the mirror tests.

---

## Sequencing

1. **Ship the two denylist/`AllowClr` mitigations first** (Finding A option A; Finding B option A) —
   both are small and close the demonstrated vectors. One PR, guarded by the finding tests.
2. **Then the reference allowlist** (Finding B option B) as its own PR with the expanded test matrix.
3. **Policy review**: decide whether typed-C# / LINQ authoring should also be Admin-gated, and whether
   `builtin.script` HTTP needs the host allowlist from the Medium finding (04 §9.6) — tracked
   separately.

## Related, not in this plan
- 04 §9.6 script HTTP has no host allowlist (SSRF) — Medium; a `ScriptHostCeilings` allowlist.
- 04 §9.5 strong-name tokens aren't authorship — Medium; Authenticode is the real fix (2.8.P2).

Both belong in a later "script/plugin trust" plan once the two High items above land.
