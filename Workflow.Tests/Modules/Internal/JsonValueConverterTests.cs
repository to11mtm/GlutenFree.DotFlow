// <copyright file="JsonValueConverterTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.Internal;

using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Workflow.Modules.Internal;
using Xunit;

/// <summary>
/// 📄 Tests for <see cref="JsonValueConverter"/> — behaviour parity + the iterative (no-stack-overflow)
/// guarantee for deeply-nested graphs~ ✨.
/// </summary>
public sealed class JsonValueConverterTests
{
    [Fact]
    public void ToClr_Object_MapsToDictionaryPreservingKeyOrder()
    {
        var node = JsonNode.Parse("{\"a\":1,\"b\":\"x\",\"c\":true,\"d\":null}");

        var result = JsonValueConverter.ToClr(node);

        var dict = result.Should().BeAssignableTo<IReadOnlyDictionary<string, object?>>().Subject;
        dict.Keys.Should().ContainInOrder("a", "b", "c", "d");
        dict["a"].Should().Be(1L);
        dict["b"].Should().Be("x");
        dict["c"].Should().Be(true);
        dict["d"].Should().BeNull();
    }

    [Fact]
    public void ToClr_Array_MapsToListPreservingOrder()
    {
        var node = JsonNode.Parse("[3,2,1]");

        var result = JsonValueConverter.ToClr(node);

        result.Should().BeAssignableTo<IReadOnlyList<object?>>()
            .Subject.Should().ContainInOrder(3L, 2L, 1L);
    }

    [Fact]
    public void ToClr_NestedGraph_RoundTripsShape()
    {
        var node = JsonNode.Parse("{\"user\":{\"name\":\"Ada\",\"tags\":[\"a\",\"b\"]},\"n\":2.5}");

        var dict = (IReadOnlyDictionary<string, object?>)JsonValueConverter.ToClr(node)!;
        var user = (IReadOnlyDictionary<string, object?>)dict["user"]!;
        user["name"].Should().Be("Ada");
        ((IReadOnlyList<object?>)user["tags"]!).Should().ContainInOrder("a", "b");
        dict["n"].Should().Be(2.5d);
    }

    [Fact]
    public void FromElement_Number_PreservesLongVsDouble()
    {
        using var intDoc = JsonDocument.Parse("42");
        using var dblDoc = JsonDocument.Parse("42.5");

        JsonValueConverter.FromElement(intDoc.RootElement).Should().Be(42L);
        JsonValueConverter.FromElement(dblDoc.RootElement).Should().Be(42.5d);
    }

    [Fact]
    public void FromElement_NestedGraph_RoundTripsShape()
    {
        using var doc = JsonDocument.Parse("{\"a\":[{\"b\":1}],\"c\":\"x\"}");

        var dict = (IReadOnlyDictionary<string, object?>)JsonValueConverter.FromElement(doc.RootElement)!;
        var list = (IReadOnlyList<object?>)dict["a"]!;
        ((IReadOnlyDictionary<string, object?>)list[0]!)["b"].Should().Be(1L);
        dict["c"].Should().Be("x");
    }

    [Theory]
    [InlineData(5000)]
    public void ToClr_DeeplyNestedObjects_DoesNotStackOverflow(int depth)
    {
        // Build {"a":{"a":{...:{"a":1}}}} nested `depth` levels deep — recursion would blow the stack~ 🛡️
        var sb = new StringBuilder();
        for (var i = 0; i < depth; i++)
        {
            sb.Append("{\"a\":");
        }

        sb.Append('1');
        for (var i = 0; i < depth; i++)
        {
            sb.Append('}');
        }

        var node = JsonNode.Parse(sb.ToString(), documentOptions: new JsonDocumentOptions { MaxDepth = depth + 8 });

        var result = JsonValueConverter.ToClr(node);

        // Walk down to the bottom to confirm the whole graph converted~
        var current = result;
        for (var i = 0; i < depth; i++)
        {
            current = ((IReadOnlyDictionary<string, object?>)current!)["a"];
        }

        current.Should().Be(1L);
    }

    [Theory]
    [InlineData(5000)]
    public void ToClr_DeeplyNestedArrays_DoesNotStackOverflow(int depth)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < depth; i++)
        {
            sb.Append('[');
        }

        sb.Append('7');
        for (var i = 0; i < depth; i++)
        {
            sb.Append(']');
        }

        var node = JsonNode.Parse(sb.ToString(), documentOptions: new JsonDocumentOptions { MaxDepth = depth + 8 });

        var result = JsonValueConverter.ToClr(node);

        var current = result;
        for (var i = 0; i < depth; i++)
        {
            current = ((IReadOnlyList<object?>)current!)[0];
        }

        current.Should().Be(7L);
    }

    [Theory]
    [InlineData(5000)]
    public void FromElement_DeeplyNested_DoesNotStackOverflow(int depth)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < depth; i++)
        {
            sb.Append("{\"a\":");
        }

        sb.Append("true");
        for (var i = 0; i < depth; i++)
        {
            sb.Append('}');
        }

        using var doc = JsonDocument.Parse(sb.ToString(), new JsonDocumentOptions { MaxDepth = depth + 8 });

        var result = JsonValueConverter.FromElement(doc.RootElement);

        var current = result;
        for (var i = 0; i < depth; i++)
        {
            current = ((IReadOnlyDictionary<string, object?>)current!)["a"];
        }

        current.Should().Be(true);
    }

    [Fact]
    public void ToClr_NullAndScalarRoots_ConvertDirectly()
    {
        JsonValueConverter.ToClr(null).Should().BeNull();
        JsonValueConverter.ToClr(JsonNode.Parse("\"hi\"")).Should().Be("hi");
        JsonValueConverter.ToClr(JsonNode.Parse("true")).Should().Be(true);
        JsonValueConverter.ToClr(JsonNode.Parse("7")).Should().Be(7L);
    }
}
