// <copyright file="VariableTokens.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Designer.State;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 🔗 Round-2 G4 — builds the <c>{{…}}</c> binding tokens the property binder resolves at
/// runtime: workflow variables (<c>{{Variable.Name}}</c>) and upstream node outputs
/// (<c>{{NodeId.Port}}</c>). Framework-free~ ✨.
/// </summary>
public static class VariableTokens
{
    /// <summary>A pickable binding token~ 🎫.</summary>
    /// <param name="Token">The literal token text to insert (e.g. <c>{{Variable.count}}</c>).</param>
    /// <param name="Label">The display label.</param>
    /// <param name="Category">"Variables" or "Upstream outputs".</param>
    public sealed record TokenOption(string Token, string Label, string Category);

    /// <summary>Builds the variable token for a name~ 🎫.</summary>
    /// <param name="name">The variable name.</param>
    /// <returns>The literal token.</returns>
    public static string VariableToken(string name) => $"{{{{Variable.{name}}}}}";

    /// <summary>Builds the upstream-output token for a node/port~ 🎫.</summary>
    /// <param name="nodeId">The source node id.</param>
    /// <param name="port">The output port name.</param>
    /// <returns>The literal token.</returns>
    public static string OutputToken(string nodeId, string port) => $"{{{{{nodeId}.{port}}}}}";

    /// <summary>Returns whether a value contains a binding token~ 🔍.</summary>
    /// <param name="value">The property value text.</param>
    /// <returns>True when a <c>{{…}}</c> pattern is present.</returns>
    public static bool ContainsToken(string? value)
        => !string.IsNullOrEmpty(value) && value.Contains("{{", StringComparison.Ordinal) && value.Contains("}}", StringComparison.Ordinal);

    /// <summary>
    /// Lists the pickable tokens for a node: all workflow variables plus every output port of
    /// every <em>upstream</em> node (nodes with a connection path into <paramref name="nodeId"/>)~ 📚.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="nodeId">The node being configured.</param>
    /// <returns>The options (variables first, then upstream outputs, both alphabetical).</returns>
    public static IReadOnlyList<TokenOption> OptionsFor(DesignerDocument document, string nodeId)
    {
        var options = new List<TokenOption>();

        foreach (var name in document.Variables.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            options.Add(new TokenOption(VariableToken(name), name, "Variables"));
        }

        foreach (var upstreamId in UpstreamOf(document, nodeId).OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
        {
            var node = document.FindNode(upstreamId);
            if (node is null)
            {
                continue;
            }

            foreach (var port in NodePorts.Outputs(node))
            {
                options.Add(new TokenOption(OutputToken(upstreamId, port), $"{node.Name} · {port}", "Upstream outputs"));
            }
        }

        return options;
    }

    private static HashSet<string> UpstreamOf(DesignerDocument document, string nodeId)
    {
        var upstream = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue(nodeId);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var c in document.Connections)
            {
                if (c.TargetNodeId == current && c.SourceNodeId != nodeId && upstream.Add(c.SourceNodeId))
                {
                    queue.Enqueue(c.SourceNodeId);
                }
            }
        }

        return upstream;
    }
}
