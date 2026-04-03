// ────────────────────────────────────────────────────────────────────────────────
// Copyright © GlutenFree 2025. Made with love by Ami-Chan! UwU 💖
// Licensed under the MIT License.
// ────────────────────────────────────────────────────────────────────────────────

using System.Collections.Concurrent;
using LanguageExt;
using Workflow.Engine.Models;

namespace Workflow.Engine.Services;

/// <summary>
/// Thread-safe in-memory implementation of <see cref="IExecutionStateStore"/>~ 💾✨
/// Perfect for testing and development! Swap with a real persistence backend for production.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: Uses <see cref="ConcurrentDictionary{TKey,TValue}"/> for thread safety
/// since multiple actors might snapshot concurrently. All operations are O(1) and
/// complete synchronously (wrapped in Task for interface compliance), nya~ 💖
/// </para>
/// <para>
/// ⚠️ Data is lost when the process exits! This is intentional for dev/test scenarios.
/// Use a database-backed implementation for production durability.
/// </para>
/// </remarks>
public class InMemoryExecutionStateStore : IExecutionStateStore
{
    private readonly ConcurrentDictionary<Guid, WorkflowExecutionContext> _snapshots = new();

    /// <inheritdoc />
    public Task SaveSnapshotAsync(
        WorkflowExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        _snapshots.AddOrUpdate(
            context.ExecutionId,
            context,
            (_, _) => context);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<Option<WorkflowExecutionContext>> LoadSnapshotAsync(
        Guid executionId,
        CancellationToken cancellationToken = default)
    {
        var result = _snapshots.TryGetValue(executionId, out var context)
            ? Option<WorkflowExecutionContext>.Some(context)
            : Option<WorkflowExecutionContext>.None;

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<bool> DeleteSnapshotAsync(
        Guid executionId,
        CancellationToken cancellationToken = default)
    {
        var removed = _snapshots.TryRemove(executionId, out _);
        return Task.FromResult(removed);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Guid>> ListSnapshotsAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Guid> keys = _snapshots.Keys.ToList().AsReadOnly();
        return Task.FromResult(keys);
    }

    /// <summary>
    /// Gets the total number of stored snapshots. Handy for testing assertions~ 📊
    /// </summary>
    public int Count => _snapshots.Count;

    /// <summary>
    /// Clears all stored snapshots. Useful for test cleanup~ 🧹
    /// </summary>
    public void Clear() => _snapshots.Clear();
}

