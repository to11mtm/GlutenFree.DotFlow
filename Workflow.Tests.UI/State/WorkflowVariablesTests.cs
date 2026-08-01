// <copyright file="WorkflowVariablesTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.State;

using System.Text.Json;
using FluentAssertions;
using Workflow.UI.Client.Designer.State;

/// <summary>
/// 💾 Phase 3.5 (V1.1) — the workflow-variable declaration helper: parsing the wire shape,
/// lossless round-tripping, and the name rules.
/// </summary>
public class WorkflowVariablesTests
{
    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement.Clone();

    [Fact]
    public void Parse_ReadsNumericEnums_AsTheWireFormatCarriesThem()
    {
        // No JsonStringEnumConverter is registered anywhere in the solution, so type/seed arrive
        // as numbers. This is the shape the designer actually receives.
        var raw = Json("""{ "name": "count", "type": 1, "seed": 1, "isSecret": false, "initialValue": 5 }""");

        var parsed = WorkflowVariables.Parse("count", raw);

        parsed.Type.Should().Be(VariableValueType.Int);
        parsed.Seed.Should().Be(VariableSeed.AlwaysOverride);
        parsed.IsSecret.Should().BeFalse();
        parsed.InitialValue!.Value.GetInt32().Should().Be(5);
    }

    [Fact]
    public void Parse_AlsoAcceptsStringEnums_SoAConverterCouldBeAddedLater()
    {
        var raw = Json("""{ "name": "mode", "type": "Boolean", "seed": "AlwaysOverride" }""");

        var parsed = WorkflowVariables.Parse("mode", raw);

        parsed.Type.Should().Be(VariableValueType.Boolean);
        parsed.Seed.Should().Be(VariableSeed.AlwaysOverride);
    }

    [Fact]
    public void Parse_LegacyDeclarationWithoutNewFields_UsesSafeDefaults()
    {
        var raw = Json("""{ "name": "counter", "type": 1, "description": "A counter" }""");

        var parsed = WorkflowVariables.Parse("counter", raw);

        parsed.Seed.Should().Be(VariableSeed.SeedOnly);
        parsed.IsSecret.Should().BeFalse();
        parsed.InitialValue.Should().BeNull(because: "no initial value was declared");
        parsed.Description.Should().Be("A counter");
    }

    [Fact]
    public void Parse_NonObjectDeclaration_DegradesInsteadOfThrowing()
    {
        var parsed = WorkflowVariables.Parse("odd", Json("\"just a string\""));

        parsed.Name.Should().Be("odd");
        parsed.Type.Should().Be(VariableValueType.String);
    }

    [Fact]
    public void ToJson_PreservesUnknownFields()
    {
        // The designer must never be the reason a field it doesn't model disappears.
        var existing = Json("""
            { "name": "count", "type": 1, "somethingFuture": { "keep": true }, "alsoKeep": 7 }
            """);
        var edited = WorkflowVariables.Parse("count", existing) with { Description = "now documented" };

        var written = WorkflowVariables.ToJson(edited, existing);

        written.GetProperty("somethingFuture").GetProperty("keep").GetBoolean().Should().BeTrue();
        written.GetProperty("alsoKeep").GetInt32().Should().Be(7);
        written.GetProperty("description").GetString().Should().Be("now documented");
    }

    [Fact]
    public void ToJson_RoundTripsThroughParse()
    {
        var original = new WorkflowVariable(
            "apiHost",
            VariableValueType.String,
            JsonValues.FromString("https://example.com"),
            "Where orders go",
            VariableSeed.AlwaysOverride,
            IsSecret: true);

        var parsed = WorkflowVariables.Parse("apiHost", WorkflowVariables.ToJson(original));

        parsed.Should().BeEquivalentTo(original, o => o.Excluding(v => v.InitialValue));
        parsed.InitialValue!.Value.GetString().Should().Be("https://example.com");
    }

    [Fact]
    public void ToJson_NoInitialValue_OmitsTheKey()
    {
        var variable = new WorkflowVariable("x", VariableValueType.String, null, null, VariableSeed.SeedOnly, false);

        var written = WorkflowVariables.ToJson(variable);

        written.TryGetProperty("initialValue", out _).Should().BeFalse();
    }

    [Fact]
    public void ToJson_ExplicitJsonNull_IsPreservedAsDeclared()
    {
        // "declared, value null" is meaningfully different from "no initial value declared".
        var variable = new WorkflowVariable(
            "x", VariableValueType.String, JsonValues.Null, null, VariableSeed.SeedOnly, false);

        var written = WorkflowVariables.ToJson(variable);

        written.TryGetProperty("initialValue", out var initial).Should().BeTrue();
        initial.ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void ToJson_DropsCasingVariantsOfKnownFields()
    {
        // A definition that arrived with PascalCase keys must not end up with both spellings.
        var existing = Json("""{ "Name": "count", "Type": 1, "IsSecret": true }""");
        var edited = WorkflowVariables.Parse("count", existing);

        var written = WorkflowVariables.ToJson(edited, existing);

        written.TryGetProperty("Name", out _).Should().BeFalse();
        written.TryGetProperty("name", out _).Should().BeTrue();
        written.GetProperty("isSecret").GetBoolean().Should().BeTrue(because: "the value still parsed");
    }

    [Theory]
    [InlineData("count")]
    [InlineData("_private")]
    [InlineData("order.id")]
    [InlineData("A1")]
    public void ValidateName_AcceptsRuntimeLegalNames(string name)
        => WorkflowVariables.ValidateName(name, []).Should().BeNull();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1count")]
    [InlineData("has space")]
    [InlineData("has-dash")]
    [InlineData(".leading")]
    public void ValidateName_RejectsNamesTheRuntimeWouldReject(string name)
        => WorkflowVariables.ValidateName(name, []).Should().NotBeNull();

    [Fact]
    public void ValidateName_RejectsCaseInsensitiveCollision()
    {
        // PropertyBinder resolves names case-insensitively, so 'Count' would alias 'count' at run
        // time — reject it here rather than ship a confusing runtime bug (Q6).
        var error = WorkflowVariables.ValidateName("Count", ["count"]);

        error.Should().NotBeNull();
        error.Should().Contain("case-insensitive");
    }

    [Fact]
    public void ValidateName_AllowsAVariableToKeepItsOwnName()
        => WorkflowVariables.ValidateName("count", ["count"], currentName: "count").Should().BeNull();

    [Fact]
    public void ValidateName_ExactDuplicate_ReadsNaturally()
        => WorkflowVariables.ValidateName("count", ["count"]).Should().Be("'count' is already declared.");

    [Theory]
    [InlineData(VariableValueType.Int, "number")]
    [InlineData(VariableValueType.Decimal, "number")]
    [InlineData(VariableValueType.Boolean, "boolean")]
    [InlineData(VariableValueType.Object, "json")]
    [InlineData(VariableValueType.Array, "json")]
    [InlineData(VariableValueType.String, "text")]
    [InlineData(VariableValueType.Guid, "text")]
    public void EditorKindFor_MapsTypesToEditors(VariableValueType type, string expected)
        => WorkflowVariables.EditorKindFor(type).Should().Be(expected);

    [Fact]
    public void InitialValueFrom_KeepsNumbersNumeric()
        => WorkflowVariables.InitialValueFrom("42", VariableValueType.Int)!.Value
            .ValueKind.Should().Be(JsonValueKind.Number);

    [Fact]
    public void InitialValueFrom_BlankMeansNoInitialValue()
        => WorkflowVariables.InitialValueFrom("  ", VariableValueType.String).Should().BeNull();

    [Fact]
    public void InitialValueFrom_InvalidJson_ReturnsNullSoTheUiCanComplain()
        => WorkflowVariables.InitialValueFrom("{ not json", VariableValueType.Object).Should().BeNull();

    [Fact]
    public void List_OrdersByNameAndParsesEach()
    {
        var doc = new DesignerDocument { Name = "wf" };
        doc.Variables["zeta"] = Json("""{ "name": "zeta", "type": 0 }""");
        doc.Variables["alpha"] = Json("""{ "name": "alpha", "type": 4 }""");

        var listed = WorkflowVariables.List(doc);

        listed.Select(v => v.Name).Should().ContainInOrder("alpha", "zeta");
        listed[1].Type.Should().Be(VariableValueType.String);
    }

    [Fact]
    public void SelectableTypes_OmitsTheReferenceMarkers()
    {
        // Connection/Variable are module-property reference markers, not values a workflow
        // variable would hold.
        WorkflowVariables.SelectableTypes.Should().NotContain(VariableValueType.Connection);
        WorkflowVariables.SelectableTypes.Should().NotContain(VariableValueType.Variable);
        WorkflowVariables.SelectableTypes.Should().Contain(VariableValueType.String);
    }
}
