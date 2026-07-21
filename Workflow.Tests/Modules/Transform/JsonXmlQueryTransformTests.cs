// <copyright file="JsonXmlQueryTransformTests.cs" company="GlutenFree">
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
/// 🎯🏷️📝 Phase 2.6.a.3 — tests for jsonquery, xmlquery, and json transform modules~ ✨.
/// </summary>
public sealed class JsonXmlQueryTransformTests : TransformModuleTestBase
{
    private readonly JsonQueryModule jsonQuery = new();
    private readonly XmlQueryModule xmlQuery = new();
    private readonly JsonTransformModule jsonTransform = new();

    [Fact]
    public void Modules_Metadata_AreValid()
    {
        this.jsonQuery.ModuleId.Should().Be("builtin.transform.jsonquery");
        this.xmlQuery.ModuleId.Should().Be("builtin.transform.xmlquery");
        this.jsonTransform.ModuleId.Should().Be("builtin.transform.json");
        var v = new ModuleValidator();
        v.Validate(this.jsonQuery).IsValid.Should().BeTrue();
        v.Validate(this.xmlQuery).IsValid.Should().BeTrue();
        v.Validate(this.jsonTransform).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task JsonQuery_SingleMatch_Unwrapped()
    {
        var data = Rec(("user", Rec(("id", "abc"))));
        var result = await this.jsonQuery.ExecuteAsync(this.Context(
            new() { ["path"] = "$.user.id" },
            new() { ["data"] = data }));

        result.Success.Should().BeTrue();
        result.Outputs["result"].Should().Be("abc");
        result.Outputs["matchCount"].Should().Be(1);
    }

    [Fact]
    public async Task JsonQuery_FilterExpression_MultiMatch()
    {
        var data = Rec(("items", new List<object?>
        {
            Rec(("name", "a"), ("price", 5L)),
            Rec(("name", "b"), ("price", 20L)),
            Rec(("name", "c"), ("price", 30L)),
        }));

        var result = await this.jsonQuery.ExecuteAsync(this.Context(
            new() { ["path"] = "$.items[?(@.price > 10)].name" },
            new() { ["data"] = data }));

        result.Outputs["matchCount"].Should().Be(2);
    }

    [Fact]
    public async Task JsonQuery_NoMatch_RequiredFails()
    {
        var result = await this.jsonQuery.ExecuteAsync(this.Context(
            new() { ["path"] = "$.nope", ["required"] = true },
            new() { ["data"] = Rec(("a", 1L)) }));

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task JsonQuery_AcceptsJsonString()
    {
        var result = await this.jsonQuery.ExecuteAsync(this.Context(
            new() { ["path"] = "$.x" },
            new() { ["data"] = "{\"x\":42}" }));

        result.Outputs["result"].Should().Be(42L);
    }

    [Fact]
    public async Task XmlQuery_XPath_SelectsElements()
    {
        var xml = "<items><item><name>a</name></item><item><name>b</name></item></items>";
        var result = await this.xmlQuery.ExecuteAsync(this.Context(
            new() { ["xpath"] = "//item/name" },
            new() { ["data"] = xml }));

        result.Success.Should().BeTrue();
        result.Outputs["matchCount"].Should().Be(2);
    }

    [Fact]
    public async Task XmlQuery_RawString_XxeRefused()
    {
        var xml = "<?xml version=\"1.0\"?><!DOCTYPE foo [<!ENTITY xxe SYSTEM \"file:///etc/passwd\">]><foo>&xxe;</foo>";
        var result = await this.xmlQuery.ExecuteAsync(this.Context(
            new() { ["xpath"] = "/foo" },
            new() { ["data"] = xml }));

        result.Success.Should().BeFalse("DTD processing must be prohibited (XXE)~ 🛡️");
    }

    [Fact]
    public async Task JsonTransform_Merge_Rfc7396_NullRemoves()
    {
        var data = Rec(("a", 1L), ("b", 2L));
        var other = Rec(("b", (object?)null), ("c", 3L));

        var result = await this.jsonTransform.ExecuteAsync(this.Context(
            new() { ["operation"] = "merge" },
            new() { ["data"] = data, ["other"] = other }));

        var merged = (IReadOnlyDictionary<string, object?>)result.Outputs["result"]!;
        merged.Should().ContainKey("a");
        merged.Should().NotContainKey("b", "null in merge-patch removes the key~");
        merged["c"].Should().Be(3L);
    }

    [Fact]
    public async Task JsonTransform_Diff_ReportsChanges()
    {
        var left = Rec(("a", 1L), ("b", 2L));
        var right = Rec(("a", 1L), ("b", 99L), ("c", 3L));

        var result = await this.jsonTransform.ExecuteAsync(this.Context(
            new() { ["operation"] = "diff" },
            new() { ["data"] = left, ["other"] = right }));

        var changes = (List<object?>)result.Outputs["result"]!;
        changes.Should().NotBeEmpty();
    }

    [Fact]
    public async Task JsonTransform_FlattenUnflatten_RoundTrips()
    {
        var data = Rec(("user", Rec(("name", "Ada"), ("addr", Rec(("city", "Paris"))))));

        var flat = await this.jsonTransform.ExecuteAsync(this.Context(
            new() { ["operation"] = "flatten" },
            new() { ["data"] = data }));
        var flatDict = (IReadOnlyDictionary<string, object?>)flat.Outputs["result"]!;
        flatDict.Should().ContainKey("user.addr.city");

        var back = await this.jsonTransform.ExecuteAsync(this.Context(
            new() { ["operation"] = "unflatten" },
            new() { ["data"] = flatDict }));
        var backDict = (IReadOnlyDictionary<string, object?>)back.Outputs["result"]!;
        ((IReadOnlyDictionary<string, object?>)((IReadOnlyDictionary<string, object?>)backDict["user"]!)["addr"]!)["city"].Should().Be("Paris");
    }

    [Fact]
    public void JsonTransform_UnknownOperation_FailsValidation()
    {
        this.jsonTransform.ValidateConfiguration(new Dictionary<string, object?> { ["operation"] = "bogus" }).IsValid.Should().BeFalse();
    }
}
