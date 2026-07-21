// <copyright file="StructuralAffordanceTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Components;

using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Workflow.UI.Client.Designer.Components;
using Workflow.UI.Client.Designer.State;
using Xunit;

/// <summary>
/// 🧪 UX-feedback F4/F5 — loop & try/catch designer affordances: dynamic trycatch ports,
/// dashed structural edges, and properties-panel callouts~ ✨.
/// </summary>
public sealed class StructuralAffordanceTests : TestContext
{
    public StructuralAffordanceTests()
    {
        this.JSInterop.Mode = JSRuntimeMode.Loose;
        this.Services.AddSingleton(new Workflow.UI.Client.Services.PaletteDragState());
        this.Services.AddSingleton(new Workflow.UI.Client.Scripts.State.ScriptStudioHandoff());
    }

    [Fact]
    public void TryCatch_EmptySchemaOutputs_SurfaceConventionalPorts()
    {
        // TryCatch declares zero output ports (dynamic) — the designer must surface them.
        var node = new DesignerNode { Id = "tc-1", ModuleId = "builtin.trycatch", Name = "Guard" };

        NodePorts.Outputs(node).Should().Equal("try", "catch", "finally", "done");
    }

    [Theory]
    [InlineData("loopBody", true)]
    [InlineData("try", true)]
    [InlineData("catch", true)]
    [InlineData("finally", true)]
    [InlineData("done", false)]
    [InlineData("success", false)]
    public void IsStructuralPort_Classifies(string port, bool expected)
        => NodePorts.IsStructuralPort(port).Should().Be(expected);

    [Fact]
    public void LoopBodyEdge_RendersDashed_OtherEdgesSolid()
    {
        var doc = new DesignerDocument { Name = "wf" };
        doc.Nodes.Add(new DesignerNode { Id = "loop-1", ModuleId = "builtin.loop.foreach", Name = "Loop", X = 100, Y = 100 });
        doc.Nodes.Add(new DesignerNode { Id = "work-1", ModuleId = "builtin.log", Name = "Work", X = 400, Y = 100 });
        doc.Nodes.Add(new DesignerNode { Id = "next-1", ModuleId = "builtin.log", Name = "Next", X = 400, Y = 300 });
        doc.Connections.Add(new DesignerConnection { SourceNodeId = "loop-1", SourcePortName = "loopBody", TargetNodeId = "work-1", TargetPortName = "input" });
        doc.Connections.Add(new DesignerConnection { SourceNodeId = "loop-1", SourcePortName = "done", TargetNodeId = "next-1", TargetPortName = "input" });

        var cut = this.RenderComponent<CanvasView>(p => p.Add(x => x.Document, doc));

        cut.FindAll("path.df-edge--structural").Should().HaveCount(1);
        cut.FindAll("path.df-edge").Should().HaveCount(2);
    }

    [Theory]
    [InlineData("builtin.loop.foreach", "loopBody")]
    [InlineData("builtin.trycatch", "catch")]
    public void PropertiesPanel_ShowsStructuralHint(string moduleId, string expectedText)
    {
        var doc = new DesignerDocument { Name = "wf" };
        doc.Nodes.Add(new DesignerNode { Id = "n1", ModuleId = moduleId, Name = "N" });
        var selection = new SelectionState();
        selection.SelectNode("n1");

        var cut = this.RenderComponent<PropertiesPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.Selection, selection)
            .Add(x => x.Commands, new CommandStack(doc)));

        cut.Find("[data-testid=structural-hint]").TextContent.Should().Contain(expectedText);
    }

    [Fact]
    public void PropertiesPanel_NoHint_ForOrdinaryModules()
    {
        var doc = new DesignerDocument { Name = "wf" };
        doc.Nodes.Add(new DesignerNode { Id = "n1", ModuleId = "builtin.log", Name = "N" });
        var selection = new SelectionState();
        selection.SelectNode("n1");

        var cut = this.RenderComponent<PropertiesPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.Selection, selection)
            .Add(x => x.Commands, new CommandStack(doc)));

        cut.FindAll("[data-testid=structural-hint]").Should().BeEmpty();
    }
}
