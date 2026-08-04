// <copyright file="PartitionModuleTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.Flow;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin.Flow;
using Xunit;

/// <summary>
/// 🪓 Tests for <see cref="PartitionModule"/> (<c>builtin.partition</c>) — routing, dynamic outputs,
/// counts, value resolution, and configuration validation~ ✨💖
/// </summary>
public sealed class PartitionModuleTests
{
    private readonly PartitionModule _module = new();

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
            NodeId = "partition-node",
        };

    private static List<object?> Rules(params (object? Match, string Port)[] rules)
        => rules.Select(r => (object?)new Dictionary<string, object?> { ["match"] = r.Match, ["port"] = r.Port }).ToList();

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    [Fact]
    public void PartitionModule_Metadata_IsCorrect()
    {
        _module.ModuleId.Should().Be("builtin.partition");
        _module.DisplayName.Should().Be("Partition");
        _module.Category.Should().Be("Flow Control");
        _module.Icon.Should().Be("🪓");
        _module.Version.Should().Be(new Version(1, 0, 0));
        _module.Schema.Outputs.ToList().Should().BeEmpty("partition output ports are dynamic~ 🎗️");
    }

    [Fact]
    public async Task ExecuteAsync_PartitionsByItemValue_WithMultipleRulesToSamePort()
    {
        var ctx = BuildContext(
            inputs: new Dictionary<string, object?> { ["items"] = new List<object?> { "Foo", "Bar", "Baz", "Foo" } },
            properties: new Dictionary<string, object?> { ["rules"] = Rules(("Foo", "foos"), ("Bar", "others"), ("Baz", "others")) });

        var result = await _module.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        result.ActivePorts.Should().BeNull("plain Ok means all output ports fire~ 🔥");
        result.Outputs["foos"].Should().BeAssignableTo<List<object?>>().Which.Should().Equal("Foo", "Foo");
        result.Outputs["others"].Should().BeAssignableTo<List<object?>>().Which.Should().Equal("Bar", "Baz");
    }

    [Fact]
    public async Task ExecuteAsync_MatchOnPath_WorksForDictionaryItems()
    {
        var items = new List<object?>
        {
            new Dictionary<string, object?> { ["kind"] = "foo", ["id"] = 1 },
            new Dictionary<string, object?> { ["kind"] = "bar", ["id"] = 2 },
        };
        var ctx = BuildContext(
            inputs: new Dictionary<string, object?> { ["items"] = items },
            properties: new Dictionary<string, object?> { ["matchOn"] = "kind", ["rules"] = Rules(("foo", "foos"), ("bar", "bars")) });

        var result = await _module.ExecuteAsync(ctx);

        result.Outputs["foos"].Should().BeAssignableTo<List<object?>>().Which.Should().ContainSingle().Which.Should().BeSameAs(items[0]);
        result.Outputs["bars"].Should().BeAssignableTo<List<object?>>().Which.Should().ContainSingle().Which.Should().BeSameAs(items[1]);
    }

    [Fact]
    public async Task ExecuteAsync_MatchOnPath_WorksForJsonElementItems()
    {
        using var doc = JsonDocument.Parse("""[{"kind":"foo","id":1},{"kind":"bar","id":2}]""");
        var ctx = BuildContext(
            inputs: new Dictionary<string, object?> { ["items"] = doc.RootElement.Clone() },
            properties: new Dictionary<string, object?> { ["matchOn"] = "kind", ["rules"] = Rules(("foo", "foos"), ("bar", "bars")) });

        var result = await _module.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        result.Outputs["foos"].Should().BeAssignableTo<List<object?>>().Which.Should().ContainSingle();
        result.Outputs["bars"].Should().BeAssignableTo<List<object?>>().Which.Should().ContainSingle();
    }

    [Fact]
    public async Task ExecuteAsync_NestedMatchOnPath_Works()
    {
        var items = new List<object?>
        {
            new Dictionary<string, object?> { ["payload"] = new Dictionary<string, object?> { ["type"] = "order" } },
            new Dictionary<string, object?> { ["payload"] = new Dictionary<string, object?> { ["type"] = "invoice" } },
        };
        var ctx = BuildContext(
            inputs: new Dictionary<string, object?> { ["items"] = items },
            properties: new Dictionary<string, object?> { ["matchOn"] = "payload.type", ["rules"] = Rules(("order", "orders"), ("invoice", "invoices")) });

        var result = await _module.ExecuteAsync(ctx);

        result.Outputs["orders"].Should().BeAssignableTo<List<object?>>().Which.Should().ContainSingle();
        result.Outputs["invoices"].Should().BeAssignableTo<List<object?>>().Which.Should().ContainSingle();
    }

    [Fact]
    public async Task ExecuteAsync_EmptyLegEmitsEmptyList_AndPreservesOriginalOrder()
    {
        var ctx = BuildContext(
            inputs: new Dictionary<string, object?> { ["items"] = new List<object?> { "Bar", "Foo", "Bar" } },
            properties: new Dictionary<string, object?> { ["rules"] = Rules(("Foo", "foos"), ("Bar", "bars"), ("Baz", "bazs")) });

        var result = await _module.ExecuteAsync(ctx);

        result.Outputs["bars"].Should().BeAssignableTo<List<object?>>().Which.Should().Equal("Bar", "Bar");
        result.Outputs["foos"].Should().BeAssignableTo<List<object?>>().Which.Should().Equal("Foo");
        result.Outputs["bazs"].Should().BeAssignableTo<List<object?>>().Which.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_UnmatchedWithoutDefault_FailsAndListsValues()
    {
        var ctx = BuildContext(
            inputs: new Dictionary<string, object?> { ["items"] = new List<object?> { "Foo", "Qux" } },
            properties: new Dictionary<string, object?> { ["rules"] = Rules(("Foo", "foos")) });

        var result = await _module.ExecuteAsync(ctx);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Qux");
    }

    [Fact]
    public async Task ExecuteAsync_UnmatchedWithDefault_UsesDefaultPort()
    {
        var ctx = BuildContext(
            inputs: new Dictionary<string, object?> { ["items"] = new List<object?> { "Foo", "Qux" } },
            properties: new Dictionary<string, object?> { ["rules"] = Rules(("Foo", "foos")), ["defaultPort"] = "other" });

        var result = await _module.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        result.Outputs["other"].Should().BeAssignableTo<List<object?>>().Which.Should().Equal("Qux");
    }

    [Fact]
    public async Task ExecuteAsync_CaseSensitive_ControlsStringMatching()
    {
        var insensitive = await _module.ExecuteAsync(BuildContext(
            inputs: new Dictionary<string, object?> { ["items"] = new List<object?> { "FOO" } },
            properties: new Dictionary<string, object?> { ["rules"] = Rules(("foo", "foos")) }));
        var sensitive = await _module.ExecuteAsync(BuildContext(
            inputs: new Dictionary<string, object?> { ["items"] = new List<object?> { "FOO" } },
            properties: new Dictionary<string, object?> { ["rules"] = Rules(("foo", "foos")), ["caseSensitive"] = true, ["defaultPort"] = "other" }));

        insensitive.Outputs["foos"].Should().BeAssignableTo<List<object?>>().Which.Should().ContainSingle();
        sensitive.Outputs["other"].Should().BeAssignableTo<List<object?>>().Which.Should().ContainSingle();
    }

    [Fact]
    public async Task ExecuteAsync_UsesItemsFromPropertyJsonString()
    {
        var ctx = BuildContext(properties: new Dictionary<string, object?>
        {
            ["items"] = "[\"Foo\",\"Bar\"]",
            ["rules"] = Rules(("Foo", "foos"), ("Bar", "bars")),
        });

        var result = await _module.ExecuteAsync(ctx);

        result.Outputs["foos"].Should().BeAssignableTo<List<object?>>().Which.Should().Equal("Foo");
        result.Outputs["bars"].Should().BeAssignableTo<List<object?>>().Which.Should().Equal("Bar");
    }

    [Fact]
    public async Task ExecuteAsync_InputItemsWinOverPropertyItems()
    {
        var ctx = BuildContext(
            inputs: new Dictionary<string, object?> { ["items"] = new List<object?> { "Input" } },
            properties: new Dictionary<string, object?> { ["items"] = new List<object?> { "Property" }, ["rules"] = Rules(("Input", "inputs"), ("Property", "properties")) });

        var result = await _module.ExecuteAsync(ctx);

        result.Outputs["inputs"].Should().BeAssignableTo<List<object?>>().Which.Should().Equal("Input");
        result.Outputs["properties"].Should().BeAssignableTo<List<object?>>().Which.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_SingleNonListItem_WrapsAndRoutes()
    {
        var ctx = BuildContext(
            inputs: new Dictionary<string, object?> { ["items"] = "Foo" },
            properties: new Dictionary<string, object?> { ["rules"] = Rules(("Foo", "foos")) });

        var result = await _module.ExecuteAsync(ctx);

        result.Outputs["foos"].Should().BeAssignableTo<List<object?>>().Which.Should().Equal("Foo");
    }

    [Fact]
    public async Task ExecuteAsync_EmitsCountsAndTotal()
    {
        var ctx = BuildContext(
            inputs: new Dictionary<string, object?> { ["items"] = new List<object?> { "Foo", "Bar", "Foo" } },
            properties: new Dictionary<string, object?> { ["rules"] = Rules(("Foo", "foos"), ("Bar", "bars"), ("Baz", "bazs")) });

        var result = await _module.ExecuteAsync(ctx);

        result.Outputs["total"].Should().Be(3);
        var counts = result.Outputs["counts"].Should().BeAssignableTo<Dictionary<string, int>>().Which;
        counts.Should().Contain(new KeyValuePair<string, int>("foos", 2));
        counts.Should().Contain(new KeyValuePair<string, int>("bars", 1));
        counts.Should().Contain(new KeyValuePair<string, int>("bazs", 0));
    }

    [Fact]
    public void ValidateConfiguration_MissingRules_Fails()
    {
        var result = _module.ValidateConfiguration(new Dictionary<string, object?>());

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "MISSING_RULES");
    }

    [Fact]
    public void ValidateConfiguration_MalformedJson_Fails()
    {
        var result = _module.ValidateConfiguration(new Dictionary<string, object?> { ["rules"] = "not json" });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "INVALID_RULES");
    }

    [Fact]
    public void ValidateConfiguration_BlankPort_Fails()
    {
        var result = _module.ValidateConfiguration(new Dictionary<string, object?> { ["rules"] = Rules(("Foo", " ")) });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "INVALID_RULE_PORT");
    }
}
