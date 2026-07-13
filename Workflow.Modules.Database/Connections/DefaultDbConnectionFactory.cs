// <copyright file="DefaultDbConnectionFactory.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Connections;

using System;
using System.Threading;
using System.Threading.Tasks;
using LinqToDB;
using LinqToDB.Data;
using Workflow.Modules.Database.Abstractions;

/// <summary>
/// 🔌 Default connection factory — composes <see cref="IDbConnectionRegistry"/> (named lookups)
/// with <see cref="IDbProviderRegistry"/> (provider validation)~ ✨.
/// </summary>
/// <remarks>
/// CopilotNote: Phase 2.4.a.0 — <see cref="DataConnection"/> construction goes through
/// linq2db's <see cref="DataOptions"/> (<c>UseConnectionString(provider, connStr)</c>).
/// The factory never opens the underlying ADO.NET connection eagerly — linq2db opens lazily
/// on first command, which keeps Create cheap. Disposal is the CALLER's job (see interface docs)~ 🌸.
/// </remarks>
public sealed class DefaultDbConnectionFactory : IDbConnectionFactory
{
    private readonly IDbConnectionRegistry connectionRegistry;
    private readonly IDbProviderRegistry providerRegistry;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultDbConnectionFactory"/> class.
    /// </summary>
    /// <param name="connectionRegistry">Registry of named connections.</param>
    /// <param name="providerRegistry">Registry of provider-key mappings.</param>
    public DefaultDbConnectionFactory(
        IDbConnectionRegistry connectionRegistry,
        IDbProviderRegistry providerRegistry)
    {
        this.connectionRegistry = connectionRegistry ?? throw new ArgumentNullException(nameof(connectionRegistry));
        this.providerRegistry = providerRegistry ?? throw new ArgumentNullException(nameof(providerRegistry));
    }

    /// <inheritdoc/>
    public async ValueTask<DataConnection> CreateAsync(string connectionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);

        var maybeDescriptor = await this.connectionRegistry.GetAsync(connectionId, ct).ConfigureAwait(false);

        var descriptor = maybeDescriptor.MatchUnsafe<DbConnectionDescriptor?>(
            Some: d => d,
            None: () => null);

        // Unknown OR disabled both surface as not-found — disabled connections must not be usable~ 🔒
        if (descriptor is null || !descriptor.Enabled)
        {
            throw new ConnectionNotFoundException(connectionId);
        }

        return this.Build(descriptor.ProviderKey, descriptor.ConnectionString);
    }

    /// <inheritdoc/>
    public ValueTask<DataConnection> CreateAsync(string providerKey, string connectionString, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        // CA2000: ownership of the DataConnection transfers to the caller by contract
        // ("disposal is the caller's responsibility" — see IDbConnectionFactory docs)~ 🧹
#pragma warning disable CA2000
        return ValueTask.FromResult(this.Build(providerKey, connectionString));
#pragma warning restore CA2000
    }

    /// <summary>
    /// Builds the <see cref="DataConnection"/> after resolving the linq2db provider name. 🏗️.
    /// </summary>
    private DataConnection Build(string providerKey, string connectionString)
    {
        // Throws UnknownProviderException on unregistered keys~ 🗂️
        var linq2DbProvider = this.providerRegistry.ResolveLinq2DbProvider(providerKey);

        var options = new DataOptions().UseConnectionString(linq2DbProvider, connectionString);
        return new DataConnection(options);
    }
}


