// <copyright file="StructuralRegions.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Designer.State;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 🔁🛡️ UX-F4.3/F5.2 — Computes presentation-only body regions for structural constructs:
/// for every edge leaving a structural port (<c>loopBody</c>/<c>try</c>/<c>catch</c>/<c>finally</c>)
/// the region is the union bounding box of the downstream closure of its target nodes. Nothing
/// is persisted — pure canvas presentation. Framework-free (D2)~ ✨.
/// </summary>
public static class StructuralRegions
{
    /// <summary>A computed body region~ 📦.</summary>
    /// <param name="OwnerNodeId">The structural node (loop / trycatch) the region belongs to.</param>
    /// <param name="Port">The structural source port (<c>loopBody</c>/<c>try</c>/…).</param>
    /// <param name="Label">The display label ("🔁 loop body", "🛡️ try", …).</param>
    /// <param name="Kind">The CSS kind: <c>loop</c>/<c>try</c>/<c>catch</c>/<c>finally</c>.</param>
    /// <param name="Bounds">The padded union bounding box in canvas space.</param>
    /// <param name="NodeIds">The body node ids (owner excluded).</param>
    public sealed record Region(
        string OwnerNodeId,
        string Port,
        string Label,
        string Kind,
        Rect Bounds,
        IReadOnlyList<string> NodeIds);

    /// <summary>Padding around the union of body node bounds~ 📏.</summary>
    public const double Padding = 16;

    /// <summary>Computes all body regions for the document~ 🔁.</summary>
    /// <param name="document">The document.</param>
    /// <returns>The regions (one per structural port that has at least one wired edge).</returns>
    public static IReadOnlyList<Region> Compute(DesignerDocument document)
    {
        var regions = new List<Region>();

        // Group structural edges by (owner, port) — one region per wired structural port.
        var structuralEdges = document.Connections
            .Where(c => NodePorts.IsStructuralPort(c.SourcePortName))
            .GroupBy(c => (c.SourceNodeId, c.SourcePortName));

        foreach (var group in structuralEdges)
        {
            var (ownerId, port) = group.Key;
            var bodyIds = DownstreamClosure(document, group.Select(c => c.TargetNodeId), ownerId);
            if (bodyIds.Count == 0)
            {
                continue;
            }

            var bounds = UnionBounds(document, bodyIds);
            if (bounds is null)
            {
                continue;
            }

            regions.Add(new Region(ownerId, port, LabelFor(port), KindFor(port), bounds.Value, bodyIds));
        }

        return regions;
    }

    private static IReadOnlyList<string> DownstreamClosure(DesignerDocument document, IEnumerable<string> seeds, string ownerId)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        foreach (var s in seeds)
        {
            if (s != ownerId && visited.Add(s))
            {
                queue.Enqueue(s);
            }
        }

        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            foreach (var c in document.Connections)
            {
                if (c.SourceNodeId == id && c.TargetNodeId != ownerId && visited.Add(c.TargetNodeId))
                {
                    queue.Enqueue(c.TargetNodeId);
                }
            }
        }

        return visited.ToList();
    }

    private static Rect? UnionBounds(DesignerDocument document, IReadOnlyList<string> nodeIds)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        var any = false;
        foreach (var id in nodeIds)
        {
            var node = document.FindNode(id);
            if (node is null)
            {
                continue;
            }

            var b = CanvasGeometry.NodeBounds(node.X, node.Y, NodePorts.Inputs(node).Count, NodePorts.Outputs(node).Count);
            minX = Math.Min(minX, b.X);
            minY = Math.Min(minY, b.Y);
            maxX = Math.Max(maxX, b.Right);
            maxY = Math.Max(maxY, b.Bottom);
            any = true;
        }

        return any
            ? new Rect(minX - Padding, minY - Padding, (maxX - minX) + (2 * Padding), (maxY - minY) + (2 * Padding))
            : null;
    }

    private static string LabelFor(string port) => port switch
    {
        "loopBody" => "🔁 loop body",
        "try" => "🛡️ try",
        "catch" => "catch",
        "finally" => "finally",
        _ => port,
    };

    private static string KindFor(string port) => port switch
    {
        "loopBody" => "loop",
        "try" => "try",
        "catch" => "catch",
        "finally" => "finally",
        _ => "loop",
    };
}
