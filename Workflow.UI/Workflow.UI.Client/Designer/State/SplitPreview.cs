// <copyright file="SplitPreview.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Designer.State;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

/// <summary>
/// 🧩 Split preview (V1) — replays <c>builtin.split</c>'s semantics client-side so the designer
/// can show which keys land on which port <em>before</em> a run: <c>outputs[key] = obj[key]</c>,
/// missing key → <c>null</c> (flagged), remainder → <c>restPort</c> (empty object when nothing
/// remains). Deliberately mirrors <c>SplitModule.ExecuteAsync</c> — a drift-guard fixture asserts
/// both sides (<c>FanInShapeDriftGuardTests</c>-style). When the sample can't be known (upstream
/// value) but the upstream <b>shape</b> can (a merged node), <see cref="ComputeFromShape"/> gives
/// a keys-only preview with no replay at all. Framework-free (D2)~ ✨.
/// </summary>
public static class SplitPreview
{
    /// <summary>The designer-only metadata key holding a persisted sample input (Q2)~ 💾.</summary>
    public const string SampleMetadataKey = "ui.sampleInput";

    /// <summary>Maximum rendered value length before truncation~ ✂️.</summary>
    public const int MaxValueLength = 60;

    /// <summary>One preview row: a port and what would land on it~ 🎫.</summary>
    /// <param name="Port">The output port.</param>
    /// <param name="ValuePreview">The compact value rendering (or a shape note).</param>
    /// <param name="IsMissing">True when the key is absent from the sample/shape (emits <c>null</c> at runtime).</param>
    /// <param name="IsRest">True for the rest-port row.</param>
    public sealed record Row(string Port, string ValuePreview, bool IsMissing, bool IsRest);

    /// <summary>A computed preview~ 📦.</summary>
    /// <param name="Success">Whether the sample parsed as an object.</param>
    /// <param name="Error">The parse/shape error when not successful.</param>
    /// <param name="Rows">The per-port rows (keys in declaration order, then rest).</param>
    public sealed record Result(bool Success, string? Error, IReadOnlyList<Row> Rows)
    {
        /// <summary>A failed preview~ 💔.</summary>
        /// <param name="error">The reason.</param>
        /// <returns>The result.</returns>
        public static Result Fail(string error) => new(false, error, Array.Empty<Row>());
    }

    /// <summary>
    /// Replays the split against a sample JSON object — the same rules as
    /// <c>SplitModule.ExecuteAsync</c> (missing key → null, rest = unlisted properties)~ 🧮.
    /// </summary>
    /// <param name="sampleJson">The sample object as JSON text.</param>
    /// <param name="keys">The configured keys.</param>
    /// <param name="restPort">The configured rest port, or null/blank.</param>
    /// <returns>The preview.</returns>
    public static Result Compute(string sampleJson, IReadOnlyList<string> keys, string? restPort)
    {
        if (string.IsNullOrWhiteSpace(sampleJson))
        {
            return Result.Fail("Paste a sample object to preview the split.");
        }

        JsonElement obj;
        try
        {
            using var doc = JsonDocument.Parse(sampleJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Result.Fail($"The sample must be a JSON object (got {doc.RootElement.ValueKind}).");
            }

            obj = doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            return Result.Fail($"Sample isn't valid JSON: {ex.Message}");
        }

        var rows = new List<Row>();
        foreach (var key in keys)
        {
            var present = obj.TryGetProperty(key, out var value);
            rows.Add(new Row(key, present ? Render(value) : "null", IsMissing: !present, IsRest: false));
        }

        var rest = NormalizedRestPort(restPort);
        if (rest is not null)
        {
            var keySet = keys.ToHashSet(StringComparer.Ordinal);
            var remainder = obj.EnumerateObject().Where(p => !keySet.Contains(p.Name)).ToList();
            var preview = remainder.Count == 0
                ? "{ } (nothing remains)"
                : Truncate("{ " + string.Join(", ", remainder.Select(p => $"\"{p.Name}\": {p.Value.GetRawText()}")) + " }");
            rows.Add(new Row(rest, preview, IsMissing: false, IsRest: true));
        }

        return new Result(true, null, rows);
    }

    /// <summary>
    /// Keys-only preview when the sample values are unknowable but the arriving <b>shape</b> is —
    /// e.g. the split's input is wired from a merged upstream node whose keys are its schema
    /// ports. No replay involved: pure set arithmetic~ 🔑.
    /// </summary>
    /// <param name="availableKeys">The keys known to exist on the arriving object.</param>
    /// <param name="keys">The configured keys.</param>
    /// <param name="restPort">The configured rest port, or null/blank.</param>
    /// <returns>The preview.</returns>
    public static Result ComputeFromShape(IReadOnlyList<string> availableKeys, IReadOnlyList<string> keys, string? restPort)
    {
        var available = availableKeys.ToHashSet(StringComparer.Ordinal);
        var rows = keys
            .Select(k => new Row(
                k,
                available.Contains(k) ? "(value from upstream)" : "null",
                IsMissing: !available.Contains(k),
                IsRest: false))
            .ToList();

        var rest = NormalizedRestPort(restPort);
        if (rest is not null)
        {
            var keySet = keys.ToHashSet(StringComparer.Ordinal);
            var remainder = availableKeys.Where(k => !keySet.Contains(k)).ToList();
            rows.Add(new Row(
                rest,
                remainder.Count == 0 ? "{ } (nothing remains)" : "{ " + string.Join(", ", remainder) + " }",
                IsMissing: false,
                IsRest: true));
        }

        return new Result(true, null, rows);
    }

    /// <summary>Parses the split node's configured keys (same JSON-or-JSON-string tolerance as the module)~ 📋.</summary>
    /// <param name="node">The split node.</param>
    /// <returns>The keys, or empty when unconfigured/unparseable.</returns>
    public static IReadOnlyList<string> KeysFor(DesignerNode node)
        => node.Properties.TryGetValue("keys", out var raw) ? ParseKeys(raw) : Array.Empty<string>();

    /// <summary>Parses a keys value (JSON array, possibly stored as a JSON string)~ 📋.</summary>
    /// <param name="raw">The raw property value.</param>
    /// <returns>The keys, or empty.</returns>
    public static IReadOnlyList<string> ParseKeys(JsonElement raw)
    {
        var el = raw;
        if (raw.ValueKind == JsonValueKind.String && raw.GetString() is { Length: > 0 } s)
        {
            try
            {
                using var doc = JsonDocument.Parse(s);
                el = doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                return Array.Empty<string>();
            }
        }

        return el.ValueKind == JsonValueKind.Array
            ? el.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .ToList()
            : Array.Empty<string>();
    }

    /// <summary>The split node's configured rest port, or null~ 🎯.</summary>
    /// <param name="node">The split node.</param>
    /// <returns>The trimmed rest port name, or null.</returns>
    public static string? RestPortFor(DesignerNode node)
        => node.Properties.TryGetValue("restPort", out var v)
           && v.ValueKind == JsonValueKind.String
            ? NormalizedRestPort(v.GetString())
            : null;

    /// <summary>The node's persisted sample (from <c>ui.sampleInput</c> metadata), or null~ 💾.</summary>
    /// <param name="node">The split node.</param>
    /// <returns>The stored sample JSON text, or null.</returns>
    public static string? PersistedSample(DesignerNode node)
        => node.Metadata.TryGetValue(SampleMetadataKey, out var s) && !string.IsNullOrWhiteSpace(s) ? s : null;

    /// <summary>
    /// The keys derivable for the split's arriving value when no sample is needed: its <c>value</c>
    /// input wired from a <b>merged</b> upstream node → that node's schema ports (Q1a)~ 🔌.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="node">The split node.</param>
    /// <returns>The upstream shape keys, or null when not derivable.</returns>
    public static IReadOnlyList<string>? UpstreamShapeKeys(DesignerDocument document, DesignerNode node)
    {
        var conn = document.Connections.FirstOrDefault(c =>
            c.TargetNodeId == node.Id && string.Equals(c.TargetPortName, "value", StringComparison.OrdinalIgnoreCase));
        if (conn is null || document.FindNode(conn.SourceNodeId) is not { } source)
        {
            return null;
        }

        if (OutputShapingUx.IsMerged(source)
            && string.Equals(conn.SourcePortName, OutputShapingUx.MergedPortName, StringComparison.OrdinalIgnoreCase)
            && source.Schema is { Outputs.Count: > 0 } schema)
        {
            return schema.Outputs.Select(p => p.Name).ToList();
        }

        return null;
    }

    private static string? NormalizedRestPort(string? restPort)
        => string.IsNullOrWhiteSpace(restPort) ? null : restPort.Trim();

    private static string Render(JsonElement value) => Truncate(value.GetRawText());

    private static string Truncate(string text)
        => text.Length <= MaxValueLength ? text : text[..MaxValueLength] + "…";
}
