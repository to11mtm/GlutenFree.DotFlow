// <copyright file="ModuleConfigurationChecker.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Validation;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;

/// <summary>
/// 🧩 §9.4 — runs a module's own <see cref="IWorkflowModule.ValidateConfiguration"/> against a node's
/// properties, shaped the way the module will actually see them at run time~ ✨.
/// </summary>
/// <remarks>
/// <para>
/// Policy (agreed 2026-11-08): on <b>save</b> the findings are warnings, so half-built workflows stay
/// saveable; when a <b>run is submitted</b> they are errors, so an invalid node can't start a run.
/// </para>
/// <para>
/// To avoid rejecting things that are fine at run time, the configuration is built like
/// <c>NodeExecutor</c> + the property binder build it: JSON values become CLR values and schema
/// defaults fill missing properties. Errors on a property whose value is a <c>{{…}}</c> template are
/// ignored (it can't be judged until it's bound), and properties in
/// <see cref="RunOnlyProperties"/> are only checked at run time.
/// </para>
/// </remarks>
public static class ModuleConfigurationChecker
{
    /// <summary>
    /// Properties that are filled in after the workflow is saved (e.g. by compiling at publish), so
    /// they're only checked when a run is submitted.
    /// </summary>
    public static readonly IReadOnlySet<string> RunOnlyProperties = new HashSet<string>(StringComparer.Ordinal)
    {
        "compiledAssemblyKey",
    };

    /// <summary>Checks one node's configuration with its module's own rules.</summary>
    /// <param name="node">The node.</param>
    /// <param name="module">The node's resolved module.</param>
    /// <param name="atRunTime"><c>false</c> on save (skips <see cref="RunOnlyProperties"/>); <c>true</c> when a run is submitted.</param>
    /// <returns>The module's errors that apply, each stamped with the node id.</returns>
    public static IReadOnlyList<ValidationError> Check(NodeDefinition node, IWorkflowModule module, bool atRunTime)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(module);

        var config = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var prop in module.Schema.Properties)
        {
            if (prop.DefaultValue is not null)
            {
                config[prop.Name] = prop.DefaultValue;
            }
        }

        var templated = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, value) in node.Properties)
        {
            var clr = ToClr(value);
            config[key] = clr;
            if (clr is string text && text.Contains("{{", StringComparison.Ordinal))
            {
                templated.Add(key);
            }
        }

        ValidationResult result;
        try
        {
            result = module.ValidateConfiguration(config);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A module whose own check throws shouldn't take validation down with it~
            return new[]
            {
                new ValidationError("MA005", $"Module '{module.ModuleId}' could not check its configuration: {ex.Message}", node.Id),
            };
        }

        return result.Errors
            .Where(e => e.PropertyName is null
                || (!templated.Contains(e.PropertyName) && (atRunTime || !RunOnlyProperties.Contains(e.PropertyName))))
            .Select(e => e with { NodeId = node.Id })
            .ToList();
    }

    /// <summary>Converts a JSON property value to the CLR shape modules receive at run time (mirrors NodeExecutor).</summary>
    private static object? ToClr(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Array => element.EnumerateArray().Select(ToClr).ToList(),
        JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => ToClr(p.Value)),
        _ => element.ToString(),
    };
}
