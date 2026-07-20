// <copyright file="ExecutionDtos.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Api.Dtos;

using System;
using System.Collections.Generic;
using System.Text.Json;

/// <summary>⚡ Phase 3.3.a.0 — Body for <c>POST /api/v1/workflows/{id}/execute</c>~ ✨.</summary>
/// <param name="Inputs">Initial inputs (name → value).</param>
/// <param name="VariableWriteMode">Optional write mode (<c>execution</c>/<c>workflow</c>/<c>dual</c>).</param>
public sealed record StartExecutionRequest(
    Dictionary<string, JsonElement>? Inputs,
    string? VariableWriteMode = null);

/// <summary>⚡ Phase 3.3.a.0 — Response from starting an execution (mirrors <c>ExecutionStartedDto</c>)~ ✨.</summary>
/// <param name="ExecutionId">The new execution id.</param>
/// <param name="Status">A hint status.</param>
public sealed record ExecutionStartedDto(Guid ExecutionId, string Status);

/// <summary>⚡ Phase 3.3.a.0 — Full execution status (mirrors <c>ExecutionStatusDto</c>)~ ✨.</summary>
/// <param name="ExecutionId">The execution id.</param>
/// <param name="State">State string.</param>
/// <param name="Progress">Completion percentage.</param>
/// <param name="NodeStates">Per-node state (node id → state string).</param>
/// <param name="StartTime">Start time.</param>
/// <param name="EndTime">End time (if terminal).</param>
/// <param name="Error">Error (if failed).</param>
/// <param name="Outputs">Outputs (if complete).</param>
public sealed record ExecutionStatusDto(
    Guid ExecutionId,
    string State,
    int Progress,
    Dictionary<string, string> NodeStates,
    DateTimeOffset StartTime,
    DateTimeOffset? EndTime,
    string? Error,
    Dictionary<string, JsonElement>? Outputs);

/// <summary>⚡ Phase 3.3.a.0 — Execution list row (mirrors <c>ExecutionDto</c>)~ ✨.</summary>
/// <param name="ExecutionId">The execution id.</param>
/// <param name="WorkflowId">The workflow id.</param>
/// <param name="State">State string.</param>
/// <param name="StartedAt">Started timestamp.</param>
/// <param name="CompletedAt">Completed timestamp.</param>
/// <param name="TriggeredBy">Caller id / trigger source.</param>
public sealed record ExecutionDto(
    Guid ExecutionId,
    Guid WorkflowId,
    string State,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string? TriggeredBy);
