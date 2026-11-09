// <copyright file="OnErrorPanelTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Components;

using System.Collections.Generic;
using System.Linq;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Workflow.UI.Client.Api.Dtos;
using Workflow.UI.Client.Designer.Components;
using Workflow.UI.Client.Designer.State;
using Xunit;

/// <summary>§12.7 — the "On error" section of the properties panel, and the dashed canvas link.</summary>
public sealed class OnErrorPanelTests : TestContext
{
    public OnErrorPanelTests()
    {
        this.JSInterop.Mode = JSRuntimeMode.Loose;
        this.Services.AddSingleton(new Workflow.UI.Client.Scripts.State.ScriptStudioHandoff());
        this.Services.AddSingleton(new Workflow.UI.Client.Linq.State.LinqStudioHandoff());
    }

    private static (DesignerDocument Doc, CommandStack Cmd, SelectionState Sel) Setup()
    {
        var doc = new DesignerDocument();
        var schema = new ModuleSchemaDto(new List<PortDefinitionDto>(), new List<PortDefinitionDto>(), new List<ModulePropertyDefinitionDto>());
        doc.Nodes.Add(new DesignerNode { Id = "work", ModuleId = "m", Name = "Work", Schema = schema });
        doc.Nodes.Add(new DesignerNode { Id = "after", ModuleId = "m", Name = "After", Schema = schema, X = 300 });
        doc.Nodes.Add(new DesignerNode { Id = "alert", ModuleId = "m", Name = "Alert", Schema = schema, Y = 200 });
        doc.Connections.Add(new DesignerConnection { SourceNodeId = "work", SourcePortName = "output", TargetNodeId = "after", TargetPortName = "input" });
        var sel = new SelectionState();
        sel.SelectNode("work");
        return (doc, new CommandStack(doc), sel);
    }

    private IRenderedComponent<PropertiesPanel> Render(DesignerDocument doc, CommandStack cmd, SelectionState sel)
        => this.RenderComponent<PropertiesPanel>(p => p.Add(x => x.Document, doc).Add(x => x.Selection, sel).Add(x => x.Commands, cmd));

    [Fact]
    public void ChoosingAHandler_StoresTheSettings_Undoably()
    {
        var (doc, cmd, sel) = Setup();
        var cut = this.Render(doc, cmd, sel);

        cut.FindAll("[data-testid=on-error-handler]").Should().BeEmpty(because: "only 'Run an error handler' asks for one");
        cut.Find("[data-testid=on-error-behavior]").Change(NodeErrorHandling.UseErrorHandler);

        cut.Find("[data-testid=on-error-handler]").QuerySelectorAll("option").Select(o => o.GetAttribute("value"))
            .Should().Equal(new[] { string.Empty, "alert" }, because: "'after' is downstream of the node, so it's skipped when the node fails");
        cut.Find("[data-testid=on-error-handler]").Change("alert");
        cut.Find("[data-testid=on-error-max]").Change("3");

        NodeErrorHandling.Read(doc.FindNode("work")!.ErrorHandling)
            .Should().Be(new NodeErrorHandling.Settings(NodeErrorHandling.UseErrorHandler, "alert", 3));
        cut.Find("[data-testid=on-error-help]").TextContent.Should().Contain("{{Variable.error}}");

        cmd.Undo();
        cmd.Undo();
        cmd.Undo();
        doc.FindNode("work")!.ErrorHandling.Should().BeNull();
    }

    [Fact]
    public void TheCanvasDrawsADashedOnErrorLink()
    {
        var (doc, _, _) = Setup();
        doc.FindNode("work")!.ErrorHandling = NodeErrorHandling.Write(new NodeErrorHandling.Settings(NodeErrorHandling.UseErrorHandler, "alert"));

        var cut = this.RenderComponent<EdgeLayer>(p => p.Add(x => x.Document, doc));

        var link = cut.Find("[data-testid=error-link]");
        link.GetAttribute("data-error-link").Should().Be("work→alert");
        link.QuerySelector("path")!.GetAttribute("stroke-dasharray").Should().NotBeNullOrEmpty();
        link.TextContent.Should().Contain("on error");
    }
}
