// <copyright file="IDbProviderRegistry.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Abstractions;

using System.Collections.Generic;

/// <summary>
/// 🗂️ Maps user-facing provider keys ("postgres", "sqlite") to linq2db <c>ProviderName</c> constants~ ✨.
/// </summary>
/// <remarks>
/// <para>
/// Provider keys are plain strings, not enums (D6) — plugins can DI-replace this registry
/// to add more providers without touching core enums. MySQL + SQL Server land in 2.4.a.P3~ 🌸.
/// </para>
/// <para>
/// CopilotNote: Phase 2.4.a.0 — the default impl is <c>DefaultDbProviderRegistry</c>
/// (hardcoded postgres/sqlite for V1). Both raw-SQL (2.4.a) and typed linq (2.4.b)
/// families resolve providers through this single registry~ 💖.
/// </para>
/// </remarks>
public interface IDbProviderRegistry
{
    /// <summary>
    /// Gets the set of provider keys this registry knows about. 📋.
    /// </summary>
    public IReadOnlyCollection<string> KnownProviders { get; }

    /// <summary>
    /// Maps a user-facing key ("postgres", "sqlite") to a linq2db <c>ProviderName</c>. 🔑.
    /// </summary>
    /// <param name="moduleProviderKey">The provider key from module configuration (case-insensitive).</param>
    /// <returns>The linq2db provider name string.</returns>
    /// <exception cref="UnknownProviderException">Thrown when the key is not registered.</exception>
    public string ResolveLinq2DbProvider(string moduleProviderKey);
}

