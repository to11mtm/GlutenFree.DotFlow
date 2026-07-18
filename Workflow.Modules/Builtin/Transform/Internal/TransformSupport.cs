// <copyright file="TransformSupport.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.Transform.Internal;

using System.Collections.Generic;

/// <summary>
/// 🧰 Shared property/data readers for transform modules~ 🔄✨.
/// </summary>
public static class TransformSupport
{
    /// <summary>
    /// Reads the working data from an input port (preferred) or a property fallback (port wins)~ 📥.
    /// </summary>
    /// <param name="context">The module execution context.</param>
    /// <param name="key">The port/property key.</param>
    /// <returns>The raw value.</returns>
    public static object? ReadData(Abstractions.ModuleExecutionContext context, string key)
        => context.Inputs.TryGetValue(key, out var portVal) && portVal is not null
            ? portVal
            : context.Properties.TryGetValue(key, out var propVal) ? propVal : null;

    /// <summary>
    /// Reads a trimmed string property or <c>null</c>~ 🔤.
    /// </summary>
    /// <param name="props">The property bag.</param>
    /// <param name="key">The key.</param>
    /// <returns>The string or <c>null</c>.</returns>
    public static string? GetString(IReadOnlyDictionary<string, object?> props, string key)
    {
        if (!props.TryGetValue(key, out var v) || v is null)
        {
            return null;
        }

        var s = v as string ?? v.ToString();
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    /// <summary>
    /// Reads a bool property~ ✅.
    /// </summary>
    /// <param name="props">The property bag.</param>
    /// <param name="key">The key.</param>
    /// <param name="def">Default when missing.</param>
    /// <returns>The bool value.</returns>
    public static bool GetBool(IReadOnlyDictionary<string, object?> props, string key, bool def = false)
    {
        if (!props.TryGetValue(key, out var v) || v is null)
        {
            return def;
        }

        return v switch { bool b => b, string s when bool.TryParse(s, out var r) => r, _ => def };
    }

    /// <summary>
    /// Reads an int property~ 🔢.
    /// </summary>
    /// <param name="props">The property bag.</param>
    /// <param name="key">The key.</param>
    /// <returns>The int value or <c>null</c>.</returns>
    public static int? GetInt(IReadOnlyDictionary<string, object?> props, string key)
    {
        if (!props.TryGetValue(key, out var v) || v is null)
        {
            return null;
        }

        return v switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            string s when int.TryParse(s, out var r) => r,
            _ => null,
        };
    }
}
