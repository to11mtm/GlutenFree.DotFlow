// <copyright file="ModuleDtos.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Api.Dtos;

using System.Collections.Generic;
using System.Text.Json;

/// <summary>
/// 📦 Phase 3.3.a.0 — Module list row (mirrors <c>ModuleSummaryDto</c> from <c>GET /api/v1/modules</c>)~ ✨.
/// </summary>
/// <param name="Id">The module id.</param>
/// <param name="DisplayName">The UI display name.</param>
/// <param name="Category">The palette category.</param>
/// <param name="Description">What the module does.</param>
/// <param name="Icon">The icon identifier.</param>
/// <param name="Version">The module version string.</param>
/// <param name="Enabled">Whether the module is enabled.</param>
public sealed record ModuleSummaryDto(
    string Id,
    string DisplayName,
    string Category,
    string Description,
    string Icon,
    string? Version,
    bool Enabled = true);

/// <summary>
/// 📦 Phase 3.3.a.0 — Full module details incl. schema (mirrors <c>ModuleDetailsDto</c>)~ ✨.
/// </summary>
public sealed record ModuleDetailsDto(
    string Id,
    string DisplayName,
    string Category,
    string Description,
    string Icon,
    string? Version,
    ModuleSchemaDto Schema,
    List<string> Dependencies,
    bool Enabled = true,
    List<string>? AvailableVersions = null);

/// <summary>📐 Phase 3.3.a.0 — A module schema: ports + properties (mirrors <c>ModuleSchemaDto</c>)~ ✨.</summary>
public sealed record ModuleSchemaDto(
    List<PortDefinitionDto> Inputs,
    List<PortDefinitionDto> Outputs,
    List<ModulePropertyDefinitionDto> Properties);

/// <summary>📦 Phase 3.6.0 — Result of installing a module package (mirrors <c>ModuleInstallResultDto</c>)~ ✨.</summary>
/// <param name="Module">The installed module's details.</param>
/// <param name="Warnings">Non-fatal advisories (missing hashes, schema-compat, signature).</param>
public sealed record ModuleInstallResultDto(
    ModuleDetailsDto Module,
    List<string> Warnings);

/// <summary>🔘 Phase 3.6.0 — Result of an enable/disable toggle (mirrors <c>ModuleToggleResultDto</c>)~ ✨.</summary>
/// <param name="ModuleId">The module id.</param>
/// <param name="Enabled">The new enabled state.</param>
/// <param name="AffectedVersions">The versions whose state changed.</param>
public sealed record ModuleToggleResultDto(
    string ModuleId,
    bool Enabled,
    List<string> AffectedVersions);

/// <summary>🔌 Phase 3.3.a.0 — A module port (mirrors <c>PortDefinitionDto</c>)~ ✨.</summary>
public sealed record PortDefinitionDto(
    string Name,
    string DisplayName,
    string? DataType,
    string? Description,
    bool IsRequired,
    JsonElement? DefaultValue);

/// <summary>
/// ⚙️ Phase 3.3.a.0 — A configurable module property (mirrors <c>ModulePropertyDefinitionDto</c>).
/// <see cref="EditorType"/> drives the schema-driven properties panel (D6)~ ✨.
/// </summary>
public sealed record ModulePropertyDefinitionDto(
    string Name,
    string DisplayName,
    string? DataType,
    string? Description,
    bool IsRequired,
    JsonElement? DefaultValue,
    string EditorType,
    List<JsonElement>? AllowedValues);
