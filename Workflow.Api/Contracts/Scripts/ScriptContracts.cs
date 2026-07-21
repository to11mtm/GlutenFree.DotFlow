// <copyright file="ScriptContracts.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.Contracts.Scripts;

using System.Collections.Generic;
using Workflow.Scripting.Abstractions;
using Workflow.Scripting.Libraries;

/// <summary>
/// 🧪 Phase 3.1.6 — Request body for the script test endpoint~ ✨.
/// </summary>
/// <param name="Language">The script language id.</param>
/// <param name="Code">The script source.</param>
/// <param name="Inputs">Optional inputs passed as the script's <c>input</c>.</param>
/// <param name="Libraries">Optional library ids to import.</param>
/// <param name="Config">Optional sandbox config overrides.</param>
public sealed record ScriptTestRequest(
    string Language,
    string Code,
    IReadOnlyDictionary<string, object?>? Inputs = null,
    IReadOnlyList<string>? Libraries = null,
    ScriptTestConfig? Config = null);

/// <summary>
/// 🔒 Phase 3.1.6 — Optional sandbox overrides for a test run (clamped to host ceilings)~ ✨.
/// </summary>
/// <param name="TimeoutSeconds">Requested timeout.</param>
/// <param name="AllowNetwork">Whether to allow HTTP.</param>
/// <param name="AllowFileSystem">Whether to allow file access.</param>
/// <param name="AllowedPaths">Permitted file paths.</param>
public sealed record ScriptTestConfig(
    int? TimeoutSeconds = null,
    bool? AllowNetwork = null,
    bool? AllowFileSystem = null,
    IReadOnlyList<string>? AllowedPaths = null);

/// <summary>
/// 🧪 Phase 3.1.6 — Result of a script test run~ ✨.
/// </summary>
/// <param name="Success">Whether the script ran.</param>
/// <param name="Result">The return value (when successful).</param>
/// <param name="Logs">Captured log entries.</param>
/// <param name="VariableUpdates">Variable writes the script staged.</param>
/// <param name="DurationMs">Execution duration in milliseconds.</param>
/// <param name="Error">The error message (when unsuccessful).</param>
public sealed record ScriptTestResultDto(
    bool Success,
    object? Result,
    IReadOnlyList<ScriptLogEntryDto> Logs,
    IReadOnlyDictionary<string, object?> VariableUpdates,
    double DurationMs,
    string? Error);

/// <summary>
/// 📝 Phase 3.1.6 — A captured script log line DTO~ ✨.
/// </summary>
/// <param name="Level">The log level.</param>
/// <param name="Message">The message.</param>
public sealed record ScriptLogEntryDto(string Level, string Message)
{
    /// <summary>Projects a <see cref="ScriptLogEntry"/> into its DTO~ 📝.</summary>
    /// <param name="entry">The log entry.</param>
    /// <returns>The DTO.</returns>
    public static ScriptLogEntryDto From(ScriptLogEntry entry) => new(entry.Level, entry.Message);
}

/// <summary>
/// 📚 Phase 3.1.6 — A script library DTO (mirrors <see cref="ScriptLibraryDefinition"/>)~ ✨.
/// </summary>
/// <param name="LibraryId">The library id.</param>
/// <param name="Name">The name.</param>
/// <param name="Description">The description.</param>
/// <param name="Language">The language.</param>
/// <param name="Code">The source.</param>
/// <param name="ExportedFunctions">Documented exports.</param>
/// <param name="Dependencies">Dependency ids.</param>
public sealed record ScriptLibraryDto(
    string LibraryId,
    string Name,
    string? Description,
    string Language,
    string Code,
    IReadOnlyList<string> ExportedFunctions,
    IReadOnlyList<string> Dependencies)
{
    /// <summary>Projects a <see cref="ScriptLibraryDefinition"/> into its DTO~ 📚.</summary>
    /// <param name="library">The library.</param>
    /// <returns>The DTO.</returns>
    public static ScriptLibraryDto From(ScriptLibraryDefinition library)
        => new(library.LibraryId, library.Name, library.Description, library.Language, library.Code, library.ExportedFunctions, library.Dependencies);

    /// <summary>Projects this DTO into a <see cref="ScriptLibraryDefinition"/>~ 📚.</summary>
    /// <returns>The definition.</returns>
    public ScriptLibraryDefinition ToDefinition()
        => new()
        {
            LibraryId = this.LibraryId,
            Name = this.Name,
            Description = this.Description,
            Language = this.Language,
            Code = this.Code,
            ExportedFunctions = this.ExportedFunctions ?? System.Array.Empty<string>(),
            Dependencies = this.Dependencies ?? System.Array.Empty<string>(),
        };
}
