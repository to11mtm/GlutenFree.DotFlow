// <copyright file="IWorkflowTableCatalog.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Abstractions;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// 📚 Catalog of tables available for workflow authoring (UI pickers + 2.4.b typed linq)~ 💖.
/// </summary>
/// <remarks>
/// <para>
/// V1 is <b>manual registration only</b> (Q4/D10) — the one-shot schema import lands in
/// 2.4.b.4 (D19) and versioned auto-discovery in 2.4.b.P3. The default impl is
/// <c>InMemoryWorkflowTableCatalog</c>~ 🌸.
/// </para>
/// <para>
/// CopilotNote: Phase 2.4.a.0 — this is intentionally a stub-level contract. The
/// <c>IBlobStore</c> namespace <c>compiled-modules/</c> is reserved alongside it for
/// 2.4.b's assembly cache (D9); no blob writes happen in 2.4.a~ ✨.
/// </para>
/// </remarks>
public interface IWorkflowTableCatalog
{
    /// <summary>
    /// Lists all catalogued tables for a named connection. 📋.
    /// </summary>
    /// <param name="connectionId">The named connection id (case-insensitive).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>All table metadata registered for the connection.</returns>
    public Task<IReadOnlyList<WorkflowTableMetadata>> ListAsync(string connectionId, CancellationToken ct = default);

    /// <summary>
    /// Inserts or updates a table registration (keyed on connection id + table name, case-insensitive). 💾.
    /// </summary>
    /// <param name="table">The table metadata to upsert.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the async operation.</returns>
    public Task UpsertAsync(WorkflowTableMetadata table, CancellationToken ct = default);

    /// <summary>
    /// Removes a table registration. 🗑️.
    /// </summary>
    /// <param name="connectionId">The named connection id (case-insensitive).</param>
    /// <param name="tableName">The table name (case-insensitive).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> when a registration was removed; <c>false</c> when unknown.</returns>
    public Task<bool> RemoveAsync(string connectionId, string tableName, CancellationToken ct = default);
}

/// <summary>
/// 📚 Metadata for a table known to the workflow table catalog~ ✨.
/// </summary>
/// <param name="ConnectionId">The named connection this table belongs to.</param>
/// <param name="TableName">The table name (unqualified).</param>
/// <param name="Schema">Optional schema (e.g. "public" on Postgres).</param>
/// <param name="Columns">Optional column metadata (populated by catalog import, 2.4.b.4).</param>
/// <param name="ClrTypeName">CLR type name for typed linq authoring — populated by 2.4.b only.</param>
/// <param name="AssemblyName">Assembly containing <paramref name="ClrTypeName"/> — populated by 2.4.b only.</param>
public sealed record WorkflowTableMetadata(
    string ConnectionId,
    string TableName,
    string? Schema = null,
    IReadOnlyList<WorkflowColumnMetadata>? Columns = null,
    string? ClrTypeName = null,
    string? AssemblyName = null);

/// <summary>
/// 📐 Column metadata for a catalogued table~.
/// </summary>
/// <param name="Name">Column name.</param>
/// <param name="DataType">Provider-reported data type (e.g. "integer", "text").</param>
/// <param name="Nullable">Whether the column allows NULL.</param>
public sealed record WorkflowColumnMetadata(string Name, string DataType, bool Nullable);


