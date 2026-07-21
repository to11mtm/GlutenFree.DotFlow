// <copyright file="IModuleWatcher.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Loading;

using System;

/// <summary>
/// 👀 Phase 2.8.3 — Watches directories for module file changes (<c>*.dll</c>/<c>*.wfmod</c>) and
/// notifies observers after debouncing. Uses the observer pattern (no C# events) to match the
/// registry convention~ ✨.
/// </summary>
public interface IModuleWatcher : IDisposable
{
    /// <summary>Starts watching a directory for module changes~ 📂.</summary>
    /// <param name="directory">The directory to watch (created implicitly if missing).</param>
    void Watch(string directory);

    /// <summary>Stops all watching and notifications~ 🛑.</summary>
    void Stop();

    /// <summary>Subscribes an observer to debounced change notifications~ 👀.</summary>
    /// <param name="observer">The observer.</param>
    /// <returns>A disposable that unsubscribes on dispose.</returns>
    IDisposable Subscribe(IModuleChangeObserver observer);
}

/// <summary>
/// 👀 Phase 2.8.3 — Receives debounced module file-change notifications~ ✨.
/// </summary>
public interface IModuleChangeObserver
{
    /// <summary>Called (once per debounce window) when a module file changes~ 🔔.</summary>
    /// <param name="path">The absolute path of the changed file.</param>
    void OnModuleFileChanged(string path);
}
