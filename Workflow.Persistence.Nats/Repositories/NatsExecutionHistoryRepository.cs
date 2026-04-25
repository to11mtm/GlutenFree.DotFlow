// <copyright file="NatsExecutionHistoryRepository.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Nats.Repositories;

using NATS.Client.KeyValueStore;
using Workflow.Core.Models;
using Workflow.Persistence.Abstractions;
using Workflow.Persistence.Models;
using Workflow.Persistence.Nats.Internal;

/// <summary>
/// 📊 NATS KV-backed implementation of <see cref="IExecutionHistoryRepository"/>~ ✨💖
/// </summary>
/// <remarks>
/// CopilotNote: Two KV buckets are used:
/// <list type="bullet">
///   <item><c>WF_EXECUTIONS</c> — one entry per execution, key = <c>{executionId}</c>.</item>
///   <item><c>WF_EXEC_NODES</c> — one entry per node record, key = <c>{executionId}:{nodeId}</c>.</item>
/// </list>
/// Filtering/pagination for <c>GetExecutionsForWorkflowAsync</c> is done in-memory since
/// NATS KV has no server-side query support~ 🧠
/// </remarks>
public sealed class NatsExecutionHistoryRepository : IExecutionHistoryRepository
{
    /// <summary>NATS KV bucket name for execution records~ 📊.</summary>
    public const string ExecutionsBucket = "WF_EXECUTIONS";

    /// <summary>NATS KV bucket name for node execution records~ 🌸.</summary>
    public const string NodesBucket = "WF_EXEC_NODES";

    private readonly INatsKVStore _execStore;
    private readonly INatsKVStore _nodeStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="NatsExecutionHistoryRepository"/> class~ 🔌.
    /// </summary>
    /// <param name="execStore">The KV store for execution records.</param>
    /// <param name="nodeStore">The KV store for node execution records.</param>
    public NatsExecutionHistoryRepository(INatsKVStore execStore, INatsKVStore nodeStore)
    {
        _execStore = execStore ?? throw new ArgumentNullException(nameof(execStore));
        _nodeStore = nodeStore ?? throw new ArgumentNullException(nameof(nodeStore));
    }

    /// <inheritdoc/>
    public async Task<Guid> CreateExecutionAsync(ExecutionRecord record, CancellationToken ct = default)
    {
        var id = record.ExecutionId == Guid.Empty ? Guid.NewGuid() : record.ExecutionId;
        var actual = record with { ExecutionId = id };

        await _execStore.PutAsync(id.ToString(), NatsJsonHelper.Serialize(actual), cancellationToken: ct)
            .ConfigureAwait(false);

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
        var existing = await GetExecutionDocAsync(executionId.ToString(), ct).ConfigureAwait(false);

        if (existing is null)
        {
            throw new InvalidOperationException($"Execution {executionId} not found~ 😿");
        }

        var updated = existing with
        {
            State = state,
            CompletedAt = endTime ?? existing.CompletedAt,
            Error = error ?? existing.Error,
        };

        await _execStore.PutAsync(executionId.ToString(), NatsJsonHelper.Serialize(updated), cancellationToken: ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<ExecutionRecord?> GetExecutionAsync(Guid executionId, CancellationToken ct = default)
        => await GetExecutionDocAsync(executionId.ToString(), ct).ConfigureAwait(false);

    /// <inheritdoc/>
    public async Task<PagedResult<ExecutionRecord>> GetExecutionsForWorkflowAsync(
        Guid workflowId,
        ExecutionFilter filter,
        Pagination pagination,
        CancellationToken ct = default)
    {
        // CopilotNote: Scan all execution keys and filter in-memory by workflowId~ 🧠
        var all = new List<ExecutionRecord>();

        await foreach (var key in _execStore.GetKeysAsync(cancellationToken: ct).ConfigureAwait(false))
        {
            var rec = await GetExecutionDocAsync(key, ct).ConfigureAwait(false);
            if (rec is not null && rec.WorkflowId == workflowId)
            {
                all.Add(rec);
            }
        }

        // Apply filter
        var filtered = all.AsEnumerable();

        if (filter.States is { Length: > 0 })
        {
            filtered = filtered.Where(e => filter.States.Contains(e.State));
        }

        if (filter.StartedAfter.HasValue)
        {
            filtered = filtered.Where(e => e.StartedAt >= filter.StartedAfter.Value);
        }

        if (filter.StartedBefore.HasValue)
        {
            filtered = filtered.Where(e => e.StartedAt <= filter.StartedBefore.Value);
        }

        var list = filtered.OrderByDescending(e => e.StartedAt).ToList();
        var totalCount = list.Count;
        var paged = list.Skip(pagination.Skip).Take(pagination.PageSize).ToList();

        return new PagedResult<ExecutionRecord>(paged, totalCount, pagination.Page, pagination.PageSize);
    }

    /// <inheritdoc/>
    public async Task RecordNodeExecutionAsync(NodeExecutionRecord nodeRecord, CancellationToken ct = default)
    {
        // CopilotNote: Key = {executionId}:{nodeId} — this is an upsert (Put always wins)~ 🔄
        var key = $"{nodeRecord.ExecutionId}-{nodeRecord.NodeId}";

        await _nodeStore.PutAsync(key, NatsJsonHelper.Serialize(nodeRecord), cancellationToken: ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<NodeExecutionRecord>> GetNodeExecutionsAsync(
        Guid executionId,
        CancellationToken ct = default)
    {
        var prefix = $"{executionId}-";
        var result = new List<NodeExecutionRecord>();

        await foreach (var key in _nodeStore.GetKeysAsync(cancellationToken: ct).ConfigureAwait(false))
        {
            if (!key.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                var entry = await _nodeStore.GetEntryAsync<string>(key, cancellationToken: ct)
                    .ConfigureAwait(false);

                if (entry.Operation == NatsKVOperation.Put && entry.Value is not null)
                {
                    var rec = NatsJsonHelper.Deserialize<NodeExecutionRecord>(entry.Value);
                    if (rec is not null)
                    {
                        result.Add(rec);
                    }
                }
            }
            catch (NatsKVKeyNotFoundException)
            {
                // Key was deleted between listing and reading — skip it~ 🙈
            }
            catch (NatsKVKeyDeletedException)
            {
                // Key was deleted between listing and reading — skip it~ 🙈
            }
        }

        return result;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<ExecutionRecord?> GetExecutionDocAsync(string key, CancellationToken ct)
    {
        try
        {
            var entry = await _execStore.GetEntryAsync<string>(key, cancellationToken: ct)
                .ConfigureAwait(false);

            if (entry.Operation != NatsKVOperation.Put || entry.Value is null)
            {
                return null;
            }

            return NatsJsonHelper.Deserialize<ExecutionRecord>(entry.Value);
        }
        catch (NatsKVKeyNotFoundException)
        {
            return null;
        }
    }
}

