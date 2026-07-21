// <copyright file="IDbConnectionRegistry.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Abstractions;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;

/// <summary>
/// 📇 A named database connection registration~ ✨
/// Hides credentials from workflow definitions (D3) — modules reference the <see cref="Id"/>,
/// never the raw connection string.
/// </summary>
/// <param name="Id">Unique key, e.g. "OrdersDb". Lookup is case-insensitive.</param>
/// <param name="ProviderKey">Provider key: "postgres" / "sqlite" (see <see cref="IDbProviderRegistry"/>).</param>
/// <param name="ConnectionString">The connection string — encrypted at rest when persisted (2.4.a.5); plain in config.</param>
/// <param name="DisplayName">Optional friendly name for UI pickers.</param>
/// <param name="Enabled">Whether this connection may be used by modules. Disabled connections resolve as not-found.</param>
public sealed record DbConnectionDescriptor(
    string Id,
    string ProviderKey,
    string ConnectionString,
    string? DisplayName = null,
    bool Enabled = true);

/// <summary>
/// 📇 Registry of named database connections~ 💖
/// Config-bound from <c>appsettings.json: Workflow:Database:Connections</c> at startup,
/// mutable at runtime via the API (2.4.a.5, opt-out via <c>DisableRuntimeCrud</c> — D4).
/// </summary>
/// <remarks>
/// CopilotNote: Phase 2.4.a.0 — mirrors the named-record CRUD shape of
/// <c>IWebhookRegistrationRepository</c> (Phase 2.3.9). Default impl is
/// <c>InMemoryDbConnectionRegistry</c>; the Sqlite-persisted variant lands in 2.4.a.5~ 🌸.
/// </remarks>
public interface IDbConnectionRegistry
{
    /// <summary>
    /// Looks up a connection descriptor by id (case-insensitive). 🔍.
    /// </summary>
    /// <param name="id">The connection id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>Some(descriptor)</c> when found and enabled-or-disabled; <c>None</c> when unknown.</returns>
    public Task<Option<DbConnectionDescriptor>> GetAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Lists all registered connection descriptors. 📋.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>All descriptors (order unspecified).</returns>
    public Task<IReadOnlyList<DbConnectionDescriptor>> ListAsync(CancellationToken ct = default);

    /// <summary>
    /// Inserts or updates a connection descriptor (keyed on <see cref="DbConnectionDescriptor.Id"/>, case-insensitive). 💾.
    /// </summary>
    /// <param name="descriptor">The descriptor to upsert.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the async operation.</returns>
    public Task UpsertAsync(DbConnectionDescriptor descriptor, CancellationToken ct = default);

    /// <summary>
    /// Deletes a connection registration by id (case-insensitive). 🗑️.
    /// </summary>
    /// <param name="id">The connection id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> when a registration was removed; <c>false</c> when the id was unknown.</returns>
    public Task<bool> DeleteAsync(string id, CancellationToken ct = default);
}

