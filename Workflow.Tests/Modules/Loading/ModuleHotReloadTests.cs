// <copyright file="ModuleHotReloadTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.Loading;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Workflow.Modules;
using Workflow.Modules.Loading;
using Workflow.Tests.SampleModules;
using Xunit;

/// <summary>
/// 🔄 Phase 2.8.3 — Tests for the module watcher (debounce/coalesce) and the reload service (unload safety)~ ✨.
/// </summary>
public sealed class ModuleHotReloadTests : IDisposable
{
    private static readonly string SampleDir = Path.GetDirectoryName(typeof(SampleLogModule).Assembly.Location)!;
    private readonly string tempDir = Path.Combine(Path.GetTempPath(), "wfmod-reload-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(this.tempDir))
            {
                Directory.Delete(this.tempDir, recursive: true);
            }
        }
        catch
        {
            // best effort
        }
    }

    // ---------- Watcher ----------
    [Fact]
    public async Task Watcher_DllChange_FiresOnceAfterDebounce()
    {
        using var watcher = new FileSystemModuleWatcher(debounceMs: 100);
        var observer = new CountingObserver();
        watcher.Subscribe(observer);

        watcher.NotifyChanged("C:/plugins/x.dll");
        await Task.Delay(300);

        observer.Count.Should().Be(1);
        observer.LastPath.Should().Be("C:/plugins/x.dll");
    }

    [Fact]
    public async Task Watcher_RapidChanges_Coalesced()
    {
        using var watcher = new FileSystemModuleWatcher(debounceMs: 150);
        var observer = new CountingObserver();
        watcher.Subscribe(observer);

        for (var i = 0; i < 10; i++)
        {
            watcher.NotifyChanged("C:/plugins/x.dll");
            await Task.Delay(20);
        }

        await Task.Delay(300);
        observer.Count.Should().Be(1, "rapid changes within the debounce window coalesce into one notification");
    }

    [Fact]
    public async Task Watcher_Stop_StopsNotifications()
    {
        using var watcher = new FileSystemModuleWatcher(debounceMs: 100);
        var observer = new CountingObserver();
        watcher.Subscribe(observer);

        watcher.NotifyChanged("C:/plugins/x.dll");
        watcher.Stop();
        await Task.Delay(250);

        observer.Count.Should().Be(0, "Stop cancels pending debounced notifications");
    }

    // ---------- Reload ----------
    [Fact]
    public async Task Reload_NoActiveExecutions_ReloadsImmediately()
    {
        var (service, registry, dllPath) = this.NewReloadService(active: false);

        var outcome = await service.ReloadAsync(dllPath);

        outcome.Should().Be(ModuleReloadOutcome.Reloaded);
        registry.HasModule("sample.log").Should().BeTrue();
    }

    [Fact]
    public async Task Reload_ActiveExecutions_Deferred_ThenReloads()
    {
        var tracker = new ToggleTracker(activeUntilCall: 2);
        var (service, registry, dllPath) = this.NewReloadService(tracker);

        var outcome = await service.ReloadAsync(dllPath);

        outcome.Should().Be(ModuleReloadOutcome.ReloadedAfterDeferral);
        registry.HasModule("sample.log").Should().BeTrue();
    }

    [Fact]
    public async Task Reload_PublishesModuleReloadedEvent()
    {
        var (service, _, dllPath) = this.NewReloadService(active: false);
        var captured = new List<ModuleReloadedInfo>();
        service.Subscribe(new CapturingReloadObserver(captured));

        await service.ReloadAsync(dllPath);

        captured.Should().Contain(i => i.ModuleId == "sample.log");
    }

    [Fact]
    public async Task Reload_NewVersionOfModule_RegistryReflectsChange()
    {
        var (service, registry, dllPath) = this.NewReloadService(active: false);
        await service.ReloadAsync(dllPath);

        registry.GetModule("sample.log").Should().NotBeNull();
        registry.GetModule("sample.log")!.Version.Should().Be(new Version(1, 0, 0));
    }

    // ---------- Helpers ----------
    private (ModuleReloadService Service, InMemoryModuleRegistry Registry, string DllPath) NewReloadService(bool active)
        => this.NewReloadService(new ToggleTracker(active ? int.MaxValue : 0));

    private (ModuleReloadService Service, InMemoryModuleRegistry Registry, string DllPath) NewReloadService(IActiveExecutionTracker tracker)
    {
        Directory.CreateDirectory(this.tempDir);
        var dllPath = Path.Combine(this.tempDir, Path.GetFileName(typeof(SampleLogModule).Assembly.Location));
        foreach (var file in Directory.GetFiles(SampleDir, "*.dll"))
        {
            File.Copy(file, Path.Combine(this.tempDir, Path.GetFileName(file)), overwrite: true);
        }

        var depsPath = Path.ChangeExtension(typeof(SampleLogModule).Assembly.Location, ".deps.json");
        if (File.Exists(depsPath))
        {
            File.Copy(depsPath, Path.Combine(this.tempDir, Path.GetFileName(depsPath)), overwrite: true);
        }

        var registry = new InMemoryModuleRegistry();
        var loader = new AssemblyModuleLoader(registry);
        var service = new ModuleReloadService(loader, registry, tracker, pollIntervalMs: 20, maxWaitMs: 5000);
        return (service, registry, dllPath);
    }

    private sealed class CountingObserver : IModuleChangeObserver
    {
        private int count;

        public int Count => this.count;

        public string? LastPath { get; private set; }

        public void OnModuleFileChanged(string path)
        {
            Interlocked.Increment(ref this.count);
            this.LastPath = path;
        }
    }

    private sealed class CapturingReloadObserver : IModuleReloadObserver
    {
        private readonly List<ModuleReloadedInfo> sink;

        public CapturingReloadObserver(List<ModuleReloadedInfo> sink) => this.sink = sink;

        public void OnModuleReloaded(ModuleReloadedInfo info) => this.sink.Add(info);
    }

    private sealed class ToggleTracker : IActiveExecutionTracker
    {
        private int callsRemaining;

        public ToggleTracker(int activeUntilCall) => this.callsRemaining = activeUntilCall;

        public bool HasActiveExecutions
        {
            get
            {
                if (this.callsRemaining <= 0)
                {
                    return false;
                }

                this.callsRemaining--;
                return true;
            }
        }
    }
}
