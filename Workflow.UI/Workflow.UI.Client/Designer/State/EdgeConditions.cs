// <copyright file="EdgeConditions.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Designer.State;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

/// <summary>
/// 🔀 Stage 7.2 — conditional edges in the designer: the edge label, and checks for conditions the
/// engine won't evaluate or chains that can never fire. Mirrors the engine's <c>EdgeRouting</c>;
/// see <c>docs/conditional-edges.md</c>.
/// </summary>
public static class EdgeConditions
{
    /// <summary>Constructs route their own outgoing edges, so conditions there aren't evaluated.</summary>
    public static readonly IReadOnlySet<string> ConstructModuleIds = new HashSet<string>(StringComparer.Ordinal)
    {
        "builtin.loop.foreach",
        "builtin.loop.while",
        "builtin.parallel",
        "builtin.fanout",
        "builtin.trycatch",
        "builtin.database.transaction",
    };

    private static readonly Regex Reference = new(@"(?<![A-Za-z0-9_.])([A-Za-z_][A-Za-z0-9_\-]*)\.[A-Za-z0-9_]+", RegexOptions.Compiled);

    private static readonly HashSet<string> NonNodeRoots = new(StringComparer.Ordinal)
    {
        "value", "Variable", "input", "item", "Math", "JSON", "Number", "String", "Date", "Object", "Array", "Boolean",
    };

    /// <summary>The label drawn on an edge: the condition, prefixed with its Priority when it has one.</summary>
    /// <param name="connection">The connection.</param>
    /// <returns>The label, or null for a plain edge.</returns>
    public static string? Label(DesignerConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var condition = string.IsNullOrWhiteSpace(connection.Condition) ? null : connection.Condition.Trim();
        return connection.Priority > 0
            ? $"{connection.Priority}: {condition ?? "else"}"
            : condition;
    }

    /// <summary>Design-time checks for edge conditions and priorities.</summary>
    /// <param name="doc">The document.</param>
    /// <returns>The issues.</returns>
    public static IReadOnlyList<GraphIssue> Validate(DesignerDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var issues = new List<GraphIssue>();

        foreach (var c in doc.Connections.Where(c => !string.IsNullOrWhiteSpace(c.Condition) || c.Priority > 0))
        {
            var source = doc.FindNode(c.SourceNodeId);
            var where = $"The connection {source?.Name ?? c.SourceNodeId} · {c.SourcePortName} → {doc.FindNode(c.TargetNodeId)?.Name ?? c.TargetNodeId}";

            if (source is not null && ConstructModuleIds.Contains(source.ModuleId))
            {
                issues.Add(new GraphIssue(IssueSeverity.Warning, $"{where} has a condition or priority, but {source.Name} is a construct that routes its own edges, so it's ignored.", c.SourceNodeId));
                continue;
            }

            if (StreamGraph.IsStreamingEdge(doc, c))
            {
                issues.Add(new GraphIssue(IssueSeverity.Warning, $"{where} is a streaming edge; conditions and priorities don't apply to streams and are ignored.", c.SourceNodeId));
                continue;
            }

            if (!string.IsNullOrWhiteSpace(c.Condition))
            {
                foreach (Match m in Reference.Matches(c.Condition))
                {
                    var root = m.Groups[1].Value;
                    if (!NonNodeRoots.Contains(root) && doc.FindNode(root) is null)
                    {
                        issues.Add(new GraphIssue(IssueSeverity.Error, $"{where}: its condition refers to '{root}', which isn't a node in this workflow, so {source?.Name ?? c.SourceNodeId} will fail when it's checked.", c.SourceNodeId));
                    }
                }
            }
        }

        // Priority chains: an unconditional ("else") edge that isn't last hides every edge after it.
        foreach (var chain in doc.Connections.Where(c => c.Priority > 0).GroupBy(c => (c.SourceNodeId, c.SourcePortName)))
        {
            var ordered = chain.OrderBy(c => c.Priority).ToList();
            var elseIndex = ordered.FindIndex(c => string.IsNullOrWhiteSpace(c.Condition));
            if (elseIndex >= 0 && elseIndex < ordered.Count - 1)
            {
                var hidden = string.Join(", ", ordered.Skip(elseIndex + 1).Select(c => doc.FindNode(c.TargetNodeId)?.Name ?? c.TargetNodeId));
                issues.Add(new GraphIssue(
                    IssueSeverity.Warning,
                    $"{doc.FindNode(chain.Key.SourceNodeId)?.Name ?? chain.Key.SourceNodeId} · {chain.Key.SourcePortName}: the edge with Priority {ordered[elseIndex].Priority} has no condition, so it always wins and the later edges ({hidden}) can never fire.",
                    chain.Key.SourceNodeId));
            }
        }

        return issues;
    }
}
