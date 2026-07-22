// <copyright file="LogModule.cs" company="GlutenFree">
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
/// 📝 Built-in Log module (<c>builtin.log</c>) — writes a structured log message
/// at a configurable level during workflow execution~ ✨💖.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: This is a property-driven module — all configuration comes from
/// <see cref="ModuleExecutionContext.Properties"/> (resolved by PropertyBinder).
/// No data-flow inputs are needed! The reference implementation for property-driven modules~ 🌸.
/// </para>
/// </remarks>
public class LogModule : IWorkflowModule
{
    /// <summary>
    /// Known log level names mapped to <see cref="LogLevel"/> values. 🎯.
    /// </summary>
    private static readonly Dictionary<string, LogLevel> _knownLevels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Trace"] = LogLevel.Trace,
        ["Debug"] = LogLevel.Debug,
        ["Information"] = LogLevel.Information,
        ["Warning"] = LogLevel.Warning,
        ["Error"] = LogLevel.Error,
        ["Critical"] = LogLevel.Critical,
    };

    /// <inheritdoc />
    public string ModuleId => "builtin.log";

    /// <inheritdoc />
    public string DisplayName => "Log Message";

    /// <inheritdoc />
    public string Category => "Utilities";

    /// <inheritdoc />
    public string Description => "Writes a structured log message at a configurable level during workflow execution~ 📝✨";

    /// <inheritdoc />
    public string Icon => "📝";

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public ModuleSchema Schema => new(
        Inputs: Arr<PortDefinition>.Empty,
        Outputs: Arr.create(
            new PortDefinition(
                Name: "timestamp",
                DisplayName: "Timestamp",
                DataType: typeof(DateTimeOffset),
                Description: "When the message was logged~ ⏰",
                IsRequired: false),
            new PortDefinition(
                Name: "message",
                DisplayName: "Message",
                DataType: typeof(string),
                Description: "The resolved/final message that was logged~ 💬",
                IsRequired: false)),
        Properties: Arr.create(
            new ModulePropertyDefinition(
                Name: "message",
                DisplayName: "Message",
                DataType: typeof(string),
                Description: "The message text to log. Supports {{Variable.Name}} references~ 💬",
                IsRequired: true,
                DefaultValue: null,
                EditorType: PropertyEditorType.Text),
            new ModulePropertyDefinition(
                Name: "level",
                DisplayName: "Log Level",
                DataType: typeof(string),
                Description: "Log level: Trace, Debug, Information, Warning, Error, Critical~ 🎚️",
                IsRequired: false,
                DefaultValue: "Information",
                EditorType: PropertyEditorType.Dropdown,
                AllowedValues: Arr.create<object>("Trace", "Debug", "Information", "Warning", "Error", "Critical")),
            new ModulePropertyDefinition(
                Name: "includeContext",
                DisplayName: "Include Context",
                DataType: typeof(bool),
                Description: "Append ExecutionId and NodeId to the message~ 🔍",
                IsRequired: false,
                DefaultValue: false,
                EditorType: PropertyEditorType.Boolean)));

    /// <summary>
    /// Validates the log level property if provided~ ✅.
    /// </summary>
    public ValidationResult ValidateConfiguration(IReadOnlyDictionary<string, object?> configuration)
    {
        if (configuration.TryGetValue("level", out var levelObj) && levelObj is string levelStr)
        {
            if (!string.IsNullOrWhiteSpace(levelStr) && !_knownLevels.ContainsKey(levelStr))
            {
                return ValidationResult.Failure(
                    new ValidationError(
                        "INVALID_LOG_LEVEL",
                        $"Unknown log level '{levelStr}'. Valid levels: {string.Join(", ", _knownLevels.Keys)}~ 💔",
                        PropertyName: "level"));
            }
        }

        return ValidationResult.Success();
    }

    /// <inheritdoc />
    public Task<ModuleResult> ExecuteAsync(
        ModuleExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        // Read message from properties (PropertyBinder already resolved {{Variable.Name}} refs)~ 💬
        var message = context.Properties.TryGetValue("message", out var msgObj) && msgObj is string msgStr
            ? msgStr
            : string.Empty;

        // Read and parse log level — default to Information for unknown values~ 🎚️
        var logLevel = LogLevel.Information;
        if (context.Properties.TryGetValue("level", out var lvlObj) && lvlObj is string lvlStr)
        {
            if (!_knownLevels.TryGetValue(lvlStr, out logLevel))
            {
                logLevel = LogLevel.Information;
            }
        }

        // Append context info if requested~ 🔍
        var includeContext = context.Properties.TryGetValue("includeContext", out var ctxObj)
            && ctxObj is true or "true" or "True";

        if (includeContext)
        {
            message = $"{message} [ExecutionId={context.ExecutionId}, NodeId={context.NodeId}]";
        }

        // Log the message at the resolved level~ 📝
        context.Logger.Log(logLevel, "📝 {Message}", message);

        var timestamp = DateTimeOffset.UtcNow;

        var outputs = new Dictionary<string, object?>
        {
            ["timestamp"] = timestamp,
            ["message"] = message,
        };

        return Task.FromResult(ModuleResult.Ok(outputs));
    }
}
