// <copyright file="InMemoryModuleRegistry.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Workflow.Modules.Abstractions;

/// <summary>
/// 🗂️ In-memory implementation of the module registry.
/// Stores modules in a thread-safe dictionary~ 💖
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: This is a simple in-memory registry suitable for
/// development and testing. For production, you might want a
/// registry that can persist to a database or load from assemblies.
/// </para>
/// </remarks>
public class InMemoryModuleRegistry : IModuleRegistry
{
    private readonly ConcurrentDictionary<string, IWorkflowModule> _modules = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public IReadOnlyList<IWorkflowModule> GetAllModules()
    {
        return _modules.Values.ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public IWorkflowModule? GetModule(string moduleId)
    {
        _modules.TryGetValue(moduleId, out var module);
        return module;
    }

    /// <inheritdoc />
    public void RegisterModule(IWorkflowModule module)
    {
        if (module == null)
        {
            throw new ArgumentNullException(nameof(module));
        }

        _modules[module.ModuleId] = module;
    }

    /// <inheritdoc />
    public bool UnregisterModule(string moduleId)
    {
        return _modules.TryRemove(moduleId, out _);
    }

    /// <inheritdoc />
    public bool HasModule(string moduleId)
    {
        return _modules.ContainsKey(moduleId);
    }
}

