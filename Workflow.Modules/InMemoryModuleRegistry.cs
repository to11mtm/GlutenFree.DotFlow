// <copyright file="InMemoryModuleRegistry.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules;

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Validation;

/// <summary>
/// 🗂️ In-memory implementation of the module registry with category lookup,
/// search, type-based registration, observer notifications, module validation,
/// and duplicate registration policy support~ 💖.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: Thread-safe via a per-instance lock. Observers are invoked in
/// registration order; exceptions in one observer do NOT block others~ ✨.
/// </para>
/// <para>
/// Phase 2.8.2: storage is keyed by <c>(moduleId, version)</c> to support side-by-side
/// versioning + enabled/disabled state. The single-arg <see cref="GetModule(string)"/>,
/// <see cref="GetAllModules"/>, category, and search APIs preserve their pre-2.8 semantics
/// by resolving the **latest enabled** version per id~ 🔢.
/// </para>
/// </remarks>
public class InMemoryModuleRegistry : IModuleRegistry
{
    private readonly Dictionary<string, List<ModuleEntry>> _modules = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private readonly List<IModuleRegistryObserver> _observers = new();
    private readonly object _observerLock = new();
    private readonly ILogger _logger;
    private readonly ModuleValidator _validator = new();
    private readonly bool _skipValidation;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryModuleRegistry"/> class
    /// with an optional logger~ 🌸.
    /// </summary>
    /// <param name="logger">Optional logger for observer error reporting.</param>
    /// <param name="skipValidation">When true, bypasses ModuleValidator for all registrations (useful in tests). 🧪.</param>
    public InMemoryModuleRegistry(ILogger<InMemoryModuleRegistry>? logger = null, bool skipValidation = false)
    {
        _logger = logger ?? (ILogger)NullLogger.Instance;
        _skipValidation = skipValidation;
    }

    /// <inheritdoc />
    public IReadOnlyList<IWorkflowModule> GetAllModules()
    {
        lock (_lock)
        {
            return _modules.Values
                .Select(LatestEnabled)
                .Where(m => m != null)
                .Cast<IWorkflowModule>()
                .ToList()
                .AsReadOnly();
        }
    }

    /// <inheritdoc />
    public IWorkflowModule? GetModule(string moduleId)
    {
        lock (_lock)
        {
            return _modules.TryGetValue(moduleId, out var entries) ? LatestEnabled(entries) : null;
        }
    }

    /// <inheritdoc />
    public IWorkflowModule? GetModule(string moduleId, Version? version)
    {
        if (version is null)
        {
            return GetModule(moduleId);
        }

        lock (_lock)
        {
            if (!_modules.TryGetValue(moduleId, out var entries))
            {
                return null;
            }

            var entry = entries.FirstOrDefault(e => e.Version == version);
            return entry is { Enabled: true } ? entry.Module : null;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<Version> GetModuleVersions(string moduleId)
    {
        lock (_lock)
        {
            return _modules.TryGetValue(moduleId, out var entries)
                ? entries.Select(e => e.Version).OrderBy(v => v).ToList().AsReadOnly()
                : (IReadOnlyList<Version>)Array.Empty<Version>();
        }
    }

    /// <inheritdoc />
    public bool SetModuleEnabled(string moduleId, Version version, bool enabled)
    {
        lock (_lock)
        {
            if (!_modules.TryGetValue(moduleId, out var entries))
            {
                return false;
            }

            var entry = entries.FirstOrDefault(e => e.Version == version);
            if (entry is null)
            {
                return false;
            }

            entry.Enabled = enabled;
            return true;
        }
    }

    /// <inheritdoc />
    public bool IsModuleEnabled(string moduleId, Version version)
    {
        lock (_lock)
        {
            return _modules.TryGetValue(moduleId, out var entries)
                && entries.Any(e => e.Version == version && e.Enabled);
        }
    }

    /// <summary>
    /// Registers a module instance. Validates the module first unless skipped. ➕.
    /// </summary>
    /// <param name="module">The module to register.</param>
    /// <param name="allowOverwrite">If true, silently overwrites an existing module with the same ID + version.</param>
    /// <param name="skipValidation">If true, skips ModuleValidator checks (useful for testing). 🧪.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the module fails validation or when a duplicate id+version is found and
    /// <paramref name="allowOverwrite"/> is false.
    /// </exception>
    public void RegisterModule(IWorkflowModule module, bool allowOverwrite = false, bool skipValidation = false)
    {
        if (module == null)
        {
            throw new ArgumentNullException(nameof(module));
        }

        if (!skipValidation && !_skipValidation)
        {
            var validation = _validator.Validate(module);
            if (!validation.IsValid)
            {
                var errorMessages = string.Join("; ", validation.Errors.Select(e => e.ToString()));
                throw new InvalidOperationException(
                    $"Module '{module.ModuleId}' failed validation: {errorMessages}");
            }
        }

        lock (_lock)
        {
            if (!_modules.TryGetValue(module.ModuleId, out var entries))
            {
                entries = new List<ModuleEntry>();
                _modules[module.ModuleId] = entries;
            }

            var existing = entries.FirstOrDefault(e => e.Version == module.Version);
            if (existing is not null)
            {
                if (!allowOverwrite)
                {
                    throw new InvalidOperationException(
                        $"Module '{module.ModuleId}' v{module.Version} is already registered. Use allowOverwrite: true to replace it~ 💔");
                }

                existing.Module = module;
                existing.Enabled = true;
            }
            else
            {
                entries.Add(new ModuleEntry(module));
            }
        }

        NotifyRegistered(module);
    }

    /// <inheritdoc />
    void IModuleRegistry.RegisterModule(IWorkflowModule module, bool allowOverwrite)
    {
        RegisterModule(module, allowOverwrite, skipValidation: false);
    }

    /// <inheritdoc />
    public void RegisterModule(Type moduleType, IServiceProvider? services = null, bool allowOverwrite = false)
    {
        if (moduleType == null)
        {
            throw new ArgumentNullException(nameof(moduleType));
        }

        if (!typeof(IWorkflowModule).IsAssignableFrom(moduleType))
        {
            throw new ArgumentException(
                $"Type '{moduleType.FullName}' does not implement IWorkflowModule! uwu~ 💔",
                nameof(moduleType));
        }

        IWorkflowModule module;
        if (services != null)
        {
            module = (IWorkflowModule)ActivatorUtilities.CreateInstance(services, moduleType);
        }
        else
        {
            module = (IWorkflowModule)(Activator.CreateInstance(moduleType)
                ?? throw new InvalidOperationException(
                    $"Failed to create instance of '{moduleType.FullName}'~ 💔"));
        }

        RegisterModule(module, allowOverwrite);
    }

    /// <inheritdoc />
    public bool UnregisterModule(string moduleId)
    {
        List<ModuleEntry>? removed;
        lock (_lock)
        {
            if (!_modules.TryGetValue(moduleId, out removed))
            {
                return false;
            }

            _modules.Remove(moduleId);
        }

        foreach (var entry in removed)
        {
            NotifyUnregistered(moduleId, entry.Module);
        }

        return true;
    }

    /// <summary>
    /// 🔢 Phase 2.8.2 — Unregisters a single version of a module. Removes the id entirely when its
    /// last version is removed~ ✨.
    /// </summary>
    /// <param name="moduleId">The module ID.</param>
    /// <param name="version">The version to remove.</param>
    /// <returns><c>true</c> when the version was found and removed.</returns>
    public bool UnregisterModule(string moduleId, Version version)
    {
        ModuleEntry? removed;
        lock (_lock)
        {
            if (!_modules.TryGetValue(moduleId, out var entries))
            {
                return false;
            }

            removed = entries.FirstOrDefault(e => e.Version == version);
            if (removed is null)
            {
                return false;
            }

            entries.Remove(removed);
            if (entries.Count == 0)
            {
                _modules.Remove(moduleId);
            }
        }

        NotifyUnregistered(moduleId, removed.Module);
        return true;
    }

    /// <inheritdoc />
    public bool HasModule(string moduleId)
    {
        lock (_lock)
        {
            return _modules.ContainsKey(moduleId);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<IWorkflowModule> GetModulesByCategory(string category)
    {
        return GetAllModules()
            .Where(m => m.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
            .ToList()
            .AsReadOnly();
    }

    /// <inheritdoc />
    public IReadOnlyList<IWorkflowModule> SearchModules(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<IWorkflowModule>();
        }

        return GetAllModules()
            .Where(m =>
                m.ModuleId.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                m.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                m.Description.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList()
            .AsReadOnly();
    }

    /// <inheritdoc />
    public IDisposable Subscribe(IModuleRegistryObserver observer)
    {
        if (observer == null)
        {
            throw new ArgumentNullException(nameof(observer));
        }

        lock (_observerLock)
        {
            _observers.Add(observer);
        }

        return new ObserverSubscription(this, observer);
    }

    /// <summary>Resolves the latest enabled version's module from an entry list~ 🔢.</summary>
    private static IWorkflowModule? LatestEnabled(List<ModuleEntry> entries)
        => entries
            .Where(e => e.Enabled)
            .OrderByDescending(e => e.Version)
            .Select(e => e.Module)
            .FirstOrDefault();

    private void NotifyRegistered(IWorkflowModule module)
    {
        List<IModuleRegistryObserver> snapshot;
        lock (_observerLock)
        {
            snapshot = new List<IModuleRegistryObserver>(_observers);
        }

        foreach (var observer in snapshot)
        {
            try
            {
                observer.OnModuleRegistered(module);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "⚠️ Observer {ObserverType} threw during OnModuleRegistered for '{ModuleId}'~ uwu",
                    observer.GetType().Name,
                    module.ModuleId);
            }
        }
    }

    private void NotifyUnregistered(string moduleId, IWorkflowModule module)
    {
        List<IModuleRegistryObserver> snapshot;
        lock (_observerLock)
        {
            snapshot = new List<IModuleRegistryObserver>(_observers);
        }

        foreach (var observer in snapshot)
        {
            try
            {
                observer.OnModuleUnregistered(moduleId, module);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "⚠️ Observer {ObserverType} threw during OnModuleUnregistered for '{ModuleId}'~ uwu",
                    observer.GetType().Name,
                    moduleId);
            }
        }
    }

    private void RemoveObserver(IModuleRegistryObserver observer)
    {
        lock (_observerLock)
        {
            _observers.Remove(observer);
        }
    }

    /// <summary>A single registered (version, module, enabled) entry~ 🗂️.</summary>
    private sealed class ModuleEntry
    {
        public ModuleEntry(IWorkflowModule module)
        {
            Module = module;
            Version = module.Version;
            Enabled = true;
        }

        public Version Version { get; }

        public IWorkflowModule Module { get; set; }

        public bool Enabled { get; set; }
    }

    /// <summary>
    /// 🎀 Disposable subscription handle — removes the observer on dispose to prevent leaks!.
    /// </summary>
    private sealed class ObserverSubscription : IDisposable
    {
        private readonly InMemoryModuleRegistry _registry;
        private readonly IModuleRegistryObserver _observer;
        private bool _disposed;

        public ObserverSubscription(InMemoryModuleRegistry registry, IModuleRegistryObserver observer)
        {
            _registry = registry;
            _observer = observer;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _registry.RemoveObserver(_observer);
            }
        }
    }
}
