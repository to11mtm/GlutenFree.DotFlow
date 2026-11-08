// <copyright file="UpstreamReferences.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Designer.State;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Workflow.UI.Client.Api.Dtos;
using Workflow.UI.Client.Designer.State.Commands;

/// <summary>
/// 🧬 §10.1 — <c>{{nodeId.port}}</c> resolves for any <b>ancestor</b> of the node (any node with a
/// connection path into it, across construct bodies) once it has run. A reference to a node that
/// isn't an ancestor — a sibling branch, something downstream — fails the node, so the designer
/// flags it and offers two optional fixes: <b>wire it in</b> or pass it <b>via a variable</b>.
/// </summary>
public static class UpstreamReferences
{
    /// <summary>The builtin module used by the "via a variable" fix.</summary>
    public const string SetVariableModuleId = "builtin.setvariable";

    private static readonly Regex TokenPattern = new(@"(?<!\\)\{\{\s*(.+?)\s*\}\}", RegexOptions.Compiled);

    private static readonly Regex PureReference = new(@"^([A-Za-z_][A-Za-z0-9_\-]*)\.([A-Za-z0-9_]+)(\.[A-Za-z0-9_.]+)?$", RegexOptions.Compiled);

    /// <summary>A <c>{{nodeId.port}}</c> whose node exists but isn't upstream of the referencing node.</summary>
    /// <param name="PropertyName">The property holding it.</param>
    /// <param name="Token">The literal token.</param>
    /// <param name="SourceNodeId">The referenced node.</param>
    /// <param name="SourcePort">The referenced output port.</param>
    /// <param name="WirePort">A free input port on the referencing node to wire into, or null (no "wire it in").</param>
    public sealed record NonUpstreamReference(
        string PropertyName,
        string Token,
        string SourceNodeId,
        string SourcePort,
        string? WirePort)
    {
        /// <summary>Gets the explanation shown in the designer.</summary>
        /// <param name="doc">The document (for node names).</param>
        /// <returns>The message.</returns>
        public string Message(DesignerDocument doc)
            => $"{this.Token} reads '{doc.FindNode(this.SourceNodeId)?.Name ?? this.SourceNodeId}', which isn't upstream of "
               + "this node — no connection path leads from it here, so its output may not exist when this node "
               + "runs, and the node will fail.";
    }

    /// <summary>Finds the non-upstream node references in one property value.</summary>
    /// <param name="doc">The document.</param>
    /// <param name="node">The referencing node.</param>
    /// <param name="propertyName">The property name.</param>
    /// <param name="value">The property value text.</param>
    /// <returns>One entry per distinct reference.</returns>
    public static IReadOnlyList<NonUpstreamReference> Find(
        DesignerDocument doc,
        DesignerNode node,
        string propertyName,
        string? value)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(node);

        if (string.IsNullOrEmpty(value))
        {
            return [];
        }

        HashSet<string>? upstream = null;
        var found = new List<NonUpstreamReference>();
        foreach (Match token in TokenPattern.Matches(value))
        {
            var match = PureReference.Match(token.Groups[1].Value.Trim());
            if (!match.Success)
            {
                continue;
            }

            var nodeId = match.Groups[1].Value;
            var port = match.Groups[2].Value;
            if (IsReservedRoot(nodeId)
                || doc.FindNode(nodeId) is null
                || nodeId == node.Id
                || found.Any(f => f.SourceNodeId == nodeId && f.SourcePort == port))
            {
                continue;
            }

            upstream ??= VariableTokens.UpstreamOf(doc, node.Id);
            if (!upstream.Contains(nodeId))
            {
                found.Add(new NonUpstreamReference(propertyName, token.Value, nodeId, port, FreeInputPort(doc, node, port)));
            }
        }

        return found;
    }

    /// <summary>
    /// "Wire it in": connect the referenced port into a free input port of this node, making the
    /// source an ancestor (the token then resolves unchanged). Null when no port is free.
    /// </summary>
    /// <param name="node">The referencing node.</param>
    /// <param name="reference">The reference.</param>
    /// <returns>The command, or null.</returns>
    public static IDesignerCommand? WireItIn(DesignerNode node, NonUpstreamReference reference)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(reference);

        return reference.WirePort is null
            ? null
            : new AddConnectionCommand(new DesignerConnection
            {
                SourceNodeId = reference.SourceNodeId,
                SourcePortName = reference.SourcePort,
                TargetNodeId = node.Id,
                TargetPortName = reference.WirePort,
            });
    }

    /// <summary>
    /// "Via a variable": add a SetVariable fed by the referenced port and point the token at the
    /// variable instead, as one undoable step. The variable is only set once the source has run —
    /// the lint keeps warning if the SetVariable isn't upstream of this node.
    /// </summary>
    /// <param name="doc">The document.</param>
    /// <param name="node">The referencing node.</param>
    /// <param name="reference">The reference.</param>
    /// <param name="value">The property's current text (may hold unapplied edits).</param>
    /// <param name="setVariableSchema">The SetVariable module schema, when known.</param>
    /// <returns>The command, the variable name, and the rewritten property text.</returns>
    public static (IDesignerCommand Command, string VariableName, string RewrittenValue) ViaVariable(
        DesignerDocument doc,
        DesignerNode node,
        NonUpstreamReference reference,
        string value,
        ModuleSchemaDto? setVariableSchema)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(reference);

        var source = doc.FindNode(reference.SourceNodeId)
            ?? throw new ArgumentException($"No node '{reference.SourceNodeId}'.", nameof(reference));
        var variableName = UniqueVariableName(doc, $"{reference.SourceNodeId}_{reference.SourcePort}");

        var setter = new DesignerNode
        {
            Id = NodeIdGenerator.Generate(SetVariableModuleId, new HashSet<string>(doc.Nodes.Select(n => n.Id))),
            ModuleId = SetVariableModuleId,
            Name = $"Keep {reference.SourcePort}",
            X = source.X + CanvasGeometry.NodeWidth + 60,
            Y = source.Y + 120,
            Schema = setVariableSchema,
        };
        setter.Properties["name"] = JsonValues.FromString(variableName);

        var rewritten = Rewrite(value, reference, variableName);
        var before = new Dictionary<string, JsonElement>(node.Properties);
        var after = new Dictionary<string, JsonElement>(node.Properties)
        {
            [reference.PropertyName] = JsonValues.FromString(rewritten),
        };

        var command = new CompositeCommand(
            $"Pass {reference.SourceNodeId}.{reference.SourcePort} via variable {variableName}",
            [
                new AddNodeCommand(setter),
                new AddConnectionCommand(new DesignerConnection
                {
                    SourceNodeId = source.Id,
                    SourcePortName = reference.SourcePort,
                    TargetNodeId = setter.Id,
                    TargetPortName = "value",
                }),
                new EditNodePropertiesCommand(node.Id, before, after),
            ]);
        return (command, variableName, rewritten);
    }

    /// <summary>Rewrites <c>source.port</c> (paths kept) to <c>Variable.name</c> inside tokens.</summary>
    /// <param name="value">The property text.</param>
    /// <param name="reference">The reference.</param>
    /// <param name="variableName">The variable name.</param>
    /// <returns>The rewritten text.</returns>
    public static string Rewrite(string value, NonUpstreamReference reference, string variableName)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var pattern = new Regex(
            $@"(?<![A-Za-z0-9_.\-]){Regex.Escape(reference.SourceNodeId)}\.{Regex.Escape(reference.SourcePort)}(?![A-Za-z0-9_])");
        return TokenPattern.Replace(value, t => pattern.Replace(t.Value, "Variable." + variableName));
    }

    private static bool IsReservedRoot(string root)
        => string.Equals(root, "Variable", StringComparison.OrdinalIgnoreCase)
           || string.Equals(root, VariableLint.SelfInputRoot, StringComparison.OrdinalIgnoreCase)
           || string.Equals(root, VariableLint.StreamItemRoot, StringComparison.OrdinalIgnoreCase);

    private static string? FreeInputPort(DesignerDocument doc, DesignerNode node, string preferred)
    {
        var ports = node.Schema?.Inputs?.Select(p => p.Name).ToList();
        if (ports is null || ports.Count == 0)
        {
            return null;
        }

        var free = ports
            .Where(p => !doc.Connections.Any(c => c.TargetNodeId == node.Id && string.Equals(c.TargetPortName, p, StringComparison.Ordinal)))
            .ToList();
        return free.FirstOrDefault(p => string.Equals(p, preferred, StringComparison.OrdinalIgnoreCase))
               ?? free.FirstOrDefault(p => string.Equals(p, "input", StringComparison.OrdinalIgnoreCase))
               ?? free.FirstOrDefault();
    }

    private static string UniqueVariableName(DesignerDocument doc, string seed)
    {
        var cleaned = Regex.Replace(seed, "[^A-Za-z0-9_]", "_");
        if (cleaned.Length == 0 || char.IsDigit(cleaned[0]))
        {
            cleaned = "_" + cleaned;
        }

        bool Taken(string name)
            => doc.Variables.Keys.Any(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase))
               || doc.Nodes.Any(n => n.ModuleId == SetVariableModuleId
                   && n.Properties.TryGetValue("name", out var v)
                   && v.ValueKind == JsonValueKind.String
                   && string.Equals(v.GetString(), name, StringComparison.OrdinalIgnoreCase));

        var candidate = cleaned;
        for (var i = 2; Taken(candidate); i++)
        {
            candidate = $"{cleaned}{i}";
        }

        return candidate;
    }
}
