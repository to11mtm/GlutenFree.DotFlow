// <copyright file="LuaScriptExecutorTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Scripting;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Workflow.Scripting.Abstractions;
using Workflow.Scripting.Lua;
using Xunit;

/// <summary>
/// 🌙 Phase 3.1.3 — Tests for the MoonSharp-based Lua executor~ ✨.
/// </summary>
public sealed class LuaScriptExecutorTests
{
    private readonly LuaScriptExecutor executor = new();

    [Fact]
    public async Task Execute_SimpleScript_ReturnsValue()
    {
        var (context, _) = ScriptTestHarness.BuildContext();
        var result = await this.executor.ExecuteAsync("return 2 + 3", context);

        result.Success.Should().BeTrue(result.Error);
        result.ReturnValue.Should().Be(5);
    }

    [Fact]
    public async Task Execute_InputTable_Accessible()
    {
        var (context, _) = ScriptTestHarness.BuildContext(
            inputs: new Dictionary<string, object?> { ["name"] = "Ami", ["count"] = 3 });
        var result = await this.executor.ExecuteAsync("return input.name .. '-' .. input.count", context);

        result.Success.Should().BeTrue(result.Error);
        result.ReturnValue.Should().Be("Ami-3");
    }

    [Fact]
    public async Task Execute_TableReturn_MarshalsToNet()
    {
        var (context, _) = ScriptTestHarness.BuildContext();
        var result = await this.executor.ExecuteAsync("return { a = 1, b = 'x' }", context);

        result.Success.Should().BeTrue(result.Error);
        var dict = result.ReturnValue.Should().BeAssignableTo<IReadOnlyDictionary<string, object?>>().Subject;
        dict["a"].Should().Be(1);
        dict["b"].Should().Be("x");
    }

    [Fact]
    public async Task Execute_ArrayReturn_MarshalsToList()
    {
        var (context, _) = ScriptTestHarness.BuildContext();
        var result = await this.executor.ExecuteAsync("return { 10, 20, 30 }", context);

        result.Success.Should().BeTrue(result.Error);
        var list = result.ReturnValue.Should().BeAssignableTo<IReadOnlyList<object?>>().Subject;
        list.Should().ContainInOrder(10, 20, 30);
    }

    [Fact]
    public async Task Execute_ApiCalls_Work()
    {
        var (context, _) = ScriptTestHarness.BuildContext(
            variables: new Dictionary<string, object?> { ["seed"] = 5 });
        var result = await this.executor.ExecuteAsync(
            "workflow.setVariable('out', 'ok'); workflow.logInfo('from lua'); return workflow.getVariable('seed')",
            context);

        result.Success.Should().BeTrue(result.Error);
        result.ReturnValue.Should().Be(5);
        result.VariableUpdates.Should().ContainKey("out").WhoseValue.Should().Be("ok");
        result.Logs.Should().Contain(l => l.Message == "from lua");
    }

    [Fact]
    public async Task Execute_SyntaxError_StructuredError()
    {
        var (context, _) = ScriptTestHarness.BuildContext();
        var result = await this.executor.ExecuteAsync("return ((", context);

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Execute_RuntimeError_StructuredError()
    {
        var (context, _) = ScriptTestHarness.BuildContext();
        var result = await this.executor.ExecuteAsync("error('boom')", context);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("boom");
    }

    [Fact]
    public async Task Execute_Timeout_Enforced()
    {
        var config = ScriptExecutionConfig.Default with { TimeoutSeconds = 1 };
        var (context, _) = ScriptTestHarness.BuildContext(config: config);
        var result = await this.executor.ExecuteAsync("while true do end", context);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("timed out");
    }

    [Fact]
    public async Task Sandbox_NoIoOsExecute()
    {
        var (context, _) = ScriptTestHarness.BuildContext();
        var result = await this.executor.ExecuteAsync("return os.execute ~= nil", context);

        // In the soft sandbox, os.execute is not available~
        result.Success.Should().BeTrue(result.Error);
        result.ReturnValue.Should().Be(false);
    }

    [Fact]
    public async Task Http_Gated_FromLua()
    {
        var (context, _) = ScriptTestHarness.BuildContext();
        var result = await this.executor.ExecuteAsync("return workflow.httpGet('http://example.com', nil)", context);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Network access is disabled");
    }

    [Fact]
    public async Task Execute_PureLuaCoroutines_Work()
    {
        var (context, _) = ScriptTestHarness.BuildContext();
        var result = await this.executor.ExecuteAsync(
            @"local co = coroutine.create(function() coroutine.yield(1); return 2 end)
              local ok1, v1 = coroutine.resume(co)
              local ok2, v2 = coroutine.resume(co)
              return v1 + v2",
            context);

        result.Success.Should().BeTrue(result.Error);
        result.ReturnValue.Should().Be(3);
    }
}
