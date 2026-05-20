// <copyright file="ResponseBodyDecoder.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.Http.Internal;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

/// <summary>
/// 📥 Phase 2.3.1 — Decodes a raw response byte payload into a workflow-friendly
/// value (object/dict for JSON, string for text, byte[] for binary) by dispatching
/// on the response's <c>Content-Type</c>~ ✨💖.
/// </summary>
/// <remarks>
/// CopilotNote: V1 decision tree:
/// <list type="bullet">
///   <item><description><c>application/json*</c> / <c>*+json</c> → <see cref="JsonDocument"/> → POCO graph (dict/list/primitive).</description></item>
///   <item><description><c>application/xml</c> / <c>text/xml</c> → <see cref="string"/> (XML→object map deferred).</description></item>
///   <item><description><c>text/*</c> → <see cref="string"/>.</description></item>
///   <item><description>anything else (or no content type) → <see cref="byte"/>[].</description></item>
/// </list>
/// Malformed JSON falls back to the raw string so the workflow can inspect what arrived~ 🧠
/// </remarks>
public static class ResponseBodyDecoder
{
    /// <summary>Decode raw response bytes into a POCO graph based on <paramref name="contentType"/>~ 📥.</summary>
    /// <param name="bytes">Raw response body bytes.</param>
    /// <param name="contentType">Response <c>Content-Type</c> media type (without parameters).</param>
    /// <returns>Decoded value (null when <paramref name="bytes"/> is empty).</returns>
    public static object? Decode(byte[] bytes, string contentType)
    {
        if (bytes is null || bytes.Length == 0)
        {
            return null;
        }

        var ct = contentType?.Trim().ToLowerInvariant() ?? string.Empty;

        if (ct.StartsWith("application/json", StringComparison.Ordinal) || ct.EndsWith("+json", StringComparison.Ordinal))
        {
            try
            {
                using var doc = JsonDocument.Parse(bytes);
                return JsonElementToObject(doc.RootElement);
            }
            catch (JsonException)
            {
                // Malformed JSON — give the workflow the raw string for diagnostics~
                return Encoding.UTF8.GetString(bytes);
            }
        }

        if (ct.StartsWith("text/", StringComparison.Ordinal)
            || ct.StartsWith("application/xml", StringComparison.Ordinal)
            || ct.StartsWith("application/xhtml", StringComparison.Ordinal))
        {
            return Encoding.UTF8.GetString(bytes);
        }

        // No content-type OR an unknown one: return raw bytes (consumer decides)~ 📦
        if (string.IsNullOrEmpty(ct))
        {
            return Encoding.UTF8.GetString(bytes);
        }

        return bytes;
    }

    /// <summary>Recursively unwrap a <see cref="JsonElement"/> into POCO primitives + dictionaries + lists~ 🔧.</summary>
    internal static object? JsonElementToObject(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.Object => el.EnumerateObject()
                .ToDictionary(p => p.Name, p => JsonElementToObject(p.Value)),
            JsonValueKind.Array => el.EnumerateArray()
                .Select(JsonElementToObject).ToList(),
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => el.GetRawText(),
        };
    }
}

