// <copyright file="DbOperationModels.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Models;

using System.Collections.Generic;

/// <summary>
/// 💼 A single operation inside a <c>builtin.database.transaction</c> — one SQL statement
/// driven either in single-mode (<see cref="Parameters"/>) or batch-mode (<see cref="ParameterSets"/>)~ ✨.
/// </summary>
/// <remarks>
/// CopilotNote: Phase 2.4.a.3 — <see cref="Parameters"/> and <see cref="ParameterSets"/> are
/// MUTUALLY EXCLUSIVE (validated up front). Uses plain BCL collections rather than LanguageExt
/// <c>HashMap</c>/<c>Arr</c> so the parser can materialise it cheaply from loosely-typed workflow
/// config. Savepoint fields land in 2.4.a.P2~ 🌸.
/// </remarks>
public sealed record DbOperationSpec
{
    /// <summary>Gets the verbatim SQL. Never template-expanded (D7).</summary>
    public required string Sql { get; init; }

    /// <summary>
    /// Gets the single-row parameter set. Mutually exclusive with <see cref="ParameterSets"/>.
    /// When both are null the statement runs with no parameters.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? Parameters { get; init; }

    /// <summary>
    /// Gets the N parameter sets for batch-mode. Mutually exclusive with <see cref="Parameters"/>.
    /// Each entry drives one execution of the same SQL within the open transaction; a length-0
    /// list is a no-op. <c>affectedRows = 0</c> for a set is NOT a failure (WHERE-guard no-op).
    /// </summary>
    public IReadOnlyList<IReadOnlyDictionary<string, object?>>? ParameterSets { get; init; }

    /// <summary>Gets a value indicating whether the last-insert id should be resolved after this op.</summary>
    public bool ExpectLastInsertId { get; init; }

    /// <summary>Gets a value indicating whether this op is batch-mode (driven by <see cref="ParameterSets"/>).</summary>
    public bool IsBatch => this.ParameterSets is not null;
}

/// <summary>
/// 📊 Per-operation result inside a transaction~ ✨.
/// </summary>
/// <param name="AffectedRows">Rows affected (sum across all sets for a batch op).</param>
/// <param name="LastInsertId">Auto-generated id when requested (last non-null across a batch), else null.</param>
/// <param name="IsBatchOp">True when the op was driven by <c>ParameterSets</c>.</param>
/// <param name="BatchExecutionCount">Number of param-set iterations (0 for single-mode).</param>
public sealed record DbOperationResult(
    int AffectedRows,
    long? LastInsertId,
    bool IsBatchOp,
    int BatchExecutionCount);

/// <summary>
/// 🚨 Failure context for the operation that aborted a transaction~ 🌸.
/// </summary>
/// <param name="OperationIndex">Zero-based index of the failing op within <c>operations</c>.</param>
/// <param name="SqlState">Provider SQLSTATE when available.</param>
/// <param name="Message">Human-readable error (enriched via DbErrorContext).</param>
/// <param name="BatchRowIndex">Which param-set row failed (null for single-mode ops).</param>
public sealed record DbOperationError(
    int OperationIndex,
    string? SqlState,
    string Message,
    int? BatchRowIndex);
