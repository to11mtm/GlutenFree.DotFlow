// ────────────────────────────────────────────────────────────────────────────────
// Copyright © GlutenFree 2025. Made with love by Ami-Chan! UwU 💖
// Licensed under the MIT License.
// ────────────────────────────────────────────────────────────────────────────────

using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Workflow.Api.Streaming;
using Workflow.Core.Models;
using Workflow.Modules.Streaming;
using Workflow.Persistence.Abstractions;
using Workflow.Persistence.Models;

namespace Workflow.Tests.Streaming;

/// <summary>
/// Phase 5.1.6 — the spill sweeper's retention rules~ 🧹
/// </summary>
/// <remarks>
/// CopilotNote: Retention keys off <b>execution completion, not artefact age</b>, which is what
/// makes it cluster-safe — any host can evaluate the rule because it depends on shared execution
/// state rather than "did I write this?". The abandoned threshold is the second rule, and it exists
/// purely to stop a crashed host leaking scratch forever~ 🪦
/// </remarks>
public class SpillCleanupTests
{
    private static SpillCleanupHostedService Service(
        ISpillStore store,
        IExecutionHistoryRepository? history,
        TimeProvider clock,
        SpillOptions? options = null)
        => new(
            store,
            Options.Create(options ?? new SpillOptions
            {
                RetentionAfterCompletion = TimeSpan.FromHours(1),
                AbandonedThreshold = TimeSpan.FromDays(2),
            }),
            NullLogger<SpillCleanupHostedService>.Instance,
            history,
            clock);

    [Fact]
    public async Task Sweep_KeepsSpill_ForARunningExecution()
    {
        // The primary rule: a job that's still going needs its scratch. Deleting it would fail the job.
        var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        var executionId = Guid.NewGuid();

        var store = new FakeSpillStore(executionId);
        var history = new FakeHistory
        {
            [executionId] = new ExecutionRecord(executionId, Guid.NewGuid(), ExecutionState.Running, now.AddMinutes(-5)),
        };

        var reclaimed = await Service(store, history, clock).SweepAsync();

        reclaimed.Should().Be(0);
        store.Purged.Should().BeEmpty();
    }

    [Fact]
    public async Task Sweep_KeepsSpill_ForARecentlyCompletedExecution()
    {
        // Retention gives a completed job a grace window — a sweep that fired the instant an
        // execution finished could race the stage that's still tearing down~
        var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        var executionId = Guid.NewGuid();

        var store = new FakeSpillStore(executionId);
        var history = new FakeHistory
        {
            [executionId] = new ExecutionRecord(
                executionId, Guid.NewGuid(), ExecutionState.Completed, now.AddHours(-2), now.AddMinutes(-10)),
        };

        var reclaimed = await Service(store, history, clock).SweepAsync();

        reclaimed.Should().Be(0);
    }

    [Theory]
    [InlineData(ExecutionState.Completed)]
    [InlineData(ExecutionState.Failed)]
    [InlineData(ExecutionState.Cancelled)]
    public async Task Sweep_ReclaimsSpill_ForEveryTerminalState(ExecutionState state)
    {
        // "whether successful or not" — a failed job's scratch is just as reclaimable as a
        // successful one's, and forgetting Cancelled would leak on every user-cancelled run~
        var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        var executionId = Guid.NewGuid();

        var store = new FakeSpillStore(executionId);
        var history = new FakeHistory
        {
            [executionId] = new ExecutionRecord(
                executionId, Guid.NewGuid(), state, now.AddHours(-5), now.AddHours(-3)),
        };

        var reclaimed = await Service(store, history, clock).SweepAsync();

        reclaimed.Should().Be(1);
        store.Purged.Should().Contain(executionId);
    }

    [Fact]
    public async Task Sweep_ReclaimsSpill_ForAnAbandonedExecution()
    {
        // 🪦 Q-E1. A host that dies mid-run leaves its record stuck non-terminal forever. Without a
        // second, longer threshold that scratch would never be reclaimed — a guaranteed leak keyed
        // to exactly the failure mode most likely to happen in production.
        var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        var executionId = Guid.NewGuid();

        var store = new FakeSpillStore(executionId);
        var history = new FakeHistory
        {
            [executionId] = new ExecutionRecord(
                executionId, Guid.NewGuid(), ExecutionState.Running, now.AddDays(-10)),
        };

        var reclaimed = await Service(store, history, clock).SweepAsync();

        reclaimed.Should().Be(1);
        store.Purged.Should().Contain(executionId);
    }

    [Fact]
    public async Task Sweep_ReclaimsSpill_WhenTheExecutionRecordIsGone()
    {
        // A purged history row means nothing will ever mark this terminal. Keeping forever would
        // leak; treating it as terminal is the only rule that terminates~
        var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        var executionId = Guid.NewGuid();

        var store = new FakeSpillStore(executionId);

        var reclaimed = await Service(store, new FakeHistory(), clock).SweepAsync();

        reclaimed.Should().Be(1);
        store.Purged.Should().Contain(executionId);
    }

    [Fact]
    public async Task Sweep_SurvivesAFailingHistoryLookup()
    {
        // A sweep that threw would take down the hosted service and stop *all* future reclaim. The
        // worst acceptable outcome is scratch lingering until the next tick~
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var store = new FakeSpillStore(Guid.NewGuid());

        var reclaimed = await Service(store, new ThrowingHistory(), clock).SweepAsync();

        reclaimed.Should().Be(0);
        store.Purged.Should().BeEmpty();
    }

    #region Test doubles

    /// <summary>A clock pinned to a fixed instant, so retention windows are exact~ 🕰️.</summary>
    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset now;

        public FakeTimeProvider(DateTimeOffset now) => this.now = now;

        public override DateTimeOffset GetUtcNow() => this.now;
    }

    private sealed class FakeSpillStore : ISpillStore
    {
        private readonly List<Guid> executions;

        public FakeSpillStore(params Guid[] executions) => this.executions = [.. executions];

        public List<Guid> Purged { get; } = [];

        public string Name => "fake";

        public Task<ISpillSession> BeginAsync(SpillSessionKey key, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<int> PurgeExecutionAsync(Guid executionId, CancellationToken ct = default)
        {
            this.Purged.Add(executionId);
            this.executions.Remove(executionId);
            return Task.FromResult(1);
        }

        public async IAsyncEnumerable<Guid> ListExecutionsAsync([EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var id in this.executions.ToList())
            {
                yield return id;
            }

            await Task.CompletedTask;
        }
    }

    private class FakeHistory : Dictionary<Guid, ExecutionRecord>, IExecutionHistoryRepository
    {
        public Task<Guid> CreateExecutionAsync(ExecutionRecord record, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task UpdateExecutionStatusAsync(Guid executionId, ExecutionState state, DateTimeOffset? endTime = null, string? error = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public virtual Task<ExecutionRecord?> GetExecutionAsync(Guid executionId, CancellationToken ct = default)
            => Task.FromResult(this.TryGetValue(executionId, out var record) ? record : null);

        public Task<PagedResult<ExecutionRecord>> GetExecutionsForWorkflowAsync(Guid workflowId, ExecutionFilter filter, Pagination pagination, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task RecordNodeExecutionAsync(NodeExecutionRecord nodeRecord, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<NodeExecutionRecord>> GetNodeExecutionsAsync(Guid executionId, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class ThrowingHistory : FakeHistory
    {
        public override Task<ExecutionRecord?> GetExecutionAsync(Guid executionId, CancellationToken ct = default)
            => throw new InvalidOperationException("the database is having a day~");
    }

    #endregion
}
