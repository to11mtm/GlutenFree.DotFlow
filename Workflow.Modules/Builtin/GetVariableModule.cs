// <copyright file="GetVariableModule.cs" company="GlutenFree">
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
/// 🔍 Built-in GetVariable module (<c>builtin.getvariable</c>) — reads a named value
/// from the workflow's variable store and exposes it as an output for downstream nodes~ ✨💖.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: The companion to <see cref="SetVariableModule"/>. This module only reads —
/// it never writes to variables. Supports a default value fallback and an optional
/// <c>throwIfMissing</c> fail-fast mode~ 🌸.
/// </para>
/// </remarks>
public class GetVariableModule : IWorkflowModule
{
    /// <inheritdoc />
    public string ModuleId => "builtin.getvariable";

    /// <inheritdoc />
    public string DisplayName => "Get Variable";

    /// <inheritdoc />
    public string Category => "Variables";

    /// <inheritdoc />
    public string Description => "Reads a named value from the workflow's variable store~ 🔍✨";

    /// <inheritdoc />
    public string Icon => "🔍";

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
                Description: "The resolved variable value (or default)~ 📦",
                IsRequired: false),
            new PortDefinition(
                Name: "exists",
                DisplayName: "Exists",
                DataType: typeof(bool),
                Description: "Whether the variable was found in the store~ ✅",
                IsRequired: false),
            new PortDefinition(
                Name: "typeName",
                DisplayName: "Type Name",
                DataType: typeof(string),
                Description: "value.GetType().Name or \"null\"~ 🏷️",
                IsRequired: false)),
        Properties: Arr.create(
            new ModulePropertyDefinition(
                Name: "name",
                DisplayName: "Variable Name",
                DataType: typeof(string),
                Description: "Variable name to read~ 🏷️",
                IsRequired: true,
                DefaultValue: null,
                EditorType: PropertyEditorType.Text),
            new ModulePropertyDefinition(
                Name: "defaultValue",
                DisplayName: "Default Value",
                DataType: typeof(string),
                Description: "Returned if variable not found. Supports {{Variable.Name}}~ 🔙",
                IsRequired: false,
                DefaultValue: null,
                EditorType: PropertyEditorType.Text),
            new ModulePropertyDefinition(
                Name: "throwIfMissing",
                DisplayName: "Throw If Missing",
                DataType: typeof(bool),
                Description: "Fail execution if variable not found and no default~ ❌",
                IsRequired: false,
                DefaultValue: false,
                EditorType: PropertyEditorType.Boolean)));

    /// <summary>
    /// Validates that <c>name</c> is not empty~ ✅.
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
        }

        return ValidationResult.Success();
    }

    /// <inheritdoc />
    public Task<ModuleResult> ExecuteAsync(
        ModuleExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        // Read variable name~ 🏷️
        var name = context.Properties.TryGetValue("name", out var nameObj) && nameObj is string nameStr
            ? nameStr
            : string.Empty;

        if (string.IsNullOrWhiteSpace(name))
        {
            return Task.FromResult(ModuleResult.Fail("Variable name is required~ 💔"));
        }

        // Look up variable~ 🔍
        var exists = context.Variables.TryGetValue(name, out var value);

        if (!exists)
        {
            // Check throwIfMissing~ ❌
            var throwIfMissing = context.Properties.TryGetValue("throwIfMissing", out var throwObj)
                && throwObj is true or "true" or "True";

            // Check for default value~ 🔙
            var hasDefault = context.Properties.TryGetValue("defaultValue", out var defaultValue)
                && defaultValue is not null;

            if (!hasDefault && throwIfMissing)
            {
                return Task.FromResult(ModuleResult.Fail($"Variable '{name}' not found~ 💔"));
            }

            if (hasDefault)
            {
                value = defaultValue;
            }
        }

        var typeName = value?.GetType().Name ?? "null";

        context.Logger.LogDebug("🔍 GetVariable: {Name} = {Value} (exists={Exists})", name, value, exists);

        var outputs = new Dictionary<string, object?>
        {
            ["value"] = value,
            ["exists"] = exists,
            ["typeName"] = typeName,
        };

        return Task.FromResult(ModuleResult.Ok(outputs));
    }
}
