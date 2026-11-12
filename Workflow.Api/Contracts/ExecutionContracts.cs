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
    /// <summary>🧯 Stage 7.8 — gets why the node was skipped, when it was (from the record's <c>skipReason</c> metadata).</summary>
    public string? SkipReason { get; init; }

    /// <summary>
    /// Gets the constructs a body node ran inside, outermost first (each loop iteration, parallel
    /// branch, try/catch branch…); null for a top-level node. <see cref="LoopId"/> /
    /// <see cref="LoopIteration"/> only name the innermost loop.
    /// </summary>
    public IReadOnlyList<ScopeStepDto>? Scope { get; init; }

    /// <summary>Gets <see cref="Scope"/> in short form, e.g. <c>outer#2/inner#1</c> or <c>par[1]/tc:catch</c>; null at top level.</summary>
    public string? ScopePath { get; init; }

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
            r.LoopIteration)
        {
            SkipReason = r.Metadata is { } metadata && metadata.TryGetValue("skipReason", out var reason)
                ? reason switch
                {
                    string text => text,
                    null => null,
                    var other => other.ToString(),
                }
                : null,
            Scope = ScopeOf(r),
            ScopePath = ScopeOf(r) is { } steps ? string.Join('/', steps.Select(s => s.ToStep().ToString())) : null,
        };

    private static readonly System.Text.Json.JsonSerializerOptions StoredJson = new(System.Text.Json.JsonSerializerDefaults.Web);

    /// <summary>
    /// The record's <c>scopePath</c> metadata: <see cref="Workflow.Engine.Models.ScopeStep"/>s when
    /// written in memory, or a JSON array once read back from a store.
    /// </summary>
    private static IReadOnlyList<ScopeStepDto>? ScopeOf(Workflow.Persistence.Models.NodeExecutionRecord r)
    {
        if (r.Metadata is null || !r.Metadata.TryGetValue("scopePath", out var raw) || raw is null)
        {
            return null;
        }

        IReadOnlyList<Workflow.Engine.Models.ScopeStep>? steps = raw switch
        {
            IEnumerable<Workflow.Engine.Models.ScopeStep> list => list.ToList(),
            System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.Array } json
                => System.Text.Json.JsonSerializer.Deserialize<List<Workflow.Engine.Models.ScopeStep>>(json, StoredJson),
            _ => null,
        };

        return steps is { Count: > 0 } ? steps.Select(ScopeStepDto.From).ToList() : null;
    }
}

/// <summary>One construct a body node ran inside (see <see cref="NodeExecutionRecordDto.Scope"/>).</summary>
/// <param name="NodeId">The construct node.</param>
/// <param name="Kind"><c>loop</c>, <c>parallel</c>, <c>trycatch</c>, <c>transaction</c> or <c>errorHandler</c>.</param>
/// <param name="Iteration">Loop: the 1-based iteration.</param>
/// <param name="BranchIndex">Parallel: the 0-based branch.</param>
/// <param name="Branch">Try/catch: <c>try</c>, <c>catch</c> or <c>finally</c>.</param>
public sealed record ScopeStepDto(string NodeId, string Kind, int? Iteration, int? BranchIndex, string? Branch)
{
    /// <summary>Projects an engine scope step.</summary>
    /// <param name="s">The step.</param>
    /// <returns>The DTO.</returns>
    public static ScopeStepDto From(Workflow.Engine.Models.ScopeStep s) => new(s.NodeId, s.Kind, s.Iteration, s.BranchIndex, s.Branch);

    /// <summary>Back to the engine step (for its short form).</summary>
    /// <returns>The step.</returns>
    public Workflow.Engine.Models.ScopeStep ToStep() => new(this.NodeId, this.Kind, this.Iteration, this.BranchIndex, this.Branch);
}
