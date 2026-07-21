// <copyright file="ModuleValidator.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Validation;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;

/// <summary>
/// ✅ Validates that workflow modules are well-formed before registration.
/// Checks module ID format, metadata completeness, schema correctness,
/// and optionally enforces strict mode for production quality~ 💖.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: This validator runs at registration time in InMemoryModuleRegistry.
/// Modules that fail validation are rejected to prevent runtime surprises!
/// Strict mode is opt-in and catches missing descriptions and icons. UwU ✨.
/// </para>
/// </remarks>
public class ModuleValidator
{
    /// <summary>
    /// Module ID naming convention: lowercase letter start, then lowercase letters,
    /// digits, dots, hyphens, or underscores. Max 128 chars~ 🏷️.
    /// </summary>
    private static readonly Regex ModuleIdPattern = new(
        @"^[a-z][a-z0-9._-]*$",
        RegexOptions.Compiled);

    /// <summary>
    /// Maximum allowed length for module IDs~ 📏.
    /// </summary>
    private const int MaxModuleIdLength = 128;

    /// <summary>
    /// Validates a workflow module for correctness and completeness. ✅.
    /// </summary>
    /// <param name="module">The module to validate.</param>
    /// <param name="strict">
    /// If true, enforces stricter checks: descriptions on all ports/properties,
    /// Icon must be set. Default is false for relaxed validation~ 🔒.
    /// </param>
    /// <returns>A <see cref="ValidationResult"/> with errors and warnings.</returns>
    public ValidationResult Validate(IWorkflowModule module, bool strict = false)
    {
        ArgumentNullException.ThrowIfNull(module);

        var errors = new List<ValidationError>();
        var warnings = new List<ValidationWarning>();

        ValidateModuleId(module, errors);
        ValidateMetadata(module, errors, warnings, strict);
        ValidateSchema(module, errors, warnings, strict);

        return ValidationResult.WithErrorsAndWarnings(errors, warnings);
    }

    /// <summary>
    /// Validates the module ID format: not empty, matches pattern, within length~ 🏷️.
    /// </summary>
    private static void ValidateModuleId(IWorkflowModule module, List<ValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(module.ModuleId))
        {
            errors.Add(new ValidationError(
                "MOD001",
                "Module ID cannot be empty or whitespace."));
            return;
        }

        if (module.ModuleId.Length > MaxModuleIdLength)
        {
            errors.Add(new ValidationError(
                "MOD002",
                $"Module ID '{module.ModuleId}' exceeds maximum length of {MaxModuleIdLength} characters."));
        }

        if (!ModuleIdPattern.IsMatch(module.ModuleId))
        {
            errors.Add(new ValidationError(
                "MOD003",
                $"Module ID '{module.ModuleId}' does not match naming convention (must be lowercase, start with letter, only [a-z0-9._-])."));
        }
    }

    /// <summary>
    /// Validates module metadata: DisplayName, Description, Category, Version~ 📝.
    /// </summary>
    private static void ValidateMetadata(
        IWorkflowModule module,
        List<ValidationError> errors,
        List<ValidationWarning> warnings,
        bool strict)
    {
        if (string.IsNullOrWhiteSpace(module.DisplayName))
        {
            errors.Add(new ValidationError(
                "MOD010",
                "Module DisplayName cannot be empty."));
        }

        if (string.IsNullOrWhiteSpace(module.Description))
        {
            errors.Add(new ValidationError(
                "MOD011",
                "Module Description cannot be empty."));
        }

        if (string.IsNullOrWhiteSpace(module.Category))
        {
            errors.Add(new ValidationError(
                "MOD012",
                "Module Category cannot be empty."));
        }

        if (module.Version == null)
        {
            errors.Add(new ValidationError(
                "MOD013",
                "Module Version cannot be null."));
        }

        // Strict: require Icon to be set~ 🔒
        if (strict && string.IsNullOrWhiteSpace(module.Icon))
        {
            errors.Add(new ValidationError(
                "MOD014",
                "Module Icon must be set in strict mode."));
        }
    }

    /// <summary>
    /// Validates the module schema: port and property definitions~ 📋.
    /// </summary>
    private static void ValidateSchema(
        IWorkflowModule module,
        List<ValidationError> errors,
        List<ValidationWarning> warnings,
        bool strict)
    {
        var schema = module.Schema;

        // Validate input ports~ 📥
        ValidatePorts(schema.Inputs, "Input", module.ModuleId, errors, warnings, strict);

        // Validate output ports~ 📤
        ValidatePorts(schema.Outputs, "Output", module.ModuleId, errors, warnings, strict);

        // Validate properties~ ⚙️
        ValidateProperties(schema.Properties, module.ModuleId, errors, warnings, strict);
    }

    /// <summary>
    /// Validates a collection of port definitions for null names, null DataType,
    /// and duplicates~ 🔌.
    /// </summary>
    private static void ValidatePorts(
        IEnumerable<PortDefinition> ports,
        string portType,
        string moduleId,
        List<ValidationError> errors,
        List<ValidationWarning> warnings,
        bool strict)
    {
        var portList = ports.ToList();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var port in portList)
        {
            if (string.IsNullOrWhiteSpace(port.Name))
            {
                errors.Add(new ValidationError(
                    "MOD020",
                    $"{portType} port has a null or empty Name in module '{moduleId}'."));
                continue;
            }

            if (port.DataType == null)
            {
                errors.Add(new ValidationError(
                    "MOD021",
                    $"{portType} port '{port.Name}' has a null DataType in module '{moduleId}'."));
            }

            if (!seenNames.Add(port.Name))
            {
                errors.Add(new ValidationError(
                    "MOD022",
                    $"Duplicate {portType.ToLowerInvariant()} port name '{port.Name}' in module '{moduleId}'."));
            }

            // Strict: require descriptions on all ports~ 🔒
            if (strict && string.IsNullOrWhiteSpace(port.Description))
            {
                errors.Add(new ValidationError(
                    "MOD023",
                    $"{portType} port '{port.Name}' is missing a Description in strict mode (module '{moduleId}')."));
            }
        }
    }

    /// <summary>
    /// Validates property definitions for duplicates and completeness~ ⚙️.
    /// </summary>
    private static void ValidateProperties(
        IEnumerable<ModulePropertyDefinition> properties,
        string moduleId,
        List<ValidationError> errors,
        List<ValidationWarning> warnings,
        bool strict)
    {
        var propList = properties.ToList();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var prop in propList)
        {
            if (string.IsNullOrWhiteSpace(prop.Name))
            {
                errors.Add(new ValidationError(
                    "MOD030",
                    $"Property has a null or empty Name in module '{moduleId}'."));
                continue;
            }

            if (!seenNames.Add(prop.Name))
            {
                errors.Add(new ValidationError(
                    "MOD031",
                    $"Duplicate property name '{prop.Name}' in module '{moduleId}'."));
            }

            // Strict: require descriptions on all properties~ 🔒
            if (strict && string.IsNullOrWhiteSpace(prop.Description))
            {
                errors.Add(new ValidationError(
                    "MOD032",
                    $"Property '{prop.Name}' is missing a Description in strict mode (module '{moduleId}')."));
            }
        }
    }
}
