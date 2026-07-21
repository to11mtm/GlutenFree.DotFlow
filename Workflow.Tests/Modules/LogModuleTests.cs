// <copyright file="LogModuleTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin;
using Workflow.Modules.Discovery;
using Workflow.Modules.Validation;
using Xunit;

/// <summary>
/// 📝 Phase 1.5.1 — Tests for <see cref="LogModule"/> (<c>builtin.log</c>)!
/// Validates metadata, schema, execution at all log levels, context inclusion,
/// and configuration validation~ ✨💖
/// </summary>
public sealed class LogModuleTests
{
    private readonly LogModule _module = new();

    #region Helpers 🛠️

    /// <summary>
    /// Builds a <see cref="ModuleExecutionContext"/> for testing the LogModule~ 🧪
    /// </summary>
    private static ModuleExecutionContext BuildContext(
        Dictionary<string, object?>? properties = null,
        Guid? executionId = null,
        string nodeId = "test-node-1")
    {
        return new ModuleExecutionContext
        {
            Inputs = new Dictionary<string, object?>(),
            Properties = properties ?? new Dictionary<string, object?> { ["message"] = "Hello from Ami~ 💖" },
            Variables = new Dictionary<string, object?>(),
            Logger = NullLogger.Instance,
            Services = new EmptyServiceProvider(),
            ExecutionId = executionId ?? Guid.NewGuid(),
            NodeId = nodeId,
        };
    }

    /// <summary>
    /// Minimal service provider for tests~ 🧪
    /// </summary>
    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    #endregion

    #region Module Metadata & Validator Tests 🏷️

    /// <summary>
    /// LogModule should pass the standard <see cref="ModuleValidator"/> checks~ ✅
    /// </summary>
    [Fact]
    public void LogModule_ShouldPassModuleValidator()
    {
        var validator = new ModuleValidator();
        var result = validator.Validate(_module);
        result.IsValid.Should().BeTrue("LogModule must pass all validator checks~ 💖");
    }

    /// <summary>
    /// <see cref="ModuleDiscovery"/> should auto-discover LogModule from the Workflow.Modules assembly~ 🔍
    /// </summary>
    [Fact]
    public void ModuleDiscovery_ShouldFindLogModule()
    {
        var discovery = new ModuleDiscovery();
        var types = discovery.DiscoverModuleTypes(typeof(LogModule).Assembly);
        types.Should().Contain(typeof(LogModule), "LogModule should be auto-discoverable~ ✨");
    }

    /// <summary>
    /// Metadata spot-check~ 🏷️
    /// </summary>
    [Fact]
    public void LogModule_Metadata_ShouldBeCorrect()
    {
        _module.ModuleId.Should().Be("builtin.log");
        _module.DisplayName.Should().Be("Log Message");
        _module.Category.Should().Be("Utilities");
        _module.Icon.Should().Be("📝");
        _module.Version.Should().Be(new Version(1, 0, 0));
        _module.Description.Should().NotBeNullOrWhiteSpace();
    }

    #endregion

    #region Execute Tests 🚀

    /// <summary>
    /// Execute with a message should return timestamp and resolved message~ 📝
    /// </summary>
    [Fact]
    public async Task Execute_WithMessage_ShouldReturnTimestampAndMessage()
    {
        var ctx = BuildContext(new Dictionary<string, object?> { ["message"] = "Kawaii log~ UwU" });

        var result = await _module.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        result.Outputs.Should().ContainKey("timestamp");
        result.Outputs.Should().ContainKey("message");
        result.Outputs["message"].Should().Be("Kawaii log~ UwU");
        result.Outputs["timestamp"].Should().BeOfType<DateTimeOffset>();
    }

    /// <summary>
    /// Execute at each supported log level should succeed~ 🎚️
    /// </summary>
    [Theory]
    [InlineData("Trace")]
    [InlineData("Debug")]
    [InlineData("Information")]
    [InlineData("Warning")]
    [InlineData("Error")]
    [InlineData("Critical")]
    public async Task Execute_AtEachLevel_ShouldSucceed(string level)
    {
        var ctx = BuildContext(new Dictionary<string, object?>
        {
            ["message"] = $"Testing level {level}",
            ["level"] = level,
        });

        var result = await _module.ExecuteAsync(ctx);
        result.Success.Should().BeTrue($"level '{level}' should execute successfully~ ✨");
    }

    /// <summary>
    /// Execute with <c>includeContext = true</c> should append ExecutionId and NodeId~ 🔍
    /// </summary>
    [Fact]
    public async Task Execute_WithIncludeContext_ShouldAppendContextInfo()
    {
        var execId = Guid.NewGuid();
        var ctx = BuildContext(
            new Dictionary<string, object?>
            {
                ["message"] = "Context test",
                ["includeContext"] = true,
            },
            executionId: execId,
            nodeId: "node-42");

        var result = await _module.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        var msg = result.Outputs["message"] as string;
        msg.Should().Contain(execId.ToString(), "should contain ExecutionId~ 🔍");
        msg.Should().Contain("node-42", "should contain NodeId~ 🔍");
    }

    /// <summary>
    /// Execute with <c>includeContext = false</c> should NOT append context info~ 🙈
    /// </summary>
    [Fact]
    public async Task Execute_WithIncludeContextFalse_ShouldNotAppendContext()
    {
        var ctx = BuildContext(new Dictionary<string, object?>
        {
            ["message"] = "No context plz",
            ["includeContext"] = false,
        });

        var result = await _module.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        result.Outputs["message"].Should().Be("No context plz");
    }

    /// <summary>
    /// Execute with unknown log level should fall back to Information (no error)~ 🤷
    /// </summary>
    [Fact]
    public async Task Execute_WithUnknownLevel_ShouldFallBackToInformation()
    {
        var ctx = BuildContext(new Dictionary<string, object?>
        {
            ["message"] = "Unknown level test",
            ["level"] = "SuperKawaii",
        });

        var result = await _module.ExecuteAsync(ctx);
        result.Success.Should().BeTrue("unknown level should gracefully default to Information~ 💖");
    }

    /// <summary>
    /// Execute with empty message should still succeed~ 📭
    /// </summary>
    [Fact]
    public async Task Execute_WithEmptyMessage_ShouldSucceed()
    {
        var ctx = BuildContext(new Dictionary<string, object?> { ["message"] = "" });

        var result = await _module.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        result.Outputs["message"].Should().Be(string.Empty);
    }

    #endregion

    #region ValidateConfiguration Tests ✅

    /// <summary>
    /// ValidateConfiguration should reject an unknown log level name~ ❌
    /// </summary>
    [Fact]
    public void ValidateConfiguration_WithUnknownLevel_ShouldFail()
    {
        var config = new Dictionary<string, object?> { ["level"] = "SuperKawaii" };

        var result = _module.ValidateConfiguration(config);

        result.IsValid.Should().BeFalse("unknown level should fail validation~ 💔");
        result.Errors.Should().ContainSingle().Which.Code.Should().Be("INVALID_LOG_LEVEL");
    }

    /// <summary>
    /// ValidateConfiguration should accept all valid level names~ ✅
    /// </summary>
    [Theory]
    [InlineData("Trace")]
    [InlineData("Debug")]
    [InlineData("Information")]
    [InlineData("Warning")]
    [InlineData("Error")]
    [InlineData("Critical")]
    public void ValidateConfiguration_WithValidLevel_ShouldSucceed(string level)
    {
        var config = new Dictionary<string, object?> { ["level"] = level };

        var result = _module.ValidateConfiguration(config);

        result.IsValid.Should().BeTrue($"'{level}' is a valid log level~ ✨");
    }

    #endregion
}

