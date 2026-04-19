// ────────────────────────────────────────────────────────────────────────────────
// Copyright © GlutenFree 2025. Made with love by Ami-Chan! UwU 💖
// Licensed under the MIT License.
// ────────────────────────────────────────────────────────────────────────────────

using LanguageExt;
using MessagePack;
using Workflow.Core.Models;
using Workflow.Engine.Messages;

namespace Workflow.Engine.Models;

/// <summary>
/// Immutable snapshot of a workflow execution's complete state~ 📊✨
/// Tracks everything from lifecycle to per-node status, outputs, and errors.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: This is the canonical state object for a workflow execution!
/// It's fully immutable (LanguageExt types everywhere), serializable (MessagePack),
/// and designed for snapshotting/persistence. Use the <c>With*</c> helpers for
/// clean state transitions — each returns a shiny new instance, nya~ 💝.
/// </para>
/// <para>
/// The <see cref="StateHistory"/> tracks every transition for auditing and debugging.
/// Keep this bounded in production (e.g., last N transitions) to avoid unbounded growth!.
/// </para>
/// </remarks>
/// <param name="ExecutionId">Unique ID for this execution instance. 🆔.</param>
/// <param name="WorkflowId">The workflow definition this execution was created from. 📋.</param>
/// <param name="WorkflowName">Human-readable name of the workflow. 🏷️.</param>
/// <param name="State">Current execution lifecycle state. 🔄.</param>
/// <param name="StartTime">When execution began (None if still Pending). ⏰.</param>
/// <param name="EndTime">When execution reached a terminal state (None if still active). 🏁.</param>
/// <param name="Variables">Workflow-scoped variables accessible to all nodes. 💾.</param>
/// <param name="NodeStates">Per-node execution state map. 🧩.</param>
/// <param name="Outputs">Final collected outputs from end nodes. 📤.</param>
/// <param name="Error">Error message if execution failed (None if no error). ❌.</param>
/// <param name="StateHistory">Ordered log of state transitions for auditing. 📜.</param>
/// <param name="NodeTimings">Per-node start/end timing information. ⏱️.</param>
[MessagePackObject(keyAsPropertyName: true)]
public record WorkflowExecutionContext(
    Guid ExecutionId,
    Guid WorkflowId,
    string WorkflowName,
    ExecutionState State,
    Option<DateTimeOffset> StartTime,
    Option<DateTimeOffset> EndTime,
    HashMap<string, object?> Variables,
    HashMap<string, NodeExecutionState> NodeStates,
    HashMap<string, object?> Outputs,
    Option<string> Error,
    Arr<StateTransition> StateHistory,
    HashMap<string, NodeTimingInfo> NodeTimings)
{
    /// <summary>
    /// Creates a fresh <see cref="WorkflowExecutionContext"/> in the Pending state~ 🌱.
    /// </summary>
    /// <param name="executionId">The unique execution ID.</param>
    /// <param name="workflowId">The workflow definition ID.</param>
    /// <param name="workflowName">Human-readable workflow name.</param>
    /// <param name="initialNodeIds">Node IDs to initialize as Pending.</param>
    /// <param name="initialVariables">Initial workflow variable values.</param>
    /// <returns>A brand-new context, fresh and ready to go! ✨.</returns>
    public static WorkflowExecutionContext Create(
        Guid executionId,
        Guid workflowId,
        string workflowName,
        IEnumerable<string> initialNodeIds,
        HashMap<string, object?> initialVariables = default)
    {
        var nodeStates = initialNodeIds
            .Select(id => (id, NodeExecutionState.Pending))
            .ToHashMap();

        return new WorkflowExecutionContext(
            ExecutionId: executionId,
            WorkflowId: workflowId,
            WorkflowName: workflowName,
            State: ExecutionState.Pending,
            StartTime: Option<DateTimeOffset>.None,
            EndTime: Option<DateTimeOffset>.None,
            Variables: initialVariables,
            NodeStates: nodeStates,
            Outputs: HashMap<string, object?>.Empty,
            Error: Option<string>.None,
            StateHistory: Arr<StateTransition>.Empty,
            NodeTimings: HashMap<string, NodeTimingInfo>.Empty);
    }

    #region State Transition Helpers 💫

    /// <summary>
    /// Transitions the workflow state with full audit trail~ 🔄.
    /// </summary>
    /// <param name="newState">The target state.</param>
    /// <param name="timestamp">When the transition occurred.</param>
    /// <param name="reason">Optional reason for the transition.</param>
    /// <returns>New context with updated state and history. ✨.</returns>
    public WorkflowExecutionContext WithState(
        ExecutionState newState,
        DateTimeOffset timestamp,
        string? reason = null)
    {
        var transition = new StateTransition(
            OldState: State,
            NewState: newState,
            Timestamp: timestamp,
            Reason: reason != null ? Option<string>.Some(reason) : Option<string>.None);

        return this with
        {
            State = newState,
            StateHistory = StateHistory.Add(transition),
        };
    }

    /// <summary>
    /// Marks execution as Running with a start timestamp~ 🚀.
    /// </summary>
    public WorkflowExecutionContext WithRunning(DateTimeOffset startTime) =>
        WithState(ExecutionState.Running, startTime, "Execution started")
            with { StartTime = Option<DateTimeOffset>.Some(startTime) };

    /// <summary>
    /// Marks execution as Completed with an end timestamp~ 🎉.
    /// </summary>
    public WorkflowExecutionContext WithCompleted(
        DateTimeOffset endTime,
        HashMap<string, object?> outputs) =>
        WithState(ExecutionState.Completed, endTime, "Execution completed successfully")
            with
            {
                EndTime = Option<DateTimeOffset>.Some(endTime),
                Outputs = outputs,
            };

    /// <summary>
    /// Marks execution as Failed with error details~ 💔.
    /// </summary>
    public WorkflowExecutionContext WithFailed(
        DateTimeOffset endTime,
        string errorMessage) =>
        WithState(ExecutionState.Failed, endTime, $"Failed: {errorMessage}")
            with
            {
                EndTime = Option<DateTimeOffset>.Some(endTime),
                Error = Option<string>.Some(errorMessage),
            };

    /// <summary>
    /// Marks execution as Cancelled~ 🛑.
    /// </summary>
    public WorkflowExecutionContext WithCancelled(DateTimeOffset endTime) =>
        WithState(ExecutionState.Cancelled, endTime, "Cancelled by user")
            with { EndTime = Option<DateTimeOffset>.Some(endTime) };

    /// <summary>
    /// Marks execution as Paused~ ⏸️.
    /// </summary>
    public WorkflowExecutionContext WithPaused(DateTimeOffset timestamp) =>
        WithState(ExecutionState.Paused, timestamp, "Execution paused");

    /// <summary>
    /// Resumes execution from Paused back to Running~ ▶️.
    /// </summary>
    public WorkflowExecutionContext WithResumed(DateTimeOffset timestamp) =>
        WithState(ExecutionState.Running, timestamp, "Execution resumed");

    #endregion

    #region Node State Helpers 🧩

    /// <summary>
    /// Updates a single node's execution state~ 🔄.
    /// </summary>
    public WorkflowExecutionContext WithNodeState(
        string nodeId,
        NodeExecutionState newState) =>
        this with { NodeStates = NodeStates.AddOrUpdate(nodeId, newState) };

    /// <summary>
    /// Records a node starting execution with timing info~ ⏱️.
    /// </summary>
    public WorkflowExecutionContext WithNodeStarted(
        string nodeId,
        DateTimeOffset startTime) =>
        WithNodeState(nodeId, NodeExecutionState.Running)
            with
            {
                NodeTimings = NodeTimings.AddOrUpdate(
                    nodeId,
                    new NodeTimingInfo(
                        Option<DateTimeOffset>.Some(startTime),
                        Option<DateTimeOffset>.None,
                        Option<TimeSpan>.None)),
            };

    /// <summary>
    /// Records a node completing execution with timing info~ ✅.
    /// </summary>
    public WorkflowExecutionContext WithNodeCompleted(
        string nodeId,
        DateTimeOffset endTime,
        TimeSpan duration) =>
        WithNodeState(nodeId, NodeExecutionState.Completed)
            with
            {
                NodeTimings = NodeTimings.AddOrUpdate(
                    nodeId,
                    NodeTimings.Find(nodeId).Match(
                        Some: existing => existing with
                        {
                            EndTime = Option<DateTimeOffset>.Some(endTime),
                            Duration = Option<TimeSpan>.Some(duration),
                        },
                        None: () => new NodeTimingInfo(
                            Option<DateTimeOffset>.None,
                            Option<DateTimeOffset>.Some(endTime),
                            Option<TimeSpan>.Some(duration)))),
            };

    /// <summary>
    /// Records a node failing execution~ ❌.
    /// </summary>
    public WorkflowExecutionContext WithNodeFailed(
        string nodeId,
        DateTimeOffset endTime,
        TimeSpan duration) =>
        WithNodeState(nodeId, NodeExecutionState.Failed)
            with
            {
                NodeTimings = NodeTimings.AddOrUpdate(
                    nodeId,
                    NodeTimings.Find(nodeId).Match(
                        Some: existing => existing with
                        {
                            EndTime = Option<DateTimeOffset>.Some(endTime),
                            Duration = Option<TimeSpan>.Some(duration),
                        },
                        None: () => new NodeTimingInfo(
                            Option<DateTimeOffset>.None,
                            Option<DateTimeOffset>.Some(endTime),
                            Option<TimeSpan>.Some(duration)))),
            };

    /// <summary>
    /// Records a node being cancelled~ 🛑.
    /// </summary>
    public WorkflowExecutionContext WithNodeCancelled(string nodeId) =>
        WithNodeState(nodeId, NodeExecutionState.Cancelled);

    /// <summary>
    /// Records a node entering retry state — resets timing for the new attempt~ 🔄.
    /// </summary>
    public WorkflowExecutionContext WithNodeRetrying(
        string nodeId,
        DateTimeOffset timestamp) =>
        WithNodeState(nodeId, NodeExecutionState.Retrying)
            with
            {
                NodeTimings = NodeTimings.AddOrUpdate(
                    nodeId,
                    new NodeTimingInfo(
                        Option<DateTimeOffset>.Some(timestamp),
                        Option<DateTimeOffset>.None,
                        Option<TimeSpan>.None)),
            };

    #endregion

    #region Variable Helpers 💾

    /// <summary>
    /// Updates a workflow variable~ 💾.
    /// </summary>
    public WorkflowExecutionContext WithVariable(string name, object? value) =>
        this with { Variables = Variables.AddOrUpdate(name, value) };

    /// <summary>
    /// Merges additional outputs into the context~ 📤.
    /// </summary>
    public WorkflowExecutionContext WithOutputs(HashMap<string, object?> additionalOutputs) =>
        this with
        {
            Outputs = additionalOutputs.Fold(Outputs, (acc, kv) => acc.AddOrUpdate(kv.Key, kv.Value)),
        };

    #endregion

    #region Query Helpers 🔍

    /// <summary>
    /// Calculates the completion percentage based on completed nodes~ 📊.
    /// </summary>
    public int CalculateProgress()
    {
        var totalNodes = NodeStates.Count;
        if (totalNodes == 0)
        {
            return 100;
        }

        var completedCount = NodeStates.Values.Count(s => s == NodeExecutionState.Completed);
        return (int)((completedCount * 100.0) / totalNodes);
    }

    /// <summary>
    /// Returns true if the execution is in a terminal state (Completed, Failed, Cancelled)~ 🏁.
    /// </summary>
    public bool IsTerminal =>
        State is ExecutionState.Completed or ExecutionState.Failed or ExecutionState.Cancelled;

    /// <summary>
    /// Gets the count of nodes in each state~ 📈.
    /// </summary>
    public (int Pending, int Running, int Completed, int Failed, int Cancelled, int Retrying) GetNodeStateCounts()
    {
        var pending = NodeStates.Values.Count(s => s == NodeExecutionState.Pending);
        var running = NodeStates.Values.Count(s => s == NodeExecutionState.Running);
        var completed = NodeStates.Values.Count(s => s == NodeExecutionState.Completed);
        var failed = NodeStates.Values.Count(s => s == NodeExecutionState.Failed);
        var cancelled = NodeStates.Values.Count(s => s == NodeExecutionState.Cancelled);
        var retrying = NodeStates.Values.Count(s => s == NodeExecutionState.Retrying);
        return (pending, running, completed, failed, cancelled, retrying);
    }

    #endregion
}

/// <summary>
/// Records a single state transition for audit trail purposes~ 📜✨.
/// </summary>
/// <param name="OldState">The state before the transition. 🔙.</param>
/// <param name="NewState">The state after the transition. 🔜.</param>
/// <param name="Timestamp">When the transition occurred. ⏰.</param>
/// <param name="Reason">Optional human-readable reason for the transition. 📝.</param>
[MessagePackObject(keyAsPropertyName: true)]
public record StateTransition(
    ExecutionState OldState,
    ExecutionState NewState,
    DateTimeOffset Timestamp,
    Option<string> Reason);

/// <summary>
/// Timing information for a single node execution~ ⏱️✨.
/// </summary>
/// <param name="StartTime">When the node started executing. 🚀.</param>
/// <param name="EndTime">When the node finished. 🏁.</param>
/// <param name="Duration">Total execution duration. ⏳.</param>
[MessagePackObject(keyAsPropertyName: true)]
public record NodeTimingInfo(
    Option<DateTimeOffset> StartTime,
    Option<DateTimeOffset> EndTime,
    Option<TimeSpan> Duration);
