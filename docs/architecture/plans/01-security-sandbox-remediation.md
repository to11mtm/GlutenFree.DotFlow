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

### Decided design (2026-10-11) — default-deny semantic check + reviewer approval

**Option B as written doesn't work.** In the .NET 8 reference pack, `System.Type`,
`System.Reflection.MethodInfo`, `Assembly`, `Activator` and `System.IO.File` are all defined in
`System.Runtime.dll`, which no compilation can drop. Reflection by string therefore reaches anything
at runtime whatever the reference set. Trimming only removes types in separate assemblies
(`Process`, `HttpClient`) and only at compile time.

Agreed instead (one change, both compilers):

1. **Semantic check (default-deny).** After binding, walk the compilation's `SemanticModel` over the
   *user body* and flag every resolved symbol in a sensitive category — reflection
   (`System.Reflection*`, `Type.GetType(string)` / `GetMethod*` / `InvokeMember` / … , `Activator`),
   file system (`System.IO` file/dir/stream types), network (`System.Net*`), process/environment
   (`System.Diagnostics.Process`, `Environment`, `AppDomain`), interop/memory
   (`System.Runtime.InteropServices`, `Marshal`, `NativeMemory`, `Unsafe`), assembly loading
   (`System.Runtime.Loader`, `Assembly.Load*`), dynamic code (`System.Linq.Expressions.Expression.Compile`,
   `System.Reflection.Emit`), and `dynamic`. Robust to spelling: it checks what the code *binds to*,
   not identifiers. The existing name walkers stay as a pre-filter but their hits become **flags**
   too (not hard errors), so everything is reviewable. `unsafe`/pointers/`stackalloc` stay hard
   errors only because the compilation sets `allowUnsafe: false`.
2. **Every flag carries a danger explanation** (per category: what an attacker could do with it)
   shown in diagnostics, the API and the UI.
3. **Approval of exact code.** A flagged script compiles only if an approval exists for its content
   hash (SHA-256 of the normalized user body + language/kind). Any edit changes the hash → needs
   re-review.
4. **Who:** new `ScriptApprover` role + `ScriptApprove` policy (Admins included). Authors request
   approval; **an author can't approve their own request**.
5. **Store:** `IScriptApprovalStore` (pending / approved / rejected records: hash, kind, code,
   flagged symbols + explanations, requestedBy/At, decidedBy/At, note), persisted as a JSON catalog
   over the blob store like `PersistedScriptLibraryStore` — one implementation for every provider.
6. **Enforcement at compile time** in both `RoslynScriptCompiler` (scripts + transforms) and
   `WorkflowLinqCompiler`: unapproved flags → compile fails with `WFSEC2xx` diagnostics listing each
   symbol + explanation. Shared checker/explanations live in `Workflow.Scripting.Roslyn`
   (`Workflow.Modules.Database.Linq` gains a reference to it); the store abstraction lives in
   `Workflow.Scripting` (no Roslyn), so the API can use it.
7. **API:** no separate "analyze" call — a denied compile *records* the exact analyzed code as a
   `Candidate` (so what gets reviewed is exactly what runs, including prepended script libraries),
   and the existing compile responses (`POST /scripts/test`, transform validate/compile, LINQ
   validate/compile) return `securityFlags` + `codeHash`. Review endpoints under
   `/api/v1/script-approvals`: `GET ?status=`, `GET /categories` (danger legend), `GET /{hash}`,
   `POST /{hash}/request` (WorkflowWrite), `POST /{hash}/approve|reject` (`ScriptApprove`:
   Admin + ScriptApprover). Reads use `ScriptApprovalRead` so approvers who aren't developers can
   see the queue. Outcomes: 404 unknown hash, 409 wrong state, 403 self-approval.
8. **UI:** Script Studio's test runner and LINQ Studio's Validate show a review panel (each
   category's danger once, the symbols + lines under it, status, note + "Request approval"); a
   Script Approvals page (`/script-approvals`, 🛡️ Approvals in the top bar) shows the exact code,
   the author's note and the dangers, with approve/reject + a note. The designer has no inline
   compile, so a node's script is reviewed via Script/LINQ Studio or by running it.

**Implemented 2026-10-11 (§9.2).** Caveat: approvals are never revoked in v1, and
`CSharpScriptExecutor` caches compiled bytes by code, so an approval takes effect immediately but
"un-approving" would need a cache flush — there's no un-approve action for that reason.

Acceptance adds: the reflection guard green for **both** compilers; IO / Net / Process-by-reflection
negative tests; approved hash compiles, edited code doesn't; self-approval rejected; role/policy
tests; existing script / transform / LINQ suites green.

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
