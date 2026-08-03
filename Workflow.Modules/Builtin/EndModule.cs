// <copyright file="EndModule.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using Microsoft.Extensions.Logging;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;

/// <summary>
/// 🏁 Built-in End module (<c>builtin.end</c>) — an explicit "this is the result" marker that
/// optionally logs the final value~ ✨💖.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: <b>End always echoes its input to its <c>result</c> output, in every mode.</b> This
/// is load-bearing, not decoration. The engine collects a workflow's outputs from the nodes with no
/// successors (<c>WorkflowExecutor.GatherWorkflowOutputs</c>), copying each terminal node's outputs
/// as <c>{nodeId}.{key}</c>. A literal no-op that produced nothing would therefore make appending an
/// End node <em>silently erase the workflow's result</em> — the previously-terminal node stops being
/// terminal, and the new terminal node contributes nothing. The workflow would complete happily with
/// an empty result and no clue why.
/// </para>
/// <para>
/// So "no-op" here means "performs no side effect" (<c>silent</c> mode), never "produces nothing".
/// The happy consequence is that a workflow ending in an End node has a predictable result shape:
/// <c>{endNodeId}.result</c>, instead of whatever the last few nodes happened to emit~ 🌸.
/// </para>
/// </remarks>
public class EndModule : IWorkflowModule
{
    /// <summary>Log levels reused from the Log module so the two feel the same~ 🎚️.</summary>
    private static readonly Dictionary<string, LogLevel> KnownLevels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Trace"] = LogLevel.Trace,
        ["Debug"] = LogLevel.Debug,
        ["Information"] = LogLevel.Information,
        ["Warning"] = LogLevel.Warning,
        ["Error"] = LogLevel.Error,
        ["Critical"] = LogLevel.Critical,
    };

    private static readonly string[] KnownModes = ["log", "silent"];

    /// <inheritdoc />
    public string ModuleId => "builtin.end";

    /// <inheritdoc />
    public string DisplayName => "End";

    /// <inheritdoc />
    public string Category => "Flow Control";

    /// <inheritdoc />
    public string Description => "Marks where a workflow ends, optionally logging the final result~ 🏁✨";

    /// <inheritdoc />
    public string Icon => "🏁";

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public ModuleSchema Schema => new(
        Inputs: Arr.create(
            new PortDefinition(
                Name: "result",
                DisplayName: "Result",
                DataType: typeof(object),
                Description: "The final value of the workflow~ 🎁",
                IsRequired: false)),
        Outputs: Arr.create(
            new PortDefinition(
                Name: "result",
                DisplayName: "Result",
                DataType: typeof(object),
                Description: "The same value, passed through so it becomes the workflow's output~ 🎁",
                IsRequired: false)),
        Properties: Arr.create(
            new ModulePropertyDefinition(
                Name: "mode",
                DisplayName: "Mode",
                DataType: typeof(string),
                Description: "'log' writes the final result to the log; 'silent' does nothing~ 🎚️",
                IsRequired: false,
                DefaultValue: "log",
                EditorType: PropertyEditorType.Dropdown,
                AllowedValues: Arr.create<object>("log", "silent")),
            new ModulePropertyDefinition(
                Name: "level",
                DisplayName: "Log Level",
                DataType: typeof(string),
                Description: "Level to log at when mode is 'log': Trace, Debug, Information, "
                    + "Warning, Error, Critical~ 🎚️",
                IsRequired: false,
                DefaultValue: "Information",
                EditorType: PropertyEditorType.Dropdown,
                AllowedValues: Arr.create<object>("Trace", "Debug", "Information", "Warning", "Error", "Critical")),
            new ModulePropertyDefinition(
                Name: "label",
                DisplayName: "Label",
                DataType: typeof(string),
                Description: "Optional prefix for the logged line, e.g. 'Order sync finished'. "
                    + "Supports {{Variable.Name}} references~ 🏷️",
                IsRequired: false,
                DefaultValue: null,
                EditorType: PropertyEditorType.Text,
                SupportsTemplates: true)));

    /// <summary>Validates the mode and level~ ✅.</summary>
    /// <param name="configuration">The node's configured properties.</param>
    /// <returns>The validation result.</returns>
    public ValidationResult ValidateConfiguration(IReadOnlyDictionary<string, object?> configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var mode = ReadString(configuration, "mode") ?? "log";
        if (!Array.Exists(KnownModes, m => string.Equals(m, mode, StringComparison.OrdinalIgnoreCase)))
        {
            return ValidationResult.Failure(
                new ValidationError(
                    "INVALID_END_MODE",
                    $"Unknown mode '{mode}'. Valid modes: {string.Join(", ", KnownModes)}~ 💔",
                    PropertyName: "mode"));
        }

        var level = ReadString(configuration, "level");
        if (!string.IsNullOrWhiteSpace(level) && !KnownLevels.ContainsKey(level))
        {
            return ValidationResult.Failure(
                new ValidationError(
                    "INVALID_LOG_LEVEL",
                    $"Unknown log level '{level}'. Valid levels: {string.Join(", ", KnownLevels.Keys)}~ 💔",
                    PropertyName: "level"));
        }

        return ValidationResult.Success();
    }

    /// <inheritdoc />
    public Task<ModuleResult> ExecuteAsync(
        ModuleExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var result = context.Inputs.TryGetValue("result", out var value) ? value : null;
        var mode = ReadString(context.Properties, "mode") ?? "log";

        if (string.Equals(mode, "log", StringComparison.OrdinalIgnoreCase))
        {
            var level = LogLevel.Information;
            if (ReadString(context.Properties, "level") is { } levelName
                && KnownLevels.TryGetValue(levelName, out var parsed))
            {
                level = parsed;
            }

            var label = ReadString(context.Properties, "label");
            var prefix = string.IsNullOrWhiteSpace(label) ? "Workflow finished" : label;

            context.Logger.Log(level, "🏁 {Label}: {Result}", prefix, result ?? "(no result)");
        }

        // Always echo — see the class remarks. This is what keeps the workflow's outputs alive.
        var outputs = new Dictionary<string, object?> { ["result"] = result };
        return Task.FromResult(ModuleResult.Ok(outputs));
    }

    private static string? ReadString(IReadOnlyDictionary<string, object?> source, string key)
        => source.TryGetValue(key, out var value) && value is not null
            ? value as string ?? value.ToString()
            : null;
}
