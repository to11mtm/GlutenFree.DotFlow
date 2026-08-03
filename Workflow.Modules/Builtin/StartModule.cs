// <copyright file="StartModule.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using Microsoft.Extensions.Logging;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;

/// <summary>
/// 🚀 Built-in Start module (<c>builtin.start</c>) — an explicit "the workflow begins here" marker
/// that emits one value for the rest of the graph~ ✨💖.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: The engine already starts every node with no incoming connections, so this module
/// adds no execution machinery — it exists to make a workflow <em>legible</em>. Having no input
/// ports means it can never be wired downstream of anything, so a Start node is always a start node.
/// </para>
/// <para>
/// The <c>value</c> property is template-enabled, which is the most useful thing about it: a Start
/// node can surface a run input or workflow variable as the graph's opening value, e.g.
/// <c>{{Variable.orderId}}</c>. Leave it blank for an empty (null) output~ 🌸.
/// </para>
/// </remarks>
public class StartModule : IWorkflowModule
{
    /// <summary>The value types a Start node can emit~ 🎨.</summary>
    private static readonly string[] KnownValueTypes = ["text", "number", "boolean", "json"];

    /// <inheritdoc />
    public string ModuleId => "builtin.start";

    /// <inheritdoc />
    public string DisplayName => "Start";

    /// <inheritdoc />
    public string Category => "Flow Control";

    /// <inheritdoc />
    public string Description => "Marks where a workflow begins and emits a starting value (or nothing)~ 🚀✨";

    /// <inheritdoc />
    public string Icon => "🚀";

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public ModuleSchema Schema => new(
        Inputs: Arr<PortDefinition>.Empty,
        Outputs: Arr.create(
            new PortDefinition(
                Name: "value",
                DisplayName: "Value",
                DataType: typeof(object),
                Description: "The starting value, or null when none was configured~ 🎁",
                IsRequired: false)),
        Properties: Arr.create(
            new ModulePropertyDefinition(
                Name: "value",
                DisplayName: "Value",
                DataType: typeof(string),
                Description: "The value to emit. Leave blank for an empty start. Supports "
                    + "{{Variable.Name}} references, so a run input can open the workflow~ 🎁",
                IsRequired: false,
                DefaultValue: null,
                EditorType: PropertyEditorType.Text,
                SupportsTemplates: true),
            new ModulePropertyDefinition(
                Name: "valueType",
                DisplayName: "Value Type",
                DataType: typeof(string),
                Description: "How to interpret the value: text, number, boolean, or json~ 🎨",
                IsRequired: false,
                DefaultValue: "text",
                EditorType: PropertyEditorType.Dropdown,
                AllowedValues: Arr.create<object>("text", "number", "boolean", "json"))));

    /// <summary>
    /// Validates the value type, and that a JSON value actually parses~ ✅.
    /// </summary>
    /// <param name="configuration">The node's configured properties.</param>
    /// <returns>The validation result.</returns>
    /// <remarks>
    /// Checking the JSON here means a malformed value is caught when the workflow is saved rather
    /// than when it runs — the whole point of a start value is that it's known up front.
    /// </remarks>
    public ValidationResult ValidateConfiguration(IReadOnlyDictionary<string, object?> configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var valueType = ReadString(configuration, "valueType") ?? "text";
        if (!Array.Exists(KnownValueTypes, t => string.Equals(t, valueType, StringComparison.OrdinalIgnoreCase)))
        {
            return ValidationResult.Failure(
                new ValidationError(
                    "INVALID_VALUE_TYPE",
                    $"Unknown value type '{valueType}'. Valid types: {string.Join(", ", KnownValueTypes)}~ 💔",
                    PropertyName: "valueType"));
        }

        var value = ReadString(configuration, "value");

        // A template can't be judged until it's resolved at run time — skip it here rather than
        // reject something that will be perfectly valid once bound.
        if (string.Equals(valueType, "json", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(value)
            && !value.Contains("{{", StringComparison.Ordinal))
        {
            try
            {
                using var _ = JsonDocument.Parse(value);
            }
            catch (JsonException ex)
            {
                return ValidationResult.Failure(
                    new ValidationError(
                        "INVALID_JSON_VALUE",
                        $"The start value isn't valid JSON: {ex.Message}~ 💔",
                        PropertyName: "value"));
            }
        }

        return ValidationResult.Success();
    }

    /// <inheritdoc />
    public Task<ModuleResult> ExecuteAsync(
        ModuleExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Properties arrive already template-resolved (PropertyBinder), so {{Variable.x}} has
        // become its value by the time we see it~ 🔗
        var raw = ReadString(context.Properties, "value");
        var valueType = ReadString(context.Properties, "valueType") ?? "text";

        var value = Convert(raw, valueType);

        context.Logger.LogInformation(
            "🚀 Start '{NodeId}' emitting {Kind} value",
            context.NodeId,
            value is null ? "empty" : valueType);

        var outputs = new Dictionary<string, object?> { ["value"] = value };
        return Task.FromResult(ModuleResult.Ok(outputs));
    }

    /// <summary>Interprets the configured text as the requested type~ 🎨.</summary>
    /// <remarks>
    /// A blank value is always null, whatever the type — that's how "or empty" is expressed
    /// without a separate "empty" type. A value that doesn't parse falls back to the text form
    /// rather than failing the node: the configuration validator already rejects the cases we can
    /// judge up front, and a run-time failure here would be a confusing place to discover a typo.
    /// </remarks>
    private static object? Convert(string? raw, string valueType)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var text = raw.Trim();

        if (string.Equals(valueType, "number", StringComparison.OrdinalIgnoreCase))
        {
            if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
            {
                return l;
            }

            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                ? d
                : raw;
        }

        if (string.Equals(valueType, "boolean", StringComparison.OrdinalIgnoreCase))
        {
            return bool.TryParse(text, out var b) ? b : raw;
        }

        if (string.Equals(valueType, "json", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var document = JsonDocument.Parse(text);
                return ToClr(document.RootElement);
            }
            catch (JsonException)
            {
                return raw;
            }
        }

        return raw;
    }

    /// <summary>
    /// Converts a parsed JSON element into the plain CLR shapes modules expect~ 🔄.
    /// </summary>
    private static object? ToClr(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var l) ? l : (object)element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.Array => System.Linq.Enumerable.ToList(
            System.Linq.Enumerable.Select(element.EnumerateArray(), ToClr)),
        JsonValueKind.Object => System.Linq.Enumerable.ToDictionary(
            element.EnumerateObject(), p => p.Name, p => ToClr(p.Value)),
        _ => element.ToString(),
    };

    private static string? ReadString(IReadOnlyDictionary<string, object?> source, string key)
        => source.TryGetValue(key, out var value) && value is not null
            ? value as string ?? value.ToString()
            : null;
}
