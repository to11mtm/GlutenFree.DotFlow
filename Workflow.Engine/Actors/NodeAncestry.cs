// <copyright file="NodeAncestry.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Engine.Actors;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Workflow.Core.Models;
using Workflow.Engine.Messages;

/// <summary>
/// 🧬 §10.1 — which nodes a node can reference with <c>{{nodeId.port}}</c>: its <b>ancestors</b>,
/// i.e. every node with a connection path into it in the definition. Structural edges count, so a
/// node inside a loop / branch / try / transaction body can reach the construct and everything
/// upstream of it (decided 2026-11-08, "A2 across bodies").
/// </summary>
internal static class NodeAncestry
{
    private static readonly ConditionalWeakTable<WorkflowDefinition, Dictionary<string, IReadOnlySet<string>>> Cache = new();

    /// <summary>Gets the ancestors of a node (cached per definition).</summary>
    /// <param name="definition">The workflow definition.</param>
    /// <param name="nodeId">The node.</param>
    /// <returns>The ids of every node with a connection path into <paramref name="nodeId"/>.</returns>
    public static IReadOnlySet<string> Of(WorkflowDefinition definition, string nodeId)
    {
        var perNode = Cache.GetValue(definition, _ => new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal));
        lock (perNode)
        {
            if (perNode.TryGetValue(nodeId, out var cached))
            {
                return cached;
            }

            var incoming = definition.Connections
                .GroupBy(c => c.TargetNodeId, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Select(c => c.SourceNodeId).ToList(), StringComparer.Ordinal);

            var ancestors = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<string>();
            queue.Enqueue(nodeId);
            while (queue.Count > 0)
            {
                if (!incoming.TryGetValue(queue.Dequeue(), out var sources))
                {
                    continue;
                }

                foreach (var source in sources)
                {
                    if (source != nodeId && ancestors.Add(source))
                    {
                        queue.Enqueue(source);
                    }
                }
            }

            perNode[nodeId] = ancestors;
            return ancestors;
        }
    }

    /// <summary>
    /// Builds what a node may bind against: the outputs of those of its ancestors that have run.
    /// </summary>
    /// <param name="definition">The workflow definition.</param>
    /// <param name="nodeId">The node about to run.</param>
    /// <param name="sources">Completed nodes' outputs, innermost scope last (later sources win).</param>
    /// <returns>The node's upstream view.</returns>
    public static UpstreamView ViewFor(
        WorkflowDefinition definition,
        string nodeId,
        params IReadOnlyDictionary<string, Dictionary<string, object?>>?[] sources)
    {
        var ancestors = Of(definition, nodeId);
        var outputs = new Dictionary<string, IReadOnlyDictionary<string, object?>>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            if (source is null)
            {
                continue;
            }

            foreach (var (id, values) in source)
            {
                if (ancestors.Contains(id))
                {
                    outputs[id] = values;
                }
            }
        }

        return new UpstreamView(outputs, ancestors.ToArray());
    }

    /// <summary>A copy of completed nodes' outputs, for handing to a construct's bodies.</summary>
    /// <param name="sources">Completed nodes' outputs, outermost first (later sources win).</param>
    /// <returns>The snapshot.</returns>
    public static Dictionary<string, Dictionary<string, object?>> Snapshot(
        params IReadOnlyDictionary<string, Dictionary<string, object?>>?[] sources)
    {
        var snapshot = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            if (source is null)
            {
                continue;
            }

            foreach (var (id, values) in source)
            {
                snapshot[id] = values;
            }
        }

        return snapshot;
    }
}
