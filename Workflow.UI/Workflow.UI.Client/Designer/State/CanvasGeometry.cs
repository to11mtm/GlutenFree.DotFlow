// <copyright file="CanvasGeometry.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Designer.State;

using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>📐 Phase 3.3.a.2 — A 2D point (framework-free)~ ✨.</summary>
/// <param name="X">Horizontal.</param>
/// <param name="Y">Vertical.</param>
public readonly record struct Point(double X, double Y);

/// <summary>📐 Phase 3.3.a.2 — An axis-aligned rectangle~ ✨.</summary>
/// <param name="X">Left.</param>
/// <param name="Y">Top.</param>
/// <param name="Width">Width.</param>
/// <param name="Height">Height.</param>
public readonly record struct Rect(double X, double Y, double Width, double Height)
{
    /// <summary>Gets the right edge.</summary>
    public double Right => this.X + this.Width;

    /// <summary>Gets the bottom edge.</summary>
    public double Bottom => this.Y + this.Height;
}

/// <summary>
/// 📐 Phase 3.3.a.2 — The canvas pan/zoom transform: canvas→screen is
/// <c>screen = canvas * Zoom + (PanX, PanY)</c>~ ✨.
/// </summary>
/// <param name="PanX">Horizontal pan (screen px).</param>
/// <param name="PanY">Vertical pan (screen px).</param>
/// <param name="Zoom">Zoom factor.</param>
public readonly record struct CanvasTransform(double PanX, double PanY, double Zoom)
{
    /// <summary>The identity transform (no pan, 100% zoom)~ 🎯.</summary>
    public static CanvasTransform Identity => new(0, 0, 1);
}

/// <summary>
/// 📐 Phase 3.3.a.2 — Pure canvas geometry: coordinate transforms, zoom-about-cursor, fit-to-content,
/// node bounds, port anchors, and bezier edge paths. No Blazor, no DOM — directly portable to TS (D2)~ ✨.
/// </summary>
public static class CanvasGeometry
{
    /// <summary>Minimum zoom factor (10%).</summary>
    public const double MinZoom = 0.1;

    /// <summary>Maximum zoom factor (300%).</summary>
    public const double MaxZoom = 3.0;

    /// <summary>Fixed node width (canvas units).</summary>
    public const double NodeWidth = 200;

    /// <summary>Node header height (canvas units).</summary>
    public const double HeaderHeight = 48;

    /// <summary>Per-port row height (canvas units).</summary>
    public const double PortRowHeight = 24;

    /// <summary>Bottom padding below the last port row.</summary>
    public const double NodePadding = 12;

    /// <summary>Clamps a zoom value to <see cref="MinZoom"/>..<see cref="MaxZoom"/>~ 🔒.</summary>
    /// <param name="zoom">The requested zoom.</param>
    /// <returns>The clamped zoom.</returns>
    public static double ClampZoom(double zoom) => Math.Clamp(zoom, MinZoom, MaxZoom);

    /// <summary>Converts a screen point to canvas coordinates~ 🔄.</summary>
    /// <param name="screen">The screen point.</param>
    /// <param name="t">The current transform.</param>
    /// <returns>The canvas point.</returns>
    public static Point ScreenToCanvas(Point screen, CanvasTransform t)
        => new((screen.X - t.PanX) / t.Zoom, (screen.Y - t.PanY) / t.Zoom);

    /// <summary>Converts a canvas point to screen coordinates~ 🔄.</summary>
    /// <param name="canvas">The canvas point.</param>
    /// <param name="t">The current transform.</param>
    /// <returns>The screen point.</returns>
    public static Point CanvasToScreen(Point canvas, CanvasTransform t)
        => new((canvas.X * t.Zoom) + t.PanX, (canvas.Y * t.Zoom) + t.PanY);

    /// <summary>
    /// Computes the new transform when zooming about a fixed screen point (the cursor), so the
    /// canvas point under the cursor stays put~ 🔍.
    /// </summary>
    /// <param name="cursorScreen">The cursor position in screen coordinates.</param>
    /// <param name="current">The current transform.</param>
    /// <param name="newZoomRaw">The requested (pre-clamp) zoom.</param>
    /// <returns>The new transform.</returns>
    public static CanvasTransform ZoomAboutCursor(Point cursorScreen, CanvasTransform current, double newZoomRaw)
    {
        var newZoom = ClampZoom(newZoomRaw);
        var canvasUnderCursor = ScreenToCanvas(cursorScreen, current);
        var panX = cursorScreen.X - (canvasUnderCursor.X * newZoom);
        var panY = cursorScreen.Y - (canvasUnderCursor.Y * newZoom);
        return new CanvasTransform(panX, panY, newZoom);
    }

    /// <summary>Computes a node's canvas-space bounding rect from its port counts~ 📦.</summary>
    /// <param name="x">Node left.</param>
    /// <param name="y">Node top.</param>
    /// <param name="inputCount">Number of input ports.</param>
    /// <param name="outputCount">Number of output ports.</param>
    /// <returns>The node rect.</returns>
    public static Rect NodeBounds(double x, double y, int inputCount, int outputCount)
    {
        var rows = Math.Max(1, Math.Max(inputCount, outputCount));
        var height = HeaderHeight + (rows * PortRowHeight) + NodePadding;
        return new Rect(x, y, NodeWidth, height);
    }

    /// <summary>Computes a port anchor point (canvas space) on a node edge~ 📍.</summary>
    /// <param name="nodeBounds">The node's bounds.</param>
    /// <param name="index">The 0-based port index.</param>
    /// <param name="isInput">True for input (left edge), false for output (right edge).</param>
    /// <returns>The anchor point.</returns>
    public static Point PortAnchor(Rect nodeBounds, int index, bool isInput)
    {
        var x = isInput ? nodeBounds.X : nodeBounds.Right;
        var y = nodeBounds.Y + HeaderHeight + ((index + 0.5) * PortRowHeight);
        return new Point(x, y);
    }

    /// <summary>Builds an SVG cubic-bezier path between two anchor points (left-to-right flow)~ 🎨.</summary>
    /// <param name="source">The source (output) anchor.</param>
    /// <param name="target">The target (input) anchor.</param>
    /// <returns>The SVG path <c>d</c> string.</returns>
    public static string BezierPath(Point source, Point target)
    {
        var dx = Math.Max(40, Math.Abs(target.X - source.X) * 0.5);
        var c1 = new Point(source.X + dx, source.Y);
        var c2 = new Point(target.X - dx, target.Y);
        return string.Format(
            CultureInfo.InvariantCulture,
            "M {0} {1} C {2} {3} {4} {5} {6} {7}",
            source.X, source.Y, c1.X, c1.Y, c2.X, c2.Y, target.X, target.Y);
    }

    /// <summary>
    /// Computes a transform that fits all node bounds within the viewport with padding. Returns the
    /// identity-ish default view for an empty set~ ⤢.
    /// </summary>
    /// <param name="nodeBounds">The node rects.</param>
    /// <param name="viewportWidth">Viewport width (screen px).</param>
    /// <param name="viewportHeight">Viewport height (screen px).</param>
    /// <param name="padding">Padding (screen px) around the content.</param>
    /// <returns>The fitting transform.</returns>
    public static CanvasTransform FitToContent(IEnumerable<Rect> nodeBounds, double viewportWidth, double viewportHeight, double padding = 40)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        var any = false;
        foreach (var b in nodeBounds)
        {
            any = true;
            minX = Math.Min(minX, b.X);
            minY = Math.Min(minY, b.Y);
            maxX = Math.Max(maxX, b.Right);
            maxY = Math.Max(maxY, b.Bottom);
        }

        if (!any)
        {
            return new CanvasTransform(padding, padding, 1);
        }

        var contentW = Math.Max(1, maxX - minX);
        var contentH = Math.Max(1, maxY - minY);
        var availableW = Math.Max(1, viewportWidth - (2 * padding));
        var availableH = Math.Max(1, viewportHeight - (2 * padding));

        var zoom = ClampZoom(Math.Min(availableW / contentW, availableH / contentH));

        // Center the content within the viewport.
        var panX = ((viewportWidth - (contentW * zoom)) / 2) - (minX * zoom);
        var panY = ((viewportHeight - (contentH * zoom)) / 2) - (minY * zoom);
        return new CanvasTransform(panX, panY, zoom);
    }
}
