// <copyright file="ScriptDtos.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Api.Dtos;

using System.Collections.Generic;
using System.Text.Json;

/// <summary>
/// 🧪 Phase 3.4.0 — Client mirror of <c>ScriptTestRequest</c> (the <c>POST /api/v1/scripts/test</c>
/// body). Plain STJ record — no Blazor/LanguageExt types (D2)~ ✨.
/// </summary>
/// <param name="Language">The script language id.</param>
/// <param name="Code">The script source.</param>
/// <param name="Inputs">Optional inputs surfaced to the script as <c>input</c>.</param>
/// <param name="Libraries">Optional library ids to import.</param>
/// <param name="Config">Optional sandbox config overrides.</param>
public sealed record ScriptTestRequestDto(
    string Language,
    string Code,
    IReadOnlyDictionary<string, JsonElement>? Inputs = null,
    IReadOnlyList<string>? Libraries = null,
    ScriptTestConfigDto? Config = null);

/// <summary>
/// 🔒 Phase 3.4.0 — Client mirror of <c>ScriptTestConfig</c> (clamped server-side to host ceilings)~ ✨.
/// </summary>
/// <param name="TimeoutSeconds">Requested timeout.</param>
/// <param name="AllowNetwork">Whether to allow HTTP.</param>
/// <param name="AllowFileSystem">Whether to allow file access.</param>
/// <param name="AllowedPaths">Permitted file paths.</param>
public sealed record ScriptTestConfigDto(
    int? TimeoutSeconds = null,
    bool? AllowNetwork = null,
    bool? AllowFileSystem = null,
    IReadOnlyList<string>? AllowedPaths = null);

/// <summary>
/// 🧪 Phase 3.4.0 — Client mirror of <c>ScriptTestResultDto</c>. A script *error* is a normal
/// <c>200</c> body with <see cref="Success"/> = <c>false</c> (the 3.1.6 convention)~ ✨.
/// </summary>
/// <param name="Success">Whether the script ran.</param>
/// <param name="Result">The return value (when successful).</param>
/// <param name="Logs">Captured log entries.</param>
/// <param name="VariableUpdates">Variable writes the script staged.</param>
/// <param name="DurationMs">Execution duration in milliseconds.</param>
/// <param name="Error">The error message (when unsuccessful).</param>
public sealed record ScriptTestResultDto(
    bool Success,
    JsonElement Result,
    IReadOnlyList<ScriptLogEntryDto> Logs,
    IReadOnlyDictionary<string, JsonElement> VariableUpdates,
    double DurationMs,
    string? Error);

/// <summary>📝 Phase 3.4.0 — Client mirror of <c>ScriptLogEntryDto</c>~ ✨.</summary>
/// <param name="Level">The log level (Debug/Info/Warning/Error).</param>
/// <param name="Message">The message.</param>
public sealed record ScriptLogEntryDto(string Level, string Message);

/// <summary>📚 Phase 3.4.0 — Client mirror of the script library DTO~ ✨.</summary>
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
    IReadOnlyList<string> Dependencies);

/// <summary>🌐 Phase 3.4.0 — Client mirror of <c>ScriptLanguageInfo</c> (from <c>/scripts/languages</c>)~ ✨.</summary>
/// <param name="LanguageId">The language id (e.g. <c>javascript</c>).</param>
/// <param name="DisplayName">The display name (e.g. <c>JavaScript</c>).</param>
public sealed record ScriptLanguageDto(string LanguageId, string DisplayName);
