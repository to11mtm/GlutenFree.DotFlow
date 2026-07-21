// <copyright file="SetVariableModuleTests.cs" company="GlutenFree">
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
/// 💾 Phase 1.5.3 — Tests for <see cref="SetVariableModule"/> (<c>builtin.setvariable</c>)!
/// Validates metadata, variable creation/update, VariableUpdates mechanism,
/// input override, and configuration validation~ ✨💖
/// </summary>
public sealed class SetVariableModuleTests
{
    private readonly SetVariableModule _module = new();

    #region Helpers 🛠️

    private static ModuleExecutionContext BuildContext(
        Dictionary<string, object?>? properties = null,
        Dictionary<string, object?>? inputs = null,
        Dictionary<string, object?>? variables = null)
    {
        return new ModuleExecutionContext
        {
            Inputs = inputs ?? new Dictionary<string, object?>(),
            Properties = properties ?? new Dictionary<string, object?> { ["name"] = "myVar", ["value"] = "hello" },
            Variables = variables ?? new Dictionary<string, object?>(),
            Logger = NullLogger.Instance,
            Services = new EmptyServiceProvider(),
            ExecutionId = Guid.NewGuid(),
            NodeId = "setvar-node-1",
        };
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    #endregion

    #region Metadata & Discovery 🏷️

    [Fact]
    public void SetVariableModule_ShouldPassModuleValidator()
    {
        var validator = new ModuleValidator();
        var result = validator.Validate(_module);
        result.IsValid.Should().BeTrue("SetVariableModule must pass all validator checks~ 💖");
    }

    [Fact]
    public void ModuleDiscovery_ShouldFindSetVariableModule()
    {
        var discovery = new ModuleDiscovery();
        var types = discovery.DiscoverModuleTypes(typeof(SetVariableModule).Assembly);
        types.Should().Contain(typeof(SetVariableModule));
    }

    #endregion

    #region Execute Tests 🚀

    [Fact]
    public async Task Execute_CreatesNewVariable_ShouldReturnWasCreatedTrue()
    {
        var ctx = BuildContext(
            properties: new Dictionary<string, object?> { ["name"] = "count", ["value"] = "42" });

        var result = await _module.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        result.Outputs["wasCreated"].Should().Be(true);
        result.Outputs["previousValue"].Should().BeNull();
        result.VariableUpdates.Should().ContainKey("count");
        result.VariableUpdates!["count"].Should().Be("42");
    }

    [Fact]
    public async Task Execute_UpdatesExistingVariable_ShouldReturnPreviousValue()
    {
        var ctx = BuildContext(
            properties: new Dictionary<string, object?> { ["name"] = "count", ["value"] = "99" },
            variables: new Dictionary<string, object?> { ["count"] = "42" });

        var result = await _module.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        result.Outputs["wasCreated"].Should().Be(false);
        result.Outputs["previousValue"].Should().Be("42");
        result.VariableUpdates!["count"].Should().Be("99");
    }

    [Fact]
    public async Task Execute_InputOverridesProperty_ShouldUseInputValue()
    {
        var ctx = BuildContext(
            properties: new Dictionary<string, object?> { ["name"] = "x", ["value"] = "from-prop" },
            inputs: new Dictionary<string, object?> { ["value"] = "from-input" });

        var result = await _module.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        result.VariableUpdates!["x"].Should().Be("from-input");
    }

    [Fact]
    public async Task Execute_NullValue_ShouldBeValid()
    {
        var ctx = BuildContext(
            properties: new Dictionary<string, object?> { ["name"] = "toDelete" });

        var result = await _module.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        result.VariableUpdates.Should().ContainKey("toDelete");
        result.VariableUpdates!["toDelete"].Should().BeNull();
    }

    [Fact]
    public async Task Execute_VariableUpdatesContainsCorrectPair()
    {
        var ctx = BuildContext(
            properties: new Dictionary<string, object?> { ["name"] = "greeting", ["value"] = "UwU" });

        var result = await _module.ExecuteAsync(ctx);

        result.VariableUpdates.Should().HaveCount(1);
        result.VariableUpdates!["greeting"].Should().Be("UwU");
    }

    #endregion

    #region ValidateConfiguration Tests ✅

    [Fact]
    public void ValidateConfiguration_EmptyName_ShouldFail()
    {
        var config = new Dictionary<string, object?> { ["name"] = "" };
        var result = _module.ValidateConfiguration(config);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Code.Should().Be("EMPTY_VARIABLE_NAME");
    }

    [Fact]
    public void ValidateConfiguration_NameWithSpaces_ShouldFail()
    {
        var config = new Dictionary<string, object?> { ["name"] = "my var" };
        var result = _module.ValidateConfiguration(config);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Code.Should().Be("INVALID_VARIABLE_NAME");
    }

    [Fact]
    public void ValidateConfiguration_DottedName_ShouldSucceed()
    {
        var config = new Dictionary<string, object?> { ["name"] = "user.count" };
        var result = _module.ValidateConfiguration(config);
        result.IsValid.Should().BeTrue();
    }

    #endregion
}

