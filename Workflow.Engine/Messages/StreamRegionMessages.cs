// <copyright file="StreamRegionMessages.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Engine.Messages;

using System;
using System.Collections.Generic;

/// <summary>
/// Tells a <see cref="Workflow.Engine.Actors.StreamRegionExecutor"/> to start its region~ 🌊▶️
/// </summary>
public record ExecuteStreamRegion;

/// <summary>
/// Sent by <see cref="Workflow.Engine.Actors.StreamRegionExecutor"/> to its parent
/// (<c>WorkflowExecutor</c>) when the whole region has drained successfully~ 🌊✅
/// </summary>
/// <param name="RegionIndex">The region's stable index. 🔢.</param>
/// <param name="NodeIds">Every node the region ran, in pipeline order. 🗺️.</param>
/// <param name="TerminalNodeId">
/// The last node in the chain — its outputs are what downstream (non-streaming) nodes receive. 🪣.
/// </param>
/// <param name="Outputs">The terminal stage's outputs (empty when the region had no terminal stage). 📦.</param>
/// <param name="ItemCounts">Items emitted per stage, for history and the monitor. 📊.</param>
/// <param name="Duration">Wall-clock duration of the region. ⏱️.</param>
/// <remarks>
/// CopilotNote: Phase 5.1.3 — WorkflowExecutor marks every member node Completed, stores the
/// terminal outputs under <c>TerminalNodeId</c>, and resumes ordinary port dispatch from there~ 💖.
/// </remarks>
public record StreamRegionCompleted(
    int RegionIndex,
    IReadOnlyList<string> NodeIds,
    string TerminalNodeId,
    IReadOnlyDictionary<string, object?> Outputs,
    IReadOnlyDictionary<string, long> ItemCounts,
    TimeSpan Duration);

/// <summary>
/// Sent by <see cref="Workflow.Engine.Actors.StreamRegionExecutor"/> to its parent when the region
/// fails — a stage threw, a module wasn't streaming, or a terminal guard tripped~ 🌊❌
/// </summary>
/// <param name="RegionIndex">The region's stable index. 🔢.</param>
/// <param name="NodeIds">Every node in the region. 🗺️.</param>
/// <param name="FailedNodeId">The node blamed for the failure (the source when unknown). 🔍.</param>
/// <param name="Error">The exception. ⚠️.</param>
/// <remarks>
/// CopilotNote: Handled exactly like a regular <see cref="NodeExecutionFailed"/>, so per-node error
/// handling, retries and enclosing try/catch boundaries all behave as usual~ 🛡️.
/// </remarks>
public record StreamRegionFailed(
    int RegionIndex,
    IReadOnlyList<string> NodeIds,
    string FailedNodeId,
    Exception Error);

/// <summary>
/// 📡 Published to the Akka <c>EventStream</c> when a streaming region finishes, carrying the
/// per-stage item counts that are the streaming equivalent of a node's outputs~ 🌊📊
/// </summary>
/// <param name="ExecutionId">The execution the region ran in. 🔗.</param>
/// <param name="RegionIndex">The region's stable index. 🔢.</param>
/// <param name="ItemCounts">Items emitted per stage node id. 📊.</param>
/// <param name="Duration">Wall-clock duration of the region. ⏱️.</param>
/// <param name="Timestamp">When the region finished. ⏰.</param>
/// <remarks>
/// CopilotNote: The 3.2 <c>ExecutionEventBridge</c> already forwards EventStream messages to
/// SignalR, so publishing here is all the monitor needs to learn about stage throughput — no hub
/// or engine-to-API coupling required~ 📡.
/// </remarks>
public record StreamRegionProgress(
    Guid ExecutionId,
    int RegionIndex,
    IReadOnlyDictionary<string, long> ItemCounts,
    TimeSpan Duration,
    DateTimeOffset Timestamp);
