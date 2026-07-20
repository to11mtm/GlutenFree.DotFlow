// <copyright file="JsonValuesTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.State;

using System.Text.Json;
using FluentAssertions;
using Workflow.UI.Client.Designer.State;
using Xunit;

/// <summary>
/// 🧪 Phase 3.3.b.3 — JSON-value plumbing specs (type preservation)~ ✨.
/// </summary>
public sealed class JsonValuesTests
{
    [Fact]
    public void FromNumber_StaysJsonNumber_NotString()
    {
        var el = JsonValues.FromNumber("42");
        el.ValueKind.Should().Be(JsonValueKind.Number);
        el.GetInt32().Should().Be(42);
    }

    [Fact]
    public void FromNumber_Decimal_Preserved()
    {
        var el = JsonValues.FromNumber("3.14");
        el.ValueKind.Should().Be(JsonValueKind.Number);
        el.GetDouble().Should().BeApproximately(3.14, 1e-9);
    }

    [Fact]
    public void FromNumber_NonNumeric_FallsBackToString()
    {
        JsonValues.FromNumber("abc").ValueKind.Should().Be(JsonValueKind.String);
    }

    [Fact]
    public void FromString_MakesStringElement()
    {
        var el = JsonValues.FromString("hi");
        el.ValueKind.Should().Be(JsonValueKind.String);
        el.GetString().Should().Be("hi");
    }

    [Fact]
    public void FromBool_And_AsBool_RoundTrip()
    {
        JsonValues.AsBool(JsonValues.FromBool(true)).Should().BeTrue();
        JsonValues.AsBool(JsonValues.FromBool(false)).Should().BeFalse();
    }

    [Fact]
    public void TryParseJson_Valid_ReturnsElement()
    {
        var el = JsonValues.TryParseJson("{\"a\":1}");
        el.Should().NotBeNull();
        el!.Value.GetProperty("a").GetInt32().Should().Be(1);
    }

    [Fact]
    public void TryParseJson_Invalid_ReturnsNull()
    {
        JsonValues.TryParseJson("{ not json").Should().BeNull();
    }

    [Fact]
    public void ToText_RendersEachKind()
    {
        JsonValues.ToText(JsonValues.FromString("x")).Should().Be("x");
        JsonValues.ToText(JsonValues.FromNumber("7")).Should().Be("7");
        JsonValues.ToText(JsonValues.FromBool(true)).Should().Be("true");
    }
}
