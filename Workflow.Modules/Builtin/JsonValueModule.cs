// <copyright file="JsonValueModule.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin;

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using Microsoft.Extensions.Logging;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;

/// <summary>
/// 🧾 Built-in JSON value module (<c>builtin.json.value</c>)~
/// Emits a configured JSON body as its output — a hand-authored piece of data you can drop
/// anywhere in a graph. Built for demos, testing, and stubbing: stand in for an API response
/// before the real HTTP node exists, feed a known object into a transform, or seed a fanout with
/// a fixed collection~ ✨💖
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: The <c>json</c> property is template-enabled, so <c>{{Variable.x}}</c> and
/// <c>{{input.path}}</c> resolve before parsing — a "static" body can still splice in live values.
/// Save-time validation parses the JSON <b>unless</b> it contains a template (unjudgeable until
/// bound), mirroring <see cref="StartModule"/>'s approach~ 🔗
/// </para>
/// <para>
/// Output port <c>value</c> carries the parsed <see cref="JsonElement"/> (object, array, or
/// scalar — all JSON kinds are legal), so downstream dot-paths like
/// <c>{{json-1.value.user.id}}</c> traverse it naturally~ 🎁
/// </para>
/// </remarks>
public sealed class JsonValueModule : IWorkflowModule
{
    /// <inheritdoc />
    public string ModuleId => "builtin.json.value";

    /// <inheritdoc />
    public string DisplayName => "JSON Value";

    /// <inheritdoc />
    public string Category => "Utilities";

    /// <inheritdoc />
    public string Description => "Emits a configured JSON body as its output — for demos, tests, and stubs~ 🧾";

    /// <inheritdoc />
    public string Icon => "🧾";

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public ModuleSchema Schema => new(
        Inputs: Arr.create(
            new PortDefinition(
                Name: "input",
                DisplayName: "Input",
                DataType: typeof(object),
                Description: "Optional. Connect a previous step to run this one after it; the value is available to templates as {{input}}~ 🔌",
                IsRequired: false)),
        Outputs: Arr.create(
            new PortDefinition(
                Name: "value",
                DisplayName: "Value",
                DataType: typeof(object),
                Description: "The configured JSON, parsed — object, array, or scalar~ 🎁",
                IsRequired: false)),
        Properties: Arr.create(
            new ModulePropertyDefinition(
                Name: "json",
                DisplayName: "JSON",
                DataType: typeof(string),
                Description: "The JSON body to emit. Supports {{Variable.x}} / {{input.path}} templates, resolved before parsing~ 🧾",
                IsRequired: true,
                DefaultValue: null,
                EditorType: PropertyEditorType.Json,
                SupportsTemplates: true)));

    /// <summary>
    /// Validates that the configured JSON parses — unless it contains a template, which can only
    /// be judged after binding at run time~ ✅.
    /// </summary>
    /// <param name="configuration">The node's configured properties.</param>
    /// <returns>The validation result.</returns>
    public ValidationResult ValidateConfiguration(IReadOnlyDictionary<string, object?> configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var json = ReadString(configuration, "json");
        if (string.IsNullOrWhiteSpace(json))
        {
            return ValidationResult.Failure(
                new ValidationError(
                    "MISSING_JSON",
                    "The 'json' property is required~ 💔",
                    PropertyName: "json"));
        }

        if (!json.Contains("{{", StringComparison.Ordinal))
        {
            try
            {
                using var _ = JsonDocument.Parse(json);
            }
            catch (JsonException ex)
            {
                return ValidationResult.Failure(
                    new ValidationError(
                        "INVALID_JSON",
                        $"The configured value isn't valid JSON: {ex.Message}~ 💔",
                        PropertyName: "json"));
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

        // Properties arrive already template-resolved (PropertyBinder)~ 🔗
        var raw = context.Properties.TryGetValue("json", out var v) ? v : null;

        // The Json editor may deliver a pre-parsed element rather than text~ 🎁
        if (raw is JsonElement el && el.ValueKind != JsonValueKind.String)
        {
            return Emit(context, el.Clone());
        }

        var json = raw is JsonElement { ValueKind: JsonValueKind.String } strEl ? strEl.GetString() : raw as string;
        if (string.IsNullOrWhiteSpace(json))
        {
            return Task.FromResult(ModuleResult.Fail(
                "JsonValueModule: the 'json' property is required but was empty~ 🧾❌"));
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            return Emit(context, doc.RootElement.Clone());
        }
        catch (JsonException ex)
        {
            // Reachable despite save-time validation when a template resolved into the body~
            return Task.FromResult(ModuleResult.Fail(
                $"JsonValueModule: the value isn't valid JSON after template resolution: {ex.Message}~ 🧾❌", ex));
        }
    }

    private static Task<ModuleResult> Emit(ModuleExecutionContext context, JsonElement value)
    {
        context.Logger.LogInformation(
            "🧾 JsonValue '{NodeId}' emitting a {Kind} value",
            context.NodeId,
            value.ValueKind);

        var outputs = new Dictionary<string, object?> { ["value"] = value };
        return Task.FromResult(ModuleResult.Ok(outputs));
    }

    private static string? ReadString(IReadOnlyDictionary<string, object?> source, string key)
        => source.TryGetValue(key, out var v)
            ? v switch
            {
                string s => s,
                JsonElement { ValueKind: JsonValueKind.String } el => el.GetString(),
                JsonElement el => el.GetRawText(),
                _ => v?.ToString(),
            }
            : null;
}
