// <copyright file="TransformDataNormalizer.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.Transform.Internal;

using System.Collections;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Workflow.Modules.Internal;

/// <summary>
/// 🧹 Coerces incoming port/property values into the uniform CLR dict/list/scalar shape all
/// transform modules operate on (D6)~ 🔄✨.
/// </summary>
/// <remarks>
/// CopilotNote: Phase 2.6.a.0. Passes CLR shapes through, converts <see cref="JsonNode"/>/
/// <see cref="JsonElement"/> stragglers via <see cref="JsonValueConverter"/>, and offers
/// <see cref="AsRows"/> for the common "array of records" case~ 🌸.
/// </remarks>
public static class TransformDataNormalizer
{
    /// <summary>
    /// Normalises any value into dict / list / scalar CLR form~ 🧹.
    /// </summary>
    /// <param name="value">The raw value.</param>
    /// <returns>The normalised value.</returns>
    public static object? Normalize(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case JsonNode node:
                return JsonValueConverter.ToClr(node);
            case JsonElement element:
                return JsonValueConverter.FromElement(element);
            case string s:
                return s;
            case IReadOnlyDictionary<string, object?> dict:
                return NormalizeDict(dict);
            case IDictionary<string, object?> mdict:
                return NormalizeDict(mdict);
            case IDictionary rawDict:
                return NormalizeRawDict(rawDict);
            case IEnumerable enumerable:
                var list = new List<object?>();
                foreach (var item in enumerable)
                {
                    list.Add(Normalize(item));
                }

                return list;
            default:
                return value;
        }
    }

    /// <summary>
    /// Normalises a value into a list of record dictionaries (the common tabular case)~ 📋.
    /// </summary>
    /// <param name="value">The raw value (array of records, or a single record).</param>
    /// <param name="rows">The resulting rows when successful.</param>
    /// <param name="error">The error message when the shape is not tabular.</param>
    /// <returns><c>true</c> when the value coerces to rows; otherwise <c>false</c>.</returns>
    public static bool AsRows(
        object? value,
        out IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        out string? error)
    {
        error = null;
        var normalized = Normalize(value);
        var result = new List<IReadOnlyDictionary<string, object?>>();

        switch (normalized)
        {
            case null:
                rows = result;
                return true;
            case IReadOnlyDictionary<string, object?> single:
                result.Add(single);
                rows = result;
                return true;
            case IEnumerable<object?> items:
                var idx = 0;
                foreach (var item in items)
                {
                    if (item is IReadOnlyDictionary<string, object?> record)
                    {
                        result.Add(record);
                    }
                    else
                    {
                        rows = result;
                        error = $"item #{idx} is not a record (expected an object, got {item?.GetType().Name ?? "null"})";
                        return false;
                    }

                    idx++;
                }

                rows = result;
                return true;
            default:
                rows = result;
                error = $"data must be an array of records or a single record (got {normalized.GetType().Name})";
                return false;
        }
    }

    private static Dictionary<string, object?> NormalizeDict(IEnumerable<KeyValuePair<string, object?>> dict)
    {
        var result = new Dictionary<string, object?>();
        foreach (var kvp in dict)
        {
            result[kvp.Key] = Normalize(kvp.Value);
        }

        return result;
    }

    private static Dictionary<string, object?> NormalizeRawDict(IDictionary raw)
    {
        var result = new Dictionary<string, object?>();
        foreach (DictionaryEntry entry in raw)
        {
            result[entry.Key?.ToString() ?? string.Empty] = Normalize(entry.Value);
        }

        return result;
    }
}
