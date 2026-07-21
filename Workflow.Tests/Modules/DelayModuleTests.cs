// <copyright file="DelayModuleTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin;
using Workflow.Modules.Discovery;
using Workflow.Modules.Validation;
using Xunit;

/// <summary>
/// ⏱️ Phase 1.5.2 — Tests for <see cref="DelayModule"/> (<c>builtin.delay</c>)!
/// Validates metadata, timing, cancellation, and configuration validation~ ✨💖
/// </summary>
public sealed class DelayModuleTests
{
    private readonly DelayModule _module = new();

    #region Helpers 🛠️

    private static ModuleExecutionContext BuildContext(
        Dictionary<string, object?>? properties = null)
    {
        return new ModuleExecutionContext
        {
            Inputs = new Dictionary<string, object?>(),
            Properties = properties ?? new Dictionary<string, object?> { ["durationMs"] = 0L },
            Variables = new Dictionary<string, object?>(),
            Logger = NullLogger.Instance,
            Services = new EmptyServiceProvider(),
            ExecutionId = Guid.NewGuid(),
            NodeId = "delay-node-1",
        };
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    #endregion

    #region Metadata & Discovery 🏷️

    [Fact]
    public void DelayModule_ShouldPassModuleValidator()
    {
        var validator = new ModuleValidator();
        var result = validator.Validate(_module);
        result.IsValid.Should().BeTrue("DelayModule must pass all validator checks~ 💖");
    }

    [Fact]
    public void ModuleDiscovery_ShouldFindDelayModule()
    {
        var discovery = new ModuleDiscovery();
        var types = discovery.DiscoverModuleTypes(typeof(DelayModule).Assembly);
        types.Should().Contain(typeof(DelayModule), "DelayModule should be auto-discoverable~ ✨");
    }

    [Fact]
    public void DelayModule_Metadata_ShouldBeCorrect()
    {
        _module.ModuleId.Should().Be("builtin.delay");
        _module.DisplayName.Should().Be("Delay");
        _module.Category.Should().Be("Flow Control");
        _module.Icon.Should().Be("⏱️");
        _module.Version.Should().Be(new Version(1, 0, 0));
    }

    #endregion

    #region Execute Tests 🚀

    [Fact]
    public async Task Execute_WithZeroDuration_ShouldCompleteImmediately()
    {
        var ctx = BuildContext(new Dictionary<string, object?> { ["durationMs"] = 0L });

        var result = await _module.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        ((long)result.Outputs["actualDurationMs"]!).Should().BeGreaterOrEqualTo(0);
        result.Outputs["wasCancelled"].Should().Be(false);
    }

    [Fact]
    public async Task Execute_With50msDuration_ShouldDelayApproximately()
    {
        var ctx = BuildContext(new Dictionary<string, object?> { ["durationMs"] = 50L });

        var result = await _module.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        ((long)result.Outputs["actualDurationMs"]!).Should().BeGreaterOrEqualTo(30, "should delay ~50ms (±tolerance)~ ⏱️");
        result.Outputs["wasCancelled"].Should().Be(false);
    }

    [Fact]
    public async Task Execute_WithCancellation_ShouldReturnWasCancelledTrue()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
        var ctx = BuildContext(new Dictionary<string, object?> { ["durationMs"] = 5000L });

        var result = await _module.ExecuteAsync(ctx, cts.Token);

        result.Success.Should().BeTrue("cancellation is graceful, not a failure~ 🛑");
        result.Outputs["wasCancelled"].Should().Be(true);
    }

    [Fact]
    public async Task Execute_NotCancelled_ShouldReturnWasCancelledFalse()
    {
        var ctx = BuildContext(new Dictionary<string, object?> { ["durationMs"] = 10L });

        var result = await _module.ExecuteAsync(ctx);

        result.Outputs["wasCancelled"].Should().Be(false);
    }

    [Fact]
    public async Task Execute_ExceedingMaxDuration_ShouldFail()
    {
        var ctx = BuildContext(new Dictionary<string, object?>
        {
            ["durationMs"] = 500_000L,
            ["maxDurationMs"] = 100_000L,
        });

        var result = await _module.ExecuteAsync(ctx);

        result.Success.Should().BeFalse("exceeding max should fail~ 💔");
        result.ErrorMessage.Should().Contain("exceeds");
    }

    #endregion

    #region ValidateConfiguration Tests ✅

    [Fact]
    public void ValidateConfiguration_WithNegativeDuration_ShouldFail()
    {
        var config = new Dictionary<string, object?> { ["durationMs"] = -100L };

        var result = _module.ValidateConfiguration(config);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Code.Should().Be("NEGATIVE_DURATION");
    }

    [Fact]
    public void ValidateConfiguration_ExceedingMax_ShouldFail()
    {
        var config = new Dictionary<string, object?>
        {
            ["durationMs"] = 500_000L,
            ["maxDurationMs"] = 100_000L,
        };

        var result = _module.ValidateConfiguration(config);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Code.Should().Be("DURATION_EXCEEDS_MAX");
    }

    [Fact]
    public void ValidateConfiguration_WithZeroDuration_ShouldSucceed()
    {
        var config = new Dictionary<string, object?> { ["durationMs"] = 0L };

        var result = _module.ValidateConfiguration(config);

        result.IsValid.Should().BeTrue();
    }

    #endregion
}

