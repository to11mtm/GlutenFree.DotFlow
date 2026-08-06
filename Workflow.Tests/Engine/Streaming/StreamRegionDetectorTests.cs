// ────────────────────────────────────────────────────────────────────────────────
// Copyright © GlutenFree 2025. Made with love by Ami-Chan! UwU 💖
// Licensed under the MIT License.
// ────────────────────────────────────────────────────────────────────────────────

using System.Text.Json;
using FluentAssertions;
using LanguageExt;
using Workflow.Core.Models;
using Workflow.Engine.Streaming;

namespace Workflow.Tests.Engine.Streaming;

/// <summary>
/// Phase 5.1.3 — engine-side streaming region detection~ 🌊🗺️
/// </summary>
/// <remarks>
/// 🛡️ <b>Drift guard:</b> the designer detects the same regions from its own model in
/// <c>Workflow.Tests.UI\State\StreamingTopologyTests.cs</c> (see the region tests there). The two
/// implementations exist because <c>Workflow.UI.Client</c> has no <c>Workflow.Core</c> reference by
/// design (Phase 3.3 D2); the fixtures below are mirrored there on purpose. <b>Change one, change
/// both</b> — same convention as <c>SplitPreviewDriftGuardTests</c>~ ✨.
/// </remarks>
public class StreamRegionDetectorTests
{
    private static PortDefinition Port(string name, bool streaming = false)
        => streaming
            ? PortDefinition.CreateStreaming(name)
            : PortDefinition.Create<object>(name, isRequired: false);

    private static ModuleSchema StreamSchema() => new(
        Arr.create(Port("items", streaming: true)),
        Arr.create(Port("items", streaming: true)),
        Arr<ModulePropertyDefinition>.Empty);

    private static ModuleSchema BatchSchema() => new(
        Arr.create(Port("input")),
        Arr.create(Port("output")),
        Arr<ModulePropertyDefinition>.Empty);

    private static NodeDefinition Node(string id)
        => new(id, "m", id, HashMap<string, JsonElement>.Empty);

    private static ConnectionDefinition Conn(string s, string sp, string t, string tp)
        => new(s, sp, t, tp);

    private static WorkflowDefinition Definition(
        IEnumerable<NodeDefinition> nodes,
        IEnumerable<ConnectionDefinition> connections)
        => new(
            Guid.NewGuid(),
            "wf",
            null,
            new Version(1, 0, 0),
            new Arr<NodeDefinition>(nodes),
            new Arr<ConnectionDefinition>(connections),
            HashMap<string, VariableDefinition>.Empty);

    /// <summary>Every node streams unless named "batch*"~ 🎛️.</summary>
    private static Func<string, ModuleSchema?> Schemas(params string[] batchNodeIds)
        => id => batchNodeIds.Contains(id) ? BatchSchema() : StreamSchema();

    [Fact]
    public void Chain_OfStreamingNodes_IsOneRegion()
    {
        var definition = Definition(
            [Node("a"), Node("b"), Node("c")],
            [Conn("a", "items", "b", "items"), Conn("b", "items", "c", "items")]);

        var regions = StreamRegionDetector.Regions(definition, Schemas());

        regions.Should().ContainSingle();
        regions[0].NodeIds.Should().BeEquivalentTo(["a", "b", "c"]);
    }

    [Fact]
    public void SeparateChains_AreSeparateRegions()
    {
        var definition = Definition(
            [Node("a"), Node("b"), Node("c"), Node("d")],
            [Conn("a", "items", "b", "items"), Conn("c", "items", "d", "items")]);

        StreamRegionDetector.Regions(definition, Schemas()).Should().HaveCount(2);
    }

    [Fact]
    public void BatchOnlyGraph_HasNoRegions()
    {
        var definition = Definition(
            [Node("a"), Node("b")],
            [Conn("a", "output", "b", "input")]);

        StreamRegionDetector.Regions(definition, Schemas("a", "b")).Should().BeEmpty();
    }

    [Fact]
    public void MismatchedEdge_DoesNotFormARegion()
    {
        var definition = Definition(
            [Node("a"), Node("b")],
            [Conn("a", "items", "b", "input")]);

        StreamRegionDetector.Regions(definition, Schemas("b"))
            .Should().BeEmpty("an invalid edge is not a pipeline");
    }

    [Fact]
    public void Region_IdentifiesSourceAndTerminal()
    {
        var definition = Definition(
            [Node("a"), Node("b"), Node("c")],
            [Conn("a", "items", "b", "items"), Conn("b", "items", "c", "items")]);

        var region = StreamRegionDetector.Regions(definition, Schemas())[0];

        region.SourceNodeIds.Should().ContainSingle().Which.Should().Be("a");
        region.TerminalNodeIds.Should().ContainSingle().Which.Should().Be("c");
    }

    [Fact]
    public void ChainOrder_FollowsThePipeline_NotDefinitionOrder()
    {
        // Nodes deliberately declared out of pipeline order~
        var definition = Definition(
            [Node("c"), Node("a"), Node("b")],
            [Conn("a", "items", "b", "items"), Conn("b", "items", "c", "items")]);

        var region = StreamRegionDetector.Regions(definition, Schemas())[0];

        region.ChainOrder().Should().ContainInOrder("a", "b", "c");
    }

    [Fact]
    public void ChainOrder_Branching_FailsLoudly()
    {
        var definition = Definition(
            [Node("a"), Node("b"), Node("c")],
            [Conn("a", "items", "b", "items"), Conn("a", "items", "c", "items")]);

        var region = StreamRegionDetector.Regions(definition, Schemas())[0];

        region.Invoking(r => r.ChainOrder())
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*branching inside a region isn't supported*");
    }

    [Fact]
    public void RegionOf_FindsTheOwningRegion()
    {
        var definition = Definition(
            [Node("a"), Node("b"), Node("outside")],
            [Conn("a", "items", "b", "items")]);

        StreamRegionDetector.RegionOf(definition, Schemas("outside"), "b").Should().NotBeNull();
        StreamRegionDetector.RegionOf(definition, Schemas("outside"), "outside").Should().BeNull();
    }

    [Fact]
    public void RegionId_IsIgnored_DetectionIsConnectionDriven()
    {
        // A definition can claim any RegionId it likes; the engine must not care~ 🛡️
        var nodes = new[]
        {
            Node("a") with { RegionId = "totally-made-up" },
            Node("b") with { RegionId = "different-lie" },
        };
        var definition = Definition(nodes, [Conn("a", "items", "b", "items")]);

        var regions = StreamRegionDetector.Regions(definition, Schemas());

        regions.Should().ContainSingle().Which.NodeIds.Should().BeEquivalentTo(["a", "b"]);
    }
}
