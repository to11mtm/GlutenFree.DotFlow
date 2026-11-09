// <copyright file="ModuleTrust.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Security;

using System;
using System.Collections.Generic;

/// <summary>🔏 Stage 7.4 — how far an installed module's origin is established.</summary>
public enum ModuleTrust
{
    /// <summary>Signed by a trusted publisher, and the files match what was verified.</summary>
    Verified,

    /// <summary>
    /// Only the entry assembly's strong-name public key token matched <c>TrustedPublicKeyTokens</c>.
    /// Accepted by <c>RequireSigned</c>, but not proof of authorship: a token can be copied.
    /// </summary>
    KeyPinOnly,

    /// <summary>Neither signed by a trusted publisher nor key-pinned.</summary>
    Unsigned,

    /// <summary>Signed, but not by a trusted publisher (or no longer trusted).</summary>
    Untrusted,

    /// <summary>Files on disk differ from the ones verified at install.</summary>
    ChangedOnDisk,
}

/// <summary>🔏 Stage 7.4 — an installed module version's trust, as last checked.</summary>
/// <param name="Trust">The trust level.</param>
/// <param name="Publisher">The signing publisher, when signed.</param>
/// <param name="Thumbprint">The signer certificate's SHA-256 thumbprint, when signed.</param>
/// <param name="TrustSource">Why the signer is trusted (pinned / CA), when verified.</param>
/// <param name="Messages">Why it's less than verified, if it is.</param>
/// <param name="CheckedAt">When this was determined.</param>
public sealed record ModuleTrustInfo(
    ModuleTrust Trust,
    string? Publisher,
    string? Thumbprint,
    PackageTrustSource TrustSource,
    IReadOnlyList<string> Messages,
    DateTimeOffset CheckedAt)
{
    /// <summary>Gets a value indicating whether <c>RequireSigned</c> accepts this.</summary>
    public bool SatisfiesRequireSigned => this.Trust is ModuleTrust.Verified or ModuleTrust.KeyPinOnly;
}

/// <summary>
/// 🔏 Stage 7.4 — what was verified when a package was installed, kept beside it as
/// <c>.dotflow-install.json</c>, so later loads can tell when the files have changed.
/// </summary>
/// <param name="FormatVersion">The record format version.</param>
/// <param name="Trust">The trust at install.</param>
/// <param name="Publisher">The signing publisher, when signed.</param>
/// <param name="Thumbprint">The signer's SHA-256 thumbprint, when signed.</param>
/// <param name="TrustSource">Why the signer was trusted.</param>
/// <param name="InstalledAt">When it was installed.</param>
/// <param name="Files">Every installed file (package-relative, '/' separators) → base64 SHA-256.</param>
public sealed record ModuleInstallRecord(
    int FormatVersion,
    ModuleTrust Trust,
    string? Publisher,
    string? Thumbprint,
    PackageTrustSource TrustSource,
    DateTimeOffset InstalledAt,
    IReadOnlyDictionary<string, string> Files)
{
    /// <summary>The record's file name inside the installed version directory.</summary>
    public const string FileName = ".dotflow-install.json";
}

/// <summary>🔏 Stage 7.4 — whether an assembly may be (re)loaded, and why.</summary>
/// <param name="Allowed">Whether the load may go ahead.</param>
/// <param name="Trust">The trust found.</param>
/// <param name="Messages">Warnings (when allowed) or reasons (when refused).</param>
public sealed record ModuleLoadCheck(bool Allowed, ModuleTrustInfo Trust, IReadOnlyList<string> Messages);

/// <summary>
/// 🔏 Stage 7.4 — checked before a module assembly is loaded outside an install (startup re-load,
/// hot reload). With <c>RequireSigned</c> off, problems only warn; with it on, the load is refused.
/// </summary>
public interface IModuleLoadGate
{
    /// <summary>Checks an assembly before it's (re)loaded.</summary>
    /// <param name="assemblyPath">The assembly about to be loaded.</param>
    /// <returns>The decision.</returns>
    ModuleLoadCheck CheckBeforeLoad(string assemblyPath);
}
