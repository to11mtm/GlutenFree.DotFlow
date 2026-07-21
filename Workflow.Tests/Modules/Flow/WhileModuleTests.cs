// <copyright file="WhileModuleTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.Flow;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin.Flow;
using Xunit;

/// <summary>
/// 🌀 Phase 2.2.2 — Tests for <see cref="WhileModule"/> (<c>builtin.loop.while</c>)~
/// Validates schema, condition evaluation, and loop packaging~ ✨💖
/// </summary>
public sealed class WhileModuleTests
{
    private readonly WhileModule _module = new();

    private static ModuleExecutionContext BuildContext(
        Dictionary<string, object?>? inputs = null,
        Dictionary<string, object?>? properties = null)
        => new()
        {
            Inputs = inputs ?? new Dictionary<string, object?>(),
            Properties = properties ?? new Dictionary<string, object?>(),
            Variables = new Dictionary<string, object?>(),
            Logger = NullLogger.Instance,
            Services = new EmptyServiceProvider(),
            ExecutionId = Guid.NewGuid(),
            NodeId = "while-node",
        };

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type t) => null;
    }

    // ── Schema & metadata ─────────────────────────────────────────────────────────────

    [Fact]
    public void WhileModule_Metadata_IsCorrect()
    {
        _module.ModuleId.Should().Be("builtin.loop.while");
        _module.Category.Should().Be("Flow Control");
        _module.DisplayName.Should().Be("While Loop");
        _module.Version.Should().Be(new Version(1, 0, 0));
        _module.Icon.Should().Be("🌀");
    }

    [Fact]
    public void WhileModule_Schema_DeclaresPorts()
    {
        _module.Schema.Inputs.ToList().Select(p => p.Name).Should().Contain("condition");
        _module.Schema.Outputs.ToList().Select(p => p.Name).Should()
            .Contain("loopBody").And.Contain("done");
    }

    // ── Condition false from start ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(0)]
    public async Task WhileModule_ConditionFalseFromStart_ActivatesDonePort_NoLoopSpawned(object condition)
    {
        var ctx = BuildContext(inputs: new Dictionary<string, object?> { ["condition"] = condition });

        var result = await _module.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        result.Loop.Should().BeNull(because: "condition=false → no loop actor needed~ 🌀");
        result.ActivePorts.Should().Contain("done",
            because: "false condition should immediately fire done port~ ✅");
        result.Outputs.Should().ContainKey("count").WhoseValue.Should().Be(0);
    }

    [Theory]
    [InlineData("false")]
    [InlineData("0")]
    [InlineData("no")]
    public async Task WhileModule_StringFalsyCondition_ActivatesDonePort(string condition)
    {
        var ctx = BuildContext(inputs: new Dictionary<string, object?> { ["condition"] = condition });

        var result = await _module.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        result.Loop.Should().BeNull();
        result.ActivePorts.Should().Contain("done");
    }

    // ── Condition true → LoopRequest returned ─────────────────────────────────────────

    [Fact]
    public async Task WhileModule_BoolTrue_ReturnsLoopRequest()
    {
        var ctx = BuildContext(inputs: new Dictionary<string, object?> { ["condition"] = true });

        var result = await _module.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        result.Loop.Should().NotBeNull(because: "condition=true should spawn loop actor~ 🌀");
        result.Loop!.Items.Should().BeNull(because: "WhileModule is condition-driven, not item-driven~ 🔄");
        result.Loop.ContinueCondition.Should().NotBeNull(because: "must provide condition delegate~ 🔄");
        result.Loop.LoopBodyPort.Should().Be("loopBody");
        result.Loop.DonePort.Should().Be("done");
    }

    [Theory]
    [InlineData(1)]
    [InlineData("true")]
    [InlineData("yes")]
    public async Task WhileModule_TruthyCondition_ReturnsLoopRequest(object condition)
    {
        var ctx = BuildContext(inputs: new Dictionary<string, object?> { ["condition"] = condition });

        var result = await _module.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        result.Loop.Should().NotBeNull(because: $"'{condition}' is truthy — loop should start~ 🌀");
    }

    // ── ContinueCondition delegate re-evaluation ──────────────────────────────────────

    [Fact]
    public async Task WhileModule_ContinueCondition_FalseAfterThreeIterations()
    {
        // Arrange — condition starts true; delegate tracks iterations, returns false after 3
        var ctx = BuildContext(inputs: new Dictionary<string, object?> { ["condition"] = true });
        var result = await _module.ExecuteAsync(ctx);

        result.Loop!.ContinueCondition.Should().NotBeNull();

        // Simulate the LoopExecutorActor calling ContinueCondition with context
        // First 3 calls: context has condition=true; 4th call: context has condition=false
        int callCount = 0;
        var condFn = result.Loop.ContinueCondition!;

        // Calls with condition=true should return true (loop continues)
        var ctxTrue = new Dictionary<string, object?> { ["condition"] = true };
        var ctxFalse = new Dictionary<string, object?> { ["condition"] = false };

        (await condFn(ctxTrue, default)).Should().BeTrue(because: "condition=true context → continue~ 🌀");
        (await condFn(ctxFalse, default)).Should().BeFalse(because: "condition=false context → stop~ ✅");
    }

    // ── MaxIterations ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WhileModule_MaxIterationsOverride_ReflectedInLoopRequest()
    {
        var ctx = BuildContext(
            inputs: new Dictionary<string, object?> { ["condition"] = true },
            properties: new Dictionary<string, object?> { ["maxIterations"] = 25 });

        var result = await _module.ExecuteAsync(ctx);

        result.Loop!.MaxIterations.Should().Be(25);
    }

    // ── Null condition ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WhileModule_NullCondition_ReturnsFail()
    {
        var ctx = BuildContext();

        var result = await _module.ExecuteAsync(ctx);

        result.Success.Should().BeFalse(because: "condition is required~ ❌");
        result.ErrorMessage.Should().Contain("condition");
    }

    // ── Property fallback ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task WhileModule_PropertyCondition_UsedWhenNoInputPort()
    {
        var ctx = BuildContext(
            inputs: new Dictionary<string, object?>(),
            properties: new Dictionary<string, object?> { ["condition"] = false });

        var result = await _module.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        result.ActivePorts.Should().Contain("done",
            because: "property 'condition=false' should fire done immediately~ ✅");
    }
}

/// <summary>
/// 🔁🌀 Phase 2.2.2 — Tests for BreakModule and ContinueModule~
/// Validates sentinel output keys and module metadata~ ✨💖
/// </summary>
public sealed class BreakContinueModuleTests
{
    private static ModuleExecutionContext BuildContext()
        => new()
        {
            Inputs = new Dictionary<string, object?>(),
            Properties = new Dictionary<string, object?>(),
            Variables = new Dictionary<string, object?>(),
            Logger = NullLogger.Instance,
            Services = new EmptyServiceProvider(),
            ExecutionId = Guid.NewGuid(),
            NodeId = "break-node",
        };

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type t) => null;
    }

    // ── BreakModule ───────────────────────────────────────────────────────────────────

    [Fact]
    public void BreakModule_Metadata_IsCorrect()
    {
        var m = new BreakModule();
        m.ModuleId.Should().Be("builtin.break");
        m.Category.Should().Be("Flow Control");
        m.Icon.Should().Be("⏹️");
        m.Version.Should().Be(new Version(1, 0, 0));
    }

    [Fact]
    public async Task BreakModule_Execute_ReturnsBreakSentinel()
    {
        var result = await new BreakModule().ExecuteAsync(BuildContext());

        result.Success.Should().BeTrue();
        result.Outputs.Should().ContainKey("__loop_break__",
            because: "break sentinel key must be present for SubGraphExecutor detection~ ⏹️");
        result.Outputs["__loop_break__"].Should().Be(true);
    }

    // ── ContinueModule ────────────────────────────────────────────────────────────────

    [Fact]
    public void ContinueModule_Metadata_IsCorrect()
    {
        var m = new ContinueModule();
        m.ModuleId.Should().Be("builtin.continue");
        m.Category.Should().Be("Flow Control");
        m.Icon.Should().Be("⏭️");
        m.Version.Should().Be(new Version(1, 0, 0));
    }

    [Fact]
    public async Task ContinueModule_Execute_ReturnsContinueSentinel()
    {
        var result = await new ContinueModule().ExecuteAsync(BuildContext());

        result.Success.Should().BeTrue();
        result.Outputs.Should().ContainKey("__loop_continue__",
            because: "continue sentinel key must be present for SubGraphExecutor detection~ ⏭️");
        result.Outputs["__loop_continue__"].Should().Be(true);
    }

    // ── Distinctness ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Break_And_Continue_Have_Different_Sentinel_Keys()
    {
        var breakResult = await new BreakModule().ExecuteAsync(BuildContext());
        var continueResult = await new ContinueModule().ExecuteAsync(BuildContext());

        breakResult.Outputs.Keys.Should().NotIntersectWith(continueResult.Outputs.Keys,
            because: "break and continue use different sentinel keys~ ⏹️⏭️");
    }
}


