// <copyright file="ModuleSchemaComparer.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Versioning;

using System;
using System.Collections.Generic;
using System.Linq;
using Workflow.Core.Models;

/// <summary>
/// 🧷 Phase 2.8.2 — Diffs two <see cref="ModuleSchema"/> shapes to surface potentially-breaking
/// changes between module versions. Warn-only (Q5) — it never blocks an install~ ✨.
/// </summary>
public static class ModuleSchemaComparer
{
    /// <summary>Compares an old schema to a new one and returns human-readable breaking-change warnings~ 🧷.</summary>
    /// <param name="oldSchema">The previously-installed version's schema.</param>
    /// <param name="newSchema">The incoming version's schema.</param>
    /// <returns>Warning messages (empty when no breaking changes are detected).</returns>
    public static IReadOnlyList<string> Compare(ModuleSchema oldSchema, ModuleSchema newSchema)
    {
        ArgumentNullException.ThrowIfNull(oldSchema);
        ArgumentNullException.ThrowIfNull(newSchema);

        var warnings = new List<string>();

        ComparePorts(oldSchema.Inputs.ToList(), newSchema.Inputs.ToList(), "input", warnings);
        ComparePorts(oldSchema.Outputs.ToList(), newSchema.Outputs.ToList(), "output", warnings);
        CompareProperties(oldSchema.Properties.ToList(), newSchema.Properties.ToList(), warnings);

        return warnings;
    }

    private static void ComparePorts(
        IReadOnlyList<PortDefinition> oldPorts,
        IReadOnlyList<PortDefinition> newPorts,
        string kind,
        List<string> warnings)
    {
        var newByName = newPorts.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var old in oldPorts)
        {
            if (!newByName.TryGetValue(old.Name, out var updated))
            {
                warnings.Add($"Removed {kind} port '{old.Name}'.");
                continue;
            }

            if (updated.DataType != old.DataType)
            {
                warnings.Add($"{char.ToUpperInvariant(kind[0])}{kind[1..]} port '{old.Name}' changed type from '{old.DataType.Name}' to '{updated.DataType.Name}'.");
            }
        }

        var oldByName = oldPorts.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var added in newPorts.Where(p => !oldByName.ContainsKey(p.Name) && p.IsRequired))
        {
            warnings.Add($"Added required {kind} port '{added.Name}'.");
        }
    }

    private static void CompareProperties(
        IReadOnlyList<ModulePropertyDefinition> oldProps,
        IReadOnlyList<ModulePropertyDefinition> newProps,
        List<string> warnings)
    {
        var newByName = newProps.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        var oldByName = oldProps.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var old in oldProps)
        {
            if (!newByName.TryGetValue(old.Name, out var updated))
            {
                warnings.Add($"Removed property '{old.Name}'.");
                continue;
            }

            if (updated.DataType != old.DataType)
            {
                warnings.Add($"Property '{old.Name}' changed type from '{old.DataType.Name}' to '{updated.DataType.Name}'.");
            }
        }

        foreach (var added in newProps.Where(p => !oldByName.ContainsKey(p.Name) && p.IsRequired))
        {
            warnings.Add($"Added required property '{added.Name}'.");
        }
    }
}
