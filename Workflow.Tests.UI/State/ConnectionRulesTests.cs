// <copyright file="ConnectionRulesTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.State;

using FluentAssertions;
using Workflow.UI.Client.Designer.State;
using Workflow.UI.Client.Designer.State.Commands;
using Xunit;

/// <summary>
/// 🧪 Phase 3.3.b.2 — Connection creation + rule specs (self/duplicate/cycle) at the state level~ ✨.
/// </summary>
public sealed class ConnectionRulesTests
{
    private static DesignerNode Node(string id) => new() { Id = id, ModuleId = "m", Name = id };

    private static DesignerConnection Conn(string s, string t)
        => new() { SourceNodeId = s, SourcePortName = "out", TargetNodeId = t, TargetPortName = "in" };

    private static DesignerDocument Doc(params string[] nodeIds)
    {
        var doc = new DesignerDocument();
        foreach (var id in nodeIds)
        {
            doc.Nodes.Add(Node(id));
        }

        return doc;
    }

    [Fact]
    public void AddConnectionCommand_Do_Undo()
    {
        var doc = Doc("a", "b");
        var stack = new CommandStack(doc);

        stack.Execute(new AddConnectionCommand(Conn("a", "b")));
        doc.Connections.Should().ContainSingle();

        stack.Undo();
        doc.Connections.Should().BeEmpty();
    }

    [Fact]
    public void Connect_WouldCreateCycle_DetectedByValidator()
    {
        var doc = Doc("a", "b");
        doc.Connections.Add(Conn("a", "b"));

        // b → a would close a cycle.
        GraphValidator.WouldCreateCycle(doc, "b", "a").Should().BeTrue();
    }

    [Fact]
    public void Connect_DiamondShape_Allowed()
    {
        var doc = Doc("a", "b", "c", "d");
        doc.Connections.Add(Conn("a", "b"));
        doc.Connections.Add(Conn("a", "c"));
        doc.Connections.Add(Conn("b", "d"));

        // c → d completes a diamond, not a cycle.
        GraphValidator.WouldCreateCycle(doc, "c", "d").Should().BeFalse();
    }

    [Fact]
    public void Connect_Self_IsCycle()
    {
        var doc = Doc("a");
        GraphValidator.WouldCreateCycle(doc, "a", "a").Should().BeTrue();
    }

    [Fact]
    public void RemoveConnections_Command_UndoRestores()
    {
        var doc = Doc("a", "b");
        var conn = Conn("a", "b");
        doc.Connections.Add(conn);
        var stack = new CommandStack(doc);

        stack.Execute(new RemoveConnectionsCommand(new[] { conn.Key }));
        doc.Connections.Should().BeEmpty();

        stack.Undo();
        doc.Connections.Should().ContainSingle();
    }

    [Fact]
    public void EditConnection_Command_SetsCondition_UndoRestores()
    {
        var doc = Doc("a", "b");
        var conn = Conn("a", "b");
        doc.Connections.Add(conn);
        var stack = new CommandStack(doc);

        stack.Execute(new EditConnectionCommand(conn.Key, null, "x > 5"));
        doc.Connections[0].Condition.Should().Be("x > 5");

        stack.Undo();
        doc.Connections[0].Condition.Should().BeNull();
    }
}
