// <copyright file="PersistedScriptLibraryStore.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Scripting.Libraries;

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// 📚 Phase 3.1.5 — Persistence-backed script library store. Serializes the whole catalog to JSON and
/// reads/writes it through an <see cref="IScriptLibraryPersistence"/> the host adapts over its blob
/// store (mirrors the 2.8 state-store pattern, D9)~ ✨.
/// </summary>
public sealed class PersistedScriptLibraryStore : ScriptLibraryStoreBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IScriptLibraryPersistence persistence;

    /// <summary>Initializes a new instance of the <see cref="PersistedScriptLibraryStore"/> class~ 📚.</summary>
    /// <param name="persistence">The backing persistence seam.</param>
    public PersistedScriptLibraryStore(IScriptLibraryPersistence persistence)
    {
        this.persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
    }

    /// <inheritdoc/>
    protected override async Task<Dictionary<string, ScriptLibraryDefinition>> LoadCatalogAsync(CancellationToken ct)
    {
        var json = await this.persistence.ReadAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, ScriptLibraryDefinition>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var list = JsonSerializer.Deserialize<List<ScriptLibraryDefinition>>(json, JsonOptions) ?? new();
            var dict = new Dictionary<string, ScriptLibraryDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (var lib in list)
            {
                dict[lib.LibraryId] = lib;
            }

            return dict;
        }
        catch (JsonException)
        {
            return new Dictionary<string, ScriptLibraryDefinition>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <inheritdoc/>
    protected override async Task SaveCatalogAsync(Dictionary<string, ScriptLibraryDefinition> catalog, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(new List<ScriptLibraryDefinition>(catalog.Values), JsonOptions);
        await this.persistence.WriteAsync(json, ct).ConfigureAwait(false);
    }
}

/// <summary>
/// 🗄️ Phase 3.1.5 — Read/write seam for the persisted library catalog JSON (host-adapted to a blob
/// store)~ ✨.
/// </summary>
public interface IScriptLibraryPersistence
{
    /// <summary>Reads the catalog JSON, or <c>null</c> when absent~ 📖.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The JSON, or <c>null</c>.</returns>
    Task<string?> ReadAsync(CancellationToken ct = default);

    /// <summary>Writes the catalog JSON~ 💾.</summary>
    /// <param name="json">The JSON.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when written.</returns>
    Task WriteAsync(string json, CancellationToken ct = default);
}
