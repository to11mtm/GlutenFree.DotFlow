// <copyright file="ModuleHotReloadHostedService.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.Modules;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Loading;
using Workflow.Modules.Packaging;

/// <summary>
/// 🔄 Phase 2.8.3 — Host wiring for module hot-reload. When <c>Modules:HotReload:Enabled=true</c>,
/// watches the installed-packages root (and loose-DLL dev folders when
/// <c>Modules:HotReload:WatchLooseDlls=true</c>), reloads changed assemblies safely via
/// <see cref="ModuleReloadService"/>, and bridges <see cref="ModuleReloadedInfo"/> to the Akka
/// EventStream. **Self-disables** (no watcher runs) when hot-reload is off~ ✨.
/// </summary>
public sealed class ModuleHotReloadHostedService : IHostedService, IModuleChangeObserver, IModuleReloadObserver
{
    private readonly HotReloadOptions options;
    private readonly ModulePackagingOptions packaging;
    private readonly IModuleLoader loader;
    private readonly IModuleRegistry registry;
    private readonly IActiveExecutionTracker tracker;
    private readonly ActorSystem actorSystem;
    private readonly ILogger<ModuleHotReloadHostedService> logger;
    private readonly ILoggerFactory loggerFactory;

    private FileSystemModuleWatcher? watcher;
    private ModuleReloadService? reloadService;
    private IDisposable? changeSubscription;
    private IDisposable? reloadSubscription;

    /// <summary>Initializes a new instance of the <see cref="ModuleHotReloadHostedService"/> class~ 🔄.</summary>
    /// <param name="configuration">The app configuration (binds <c>Modules:HotReload</c>).</param>
    /// <param name="packaging">The packaging options (packages root path).</param>
    /// <param name="loader">The module loader.</param>
    /// <param name="registry">The module registry.</param>
    /// <param name="tracker">The active-execution tracker (unload-safety gate).</param>
    /// <param name="actorSystem">The actor system (for EventStream publication).</param>
    /// <param name="loggerFactory">The logger factory.</param>
    public ModuleHotReloadHostedService(
        IConfiguration configuration,
        ModulePackagingOptions packaging,
        IModuleLoader loader,
        IModuleRegistry registry,
        IActiveExecutionTracker tracker,
        ActorSystem actorSystem,
        ILoggerFactory loggerFactory)
    {
        this.options = new HotReloadOptions();
        configuration.GetSection(HotReloadOptions.SectionName).Bind(this.options);
        this.packaging = packaging;
        this.loader = loader;
        this.registry = registry;
        this.tracker = tracker;
        this.actorSystem = actorSystem;
        this.loggerFactory = loggerFactory;
        this.logger = loggerFactory.CreateLogger<ModuleHotReloadHostedService>();
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!this.options.Enabled)
        {
            this.logger.LogDebug("🔄 Module hot-reload is disabled (Modules:HotReload:Enabled=false)~");
            return Task.CompletedTask;
        }

        this.reloadService = new ModuleReloadService(
            this.loader,
            this.registry,
            this.tracker,
            this.loggerFactory.CreateLogger<ModuleReloadService>());
        this.reloadSubscription = this.reloadService.Subscribe(this);

        this.watcher = new FileSystemModuleWatcher(
            this.options.DebounceMs,
            this.loggerFactory.CreateLogger<FileSystemModuleWatcher>());
        this.changeSubscription = this.watcher.Subscribe(this);

        var packagesRoot = Path.GetFullPath(this.packaging.PackagesPath);
        this.watcher.Watch(packagesRoot);

        if (this.options.WatchLooseDlls)
        {
            foreach (var dir in this.LooseWatchDirectories())
            {
                this.watcher.Watch(dir);
            }
        }

        this.logger.LogInformation("🔄 Module hot-reload enabled — watching '{Root}'~", packagesRoot);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        this.changeSubscription?.Dispose();
        this.reloadSubscription?.Dispose();
        this.watcher?.Dispose();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public void OnModuleFileChanged(string path)
    {
        if (this.reloadService is null)
        {
            return;
        }

        // Fire-and-forget the safe reload — the service defers while executions are in flight~
        _ = Task.Run(async () =>
        {
            try
            {
                await this.reloadService.ReloadAsync(path).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                this.logger.LogWarning(ex, "🔄 Hot-reload of '{Path}' failed~", path);
            }
        });
    }

    /// <inheritdoc/>
    public void OnModuleReloaded(ModuleReloadedInfo info)
    {
        // 🔔 Bridge the reload to the Akka EventStream so interested actors can react~
        this.actorSystem.EventStream.Publish(info);
        this.logger.LogInformation("🔄 Published ModuleReloaded for {ModuleId} v{Version}~", info.ModuleId, info.Version);
    }

    private IEnumerable<string> LooseWatchDirectories()
    {
        // Loose-DLL dev folders are declared under Modules:HotReload:LooseDllPaths (optional)~
        var configured = this.options.LooseDllPaths;
        var result = new List<string>();
        foreach (var dir in configured)
        {
            if (!string.IsNullOrWhiteSpace(dir))
            {
                result.Add(Path.GetFullPath(dir));
            }
        }

        return result;
    }
}
