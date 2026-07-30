// <copyright file="GraphValidator.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Designer.State;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

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
    /// <summary>The structural database-transaction module id~ 💼.</summary>
    private const string TransactionModuleId = "builtin.database.transaction";

    /// <summary>The port whose sub-graph runs inside the transaction~ 💼.</summary>
    private const string TransactionBodyPort = "transactionBody";

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

        issues.AddRange(ValidateTransactions(doc));

        return issues;
    }

    /// <summary>
    /// 💼 Transaction-body rules: nested transactions aren't supported in V1, and a database node
    /// inside a transaction body that targets a different connection won't take part in it~ ✨.
    /// </summary>
    /// <param name="doc">The document.</param>
    /// <returns>The transaction-related issues.</returns>
    public static IReadOnlyList<GraphIssue> ValidateTransactions(DesignerDocument doc)
    {
        if (doc is null)
        {
            throw new ArgumentNullException(nameof(doc));
        }

        var issues = new List<GraphIssue>();
        var transactions = doc.Nodes.Where(n => n.ModuleId == TransactionModuleId).ToList();
        if (transactions.Count == 0)
        {
            return issues;
        }

        var byId = doc.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);

        foreach (var tx in transactions)
        {
            var bodyIds = StructuralRegions.BodyScope(doc, tx.Id, TransactionBodyPort);
            var owner = ConnectionIdOf(tx);

            foreach (var id in bodyIds)
            {
                if (!byId.TryGetValue(id, out var node))
                {
                    continue;
                }

                if (node.ModuleId == TransactionModuleId)
                {
                    issues.Add(new GraphIssue(
                        IssueSeverity.Error,
                        $"Nested transactions aren't supported: '{node.Name}' sits inside the body of '{tx.Name}'.",
                        node.Id));
                    continue;
                }

                if (!node.ModuleId.StartsWith("builtin.database.", StringComparison.Ordinal))
                {
                    continue;
                }

                var nodeConnection = ConnectionIdOf(node);
                if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(nodeConnection))
                {
                    continue;
                }

                if (!string.Equals(owner, nodeConnection, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(new GraphIssue(
                        IssueSeverity.Warning,
                        $"'{node.Name}' uses connection '{nodeConnection}' but the transaction runs on '{owner}' — "
                        + "it will run outside the transaction and won't be rolled back.",
                        node.Id));
                }
            }
        }

        return issues;
    }

    private static string? ConnectionIdOf(DesignerNode node)
        => node.Properties.TryGetValue("connectionId", out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

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
