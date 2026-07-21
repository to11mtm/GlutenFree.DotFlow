// <copyright file="ModuleStateStoreFactory.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.State;

using System;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// 🏭 Phase 2.8.2 — Selects the module state store per <c>Modules:StateStore</c> (Q2). Chooses the
/// file store by default; chooses the repository store when requested **and** a backing persistence
/// seam is available, otherwise falls back to the file store with a warning~ ✨.
/// </summary>
public static class ModuleStateStoreFactory
{
    /// <summary>The file-store selector value~ 🗂️.</summary>
    public const string FileMode = "file";

    /// <summary>The repository-store selector value~ 🗄️.</summary>
    public const string RepositoryMode = "repository";

    /// <summary>Creates the configured state store, applying the repository→file fallback~ 🏭.</summary>
    /// <param name="mode">The configured mode (<c>file</c>/<c>repository</c>; null/blank → file).</param>
    /// <param name="stateFilePath">The absolute path for the file store.</param>
    /// <param name="persistence">The optional backing persistence seam (required for repository mode).</param>
    /// <param name="logger">Optional logger for the fallback warning.</param>
    /// <returns>The selected <see cref="IModuleStateStore"/>.</returns>
    public static IModuleStateStore Create(
        string? mode,
        string stateFilePath,
        IModuleStatePersistence? persistence,
        ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(stateFilePath);
        logger ??= NullLogger.Instance;

        var selected = (mode ?? FileMode).Trim().ToLowerInvariant();
        if (selected == RepositoryMode)
        {
            if (persistence is not null)
            {
                return new RepositoryModuleStateStore(persistence);
            }

            logger.LogWarning(
                "🗄️ Modules:StateStore=repository was requested but no persistence backing is configured — falling back to the file store~");
        }

        return new FileModuleStateStore(stateFilePath);
    }

    /// <summary>Computes the default state file path under a packages root~ 📁.</summary>
    /// <param name="packagesPath">The packages root directory.</param>
    /// <returns>The absolute <c>state.json</c> path.</returns>
    public static string DefaultStateFilePath(string packagesPath)
        => Path.GetFullPath(Path.Combine(packagesPath, "state.json"));
}
