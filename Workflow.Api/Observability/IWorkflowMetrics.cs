// <copyright file="IWorkflowMetrics.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.Observability;

using System.Collections.Generic;

/// <summary>
/// 📈 Phase 2.7.5 — A tiny counter seam the API uses to track execution activity. A Prometheus
/// text exporter can be layered over this later (2.7.P2 per Q2)~ ✨.
/// </summary>
public interface IWorkflowMetrics
{
    /// <summary>Records that an execution was started (increments started + active)~ 🚀.</summary>
    void RecordStarted();

    /// <summary>Records that an execution completed successfully (decrements active)~ ✅.</summary>
    void RecordCompleted();

    /// <summary>Records that an execution failed (decrements active)~ ❌.</summary>
    void RecordFailed();

    /// <summary>Records that an execution was cancelled (decrements active)~ 🛑.</summary>
    void RecordCancelled();

    /// <summary>Gets a snapshot of the current counter values~ 📊.</summary>
    /// <returns>An immutable snapshot.</returns>
    WorkflowMetricsSnapshot Snapshot();
}

/// <summary>
/// 📊 Phase 2.7.5 — An immutable snapshot of the execution counters~ ✨.
/// </summary>
/// <param name="Started">Total executions started.</param>
/// <param name="Completed">Total executions completed successfully.</param>
/// <param name="Failed">Total executions failed.</param>
/// <param name="Cancelled">Total executions cancelled.</param>
/// <param name="Active">Currently-active (non-terminal) executions.</param>
public sealed record WorkflowMetricsSnapshot(
    long Started,
    long Completed,
    long Failed,
    long Cancelled,
    long Active)
{
    /// <summary>Renders the snapshot as a flat dictionary (for JSON/metric dumps)~ 🗂️.</summary>
    /// <returns>A dictionary of metric name → value.</returns>
    public IReadOnlyDictionary<string, long> ToDictionary()
        => new Dictionary<string, long>
        {
            ["executions_started_total"] = this.Started,
            ["executions_completed_total"] = this.Completed,
            ["executions_failed_total"] = this.Failed,
            ["executions_cancelled_total"] = this.Cancelled,
            ["executions_active"] = this.Active,
        };
}
