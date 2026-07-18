// <copyright file="EncodingResolver.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.File.Internal;

using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// 🔤 Resolves friendly encoding keys (e.g. <c>"utf-8"</c>, <c>"latin1"</c>) to
/// <see cref="Encoding"/> instances~ ✨.
/// </summary>
/// <remarks>
/// CopilotNote: Phase 2.5.a.0. String keys (not a core enum) so plugins/hosts can pass
/// any alias <see cref="Encoding.GetEncoding(string)"/> understands. Default is UTF-8
/// without a BOM on write~ 🌸.
/// </remarks>
public static class EncodingResolver
{
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["utf8"] = "utf-8",
        ["utf-8"] = "utf-8",
        ["utf16"] = "utf-16",
        ["utf-16"] = "utf-16",
        ["utf-16le"] = "utf-16",
        ["unicode"] = "utf-16",
        ["utf-16be"] = "unicodeFFFE",
        ["ascii"] = "us-ascii",
        ["us-ascii"] = "us-ascii",
        ["latin1"] = "iso-8859-1",
        ["latin-1"] = "iso-8859-1",
        ["iso-8859-1"] = "iso-8859-1",
    };

    /// <summary>
    /// The default encoding used when no key is supplied (UTF-8, no BOM)~ 📄.
    /// </summary>
    public static Encoding Default { get; } = new UTF8Encoding(false);

    /// <summary>
    /// Attempts to resolve an encoding key~ 🔤.
    /// </summary>
    /// <param name="key">The friendly encoding key, or <c>null</c>/empty for the default.</param>
    /// <param name="encoding">The resolved encoding when successful.</param>
    /// <param name="error">The error message when resolution fails.</param>
    /// <returns><c>true</c> when resolved; otherwise <c>false</c>.</returns>
    public static bool TryResolve(string? key, out Encoding encoding, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(key))
        {
            encoding = Default;
            return true;
        }

        var lookup = Aliases.TryGetValue(key.Trim(), out var canonical) ? canonical : key.Trim();

        try
        {
            // UTF-8 special-case → no BOM on write, matches Default~ 📄
            if (string.Equals(lookup, "utf-8", StringComparison.OrdinalIgnoreCase))
            {
                encoding = Default;
                return true;
            }

            // Latin1 is natively supported in .NET 5+ without the CodePages provider~ 🔤
            if (string.Equals(lookup, "iso-8859-1", StringComparison.OrdinalIgnoreCase))
            {
                encoding = Encoding.Latin1;
                return true;
            }

            encoding = Encoding.GetEncoding(lookup);
            return true;
        }
        catch (ArgumentException)
        {
            encoding = Default;
            error = $"unknown encoding '{key}'";
            return false;
        }
    }
}
