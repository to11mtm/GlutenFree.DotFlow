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

    /// <summary>The explicit graph-start marker module id~ 🚀.</summary>
    private const string StartModuleId = "builtin.start";

    /// <summary>The explicit graph-end marker module id~ 🏁.</summary>
    private const string EndModuleId = "builtin.end";

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
        issues.AddRange(ValidateStartAndEnd(doc));
        issues.AddRange(ValidateFanOut(doc));
        issues.AddRange(ValidatePorts(doc));
        issues.AddRange(ValidateStreaming(doc));

        return issues;
    }

    /// <summary>
    /// 🔌 Port-name rules (HTTP-input plan D2/D5) — mirrors the server's MA003/MA004 so the
    /// mismatch is caught while editing rather than at save. Nodes without a loaded schema are
    /// skipped (the unknown-module error already covers them); dynamic-output modules (empty
    /// declared outputs) and merged-mode 'output' are exempt exactly like the server~ 🧭.
    /// </summary>
    /// <param name="doc">The document.</param>
    /// <returns>The issues found.</returns>
    public static IReadOnlyList<GraphIssue> ValidatePorts(DesignerDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        var issues = new List<GraphIssue>();

        // D5 — 'input' is a reserved template root; a node literally named 'input' would be
        // shadowed by it. Undrawable (ids are generated) but reachable via import~ 📥
        foreach (var node in doc.Nodes)
        {
            if (string.Equals(node.Id, "input", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new GraphIssue(
                    IssueSeverity.Warning,
                    "'input' is a reserved word in templates ({{input}} means a node's own input), "
                        + "so outputs of a node with id 'input' can't be referenced. Rename the node.",
                    node.Id));
            }
        }

        foreach (var c in doc.Connections)
        {
            // Target input port must be declared (MA004)~
            if (doc.FindNode(c.TargetNodeId) is { Schema: { } targetSchema } target
                && !string.IsNullOrWhiteSpace(c.TargetPortName))
            {
                var declared = targetSchema.Inputs.Select(p => p.Name).ToList();
                if (!declared.Contains(c.TargetPortName, StringComparer.OrdinalIgnoreCase))
                {
                    var hint = declared.Count == 0
                        ? $"'{target.ModuleId}' accepts no inputs."
                        : $"'{target.ModuleId}' accepts: {string.Join(", ", declared)}.";
                    issues.Add(new GraphIssue(
                        IssueSeverity.Error,
                        $"Connection into '{c.TargetNodeId}' uses input port '{c.TargetPortName}', "
                            + $"which the module doesn't declare. {hint}",
                        c.TargetNodeId));
                }
            }

            // Source output port must be declared (MA003), unless dynamic or merged~
            if (doc.FindNode(c.SourceNodeId) is { Schema: { } sourceSchema } source
                && !string.IsNullOrWhiteSpace(c.SourcePortName))
            {
                var declared = sourceSchema.Outputs.Select(p => p.Name).ToList();
                var mergedOk = string.Equals(c.SourcePortName, OutputShapingUx.MergedPortName, StringComparison.OrdinalIgnoreCase)
                    && OutputShapingUx.IsMerged(source);

                if (declared.Count > 0 && !mergedOk
                    && !declared.Contains(c.SourcePortName, StringComparer.OrdinalIgnoreCase))
                {
                    issues.Add(new GraphIssue(
                        IssueSeverity.Error,
                        $"Connection out of '{c.SourceNodeId}' uses output port '{c.SourcePortName}', "
                            + $"which the module doesn't declare. '{source.ModuleId}' emits: {string.Join(", ", declared)}.",
                        c.SourceNodeId));
                }
            }
        }

        return issues;
    }

    /// <summary>
    /// 🌟 Fan-out legibility rule (K5): a Fan Out whose <c>branch</c> port is wired to nothing
    /// runs an empty sub-graph once per item — legal, and almost certainly not what was meant~ 🧭.
    /// </summary>
    /// <param name="doc">The document.</param>
    /// <returns>The issues found (warnings only).</returns>
    public static IReadOnlyList<GraphIssue> ValidateFanOut(DesignerDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        var issues = new List<GraphIssue>();
        foreach (var node in doc.Nodes)
        {
            if (node.ModuleId == "builtin.fanout"
                && !doc.Connections.Any(c => c.SourceNodeId == node.Id && c.SourcePortName == "branch"))
            {
                issues.Add(new GraphIssue(
                    IssueSeverity.Warning,
                    "Fan Out's branch isn't connected — each item will run an empty sub-graph. "
                        + "Wire the branch port to the nodes that should run once per item.",
                    node.Id));
            }
        }

        return issues;
    }

    /// <summary>
    /// 🚀🏁 Start/End legibility rules — all <b>warnings</b>, never errors.
    /// </summary>
    /// <param name="doc">The document.</param>
    /// <returns>The issues found.</returns>
    /// <remarks>
    /// CopilotNote: every situation flagged here is perfectly legal to the engine — it will run
    /// these workflows without complaint. That's exactly the problem: Start and End exist to make a
    /// graph's shape obvious, so a Start that isn't the start (or an End that isn't the end) is a
    /// diagram that lies. Warn while editing; never block the save (D5)~ 🌸.
    /// </remarks>
    public static IReadOnlyList<GraphIssue> ValidateStartAndEnd(DesignerDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        var issues = new List<GraphIssue>();

        var startNodes = doc.Nodes.Where(n => n.ModuleId == StartModuleId).ToList();
        if (startNodes.Count > 1)
        {
            issues.Add(new GraphIssue(
                IssueSeverity.Warning,
                $"This workflow has {startNodes.Count} Start nodes. All of them run, in parallel — "
                    + "if you meant a single entry point, keep one.",
                startNodes[1].Id));
        }

        foreach (var node in doc.Nodes)
        {
            if (node.ModuleId == StartModuleId
                && doc.Connections.Any(c => c.TargetNodeId == node.Id))
            {
                issues.Add(new GraphIssue(
                    IssueSeverity.Warning,
                    "A Start node has an incoming connection, so it isn't a start. Remove the "
                        + "connection, or use a different module here.",
                    node.Id));
            }

            if (node.ModuleId == EndModuleId
                && doc.Connections.Any(c => c.SourceNodeId == node.Id))
            {
                issues.Add(new GraphIssue(
                    IssueSeverity.Warning,
                    "An End node has outgoing connections, so the nodes after it become the "
                        + "workflow's result instead of this one.",
                    node.Id));
            }
        }

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
    /// 🌊 Phase 5.1.2 — streaming-topology rules. The shape rule is enforced at drag time too
    /// (<c>CanvasView</c>), so these mostly catch imported or hand-edited graphs — but they're the
    /// authority, and they're what turns "it hung" into "you can't do that, here's the fix"~ 🧭.
    /// </summary>
    /// <param name="doc">The document.</param>
    /// <returns>The streaming issues found.</returns>
    public static IReadOnlyList<GraphIssue> ValidateStreaming(DesignerDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        var issues = new List<GraphIssue>();

        // ── Rule 1: the shape rule — streaming ends only meet streaming ends ────────────────
        foreach (var c in doc.Connections)
        {
            var source = doc.FindNode(c.SourceNodeId);
            var target = doc.FindNode(c.TargetNodeId);

            if (!StreamGraph.IsShapeMismatch(source, c.SourcePortName, target, c.TargetPortName))
            {
                continue;
            }

            var sourceIsStreaming = StreamGraph.IsStreamingPort(source, c.SourcePortName, isOutput: true);
            var bridge = StreamGraph.BridgeFor(sourceIsStreaming);
            var direction = sourceIsStreaming
                ? $"'{c.SourceNodeId}.{c.SourcePortName}' is a stream but '{c.TargetNodeId}.{c.TargetPortName}' expects a single value"
                : $"'{c.SourceNodeId}.{c.SourcePortName}' is a single value but '{c.TargetNodeId}.{c.TargetPortName}' expects a stream";

            issues.Add(new GraphIssue(
                IssueSeverity.Error,
                $"{direction}. Insert a '{bridge}' node between them.",
                c.TargetNodeId));
        }

        var regionIndexByNode = StreamGraph.RegionIndexByNode(doc);
        if (regionIndexByNode.Count == 0)
        {
            return issues;
        }

        // ── Rule 2: no variable writes inside a region ─────────────────────────────────────
        // Items are processed concurrently and out of order, so "the" value a write leaves behind
        // is undefined. Variables are a read-only snapshot taken when the region starts~ 💾
        foreach (var node in doc.Nodes)
        {
            if (regionIndexByNode.ContainsKey(node.Id)
                && string.Equals(node.ModuleId, StreamGraph.SetVariableModuleId, StringComparison.Ordinal))
            {
                issues.Add(new GraphIssue(
                    IssueSeverity.Error,
                    $"'{node.Name}' writes a variable inside a streaming region, where item order isn't defined. "
                        + "Collect the stream first, then write the variable after the region.",
                    node.Id));
            }
        }

        // ── Rule 3: streams may not cross a construct boundary ─────────────────────────────
        // A live stream entering a loop body / try / catch / parallel branch would tie two
        // schedulers together — deadlock territory. Bridge out, then back in~ 🚧
        foreach (var c in doc.Connections)
        {
            if (!StreamGraph.IsStreamingEdge(doc, c))
            {
                continue;
            }

            var source = doc.FindNode(c.SourceNodeId);
            if (NodePorts.IsStructuralEdge(source, c.SourcePortName))
            {
                issues.Add(new GraphIssue(
                    IssueSeverity.Error,
                    $"A stream can't cross into a '{c.SourcePortName}' body. Collect it before the boundary "
                        + "and stream again inside, or keep the whole region outside the construct.",
                    c.SourceNodeId));
            }
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
