// <copyright file="LinqDiagnostic.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Linq.Abstractions;

/// <summary>🚦 Severity of a <see cref="LinqDiagnostic"/>~ ✨.</summary>
public enum LinqDiagnosticSeverity
{
    /// <summary>A non-fatal warning — compilation still succeeds~ ⚠️.</summary>
    Warning,

    /// <summary>A fatal error — compilation fails~ ✖️.</summary>
    Error,
}

/// <summary>
/// 🧬 A single compiler diagnostic surfaced to the authoring UI (2.4.b.1)~ 💖.
/// </summary>
/// <param name="Id">The diagnostic id (Roslyn <c>CSxxxx</c> or a <c>WFLINQxxx</c> pre-compile code).</param>
/// <param name="Severity">Whether this blocks compilation.</param>
/// <param name="Message">Human-readable message.</param>
/// <param name="Line">1-based line in the compiled unit (0 when not location-bound).</param>
/// <param name="Column">1-based column in the compiled unit (0 when not location-bound).</param>
/// <remarks>
/// CopilotNote: <see cref="Line"/>/<see cref="Column"/> point into the codegen-wrapped compilation
/// unit, not the user's raw body — user-relative mapping is a 2.4.b.5 refinement. The line/col are
/// still carried so the UI can render squigglies once that mapping lands~ 🌸.
/// </remarks>
public sealed record LinqDiagnostic(
    string Id,
    LinqDiagnosticSeverity Severity,
    string Message,
    int Line = 0,
    int Column = 0);

