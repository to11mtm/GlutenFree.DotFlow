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
    /// <summary>The picker group name for store-backed global variables~ 🌍.</summary>
    public const string GlobalsCategory = "Globals";

    /// <summary>
    /// The interim notice shown alongside globals until secret support ships (parent plan V3.5 /
    /// Q16). Globals became functional in V3 but are stored and returned in plaintext until the
    /// secrets workstream lands, so the warning is deliberately blunt~ 🔒.
    /// </summary>
    public const string GlobalsCredentialWarning =
        "🔒 Not for credentials yet — global values are stored and readable in plaintext until secret support ships.";

    /// <summary>A pickable binding token~ 🎫.</summary>
    /// <param name="Token">The literal token text to insert (e.g. <c>{{Variable.count}}</c>).</param>
    /// <param name="Label">The display label.</param>
    /// <param name="Category">"Variables" or "Upstream outputs".</param>
    /// <param name="Detail">Optional hover detail (a variable's description).</param>
    public sealed record TokenOption(string Token, string Label, string Category, string? Detail = null);

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
    /// Lists the pickable tokens for a node: all workflow variables, any global variables, plus
    /// every output port of every <em>upstream</em> node (nodes with a connection path into
    /// <paramref name="nodeId"/>)~ 📚.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="nodeId">The node being configured.</param>
    /// <param name="globals">
    /// Global variable names from the store, when available. A name already declared on the
    /// workflow is omitted — the workflow's own declaration is what the run will resolve, since it
    /// layers above the global (V3's precedence chain).
    /// </param>
    /// <returns>The options (variables, globals, then upstream outputs).</returns>
    public static IReadOnlyList<TokenOption> OptionsFor(
        DesignerDocument document,
        string nodeId,
        IReadOnlyCollection<string>? globals = null)
    {
        var options = new List<TokenOption>();

        foreach (var (name, raw) in document.Variables.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            // V1.6 — surface the declared type and description so the picker teaches what the
            // variable is, rather than listing a bare name.
            var declared = WorkflowVariables.Parse(name, raw);
            var label = declared.IsSecret
                ? $"{name} — {declared.Type} 🔒"
                : $"{name} — {declared.Type}";
            options.Add(new TokenOption(VariableToken(name), label, "Variables", declared.Description));
        }

        if (globals is { Count: > 0 })
        {
            foreach (var name in globals
                         .Where(g => !document.Variables.Keys.Contains(g, StringComparer.OrdinalIgnoreCase))
                         .OrderBy(g => g, StringComparer.OrdinalIgnoreCase))
            {
                options.Add(new TokenOption(
                    VariableToken(name),
                    name,
                    GlobalsCategory,
                    "Shared across every workflow — set outside this designer."));
            }
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
