// <copyright file="JsonValues.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Designer.State;

using System.Globalization;
using System.Text.Json;

/// <summary>
/// 🔤 Phase 3.3.b.3 — Helpers for reading/writing property values as <see cref="JsonElement"/> while
/// preserving JSON types (a Number property must not become a JSON string). Framework-free~ ✨.
/// </summary>
public static class JsonValues
{
    /// <summary>A JSON <c>null</c> element~ 🫥.</summary>
    public static JsonElement Null { get; } = JsonDocument.Parse("null").RootElement.Clone();

    /// <summary>Renders an element as a display/edit string~ 📝.</summary>
    /// <param name="element">The element.</param>
    /// <returns>The string form.</returns>
    public static string ToText(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? string.Empty,
        JsonValueKind.Number => element.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
        _ => element.GetRawText(),
    };

    /// <summary>Creates a JSON string element~ 🔤.</summary>
    /// <param name="value">The string.</param>
    /// <returns>The element.</returns>
    public static JsonElement FromString(string? value)
        => JsonSerializer.SerializeToElement(value ?? string.Empty);

    /// <summary>Creates a JSON number element from text (falls back to a string element)~ 🔢.</summary>
    /// <param name="text">The numeric text.</param>
    /// <returns>The element.</returns>
    public static JsonElement FromNumber(string? text)
    {
        if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
        {
            // Preserve integers as integers.
            if (d == System.Math.Floor(d) && !text!.Contains('.') && long.TryParse(text, out var l))
            {
                return JsonSerializer.SerializeToElement(l);
            }

            return JsonSerializer.SerializeToElement(d);
        }

        return FromString(text);
    }

    /// <summary>Creates a JSON boolean element~ ✅.</summary>
    /// <param name="value">The boolean.</param>
    /// <returns>The element.</returns>
    public static JsonElement FromBool(bool value) => JsonSerializer.SerializeToElement(value);

    /// <summary>Reads a boolean from an element (tolerant of string "true")~ ✅.</summary>
    /// <param name="element">The element.</param>
    /// <returns>The boolean value.</returns>
    public static bool AsBool(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => bool.TryParse(element.GetString(), out var b) && b,
        _ => false,
    };

    /// <summary>Parses arbitrary JSON text into an element, or null when invalid~ 📦.</summary>
    /// <param name="json">The JSON text.</param>
    /// <returns>The element, or null.</returns>
    public static JsonElement? TryParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Null;
        }

        try
        {
            return JsonDocument.Parse(json).RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
