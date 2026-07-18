// <copyright file="ExecutionContracts.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.Contracts;

using System;
using System.Collections.Generic;
using Workflow.Api.Execution;

/// <summary>
/// ⚡ Request body for starting an execution (Phase 2.7.2)~ ✨.
/// </summary>
/// <param name="Inputs">Initial inputs (name → value).</param>
/// <param name="VariableWriteMode">Optional: <c>execution</c> (default) / <c>workflow</c> / <c>dual</c>.</param>
public record StartExecutionRequest(
    Dictionary<string, object?>? Inputs,
    string? VariableWriteMode);

/// <summary>⚡ Response after starting an execution~ ✨.</summary>
/// <param name="ExecutionId">The new execution id.</param>
/// <param name="Status">A hint status (<c>accepted</c>/<c>running</c>).</param>
public record ExecutionStartedDto(Guid ExecutionId, string Status);

/// <summary>⚡ Full execution status projection~ ✨.</summary>
/// <param name="ExecutionId">The execution id.</param>
/// <param name="State">The state (string).</param>
/// <param name="Progress">Completion percentage.</param>
/// <param name="NodeStates">Per-node state.</param>
/// <param name="StartTime">Start time.</param>
/// <param name="EndTime">End time (if terminal).</param>
/// <param name="Error">Error (if failed).</param>
/// <param name="Outputs">Outputs (if complete).</param>
public record ExecutionStatusDto(
    Guid ExecutionId,
    string State,
    int Progress,
    IReadOnlyDictionary<string, string> NodeStates,
    DateTimeOffset StartTime,
    DateTimeOffset? EndTime,
    string? Error,
    IReadOnlyDictionary<string, object?>? Outputs)
{
    /// <summary>Projects an <see cref="ExecutionStatusResult"/> into the DTO~ 📊.</summary>
    /// <param name="r">The status result.</param>
    /// <returns>The DTO.</returns>
    public static ExecutionStatusDto From(ExecutionStatusResult r)
        => new(r.ExecutionId, r.State.ToString(), r.Progress, r.NodeStates, r.StartTime, r.EndTime, r.Error, r.Outputs);
}

/// <summary>⚡ Execution list row (Phase 2.7.2)~ ✨.</summary>
/// <param name="ExecutionId">The execution id.</param>
/// <param name="WorkflowId">The workflow id.</param>
/// <param name="State">The state (string).</param>
/// <param name="StartedAt">Started timestamp.</param>
/// <param name="CompletedAt">Completed timestamp.</param>
/// <param name="TriggeredBy">Caller id / trigger source.</param>
public record ExecutionDto(
    Guid ExecutionId,
    Guid WorkflowId,
    string State,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string? TriggeredBy);
