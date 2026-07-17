// <copyright file="IDbConnectionFactory.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Abstractions;

using System.Threading;
using System.Threading.Tasks;
using LinqToDB;
using LinqToDB.Data;

/// <summary>
/// 🔌 Creates linq2db <see cref="DataConnection"/>s for the database module family~ ✨
/// Resolve by named registration (preferred, D3) or by raw (provider, connection string) pair.
/// </summary>
/// <remarks>
/// <para>
/// <b>Disposal is the caller's responsibility</b> — always
/// <c>using var db = await factory.CreateAsync(...)</c>! The factory never pools
/// <see cref="DataConnection"/> instances itself; ADO.NET connection pooling underneath
/// (Npgsql / Microsoft.Data.Sqlite) does the heavy lifting~ 🌸.
/// </para>
/// <para>
/// CopilotNote: Phase 2.4.a.0 — this is the single seam both module families share.
/// Raw-SQL modules (2.4.a) and the Roslyn-compiled <c>DynamicWorkflowContext</c> (2.4.b)
/// route through here so connection-string handling stays in one place. User code in 2.4.b
/// never sees raw connection strings (mitigates design-doc C3)~ 💖.
/// </para>
/// </remarks>
public interface IDbConnectionFactory
{
    /// <summary>
    /// Resolves a named connection registration and opens a <see cref="DataConnection"/> for it. 📇.
    /// </summary>
    /// <param name="connectionId">The registered connection id (case-insensitive).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A ready-to-use <see cref="DataConnection"/> — caller must dispose.</returns>
    /// <exception cref="ConnectionNotFoundException">Thrown when the id is unknown or the registration is disabled.</exception>
    /// <exception cref="UnknownProviderException">Thrown when the registration's provider key is not registered.</exception>
    public ValueTask<DataConnection> CreateAsync(string connectionId, CancellationToken ct = default);

    /// <summary>
    /// Opens a <see cref="DataConnection"/> from a raw (provider key, connection string) pair — the escape hatch~ 🔓.
    /// </summary>
    /// <param name="providerKey">The provider key ("postgres" / "sqlite"), validated via <see cref="IDbProviderRegistry"/>.</param>
    /// <param name="connectionString">The raw connection string.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A ready-to-use <see cref="DataConnection"/> — caller must dispose.</returns>
    /// <exception cref="UnknownProviderException">Thrown when the provider key is not registered.</exception>
    public ValueTask<DataConnection> CreateAsync(string providerKey, string connectionString, CancellationToken ct = default);

    /// <summary>
    /// Resolves a named connection into linq2db <see cref="DataOptions"/> (provider + connection string)~ 🧩.
    /// </summary>
    /// <param name="connectionId">The registered connection id (case-insensitive).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The <see cref="DataOptions"/> for the connection.</returns>
    /// <remarks>
    /// CopilotNote: Phase 2.4.b.3 — the Roslyn-compiled <c>DynamicWorkflowContext(DataOptions)</c> ctor
    /// needs <see cref="DataOptions"/> (not a built <see cref="DataConnection"/>). This keeps
    /// connection-string resolution in the one factory instead of duplicating it in the linq family~ 💖.
    /// </remarks>
    /// <exception cref="ConnectionNotFoundException">Thrown when the id is unknown or disabled.</exception>
    /// <exception cref="UnknownProviderException">Thrown when the provider key is not registered.</exception>
    public ValueTask<DataOptions> CreateOptionsAsync(string connectionId, CancellationToken ct = default);
}

