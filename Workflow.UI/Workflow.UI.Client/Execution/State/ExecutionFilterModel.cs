// <copyright file="ExecutionFilterModel.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Execution.State;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 🔍 Phase 3.5.4 — Framework-free filter + sort model for the monitor. Status/date map to the server
/// query (the existing `ExecutionFilter`); duration + sort are applied client-side over the loaded
/// rows (D9). No Blazor/JS types (D2)~ ✨.
/// </summary>
public sealed class ExecutionFilterModel
{
    /// <summary>Gets or sets the workflow filter (required for a REST history query).</summary>
    public Guid? WorkflowId { get; set; }

    /// <summary>Gets or sets the state filter (null / "All" = every state).</summary>
    public string? Status { get; set; }

    /// <summary>Gets or sets the started-after filter.</summary>
    public DateTimeOffset? From { get; set; }

    /// <summary>Gets or sets the started-before filter.</summary>
    public DateTimeOffset? To { get; set; }

    /// <summary>Gets or sets the minimum duration (ms) client-side filter.</summary>
    public double? MinDurationMs { get; set; }

    /// <summary>Gets or sets the sort column (Started/Duration/State/Workflow).</summary>
    public string SortColumn { get; set; } = "Started";

    /// <summary>Gets or sets a value indicating whether the sort is descending.</summary>
    public bool Descending { get; set; } = true;

    /// <summary>Gets the server-side status filter (null when "all")~ 🌐.</summary>
    public string? ServerStatus
        => string.IsNullOrWhiteSpace(this.Status) || string.Equals(this.Status, "All", StringComparison.OrdinalIgnoreCase)
            ? null
            : this.Status;

    /// <summary>Whether a row passes the client-side duration filter~ ⏱.</summary>
    /// <param name="row">The row.</param>
    /// <returns><c>true</c> when it passes.</returns>
    public bool MatchesDuration(MonitorRow row)
        => this.MinDurationMs is not { } min || (row.DurationMs ?? 0) >= min;

    /// <summary>Applies the client-side duration filter + sort to a set of rows~ 🔃.</summary>
    /// <param name="rows">The rows.</param>
    /// <returns>The filtered, sorted rows.</returns>
    public IReadOnlyList<MonitorRow> Apply(IEnumerable<MonitorRow> rows)
    {
        var filtered = rows.Where(this.MatchesDuration);
        Func<MonitorRow, object> key = this.SortColumn switch
        {
            "Duration" => r => r.DurationMs ?? 0d,
            "State" => r => r.State,
            "Workflow" => r => r.WorkflowId?.ToString() ?? string.Empty,
            _ => r => r.StartedAt,
        };
        return (this.Descending ? filtered.OrderByDescending(key) : filtered.OrderBy(key)).ToList();
    }

    /// <summary>Toggles the sort direction (or switches column, defaulting to descending)~ 🔃.</summary>
    /// <param name="column">The clicked column.</param>
    public void ToggleSort(string column)
    {
        if (string.Equals(this.SortColumn, column, StringComparison.Ordinal))
        {
            this.Descending = !this.Descending;
        }
        else
        {
            this.SortColumn = column;
            this.Descending = true;
        }
    }
}

/// <summary>
/// 📝 Phase 3.5.4 — Classifies an event-derived run-log line into a level (D7). Framework-free~ ✨.
/// </summary>
public static class RunLogClassifier
{
    /// <summary>Infers a log level from a run-log line's text~ 🏷️.</summary>
    /// <param name="text">The log text.</param>
    /// <returns>Debug/Info/Warning/Error.</returns>
    public static string LevelOf(string text)
    {
        if (text.Contains("failed", StringComparison.OrdinalIgnoreCase) || text.Contains("error", StringComparison.OrdinalIgnoreCase))
        {
            return "Error";
        }

        if (text.Contains("cancel", StringComparison.OrdinalIgnoreCase))
        {
            return "Warning";
        }

        if (text.Contains("started", StringComparison.OrdinalIgnoreCase))
        {
            return "Debug";
        }

        return "Info";
    }
}
