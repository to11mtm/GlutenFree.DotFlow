// <copyright file="PostgresExecutionHistoryRepository.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Postgres.Repositories;

using System.Text.Json;
using LinqToDB;
using Workflow.Core.Models;
using Workflow.Persistence.Abstractions;
using Workflow.Persistence.Models;
using Workflow.Persistence.Postgres.Data;
using Workflow.Persistence.Postgres.Data.Entities;

/// <summary>
/// 📊 PostgreSQL-backed implementation of <see cref="IExecutionHistoryRepository"/>~ ✨💖
/// </summary>
public sealed class PostgresExecutionHistoryRepository : IExecutionHistoryRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly WorkflowDataConnectionFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresExecutionHistoryRepository"/> class~ 🔌.
    /// </summary>
    /// <param name="factory">The data connection factory.</param>
    public PostgresExecutionHistoryRepository(WorkflowDataConnectionFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <inheritdoc/>
    public async Task<Guid> CreateExecutionAsync(ExecutionRecord record, CancellationToken ct = default)
    {
        var id = record.ExecutionId == Guid.Empty ? Guid.NewGuid() : record.ExecutionId;

        var entity = new ExecutionEntity
        {
            Id = id,
            WorkflowId = record.WorkflowId,
            State = record.State.ToString(),
            StartedAt = record.StartedAt,
            CompletedAt = record.CompletedAt,
            Inputs = record.Inputs is not null ? JsonSerializer.Serialize(record.Inputs, JsonOptions) : null,
            Outputs = record.Outputs is not null ? JsonSerializer.Serialize(record.Outputs, JsonOptions) : null,
            Error = record.Error,
            TriggeredBy = record.TriggeredBy,
        };

        await using var db = _factory.Create();
        await db.InsertAsync(entity, token: ct).ConfigureAwait(false);
        return id;
    }

    /// <inheritdoc/>
    public async Task UpdateExecutionStatusAsync(
        Guid executionId,
        ExecutionState state,
        DateTimeOffset? endTime = null,
        string? error = null,
        CancellationToken ct = default)
    {
        await using var db = _factory.Create();
        await db.Executions
            .Where(e => e.Id == executionId)
            .Set(e => e.State, state.ToString())
            .Set(e => e.CompletedAt, endTime)
            .Set(e => e.Error, error)
            .UpdateAsync(token: ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<ExecutionRecord?> GetExecutionAsync(Guid executionId, CancellationToken ct = default)
    {
        await using var db = _factory.Create();
        var entity = await db.Executions
            .FirstOrDefaultAsync(e => e.Id == executionId, token: ct)
            .ConfigureAwait(false);

        return entity is null ? null : MapToRecord(entity);
    }

    /// <inheritdoc/>
    public async Task<PagedResult<ExecutionRecord>> GetExecutionsForWorkflowAsync(
        Guid workflowId,
        ExecutionFilter filter,
        Pagination pagination,
        CancellationToken ct = default)
    {
        await using var db = _factory.Create();
        var query = db.Executions.Where(e => e.WorkflowId == workflowId);

        if (filter.States is { Length: > 0 })
        {
            IReadOnlyList<string> stateNames = filter.States.Select(s => s.ToString()).ToArray();
            query = query.Where(e => stateNames.Contains(e.State));
        }

        if (filter.StartedAfter.HasValue)
        {
            var after = filter.StartedAfter.Value;
            query = query.Where(e => e.StartedAt >= after);
        }

        if (filter.StartedBefore.HasValue)
        {
            var before = filter.StartedBefore.Value;
            query = query.Where(e => e.StartedAt <= before);
        }

        var totalCount = await query.CountAsync(token: ct).ConfigureAwait(false);
        var items = await query
            .OrderByDescending(e => e.StartedAt)
            .Skip(pagination.Skip)
            .Take(pagination.PageSize)
            .ToListAsync(token: ct)
            .ConfigureAwait(false);

        return new PagedResult<ExecutionRecord>(
            items.Select(MapToRecord).ToList(),
            totalCount,
            pagination.Page,
            pagination.PageSize);
    }

    /// <inheritdoc/>
    public async Task RecordNodeExecutionAsync(NodeExecutionRecord nodeRecord, CancellationToken ct = default)
    {
        await using var db = _factory.Create();

        var existing = await db.ExecutionNodes
            .FirstOrDefaultAsync(n => n.ExecutionId == nodeRecord.ExecutionId && n.NodeId == nodeRecord.NodeId, token: ct)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            await db.ExecutionNodes
                .Where(n => n.Id == existing.Id)
                .Set(n => n.State, nodeRecord.State.ToString())
                .Set(n => n.CompletedAt, nodeRecord.CompletedAt)
                .Set(n => n.Outputs, nodeRecord.Outputs is not null ? JsonSerializer.Serialize(nodeRecord.Outputs, JsonOptions) : null)
                .Set(n => n.Error, nodeRecord.Error)
                .Set(n => n.DurationMs, nodeRecord.Duration == default ? (long?)null : (long)nodeRecord.Duration.TotalMilliseconds)
                .UpdateAsync(token: ct)
                .ConfigureAwait(false);
        }
        else
        {
            await db.InsertAsync(new ExecutionNodeEntity
            {
                ExecutionId = nodeRecord.ExecutionId,
                NodeId = nodeRecord.NodeId,
                State = nodeRecord.State.ToString(),
                StartedAt = nodeRecord.StartedAt,
                CompletedAt = nodeRecord.CompletedAt,
                Inputs = nodeRecord.Inputs is not null ? JsonSerializer.Serialize(nodeRecord.Inputs, JsonOptions) : null,
                Outputs = nodeRecord.Outputs is not null ? JsonSerializer.Serialize(nodeRecord.Outputs, JsonOptions) : null,
                Error = nodeRecord.Error,
                DurationMs = nodeRecord.Duration == default ? null : (long)nodeRecord.Duration.TotalMilliseconds,
            }, token: ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<NodeExecutionRecord>> GetNodeExecutionsAsync(
        Guid executionId,
        CancellationToken ct = default)
    {
        await using var db = _factory.Create();
        var items = await db.ExecutionNodes
            .Where(n => n.ExecutionId == executionId)
            .OrderBy(n => n.StartedAt)
            .ToListAsync(token: ct)
            .ConfigureAwait(false);

        return items.Select(MapToNodeRecord).ToList();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ExecutionRecord MapToRecord(ExecutionEntity e) =>
        new ExecutionRecord(
            ExecutionId: e.Id,
            WorkflowId: e.WorkflowId,
            State: Enum.Parse<ExecutionState>(e.State),
            StartedAt: e.StartedAt,
            CompletedAt: e.CompletedAt,
            Inputs: e.Inputs is not null ? JsonSerializer.Deserialize<Dictionary<string, object?>>(e.Inputs, JsonOptions) : null,
            Outputs: e.Outputs is not null ? JsonSerializer.Deserialize<Dictionary<string, object?>>(e.Outputs, JsonOptions) : null,
            Error: e.Error,
            TriggeredBy: e.TriggeredBy);

    private static NodeExecutionRecord MapToNodeRecord(ExecutionNodeEntity n) =>
        new NodeExecutionRecord(
            ExecutionId: n.ExecutionId,
            NodeId: n.NodeId,
            State: Enum.Parse<NodeExecutionState>(n.State),
            StartedAt: n.StartedAt,
            CompletedAt: n.CompletedAt,
            Inputs: n.Inputs is not null ? JsonSerializer.Deserialize<Dictionary<string, object?>>(n.Inputs, JsonOptions) : null,
            Outputs: n.Outputs is not null ? JsonSerializer.Deserialize<Dictionary<string, object?>>(n.Outputs, JsonOptions) : null,
            Error: n.Error,
            Duration: n.DurationMs.HasValue ? TimeSpan.FromMilliseconds(n.DurationMs.Value) : default);
}

