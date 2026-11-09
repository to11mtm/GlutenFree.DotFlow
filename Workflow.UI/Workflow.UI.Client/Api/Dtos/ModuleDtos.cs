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
    bool Enabled = true,
    bool StreamCapable = false)
{
    /// <summary>🔏 Stage 7.4 — gets the installed package's trust (null for built-in modules).</summary>
    public ModuleTrustDto? Trust { get; init; }
}

/// <summary>🔏 Stage 7.4 — an installed package's trust, as last checked (mirrors the API's <c>ModuleTrustDto</c>).</summary>
/// <param name="Status"><c>verified</c>, <c>keyPinOnly</c>, <c>unsigned</c>, <c>untrusted</c> or <c>changedOnDisk</c>.</param>
/// <param name="Publisher">The signing publisher, when signed.</param>
/// <param name="Thumbprint">The signer certificate's SHA-256 thumbprint, when signed.</param>
/// <param name="TrustSource"><c>pinnedCertificate</c> or <c>certificateAuthority</c> when verified.</param>
/// <param name="Messages">Why it's less than verified, if it is.</param>
/// <param name="CheckedAt">When it was checked.</param>
public sealed record ModuleTrustDto(
    string Status,
    string? Publisher,
    string? Thumbprint,
    string? TrustSource,
    List<string> Messages,
    DateTimeOffset CheckedAt)
{
    /// <summary>Gets the badge text.</summary>
    public string Label => this.Status switch
    {
        "verified" => $"✔ Verified: {this.Publisher}",
        "keyPinOnly" => "🔑 Key pin only",
        "untrusted" => this.Publisher is null ? "⚠ Untrusted signer" : $"⚠ Untrusted signer: {this.Publisher}",
        "changedOnDisk" => "⚠ Changed on disk",
        _ => "Unsigned",
    };

    /// <summary>Gets the badge's CSS modifier.</summary>
    public string Css => this.Status switch
    {
        "verified" => "df-trust--ok",
        "unsigned" => "df-trust--none",
        "changedOnDisk" => "df-trust--bad",
        _ => "df-trust--warn",
    };

    /// <summary>Gets the tooltip: what it means, and why it's less than verified.</summary>
    public string Tooltip => this.Status switch
    {
        "verified" => $"Signed by {this.Publisher}, a trusted publisher ({(this.TrustSource == "certificateAuthority" ? "via a trusted CA root" : "pinned certificate")}). Thumbprint {this.Thumbprint}.",
        _ => string.Join(" ", this.Messages),
    };
}

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
    List<string>? AvailableVersions = null,
    bool StreamCapable = false,
    string? StreamShape = null);

/// <summary>📐 Phase 3.3.a.0 — A module schema: ports + properties (mirrors <c>ModuleSchemaDto</c>)~ ✨.</summary>
public sealed record ModuleSchemaDto(
    List<PortDefinitionDto> Inputs,
    List<PortDefinitionDto> Outputs,
    List<ModulePropertyDefinitionDto> Properties,
    string? StreamShape = null);

/// <summary>📦 Phase 3.6.0 — Result of installing a module package (mirrors <c>ModuleInstallResultDto</c>)~ ✨.</summary>
/// <param name="Module">The installed module's details.</param>
/// <param name="Warnings">Non-fatal advisories (missing hashes, schema-compat, signature).</param>
public sealed record ModuleInstallResultDto(
    ModuleDetailsDto Module,
    List<string> Warnings)
{
    /// <summary>🔏 Stage 7.4 — gets who signed the package and whether they're trusted.</summary>
    public ModuleTrustDto? Trust { get; init; }
}

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
    JsonElement? DefaultValue,
    bool IsStreaming = false);

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
    List<JsonElement>? AllowedValues,
    bool SupportsTemplates = false);
