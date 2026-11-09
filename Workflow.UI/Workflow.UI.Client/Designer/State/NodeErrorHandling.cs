// <copyright file="NodeErrorHandling.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Designer.State;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

/// <summary>
/// 🚑 §12.7 — a node's "On error" settings in the designer: what happens when it fails (Fail /
/// Continue / Retry / Use error handler), which node handles the error, and how many failures in a
/// row are handled before the run fails. Mirrors the engine's <c>ErrorHandling</c> record
/// (<c>onErrorBehavior</c>, <c>errorNodeId</c>, <c>maxConsecutiveErrors</c>); see
/// <c>docs/error-handling.md</c>.
/// </summary>
public static class NodeErrorHandling
{
    /// <summary>Stop the run (the default).</summary>
    public const string Fail = "Fail";

    /// <summary>Carry on as if the node had succeeded.</summary>
    public const string Continue = "Continue";

    /// <summary>Retry with the node's retry policy (or the default policy).</summary>
    public const string Retry = "Retry";

    /// <summary>Run the handler node (after retries, if the node has a retry policy).</summary>
    public const string UseErrorHandler = "UseErrorHandler";

    /// <summary>The choices, in display order, with labels.</summary>
    public static IReadOnlyList<(string Value, string Label)> Behaviors { get; } =
    [
        (Fail, "Fail the run"),
        (Continue, "Continue as if it succeeded"),
        (Retry, "Retry, then fail"),
        (UseErrorHandler, "Run an error handler"),
    ];

    // The engine's enum order, for documents that store the number instead of the name.
    private static readonly string[] EnumOrder = [Fail, Continue, UseErrorHandler, Retry];

    /// <summary>A node's (or the workflow's) error-handling settings.</summary>
    /// <param name="Behavior">One of the behavior constants.</param>
    /// <param name="HandlerId">The handler node (Use error handler only).</param>
    /// <param name="MaxConsecutiveErrors">Handled failures in a row before the run fails (null = no limit).</param>
    public sealed record Settings(string Behavior, string? HandlerId = null, int? MaxConsecutiveErrors = null)
    {
        /// <summary>The default: fail, no handler.</summary>
        public static readonly Settings Default = new(Fail);
    }

    /// <summary>An "on error" link to draw: from the node that may fail to its handler.</summary>
    /// <param name="NodeId">The node.</param>
    /// <param name="HandlerId">Its handler.</param>
    public sealed record Link(string NodeId, string HandlerId);

    /// <summary>Reads the settings stored on a node (or the workflow).</summary>
    /// <param name="raw">The stored <c>errorHandling</c> JSON (null = default).</param>
    /// <returns>The settings.</returns>
    public static Settings Read(JsonElement? raw)
    {
        if (raw is not { ValueKind: JsonValueKind.Object } json)
        {
            return Settings.Default;
        }

        var behavior = Property(json, "onErrorBehavior") switch
        {
            { ValueKind: JsonValueKind.String } s => Normalize(s.GetString()),
            { ValueKind: JsonValueKind.Number } n when n.TryGetInt32(out var i) && i >= 0 && i < EnumOrder.Length => EnumOrder[i],
            _ => Fail,
        };
        var handler = Property(json, "errorNodeId") is { ValueKind: JsonValueKind.String } h ? h.GetString() : null;
        var max = Property(json, "maxConsecutiveErrors") is { ValueKind: JsonValueKind.Number } m && m.TryGetInt32(out var v) ? v : (int?)null;
        return new Settings(behavior, string.IsNullOrWhiteSpace(handler) ? null : handler, max);
    }

    /// <summary>Writes settings back as the stored JSON (null for the plain default).</summary>
    /// <param name="settings">The settings.</param>
    /// <returns>The JSON, or null.</returns>
    public static JsonElement? Write(Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings == Settings.Default)
        {
            return null;
        }

        var useHandler = settings.Behavior == UseErrorHandler;
        return JsonSerializer.SerializeToElement(new
        {
            onErrorBehavior = settings.Behavior,
            errorNodeId = useHandler ? settings.HandlerId : null,
            maxConsecutiveErrors = useHandler ? settings.MaxConsecutiveErrors : null,
        });
    }

    /// <summary>
    /// Nodes that can handle <paramref name="node"/>'s errors: any other node that isn't downstream
    /// of it (a downstream node is skipped when it fails, so it can't also be its handler).
    /// </summary>
    /// <param name="doc">The document.</param>
    /// <param name="node">The node that may fail.</param>
    /// <returns>The candidates, by name.</returns>
    public static IReadOnlyList<DesignerNode> HandlerCandidates(DesignerDocument doc, DesignerNode node)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(node);

        var downstream = Downstream(doc, node.Id);
        return doc.Nodes
            .Where(n => n.Id != node.Id && !downstream.Contains(n.Id))
            .OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>The "on error" links to draw on the canvas.</summary>
    /// <param name="doc">The document.</param>
    /// <returns>One per node whose handler exists.</returns>
    public static IReadOnlyList<Link> Links(DesignerDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        return doc.Nodes
            .Select(n => (n.Id, Settings: Read(n.ErrorHandling)))
            .Where(x => x.Settings.Behavior == UseErrorHandler && x.Settings.HandlerId is { } h && doc.FindNode(h) is not null)
            .Select(x => new Link(x.Id, x.Settings.HandlerId!))
            .ToList();
    }

    /// <summary>Design-time checks for "Run an error handler".</summary>
    /// <param name="doc">The document.</param>
    /// <returns>The issues.</returns>
    public static IReadOnlyList<GraphIssue> Validate(DesignerDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var issues = new List<GraphIssue>();

        foreach (var node in doc.Nodes)
        {
            var settings = Read(node.ErrorHandling);
            if (settings.Behavior != UseErrorHandler)
            {
                continue;
            }

            var problem = settings.HandlerId switch
            {
                null => "is set to run an error handler but no handler node is chosen, so a failure fails the run",
                var h when h == node.Id => "can't be its own error handler",
                var h when doc.FindNode(h) is null => $"names error handler '{h}', which isn't in this workflow",
                var h when Downstream(doc, node.Id).Contains(h) => $"has its error handler '{doc.FindNode(h)!.Name}' downstream of it — that node is skipped when this one fails, so it can't handle the error",
                _ => null,
            };
            if (problem is not null)
            {
                issues.Add(new GraphIssue(IssueSeverity.Error, $"'{node.Name}' {problem}.", node.Id));
            }

            if (settings.MaxConsecutiveErrors is <= 0)
            {
                issues.Add(new GraphIssue(
                    IssueSeverity.Warning,
                    $"'{node.Name}': max consecutive errors is {settings.MaxConsecutiveErrors}, so its first failure already fails the run.",
                    node.Id));
            }
        }

        var workflow = Read(doc.ErrorHandling);
        if (workflow.Behavior == UseErrorHandler && (workflow.HandlerId is null || doc.FindNode(workflow.HandlerId) is null))
        {
            issues.Add(new GraphIssue(
                IssueSeverity.Error,
                "The workflow's default error handling runs an error handler, but its handler node is missing."));
        }

        return issues;
    }

    private static HashSet<string> Downstream(DesignerDocument doc, string nodeId)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue(nodeId);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var c in doc.Connections)
            {
                if (c.SourceNodeId == current && seen.Add(c.TargetNodeId))
                {
                    queue.Enqueue(c.TargetNodeId);
                }
            }
        }

        return seen;
    }

    private static string Normalize(string? value)
        => Behaviors.Select(b => b.Value).FirstOrDefault(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase)) ?? Fail;

    private static JsonElement? Property(JsonElement json, string name)
    {
        foreach (var property in json.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }
}
