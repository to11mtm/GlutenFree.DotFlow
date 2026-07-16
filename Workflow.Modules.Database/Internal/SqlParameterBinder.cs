// <copyright file="SqlParameterBinder.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Internal;

using System;
using System.Collections;
using System.Collections.Generic;
using LinqToDB.Data;
using Workflow.Modules.Database.Abstractions;

/// <summary>
/// 🧷 Converts a workflow property map of parameter values into linq2db
/// <see cref="DataParameter"/>s — the ONLY way SQL parameters ever reach the database
/// (D7: parameterisation is mandatory, no string concatenation ever!)~ ✨.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: Phase 2.4.a.1 — the value-type allowlist is deliberately conservative.
/// Anything outside it throws <see cref="SqlParameterBindingException"/> so a workflow
/// author gets a crisp error instead of a provider-specific type-mapping surprise deep
/// inside Npgsql/Sqlite. Provider-specific tweaks (e.g. Postgres uuid) can layer on here
/// later without touching the modules~ 🌸.
/// </para>
/// </remarks>
public static class SqlParameterBinder
{
    /// <summary>
    /// Binds a parameter map to an array of linq2db <see cref="DataParameter"/>s~ 🧷.
    /// </summary>
    /// <param name="parameters">
    /// The parameter name→value map (from module config). May be <see langword="null"/> or empty,
    /// in which case an empty array is returned.
    /// </param>
    /// <returns>The bound parameters, ready for <c>db.Query</c>/<c>db.Execute</c>.</returns>
    /// <exception cref="SqlParameterBindingException">
    /// Thrown when a value's runtime type is not in the supported allowlist.
    /// </exception>
    public static DataParameter[] Bind(IReadOnlyDictionary<string, object?>? parameters)
    {
        if (parameters is null || parameters.Count == 0)
        {
            return Array.Empty<DataParameter>();
        }

        var result = new DataParameter[parameters.Count];
        var i = 0;
        foreach (var kv in parameters)
        {
            result[i++] = BindOne(kv.Key, kv.Value);
        }

        return result;
    }

    /// <summary>
    /// Binds a single name/value pair to a linq2db <see cref="DataParameter"/>, validating the
    /// value type against the supported allowlist~ 🧷.
    /// </summary>
    /// <param name="name">The parameter name (a leading <c>@</c>/<c>:</c>/<c>?</c> is trimmed).</param>
    /// <param name="value">The parameter value.</param>
    /// <returns>The bound parameter.</returns>
    /// <exception cref="SqlParameterBindingException">Thrown when the value type is unsupported.</exception>
    public static DataParameter BindOne(string name, object? value)
        => new DataParameter(NormalizeName(name), CoerceValue(name, value));

    /// <summary>
    /// Coerces a loosely-typed properties entry (which may arrive as a <see cref="HashMap{K,V}"/>,
    /// a plain dictionary, or a nested object bag from JSON) into a uniform
    /// <see cref="IReadOnlyDictionary{TKey, TValue}"/>~ 🔧.
    /// </summary>
    /// <param name="raw">The raw <c>parameters</c> property value.</param>
    /// <returns>A normalised dictionary, or <see langword="null"/> when no parameters were supplied.</returns>
    public static IReadOnlyDictionary<string, object?>? Normalize(object? raw)
    {
        switch (raw)
        {
            case null:
                return null;
            case IReadOnlyDictionary<string, object?> rod:
                return rod;
            case IDictionary<string, object?> od:
                return new Dictionary<string, object?>(od);
            case IDictionary nd:
            {
                var map = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (DictionaryEntry entry in nd)
                {
                    map[entry.Key?.ToString() ?? string.Empty] = entry.Value;
                }

                return map;
            }

            default:
                return null;
        }
    }

    /// <summary>
    /// Normalises a parameter name to the leading-<c>@</c>-free form linq2db expects~ 🏷️.
    /// </summary>
    private static string NormalizeName(string name)
        => string.IsNullOrEmpty(name) ? name : name.TrimStart('@', ':', '?');

    /// <summary>
    /// Validates + passes through a supported value, or throws for unsupported types~ 🛡️.
    /// </summary>
    private static object? CoerceValue(string paramName, object? value)
        => value switch
        {
            null => null,
            string => value,
            bool => value,
            int => value,
            long => value,
            short => value,
            byte => value,
            double => value,
            float => value,
            decimal => value,
            Guid => value,
            DateTime => value,
            DateTimeOffset => value,
            TimeSpan => value,
            byte[] => value,
            _ => throw new SqlParameterBindingException(
                paramName,
                $"Type '{value.GetType().FullName}' is not supported in V1. Supported: string, bool, integral/floating/decimal numbers, Guid, DateTime, DateTimeOffset, TimeSpan, byte[], null~"),
        };
}



