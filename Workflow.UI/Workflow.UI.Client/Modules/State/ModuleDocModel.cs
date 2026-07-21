// <copyright file="ModuleDocModel.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Modules.State;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Workflow.UI.Client.Api.Dtos;

/// <summary>📖 Phase 3.6.1 — A documented port (input/output) row~ ✨.</summary>
/// <param name="Name">The port name.</param>
/// <param name="DisplayName">The display name.</param>
/// <param name="Type">The data type (or "any").</param>
/// <param name="Required">Whether the port is required.</param>
/// <param name="Description">Optional description.</param>
/// <param name="Default">Optional default value (rendered).</param>
public sealed record DocPort(string Name, string DisplayName, string Type, bool Required, string? Description, string? Default);

/// <summary>📖 Phase 3.6.1 — A documented configurable property row~ ✨.</summary>
/// <param name="Name">The property name.</param>
/// <param name="DisplayName">The display name.</param>
/// <param name="Editor">The editor type (Text/Select/Code/…).</param>
/// <param name="Type">The data type.</param>
/// <param name="Required">Whether it is required.</param>
/// <param name="Description">Optional description.</param>
/// <param name="Default">Optional default value (rendered).</param>
/// <param name="Allowed">Allowed values (dropdown/enum), rendered.</param>
public sealed record DocProperty(
    string Name,
    string DisplayName,
    string Editor,
    string Type,
    bool Required,
    string? Description,
    string? Default,
    IReadOnlyList<string> Allowed);

/// <summary>📖 Phase 3.6.1 — A documented version row~ ✨.</summary>
/// <param name="Version">The version string.</param>
/// <param name="Active">Whether it is the currently-resolved version.</param>
public sealed record DocVersion(string Version, bool Active);

/// <summary>
/// 📖 Phase 3.6.1 (D6) — Framework-free **generated** documentation for a module, projected from its
/// <see cref="ModuleDetailsDto"/> (description + schema + dependencies + versions). There is no
/// README/usage-examples/changelog in the model — those are post-MVP (3.6.P1). No Blazor/JS types~ ✨.
/// </summary>
public sealed class ModuleDocModel
{
    private ModuleDocModel()
    {
    }

    /// <summary>Gets the module id.</summary>
    public string Id { get; private init; } = string.Empty;

    /// <summary>Gets the display name.</summary>
    public string DisplayName { get; private init; } = string.Empty;

    /// <summary>Gets the category.</summary>
    public string Category { get; private init; } = string.Empty;

    /// <summary>Gets the icon.</summary>
    public string Icon { get; private init; } = string.Empty;

    /// <summary>Gets the resolved version.</summary>
    public string? Version { get; private init; }

    /// <summary>Gets a value indicating whether the resolved version is enabled.</summary>
    public bool Enabled { get; private init; }

    /// <summary>Gets the description.</summary>
    public string Description { get; private init; } = string.Empty;

    /// <summary>Gets the input ports.</summary>
    public IReadOnlyList<DocPort> Inputs { get; private init; } = Array.Empty<DocPort>();

    /// <summary>Gets the output ports.</summary>
    public IReadOnlyList<DocPort> Outputs { get; private init; } = Array.Empty<DocPort>();

    /// <summary>Gets the configurable properties.</summary>
    public IReadOnlyList<DocProperty> Properties { get; private init; } = Array.Empty<DocProperty>();

    /// <summary>Gets the module ids this module depends on.</summary>
    public IReadOnlyList<string> Dependencies { get; private init; } = Array.Empty<string>();

    /// <summary>Gets the available versions (with the active one flagged).</summary>
    public IReadOnlyList<DocVersion> Versions { get; private init; } = Array.Empty<DocVersion>();

    /// <summary>Projects a <see cref="ModuleDetailsDto"/> into generated documentation~ 📖.</summary>
    /// <param name="d">The module details.</param>
    /// <returns>The doc model.</returns>
    public static ModuleDocModel From(ModuleDetailsDto d)
        => new()
        {
            Id = d.Id,
            DisplayName = d.DisplayName,
            Category = d.Category,
            Icon = d.Icon,
            Version = d.Version,
            Enabled = d.Enabled,
            Description = d.Description,
            Inputs = d.Schema.Inputs.Select(PortFrom).ToList(),
            Outputs = d.Schema.Outputs.Select(PortFrom).ToList(),
            Properties = d.Schema.Properties.Select(PropFrom).ToList(),
            Dependencies = d.Dependencies ?? new List<string>(),
            Versions = (d.AvailableVersions ?? new List<string>())
                .Select(v => new DocVersion(v, string.Equals(v, d.Version, StringComparison.Ordinal)))
                .ToList(),
        };

    private static DocPort PortFrom(PortDefinitionDto p)
        => new(p.Name, p.DisplayName, p.DataType ?? "any", p.IsRequired, p.Description, Render(p.DefaultValue));

    private static DocProperty PropFrom(ModulePropertyDefinitionDto p)
        => new(
            p.Name,
            p.DisplayName,
            p.EditorType,
            p.DataType ?? "any",
            p.IsRequired,
            p.Description,
            Render(p.DefaultValue),
            p.AllowedValues is { } allowed ? allowed.Select(v => Render(v) ?? string.Empty).ToList() : new List<string>());

    private static string? Render(JsonElement? value)
    {
        if (value is not { } v || v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText();
    }
}
