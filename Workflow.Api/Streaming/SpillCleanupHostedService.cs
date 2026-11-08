// <copyright file="SpillCleanupHostedService.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.Streaming;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Workflow.Core.Models;
using Workflow.Modules.Streaming;
using Workflow.Persistence.Abstractions;
using ExecutionRecord = Workflow.Persistence.Models.ExecutionRecord;

/// <summary>
/// 🧹 Phase 5.1.6 — reclaims spill artefacts whose execution no longer needs them.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: The happy path never needs this — <see cref="ISpillSession.DisposeAsync"/> deletes
/// eagerly. The sweeper exists for the unhappy paths: a host killed mid-run, a process crash, a
/// dispose that threw. It is a safety net, not the mechanism~ 🕸️.
/// </para>
/// <para>
/// Retention is keyed to <b>execution completion, not artefact age</b> (plan §12). That's what
/// makes it cluster-safe: any host can evaluate the rule because it depends on shared execution
/// state rather than "did I write this?". Age only breaks ties for executions whose record is
/// already gone~ 🔑.
/// </para>
/// <para>
/// The second, longer <c>AbandonedThreshold</c> closes the crash hole (plan §13, Q-E1): without it
/// a host that dies mid-execution leaves a record stuck non-terminal forever and its spill would
/// never be reclaimed~ 🪦.
/// </para>
/// </remarks>
public sealed class SpillCleanupHostedService : BackgroundService
{
    private readonly ISpillStore store;
    private readonly IExecutionHistoryRepository? history;
    private readonly SpillOptions options;
    private readonly ILogger<SpillCleanupHostedService> logger;
    private readonly TimeProvider timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpillCleanupHostedService"/> class~ 🧹.
    /// </summary>
    /// <param name="store">The configured spill store.</param>
    /// <param name="options">Spill options.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="history">Execution history, when the host has persistence configured.</param>
    /// <param name="timeProvider">Clock, injectable for tests.</param>
    public SpillCleanupHostedService(
        ISpillStore store,
        IOptions<SpillOptions> options,
        ILogger<SpillCleanupHostedService> logger,
        IExecutionHistoryRepository? history = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.options = options.Value;
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.history = history;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Runs one sweep and returns how many executions were reclaimed~ 🧹.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The number of executions whose spill was purged.</returns>
    /// <remarks>Exposed so tests can drive a sweep deterministically instead of racing a timer~ 🧪.</remarks>
    public async Task<int> SweepAsync(CancellationToken ct = default)
    {
        var now = this.timeProvider.GetUtcNow();
        var reclaimed = 0;

        var executions = new List<Guid>();
        await foreach (var id in this.store.ListExecutionsAsync(ct).ConfigureAwait(false))
        {
            executions.Add(id);
        }

        foreach (var executionId in executions)
        {
            ct.ThrowIfCancellationRequested();

            var decision = await this.DecideAsync(executionId, now, ct).ConfigureAwait(false);
            if (decision == SweepDecision.Keep)
            {
                continue;
            }

            var removed = await this.store.PurgeExecutionAsync(executionId, ct).ConfigureAwait(false);
            reclaimed++;

            if (decision == SweepDecision.DeleteAbandoned)
            {
                // 📢 A reclaimed leak is also a signal that a host died — say so loudly.
                this.logger.LogWarning(
                    "🪦 Reclaimed {Count} spill artefacts for execution {ExecutionId}, which has been non-terminal for longer than the abandoned threshold ({Threshold}). Its host most likely died mid-run~",
                    removed,
                    executionId,
                    this.options.AbandonedThreshold);
            }
            else
            {
                this.logger.LogDebug(
                    "🧹 Reclaimed {Count} spill artefacts for completed execution {ExecutionId}~",
                    removed,
                    executionId);
            }
        }

        return reclaimed;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (this.history is null)
        {
            // §9.6 — say clearly what retention means on this host~ 📢
            this.logger.LogWarning(
                "🧹 Spill store '{Store}' is configured but there is no execution history store, so spill can't be "
                + "tied to its run's completion. Spill nothing has written to for longer than the abandoned threshold "
                + "({Threshold}) will be reclaimed by age — a run that goes that long without spilling again could lose "
                + "its spill. Configure persistence to key retention to execution completion instead~",
                this.store.Name,
                this.options.AbandonedThreshold);
        }

        // CopilotNote: sweep at startup as well as on the timer — a host that just came back from a
        // crash is exactly the host most likely to be holding abandoned scratch~ 🧹
        await this.SafeSweepAsync(stoppingToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(this.options.SweepInterval, this.timeProvider);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await this.SafeSweepAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down~ 👋
        }
    }

    private async Task SafeSweepAsync(CancellationToken ct)
    {
        try
        {
            await this.SweepAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A failed sweep must never take the host down — the worst case is scratch lingering
            // until the next tick~ 🕸️
            this.logger.LogError(ex, "🧹 Spill sweep failed; will retry on the next interval~");
        }
    }

    private async Task<SweepDecision> DecideAsync(Guid executionId, DateTimeOffset now, CancellationToken ct)
    {
        if (this.history is null)
        {
            // §9.6 (B2): with no execution history there is nothing to key retention to, so fall back
            // to the spill's own age — reclaim what nothing has written to for longer than the
            // abandoned threshold. Unknown age (a store that can't tell) is kept~ ⏱️
            DateTimeOffset? lastWritten;
            try
            {
                lastWritten = await this.store.GetLastWrittenAsync(executionId, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                this.logger.LogDebug(ex, "🧹 Could not read the spill age of execution {ExecutionId}~", executionId);
                return SweepDecision.Keep;
            }

            return lastWritten is { } written && now - written > this.options.AbandonedThreshold
                ? SweepDecision.DeleteAbandoned
                : SweepDecision.Keep;
        }

        ExecutionRecord? record;
        try
        {
            record = await this.history.GetExecutionAsync(executionId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            this.logger.LogDebug(ex, "🧹 Could not read execution {ExecutionId} while sweeping spill~", executionId);
            return SweepDecision.Keep;
        }

        if (record is null)
        {
            // Record purged — treat as terminal and fall back to age.
            return SweepDecision.DeleteCompleted;
        }

        if (IsTerminal(record.State))
        {
            var terminalAt = record.CompletedAt ?? record.StartedAt;
            return now - terminalAt > this.options.RetentionAfterCompletion
                ? SweepDecision.DeleteCompleted
                : SweepDecision.Keep;
        }

        // Non-terminal. Normally we keep spill forever — a running job needs it. But a record that
        // hasn't reached a terminal state within the abandoned threshold almost certainly belongs to
        // a host that died, so reclaim it rather than leak (plan §13, Q-E1)~ 🪦
        //
        // CopilotNote: ExecutionRecord carries no "last updated" timestamp, so staleness is measured
        // from StartedAt. That's conservative in the right direction — it can only ever *delay*
        // reclaiming a genuinely long-running job, never hasten it.
        return now - record.StartedAt > this.options.AbandonedThreshold
            ? SweepDecision.DeleteAbandoned
            : SweepDecision.Keep;
    }

    private static bool IsTerminal(ExecutionState state)
        => state is ExecutionState.Completed or ExecutionState.Failed or ExecutionState.Cancelled;

    private enum SweepDecision
    {
        Keep,
        DeleteCompleted,
        DeleteAbandoned,
    }
}
