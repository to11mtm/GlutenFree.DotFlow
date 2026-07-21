// <copyright file="ClipboardTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.State;

using System.Linq;
using System.Collections.Generic;
using FluentAssertions;
using Workflow.UI.Client.Designer.State;
using Workflow.UI.Client.Designer.State.Commands;
using Xunit;

/// <summary>
/// 🧪 Phase 3.3.b.4 — Copy/paste clipboard + composite-command specs~ ✨.
/// </summary>
public sealed class ClipboardTests
{
    private static DesignerNode Node(string id) => new() { Id = id, ModuleId = "builtin.http.request", Name = id, X = 10, Y = 10 };

    private static DesignerConnection Conn(string s, string t)
        => new() { SourceNodeId = s, SourcePortName = "out", TargetNodeId = t, TargetPortName = "in" };

    [Fact]
    public void Copy_Then_BuildPaste_ClonesWithFreshIds_AndInternalEdges()
    {
        var doc = new DesignerDocument();
        doc.Nodes.Add(Node("a"));
        doc.Nodes.Add(Node("b"));
        doc.Connections.Add(Conn("a", "b"));
        var clip = new DesignerClipboard();

        clip.Copy(doc, new[] { "a", "b" });
        var existing = new HashSet<string> { "a", "b" };
        var (nodes, conns) = clip.BuildPaste(existing);

        nodes.Should().HaveCount(2);
        nodes.Select(n => n.Id).Should().NotContain(new[] { "a", "b" }, "pasted nodes get fresh ids");
        conns.Should().ContainSingle("the internal edge is re-wired between the new ids");
        conns[0].SourceNodeId.Should().Be(nodes[0].Id);
    }

    [Fact]
    public void BuildPaste_OffsetsPositions()
    {
        var doc = new DesignerDocument();
        doc.Nodes.Add(Node("a"));
        var clip = new DesignerClipboard();
        clip.Copy(doc, new[] { "a" });

        var (nodes, _) = clip.BuildPaste(new HashSet<string> { "a" });

        nodes[0].X.Should().Be(50); // 10 + 40 offset
    }

    [Fact]
    public void Copy_ExternalEdge_NotIncluded()
    {
        var doc = new DesignerDocument();
        doc.Nodes.Add(Node("a"));
        doc.Nodes.Add(Node("b"));
        doc.Connections.Add(Conn("a", "b"));
        var clip = new DesignerClipboard();

        // Only copy "a" — the edge a→b is external to the copied set.
        clip.Copy(doc, new[] { "a" });
        var (nodes, conns) = clip.BuildPaste(new HashSet<string> { "a", "b" });

        nodes.Should().ContainSingle();
        conns.Should().BeEmpty();
    }

    [Fact]
    public void CompositeCommand_Paste_IsSingleUndoUnit()
    {
        var doc = new DesignerDocument();
        var stack = new CommandStack(doc);

        var children = new List<IDesignerCommand>
        {
            new AddNodeCommand(Node("x")),
            new AddNodeCommand(Node("y")),
            new AddConnectionCommand(Conn("x", "y")),
        };
        stack.Execute(new CompositeCommand("Paste", children));

        doc.Nodes.Should().HaveCount(2);
        doc.Connections.Should().ContainSingle();

        stack.Undo();
        doc.Nodes.Should().BeEmpty();
        doc.Connections.Should().BeEmpty();
    }
}
