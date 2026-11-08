// <copyright file="NodeReferenceChecker.cs" company="GlutenFree">
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
/// 🧬 §10.1 — save-time warning for a <c>{{nodeId.port}}</c> whose node exists but isn't an
/// <b>ancestor</b> of the referencing node (no connection path leads from it there, structural
/// edges included). Only ancestors that have run are bindable, so such a reference fails the node.
/// Mirrors the designer's lint (<c>UpstreamReferences</c>).
/// </summary>
public static class NodeReferenceChecker
{
    /// <summary>The warning code.</summary>
    public const string Code = "VB002";

    private static readonly Regex TokenPattern = new(@"(?<!\\)\{\{\s*(.+?)\s*\}\}", RegexOptions.Compiled);

    private static readonly Regex PureReference = new(@"^([A-Za-z_][A-Za-z0-9_\-]*)\.([A-Za-z0-9_]+)(\.[A-Za-z0-9_.]+)?$", RegexOptions.Compiled);

    private static readonly System.Collections.Generic.HashSet<string> ReservedRoots = new(StringComparer.OrdinalIgnoreCase) { "Variable", "input", "item" };

    /// <summary>Finds references to existing nodes that aren't upstream, in one node's template-enabled properties.</summary>
    /// <param name="workflow">The workflow.</param>
    /// <param name="node">The node.</param>
    /// <param name="module">The module the node runs (for which properties take templates).</param>
    /// <returns>One warning per distinct non-upstream node referenced.</returns>
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

        var nodeIds = workflow.Nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        HashSet<string>? ancestors = null;
        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (propertyName, value) in node.Properties)
        {
            if (!templated.Contains(propertyName) || value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            foreach (Match token in TokenPattern.Matches(value.GetString() ?? string.Empty))
            {
                var match = PureReference.Match(token.Groups[1].Value.Trim());
                if (!match.Success)
                {
                    continue;
                }

                var referenced = match.Groups[1].Value;
                if (ReservedRoots.Contains(referenced) || referenced == node.Id || !nodeIds.Contains(referenced))
                {
                    continue;
                }

                ancestors ??= AncestorsOf(workflow, node.Id);
                if (!ancestors.Contains(referenced) && reported.Add(referenced))
                {
                    yield return new ValidationWarning(
                        Code,
                        $"'{propertyName}': {token.Value} reads node '{referenced}', which isn't upstream of this node (no "
                        + "connection path leads from it here), so this node will fail at run time. Wire it in, or pass "
                        + "the value through a variable.",
                        node.Id);
                }
            }
        }
    }

    private static HashSet<string> AncestorsOf(WorkflowDefinition workflow, string nodeId)
    {
        var ancestors = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue(nodeId);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var c in workflow.Connections)
            {
                if (c.TargetNodeId == current && c.SourceNodeId != nodeId && ancestors.Add(c.SourceNodeId))
                {
                    queue.Enqueue(c.SourceNodeId);
                }
            }
        }

        return ancestors;
    }
}
