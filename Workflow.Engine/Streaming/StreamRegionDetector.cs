// <copyright file="StreamRegionDetector.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Engine.Streaming;

using System;
using System.Collections.Generic;
using System.Linq;
using Workflow.Core.Models;

/// <summary>
/// 🌊 Phase 5.1.3 — finds the <b>streaming regions</b> in a workflow: maximal groups of nodes
/// connected by streaming edges, which the engine runs as one back-pressured pipeline.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: This is the engine-side twin of the designer's
/// <c>Workflow.UI.Client/Designer/State/StreamGraph.cs</c>. They can't share code —
/// <c>Workflow.UI.Client</c> deliberately has no <c>Workflow.Core</c> reference so a React port
/// stays additive (Phase 3.3 D2) — so a <b>drift-guard test</b> keeps the two honest, exactly like
/// the Split preview does (phase plan D21)~ 🛡️.
/// </para>
/// <para>
/// Detection is driven purely by <b>connections and port schemas</b>. <c>NodeDefinition.RegionId</c>
/// stays a designer-only hint the engine never reads, so a hand-edited or imported definition can
/// never disagree with what actually runs~ ✨.
/// </para>
/// </remarks>
public static class StreamRegionDetector
{
    /// <summary>
    /// Gets whether a module's port carries a stream of items. 🌊.
    /// </summary>
    /// <param name="schema">The module's schema (null ⇒ false).</param>
    /// <param name="portName">The port name.</param>
    /// <param name="isOutput">True for an output port, false for an input.</param>
    /// <returns>True when the declared port is streaming.</returns>
    public static bool IsStreamingPort(ModuleSchema? schema, string portName, bool isOutput)
    {
        if (schema is null)
        {
            return false;
        }

        var ports = isOutput ? schema.Outputs : schema.Inputs;
        return ports.Any(p =>
            string.Equals(p.Name, portName, StringComparison.OrdinalIgnoreCase) && p.IsStreaming);
    }

    /// <summary>
    /// Gets whether a connection is a valid streaming edge (both ends streaming). 🌊.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="schemaOf">Resolves a node id to its module schema (null when unknown).</param>
    /// <returns>True when both endpoints are streaming ports.</returns>
    public static bool IsStreamingEdge(
        ConnectionDefinition connection,
        Func<string, ModuleSchema?> schemaOf)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(schemaOf);

        return IsStreamingPort(schemaOf(connection.SourceNodeId), connection.SourcePortName, isOutput: true)
               && IsStreamingPort(schemaOf(connection.TargetNodeId), connection.TargetPortName, isOutput: false);
    }

    /// <summary>
    /// Computes the workflow's streaming regions. 🗺️.
    /// </summary>
    /// <param name="definition">The workflow definition.</param>
    /// <param name="schemaOf">Resolves a node id to its module schema.</param>
    /// <returns>The regions, each with a stable index and its node ids in definition order.</returns>
    public static IReadOnlyList<StreamRegion> Regions(
        WorkflowDefinition definition,
        Func<string, ModuleSchema?> schemaOf)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(schemaOf);

        var adjacency = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var edges = new List<ConnectionDefinition>();

        foreach (var connection in definition.Connections)
        {
            if (!IsStreamingEdge(connection, schemaOf))
            {
                continue;
            }

            edges.Add(connection);
            Link(adjacency, connection.SourceNodeId, connection.TargetNodeId);
            Link(adjacency, connection.TargetNodeId, connection.SourceNodeId);
        }

        var regions = new List<StreamRegion>();
        var visited = new HashSet<string>(StringComparer.Ordinal);

        // Walk in definition order so region indices are stable run to run~ 🎯
        foreach (var node in definition.Nodes)
        {
            if (!adjacency.ContainsKey(node.Id) || !visited.Add(node.Id))
            {
                continue;
            }

            var members = new HashSet<string>(StringComparer.Ordinal) { node.Id };
            var queue = new Queue<string>();
            queue.Enqueue(node.Id);

            while (queue.Count > 0)
            {
                foreach (var neighbour in adjacency[queue.Dequeue()])
                {
                    if (visited.Add(neighbour))
                    {
                        members.Add(neighbour);
                        queue.Enqueue(neighbour);
                    }
                }
            }

            var ordered = definition.Nodes
                .Where(n => members.Contains(n.Id))
                .Select(n => n.Id)
                .ToList();

            var regionEdges = edges
                .Where(e => members.Contains(e.SourceNodeId))
                .ToList();

            regions.Add(new StreamRegion(regions.Count, ordered, regionEdges));
        }

        return regions;
    }

    /// <summary>
    /// Finds the region a node belongs to, or null when it isn't in one. 🔎.
    /// </summary>
    /// <param name="definition">The workflow definition.</param>
    /// <param name="schemaOf">Resolves a node id to its module schema.</param>
    /// <param name="nodeId">The node to look for.</param>
    /// <returns>The region containing the node, or null.</returns>
    public static StreamRegion? RegionOf(
        WorkflowDefinition definition,
        Func<string, ModuleSchema?> schemaOf,
        string nodeId)
        => Regions(definition, schemaOf)
            .FirstOrDefault(r => r.NodeIds.Contains(nodeId, StringComparer.Ordinal));

    private static void Link(Dictionary<string, List<string>> adjacency, string from, string to)
    {
        if (!adjacency.TryGetValue(from, out var list))
        {
            list = new List<string>();
            adjacency[from] = list;
        }

        if (!list.Contains(to, StringComparer.Ordinal))
        {
            list.Add(to);
        }
    }
}

/// <summary>
/// 🗺️ A maximal group of nodes linked by streaming edges — one back-pressured pipeline.
/// </summary>
/// <param name="Index">A stable index (definition order).</param>
/// <param name="NodeIds">The member node ids, in definition order.</param>
/// <param name="Edges">The streaming connections inside the region.</param>
public sealed record StreamRegion(
    int Index,
    IReadOnlyList<string> NodeIds,
    IReadOnlyList<ConnectionDefinition> Edges)
{
    /// <summary>
    /// Gets the region's <b>source</b> nodes — members with no incoming streaming edge. 🚰.
    /// </summary>
    public IReadOnlyList<string> SourceNodeIds
        => this.NodeIds
            .Where(id => !this.Edges.Any(e => string.Equals(e.TargetNodeId, id, StringComparison.Ordinal)))
            .ToList();

    /// <summary>
    /// Gets the region's <b>terminal</b> nodes — members with no outgoing streaming edge. 🪣.
    /// </summary>
    public IReadOnlyList<string> TerminalNodeIds
        => this.NodeIds
            .Where(id => !this.Edges.Any(e => string.Equals(e.SourceNodeId, id, StringComparison.Ordinal)))
            .ToList();

    /// <summary>
    /// Gets the member node ids in pipeline order (source → … → terminal). 📐.
    /// </summary>
    /// <returns>The ordered node ids.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the region isn't a simple chain.</exception>
    /// <remarks>
    /// v1 regions are <b>linear chains</b>: one source, one terminal, one edge between neighbours.
    /// Branching inside a region (broadcast/merge) is deliberately out of scope until the operators
    /// that need it land — failing loudly beats materializing a graph we can't reason about~ 🚧.
    /// </remarks>
    public IReadOnlyList<string> ChainOrder()
    {
        var sources = this.SourceNodeIds;
        if (sources.Count != 1)
        {
            throw new InvalidOperationException(
                $"A streaming region must start at exactly one source node, but this one has {sources.Count}. " +
                "Branching inside a region isn't supported yet~ 🚧");
        }

        var order = new List<string> { sources[0] };
        var guard = this.NodeIds.Count + 1;

        while (order.Count < this.NodeIds.Count && guard-- > 0)
        {
            var current = order[^1];
            var next = this.Edges
                .Where(e => string.Equals(e.SourceNodeId, current, StringComparison.Ordinal))
                .ToList();

            if (next.Count != 1)
            {
                throw new InvalidOperationException(
                    $"Node '{current}' has {next.Count} outgoing streaming edges. A region must be a simple " +
                    "chain for now — branching inside a region isn't supported yet~ 🚧");
            }

            order.Add(next[0].TargetNodeId);
        }

        return order;
    }
}
