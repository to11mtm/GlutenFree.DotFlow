// <copyright file="JsonTransformModule.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.Transform;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin.Transform.Internal;
using Workflow.Modules.Internal;

/// <summary>
/// 📝 Built-in JSON Transform module (<c>builtin.transform.json</c>) — structural JSON operations:
/// merge, patch, diff, flatten, unflatten (D10)~ ✨.
/// </summary>
public sealed class JsonTransformModule : IWorkflowModule
{
    private static readonly System.Collections.Generic.HashSet<string> KnownOps = new(StringComparer.OrdinalIgnoreCase)
    {
        "merge", "patch", "diff", "flatten", "unflatten",
    };

    /// <inheritdoc />
    public string ModuleId => "builtin.transform.json";

    /// <inheritdoc />
    public string DisplayName => "JSON Transform";

    /// <inheritdoc />
    public string Category => "Transformation";

    /// <inheritdoc />
    public string Description => "Merge, patch, diff, flatten, or unflatten JSON~ 📝✨";

    /// <inheritdoc />
    public string Icon => "📝";

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public ModuleSchema Schema => new(
        Inputs: Arr.create(
            new PortDefinition("data", "Data", typeof(object), "Primary JSON operand~ 📥", false),
            new PortDefinition("other", "Other", typeof(object), "Second operand (merge/patch/diff)~ 📥", false)),
        Outputs: Arr.create(
            new PortDefinition("result", "Result", typeof(object), "Transformed JSON~ 📤", false),
            new PortDefinition("success", "Success", typeof(bool), "Whether the transform succeeded~ ✅", false)),
        Properties: Arr.create(
            new ModulePropertyDefinition("data", "Data", typeof(object), "Primary operand when not connected~ 📥", false, null, PropertyEditorType.Json),
            new ModulePropertyDefinition("operation", "Operation", typeof(string), "merge/patch/diff/flatten/unflatten~ 📝", true, "merge", PropertyEditorType.Dropdown),
            new ModulePropertyDefinition("other", "Other", typeof(object), "Second operand when not connected~ 📥", false, null, PropertyEditorType.Json),
            new ModulePropertyDefinition("separator", "Separator", typeof(string), "Key separator for flatten/unflatten~ 🔣", false, ".", PropertyEditorType.Text)));

    /// <inheritdoc />
    public ValidationResult ValidateConfiguration(IReadOnlyDictionary<string, object?> configuration)
    {
        var op = TransformSupport.GetString(configuration, "operation");
        if (op is null || !KnownOps.Contains(op))
        {
            return ValidationResult.Failure(new ValidationError("INVALID_OPERATION", $"operation must be one of: {string.Join(", ", KnownOps)}~ 💔", PropertyName: "operation"));
        }

        return ValidationResult.Success();
    }

    /// <inheritdoc />
    public Task<ModuleResult> ExecuteAsync(
        ModuleExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var operation = (TransformSupport.GetString(context.Properties, "operation") ?? string.Empty).ToLowerInvariant();
        if (!KnownOps.Contains(operation))
        {
            return Task.FromResult(ModuleResult.Fail($"📝 Unknown operation '{operation}'~ 💔"));
        }

        var separator = TransformSupport.GetString(context.Properties, "separator") ?? ".";

        var sw = Stopwatch.StartNew();
        try
        {
            var data = ToNode(TransformSupport.ReadData(context, "data"));
            object? result = operation switch
            {
                "merge" => JsonValueConverter.ToClr(Merge(data, ToNode(TransformSupport.ReadData(context, "other")))),
                "patch" => JsonValueConverter.ToClr(Merge(data, ToNode(TransformSupport.ReadData(context, "other")))),
                "diff" => Diff(data, ToNode(TransformSupport.ReadData(context, "other"))),
                "flatten" => Flatten(JsonValueConverter.ToClr(data), separator),
                "unflatten" => Unflatten(JsonValueConverter.ToClr(data), separator),
                _ => null,
            };

            sw.Stop();
            return Task.FromResult(ModuleResult.Ok(
                new Dictionary<string, object?> { ["result"] = result, ["success"] = true },
                ExecutionMetrics.FromDuration(sw.Elapsed)));
        }
        catch (Exception ex) when (ex is FormatException or System.Text.Json.JsonException or InvalidOperationException)
        {
            return Task.FromResult(ModuleResult.Fail($"📝 JSON transform failed: {ex.Message}~ 💔", ex));
        }
    }

    private static JsonNode? ToNode(object? raw)
    {
        var normalized = TransformDataNormalizer.Normalize(raw);
        return normalized switch
        {
            null => null,
            string s => JsonNode.Parse(s),
            _ => System.Text.Json.JsonSerializer.SerializeToNode(normalized),
        };
    }

    // RFC 7396 merge-patch: null removes a key~ 🧩
    private static JsonNode? Merge(JsonNode? target, JsonNode? patch)
    {
        if (patch is not JsonObject patchObj)
        {
            return patch?.DeepClone();
        }

        var result = target is JsonObject targetObj ? (JsonObject)targetObj.DeepClone() : new JsonObject();
        foreach (var kvp in patchObj)
        {
            if (kvp.Value is null)
            {
                result.Remove(kvp.Key);
            }
            else
            {
                result[kvp.Key] = Merge(result.TryGetPropertyValue(kvp.Key, out var existing) ? existing : null, kvp.Value);
            }
        }

        return result;
    }

    private static List<object?> Diff(JsonNode? left, JsonNode? right)
    {
        var changes = new List<object?>();
        DiffInto(string.Empty, left, right, changes);
        return changes;
    }

    private static void DiffInto(string path, JsonNode? left, JsonNode? right, List<object?> changes)
    {
        var leftObj = left as JsonObject;
        var rightObj = right as JsonObject;

        if (leftObj is not null && rightObj is not null)
        {
            var keys = leftObj.Select(k => k.Key).Union(rightObj.Select(k => k.Key));
            foreach (var key in keys)
            {
                var childPath = path.Length == 0 ? key : path + "." + key;
                var hasLeft = leftObj.TryGetPropertyValue(key, out var lv);
                var hasRight = rightObj.TryGetPropertyValue(key, out var rv);
                if (!hasLeft)
                {
                    changes.Add(Change("add", childPath, rv));
                }
                else if (!hasRight)
                {
                    changes.Add(Change("remove", childPath, null));
                }
                else
                {
                    DiffInto(childPath, lv, rv, changes);
                }
            }

            return;
        }

        if (!JsonNodeEquals(left, right))
        {
            changes.Add(Change("replace", path, right));
        }
    }

    private static Dictionary<string, object?> Change(string op, string path, JsonNode? value)
        => new() { ["op"] = op, ["path"] = path, ["value"] = value is null ? null : JsonValueConverter.ToClr(value) };

    private static bool JsonNodeEquals(JsonNode? a, JsonNode? b)
        => (a?.ToJsonString() ?? "null") == (b?.ToJsonString() ?? "null");

    private static Dictionary<string, object?> Flatten(object? value, string separator)
    {
        var result = new Dictionary<string, object?>();
        FlattenInto(string.Empty, value, separator, result);
        return result;
    }

    private static void FlattenInto(string prefix, object? value, string separator, Dictionary<string, object?> result)
    {
        switch (value)
        {
            case IReadOnlyDictionary<string, object?> dict:
                foreach (var (key, v) in dict)
                {
                    FlattenInto(prefix.Length == 0 ? key : prefix + separator + key, v, separator, result);
                }

                break;
            case IReadOnlyList<object?> list:
                for (var i = 0; i < list.Count; i++)
                {
                    FlattenInto($"{prefix}{separator}{i}", list[i], separator, result);
                }

                break;
            default:
                result[prefix] = value;
                break;
        }
    }

    private static Dictionary<string, object?> Unflatten(object? value, string separator)
    {
        var result = new Dictionary<string, object?>();
        if (value is not IReadOnlyDictionary<string, object?> flat)
        {
            return result;
        }

        foreach (var (key, v) in flat)
        {
            var segments = key.Split(separator);
            var current = result;
            for (var i = 0; i < segments.Length - 1; i++)
            {
                if (current.TryGetValue(segments[i], out var next) && next is Dictionary<string, object?> nextDict)
                {
                    current = nextDict;
                }
                else
                {
                    var created = new Dictionary<string, object?>();
                    current[segments[i]] = created;
                    current = created;
                }
            }

            current[segments[^1]] = v;
        }

        return result;
    }
}
