// <copyright file="SqlParams.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Designer.State;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Workflow.UI.Client.Api.Dtos;

/// <summary>
/// 🛡️ Round-2 G9 — helpers for the safe SQL parameter builder: SQL text properties are verbatim
/// (never template-expanded, D7), so values must flow through the sibling <c>parameters</c> JSON
/// map and be referenced as <c>@name</c> placeholders. Framework-free~ ✨.
/// </summary>
public static class SqlParams
{
    /// <summary>The parameter-map property name~ 🧷.</summary>
    public const string ParametersProperty = "parameters";

    private static readonly string[] SqlPropertyNames = { "query", "command", "sql" };

    /// <summary>
    /// Finds the node's verbatim-SQL property (a <c>Code</c> editor named query/command/sql) when
    /// it also declares a <c>parameters</c> JSON map — the shape the builder supports~ 🔍.
    /// </summary>
    /// <param name="schema">The node's module schema.</param>
    /// <returns>The SQL property name, or null when the node isn't SQL-shaped.</returns>
    public static string? FindSqlProperty(ModuleSchemaDto? schema)
    {
        if (schema is null)
        {
            return null;
        }

        var hasParams = schema.Properties.Any(p =>
            p.Name == ParametersProperty && p.EditorType == "Json");
        if (!hasParams)
        {
            return null;
        }

        return schema.Properties.FirstOrDefault(p =>
            p.EditorType == "Code" && SqlPropertyNames.Contains(p.Name, StringComparer.OrdinalIgnoreCase))?.Name;
    }

    /// <summary>Sanitizes a parameter name: strips a leading @/:/? and non-word characters~ 🧼.</summary>
    /// <param name="raw">The raw name text.</param>
    /// <returns>The clean name, or null when nothing valid remains.</returns>
    public static string? SanitizeName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var t = raw.Trim().TrimStart('@', ':', '?');
        var clean = new string(t.Where(ch => char.IsLetterOrDigit(ch) || ch == '_').ToArray());
        return clean.Length == 0 ? null : clean;
    }

    /// <summary>The SQL placeholder for a parameter name~ 🏷️.</summary>
    /// <param name="name">The (sanitized) parameter name.</param>
    /// <returns><c>@name</c>.</returns>
    public static string Placeholder(string name) => "@" + name;

    /// <summary>Parses the parameters JSON into displayable name/value pairs~ 📋.</summary>
    /// <param name="parameters">The current <c>parameters</c> property value.</param>
    /// <returns>The entries (raw JSON text as value).</returns>
    public static IReadOnlyList<(string Name, string Value)> Parse(JsonElement? parameters)
    {
        if (parameters is not { ValueKind: JsonValueKind.Object } obj)
        {
            return Array.Empty<(string, string)>();
        }

        return obj.EnumerateObject()
            .Select(p => (p.Name, p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? string.Empty : p.Value.GetRawText()))
            .ToList();
    }

    /// <summary>
    /// Adds or replaces a parameter, typing the value: numbers and true/false stay typed,
    /// anything else becomes a JSON string (bound safely server-side — never concatenated)~ 🧷.
    /// </summary>
    /// <param name="parameters">The current <c>parameters</c> value (may be null/undefined).</param>
    /// <param name="name">The (sanitized) parameter name.</param>
    /// <param name="valueText">The literal value text.</param>
    /// <returns>The new parameters object.</returns>
    public static JsonElement Upsert(JsonElement? parameters, string name, string valueText)
    {
        var map = ToMap(parameters);
        map[name] = TypeValue(valueText);
        return JsonSerializer.SerializeToElement(map);
    }

    /// <summary>Removes a parameter~ 🗑️.</summary>
    /// <param name="parameters">The current <c>parameters</c> value.</param>
    /// <param name="name">The parameter name to remove.</param>
    /// <returns>The new parameters object.</returns>
    public static JsonElement Remove(JsonElement? parameters, string name)
    {
        var map = ToMap(parameters);
        map.Remove(name);
        return JsonSerializer.SerializeToElement(map);
    }

    private static Dictionary<string, object?> ToMap(JsonElement? parameters)
    {
        var map = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (parameters is { ValueKind: JsonValueKind.Object } obj)
        {
            foreach (var p in obj.EnumerateObject())
            {
                map[p.Name] = p.Value.Clone();
            }
        }

        return map;
    }

    private static object? TypeValue(string valueText)
    {
        var t = valueText.Trim();
        if (t is "true")
        {
            return true;
        }

        if (t is "false")
        {
            return false;
        }

        if (long.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
        {
            return l;
        }

        if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
        {
            return d;
        }

        return valueText;
    }
}
