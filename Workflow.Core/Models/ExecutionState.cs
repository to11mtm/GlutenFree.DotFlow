// <copyright file="ExecutionState.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Core.Models;

/// <summary>
/// Enum representing the current state of a workflow execution.
/// Tracks the lifecycle from creation to completion~ 🔄.
/// </summary>
public enum ExecutionState
{
    /// <summary>Execution has been created but not yet started.</summary>
    Pending,

    /// <summary>Execution is currently running.</summary>
    Running,

    /// <summary>Execution completed successfully.</summary>
    Completed,

    /// <summary>Execution failed with an error.</summary>
    Failed,

    /// <summary>Execution was cancelled by user request.</summary>
    Cancelled,

    /// <summary>Execution is temporarily paused.</summary>
    Paused,
}

/// <summary>
/// Enum representing the execution state of an individual node.
/// Tracks node lifecycle within a workflow execution~ 🌸.
/// </summary>
public enum NodeExecutionState
{
    /// <summary>Node is waiting to be executed (dependencies not met).</summary>
    Pending,

    /// <summary>Node is currently executing.</summary>
    Running,

    /// <summary>Node completed successfully.</summary>
    Completed,

    /// <summary>Node execution failed.</summary>
    Failed,

    /// <summary>Node was skipped (conditional execution).</summary>
    Skipped,

    /// <summary>Node was cancelled before completion.</summary>
    Cancelled,

    /// <summary>Node is being retried after a failure.</summary>
    Retrying,
}

