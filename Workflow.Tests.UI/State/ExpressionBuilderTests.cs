// <copyright file="ExpressionBuilderTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.State;

using FluentAssertions;
using Workflow.UI.Client.Designer.State;
using Xunit;

/// <summary>
/// 🧪 Round-2 G8 — Unit tests for <see cref="ExpressionBuilder"/> (ƒx composition)~ ✨.
/// </summary>
public sealed class ExpressionBuilderTests
{
    [Fact]
    public void Compose_NoOperator_ReturnsPlainToken()
        => ExpressionBuilder.Compose("{{Variable.count}}", ExpressionBuilder.NoOperator, "ignored")
            .Should().Be("{{Variable.count}}");

    [Fact]
    public void Compose_NoValue_ReturnsPlainToken()
        => ExpressionBuilder.Compose("{{http-1.body}}", ">", "  ")
            .Should().Be("{{http-1.body}}");

    [Theory]
    [InlineData("5", "{{Variable.count > 5}}")]
    [InlineData("2.5", "{{Variable.count > 2.5}}")]
    [InlineData("true", "{{Variable.count > true}}")]
    public void Compose_NumericAndBoolValues_StayRaw(string value, string expected)
        => ExpressionBuilder.Compose("{{Variable.count}}", ">", value).Should().Be(expected);

    [Fact]
    public void Compose_StringValue_IsAutoQuoted()
        => ExpressionBuilder.Compose("{{Variable.status}}", "==", "done")
            .Should().Be("{{Variable.status == \"done\"}}");

    [Fact]
    public void Compose_AlreadyQuotedValue_NotDoubleQuoted()
        => ExpressionBuilder.Compose("{{Variable.status}}", "==", "\"done\"")
            .Should().Be("{{Variable.status == \"done\"}}");

    [Fact]
    public void Compose_AcceptsBareReference()
        => ExpressionBuilder.Compose("node-1.output", "!=", "3")
            .Should().Be("{{node-1.output != 3}}");

    [Fact]
    public void InnerOf_StripsBraces()
    {
        ExpressionBuilder.InnerOf("{{Variable.x}}").Should().Be("Variable.x");
        ExpressionBuilder.InnerOf("Variable.x").Should().Be("Variable.x");
    }
}
