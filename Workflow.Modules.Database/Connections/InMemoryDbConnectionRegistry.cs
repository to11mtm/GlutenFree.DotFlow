// <copyright file="InMemoryDbConnectionRegistry.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Connections;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using Microsoft.Extensions.Options;
using Workflow.Modules.Database.Abstractions;
using Workflow.Modules.Database.Configuration;

/// <summary>
/// 📇 In-memory connection registry — hydrated from config at construction,
/// mutable in-process via <see cref="UpsertAsync"/>/<see cref="DeleteAsync"/>~ ✨.
/// </summary>
/// <remarks>
/// <para>
/// Lookup is <b>case-insensitive</b> on <see cref="DbConnectionDescriptor.Id"/>.
/// Runtime mutations are process-local and lost on restart — the Sqlite-persisted
/// variant (2.4.a.5) overrides this registration when persistence is configured~ 🌸.
/// </para>
/// <para>
/// CopilotNote: Phase 2.4.a.0 — when a config entry's dictionary key differs from the
/// descriptor's <c>Id</c>, the dictionary key wins (it's what ops see in appsettings).
/// Credential encryption only applies to the persisted registry (2.4.a.5) — config values
/// are plain by design (D3)~ 💖.
/// </para>
/// </remarks>
public sealed class InMemoryDbConnectionRegistry : IDbConnectionRegistry
{
    /// <summary>
    /// Case-insensitive descriptor store keyed on connection id. 🗃️.
    /// </summary>
    private readonly ConcurrentDictionary<string, DbConnectionDescriptor> store =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryDbConnectionRegistry"/> class,
    /// hydrating from bound <see cref="DatabaseConnectionsOptions"/>~ ⚙️.
    /// </summary>
    /// <param name="options">The config-bound options (may contain zero connections).</param>
    public InMemoryDbConnectionRegistry(IOptions<DatabaseConnectionsOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        foreach (var (key, descriptor) in options.Value.Connections)
        {
            // The appsettings dictionary key is authoritative for the id~ 🔑
            this.store[key] = descriptor with { Id = key };
        }
    }

    /// <inheritdoc/>
    public Task<Option<DbConnectionDescriptor>> GetAsync(string id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        return Task.FromResult(
            this.store.TryGetValue(id, out var descriptor)
                ? Option<DbConnectionDescriptor>.Some(descriptor)
                : Option<DbConnectionDescriptor>.None);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<DbConnectionDescriptor>> ListAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<DbConnectionDescriptor>>(this.store.Values.ToList());

    /// <inheritdoc/>
    public Task UpsertAsync(DbConnectionDescriptor descriptor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Id);

        this.store[descriptor.Id] = descriptor;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        return Task.FromResult(this.store.TryRemove(id, out _));
    }
}

