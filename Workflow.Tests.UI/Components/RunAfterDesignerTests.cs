// <copyright file="RunAfterDesignerTests.cs" company="GlutenFree">
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
using Workflow.UI.Client.Designer.State.Commands;
using Xunit;

/// <summary>
/// Plan 09 — drawing and editing run-after edges: drop a wire on a node's body, see a dotted
/// "after" edge, and switch an edge between Data and Run after in the inspector.
/// </summary>
public sealed class RunAfterDesignerTests : TestContext
{
    public RunAfterDesignerTests()
    {
        this.JSInterop.Mode = JSRuntimeMode.Loose;
        this.Services.AddSingleton(new Workflow.UI.Client.Services.PaletteDragState());
        this.Services.AddSingleton(new Workflow.UI.Client.Scripts.State.ScriptStudioHandoff());
        this.Services.AddSingleton(new Workflow.UI.Client.Linq.State.LinqStudioHandoff());
    }

    private static ModuleSchemaDto Schema(params string[] inputs)
        => new(
            inputs.Select(p => new PortDefinitionDto(p, p, "object", null, false, null)).ToList(),
            new List<PortDefinitionDto> { new("out", "Out", "object", null, false, null) },
            new List<ModulePropertyDefinitionDto>());

    private static DesignerDocument Doc(ModuleSchemaDto targetSchema)
    {
        var doc = new DesignerDocument { Name = "wf" };
        doc.Nodes.Add(new DesignerNode { Id = "a", ModuleId = "m", Name = "A", X = 100, Y = 100, Schema = Schema("in") });
        doc.Nodes.Add(new DesignerNode { Id = "b", ModuleId = "m", Name = "B", X = 400, Y = 100, Schema = targetSchema });
        return doc;
    }

    private IRenderedComponent<CanvasView> Canvas(DesignerDocument doc, CommandStack cmd)
        => this.RenderComponent<CanvasView>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.Selection, new SelectionState())
            .Add(x => x.Commands, cmd));

    [Fact]
    public void DroppingAWireOnANodeBody_CreatesARunAfterEdge_Undoably()
    {
        var doc = Doc(Schema("value"));
        var cmd = new CommandStack(doc);
        var cut = this.Canvas(doc, cmd);

        cut.Find("[data-node-id=a] [data-port-out=out]").PointerDown(new PointerEventArgs());
        var viewport = cut.Find(".df-canvas-viewport");
        viewport.PointerMove(new PointerEventArgs { OffsetX = 430, OffsetY = 120 });
        viewport.PointerUp(new PointerEventArgs { OffsetX = 430, OffsetY = 120 });

        doc.Connections.Should().ContainSingle(c => c.IsRunAfter && c.SourceNodeId == "a" && c.TargetNodeId == "b" && c.TargetPortName == string.Empty);
        cmd.Undo();
        doc.Connections.Should().BeEmpty();
    }

    [Fact]
    public void AWireIntoTheDefaultInputHandle_OfAModuleWithNoInputs_IsRunAfter()
    {
        var doc = Doc(Schema());
        var cut = this.Canvas(doc, new CommandStack(doc));

        cut.Find("[data-node-id=a] [data-port-out=out]").PointerDown(new PointerEventArgs());
        cut.Find(".df-canvas-viewport").PointerMove(new PointerEventArgs { OffsetX = 300, OffsetY = 120 });
        cut.Find("[data-node-id=b] [data-port-in=input]").PointerUp(new PointerEventArgs());

        doc.Connections.Should().ContainSingle(c => c.IsRunAfter, "the module declares no inputs, so the handle only sequences");
    }

    [Fact]
    public void ARunAfterEdge_IsDrawnDotted_AndLabelledAfter()
    {
        var doc = Doc(Schema("value"));
        doc.Connections.Add(RunAfterEdges.Create("a", "out", "b"));

        var cut = this.RenderComponent<EdgeLayer>(p => p.Add(x => x.Document, doc).Add(x => x.Selection, new SelectionState()));

        cut.Find("[data-edge-kind=RunAfter]").GetAttribute("stroke-dasharray").Should().NotBeNullOrEmpty();
        cut.Markup.Should().Contain(">after<");
    }

    [Fact]
    public void Inspector_ShowsRunAfter_AndSwitchesToAData_Port()
    {
        var doc = Doc(Schema("value"));
        doc.Connections.Add(RunAfterEdges.Create("a", "out", "b"));
        var selection = new SelectionState();
        selection.SelectConnection(doc.Connections[0].Key);
        var cmd = new CommandStack(doc);

        var cut = this.RenderComponent<PropertiesPanel>(p => p.Add(x => x.Document, doc).Add(x => x.Selection, selection).Add(x => x.Commands, cmd));

        cut.Find("[data-testid=conn-to]").TextContent.Should().Contain("after (no data)");
        cut.Find("[data-testid=conn-kind-help]").TextContent.Should().Contain("{{a.out}}");

        cut.Find("[data-testid=conn-kind]").Change("value");

        doc.Connections[0].IsRunAfter.Should().BeFalse();
        doc.Connections[0].TargetPortName.Should().Be("value");
        selection.IsConnectionSelected(doc.Connections[0].Key).Should().BeTrue("the edge stays selected under its new key");

        cut.Find("[data-testid=conn-kind]").Change("@after");
        doc.Connections[0].IsRunAfter.Should().BeTrue();
    }
}
