// <copyright file="JavaScriptScriptExecutorTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Scripting;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Workflow.Scripting.Abstractions;
using Workflow.Scripting.Executors;
using Xunit;

/// <summary>
/// 🟨 Phase 3.1.0 — Tests for the Jint-based JavaScript executor~ ✨.
/// </summary>
public sealed class JavaScriptScriptExecutorTests
{
    private readonly JavaScriptScriptExecutor executor = new();

    [Fact]
    public async Task Execute_SimpleScript_ReturnsValue()
    {
        var (context, _) = ScriptTestHarness.BuildContext();
        var result = await this.executor.ExecuteAsync("return 2 + 3;", context);

        result.Success.Should().BeTrue(result.Error);
        result.ReturnValue.Should().Be(5);
    }

    [Fact]
    public async Task Execute_InputData_Accessible()
    {
        var (context, _) = ScriptTestHarness.BuildContext(
            inputs: new Dictionary<string, object?> { ["name"] = "Ami", ["count"] = 3 });

        var result = await this.executor.ExecuteAsync("return input.name + '-' + input.count;", context);

        result.Success.Should().BeTrue(result.Error);
        result.ReturnValue.Should().Be("Ami-3");
    }

    [Fact]
    public async Task Execute_ObjectReturn_MarshalsToNet()
    {
        var (context, _) = ScriptTestHarness.BuildContext();
        var result = await this.executor.ExecuteAsync("return { a: 1, b: [2, 3], c: 'x' };", context);

        result.Success.Should().BeTrue(result.Error);
        var dict = result.ReturnValue.Should().BeAssignableTo<IReadOnlyDictionary<string, object?>>().Subject;
        dict["a"].Should().Be(1);
        dict["c"].Should().Be("x");
    }

    [Fact]
    public async Task Execute_SyntaxError_StructuredError()
    {
        var (context, _) = ScriptTestHarness.BuildContext();
        var result = await this.executor.ExecuteAsync("return (((;", context);

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Execute_RuntimeError_StructuredError()
    {
        var (context, _) = ScriptTestHarness.BuildContext();
        var result = await this.executor.ExecuteAsync("throw new Error('boom');", context);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("boom");
    }

    [Fact]
    public async Task Execute_Timeout_Enforced()
    {
        var config = ScriptExecutionConfig.Default with { TimeoutSeconds = 1 };
        var (context, _) = ScriptTestHarness.BuildContext(config: config);

        var result = await this.executor.ExecuteAsync("while(true){}", context);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("timed out");
    }

    [Fact]
    public async Task Execute_Variables_StagedInResult()
    {
        var (context, _) = ScriptTestHarness.BuildContext(
            variables: new Dictionary<string, object?> { ["existing"] = 10 });

        var result = await this.executor.ExecuteAsync(
            "workflow.setVariable('greeting', 'hi'); return workflow.getVariable('existing');",
            context);

        result.Success.Should().BeTrue(result.Error);
        result.ReturnValue.Should().Be(10);
        result.VariableUpdates.Should().ContainKey("greeting");
        result.VariableUpdates["greeting"].Should().Be("hi");
    }

    [Fact]
    public async Task Execute_Logging_Captured()
    {
        var (context, _) = ScriptTestHarness.BuildContext();
        var result = await this.executor.ExecuteAsync(
            "workflow.logInfo('hello'); workflow.logError('bad'); return true;",
            context);

        result.Success.Should().BeTrue(result.Error);
        result.Logs.Should().Contain(l => l.Level == "info" && l.Message == "hello");
        result.Logs.Should().Contain(l => l.Level == "error" && l.Message == "bad");
    }

    [Fact]
    public async Task Execute_Utilities_Work()
    {
        var (context, _) = ScriptTestHarness.BuildContext();
        var result = await this.executor.ExecuteAsync(
            "return workflow.base64Encode('abc');",
            context);

        result.Success.Should().BeTrue(result.Error);
        result.ReturnValue.Should().Be("YWJj");
    }

    [Fact]
    public async Task Execute_HttpBlocked_WhenNetworkDisallowed()
    {
        var (context, _) = ScriptTestHarness.BuildContext();
        var result = await this.executor.ExecuteAsync(
            "return await workflow.httpGet('http://example.com');",
            context);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Network access is disabled");
    }

    [Fact]
    public async Task Execute_Async_Awaited()
    {
        var (context, _) = ScriptTestHarness.BuildContext();
        var result = await this.executor.ExecuteAsync(
            "await workflow.wait(1); return 'done';",
            context);

        result.Success.Should().BeTrue(result.Error);
        result.ReturnValue.Should().Be("done");
    }

    [Fact]
    public void Config_Clamp_NodeCannotExceedHostCeilings()
    {
        var ceilings = new ScriptHostCeilings { MaxTimeoutSeconds = 10, AllowNetwork = false };
        var requested = ScriptExecutionConfig.Default with { TimeoutSeconds = 999, AllowNetwork = true };

        var clamped = requested.ClampTo(ceilings);

        clamped.TimeoutSeconds.Should().Be(10);
        clamped.AllowNetwork.Should().BeFalse("host disallows network");
    }

    [Fact]
    public async Task Execute_CancellationToken_Honoured()
    {
        var (context, _) = ScriptTestHarness.BuildContext();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await this.executor.ExecuteAsync("return 1;", context, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
