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
}
