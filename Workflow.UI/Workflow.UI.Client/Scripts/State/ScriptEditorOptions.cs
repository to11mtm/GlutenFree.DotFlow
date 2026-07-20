// <copyright file="ScriptEditorOptions.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Scripts.State;

using System;
using System.Collections.Generic;

/// <summary>
/// ⚙️ Phase 3.4.0 — Framework-free editor preferences + the language↔Monaco-mode map. No Blazor or
/// JS-interop types (D2) so the React port re-uses the same model~ ✨.
/// </summary>
public sealed class ScriptEditorOptions
{
    /// <summary>The set of language ids Script Studio can highlight/edit (a superset of the runnable ones)~ 🌈.</summary>
    private static readonly Dictionary<string, string> LanguageToMonacoMode = new(StringComparer.OrdinalIgnoreCase)
    {
        ["javascript"] = "javascript",
        ["js"] = "javascript",
        ["typescript"] = "typescript",
        ["csharp"] = "csharp",
        ["cs"] = "csharp",
        ["lua"] = "lua",
        ["python"] = "python",
        ["py"] = "python",
        ["json"] = "json",
    };

    /// <summary>Language ids that can be *run* today (others are edit/highlight-only — Q2)~ ▶️.</summary>
    private static readonly HashSet<string> KnownRunnable = new(StringComparer.OrdinalIgnoreCase)
    {
        "javascript", "csharp", "lua",
    };

    /// <summary>Gets or sets the editor theme (<c>vs-dark</c> or <c>vs</c>).</summary>
    public string Theme { get; set; } = "vs-dark";

    /// <summary>Gets or sets the font size in pixels.</summary>
    public int FontSize { get; set; } = 13;

    /// <summary>Gets or sets a value indicating whether the gutter line numbers are shown.</summary>
    public bool LineNumbers { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the Monaco minimap is shown.</summary>
    public bool Minimap { get; set; }

    /// <summary>Gets or sets a value indicating whether long lines wrap.</summary>
    public bool WordWrap { get; set; }

    /// <summary>Maps a language id to its Monaco mode (defaults to <c>plaintext</c>)~ 🌈.</summary>
    /// <param name="languageId">The language id.</param>
    /// <returns>The Monaco language mode.</returns>
    public static string MonacoMode(string? languageId)
        => languageId is not null && LanguageToMonacoMode.TryGetValue(languageId, out var mode) ? mode : "plaintext";

    /// <summary>Whether a language id is known to be highlightable in Monaco~ 🌈.</summary>
    /// <param name="languageId">The language id.</param>
    /// <returns><c>true</c> when Monaco has a grammar for it.</returns>
    public static bool IsHighlightable(string? languageId)
        => languageId is not null && LanguageToMonacoMode.ContainsKey(languageId);

    /// <summary>Whether a language id is runnable today (Q2 — Python is edit-only for now)~ ▶️.</summary>
    /// <param name="languageId">The language id.</param>
    /// <returns><c>true</c> when the language has a registered executor by convention.</returns>
    public static bool IsKnownRunnable(string? languageId)
        => languageId is not null && KnownRunnable.Contains(languageId);
}
