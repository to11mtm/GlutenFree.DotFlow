// <copyright file="MonitorState.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Execution.State;

using System;
using System.Collections.Generic;
using System.Linq;
using Workflow.UI.Client.Api.Dtos;

/// <summary>🖥️ Phase 3.5.2 — A live execution row in the monitor dashboard~ ✨.</summary>
public sealed class MonitorRow
{
    /// <summary>Gets or sets the execution id.</summary>
    public Guid ExecutionId { get; set; }

    /// <summary>Gets or sets the owning workflow id.</summary>
    public Guid? WorkflowId { get; set; }

    /// <summary>Gets or sets the state (Running/Completed/Failed/Cancelled/Pending).</summary>
    public string State { get; set; } = "Running";

    /// <summary>Gets or sets the start time.</summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>Gets or sets the completion time (if terminal).</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Gets or sets the completion percentage.</summary>
    public int Progress { get; set; }

    /// <summary>Gets or sets the currently-executing node id.</summary>
    public string? CurrentNode { get; set; }

    /// <summary>Gets or sets the total duration in milliseconds (terminal).</summary>
    public double? DurationMs { get; set; }

    /// <summary>Gets or sets the error message (if failed).</summary>
    public string? Error { get; set; }

    /// <summary>Gets a value indicating whether this row is still in-flight.</summary>
    public bool IsRunning => this.State is "Running" or "Pending";
}

/// <summary>
/// 🖥️ Phase 3.5.2 — Framework-free state for the monitor dashboard: a keyed set of execution rows,
/// seeded from REST and **merged** with hub firehose events (`ExecutionStarted/Progress/Completed/
/// Failed`). No Blazor/JS types (D2) so the React port reuses it~ ✨.
/// </summary>
public sealed class MonitorState
{
    private readonly Dictionary<Guid, MonitorRow> rows = new();

    /// <summary>Raised whenever the row set changes~ 🔔.</summary>
    public event Action? Changed;

    /// <summary>Gets the running rows (newest first)~ ▶️.</summary>
    public IReadOnlyList<MonitorRow> Running
        => this.rows.Values.Where(r => r.IsRunning).OrderByDescending(r => r.StartedAt).ToList();

    /// <summary>Gets the terminal (recent) rows, sorted by the given key/direction~ 🕘.</summary>
    /// <param name="sort">The sort column (Started/Duration/State/Workflow).</param>
    /// <param name="descending">Whether to sort descending.</param>
    /// <returns>The recent rows.</returns>
    public IReadOnlyList<MonitorRow> Recent(string sort = "Started", bool descending = true)
    {
        IEnumerable<MonitorRow> terminal = this.rows.Values.Where(r => !r.IsRunning);
        Func<MonitorRow, object> key = sort switch
        {
            "Duration" => r => r.DurationMs ?? 0d,
            "State" => r => r.State,
            "Workflow" => r => r.WorkflowId?.ToString() ?? string.Empty,
            _ => r => r.StartedAt,
        };
        terminal = descending ? terminal.OrderByDescending(key) : terminal.OrderBy(key);
        return terminal.ToList();
    }

    /// <summary>Gets every row (for tests / totals)~ 📋.</summary>
    public IReadOnlyCollection<MonitorRow> Rows => this.rows.Values.ToList();

    /// <summary>Tries to get a row by id~ 🔍.</summary>
    /// <param name="executionId">The execution id.</param>
    /// <returns>The row, or null.</returns>
    public MonitorRow? Find(Guid executionId) => this.rows.TryGetValue(executionId, out var r) ? r : null;

    /// <summary>Seeds/updates rows from a REST list page (historical snapshot)~ 📥.</summary>
    /// <param name="items">The execution rows.</param>
    public void SeedFromList(IEnumerable<ExecutionDto> items)
    {
        foreach (var e in items)
        {
            var row = this.Upsert(e.ExecutionId);
            row.WorkflowId = e.WorkflowId;
            row.State = e.State;
            row.StartedAt = e.StartedAt;
            row.CompletedAt = e.CompletedAt;
            row.DurationMs = e.CompletedAt is { } done ? (done - e.StartedAt).TotalMilliseconds : row.DurationMs;
            if (!row.IsRunning)
            {
                row.Progress = 100;
            }
        }

        this.Raise();
    }

    /// <summary>Merges an <c>ExecutionStarted</c> event~ 🚀.</summary>
    /// <param name="e">The event.</param>
    public void ApplyStarted(ExecutionStartedEvent e)
    {
        var row = this.Upsert(e.ExecutionId);
        row.WorkflowId = e.WorkflowId ?? row.WorkflowId;
        row.State = "Running";
        row.StartedAt = e.Timestamp;
        row.CompletedAt = null;
        row.Error = null;
        this.Raise();
    }

    /// <summary>Merges an <c>ExecutionProgress</c> event~ 💫.</summary>
    /// <param name="e">The event.</param>
    public void ApplyProgress(ExecutionProgressEvent e)
    {
        var row = this.Upsert(e.ExecutionId);
        row.Progress = e.Percentage;
        row.CurrentNode = e.CurrentNode;
        if (!row.IsRunning)
        {
            row.State = "Running";
        }

        this.Raise();
    }

    /// <summary>Merges an <c>ExecutionCompleted</c> event~ 🎊.</summary>
    /// <param name="e">The event.</param>
    public void ApplyCompleted(ExecutionCompletedEvent e)
    {
        var row = this.Upsert(e.ExecutionId);
        row.WorkflowId = e.WorkflowId ?? row.WorkflowId;
        row.State = "Completed";
        row.Progress = 100;
        row.CompletedAt = e.Timestamp;
        row.DurationMs = e.DurationMs;
        row.CurrentNode = null;
        this.Raise();
    }

    /// <summary>Merges an <c>ExecutionFailed</c> event~ 😿.</summary>
    /// <param name="e">The event.</param>
    public void ApplyFailed(ExecutionFailedEvent e)
    {
        var row = this.Upsert(e.ExecutionId);
        row.WorkflowId = e.WorkflowId ?? row.WorkflowId;
        row.State = "Failed";
        row.CompletedAt = e.Timestamp;
        row.DurationMs = e.DurationMs;
        row.Error = e.Error;
        row.CurrentNode = null;
        this.Raise();
    }

    /// <summary>Marks a row cancelled (after a cancel request succeeds)~ 🛑.</summary>
    /// <param name="executionId">The execution id.</param>
    public void MarkCancelled(Guid executionId)
    {
        if (this.rows.TryGetValue(executionId, out var row))
        {
            row.State = "Cancelled";
            row.CompletedAt = DateTimeOffset.UtcNow;
            this.Raise();
        }
    }

    private MonitorRow Upsert(Guid executionId)
    {
        if (!this.rows.TryGetValue(executionId, out var row))
        {
            row = new MonitorRow { ExecutionId = executionId, StartedAt = DateTimeOffset.UtcNow };
            this.rows[executionId] = row;
        }

        return row;
    }

    private void Raise() => this.Changed?.Invoke();
}
