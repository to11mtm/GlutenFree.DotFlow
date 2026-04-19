// <copyright file="SqliteExecutionHistoryRepository.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Sqlite.Repositories;

using System.Text.Json;
using LinqToDB;
using Workflow.Core.Models;
using Workflow.Persistence.Abstractions;
using Workflow.Persistence.Models;
using Workflow.Persistence.Sqlite.Data;
using Workflow.Persistence.Sqlite.Data.Entities;

/// <summary>
/// 📊 SQLite-backed implementation of <see cref="IExecutionHistoryRepository"/>~ ✨💖
/// </summary>
/// <remarks>
/// CopilotNote: Execution records use ISO-8601 strings for timestamps and enum .Name for states.
/// Node execution records are upserted by (execution_id, node_id)~ UwU 🌸
/// </remarks>
public sealed class SqliteExecutionHistoryRepository : IExecutionHistoryRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly WorkflowDataConnectionFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteExecutionHistoryRepository"/> class~ 🔌.
    /// </summary>
    /// <param name="factory">The data connection factory.</param>
    public SqliteExecutionHistoryRepository(WorkflowDataConnectionFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <inheritdoc/>
    public async Task<Guid> CreateExecutionAsync(ExecutionRecord record, CancellationToken ct = default)
    {
        var id = record.ExecutionId == Guid.Empty ? Guid.NewGuid() : record.ExecutionId;

        var entity = new ExecutionEntity
        {
            Id = id.ToString(),
            WorkflowId = record.WorkflowId.ToString(),
            State = record.State.ToString(),
            StartedAt = record.StartedAt.ToString("O"),
            CompletedAt = record.CompletedAt?.ToString("O"),
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
        var idStr = executionId.ToString();
        await db.Executions
            .Where(e => e.Id == idStr)
            .Set(e => e.State, state.ToString())
            .Set(e => e.CompletedAt, endTime.HasValue ? endTime.Value.ToString("O") : null)
            .Set(e => e.Error, error)
            .UpdateAsync(token: ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<ExecutionRecord?> GetExecutionAsync(Guid executionId, CancellationToken ct = default)
    {
        await using var db = _factory.Create();
        var entity = await db.Executions
            .FirstOrDefaultAsync(e => e.Id == executionId.ToString(), token: ct)
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
        var query = db.Executions.Where(e => e.WorkflowId == workflowId.ToString());

        if (filter.States is { Length: > 0 })
        {
            var stateNames = filter.States.Select(s => s.ToString()).ToArray();
            query = query.Where(e => stateNames.Contains(e.State));
        }

        if (filter.StartedAfter.HasValue)
        {
            var after = filter.StartedAfter.Value.ToString("O");
            query = query.Where(e => string.Compare(e.StartedAt, after, StringComparison.Ordinal) >= 0);
        }

        if (filter.StartedBefore.HasValue)
        {
            var before = filter.StartedBefore.Value.ToString("O");
            query = query.Where(e => string.Compare(e.StartedAt, before, StringComparison.Ordinal) <= 0);
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
        var execIdStr = nodeRecord.ExecutionId.ToString();

        // Check if a row already exists for this (execution_id, node_id) pair — upsert semantics~ 🌸
        var existing = await db.ExecutionNodes
            .FirstOrDefaultAsync(
                n => n.ExecutionId == execIdStr && n.NodeId == nodeRecord.NodeId,
                token: ct)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            await db.ExecutionNodes
                .Where(n => n.Id == existing.Id)
                .Set(n => n.State, nodeRecord.State.ToString())
                .Set(n => n.CompletedAt, nodeRecord.CompletedAt?.ToString("O"))
                .Set(n => n.Outputs, nodeRecord.Outputs is not null
                    ? JsonSerializer.Serialize(nodeRecord.Outputs, JsonOptions)
                    : null)
                .Set(n => n.Error, nodeRecord.Error)
                .Set(n => n.DurationMs, nodeRecord.Duration == default
                    ? (long?)null
                    : (long)nodeRecord.Duration.TotalMilliseconds)
                .UpdateAsync(token: ct)
                .ConfigureAwait(false);
        }
        else
        {
            var entity = new ExecutionNodeEntity
            {
                ExecutionId = execIdStr,
                NodeId = nodeRecord.NodeId,
                State = nodeRecord.State.ToString(),
                StartedAt = nodeRecord.StartedAt.ToString("O"),
                CompletedAt = nodeRecord.CompletedAt?.ToString("O"),
                Inputs = nodeRecord.Inputs is not null
                    ? JsonSerializer.Serialize(nodeRecord.Inputs, JsonOptions)
                    : null,
                Outputs = nodeRecord.Outputs is not null
                    ? JsonSerializer.Serialize(nodeRecord.Outputs, JsonOptions)
                    : null,
                Error = nodeRecord.Error,
                DurationMs = nodeRecord.Duration == default
                    ? null
                    : (long)nodeRecord.Duration.TotalMilliseconds,
            };

            await db.InsertAsync(entity, token: ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<NodeExecutionRecord>> GetNodeExecutionsAsync(
        Guid executionId,
        CancellationToken ct = default)
    {
        await using var db = _factory.Create();
        var items = await db.ExecutionNodes
            .Where(n => n.ExecutionId == executionId.ToString())
            .OrderBy(n => n.StartedAt)
            .ToListAsync(token: ct)
            .ConfigureAwait(false);

        return items.Select(MapToNodeRecord).ToList();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ExecutionRecord MapToRecord(ExecutionEntity entity)
    {
        return new ExecutionRecord(
            ExecutionId: Guid.Parse(entity.Id),
            WorkflowId: Guid.Parse(entity.WorkflowId),
            State: Enum.Parse<ExecutionState>(entity.State),
            StartedAt: DateTimeOffset.Parse(entity.StartedAt, null, System.Globalization.DateTimeStyles.RoundtripKind),
            CompletedAt: entity.CompletedAt is not null
                ? DateTimeOffset.Parse(entity.CompletedAt, null, System.Globalization.DateTimeStyles.RoundtripKind)
                : null,
            Inputs: entity.Inputs is not null
                ? JsonSerializer.Deserialize<Dictionary<string, object?>>(entity.Inputs, JsonOptions)
                : null,
            Outputs: entity.Outputs is not null
                ? JsonSerializer.Deserialize<Dictionary<string, object?>>(entity.Outputs, JsonOptions)
                : null,
            Error: entity.Error,
            TriggeredBy: entity.TriggeredBy);
    }

    private static NodeExecutionRecord MapToNodeRecord(ExecutionNodeEntity entity)
    {
        var duration = entity.DurationMs.HasValue
            ? TimeSpan.FromMilliseconds(entity.DurationMs.Value)
            : default;

        return new NodeExecutionRecord(
            ExecutionId: Guid.Parse(entity.ExecutionId),
            NodeId: entity.NodeId,
            State: Enum.Parse<NodeExecutionState>(entity.State),
            StartedAt: DateTimeOffset.Parse(entity.StartedAt, null, System.Globalization.DateTimeStyles.RoundtripKind),
            CompletedAt: entity.CompletedAt is not null
                ? DateTimeOffset.Parse(entity.CompletedAt, null, System.Globalization.DateTimeStyles.RoundtripKind)
                : null,
            Inputs: entity.Inputs is not null
                ? JsonSerializer.Deserialize<Dictionary<string, object?>>(entity.Inputs, JsonOptions)
                : null,
            Outputs: entity.Outputs is not null
                ? JsonSerializer.Deserialize<Dictionary<string, object?>>(entity.Outputs, JsonOptions)
                : null,
            Error: entity.Error,
            Duration: duration);
    }
}

