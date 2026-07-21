// <copyright file="JsonPathExtractor.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.Http.Internal;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using JPath = Json.Path.JsonPath;
using Microsoft.Extensions.Logging;

/// <summary>
/// 🎯 Phase 2.3.5 — Extracts named output values from HTTP responses using JSONPath expressions,
/// regex patterns, and response header lookups~ ✨💖.
/// </summary>
/// <remarks>
/// <para>
/// Three extraction strategies, all keyed the same way (output port name → selector string):
/// <list type="bullet">
///   <item><description><see cref="ExtractJsonPath"/> — JSONPath via <c>JsonPath.Net</c> (json-everything, MIT). JSON responses only.</description></item>
///   <item><description><see cref="ExtractRegex"/> — Named capture group <c>(?&lt;value&gt;...)</c> over the response body string.</description></item>
///   <item><description><see cref="ExtractHeaders"/> — Direct header name lookup over the flattened response headers dict.</description></item>
/// </list>
/// </para>
/// <para>
/// CopilotNote: JSONPath single-element results are automatically unwrapped to scalars so that
/// <c>$.user.id</c> outputs <c>"abc"</c> not <c>["abc"]</c>. Multi-node results (e.g. <c>$.items[*].id</c>)
/// return a <see cref="List{T}"/>. Missing paths return <c>null</c> unless the <c>required</c> flag is
/// <c>true</c>, in which case the caller receives an error message and should <c>ModuleResult.Fail</c>~ 🧠
/// </para>
/// </remarks>
public static class JsonPathExtractor
{
    // =========================================================================
    // JSONPath extraction 🎯
    // =========================================================================

    /// <summary>
    /// Evaluate each JSONPath expression in <paramref name="expressions"/> against the raw response
    /// <paramref name="jsonBytes"/> and return a map of output port name → extracted value.
    /// </summary>
    /// <param name="jsonBytes">Raw response body bytes.</param>
    /// <param name="contentType">Response Content-Type — must be JSON for extraction to run.</param>
    /// <param name="expressions">Output port name → JSONPath expression map.</param>
    /// <param name="required">
    /// When <c>true</c>, any expression that finds no matches returns an error string instead of
    /// a <c>null</c> value (caller should propagate as <c>ModuleResult.Fail</c>).
    /// </param>
    /// <param name="logger">Optional logger for warnings on parse/match failures.</param>
    /// <returns>
    /// A tuple: extracted values dictionary and an optional error string (non-null only when
    /// <paramref name="required"/> is <c>true</c> and a required path has no match).
    /// </returns>
    public static (Dictionary<string, object?> Values, string? Error) ExtractJsonPath(
        byte[] jsonBytes,
        string contentType,
        IReadOnlyDictionary<string, string> expressions,
        bool required,
        ILogger? logger = null)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);

        // Only run JSONPath against JSON content types — others silently produce all-null outputs~
        var ct = (contentType ?? string.Empty).Trim().ToUpperInvariant();
        bool isJson = ct.StartsWith("APPLICATION/JSON", StringComparison.Ordinal)
                   || ct.EndsWith("+JSON", StringComparison.Ordinal);

        if (!isJson || jsonBytes.Length == 0)
        {
            foreach (var kv in expressions)
            {
                result[kv.Key] = null;
            }

            return (result, null);
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(jsonBytes);
        }
        catch (JsonException je)
        {
            logger?.LogWarning(
                "🎯 JSONPath extraction skipped — response body is not valid JSON: {Error}~",
                je.Message);

            foreach (var kv in expressions)
            {
                result[kv.Key] = null;
            }

            return (result, null);
        }

        foreach (var (outputName, expression) in expressions)
        {
            if (string.IsNullOrWhiteSpace(expression))
            {
                result[outputName] = null;
                continue;
            }

            try
            {
                var path = JPath.Parse(expression);
                var pathResult = path.Evaluate(root);

                // NodeList is IReadOnlyList<Node> in JsonPath.Net 0.8.x; .Value on each Node is JsonNode?
                var matches = pathResult.Matches?.ToList()
                    ?? new List<Json.Path.Node>();

                if (matches.Count == 0)
                {
                    if (required)
                    {
                        return (result,
                            $"Required JSONPath expression '{expression}' for output port '{outputName}' found no matches in the response~ 💔");
                    }

                    result[outputName] = null;
                }
                else if (matches.Count == 1)
                {
                    // CopilotNote: Unwrap single-element NodeList → scalar so $.user.id → "abc" not ["abc"]~
                    result[outputName] = JsonNodeToObject(matches[0].Value);
                }
                else
                {
                    // Multiple match results → list (e.g. $.items[*].id → ["a","b","c"])~
                    result[outputName] = matches
                        .Select(m => JsonNodeToObject(m.Value))
                        .ToList();
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                logger?.LogWarning(
                    "🎯 JSONPath expression '{Expression}' for output '{Output}' threw: {Error}~",
                    expression, outputName, ex.Message);
                result[outputName] = null;
            }
        }

        return (result, null);
    }

    // =========================================================================
    // Regex extraction 🔍
    // =========================================================================

    /// <summary>
    /// Match each regex pattern in <paramref name="patterns"/> against <paramref name="bodyString"/>
    /// and return a map of output port name → captured value.
    /// </summary>
    /// <remarks>
    /// Patterns should contain a named capture group <c>(?&lt;value&gt;...)</c> — when present, the
    /// captured group's value is returned; otherwise the full match string is returned.
    /// Non-matching patterns produce <c>null</c>. Invalid regex expressions log a warning and
    /// also produce <c>null</c>~ 🌸
    /// </remarks>
    /// <param name="bodyString">Response body as a string (null or empty → all-null results).</param>
    /// <param name="patterns">Output port name → regex pattern map.</param>
    /// <param name="logger">Optional logger for warnings on invalid patterns.</param>
    public static Dictionary<string, object?> ExtractRegex(
        string? bodyString,
        IReadOnlyDictionary<string, string> patterns,
        ILogger? logger = null)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var (outputName, pattern) in patterns)
        {
            if (string.IsNullOrEmpty(bodyString) || string.IsNullOrWhiteSpace(pattern))
            {
                result[outputName] = null;
                continue;
            }

            try
            {
                // Timeout prevents catastrophic backtracking on hostile input~ 🛡️
                var match = Regex.Match(
                    bodyString,
                    pattern,
                    RegexOptions.None,
                    matchTimeout: TimeSpan.FromSeconds(5));

                if (!match.Success)
                {
                    result[outputName] = null;
                }
                else
                {
                    // Prefer the named "value" group; fall back to the full match~
                    var valueGroup = match.Groups["value"];
                    result[outputName] = valueGroup.Success ? valueGroup.Value : match.Value;
                }
            }
            catch (ArgumentException ae)
            {
                logger?.LogWarning(
                    "🔍 Regex pattern for output '{Output}' is invalid: {Error}~",
                    outputName, ae.Message);
                result[outputName] = null;
            }
            catch (RegexMatchTimeoutException)
            {
                logger?.LogWarning(
                    "🔍 Regex pattern for output '{Output}' timed out — possible catastrophic backtracking~ ⚠️",
                    outputName);
                result[outputName] = null;
            }
        }

        return result;
    }

    // =========================================================================
    // Header extraction 🏷️
    // =========================================================================

    /// <summary>
    /// Look up each header name in <paramref name="headerNameMap"/> from the flattened
    /// <paramref name="responseHeaders"/> dictionary and return a map of output port name → value.
    /// </summary>
    /// <remarks>
    /// Header name lookup is case-insensitive (the flattened headers dict uses
    /// <see cref="StringComparer.OrdinalIgnoreCase"/>). Missing headers produce <c>null</c>~ 🌸
    /// </remarks>
    /// <param name="responseHeaders">Flattened response headers (from <c>HttpRequestModule.FlattenHeaders</c>).</param>
    /// <param name="headerNameMap">Output port name → response header name map.</param>
    public static Dictionary<string, object?> ExtractHeaders(
        IReadOnlyDictionary<string, string> responseHeaders,
        IReadOnlyDictionary<string, string> headerNameMap)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var (outputName, headerName) in headerNameMap)
        {
            result[outputName] =
                responseHeaders.TryGetValue(headerName, out var val) ? val : (object?)null;
        }

        return result;
    }

    // =========================================================================
    // Helpers 🛠️
    // =========================================================================

    /// <summary>
    /// Convert a <see cref="JsonNode"/> to a POCO value by round-tripping through
    /// <see cref="ResponseBodyDecoder.JsonElementToObject"/> for consistency~ 🔧.
    /// </summary>
    private static object? JsonNodeToObject(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        try
        {
            // Round-trip via JsonElement so we reuse the existing unwrap logic (dict/list/primitive)~
            using var doc = JsonDocument.Parse(node.ToJsonString());
            return ResponseBodyDecoder.JsonElementToObject(doc.RootElement);
        }
        catch (JsonException)
        {
            return node.ToString();
        }
    }
}


