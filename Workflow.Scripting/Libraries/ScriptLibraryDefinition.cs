// <copyright file="ScriptLibraryDefinition.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Scripting.Libraries;

using System;
using System.Collections.Generic;

/// <summary>
/// 📚 Phase 3.1.5 — A reusable, per-language script snippet importable by scripts (D9)~ ✨.
/// </summary>
public sealed record ScriptLibraryDefinition
{
    /// <summary>Gets the unique library id (used for imports).</summary>
    public string LibraryId { get; init; } = string.Empty;

    /// <summary>Gets the display name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the optional description.</summary>
    public string? Description { get; init; }

    /// <summary>Gets the language id (must match a registered executor).</summary>
    public string Language { get; init; } = string.Empty;

    /// <summary>Gets the library source code.</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>Gets the documented exported function names (metadata only).</summary>
    public IReadOnlyList<string> ExportedFunctions { get; init; } = Array.Empty<string>();

    /// <summary>Gets the ids of other same-language libraries this one depends on.</summary>
    public IReadOnlyList<string> Dependencies { get; init; } = Array.Empty<string>();
}
