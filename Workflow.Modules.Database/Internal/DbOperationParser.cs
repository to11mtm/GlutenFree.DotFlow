// <copyright file="DbOperationParser.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Internal;

using System;
using System.Collections;
using System.Collections.Generic;
using Workflow.Modules.Database.Models;

/// <summary>
/// 🚨 Thrown when an operation entry is structurally invalid (missing SQL, wrong shape,
/// or both <c>parameters</c> and <c>parameterSets</c> present)~ 🌸.
/// </summary>
public sealed class DbOperationParseException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="DbOperationParseException"/> class.</summary>
    /// <param name="index">The offending operation index (or -1 when not applicable).</param>
    /// <param name="reason">Why parsing failed.</param>
    public DbOperationParseException(int index, string reason)
        : base(index >= 0 ? $"operations[{index}]: {reason}" : reason)
    {
        this.Index = index;
    }

    /// <summary>Initializes a new instance of the <see cref="DbOperationParseException"/> class.</summary>
    public DbOperationParseException()
        : base()
    {
        this.Index = -1;
    }

    /// <summary>Initializes a new instance of the <see cref="DbOperationParseException"/> class.</summary>
    /// <param name="message">The error message.</param>
    public DbOperationParseException(string message)
        : base(message)
    {
        this.Index = -1;
    }

    /// <summary>Initializes a new instance of the <see cref="DbOperationParseException"/> class.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public DbOperationParseException(string message, Exception innerException)
        : base(message, innerException)
    {
        this.Index = -1;
    }

    /// <summary>Gets the offending operation index (or -1).</summary>
    public int Index { get; }
}

/// <summary>
/// 🧩 Parses the loosely-typed <c>operations</c> module property (a list of dictionaries from
/// workflow config / JSON) into strongly-typed <see cref="DbOperationSpec"/> records~ ✨.
/// </summary>
/// <remarks>
/// CopilotNote: Phase 2.4.a.3 — kept separate from the module so the fiddly "coerce whatever
/// shape the engine handed us" logic is unit-testable in isolation. Reuses
/// <see cref="SqlParameterBinder.Normalize"/> for each parameter map~ 🌸.
/// </remarks>
public static class DbOperationParser
{
    /// <summary>
    /// Parses the raw <c>operations</c> property value into a list of specs~ 🧩.
    /// </summary>
    /// <param name="raw">The raw property value (expected: an enumerable of dict-like entries).</param>
    /// <returns>The parsed specs (empty list when <paramref name="raw"/> is null/empty).</returns>
    /// <exception cref="DbOperationParseException">Thrown on a structurally invalid entry.</exception>
    public static IReadOnlyList<DbOperationSpec> Parse(object? raw)
    {
        if (raw is null)
        {
            return Array.Empty<DbOperationSpec>();
        }

        if (raw is not IEnumerable enumerable || raw is string)
        {
            throw new DbOperationParseException(-1, "'operations' must be a list of operation objects~ 💔");
        }

        var specs = new List<DbOperationSpec>();
        var index = 0;
        foreach (var item in enumerable)
        {
            specs.Add(ParseOne(index, item));
            index++;
        }

        return specs;
    }

    private static DbOperationSpec ParseOne(int index, object? item)
    {
        var map = ToStringMap(item)
            ?? throw new DbOperationParseException(index, "each operation must be an object with a 'sql' field~ 💔");

        if (map.ContainsKey("savepoint") || map.ContainsKey("rollbackToSavepoint"))
        {
            throw new DbOperationParseException(index, "savepoints are deferred to 2.4.a.P2~ 💔");
        }

        var sql = GetString(map, "sql");
        if (string.IsNullOrWhiteSpace(sql))
        {
            throw new DbOperationParseException(index, "'sql' is required and must be non-empty~ 💔");
        }

        var hasParameters = map.TryGetValue("parameters", out var parametersRaw) && parametersRaw is not null;
        var hasParameterSets = map.TryGetValue("parameterSets", out var parameterSetsRaw) && parameterSetsRaw is not null;

        if (hasParameters && hasParameterSets)
        {
            throw new DbOperationParseException(
                index,
                "'parameters' and 'parameterSets' are mutually exclusive — set at most one~ 💔");
        }

        IReadOnlyDictionary<string, object?>? parameters = null;
        IReadOnlyList<IReadOnlyDictionary<string, object?>>? parameterSets = null;

        if (hasParameters)
        {
            parameters = SqlParameterBinder.Normalize(parametersRaw);
        }
        else if (hasParameterSets)
        {
            parameterSets = ParseParameterSets(index, parameterSetsRaw);
        }

        return new DbOperationSpec
        {
            Sql = sql!,
            Parameters = parameters,
            ParameterSets = parameterSets,
            ExpectLastInsertId = GetBool(map, "expectLastInsertId"),
        };
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> ParseParameterSets(int index, object? raw)
    {
        if (raw is not IEnumerable enumerable || raw is string)
        {
            throw new DbOperationParseException(index, "'parameterSets' must be a list of parameter objects~ 💔");
        }

        var sets = new List<IReadOnlyDictionary<string, object?>>();
        foreach (var entry in enumerable)
        {
            sets.Add(SqlParameterBinder.Normalize(entry) ?? new Dictionary<string, object?>());
        }

        return sets;
    }

    private static IReadOnlyDictionary<string, object?>? ToStringMap(object? item)
    {
        switch (item)
        {
            case IReadOnlyDictionary<string, object?> rod:
                return new Dictionary<string, object?>(rod, StringComparer.OrdinalIgnoreCase);
            case IDictionary<string, object?> od:
                return new Dictionary<string, object?>(od, StringComparer.OrdinalIgnoreCase);
            case IDictionary nd:
            {
                var map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (DictionaryEntry e in nd)
                {
                    map[e.Key.ToString() ?? string.Empty] = e.Value;
                }

                return map;
            }

            default:
                return null;
        }
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> map, string key)
        => map.TryGetValue(key, out var v) && v is not null ? v.ToString() : null;

    private static bool GetBool(IReadOnlyDictionary<string, object?> map, string key)
        => map.TryGetValue(key, out var v) && v switch
        {
            bool b => b,
            string s when bool.TryParse(s, out var parsed) => parsed,
            int i => i != 0,
            long l => l != 0,
            _ => false,
        };
}
