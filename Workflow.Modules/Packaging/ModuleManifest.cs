// <copyright file="ModuleManifest.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Packaging;

using System;
using System.Collections.Generic;
using System.Linq;
using Workflow.Core.Models;

/// <summary>
/// 📜 Phase 2.8.0 — The deserialized <c>module.json</c> manifest at the root of a <c>.wfmod</c>
/// package. Describes the module's identity, engine requirement, dependencies, entry assembly, and
/// optional per-file content hashes~ ✨.
/// </summary>
public sealed record ModuleManifest
{
    /// <summary>Gets the unique module id (matches <c>IWorkflowModule.ModuleId</c>).</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Gets the module version string (SemVer-style <c>major.minor.patch</c>).</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>Gets the display name.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Gets the optional description.</summary>
    public string? Description { get; init; }

    /// <summary>Gets the optional author.</summary>
    public string? Author { get; init; }

    /// <summary>Gets the minimum DotFlow engine version this module requires (SemVer-style).</summary>
    public string? MinEngineVersion { get; init; }

    /// <summary>Gets the declared module dependencies (id + optional version range).</summary>
    public IReadOnlyList<ModuleDependency> Dependencies { get; init; } = Array.Empty<ModuleDependency>();

    /// <summary>Gets the entry assembly path, relative to the package root (e.g. <c>lib/My.Module.dll</c>).</summary>
    public string EntryAssembly { get; init; } = string.Empty;

    /// <summary>
    /// Gets the optional map of package-relative file path → base64 SHA-256 hash. When present it is
    /// validated on import; when absent, import trips a warning (Q7)~ 🔐.
    /// </summary>
    public IReadOnlyDictionary<string, string>? ContentHashes { get; init; }

    /// <summary>Parses <see cref="Version"/> into a <see cref="System.Version"/>~ 🏷️.</summary>
    /// <returns>The parsed version, or <c>null</c> when unparseable.</returns>
    public Version? ParseVersion()
        => System.Version.TryParse(this.Version, out var v) ? v : null;

    /// <summary>Parses <see cref="MinEngineVersion"/> into a <see cref="System.Version"/>~ 🏷️.</summary>
    /// <returns>The parsed minimum engine version, or <c>null</c> when unset/unparseable.</returns>
    public Version? ParseMinEngineVersion()
        => string.IsNullOrWhiteSpace(this.MinEngineVersion)
            ? null
            : System.Version.TryParse(this.MinEngineVersion, out var v) ? v : null;

    /// <summary>
    /// Validates the manifest's structural correctness (required fields, parseable versions, no
    /// path traversal in the entry assembly)~ ✅.
    /// </summary>
    /// <returns>A <see cref="ValidationResult"/> — <c>IsValid</c> when the manifest is well-formed.</returns>
    public ValidationResult Validate()
    {
        var errors = new List<ValidationError>();

        if (string.IsNullOrWhiteSpace(this.Id))
        {
            errors.Add(new ValidationError("MP001", "Manifest 'id' is required."));
        }

        if (string.IsNullOrWhiteSpace(this.Version))
        {
            errors.Add(new ValidationError("MP002", "Manifest 'version' is required."));
        }
        else if (this.ParseVersion() is null)
        {
            errors.Add(new ValidationError("MP003", $"Manifest 'version' ('{this.Version}') is not a valid version."));
        }

        if (!string.IsNullOrWhiteSpace(this.MinEngineVersion) && this.ParseMinEngineVersion() is null)
        {
            errors.Add(new ValidationError("MP004", $"Manifest 'minEngineVersion' ('{this.MinEngineVersion}') is not a valid version."));
        }

        if (string.IsNullOrWhiteSpace(this.EntryAssembly))
        {
            errors.Add(new ValidationError("MP005", "Manifest 'entryAssembly' is required."));
        }
        else if (PackagePath.EscapesRoot(this.EntryAssembly))
        {
            errors.Add(new ValidationError("MP006", $"Manifest 'entryAssembly' ('{this.EntryAssembly}') must stay within the package."));
        }

        foreach (var dep in this.Dependencies)
        {
            if (string.IsNullOrWhiteSpace(dep.Id))
            {
                errors.Add(new ValidationError("MP007", "A dependency is missing its 'id'."));
            }

            if (!string.IsNullOrWhiteSpace(dep.MinVersion) && !System.Version.TryParse(dep.MinVersion, out _))
            {
                errors.Add(new ValidationError("MP008", $"Dependency '{dep.Id}' has an invalid 'minVersion' ('{dep.MinVersion}')."));
            }

            if (!string.IsNullOrWhiteSpace(dep.MaxVersion) && !System.Version.TryParse(dep.MaxVersion, out _))
            {
                errors.Add(new ValidationError("MP009", $"Dependency '{dep.Id}' has an invalid 'maxVersion' ('{dep.MaxVersion}')."));
            }
        }

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors.ToArray());
    }
}

/// <summary>
/// 🔗 Phase 2.8.0 — A declared dependency on another module, optionally constrained to a version range.
/// </summary>
public sealed record ModuleDependency
{
    /// <summary>Gets the dependency module id.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Gets the inclusive minimum version (SemVer-style), or <c>null</c> for no lower bound.</summary>
    public string? MinVersion { get; init; }

    /// <summary>Gets the inclusive maximum version (SemVer-style), or <c>null</c> for no upper bound.</summary>
    public string? MaxVersion { get; init; }

    /// <summary>Checks whether a concrete <paramref name="version"/> satisfies this dependency's range~ 🎯.</summary>
    /// <param name="version">The candidate version.</param>
    /// <returns><c>true</c> when the version is within [MinVersion, MaxVersion].</returns>
    public bool IsSatisfiedBy(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);

        if (!string.IsNullOrWhiteSpace(this.MinVersion)
            && System.Version.TryParse(this.MinVersion, out var min)
            && version < min)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(this.MaxVersion)
            && System.Version.TryParse(this.MaxVersion, out var max)
            && version > max)
        {
            return false;
        }

        return true;
    }
}
