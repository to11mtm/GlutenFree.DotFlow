// <copyright file="EndModuleTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin;
using Workflow.Modules.Validation;
using Xunit;

/// <summary>
/// 🏁 Tests for <see cref="EndModule"/> (<c>builtin.end</c>) — echo behaviour, logging modes,
/// and the F2 regression guard~ ✨💖
/// </summary>
public sealed class EndModuleTests
{
    private readonly EndModule _module = new();

    #region Helpers 🛠️

    private static ModuleExecutionContext BuildContext(
        object? result = null,
        Dictionary<string, object?>? properties = null,
        ILogger? logger = null,
        bool omitInput = false)
        => new()
        {
            Inputs = omitInput
                ? new Dictionary<string, object?>()
                : new Dictionary<string, object?> { ["result"] = result },
            Properties = properties ?? new Dictionary<string, object?>(),
            Variables = new Dictionary<string, object?>(),
            Logger = logger ?? NullLogger.Instance,
            Services = new EmptyServiceProvider(),
            ExecutionId = Guid.NewGuid(),
            NodeId = "end-1",
        };

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    /// <summary>Captures log entries so the mode behaviour can be asserted~ 🔍.</summary>
    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    #endregion

    #region Metadata & Schema 🏷️

    [Fact]
    public void EndModule_ShouldPassModuleValidator()
    {
        new ModuleValidator().Validate(_module).IsValid.Should().BeTrue();
    }

    [Fact]
    public void EndModule_HasExpectedMetadata()
    {
        _module.ModuleId.Should().Be("builtin.end");
        _module.DisplayName.Should().Be("End");
        _module.Category.Should().Be("Flow Control");
    }

    [Fact]
    public void EndModule_ResultInput_IsOptional()
    {
        _module.Schema.Inputs.Count.Should().Be(1);
        _module.Schema.Inputs[0].Name.Should().Be("result");
        _module.Schema.Inputs[0].IsRequired.Should().BeFalse();
    }

    [Fact]
    public void EndModule_ResultInput_DoesNotExpandTemplates()
    {
        // F9: input values are data from upstream, never author-typed text — expanding them would
        // let untrusted data reach workflow variables~ 🛡️
        _module.Schema.Inputs[0].SupportsTemplates.Should().BeFalse();
    }

    [Fact]
    public void EndModule_HasResultOutput()
    {
        _module.Schema.Outputs.Count.Should().Be(1);
        _module.Schema.Outputs[0].Name.Should().Be("result");
    }

    [Fact]
    public void EndModule_IsRegisteredInBuiltins()
    {
        BuiltinModules.GetAll().Should().Contain(m => m.ModuleId == "builtin.end");
    }

    #endregion

    #region Echo Behaviour — the F2 guard 🛟

    [Fact]
    public async Task Execute_LogMode_ProducesNonEmptyOutputs()
    {
        // F2 REGRESSION GUARD. Workflow outputs are gathered from nodes with no successors, keyed
        // {nodeId}.{key}. If End ever stopped emitting, appending it to a workflow would silently
        // replace that workflow's result with nothing — successfully, and invisibly. Both modes
        // must keep producing outputs~ 🛟
        var result = await _module.ExecuteAsync(BuildContext("done"));

        result.Success.Should().BeTrue();
        result.Outputs.Should().NotBeEmpty("an End node with no outputs erases the workflow's result~ 💔");
    }

    [Fact]
    public async Task Execute_SilentMode_ProducesNonEmptyOutputs()
    {
        // "No-op" means "no side effect", never "no output" — see F2~ 🛟
        var result = await _module.ExecuteAsync(
            BuildContext("done", new() { ["mode"] = "silent" }));

        result.Outputs.Should().NotBeEmpty();
        result.Outputs["result"].Should().Be("done");
    }

    [Fact]
    public async Task Execute_LogMode_EchoesInput()
    {
        var payload = new Dictionary<string, object?> { ["id"] = 7 };

        var result = await _module.ExecuteAsync(BuildContext(payload));

        result.Outputs["result"].Should().BeSameAs(payload);
    }

    [Fact]
    public async Task Execute_MissingInput_StillEmitsResultKey()
    {
        // The output key must exist even when unconnected, so the result shape stays predictable~ 🎁
        var result = await _module.ExecuteAsync(BuildContext(omitInput: true));

        result.Success.Should().BeTrue();
        result.Outputs.Should().ContainKey("result");
        result.Outputs["result"].Should().BeNull();
    }

    [Fact]
    public async Task Execute_NullInput_EchoesNull()
    {
        var result = await _module.ExecuteAsync(BuildContext(null));

        result.Outputs["result"].Should().BeNull();
    }

    #endregion

    #region Logging 📝

    [Fact]
    public async Task Execute_LogMode_WritesTheResult()
    {
        var logger = new CapturingLogger();

        await _module.ExecuteAsync(BuildContext("all done", logger: logger));

        logger.Entries.Should().ContainSingle();
        logger.Entries[0].Message.Should().Contain("all done");
    }

    [Fact]
    public async Task Execute_LogMode_IsTheDefault()
    {
        var logger = new CapturingLogger();

        await _module.ExecuteAsync(BuildContext("x", logger: logger));

        logger.Entries.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Execute_SilentMode_WritesNothing()
    {
        var logger = new CapturingLogger();

        await _module.ExecuteAsync(
            BuildContext("x", new() { ["mode"] = "silent" }, logger));

        logger.Entries.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Warning", LogLevel.Warning)]
    [InlineData("Debug", LogLevel.Debug)]
    [InlineData("Critical", LogLevel.Critical)]
    public async Task Execute_HonoursConfiguredLevel(string level, LogLevel expected)
    {
        var logger = new CapturingLogger();

        await _module.ExecuteAsync(
            BuildContext("x", new() { ["level"] = level }, logger));

        logger.Entries[0].Level.Should().Be(expected);
    }

    [Fact]
    public async Task Execute_UnknownLevel_FallsBackToInformation()
    {
        var logger = new CapturingLogger();

        await _module.ExecuteAsync(
            BuildContext("x", new() { ["level"] = "Shouty" }, logger));

        logger.Entries[0].Level.Should().Be(LogLevel.Information);
    }

    [Fact]
    public async Task Execute_Label_IsUsedAsThePrefix()
    {
        var logger = new CapturingLogger();

        await _module.ExecuteAsync(
            BuildContext("x", new() { ["label"] = "Order sync finished" }, logger));

        logger.Entries[0].Message.Should().Contain("Order sync finished");
    }

    [Fact]
    public async Task Execute_NoResult_LogsAPlaceholder()
    {
        var logger = new CapturingLogger();

        await _module.ExecuteAsync(BuildContext(null, logger: logger));

        logger.Entries[0].Message.Should().Contain("(no result)");
    }

    #endregion

    #region Configuration Validation ✅

    [Fact]
    public void Validate_EmptyConfiguration_IsValid()
    {
        _module.ValidateConfiguration(new Dictionary<string, object?>()).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("log")]
    [InlineData("silent")]
    public void Validate_KnownModes_AreValid(string mode)
    {
        _module.ValidateConfiguration(new Dictionary<string, object?> { ["mode"] = mode })
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_UnknownMode_IsRejected()
    {
        var result = _module.ValidateConfiguration(
            new Dictionary<string, object?> { ["mode"] = "shout" });

        result.IsValid.Should().BeFalse();
        result.Errors[0].Code.Should().Be("INVALID_END_MODE");
    }

    [Fact]
    public void Validate_UnknownLevel_IsRejected()
    {
        var result = _module.ValidateConfiguration(
            new Dictionary<string, object?> { ["level"] = "Shouty" });

        result.IsValid.Should().BeFalse();
        result.Errors[0].Code.Should().Be("INVALID_LOG_LEVEL");
    }

    #endregion
}
