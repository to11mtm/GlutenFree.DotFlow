// <copyright file="ScriptResultMaterializer.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Scripting.Roslyn.Execution;

using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 📤 Copies script results out of the collectible ALC into plain BCL types (no ALC-rooted refs)~ ✨.
/// </summary>
/// <remarks>
/// CopilotNote: The runner reuses one ALC per assembly, but results still must not root ALC-defined
/// types beyond the call. Since transform scripts return BCL shapes (dict/list/scalar) this is a
/// deep copy into fresh <see cref="Dictionary{TKey,TValue}"/>/<see cref="List{T}"/>~ 🌸.
/// </remarks>
public static class ScriptResultMaterializer
{
    /// <summary>
    /// Deep-copies a result value into plain BCL containers~ 📤.
    /// </summary>
    /// <param name="value">The raw result from the ALC.</param>
    /// <returns>An ALC-free copy.</returns>
    public static object? Materialize(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case string s:
                return s;
            case IDictionary<string, object?> genericDict:
                var gd = new Dictionary<string, object?>();
                foreach (var kvp in genericDict)
                {
                    gd[kvp.Key] = Materialize(kvp.Value);
                }

                return gd;
            case IDictionary rawDict:
                var rd = new Dictionary<string, object?>();
                foreach (DictionaryEntry entry in rawDict)
                {
                    rd[entry.Key?.ToString() ?? string.Empty] = Materialize(entry.Value);
                }

                return rd;
            case IEnumerable enumerable when value is not string:
                var list = new List<object?>();
                foreach (var item in enumerable)
                {
                    list.Add(Materialize(item));
                }

                return list;
            default:
                return value;
        }
    }
}
