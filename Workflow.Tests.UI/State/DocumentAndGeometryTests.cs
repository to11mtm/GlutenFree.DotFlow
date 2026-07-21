// <copyright file="DocumentAndGeometryTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.State;

using System;
using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using Workflow.UI.Client.Api.Dtos;
using Workflow.UI.Client.Designer.State;
using Xunit;

/// <summary>
/// 🧪 Phase 3.3.a.2 — Pure specs for the framework-free state core. These double as the React
/// port spec (D2)~ ✨.
/// </summary>
public sealed class DocumentAndGeometryTests
{
    private static JsonElement El(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static WorkflowDto SampleDto()
        => new(
            Guid.NewGuid(),
            "wf",
            "desc",
            "1.2.0",
            new List<NodeDto>
            {
                new("http-1", "builtin.http.request", "HTTP", new Dictionary<string, JsonElement> { ["url"] = El("\"x\"") }, new PositionDto(10, 20), Metadata: new Dictionary<string, string> { ["moduleVersion"] = "1.0.0" }),
                new("log-1", "builtin.log", "Log", new Dictionary<string, JsonElement>(), new PositionDto(300, 20)),
            },
            new List<ConnectionDto> { new("http-1", "success", "log-1", "input") },
            new Dictionary<string, JsonElement>(),
            null,
            null,
            null,
            null,
            new List<string> { "prod" });

    [Fact]
    public void Document_FromDto_ToDto_RoundTripsLosslessly()
    {
        var dto = SampleDto();
        var doc = DesignerDocument.FromDto(dto, _ => null);

        var back = doc.ToDto();

        JsonSerializer.Serialize(back).Should().Be(JsonSerializer.Serialize(dto));
    }

    [Fact]
    public void Document_UnknownModule_KeptNotDropped()
    {
        var dto = SampleDto();
        var doc = DesignerDocument.FromDto(dto, _ => null);

        doc.Nodes.Should().HaveCount(2);
        doc.FindNode("http-1")!.IsModuleKnown.Should().BeFalse();
    }

    [Fact]
    public void Geometry_ScreenToCanvas_InvertsCanvasToScreen()
    {
        var t = new CanvasTransform(35, -12, 1.7);
        var canvas = new Point(120, 240);

        var screen = CanvasGeometry.CanvasToScreen(canvas, t);
        var roundTrip = CanvasGeometry.ScreenToCanvas(screen, t);

        roundTrip.X.Should().BeApproximately(canvas.X, 1e-9);
        roundTrip.Y.Should().BeApproximately(canvas.Y, 1e-9);
    }

    [Fact]
    public void Geometry_ZoomAboutCursor_KeepsCursorPointFixed()
    {
        var t = new CanvasTransform(50, 30, 1.0);
        var cursor = new Point(400, 300);

        var canvasBefore = CanvasGeometry.ScreenToCanvas(cursor, t);
        var t2 = CanvasGeometry.ZoomAboutCursor(cursor, t, 2.2);
        var canvasAfter = CanvasGeometry.ScreenToCanvas(cursor, t2);

        canvasAfter.X.Should().BeApproximately(canvasBefore.X, 1e-9);
        canvasAfter.Y.Should().BeApproximately(canvasBefore.Y, 1e-9);
    }

    [Fact]
    public void Geometry_Zoom_ClampedToLimits()
    {
        CanvasGeometry.ClampZoom(99).Should().Be(CanvasGeometry.MaxZoom);
        CanvasGeometry.ClampZoom(0.0001).Should().Be(CanvasGeometry.MinZoom);
        CanvasGeometry.ZoomAboutCursor(new Point(0, 0), CanvasTransform.Identity, 999).Zoom.Should().Be(CanvasGeometry.MaxZoom);
    }

    [Fact]
    public void Geometry_FitToContent_ContainsAllNodes_WithPadding()
    {
        var bounds = new[]
        {
            new Rect(0, 0, 200, 100),
            new Rect(500, 300, 200, 100),
        };

        var t = CanvasGeometry.FitToContent(bounds, 800, 600, 40);

        // Every corner must map inside the viewport.
        foreach (var b in bounds)
        {
            var tl = CanvasGeometry.CanvasToScreen(new Point(b.X, b.Y), t);
            var br = CanvasGeometry.CanvasToScreen(new Point(b.Right, b.Bottom), t);
            tl.X.Should().BeGreaterThanOrEqualTo(-0.5);
            tl.Y.Should().BeGreaterThanOrEqualTo(-0.5);
            br.X.Should().BeLessThanOrEqualTo(800.5);
            br.Y.Should().BeLessThanOrEqualTo(600.5);
        }
    }

    [Fact]
    public void Geometry_FitToContent_EmptyDocument_DefaultView()
    {
        var t = CanvasGeometry.FitToContent(Array.Empty<Rect>(), 800, 600);
        t.Zoom.Should().Be(1);
    }

    [Fact]
    public void Geometry_PortAnchors_InputsLeft_OutputsRight_EvenlySpaced()
    {
        var bounds = CanvasGeometry.NodeBounds(100, 100, inputCount: 2, outputCount: 1);

        var in0 = CanvasGeometry.PortAnchor(bounds, 0, isInput: true);
        var in1 = CanvasGeometry.PortAnchor(bounds, 1, isInput: true);
        var out0 = CanvasGeometry.PortAnchor(bounds, 0, isInput: false);

        in0.X.Should().Be(bounds.X);
        out0.X.Should().Be(bounds.Right);
        (in1.Y - in0.Y).Should().BeApproximately(CanvasGeometry.PortRowHeight, 1e-9);
    }

    [Fact]
    public void Geometry_BezierPath_EndpointsMatchAnchors()
    {
        var s = new Point(10, 20);
        var t = new Point(200, 120);

        var path = CanvasGeometry.BezierPath(s, t);

        path.Should().StartWith("M 10 20 C");
        path.Should().EndWith("200 120");
    }

    [Fact]
    public void NodeId_Generation_UniqueAndStable()
    {
        var existing = new HashSet<string>();
        var a = NodeIdGenerator.Generate("builtin.http.request", existing);
        existing.Add(a);
        var b = NodeIdGenerator.Generate("builtin.http.request", existing);

        a.Should().Be("request-1");
        b.Should().Be("request-2");
    }
}
