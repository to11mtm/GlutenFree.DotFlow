// <copyright file="WorkflowMessages.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Engine.Messages;

using System;
using LanguageExt;
using MessagePack;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;

#region Base Interface

/// <summary>
/// Base interface for all workflow-related messages in the actor system.
/// This enables type-safe message handling and pattern matching~ 💖.
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
[Union(20, typeof(ExecutionStateChanged))]
[Union(21, typeof(NodeStateChanged))]
[Union(22, typeof(SaveExecutionSnapshot))]
[Union(23, typeof(ExecutionSnapshotSaved))]
[Union(24, typeof(GetExecutionSnapshot))]
[Union(25, typeof(ExecutionSnapshotResponse))]
[Union(26, typeof(SupervisionEvent))]
[Union(27, typeof(GracefulShutdown))]
[Union(28, typeof(GracefulShutdownComplete))]
[Union(29, typeof(ActorLifecycleEvent))]
public interface IWorkflowMessage
{
}

#endregion

#region Supervisor Messages

/// <summary>
/// Message to create a new workflow instance.
/// This is sent to the WorkflowSupervisor to initiate a new workflow execution~ ✨.
/// </summary>
/// <param name="WorkflowId">Unique identifier for the workflow definition.</param>
/// <param name="Definition">The workflow definition containing nodes and connections.</param>
/// <param name="Inputs">Initial input values for the workflow execution (immutable HashMap).</param>
/// <param name="StartOptions">Optional execution start options (caller identity, variable write mode).</param>
[MessagePackObject(keyAsPropertyName: true)]
public record CreateWorkflowInstance(
    Guid WorkflowId,
    WorkflowDefinition Definition,
    HashMap<string, object?> Inputs,
    ExecutionStartOptions? StartOptions = null) : IWorkflowMessage;

/// <summary>
/// Defines how variable updates emitted by modules should be persisted~ 💾.
/// </summary>
public enum VariableWriteMode
{
    /// <summary>Persist module variable updates to execution scope only.</summary>
    Execution = 0,

    /// <summary>Persist module variable updates to workflow scope only.</summary>
    Workflow = 1,

    /// <summary>Persist module variable updates to both execution and workflow scopes.</summary>
    Dual = 2,
}

/// <summary>
/// Optional settings provided when starting a workflow execution.
/// </summary>
/// <param name="CallerId">Identity of the API/internal caller used for execution audit fields.</param>
/// <param name="VariableWriteMode">How variable updates are persisted. Defaults to execution scope.</param>
[MessagePackObject(keyAsPropertyName: true)]
public record ExecutionStartOptions(
    string? CallerId = null,
    VariableWriteMode VariableWriteMode = VariableWriteMode.Execution);

/// <summary>
/// Response message containing the execution ID for a newly created workflow instance.
/// Sent back to the caller after successful workflow creation~ 🎉.
/// </summary>
/// <param name="ExecutionId">Unique identifier for this workflow execution.</param>
/// <param name="WorkflowId">The workflow definition ID that was instantiated.</param>
[MessagePackObject(keyAsPropertyName: true)]
public record WorkflowInstanceCreated(
    Guid ExecutionId,
    Guid WorkflowId) : IWorkflowMessage;

/// <summary>
/// Response message when workflow instance creation fails.
/// Contains validation errors or other failure reasons~ ❌.
/// </summary>
/// <param name="WorkflowId">The workflow definition ID that failed to instantiate.</param>
/// <param name="Errors">List of error messages describing why creation failed.</param>
[MessagePackObject(keyAsPropertyName: true)]
public record WorkflowInstanceCreationFailed(
    Guid WorkflowId,
    Arr<string> Errors) : IWorkflowMessage;

/// <summary>
/// Message to query the current status of a workflow execution.
/// Returns detailed execution state including progress and node states~ 📊.
/// </summary>
/// <param name="ExecutionId">The execution ID to query.</param>
[MessagePackObject(keyAsPropertyName: true)]
public record GetWorkflowStatus(Guid ExecutionId) : IWorkflowMessage;

/// <summary>
/// Response message containing the current workflow execution status.
/// Includes state, progress, and detailed information~ 💝.
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
    Option<string> Error) : IWorkflowMessage;

#endregion

#region Executor Messages

/// <summary>
/// Message to start execution of a previously created workflow instance.
/// This triggers the actual execution of nodes in the workflow~ 🚀.
/// </summary>
/// <param name="ExecutionId">The execution ID to start.</param>
[MessagePackObject(keyAsPropertyName: true)]
public record StartExecution(Guid ExecutionId) : IWorkflowMessage;

/// <summary>
/// Message to request cancellation of a running workflow execution.
/// The workflow will attempt to gracefully stop all running nodes~ 🛑.
/// </summary>
/// <param name="ExecutionId">The execution ID to cancel.</param>
[MessagePackObject(keyAsPropertyName: true)]
public record CancelExecution(Guid ExecutionId) : IWorkflowMessage;

/// <summary>
/// Message to pause a running workflow execution.
/// Running nodes will complete, but no new nodes will start~ ⏸️.
/// </summary>
/// <param name="ExecutionId">The execution ID to pause.</param>
[MessagePackObject(keyAsPropertyName: true)]
public record PauseExecution(Guid ExecutionId) : IWorkflowMessage;

/// <summary>
/// Message to resume a paused workflow execution.
/// Execution will continue from where it was paused~ ▶️.
/// </summary>
/// <param name="ExecutionId">The execution ID to resume.</param>
[MessagePackObject(keyAsPropertyName: true)]
public record ResumeExecution(Guid ExecutionId) : IWorkflowMessage;

/// <summary>
/// Confirmation that a workflow execution has been paused.
/// Sent to parent after pause is complete~ ⏸️.
/// </summary>
/// <param name="ExecutionId">The paused execution ID.</param>
/// <param name="CompletedNodes">Number of nodes that completed before pause.</param>
/// <param name="PendingNodes">Number of nodes still pending.</param>
[MessagePackObject(keyAsPropertyName: true)]
public record ExecutionPaused(
    Guid ExecutionId,
    int CompletedNodes,
    int PendingNodes) : IWorkflowMessage;

/// <summary>
/// Confirmation that a workflow execution has been resumed.
/// Sent to parent after resume is complete~ ▶️.
/// </summary>
/// <param name="ExecutionId">The resumed execution ID.</param>
[MessagePackObject(keyAsPropertyName: true)]
public record ExecutionResumed(Guid ExecutionId) : IWorkflowMessage;

/// <summary>
/// Message to request current progress of a workflow execution.
/// Returns percentage complete and current node information~ 📈.
/// </summary>
[MessagePackObject(keyAsPropertyName: true)]
public record GetProgress() : IWorkflowMessage;

/// <summary>
/// Message containing progress update information.
/// Sent in response to GetProgress or as a periodic update~ 💫.
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
    int TotalNodes) : IWorkflowMessage;

/// <summary>
/// Message sent when an entire workflow execution completes successfully.
/// Contains the final outputs of the workflow~ 🎊.
/// </summary>
/// <param name="ExecutionId">The completed execution ID.</param>
/// <param name="Outputs">Final output values from the workflow (immutable HashMap).</param>
/// <param name="Duration">Total workflow execution time.</param>
[MessagePackObject(keyAsPropertyName: true)]
public record WorkflowCompleted(
    Guid ExecutionId,
    HashMap<string, object?> Outputs,
    TimeSpan Duration) : IWorkflowMessage;

/// <summary>
/// Message sent when a workflow execution fails.
/// Contains error information and partial results if any~ 😿.
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
/// Contains all the data needed for the node to perform its operation~ ⚡.
/// </summary>
/// <param name="NodeId">The unique ID of the node to execute.</param>
/// <param name="Inputs">Input values for the node (immutable HashMap).</param>
/// <param name="ExecutionId">The parent execution ID for correlation.</param>
/// <param name="Variables">Current workflow-level variables for module access. 💾.</param>
[MessagePackObject(keyAsPropertyName: true)]
public record Execute(
    string NodeId,
    HashMap<string, object?> Inputs,
    Guid ExecutionId,
    HashMap<string, object?> Variables = default) : IWorkflowMessage;

/// <summary>
/// Message sent when a node completes execution successfully.
/// Contains the node's output values to be passed to downstream nodes~ ✅.
/// </summary>
/// <param name="NodeId">The ID of the completed node.</param>
/// <param name="Outputs">Output values produced by the node (immutable HashMap).</param>
/// <param name="ExecutionId">The parent execution ID.</param>
/// <param name="Duration">How long the node took to execute.</param>
/// <param name="Metrics">Optional execution metrics (duration, memory, custom). 📊.</param>
/// <param name="VariableUpdates">Optional workflow variable mutations from the module. 💾.</param>
/// <param name="ActivePorts">
/// Optional selective port activation for downstream routing~ 🎯.
/// CopilotNote: When empty/default, the engine fires ALL outgoing connections (legacy behaviour).
/// When non-empty, only connections whose SourcePortName appears in this list are activated.
/// Populated by NodeExecutor from <see cref="Workflow.Modules.Abstractions.ModuleResult.ActivePorts"/>~ 🌸.
/// </param>
[MessagePackObject(keyAsPropertyName: true)]
public record NodeExecutionCompleted(
    string NodeId,
    HashMap<string, object?> Outputs,
    Guid ExecutionId,
    TimeSpan Duration,
    ExecutionMetrics? Metrics = null,
    HashMap<string, object?>? VariableUpdates = null,
    Arr<string> ActivePorts = default) : IWorkflowMessage;

/// <summary>
/// Message sent when a node execution fails.
/// Contains error details for logging and error handling~ ⚠️.
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
    TimeSpan Duration) : IWorkflowMessage;

/// <summary>
/// Message to retry a failed node.
/// Used when retry policy is configured for the node~ 🔄.
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
    int MaxAttempts) : IWorkflowMessage;

/// <summary>
/// Notification that a node is being retried.
/// Sent to parent when retry begins~ 🔄.
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
    Exception LastError) : IWorkflowMessage;

#endregion

#region State Tracking Messages

/// <summary>
/// Notification published when a workflow execution transitions between states.
/// Published to both parent actor and EventStream for decoupled observability~ 📡✨.
/// </summary>
/// <remarks>
/// CopilotNote: Subscribe to these via <c>Context.System.EventStream.Subscribe&lt;ExecutionStateChanged&gt;(Self)</c>
/// for building monitoring dashboards, audit logs, or webhook triggers! UwU 💖.
/// </remarks>
/// <param name="ExecutionId">The execution that transitioned. 🆔.</param>
/// <param name="OldState">The state before the transition. 🔙.</param>
/// <param name="NewState">The state after the transition. 🔜.</param>
/// <param name="Timestamp">When the transition occurred. ⏰.</param>
[MessagePackObject(keyAsPropertyName: true)]
public record ExecutionStateChanged(
    Guid ExecutionId,
    ExecutionState OldState,
    ExecutionState NewState,
    DateTimeOffset Timestamp) : IWorkflowMessage;

/// <summary>
/// Notification published when an individual node transitions between states.
/// Published alongside <see cref="ExecutionStateChanged"/> for granular tracking~ 🧩✨.
/// </summary>
/// <param name="NodeId">The node that transitioned. 🆔.</param>
/// <param name="ExecutionId">The parent execution. 🔗.</param>
/// <param name="OldState">The state before the transition. 🔙.</param>
/// <param name="NewState">The state after the transition. 🔜.</param>
/// <param name="Timestamp">When the transition occurred. ⏰.</param>
[MessagePackObject(keyAsPropertyName: true)]
public record NodeStateChanged(
    string NodeId,
    Guid ExecutionId,
    NodeExecutionState OldState,
    NodeExecutionState NewState,
    DateTimeOffset Timestamp) : IWorkflowMessage;

/// <summary>
/// Request to persist a snapshot of the current execution context.
/// Used for persistence and resumability~ 💾.
/// </summary>
/// <param name="ExecutionId">The execution to snapshot. 🆔.</param>
[MessagePackObject(keyAsPropertyName: true)]
public record SaveExecutionSnapshot(
    Guid ExecutionId) : IWorkflowMessage;

/// <summary>
/// Confirmation that an execution snapshot was saved successfully~ ✅.
/// </summary>
/// <param name="ExecutionId">The execution that was snapshotted. 🆔.</param>
/// <param name="Timestamp">When the snapshot was saved. ⏰.</param>
[MessagePackObject(keyAsPropertyName: true)]
public record ExecutionSnapshotSaved(
    Guid ExecutionId,
    DateTimeOffset Timestamp) : IWorkflowMessage;

/// <summary>
/// Request to retrieve a previously saved execution snapshot~ 📥.
/// </summary>
/// <param name="ExecutionId">The execution to retrieve. 🆔.</param>
[MessagePackObject(keyAsPropertyName: true)]
public record GetExecutionSnapshot(
    Guid ExecutionId) : IWorkflowMessage;

/// <summary>
/// Response containing a previously saved execution snapshot~ 📊.
/// </summary>
/// <param name="ExecutionId">The execution ID. 🆔.</param>
/// <param name="Context">The saved context (None if not found). 📋.</param>
[MessagePackObject(keyAsPropertyName: true)]
public record ExecutionSnapshotResponse(
    Guid ExecutionId,
    Option<Models.WorkflowExecutionContext> Context) : IWorkflowMessage;

/// <summary>
/// Event published when the Akka.NET supervision strategy makes a decision.
/// Enables monitoring and observability of actor failure handling~ 🛡️✨.
/// </summary>
/// <remarks>
/// CopilotNote: Subscribe to these via <c>Context.System.EventStream.Subscribe&lt;SupervisionEvent&gt;(Self)</c>
/// for building failure dashboards, alerting, or audit logs! UwU 💖.
/// </remarks>
/// <param name="ActorPath">The path of the actor that failed. 📍.</param>
/// <param name="ExceptionType">The type name of the exception that caused the failure. 💥.</param>
/// <param name="ExceptionMessage">The exception message for debugging. 📝.</param>
/// <param name="Directive">The supervision directive applied (Restart, Stop, Escalate, Resume). 🎯.</param>
/// <param name="Timestamp">When the supervision event occurred. ⏰.</param>
/// <param name="ExecutionId">The related execution ID, if known. 🆔.</param>
[MessagePackObject(keyAsPropertyName: true)]
public record SupervisionEvent(
    string ActorPath,
    string ExceptionType,
    string ExceptionMessage,
    string Directive,
    DateTimeOffset Timestamp,
    Option<Guid> ExecutionId) : IWorkflowMessage;

#endregion

#region Lifecycle Messages

/// <summary>
/// Request for a graceful shutdown of the WorkflowSupervisor.
/// Active workflows will be given a chance to complete before the supervisor stops~ 🛑🌸.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: Send this message to the WorkflowSupervisor to initiate a clean shutdown.
/// Workflows already in a terminal state are ignored. Running/paused workflows are cancelled.
/// The supervisor responds with <see cref="GracefulShutdownComplete"/> when done. UwU 💖.
/// </para>
/// </remarks>
/// <param name="Timeout">Maximum time to wait for active workflows to finish before forcing cancellation. ⏰.</param>
[MessagePackObject(keyAsPropertyName: true)]
public record GracefulShutdown(
    TimeSpan Timeout) : IWorkflowMessage;

/// <summary>
/// Response confirming that graceful shutdown completed.
/// Contains a summary of what happened to active workflows~ ✅🌸.
/// </summary>
/// <param name="CancelledCount">Number of workflows that were cancelled during shutdown. 🛑.</param>
/// <param name="CompletedCount">Number of workflows that were already in a terminal state. ✅.</param>
/// <param name="Timestamp">When the shutdown completed. ⏰.</param>
[MessagePackObject(keyAsPropertyName: true)]
public record GracefulShutdownComplete(
    int CancelledCount,
    int CompletedCount,
    DateTimeOffset Timestamp) : IWorkflowMessage;

/// <summary>
/// Event published when an actor lifecycle hook fires (PreStart, PostStop, PreRestart, PostRestart).
/// Enables external monitoring and testing of actor lifecycle behavior~ 🌸📡.
/// </summary>
/// <remarks>
/// CopilotNote: Subscribe via <c>Context.System.EventStream.Subscribe&lt;ActorLifecycleEvent&gt;(Self)</c>
/// to monitor actor lifecycle in tests or for operational dashboards! UwU 💖.
/// </remarks>
/// <param name="ActorPath">The path of the actor whose lifecycle changed. 📍.</param>
/// <param name="ActorType">The CLR type name of the actor (e.g., "WorkflowSupervisor"). 🏷️.</param>
/// <param name="Hook">Which lifecycle hook fired (PreStart, PostStop, PreRestart, PostRestart). 🔄.</param>
/// <param name="Timestamp">When the event occurred. ⏰.</param>
/// <param name="Reason">Optional reason (e.g., exception message for restart hooks). 📝.</param>
[MessagePackObject(keyAsPropertyName: true)]
public record ActorLifecycleEvent(
    string ActorPath,
    string ActorType,
    string Hook,
    DateTimeOffset Timestamp,
    Option<string> Reason) : IWorkflowMessage;

#endregion

#region Enums

/// <summary>
/// CopilotNote: ExecutionState and NodeExecutionState enums have been moved to
/// Workflow.Core/Models/ExecutionState.cs so that Workflow.Persistence can reference them
/// without depending on Workflow.Engine~ 💖
/// </summary>

#endregion

