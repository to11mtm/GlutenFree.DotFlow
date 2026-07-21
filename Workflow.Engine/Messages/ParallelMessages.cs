// <copyright file="ParallelMessages.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Engine.Messages;

using System;
using System.Collections.Generic;

/// <summary>
/// Sent by <see cref="Workflow.Engine.Actors.ParallelExecutionCoordinator"/> to its parent
/// (<c>WorkflowExecutor</c>) when all parallel branches complete successfully~ 🌐✅
/// </summary>
/// <param name="ParallelNodeId">The node ID of the parallel module that initiated this coordinator. 🆔.</param>
/// <param name="Outputs">
/// Aggregated branch outputs: <c>results</c> (list, branch-indexed), <c>count</c> (int).
/// Stored in <c>WorkflowExecutor._nodeOutputs[parallelNodeId]</c> and forwarded as done-port payload~ 📦.
/// </param>
/// <remarks>
/// CopilotNote: Phase 2.2.3a — WorkflowExecutor handles this by firing the <c>done</c> port
/// successors of the parallel node (activePorts = [parallel.DonePort])~ 💖.
/// </remarks>
public record ParallelCompleted(
    string ParallelNodeId,
    IReadOnlyDictionary<string, object?> Outputs);

/// <summary>
/// Sent by <see cref="Workflow.Engine.Actors.ParallelExecutionCoordinator"/> to its parent
/// (<c>WorkflowExecutor</c>) when one or more branches fail (in fail-fast mode, the first
/// failure cancels siblings and reports here)~ 🌐❌
/// </summary>
/// <param name="ParallelNodeId">The node ID of the originating parallel module. 🆔.</param>
/// <param name="Error">The exception that caused the failure. ⚠️.</param>
/// <param name="FailedNodeId">The node ID inside a branch that triggered the failure, if known. 🔍.</param>
public record ParallelFailed(
    string ParallelNodeId,
    Exception Error,
    string? FailedNodeId = null);

/// <summary>
/// Sent by <see cref="Workflow.Engine.Actors.WorkflowExecutor"/> to
/// <see cref="Workflow.Engine.Actors.ParallelExecutionCoordinator"/> to cooperatively cancel
/// all in-flight branches~ 🛑
/// </summary>
/// <param name="ParallelNodeId">The parallel node ID (for logging). 🆔.</param>
/// <param name="Reason">Human-readable cancellation reason. 💬.</param>
public record CooperativeCancelParallel(string ParallelNodeId, string? Reason = null);

