// <copyright file="SplitModuleTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.Transform;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin.Transform;
using Xunit;

/// <summary>
/// 🧩 Tests for <see cref="SplitModule"/> (<c>builtin.split</c>) — dynamic outputs,
/// object coercion, optional fields, rest output, and configuration validation~ ✨💖
/// </summary>
public sealed class SplitModuleTests
{
    private readonly SplitModule _module = new();

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
            NodeId = "split-node",
        };

    private static List<object?> Keys(params string[] keys)
        => keys.Cast<object?>().ToList();

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    [Fact]
    public void SplitModule_Metadata_IsCorrect()
    {
        _module.ModuleId.Should().Be("builtin.split");
        _module.DisplayName.Should().Be("Split");
        _module.Category.Should().Be("Transformation");
        _module.Icon.Should().Be("🧩");
        _module.Version.Should().Be(new Version(1, 0, 0));
        _module.Schema.Outputs.ToList().Should().BeEmpty("split output ports are dynamic~ 🎗️");
    }

    [Fact]
    public async Task ExecuteAsync_SplitsDictionaryObject()
    {
        var ctx = BuildContext(
            inputs: new Dictionary<string, object?> { ["value"] = new Dictionary<string, object?> { ["foo"] = 1, ["bar"] = "two" } },
            properties: new Dictionary<string, object?> { ["keys"] = Keys("foo", "bar") });

        var result = await _module.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        result.ActivePorts.Should().BeNull("plain Ok means all output ports fire~ 🔥");
        result.Outputs["foo"].Should().Be(1);
        result.Outputs["bar"].Should().Be("two");
    }

    [Fact]
    public async Task ExecuteAsync_SplitsJsonElementObject()
    {
        using var doc = JsonDocument.Parse("""{"foo":1,"bar":"two"}""");
        var ctx = BuildContext(
            inputs: new Dictionary<string, object?> { ["value"] = doc.RootElement.Clone() },
            properties: new Dictionary<string, object?> { ["keys"] = Keys("foo", "bar") });

        var result = await _module.ExecuteAsync(ctx);

        result.Outputs["foo"].Should().Be(1L);
        result.Outputs["bar"].Should().Be("two");
    }

    [Fact]
    public async Task ExecuteAsync_SplitsJsonStringObject()
    {
        var ctx = BuildContext(
            inputs: new Dictionary<string, object?> { ["value"] = "{\"foo\":1,\"bar\":\"two\"}" },
            properties: new Dictionary<string, object?> { ["keys"] = Keys("foo", "bar") });

        var result = await _module.ExecuteAsync(ctx);

        result.Outputs["foo"].Should().Be(1L);
        result.Outputs["bar"].Should().Be("two");
    }

    [Fact]
    public async Task ExecuteAsync_MissingKey_EmitsNull()
    {
        var ctx = BuildContext(
            inputs: new Dictionary<string, object?> { ["value"] = new Dictionary<string, object?> { ["foo"] = 1 } },
            properties: new Dictionary<string, object?> { ["keys"] = Keys("foo", "missing") });

        var result = await _module.ExecuteAsync(ctx);

        result.Outputs["foo"].Should().Be(1);
        result.Outputs["missing"].Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_RestPortCollectsRemainder()
    {
        var ctx = BuildContext(
            inputs: new Dictionary<string, object?> { ["value"] = new Dictionary<string, object?> { ["foo"] = 1, ["bar"] = 2, ["baz"] = 3 } },
            properties: new Dictionary<string, object?> { ["keys"] = Keys("foo"), ["restPort"] = "rest" });

        var result = await _module.ExecuteAsync(ctx);

        var rest = result.Outputs["rest"].Should().BeAssignableTo<Dictionary<string, object?>>().Which;
        rest.Should().BeEquivalentTo(new Dictionary<string, object?> { ["bar"] = 2, ["baz"] = 3 });
    }

    [Fact]
    public async Task ExecuteAsync_RestPortEmitsEmptyObjectWhenNothingRemains()
    {
        var ctx = BuildContext(
            inputs: new Dictionary<string, object?> { ["value"] = new Dictionary<string, object?> { ["foo"] = 1 } },
            properties: new Dictionary<string, object?> { ["keys"] = Keys("foo"), ["restPort"] = "rest" });

        var result = await _module.ExecuteAsync(ctx);

        result.Outputs["rest"].Should().BeAssignableTo<Dictionary<string, object?>>().Which.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_NoRestPort_DropsExtras()
    {
        var ctx = BuildContext(
            inputs: new Dictionary<string, object?> { ["value"] = new Dictionary<string, object?> { ["foo"] = 1, ["extra"] = 2 } },
            properties: new Dictionary<string, object?> { ["keys"] = Keys("foo") });

        var result = await _module.ExecuteAsync(ctx);

        result.Outputs.Should().ContainKey("foo");
        result.Outputs.Should().NotContainKey("extra");
    }

    [Fact]
    public async Task ExecuteAsync_NonObjectValue_Fails()
    {
        var result = await _module.ExecuteAsync(BuildContext(
            inputs: new Dictionary<string, object?> { ["value"] = new List<object?> { 1 } },
            properties: new Dictionary<string, object?> { ["keys"] = Keys("foo") }));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("object");
    }

    [Fact]
    public async Task ExecuteAsync_NullValue_Fails()
    {
        var result = await _module.ExecuteAsync(BuildContext(
            inputs: new Dictionary<string, object?> { ["value"] = null },
            properties: new Dictionary<string, object?> { ["keys"] = Keys("foo") }));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("null");
    }

    [Fact]
    public async Task ExecuteAsync_InputPortWinsOverProperty()
    {
        var result = await _module.ExecuteAsync(BuildContext(
            inputs: new Dictionary<string, object?> { ["value"] = new Dictionary<string, object?> { ["foo"] = "input" } },
            properties: new Dictionary<string, object?> { ["value"] = new Dictionary<string, object?> { ["foo"] = "property" }, ["keys"] = Keys("foo") }));

        result.Outputs["foo"].Should().Be("input");
    }

    [Fact]
    public void ValidateConfiguration_MissingKeys_Fails()
    {
        var result = _module.ValidateConfiguration(new Dictionary<string, object?>());

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "MISSING_KEYS");
    }

    [Fact]
    public void ValidateConfiguration_EmptyArray_Fails()
    {
        var result = _module.ValidateConfiguration(new Dictionary<string, object?> { ["keys"] = new List<object?>() });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "EMPTY_KEYS");
    }

    [Fact]
    public void ValidateConfiguration_DuplicateKeys_Fails()
    {
        var result = _module.ValidateConfiguration(new Dictionary<string, object?> { ["keys"] = Keys("foo", "foo") });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "DUPLICATE_KEYS");
    }

    [Fact]
    public void ValidateConfiguration_RestPortCollides_Fails()
    {
        var result = _module.ValidateConfiguration(new Dictionary<string, object?> { ["keys"] = Keys("foo"), ["restPort"] = "foo" });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "RESTPORT_COLLIDES");
    }
}
