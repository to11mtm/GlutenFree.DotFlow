// <copyright file="ITransformScriptPreviewer.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Transform.Script.Abstractions;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Workflow.Scripting.Roslyn.Abstractions;

/// <summary>
/// 🔎 The result of a transform-script preview~ ✨.
/// </summary>
/// <param name="Success">Whether compile + run succeeded.</param>
/// <param name="Result">The materialised result (when successful).</param>
/// <param name="DurationMs">Execution duration in milliseconds.</param>
/// <param name="Diagnostics">Compile diagnostics.</param>
public record TransformScriptPreviewResult(
    bool Success,
    object? Result,
    long DurationMs,
    IReadOnlyList<ScriptDiagnostic> Diagnostics);

/// <summary>
/// 🔎 Compiles a transform body and runs it against caller-supplied sample rows in-memory
/// (compile-inclusive; no side effects, short timeout)~ ✨.
/// </summary>
public interface ITransformScriptPreviewer
{
    /// <summary>
    /// Previews a transform body~ 🔎.
    /// </summary>
    /// <param name="userBody">The user C# body.</param>
    /// <param name="sampleRows">Sample rows to run against.</param>
    /// <param name="sampleInputs">Sample named inputs.</param>
    /// <param name="timeoutSeconds">Execution timeout (default 5).</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The preview result.</returns>
    Task<TransformScriptPreviewResult> PreviewAsync(
        string userBody,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> sampleRows,
        IReadOnlyDictionary<string, object?> sampleInputs,
        int timeoutSeconds = 5,
        CancellationToken ct = default);
}
