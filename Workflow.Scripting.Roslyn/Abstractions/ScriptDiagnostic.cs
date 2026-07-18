// <copyright file="ScriptDiagnostic.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Scripting.Roslyn.Abstractions;

/// <summary>🚦 Severity of a script diagnostic~ ✨.</summary>
public enum ScriptDiagnosticSeverity
{
    /// <summary>Informational.</summary>
    Info,

    /// <summary>A non-fatal warning.</summary>
    Warning,

    /// <summary>A fatal error — compilation is rejected.</summary>
    Error,
}

/// <summary>
/// 🩺 A single compile/validation diagnostic from the scripting core~ ✨.
/// </summary>
/// <param name="Id">A stable diagnostic id (e.g. <c>WFSCRIPT100</c>).</param>
/// <param name="Severity">The severity.</param>
/// <param name="Message">The human-readable message.</param>
/// <param name="Line">Optional 1-based line in the user body.</param>
/// <param name="Column">Optional 1-based column in the user body.</param>
public record ScriptDiagnostic(
    string Id,
    ScriptDiagnosticSeverity Severity,
    string Message,
    int? Line = null,
    int? Column = null);
