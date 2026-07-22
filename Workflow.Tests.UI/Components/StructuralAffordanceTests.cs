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
    public void Canvas_RendersRegion_ForLoopBody()
    {
        var doc = new DesignerDocument { Name = "wf" };
        doc.Nodes.Add(new DesignerNode { Id = "loop-1", ModuleId = "builtin.loop.foreach", Name = "Loop", X = 100, Y = 100 });
        doc.Nodes.Add(new DesignerNode { Id = "work-1", ModuleId = "builtin.log", Name = "Work", X = 400, Y = 100 });
        doc.Connections.Add(new DesignerConnection { SourceNodeId = "loop-1", SourcePortName = "loopBody", TargetNodeId = "work-1", TargetPortName = "input" });

        var cut = this.RenderComponent<CanvasView>(p => p.Add(x => x.Document, doc));

        var region = cut.Find(".df-region--loop");
        region.GetAttribute("data-region-for").Should().Be("loop-1");
        region.TextContent.Should().Contain("loop body");
    }

    [Fact]
    public void Canvas_RendersThreeRegions_ForWiredTryCatch()
    {
        var doc = new DesignerDocument { Name = "wf" };
        doc.Nodes.Add(new DesignerNode { Id = "tc", ModuleId = "builtin.trycatch", Name = "Guard", X = 100, Y = 100 });
        doc.Nodes.Add(new DesignerNode { Id = "t1", ModuleId = "builtin.log", Name = "T", X = 400, Y = 100 });
        doc.Nodes.Add(new DesignerNode { Id = "c1", ModuleId = "builtin.log", Name = "C", X = 400, Y = 300 });
        doc.Nodes.Add(new DesignerNode { Id = "f1", ModuleId = "builtin.log", Name = "F", X = 400, Y = 500 });
        doc.Connections.Add(new DesignerConnection { SourceNodeId = "tc", SourcePortName = "try", TargetNodeId = "t1", TargetPortName = "input" });
        doc.Connections.Add(new DesignerConnection { SourceNodeId = "tc", SourcePortName = "catch", TargetNodeId = "c1", TargetPortName = "input" });
        doc.Connections.Add(new DesignerConnection { SourceNodeId = "tc", SourcePortName = "finally", TargetNodeId = "f1", TargetPortName = "input" });

        var cut = this.RenderComponent<CanvasView>(p => p.Add(x => x.Document, doc));

        cut.FindAll(".df-region").Should().HaveCount(3);
        cut.FindAll(".df-region--try").Should().ContainSingle();
        cut.FindAll(".df-region--catch").Should().ContainSingle();
        cut.FindAll(".df-region--finally").Should().ContainSingle();
    }

    [Fact]
    public void Canvas_NoRegions_ForOrdinaryGraph()
    {
        var doc = new DesignerDocument { Name = "wf" };
        doc.Nodes.Add(new DesignerNode { Id = "a", ModuleId = "builtin.log", Name = "A", X = 100, Y = 100 });
        doc.Nodes.Add(new DesignerNode { Id = "b", ModuleId = "builtin.log", Name = "B", X = 400, Y = 100 });
        doc.Connections.Add(new DesignerConnection { SourceNodeId = "a", SourcePortName = "output", TargetNodeId = "b", TargetPortName = "input" });

        var cut = this.RenderComponent<CanvasView>(p => p.Add(x => x.Document, doc));

        cut.FindAll(".df-region").Should().BeEmpty();
    }

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
    public void FanIn_MetaHidden_HidesCountDonePorts_OnCanvas()
    {
        var node = new DesignerNode { Id = "f", ModuleId = "builtin.fanin", Name = "F" };
        node.Schema = new Workflow.UI.Client.Api.Dtos.ModuleSchemaDto(
            new System.Collections.Generic.List<Workflow.UI.Client.Api.Dtos.PortDefinitionDto> { new("branches", "Branches", "object", null, false, null) },
            new System.Collections.Generic.List<Workflow.UI.Client.Api.Dtos.PortDefinitionDto>
            {
                new("result", "Result", "object", null, false, null),
                new("count", "Count", "int", null, false, null),
                new("done", "Done", "object", null, false, null),
            },
            new System.Collections.Generic.List<Workflow.UI.Client.Api.Dtos.ModulePropertyDefinitionDto>());

        NodePorts.Outputs(node).Should().Equal("result", "count", "done");

        node.Properties["meta"] = System.Text.Json.JsonSerializer.SerializeToElement("hidden");
        NodePorts.Outputs(node).Should().Equal("result");

        node.Properties["meta"] = System.Text.Json.JsonSerializer.SerializeToElement("embedded");
        NodePorts.Outputs(node).Should().Equal("result");

        node.Properties["meta"] = System.Text.Json.JsonSerializer.SerializeToElement("separate");
        NodePorts.Outputs(node).Should().Equal("result", "count", "done");
    }

    [Fact]
    public void OutputMode_Eligibility_DataModulesOnly()
    {
        static DesignerNode WithTwoOutputs(string moduleId)
        {
            var n = new DesignerNode { Id = "n", ModuleId = moduleId, Name = "N" };
            n.Schema = new Workflow.UI.Client.Api.Dtos.ModuleSchemaDto(
                new System.Collections.Generic.List<Workflow.UI.Client.Api.Dtos.PortDefinitionDto> { new("input", "In", "object", null, false, null) },
                new System.Collections.Generic.List<Workflow.UI.Client.Api.Dtos.PortDefinitionDto>
                {
                    new("body", "Body", "object", null, false, null),
                    new("statusCode", "Status", "int", null, false, null),
                },
                new System.Collections.Generic.List<Workflow.UI.Client.Api.Dtos.ModulePropertyDefinitionDto>());
            return n;
        }

        OutputShapingUx.IsEligible(WithTwoOutputs("builtin.http.request")).Should().BeTrue();
        OutputShapingUx.IsEligible(WithTwoOutputs("builtin.fanin")).Should().BeFalse(because: "fanin has its own meta option~");
        OutputShapingUx.IsEligible(WithTwoOutputs("builtin.trycatch")).Should().BeFalse();
        OutputShapingUx.IsEligible(WithTwoOutputs("builtin.fanout")).Should().BeFalse();
        OutputShapingUx.IsEligible(new DesignerNode { Id = "x", ModuleId = "builtin.log", Name = "X" })
            .Should().BeFalse(because: "no schema / fewer than two outputs~");
    }

    [Fact]
    public void OutputMode_Merged_CollapsesPortsToSingleOutput()
    {
        var node = new DesignerNode { Id = "h", ModuleId = "builtin.http.request", Name = "H" };
        node.Schema = new Workflow.UI.Client.Api.Dtos.ModuleSchemaDto(
            new System.Collections.Generic.List<Workflow.UI.Client.Api.Dtos.PortDefinitionDto>(),
            new System.Collections.Generic.List<Workflow.UI.Client.Api.Dtos.PortDefinitionDto>
            {
                new("body", "Body", "object", null, false, null),
                new("statusCode", "Status", "int", null, false, null),
            },
            new System.Collections.Generic.List<Workflow.UI.Client.Api.Dtos.ModulePropertyDefinitionDto>());

        NodePorts.Outputs(node).Should().Equal("body", "statusCode");

        node.Properties["outputMode"] = System.Text.Json.JsonSerializer.SerializeToElement("merged");
        NodePorts.Outputs(node).Should().Equal("output");
    }

    [Fact]
    public void PropertiesPanel_OutputModeSelector_TogglesMerged()
    {
        var doc = new DesignerDocument { Name = "wf" };
        var node = new DesignerNode { Id = "h", ModuleId = "builtin.http.request", Name = "H" };
        node.Schema = new Workflow.UI.Client.Api.Dtos.ModuleSchemaDto(
            new System.Collections.Generic.List<Workflow.UI.Client.Api.Dtos.PortDefinitionDto>(),
            new System.Collections.Generic.List<Workflow.UI.Client.Api.Dtos.PortDefinitionDto>
            {
                new("body", "Body", "object", null, false, null),
                new("statusCode", "Status", "int", null, false, null),
            },
            new System.Collections.Generic.List<Workflow.UI.Client.Api.Dtos.ModulePropertyDefinitionDto>());
        doc.Nodes.Add(node);
        var selection = new SelectionState();
        selection.SelectNode("h");
        var commands = new CommandStack(doc);

        var cut = this.RenderComponent<PropertiesPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.Selection, selection)
            .Add(x => x.Commands, commands));

        cut.Find("[data-testid=output-mode]").Change("merged");
        OutputShapingUx.IsMerged(node).Should().BeTrue();

        // Undoable, and switching back removes the property.
        cut.Find("[data-testid=output-mode]").Change("ports");
        OutputShapingUx.IsMerged(node).Should().BeFalse();
        node.Properties.Should().NotContainKey("outputMode");
    }

    [Fact]
    public void PropertiesPanel_NoOutputModeSelector_ForControlFlow()
    {
        var doc = new DesignerDocument { Name = "wf" };
        doc.Nodes.Add(new DesignerNode { Id = "tc", ModuleId = "builtin.trycatch", Name = "TC" });
        var selection = new SelectionState();
        selection.SelectNode("tc");

        var cut = this.RenderComponent<PropertiesPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.Selection, selection)
            .Add(x => x.Commands, new CommandStack(doc)));

        cut.FindAll("[data-testid=output-mode]").Should().BeEmpty();
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
