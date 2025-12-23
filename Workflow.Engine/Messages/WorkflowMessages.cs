// <copyright file="WorkflowMessages.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Engine.Messages;

using System;
using System.Collections.Generic;
using Workflow.Core.Models;

/// <summary>
/// Base interface for all workflow-related messages in the actor system.
/// This enables type-safe message handling and pattern matching~ 💖
/// </summary>
/// <remarks>
/// CopilotNote: All workflow messages should implement this interface for consistent handling.
/// </remarks>
public interface IWorkflowMessage
{
}

/// <summary>
/// Message to create a new workflow instance.
/// This is sent to the WorkflowSupervisor to initiate a new workflow execution~ ✨
/// </summary>
/// <param name="WorkflowId">Unique identifier for the workflow definition.</param>
/// <param name="Definition">The workflow definition containing nodes and connections.</param>
/// <param name="Inputs">Initial input values for the workflow execution.</param>
public record CreateWorkflowInstance(
    Guid WorkflowId,
    WorkflowDefinition Definition,
    Dictionary<string, object?> Inputs) : IWorkflowMessage;

/// <summary>
/// Response message containing the execution ID for a newly created workflow instance.
/// Sent back to the caller after successful workflow creation~ 🎉
/// </summary>
/// <param name="ExecutionId">Unique identifier for this workflow execution.</param>
/// <param name="WorkflowId">The workflow definition ID that was instantiated.</param>
public record WorkflowInstanceCreated(
    Guid ExecutionId,
    Guid WorkflowId) : IWorkflowMessage;

/// <summary>
/// Message to start execution of a previously created workflow instance.
/// This triggers the actual execution of nodes in the workflow~ 🚀
/// </summary>
/// <param name="ExecutionId">The execution ID to start.</param>
public record StartExecution(Guid ExecutionId) : IWorkflowMessage;

/// <summary>
/// Message to request cancellation of a running workflow execution.
/// The workflow will attempt to gracefully stop all running nodes~ 🛑
/// </summary>
/// <param name="ExecutionId">The execution ID to cancel.</param>
public record CancelExecution(Guid ExecutionId) : IWorkflowMessage;

/// <summary>
/// Message to query the current status of a workflow execution.
/// Returns detailed execution state including progress and node states~ 📊
/// </summary>
/// <param name="ExecutionId">The execution ID to query.</param>
public record GetWorkflowStatus(Guid ExecutionId) : IWorkflowMessage;

/// <summary>
/// Response message containing the current workflow execution status.
/// Includes state, progress, and detailed information~ 💝
/// </summary>
/// <param name="ExecutionId">The execution ID.</param>
/// <param name="State">Current execution state.</param>
/// <param name="Progress">Completion percentage (0-100).</param>
/// <param name="NodeStates">Status of individual nodes.</param>
/// <param name="StartTime">When execution started.</param>
/// <param name="EndTime">When execution completed (if finished).</param>
/// <param name="Error">Error message if execution failed.</param>
public record WorkflowStatusResponse(
    Guid ExecutionId,
    ExecutionState State,
    int Progress,
    Dictionary<string, NodeExecutionState> NodeStates,
    DateTimeOffset StartTime,
    DateTimeOffset? EndTime,
    string? Error) : IWorkflowMessage;

/// <summary>
/// Message sent to execute a single workflow node.
/// Contains all the data needed for the node to perform its operation~ ⚡
/// </summary>
/// <param name="NodeId">The unique ID of the node to execute.</param>
/// <param name="Inputs">Input values for the node (from previous nodes or workflow inputs).</param>
/// <param name="ExecutionId">The parent execution ID for correlation.</param>
public record Execute(
    string NodeId,
    Dictionary<string, object?> Inputs,
    Guid ExecutionId) : IWorkflowMessage;

/// <summary>
/// Message sent when a node completes execution successfully.
/// Contains the node's output values to be passed to downstream nodes~ ✅
/// </summary>
/// <param name="NodeId">The ID of the completed node.</param>
/// <param name="Outputs">Output values produced by the node.</param>
/// <param name="ExecutionId">The parent execution ID.</param>
/// <param name="Duration">How long the node took to execute.</param>
public record NodeExecutionCompleted(
    string NodeId,
    Dictionary<string, object?> Outputs,
    Guid ExecutionId,
    TimeSpan Duration) : IWorkflowMessage;

/// <summary>
/// Message sent when a node execution fails.
/// Contains error details for logging and error handling~ ⚠️
/// </summary>
/// <param name="NodeId">The ID of the failed node.</param>
/// <param name="Error">The exception that occurred.</param>
/// <param name="ExecutionId">The parent execution ID.</param>
/// <param name="Duration">How long the node ran before failing.</param>
public record NodeExecutionFailed(
    string NodeId,
    Exception Error,
    Guid ExecutionId,
    TimeSpan Duration) : IWorkflowMessage;

/// <summary>
/// Message sent when an entire workflow execution completes successfully.
/// Contains the final outputs of the workflow~ 🎊
/// </summary>
/// <param name="ExecutionId">The completed execution ID.</param>
/// <param name="Outputs">Final output values from the workflow.</param>
/// <param name="Duration">Total workflow execution time.</param>
public record WorkflowCompleted(
    Guid ExecutionId,
    Dictionary<string, object?> Outputs,
    TimeSpan Duration) : IWorkflowMessage;

/// <summary>
/// Message sent when a workflow execution fails.
/// Contains error information and partial results if any~ 😿
/// </summary>
/// <param name="ExecutionId">The failed execution ID.</param>
/// <param name="Error">The exception that caused the failure.</param>
/// <param name="Duration">How long the workflow ran before failing.</param>
/// <param name="PartialOutputs">Any outputs that were generated before failure.</param>
public record WorkflowFailed(
    Guid ExecutionId,
    Exception Error,
    TimeSpan Duration,
    Dictionary<string, object?>? PartialOutputs) : IWorkflowMessage;

/// <summary>
/// Message to request current progress of a workflow execution.
/// Returns percentage complete and current node information~ 📈
/// </summary>
public record GetProgress : IWorkflowMessage;

/// <summary>
/// Message containing progress update information.
/// Sent in response to GetProgress or as a periodic update~ 💫
/// </summary>
/// <param name="ExecutionId">The execution ID.</param>
/// <param name="Percentage">Completion percentage (0-100).</param>
/// <param name="CurrentNode">The node currently being executed.</param>
/// <param name="CompletedNodes">Number of nodes completed.</param>
/// <param name="TotalNodes">Total number of nodes in the workflow.</param>
public record ProgressUpdate(
    Guid ExecutionId,
    int Percentage,
    string? CurrentNode,
    int CompletedNodes,
    int TotalNodes) : IWorkflowMessage;

/// <summary>
/// Enum representing the current state of a workflow execution.
/// Tracks the lifecycle from creation to completion~ 🔄
/// </summary>
public enum ExecutionState
{
    /// <summary>
    /// Execution has been created but not yet started.
    /// </summary>
    Pending,

    /// <summary>
    /// Execution is currently running.
    /// </summary>
    Running,

    /// <summary>
    /// Execution completed successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// Execution failed with an error.
    /// </summary>
    Failed,

    /// <summary>
    /// Execution was cancelled by user request.
    /// </summary>
    Cancelled,

    /// <summary>
    /// Execution is temporarily paused.
    /// </summary>
    Paused,
}

/// <summary>
/// Enum representing the execution state of an individual node.
/// Tracks node lifecycle within a workflow execution~ 🌸
/// </summary>
public enum NodeExecutionState
{
    /// <summary>
    /// Node is waiting to be executed (dependencies not met).
    /// </summary>
    Pending,

    /// <summary>
    /// Node is currently executing.
    /// </summary>
    Running,

    /// <summary>
    /// Node completed successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// Node execution failed.
    /// </summary>
    Failed,

    /// <summary>
    /// Node was skipped (conditional execution).
    /// </summary>
    Skipped,

    /// <summary>
    /// Node was cancelled before completion.
    /// </summary>
    Cancelled,
}

