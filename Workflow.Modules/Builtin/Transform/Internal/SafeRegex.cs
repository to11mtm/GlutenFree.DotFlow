// <copyright file="SafeRegex.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.Transform.Internal;

using System;
using System.Text.RegularExpressions;

/// <summary>
/// 🛡️ ReDoS-safe regex construction (Q8) — prefers <see cref="RegexOptions.NonBacktracking"/>,
/// always enforces a match timeout as a backstop~ ✨.
/// </summary>
public static class SafeRegex
{
    /// <summary>The default per-match timeout~ ⏱️.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Creates a regex with ReDoS mitigations~ 🛡️.
    /// </summary>
    /// <param name="pattern">The regex pattern.</param>
    /// <param name="ignoreCase">Whether to ignore case.</param>
    /// <param name="timeout">Optional match timeout (defaults to 1s).</param>
    /// <returns>The compiled regex.</returns>
    public static Regex Create(string pattern, bool ignoreCase = false, TimeSpan? timeout = null)
    {
        var options = ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
        var effectiveTimeout = timeout ?? DefaultTimeout;

        try
        {
            // NonBacktracking eliminates catastrophic backtracking entirely where the pattern allows~ 🛡️
            return new Regex(pattern, options | RegexOptions.NonBacktracking, effectiveTimeout);
        }
        catch (NotSupportedException)
        {
            // Pattern uses a construct NonBacktracking can't express (backreferences, lookaround);
            // fall back to the standard engine but keep the match timeout as the backstop~ ⏱️
            return new Regex(pattern, options, effectiveTimeout);
        }
    }
}
