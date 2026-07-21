// <copyright file="SetVariableModule.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin;

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using Microsoft.Extensions.Logging;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;

/// <summary>
/// 💾 Built-in SetVariable module (<c>builtin.setvariable</c>) — writes a named value
/// into the workflow's variable store so downstream nodes can read it~ ✨💖.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: This module uses <see cref="ModuleResult.VariableUpdates"/> (Phase 1.5.0)
/// to declare variable mutations. It never mutates the execution context directly — the
/// <c>WorkflowExecutor</c> applies the updates after the node completes~ 🌸.
/// </para>
/// </remarks>
public partial class SetVariableModule : IWorkflowModule
{
    /// <summary>
    /// Valid variable name pattern: starts with letter or underscore,
    /// followed by letters, digits, underscores, or dots~ 🏷️.
    /// </summary>
    [GeneratedRegex(@"^[a-zA-Z_][a-zA-Z0-9_.]*$")]
    private static partial Regex VariableNamePattern();

    /// <inheritdoc />
    public string ModuleId => "builtin.setvariable";

    /// <inheritdoc />
    public string DisplayName => "Set Variable";

    /// <inheritdoc />
    public string Category => "Variables";

    /// <inheritdoc />
    public string Description => "Writes a named value into the workflow's variable store~ 💾✨";

    /// <inheritdoc />
    public string Icon => "💾";

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public ModuleSchema Schema => new(
        Inputs: Arr.create(
            new PortDefinition(
                Name: "value",
                DisplayName: "Value",
                DataType: typeof(object),
                Description: "When connected, overrides the value property (runtime data wins)~ 🔗",
                IsRequired: false)),
        Outputs: Arr.create(
            new PortDefinition(
                Name: "previousValue",
                DisplayName: "Previous Value",
                DataType: typeof(object),
                Description: "Previous value of the variable, or null if new~ 🔙",
                IsRequired: false),
            new PortDefinition(
                Name: "wasCreated",
                DisplayName: "Was Created",
                DataType: typeof(bool),
                Description: "True if the variable was new, false if updated~ 🆕",
                IsRequired: false)),
        Properties: Arr.create(
            new ModulePropertyDefinition(
                Name: "name",
                DisplayName: "Variable Name",
                DataType: typeof(string),
                Description: "Variable name to create/update. Must match ^[a-zA-Z_][a-zA-Z0-9_.]*$~ 🏷️",
                IsRequired: true,
                DefaultValue: null,
                EditorType: PropertyEditorType.Text),
            new ModulePropertyDefinition(
                Name: "value",
                DisplayName: "Value",
                DataType: typeof(string),
                Description: "Value to set. Supports {{Variable.Name}} references. Overridden by connected input~ 💬",
                IsRequired: false,
                DefaultValue: null,
                EditorType: PropertyEditorType.Text)));

    /// <summary>
    /// Validates the variable name format~ ✅.
    /// </summary>
    public ValidationResult ValidateConfiguration(IReadOnlyDictionary<string, object?> configuration)
    {
        if (configuration.TryGetValue("name", out var nameObj) && nameObj is string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return ValidationResult.Failure(
                    new ValidationError("EMPTY_VARIABLE_NAME", "Variable name cannot be empty~ 💔", PropertyName: "name"));
            }

            if (!VariableNamePattern().IsMatch(name))
            {
                return ValidationResult.Failure(
                    new ValidationError(
                        "INVALID_VARIABLE_NAME",
                        $"Variable name '{name}' contains invalid characters. Must match ^[a-zA-Z_][a-zA-Z0-9_.]*$~ 💔",
                        PropertyName: "name"));
            }
        }

        return ValidationResult.Success();
    }

    /// <inheritdoc />
    public Task<ModuleResult> ExecuteAsync(
        ModuleExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        // Read variable name from properties~ 🏷️
        var name = context.Properties.TryGetValue("name", out var nameObj) && nameObj is string nameStr
            ? nameStr
            : string.Empty;

        if (string.IsNullOrWhiteSpace(name))
        {
            return Task.FromResult(ModuleResult.Fail("Variable name is required~ 💔"));
        }

        // Determine new value: prefer connected input over property~ 🔗
        object? newValue = null;
        if (context.Inputs.TryGetValue("value", out var inputValue))
        {
            newValue = inputValue;
        }
        else if (context.Properties.TryGetValue("value", out var propValue))
        {
            newValue = propValue;
        }

        // Capture previous value~ 🔙
        var exists = context.Variables.TryGetValue(name, out var previousValue);

        context.Logger.LogDebug("💾 SetVariable: {Name} = {Value} (was {PreviousValue})", name, newValue, previousValue);

        var outputs = new Dictionary<string, object?>
        {
            ["previousValue"] = exists ? previousValue : null,
            ["wasCreated"] = !exists,
        };

        var variableUpdates = new Dictionary<string, object?>
        {
            [name] = newValue,
        };

        return Task.FromResult(ModuleResult.Ok(outputs, variableUpdates));
    }
}
