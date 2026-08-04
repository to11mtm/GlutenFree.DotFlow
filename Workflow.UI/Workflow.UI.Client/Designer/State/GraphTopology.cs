// <copyright file="GraphTopology.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Designer.State;

using System.Collections.Generic;

/// <summary>
/// 🧭 Start/End clarity (C1) — derives which nodes are the workflow's <b>starts</b> and <b>ends</b>
/// the same way the engine does: a start is any node with in-degree 0
/// (<c>WorkflowExecutor.GetStartNodes</c>) and an end is any node with out-degree 0
/// (<c>GatherWorkflowOutputs</c>). Presentation-only, framework-free (D2) — nothing is persisted.
/// Dangling connections still count as edges: they are already a validation error, and pretending
/// they don't exist would flip a node's role while the user is mid-fix~ ✨.
/// </summary>
public static class GraphTopology
{
    /// <summary>A node's derived start/end role, with position among its peers for "n of N" badges~ 🏷️.</summary>
    /// <param name="IsStart">Whether the node has no incoming connections.</param>
    /// <param name="IsEnd">Whether the node has no outgoing connections.</param>
    /// <param name="StartIndex">Zero-based position among the starts (document order); -1 when not a start.</param>
    /// <param name="StartCount">Total number of start nodes in the document.</param>
    /// <param name="EndIndex">Zero-based position among the ends (document order); -1 when not an end.</param>
    /// <param name="EndCount">Total number of end nodes in the document.</param>
    public sealed record NodeRole(
        bool IsStart,
        bool IsEnd,
        int StartIndex,
        int StartCount,
        int EndIndex,
        int EndCount)
    {
        /// <summary>Gets a value indicating whether the node is both a start and an end — i.e. wired to nothing~ 🔌.</summary>
        public bool IsIsolated => this.IsStart && this.IsEnd;
    }

    /// <summary>The derived topology of a document~ 🗺️.</summary>
    /// <param name="StartNodeIds">Nodes with in-degree 0, in document order.</param>
    /// <param name="EndNodeIds">Nodes with out-degree 0, in document order.</param>
    /// <param name="IsolatedNodeIds">Nodes in both sets (no connections at all), in document order.</param>
    /// <param name="Roles">Per-node role lookup (every document node has an entry).</param>
    public sealed record Result(
        IReadOnlyList<string> StartNodeIds,
        IReadOnlyList<string> EndNodeIds,
        IReadOnlyList<string> IsolatedNodeIds,
        IReadOnlyDictionary<string, NodeRole> Roles);

    /// <summary>Computes start/end roles for every node in the document~ 🧮.</summary>
    /// <param name="document">The document.</param>
    /// <returns>The derived topology.</returns>
    public static Result Compute(DesignerDocument document)
    {
        var hasIncoming = new HashSet<string>();
        var hasOutgoing = new HashSet<string>();
        foreach (var c in document.Connections)
        {
            hasOutgoing.Add(c.SourceNodeId);
            hasIncoming.Add(c.TargetNodeId);
        }

        var starts = new List<string>();
        var ends = new List<string>();
        var isolated = new List<string>();
        foreach (var n in document.Nodes)
        {
            var isStart = !hasIncoming.Contains(n.Id);
            var isEnd = !hasOutgoing.Contains(n.Id);
            if (isStart)
            {
                starts.Add(n.Id);
            }

            if (isEnd)
            {
                ends.Add(n.Id);
            }

            if (isStart && isEnd)
            {
                isolated.Add(n.Id);
            }
        }

        var roles = new Dictionary<string, NodeRole>(document.Nodes.Count);
        foreach (var n in document.Nodes)
        {
            var startIndex = starts.IndexOf(n.Id);
            var endIndex = ends.IndexOf(n.Id);
            roles[n.Id] = new NodeRole(
                IsStart: startIndex >= 0,
                IsEnd: endIndex >= 0,
                StartIndex: startIndex,
                StartCount: starts.Count,
                EndIndex: endIndex,
                EndCount: ends.Count);
        }

        return new Result(starts, ends, isolated, roles);
    }
}
