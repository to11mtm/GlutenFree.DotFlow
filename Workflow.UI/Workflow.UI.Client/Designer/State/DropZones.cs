// <copyright file="DropZones.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Designer.State;

using System;

/// <summary>
/// 🎯 Output-side drop zones for the Fan In drag gesture: the rect right of each node where
/// dropping a <c>builtin.fanin</c> auto-wires all of that node's outputs. Shared by the canvas
/// (zone rendering + armed highlight) and the designer page (drop handling) so they never
/// diverge. Framework-free~ ✨.
/// </summary>
public static class DropZones
{
    /// <summary>The module id whose palette drag activates the zones~ 🪄.</summary>
    public const string FanInModuleId = "builtin.fanin";

    /// <summary>How far right of a node's edge still counts as its output side~ 📏.</summary>
    public const double Reach = 120;

    /// <summary>Computes the visual zone rect for a node (right of its output edge)~ 📦.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The zone rect in canvas space.</returns>
    public static Rect ZoneFor(DesignerNode node)
    {
        var b = CanvasGeometry.NodeBounds(node.X, node.Y, NodePorts.Inputs(node).Count, NodePorts.Outputs(node).Count);
        return new Rect(b.Right + 6, b.Y, Reach - 6, b.Height);
    }

    /// <summary>
    /// Finds the node whose body or output-side zone contains the given canvas point, preferring
    /// the closest output edge when zones overlap, or null~ 🎯.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="canvasPoint">The point in canvas space.</param>
    /// <returns>The hit node, or null.</returns>
    public static DesignerNode? HitTestOutputSide(DesignerDocument document, Point canvasPoint)
    {
        DesignerNode? best = null;
        var bestDist = double.MaxValue;
        foreach (var n in document.Nodes)
        {
            var b = CanvasGeometry.NodeBounds(n.X, n.Y, NodePorts.Inputs(n).Count, NodePorts.Outputs(n).Count);
            var inYRange = canvasPoint.Y >= b.Y && canvasPoint.Y <= b.Bottom;
            var inXRange = canvasPoint.X >= b.X && canvasPoint.X <= b.Right + Reach;
            if (inYRange && inXRange)
            {
                var dist = Math.Abs(canvasPoint.X - b.Right);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = n;
                }
            }
        }

        return best;
    }
}
