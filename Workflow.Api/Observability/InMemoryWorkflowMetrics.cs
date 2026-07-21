// <copyright file="InMemoryWorkflowMetrics.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.Observability;

using System.Threading;

/// <summary>
/// 📈 Phase 2.7.5 — Thread-safe in-process implementation of <see cref="IWorkflowMetrics"/> backed
/// by interlocked counters. Resets on restart — durable metrics arrive with the Prometheus
/// exporter (2.7.P2)~ ✨💖.
/// </summary>
public sealed class InMemoryWorkflowMetrics : IWorkflowMetrics
{
    private long started;
    private long completed;
    private long failed;
    private long cancelled;
    private long active;

    /// <inheritdoc/>
    public void RecordStarted()
    {
        Interlocked.Increment(ref this.started);
        Interlocked.Increment(ref this.active);
    }

    /// <inheritdoc/>
    public void RecordCompleted()
    {
        Interlocked.Increment(ref this.completed);
        this.DecrementActive();
    }

    /// <inheritdoc/>
    public void RecordFailed()
    {
        Interlocked.Increment(ref this.failed);
        this.DecrementActive();
    }

    /// <inheritdoc/>
    public void RecordCancelled()
    {
        Interlocked.Increment(ref this.cancelled);
        this.DecrementActive();
    }

    /// <inheritdoc/>
    public WorkflowMetricsSnapshot Snapshot()
        => new(
            Interlocked.Read(ref this.started),
            Interlocked.Read(ref this.completed),
            Interlocked.Read(ref this.failed),
            Interlocked.Read(ref this.cancelled),
            Interlocked.Read(ref this.active));

    private void DecrementActive()
    {
        // Clamp at zero so a terminal transition without a matching start never goes negative~
        long current;
        do
        {
            current = Interlocked.Read(ref this.active);
            if (current <= 0)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref this.active, current - 1, current) != current);
    }
}
