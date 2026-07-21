// <copyright file="ModuleDependencyResolver.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Dependencies;

using System;
using System.Collections.Generic;
using System.Linq;
using Workflow.Modules.Abstractions;

/// <summary>
/// 🔗 Phase 2.8.1 — Resolves module load/registration order from declared dependencies.
/// Topologically sorts modules, detects cycles (reporting the offending path), and validates that
/// every declared dependency is available~ ✨.
/// </summary>
public sealed class ModuleDependencyResolver
{
    private readonly IModuleRegistry? existingRegistry;

    /// <summary>Initializes a new instance of the <see cref="ModuleDependencyResolver"/> class~ 🔗.</summary>
    /// <param name="existingRegistry">
    /// Optional registry of already-registered modules, treated as satisfied dependencies when
    /// resolving a new batch (so incremental installs can depend on prior ones).
    /// </param>
    public ModuleDependencyResolver(IModuleRegistry? existingRegistry = null)
    {
        this.existingRegistry = existingRegistry;
    }

    /// <summary>
    /// Resolves a dependency-ordered sequence for the given modules~ 🧮.
    /// </summary>
    /// <param name="modules">The modules to order.</param>
    /// <returns>A <see cref="DependencyResolution"/> — <c>Success</c> with ordered modules, or errors.</returns>
    public DependencyResolution Resolve(IEnumerable<IWorkflowModule> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);

        var list = modules.ToList();
        var byId = new Dictionary<string, IWorkflowModule>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in list)
        {
            byId[module.ModuleId] = module;
        }

        // 🔎 Missing-dependency detection (against the batch + the existing registry)~
        var missing = new List<string>();
        foreach (var module in list)
        {
            foreach (var depId in module.Dependencies)
            {
                if (!byId.ContainsKey(depId) && this.existingRegistry?.HasModule(depId) != true)
                {
                    missing.Add($"'{module.ModuleId}' requires missing dependency '{depId}'");
                }
            }
        }

        if (missing.Count > 0)
        {
            return DependencyResolution.Failed(missing);
        }

        // 🔁 Topological sort via DFS with cycle detection~
        var ordered = new List<IWorkflowModule>();
        var state = new Dictionary<string, VisitState>(StringComparer.OrdinalIgnoreCase);
        var stack = new List<string>();

        foreach (var module in list)
        {
            var cycle = Visit(module.ModuleId, byId, state, stack, ordered);
            if (cycle is not null)
            {
                return DependencyResolution.Failed(new[] { $"Circular dependency detected: {string.Join(" → ", cycle)}" });
            }
        }

        return DependencyResolution.Ok(ordered);
    }

    /// <summary>
    /// Returns the ids of modules (within the given set) that depend on <paramref name="moduleId"/>~ 🔄.
    /// </summary>
    /// <param name="moduleId">The module whose dependents are wanted.</param>
    /// <param name="candidates">The set to search (e.g. all registered modules).</param>
    /// <returns>The dependent module ids.</returns>
    public IReadOnlyList<string> GetDependents(string moduleId, IEnumerable<IWorkflowModule> candidates)
    {
        ArgumentException.ThrowIfNullOrEmpty(moduleId);
        ArgumentNullException.ThrowIfNull(candidates);

        return candidates
            .Where(m => m.Dependencies.Any(d => string.Equals(d, moduleId, StringComparison.OrdinalIgnoreCase)))
            .Select(m => m.ModuleId)
            .ToList();
    }

    private static List<string>? Visit(
        string moduleId,
        IReadOnlyDictionary<string, IWorkflowModule> byId,
        Dictionary<string, VisitState> state,
        List<string> stack,
        List<IWorkflowModule> ordered)
    {
        if (state.TryGetValue(moduleId, out var s))
        {
            if (s == VisitState.Visiting)
            {
                // Found a cycle — build the path from where the id first appears on the stack~
                var startIndex = stack.FindIndex(id => string.Equals(id, moduleId, StringComparison.OrdinalIgnoreCase));
                var cycle = stack.Skip(startIndex).ToList();
                cycle.Add(moduleId);
                return cycle;
            }

            return null; // already fully visited
        }

        // Dependencies outside the batch are already satisfied (checked earlier) — skip them~
        if (!byId.TryGetValue(moduleId, out var module))
        {
            return null;
        }

        state[moduleId] = VisitState.Visiting;
        stack.Add(moduleId);

        foreach (var depId in module.Dependencies)
        {
            var cycle = Visit(depId, byId, state, stack, ordered);
            if (cycle is not null)
            {
                return cycle;
            }
        }

        stack.RemoveAt(stack.Count - 1);
        state[moduleId] = VisitState.Visited;
        ordered.Add(module);
        return null;
    }

    private enum VisitState
    {
        Visiting,
        Visited,
    }
}

/// <summary>
/// 🔗 Phase 2.8.1 — The outcome of a dependency resolution~ ✨.
/// </summary>
/// <param name="Success">Whether resolution succeeded.</param>
/// <param name="Ordered">Modules in dependency order (dependencies first) on success.</param>
/// <param name="Errors">Missing-dependency or cycle errors on failure.</param>
public sealed record DependencyResolution(
    bool Success,
    IReadOnlyList<IWorkflowModule> Ordered,
    IReadOnlyList<string> Errors)
{
    /// <summary>Creates a successful resolution~ ✅.</summary>
    /// <param name="ordered">The dependency-ordered modules.</param>
    /// <returns>A successful <see cref="DependencyResolution"/>.</returns>
    public static DependencyResolution Ok(IReadOnlyList<IWorkflowModule> ordered)
        => new(true, ordered, Array.Empty<string>());

    /// <summary>Creates a failed resolution~ ❌.</summary>
    /// <param name="errors">The errors.</param>
    /// <returns>A failed <see cref="DependencyResolution"/>.</returns>
    public static DependencyResolution Failed(IReadOnlyList<string> errors)
        => new(false, Array.Empty<IWorkflowModule>(), errors);
}
