// <copyright file="ScriptLibraryStoreBase.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Scripting.Libraries;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Workflow.Scripting.Abstractions;

/// <summary>
/// 📚 Phase 3.1.5 — Shared library store logic: CRUD over an in-memory catalog snapshot + the
/// dependency-ordered resolver (topological sort with cycle detection). Persistent implementations
/// override <see cref="LoadCatalogAsync"/>/<see cref="SaveCatalogAsync"/>~ ✨.
/// </summary>
public abstract class ScriptLibraryStoreBase : IScriptLibraryStore
{
    private readonly SemaphoreSlim gate = new(1, 1);

    /// <inheritdoc/>
    public async Task SaveAsync(ScriptLibraryDefinition library, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(library);
        if (string.IsNullOrWhiteSpace(library.LibraryId))
        {
            throw new ScriptLibraryException("A library id is required.");
        }

        if (string.IsNullOrWhiteSpace(library.Language))
        {
            throw new ScriptLibraryException("A library language is required.");
        }

        await this.gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var catalog = await this.LoadCatalogAsync(ct).ConfigureAwait(false);
            catalog[library.LibraryId] = library;
            await this.SaveCatalogAsync(catalog, ct).ConfigureAwait(false);
        }
        finally
        {
            this.gate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<ScriptLibraryDefinition?> GetAsync(string libraryId, CancellationToken ct = default)
    {
        var catalog = await this.LoadCatalogAsync(ct).ConfigureAwait(false);
        return catalog.TryGetValue(libraryId, out var lib) ? lib : null;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ScriptLibraryDefinition>> GetAllAsync(string? language = null, CancellationToken ct = default)
    {
        var catalog = await this.LoadCatalogAsync(ct).ConfigureAwait(false);
        var all = catalog.Values.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(language))
        {
            all = all.Where(l => string.Equals(l.Language, language, StringComparison.OrdinalIgnoreCase));
        }

        return all.OrderBy(l => l.LibraryId).ToList();
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync(string libraryId, CancellationToken ct = default)
    {
        await this.gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var catalog = await this.LoadCatalogAsync(ct).ConfigureAwait(false);
            if (!catalog.Remove(libraryId))
            {
                return false;
            }

            await this.SaveCatalogAsync(catalog, ct).ConfigureAwait(false);
            return true;
        }
        finally
        {
            this.gate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ScriptLibrarySource>> ResolveAsync(
        string language,
        IReadOnlyList<string> libraryIds,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(libraryIds);
        if (libraryIds.Count == 0)
        {
            return Array.Empty<ScriptLibrarySource>();
        }

        var catalog = await this.LoadCatalogAsync(ct).ConfigureAwait(false);
        var ordered = new List<ScriptLibraryDefinition>();
        var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); // 1=visiting, 2=done
        var stack = new List<string>();

        foreach (var id in libraryIds)
        {
            Visit(id);
        }

        return ordered.Select(l => new ScriptLibrarySource(l.LibraryId, l.Code)).ToList();

        void Visit(string id)
        {
            if (state.TryGetValue(id, out var s))
            {
                if (s == 1)
                {
                    var cycleStart = stack.FindIndex(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
                    var cycle = stack.Skip(cycleStart).Append(id);
                    throw new ScriptLibraryException($"Circular library dependency: {string.Join(" → ", cycle)}.");
                }

                return;
            }

            if (!catalog.TryGetValue(id, out var lib))
            {
                throw new ScriptLibraryException($"Unknown library '{id}'.");
            }

            if (!string.Equals(lib.Language, language, StringComparison.OrdinalIgnoreCase))
            {
                throw new ScriptLibraryException($"Library '{id}' is a {lib.Language} library and cannot be imported into a {language} script.");
            }

            state[id] = 1;
            stack.Add(id);
            foreach (var dep in lib.Dependencies)
            {
                Visit(dep);
            }

            stack.RemoveAt(stack.Count - 1);
            state[id] = 2;
            ordered.Add(lib);
        }
    }

    /// <summary>Loads the full catalog (mutable copy)~ 📖.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The catalog keyed by library id.</returns>
    protected abstract Task<Dictionary<string, ScriptLibraryDefinition>> LoadCatalogAsync(CancellationToken ct);

    /// <summary>Persists the full catalog~ 💾.</summary>
    /// <param name="catalog">The catalog.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when saved.</returns>
    protected abstract Task SaveCatalogAsync(Dictionary<string, ScriptLibraryDefinition> catalog, CancellationToken ct);
}
