// <copyright file="RealTimeEvents.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.Contracts.RealTime;

using System;
using System.Collections.Generic;

/*
 * 📡 Phase 3.2 — Client-facing real-time event contracts. Plain, serializable records (no
 * LanguageExt leakage); SignalR's JSON protocol camelCases them on the wire~ ✨
 *
 * CopilotNote: These are SUMMARY payloads (Q6) — ids, timings, status, error, progress.
 * Large outputs/variables are intentionally omitted; clients fetch them via
 * GET /api/v1/executions/{id} when needed.
 */

/// <summary>🚀 An execution began running.</summary>
/// <param name="ExecutionId">The execution id.</param>
/// <param name="WorkflowId">The owning workflow id (null when it can't be resolved).</param>
/// <param name="Timestamp">When the transition occurred.</param>
public sealed record ExecutionStartedEvent(Guid ExecutionId, Guid? WorkflowId, DateTimeOffset Timestamp);

/// <summary>🎊 An execution completed successfully.</summary>
/// <param name="ExecutionId">The execution id.</param>
/// <param name="WorkflowId">The owning workflow id (null when unresolved).</param>
/// <param name="DurationMs">Total execution time in milliseconds.</param>
/// <param name="Timestamp">When completion occurred.</param>
public sealed record ExecutionCompletedEvent(Guid ExecutionId, Guid? WorkflowId, double DurationMs, DateTimeOffset Timestamp);

/// <summary>😿 An execution failed.</summary>
/// <param name="ExecutionId">The execution id.</param>
/// <param name="WorkflowId">The owning workflow id (null when unresolved).</param>
/// <param name="Error">The failure message.</param>
/// <param name="DurationMs">Time before failure in milliseconds.</param>
/// <param name="Timestamp">When failure occurred.</param>
public sealed record ExecutionFailedEvent(Guid ExecutionId, Guid? WorkflowId, string Error, double DurationMs, DateTimeOffset Timestamp);

/// <summary>⚡ A node began executing.</summary>
/// <param name="ExecutionId">The parent execution id.</param>
/// <param name="NodeId">The node id.</param>
/// <param name="Timestamp">When the node started.</param>
public sealed record NodeStartedEvent(Guid ExecutionId, string NodeId, DateTimeOffset Timestamp);

/// <summary>✅ A node completed.</summary>
/// <param name="ExecutionId">The parent execution id.</param>
/// <param name="NodeId">The node id.</param>
/// <param name="DurationMs">Node execution time in milliseconds.</param>
/// <param name="Timestamp">When the node completed.</param>
public sealed record NodeCompletedEvent(Guid ExecutionId, string NodeId, double DurationMs, DateTimeOffset Timestamp);

/// <summary>⚠️ A node failed.</summary>
/// <param name="ExecutionId">The parent execution id.</param>
/// <param name="NodeId">The node id.</param>
/// <param name="Error">The failure message.</param>
/// <param name="DurationMs">Time before failure in milliseconds.</param>
/// <param name="Timestamp">When the node failed.</param>
public sealed record NodeFailedEvent(Guid ExecutionId, string NodeId, string Error, double DurationMs, DateTimeOffset Timestamp);

/// <summary>💫 A progress update for an execution.</summary>
/// <param name="ExecutionId">The execution id.</param>
/// <param name="Percentage">Completion percentage (0-100).</param>
/// <param name="CurrentNode">The node currently executing (null when idle).</param>
/// <param name="CompletedNodes">Number of nodes completed.</param>
/// <param name="TotalNodes">Total number of nodes.</param>
/// <param name="Timestamp">When the update occurred.</param>
public sealed record ExecutionProgressEvent(Guid ExecutionId, int Percentage, string? CurrentNode, int CompletedNodes, int TotalNodes, DateTimeOffset Timestamp);

/// <summary>
/// 📸 A point-in-time status snapshot pushed to a client immediately after it subscribes to an
/// execution, so late subscribers aren't blank until the next live event~ ✨.
/// </summary>
/// <param name="ExecutionId">The execution id.</param>
/// <param name="State">The current state.</param>
/// <param name="Progress">Completion percentage (0-100).</param>
/// <param name="NodeStates">Per-node state (name → state string).</param>
/// <param name="EndTime">When the execution finished (if terminal).</param>
/// <param name="Error">The error message, if failed.</param>
public sealed record ExecutionSnapshotEvent(
    Guid ExecutionId,
    string State,
    int Progress,
    IReadOnlyDictionary<string, string> NodeStates,
    DateTimeOffset? EndTime,
    string? Error);
