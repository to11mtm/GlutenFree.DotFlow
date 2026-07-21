// <copyright file="TransformValueConverter.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.Transform.Internal;

using System;
using System.Globalization;

/// <summary>
/// 🔁 Invariant-culture scalar coercion shared by map/query transforms~ ✨.
/// </summary>
public static class TransformValueConverter
{
    /// <summary>
    /// Converts a value to the named target type~ 🔁.
    /// </summary>
    /// <param name="value">The source value.</param>
    /// <param name="type">The target type key (string/int/long/double/decimal/bool/dateTime/guid).</param>
    /// <param name="result">The converted value when successful.</param>
    /// <param name="error">The error message when conversion fails.</param>
    /// <returns><c>true</c> when converted; otherwise <c>false</c>.</returns>
    public static bool TryConvert(object? value, string type, out object? result, out string? error)
    {
        error = null;
        result = null;
        var ci = CultureInfo.InvariantCulture;
        var s = value?.ToString();

        try
        {
            switch (type.ToLowerInvariant())
            {
                case "string":
                    result = s;
                    return true;
                case "int":
                    result = value is null ? null : Convert.ToInt32(value, ci);
                    return true;
                case "long":
                    result = value is null ? null : Convert.ToInt64(value, ci);
                    return true;
                case "double":
                    result = value is null ? null : Convert.ToDouble(value, ci);
                    return true;
                case "decimal":
                    result = value is null ? null : Convert.ToDecimal(value, ci);
                    return true;
                case "bool":
                    result = value switch
                    {
                        null => (object?)null,
                        bool b => b,
                        _ => bool.Parse(s!),
                    };
                    return true;
                case "datetime":
                    result = value is null ? null : DateTimeOffset.Parse(s!, ci, DateTimeStyles.RoundtripKind);
                    return true;
                case "guid":
                    result = value is null ? null : Guid.Parse(s!);
                    return true;
                default:
                    error = $"unknown convert type '{type}'";
                    return false;
            }
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException or ArgumentNullException)
        {
            error = $"cannot convert '{s}' to {type}: {ex.Message}";
            return false;
        }
    }
}
