// <copyright file="ModulePackagingOptions.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Packaging;

/// <summary>
/// ⚙️ Phase 2.8.0 — Configuration for module packaging/install (bound from <c>Modules</c>)~ ✨.
/// </summary>
public sealed class ModulePackagingOptions
{
    /// <summary>The configuration section name~ 📇.</summary>
    public const string SectionName = "Modules";

    /// <summary>Gets or sets the root directory installed packages are extracted to (default <c>./modules</c>).</summary>
    public string PackagesPath { get; set; } = "./modules";

    /// <summary>Gets or sets the maximum accepted package size in bytes (default 50 MB).</summary>
    public long MaxPackageBytes { get; set; } = 50L * 1024 * 1024;

    /// <summary>
    /// Gets or sets a value indicating whether uploaded <c>.wfmod</c> bytes are archived to the
    /// blob store for re-provisioning. Defaults true when a persistence provider is configured (Q1).
    /// </summary>
    public bool ArchivePackages { get; set; }

    /// <summary>
    /// Gets or sets the running engine version (SemVer-style) used for the <c>MinEngineVersion</c>
    /// install gate (Q6). Set by the host from the engine assembly version; when <c>null</c> the gate
    /// is skipped.
    /// </summary>
    public string? EngineVersion { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether unsigned/untrusted assemblies are refused (D9). When
    /// <c>false</c> (default) they load with a warning; when <c>true</c> the install is blocked.
    /// </summary>
    public bool RequireSigned { get; set; }

    /// <summary>Gets or sets the trusted strong-name public key tokens (hex). Empty = trust any signed assembly.</summary>
    public IList<string> TrustedPublicKeyTokens { get; set; } = new List<string>();
}
