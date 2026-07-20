// <copyright file="CanvasReadOnlyTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Components;

using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Workflow.UI.Client.Api.Dtos;
using Workflow.UI.Client.Designer.Components;
using Workflow.UI.Client.Designer.State;
using Xunit;

/// <summary>
/// 🧪 Phase 3.3.a.3 — bUnit tests for the read-only canvas (node/edge rendering, pan, zoom, fit)~ ✨.
/// </summary>
public sealed class CanvasReadOnlyTests : TestContext
{
    public CanvasReadOnlyTests()
    {
        this.JSInterop.Mode = JSRuntimeMode.Loose;
        this.Services.AddSingleton(new Workflow.UI.Client.Services.PaletteDragState());
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
    public void Canvas_RendersNode_PerDocumentNode()
    {
        var cut = this.RenderComponent<CanvasView>(p => p.Add(x => x.Document, TwoNodeDoc()));

        cut.FindAll(".df-node").Should().HaveCount(2);
        cut.Markup.Should().Contain("HTTP").And.Contain("Log");
    }

    [Fact]
    public void Canvas_RendersEdge_PerConnection()
    {
        var cut = this.RenderComponent<CanvasView>(p => p.Add(x => x.Document, TwoNodeDoc()));

        cut.FindAll("path.df-edge").Should().HaveCount(1);
    }

    [Fact]
    public void Node_ShowsName_ModuleId_AndPorts()
    {
        var cut = this.RenderComponent<CanvasView>(p => p.Add(x => x.Document, TwoNodeDoc()));

        // HTTP node has 1 input + 2 outputs from schema.
        cut.Markup.Should().Contain("builtin.http.request");
        cut.FindAll("[data-port-out=success]").Should().NotBeEmpty();
        cut.FindAll("[data-port-out=error]").Should().NotBeEmpty();
        cut.FindAll("[data-port-in=input]").Should().NotBeEmpty();
    }

    [Fact]
    public void Node_UnknownModule_ShowsFallback()
    {
        var doc = new DesignerDocument { Name = "wf" };
        doc.Nodes.Add(new DesignerNode { Id = "x", ModuleId = "nope", Name = "X", Schema = null });

        var cut = this.RenderComponent<CanvasView>(p => p.Add(x => x.Document, doc));

        cut.Markup.Should().Contain("unknown module");
        // Falls back to default input/output ports.
        cut.FindAll("[data-port-in=input]").Should().NotBeEmpty();
        cut.FindAll("[data-port-out=output]").Should().NotBeEmpty();
    }

    [Fact]
    public void Pan_Drag_UpdatesTransform()
    {
        CanvasTransform captured = default;
        var cut = this.RenderComponent<CanvasView>(p => p
            .Add(x => x.Document, TwoNodeDoc())
            .Add(x => x.TransformChanged, t => captured = t));

        var viewport = cut.Find(".df-canvas-viewport");
        viewport.PointerDown(new Microsoft.AspNetCore.Components.Web.PointerEventArgs { ClientX = 100, ClientY = 100 });
        viewport.PointerMove(new Microsoft.AspNetCore.Components.Web.PointerEventArgs { ClientX = 160, ClientY = 130 });

        captured.PanX.Should().Be(60);
        captured.PanY.Should().Be(30);
    }

    [Fact]
    public void Wheel_ZoomsAboutCursor()
    {
        CanvasTransform captured = CanvasTransform.Identity;
        var cut = this.RenderComponent<CanvasView>(p => p
            .Add(x => x.Document, TwoNodeDoc())
            .Add(x => x.TransformChanged, t => captured = t));

        var viewport = cut.Find(".df-canvas-viewport");
        viewport.Wheel(new Microsoft.AspNetCore.Components.Web.WheelEventArgs { DeltaY = -100, OffsetX = 300, OffsetY = 200 });

        captured.Zoom.Should().BeGreaterThan(1.0);

        // The canvas point under the cursor stays fixed.
        var before = CanvasGeometry.ScreenToCanvas(new Point(300, 200), CanvasTransform.Identity);
        var after = CanvasGeometry.ScreenToCanvas(new Point(300, 200), captured);
        after.X.Should().BeApproximately(before.X, 1e-6);
        after.Y.Should().BeApproximately(before.Y, 1e-6);
    }

    [Fact]
    public void ZoomControls_PlusMinusReset_Work()
    {
        CanvasTransform captured = CanvasTransform.Identity;
        var cut = this.RenderComponent<CanvasView>(p => p
            .Add(x => x.Document, TwoNodeDoc())
            .Add(x => x.TransformChanged, t => captured = t));

        cut.Find("[data-testid=zoom-in]").Click();
        captured.Zoom.Should().BeGreaterThan(1.0);

        cut.Find("[data-testid=zoom-reset]").Click();
        captured.Zoom.Should().Be(1.0);

        cut.Find("[data-testid=zoom-out]").Click();
        captured.Zoom.Should().BeLessThan(1.0);
    }

    [Fact]
    public void FitButton_AppliesFitTransform()
    {
        CanvasTransform captured = default;
        var cut = this.RenderComponent<CanvasView>(p => p
            .Add(x => x.Document, TwoNodeDoc())
            .Add(x => x.TransformChanged, t => captured = t));

        cut.Find("[data-testid=fit]").Click();

        // Fit produces a valid clamped zoom (fallback viewport in tests).
        captured.Zoom.Should().BeInRange(CanvasGeometry.MinZoom, CanvasGeometry.MaxZoom);
    }
}
