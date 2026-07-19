// <copyright file="InMemoryScriptLibraryStore.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Scripting.Libraries;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// 📚 Phase 3.1.5 — In-memory script library store (fallback when no blob store is configured)~ ✨.
/// </summary>
public sealed class InMemoryScriptLibraryStore : ScriptLibraryStoreBase
{
    private readonly Dictionary<string, ScriptLibraryDefinition> catalog = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    protected override Task<Dictionary<string, ScriptLibraryDefinition>> LoadCatalogAsync(CancellationToken ct)
        => Task.FromResult(new Dictionary<string, ScriptLibraryDefinition>(this.catalog, StringComparer.OrdinalIgnoreCase));

    /// <inheritdoc/>
    protected override Task SaveCatalogAsync(Dictionary<string, ScriptLibraryDefinition> catalog, CancellationToken ct)
    {
        this.catalog.Clear();
        foreach (var (id, lib) in catalog)
        {
            this.catalog[id] = lib;
        }

        return Task.CompletedTask;
    }
}
