// ────────────────────────────────────────────────────────────────────────────────
// Copyright © GlutenFree 2025. Made with love by Ami-Chan! UwU 💖
// Licensed under the MIT License.
// ────────────────────────────────────────────────────────────────────────────────

using System.Text.Json;
using FluentAssertions;
using LanguageExt;
using Workflow.Core.Models;
using Workflow.Engine.Models;

namespace Workflow.Tests.Engine;

/// <summary>
/// 🌱 Phase 3.5 (V2/V3) — the variable layering rules. These are pure so the precedence chain can
/// be pinned down without spinning up an execution.
/// </summary>
public class InitialVariablesTests
{
    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement.Clone();

    private static WorkflowDefinition WorkflowWith(params VariableDefinition[] variables)
        => new(
            Id: Guid.NewGuid(),
            Name: "wf",
            Description: null,
            Version: new Version(1, 0, 0),
            Nodes: Arr<NodeDefinition>.Empty,
            Connections: Arr<ConnectionDefinition>.Empty,
            Variables: variables.Select(v => (v.Name, v)).ToHashMap(),
            Trigger: null,
            ErrorHandling: null,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: null,
            Tags: null);

    private static Dictionary<string, object?> Values(params (string Name, object? Value)[] pairs)
        => pairs.ToDictionary(p => p.Name, p => p.Value);

    [Fact]
    public void DeclaredInitialValue_ReachesTheRun()
    {
        // The whole point of V2: before this, declaring a variable had no runtime effect at all.
        var definition = WorkflowWith(new VariableDefinition("count", PropertyType.Int, Json("5")));

        var resolved = InitialVariables.Resolve(definition, null);

        resolved.Variables["count"].Should().Be(5L);
    }

    [Fact]
    public void RunInputs_BeatDeclaredValues()
    {
        var definition = WorkflowWith(new VariableDefinition("count", PropertyType.Int, Json("5")));

        var resolved = InitialVariables.Resolve(definition, Values(("count", 99L)));

        resolved.Variables["count"].Should().Be(99L, because: "an ad-hoc run must stay easy to steer");
    }

    [Fact]
    public void SeedOnly_YieldsToAValueAPreviousRunPersisted()
    {
        var definition = WorkflowWith(
            new VariableDefinition("count", PropertyType.Int, Json("5"), Seed: VariableSeedMode.SeedOnly));

        var resolved = InitialVariables.Resolve(definition, null, workflowScoped: Values(("count", 42L)));

        resolved.Variables["count"].Should().Be(42L, because: "SeedOnly only fills a gap");
    }

    [Fact]
    public void SeedOnly_StillAppliesWhenNothingIsStored()
    {
        var definition = WorkflowWith(
            new VariableDefinition("count", PropertyType.Int, Json("5"), Seed: VariableSeedMode.SeedOnly));

        var resolved = InitialVariables.Resolve(definition, null);

        resolved.Variables["count"].Should().Be(5L);
    }

    [Fact]
    public void AlwaysOverride_BeatsTheStoredValue()
    {
        var definition = WorkflowWith(
            new VariableDefinition("count", PropertyType.Int, Json("5"), Seed: VariableSeedMode.AlwaysOverride));

        var resolved = InitialVariables.Resolve(definition, null, workflowScoped: Values(("count", 42L)));

        resolved.Variables["count"].Should().Be(5L, because: "AlwaysOverride resets on every run");
    }

    [Fact]
    public void RunInputs_BeatEvenAlwaysOverride()
    {
        var definition = WorkflowWith(
            new VariableDefinition("count", PropertyType.Int, Json("5"), Seed: VariableSeedMode.AlwaysOverride));

        var resolved = InitialVariables.Resolve(
            definition,
            Values(("count", 99L)),
            workflowScoped: Values(("count", 42L)));

        resolved.Variables["count"].Should().Be(99L);
    }

    [Fact]
    public void WorkflowScope_BeatsGlobalScope()
    {
        var definition = WorkflowWith();

        var resolved = InitialVariables.Resolve(
            definition,
            null,
            globalScoped: Values(("apiHost", "global.example.com")),
            workflowScoped: Values(("apiHost", "workflow.example.com")));

        resolved.Variables["apiHost"].Should().Be("workflow.example.com");
    }

    [Fact]
    public void GlobalScope_IsVisibleWhenNothingElseSuppliesIt()
    {
        // V3's headline: a value another workflow set can finally be referenced.
        var resolved = InitialVariables.Resolve(
            WorkflowWith(),
            null,
            globalScoped: Values(("apiHost", "global.example.com")));

        resolved.Variables["apiHost"].Should().Be("global.example.com");
    }

    [Fact]
    public void FullPrecedenceChain_IsGlobalThenWorkflowThenDeclaredThenInputs()
    {
        var definition = WorkflowWith(
            new VariableDefinition("a", PropertyType.String, Json("\"declared\""), Seed: VariableSeedMode.AlwaysOverride),
            new VariableDefinition("b", PropertyType.String, Json("\"declared\""), Seed: VariableSeedMode.AlwaysOverride));

        var resolved = InitialVariables.Resolve(
            definition,
            Values(("b", "input")),
            globalScoped: Values(("a", "global"), ("b", "global"), ("c", "global")),
            workflowScoped: Values(("a", "workflow"), ("b", "workflow")));

        resolved.Variables["a"].Should().Be("declared");
        resolved.Variables["b"].Should().Be("input");
        resolved.Variables["c"].Should().Be("global");
    }

    [Fact]
    public void DeclaredWithoutValue_StartsAsNullAndWarns()
    {
        // Q15: declaring a variable is a contract — it exists for the whole run, so a reference
        // resolves to null instead of failing the node. The gap is still worth surfacing.
        var definition = WorkflowWith(new VariableDefinition("orderId", PropertyType.String));

        var resolved = InitialVariables.Resolve(definition, null);

        resolved.Variables.ContainsKey("orderId").Should().BeTrue();
        resolved.Variables["orderId"].Should().BeNull();
        resolved.Warnings.Should().ContainSingle().Which.Should().Contain("orderId").And.Contain("declared");
    }

    [Fact]
    public void DeclaredWithoutValue_DoesNotWarnWhenARunInputSuppliesIt()
    {
        var definition = WorkflowWith(new VariableDefinition("orderId", PropertyType.String));

        var resolved = InitialVariables.Resolve(definition, Values(("orderId", "A-1")));

        resolved.Variables["orderId"].Should().Be("A-1");
        resolved.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void DeclaredWithoutValue_DoesNotWarnWhenTheStoreSuppliesIt()
    {
        var definition = WorkflowWith(new VariableDefinition("apiKey", PropertyType.String));

        var resolved = InitialVariables.Resolve(definition, null, globalScoped: Values(("apiKey", "k")));

        resolved.Variables["apiKey"].Should().Be("k");
        resolved.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void TypeMismatch_WarnsButKeepsTheValue()
    {
        var definition = WorkflowWith(new VariableDefinition("count", PropertyType.Int, Json("\"not a number\"")));

        var resolved = InitialVariables.Resolve(definition, null);

        resolved.Variables["count"].Should().Be("not a number", because: "the warning must not change behaviour");
        resolved.Warnings.Should().ContainSingle().Which.Should().Contain("declares type Int");
    }

    [Theory]
    [InlineData(PropertyType.String, "\"x\"")]
    [InlineData(PropertyType.Int, "3")]
    [InlineData(PropertyType.Long, "3")]
    [InlineData(PropertyType.Decimal, "3.5")]
    [InlineData(PropertyType.Boolean, "true")]
    [InlineData(PropertyType.Object, "{\"a\":1}")]
    [InlineData(PropertyType.Array, "[1,2]")]
    [InlineData(PropertyType.DateTime, "\"2026-01-01\"")]
    [InlineData(PropertyType.Guid, "\"not-judged\"")]
    public void MatchingOrUnjudgeableTypes_DoNotWarn(PropertyType type, string json)
    {
        var definition = WorkflowWith(new VariableDefinition("v", type, Json(json)));

        InitialVariables.Resolve(definition, null).Warnings.Should().BeEmpty();
    }

    [Fact]
    public void JsonObjectsAndArrays_BecomeClrShapes()
    {
        var definition = WorkflowWith(
            new VariableDefinition("obj", PropertyType.Object, Json("""{ "a": 1 }""")),
            new VariableDefinition("arr", PropertyType.Array, Json("[1, 2]")));

        var resolved = InitialVariables.Resolve(definition, null);

        resolved.Variables["obj"].Should().BeAssignableTo<IDictionary<string, object?>>()
            .Which["a"].Should().Be(1L);
        resolved.Variables["arr"].Should().BeAssignableTo<System.Collections.IList>()
            .Which.Count.Should().Be(2);
    }

    [Fact]
    public void JsonNullInitialValue_IsAPresentNullNotAWarning()
    {
        var definition = WorkflowWith(new VariableDefinition("v", PropertyType.String, Json("null")));

        var resolved = InitialVariables.Resolve(definition, null);

        resolved.Variables.ContainsKey("v").Should().BeTrue();
        resolved.Variables["v"].Should().BeNull();
        resolved.Warnings.Should().BeEmpty(because: "the author explicitly declared the value null");
    }

    [Fact]
    public void Lookup_IsCaseInsensitive_MatchingThePropertyBinder()
    {
        // PropertyBinder resolves {{Variable.X}} case-insensitively, so the starting map must not
        // be able to hold 'count' and 'Count' as two separate entries.
        var definition = WorkflowWith(new VariableDefinition("count", PropertyType.Int, Json("5")));

        var resolved = InitialVariables.Resolve(definition, Values(("COUNT", 7L)));

        resolved.Variables.Count.Should().Be(1);
        resolved.Variables["count"].Should().Be(7L);
    }

    [Fact]
    public void StoredValues_AreNormalisedFromJsonElements()
    {
        // Every IVariableStore round-trips through JsonSerializer.Deserialize<object?>, so values
        // arrive as JsonElement. If they reached the binder that way, {{Variable.x}} would resolve
        // to a JsonElement instead of a string.
        var stored = new Dictionary<string, object?>
        {
            ["host"] = Json("\"example.com\""),
            ["retries"] = Json("3"),
            ["enabled"] = Json("true"),
        };

        var resolved = InitialVariables.Resolve(WorkflowWith(), null, globalScoped: stored);

        resolved.Variables["host"].Should().BeOfType<string>().And.Be("example.com");
        resolved.Variables["retries"].Should().Be(3L);
        resolved.Variables["enabled"].Should().Be(true);
    }

    [Fact]
    public void NoDeclarationsAndNoInputs_ProducesAnEmptyMapNotAnError()
        => InitialVariables.Resolve(WorkflowWith(), null).Variables.Count.Should().Be(0);
}
