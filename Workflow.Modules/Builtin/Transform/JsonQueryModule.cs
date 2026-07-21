// <copyright file="JsonQueryModule.cs" company="GlutenFree">
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
using JPath = Json.Path.JsonPath;

/// <summary>
/// 🎯 Built-in JSON Query module (<c>builtin.transform.jsonquery</c>) — evaluates a JSONPath
/// expression over a data value (D10)~ ✨.
/// </summary>
public sealed class JsonQueryModule : IWorkflowModule
{
    /// <inheritdoc />
    public string ModuleId => "builtin.transform.jsonquery";

    /// <inheritdoc />
    public string DisplayName => "JSON Query (JSONPath)";

    /// <inheritdoc />
    public string Category => "Transformation";

    /// <inheritdoc />
    public string Description => "Evaluates a JSONPath expression over JSON data~ 🎯✨";

    /// <inheritdoc />
    public string Icon => "🎯";

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public ModuleSchema Schema => new(
        Inputs: Arr.create(
            new PortDefinition("data", "Data", typeof(object), "JSON data (object/array or JSON string)~ 📥", false)),
        Outputs: Arr.create(
            new PortDefinition("result", "Result", typeof(object), "Matched value(s)~ 📤", false),
            new PortDefinition("matchCount", "Match Count", typeof(int), "Number of matches~ 🔢", false),
            new PortDefinition("success", "Success", typeof(bool), "Whether the query succeeded~ ✅", false)),
        Properties: Arr.create(
            new ModulePropertyDefinition("data", "Data", typeof(object), "JSON data when not connected via port~ 📥", false, null, PropertyEditorType.Json),
            new ModulePropertyDefinition("path", "JSONPath", typeof(string), "JSONPath expression (e.g. $.items[?(@.price > 10)])~ 🎯", true, null, PropertyEditorType.Text),
            new ModulePropertyDefinition("required", "Required", typeof(bool), "Fail when there are no matches~ ❗", false, false, PropertyEditorType.Boolean)));

    /// <inheritdoc />
    public ValidationResult ValidateConfiguration(IReadOnlyDictionary<string, object?> configuration)
    {
        var path = TransformSupport.GetString(configuration, "path");
        if (path is null)
        {
            return ValidationResult.Failure(new ValidationError("PATH_REQUIRED", "path is required~ 💔", PropertyName: "path"));
        }

        try
        {
            JPath.Parse(path);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return ValidationResult.Failure(new ValidationError("PATH_INVALID", $"invalid JSONPath: {ex.Message}~ 💔", PropertyName: "path"));
        }

        return ValidationResult.Success();
    }

    /// <inheritdoc />
    public Task<ModuleResult> ExecuteAsync(
        ModuleExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var path = TransformSupport.GetString(context.Properties, "path");
        if (path is null)
        {
            return Task.FromResult(ModuleResult.Fail("🎯 path is required~ 💔"));
        }

        var required = TransformSupport.GetBool(context.Properties, "required");
        var raw = TransformSupport.ReadData(context, "data");

        var sw = Stopwatch.StartNew();
        try
        {
            var node = ToJsonNode(raw);
            var jsonPath = JPath.Parse(path);
            var matches = jsonPath.Evaluate(node).Matches;

            var values = matches.Select(m => JsonValueConverter.ToClr(m.Value)).ToList();
            sw.Stop();

            if (values.Count == 0 && required)
            {
                return Task.FromResult(ModuleResult.Fail($"🎯 JSONPath '{path}' matched nothing (required)~ 💔"));
            }

            object? result = values.Count == 1 ? values[0] : values;
            return Task.FromResult(ModuleResult.Ok(
                new Dictionary<string, object?>
                {
                    ["result"] = result,
                    ["matchCount"] = values.Count,
                    ["success"] = true,
                },
                ExecutionMetrics.FromDuration(sw.Elapsed)));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return Task.FromResult(ModuleResult.Fail($"🎯 JSON query failed: {ex.Message}~ 💔", ex));
        }
    }

    private static JsonNode? ToJsonNode(object? raw)
    {
        var normalized = TransformDataNormalizer.Normalize(raw);
        return normalized switch
        {
            null => null,
            string s => JsonNode.Parse(s),
            _ => System.Text.Json.JsonSerializer.SerializeToNode(normalized),
        };
    }
}
