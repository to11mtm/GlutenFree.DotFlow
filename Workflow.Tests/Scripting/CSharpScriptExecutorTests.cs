// <copyright file="CSharpScriptExecutorTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Scripting;

using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Workflow.Scripting.Abstractions;
using Workflow.Scripting.Roslyn.Compilation;
using Workflow.Scripting.Roslyn.Execution;
using Workflow.Scripting.Roslyn.Executors;
using Xunit;

/// <summary>
/// 🟪 Phase 3.1.2 — Tests for the Roslyn-backed C# script executor~ ✨.
/// </summary>
public sealed class CSharpScriptExecutorTests
{
    private static CSharpScriptExecutor NewExecutor(out CollectibleScriptRunner runner)
    {
        runner = new CollectibleScriptRunner();
        return new CSharpScriptExecutor(new RoslynScriptCompiler(), runner);
    }

    [Fact]
    public async Task Execute_SimpleScript_ReturnsValue()
    {
        var executor = NewExecutor(out var runner);
        using (runner)
        {
            var (context, _) = ScriptTestHarness.BuildContext();
            var result = await executor.ExecuteAsync("return 2 + 3;", context);

            result.Success.Should().BeTrue(result.Error);
            result.ReturnValue.Should().Be(5);
        }
    }

    [Fact]
    public async Task Execute_InputAccessible()
    {
        var executor = NewExecutor(out var runner);
        using (runner)
        {
            var (context, _) = ScriptTestHarness.BuildContext(
                inputs: new Dictionary<string, object?> { ["name"] = "Ami" });
            var result = await executor.ExecuteAsync("return input[\"name\"];", context);

            result.Success.Should().BeTrue(result.Error);
            result.ReturnValue.Should().Be("Ami");
        }
    }

    [Fact]
    public async Task Execute_WorkflowApi_Callable()
    {
        var executor = NewExecutor(out var runner);
        using (runner)
        {
            var (context, _) = ScriptTestHarness.BuildContext();
            var result = await executor.ExecuteAsync(
                "workflow.SetVariable(\"x\", 7); workflow.LogInfo(\"hi\"); return workflow.NewGuid();",
                context);

            result.Success.Should().BeTrue(result.Error);
            result.VariableUpdates.Should().ContainKey("x").WhoseValue.Should().Be(7);
            result.Logs.Should().Contain(l => l.Message == "hi");
        }
    }

    [Fact]
    public async Task Execute_ForbiddenSyntax_Rejected()
    {
        var executor = NewExecutor(out var runner);
        using (runner)
        {
            var (context, _) = ScriptTestHarness.BuildContext();
            var result = await executor.ExecuteAsync("System.IO.File.ReadAllText(\"/etc/passwd\"); return 1;", context);

            result.Success.Should().BeFalse();
            result.Error.Should().Contain("compile failed");
        }
    }

    [Fact]
    public async Task Execute_CompileError_StructuredDiagnostics()
    {
        var executor = NewExecutor(out var runner);
        using (runner)
        {
            var (context, _) = ScriptTestHarness.BuildContext();
            var result = await executor.ExecuteAsync("return notAThing +;", context);

            result.Success.Should().BeFalse();
            result.Error.Should().Contain("compile failed");
        }
    }

    [Fact]
    public async Task Execute_Async_Awaited()
    {
        var executor = NewExecutor(out var runner);
        using (runner)
        {
            var (context, _) = ScriptTestHarness.BuildContext();
            var result = await executor.ExecuteAsync("await Task.Delay(1, ct); return 99;", context);

            result.Success.Should().BeTrue(result.Error);
            result.ReturnValue.Should().Be(99);
        }
    }

    [Fact]
    public async Task Cache_SecondRun_UsesCompiled()
    {
        var executor = NewExecutor(out var runner);
        using (runner)
        {
            var (c1, _) = ScriptTestHarness.BuildContext();
            var (c2, _) = ScriptTestHarness.BuildContext();

            (await executor.ExecuteAsync("return 42;", c1)).ReturnValue.Should().Be(42);
            (await executor.ExecuteAsync("return 42;", c2)).ReturnValue.Should().Be(42);

            // Only one distinct assembly should be loaded for the identical body~
            runner.LoadedAssemblyCount.Should().Be(1);
        }
    }

    [Fact]
    public async Task Execute_Timeout_Enforced()
    {
        var executor = NewExecutor(out var runner);
        using (runner)
        {
            var config = ScriptExecutionConfig.Default with { TimeoutSeconds = 1 };
            var (context, _) = ScriptTestHarness.BuildContext(config: config);
            var result = await executor.ExecuteAsync("while(!ct.IsCancellationRequested){} ct.ThrowIfCancellationRequested(); return 1;", context);

            result.Success.Should().BeFalse();
            result.Error.Should().Contain("timed out");
        }
    }
}
