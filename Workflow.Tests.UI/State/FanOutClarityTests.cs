// <copyright file="FanOutClarityTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.State;

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Workflow.UI.Client.Designer.State;
using Xunit;

/// <summary>
/// 🧪 FanOut clarity (K1/K5/P2) — specs for fanout/parallel structural regions, the unwired-branch
/// warning, and property-derived dynamic output ports~ 🌟.
/// </summary>
public sealed class FanOutClarityTests
{
    private static readonly HashSet<string> Known = new()
    {
        "m", "builtin.fanout", "builtin.parallel", "builtin.switch", "builtin.partition", "builtin.split",
    };

    private static DesignerNode Node(string id, string moduleId = "m")
        => new() { Id = id, ModuleId = moduleId, Name = id };

    private static DesignerConnection Conn(string s, string t, string sourcePort = "out")
        => new() { SourceNodeId = s, SourcePortName = sourcePort, TargetNodeId = t, TargetPortName = "in" };

    private static DesignerDocument Doc(
        IEnumerable<DesignerNode> nodes,
        IEnumerable<DesignerConnection>? conns = null)
    {
        var doc = new DesignerDocument();
        doc.Nodes.AddRange(nodes);
        doc.Connections.AddRange(conns ?? []);
        return doc;
    }

    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement.Clone();

    // ─── K1 — regions ───────────────────────────────────────────────────────────

    [Fact]
    public void FanOutBranch_GetsAPerItemRegion()
    {
        var doc = Doc(
            [Node("fan", "builtin.fanout"), Node("work")],
            [Conn("fan", "work", "branch")]);

        var regions = StructuralRegions.Compute(doc);

        var region = regions.Should().ContainSingle().Subject;
        region.Kind.Should().Be("fanout");
        region.Label.Should().Contain("per item");
        region.OwnerNodeId.Should().Be("fan");
        region.NodeIds.Should().Contain("work");
    }

    [Fact]
    public void ParallelBranchPort_GetsABranchRegion()
    {
        var doc = Doc(
            [Node("par", "builtin.parallel"), Node("work")],
            [Conn("par", "work", "notify")]);

        var regions = StructuralRegions.Compute(doc);

        var region = regions.Should().ContainSingle().Subject;
        region.Kind.Should().Be("parallel");
        region.Label.Should().Contain("notify");
    }

    [Fact]
    public void ParallelDonePort_GetsNoRegion()
    {
        var doc = Doc(
            [Node("par", "builtin.parallel"), Node("after")],
            [Conn("par", "after", "done")]);

        StructuralRegions.Compute(doc).Should().BeEmpty();
    }

    [Fact]
    public void PlainModulePortNamedBranch_GetsNoRegion()
    {
        // F3 caveat: a non-fanout module with an output that happens to be called 'branch'.
        var doc = Doc(
            [Node("a", "m"), Node("b")],
            [Conn("a", "b", "branch")]);

        StructuralRegions.Compute(doc).Should().BeEmpty();
    }

    [Fact]
    public void LoopBodyRegions_StillWork()
    {
        var doc = Doc(
            [Node("loop", "builtin.loop.foreach"), Node("body")],
            [Conn("loop", "body", "loopBody")]);

        var region = StructuralRegions.Compute(doc).Should().ContainSingle().Subject;
        region.Kind.Should().Be("loop");
    }

    // ─── K5 — unwired branch warning ────────────────────────────────────────────

    [Fact]
    public void FanOutWithUnwiredBranch_Warns()
    {
        var doc = Doc([Node("a"), Node("fan", "builtin.fanout")], [Conn("a", "fan")]);

        var issues = GraphValidator.Validate(doc, Known);

        var issue = issues.Should().ContainSingle(i => i.Message.Contains("branch isn't connected")).Subject;
        issue.Severity.Should().Be(IssueSeverity.Warning);
        issue.NodeId.Should().Be("fan");
    }

    [Fact]
    public void FanOutWithWiredBranch_NoWarning()
    {
        var doc = Doc(
            [Node("fan", "builtin.fanout"), Node("work")],
            [Conn("fan", "work", "branch")]);

        GraphValidator.ValidateFanOut(doc).Should().BeEmpty();
    }

    [Fact]
    public void NonFanOutModules_Unaffected()
    {
        var doc = Doc([Node("a"), Node("b")], [Conn("a", "b")]);

        GraphValidator.ValidateFanOut(doc).Should().BeEmpty();
    }

    // ─── P2 — property-derived output ports ─────────────────────────────────────

    [Fact]
    public void PartitionPorts_DeriveFromRules_InOrder_WithDefaultAndMeta()
    {
        var node = Node("p", "builtin.partition");
        node.Properties["rules"] = Json(
            """[ { "match": "Foo", "port": "foos" }, { "match": "Bar", "port": "others" }, { "match": "Baz", "port": "others" } ]""");
        node.Properties["defaultPort"] = Json("\"unmatched\"");

        var ports = NodePorts.Outputs(node);

        ports.Should().Equal("foos", "others", "unmatched", "counts", "total");
    }

    [Fact]
    public void PartitionRulesAsJsonString_AlsoParse()
    {
        // The JSON editor stores values as JSON *strings*; both forms must work.
        var node = Node("p", "builtin.partition");
        node.Properties["rules"] = Json(
            "\"[ { \\\"match\\\": \\\"Foo\\\", \\\"port\\\": \\\"foos\\\" } ]\"");

        NodePorts.Outputs(node).Should().StartWith(new[] { "foos" });
    }

    [Fact]
    public void PartitionWithoutRules_FallsBackToDefaults()
    {
        var node = Node("p", "builtin.partition");

        NodePorts.Outputs(node).Should().Equal("output");
    }

    [Fact]
    public void SplitPorts_DeriveFromKeys_PlusRestPort()
    {
        var node = Node("s", "builtin.split");
        node.Properties["keys"] = Json("""[ "Foo", "Bar", "Baz" ]""");
        node.Properties["restPort"] = Json("\"rest\"");

        NodePorts.Outputs(node).Should().Equal("Foo", "Bar", "Baz", "rest");
    }

    [Fact]
    public void SwitchPorts_DeriveFromCases()
    {
        var node = Node("sw", "builtin.switch");
        node.Properties["cases"] = Json(
            """[ { "match": "cat", "port": "case_cat" }, { "match": "dog", "port": "case_dog" } ]""");
        node.Properties["defaultPort"] = Json("\"fallback\"");

        NodePorts.Outputs(node).Should().Equal("case_cat", "case_dog", "fallback");
    }

    [Fact]
    public void ParallelPorts_DeriveFromBranchNames_OrBranchCount()
    {
        var named = Node("p1", "builtin.parallel");
        named.Properties["branches"] = Json("""[ "notify", "persist" ]""");
        NodePorts.Outputs(named).Should().Equal("notify", "persist", "done");

        var counted = Node("p2", "builtin.parallel");
        counted.Properties["branchCount"] = Json("3");
        NodePorts.Outputs(counted).Should().Equal("branch1", "branch2", "branch3", "done");

        // No config at all → the documented default of 2 branches.
        NodePorts.Outputs(Node("p3", "builtin.parallel")).Should().Equal("branch1", "branch2", "done");
    }

    [Fact]
    public void MalformedRuleJson_FallsBackToDefaults()
    {
        var node = Node("p", "builtin.partition");
        node.Properties["rules"] = Json("\"not json at all\"");

        NodePorts.Outputs(node).Should().Equal("output");
    }

    [Fact]
    public void DerivedPorts_AnchorMathUsesThem()
    {
        // The EdgeLayer anchors by index into the same list NodeView renders — derived ports
        // must flow through Anchor without falling back to index 0.
        var node = Node("sw", "builtin.switch");
        node.Properties["cases"] = Json(
            """[ { "match": "a", "port": "pa" }, { "match": "b", "port": "pb" } ]""");

        var first = NodePorts.Anchor(node, "pa", isOutput: true);
        var second = NodePorts.Anchor(node, "pb", isOutput: true);

        second.Y.Should().BeGreaterThan(first.Y);
    }
}
