// <copyright file="TransactionMessages.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Engine.Messages;

using System;
using System.Collections.Generic;

/// <summary>
/// 💼 Sent by <see cref="Workflow.Engine.Actors.TransactionExecutorActor"/> to its parent
/// (<c>WorkflowExecutor</c>) when the transaction body completes and commits or rolls back~ ✅
/// </summary>
/// <param name="TransactionNodeId">The node ID of the transaction module that initiated this actor. 🆔.</param>
/// <param name="Outputs">Result outputs stored on the transaction node and used for committed/rolledBack routing~ 📦.</param>
public record TransactionCompleted(
    string TransactionNodeId,
    IReadOnlyDictionary<string, object?> Outputs);

/// <summary>
/// 💼 Sent by <see cref="Workflow.Engine.Actors.TransactionExecutorActor"/> when transaction infrastructure fails
/// (opening or committing the transaction), so the workflow failure path handles it~ ❌
/// </summary>
/// <param name="TransactionNodeId">The node ID of the transaction module. 🆔.</param>
/// <param name="Error">The infrastructure exception to raise into the workflow failure path. ⚠️.</param>
public record TransactionFailed(
    string TransactionNodeId,
    Exception Error);

/// <summary>
/// 💼 Sent by <see cref="Workflow.Engine.Actors.NodeExecutor"/> to <c>WorkflowExecutor</c>
/// BEFORE <see cref="NodeExecutionCompleted"/> when a transaction module returns a
/// <see cref="Workflow.Core.Models.TransactionRequest"/> in its <c>ModuleResult</c>~
/// </summary>
/// <remarks>
/// CopilotNote: WorkflowExecutor stores this in <c>_pendingTransactions[NodeId]</c>.
/// Sending before NodeExecutionCompleted guarantees ordering (same sender → same receiver = FIFO)~ 💖.
/// </remarks>
public sealed class NodeTransactionExecutionRequested
{
    /// <summary>Gets the node ID of the transaction module~ 🆔.</summary>
    public required string NodeId { get; init; }

    /// <summary>Gets the transaction execution specification~ 💼.</summary>
    public required Workflow.Core.Models.TransactionRequest Transaction { get; init; }
}
