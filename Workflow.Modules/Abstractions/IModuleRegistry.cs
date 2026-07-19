// <copyright file="IModuleRegistry.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Abstractions;

using System;
using System.Collections.Generic;

/// <summary>
/// 🔍 Registry for discovering and accessing workflow modules.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: The module registry is the central place to look up
/// modules by their ID. Modules can be registered at startup or
/// dynamically loaded from assemblies~ 💖.
/// </para>
/// <para>
/// Phase 1.4.2 additions: category lookup, search, type-based registration,
/// and observer-based notifications. No events — we use the observer pattern
/// for cross-domain modularity! 🎯.
/// </para>
/// </remarks>
public interface IModuleRegistry
{
    /// <summary>
    /// Get all registered modules. 📋.
    /// </summary>
    /// <returns>All registered modules.</returns>
    public IReadOnlyList<IWorkflowModule> GetAllModules();

    /// <summary>
    /// Get a specific module by ID. 🔍.
    /// </summary>
    /// <param name="moduleId">The module ID to look up.</param>
    /// <returns>The module if found, null otherwise.</returns>
    public IWorkflowModule? GetModule(string moduleId);

    /// <summary>
    /// 🔢 Phase 2.8.2 — Get a specific version of a module (side-by-side versioning). When
    /// <paramref name="version"/> is <c>null</c>, returns the latest **enabled** version (same as
    /// <see cref="GetModule(string)"/>)~ ✨.
    /// </summary>
    /// <param name="moduleId">The module ID to look up.</param>
    /// <param name="version">The exact version to resolve, or <c>null</c> for latest enabled.</param>
    /// <returns>The resolved module, or <c>null</c> when absent/disabled.</returns>
    /// <remarks>
    /// Default implementation ignores the version (single-version registries) so existing
    /// <see cref="IModuleRegistry"/> implementers keep compiling~ 🌸.
    /// </remarks>
    public IWorkflowModule? GetModule(string moduleId, Version? version) => this.GetModule(moduleId);

    /// <summary>
    /// 🔢 Phase 2.8.2 — Get the installed versions of a module, ascending~ ✨.
    /// </summary>
    /// <param name="moduleId">The module ID.</param>
    /// <returns>The available versions (empty when the module is not registered).</returns>
    public IReadOnlyList<Version> GetModuleVersions(string moduleId)
    {
        var module = this.GetModule(moduleId);
        return module is null ? System.Array.Empty<Version>() : new[] { module.Version };
    }

    /// <summary>
    /// 🔘 Phase 2.8.2 — Enable or disable a specific module version. Disabled versions are excluded
    /// from latest-version resolution and workflow validation~ ✨.
    /// </summary>
    /// <param name="moduleId">The module ID.</param>
    /// <param name="version">The version to toggle.</param>
    /// <param name="enabled">Whether the version should be enabled.</param>
    /// <returns><c>true</c> when the version was found and updated.</returns>
    /// <remarks>Default implementation is a no-op for single-version registries~ 🌸.</remarks>
    public bool SetModuleEnabled(string moduleId, Version version, bool enabled) => false;

    /// <summary>
    /// 🔘 Phase 2.8.2 — Reports whether a specific module version is enabled~ ✨.
    /// </summary>
    /// <param name="moduleId">The module ID.</param>
    /// <param name="version">The version to check.</param>
    /// <returns><c>true</c> when the version exists and is enabled.</returns>
    public bool IsModuleEnabled(string moduleId, Version version)
        => this.GetModule(moduleId, version) is not null;

    /// <summary>
    /// Register a module instance. ➕.
    /// </summary>
    /// <param name="module">The module to register.</param>
    /// <param name="allowOverwrite">
    /// If false (default), throws <see cref="InvalidOperationException"/> when a module
    /// with the same ID is already registered. If true, silently overwrites.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="allowOverwrite"/> is false and a module with the
    /// same ID already exists in the registry.
    /// </exception>
    public void RegisterModule(IWorkflowModule module, bool allowOverwrite = false);

    /// <summary>
    /// Register a module by its <see cref="Type"/>. The type must implement
    /// <see cref="IWorkflowModule"/>. Instantiated via DI if services are provided,
    /// otherwise via <see cref="Activator.CreateInstance(Type)"/>. ➕🏭.
    /// </summary>
    /// <param name="moduleType">The concrete type that implements <see cref="IWorkflowModule"/>.</param>
    /// <param name="services">Optional service provider for constructor injection.</param>
    /// <param name="allowOverwrite">Whether to allow overwriting an existing module with the same ID.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="moduleType"/> does not implement <see cref="IWorkflowModule"/>.
    /// </exception>
    public void RegisterModule(Type moduleType, IServiceProvider? services = null, bool allowOverwrite = false);

    /// <summary>
    /// Unregister a module. ➖.
    /// </summary>
    /// <param name="moduleId">The module ID to unregister.</param>
    /// <returns>True if the module was found and removed.</returns>
    public bool UnregisterModule(string moduleId);

    /// <summary>
    /// Check if a module is registered. ✅.
    /// </summary>
    /// <param name="moduleId">The module ID to check.</param>
    /// <returns>True if the module is registered.</returns>
    public bool HasModule(string moduleId);

    /// <summary>
    /// Gets all modules matching the given category (case-insensitive). 📁.
    /// </summary>
    /// <param name="category">The category name to filter by.</param>
    /// <returns>Modules in the specified category, or empty if none match.</returns>
    public IReadOnlyList<IWorkflowModule> GetModulesByCategory(string category);

    /// <summary>
    /// Searches modules by a query string, matching against ModuleId, DisplayName,
    /// and Description (case-insensitive). 🔎.
    /// </summary>
    /// <param name="query">The search query text.</param>
    /// <returns>Modules matching the query, or empty if none match.</returns>
    public IReadOnlyList<IWorkflowModule> SearchModules(string query);

    /// <summary>
    /// Subscribes an observer to receive notifications when modules are
    /// registered or unregistered. Returns an <see cref="IDisposable"/> that
    /// removes the subscription when disposed. 👀.
    /// </summary>
    /// <param name="observer">The observer to notify.</param>
    /// <returns>A disposable that unsubscribes the observer when disposed.</returns>
    /// <remarks>
    /// CopilotNote: Observer pattern instead of events — keeps things clean
    /// for cross-domain and prevents memory leaks via IDisposable! ✨
    /// Observers are invoked in registration order. Exceptions in one observer
    /// do NOT block others from receiving the notification~ 💖.
    /// </remarks>
    public IDisposable Subscribe(IModuleRegistryObserver observer);
}

/// <summary>
/// 👀 Observer interface for module registry change notifications.
/// Receives callbacks when modules are registered or unregistered.
/// </summary>
/// <remarks>
/// CopilotNote: Both methods are synchronous (fire-and-forget notifications).
/// If an observer throws, the exception is caught and logged — it won't
/// prevent other observers from receiving the notification~ 💖.
/// </remarks>
public interface IModuleRegistryObserver
{
    /// <summary>
    /// Called after a module is successfully registered. ➕🔔.
    /// </summary>
    /// <param name="module">The module that was registered.</param>
    public void OnModuleRegistered(IWorkflowModule module);

    /// <summary>
    /// Called after a module is successfully unregistered. ➖🔔.
    /// </summary>
    /// <param name="moduleId">The ID of the module that was removed.</param>
    /// <param name="module">The module instance that was removed.</param>
    public void OnModuleUnregistered(string moduleId, IWorkflowModule module);
}
