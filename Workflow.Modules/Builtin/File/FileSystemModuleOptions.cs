// <copyright file="FileSystemModuleOptions.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.File;

using System;

/// <summary>
/// ⚙️ Configuration options for the file-system built-in module family~ 📁✨.
/// </summary>
/// <remarks>
/// <para>
/// Bind from configuration section <see cref="SectionName"/>
/// (e.g. <c>Workflow:FileSystem</c>) in host startup~ 🌸.
/// </para>
/// <para>
/// CopilotNote: Phase 2.5.a.0. The sandbox posture is governed by
/// <see cref="AllowedRoots"/> + <see cref="UnrestrictedIfNoRoots"/>. When roots are
/// configured every file path must resolve inside one of them (deny-by-default);
/// when none are configured the default is unrestricted-with-a-warning (Q1 V1 rec) so
/// local dev "just works". Hosts SHOULD configure roots in production~ 🛡️.
/// </para>
/// </remarks>
public sealed class FileSystemModuleOptions
{
    /// <summary>
    /// Configuration section name for binding~ 🏷️.
    /// </summary>
    public const string SectionName = "Workflow:FileSystem";

    /// <summary>
    /// Default maximum read size in bytes (16 MiB)~ 🧠.
    /// </summary>
    public const long DefaultMaxReadBytesConst = 16L * 1024 * 1024;

    /// <summary>
    /// Gets or sets the sandbox root directories~ 🛡️
    /// When non-empty, any path resolving outside every root fails validation.
    /// When empty, behaviour is governed by <see cref="UnrestrictedIfNoRoots"/>.
    /// </summary>
    public string[] AllowedRoots { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets a value indicating whether file access is unrestricted when
    /// <see cref="AllowedRoots"/> is empty~ 🚪 Default <c>true</c> (dev ergonomics).
    /// </summary>
    public bool UnrestrictedIfNoRoots { get; set; } = true;

    /// <summary>
    /// Gets or sets file extensions blocked on <b>write</b> operations~ 🚫
    /// Comparison is case-insensitive; entries may include or omit the leading dot.
    /// </summary>
    public string[] BlockedExtensions { get; set; } =
    {
        ".exe", ".dll", ".bat", ".cmd", ".ps1", ".sh",
    };

    /// <summary>
    /// Gets or sets the default maximum read size in bytes when a module doesn't
    /// specify <c>maxSize</c>~ 🧠 Default 16 MiB.
    /// </summary>
    public long DefaultMaxReadBytes { get; set; } = DefaultMaxReadBytesConst;

    /// <summary>
    /// Gets or sets a value indicating whether symlinks are resolved and re-checked
    /// against <see cref="AllowedRoots"/>~ 🔗 Only consulted when roots are configured.
    /// </summary>
    public bool ResolveSymlinks { get; set; } = true;
}
