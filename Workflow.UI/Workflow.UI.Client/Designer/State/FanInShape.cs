// <copyright file="FanInShape.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Designer.State;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 🪄 Input-shape hinting (T7) — derives, at design time, what a FanIn node will receive and
/// produce: the ordered branch list (connection <em>declaration</em> order, exactly what the
/// engine iterates), and the mode-aware result shape. The <c>named</c>-key and <c>merge</c>
/// computations deliberately mirror <c>FanInModule.NamedBranches</c>/<c>MergeBranches</c> — a
/// drift-guard test asserts both sides against shared fixtures. Framework-free (D2)~ ✨.
/// </summary>
public static class FanInShape
{
    /// <summary>The FanIn module id~ 🪄.</summary>
    public const string ModuleId = "builtin.fanin";

    /// <summary>One incoming branch, in engine order~ 🌿.</summary>
    /// <param name="Index">Zero-based branch index (connection declaration order).</param>
    /// <param name="SourceNodeId">The source node id.</param>
    /// <param name="SourceNodeName">The source node display name.</param>
    /// <param name="SourcePortName">The source output port.</param>
    public sealed record Branch(int Index, string SourceNodeId, string SourceNodeName, string SourcePortName);

    /// <summary>Whether a node is a FanIn~ 🔍.</summary>
    /// <param name="node">The node.</param>
    /// <returns>True for <c>builtin.fanin</c>.</returns>
    public static bool IsFanIn(DesignerNode node) => node.ModuleId == ModuleId;

    /// <summary>
    /// The node's incoming branches in <b>connection declaration order</b> — the order the engine
    /// builds <c>__incomingBranches__</c> in (every edge into a FanIn is a branch, whatever port
    /// it lands on)~ 🌿.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="nodeId">The FanIn node id.</param>
    /// <returns>The ordered branches.</returns>
    public static IReadOnlyList<Branch> Branches(DesignerDocument document, string nodeId)
    {
        var branches = new List<Branch>();
        foreach (var c in document.Connections)
        {
            if (c.TargetNodeId != nodeId)
            {
                continue;
            }

            var source = document.FindNode(c.SourceNodeId);
            branches.Add(new Branch(branches.Count, c.SourceNodeId, source?.Name ?? c.SourceNodeId, c.SourcePortName));
        }

        return branches;
    }

    /// <summary>The node's aggregation mode (lower-case), defaulting to <c>concat</c>~ 🎛️.</summary>
    /// <param name="node">The FanIn node.</param>
    /// <returns>The mode.</returns>
    public static string Mode(DesignerNode node)
        => node.Properties.TryGetValue("mode", out var v)
           && v.ValueKind == System.Text.Json.JsonValueKind.String
           && v.GetString() is { Length: > 0 } s
            ? s.ToLowerInvariant()
            : "concat";

    /// <summary>
    /// The keys a <c>named</c>-mode result will have — the engine's rule exactly: each branch's
    /// source port name; colliding port names (same port from different nodes) fall back to
    /// <c>nodeId.port</c> for the colliding entries~ 🏷️.
    /// </summary>
    /// <param name="branches">The ordered branches.</param>
    /// <returns>The result keys, index-aligned with the branches.</returns>
    public static IReadOnlyList<string> NamedKeys(IReadOnlyList<Branch> branches)
    {
        var portCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var b in branches)
        {
            portCounts[b.SourcePortName] = portCounts.TryGetValue(b.SourcePortName, out var c) ? c + 1 : 1;
        }

        return branches
            .Select(b => portCounts[b.SourcePortName] > 1 ? $"{b.SourceNodeId}.{b.SourcePortName}" : b.SourcePortName)
            .ToList();
    }

    /// <summary>
    /// The keys a <c>merge</c>-mode result will have: the union of every branch payload's keys
    /// (a branch payload is the source node's whole output dictionary, so its keys are that node's
    /// output ports), each with the index of the branch whose value wins (last writer)~ 🧪.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="branches">The ordered branches.</param>
    /// <returns>Key → winning branch index, in first-appearance order.</returns>
    public static IReadOnlyList<(string Key, int WinningBranchIndex)> MergeKeys(
        DesignerDocument document,
        IReadOnlyList<Branch> branches)
    {
        var winners = new Dictionary<string, int>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var b in branches)
        {
            var source = document.FindNode(b.SourceNodeId);
            if (source is null)
            {
                continue;
            }

            foreach (var key in NodePorts.Outputs(source))
            {
                if (!winners.ContainsKey(key))
                {
                    order.Add(key);
                }

                winners[key] = b.Index;
            }
        }

        return order.Select(k => (k, winners[k])).ToList();
    }

    /// <summary>A one-line, beginner-readable description of the result shape for the mode~ 💬.</summary>
    /// <param name="document">The document.</param>
    /// <param name="node">The FanIn node.</param>
    /// <returns>The summary text.</returns>
    public static string ShapeSummary(DesignerDocument document, DesignerNode node)
    {
        var branches = Branches(document, node.Id);
        if (branches.Count == 0)
        {
            return "No branches yet — every connection drawn into this node becomes one branch.";
        }

        return Mode(node) switch
        {
            "concat" => $"result = a list of {branches.Count} branch payload(s), in the order above.",
            "merge" => "result = one object — all branch payloads merged; when two branches emit the same key, the later branch wins.",
            "named" => $"result = one object with a key per branch: {string.Join(", ", NamedKeys(branches))}.",
            "first" => $"result = branch 1's payload ({branches[0].SourceNodeName} · {branches[0].SourcePortName}).",
            "last" => $"result = branch {branches.Count}'s payload ({branches[^1].SourceNodeName} · {branches[^1].SourcePortName}).",
            var other => $"result shape depends on mode '{other}'.",
        };
    }
}
