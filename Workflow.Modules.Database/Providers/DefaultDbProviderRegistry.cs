// <copyright file="DefaultDbProviderRegistry.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Providers;

using System;
using System.Collections.Generic;
using LinqToDB;
using Workflow.Modules.Database.Abstractions;

/// <summary>
/// 🗂️ Default provider registry — hardcoded Postgres + SQLite for V1 (D5)~ ✨.
/// </summary>
/// <remarks>
/// <para>
/// Mappings:
/// <list type="bullet">
///   <item><description><c>"postgres"</c> → <see cref="ProviderName.PostgreSQL15"/></description></item>
///   <item><description><c>"sqlite"</c> → <see cref="ProviderName.SQLiteMS"/> (Microsoft.Data.Sqlite)</description></item>
/// </list>
/// </para>
/// <para>
/// CopilotNote: Phase 2.4.a.0 — MySQL ("mysql" → MySql80) + SQL Server ("sqlserver" → SqlServer2022)
/// land in 2.4.a.P3. Plugins wanting exotic providers DI-replace <see cref="IDbProviderRegistry"/>
/// entirely (D6) — don't add a mutable registration API here, replacement is the extension point~ 🌸.
/// </para>
/// </remarks>
public sealed class DefaultDbProviderRegistry : IDbProviderRegistry
{
    /// <summary>
    /// Case-insensitive provider-key → linq2db ProviderName map. 🔑.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Mappings =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["postgres"] = ProviderName.PostgreSQL15,
            ["sqlite"] = ProviderName.SQLiteMS,
        };

    /// <inheritdoc/>
    public IReadOnlyCollection<string> KnownProviders => Mappings.Keys.ToList();

    /// <inheritdoc/>
    public string ResolveLinq2DbProvider(string moduleProviderKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleProviderKey);

        return Mappings.TryGetValue(moduleProviderKey, out var linq2DbProvider)
            ? linq2DbProvider
            : throw new UnknownProviderException(moduleProviderKey);
    }
}

