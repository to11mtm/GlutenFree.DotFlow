// <copyright file="RepositoryModuleStateStore.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.State;

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// 🗄️ Phase 2.8.2 — The **optional** persistence-backed module state store. Serializes the snapshot
/// to JSON and delegates read/write to an <see cref="IModuleStatePersistence"/> the host wires over
/// its persistence provider (Q2)~ ✨.
/// </summary>
public sealed class RepositoryModuleStateStore : IModuleStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };

    private readonly IModuleStatePersistence persistence;

    /// <summary>Initializes a new instance of the <see cref="RepositoryModuleStateStore"/> class~ 🗄️.</summary>
    /// <param name="persistence">The backing read/write seam.</param>
    public RepositoryModuleStateStore(IModuleStatePersistence persistence)
    {
        this.persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
    }

    /// <inheritdoc/>
    public async Task<ModuleStateSnapshot> LoadAsync(CancellationToken ct = default)
    {
        var json = await this.persistence.ReadAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return ModuleStateSnapshot.Empty;
        }

        try
        {
            return JsonSerializer.Deserialize<ModuleStateSnapshot>(json, JsonOptions) ?? ModuleStateSnapshot.Empty;
        }
        catch (JsonException)
        {
            return ModuleStateSnapshot.Empty;
        }
    }

    /// <inheritdoc/>
    public async Task SaveAsync(ModuleStateSnapshot snapshot, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        await this.persistence.WriteAsync(json, ct).ConfigureAwait(false);
    }
}
