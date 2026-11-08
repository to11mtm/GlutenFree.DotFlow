// <copyright file="VariableClashChecker.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Validation;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;

/// <summary>
/// 🔀 §10.2 — save-time warning for a <c>{{Variable.x}}</c> in a node whose input port <c>x</c> is
/// wired. The token reads the variable, never the value on the port (port values stopped doubling
/// as variables in §10.2), so the two are easy to mix up.
/// </summary>
/// <remarks>
/// Mirrors the designer's <c>VariableLint.Clashes</c>: same token parsing, same acknowledgement —
/// node <c>Metadata["variableClashAck"]</c> holding comma-separated <c>name@sourceNode.sourcePort</c>
/// entries ("keep the variable"). The server can't see global variables, so this is always a
/// warning; the run itself fails clearly if the variable really doesn't exist.
/// </remarks>
public static class VariableClashChecker
{
    /// <summary>The warning code.</summary>
    public const string Code = "VB001";

    /// <summary>The node metadata key holding acknowledged clashes.</summary>
    public const string AckKey = "variableClashAck";

    private const string SetVariableModuleId = "builtin.setvariable";

    private static readonly Regex TokenPattern = new(@"(?<!\\)\{\{\s*(.+?)\s*\}\}", RegexOptions.Compiled);

    private static readonly Regex VariableReference = new(
        @"(?<![A-Za-z0-9_.])Variable\.([A-Za-z0-9_]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Finds the unacknowledged clashes in one node's template-enabled properties.</summary>
    /// <param name="workflow">The workflow.</param>
    /// <param name="node">The node.</param>
    /// <param name="module">The module the node runs (for which properties take templates).</param>
    /// <returns>One warning per clashing name.</returns>
    public static IEnumerable<ValidationWarning> Check(WorkflowDefinition workflow, NodeDefinition node, IWorkflowModule module)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(module);

        var templated = module.Schema.Properties
            .Where(p => p.SupportsTemplates)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (templated.Count == 0)
        {
            yield break;
        }

        var acknowledged = Acknowledged(node);
        var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (propertyName, value) in node.Properties)
        {
            if (!templated.Contains(propertyName) || value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            foreach (var name in VariableNames(value.GetString()))
            {
                var wire = workflow.Connections.FirstOrDefault(c =>
                    c.TargetNodeId == node.Id && string.Equals(c.TargetPortName, name, StringComparison.OrdinalIgnoreCase));
                if (wire is null
                    || acknowledged.Contains($"{wire.TargetPortName}@{wire.SourceNodeId}.{wire.SourcePortName}")
                    || !reported.Add(name))
                {
                    continue;
                }

                var incoming = $"{{{{{wire.SourceNodeId}.{wire.SourcePortName}}}}}";
                var message = IsKnownVariable(workflow, name)
                    ? $"'{propertyName}': {{{{Variable.{name}}}}} reads the variable '{name}', not the value wired into "
                      + $"this node's '{wire.TargetPortName}' port ({incoming}). Use {incoming} for the incoming value, "
                      + "or keep the variable in the designer to silence this."
                    : $"'{propertyName}': {{{{Variable.{name}}}}} reads a variable, but no variable '{name}' is declared or "
                      + $"set — the value wired into the '{wire.TargetPortName}' port is {incoming}. Unless '{name}' is a "
                      + "global or a run input, this node will fail at run time.";
                yield return new ValidationWarning(Code, message, node.Id);
            }
        }
    }

    private static IEnumerable<string> VariableNames(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            yield break;
        }

        foreach (Match token in TokenPattern.Matches(value))
        {
            foreach (Match reference in VariableReference.Matches(token.Groups[1].Value))
            {
                yield return reference.Groups[1].Value;
            }
        }
    }

    private static bool IsKnownVariable(WorkflowDefinition workflow, string name)
        => workflow.Variables.Keys.Any(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase))
           || workflow.Nodes.Any(n =>
               string.Equals(n.ModuleId, SetVariableModuleId, StringComparison.OrdinalIgnoreCase)
               && n.Properties.Find("name").Match(
                   Some: v => v.ValueKind == JsonValueKind.String && string.Equals(v.GetString(), name, StringComparison.OrdinalIgnoreCase),
                   None: () => false));

    private static HashSet<string> Acknowledged(NodeDefinition node)
        => node.Metadata is { } metadata && metadata.Find(AckKey).Match(Some: v => v, None: () => (string?)null) is { Length: > 0 } raw
            ? raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}
