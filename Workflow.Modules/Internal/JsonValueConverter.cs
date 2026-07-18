// <copyright file="JsonValueConverter.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Internal;

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// 📄 Converts between <see cref="JsonNode"/> and plain CLR objects so downstream modules
/// consume a uniform dictionary/list/scalar shape~ ✨.
/// </summary>
/// <remarks>
/// CopilotNote: Phase 2.5.a.2. Objects → <see cref="Dictionary{TKey,TValue}"/>,
/// arrays → <see cref="List{T}"/>, scalars → string/long/double/bool/null~ 🌸.
/// </remarks>
public static class JsonValueConverter
{
    /// <summary>
    /// Converts a parsed <see cref="JsonNode"/> to a plain CLR object graph~ 📄.
    /// </summary>
    /// <param name="node">The node to convert (may be <c>null</c>).</param>
    /// <returns>A dictionary, list, scalar, or <c>null</c>.</returns>
    public static object? ToClr(JsonNode? node)
    {
        switch (node)
        {
            case null:
                return null;
            case JsonObject obj:
                var dict = new Dictionary<string, object?>();
                foreach (var kvp in obj)
                {
                    dict[kvp.Key] = ToClr(kvp.Value);
                }

                return dict;
            case JsonArray arr:
                var list = new List<object?>();
                foreach (var item in arr)
                {
                    list.Add(ToClr(item));
                }

                return list;
            case JsonValue value:
                return ScalarToClr(value);
            default:
                return node.ToJsonString();
        }
    }

    /// <summary>
    /// Converts a <see cref="JsonElement"/> directly to a plain CLR object graph~ 📄.
    /// </summary>
    /// <param name="element">The element to convert.</param>
    /// <returns>A dictionary, list, scalar, or <c>null</c>.</returns>
    public static object? FromElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var dict = new Dictionary<string, object?>();
                foreach (var prop in element.EnumerateObject())
                {
                    dict[prop.Name] = FromElement(prop.Value);
                }

                return dict;
            case JsonValueKind.Array:
                var list = new List<object?>();
                foreach (var item in element.EnumerateArray())
                {
                    list.Add(FromElement(item));
                }

                return list;
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Null or JsonValueKind.Undefined:
                return null;
            case JsonValueKind.Number:
                return element.TryGetInt64(out var l) ? (object)l : element.GetDouble();
            default:
                return element.GetRawText();
        }
    }

    private static object? ScalarToClr(JsonValue value)
    {
        var element = value.GetValue<JsonElement>();
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Number => element.TryGetInt64(out var l) ? (object)l : element.GetDouble(),
            _ => element.GetRawText(),
        };
    }
}
