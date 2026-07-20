// <copyright file="GraphValidator.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Designer.State;

using System.Collections.Generic;
using System.Linq;

/// <summary>🔎 Phase 3.3.a.2 — Severity of a validation issue~ ✨.</summary>
public enum IssueSeverity
{
    /// <summary>Blocks save.</summary>
    Error,

    /// <summary>Advisory; save allowed.</summary>
    Warning,
}

/// <summary>🔎 Phase 3.3.a.2 — A single structural validation issue~ ✨.</summary>
/// <param name="Severity">The severity.</param>
/// <param name="Message">Human-readable message.</param>
/// <param name="NodeId">The offending node id, when applicable.</param>
public sealed record GraphIssue(IssueSeverity Severity, string Message, string? NodeId = null);

/// <summary>
/// 🔎 Phase 3.3.a.2 — Client-side structural validation of a <see cref="DesignerDocument"/>:
/// unknown modules, dangling connections, duplicate connections, self-connections, and cycles.
/// Framework-free (D2). This is the *fast* client gate; the authoritative check is the server
/// validate endpoint (D14)~ ✨.
/// </summary>
public static class GraphValidator
{
    /// <summary>Validates the document against the set of known module ids~ 🔎.</summary>
    /// <param name="doc">The document.</param>
    /// <param name="knownModuleIds">The module ids the server knows about.</param>
    /// <returns>The issues found (empty when structurally valid).</returns>
    public static IReadOnlyList<GraphIssue> Validate(DesignerDocument doc, ISet<string> knownModuleIds)
    {
        var issues = new List<GraphIssue>();
        var nodeIds = new HashSet<string>(doc.Nodes.Select(n => n.Id));

        foreach (var node in doc.Nodes)
        {
            if (!knownModuleIds.Contains(node.ModuleId))
            {
                issues.Add(new GraphIssue(IssueSeverity.Error, $"Unknown module '{node.ModuleId}'.", node.Id));
            }
        }

        var seenConnections = new HashSet<string>();
        foreach (var c in doc.Connections)
        {
            if (c.SourceNodeId == c.TargetNodeId)
            {
                issues.Add(new GraphIssue(IssueSeverity.Error, "A node cannot connect to itself.", c.SourceNodeId));
            }

            if (!nodeIds.Contains(c.SourceNodeId))
            {
                issues.Add(new GraphIssue(IssueSeverity.Error, $"Connection references missing source node '{c.SourceNodeId}'.", c.SourceNodeId));
            }

            if (!nodeIds.Contains(c.TargetNodeId))
            {
                issues.Add(new GraphIssue(IssueSeverity.Error, $"Connection references missing target node '{c.TargetNodeId}'.", c.TargetNodeId));
            }

            if (!seenConnections.Add(c.Key))
            {
                issues.Add(new GraphIssue(IssueSeverity.Warning, $"Duplicate connection {c.Key}.", c.SourceNodeId));
            }
        }

        if (HasCycle(doc))
        {
            issues.Add(new GraphIssue(IssueSeverity.Error, "The workflow contains a cycle."));
        }

        return issues;
    }

    /// <summary>
    /// Returns true if adding the candidate edge (source→target) would create a cycle in the
    /// document's current node-level graph. Used by the connection-drawing live check (3.3.b.2)~ 🔄.
    /// </summary>
    /// <param name="doc">The document.</param>
    /// <param name="sourceNodeId">The candidate edge source node.</param>
    /// <param name="targetNodeId">The candidate edge target node.</param>
    /// <returns>True if a cycle would form.</returns>
    public static bool WouldCreateCycle(DesignerDocument doc, string sourceNodeId, string targetNodeId)
    {
        if (sourceNodeId == targetNodeId)
        {
            return true;
        }

        // A cycle forms iff target can already reach source (then source→target closes the loop).
        var adjacency = BuildAdjacency(doc);
        return CanReach(adjacency, targetNodeId, sourceNodeId);
    }

    private static bool HasCycle(DesignerDocument doc)
    {
        var adjacency = BuildAdjacency(doc);
        var state = new Dictionary<string, int>(); // 0=unvisited,1=in-stack,2=done

        bool Dfs(string node)
        {
            state[node] = 1;
            if (adjacency.TryGetValue(node, out var neighbours))
            {
                foreach (var next in neighbours)
                {
                    var s = state.TryGetValue(next, out var v) ? v : 0;
                    if (s == 1)
                    {
                        return true;
                    }

                    if (s == 0 && Dfs(next))
                    {
                        return true;
                    }
                }
            }

            state[node] = 2;
            return false;
        }

        foreach (var node in doc.Nodes)
        {
            if (!state.ContainsKey(node.Id) && Dfs(node.Id))
            {
                return true;
            }
        }

        return false;
    }

    private static Dictionary<string, List<string>> BuildAdjacency(DesignerDocument doc)
    {
        var adjacency = new Dictionary<string, List<string>>();
        foreach (var c in doc.Connections)
        {
            if (!adjacency.TryGetValue(c.SourceNodeId, out var list))
            {
                list = new List<string>();
                adjacency[c.SourceNodeId] = list;
            }

            list.Add(c.TargetNodeId);
        }

        return adjacency;
    }

    private static bool CanReach(Dictionary<string, List<string>> adjacency, string from, string to)
    {
        var stack = new Stack<string>();
        var visited = new HashSet<string>();
        stack.Push(from);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current == to)
            {
                return true;
            }

            if (!visited.Add(current))
            {
                continue;
            }

            if (adjacency.TryGetValue(current, out var neighbours))
            {
                foreach (var n in neighbours)
                {
                    stack.Push(n);
                }
            }
        }

        return false;
    }
}
