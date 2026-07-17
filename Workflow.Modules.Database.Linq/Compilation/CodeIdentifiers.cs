// <copyright file="CodeIdentifiers.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Linq.Compilation;

using System.Text;

/// <summary>
/// 🔤 Helpers for turning table/column names into safe C# identifiers for codegen~ ✨.
/// </summary>
internal static class CodeIdentifiers
{
    /// <summary>
    /// Sanitises an arbitrary name into a valid C# identifier (letters/digits/underscore, non-digit start)~ 🧼.
    /// </summary>
    /// <param name="name">The raw name (table or column).</param>
    /// <param name="fallback">The identifier to use when <paramref name="name"/> is empty.</param>
    /// <returns>A valid C# identifier.</returns>
    public static string Sanitize(string? name, string fallback = "_")
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return fallback;
        }

        var sb = new StringBuilder(name.Length + 1);
        foreach (var ch in name)
        {
            sb.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
        }

        var result = sb.ToString();
        if (result.Length == 0)
        {
            return fallback;
        }

        // C# identifiers can't start with a digit — prefix an underscore.
        if (char.IsDigit(result[0]))
        {
            result = "_" + result;
        }

        return result;
    }

    /// <summary>Escapes a string literal for embedding in generated C# (quotes + backslashes)~ 🔒.</summary>
    /// <param name="value">The raw string.</param>
    /// <returns>The escaped literal contents (without surrounding quotes).</returns>
    public static string EscapeLiteral(string value)
        => value.Replace("\\", "\\\\", System.StringComparison.Ordinal)
                .Replace("\"", "\\\"", System.StringComparison.Ordinal);
}

