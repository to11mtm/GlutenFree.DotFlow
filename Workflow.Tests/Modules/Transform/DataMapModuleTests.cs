// <copyright file="DataMapModuleTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.Transform;

using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Workflow.Modules.Builtin.Transform;
using Workflow.Modules.Validation;
using Xunit;

/// <summary>
/// 🔄 Phase 2.6.a.1 — tests for <see cref="DataMapModule"/>~ ✨.
/// </summary>
public sealed class DataMapModuleTests : TransformModuleTestBase
{
    private readonly DataMapModule module = new();

    [Fact]
    public void MapModule_Metadata_IsCorrect()
    {
        this.module.ModuleId.Should().Be("builtin.transform.map");
        this.module.Category.Should().Be("Transformation");
        new ModuleValidator().Validate(this.module).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Map_SimpleRename_Works()
    {
        var source = Rec(("firstName", "Ada"), ("years", 36L));
        var result = await this.module.ExecuteAsync(this.Context(
            new() { ["mapping"] = new Dictionary<string, object?> { ["name"] = "firstName", ["age"] = "years" } },
            new() { ["source"] = source }));

        result.Success.Should().BeTrue();
        var mapped = (IReadOnlyDictionary<string, object?>)result.Outputs["result"]!;
        mapped["name"].Should().Be("Ada");
        mapped["age"].Should().Be(36L);
    }

    [Fact]
    public async Task Map_NestedPath_Resolves()
    {
        var source = Rec(("user", Rec(("address", Rec(("city", "Paris"))))));
        var result = await this.module.ExecuteAsync(this.Context(
            new() { ["mapping"] = new Dictionary<string, object?> { ["city"] = "user.address.city" } },
            new() { ["source"] = source }));

        ((IReadOnlyDictionary<string, object?>)result.Outputs["result"]!)["city"].Should().Be("Paris");
    }

    [Fact]
    public async Task Map_MissingPath_UsesDefault()
    {
        var result = await this.module.ExecuteAsync(this.Context(
            new()
            {
                ["mapping"] = new Dictionary<string, object?>
                {
                    ["country"] = new Dictionary<string, object?> { ["path"] = "user.country", ["default"] = "Unknown" },
                },
            },
            new() { ["source"] = Rec(("user", Rec(("city", "Paris")))) }));

        ((IReadOnlyDictionary<string, object?>)result.Outputs["result"]!)["country"].Should().Be("Unknown");
    }

    [Fact]
    public async Task Map_Expression_ComputesValue()
    {
        var result = await this.module.ExecuteAsync(this.Context(
            new()
            {
                ["mapping"] = new Dictionary<string, object?>
                {
                    ["full"] = new Dictionary<string, object?> { ["expression"] = "item.first + ' ' + item.last" },
                },
            },
            new() { ["source"] = Rec(("first", "Ada"), ("last", "Lovelace")) }));

        ((IReadOnlyDictionary<string, object?>)result.Outputs["result"]!)["full"].Should().Be("Ada Lovelace");
    }

    [Fact]
    public async Task Map_ConditionalExpression_Works()
    {
        var result = await this.module.ExecuteAsync(this.Context(
            new()
            {
                ["mapping"] = new Dictionary<string, object?>
                {
                    ["tier"] = new Dictionary<string, object?> { ["expression"] = "item.spend > 100 ? 'gold' : 'silver'" },
                },
            },
            new() { ["source"] = Rec(("spend", 150L)) }));

        ((IReadOnlyDictionary<string, object?>)result.Outputs["result"]!)["tier"].Should().Be("gold");
    }

    [Fact]
    public async Task Map_TypeConversion_IntFromString()
    {
        var result = await this.module.ExecuteAsync(this.Context(
            new()
            {
                ["mapping"] = new Dictionary<string, object?>
                {
                    ["age"] = new Dictionary<string, object?> { ["path"] = "ageStr", ["convert"] = "int" },
                },
            },
            new() { ["source"] = Rec(("ageStr", "42")) }));

        ((IReadOnlyDictionary<string, object?>)result.Outputs["result"]!)["age"].Should().Be(42);
    }

    [Fact]
    public async Task Map_TypeConversion_Invalid_FailsWithItemIndex()
    {
        var result = await this.module.ExecuteAsync(this.Context(
            new()
            {
                ["mapping"] = new Dictionary<string, object?>
                {
                    ["age"] = new Dictionary<string, object?> { ["path"] = "ageStr", ["convert"] = "int" },
                },
            },
            new() { ["source"] = new List<object?> { Rec(("ageStr", "notanumber")) } }));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("item #0");
    }

    [Fact]
    public async Task Map_ArrayInput_MapsEachRecord()
    {
        var source = new List<object?> { Rec(("n", "a")), Rec(("n", "b")) };
        var result = await this.module.ExecuteAsync(this.Context(
            new() { ["mapping"] = new Dictionary<string, object?> { ["name"] = "n" } },
            new() { ["source"] = source }));

        result.Outputs["count"].Should().Be(2);
        result.Outputs["result"].Should().BeAssignableTo<IReadOnlyList<object?>>();
    }

    [Fact]
    public async Task Map_Flatten_ProducesDottedKeys()
    {
        var result = await this.module.ExecuteAsync(this.Context(
            new()
            {
                ["mapping"] = new Dictionary<string, object?> { ["addr"] = "address" },
                ["flatten"] = true,
            },
            new() { ["source"] = Rec(("address", Rec(("city", "Paris"), ("zip", "75001")))) }));

        var mapped = (IReadOnlyDictionary<string, object?>)result.Outputs["result"]!;
        mapped.Should().ContainKey("addr.city");
        mapped["addr.city"].Should().Be("Paris");
    }

    [Fact]
    public async Task Map_IgnoreNulls_DropsKeys()
    {
        var result = await this.module.ExecuteAsync(this.Context(
            new()
            {
                ["mapping"] = new Dictionary<string, object?> { ["missing"] = "nope", ["present"] = "here" },
                ["ignoreNulls"] = true,
            },
            new() { ["source"] = Rec(("here", "yes")) }));

        var mapped = (IReadOnlyDictionary<string, object?>)result.Outputs["result"]!;
        mapped.Should().NotContainKey("missing");
        mapped.Should().ContainKey("present");
    }

    [Fact]
    public void Map_ValidateConfiguration_MissingMapping_Fails()
    {
        this.module.ValidateConfiguration(new Dictionary<string, object?>()).IsValid.Should().BeFalse();
    }
}
