// <copyright file="DependencyHints.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Modules.State;

using System;
using System.Collections.Generic;
using System.Linq;
using Workflow.UI.Client.Api.Dtos;

/// <summary>
/// 🔗 Phase 3.6.3 (D8) — Framework-free module→module dependency hints for the disable heads-up.
/// Computes which *known* modules declare a given module as a dependency. Workflow-level usage isn't
/// indexed (that's 3.6.P3), so the result is scoped to the module details the client has loaded~ ✨.
/// </summary>
public static class DependencyHints
{
    /// <summary>Lists the ids of known modules that depend on <paramref name="moduleId"/>~ 🔗.</summary>
    /// <param name="known">The module details the client currently knows about.</param>
    /// <param name="moduleId">The module id to find dependents of.</param>
    /// <returns>The dependent module ids (sorted, distinct).</returns>
    public static IReadOnlyList<string> Dependents(IEnumerable<ModuleDetailsDto> known, string moduleId)
        => known
            .Where(m => m.Id != moduleId && m.Dependencies is not null &&
                        m.Dependencies.Any(d => string.Equals(d, moduleId, StringComparison.OrdinalIgnoreCase)))
            .Select(m => m.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
