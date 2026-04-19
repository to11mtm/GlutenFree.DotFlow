// <copyright file="NodeExecutionRecord.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Models;

using Workflow.Core.Models;

/// <summary>
/// 🌸 A persisted record of a single node's execution within a workflow run~ ✨
/// </summary>
public record NodeExecutionRecord(
    Guid ExecutionId,
    string NodeId,
    NodeExecutionState State,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt = null,
    IReadOnlyDictionary<string, object?>? Inputs = null,
    IReadOnlyDictionary<string, object?>? Outputs = null,
    string? Error = null,
    TimeSpan Duration = default);

