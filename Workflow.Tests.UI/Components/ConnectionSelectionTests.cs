// <copyright file="ConnectionSelectionTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Components;

using System.Collections.Generic;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Workflow.UI.Client.Api.Dtos;
using Workflow.UI.Client.Designer.Components;
using Workflow.UI.Client.Designer.State;
using Workflow.UI.Client.Designer.State.Commands;
using Xunit;

/// <summary>
/// 🧪 Connection selection/inspection/deletion in the designer (EdgeLayer + PropertiesPanel)~ ✨.
/// </summary>
public sealed class ConnectionSelectionTests : TestContext
{
    public ConnectionSelectionTests()
    {
        this.JSInterop.Mode = JSRuntimeMode.Loose;
        this.Services.AddSingleton(new Workflow.UI.Client.Services.PaletteDragState());
        this.Services.AddSingleton(new Workflow.UI.Client.Scripts.State.ScriptStudioHandoff());
    }

    private static ModuleSchemaDto HttpSchema()
        => new(
            new List<PortDefinitionDto> { new("input", "Input", "object", null, true, null) },
            new List<PortDefinitionDto>
            {
                new("success", "Success", "object", null, false, null),
                new("error", "Error", "object", null, false, null),
            },
            new List<ModulePropertyDefinitionDto>());

    private static DesignerDocument TwoNodeDoc()
    {
        var doc = new DesignerDocument { Name = "wf" };
        doc.Nodes.Add(new DesignerNode { Id = "http-1", ModuleId = "builtin.http.request", Name = "HTTP", X = 100, Y = 100, Schema = HttpSchema() });
        doc.Nodes.Add(new DesignerNode { Id = "log-1", ModuleId = "builtin.log", Name = "Log", X = 400, Y = 100 });
        doc.Connections.Add(new DesignerConnection { SourceNodeId = "http-1", SourcePortName = "success", TargetNodeId = "log-1", TargetPortName = "input" });
        return doc;
    }

    [Fact]
    public void Edge_HasHitPath_WhenSelectionProvided()
    {
        var doc = TwoNodeDoc();
        var cut = this.RenderComponent<CanvasView>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.Selection, new SelectionState())
            .Add(x => x.Commands, new CommandStack(doc)));

        cut.FindAll("path.df-edge-hit").Should().HaveCount(1);
    }

    [Fact]
    public void ClickingEdge_SelectsConnection()
    {
        var selection = new SelectionState();
        var doc = TwoNodeDoc();
        var cut = this.RenderComponent<CanvasView>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.Selection, selection)
            .Add(x => x.Commands, new CommandStack(doc)));

        cut.Find("path.df-edge-hit").PointerDown();

        selection.Connections.Should().ContainSingle()
            .Which.Should().Be(doc.Connections[0].Key);
        cut.FindAll("path.df-edge--selected").Should().HaveCount(1);
    }

    [Fact]
    public void PropertiesPanel_ShowsFromTo_ForSelectedConnection()
    {
        var doc = TwoNodeDoc();
        var selection = new SelectionState();
        selection.SelectConnection(doc.Connections[0].Key);

        var cut = this.RenderComponent<PropertiesPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.Selection, selection)
            .Add(x => x.Commands, new CommandStack(doc)));

        cut.Find("[data-testid=conn-from]").TextContent.Should().Contain("HTTP").And.Contain("success");
        cut.Find("[data-testid=conn-to]").TextContent.Should().Contain("Log").And.Contain("input");
    }

    [Fact]
    public void PropertiesPanel_DeleteButton_RemovesConnection()
    {
        var doc = TwoNodeDoc();
        var selection = new SelectionState();
        selection.SelectConnection(doc.Connections[0].Key);
        var commands = new CommandStack(doc);

        var cut = this.RenderComponent<PropertiesPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.Selection, selection)
            .Add(x => x.Commands, commands));

        cut.Find("[data-testid=conn-delete]").Click();

        doc.Connections.Should().BeEmpty();
        selection.Connections.Should().BeEmpty();
    }

    [Fact]
    public void PropertiesPanel_ApplyCondition_EditsConnection()
    {
        var doc = TwoNodeDoc();
        var selection = new SelectionState();
        selection.SelectConnection(doc.Connections[0].Key);
        var commands = new CommandStack(doc);

        var cut = this.RenderComponent<PropertiesPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.Selection, selection)
            .Add(x => x.Commands, commands));

        cut.Find("[data-testid=conn-condition]").Change("output.status == 200");
        cut.Find("[data-testid=conn-apply]").Click();

        doc.Connections[0].Condition.Should().Be("output.status == 200");
    }

    [Fact]
    public void SelectedConnection_HighlightsEndpointPorts()
    {
        var doc = TwoNodeDoc();
        var selection = new SelectionState();
        var cut = this.RenderComponent<CanvasView>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.Selection, selection)
            .Add(x => x.Commands, new CommandStack(doc)));

        cut.Find("path.df-edge-hit").PointerDown();

        // The success output on http-1 and input on log-1 are the selected edge's endpoints.
        cut.Find("[data-node-id='http-1'] [data-port-out=success]").ClassList.Should().Contain("df-port--endpoint");
        cut.Find("[data-node-id='log-1'] [data-port-in=input]").ClassList.Should().Contain("df-port--endpoint");
        // The unrelated error port is not highlighted.
        cut.Find("[data-node-id='http-1'] [data-port-out=error]").ClassList.Should().NotContain("df-port--endpoint");
    }

    [Fact]
    public void ConnectionDrag_HighlightsSourcePort_AndCompatibleTargets()
    {
        var doc = TwoNodeDoc();
        var selection = new SelectionState();
        var cut = this.RenderComponent<CanvasView>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.Selection, selection)
            .Add(x => x.Commands, new CommandStack(doc)));

        // Start a connection drag from http-1's error output.
        cut.Find("[data-node-id='http-1'] [data-port-out=error]").PointerDown();

        cut.Find("[data-node-id='http-1'] [data-port-out=error]").ClassList.Should().Contain("df-port--source");
        cut.Find("[data-node-id='http-1'] [data-port-out=success]").ClassList.Should().NotContain("df-port--source");
        // log-1's input is a valid target → compatible highlight.
        cut.Find("[data-node-id='log-1'] [data-port-in=input]").ClassList.Should().Contain("df-port--compatible");
    }

    [Fact]
    public void FanInDrag_ShowsDropZones_PerNode()
    {
        var doc = TwoNodeDoc();
        var drag = this.Services.GetRequiredService<Workflow.UI.Client.Services.PaletteDragState>();
        var cut = this.RenderComponent<CanvasView>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.Selection, new SelectionState())
            .Add(x => x.Commands, new CommandStack(doc)));

        cut.FindAll(".df-dropzone").Should().BeEmpty();

        drag.Begin("builtin.fanin");
        cut.WaitForAssertion(() => cut.FindAll(".df-dropzone").Should().HaveCount(2));

        drag.End();
        cut.WaitForAssertion(() => cut.FindAll(".df-dropzone").Should().BeEmpty());
    }

    [Theory]
    [InlineData("builtin.loop.foreach", "loop from here")]
    [InlineData("builtin.loop.while", "loop from here")]
    [InlineData("builtin.trycatch", "guard from here")]
    public void StructuralDrag_ShowsDropZones_WithKindLabel(string moduleId, string expectedLabel)
    {
        var doc = TwoNodeDoc();
        var drag = this.Services.GetRequiredService<Workflow.UI.Client.Services.PaletteDragState>();
        var cut = this.RenderComponent<CanvasView>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.Selection, new SelectionState())
            .Add(x => x.Commands, new CommandStack(doc)));

        drag.Begin(moduleId);
        cut.WaitForAssertion(() =>
        {
            cut.FindAll(".df-dropzone").Should().HaveCount(2);
            cut.Find(".df-dropzone .df-dropzone__label").TextContent.Should().Contain(expectedLabel);
        });
        drag.End();
    }

    [Fact]
    public void OrdinaryModuleDrag_ShowsGenericWireZones()
    {
        var doc = TwoNodeDoc();
        var drag = this.Services.GetRequiredService<Workflow.UI.Client.Services.PaletteDragState>();
        var cut = this.RenderComponent<CanvasView>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.Selection, new SelectionState())
            .Add(x => x.Commands, new CommandStack(doc)));

        // UX-R2: every palette drag can dock onto a node's output side.
        drag.Begin("builtin.log");
        cut.WaitForAssertion(() =>
        {
            cut.FindAll(".df-dropzone").Should().HaveCount(2);
            cut.Find(".df-dropzone .df-dropzone__label").TextContent.Should().Contain("wire from here");
        });
        drag.End();
        cut.WaitForAssertion(() => cut.FindAll(".df-dropzone").Should().BeEmpty());
    }

    [Fact]
    public void FanInDrag_OverOutputSide_ArmsZone()
    {
        var doc = TwoNodeDoc(); // http-1 at (100,100), right edge = 300.
        var drag = this.Services.GetRequiredService<Workflow.UI.Client.Services.PaletteDragState>();
        var cut = this.RenderComponent<CanvasView>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.Selection, new SelectionState())
            .Add(x => x.Commands, new CommandStack(doc)));

        drag.Begin("builtin.fanin");
        cut.WaitForAssertion(() => cut.FindAll(".df-dropzone").Should().HaveCount(2));

        // Hover in http-1's output-side zone → armed; then over empty space → disarmed.
        cut.Find(".df-canvas-viewport").DragOver(new Microsoft.AspNetCore.Components.Web.DragEventArgs { OffsetX = 320, OffsetY = 130 });
        cut.WaitForAssertion(() =>
            cut.Find("[data-dropzone-for='http-1']").ClassList.Should().Contain("df-dropzone--armed"));
        cut.Find("[data-dropzone-for='log-1']").ClassList.Should().NotContain("df-dropzone--armed");

        cut.Find(".df-canvas-viewport").DragOver(new Microsoft.AspNetCore.Components.Web.DragEventArgs { OffsetX = 900, OffsetY = 600 });
        cut.WaitForAssertion(() =>
            cut.Find("[data-dropzone-for='http-1']").ClassList.Should().NotContain("df-dropzone--armed"));
        drag.End();
    }
}
