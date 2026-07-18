// <copyright file="DataMapModule.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.Transform;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin.Transform.Internal;

/// <summary>
/// 🔄 Built-in Data Map module (<c>builtin.transform.map</c>) — declarative per-record reshaping:
/// rename, nested access, defaults, type conversion, and computed expressions~ ✨.
/// </summary>
public sealed class DataMapModule : IWorkflowModule
{
    /// <inheritdoc />
    public string ModuleId => "builtin.transform.map";

    /// <inheritdoc />
    public string DisplayName => "Map Data";

    /// <inheritdoc />
    public string Category => "Transformation";

    /// <inheritdoc />
    public string Description => "Reshapes records: rename, nested access, defaults, convert, compute~ 🔄✨";

    /// <inheritdoc />
    public string Icon => "🔄";

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public ModuleSchema Schema => new(
        Inputs: Arr.create(
            new PortDefinition("source", "Source", typeof(object), "Record or array of records to map~ 📥", false)),
        Outputs: Arr.create(
            new PortDefinition("result", "Result", typeof(object), "Mapped record(s)~ 📤", false),
            new PortDefinition("count", "Count", typeof(int), "Number of records mapped~ 🔢", false),
            new PortDefinition("success", "Success", typeof(bool), "Whether mapping succeeded~ ✅", false)),
        Properties: Arr.create(
            new ModulePropertyDefinition("source", "Source", typeof(object), "Source data when not connected via port~ 📥", false, null, PropertyEditorType.Json),
            new ModulePropertyDefinition("mapping", "Mapping", typeof(object), "targetField → path string or { path, expression, default, convert }~ 🗺️", true, null, PropertyEditorType.Json),
            new ModulePropertyDefinition("language", "Expression Language", typeof(string), "js (default) or csharp~ 🧮", false, "js", PropertyEditorType.Text),
            new ModulePropertyDefinition("flatten", "Flatten", typeof(bool), "Flatten nested output to dotted keys~ 📏", false, false, PropertyEditorType.Boolean),
            new ModulePropertyDefinition("ignoreNulls", "Ignore Nulls", typeof(bool), "Drop keys whose mapped value is null~ 🧹", false, false, PropertyEditorType.Boolean)));

    /// <inheritdoc />
    public ValidationResult ValidateConfiguration(IReadOnlyDictionary<string, object?> configuration)
    {
        if (!configuration.TryGetValue("mapping", out var mapping) || mapping is null)
        {
            return ValidationResult.Failure(new ValidationError("MAPPING_REQUIRED", "mapping is required~ 💔", PropertyName: "mapping"));
        }

        if (TransformDataNormalizer.Normalize(mapping) is not IReadOnlyDictionary<string, object?> map || map.Count == 0)
        {
            return ValidationResult.Failure(new ValidationError("MAPPING_INVALID", "mapping must be a non-empty object~ 💔", PropertyName: "mapping"));
        }

        return ValidationResult.Success();
    }

    /// <inheritdoc />
    public async Task<ModuleResult> ExecuteAsync(
        ModuleExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var raw = context.Inputs.TryGetValue("source", out var portVal) && portVal is not null
            ? portVal
            : context.Properties.TryGetValue("source", out var propVal) ? propVal : null;

        if (TransformDataNormalizer.Normalize(context.Properties.GetValueOrDefault("mapping")) is not IReadOnlyDictionary<string, object?> mapping)
        {
            return ModuleResult.Fail("🔄 mapping must be an object~ 💔");
        }

        var language = ReadString(context.Properties, "language");
        if (!ItemExpressionEvaluator.TryResolve(context, language, out var evaluator, out var evalFail))
        {
            return evalFail!;
        }

        var flatten = ReadBool(context.Properties, "flatten");
        var ignoreNulls = ReadBool(context.Properties, "ignoreNulls");

        var normalized = TransformDataNormalizer.Normalize(raw);
        var isArray = normalized is IReadOnlyList<object?>;
        var records = normalized switch
        {
            IReadOnlyList<object?> list => list,
            null => new List<object?>(),
            _ => new List<object?> { normalized },
        };

        var sw = Stopwatch.StartNew();
        try
        {
            var mapped = new List<object?>();
            var index = 0;
            foreach (var record in records)
            {
                mapped.Add(await this.MapRecord(context, evaluator, mapping, record, index, flatten, ignoreNulls, cancellationToken).ConfigureAwait(false));
                index++;
            }

            sw.Stop();

            object? result = isArray ? mapped : (mapped.Count > 0 ? mapped[0] : null);
            var outputs = new Dictionary<string, object?>
            {
                ["result"] = result,
                ["count"] = mapped.Count,
                ["success"] = true,
            };
            return ModuleResult.Ok(outputs, ExecutionMetrics.FromDuration(sw.Elapsed));
        }
        catch (TransformModuleException ex)
        {
            return ModuleResult.Fail($"🔄 Map failed: {ex.Message}~ 💔", ex);
        }
    }

    private async Task<Dictionary<string, object?>> MapRecord(
        ModuleExecutionContext context,
        ItemExpressionEvaluator evaluator,
        IReadOnlyDictionary<string, object?> mapping,
        object? record,
        int index,
        bool flatten,
        bool ignoreNulls,
        CancellationToken ct)
    {
        var scope = ItemExpressionEvaluator.Scope(context, record, index);
        var output = new Dictionary<string, object?>();

        foreach (var (targetField, specRaw) in mapping)
        {
            object? value;

            if (specRaw is string path)
            {
                value = DotPath.Resolve(record, path, out _);
            }
            else if (TransformDataNormalizer.Normalize(specRaw) is IReadOnlyDictionary<string, object?> spec)
            {
                if (spec.TryGetValue("expression", out var expr) && expr is string exprStr)
                {
                    value = await evaluator.EvalValueAsync(exprStr, scope, index, ct).ConfigureAwait(false);
                }
                else if (spec.TryGetValue("path", out var p) && p is string pathStr)
                {
                    value = DotPath.Resolve(record, pathStr, out var found);
                    if (!found && spec.TryGetValue("default", out var def))
                    {
                        value = def;
                    }
                }
                else
                {
                    throw new TransformModuleException($"mapping for '{targetField}' must set 'path' or 'expression'", index);
                }

                if (value is null && spec.TryGetValue("default", out var d))
                {
                    value = d;
                }

                if (spec.TryGetValue("convert", out var conv) && conv is string convType && value is not null)
                {
                    if (!TransformValueConverter.TryConvert(value, convType, out var converted, out var convErr))
                    {
                        throw new TransformModuleException($"field '{targetField}': {convErr}", index);
                    }

                    value = converted;
                }
            }
            else
            {
                throw new TransformModuleException($"mapping for '{targetField}' must be a path string or a spec object", index);
            }

            if (ignoreNulls && value is null)
            {
                continue;
            }

            output[targetField] = value;
        }

        return flatten ? Flatten(output) : output;
    }

    private static Dictionary<string, object?> Flatten(IReadOnlyDictionary<string, object?> source, string prefix = "")
    {
        var result = new Dictionary<string, object?>();
        foreach (var (key, value) in source)
        {
            var full = prefix.Length == 0 ? key : prefix + "." + key;
            if (value is IReadOnlyDictionary<string, object?> nested)
            {
                foreach (var kvp in Flatten(nested, full))
                {
                    result[kvp.Key] = kvp.Value;
                }
            }
            else
            {
                result[full] = value;
            }
        }

        return result;
    }

    private static string? ReadString(IReadOnlyDictionary<string, object?> props, string key)
        => props.TryGetValue(key, out var v) && v is not null ? v.ToString() : null;

    private static bool ReadBool(IReadOnlyDictionary<string, object?> props, string key)
        => props.TryGetValue(key, out var v) && v switch { bool b => b, string s => bool.TryParse(s, out var r) && r, _ => false };
}
