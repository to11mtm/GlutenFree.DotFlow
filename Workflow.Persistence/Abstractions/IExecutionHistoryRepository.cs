// <copyright file="IExecutionHistoryRepository.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Abstractions;

using Workflow.Core.Models;
using Workflow.Persistence.Models;

/// <summary>
/// 📊 Repository for tracking workflow execution history and per-node execution records~ ✨💖
/// </summary>
public interface IExecutionHistoryRepository
{
    /// <summary>Creates a new execution record and returns its ID~ 🚀.</summary>
    Task<Guid> CreateExecutionAsync(ExecutionRecord record, CancellationToken ct = default);

    /// <summary>Updates an execution's state and optional end time / error~ 🔄.</summary>
    Task UpdateExecutionStatusAsync(
        Guid executionId,
        ExecutionState state,
        DateTimeOffset? endTime = null,
        string? error = null,
        CancellationToken ct = default);

    /// <summary>Gets an execution record by ID. Returns <c>null</c> if not found~ 🔍.</summary>
    Task<ExecutionRecord?> GetExecutionAsync(Guid executionId, CancellationToken ct = default);

    /// <summary>Gets a filtered, paginated list of executions for a given workflow~ 📄.</summary>
    Task<PagedResult<ExecutionRecord>> GetExecutionsForWorkflowAsync(
        Guid workflowId,
        ExecutionFilter filter,
        Pagination pagination,
        CancellationToken ct = default);

    /// <summary>Records a node-level execution event (insert or upsert)~ 🌸.</summary>
    Task RecordNodeExecutionAsync(NodeExecutionRecord nodeRecord, CancellationToken ct = default);

    /// <summary>Gets all node execution records for a given execution~ 📋.</summary>
    Task<IReadOnlyList<NodeExecutionRecord>> GetNodeExecutionsAsync(Guid executionId, CancellationToken ct = default);
}

