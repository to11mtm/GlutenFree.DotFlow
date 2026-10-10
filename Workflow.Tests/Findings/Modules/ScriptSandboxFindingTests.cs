// <copyright file="ScriptSandboxFindingTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Findings.Modules;

using System.Threading.Tasks;
using FluentAssertions;
using Workflow.Scripting.Executors;
using Workflow.Scripting.Roslyn.Compilation;
using Workflow.Scripting.Roslyn.Execution;
using Workflow.Scripting.Roslyn.Executors;
using Workflow.Tests.Scripting;
using Xunit;

/// <summary>
/// Defensive checks for the script-sandbox findings in
/// <c>docs/architecture/04-modules-plugins-and-scripting.md §9.1–9.2</c>.
/// </summary>
/// <remarks>
/// These verify that user scripts cannot reach the .NET runtime past the intended capability gates.
/// They use only benign, read-only probes (a type lookup and a <c>Math.Max</c> call) and perform no
/// file, network, or process access. Each asserts the sandbox SHOULD block the reach, so a failure
/// means the reach is currently possible — that is the finding. Safe to run.
/// </remarks>
[Trait("Category", FindingDefs.Category)]
public sealed class ScriptSandboxFindingTests
{
    private readonly JavaScriptScriptExecutor js = new();

    // ── Finding 04 §9.1 — the JavaScript engine enables CLR interop ──────────────────────────────

    /// <summary>
    /// A user script should not see any CLR namespace root. With CLR interop enabled, the
    /// <c>System</c> namespace becomes reachable from script. Benign probe: <c>typeof System</c>.
    /// </summary>
    [Fact]
    public async Task JavaScript_ShouldNotExpose_ClrNamespaceRoot()
    {
        var (context, _) = ScriptTestHarness.BuildContext();

        var result = await this.js.ExecuteAsync("return (typeof System === 'undefined');", context);

        result.Success.Should().BeTrue(result.Error);
        result.ReturnValue.Should().Be(
            true,
            because: "a sandboxed script should not have the CLR 'System' namespace in scope (04 §9.1)");
    }

    /// <summary>
    /// A benign CLR static call from script should NOT succeed. If it returns a value, the runtime
    /// is reachable and the network/file-system capability gates can be bypassed. Probe:
    /// <c>System.Math.Max(1, 2)</c> — no I/O.
    /// </summary>
    [Fact]
    public async Task JavaScript_ShouldNotInvoke_ClrStaticMethods()
    {
        var (context, _) = ScriptTestHarness.BuildContext();

        var result = await this.js.ExecuteAsync("return System.Math.Max(1, 2);", context);

        result.Success.Should().BeFalse(
            "reaching a CLR static from script means interop is on and the capability gates can be bypassed (04 §9.1)");
    }

    // ── Finding 04 §9.2 — the C# blocklist can be bypassed via reflection ─────────────────────────

    /// <summary>
    /// The C# script blocklist rejects certain identifiers (e.g. <c>File</c>) but references the
    /// whole BCL, so reflection that never names a banned identifier compiles and runs. Benign
    /// probe: resolve the <c>StringBuilder</c> type by name — no I/O. The sandbox SHOULD reject
    /// reflection-based type resolution.
    /// </summary>
    [Fact]
    public async Task CSharp_ShouldNotAllow_ReflectionTypeResolution()
    {
        var runner = new CollectibleScriptRunner();
        using (runner)
        {
            var executor = new CSharpScriptExecutor(new RoslynScriptCompiler(), runner);
            var (context, _) = ScriptTestHarness.BuildContext();

            // Resolves a benign type via reflection without naming any blocked identifier.
            var result = await executor.ExecuteAsync(
                "var t = System.Type.GetType(\"System.Text.StringBuilder\"); return t != null;",
                context);

            result.Success.Should().BeFalse(
                "reflection reaching arbitrary types bypasses the identifier blocklist, so it should be rejected (04 §9.2)");
        }
    }
}
