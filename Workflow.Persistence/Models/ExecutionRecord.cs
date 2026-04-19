// <copyright file="ExecutionRecord.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Models;

using Workflow.Core.Models;

/// <summary>
/// 📊 A persisted record of a workflow execution~ ✨
/// </summary>
public record ExecutionRecord(
    Guid ExecutionId,
    Guid WorkflowId,
    ExecutionState State,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt = null,
    IReadOnlyDictionary<string, object?>? Inputs = null,
    IReadOnlyDictionary<string, object?>? Outputs = null,
    string? Error = null,
    string? TriggeredBy = null);

