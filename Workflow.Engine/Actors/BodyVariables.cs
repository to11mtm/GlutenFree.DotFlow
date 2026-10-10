// <copyright file="BodyVariables.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Engine.Actors;

using System.Collections.Generic;
using System.Linq;
using LanguageExt;

/// <summary>
/// A construct body's view of workflow variables, plus the writes made inside it (§10.3)~ 💾
/// </summary>
/// <remarks>
/// <para>
/// Scoping rules (agreed 2026-10-10):
/// <list type="bullet">
///   <item>Sequential bodies (loop iterations, try → catch → finally, transaction bodies) see the
///         writes of the steps before them; the construct reports all its writes when it ends.</item>
///   <item>Parallel branches each start from the same snapshot and don't see each other's writes;
///         the coordinator merges them in branch order when the parallel ends.</item>
///   <item>Writes made before a failure are kept and reported with the failure.</item>
/// </list>
/// </para>
/// <para>Single actor use only (not thread-safe).</para>
/// </remarks>
internal sealed class BodyVariables
{
    private readonly Dictionary<string, object?> _current;
    private readonly Dictionary<string, object?> _writes = new();

    /// <summary>Initializes a new instance of the <see cref="BodyVariables"/> class.</summary>
    /// <param name="initial">The variables visible when the body starts (may be null).</param>
    public BodyVariables(IReadOnlyDictionary<string, object?>? initial)
    {
        _current = initial is null
            ? new Dictionary<string, object?>()
            : new Dictionary<string, object?>(initial);
    }

    /// <summary>Gets the variables as the body currently sees them.</summary>
    public IReadOnlyDictionary<string, object?> Current => _current;

    /// <summary>Gets the writes made inside this body (last write per variable), or null if none.</summary>
    public IReadOnlyDictionary<string, object?>? Writes => _writes.Count > 0 ? _writes : null;

    /// <summary>Applies writes from a node or a nested body.</summary>
    /// <param name="updates">The writes to apply; null or empty is a no-op.</param>
    public void Apply(IEnumerable<KeyValuePair<string, object?>>? updates)
    {
        if (updates is null)
        {
            return;
        }

        foreach (var (name, value) in updates)
        {
            _current[name] = value;
            _writes[name] = value;
        }
    }

    /// <summary>Gets the current variables as the <see cref="HashMap{K, V}"/> NodeExecutor expects.</summary>
    /// <returns>The current variables.</returns>
    public HashMap<string, object?> CurrentAsHashMap()
        => _current.Select(kv => (kv.Key, kv.Value)).ToHashMap();
}
