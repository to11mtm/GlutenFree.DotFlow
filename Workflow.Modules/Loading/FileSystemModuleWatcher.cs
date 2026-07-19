// <copyright file="FileSystemModuleWatcher.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Loading;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// 📂 Phase 2.8.3 — Watches directories for <c>*.dll</c>/<c>*.wfmod</c> changes using
/// <see cref="FileSystemWatcher"/>, coalescing rapid create/change/rename storms via a per-path
/// debounce timer before notifying observers~ ✨.
/// </summary>
/// <remarks>
/// The raw file-system event handlers funnel into <see cref="NotifyChanged"/>, which owns the
/// debounce logic — so the coalescing behaviour is directly unit-testable without real FS events~ 🌸.
/// </remarks>
public sealed class FileSystemModuleWatcher : IModuleWatcher
{
    private static readonly string[] Patterns = { "*.dll", "*.wfmod" };

    private readonly TimeSpan debounce;
    private readonly ILogger logger;
    private readonly List<FileSystemWatcher> watchers = new();
    private readonly List<IModuleChangeObserver> observers = new();
    private readonly Dictionary<string, Timer> pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly object gate = new();
    private bool disposed;

    /// <summary>Initializes a new instance of the <see cref="FileSystemModuleWatcher"/> class~ 📂.</summary>
    /// <param name="debounceMs">The debounce window in milliseconds (default 500).</param>
    /// <param name="logger">Optional logger.</param>
    public FileSystemModuleWatcher(int debounceMs = 500, ILogger<FileSystemModuleWatcher>? logger = null)
    {
        this.debounce = TimeSpan.FromMilliseconds(Math.Max(1, debounceMs));
        this.logger = logger ?? (ILogger)NullLogger.Instance;
    }

    /// <inheritdoc/>
    public void Watch(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        Directory.CreateDirectory(directory);

        lock (this.gate)
        {
            foreach (var pattern in Patterns)
            {
                var fsw = new FileSystemWatcher(directory, pattern)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
                    EnableRaisingEvents = true,
                };

                fsw.Changed += this.OnRawChange;
                fsw.Created += this.OnRawChange;
                fsw.Renamed += this.OnRawChange;
                this.watchers.Add(fsw);
            }
        }

        this.logger.LogInformation("📂 Watching '{Directory}' for module changes~", directory);
    }

    /// <inheritdoc/>
    public void Stop()
    {
        lock (this.gate)
        {
            foreach (var fsw in this.watchers)
            {
                fsw.EnableRaisingEvents = false;
                fsw.Dispose();
            }

            this.watchers.Clear();

            foreach (var timer in this.pending.Values)
            {
                timer.Dispose();
            }

            this.pending.Clear();
        }
    }

    /// <inheritdoc/>
    public IDisposable Subscribe(IModuleChangeObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        lock (this.gate)
        {
            this.observers.Add(observer);
        }

        return new Unsubscriber(this, observer);
    }

    /// <summary>
    /// Records a change for a path and (re)starts its debounce timer. Public so the debounce/coalesce
    /// behaviour is directly testable~ 🔔.
    /// </summary>
    /// <param name="path">The changed file path.</param>
    public void NotifyChanged(string path)
    {
        if (this.disposed)
        {
            return;
        }

        lock (this.gate)
        {
            if (this.pending.TryGetValue(path, out var existing))
            {
                existing.Change(this.debounce, Timeout.InfiniteTimeSpan);
                return;
            }

            var timer = new Timer(_ => this.Fire(path), null, this.debounce, Timeout.InfiniteTimeSpan);
            this.pending[path] = timer;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        this.disposed = true;
        this.Stop();
    }

    private void OnRawChange(object sender, FileSystemEventArgs e) => this.NotifyChanged(e.FullPath);

    private void Fire(string path)
    {
        IModuleChangeObserver[] snapshot;
        lock (this.gate)
        {
            if (this.pending.TryGetValue(path, out var timer))
            {
                timer.Dispose();
                this.pending.Remove(path);
            }

            snapshot = this.observers.ToArray();
        }

        foreach (var observer in snapshot)
        {
            try
            {
                observer.OnModuleFileChanged(path);
            }
            catch (Exception ex)
            {
                this.logger.LogWarning(ex, "⚠️ Module change observer threw for '{Path}'~", path);
            }
        }
    }

    private sealed class Unsubscriber : IDisposable
    {
        private readonly FileSystemModuleWatcher owner;
        private readonly IModuleChangeObserver observer;

        public Unsubscriber(FileSystemModuleWatcher owner, IModuleChangeObserver observer)
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
