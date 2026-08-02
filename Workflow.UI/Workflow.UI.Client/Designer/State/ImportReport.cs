// <copyright file="ImportReport.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Designer.State;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Workflow.UI.Client.Api.Dtos;

/// <summary>
/// 🧭 Phase 3.6 (E5) — what won't survive the trip into *this* environment.
/// </summary>
/// <remarks>
/// A workflow file is portable, but the things it references — installed modules, module versions,
/// named database connections, global variables — are environment configuration. Importing without
/// checking produces one of two bad outcomes: a wholesale <c>422</c> from the API's
/// <c>ModuleAwareWorkflowValidator</c> with no explanation, or a workflow that saves cleanly and
/// then fails at execution (a pinned module version that isn't installed does exactly that).
/// <para>
/// This runs client-side after parsing and before saving, so problems are visible while they're
/// still cheap to fix~ ✨.
/// </para>
/// </remarks>
public static class ImportReport
{
    private const string ModuleVersionKey = "moduleVersion";
    private const string ConnectionIdProperty = "connectionId";

    /// <summary>Builds the report for an imported workflow~ 🧭.</summary>
    /// <param name="workflow">The parsed workflow.</param>
    /// <param name="knownModuleIds">Module ids installed here.</param>
    /// <param name="moduleVersions">Installed versions per module id, when known.</param>
    /// <param name="knownConnectionIds">Named database connections registered here.</param>
    /// <param name="globalVariableNames">Global variable names available here.</param>
    /// <returns>The issues found, most severe first.</returns>
    public static IReadOnlyList<GraphIssue> Build(
        WorkflowDto workflow,
        ISet<string> knownModuleIds,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>>? moduleVersions = null,
        IReadOnlyCollection<string>? knownConnectionIds = null,
        IReadOnlyCollection<string>? globalVariableNames = null)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(knownModuleIds);

        var issues = new List<GraphIssue>();

        foreach (var node in workflow.Nodes ?? new List<NodeDto>())
        {
            // E5.1 — a missing module is fatal: the API's validator will refuse the save anyway,
            // so say which ones and why rather than letting a 422 explain it badly.
            if (!knownModuleIds.Contains(node.ModuleId))
            {
                issues.Add(new GraphIssue(
                    IssueSeverity.Error,
                    $"'{node.Name}' needs module '{node.ModuleId}', which isn't installed here. "
                    + "Install it before saving, or delete the node.",
                    node.Id));
                continue;
            }

            // E5.2 — a pinned version that isn't installed saves fine and fails at *execution*
            // (NodeExecutor.ResolvePinnedVersion). Pull that failure forward to import time.
            if (PinnedVersionOf(node) is { } pinned
                && moduleVersions is not null
                && moduleVersions.TryGetValue(node.ModuleId, out var available)
                && !available.Contains(pinned, StringComparer.OrdinalIgnoreCase))
            {
                issues.Add(new GraphIssue(
                    IssueSeverity.Warning,
                    $"'{node.Name}' pins {node.ModuleId} v{pinned}, which isn't installed here. "
                    + "It will import, but fail when the workflow runs.",
                    node.Id));
            }

            // E5.3 — named connections are environment config by design, so this is expected on a
            // cross-environment import rather than a defect. Still worth listing.
            if (knownConnectionIds is not null
                && ConnectionIdOf(node) is { } connectionId
                && !knownConnectionIds.Contains(connectionId, StringComparer.OrdinalIgnoreCase))
            {
                issues.Add(new GraphIssue(
                    IssueSeverity.Warning,
                    $"'{node.Name}' uses database connection '{connectionId}', which isn't registered "
                    + "here. Add it in Settings before running.",
                    node.Id));
            }
        }

        issues.AddRange(VariableIssues(workflow, globalVariableNames));

        return issues
            .OrderBy(i => i.Severity == IssueSeverity.Error ? 0 : 1)
            .ToList();
    }

    /// <summary>Whether the report contains anything that blocks saving~ 🛑.</summary>
    /// <param name="issues">The report.</param>
    /// <returns>True when at least one error is present.</returns>
    public static bool HasBlockers(IEnumerable<GraphIssue> issues)
        => issues?.Any(i => i.Severity == IssueSeverity.Error) ?? false;

    private static IEnumerable<GraphIssue> VariableIssues(
        WorkflowDto workflow,
        IReadOnlyCollection<string>? globalVariableNames)
    {
        var declared = workflow.Variables ?? new Dictionary<string, JsonElement>();

        // E5.5 — secrets deliberately carry no value (a definition must never contain a
        // credential), so an import always needs them supplied here.
        var secrets = declared
            .Select(kv => WorkflowVariables.Parse(kv.Key, kv.Value))
            .Where(v => v.IsSecret)
            .Select(v => v.Name)
            .ToList();

        if (secrets.Count > 0)
        {
            var plural = secrets.Count == 1 ? "Secret variable" : "Secret variables";
            yield return new GraphIssue(
                IssueSeverity.Warning,
                $"{plural} {Quote(secrets)} carry no value by design — supply them here before running.");
        }

        // E5.4 — references to globals this environment doesn't have. Only meaningful when we
        // actually know the global list; a null means "couldn't check", not "none exist".
        if (globalVariableNames is null)
        {
            yield break;
        }

        foreach (var missing in ReferencedGlobals(workflow, declared)
                     .Where(name => !globalVariableNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                     .OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            yield return new GraphIssue(
                IssueSeverity.Warning,
                $"This workflow references '{{{{Variable.{missing}}}}}', which is neither declared "
                + "here nor a known global variable.");
        }
    }

    /// <summary>
    /// Variable references that the workflow itself doesn't declare — i.e. ones it expects the
    /// environment (a global, or a run input) to supply.
    /// </summary>
    private static IEnumerable<string> ReferencedGlobals(
        WorkflowDto workflow,
        IReadOnlyDictionary<string, JsonElement> declared)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in workflow.Nodes ?? new List<NodeDto>())
        {
            foreach (var value in node.Properties?.Values ?? Enumerable.Empty<JsonElement>())
            {
                if (value.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                foreach (var name in VariableLint.ReferencedVariableNames(value.GetString()))
                {
                    if (!declared.Keys.Contains(name, StringComparer.OrdinalIgnoreCase) && seen.Add(name))
                    {
                        yield return name;
                    }
                }
            }
        }
    }

    private static string? PinnedVersionOf(NodeDto node)
        => node.Metadata is { } metadata
           && metadata.TryGetValue(ModuleVersionKey, out var version)
           && !string.IsNullOrWhiteSpace(version)
            ? version
            : null;

    private static string? ConnectionIdOf(NodeDto node)
        => node.Properties is { } properties
           && properties.TryGetValue(ConnectionIdProperty, out var element)
           && element.ValueKind == JsonValueKind.String
           && element.GetString() is { Length: > 0 } id
            ? id
            : null;

    private static string Quote(IEnumerable<string> names)
        => string.Join(", ", names.Select(n => $"'{n}'"));
}
