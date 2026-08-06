// <copyright file="StreamingTopologyTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.State;

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Workflow.UI.Client.Api.Dtos;
using Workflow.UI.Client.Designer.State;
using Xunit;

/// <summary>
/// 🧪 Phase 5.1.2 — specs for streaming topology in the designer: which ports/edges are streaming,
/// how regions are detected, and the rules that stop an unrunnable streaming graph being drawn~ 🌊.
/// </summary>
public sealed class StreamingTopologyTests
{
    private static PortDefinitionDto Port(string name, bool streaming = false)
        => new(name, name, "object", null, false, null, streaming);

    private static ModuleSchemaDto Schema(
        IEnumerable<PortDefinitionDto> inputs,
        IEnumerable<PortDefinitionDto> outputs)
        => new(inputs.ToList(), outputs.ToList(), new List<ModulePropertyDefinitionDto>());

    /// <summary>A streaming transform: stream in, stream out.</summary>
    private static DesignerNode StreamNode(string id, string moduleId = "m.stream")
        => new()
        {
            Id = id,
            ModuleId = moduleId,
            Name = id,
            Schema = Schema([Port("items", streaming: true)], [Port("items", streaming: true)]),
        };

    /// <summary>An ordinary batch node.</summary>
    private static DesignerNode BatchNode(string id, string moduleId = "m.batch")
        => new()
        {
            Id = id,
            ModuleId = moduleId,
            Name = id,
            Schema = Schema([Port("input")], [Port("output")]),
        };

    private static DesignerConnection Conn(string s, string sp, string t, string tp)
        => new() { SourceNodeId = s, SourcePortName = sp, TargetNodeId = t, TargetPortName = tp };

    private static DesignerDocument Doc(
        IEnumerable<DesignerNode> nodes,
        IEnumerable<DesignerConnection>? conns = null)
    {
        var doc = new DesignerDocument();
        doc.Nodes.AddRange(nodes);
        doc.Connections.AddRange(conns ?? []);
        return doc;
    }

    #region Port & edge classification

    [Fact]
    public void StreamingPort_IsReadFromTheSchema()
    {
        var node = StreamNode("a");

        StreamGraph.IsStreamingPort(node, "items", isOutput: true).Should().BeTrue();
        StreamGraph.IsStreamingPort(BatchNode("b"), "output", isOutput: true).Should().BeFalse();
    }

    [Fact]
    public void UnknownModule_IsNeverTreatedAsStreaming()
    {
        var unknown = new DesignerNode { Id = "x", ModuleId = "nope", Name = "x", Schema = null };

        StreamGraph.IsStreamingPort(unknown, "items", isOutput: true).Should().BeFalse();
        StreamGraph.IsStreamCapable(unknown).Should().BeFalse();
    }

    [Fact]
    public void StreamingEdge_RequiresBothEndsStreaming()
    {
        var doc = Doc([StreamNode("a"), StreamNode("b"), BatchNode("c")],
            [Conn("a", "items", "b", "items")]);

        StreamGraph.IsStreamingEdge(doc, doc.Connections[0]).Should().BeTrue();
    }

    #endregion

    #region Shape rule

    [Fact]
    public void ShapeMismatch_StreamIntoBatch_IsDetected()
        => StreamGraph.IsShapeMismatch(StreamNode("a"), "items", BatchNode("b"), "input")
            .Should().BeTrue();

    [Fact]
    public void ShapeMismatch_BatchIntoStream_IsDetected()
        => StreamGraph.IsShapeMismatch(BatchNode("a"), "output", StreamNode("b"), "items")
            .Should().BeTrue();

    [Fact]
    public void ShapeMatch_IsAllowedBothWays()
    {
        StreamGraph.IsShapeMismatch(StreamNode("a"), "items", StreamNode("b"), "items").Should().BeFalse();
        StreamGraph.IsShapeMismatch(BatchNode("a"), "output", BatchNode("b"), "input").Should().BeFalse();
    }

    [Fact]
    public void ShapeMismatch_WithoutSchema_StaysQuiet()
    {
        var unknown = new DesignerNode { Id = "x", ModuleId = "nope", Name = "x", Schema = null };

        StreamGraph.IsShapeMismatch(StreamNode("a"), "items", unknown, "input")
            .Should().BeFalse("refusing wires because a schema hasn't loaded would be maddening");
    }

    [Fact]
    public void BridgeSuggestion_PointsAtTheRightDirection()
    {
        StreamGraph.BridgeFor(sourceIsStreaming: true).Should().Be("builtin.stream.collect");
        StreamGraph.BridgeFor(sourceIsStreaming: false).Should().Be("builtin.stream.fromitems");
    }

    [Fact]
    public void Validator_ShapeMismatch_IsAnErrorNamingTheBridge()
    {
        var doc = Doc([StreamNode("a"), BatchNode("b")], [Conn("a", "items", "b", "input")]);

        var issues = GraphValidator.ValidateStreaming(doc);

        var issue = issues.Should().ContainSingle().Subject;
        issue.Severity.Should().Be(IssueSeverity.Error);
        issue.Message.Should().Contain("builtin.stream.collect");
    }

    #endregion

    #region Region detection

    [Fact]
    public void Region_GroupsStreamLinkedNodes()
    {
        var doc = Doc(
            [StreamNode("a"), StreamNode("b"), StreamNode("c")],
            [Conn("a", "items", "b", "items"), Conn("b", "items", "c", "items")]);

        var regions = StreamGraph.Regions(doc);

        regions.Should().ContainSingle();
        regions[0].NodeIds.Should().BeEquivalentTo(["a", "b", "c"]);
    }

    [Fact]
    public void Region_SeparateChains_AreSeparateRegions()
    {
        var doc = Doc(
            [StreamNode("a"), StreamNode("b"), StreamNode("c"), StreamNode("d")],
            [Conn("a", "items", "b", "items"), Conn("c", "items", "d", "items")]);

        StreamGraph.Regions(doc).Should().HaveCount(2);
    }

    [Fact]
    public void Region_BatchOnlyGraph_HasNoRegions()
    {
        var doc = Doc([BatchNode("a"), BatchNode("b")], [Conn("a", "output", "b", "input")]);

        StreamGraph.Regions(doc).Should().BeEmpty();
    }

    [Fact]
    public void Region_MismatchedEdge_DoesNotFormARegion()
    {
        var doc = Doc([StreamNode("a"), BatchNode("b")], [Conn("a", "items", "b", "input")]);

        StreamGraph.Regions(doc).Should().BeEmpty("an invalid edge is not a pipeline");
    }

    [Fact]
    public void RegionIndex_MapsEveryMemberNode()
    {
        var doc = Doc(
            [StreamNode("a"), StreamNode("b"), BatchNode("outside")],
            [Conn("a", "items", "b", "items")]);

        var map = StreamGraph.RegionIndexByNode(doc);

        map.Should().ContainKeys("a", "b");
        map.Should().NotContainKey("outside");
    }

    #endregion

    #region Region rules

    [Fact]
    public void SetVariable_InsideARegion_IsAnError()
    {
        var setVar = StreamNode("sv", "builtin.setvariable");
        var doc = Doc([StreamNode("a"), setVar], [Conn("a", "items", "sv", "items")]);

        var issues = GraphValidator.ValidateStreaming(doc);

        issues.Should().ContainSingle(i =>
            i.NodeId == "sv" && i.Severity == IssueSeverity.Error && i.Message.Contains("item order isn't defined"));
    }

    [Fact]
    public void SetVariable_OutsideARegion_IsFine()
    {
        var doc = Doc(
            [BatchNode("a"), BatchNode("sv", "builtin.setvariable")],
            [Conn("a", "output", "sv", "input")]);

        GraphValidator.ValidateStreaming(doc).Should().BeEmpty();
    }

    [Fact]
    public void Stream_CrossingIntoALoopBody_IsAnError()
    {
        var loop = new DesignerNode
        {
            Id = "loop",
            ModuleId = "builtin.loop.foreach",
            Name = "loop",
            Schema = Schema([Port("items")], [Port("loopBody", streaming: true), Port("done")]),
        };
        var doc = Doc([loop, StreamNode("inner")], [Conn("loop", "loopBody", "inner", "items")]);

        var issues = GraphValidator.ValidateStreaming(doc);

        issues.Should().ContainSingle(i =>
            i.Severity == IssueSeverity.Error && i.Message.Contains("can't cross into a 'loopBody' body"));
    }

    [Fact]
    public void CleanStreamingChain_ProducesNoIssues()
    {
        var doc = Doc(
            [StreamNode("a"), StreamNode("b")],
            [Conn("a", "items", "b", "items")]);

        GraphValidator.ValidateStreaming(doc).Should().BeEmpty();
    }

    #endregion

    #region Worker & ordering knobs (5.1.4)

    private static DesignerNode PerItemNode(string id, int? maxWorkers = null, bool? ordered = null)
    {
        var node = new DesignerNode
        {
            Id = id,
            ModuleId = "m.stream",
            Name = id,
            Schema = new ModuleSchemaDto(
                [Port("items", streaming: true)],
                [Port("items", streaming: true)],
                new List<ModulePropertyDefinitionDto>(),
                "perItem"),
        };

        if (maxWorkers is { } w)
        {
            node.Properties["maxWorkers"] = System.Text.Json.JsonDocument.Parse(
                w.ToString(System.Globalization.CultureInfo.InvariantCulture)).RootElement.Clone();
        }

        if (ordered is { } o)
        {
            node.Properties["ordered"] = System.Text.Json.JsonDocument.Parse(o ? "true" : "false").RootElement.Clone();
        }

        return node;
    }

    [Fact]
    public void MaxWorkers_DefaultsToOne_AndOrderedDefaultsToTrue()
    {
        var node = PerItemNode("a");

        StreamGraph.MaxWorkersOf(node).Should().Be(1);
        StreamGraph.OrderedOf(node).Should().BeTrue("ordering is free, so it's on unless you opt out");
        StreamGraph.IsUnorderedStage(node).Should().BeFalse();
    }

    [Fact]
    public void UnorderedStage_RequiresBothConcurrencyAndOptOut()
    {
        StreamGraph.IsUnorderedStage(PerItemNode("a", maxWorkers: 4, ordered: true)).Should().BeFalse();
        StreamGraph.IsUnorderedStage(PerItemNode("b", maxWorkers: 1, ordered: false)).Should().BeFalse(
            "one worker can't produce out-of-order output whatever the flag says");
        StreamGraph.IsUnorderedStage(PerItemNode("c", maxWorkers: 4, ordered: false)).Should().BeTrue();
    }

    [Fact]
    public void UnorderedStage_InARegion_IsWarned()
    {
        var a = PerItemNode("a");
        var b = PerItemNode("b", maxWorkers: 4, ordered: false);
        var doc = Doc([a, b], [Conn("a", "items", "b", "items")]);

        var issues = GraphValidator.ValidateStreaming(doc);

        issues.Should().ContainSingle(i =>
            i.NodeId == "b" && i.Severity == IssueSeverity.Warning && i.Message.Contains("completion order"));
    }

    [Fact]
    public void MaxWorkers_OnAWholeStreamStage_WarnsItWillBeIgnored()
    {
        // 🛡️ D26 — a module that owns its own loop can't be parallelised, so the knob is a lie.
        var a = StreamNode("a");
        var b = StreamNode("b");
        b.Schema = new ModuleSchemaDto(
            [Port("items", streaming: true)],
            [Port("items", streaming: true)],
            new List<ModulePropertyDefinitionDto>(),
            "wholeStream");
        b.Properties["maxWorkers"] = System.Text.Json.JsonDocument.Parse("4").RootElement.Clone();

        var issues = GraphValidator.ValidateStreaming(Doc([a, b], [Conn("a", "items", "b", "items")]));

        issues.Should().ContainSingle(i =>
            i.NodeId == "b" && i.Message.Contains("one item at a time"));
    }

    [Fact]
    public void MaxWorkers_BelowOne_IsAnError()
    {
        var a = PerItemNode("a");
        var b = PerItemNode("b", maxWorkers: 0);

        var issues = GraphValidator.ValidateStreaming(Doc([a, b], [Conn("a", "items", "b", "items")]));

        issues.Should().ContainSingle(i => i.NodeId == "b" && i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public void WorkerKnobs_OutsideARegion_AreNotLinted()
    {
        var lonely = PerItemNode("solo", maxWorkers: 8, ordered: false);

        GraphValidator.ValidateStreaming(Doc([lonely])).Should().BeEmpty(
            "an unwired node isn't running anything yet");
    }

    #endregion

    #region {{item}} token (Q5)

    [Fact]
    public void ItemToken_IsOfferedInsideARegion()
    {
        var doc = Doc([StreamNode("a"), StreamNode("b")], [Conn("a", "items", "b", "items")]);

        var options = VariableTokens.OptionsFor(doc, "b");

        options.Should().Contain(o => o.Token == VariableTokens.StreamItemToken);
    }

    [Fact]
    public void ItemToken_IsHiddenOutsideARegion()
    {
        var doc = Doc([BatchNode("a")]);

        VariableTokens.OptionsFor(doc, "a")
            .Should().NotContain(o => o.Token == VariableTokens.StreamItemToken);
    }

    [Fact]
    public void ItemReference_OutsideARegion_IsALintErrorThatExplainsWhy()
    {
        var node = new DesignerNode
        {
            Id = "n1",
            ModuleId = "builtin.log",
            Name = "Log",
            Schema = new ModuleSchemaDto(
                [Port("input")],
                [Port("output")],
                [new ModulePropertyDefinitionDto("message", "Message", "string", null, false, null, "Text", null, true)]),
        };
        node.Properties["message"] = System.Text.Json.JsonDocument.Parse("\"{{item.name}}\"").RootElement.Clone();

        var issues = VariableLint.Validate(Doc([node]));

        issues.Should().ContainSingle(i =>
            i.Severity == IssueSeverity.Error && i.Message.Contains("isn't inside a streaming region"));
    }

    [Fact]
    public void ItemReference_InsideARegion_IsAccepted()
    {
        var a = StreamNode("a");
        var b = new DesignerNode
        {
            Id = "b",
            ModuleId = "m.stream",
            Name = "b",
            Schema = new ModuleSchemaDto(
                [Port("items", streaming: true)],
                [Port("items", streaming: true)],
                [new ModulePropertyDefinitionDto("message", "Message", "string", null, false, null, "Text", null, true)]),
        };
        b.Properties["message"] = System.Text.Json.JsonDocument.Parse("\"{{item.name}}\"").RootElement.Clone();

        var issues = VariableLint.Validate(Doc([a, b], [Conn("a", "items", "b", "items")]));

        issues.Should().BeEmpty();
    }

    [Fact]
    public void InputReference_IsNeverTreatedAsANodeName()
    {
        var node = new DesignerNode
        {
            Id = "n1",
            ModuleId = "builtin.log",
            Name = "Log",
            Schema = new ModuleSchemaDto(
                [Port("input")],
                [Port("output")],
                [new ModulePropertyDefinitionDto("message", "Message", "string", null, false, null, "Text", null, true)]),
        };
        node.Properties["message"] = System.Text.Json.JsonDocument.Parse("\"{{input.orderId}}\"").RootElement.Clone();

        VariableLint.Validate(Doc([node]))
            .Should().BeEmpty("'input' is the reserved root for a node's own incoming value");
    }

    #endregion
}
