// <copyright file="ScriptExecutionResult.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Scripting.Abstractions;

using System;
using System.Collections.Generic;

/// <summary>
/// 🎯 Phase 3.1 — The outcome of running a script: return value, staged variable writes, captured
/// logs, timing, and structured errors~ ✨.
/// </summary>
/// <param name="Success">Whether the script ran to completion without error.</param>
/// <param name="ReturnValue">The script's return value, marshalled to a CLR value (may be <c>null</c>).</param>
/// <param name="VariableUpdates">Variable writes the script staged (applied by the engine after the node, D7).</param>
/// <param name="Logs">Log lines the script emitted via the API (for the test endpoint).</param>
/// <param name="Error">A human-readable error message when <paramref name="Success"/> is <c>false</c>.</param>
/// <param name="Duration">How long execution took.</param>
public sealed record ScriptExecutionResult(
    bool Success,
    object? ReturnValue,
    IReadOnlyDictionary<string, object?> VariableUpdates,
    IReadOnlyList<ScriptLogEntry> Logs,
    string? Error,
    TimeSpan Duration)
{
    /// <summary>Creates a successful result~ ✅.</summary>
    /// <param name="returnValue">The script's return value.</param>
    /// <param name="variableUpdates">Staged variable writes.</param>
    /// <param name="logs">Captured log entries.</param>
    /// <param name="duration">Execution duration.</param>
    /// <returns>A successful <see cref="ScriptExecutionResult"/>.</returns>
    public static ScriptExecutionResult Ok(
        object? returnValue,
        IReadOnlyDictionary<string, object?> variableUpdates,
        IReadOnlyList<ScriptLogEntry> logs,
        TimeSpan duration)
        => new(true, returnValue, variableUpdates, logs, null, duration);

    /// <summary>Creates a failed result~ ❌.</summary>
    /// <param name="error">The error message.</param>
    /// <param name="logs">Any captured log entries before the failure.</param>
    /// <param name="duration">Execution duration.</param>
    /// <param name="variableUpdates">Any variable writes staged before the failure (usually none).</param>
    /// <returns>A failed <see cref="ScriptExecutionResult"/>.</returns>
    public static ScriptExecutionResult Fail(
        string error,
        IReadOnlyList<ScriptLogEntry> logs,
        TimeSpan duration,
        IReadOnlyDictionary<string, object?>? variableUpdates = null)
        => new(false, null, variableUpdates ?? new Dictionary<string, object?>(), logs, error, duration);
}

/// <summary>
/// 📝 Phase 3.1 — A single log line captured from a script's <c>workflow.log*</c> call~ ✨.
/// </summary>
/// <param name="Level">The log level (<c>debug</c>/<c>info</c>/<c>warning</c>/<c>error</c>).</param>
/// <param name="Message">The message text.</param>
public sealed record ScriptLogEntry(string Level, string Message);
