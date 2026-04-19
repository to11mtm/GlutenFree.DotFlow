// <copyright file="GetVariableModuleTests.cs" company="GlutenFree">
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
/// 🔍 Phase 1.5.4 — Tests for <see cref="GetVariableModule"/> (<c>builtin.getvariable</c>)~ ✨💖
/// </summary>
public sealed class GetVariableModuleTests
{
    private readonly GetVariableModule _module = new();

    #region Helpers 🛠️

    private static ModuleExecutionContext BuildContext(
        Dictionary<string, object?>? properties = null,
        Dictionary<string, object?>? variables = null)
    {
        return new ModuleExecutionContext
        {
            Inputs = new Dictionary<string, object?>(),
            Properties = properties ?? new Dictionary<string, object?> { ["name"] = "myVar" },
            Variables = variables ?? new Dictionary<string, object?>(),
            Logger = NullLogger.Instance,
            Services = new EmptyServiceProvider(),
            ExecutionId = Guid.NewGuid(),
            NodeId = "getvar-node-1",
        };
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    #endregion

    #region Metadata & Discovery 🏷️

    [Fact]
    public void GetVariableModule_ShouldPassModuleValidator()
    {
        var validator = new ModuleValidator();
        var result = validator.Validate(_module);
        result.IsValid.Should().BeTrue("GetVariableModule must pass all validator checks~ 💖");
    }

    [Fact]
    public void ModuleDiscovery_ShouldFindGetVariableModule()
    {
        var discovery = new ModuleDiscovery();
        var types = discovery.DiscoverModuleTypes(typeof(GetVariableModule).Assembly);
        types.Should().Contain(typeof(GetVariableModule));
    }

    #endregion

    #region Execute Tests 🚀

    [Fact]
    public async Task Execute_WithExistingVariable_ShouldReturnValue()
    {
        var ctx = BuildContext(
            properties: new Dictionary<string, object?> { ["name"] = "greeting" },
            variables: new Dictionary<string, object?> { ["greeting"] = "UwU" });

        var result = await _module.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        result.Outputs["value"].Should().Be("UwU");
        result.Outputs["exists"].Should().Be(true);
    }

    [Fact]
    public async Task Execute_MissingVariable_NoDefault_ShouldReturnNull()
    {
        var ctx = BuildContext(
            properties: new Dictionary<string, object?> { ["name"] = "missing" });

        var result = await _module.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        result.Outputs["value"].Should().BeNull();
        result.Outputs["exists"].Should().Be(false);
    }

    [Fact]
    public async Task Execute_MissingVariable_WithDefault_ShouldReturnDefault()
    {
        var ctx = BuildContext(
            properties: new Dictionary<string, object?> { ["name"] = "missing", ["defaultValue"] = "fallback" });

        var result = await _module.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        result.Outputs["value"].Should().Be("fallback");
        result.Outputs["exists"].Should().Be(false);
    }

    [Fact]
    public async Task Execute_MissingVariable_ThrowIfMissing_ShouldFail()
    {
        var ctx = BuildContext(
            properties: new Dictionary<string, object?> { ["name"] = "missing", ["throwIfMissing"] = true });

        var result = await _module.ExecuteAsync(ctx);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("missing");
    }

    [Fact]
    public async Task Execute_TypeName_ShouldMatchActualType()
    {
        var ctx = BuildContext(
            properties: new Dictionary<string, object?> { ["name"] = "count" },
            variables: new Dictionary<string, object?> { ["count"] = 42 });

        var result = await _module.ExecuteAsync(ctx);

        result.Outputs["typeName"].Should().Be("Int32");
    }

    [Fact]
    public async Task Execute_NullValue_TypeName_ShouldBeNull()
    {
        var ctx = BuildContext(
            properties: new Dictionary<string, object?> { ["name"] = "empty" },
            variables: new Dictionary<string, object?> { ["empty"] = null });

        var result = await _module.ExecuteAsync(ctx);

        result.Outputs["value"].Should().BeNull();
        result.Outputs["exists"].Should().Be(true);
        result.Outputs["typeName"].Should().Be("null");
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

    #endregion
}

