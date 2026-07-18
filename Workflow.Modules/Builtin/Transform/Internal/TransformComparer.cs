// <copyright file="TransformComparer.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.Transform.Internal;

using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>
/// 🔢 Numeric-aware comparison + key equality helpers shared by query/aggregate/join~ ✨.
/// </summary>
public static class TransformComparer
{
    /// <summary>
    /// Compares two values: numeric when both coerce to numbers, ordinal-string otherwise;
    /// <c>null</c> sorts first~ 🔢.
    /// </summary>
    /// <param name="a">Left value.</param>
    /// <param name="b">Right value.</param>
    /// <returns>Comparison result.</returns>
    public static int Compare(object? a, object? b)
    {
        if (a is null && b is null)
        {
            return 0;
        }

        if (a is null)
        {
            return -1;
        }

        if (b is null)
        {
            return 1;
        }

        if (TryToDouble(a, out var da) && TryToDouble(b, out var db))
        {
            return da.CompareTo(db);
        }

        return string.CompareOrdinal(a.ToString(), b.ToString());
    }

    /// <summary>
    /// Builds a normalised equality key for join/group matching (numbers compare numerically)~ 🔑.
    /// </summary>
    /// <param name="value">The key value.</param>
    /// <returns>A stable string key.</returns>
    public static string KeyOf(object? value)
    {
        if (value is null)
        {
            return "\0null";
        }

        return TryToDouble(value, out var d)
            ? "n:" + d.ToString("R", CultureInfo.InvariantCulture)
            : "s:" + value;
    }

    /// <summary>
    /// Attempts to coerce a value to <see cref="double"/>~ 🔢.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="result">The coerced number.</param>
    /// <returns><c>true</c> when numeric.</returns>
    public static bool TryToDouble(object? value, out double result)
    {
        switch (value)
        {
            case null:
                result = 0;
                return false;
            case double d:
                result = d;
                return true;
            case float f:
                result = f;
                return true;
            case long l:
                result = l;
                return true;
            case int i:
                result = i;
                return true;
            case short sh:
                result = sh;
                return true;
            case decimal m:
                result = (double)m;
                return true;
            case bool:
                result = 0;
                return false;
            case string s when double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed):
                result = parsed;
                return true;
            default:
                result = 0;
                return false;
        }
    }
}
