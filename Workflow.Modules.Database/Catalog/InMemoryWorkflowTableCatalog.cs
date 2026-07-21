// <copyright file="InMemoryWorkflowTableCatalog.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Catalog;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Workflow.Modules.Database.Abstractions;

/// <summary>
/// 📚 In-memory table catalog — manual upsert only for V1 (Q4/D10)~ ✨.
/// </summary>
/// <remarks>
/// CopilotNote: Phase 2.4.a.0 — keyed on (connectionId, tableName), both case-insensitive.
/// One-shot schema import populates this in 2.4.b.4 (D19); versioned auto-discovery is 2.4.b.P3.
/// Process-local — registrations don't survive restart~ 🌸.
/// </remarks>
public sealed class InMemoryWorkflowTableCatalog : IWorkflowTableCatalog
{
    /// <summary>
    /// Store keyed on "connectionId::tableName" (case-insensitive). 🗃️.
    /// </summary>
    private readonly ConcurrentDictionary<string, WorkflowTableMetadata> store =
        new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public Task<IReadOnlyList<WorkflowTableMetadata>> ListAsync(string connectionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);

        IReadOnlyList<WorkflowTableMetadata> tables = this.store.Values
            .Where(t => string.Equals(t.ConnectionId, connectionId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.TableName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Task.FromResult(tables);
    }

    /// <inheritdoc/>
    public Task UpsertAsync(WorkflowTableMetadata table, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentException.ThrowIfNullOrWhiteSpace(table.ConnectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(table.TableName);

        this.store[Key(table.ConnectionId, table.TableName)] = table;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<bool> RemoveAsync(string connectionId, string tableName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        return Task.FromResult(this.store.TryRemove(Key(connectionId, tableName), out _));
    }

    /// <summary>
    /// Composite store key builder~ 🔑.
    /// </summary>
    private static string Key(string connectionId, string tableName)
        => $"{connectionId}::{tableName}";
}

