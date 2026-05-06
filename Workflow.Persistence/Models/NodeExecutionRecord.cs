// <copyright file="NodeExecutionRecord.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Models;

using Workflow.Core.Models;

/// <summary>
/// 🌸 A persisted record of a single node's execution within a workflow run~ ✨
/// </summary>
/// <remarks>
/// CopilotNote: Phase 2.2.0b — <see cref="LoopId"/>, <see cref="LoopIteration"/>, and
/// <see cref="Metadata"/> are optional fields stamped by WorkflowExecutor when a loop scope
/// is active. They allow history queries to correlate records across loop iterations~ 💖.
/// </remarks>
public record NodeExecutionRecord(
    Guid ExecutionId,
    string NodeId,
    NodeExecutionState State,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt = null,
    IReadOnlyDictionary<string, object?>? Inputs = null,
    IReadOnlyDictionary<string, object?>? Outputs = null,
    string? Error = null,
    TimeSpan Duration = default,
    /// <summary>Gets the loop scope ID this node ran inside, if any~ 🔁.</summary>
    string? LoopId = null,
    /// <summary>Gets the 1-based iteration number within the loop scope, if any~ 🔢.</summary>
    int? LoopIteration = null,
    /// <summary>Gets arbitrary metadata key/value pairs stamped by the engine or modules~ 🗂️.</summary>
    IReadOnlyDictionary<string, object?>? Metadata = null);

