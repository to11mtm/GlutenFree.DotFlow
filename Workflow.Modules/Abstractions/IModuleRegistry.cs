// <copyright file="IModuleRegistry.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Abstractions;

using System.Collections.Generic;

/// <summary>
/// 🔍 Registry for discovering and accessing workflow modules.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: The module registry is the central place to look up
/// modules by their ID. Modules can be registered at startup or
/// dynamically loaded from assemblies~ 💖
/// </para>
/// </remarks>
public interface IModuleRegistry
{
    /// <summary>
    /// Get all registered modules. 📋
    /// </summary>
    /// <returns>All registered modules.</returns>
    IReadOnlyList<IWorkflowModule> GetAllModules();

    /// <summary>
    /// Get a specific module by ID. 🔍
    /// </summary>
    /// <param name="moduleId">The module ID to look up.</param>
    /// <returns>The module if found, null otherwise.</returns>
    IWorkflowModule? GetModule(string moduleId);

    /// <summary>
    /// Register a module. ➕
    /// </summary>
    /// <param name="module">The module to register.</param>
    void RegisterModule(IWorkflowModule module);

    /// <summary>
    /// Unregister a module. ➖
    /// </summary>
    /// <param name="moduleId">The module ID to unregister.</param>
    /// <returns>True if the module was found and removed.</returns>
    bool UnregisterModule(string moduleId);

    /// <summary>
    /// Check if a module is registered. ✅
    /// </summary>
    /// <param name="moduleId">The module ID to check.</param>
    /// <returns>True if the module is registered.</returns>
    bool HasModule(string moduleId);
}

