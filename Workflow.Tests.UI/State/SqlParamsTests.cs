// <copyright file="SqlParamsTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.State;

using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using Workflow.UI.Client.Api.Dtos;
using Workflow.UI.Client.Designer.State;
using Xunit;

/// <summary>
/// 🧪 Round-2 G9 — Unit tests for <see cref="SqlParams"/> (safe SQL parameter builder)~ ✨.
/// </summary>
public sealed class SqlParamsTests
{
    private static ModuleSchemaDto Schema(params ModulePropertyDefinitionDto[] props)
        => new(new(), new(), new List<ModulePropertyDefinitionDto>(props));

    private static ModulePropertyDefinitionDto Prop(string name, string editor)
        => new(name, name, "String", null, false, null, editor, null);

    [Fact]
    public void FindSqlProperty_MatchesQueryCommandSql_WithParametersMap()
    {
        SqlParams.FindSqlProperty(Schema(Prop("query", "Code"), Prop("parameters", "Json")))
            .Should().Be("query");
        SqlParams.FindSqlProperty(Schema(Prop("command", "Code"), Prop("parameters", "Json")))
            .Should().Be("command");
        SqlParams.FindSqlProperty(Schema(Prop("query", "Code")))
            .Should().BeNull(because: "no parameters map → the builder can't bind safely~");
        SqlParams.FindSqlProperty(Schema(Prop("code", "Code"), Prop("parameters", "Json")))
            .Should().BeNull(because: "a generic code editor isn't SQL~");
        SqlParams.FindSqlProperty(null).Should().BeNull();
    }

    [Theory]
    [InlineData("@id", "id")]
    [InlineData(":name", "name")]
    [InlineData("max id!", "maxid")]
    [InlineData("  ", null)]
    [InlineData("@@", null)]
    public void SanitizeName_StripsPrefixAndInvalidChars(string raw, string? expected)
        => SqlParams.SanitizeName(raw).Should().Be(expected);

    [Fact]
    public void Upsert_TypesValues_NumbersBoolsText()
    {
        var one = SqlParams.Upsert(null, "id", "42");
        var two = SqlParams.Upsert(one, "active", "true");
        var three = SqlParams.Upsert(two, "name", "Ami");

        three.GetProperty("id").ValueKind.Should().Be(JsonValueKind.Number);
        three.GetProperty("active").ValueKind.Should().Be(JsonValueKind.True);
        three.GetProperty("name").GetString().Should().Be("Ami");
    }

    [Fact]
    public void Upsert_ReplacesExisting_AndRemoveDeletes()
    {
        var p = SqlParams.Upsert(null, "id", "1");
        p = SqlParams.Upsert(p, "id", "2");
        p.GetProperty("id").GetInt32().Should().Be(2);

        p = SqlParams.Remove(p, "id");
        SqlParams.Parse(p).Should().BeEmpty();
    }

    [Fact]
    public void Parse_ListsEntries()
    {
        var p = SqlParams.Upsert(SqlParams.Upsert(null, "id", "42"), "name", "Ami");

        SqlParams.Parse(p).Should().BeEquivalentTo(new[] { ("id", "42"), ("name", "Ami") });
    }
}
