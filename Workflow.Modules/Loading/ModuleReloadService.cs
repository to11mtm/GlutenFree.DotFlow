// <copyright file="ModuleReloadService.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Loading;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Workflow.Modules.Abstractions;

/// <summary>
/// 🔄 Phase 2.8.3 — Reloads a changed module assembly safely: it never unloads while executions are
/// in flight, deferring (with retry) until the active count drains. Publishes a
/// <see cref="ModuleReloadedInfo"/> to observers on success (the host bridges this to the Akka
/// EventStream)~ ✨.
/// </summary>
public sealed class ModuleReloadService
{
    private readonly IModuleLoader loader;
    private readonly IModuleRegistry registry;
    private readonly IActiveExecutionTracker tracker;
    private readonly ILogger logger;
    private readonly List<IModuleReloadObserver> observers = new();
    private readonly object gate = new();
    private readonly TimeSpan pollInterval;
    private readonly TimeSpan maxWait;

    /// <summary>Initializes a new instance of the <see cref="ModuleReloadService"/> class~ 🔄.</summary>
    /// <param name="loader">The assembly loader.</param>
    /// <param name="registry">The module registry.</param>
    /// <param name="tracker">The active-execution tracker (unload-safety gate).</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="pollIntervalMs">Deferral retry interval in ms (default 100).</param>
    /// <param name="maxWaitMs">Maximum time to wait for executions to drain in ms (default 30000).</param>
    public ModuleReloadService(
        IModuleLoader loader,
        IModuleRegistry registry,
        IActiveExecutionTracker tracker,
        ILogger<ModuleReloadService>? logger = null,
        int pollIntervalMs = 100,
        int maxWaitMs = 30000)
    {
        this.loader = loader ?? throw new ArgumentNullException(nameof(loader));
        this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
        this.tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        this.logger = logger ?? (ILogger)NullLogger.Instance;
        this.pollInterval = TimeSpan.FromMilliseconds(Math.Max(1, pollIntervalMs));
        this.maxWait = TimeSpan.FromMilliseconds(Math.Max(1, maxWaitMs));
    }

    /// <summary>Subscribes an observer to reload notifications~ 👀.</summary>
    /// <param name="observer">The observer.</param>
    /// <returns>A disposable that unsubscribes on dispose.</returns>
    public IDisposable Subscribe(IModuleReloadObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        lock (this.gate)
        {
            this.observers.Add(observer);
        }

        return new Unsubscriber(this, observer);
    }

    /// <summary>Reloads the module assembly at <paramref name="assemblyPath"/> when it is safe to do so~ 🔄.</summary>
    /// <param name="assemblyPath">The absolute assembly path to reload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The reload outcome.</returns>
    public async Task<ModuleReloadOutcome> ReloadAsync(string assemblyPath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(assemblyPath);

        // 🛟 Defer while executions are in flight (unload safety, D8)~
        var deadline = DateTimeOffset.UtcNow + this.maxWait;
        var deferred = false;
        while (this.tracker.HasActiveExecutions)
        {
            deferred = true;
            if (DateTimeOffset.UtcNow >= deadline)
            {
                this.logger.LogWarning("🔄 Reload of '{Path}' abandoned — executions did not drain within the max wait~", assemblyPath);
                return ModuleReloadOutcome.TimedOut;
            }

            await Task.Delay(this.pollInterval, ct).ConfigureAwait(false);
        }

        // 🔌 Unload (if tracked) then reload~
        this.loader.UnloadAssembly(assemblyPath);
        var result = this.loader.LoadFromAssembly(assemblyPath);
        if (!result.Success)
        {
            this.logger.LogWarning("🔄 Reload of '{Path}' failed: {Errors}~", assemblyPath, string.Join("; ", result.Errors));
            return ModuleReloadOutcome.Failed;
        }

        foreach (var module in result.LoadedModules)
        {
            this.Notify(new ModuleReloadedInfo(module.ModuleId, module.Version, assemblyPath));
        }

        this.logger.LogInformation("🔄 Reloaded {Count} module(s) from '{Path}'~", result.LoadedModules.Count, assemblyPath);
        return deferred ? ModuleReloadOutcome.ReloadedAfterDeferral : ModuleReloadOutcome.Reloaded;
    }

    private void Notify(ModuleReloadedInfo info)
    {
        IModuleReloadObserver[] snapshot;
        lock (this.gate)
        {
            snapshot = this.observers.ToArray();
        }

        foreach (var observer in snapshot)
        {
            try
            {
                observer.OnModuleReloaded(info);
            }
            catch (Exception ex)
            {
                this.logger.LogWarning(ex, "⚠️ Reload observer threw for '{ModuleId}'~", info.ModuleId);
            }
        }
    }

    private sealed class Unsubscriber : IDisposable
    {
        private readonly ModuleReloadService owner;
        private readonly IModuleReloadObserver observer;

        public Unsubscriber(ModuleReloadService owner, IModuleReloadObserver observer)
        {
            this.owner = owner;
            this.observer = observer;
        }

        public void Dispose()
        {
            lock (this.owner.gate)
            {
                this.owner.observers.Remove(this.observer);
            }
        }
    }
}

/// <summary>
/// 🛟 Phase 2.8.3 — Reports whether the engine currently has in-flight executions (unload-safety
/// gate). The host implements this over the metrics active gauge / supervisor (D8)~ ✨.
/// </summary>
public interface IActiveExecutionTracker
{
    /// <summary>Gets a value indicating whether any executions are currently in flight~ 🛟.</summary>
    bool HasActiveExecutions { get; }
}

/// <summary>
/// 🔔 Phase 2.8.3 — Receives module reload notifications~ ✨.
/// </summary>
public interface IModuleReloadObserver
{
    /// <summary>Called after a module version is successfully reloaded~ 🔔.</summary>
    /// <param name="info">The reloaded module info.</param>
    void OnModuleReloaded(ModuleReloadedInfo info);
}

/// <summary>
/// 🔔 Phase 2.8.3 — Describes a reloaded module version~ ✨.
/// </summary>
/// <param name="ModuleId">The module id.</param>
/// <param name="Version">The reloaded version.</param>
/// <param name="AssemblyPath">The assembly path that was reloaded.</param>
public sealed record ModuleReloadedInfo(string ModuleId, Version Version, string AssemblyPath);

/// <summary>
/// 🔄 Phase 2.8.3 — The outcome of a reload attempt~ ✨.
/// </summary>
public enum ModuleReloadOutcome
{
    /// <summary>Reloaded immediately (no active executions).</summary>
    Reloaded,

    /// <summary>Reloaded after deferring until executions drained.</summary>
    ReloadedAfterDeferral,

    /// <summary>Executions did not drain within the max wait.</summary>
    TimedOut,

    /// <summary>The assembly failed to reload.</summary>
    Failed,
}
