// <copyright file="StructuralRegionsTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.State;

using System.Linq;
using FluentAssertions;
using Workflow.UI.Client.Designer.State;
using Xunit;

/// <summary>
/// 🧪 UX-F4.3/F5.2 — Unit tests for <see cref="StructuralRegions"/> (body-region computation)~ ✨.
/// </summary>
public sealed class StructuralRegionsTests
{
    private static DesignerNode Node(string id, string moduleId, double x, double y)
        => new() { Id = id, ModuleId = moduleId, Name = id, X = x, Y = y };

    private static DesignerConnection Edge(string src, string port, string tgt)
        => new() { SourceNodeId = src, SourcePortName = port, TargetNodeId = tgt, TargetPortName = "input" };

    [Fact]
    public void LoopBody_Chain_IsOneRegion_WithClosure()
    {
        var doc = new DesignerDocument { Name = "wf" };
        doc.Nodes.Add(Node("loop", "builtin.loop.foreach", 0, 0));
        doc.Nodes.Add(Node("a", "builtin.log", 300, 0));
        doc.Nodes.Add(Node("b", "builtin.log", 600, 0));
        doc.Nodes.Add(Node("next", "builtin.log", 300, 400));
        doc.Connections.Add(Edge("loop", "loopBody", "a"));
        doc.Connections.Add(Edge("a", "output", "b"));
        doc.Connections.Add(Edge("loop", "done", "next"));

        var regions = StructuralRegions.Compute(doc);

        var r = regions.Should().ContainSingle().Which;
        r.OwnerNodeId.Should().Be("loop");
        r.Port.Should().Be("loopBody");
        r.Kind.Should().Be("loop");
        r.NodeIds.Should().BeEquivalentTo(new[] { "a", "b" }, because: "the closure follows a → b but not the done route~ 🔁");
    }

    [Fact]
    public void TryCatch_ThreeWiredPorts_YieldThreeRegions()
    {
        var doc = new DesignerDocument { Name = "wf" };
        doc.Nodes.Add(Node("tc", "builtin.trycatch", 0, 0));
        doc.Nodes.Add(Node("t1", "builtin.log", 300, 0));
        doc.Nodes.Add(Node("c1", "builtin.log", 300, 200));
        doc.Nodes.Add(Node("f1", "builtin.log", 300, 400));
        doc.Connections.Add(Edge("tc", "try", "t1"));
        doc.Connections.Add(Edge("tc", "catch", "c1"));
        doc.Connections.Add(Edge("tc", "finally", "f1"));

        var regions = StructuralRegions.Compute(doc);

        regions.Should().HaveCount(3);
        regions.Select(r => r.Kind).Should().BeEquivalentTo(new[] { "try", "catch", "finally" });
        regions.Single(r => r.Kind == "try").NodeIds.Should().Equal("t1");
    }

    [Fact]
    public void Bounds_CoverBodyNodes_WithPadding()
    {
        var doc = new DesignerDocument { Name = "wf" };
        doc.Nodes.Add(Node("loop", "builtin.loop.foreach", 0, 0));
        doc.Nodes.Add(Node("a", "builtin.log", 300, 100));
        doc.Connections.Add(Edge("loop", "loopBody", "a"));

        var r = StructuralRegions.Compute(doc).Single();

        var nodeBounds = CanvasGeometry.NodeBounds(300, 100, 1, 1);
        r.Bounds.X.Should().Be(nodeBounds.X - StructuralRegions.Padding);
        r.Bounds.Y.Should().Be(nodeBounds.Y - StructuralRegions.Padding);
        r.Bounds.Right.Should().Be(nodeBounds.Right + StructuralRegions.Padding);
        r.Bounds.Bottom.Should().Be(nodeBounds.Bottom + StructuralRegions.Padding);
    }

    [Fact]
    public void UnwiredStructuralPort_YieldsNoRegion()
    {
        var doc = new DesignerDocument { Name = "wf" };
        doc.Nodes.Add(Node("loop", "builtin.loop.foreach", 0, 0));

        StructuralRegions.Compute(doc).Should().BeEmpty();
    }

    [Fact]
    public void OrdinaryGraph_YieldsNoRegions()
    {
        var doc = new DesignerDocument { Name = "wf" };
        doc.Nodes.Add(Node("a", "builtin.log", 0, 0));
        doc.Nodes.Add(Node("b", "builtin.log", 300, 0));
        doc.Connections.Add(Edge("a", "output", "b"));

        StructuralRegions.Compute(doc).Should().BeEmpty();
    }

    [Fact]
    public void CyclicBody_DoesNotLoopForever()
    {
        var doc = new DesignerDocument { Name = "wf" };
        doc.Nodes.Add(Node("loop", "builtin.loop.foreach", 0, 0));
        doc.Nodes.Add(Node("a", "builtin.log", 300, 0));
        doc.Nodes.Add(Node("b", "builtin.log", 600, 0));
        doc.Connections.Add(Edge("loop", "loopBody", "a"));
        doc.Connections.Add(Edge("a", "output", "b"));
        doc.Connections.Add(Edge("b", "output", "a")); // cycle a↔b

        var r = StructuralRegions.Compute(doc).Single();

        r.NodeIds.Should().BeEquivalentTo(new[] { "a", "b" });
    }

    [Fact]
    public void ClosureNeverIncludesOwner()
    {
        var doc = new DesignerDocument { Name = "wf" };
        doc.Nodes.Add(Node("loop", "builtin.loop.foreach", 0, 0));
        doc.Nodes.Add(Node("a", "builtin.log", 300, 0));
        doc.Connections.Add(Edge("loop", "loopBody", "a"));
        doc.Connections.Add(Edge("a", "output", "loop")); // body feeds back into the loop node

        var r = StructuralRegions.Compute(doc).Single();

        r.NodeIds.Should().Equal("a");
    }
}
