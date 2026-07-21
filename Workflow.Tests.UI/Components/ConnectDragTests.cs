// <copyright file="ConnectDragTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Components;

using System.Collections.Generic;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Workflow.UI.Client.Api.Dtos;
using Workflow.UI.Client.Designer.Components;
using Workflow.UI.Client.Designer.State;
using Workflow.UI.Client.Services;
using Xunit;

/// <summary>
/// 🧪 Phase 3.3.b.2 — Port-to-port connection drag interaction specs via CanvasView~ ✨.
/// </summary>
public sealed class ConnectDragTests : TestContext
{
    public ConnectDragTests()
    {
        this.JSInterop.Mode = JSRuntimeMode.Loose;
        this.Services.AddSingleton(new PaletteDragState());
    }

    private static ModuleSchemaDto Schema()
        => new(
            new List<PortDefinitionDto> { new("in", "In", "object", null, true, null) },
            new List<PortDefinitionDto> { new("out", "Out", "object", null, false, null) },
            new List<ModulePropertyDefinitionDto>());

    private static DesignerDocument TwoNodeDoc()
    {
        var doc = new DesignerDocument { Name = "wf" };
        doc.Nodes.Add(new DesignerNode { Id = "a", ModuleId = "m", Name = "A", X = 100, Y = 100, Schema = Schema() });
        doc.Nodes.Add(new DesignerNode { Id = "b", ModuleId = "m", Name = "B", X = 400, Y = 100, Schema = Schema() });
        return doc;
    }

    private IRenderedComponent<CanvasView> Render(DesignerDocument doc, CommandStack cmd)
        => this.RenderComponent<CanvasView>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.Selection, new SelectionState())
            .Add(x => x.Commands, cmd));

    private static void DragConnect(IRenderedComponent<CanvasView> cut, string sourceNode, string targetNode)
    {
        cut.Find($"[data-node-id={sourceNode}] [data-port-out=out]").PointerDown(new PointerEventArgs());
        cut.Find(".df-canvas-viewport").PointerMove(new PointerEventArgs { OffsetX = 300, OffsetY = 120 });
        cut.Find($"[data-node-id={targetNode}] [data-port-in=in]").PointerUp(new PointerEventArgs());
    }

    [Fact]
    public void Connect_ValidPorts_CreatesConnection()
    {
        var doc = TwoNodeDoc();
        var cmd = new CommandStack(doc);
        var cut = this.Render(doc, cmd);

        DragConnect(cut, "a", "b");

        doc.Connections.Should().ContainSingle(c =>
            c.SourceNodeId == "a" && c.SourcePortName == "out" && c.TargetNodeId == "b" && c.TargetPortName == "in");
        cmd.CanUndo.Should().BeTrue();
    }

    [Fact]
    public void Connect_Ghost_ShownDuringDrag()
    {
        var doc = TwoNodeDoc();
        var cut = this.Render(doc, new CommandStack(doc));

        cut.Find("[data-node-id=a] [data-port-out=out]").PointerDown(new PointerEventArgs());
        cut.Find(".df-canvas-viewport").PointerMove(new PointerEventArgs { OffsetX = 300, OffsetY = 120 });

        cut.FindAll(".df-connect-ghost").Should().NotBeEmpty();
    }

    [Fact]
    public void Connect_Duplicate_Rejected()
    {
        var doc = TwoNodeDoc();
        doc.Connections.Add(new DesignerConnection { SourceNodeId = "a", SourcePortName = "out", TargetNodeId = "b", TargetPortName = "in" });
        var cmd = new CommandStack(doc);
        var cut = this.Render(doc, cmd);

        DragConnect(cut, "a", "b");

        doc.Connections.Should().ContainSingle("duplicate connections are rejected");
    }

    [Fact]
    public void Connect_WouldCreateCycle_Rejected()
    {
        var doc = TwoNodeDoc();
        doc.Connections.Add(new DesignerConnection { SourceNodeId = "a", SourcePortName = "out", TargetNodeId = "b", TargetPortName = "in" });
        var cmd = new CommandStack(doc);
        var cut = this.Render(doc, cmd);

        // Dragging b → a would create a cycle.
        DragConnect(cut, "b", "a");

        doc.Connections.Should().ContainSingle("a cycle-forming connection is rejected");
    }

    [Fact]
    public void Connect_ReleaseOnBackground_Cancels()
    {
        var doc = TwoNodeDoc();
        var cut = this.Render(doc, new CommandStack(doc));

        cut.Find("[data-node-id=a] [data-port-out=out]").PointerDown(new PointerEventArgs());
        var viewport = cut.Find(".df-canvas-viewport");
        viewport.PointerMove(new PointerEventArgs { OffsetX = 300, OffsetY = 400 });
        viewport.PointerUp(new PointerEventArgs { OffsetX = 300, OffsetY = 400 });

        doc.Connections.Should().BeEmpty();
        cut.FindAll(".df-connect-ghost").Should().BeEmpty();
    }
}
