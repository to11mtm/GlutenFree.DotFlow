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

/// <summary>
/// 📊 Phase 3.5.0 — A persisted execution record projection (for the monitor's historical detail).
/// Unlike <see cref="ExecutionStatusDto"/> (live, from the actor) this reads the history repository,
/// so it renders even after the run leaves memory~ ✨.
/// </summary>
/// <param name="ExecutionId">The execution id.</param>
/// <param name="WorkflowId">The owning workflow id.</param>
/// <param name="State">The state (string).</param>
/// <param name="StartedAt">Started timestamp.</param>
/// <param name="CompletedAt">Completed timestamp (if terminal).</param>
/// <param name="DurationMs">Total duration in milliseconds (if terminal).</param>
/// <param name="Inputs">The execution inputs.</param>
/// <param name="Outputs">The execution outputs (if complete).</param>
/// <param name="Error">The error (if failed).</param>
/// <param name="TriggeredBy">Caller id / trigger source.</param>
public record ExecutionDetailDto(
    Guid ExecutionId,
    Guid WorkflowId,
    string State,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    double? DurationMs,
    IReadOnlyDictionary<string, object?>? Inputs,
    IReadOnlyDictionary<string, object?>? Outputs,
    string? Error,
    string? TriggeredBy)
{
    /// <summary>Projects an <see cref="Workflow.Persistence.Models.ExecutionRecord"/> into the DTO~ 📊.</summary>
    /// <param name="r">The execution record.</param>
    /// <returns>The DTO.</returns>
    public static ExecutionDetailDto From(Workflow.Persistence.Models.ExecutionRecord r)
        => new(
            r.ExecutionId,
            r.WorkflowId,
            r.State.ToString(),
            r.StartedAt,
            r.CompletedAt,
            r.CompletedAt is { } done ? (done - r.StartedAt).TotalMilliseconds : null,
            r.Inputs,
            r.Outputs,
            r.Error,
            r.TriggeredBy);
}

/// <summary>
/// 🌸 Phase 3.5.0 — A persisted node-execution record projection (for the monitor's node inspector
/// + replay). Carries the inputs/outputs/timing/error the engine already stores per node~ ✨.
/// </summary>
/// <param name="NodeId">The node id.</param>
/// <param name="State">The node state (string).</param>
/// <param name="StartedAt">Started timestamp.</param>
/// <param name="CompletedAt">Completed timestamp (if terminal).</param>
/// <param name="DurationMs">Node duration in milliseconds.</param>
/// <param name="Inputs">The node inputs.</param>
/// <param name="Outputs">The node outputs.</param>
/// <param name="Error">The error (if failed).</param>
/// <param name="LoopId">The loop scope id, if the node ran inside a loop.</param>
/// <param name="LoopIteration">The 1-based iteration within the loop scope, if any.</param>
public record NodeExecutionRecordDto(
    string NodeId,
    string State,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    double DurationMs,
    IReadOnlyDictionary<string, object?>? Inputs,
    IReadOnlyDictionary<string, object?>? Outputs,
    string? Error,
    string? LoopId,
    int? LoopIteration)
{
    /// <summary>Projects a <see cref="Workflow.Persistence.Models.NodeExecutionRecord"/> into the DTO~ 🌸.</summary>
    /// <param name="r">The node record.</param>
    /// <returns>The DTO.</returns>
    public static NodeExecutionRecordDto From(Workflow.Persistence.Models.NodeExecutionRecord r)
        => new(
            r.NodeId,
            r.State.ToString(),
            r.StartedAt,
            r.CompletedAt,
            r.Duration.TotalMilliseconds,
            r.Inputs,
            r.Outputs,
            r.Error,
            r.LoopId,
            r.LoopIteration);
}
