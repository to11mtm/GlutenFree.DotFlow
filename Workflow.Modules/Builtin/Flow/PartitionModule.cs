// <copyright file="PartitionModule.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.Flow;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;

/// <summary>
/// 🪓 Built-in partition module (<c>builtin.partition</c>)~
/// Splits a collection into named legs by per-item routing rules~ ✨💖
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: Outputs are intentionally EMPTY — ports are dynamic (one per rule port + optional default).
/// <c>ValidateConnectionPorts</c> skips validation for modules with no declared outputs~ 🎗️
/// </para>
/// </remarks>
public sealed class PartitionModule : IWorkflowModule
{
    /// <inheritdoc />
    public string ModuleId => "builtin.partition";

    /// <inheritdoc />
    public string DisplayName => "Partition";

    /// <inheritdoc />
    public string Category => "Flow Control";

    /// <inheritdoc />
    public string Description => "Splits a collection into named legs by matching each item against rules~ 🪓✨";

    /// <inheritdoc />
    public string Icon => "🪓";

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    /// <remarks>
    /// CopilotNote: Outputs are dynamic; empty outputs cause ValidateConnectionPorts to skip port-name validation~ 🎗️
    /// </remarks>
    public ModuleSchema Schema => new(
        Inputs: Arr.create(
            new PortDefinition("items", "Items", typeof(object), "Collection to split; each element becomes one routed item (overrides items property when connected)~ 📦", false)),
        Outputs: Arr<PortDefinition>.Empty,
        Properties: Arr.create(
            new ModulePropertyDefinition("items", "Items", typeof(object), "Static items to split; each element becomes one routed item (input port wins when connected)~ 📦", false, null, PropertyEditorType.Json),
            new ModulePropertyDefinition("rules", "Rules", typeof(object), "JSON array of {match, port} objects. First match wins for each item~ 📋", true, null, PropertyEditorType.Json),
            new ModulePropertyDefinition("matchOn", "Match On", typeof(string), "Optional dot-path to read from each item before matching (blank = item itself)~ 🔎", false, null, PropertyEditorType.Text),
            new ModulePropertyDefinition("defaultPort", "Default Port", typeof(string), "Output leg for unmatched items. Leave empty to fail on any unmatched item~ 🎯", false, null, PropertyEditorType.Text),
            new ModulePropertyDefinition("caseSensitive", "Case Sensitive", typeof(bool), "Whether string comparison is case-sensitive (default false)~ 🔡", false, false, PropertyEditorType.Boolean)));

    /// <inheritdoc />
    public ValidationResult ValidateConfiguration(IReadOnlyDictionary<string, object?> configuration)
    {
        if (!configuration.TryGetValue("rules", out var rulesRaw) || rulesRaw is null)
        {
            return ValidationResult.Failure(
                new ValidationError("MISSING_RULES", "The 'rules' property is required~ 💔", "rules"));
        }

        var rules = ParseRules(rulesRaw, out var parseError);
        if (parseError is not null)
        {
            return ValidationResult.Failure(
                new ValidationError("INVALID_RULES", $"Cannot parse 'rules': {parseError}~ 💔", "rules"));
        }

        if (rules is null || rules.Count == 0)
        {
            return ValidationResult.Failure(
                new ValidationError("EMPTY_RULES", "The 'rules' array must contain at least one entry~ 💔", "rules"));
        }

        if (rules.Any(r => !r.TryGetValue("port", out var portObj) || portObj is not string port || string.IsNullOrWhiteSpace(port)))
        {
            return ValidationResult.Failure(
                new ValidationError("INVALID_RULE_PORT", "Each rule entry must include a non-blank 'port'~ 💔", "rules"));
        }

        return ValidationResult.Success();
    }

    /// <inheritdoc />
    public Task<ModuleResult> ExecuteAsync(ModuleExecutionContext context, CancellationToken cancellationToken = default)
    {
        var rawItems = context.Inputs.TryGetValue("items", out var inputItems) && inputItems != null
            ? inputItems
            : context.Properties.TryGetValue("items", out var propItems) ? propItems : null;

        if (rawItems is null)
        {
            return Task.FromResult(ModuleResult.Fail("PartitionModule: 'items' input or property is required but was null~ 🪓❌"));
        }

        IReadOnlyList<object?> items;
        try
        {
            items = CoerceToList(rawItems);
        }
        catch (Exception ex)
        {
            return Task.FromResult(ModuleResult.Fail(
                $"PartitionModule: could not coerce 'items' to a list: {ex.Message}~ ❌", ex));
        }

        if (!context.Properties.TryGetValue("rules", out var rulesRaw) || rulesRaw is null)
        {
            return Task.FromResult(ModuleResult.Fail("The 'rules' property is required~ 💔"));
        }

        var rules = ParseRules(rulesRaw, out var parseError);
        if (parseError is not null)
        {
            return Task.FromResult(ModuleResult.Fail($"Cannot parse 'rules': {parseError}~ 💔"));
        }

        if (rules is null || rules.Count == 0)
        {
            return Task.FromResult(ModuleResult.Fail("The 'rules' array must contain at least one entry~ 💔"));
        }

        var defaultPort = context.Properties.TryGetValue("defaultPort", out var dpRaw) && dpRaw is string dp
            ? dp.Trim()
            : string.Empty;
        var matchOn = context.Properties.TryGetValue("matchOn", out var moRaw) && moRaw is string mo
            ? mo.Trim()
            : string.Empty;
        var comparison = context.Properties.TryGetValue("caseSensitive", out var csRaw) && csRaw is bool csBool && csBool
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        var ports = rules
            .Select(r => r.TryGetValue("port", out var portObj) ? portObj as string : null)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (!string.IsNullOrWhiteSpace(defaultPort) && !ports.Contains(defaultPort, StringComparer.Ordinal))
        {
            ports.Add(defaultPort);
        }

        var buckets = ports.ToDictionary(p => p, _ => new List<object?>(), StringComparer.Ordinal);
        var unmatched = new List<string>();

        foreach (var item in items)
        {
            var matchValue = ResolveMatchValue(item, matchOn, out var found);
            var valueStr = found ? ConvertToString(matchValue) : "<missing>";
            string? matchedPort = null;

            if (found)
            {
                foreach (var rule in rules)
                {
                    if (!rule.TryGetValue("match", out var matchObj))
                    {
                        continue;
                    }

                    if (string.Equals(valueStr, ConvertToString(matchObj), comparison)
                        && rule.TryGetValue("port", out var portObj)
                        && portObj is string port
                        && !string.IsNullOrWhiteSpace(port))
                    {
                        matchedPort = port.Trim();
                        break;
                    }
                }
            }

            if (matchedPort is not null)
            {
                buckets[matchedPort].Add(item);
            }
            else if (!string.IsNullOrWhiteSpace(defaultPort))
            {
                buckets[defaultPort].Add(item);
            }
            else
            {
                unmatched.Add(valueStr);
            }
        }

        if (unmatched.Count > 0)
        {
            return Task.FromResult(ModuleResult.Fail(
                $"PartitionModule: unmatched item values {FormatUnmatched(unmatched)} and no defaultPort is configured~ 💔"));
        }

        var counts = buckets.ToDictionary(kv => kv.Key, kv => kv.Value.Count, StringComparer.Ordinal);
        var outputs = buckets.ToDictionary(kv => kv.Key, kv => (object?)kv.Value, StringComparer.Ordinal);
        outputs["counts"] = counts;
        outputs["total"] = items.Count;

        return Task.FromResult(ModuleResult.Ok(outputs));
    }

    private static List<Dictionary<string, object?>>? ParseRules(object rulesRaw, out string? error)
    {
        error = null;

        if (rulesRaw is List<object?> list)
        {
            return ConvertRuleList(list, out error);
        }

        if (rulesRaw is JsonElement element && element.ValueKind == JsonValueKind.Array)
        {
            return ConvertRuleList(element.EnumerateArray().Select(e => ConvertJsonObject(e)).Cast<object?>().ToList(), out error);
        }

        if (rulesRaw is string json)
        {
            try
            {
                var deserialized = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(json);
                if (deserialized is null)
                {
                    error = "deserialized to null";
                    return null;
                }

                return deserialized.Select(d => d.ToDictionary(
                    kv => kv.Key,
                    kv => ConvertJsonElement(kv.Value))).ToList();
            }
            catch (JsonException ex)
            {
                error = ex.Message;
                return null;
            }
        }

        error = $"Expected JSON string or list, got {rulesRaw.GetType().Name}";
        return null;
    }

    private static List<Dictionary<string, object?>>? ConvertRuleList(List<object?> list, out string? error)
    {
        error = null;
        var result = new List<Dictionary<string, object?>>();
        foreach (var item in list)
        {
            if (item is Dictionary<string, object?> dict)
            {
                result.Add(dict);
            }
            else
            {
                error = $"Each rule entry must be an object (got {item?.GetType().Name ?? "null"})";
                return null;
            }
        }

        return result;
    }

    private static object? ResolveMatchValue(object? item, string matchOn, out bool found)
    {
        if (string.IsNullOrWhiteSpace(matchOn))
        {
            found = true;
            return item;
        }

        object? current = item;
        foreach (var segment in matchOn.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (current is Dictionary<string, object?> dict && dict.TryGetValue(segment, out var dictValue))
            {
                current = dictValue;
            }
            else if (current is JsonElement element && element.ValueKind == JsonValueKind.Object && element.TryGetProperty(segment, out var property))
            {
                current = property;
            }
            else
            {
                found = false;
                return null;
            }
        }

        found = true;
        return current is JsonElement je ? ConvertJsonElement(je) : current;
    }

    private static string FormatUnmatched(List<string> unmatched)
    {
        var sample = unmatched.Take(5).ToList();
        var suffix = unmatched.Count > sample.Count ? $" (+{unmatched.Count - sample.Count} more, {unmatched.Count} total)" : string.Empty;
        return $"[{string.Join(", ", sample)}]{suffix}";
    }

    private static string ConvertToString(object? value)
    {
        return value switch
        {
            null => string.Empty,
            string s => s,
            bool b => b.ToString(CultureInfo.InvariantCulture).ToLowerInvariant(),
            JsonElement je when je.ValueKind is JsonValueKind.Object or JsonValueKind.Array => je.ToString(),
            JsonElement je => ConvertToString(ConvertJsonElement(je)),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };
    }

    private static IReadOnlyList<object?> CoerceToList(object raw)
    {
        if (raw is IReadOnlyList<object?> readOnly)
        {
            return readOnly;
        }

        if (raw is IEnumerable enumerable and not string)
        {
            return enumerable.Cast<object?>().ToList();
        }

        if (raw is JsonElement je && je.ValueKind == JsonValueKind.Array)
        {
            return je.EnumerateArray().Select(e => (object?)ConvertJsonElement(e)).ToList();
        }

        if (raw is string s && s.TrimStart().StartsWith('['))
        {
            return JsonSerializer.Deserialize<List<JsonElement>>(s)
                ?.Select(e => (object?)ConvertJsonElement(e)).ToList()
                ?? new List<object?>();
        }

        return new List<object?> { raw };
    }

    private static Dictionary<string, object?> ConvertJsonObject(JsonElement element)
        => element.EnumerateObject().ToDictionary(p => p.Name, p => ConvertJsonElement(p.Value));

    private static object? ConvertJsonElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Object => element,
        JsonValueKind.Array => element.EnumerateArray().Select(e => ConvertJsonElement(e)).ToList(),
        _ => element.GetRawText(),
    };
}
