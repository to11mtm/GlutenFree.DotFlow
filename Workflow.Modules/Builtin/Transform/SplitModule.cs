// <copyright file="SplitModule.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.Transform;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;

/// <summary>
/// 🧩 Built-in split module (<c>builtin.split</c>)~
/// Splits an object's properties into separate output ports~ ✨💖
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: Outputs are intentionally EMPTY — ports are dynamic (one per key + optional restPort).
/// <c>ValidateConnectionPorts</c> skips validation for modules with no declared outputs~ 🎗️
/// </para>
/// <para>
/// ⚠️ <b>Semantics are mirrored in the designer.</b> The client-side
/// <c>Workflow.UI.Client\Designer\State\SplitPreview.cs</c> replays these exact rules
/// (missing key → <c>null</c>, rest = unlisted properties, empty rest object still emits) to show
/// a live preview without a server round-trip; shared fixtures in
/// <c>SplitPreviewDriftGuardTests</c> / <c>SplitPreviewTests</c> pin both sides. If this module's
/// behaviour changes, update the mirror and both fixture sets together — and if a preview API is
/// ever added (<c>POST /api/builtin/split/preview</c> was considered and deferred, see
/// <c>use-testing-feedback\Designer-Split-Preview-Plan.md</c> Q1), implement it by calling this
/// module so there is exactly one source of truth~ 🛡️
/// </para>
/// </remarks>
public sealed class SplitModule : IWorkflowModule
{
    /// <inheritdoc />
    public string ModuleId => "builtin.split";

    /// <inheritdoc />
    public string DisplayName => "Split";

    /// <inheritdoc />
    public string Category => "Transformation";

    /// <inheritdoc />
    public string Description => "Splits an object's properties into separate output ports~ 🧩✨";

    /// <inheritdoc />
    public string Icon => "🧩";

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    /// <remarks>
    /// CopilotNote: Outputs are dynamic; empty outputs cause ValidateConnectionPorts to skip port-name validation~ 🎗️
    /// </remarks>
    public ModuleSchema Schema => new(
        Inputs: Arr.create(
            new PortDefinition("value", "Value", typeof(object), "Object to split into per-property output ports (overrides value property when connected)~ 📥", false)),
        Outputs: Arr<PortDefinition>.Empty,
        Properties: Arr.create(
            new ModulePropertyDefinition("value", "Value", typeof(object), "Static object to split when no input port is connected~ 🧩", false, null, PropertyEditorType.Json),
            new ModulePropertyDefinition("keys", "Keys", typeof(object), "JSON array of property names; each name becomes an output port~ 🔑", true, null, PropertyEditorType.Json),
            new ModulePropertyDefinition("restPort", "Rest Port", typeof(string), "Optional output port for properties not listed in keys~ 🧺", false, null, PropertyEditorType.Text)));

    /// <inheritdoc />
    public ValidationResult ValidateConfiguration(IReadOnlyDictionary<string, object?> configuration)
    {
        if (!configuration.TryGetValue("keys", out var keysRaw) || keysRaw is null)
        {
            return ValidationResult.Failure(
                new ValidationError("MISSING_KEYS", "The 'keys' property is required~ 💔", "keys"));
        }

        var keys = ParseKeys(keysRaw, out var parseError);
        if (parseError is not null)
        {
            return ValidationResult.Failure(
                new ValidationError("INVALID_KEYS", $"Cannot parse 'keys': {parseError}~ 💔", "keys"));
        }

        if (keys is null || keys.Count == 0)
        {
            return ValidationResult.Failure(
                new ValidationError("EMPTY_KEYS", "The 'keys' array must contain at least one entry~ 💔", "keys"));
        }

        if (keys.Any(string.IsNullOrWhiteSpace))
        {
            return ValidationResult.Failure(
                new ValidationError("INVALID_KEY", "Each key must be a non-blank string~ 💔", "keys"));
        }

        if (keys.GroupBy(k => k, StringComparer.Ordinal).Any(g => g.Count() > 1))
        {
            return ValidationResult.Failure(
                new ValidationError("DUPLICATE_KEYS", "The 'keys' array must not contain duplicates~ 💔", "keys"));
        }

        if (configuration.TryGetValue("restPort", out var restRaw) && restRaw is string restPort && !string.IsNullOrWhiteSpace(restPort)
            && keys.Contains(restPort.Trim(), StringComparer.Ordinal))
        {
            return ValidationResult.Failure(
                new ValidationError("RESTPORT_COLLIDES", "The 'restPort' must not match any key output port~ 💔", "restPort"));
        }

        return ValidationResult.Success();
    }

    /// <inheritdoc />
    public Task<ModuleResult> ExecuteAsync(ModuleExecutionContext context, CancellationToken cancellationToken = default)
    {
        var rawValue = context.Inputs.TryGetValue("value", out var inputValue)
            ? inputValue
            : context.Properties.TryGetValue("value", out var propValue) ? propValue : null;

        if (rawValue is null)
        {
            return Task.FromResult(ModuleResult.Fail("SplitModule: 'value' input or property must be an object but was null~ 🧩❌"));
        }

        var obj = CoerceToObject(rawValue, out var objectError);
        if (objectError is not null || obj is null)
        {
            return Task.FromResult(ModuleResult.Fail($"SplitModule: 'value' must be an object: {objectError ?? "not an object"}~ 💔"));
        }

        if (!context.Properties.TryGetValue("keys", out var keysRaw) || keysRaw is null)
        {
            return Task.FromResult(ModuleResult.Fail("The 'keys' property is required~ 💔"));
        }

        var keys = ParseKeys(keysRaw, out var parseError);
        if (parseError is not null || keys is null || keys.Count == 0)
        {
            return Task.FromResult(ModuleResult.Fail($"Cannot parse 'keys': {parseError ?? "array must contain at least one entry"}~ 💔"));
        }

        var outputs = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            outputs[key] = obj.TryGetValue(key, out var value) ? ConvertIfJsonElement(value) : null;
        }

        if (context.Properties.TryGetValue("restPort", out var restRaw) && restRaw is string restPortRaw && !string.IsNullOrWhiteSpace(restPortRaw))
        {
            var restPort = restPortRaw.Trim();
            var keySet = keys.ToHashSet(StringComparer.Ordinal);
            outputs[restPort] = obj
                .Where(kv => !keySet.Contains(kv.Key))
                .ToDictionary(kv => kv.Key, kv => ConvertIfJsonElement(kv.Value), StringComparer.Ordinal);
        }

        return Task.FromResult(ModuleResult.Ok(outputs));
    }

    private static List<string>? ParseKeys(object keysRaw, out string? error)
    {
        error = null;

        if (keysRaw is List<object?> list)
        {
            return ConvertKeyList(list, out error);
        }

        if (keysRaw is JsonElement element && element.ValueKind == JsonValueKind.Array)
        {
            return ConvertKeyList(element.EnumerateArray().Select(e => ConvertJsonElement(e)).ToList(), out error);
        }

        if (keysRaw is string json)
        {
            try
            {
                var deserialized = JsonSerializer.Deserialize<List<string>>(json);
                if (deserialized is null)
                {
                    error = "deserialized to null";
                    return null;
                }

                return deserialized;
            }
            catch (JsonException ex)
            {
                error = ex.Message;
                return null;
            }
        }

        error = $"Expected JSON string or list, got {keysRaw.GetType().Name}";
        return null;
    }

    private static List<string>? ConvertKeyList(List<object?> list, out string? error)
    {
        error = null;
        var keys = new List<string>();
        foreach (var item in list)
        {
            if (item is string key)
            {
                keys.Add(key);
            }
            else
            {
                error = $"Each key must be a string (got {item?.GetType().Name ?? "null"})";
                return null;
            }
        }

        return keys;
    }

    private static Dictionary<string, object?>? CoerceToObject(object raw, out string? error)
    {
        error = null;

        if (raw is Dictionary<string, object?> dict)
        {
            return dict.ToDictionary(kv => kv.Key, kv => ConvertIfJsonElement(kv.Value), StringComparer.Ordinal);
        }

        if (raw is IReadOnlyDictionary<string, object?> readOnlyDict)
        {
            return readOnlyDict.ToDictionary(kv => kv.Key, kv => ConvertIfJsonElement(kv.Value), StringComparer.Ordinal);
        }

        if (raw is JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                error = $"JsonElement was {element.ValueKind}, not Object";
                return null;
            }

            return element.EnumerateObject().ToDictionary(p => p.Name, p => ConvertJsonElement(p.Value), StringComparer.Ordinal);
        }

        if (raw is string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    error = $"JSON string root was {doc.RootElement.ValueKind}, not Object";
                    return null;
                }

                return doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => ConvertJsonElement(p.Value), StringComparer.Ordinal);
            }
            catch (JsonException ex)
            {
                error = ex.Message;
                return null;
            }
        }

        error = $"got {raw.GetType().Name}";
        return null;
    }

    private static object? ConvertIfJsonElement(object? value)
        => value is JsonElement element ? ConvertJsonElement(element) : value;

    private static object? ConvertJsonElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => ConvertJsonElement(p.Value), StringComparer.Ordinal),
        JsonValueKind.Array => element.EnumerateArray().Select(e => ConvertJsonElement(e)).ToList(),
        _ => element.GetRawText(),
    };
}
