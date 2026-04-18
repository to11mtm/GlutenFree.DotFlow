// <copyright file="IModuleLoader.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Loading;

using System;
using System.Collections.Generic;
using Workflow.Modules.Abstractions;

/// <summary>
/// 🚀 Service for dynamically loading <see cref="IWorkflowModule"/> implementations
/// from assemblies on disk at runtime, using isolated
/// <see cref="System.Runtime.Loader.AssemblyLoadContext"/> instances for plugin-style
/// extensibility and safe unloading~ ✨
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: Each loaded assembly gets its own collectible AssemblyLoadContext.
/// "Shared" host assemblies (Workflow.Core, Workflow.Modules, Microsoft.Extensions.*)
/// are resolved from the host context to ensure type-identity — this prevents the
/// classic plugin problem where <c>IWorkflowModule</c> from the plugin is considered
/// a DIFFERENT type than the host's version~ 💖
/// </para>
/// <para>
/// Modules discovered in a loaded assembly are automatically registered into the
/// <see cref="IModuleRegistry"/> provided at loader construction time.
/// </para>
/// </remarks>
public interface IModuleLoader
{
    /// <summary>
    /// Loads a single assembly from the given file path, discovers all
    /// <see cref="IWorkflowModule"/> implementations, and registers them.
    /// Returns a <see cref="ModuleLoadResult"/> describing what was loaded~ 📦
    /// </summary>
    /// <param name="assemblyPath">The absolute path to the assembly DLL to load.</param>
    /// <returns>
    /// A <see cref="ModuleLoadResult"/> with the loaded modules and any errors.
    /// <see cref="ModuleLoadResult.Success"/> is false if the assembly could not be
    /// loaded at all (invalid path, missing deps, etc.)~ 🎯
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="assemblyPath"/> is null or empty.
    /// </exception>
    public ModuleLoadResult LoadFromAssembly(string assemblyPath);

    /// <summary>
    /// Scans a directory for <c>*.dll</c> files and attempts to load each one,
    /// returning one <see cref="ModuleLoadResult"/> per DLL file found~ 📁
    /// </summary>
    /// <param name="directoryPath">The directory to scan for DLL files.</param>
    /// <returns>
    /// A list of results, one per DLL found. Empty list if directory is empty or missing.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="directoryPath"/> is null or empty.
    /// </exception>
    /// <remarks>
    /// CopilotNote: All DLLs are attempted. A DLL that contains no modules returns a
    /// result with empty <see cref="ModuleLoadResult.LoadedModules"/> — that is NOT
    /// an error. Only file-system / load-time failures produce errors~ 💖
    /// </remarks>
    public IReadOnlyList<ModuleLoadResult> LoadFromDirectory(string directoryPath);

    /// <summary>
    /// Unregisters all modules from the given assembly path and unloads its
    /// <see cref="System.Runtime.Loader.AssemblyLoadContext"/>. Returns
    /// <c>true</c> if the assembly was tracked and unloaded, <c>false</c> if it
    /// was never loaded~ 🗑️
    /// </summary>
    /// <param name="assemblyPath">The path that was originally passed to
    /// <see cref="LoadFromAssembly"/>.</param>
    /// <returns><c>true</c> if unloaded, <c>false</c> if not found.</returns>
    /// <remarks>
    /// CopilotNote: The ALC unload is initiated synchronously but GC collection
    /// happens asynchronously. The registered modules are removed from the registry
    /// immediately~ 💖
    /// </remarks>
    public bool UnloadAssembly(string assemblyPath);

    /// <summary>
    /// Returns the absolute paths of all assemblies currently tracked by this loader~ 📋
    /// </summary>
    /// <returns>Read-only list of tracked assembly paths.</returns>
    public IReadOnlyList<string> GetLoadedAssemblies();
}

/// <summary>
/// 📦 Represents the result of a single assembly load attempt by
/// <see cref="IModuleLoader"/>. Contains the modules that were loaded,
/// any errors that occurred, and whether the overall load was successful~ ✨
/// </summary>
/// <param name="AssemblyPath">The file path of the assembly that was loaded (or attempted).</param>
/// <param name="LoadedModules">
/// The modules successfully discovered and registered from this assembly.
/// May be empty if no valid modules were found (not an error on its own!).
/// </param>
/// <param name="Errors">
/// Any errors that occurred during loading, instantiation, or validation.
/// Populated even on partial success — some modules may load while others fail.
/// </param>
/// <param name="Success">
/// <c>true</c> if the assembly itself was loaded successfully (even if no modules
/// were found or some modules failed validation). <c>false</c> only when the
/// assembly file could not be loaded at all~ 🎯
/// </param>
/// <remarks>
/// CopilotNote: Success = the assembly was loadable. Empty LoadedModules ≠ failure!
/// Errors can exist alongside Success=true (partial loads where some modules failed).
/// Only Success=false means the whole assembly couldn't be loaded at all~ 💖
/// </remarks>
public record ModuleLoadResult(
    string AssemblyPath,
    IReadOnlyList<IWorkflowModule> LoadedModules,
    IReadOnlyList<string> Errors,
    bool Success)
{
    /// <summary>
    /// Creates a successful load result with the given loaded modules and optional errors~ ✅
    /// </summary>
    /// <param name="assemblyPath">The assembly file path.</param>
    /// <param name="loadedModules">The successfully registered modules.</param>
    /// <param name="errors">Any non-fatal errors (e.g., skipped invalid modules).</param>
    /// <returns>A successful <see cref="ModuleLoadResult"/>.</returns>
    public static ModuleLoadResult Ok(
        string assemblyPath,
        IReadOnlyList<IWorkflowModule> loadedModules,
        IReadOnlyList<string>? errors = null)
        => new(assemblyPath, loadedModules, errors ?? Array.Empty<string>(), Success: true);

    /// <summary>
    /// Creates a failed load result when the assembly itself could not be loaded~ ❌
    /// </summary>
    /// <param name="assemblyPath">The assembly file path that failed.</param>
    /// <param name="errors">The errors explaining why the load failed.</param>
    /// <returns>A failed <see cref="ModuleLoadResult"/>.</returns>
    public static ModuleLoadResult Fail(
        string assemblyPath,
        IReadOnlyList<string> errors)
        => new(assemblyPath, Array.Empty<IWorkflowModule>(), errors, Success: false);
}

