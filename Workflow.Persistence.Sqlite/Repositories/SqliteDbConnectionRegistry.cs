// <copyright file="SqliteDbConnectionRegistry.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

using LinqToDB.Async;

namespace Workflow.Persistence.Sqlite.Repositories;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using LinqToDB;
using Workflow.Modules.Database.Abstractions;
using Workflow.Persistence.Sqlite.Data;
using Workflow.Persistence.Sqlite.Data.Entities;

/// <summary>
/// 📇 Phase 2.4.a.5 — SQLite-backed <see cref="IDbConnectionRegistry"/>~ ✨.
/// Named connections survive host restarts; connection strings are encrypted at rest via the
/// host-supplied <see cref="IConnectionStringProtector"/>~ 🔒.
/// </summary>
/// <remarks>
/// CopilotNote: Mirrors <c>SqliteWebhookRegistrationRepository</c> (2.3.9). Lives here (not in
/// Workflow.Modules.Database) because it needs the linq2db/migration infra — and to keep
/// <c>IPersistenceProvider</c> free of a modules-layer dependency. Lookups are case-insensitive
/// via the <c>connection_id</c> PK~ 🌸.
/// </remarks>
public sealed class SqliteDbConnectionRegistry : IDbConnectionRegistry
{
    private readonly WorkflowDataConnectionFactory factory;
    private readonly IConnectionStringProtector protector;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteDbConnectionRegistry"/> class~ 🔌.
    /// </summary>
    /// <param name="factory">Shared SQLite data-connection factory.</param>
    /// <param name="protector">Connection-string protector for at-rest encryption.</param>
    public SqliteDbConnectionRegistry(WorkflowDataConnectionFactory factory, IConnectionStringProtector protector)
    {
        this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
        this.protector = protector ?? throw new ArgumentNullException(nameof(protector));
    }

    /// <inheritdoc/>
    public async Task<Option<DbConnectionDescriptor>> GetAsync(string id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        await using var db = this.factory.Create();
        var entity = await db.DbConnections
            .FirstOrDefaultAsync(c => c.ConnectionId == id, token: ct)
            .ConfigureAwait(false);

        return entity is null
            ? Option<DbConnectionDescriptor>.None
            : Option<DbConnectionDescriptor>.Some(this.ToDomain(entity));
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<DbConnectionDescriptor>> ListAsync(CancellationToken ct = default)
    {
        await using var db = this.factory.Create();
        var entities = await db.DbConnections.ToListAsync(token: ct).ConfigureAwait(false);
        return entities.Select(this.ToDomain).ToList();
    }

    /// <inheritdoc/>
    public async Task UpsertAsync(DbConnectionDescriptor descriptor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Id);

        await using var db = this.factory.Create();
        var entity = this.ToEntity(descriptor);

        var exists = await db.DbConnections
            .AnyAsync(c => c.ConnectionId == descriptor.Id, token: ct)
            .ConfigureAwait(false);

        if (exists)
        {
            await db.UpdateAsync(entity, token: ct).ConfigureAwait(false);
        }
        else
        {
            await db.InsertAsync(entity, token: ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        await using var db = this.factory.Create();
        var affected = await db.DbConnections
            .Where(c => c.ConnectionId == id)
            .DeleteAsync(token: ct)
            .ConfigureAwait(false);
        return affected > 0;
    }

    // ── Mapping helpers ────────────────────────────────────────────────────────────

    private DbConnectionEntity ToEntity(DbConnectionDescriptor descriptor) =>
        new()
        {
            ConnectionId = descriptor.Id,
            ProviderKey = descriptor.ProviderKey,
            ConnectionStringEncrypted = this.protector.Protect(descriptor.ConnectionString),
            DisplayName = descriptor.DisplayName,
            Enabled = descriptor.Enabled ? 1 : 0,
        };

    private DbConnectionDescriptor ToDomain(DbConnectionEntity entity) =>
        new(
            Id: entity.ConnectionId,
            ProviderKey: entity.ProviderKey,
            ConnectionString: this.protector.Unprotect(entity.ConnectionStringEncrypted),
            DisplayName: entity.DisplayName,
            Enabled: entity.Enabled != 0);
}

