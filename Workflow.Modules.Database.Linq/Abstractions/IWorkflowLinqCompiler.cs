// <copyright file="IWorkflowLinqCompiler.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Linq.Abstractions;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Workflow.Core.Models;
using Workflow.Modules.Database.Abstractions;

/// <summary>
/// 🧬 Request to compile a typed linq node body at publish time (2.4.b.1)~ ✨.
/// </summary>
/// <param name="DefinitionId">Owning workflow definition id (for diagnostics + the 2.4.b.2 cache key).</param>
/// <param name="NodeId">The linq node id (for diagnostics + the 2.4.b.2 cache key).</param>
/// <param name="UserCodeBody">The user's method body only — wrapped by codegen into <c>WorkflowScript.ExecuteAsync</c>.</param>
/// <param name="SelectedTables">
/// Tables the node authors against. Each resolves its entity type EITHER from a plugin POCO
/// (<see cref="WorkflowTableMetadata.ClrTypeName"/> + <see cref="WorkflowTableMetadata.AssemblyName"/>)
/// OR from a column-generated POCO (<see cref="WorkflowTableMetadata.Columns"/>)~ 🧩.
/// </param>
/// <param name="InputSchema">Drives the generated <c>LinqInputs</c> accessor struct (§8.6 Phase 1).</param>
/// <param name="StrictTypeMode">
/// When <c>true</c>, a non-allowlisted input/column type is an error; otherwise it falls back to
/// <c>object?</c> with a warning.
/// </param>
public sealed record LinqCompileRequest(
    string DefinitionId,
    string NodeId,
    string UserCodeBody,
    IReadOnlyList<WorkflowTableMetadata> SelectedTables,
    ModuleSchema InputSchema,
    bool StrictTypeMode = false);

/// <summary>
/// 🧬 Result of a compile — emitted assembly bytes (in-memory) + diagnostics~ 💖.
/// </summary>
/// <param name="Success">True when the assembly emitted without errors.</param>
/// <param name="AssemblyBytes">The emitted assembly (null on failure). Blob-cached + HMAC-signed by 2.4.b.2.</param>
/// <param name="Errors">Fatal diagnostics (empty on success).</param>
/// <param name="Warnings">Non-fatal diagnostics (surfaced even on success — mitigates C9).</param>
/// <remarks>
/// CopilotNote: 2.4.b.1 deliberately returns raw <see cref="AssemblyBytes"/> and does NOT write to
/// <c>IBlobStore</c> or HMAC-sign — that lives in 2.4.b.2 (co-located with storage + the 2.4.b.3
/// load-time verify). This keeps the compiler pure/testable~ 🌸.
/// </remarks>
public sealed record LinqCompileResult(
    bool Success,
    byte[]? AssemblyBytes,
    IReadOnlyList<LinqDiagnostic> Errors,
    IReadOnlyList<LinqDiagnostic> Warnings)
{
    /// <summary>Builds a failed result from the given diagnostics~ ✖️.</summary>
    /// <param name="errors">The fatal diagnostics.</param>
    /// <param name="warnings">Optional warnings collected before the failure.</param>
    /// <returns>A failed <see cref="LinqCompileResult"/>.</returns>
    public static LinqCompileResult Fail(
        IReadOnlyList<LinqDiagnostic> errors,
        IReadOnlyList<LinqDiagnostic>? warnings = null)
        => new(false, null, errors, warnings ?? System.Array.Empty<LinqDiagnostic>());
}

/// <summary>
/// 🧬 Compiles a typed linq node body into a loadable assembly, enforcing the security allowlist~ ✨.
/// </summary>
public interface IWorkflowLinqCompiler
{
    /// <summary>Compiles the request into an in-memory assembly + diagnostics~ 🧬.</summary>
    /// <param name="request">The compile request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The compile result.</returns>
    Task<LinqCompileResult> CompileAsync(LinqCompileRequest request, CancellationToken ct = default);
}

