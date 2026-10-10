// <copyright file="SpillCleanupFindingTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Findings.Streaming;

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Workflow.Api.Streaming;
using Workflow.Modules.Streaming;
using Xunit;

/// <summary>
/// Finding 03 §9.6 — spill is kept forever when no execution history is registered.
/// </summary>
/// <remarks>
/// <c>SpillCleanupHostedService.DecideAsync</c> carries a comment saying that with no history it
/// should "fall back to the abandoned threshold" because "keeping forever would be a guaranteed
/// leak", but the code returns <c>Keep</c>. This test encodes that stated intent: an execution
/// whose spill is older than the abandoned threshold, with no history to consult, should be
/// reclaimed. It fails today (nothing is reclaimed). In-memory fakes only.
/// If the team decides "keep when history is unknown" is actually correct, delete this test.
/// </remarks>
[Trait("Category", FindingDefs.Category)]
public sealed class SpillCleanupFindingTests
{
    [Fact]
    public async Task Sweep_WithNoHistory_ShouldReclaimAbandonedSpill()
    {
        var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new FixedClock(now);
        var executionId = Guid.NewGuid();
        var store = new RecordingSpillStore(executionId);

        var service = new SpillCleanupHostedService(
            store,
            Options.Create(new SpillOptions
            {
                RetentionAfterCompletion = TimeSpan.FromHours(1),
                AbandonedThreshold = TimeSpan.FromDays(2),
            }),
            NullLogger<SpillCleanupHostedService>.Instance,
            history: null,
            timeProvider: clock);

        var reclaimed = await service.SweepAsync();

        reclaimed.Should().BeGreaterThan(
            0,
            because: "with no history to key retention to, abandoned spill should still be reclaimed, per the code's own comment (03 §9.6)");
        store.Purged.Should().Contain(executionId);
    }

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset now;

        public FixedClock(DateTimeOffset now) => this.now = now;

        public override DateTimeOffset GetUtcNow() => this.now;
    }

    private sealed class RecordingSpillStore : ISpillStore
    {
        private readonly List<Guid> executions;

        public RecordingSpillStore(params Guid[] executions) => this.executions = new List<Guid>(executions);

        public List<Guid> Purged { get; } = new();

        public string Name => "recording";

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
            foreach (var id in this.executions.ToArray())
            {
                yield return id;
            }

            await Task.CompletedTask;
        }
    }
}
