// <copyright file="IScriptLibraryStore.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Scripting.Libraries;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Workflow.Scripting.Abstractions;

/// <summary>
/// 📚 Phase 3.1.5 — Storage + resolution for script libraries~ ✨.
/// </summary>
public interface IScriptLibraryStore
{
    /// <summary>Saves (creates or replaces) a library~ 💾.</summary>
    /// <param name="library">The library.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when saved.</returns>
    Task SaveAsync(ScriptLibraryDefinition library, CancellationToken ct = default);

    /// <summary>Gets a library by id, or <c>null</c> when absent~ 📖.</summary>
    /// <param name="libraryId">The library id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The library, or <c>null</c>.</returns>
    Task<ScriptLibraryDefinition?> GetAsync(string libraryId, CancellationToken ct = default);

    /// <summary>Lists libraries, optionally filtered by language~ 📋.</summary>
    /// <param name="language">Optional language filter.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The libraries.</returns>
    Task<IReadOnlyList<ScriptLibraryDefinition>> GetAllAsync(string? language = null, CancellationToken ct = default);

    /// <summary>Deletes a library by id~ 🗑️.</summary>
    /// <param name="libraryId">The library id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> when a library was removed.</returns>
    Task<bool> DeleteAsync(string libraryId, CancellationToken ct = default);

    /// <summary>
    /// Resolves the given library ids (of a language) into dependency-ordered <see cref="ScriptLibrarySource"/>
    /// entries for injection, expanding transitive dependencies and detecting cycles~ 🔗.
    /// </summary>
    /// <param name="language">The script language.</param>
    /// <param name="libraryIds">The explicitly-imported library ids.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The dependency-ordered sources.</returns>
    /// <exception cref="ScriptLibraryException">When a library is missing, wrong-language, or forms a cycle.</exception>
    Task<IReadOnlyList<ScriptLibrarySource>> ResolveAsync(string language, IReadOnlyList<string> libraryIds, CancellationToken ct = default);
}

/// <summary>
/// 📚 Phase 3.1.5 — Thrown when a library import cannot be resolved (missing / wrong language / cycle)~ ✨.
/// </summary>
public sealed class ScriptLibraryException : System.Exception
{
    /// <summary>Initializes a new instance of the <see cref="ScriptLibraryException"/> class~ 📚.</summary>
    /// <param name="message">The reason.</param>
    public ScriptLibraryException(string message)
        : base(message)
    {
    }
}
