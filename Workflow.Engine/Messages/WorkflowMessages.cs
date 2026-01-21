// <copyright file="WorkflowMessages.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Engine.Messages;

using System;
using LanguageExt;
using MessagePack;
using Workflow.Core.Models;

#region Base Interface

/// <summary>
/// Base interface for all workflow-related messages in the actor system.
/// This enables type-safe message handling and pattern matching~ 💖
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: All workflow messages should implement this interface for consistent handling.
/// Messages use LanguageExt collections for immutability and structural equality.
/// MessagePack attributes enable efficient binary serialization for persistence and clustering.
/// </para>
/// </remarks>
[Union(0, typeof(CreateWorkflowInstance))]
[Union(1, typeof(WorkflowInstanceCreated))]
[Union(2, typeof(WorkflowInstanceCreationFailed))]
[Union(3, typeof(GetWorkflowStatus))]
[Union(4, typeof(WorkflowStatusResponse))]
[Union(5, typeof(StartExecution))]
[Union(6, typeof(CancelExecution))]
[Union(7, typeof(PauseExecution))]
[Union(8, typeof(ResumeExecution))]
[Union(9, typeof(ExecutionPaused))]
[Union(10, typeof(ExecutionResumed))]
[Union(11, typeof(GetProgress))]
[Union(12, typeof(ProgressUpdate))]
[Union(13, typeof(WorkflowCompleted))]
[Union(14, typeof(WorkflowFailed))]
[Union(15, typeof(Execute))]
[Union(16, typeof(NodeExecutionCompleted))]
[Union(17, typeof(NodeExecutionFailed))]
[Union(18, typeof(RetryNode))]
[Union(19, typeof(NodeRetrying))]
public interface IWorkflowMessage
{
}

#endregion

#region Supervisor Messages

/// <summary>
/// Message to create a new workflow instance.
/// This is sent to the WorkflowSupervisor to initiate a new workflow execution~ ✨
/// </summary>
/// <param name="WorkflowId">Unique identifier for the workflow definition.</param>
/// <param name="Definition">The workflow definition containing nodes and connections.</param>
/// <param name="Inputs">Initial input values for the workflow execution (immutable HashMap).</param>
[MessagePackObject(keyAsPropertyName: true)]
public record CreateWorkflowInstance(
    Guid WorkflowId,
    WorkflowDefinition Definition,
    HashMap<string, object?> Inputs): IWorkflowMessage;

/// <summary>
/// Response message containing the execution ID for a newly created workflow instance.
/// Sent back to the caller after successful workflow creation~ 🎉
/// </summary>
/// <param name="ExecutionId">Unique identifier for this workflow execution.</param>
/// <param name="WorkflowId">The workflow definition ID that was instantiated.</param>
[MessagePackObject(keyAsPropertyName: true)]
public record WorkflowInstanceCreated(
    Guid ExecutionId,
    Guid WorkflowId): IWorkflowMessage;

/// <summary>
/// Response message when workflow instance creation fails.
/// Contains validation errors or other failure reasons~ ❌
/// </summary>
/// <param name="WorkflowId">The workflow definition ID that failed to instantiate.</param>
/// <param name="Errors">List of error messages describing why creation failed.</param>
[MessagePackObject(keyAsPropertyName: true)]
public record WorkflowInstanceCreationFailed(
    Guid WorkflowId,
    Arr<string> Errors): IWorkflowMessage;

/// <summary>
/// Message to query the current status of a workflow execution.
/// Returns detailed execution state including progress and node states~ 📊
/// </summary>
/// <param name="ExecutionId">The execution ID to query.</param>
[MessagePackObject(keyAsPropertyName: true)]
public record GetWorkflowStatus(Guid ExecutionId): IWorkflowMessage;

/// <summary>
/// Response message containing the current workflow execution status.
/// Includes state, progress, and detailed information~ 💝
/// </summary>
/// <param name="ExecutionId">The execution ID.</param>
/// <param name="State">Current execution state.</param>
/// <param name="Progress">Completion percentage (0-100).</param>
/// <param name="NodeStates">Status of individual nodes (immutable HashMap).</param>
/// <param name="StartTime">When execution started.</param>
/// <param name="EndTime">When execution completed (if finished).</param>
/// <param name="Error">Error message if execution failed.</param>
[MessagePackObject(keyAsPropertyName: true)]
public record WorkflowStatusResponse(
    Guid ExecutionId,
    ExecutionState State,
    int Progress,
    HashMap<string, NodeExecutionState> NodeStates,
    DateTimeOffset StartTime,
    Option<DateTimeOffset> EndTime,
    Option<string> Error): IWorkflowMessage;

#endregion

#region Executor Messages

/// <summary>
/// Message to start execution of a previously created workflow instance.
/// This triggers the actual execution of nodes in the workflow~ 🚀
/// </summary>
/// <param name="ExecutionId">The execution ID to start.</param>
[MessagePackObject(keyAsPropertyName: true)]
public record StartExecution(Guid ExecutionId): IWorkflowMessage;

/// <summary>
/// Message to request cancellation of a running workflow execution.
/// The workflow will attempt to gracefully stop all running nodes~ 🛑
/// </summary>
/// <param name="ExecutionId">The execution ID to cancel.</param>
[MessagePackObject(keyAsPropertyName: true)]
public record CancelExecution(Guid ExecutionId): IWorkflowMessage;

/// <summary>
/// Message to pause a running workflow execution.
/// Running nodes will complete, but no new nodes will start~ ⏸️
/// </summary>
/// <param name="ExecutionId">The execution ID to pause.</param>
[MessagePackObject(keyAsPropertyName: true)]
public record PauseExecution(Guid ExecutionId): IWorkflowMessage;

/// <summary>
/// Message to resume a paused workflow execution.
/// Execution will continue from where it was paused~ ▶️
/// </summary>
/// <param name="ExecutionId">The execution ID to resume.</param>
[MessagePackObject(keyAsPropertyName: true)]
public record ResumeExecution(Guid ExecutionId): IWorkflowMessage;

/// <summary>
/// Confirmation that a workflow execution has been paused.
/// Sent to parent after pause is complete~ ⏸️
/// </summary>
/// <param name="ExecutionId">The paused execution ID.</param>
/// <param name="CompletedNodes">Number of nodes that completed before pause.</param>
/// <param name="PendingNodes">Number of nodes still pending.</param>
[MessagePackObject(keyAsPropertyName: true)]
public record ExecutionPaused(
    Guid ExecutionId,
    int CompletedNodes,
    int PendingNodes): IWorkflowMessage;

/// <summary>
/// Confirmation that a workflow execution has been resumed.
/// Sent to parent after resume is complete~ ▶️
/// </summary>
/// <param name="ExecutionId">The resumed execution ID.</param>
[MessagePackObject(keyAsPropertyName: true)]
public record ExecutionResumed(Guid ExecutionId): IWorkflowMessage;

/// <summary>
/// Message to request current progress of a workflow execution.
/// Returns percentage complete and current node information~ 📈
/// </summary>
[MessagePackObject(keyAsPropertyName: true)]
public record GetProgress(): IWorkflowMessage;

/// <summary>
/// Message containing progress update information.
/// Sent in response to GetProgress or as a periodic update~ 💫
/// </summary>
/// <param name="ExecutionId">The execution ID.</param>
/// <param name="Percentage">Completion percentage (0-100).</param>
/// <param name="CurrentNode">The node currently being executed (None if idle).</param>
/// <param name="CompletedNodes">Number of nodes completed.</param>
/// <param name="TotalNodes">Total number of nodes in the workflow.</param>
[MessagePackObject(keyAsPropertyName: true)]
public record ProgressUpdate(
    Guid ExecutionId,
    int Percentage,
    Option<string> CurrentNode,
    int CompletedNodes,
    int TotalNodes): IWorkflowMessage;

/// <summary>
/// Message sent when an entire workflow execution completes successfully.
/// Contains the final outputs of the workflow~ 🎊
/// </summary>
/// <param name="ExecutionId">The completed execution ID.</param>
/// <param name="Outputs">Final output values from the workflow (immutable HashMap).</param>
/// <param name="Duration">Total workflow execution time.</param>
[MessagePackObject(keyAsPropertyName: true)]
public record WorkflowCompleted(
    Guid ExecutionId,
    HashMap<string, object?> Outputs,
    TimeSpan Duration): IWorkflowMessage;

/// <summary>
/// Message sent when a workflow execution fails.
/// Contains error information and partial results if any~ 😿
/// </summary>
/// <param name="ExecutionId">The failed execution ID.</param>
/// <param name="Error">The exception that caused the failure.</param>
/// <param name="Duration">How long the workflow ran before failing.</param>
/// <param name="PartialOutputs">Any outputs that were generated before failure.</param>
[MessagePackObject(keyAsPropertyName: true)]
public record WorkflowFailed(
    Guid ExecutionId,
    Exception Error,
    TimeSpan Duration,
    Option<HashMap<string, object?>> PartialOutputs) : IWorkflowMessage;

#endregion

#region Node Messages

/// <summary>
/// Message sent to execute a single workflow node.
/// Contains all the data needed for the node to perform its operation~ ⚡
/// </summary>
/// <param name="NodeId">The unique ID of the node to execute.</param>
/// <param name="Inputs">Input values for the node (immutable HashMap).</param>
/// <param name="ExecutionId">The parent execution ID for correlation.</param>
[MessagePackObject(keyAsPropertyName: true)]
public record Execute(
    string NodeId,
    HashMap<string, object?> Inputs,
    Guid ExecutionId): IWorkflowMessage;

/// <summary>
/// Message sent when a node completes execution successfully.
/// Contains the node's output values to be passed to downstream nodes~ ✅
/// </summary>
/// <param name="NodeId">The ID of the completed node.</param>
/// <param name="Outputs">Output values produced by the node (immutable HashMap).</param>
/// <param name="ExecutionId">The parent execution ID.</param>
/// <param name="Duration">How long the node took to execute.</param>
[MessagePackObject(keyAsPropertyName: true)]
public record NodeExecutionCompleted(
    string NodeId,
    HashMap<string, object?> Outputs,
    Guid ExecutionId,
    TimeSpan Duration): IWorkflowMessage;

/// <summary>
/// Message sent when a node execution fails.
/// Contains error details for logging and error handling~ ⚠️
/// </summary>
/// <param name="NodeId">The ID of the failed node.</param>
/// <param name="Error">The exception that occurred.</param>
/// <param name="ExecutionId">The parent execution ID.</param>
/// <param name="Duration">How long the node ran before failing.</param>
[MessagePackObject(keyAsPropertyName: true)]
public record NodeExecutionFailed(
    string NodeId,
    Exception Error,
    Guid ExecutionId,
    TimeSpan Duration): IWorkflowMessage;

/// <summary>
/// Message to retry a failed node.
/// Used when retry policy is configured for the node~ 🔄
/// </summary>
/// <param name="NodeId">The ID of the node to retry.</param>
/// <param name="ExecutionId">The parent execution ID.</param>
/// <param name="Attempt">The retry attempt number (1-based).</param>
/// <param name="MaxAttempts">Maximum number of attempts allowed.</param>
[MessagePackObject(keyAsPropertyName: true)]
public record RetryNode(
    string NodeId,
    Guid ExecutionId,
    int Attempt,
    int MaxAttempts): IWorkflowMessage;

/// <summary>
/// Notification that a node is being retried.
/// Sent to parent when retry begins~ 🔄
/// </summary>
/// <param name="NodeId">The ID of the node being retried.</param>
/// <param name="ExecutionId">The parent execution ID.</param>
/// <param name="Attempt">The current retry attempt number (1-based).</param>
/// <param name="MaxAttempts">Maximum number of attempts allowed.</param>
/// <param name="LastError">The error from the previous attempt.</param>
[MessagePackObject(keyAsPropertyName: true)]
public record NodeRetrying(
    string NodeId,
    Guid ExecutionId,
    int Attempt,
    int MaxAttempts,
    Exception LastError): IWorkflowMessage;

#endregion

#region Enums

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

    /// <summary>
    /// Node is being retried after a failure.
    /// </summary>
    Retrying,
}

#endregion

