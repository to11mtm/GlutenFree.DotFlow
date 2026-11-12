// <copyright file="RunAfterEdges.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Designer.State;

using System;
using System.Linq;

/// <summary>
/// ⏭️ Plan 09 — designer rules for "run after" edges: a wire dropped on a node's body (not a port)
/// only sequences; it has no target port and delivers no data. Legacy edges into an undeclared
/// <c>input</c> port load as run-after (Q2)~.
/// </summary>
public static class RunAfterEdges
{
    /// <summary>The legacy port that a run-after edge used to be drawn into.</summary>
    public const string LegacyInputPort = "input";

    /// <summary>Creates a run-after edge from a source port to a node.</summary>
    /// <param name="sourceNodeId">The source node id.</param>
    /// <param name="sourcePort">The source output port (routing still applies).</param>
    /// <param name="targetNodeId">The target node id.</param>
    /// <returns>The connection.</returns>
    public static DesignerConnection Create(string sourceNodeId, string sourcePort, string targetNodeId)
        => new()
        {
            SourceNodeId = sourceNodeId,
            SourcePortName = sourcePort,
            TargetNodeId = targetNodeId,
            TargetPortName = string.Empty,
            Kind = DesignerConnection.RunAfterKind,
        };

    /// <summary>The node whose body contains a canvas point (topmost = last drawn), or null.</summary>
    /// <param name="document">The document.</param>
    /// <param name="canvasPoint">The point in canvas coordinates.</param>
    /// <returns>The node, or null.</returns>
    public static DesignerNode? HitTestNodeBody(DesignerDocument document, Point canvasPoint)
    {
        ArgumentNullException.ThrowIfNull(document);
        for (var i = document.Nodes.Count - 1; i >= 0; i--)
        {
            var n = document.Nodes[i];
            var b = CanvasGeometry.NodeBounds(n.X, n.Y, NodePorts.Inputs(n).Count, NodePorts.Outputs(n).Count);
            if (canvasPoint.X >= b.X && canvasPoint.X <= b.Right && canvasPoint.Y >= b.Y && canvasPoint.Y <= b.Bottom)
            {
                return n;
            }
        }

        return null;
    }

    /// <summary>Whether a run-after edge from <paramref name="sourcePort"/> to the target may be added.</summary>
    /// <param name="document">The document.</param>
    /// <param name="sourceNodeId">The source node id.</param>
    /// <param name="sourcePort">The source port.</param>
    /// <param name="targetNodeId">The target node id.</param>
    /// <returns>False for a self edge, a duplicate, or a cycle.</returns>
    public static bool CanAdd(DesignerDocument document, string sourceNodeId, string sourcePort, string targetNodeId)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (sourceNodeId == targetNodeId)
        {
            return false;
        }

        var key = Create(sourceNodeId, sourcePort, targetNodeId).Key;
        return document.Connections.All(c => c.Key != key)
               && !GraphValidator.WouldCreateCycle(document, sourceNodeId, targetNodeId);
    }

    /// <summary>
    /// Whether a port is the designer's default <c>input</c> handle on a module that doesn't declare
    /// it (a module with no inputs) — a wire into it is really "run after".
    /// </summary>
    /// <param name="node">The target node.</param>
    /// <param name="port">The port.</param>
    /// <returns>True when the schema is known and doesn't declare the port.</returns>
    public static bool IsUndeclaredInput(DesignerNode node, string port)
        => node.Schema is { } schema
           && !schema.Inputs.Any(p => string.Equals(p.Name, port, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Plan 09 Q2 — turns legacy data edges into an undeclared <c>input</c> port into run-after edges
    /// (the engine always ran them as "after"; the target never read the value).
    /// </summary>
    /// <param name="document">The loaded document (node schemas resolved).</param>
    /// <returns>How many edges were converted.</returns>
    public static int NormaliseLegacy(DesignerDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var converted = 0;
        foreach (var c in document.Connections)
        {
            if (!c.IsRunAfter
                && string.Equals(c.TargetPortName, LegacyInputPort, StringComparison.OrdinalIgnoreCase)
                && document.FindNode(c.TargetNodeId) is { } target
                && IsUndeclaredInput(target, c.TargetPortName))
            {
                c.Kind = DesignerConnection.RunAfterKind;
                c.TargetPortName = string.Empty;
                converted++;
            }
        }

        return converted;
    }

    /// <summary>Where a run-after edge meets its target: the middle of the node's top edge.</summary>
    /// <param name="node">The target node.</param>
    /// <returns>The anchor point.</returns>
    public static Point Anchor(DesignerNode node)
    {
        var b = CanvasGeometry.NodeBounds(node.X, node.Y, NodePorts.Inputs(node).Count, NodePorts.Outputs(node).Count);
        return new Point(b.X + (b.Width / 2), b.Y);
    }
}
