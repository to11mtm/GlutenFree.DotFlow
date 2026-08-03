// <copyright file="StartModuleTests.cs" company="GlutenFree">
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
using Workflow.Modules.Validation;
using Xunit;

/// <summary>
/// 🚀 Tests for <see cref="StartModule"/> (<c>builtin.start</c>) — metadata, value typing,
/// the empty case, and configuration validation~ ✨💖
/// </summary>
public sealed class StartModuleTests
{
    private readonly StartModule _module = new();

    #region Helpers 🛠️

    private static ModuleExecutionContext BuildContext(Dictionary<string, object?>? properties = null)
        => new()
        {
            Inputs = new Dictionary<string, object?>(),
            Properties = properties ?? new Dictionary<string, object?>(),
            Variables = new Dictionary<string, object?>(),
            Logger = NullLogger.Instance,
            Services = new EmptyServiceProvider(),
            ExecutionId = Guid.NewGuid(),
            NodeId = "start-1",
        };

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    #endregion

    #region Metadata & Schema 🏷️

    [Fact]
    public void StartModule_ShouldPassModuleValidator()
    {
        var result = new ModuleValidator().Validate(_module);
        result.IsValid.Should().BeTrue("StartModule must pass all validator checks~ 💖");
    }

    [Fact]
    public void StartModule_HasExpectedMetadata()
    {
        _module.ModuleId.Should().Be("builtin.start");
        _module.DisplayName.Should().Be("Start");
        _module.Category.Should().Be("Flow Control");
    }

    [Fact]
    public void StartModule_HasNoInputs()
    {
        // F1: no input ports is what makes a Start node unconditionally a start node~ 🚀
        _module.Schema.Inputs.Count.Should().Be(0);
    }

    [Fact]
    public void StartModule_HasSingleValueOutput()
    {
        _module.Schema.Outputs.Count.Should().Be(1);
        _module.Schema.Outputs[0].Name.Should().Be("value");
    }

    [Fact]
    public void StartModule_ValueProperty_SupportsTemplates()
    {
        // D1: the whole point — a Start node can surface {{Variable.orderId}} as the opening value~ 🔗
        var value = _module.Schema.Properties.Find(p => p.Name == "value");
        value.IsSome.Should().BeTrue();
        value.IfSome(p => p.SupportsTemplates.Should().BeTrue());
    }

    [Fact]
    public void StartModule_IsRegisteredInBuiltins()
    {
        BuiltinModules.GetAll().Should().Contain(m => m.ModuleId == "builtin.start");
    }

    #endregion

    #region Execution 🚀

    [Fact]
    public async Task Execute_BlankValue_EmitsNull()
    {
        // D2: "or empty" needs no special type — blank simply means null~ 🌸
        var result = await _module.ExecuteAsync(BuildContext());

        result.Success.Should().BeTrue();
        result.Outputs.Should().ContainKey("value");
        result.Outputs["value"].Should().BeNull();
    }

    [Fact]
    public async Task Execute_WhitespaceValue_EmitsNull()
    {
        var result = await _module.ExecuteAsync(BuildContext(new() { ["value"] = "   " }));

        result.Outputs["value"].Should().BeNull();
    }

    [Fact]
    public async Task Execute_TextValue_EmitsString()
    {
        var result = await _module.ExecuteAsync(
            BuildContext(new() { ["value"] = "hello", ["valueType"] = "text" }));

        result.Outputs["value"].Should().Be("hello");
    }

    [Fact]
    public async Task Execute_DefaultValueType_IsText()
    {
        var result = await _module.ExecuteAsync(BuildContext(new() { ["value"] = "hello" }));

        result.Outputs["value"].Should().Be("hello");
    }

    [Fact]
    public async Task Execute_IntegerNumber_EmitsLong()
    {
        var result = await _module.ExecuteAsync(
            BuildContext(new() { ["value"] = "42", ["valueType"] = "number" }));

        result.Outputs["value"].Should().Be(42L);
    }

    [Fact]
    public async Task Execute_DecimalNumber_EmitsDouble()
    {
        var result = await _module.ExecuteAsync(
            BuildContext(new() { ["value"] = "4.5", ["valueType"] = "number" }));

        result.Outputs["value"].Should().Be(4.5d);
    }

    [Fact]
    public async Task Execute_UnparseableNumber_FallsBackToText()
    {
        // Failing the node at run time would be a baffling place to learn about a typo~ 🩹
        var result = await _module.ExecuteAsync(
            BuildContext(new() { ["value"] = "not-a-number", ["valueType"] = "number" }));

        result.Success.Should().BeTrue();
        result.Outputs["value"].Should().Be("not-a-number");
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("True", true)]
    public async Task Execute_Boolean_EmitsBool(string raw, bool expected)
    {
        var result = await _module.ExecuteAsync(
            BuildContext(new() { ["value"] = raw, ["valueType"] = "boolean" }));

        result.Outputs["value"].Should().Be(expected);
    }

    [Fact]
    public async Task Execute_JsonObject_EmitsDictionary()
    {
        var result = await _module.ExecuteAsync(
            BuildContext(new() { ["value"] = """{"id":7,"name":"Ami"}""", ["valueType"] = "json" }));

        var dict = result.Outputs["value"].Should().BeAssignableTo<IDictionary<string, object?>>().Subject;
        dict["id"].Should().Be(7L);
        dict["name"].Should().Be("Ami");
    }

    [Fact]
    public async Task Execute_JsonArray_EmitsList()
    {
        var result = await _module.ExecuteAsync(
            BuildContext(new() { ["value"] = "[1,2,3]", ["valueType"] = "json" }));

        var list = result.Outputs["value"].Should().BeAssignableTo<IReadOnlyList<object?>>().Subject;
        list.Count.Should().Be(3);
        list[0].Should().Be(1L);
    }

    [Fact]
    public async Task Execute_ResolvedTemplateValue_IsUsedVerbatim()
    {
        // The binder resolves {{…}} before we're called, so we only ever see the resolved text~ 🔗
        var result = await _module.ExecuteAsync(BuildContext(new() { ["value"] = "ORD-123" }));

        result.Outputs["value"].Should().Be("ORD-123");
    }

    #endregion

    #region Configuration Validation ✅

    [Fact]
    public void Validate_EmptyConfiguration_IsValid()
    {
        _module.ValidateConfiguration(new Dictionary<string, object?>()).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("text")]
    [InlineData("number")]
    [InlineData("boolean")]
    [InlineData("json")]
    public void Validate_KnownValueTypes_AreValid(string valueType)
    {
        var config = new Dictionary<string, object?> { ["valueType"] = valueType };

        _module.ValidateConfiguration(config).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_UnknownValueType_IsRejected()
    {
        var config = new Dictionary<string, object?> { ["valueType"] = "xml" };

        var result = _module.ValidateConfiguration(config);

        result.IsValid.Should().BeFalse();
        result.Errors[0].Code.Should().Be("INVALID_VALUE_TYPE");
    }

    [Fact]
    public void Validate_MalformedJson_IsRejectedAtSaveTime()
    {
        var config = new Dictionary<string, object?> { ["valueType"] = "json", ["value"] = "{nope" };

        var result = _module.ValidateConfiguration(config);

        result.IsValid.Should().BeFalse();
        result.Errors[0].Code.Should().Be("INVALID_JSON_VALUE");
    }

    [Fact]
    public void Validate_TemplatedJson_IsNotJudged()
    {
        // A template can't be valid JSON until it's resolved, so rejecting it would be wrong~ 🔗
        var config = new Dictionary<string, object?>
        {
            ["valueType"] = "json",
            ["value"] = "{{Variable.payload}}",
        };

        _module.ValidateConfiguration(config).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_BlankJsonValue_IsValid()
    {
        var config = new Dictionary<string, object?> { ["valueType"] = "json", ["value"] = string.Empty };

        _module.ValidateConfiguration(config).IsValid.Should().BeTrue();
    }

    #endregion
}
