// <copyright file="StreamGraph.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Designer.State;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

/// <summary>
/// 🌊 Phase 5.1.2 — streaming-topology helpers: which ports carry streams, which edges are
/// streaming, and which nodes form a <b>streaming region</b> (a maximal stream-linked sub-graph).
/// Framework-free, like the rest of the designer state core~ ✨.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: The engine will need the same region detection over <c>WorkflowDefinition</c>
/// (phase 5.1.3). It can't literally share this code — <c>Workflow.UI.Client</c> deliberately has
/// no reference to <c>Workflow.Core</c> so the React port stays additive (Phase 3.3 D2) — so the
/// two implementations get a <b>drift-guard test</b> the same way the Split preview does~ 🛡️.
/// </para>
/// <para>
/// Shape rule (design doc 06 §3.1): a streaming output may only feed a streaming input, and a
/// batch output may only feed a batch input. The <c>builtin.stream.collect</c> /
/// <c>builtin.stream.fromitems</c> bridges are how you cross~ 🌉.
/// </para>
/// </remarks>
public static class StreamGraph
{
    /// <summary>The stream → batch bridge module id~ 🪣.</summary>
    public const string CollectModuleId = "builtin.stream.collect";

    /// <summary>The batch → stream bridge module id~ 🚰.</summary>
    public const string FromItemsModuleId = "builtin.stream.fromitems";

    /// <summary>The module id whose variable writes are illegal inside a region~ 💾.</summary>
    public const string SetVariableModuleId = "builtin.setvariable";

    /// <summary>Gets whether a node's port carries a stream of items~ 🌊.</summary>
    /// <param name="node">The node (null ⇒ false).</param>
    /// <param name="portName">The port name.</param>
    /// <param name="isOutput">True for an output port, false for an input.</param>
    /// <returns>True when the declared port is streaming.</returns>
    public static bool IsStreamingPort(DesignerNode? node, string portName, bool isOutput)
    {
        var ports = isOutput ? node?.Schema?.Outputs : node?.Schema?.Inputs;
        return ports?.FirstOrDefault(p => string.Equals(p.Name, portName, StringComparison.OrdinalIgnoreCase))
                   ?.IsStreaming
               ?? false;
    }

    /// <summary>Gets whether a node declares any streaming port at all~ 🌊.</summary>
    /// <param name="node">The node.</param>
    /// <returns>True when the module is stream-capable as configured.</returns>
    public static bool IsStreamCapable(DesignerNode? node)
        => (node?.Schema?.Inputs.Any(p => p.IsStreaming) ?? false)
           || (node?.Schema?.Outputs.Any(p => p.IsStreaming) ?? false);

    /// <summary>
    /// 👷 Phase 5.1.4 — whether a stage's output may arrive out of source order: it asked for
    /// concurrency and opted out of ordering.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns>True when the node is configured unordered with more than one worker.</returns>
    /// <remarks>
    /// CopilotNote: ordering is <b>free</b> (D25 — Akka orders by input slot), so turning it off is
    /// a deliberate throughput trade. The designer badges it rather than hiding it, because
    /// "why is my output shuffled?" is otherwise a very expensive question to answer~ ⚠️.
    /// </remarks>
    public static bool IsUnorderedStage(DesignerNode? node)
        => node is not null
           && MaxWorkersOf(node) > 1
           && !OrderedOf(node);

    /// <summary>Reads a node's <c>maxWorkers</c> property (default 1)~ 👷.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The configured worker count.</returns>
    public static int MaxWorkersOf(DesignerNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (!node.Properties.TryGetValue("maxWorkers", out var value))
        {
            return 1;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var n) => n,
            JsonValueKind.String when int.TryParse(value.GetString(), out var n) => n,
            _ => 1,
        };
    }

    /// <summary>Reads a node's <c>ordered</c> property (default true)~ 🔢.</summary>
    /// <param name="node">The node.</param>
    /// <returns>Whether the stage keeps source order.</returns>
    public static bool OrderedOf(DesignerNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (!node.Properties.TryGetValue("ordered", out var value))
        {
            return true;
        }

        return value.ValueKind switch
        {
            JsonValueKind.False => false,
            JsonValueKind.True => true,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var b) => b,
            _ => true,
        };
    }

    /// <summary>
    /// Gets whether a connection is a <b>valid</b> streaming edge (both ends streaming)~ 🌊.
    /// </summary>
    /// <param name="doc">The document.</param>
    /// <param name="connection">The connection.</param>
    /// <returns>True when both endpoints are streaming ports.</returns>
    public static bool IsStreamingEdge(DesignerDocument doc, DesignerConnection connection)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(connection);

        return IsStreamingPort(doc.FindNode(connection.SourceNodeId), connection.SourcePortName, isOutput: true)
               && IsStreamingPort(doc.FindNode(connection.TargetNodeId), connection.TargetPortName, isOutput: false);
    }

    /// <summary>
    /// Gets whether an edge (or a candidate edge being dragged) breaks the shape rule — one end
    /// streaming, the other batch~ 🚫.
    /// </summary>
    /// <param name="sourceNode">The source node.</param>
    /// <param name="sourcePortName">The source output port.</param>
    /// <param name="targetNode">The target node.</param>
    /// <param name="targetPortName">The target input port.</param>
    /// <returns>True when the two ends disagree about being streaming.</returns>
    /// <remarks>
    /// Unknown modules (no schema) report false — the unknown-module error already covers them,
    /// and refusing wires because a schema hasn't loaded would be maddening~ 🛡️.
    /// </remarks>
    public static bool IsShapeMismatch(
        DesignerNode? sourceNode,
        string sourcePortName,
        DesignerNode? targetNode,
        string targetPortName)
    {
        if (sourceNode?.Schema is null || targetNode?.Schema is null)
        {
            return false;
        }

        return IsStreamingPort(sourceNode, sourcePortName, isOutput: true)
               != IsStreamingPort(targetNode, targetPortName, isOutput: false);
    }

    /// <summary>
    /// Suggests the bridge that fixes a shape mismatch~ 🌉.
    /// </summary>
    /// <param name="sourceIsStreaming">Whether the source port is streaming.</param>
    /// <returns>The module id of the bridge to insert.</returns>
    public static string BridgeFor(bool sourceIsStreaming)
        => sourceIsStreaming ? CollectModuleId : FromItemsModuleId;

    /// <summary>
    /// Computes the document's streaming regions: maximal groups of nodes connected by streaming
    /// edges. Nodes with no streaming edge belong to no region~ 🗺️.
    /// </summary>
    /// <param name="doc">The document.</param>
    /// <returns>The regions, each with a stable index and its node ids in document order.</returns>
    public static IReadOnlyList<StreamRegion> Regions(DesignerDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        // Undirected adjacency over valid streaming edges only~ 🔗
        var adjacency = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var connection in doc.Connections)
        {
            if (!IsStreamingEdge(doc, connection))
            {
                continue;
            }

            Link(adjacency, connection.SourceNodeId, connection.TargetNodeId);
            Link(adjacency, connection.TargetNodeId, connection.SourceNodeId);
        }

        var regions = new List<StreamRegion>();
        var visited = new HashSet<string>(StringComparer.Ordinal);

        // Walk nodes in document order so region indices are stable across renders~ 🎯
        foreach (var node in doc.Nodes)
        {
            if (!adjacency.ContainsKey(node.Id) || !visited.Add(node.Id))
            {
                continue;
            }

            var members = new List<string> { node.Id };
            var queue = new Queue<string>();
            queue.Enqueue(node.Id);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var neighbour in adjacency[current])
                {
                    if (visited.Add(neighbour))
                    {
                        members.Add(neighbour);
                        queue.Enqueue(neighbour);
                    }
                }
            }

            // Keep document order inside the region so halo bounds are deterministic~ 📐
            var ordered = doc.Nodes.Where(n => members.Contains(n.Id)).Select(n => n.Id).ToList();
            regions.Add(new StreamRegion(regions.Count, ordered));
        }

        return regions;
    }

    /// <summary>
    /// Maps each node id to its region index, for O(1) lookups while rendering~ 🗺️.
    /// </summary>
    /// <param name="doc">The document.</param>
    /// <returns>A node-id → region-index map (nodes outside a region are absent).</returns>
    public static IReadOnlyDictionary<string, int> RegionIndexByNode(DesignerDocument doc)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var region in Regions(doc))
        {
            foreach (var nodeId in region.NodeIds)
            {
                map[nodeId] = region.Index;
            }
        }

        return map;
    }

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
/// 🗺️ A maximal group of nodes linked by streaming edges — what the engine runs as one
/// back-pressured pipeline, and what the designer draws a halo around.
/// </summary>
/// <param name="Index">A stable index used for the halo's colour/label.</param>
/// <param name="NodeIds">The member node ids, in document order.</param>
public sealed record StreamRegion(int Index, IReadOnlyList<string> NodeIds);
