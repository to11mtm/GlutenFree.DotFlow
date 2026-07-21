// <copyright file="DefaultWorkflowPathValidator.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.File.Internal;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// 🛡️ Default <see cref="IWorkflowPathValidator"/> — canonicalises paths and enforces the
/// sandbox described by <see cref="FileSystemModuleOptions"/>~ 📁✨.
/// </summary>
/// <remarks>
/// CopilotNote: Phase 2.5.a.0. Root containment uses a canonical-prefix check with a
/// trailing-separator guard so <c>C:\data-evil</c> does NOT match root <c>C:\data</c>.
/// Case sensitivity follows the OS (ordinal-ignore-case on Windows, ordinal elsewhere)~ 🚫.
/// </remarks>
public sealed class DefaultWorkflowPathValidator : IWorkflowPathValidator
{
    private readonly FileSystemModuleOptions options;
    private readonly ILogger<DefaultWorkflowPathValidator> logger;
    private readonly IReadOnlyList<string> canonicalRoots;
    private readonly StringComparison pathComparison;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultWorkflowPathValidator"/> class.
    /// </summary>
    /// <param name="options">The file-system module options.</param>
    /// <param name="logger">The logger.</param>
    public DefaultWorkflowPathValidator(
        IOptions<FileSystemModuleOptions> options,
        ILogger<DefaultWorkflowPathValidator> logger)
    {
        this.options = options.Value;
        this.logger = logger;
        this.pathComparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        this.canonicalRoots = this.options.AllowedRoots
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => NormalizeRoot(Path.GetFullPath(r)))
            .ToList();

        if (this.canonicalRoots.Count == 0 && this.options.UnrestrictedIfNoRoots)
        {
            this.logger.LogWarning(
                "🛡️⚠️ File-system modules are running UNRESTRICTED — no AllowedRoots configured. " +
                "Configure Workflow:FileSystem:AllowedRoots in production to sandbox file access~ 🚫");
        }
    }

    /// <inheritdoc />
    public PathValidationResult ValidatePath(string rawPath, PathAccessIntent intent)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return PathValidationResult.Reject("path is empty");
        }

        if (rawPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            return PathValidationResult.Reject("path contains invalid characters");
        }

        string resolved;
        try
        {
            resolved = Path.GetFullPath(rawPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return PathValidationResult.Reject($"path could not be resolved: {ex.Message}");
        }

        var rootsConfigured = this.canonicalRoots.Count > 0;

        if (!rootsConfigured)
        {
            if (!this.options.UnrestrictedIfNoRoots)
            {
                return PathValidationResult.Reject(
                    "no AllowedRoots configured and unrestricted access is disabled");
            }

            // Unrestricted mode: still apply the write-side blocked-extension policy~ 🚫
            return this.CheckExtension(resolved, intent) ?? PathValidationResult.Ok(resolved);
        }

        // Sandbox mode — the resolved path must live under a configured root~ 🛡️
        if (!this.IsUnderAnyRoot(resolved))
        {
            return PathValidationResult.Reject("path resolves outside the configured sandbox roots");
        }

        // Symlink re-check: resolve the final link target and re-validate containment~ 🔗
        if (this.options.ResolveSymlinks)
        {
            var linkTarget = ResolveLinkTargetOrSelf(resolved);
            if (!string.Equals(linkTarget, resolved, this.pathComparison) && !this.IsUnderAnyRoot(linkTarget))
            {
                return PathValidationResult.Reject("path is a symlink whose target escapes the sandbox roots");
            }
        }

        return this.CheckExtension(resolved, intent) ?? PathValidationResult.Ok(resolved);
    }

    private static string NormalizeRoot(string fullPath)
        => fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string ResolveLinkTargetOrSelf(string path)
    {
        try
        {
            FileSystemInfo? info = System.IO.File.Exists(path)
                ? new FileInfo(path)
                : Directory.Exists(path) ? new DirectoryInfo(path) : null;

            var target = info?.ResolveLinkTarget(returnFinalTarget: true);
            return target is null ? path : Path.GetFullPath(target.FullName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return path;
        }
    }

    private bool IsUnderAnyRoot(string resolvedPath)
    {
        var candidate = NormalizeRoot(resolvedPath);
        foreach (var root in this.canonicalRoots)
        {
            if (string.Equals(candidate, root, this.pathComparison))
            {
                return true;
            }

            var rootWithSep = root + Path.DirectorySeparatorChar;
            if (candidate.StartsWith(rootWithSep, this.pathComparison))
            {
                return true;
            }
        }

        return false;
    }

    private PathValidationResult? CheckExtension(string resolvedPath, PathAccessIntent intent)
    {
        if (intent != PathAccessIntent.Write)
        {
            return null;
        }

        var ext = Path.GetExtension(resolvedPath);
        if (string.IsNullOrEmpty(ext))
        {
            return null;
        }

        foreach (var blocked in this.options.BlockedExtensions)
        {
            var normalized = blocked.StartsWith('.') ? blocked : "." + blocked;
            if (string.Equals(ext, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return PathValidationResult.Reject($"write to blocked extension '{ext}' is not permitted");
            }
        }

        return null;
    }
}
