// <copyright file="GraphTopologyTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.State;

using System.Collections.Generic;
using FluentAssertions;
using Workflow.UI.Client.Designer.State;
using Xunit;

/// <summary>
/// 🧪 Start/End clarity C1 — specs for <see cref="GraphTopology"/>: starts are in-degree-0 nodes,
/// ends are out-degree-0 nodes, exactly like the engine (<c>GetStartNodes</c> /
/// <c>GatherWorkflowOutputs</c>)~ 🧭.
/// </summary>
public sealed class GraphTopologyTests
{
    private static DesignerNode Node(string id)
        => new() { Id = id, ModuleId = "m", Name = id };

    private static DesignerConnection Conn(string s, string t)
        => new() { SourceNodeId = s, SourcePortName = "out", TargetNodeId = t, TargetPortName = "in" };

    private static DesignerDocument Doc(
        IEnumerable<DesignerNode> nodes,
        IEnumerable<DesignerConnection>? conns = null)
    {
        var doc = new DesignerDocument();
        doc.Nodes.AddRange(nodes);
        doc.Connections.AddRange(conns ?? []);
        return doc;
    }

    [Fact]
    public void LinearChain_HasOneStartAndOneEnd()
    {
        var topo = GraphTopology.Compute(Doc(
            [Node("a"), Node("b"), Node("c")],
            [Conn("a", "b"), Conn("b", "c")]));

        topo.StartNodeIds.Should().Equal("a");
        topo.EndNodeIds.Should().Equal("c");
        topo.IsolatedNodeIds.Should().BeEmpty();
        topo.Roles["a"].Should().Be(new GraphTopology.NodeRole(true, false, 0, 1, -1, 1));
        topo.Roles["b"].Should().Be(new GraphTopology.NodeRole(false, false, -1, 1, -1, 1));
        topo.Roles["c"].Should().Be(new GraphTopology.NodeRole(false, true, -1, 1, 0, 1));
    }

    [Fact]
    public void Diamond_HasOneStartAndOneEnd()
    {
        // a → b, a → c, b → d, c → d
        var topo = GraphTopology.Compute(Doc(
            [Node("a"), Node("b"), Node("c"), Node("d")],
            [Conn("a", "b"), Conn("a", "c"), Conn("b", "d"), Conn("c", "d")]));

        topo.StartNodeIds.Should().Equal("a");
        topo.EndNodeIds.Should().Equal("d");
    }

    [Fact]
    public void MultipleStartsAndEnds_IndexedInDocumentOrder()
    {
        // s1 → e1, s2 → e2 — two independent chains.
        var topo = GraphTopology.Compute(Doc(
            [Node("s1"), Node("e1"), Node("s2"), Node("e2")],
            [Conn("s1", "e1"), Conn("s2", "e2")]));

        topo.StartNodeIds.Should().Equal("s1", "s2");
        topo.EndNodeIds.Should().Equal("e1", "e2");
        topo.Roles["s2"].StartIndex.Should().Be(1);
        topo.Roles["s2"].StartCount.Should().Be(2);
        topo.Roles["e1"].EndIndex.Should().Be(0);
        topo.Roles["e1"].EndCount.Should().Be(2);
    }

    [Fact]
    public void IsolatedNode_IsBothStartAndEnd()
    {
        var topo = GraphTopology.Compute(Doc(
            [Node("a"), Node("b"), Node("lonely")],
            [Conn("a", "b")]));

        topo.IsolatedNodeIds.Should().Equal("lonely");
        topo.Roles["lonely"].IsIsolated.Should().BeTrue();
        topo.StartNodeIds.Should().Contain("lonely");
        topo.EndNodeIds.Should().Contain("lonely");
    }

    [Fact]
    public void EmptyDocument_HasNoRoles()
    {
        var topo = GraphTopology.Compute(Doc([]));

        topo.StartNodeIds.Should().BeEmpty();
        topo.EndNodeIds.Should().BeEmpty();
        topo.IsolatedNodeIds.Should().BeEmpty();
        topo.Roles.Should().BeEmpty();
    }

    [Fact]
    public void DanglingConnection_StillCountsAsAnEdge()
    {
        // b's only incoming edge comes from a node that doesn't exist — already a validation
        // error; the role must not flip while the user is mid-fix (C1.1).
        var topo = GraphTopology.Compute(Doc(
            [Node("b")],
            [Conn("ghost", "b")]));

        topo.StartNodeIds.Should().BeEmpty();
        topo.EndNodeIds.Should().Equal("b");
    }

    [Fact]
    public void Cycle_HasNoStartOrEnd()
    {
        // a → b → a. The cycle validator flags this; topology just reports what's there.
        var topo = GraphTopology.Compute(Doc(
            [Node("a"), Node("b")],
            [Conn("a", "b"), Conn("b", "a")]));

        topo.StartNodeIds.Should().BeEmpty();
        topo.EndNodeIds.Should().BeEmpty();
    }
}
