// <copyright file="SelectionInteractionTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Components;

using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Workflow.UI.Client.Designer.Components;
using Workflow.UI.Client.Designer.State;
using Workflow.UI.Client.Services;
using Xunit;

/// <summary>
/// 🧪 Phase 3.3.b.1 — Selection + node-move interaction specs via CanvasView~ ✨.
/// </summary>
public sealed class SelectionInteractionTests : TestContext
{
    public SelectionInteractionTests()
    {
        this.JSInterop.Mode = JSRuntimeMode.Loose;
        this.Services.AddSingleton(new PaletteDragState());
    }

    private static DesignerDocument TwoNodeDoc()
    {
        var doc = new DesignerDocument { Name = "wf" };
        doc.Nodes.Add(new DesignerNode { Id = "a", ModuleId = "m", Name = "A", X = 100, Y = 100 });
        doc.Nodes.Add(new DesignerNode { Id = "b", ModuleId = "m", Name = "B", X = 400, Y = 100 });
        return doc;
    }

    private IRenderedComponent<CanvasView> Render(DesignerDocument doc, SelectionState sel, CommandStack cmd)
        => this.RenderComponent<CanvasView>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.Selection, sel)
            .Add(x => x.Commands, cmd));

    [Fact]
    public void Click_SelectsSingle()
    {
        var doc = TwoNodeDoc();
        var sel = new SelectionState();
        var cut = this.Render(doc, sel, new CommandStack(doc));

        cut.Find("[data-node-id=a]").PointerDown(new PointerEventArgs { ClientX = 100, ClientY = 100 });

        sel.IsNodeSelected("a").Should().BeTrue();
        sel.Nodes.Should().ContainSingle();
    }

    [Fact]
    public void CtrlClick_Toggles()
    {
        var doc = TwoNodeDoc();
        var sel = new SelectionState();
        var cut = this.Render(doc, sel, new CommandStack(doc));

        cut.Find("[data-node-id=a]").PointerDown(new PointerEventArgs { ClientX = 100, ClientY = 100 });
        cut.Find("[data-node-id=b]").PointerDown(new PointerEventArgs { CtrlKey = true, ClientX = 400, ClientY = 100 });

        sel.Nodes.Should().BeEquivalentTo(new[] { "a", "b" });
    }

    [Fact]
    public void CanvasBackgroundClick_ClearsSelection()
    {
        var doc = TwoNodeDoc();
        var sel = new SelectionState();
        sel.SelectNode("a");
        var cut = this.Render(doc, sel, new CommandStack(doc));

        var viewport = cut.Find(".df-canvas-viewport");
        viewport.PointerDown(new PointerEventArgs { ClientX = 700, ClientY = 500 });
        viewport.PointerUp(new PointerEventArgs { ClientX = 700, ClientY = 500 });

        sel.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void DragNode_ProducesSingleMoveCommand_UndoRestores()
    {
        var doc = TwoNodeDoc();
        var sel = new SelectionState();
        var cmd = new CommandStack(doc);
        var cut = this.Render(doc, sel, cmd);

        var node = cut.Find("[data-node-id=a]");
        node.PointerDown(new PointerEventArgs { ClientX = 100, ClientY = 100 });

        var viewport = cut.Find(".df-canvas-viewport");
        viewport.PointerMove(new PointerEventArgs { ClientX = 150, ClientY = 130 });
        viewport.PointerUp(new PointerEventArgs { ClientX = 150, ClientY = 130 });

        doc.FindNode("a")!.X.Should().Be(150);
        doc.FindNode("a")!.Y.Should().Be(130);
        cmd.CanUndo.Should().BeTrue();

        cmd.Undo();
        doc.FindNode("a")!.X.Should().Be(100);
        doc.FindNode("a")!.Y.Should().Be(100);
    }

    [Fact]
    public void RubberBand_SelectsIntersectingNodes()
    {
        var doc = TwoNodeDoc();
        var sel = new SelectionState();
        var cut = this.Render(doc, sel, new CommandStack(doc));

        var viewport = cut.Find(".df-canvas-viewport");
        // Shift-drag a rectangle from (80,80) to (330,260) — covers node "a" (at 100,100) only.
        viewport.PointerDown(new PointerEventArgs { ShiftKey = true, OffsetX = 80, OffsetY = 80, ClientX = 80, ClientY = 80 });
        viewport.PointerMove(new PointerEventArgs { ShiftKey = true, OffsetX = 330, OffsetY = 260, ClientX = 330, ClientY = 260 });
        viewport.PointerUp(new PointerEventArgs { OffsetX = 330, OffsetY = 260, ClientX = 330, ClientY = 260 });

        sel.IsNodeSelected("a").Should().BeTrue();
        sel.IsNodeSelected("b").Should().BeFalse();
    }
}
