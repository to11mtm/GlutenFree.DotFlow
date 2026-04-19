// <copyright file="InMemoryModuleRegistry.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules;

using System;
using System.Collections.Concurrent;
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
/// CopilotNote: Thread-safe via ConcurrentDictionary and lock on observer list.
/// Observers are invoked in registration order. Exceptions in one observer
/// do NOT block others — each is wrapped in try/catch. UwU ✨.
/// </para>
/// <para>
/// Phase 1.4.3: ModuleValidator is wired in — modules are validated at
/// registration time. Use <c>skipValidation: true</c> to bypass for testing~ 🧪.
/// </para>
/// </remarks>
public class InMemoryModuleRegistry : IModuleRegistry
{
    private readonly ConcurrentDictionary<string, IWorkflowModule> _modules = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IModuleRegistryObserver> _observers = new();
    private readonly object _observerLock = new();
    private readonly ILogger _logger;
    private readonly ModuleValidator _validator = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryModuleRegistry"/> class
    /// with an optional logger~ 🌸.
    /// </summary>
    /// <param name="logger">Optional logger for observer error reporting.</param>
    public InMemoryModuleRegistry(ILogger<InMemoryModuleRegistry>? logger = null)
    {
        _logger = logger ?? (ILogger)NullLogger.Instance;
    }

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

    /// <summary>
    /// Registers a module instance. Validates the module first unless skipped. ➕.
    /// </summary>
    /// <param name="module">The module to register.</param>
    /// <param name="allowOverwrite">If true, silently overwrites an existing module with the same ID.</param>
    /// <param name="skipValidation">If true, skips ModuleValidator checks (useful for testing). 🧪.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the module fails validation or when a duplicate is found and
    /// <paramref name="allowOverwrite"/> is false.
    /// </exception>
    public void RegisterModule(IWorkflowModule module, bool allowOverwrite = false, bool skipValidation = false)
    {
        if (module == null)
        {
            throw new ArgumentNullException(nameof(module));
        }

        // Validate the module before registration (unless explicitly skipped)~ ✅
        if (!skipValidation)
        {
            var validation = _validator.Validate(module);
            if (!validation.IsValid)
            {
                var errorMessages = string.Join("; ", validation.Errors.Select(e => e.ToString()));
                throw new InvalidOperationException(
                    $"Module '{module.ModuleId}' failed validation: {errorMessages}");
            }
        }

        if (!allowOverwrite && _modules.ContainsKey(module.ModuleId))
        {
            throw new InvalidOperationException(
                $"Module '{module.ModuleId}' is already registered. Use allowOverwrite: true to replace it~ 💔");
        }

        _modules[module.ModuleId] = module;
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

        // Instantiate via DI if services available, otherwise plain Activator~ 🏭
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
        if (_modules.TryRemove(moduleId, out var removedModule))
        {
            NotifyUnregistered(moduleId, removedModule);
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public bool HasModule(string moduleId)
    {
        return _modules.ContainsKey(moduleId);
    }

    /// <inheritdoc />
    public IReadOnlyList<IWorkflowModule> GetModulesByCategory(string category)
    {
        return _modules.Values
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

        return _modules.Values
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

    /// <summary>
    /// Notifies all observers that a module was registered. 🔔➕
    /// Each observer is wrapped in try/catch — one failure doesn't block others~ 💖.
    /// </summary>
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

    /// <summary>
    /// Notifies all observers that a module was unregistered. 🔔➖.
    /// </summary>
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

    /// <summary>
    /// Removes an observer from the subscription list. Called by <see cref="ObserverSubscription.Dispose"/>~ 🧹.
    /// </summary>
    private void RemoveObserver(IModuleRegistryObserver observer)
    {
        lock (_observerLock)
        {
            _observers.Remove(observer);
        }
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
