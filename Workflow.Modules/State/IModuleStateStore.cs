// <copyright file="IModuleStateStore.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.State;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// 🗂️ Phase 2.8.2 — Pluggable persistence seam for module enabled/installed state. Two MVP
/// implementations ship: <see cref="FileModuleStateStore"/> (default) and
/// <see cref="RepositoryModuleStateStore"/> (optional, persistence-backed) — selected via
/// <c>Modules:StateStore</c> (Q2)~ ✨.
/// </summary>
public interface IModuleStateStore
{
    /// <summary>Loads the persisted state snapshot (empty when none exists)~ 📖.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The loaded snapshot.</returns>
    Task<ModuleStateSnapshot> LoadAsync(CancellationToken ct = default);

    /// <summary>Persists the state snapshot (write-through)~ 💾.</summary>
    /// <param name="snapshot">The snapshot to save.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when saved.</returns>
    Task SaveAsync(ModuleStateSnapshot snapshot, CancellationToken ct = default);
}

/// <summary>
/// 🗄️ Phase 2.8.2 — Backing store for <see cref="RepositoryModuleStateStore"/>: reads/writes the raw
/// state JSON. The host implements this over its persistence provider (e.g. blob store); when no
/// provider is configured the host falls back to the file store~ ✨.
/// </summary>
public interface IModuleStatePersistence
{
    /// <summary>Reads the persisted state JSON, or <c>null</c> when absent~ 📖.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The JSON string, or <c>null</c>.</returns>
    Task<string?> ReadAsync(CancellationToken ct = default);

    /// <summary>Writes the state JSON~ 💾.</summary>
    /// <param name="json">The JSON to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when written.</returns>
    Task WriteAsync(string json, CancellationToken ct = default);
}
