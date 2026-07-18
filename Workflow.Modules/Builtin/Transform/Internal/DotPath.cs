// <copyright file="DotPath.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.Transform.Internal;

using System.Collections.Generic;
using System.Globalization;

/// <summary>
/// 🧭 Resolves dot-notation paths (<c>"user.address.city"</c>) over the D6 dict/list shape~ ✨.
/// </summary>
public static class DotPath
{
    /// <summary>
    /// Resolves a dot-path against a value, returning <c>null</c> for any missing segment~ 🧭.
    /// </summary>
    /// <param name="root">The root value (dict/list/scalar).</param>
    /// <param name="path">The dot-path (segments may be array indices).</param>
    /// <param name="found">Whether every segment resolved.</param>
    /// <returns>The resolved value, or <c>null</c>.</returns>
    public static object? Resolve(object? root, string path, out bool found)
    {
        found = true;
        if (string.IsNullOrEmpty(path))
        {
            return root;
        }

        var current = root;
        foreach (var segment in path.Split('.'))
        {
            switch (current)
            {
                case IReadOnlyDictionary<string, object?> dict when dict.TryGetValue(segment, out var v):
                    current = v;
                    break;
                case IReadOnlyList<object?> list when int.TryParse(segment, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx) && idx >= 0 && idx < list.Count:
                    current = list[idx];
                    break;
                default:
                    found = false;
                    return null;
            }
        }

        return current;
    }
}
