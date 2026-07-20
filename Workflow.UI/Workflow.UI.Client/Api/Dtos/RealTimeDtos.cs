// <copyright file="RealTimeDtos.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Api.Dtos;

using System;
using System.Collections.Generic;

/// <summary>
/// 📡 Phase 3.3.a.0 — Client mirrors of the Phase 3.2 hub event payloads
/// (<c>Workflow.Api/Contracts/RealTime/RealTimeEvents.cs</c>). Field names/types must match so
/// SignalR JSON deserialization binds correctly~ ✨.
/// </summary>
/// <param name="ExecutionId">The execution id.</param>
/// <param name="WorkflowId">The owning workflow id (nullable).</param>
/// <param name="Timestamp">When it occurred.</param>
public sealed record ExecutionStartedEvent(Guid ExecutionId, Guid? WorkflowId, DateTimeOffset Timestamp);

/// <summary>🎊 Execution completed.</summary>
public sealed record ExecutionCompletedEvent(Guid ExecutionId, Guid? WorkflowId, double DurationMs, DateTimeOffset Timestamp);

/// <summary>😿 Execution failed.</summary>
public sealed record ExecutionFailedEvent(Guid ExecutionId, Guid? WorkflowId, string Error, double DurationMs, DateTimeOffset Timestamp);

/// <summary>⚡ Node started.</summary>
public sealed record NodeStartedEvent(Guid ExecutionId, string NodeId, DateTimeOffset Timestamp);

/// <summary>✅ Node completed.</summary>
public sealed record NodeCompletedEvent(Guid ExecutionId, string NodeId, double DurationMs, DateTimeOffset Timestamp);

/// <summary>⚠️ Node failed.</summary>
public sealed record NodeFailedEvent(Guid ExecutionId, string NodeId, string Error, double DurationMs, DateTimeOffset Timestamp);

/// <summary>💫 Execution progress.</summary>
public sealed record ExecutionProgressEvent(Guid ExecutionId, int Percentage, string? CurrentNode, int CompletedNodes, int TotalNodes, DateTimeOffset Timestamp);

/// <summary>📸 Snapshot pushed after subscribing to an execution.</summary>
public sealed record ExecutionSnapshotEvent(
    Guid ExecutionId,
    string State,
    int Progress,
    Dictionary<string, string> NodeStates,
    DateTimeOffset? EndTime,
    string? Error);
