// <copyright file="IWorkflowLinqPreviewer.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Linq.Abstractions;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// 🔎 Request to preview a typed linq body against a throwaway seeded SQLite sandbox (2.4.b.4)~ ✨.
/// </summary>
/// <param name="Compile">The compile request (code + tables + input schema).</param>
/// <param name="Inputs">Sample input values exposed to the body via <c>LinqInputs</c> (defaults to empty).</param>
/// <param name="SampleRowsPerTable">How many sample rows to seed per selected table (default 3).</param>
public sealed record LinqPreviewRequest(
    LinqCompileRequest Compile,
    IReadOnlyDictionary<string, object?>? Inputs = null,
    int SampleRowsPerTable = 3);

/// <summary>
/// 🔎 Request to preview a typed linq body against the <b>real</b> named connection, inside a
/// transaction that is always rolled back (2.4.b.P2)~ ⚠️.
/// </summary>
/// <param name="Compile">The compile request (code + tables + input schema).</param>
/// <param name="ProviderName">The linq2db provider name (from <c>IDbProviderRegistry</c>).</param>
/// <param name="ConnectionString">The resolved connection string.</param>
/// <param name="Inputs">Input values exposed to the body via <c>LinqInputs</c>.</param>
public sealed record LinqLivePreviewRequest(
    LinqCompileRequest Compile,
    string ProviderName,
    string ConnectionString,
    IReadOnlyDictionary<string, object?>? Inputs = null);

/// <summary>
/// 🔎 Result of a sandbox preview~ 💖.
/// </summary>
/// <param name="Success">True when compile + run succeeded.</param>
/// <param name="Rows">Materialised sample rows (null for scalars / on failure).</param>
/// <param name="Result">Raw materialised value.</param>
/// <param name="RowCount">Row/element count when the result is a sequence.</param>
/// <param name="DurationMs">Elapsed run time.</param>
/// <param name="Diagnostics">Compile diagnostics (errors on failure; warnings on success).</param>
/// <param name="SampleRowsSeeded">Total sample rows seeded across all tables.</param>
/// <param name="PostRollbackRowCount">
/// Row count of the first seeded table AFTER the always-rollback wrapper — equals the per-table seed
/// count when the user body's mutations were correctly discarded (§8.5/C6).
/// </param>
/// <param name="Statements">
/// The SQL the body produced — every statement linq2db actually executed, plus the rendered SQL of an
/// unmaterialised <c>IQueryable</c> result (<c>ToSqlQuery()</c>)~ 🧾.
/// </param>
public sealed record LinqPreviewResult(
    bool Success,
    IReadOnlyList<IReadOnlyDictionary<string, object?>>? Rows,
    object? Result,
    int? RowCount,
    long DurationMs,
    IReadOnlyList<LinqDiagnostic> Diagnostics,
    int SampleRowsSeeded,
    int? PostRollbackRowCount,
    IReadOnlyList<string>? Statements = null);

/// <summary>
/// 🔎 Compiles + previews a typed linq body in a rollback-only in-memory SQLite sandbox~ ✨.
/// </summary>
/// <remarks>
/// CopilotNote: SQLite preview semantics ≠ the target provider (C10) — a real-provider preview is
/// tracked as 2.4.b.P2. The user body runs inside an always-rolled-back transaction so side effects
/// never persist even in the throwaway DB (§8.5)~ 🌸.
/// </remarks>
public interface IWorkflowLinqPreviewer
{
    /// <summary>Compiles + previews the request~ 🎯.</summary>
    /// <param name="request">The preview request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The preview result.</returns>
    Task<LinqPreviewResult> PreviewAsync(LinqPreviewRequest request, CancellationToken ct = default);

    /// <summary>
    /// Compiles + runs the body against the <b>real</b> connection inside an always-rolled-back
    /// transaction — dialect-correct SQL and real data, with no persisted side effects~ ⚠️.
    /// </summary>
    /// <param name="request">The live preview request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The preview result (rows + captured SQL).</returns>
    Task<LinqPreviewResult> PreviewLiveAsync(LinqLivePreviewRequest request, CancellationToken ct = default);
}

