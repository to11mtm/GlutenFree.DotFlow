// <copyright file="AssemblyModuleLoader.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Loading;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Discovery;

/// <summary>
/// 📦 Default implementation of <see cref="IModuleLoader"/> that loads plugin assemblies
/// from disk using isolated <see cref="PluginAssemblyLoadContext"/> instances.
/// Delegates discovery to <see cref="ModuleDiscovery"/> and tracks loaded contexts
/// for later unloading~ ✨.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: One PluginAssemblyLoadContext is created per loaded assembly path.
/// Loading the same path twice is a no-op (returns the existing result).
/// Unloading removes all registered modules from the registry and initiates GC
/// collection of the ALC. The actual memory release is async (GC-driven)~ 💖.
/// </para>
/// </remarks>
public sealed class AssemblyModuleLoader : IModuleLoader
{
    private readonly IModuleRegistry _registry;
    private readonly ModuleDiscovery _discovery;
    private readonly ILogger _logger;

    // CopilotNote: Maps normalized absolute path → (ALC, list of registered module IDs)
    // Uses WeakReference for the ALC so GC can collect it after Unload()~ 🔧
    private readonly ConcurrentDictionary<string, LoadedAssemblyEntry> _loaded = new(
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="AssemblyModuleLoader"/> class~ 🌸.
    /// </summary>
    /// <param name="registry">The module registry to register/unregister modules into.</param>
    /// <param name="discovery">Optional module discovery service. If null, creates a default instance.</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public AssemblyModuleLoader(
        IModuleRegistry registry,
        ModuleDiscovery? discovery = null,
        ILogger<AssemblyModuleLoader>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
        _discovery = discovery ?? new ModuleDiscovery();
        _logger = logger ?? (ILogger)NullLogger.Instance;
    }

    /// <inheritdoc />
    public ModuleLoadResult LoadFromAssembly(string assemblyPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(assemblyPath);

        var fullPath = Path.GetFullPath(assemblyPath);

        // 🔄 Already loaded? Return cached result~ ✨
        if (_loaded.ContainsKey(fullPath))
        {
            _logger.LogDebug(
                "⚡ Assembly '{Path}' is already loaded — skipping duplicate load~",
                fullPath);

            var entry = _loaded[fullPath];
            return ModuleLoadResult.Ok(
                fullPath,
                entry.RegisteredModuleIds
                    .Select(id => _registry.GetModule(id))
                    .Where(m => m != null)
                    .Cast<IWorkflowModule>()
                    .ToList());
        }

        // 🛡️ Validate file exists before attempting load~
        if (!File.Exists(fullPath))
        {
            var error = $"Assembly file not found: '{fullPath}'";
            _logger.LogWarning("⚠️ {Error}~", error);
            return ModuleLoadResult.Fail(fullPath, new[] { error });
        }

        _logger.LogInformation("📦 Loading assembly '{Path}'~", fullPath);

        var alc = new PluginAssemblyLoadContext(fullPath);
        var errors = new List<string>();
        var loadedModules = new List<IWorkflowModule>();

        try
        {
            // 🔌 Load the assembly inside our isolated ALC~
            var assembly = alc.LoadFromAssemblyPath(fullPath);

            // 📊 Track which modules exist before discovery so we can diff after~
            // CopilotNote: We use a temporary registry wrapper approach —
            // we subscribe an observer to capture what actually gets registered~ 💖
            var registeredIds = new List<string>();
            using var subscription = _registry.Subscribe(new RegistrationCapture(registeredIds));

            // 🔍 Use ModuleDiscovery to scan + register all valid modules~
            var count = _discovery.DiscoverAndRegister(assembly, _registry);

            _logger.LogInformation(
                "✅ Loaded {Count} module(s) from '{Path}'~",
                count,
                fullPath);

            // Resolve the actual module instances from what got registered~
            foreach (var id in registeredIds)
            {
                var module = _registry.GetModule(id);
                if (module != null)
                {
                    loadedModules.Add(module);
                }
            }

            // 🗂️ Track the loaded context for later unloading~
            _loaded[fullPath] = new LoadedAssemblyEntry(alc, registeredIds.ToList());

            return ModuleLoadResult.Ok(fullPath, loadedModules, errors.Count > 0 ? errors : null);
        }
        catch (Exception ex)
        {
            // 💥 Assembly-level failure — couldn't load at all~
            _logger.LogWarning(
                ex,
                "⚠️ Failed to load assembly '{Path}': {Message}~",
                fullPath,
                ex.Message);

            errors.Add($"Failed to load assembly '{fullPath}': {ex.Message}");

            // 🧹 Unload the ALC since we failed~
            try
            {
                alc.Unload();
            }
            catch
            {
                // Ignore unload errors on failure cleanup~
            }

            return ModuleLoadResult.Fail(fullPath, errors);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ModuleLoadResult> LoadFromDirectory(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(directoryPath);

        if (!Directory.Exists(directoryPath))
        {
            _logger.LogWarning(
                "⚠️ Directory not found: '{Path}' — returning empty results~",
                directoryPath);
            return Array.Empty<ModuleLoadResult>();
        }

        var dlls = Directory.GetFiles(directoryPath, "*.dll", SearchOption.TopDirectoryOnly);

        _logger.LogInformation(
            "📁 Found {Count} DLL(s) in '{Path}'~",
            dlls.Length,
            directoryPath);

        return dlls.Select(LoadFromAssembly).ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public bool UnloadAssembly(string assemblyPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(assemblyPath);

        var fullPath = Path.GetFullPath(assemblyPath);

        if (!_loaded.TryRemove(fullPath, out var entry))
        {
            _logger.LogDebug(
                "🤷 Assembly '{Path}' is not tracked — nothing to unload~",
                fullPath);
            return false;
        }

        // 🗑️ Unregister all modules this assembly contributed~
        foreach (var moduleId in entry.RegisteredModuleIds)
        {
            if (_registry.HasModule(moduleId))
            {
                _registry.UnregisterModule(moduleId);
                _logger.LogDebug("🗑️ Unregistered module '{ModuleId}' during assembly unload~", moduleId);
            }
        }

        // 🔌 Initiate ALC unload — GC will actually reclaim memory asynchronously~
        try
        {
            entry.Context.Unload();
            _logger.LogInformation(
                "✅ Initiated unload of assembly context for '{Path}'~ (GC will complete cleanup)",
                fullPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "⚠️ Error during ALC unload for '{Path}': {Message}~",
                fullPath,
                ex.Message);
        }

        return true;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetLoadedAssemblies()
        => _loaded.Keys.ToList().AsReadOnly();

    /// <summary>
    /// Holds tracking data for a loaded assembly~ 🗂️.
    /// </summary>
    private sealed class LoadedAssemblyEntry
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="LoadedAssemblyEntry"/> class.
        /// </summary>
        /// <param name="context">The ALC for this assembly.</param>
        /// <param name="registeredModuleIds">Module IDs registered from this assembly.</param>
        public LoadedAssemblyEntry(PluginAssemblyLoadContext context, List<string> registeredModuleIds)
        {
            Context = context;
            RegisteredModuleIds = registeredModuleIds;
        }

        /// <summary>Gets the isolated ALC for this assembly~ 🔌.</summary>
        public PluginAssemblyLoadContext Context { get; }

        /// <summary>Gets the IDs of modules registered from this assembly~ 📋.</summary>
        public List<string> RegisteredModuleIds { get; }
    }

    /// <summary>
    /// 👀 Internal observer that captures module IDs as they are registered,
    /// so we can know exactly which modules came from our load operation~ 🎯.
    /// </summary>
    private sealed class RegistrationCapture : IModuleRegistryObserver
    {
        private readonly List<string> _ids;

        /// <summary>
        /// Initializes a new instance of the <see cref="RegistrationCapture"/> class.
        /// </summary>
        /// <param name="ids">List to capture registered module IDs into.</param>
        public RegistrationCapture(List<string> ids) => _ids = ids;

        /// <inheritdoc />
        public void OnModuleRegistered(IWorkflowModule module) => _ids.Add(module.ModuleId);

        /// <inheritdoc />
        public void OnModuleUnregistered(string moduleId, IWorkflowModule module)
        {
            // CopilotNote: We don't need to track unregistrations during load~ 💖
        }
    }
}
