// <copyright file="CommandStackTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.State;

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Workflow.UI.Client.Designer.State;
using Workflow.UI.Client.Designer.State.Commands;
using Xunit;

/// <summary>
/// 🧪 Phase 3.3.b — Command-stack + command specs (undo/redo, dirty tracking, cap)~ ✨.
/// </summary>
public sealed class CommandStackTests
{
    private static JsonElement El(string j) => JsonDocument.Parse(j).RootElement.Clone();

    private static DesignerNode NewNode(string id, double x = 0, double y = 0)
        => new() { Id = id, ModuleId = "m", Name = id, X = x, Y = y };

    [Fact]
    public void AddNode_Do_Undo()
    {
        var doc = new DesignerDocument();
        var stack = new CommandStack(doc);

        stack.Execute(new AddNodeCommand(NewNode("a")));
        doc.Nodes.Should().ContainSingle();

        stack.Undo();
        doc.Nodes.Should().BeEmpty();

        stack.Redo();
        doc.Nodes.Should().ContainSingle();
    }

    [Fact]
    public void RemoveNodes_RemovesAttachedConnections_UndoRestoresBoth()
    {
        var doc = new DesignerDocument();
        doc.Nodes.Add(NewNode("a"));
        doc.Nodes.Add(NewNode("b"));
        doc.Connections.Add(new DesignerConnection { SourceNodeId = "a", SourcePortName = "o", TargetNodeId = "b", TargetPortName = "i" });
        var stack = new CommandStack(doc);

        stack.Execute(new RemoveNodesCommand(new[] { "a" }));
        doc.Nodes.Should().ContainSingle(n => n.Id == "b");
        doc.Connections.Should().BeEmpty();

        stack.Undo();
        doc.Nodes.Should().HaveCount(2);
        doc.Connections.Should().ContainSingle();
    }

    [Fact]
    public void MoveNodes_UndoRestoresPositions()
    {
        var doc = new DesignerDocument();
        doc.Nodes.Add(NewNode("a", 10, 10));
        var stack = new CommandStack(doc);

        var before = new Dictionary<string, (double, double)> { ["a"] = (10, 10) };
        var after = new Dictionary<string, (double, double)> { ["a"] = (99, 88) };
        stack.Execute(new MoveNodesCommand(before, after));
        doc.FindNode("a")!.X.Should().Be(99);

        stack.Undo();
        doc.FindNode("a")!.X.Should().Be(10);
        doc.FindNode("a")!.Y.Should().Be(10);
    }

    [Fact]
    public void EditProperties_UndoRestoresBefore()
    {
        var doc = new DesignerDocument();
        var node = NewNode("a");
        node.Properties["url"] = El("\"old\"");
        doc.Nodes.Add(node);
        var stack = new CommandStack(doc);

        var before = new Dictionary<string, JsonElement>(node.Properties);
        var after = new Dictionary<string, JsonElement> { ["url"] = El("\"new\"") };
        stack.Execute(new EditNodePropertiesCommand("a", before, after));
        doc.FindNode("a")!.Properties["url"].GetString().Should().Be("new");

        stack.Undo();
        doc.FindNode("a")!.Properties["url"].GetString().Should().Be("old");
    }

    [Fact]
    public void Stack_NewCommand_TruncatesRedoTail()
    {
        var doc = new DesignerDocument();
        var stack = new CommandStack(doc);
        stack.Execute(new AddNodeCommand(NewNode("a")));
        stack.Execute(new AddNodeCommand(NewNode("b")));
        stack.Undo(); // b removed, redo available

        stack.CanRedo.Should().BeTrue();
        stack.Execute(new AddNodeCommand(NewNode("c"))); // truncates redo of b
        stack.CanRedo.Should().BeFalse();
        doc.Nodes.Select(n => n.Id).Should().BeEquivalentTo(new[] { "a", "c" });
    }

    [Fact]
    public void Stack_CapAt50_DropsOldest()
    {
        var doc = new DesignerDocument();
        var stack = new CommandStack(doc);
        for (var i = 0; i < 60; i++)
        {
            stack.Execute(new AddNodeCommand(NewNode($"n{i}")));
        }

        // Only 50 undos are retained.
        var undoCount = 0;
        while (stack.CanUndo)
        {
            stack.Undo();
            undoCount++;
        }

        undoCount.Should().Be(50);
    }

    [Fact]
    public void Dirty_TrueAfterEdit_FalseAfterSave_TrueAfterUndoPastSavePoint()
    {
        var doc = new DesignerDocument();
        var stack = new CommandStack(doc);
        stack.IsDirty.Should().BeFalse();

        stack.Execute(new AddNodeCommand(NewNode("a")));
        stack.IsDirty.Should().BeTrue();

        stack.MarkSaved();
        stack.IsDirty.Should().BeFalse();

        stack.Undo();
        stack.IsDirty.Should().BeTrue();

        stack.Redo();
        stack.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void UndoRedo_Descriptions_Reflectcommand()
    {
        var doc = new DesignerDocument();
        var stack = new CommandStack(doc);
        stack.Execute(new AddNodeCommand(NewNode("a")));

        stack.UndoDescription.Should().Contain("Add");
        stack.Undo();
        stack.RedoDescription.Should().Contain("Add");
    }
}
