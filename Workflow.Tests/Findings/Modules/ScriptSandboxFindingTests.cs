// <copyright file="ScriptSandboxFindingTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Findings.Modules;

using System.Threading.Tasks;
using FluentAssertions;
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
