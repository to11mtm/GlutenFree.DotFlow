// <copyright file="ScriptExecutionConfig.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Scripting.Abstractions;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 🔒 Phase 3.1 — Sandbox limits + capability gates for a single script execution. Every
/// outside-world capability is **deny-by-default** (D12); a node may loosen limits only up to
/// host-configured ceilings via <see cref="ClampTo"/>~ ✨.
/// </summary>
public sealed record ScriptExecutionConfig
{
    /// <summary>Gets the wall-clock execution timeout in seconds (default 30).</summary>
    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>Gets the memory ceiling in bytes (default 64 MB; honoured where the engine supports it).</summary>
    public long MaxMemoryBytes { get; init; } = 64L * 1024 * 1024;

    /// <summary>Gets a value indicating whether network (HTTP) access is allowed (default <c>false</c>, Q3).</summary>
    public bool AllowNetwork { get; init; }

    /// <summary>Gets a value indicating whether file-system access is allowed (default <c>false</c>).</summary>
    public bool AllowFileSystem { get; init; }

    /// <summary>Gets the file-system paths a script may touch when <see cref="AllowFileSystem"/> is on.</summary>
    public IReadOnlyList<string> AllowedPaths { get; init; } = Array.Empty<string>();

    /// <summary>Gets the maximum number of HTTP requests a script may make (default 10).</summary>
    public int MaxHttpRequests { get; init; } = 10;

    /// <summary>The default (fully locked-down) config~ 🔒.</summary>
    public static ScriptExecutionConfig Default { get; } = new();

    /// <summary>
    /// Produces a config clamped so it never exceeds the host ceilings — a workflow author can loosen
    /// limits only within what the operator permits~ 🛡️.
    /// </summary>
    /// <param name="ceilings">The host ceilings.</param>
    /// <returns>A clamped copy of this config.</returns>
    public ScriptExecutionConfig ClampTo(ScriptHostCeilings ceilings)
    {
        ArgumentNullException.ThrowIfNull(ceilings);

        var allowedPaths = ceilings.AllowedRootPaths.Count == 0
            ? this.AllowedPaths
            : this.AllowedPaths.Where(p => ceilings.IsPathPermitted(p)).ToList();

        return this with
        {
            TimeoutSeconds = Math.Clamp(this.TimeoutSeconds, 1, ceilings.MaxTimeoutSeconds),
            MaxMemoryBytes = Math.Min(this.MaxMemoryBytes, ceilings.MaxMemoryBytes),
            AllowNetwork = this.AllowNetwork && ceilings.AllowNetwork,
            AllowFileSystem = this.AllowFileSystem && ceilings.AllowFileSystem,
            AllowedPaths = allowedPaths,
            MaxHttpRequests = Math.Min(this.MaxHttpRequests, ceilings.MaxHttpRequests),
        };
    }
}

/// <summary>
/// 🛡️ Phase 3.1 — Host-operator ceilings that cap what any script node may request (bound from
/// <c>Scripting</c> config)~ ✨.
/// </summary>
public sealed record ScriptHostCeilings
{
    /// <summary>The configuration section name~ 📇.</summary>
    public const string SectionName = "Scripting";

    /// <summary>Gets the maximum permitted timeout in seconds (default 120).</summary>
    public int MaxTimeoutSeconds { get; init; } = 120;

    /// <summary>Gets the maximum permitted memory in bytes (default 256 MB).</summary>
    public long MaxMemoryBytes { get; init; } = 256L * 1024 * 1024;

    /// <summary>Gets a value indicating whether hosts allow scripts to use the network at all.</summary>
    public bool AllowNetwork { get; init; } = true;

    /// <summary>Gets a value indicating whether hosts allow scripts to touch the file system at all.</summary>
    public bool AllowFileSystem { get; init; }

    /// <summary>Gets the maximum permitted HTTP requests per execution (default 50).</summary>
    public int MaxHttpRequests { get; init; } = 50;

    /// <summary>Gets the absolute root paths under which script file access is permitted (empty = any allowed path passes host filtering).</summary>
    public IReadOnlyList<string> AllowedRootPaths { get; init; } = Array.Empty<string>();

    /// <summary>The permissive default ceilings (dev)~ 🛡️.</summary>
    public static ScriptHostCeilings Default { get; } = new();

    /// <summary>Checks whether a requested path falls under one of the host's allowed root paths~ 🛡️.</summary>
    /// <param name="path">The requested path.</param>
    /// <returns><c>true</c> when permitted.</returns>
    public bool IsPathPermitted(string path)
    {
        if (this.AllowedRootPaths.Count == 0)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var full = System.IO.Path.GetFullPath(path);
        return this.AllowedRootPaths.Any(root =>
        {
            var rootFull = System.IO.Path.GetFullPath(root);
            return full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase);
        });
    }
}
