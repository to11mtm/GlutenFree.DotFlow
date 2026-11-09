// <copyright file="RunState.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Execution.State;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>🏃 Phase 3.3.c / 3.5.1 — Per-node run state for the execution overlay (shared by the
/// designer and the monitor)~ ✨.</summary>
public enum NodeRunState
{
    /// <summary>Not started.</summary>
    Pending,

    /// <summary>Currently executing.</summary>
    Running,

    /// <summary>Finished successfully.</summary>
    Completed,

    /// <summary>Failed.</summary>
    Failed,

    /// <summary>Skipped.</summary>
    Skipped,
}

/// <summary>🏃 Phase 3.3.c — A node's run status~ ✨.</summary>
/// <param name="State">The run state.</param>
/// <param name="DurationMs">Duration when completed/failed.</param>
/// <param name="Error">Error message when failed.</param>
public sealed record NodeRunStatus(NodeRunState State, double? DurationMs = null, string? Error = null);

/// <summary>📝 Phase 3.3.c — A run-log line~ ✨.</summary>
/// <param name="Timestamp">When it happened.</param>
/// <param name="Text">The message.</param>
public sealed record RunLogEntry(DateTimeOffset Timestamp, string Text);

/// <summary>
/// 🏃 Phase 3.3.c / 3.5.1 — Framework-free live/historical execution state for the run overlay + the
/// monitor's detail view. Fed by the SignalR hub (3.3.c.1 / 3.5) or a status snapshot (3.3.c.2 / 3.5);
/// views paint from <see cref="CssClassFor"/>. Generalized out of <c>Designer/State</c> so the
/// designer and the execution monitor share one model (3.5.1 D3)~ ✨.
/// </summary>
public sealed class RunState
{
    private readonly Dictionary<string, NodeRunStatus> nodes = new(StringComparer.Ordinal);
    private readonly List<RunLogEntry> log = new();
    private readonly Dictionary<string, StreamStageCount> streamCounts = new(StringComparer.Ordinal);

    /// <summary>Raised on any change~ 🔔.</summary>
    public event Action? Changed;

    /// <summary>Gets or sets the execution id.</summary>
    public Guid ExecutionId { get; set; }

    /// <summary>Gets the overall state (Pending/Running/Completed/Failed/Cancelled).</summary>
    public string Overall { get; private set; } = "Running";

    /// <summary>Gets the completion percentage.</summary>
    public int Percentage { get; private set; }

    /// <summary>Gets the completed node count.</summary>
    public int CompletedNodes { get; private set; }

    /// <summary>Gets the total node count.</summary>
    public int TotalNodes { get; private set; }

    /// <summary>Sets the initial total node count (before progress events arrive)~ 🔢.</summary>
    /// <param name="total">The node count.</param>
    public void SetTotalNodes(int total)
    {
        this.TotalNodes = total;
        this.Raise();
    }

    /// <summary>Gets the currently-executing node id.</summary>
    public string? CurrentNode { get; private set; }

    /// <summary>Gets the terminal error message, if any.</summary>
    public string? Error { get; private set; }

    /// <summary>Gets a value indicating whether the run reached a terminal state.</summary>
    public bool IsTerminal => this.Overall is "Completed" or "Failed" or "Cancelled";

    /// <summary>Gets the per-node statuses.</summary>
    public IReadOnlyDictionary<string, NodeRunStatus> Nodes => this.nodes;

    /// <summary>Gets the run log (in order).</summary>
    public IReadOnlyList<RunLogEntry> Log => this.log;

    /// <summary>📡 Stage 7.6 — items each streaming stage (node id) has emitted, live or final.</summary>
    public IReadOnlyDictionary<string, StreamStageCount> StreamCounts => this.streamCounts;

    /// <summary>
    /// 📡 Stage 7.6 — applies streaming-region item counts. Events for another execution are
    /// ignored; a late live snapshot never overwrites a final count.
    /// </summary>
    /// <param name="executionId">The execution the counts belong to.</param>
    /// <param name="regionIndex">The streaming region.</param>
    /// <param name="itemCounts">Items per stage node id.</param>
    /// <param name="elapsedMs">Time since the region started.</param>
    /// <param name="completed">Whether these are the final counts.</param>
    public void StreamProgress(Guid executionId, int regionIndex, IReadOnlyDictionary<string, long> itemCounts, double elapsedMs, bool completed)
    {
        if (executionId != this.ExecutionId)
        {
            return;
        }

        foreach (var (nodeId, count) in itemCounts)
        {
            if (this.streamCounts.TryGetValue(nodeId, out var existing) && existing.Completed && !completed)
            {
                continue; // a live tick that arrives after the final summary~
            }

            this.streamCounts[nodeId] = new StreamStageCount(regionIndex, count, elapsedMs, completed);
        }

        if (completed)
        {
            this.AddLog($"streaming region {regionIndex} finished: " + string.Join(", ", itemCounts.Select(kv => $"{kv.Key} {kv.Value:N0}")));
            return; // AddLog raised already
        }

        this.Raise();
    }

    /// <summary>📡 Stage 7.6 — the item-count badge for a node on the canvas (null when not streaming).</summary>
    /// <param name="nodeId">The node id.</param>
    /// <returns>e.g. "1,234 items", or null.</returns>
    public string? ItemBadgeFor(string nodeId)
        => this.streamCounts.TryGetValue(nodeId, out var c) ? $"{c.Items:N0} item{(c.Items == 1 ? string.Empty : "s")}" : null;

    /// <summary>The CSS state class for a node (empty when unknown)~ 🎨.</summary>
    /// <param name="nodeId">The node id.</param>
    /// <returns>The CSS class.</returns>
    public string CssClassFor(string nodeId)
        => this.nodes.TryGetValue(nodeId, out var s)
            ? s.State switch
            {
                NodeRunState.Running => "df-node--running",
                NodeRunState.Completed => "df-node--completed",
                NodeRunState.Failed => "df-node--failed",
                NodeRunState.Skipped => "df-node--skipped",
                _ => string.Empty,
            }
            : string.Empty;

    /// <summary>Seeds state from a status snapshot (late-join / history)~ 📸.</summary>
    /// <param name="overall">Overall state string.</param>
    /// <param name="progress">Progress percentage.</param>
    /// <param name="nodeStates">Per-node state strings (node id → state).</param>
    /// <param name="error">Terminal error, if any.</param>
    public void SeedFromSnapshot(string overall, int progress, IReadOnlyDictionary<string, string> nodeStates, string? error)
    {
        this.Overall = overall;
        this.Percentage = progress;
        this.Error = error;
        this.nodes.Clear();
        foreach (var (id, state) in nodeStates)
        {
            this.nodes[id] = new NodeRunStatus(ParseNodeState(state));
        }

        this.Raise();
    }

    /// <summary>Marks a node running~ ⚡.</summary>
    /// <param name="nodeId">The node id.</param>
    public void NodeStarted(string nodeId)
    {
        this.nodes[nodeId] = new NodeRunStatus(NodeRunState.Running);
        this.CurrentNode = nodeId;
        this.AddLog($"node {nodeId} started");
        this.Raise();
    }

    /// <summary>Marks a node completed~ ✅.</summary>
    /// <param name="nodeId">The node id.</param>
    /// <param name="durationMs">Duration.</param>
    public void NodeCompleted(string nodeId, double durationMs)
    {
        this.nodes[nodeId] = new NodeRunStatus(NodeRunState.Completed, durationMs);
        this.AddLog($"node {nodeId} completed ({durationMs:F0}ms)");
        this.Raise();
    }

    /// <summary>Marks a node failed~ ⚠️.</summary>
    /// <param name="nodeId">The node id.</param>
    /// <param name="error">Error message.</param>
    /// <param name="durationMs">Duration.</param>
    public void NodeFailed(string nodeId, string error, double durationMs)
    {
        this.nodes[nodeId] = new NodeRunStatus(NodeRunState.Failed, durationMs, error);
        this.AddLog($"node {nodeId} failed: {error}");
        this.Raise();
    }

    /// <summary>Applies a progress update~ 💫.</summary>
    /// <param name="percentage">Percentage.</param>
    /// <param name="currentNode">Current node.</param>
    /// <param name="completed">Completed count.</param>
    /// <param name="total">Total count.</param>
    public void Progress(int percentage, string? currentNode, int completed, int total)
    {
        this.Percentage = percentage;
        this.CurrentNode = currentNode;
        this.CompletedNodes = completed;
        this.TotalNodes = total;
        this.Raise();
    }

    /// <summary>Marks the execution completed~ 🎊.</summary>
    /// <param name="durationMs">Total duration.</param>
    public void MarkCompleted(double durationMs)
    {
        this.Overall = "Completed";
        this.Percentage = 100;
        this.AddLog($"execution completed ({durationMs:F0}ms)");
        this.Raise();
    }

    /// <summary>Marks the execution failed~ 😿.</summary>
    /// <param name="error">Error message.</param>
    /// <param name="durationMs">Duration.</param>
    public void MarkFailed(string error, double durationMs)
    {
        this.Overall = "Failed";
        this.Error = error;
        this.AddLog($"execution failed: {error}");
        this.Raise();
    }

    /// <summary>Marks the execution cancelled~ 🛑.</summary>
    public void MarkCancelled()
    {
        this.Overall = "Cancelled";
        this.AddLog("execution cancelled");
        this.Raise();
    }

    /// <summary>Appends a log line~ 📝.</summary>
    /// <param name="text">The text.</param>
    public void AddLog(string text)
    {
        this.log.Add(new RunLogEntry(DateTimeOffset.UtcNow, text));
        this.Raise();
    }

    private static NodeRunState ParseNodeState(string state) => state switch
    {
        "Running" => NodeRunState.Running,
        "Completed" => NodeRunState.Completed,
        "Failed" => NodeRunState.Failed,
        "Skipped" => NodeRunState.Skipped,
        _ => NodeRunState.Pending,
    };

    private void Raise() => this.Changed?.Invoke();
}

/// <summary>📡 Stage 7.6 — one streaming stage's item count.</summary>
/// <param name="RegionIndex">The streaming region.</param>
/// <param name="Items">Items emitted so far (or in total, once completed).</param>
/// <param name="ElapsedMs">Time since the region started.</param>
/// <param name="Completed">Whether the region has finished.</param>
public sealed record StreamStageCount(int RegionIndex, long Items, double ElapsedMs, bool Completed);
