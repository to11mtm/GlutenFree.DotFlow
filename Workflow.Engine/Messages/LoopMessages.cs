// <copyright file="LoopMessages.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Engine.Messages;

using System;
using System.Collections.Generic;

/// <summary>
/// Sent by <see cref="Workflow.Engine.Actors.LoopExecutorActor"/> to its parent
/// (<c>WorkflowExecutor</c>) when all loop iterations complete successfully~ 🔁✅
/// </summary>
/// <param name="LoopNodeId">The node ID of the loop module that initiated this actor. 🆔.</param>
/// <param name="Outputs">
/// Aggregated loop outputs: <c>results</c> (list), <c>count</c> (int), <c>errors</c> (list).
/// Stored in <c>WorkflowExecutor._nodeOutputs[loopNodeId]</c> and forwarded as done-port payload~ 📦.
/// </param>
/// <remarks>
/// CopilotNote: Phase 2.2.2 — WorkflowExecutor handles this by firing the <c>done</c> port
/// successors of the loop node (activePorts = [loop.DonePort])~ 💖.
/// </remarks>
public record LoopCompleted(
    string LoopNodeId,
    IReadOnlyDictionary<string, object?> Outputs);

/// <summary>
/// Sent by <see cref="Workflow.Engine.Actors.LoopExecutorActor"/> to its parent
/// (<c>WorkflowExecutor</c>) when the loop fails (node error without continueOnError,
/// or maxIterations exceeded)~ 🔁❌
/// </summary>
/// <param name="LoopNodeId">The node ID of the originating loop module. 🆔.</param>
/// <param name="Error">The exception that caused the failure. ⚠️.</param>
/// <param name="FailedNodeId">The node ID that triggered the failure, if known. 🔍.</param>
/// <remarks>
/// CopilotNote: Phase 2.2.2 — WorkflowExecutor handles this the same as a regular
/// NodeExecutionFailed: route to ErrorBoundary or fail the workflow~ 💔.
/// </remarks>
public record LoopFailed(
    string LoopNodeId,
    Exception Error,
    string? FailedNodeId = null);

/// <summary>
/// Sent by <see cref="Workflow.Engine.Actors.WorkflowExecutor"/> to
/// <see cref="Workflow.Engine.Actors.LoopExecutorActor"/> to cooperatively cancel the loop~ 🛑
/// </summary>
/// <param name="LoopNodeId">The loop node ID (for logging). 🆔.</param>
/// <param name="Reason">Human-readable cancellation reason. 💬.</param>
public record CooperativeCancelLoop(string LoopNodeId, string? Reason = null);

