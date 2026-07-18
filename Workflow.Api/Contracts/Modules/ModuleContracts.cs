// <copyright file="ModuleContracts.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.Contracts.Modules;

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;

/// <summary>
/// 🔌 Phase 2.7.3 — Serializable projection of a module <see cref="PortDefinition"/> (input/output)~ ✨.
/// </summary>
/// <param name="Name">The unique port name used in connections.</param>
/// <param name="DisplayName">The UI display name.</param>
/// <param name="DataType">The .NET data type rendered as a stable string.</param>
/// <param name="Description">Optional human-readable description.</param>
/// <param name="IsRequired">Whether the port must be connected.</param>
/// <param name="DefaultValue">Optional default value (for optional inputs), as JSON.</param>
public sealed record PortDefinitionDto(
    string Name,
    string DisplayName,
    string? DataType,
    string? Description,
    bool IsRequired,
    JsonElement? DefaultValue)
{
    /// <summary>Projects a <see cref="PortDefinition"/> into its DTO~ 🔌.</summary>
    /// <param name="port">The domain port definition.</param>
    /// <returns>A serializable <see cref="PortDefinitionDto"/>.</returns>
    public static PortDefinitionDto From(PortDefinition port)
        => new(
            port.Name,
            port.DisplayName,
            JsonTypeHelpers.TypeName(port.DataType),
            port.Description,
            port.IsRequired,
            ModuleJson.ToElement(port.DefaultValue));
}

/// <summary>
/// ⚙️ Phase 2.7.3 — Serializable projection of a module <see cref="ModulePropertyDefinition"/>~ ✨.
/// </summary>
/// <param name="Name">The unique property name.</param>
/// <param name="DisplayName">The UI display name.</param>
/// <param name="DataType">The .NET data type rendered as a stable string.</param>
/// <param name="Description">Optional human-readable description.</param>
/// <param name="IsRequired">Whether the property must be provided.</param>
/// <param name="DefaultValue">Optional default value, as JSON.</param>
/// <param name="EditorType">The UI editor type rendered as a string.</param>
/// <param name="AllowedValues">Optional allowed values (dropdown/enum), as JSON.</param>
public sealed record ModulePropertyDefinitionDto(
    string Name,
    string DisplayName,
    string? DataType,
    string? Description,
    bool IsRequired,
    JsonElement? DefaultValue,
    string EditorType,
    IReadOnlyList<JsonElement>? AllowedValues)
{
    /// <summary>Projects a <see cref="ModulePropertyDefinition"/> into its DTO~ ⚙️.</summary>
    /// <param name="property">The domain property definition.</param>
    /// <returns>A serializable <see cref="ModulePropertyDefinitionDto"/>.</returns>
    public static ModulePropertyDefinitionDto From(ModulePropertyDefinition property)
        => new(
            property.Name,
            property.DisplayName,
            JsonTypeHelpers.TypeName(property.DataType),
            property.Description,
            property.IsRequired,
            ModuleJson.ToElement(property.DefaultValue),
            property.EditorType.ToString(),
            property.AllowedValues is { } allowed
                ? allowed.Select(v => ModuleJson.ToElement(v) ?? default).ToList()
                : null);
}

/// <summary>
/// 📐 Phase 2.7.3 — Serializable projection of a module's <see cref="ModuleSchema"/>~ ✨.
/// </summary>
/// <param name="Inputs">Input port DTOs.</param>
/// <param name="Outputs">Output port DTOs.</param>
/// <param name="Properties">Configuration property DTOs.</param>
public sealed record ModuleSchemaDto(
    IReadOnlyList<PortDefinitionDto> Inputs,
    IReadOnlyList<PortDefinitionDto> Outputs,
    IReadOnlyList<ModulePropertyDefinitionDto> Properties)
{
    /// <summary>Projects a <see cref="ModuleSchema"/> into its DTO~ 📐.</summary>
    /// <param name="schema">The domain schema.</param>
    /// <returns>A serializable <see cref="ModuleSchemaDto"/>.</returns>
    public static ModuleSchemaDto From(ModuleSchema schema)
        => new(
            schema.Inputs.Select(PortDefinitionDto.From).ToList(),
            schema.Outputs.Select(PortDefinitionDto.From).ToList(),
            schema.Properties.Select(ModulePropertyDefinitionDto.From).ToList());
}

/// <summary>
/// 📦 Phase 2.7.3 — Lightweight module summary (no schema) for list endpoints~ ✨.
/// </summary>
/// <param name="Id">The module id.</param>
/// <param name="DisplayName">The UI display name.</param>
/// <param name="Category">The palette category.</param>
/// <param name="Description">What the module does.</param>
/// <param name="Icon">The icon identifier.</param>
/// <param name="Version">The module version as a string.</param>
public sealed record ModuleSummaryDto(
    string Id,
    string DisplayName,
    string Category,
    string Description,
    string Icon,
    string? Version)
{
    /// <summary>Projects an <see cref="IWorkflowModule"/> into a summary DTO~ 📦.</summary>
    /// <param name="module">The module.</param>
    /// <returns>A serializable <see cref="ModuleSummaryDto"/>.</returns>
    public static ModuleSummaryDto From(IWorkflowModule module)
        => new(
            module.ModuleId,
            module.DisplayName,
            module.Category,
            module.Description,
            module.Icon,
            JsonTypeHelpers.VersionString(module.Version));
}

/// <summary>
/// 📦 Phase 2.7.3 — Full module details (summary + schema + dependencies) for the by-id endpoint~ ✨.
/// </summary>
/// <param name="Id">The module id.</param>
/// <param name="DisplayName">The UI display name.</param>
/// <param name="Category">The palette category.</param>
/// <param name="Description">What the module does.</param>
/// <param name="Icon">The icon identifier.</param>
/// <param name="Version">The module version as a string.</param>
/// <param name="Schema">The module schema (ports + properties).</param>
/// <param name="Dependencies">Module ids this module depends on.</param>
public sealed record ModuleDetailsDto(
    string Id,
    string DisplayName,
    string Category,
    string Description,
    string Icon,
    string? Version,
    ModuleSchemaDto Schema,
    IReadOnlyList<string> Dependencies)
{
    /// <summary>Projects an <see cref="IWorkflowModule"/> into a details DTO~ 📦.</summary>
    /// <param name="module">The module.</param>
    /// <returns>A serializable <see cref="ModuleDetailsDto"/>.</returns>
    public static ModuleDetailsDto From(IWorkflowModule module)
        => new(
            module.ModuleId,
            module.DisplayName,
            module.Category,
            module.Description,
            module.Icon,
            JsonTypeHelpers.VersionString(module.Version),
            ModuleSchemaDto.From(module.Schema),
            module.Dependencies.ToList());
}

/// <summary>
/// 🔤 Internal helper — serializes an arbitrary CLR value (module default/allowed values) into a
/// <see cref="JsonElement"/> for the DTO layer, never throwing on odd values~ ✨.
/// </summary>
internal static class ModuleJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Serializes a CLR value to a nullable <see cref="JsonElement"/>~ 🔤.</summary>
    /// <param name="value">The value (may be <c>null</c>).</param>
    /// <returns>A <see cref="JsonElement"/>, or <c>null</c> when the value is <c>null</c>.</returns>
    public static JsonElement? ToElement(object? value)
    {
        if (value is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.SerializeToElement(value, Options);
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(value.ToString(), Options);
        }
    }
}
