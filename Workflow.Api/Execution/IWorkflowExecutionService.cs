// <copyright file="IWorkflowExecutionService.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.Execution;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Workflow.Core.Models;
using Workflow.Engine.Messages;

/// <summary>
/// 🚀 A unified execution status snapshot, buildable from either the live actor status or the
/// persisted execution history (Phase 2.7.2)~ ✨.
/// </summary>
/// <param name="ExecutionId">The execution id.</param>
/// <param name="State">The current state.</param>
/// <param name="Progress">Completion percentage (0-100).</param>
/// <param name="NodeStates">Per-node state (name → state string).</param>
/// <param name="StartTime">When execution started.</param>
/// <param name="EndTime">When execution finished (if terminal).</param>
/// <param name="Error">Error message if failed.</param>
/// <param name="Outputs">Final outputs (from history, when complete).</param>
public record ExecutionStatusResult(
    Guid ExecutionId,
    ExecutionState State,
    int Progress,
    IReadOnlyDictionary<string, string> NodeStates,
    DateTimeOffset StartTime,
    DateTimeOffset? EndTime,
    string? Error,
    IReadOnlyDictionary<string, object?>? Outputs);

/// <summary>
/// 🚀 The outcome of starting a workflow — either created or failed with reasons (Phase 2.7.2)~ ✨.
/// </summary>
/// <param name="Success">Whether the instance was created.</param>
/// <param name="ExecutionId">The new execution id (when successful).</param>
/// <param name="Errors">Failure reasons (when not successful).</param>
public record StartResult(bool Success, Guid ExecutionId, IReadOnlyList<string> Errors);

/// <summary>
/// 🚀 General execution service wrapping the Akka <c>WorkflowSupervisor</c> for start / status /
/// cancel — the API's replacement for the webhook-specific launcher (Phase 2.7.2 / D3)~ ✨.
/// </summary>
public interface IWorkflowExecutionService
{
    /// <summary>Starts a workflow execution for the given definition id~ 🚀.</summary>
    /// <param name="definitionId">The workflow definition id.</param>
    /// <param name="inputs">The initial inputs.</param>
    /// <param name="options">Execution start options (caller id, variable write mode).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The start result.</returns>
    Task<StartResult> StartAsync(
        Guid definitionId,
        IReadOnlyDictionary<string, object?> inputs,
        ExecutionStartOptions options,
        CancellationToken ct = default);

    /// <summary>Gets the current status of an execution (live actor, else persisted history)~ 📊.</summary>
    /// <param name="executionId">The execution id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The status, or <c>null</c> if unknown.</returns>
    Task<ExecutionStatusResult?> GetStatusAsync(Guid executionId, CancellationToken ct = default);

    /// <summary>Requests cancellation of a running execution~ 🛑.</summary>
    /// <param name="executionId">The execution id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> if the execution exists and cancellation was requested.</returns>
    Task<bool> CancelAsync(Guid executionId, CancellationToken ct = default);

    /// <summary>Starts an execution then awaits a terminal state or the timeout~ ⏱️.</summary>
    /// <param name="definitionId">The workflow definition id.</param>
    /// <param name="inputs">The initial inputs.</param>
    /// <param name="options">Execution start options.</param>
    /// <param name="timeout">The wait timeout.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The start result + final status (null when it timed out before terminal) + timedOut flag.</returns>
    Task<(StartResult Start, ExecutionStatusResult? Final, bool TimedOut)> StartAndWaitAsync(
        Guid definitionId,
        IReadOnlyDictionary<string, object?> inputs,
        ExecutionStartOptions options,
        TimeSpan timeout,
        CancellationToken ct = default);
}
