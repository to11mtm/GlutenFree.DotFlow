// <copyright file="TryCatchMessages.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Engine.Messages;

using System;
using System.Collections.Generic;

/// <summary>
/// 🛡️ Sent by <see cref="Workflow.Engine.Actors.TryCatchExecutorActor"/> to its parent
/// (<c>WorkflowExecutor</c>) when the try/catch/finally sequence completes without re-throwing~ ✅
/// </summary>
/// <param name="TryCatchNodeId">The node ID of the trycatch module that initiated this actor. 🆔.</param>
/// <param name="Outputs">
/// Result outputs: <c>error</c> (WorkflowError? — null on success), <c>success</c> (bool).
/// Stored in <c>WorkflowExecutor._nodeOutputs[tryCatchNodeId]</c> and forwarded as done-port payload~ 📦.
/// </param>
/// <remarks>
/// CopilotNote: Phase 2.2.4 — WorkflowExecutor handles this by firing the <c>done</c> port
/// successors of the trycatch node, pre-marking try/catch/finally branch nodes as skipped~ 💖.
/// </remarks>
public record TryCatchCompleted(
    string TryCatchNodeId,
    IReadOnlyDictionary<string, object?> Outputs);

/// <summary>
/// 🛡️ Sent by <see cref="Workflow.Engine.Actors.TryCatchExecutorActor"/> to its parent
/// (<c>WorkflowExecutor</c>) when the error propagates (rethrow=true or unhandled failure)~ ❌
/// </summary>
/// <param name="TryCatchNodeId">The node ID of the trycatch module. 🆔.</param>
/// <param name="Error">The exception to be re-raised into the workflow failure path. ⚠️.</param>
/// <remarks>
/// CopilotNote: Phase 2.2.4 — WorkflowExecutor handles this the same as a regular
/// NodeExecutionFailed: route to outer ErrorBoundary or fail the workflow~ 💔.
/// </remarks>
public record TryCatchFailed(
    string TryCatchNodeId,
    Exception Error);

/// <summary>
/// 🛡️ Sent by <see cref="Workflow.Engine.Actors.NodeExecutor"/> to <c>WorkflowExecutor</c>
/// BEFORE <see cref="NodeExecutionCompleted"/> when a trycatch module returns a
/// <see cref="Workflow.Core.Models.TryCatchRequest"/> in its <c>ModuleResult</c>~
/// </summary>
/// <remarks>
/// CopilotNote: Phase 2.2.4 — WorkflowExecutor stores this in <c>_pendingTryCatches[NodeId]</c>.
/// When the subsequent NodeExecutionCompleted arrives, it detects the stored TryCatchRequest
/// and spawns a TryCatchExecutorActor instead of calling ExecuteReadySuccessors directly.
/// Sending before NodeExecutionCompleted guarantees ordering (same sender → same receiver = FIFO)~ 💖.
/// </remarks>
public sealed class NodeTryCatchExecutionRequested
{
    /// <summary>Gets the node ID of the trycatch module~ 🆔.</summary>
    public required string NodeId { get; init; }

    /// <summary>Gets the try/catch execution specification~ 🛡️.</summary>
    public required Workflow.Core.Models.TryCatchRequest TryCatch { get; init; }
}

