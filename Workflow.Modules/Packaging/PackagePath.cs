// <copyright file="PackagePath.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Packaging;

using System;
using System.IO;

/// <summary>
/// 🛡️ Phase 2.8.0 — Small helpers for validating package-relative paths (zip-slip / traversal guards)~ ✨.
/// </summary>
public static class PackagePath
{
    /// <summary>
    /// Determines whether a package-relative entry name would escape the package root when combined
    /// with a destination directory (absolute paths, <c>..</c> traversal, or rooted paths)~ 🛡️.
    /// </summary>
    /// <param name="entryName">The archive entry name (or manifest-relative path).</param>
    /// <returns><c>true</c> when the entry is unsafe.</returns>
    public static bool EscapesRoot(string entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName))
        {
            return true;
        }

        var normalized = entryName.Replace('\\', '/');

        if (Path.IsPathRooted(normalized) || normalized.Contains(':'))
        {
            return true;
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            if (segment == "..")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Safely resolves a package-relative entry to an absolute path under <paramref name="destinationRoot"/>,
    /// returning <c>null</c> when the entry would escape the root~ 🛡️.
    /// </summary>
    /// <param name="destinationRoot">The absolute destination directory.</param>
    /// <param name="entryName">The package-relative entry name.</param>
    /// <returns>The resolved absolute path, or <c>null</c> when unsafe.</returns>
    public static string? ResolveSafe(string destinationRoot, string entryName)
    {
        if (EscapesRoot(entryName))
        {
            return null;
        }

        var rootFull = Path.GetFullPath(destinationRoot);
        var combined = Path.GetFullPath(Path.Combine(rootFull, entryName.Replace('\\', '/')));

        var rootWithSep = rootFull.EndsWith(Path.DirectorySeparatorChar)
            ? rootFull
            : rootFull + Path.DirectorySeparatorChar;

        return combined.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase) || combined == rootFull
            ? combined
            : null;
    }
}
