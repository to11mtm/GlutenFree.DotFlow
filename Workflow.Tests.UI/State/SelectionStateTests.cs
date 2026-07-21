// <copyright file="SelectionStateTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.State;

using FluentAssertions;
using Workflow.UI.Client.Designer.State;
using Xunit;

/// <summary>
/// 🧪 Phase 3.3.b.1 — Selection-state specs~ ✨.
/// </summary>
public sealed class SelectionStateTests
{
    [Fact]
    public void SelectNode_SelectsSingle_ClearsOthers()
    {
        var sel = new SelectionState();
        sel.SelectNode("a");
        sel.SelectNode("b");

        sel.IsNodeSelected("b").Should().BeTrue();
        sel.IsNodeSelected("a").Should().BeFalse();
        sel.Nodes.Should().ContainSingle();
    }

    [Fact]
    public void ToggleNode_AddsAndRemoves()
    {
        var sel = new SelectionState();
        sel.SelectNode("a");
        sel.ToggleNode("b");
        sel.Nodes.Should().BeEquivalentTo(new[] { "a", "b" });

        sel.ToggleNode("a");
        sel.Nodes.Should().BeEquivalentTo(new[] { "b" });
    }

    [Fact]
    public void Clear_EmptiesSelection()
    {
        var sel = new SelectionState();
        sel.SelectNode("a");
        sel.Clear();
        sel.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void SelectAll_SelectsEveryNode()
    {
        var doc = new DesignerDocument();
        doc.Nodes.Add(new DesignerNode { Id = "a", ModuleId = "m", Name = "a" });
        doc.Nodes.Add(new DesignerNode { Id = "b", ModuleId = "m", Name = "b" });
        var sel = new SelectionState();

        sel.SelectAll(doc);

        sel.Nodes.Should().BeEquivalentTo(new[] { "a", "b" });
    }

    [Fact]
    public void Prune_RemovesDeletedIds()
    {
        var doc = new DesignerDocument();
        doc.Nodes.Add(new DesignerNode { Id = "a", ModuleId = "m", Name = "a" });
        var sel = new SelectionState();
        sel.SetNodes(new[] { "a", "ghost" });

        sel.Prune(doc);

        sel.Nodes.Should().BeEquivalentTo(new[] { "a" });
    }

    [Fact]
    public void Changed_Fires_OnSelect()
    {
        var sel = new SelectionState();
        var fired = 0;
        sel.Changed += () => fired++;

        sel.SelectNode("a");

        fired.Should().BeGreaterThan(0);
    }
}
