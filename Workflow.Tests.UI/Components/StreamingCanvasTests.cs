// <copyright file="StreamingCanvasTests.cs" company="GlutenFree">
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
/// 🧪 Phase 5.1.2 — the designer's streaming affordances: diamond ports, stream-styled edges,
/// region halos, and — most importantly — an invalid streaming graph you simply <b>cannot draw</b>~ 🌊.
/// </summary>
public sealed class StreamingCanvasTests : TestContext
{
    public StreamingCanvasTests()
    {
        this.JSInterop.Mode = JSRuntimeMode.Loose;
        this.Services.AddSingleton(new PaletteDragState());
        this.Services.AddSingleton(new Workflow.UI.Client.Scripts.State.ScriptStudioHandoff());
        this.Services.AddSingleton(new Workflow.UI.Client.Linq.State.LinqStudioHandoff());
    }

    private static PortDefinitionDto Port(string name, bool streaming = false)
        => new(name, name, "object", null, false, null, streaming);

    private static ModuleSchemaDto StreamSchema()
        => new(
            new List<PortDefinitionDto> { Port("items", streaming: true) },
            new List<PortDefinitionDto> { Port("items", streaming: true) },
            new List<ModulePropertyDefinitionDto>());

    private static ModuleSchemaDto BatchSchema()
        => new(
            new List<PortDefinitionDto> { Port("in") },
            new List<PortDefinitionDto> { Port("out") },
            new List<ModulePropertyDefinitionDto>());

    private static DesignerNode Node(string id, ModuleSchemaDto schema, double x)
        => new() { Id = id, ModuleId = "m", Name = id, X = x, Y = 100, Schema = schema };

    private IRenderedComponent<CanvasView> Render(DesignerDocument doc, CommandStack cmd)
        => this.RenderComponent<CanvasView>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.Selection, new SelectionState())
            .Add(x => x.Commands, cmd));

    [Fact]
    public void StreamingPorts_RenderWithTheStreamGlyph()
    {
        var doc = new DesignerDocument { Name = "wf" };
        doc.Nodes.Add(Node("a", StreamSchema(), 100));

        var cut = this.Render(doc, new CommandStack(doc));

        cut.Find("[data-node-id=a] [data-port-out=items]").ClassList.Should().Contain("df-port--stream");
        cut.Find("[data-node-id=a] [data-port-out=items]").GetAttribute("data-port-streaming").Should().Be("true");
    }

    [Fact]
    public void BatchPorts_KeepTheOrdinaryGlyph()
    {
        var doc = new DesignerDocument { Name = "wf" };
        doc.Nodes.Add(Node("a", BatchSchema(), 100));

        var cut = this.Render(doc, new CommandStack(doc));

        cut.Find("[data-node-id=a] [data-port-out=out]").ClassList.Should().NotContain("df-port--stream");
    }

    [Fact]
    public void StreamingPort_TooltipTeachesTheRule()
    {
        var doc = new DesignerDocument { Name = "wf" };
        doc.Nodes.Add(Node("a", StreamSchema(), 100));

        var cut = this.Render(doc, new CommandStack(doc));

        cut.Find("[data-node-id=a] [data-port-out=items]").GetAttribute("title")
            .Should().Contain("connects only to other streaming ports");
    }

    [Fact]
    public void DraggingStreamOutputIntoBatchInput_IsRefused()
    {
        var doc = new DesignerDocument { Name = "wf" };
        doc.Nodes.Add(Node("a", StreamSchema(), 100));
        doc.Nodes.Add(Node("b", BatchSchema(), 400));
        var cmd = new CommandStack(doc);
        var cut = this.Render(doc, cmd);

        cut.Find("[data-node-id=a] [data-port-out=items]").PointerDown(new PointerEventArgs());
        cut.Find(".df-canvas-viewport").PointerMove(new PointerEventArgs { OffsetX = 300, OffsetY = 120 });
        cut.Find("[data-node-id=b] [data-port-in=in]").PointerUp(new PointerEventArgs());

        doc.Connections.Should().BeEmpty("a stream may only meet a stream — the wire never exists");
        cmd.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void DraggingStreamIntoStream_IsAllowed()
    {
        var doc = new DesignerDocument { Name = "wf" };
        doc.Nodes.Add(Node("a", StreamSchema(), 100));
        doc.Nodes.Add(Node("b", StreamSchema(), 400));
        var cut = this.Render(doc, new CommandStack(doc));

        cut.Find("[data-node-id=a] [data-port-out=items]").PointerDown(new PointerEventArgs());
        cut.Find(".df-canvas-viewport").PointerMove(new PointerEventArgs { OffsetX = 300, OffsetY = 120 });
        cut.Find("[data-node-id=b] [data-port-in=items]").PointerUp(new PointerEventArgs());

        doc.Connections.Should().ContainSingle();
    }

    [Fact]
    public void StreamingEdge_IsDrawnDistinctly()
    {
        var doc = new DesignerDocument { Name = "wf" };
        doc.Nodes.Add(Node("a", StreamSchema(), 100));
        doc.Nodes.Add(Node("b", StreamSchema(), 400));
        doc.Connections.Add(new DesignerConnection
        {
            SourceNodeId = "a", SourcePortName = "items", TargetNodeId = "b", TargetPortName = "items",
        });

        var cut = this.Render(doc, new CommandStack(doc));

        cut.FindAll("[data-edge-streaming=true]").Should().ContainSingle();
    }

    [Fact]
    public void StreamingRegion_DrawsAHalo()
    {
        var doc = new DesignerDocument { Name = "wf" };
        doc.Nodes.Add(Node("a", StreamSchema(), 100));
        doc.Nodes.Add(Node("b", StreamSchema(), 400));
        doc.Connections.Add(new DesignerConnection
        {
            SourceNodeId = "a", SourcePortName = "items", TargetNodeId = "b", TargetPortName = "items",
        });

        var cut = this.Render(doc, new CommandStack(doc));

        var halo = cut.Find(".df-region--stream");
        halo.TextContent.Should().Contain("streaming region");
    }

    [Fact]
    public void BatchOnlyGraph_HasNoStreamHalo()
    {
        var doc = new DesignerDocument { Name = "wf" };
        doc.Nodes.Add(Node("a", BatchSchema(), 100));
        doc.Nodes.Add(Node("b", BatchSchema(), 400));
        doc.Connections.Add(new DesignerConnection
        {
            SourceNodeId = "a", SourcePortName = "out", TargetNodeId = "b", TargetPortName = "in",
        });

        var cut = this.Render(doc, new CommandStack(doc));

        cut.FindAll(".df-region--stream").Should().BeEmpty();
    }

    #region Buffer capacity editor (D18)

    /// <summary>
    /// ⚠️ 5.1.4 — a stage that traded source order for throughput says so on the node itself,
    /// where the person reading the graph will actually see it~
    /// </summary>
    [Fact]
    public void UnorderedStage_ShowsABadgeOnTheNode()
    {
        var doc = new DesignerDocument { Name = "wf" };
        var node = Node("a", StreamSchema(), 100);
        node.Properties["maxWorkers"] = System.Text.Json.JsonDocument.Parse("4").RootElement.Clone();
        node.Properties["ordered"] = System.Text.Json.JsonDocument.Parse("false").RootElement.Clone();
        doc.Nodes.Add(node);

        var cut = this.Render(doc, new CommandStack(doc));

        cut.Find("[data-testid=node-unordered]").TextContent.Should().Contain("unordered");
    }

    [Fact]
    public void OrderedStage_ShowsNoBadge()
    {
        var doc = new DesignerDocument { Name = "wf" };
        var node = Node("a", StreamSchema(), 100);
        node.Properties["maxWorkers"] = System.Text.Json.JsonDocument.Parse("4").RootElement.Clone();
        doc.Nodes.Add(node);

        var cut = this.Render(doc, new CommandStack(doc));

        cut.FindAll("[data-testid=node-unordered]").Should().BeEmpty("ordering is on by default");
    }

    private static DesignerDocument StreamingPair()
    {
        var doc = new DesignerDocument { Name = "wf" };
        doc.Nodes.Add(Node("a", StreamSchema(), 100));
        doc.Nodes.Add(Node("b", StreamSchema(), 400));
        doc.Connections.Add(new DesignerConnection
        {
            SourceNodeId = "a", SourcePortName = "items", TargetNodeId = "b", TargetPortName = "items",
        });
        return doc;
    }

    private IRenderedComponent<PropertiesPanel> RenderPanel(
        DesignerDocument doc,
        CommandStack cmd,
        SelectionState selection)
        => this.RenderComponent<PropertiesPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.Selection, selection)
            .Add(x => x.Commands, cmd));

    [Fact]
    public void SelectingAStreamEdge_OffersTheBufferEditor()
    {
        var doc = StreamingPair();
        var selection = new SelectionState();
        selection.SelectConnection(doc.Connections[0].Key);

        var cut = this.RenderPanel(doc, new CommandStack(doc), selection);

        cut.FindAll("[data-testid=conn-buffer]").Should().ContainSingle();
    }

    [Fact]
    public void SelectingABatchEdge_HidesTheBufferEditor()
    {
        var doc = new DesignerDocument { Name = "wf" };
        doc.Nodes.Add(Node("a", BatchSchema(), 100));
        doc.Nodes.Add(Node("b", BatchSchema(), 400));
        doc.Connections.Add(new DesignerConnection
        {
            SourceNodeId = "a", SourcePortName = "out", TargetNodeId = "b", TargetPortName = "in",
        });
        var selection = new SelectionState();
        selection.SelectConnection(doc.Connections[0].Key);

        var cut = this.RenderPanel(doc, new CommandStack(doc), selection);

        cut.FindAll("[data-testid=conn-buffer]").Should().BeEmpty("capacity is meaningless off a stream");
    }

    [Fact]
    public void EditingBufferCapacity_IsUndoable()
    {
        var doc = StreamingPair();
        var cmd = new CommandStack(doc);
        var selection = new SelectionState();
        selection.SelectConnection(doc.Connections[0].Key);
        var cut = this.RenderPanel(doc, cmd, selection);

        cut.Find("[data-testid=conn-buffer]").Change("256");
        cut.Find("[data-testid=conn-apply]").Click();

        doc.Connections[0].BufferCapacity.Should().Be(256);

        cmd.Undo();
        doc.Connections[0].BufferCapacity.Should().BeNull("clearing back to 'inherit' is what undo means here");
    }

    #endregion
}
