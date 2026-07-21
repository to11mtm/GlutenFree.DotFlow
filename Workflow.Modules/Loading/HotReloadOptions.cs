// <copyright file="HotReloadOptions.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Loading;

using System.Collections.Generic;

/// <summary>
/// ⚙️ Phase 2.8.3 — Hot-reload configuration (bound from <c>Modules:HotReload</c>)~ ✨.
/// </summary>
public sealed class HotReloadOptions
{
    /// <summary>The configuration section name~ 📇.</summary>
    public const string SectionName = "Modules:HotReload";

    /// <summary>
    /// Gets or sets a value indicating whether hot-reload is enabled. **Off by default** (D8) — the
    /// watcher does not run unless this is set.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether loose-DLL dev folders are watched in addition to the
    /// installed-packages root (Q4). Only honoured when <see cref="Enabled"/> is true.
    /// </summary>
    public bool WatchLooseDlls { get; set; }

    /// <summary>Gets or sets the loose-DLL dev folders to watch when <see cref="WatchLooseDlls"/> is true.</summary>
    public IList<string> LooseDllPaths { get; set; } = new List<string>();

    /// <summary>Gets or sets the debounce window in milliseconds (default 500).</summary>
    public int DebounceMs { get; set; } = 500;
}
