// <copyright file="ScriptModuleTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.Script;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin.Script;
using Workflow.Scripting;
using Workflow.Scripting.Lua;
using Workflow.Scripting.Roslyn;
using Xunit;

/// <summary>
/// 📜 Phase 3.1.4 — Tests for the <c>builtin.script</c> module across all three languages~ ✨.
/// </summary>
public sealed class ScriptModuleTests
{
    private static readonly IServiceProvider Services = BuildServices();

    private static IServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWorkflowScripting();
        services.AddRoslynScripting();
        services.AddLuaScripting();
        return services.BuildServiceProvider();
    }

    private static ModuleExecutionContext Context(
        Dictionary<string, object?> properties,
        Dictionary<string, object?>? inputs = null,
        Dictionary<string, object?>? variables = null)
        => new()
        {
            Inputs = inputs ?? new Dictionary<string, object?>(),
            Properties = properties,
            Variables = variables ?? new Dictionary<string, object?>(),
            Logger = NullLogger.Instance,
            Services = Services,
            ExecutionId = Guid.NewGuid(),
            NodeId = "script-node",
        };

    [Theory]
    [InlineData("javascript", "return input.a + input.b;")]
    [InlineData("lua", "return input.a + input.b")]
    [InlineData("csharp", "return (int)input[\"a\"] + (int)input[\"b\"];")]
    public async Task Execute_EachLanguage_EndToEnd(string language, string code)
    {
        var module = new ScriptModule();
        var context = Context(
            new Dictionary<string, object?> { ["language"] = language, ["code"] = code },
            inputs: new Dictionary<string, object?> { ["input"] = new Dictionary<string, object?> { ["a"] = 2, ["b"] = 3 } });

        var result = await module.ExecuteAsync(context);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.Outputs["result"].Should().Be(5);
        result.Outputs["success"].Should().Be(true);
    }

    [Fact]
    public async Task VariableUpdates_StagedInModuleResult()
    {
        var module = new ScriptModule();
        var context = Context(
            new Dictionary<string, object?>
            {
                ["language"] = "javascript",
                ["code"] = "workflow.setVariable('greeting', 'hi'); return null;",
            },
            variables: new Dictionary<string, object?> { ["existing"] = 1 });

        var result = await module.ExecuteAsync(context);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.VariableUpdates.Should().NotBeNull();
        result.VariableUpdates!["greeting"].Should().Be("hi");
    }

    [Fact]
    public async Task Inputs_FlowIntoScript()
    {
        var module = new ScriptModule();
        var context = Context(
            new Dictionary<string, object?> { ["language"] = "javascript", ["code"] = "return input.name;" },
            inputs: new Dictionary<string, object?> { ["input"] = new Dictionary<string, object?> { ["name"] = "Ami" } });

        var result = await module.ExecuteAsync(context);
        result.Outputs["result"].Should().Be("Ami");
    }

    [Fact]
    public async Task UnknownLanguage_Fails()
    {
        var module = new ScriptModule();
        var context = Context(new Dictionary<string, object?> { ["language"] = "cobol", ["code"] = "x" });

        var result = await module.ExecuteAsync(context);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Unknown script language");
    }

    [Fact]
    public void EmptyCode_FailsValidation()
    {
        var module = new ScriptModule();
        var validation = module.ValidateConfiguration(new Dictionary<string, object?> { ["language"] = "javascript", ["code"] = "" });
        validation.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task NodeTimeout_OverHostCeiling_Clamped()
    {
        // Host ceiling of 1s; node requests 999s → clamped, so an infinite JS loop times out ~1s.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWorkflowScripting();
        services.AddSingleton(new Workflow.Scripting.Abstractions.ScriptHostCeilings { MaxTimeoutSeconds = 1 });
        var sp = services.BuildServiceProvider();

        var module = new ScriptModule();
        var context = new ModuleExecutionContext
        {
            Inputs = new Dictionary<string, object?>(),
            Properties = new Dictionary<string, object?> { ["language"] = "javascript", ["code"] = "while(true){}", ["timeoutSeconds"] = 999 },
            Variables = new Dictionary<string, object?>(),
            Logger = NullLogger.Instance,
            Services = sp,
            ExecutionId = Guid.NewGuid(),
            NodeId = "n",
        };

        var result = await module.ExecuteAsync(context);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("timed out");
    }

    [Fact]
    public async Task ScriptFailure_ProducesModuleFailure()
    {
        var module = new ScriptModule();
        var context = Context(new Dictionary<string, object?> { ["language"] = "javascript", ["code"] = "throw new Error('nope');" });

        var result = await module.ExecuteAsync(context);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("nope");
    }
}
