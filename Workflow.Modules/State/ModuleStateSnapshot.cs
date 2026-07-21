// <copyright file="ModuleStateSnapshot.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.State;

using System;
using System.Collections.Generic;

/// <summary>
/// 🗂️ Phase 2.8.2 — A persisted snapshot of per-version module state (enabled flags + installed
/// package records)~ ✨.
/// </summary>
/// <param name="Modules">The recorded module states.</param>
public sealed record ModuleStateSnapshot(IReadOnlyList<ModuleStateRecord> Modules)
{
    /// <summary>An empty snapshot~ ✨.</summary>
    public static ModuleStateSnapshot Empty { get; } = new(Array.Empty<ModuleStateRecord>());
}

/// <summary>
/// 🗂️ Phase 2.8.2 — Recorded state for one module version~ ✨.
/// </summary>
/// <param name="ModuleId">The module id.</param>
/// <param name="Version">The module version (string form).</param>
/// <param name="Enabled">Whether the version is enabled.</param>
public sealed record ModuleStateRecord(string ModuleId, string Version, bool Enabled);
