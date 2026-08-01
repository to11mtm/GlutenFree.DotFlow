// <copyright file="VariableEnumDriftTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.State;

using System;
using System.Linq;
using FluentAssertions;
using Workflow.UI.Client.Designer.State;
using CoreModels = Workflow.Core.Models;

/// <summary>
/// 🔒 Phase 3.5 (V1.1) drift-guard — the designer is framework-free (D2) and so mirrors the Core
/// enums rather than referencing them. Because no <c>JsonStringEnumConverter</c> is registered
/// anywhere in the solution, these enums cross the wire as <em>numbers</em>: the ordinals are the
/// contract.
/// </summary>
/// <remarks>
/// Without these tests, reordering <c>PropertyType</c> or <c>VariableSeedMode</c> would silently
/// mis-type every variable in the designer — a `Boolean` would quietly become a `Guid` — with no
/// compiler error anywhere. This is the same drift-guard pattern used for
/// <c>IWorkflowScriptApi</c> in Phase 3.4.1.
/// </remarks>
public class VariableEnumDriftTests
{
    [Fact]
    public void VariableValueType_MatchesCorePropertyType_NameAndOrdinal()
    {
        var core = Enum.GetValues<CoreModels.PropertyType>()
            .ToDictionary(v => v.ToString(), v => (int)v, StringComparer.Ordinal);
        var ui = Enum.GetValues<VariableValueType>()
            .ToDictionary(v => v.ToString(), v => (int)v, StringComparer.Ordinal);

        ui.Should().BeEquivalentTo(
            core,
            because: "the designer mirrors PropertyType and the numeric ordinals are the wire contract");
    }

    [Fact]
    public void VariableSeed_MatchesCoreVariableSeedMode_NameAndOrdinal()
    {
        var core = Enum.GetValues<CoreModels.VariableSeedMode>()
            .ToDictionary(v => v.ToString(), v => (int)v, StringComparer.Ordinal);
        var ui = Enum.GetValues<VariableSeed>()
            .ToDictionary(v => v.ToString(), v => (int)v, StringComparer.Ordinal);

        ui.Should().BeEquivalentTo(core, because: "seed mode also crosses the wire as a number");
    }

    [Fact]
    public void CoreVariableDefinition_SerialisesToTheShapeTheDesignerParses()
    {
        // Golden-wire check: build a real Core definition, serialise it the way the API does, and
        // parse it with the designer's helper. This catches a rename of any field the UI reads.
        var definition = new CoreModels.VariableDefinition(
            "count",
            CoreModels.PropertyType.Int,
            System.Text.Json.JsonDocument.Parse("7").RootElement,
            "Orders processed",
            CoreModels.VariableSeedMode.AlwaysOverride,
            IsSecret: false);

        var wire = System.Text.Json.JsonSerializer.SerializeToElement(
            definition,
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            });

        var parsed = WorkflowVariables.Parse("count", wire);

        parsed.Type.Should().Be(VariableValueType.Int);
        parsed.Seed.Should().Be(VariableSeed.AlwaysOverride);
        parsed.Description.Should().Be("Orders processed");
        parsed.IsSecret.Should().BeFalse();
        parsed.InitialValue!.Value.GetInt32().Should().Be(7);
    }
}
