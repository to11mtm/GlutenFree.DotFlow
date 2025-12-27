// <copyright file="WorkflowExecutor.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Engine.Actors;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Akka.Actor;
using Akka.Event;
using LanguageExt;
using Workflow.Core.Models;
using Workflow.Engine.Messages;

/// <summary>
/// Actor responsible for executing a single workflow instance.
/// Orchestrates the execution graph, manages node actors, and tracks completion. 🎬✨
/// </summary>
/// <remarks>
/// <para>
/// The WorkflowExecutor manages the lifecycle of a workflow execution. It creates
/// NodeExecutor child actors for each node, handles their completion/failure messages,
/// and determines when the overall workflow is complete.
/// </para>
/// <para>
/// CopilotNote: This actor implements the "executor" pattern - it coordinates many
/// child actors (NodeExecutors) to accomplish a larger goal (workflow execution).
/// The execution follows a topological order based on node dependencies~ 💖
/// </para>
/// </remarks>
public class WorkflowExecutor : ReceiveActor
{
    private readonly ILoggingAdapter _log;
    private readonly Guid _executionId;
    private readonly WorkflowDefinition _definition;
    private readonly Dictionary<string, object?> _workflowInputs;
    private readonly IServiceProvider _serviceProvider;

    // Execution state tracking
    private ExecutionState _state = ExecutionState.Pending;
    private readonly Stopwatch _executionTimer = new();
    private DateTimeOffset _startTime;
    private DateTimeOffset? _endTime;

    // Node tracking
    private readonly Dictionary<string, IActorRef> _nodeActors = new();
    private readonly Dictionary<string, NodeExecutionState> _nodeStates = new();
    private readonly Dictionary<string, Dictionary<string, object?>> _nodeOutputs = new();
    private readonly LanguageExt.HashSet<string> _completedNodes = new();
    private readonly LanguageExt.HashSet<string> _failedNodes = new();
    private readonly LanguageExt.HashSet<string> _runningNodes = new();

    // Graph structure (built from connections)
    private readonly Dictionary<string, List<string>> _nodeSuccessors = new();
    private readonly Dictionary<string, List<string>> _nodePredecessors = new();
    private readonly Dictionary<string, int> _inDegree = new();

    // Error tracking
    private Exception? _lastError;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowExecutor"/> class.
    /// Sets up message handlers and builds the execution graph. UwU~
    /// </summary>
    /// <param name="executionId">The unique execution ID.</param>
    /// <param name="definition">The workflow definition to execute.</param>
    /// <param name="inputs">Initial input values for the workflow.</param>
    /// <param name="serviceProvider">Service provider for dependency injection.</param>
    public WorkflowExecutor(
        Guid executionId,
        WorkflowDefinition definition,
        Dictionary<string, object?> inputs,
        IServiceProvider serviceProvider)
    {
        _log = Context.GetLogger();
        _executionId = executionId;
        _definition = definition;
        _workflowInputs = inputs;
        _serviceProvider = serviceProvider;

        // Build the execution graph from connections
        BuildExecutionGraph();

        // Initialize node states
        foreach (var node in _definition.Nodes)
        {
            _nodeStates[node.Id] = NodeExecutionState.Pending;
        }

        // Set up message handlers
        Receive<StartExecution>(HandleStartExecution);
        Receive<NodeExecutionCompleted>(HandleNodeExecutionCompleted);
        Receive<NodeExecutionFailed>(HandleNodeExecutionFailed);
        Receive<GetWorkflowStatus>(HandleGetWorkflowStatus);
        Receive<GetProgress>(HandleGetProgress);
        Receive<CancelExecution>(HandleCancelExecution);
        Receive<Terminated>(HandleTerminated);

        _log.Info(
            "🎬 WorkflowExecutor created for execution {ExecutionId}, workflow '{WorkflowName}' with {NodeCount} nodes",
            _executionId,
            _definition.Name,
            _definition.Nodes.Count);
    }

    /// <summary>
    /// Creates Props for spawning a WorkflowExecutor actor.
    /// Use this factory method for proper instantiation~ 💝
    /// </summary>
    public static Props Props(
        Guid executionId,
        WorkflowDefinition definition,
        Dictionary<string, object?> inputs,
        IServiceProvider serviceProvider)
    {
        return Akka.Actor.Props.Create(
            () => new WorkflowExecutor(executionId, definition, inputs, serviceProvider));
    }

    /// <summary>
    /// Builds the execution graph (successors, predecessors, in-degrees) from workflow connections.
    /// This enables topological execution order and dependency tracking~ 🔗
    /// </summary>
    private void BuildExecutionGraph()
    {
        // Initialize empty lists for all nodes
        foreach (var node in _definition.Nodes)
        {
            _nodeSuccessors[node.Id] = new List<string>();
            _nodePredecessors[node.Id] = new List<string>();
            _inDegree[node.Id] = 0;
        }

        // Build adjacency lists from connections
        foreach (var connection in _definition.Connections)
        {
            var source = connection.SourceNodeId;
            var target = connection.TargetNodeId;

            if (_nodeSuccessors.ContainsKey(source) && _nodePredecessors.ContainsKey(target))
            {
                if (!_nodeSuccessors[source].Contains(target))
                {
                    _nodeSuccessors[source].Add(target);
                }

                if (!_nodePredecessors[target].Contains(source))
                {
                    _nodePredecessors[target].Add(source);
                    _inDegree[target]++;
                }
            }
        }

        _log.Debug(
            "📊 Execution graph built: {ConnectionCount} connections, start nodes: {StartNodes}",
            _definition.Connections.Count,
            string.Join(", ", GetStartNodes()));
    }

    /// <summary>
    /// Gets nodes with no incoming connections (start nodes).
    /// These are executed first when the workflow starts~ 🚀
    /// </summary>
    private IEnumerable<string> GetStartNodes()
    {
        return _inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key);
    }

    /// <summary>
    /// Gets the node definition by ID.
    /// </summary>
    private NodeDefinition? GetNodeDefinition(string nodeId)
    {
        return _definition.Nodes.Find(n => n.Id == nodeId).Match(
            Some: n => n,
            None: () => null);
    }

    /// <summary>
    /// Handles the StartExecution message.
    /// Initializes execution state and starts executing nodes with no dependencies~ 🎬
    /// </summary>
    private void HandleStartExecution(StartExecution message)
    {
        if (_state != ExecutionState.Pending)
        {
            _log.Warning(
                "⚠️ Cannot start execution {ExecutionId} - already in state {State}",
                _executionId,
                _state);
            return;
        }

        _log.Info("🚀 Starting workflow execution {ExecutionId}", _executionId);

        _state = ExecutionState.Running;
        _startTime = DateTimeOffset.UtcNow;
        _executionTimer.Start();

        // Find and execute start nodes (nodes with no dependencies)
        var startNodes = GetStartNodes().ToList();

        if (startNodes.Count == 0)
        {
            // Edge case: workflow with no nodes or all nodes have dependencies (cycle)
            _log.Warning("⚠️ No start nodes found in workflow {ExecutionId}", _executionId);
            CompleteWorkflow();
            return;
        }

        _log.Info(
            "✨ Executing {StartNodeCount} start nodes: {StartNodes}",
            startNodes.Count,
            string.Join(", ", startNodes));

        foreach (var nodeId in startNodes)
        {
            ExecuteNode(nodeId);
        }
    }

    /// <summary>
    /// Executes a single node by creating a NodeExecutor actor and sending Execute message.
    /// Gathers inputs from workflow inputs and predecessor outputs~ ⚡
    /// </summary>
    private void ExecuteNode(string nodeId)
    {
        var nodeDef = GetNodeDefinition(nodeId);
        if (nodeDef == null)
        {
            _log.Error("❌ Node definition not found for node {NodeId}", nodeId);
            HandleNodeFailure(nodeId, new InvalidOperationException($"Node {nodeId} not found in definition"));
            return;
        }

        _log.Info("⚡ Executing node {NodeId} ({NodeName})", nodeId, nodeDef.Name);

        // Gather inputs from workflow inputs and predecessor outputs
        var nodeInputs = GatherNodeInputs(nodeId);

        // Update state
        _nodeStates[nodeId] = NodeExecutionState.Running;
        _runningNodes.Add(nodeId);

        // Create NodeExecutor actor
        var executorName = $"node-{nodeId.Replace(".", "-")}";
        var nodeActor = Context.ActorOf(
            NodeExecutor.Props(nodeId, nodeDef, nodeInputs, _executionId, _serviceProvider),
            executorName);

        // Watch for termination
        Context.Watch(nodeActor);
        _nodeActors[nodeId] = nodeActor;

        // Send execute message
        nodeActor.Tell(new Execute(nodeId, nodeInputs, _executionId));
    }

    /// <summary>
    /// Gathers inputs for a node from workflow inputs and predecessor outputs.
    /// Follows connection mappings to route data correctly~ 📥
    /// </summary>
    private Dictionary<string, object?> GatherNodeInputs(string nodeId)
    {
        var inputs = new Dictionary<string, object?>();

        // Start with workflow-level inputs
        foreach (var (key, value) in _workflowInputs)
        {
            inputs[key] = value;
        }

        // Get outputs from predecessor nodes via connections
        var incomingConnections = _definition.Connections
            .Where(c => c.TargetNodeId == nodeId)
            .ToList();

        foreach (var conn in incomingConnections)
        {
            if (_nodeOutputs.TryGetValue(conn.SourceNodeId, out var sourceOutputs))
            {
                // Map source output port to target input port
                if (sourceOutputs.TryGetValue(conn.SourcePortName, out var outputValue))
                {
                    inputs[conn.TargetPortName] = outputValue;
                }

                // Also copy all outputs with a prefix for flexibility
                foreach (var (key, value) in sourceOutputs)
                {
                    inputs[$"{conn.SourceNodeId}.{key}"] = value;
                }
            }
        }

        return inputs;
    }

    /// <summary>
    /// Handles NodeExecutionCompleted message.
    /// Marks node as complete, stores outputs, and triggers successor nodes~ ✅
    /// </summary>
    private void HandleNodeExecutionCompleted(NodeExecutionCompleted message)
    {
        var nodeId = message.NodeId;

        _log.Info(
            "✅ Node {NodeId} completed in {Duration}ms",
            nodeId,
            message.Duration.TotalMilliseconds);

        // Update state
        _nodeStates[nodeId] = NodeExecutionState.Completed;
        _completedNodes.Add(nodeId);
        _runningNodes.Remove(nodeId);
        _nodeOutputs[nodeId] = message.Outputs;

        // Clean up actor reference
        if (_nodeActors.TryGetValue(nodeId, out var actor))
        {
            Context.Unwatch(actor);
            Context.Stop(actor);
            _nodeActors.Remove(nodeId);
        }

        // Check if workflow is complete
        if (IsWorkflowComplete())
        {
            CompleteWorkflow();
            return;
        }

        // Find and execute successor nodes whose dependencies are now satisfied
        ExecuteReadySuccessors(nodeId);
    }

    /// <summary>
    /// Checks which successor nodes are ready to execute and starts them.
    /// A node is ready when all its predecessors have completed~ 🔄
    /// </summary>
    private void ExecuteReadySuccessors(string completedNodeId)
    {
        if (!_nodeSuccessors.TryGetValue(completedNodeId, out var successors))
        {
            return;
        }

        foreach (var successorId in successors)
        {
            // Check if already running, completed, or failed
            if (_runningNodes.Contains(successorId) ||
                _completedNodes.Contains(successorId) ||
                _failedNodes.Contains(successorId))
            {
                continue;
            }

            // Check if all predecessors are complete
            var predecessors = _nodePredecessors[successorId];
            var allPredecessorsComplete = predecessors.All(p => _completedNodes.Contains(p));

            if (allPredecessorsComplete)
            {
                _log.Debug("🔗 Predecessor {CompletedNode} done, executing successor {SuccessorNode}",
                    completedNodeId, successorId);
                ExecuteNode(successorId);
            }
        }
    }

    /// <summary>
    /// Handles NodeExecutionFailed message.
    /// Implements error handling based on configuration (fail-fast, continue-on-error, retry)~ ⚠️
    /// </summary>
    private void HandleNodeExecutionFailed(NodeExecutionFailed message)
    {
        var nodeId = message.NodeId;

        _log.Error(
            message.Error,
            "❌ Node {NodeId} failed after {Duration}ms: {Error}",
            nodeId,
            message.Duration.TotalMilliseconds,
            message.Error.Message);

        HandleNodeFailure(nodeId, message.Error);
    }

    /// <summary>
    /// Handles node failure with appropriate error handling strategy.
    /// </summary>
    private void HandleNodeFailure(string nodeId, Exception error)
    {
        _nodeStates[nodeId] = NodeExecutionState.Failed;
        _failedNodes.Add(nodeId);
        _runningNodes.Remove(nodeId);
        _lastError = error;

        // Clean up actor reference
        if (_nodeActors.TryGetValue(nodeId, out var actor))
        {
            Context.Unwatch(actor);
            Context.Stop(actor);
            _nodeActors.Remove(nodeId);
        }

        // Get error handling configuration
        var nodeDef = GetNodeDefinition(nodeId);
        var errorHandling = nodeDef?.ErrorHandling ?? _definition.ErrorHandling;
        var behavior = errorHandling?.OnErrorBehavior ?? ErrorBehavior.Fail;

        switch (behavior)
        {
            case ErrorBehavior.Continue:
                _log.Warning("⚡ Continue-on-error: proceeding despite failure in node {NodeId}", nodeId);
                // Treat as completed for successor execution purposes
                _completedNodes.Add(nodeId);
                if (IsWorkflowComplete())
                {
                    // Complete with partial success
                    CompleteWorkflow();
                }
                else
                {
                    ExecuteReadySuccessors(nodeId);
                }
                break;

            case ErrorBehavior.Fail:
            default:
                _log.Error("🛑 Fail-fast: stopping workflow execution due to failure in node {NodeId}", nodeId);
                FailWorkflow(error);
                break;
        }
    }

    /// <summary>
    /// Handles CancelExecution message.
    /// Stops all running node actors and marks workflow as cancelled~ 🛑
    /// </summary>
    private void HandleCancelExecution(CancelExecution message)
    {
        if (_state == ExecutionState.Completed ||
            _state == ExecutionState.Failed ||
            _state == ExecutionState.Cancelled)
        {
            _log.Warning("⚠️ Cannot cancel execution {ExecutionId} - already in terminal state {State}",
                _executionId, _state);
            return;
        }

        _log.Info("🛑 Cancelling workflow execution {ExecutionId}", _executionId);

        // Cancel all running nodes
        foreach (var (nodeId, actor) in _nodeActors.ToList())
        {
            _log.Debug("🛑 Cancelling node {NodeId}", nodeId);
            _nodeStates[nodeId] = NodeExecutionState.Cancelled;
            Context.Unwatch(actor);
            Context.Stop(actor);
        }

        _nodeActors.Clear();
        _runningNodes.Clear();

        _state = ExecutionState.Cancelled;
        _endTime = DateTimeOffset.UtcNow;
        _executionTimer.Stop();

        // Notify parent
        Context.Parent.Tell(new WorkflowFailed(
            _executionId,
            new OperationCanceledException("Workflow execution was cancelled"),
            _executionTimer.Elapsed,
            GatherWorkflowOutputs()));
    }

    /// <summary>
    /// Handles GetWorkflowStatus message.
    /// Returns current execution state and progress~ 📊
    /// </summary>
    private void HandleGetWorkflowStatus(GetWorkflowStatus message)
    {
        var response = new WorkflowStatusResponse(
            _executionId,
            _state,
            CalculateProgress(),
            new Dictionary<string, NodeExecutionState>(_nodeStates),
            _startTime,
            _endTime,
            _lastError?.Message);

        Sender.Tell(response);
    }

    /// <summary>
    /// Handles GetProgress message.
    /// Returns detailed progress information~ 📈
    /// </summary>
    private void HandleGetProgress(GetProgress message)
    {
        var currentNode = _runningNodes.FirstOrDefault();
        var progress = new ProgressUpdate(
            _executionId,
            CalculateProgress(),
            currentNode,
            _completedNodes.Count,
            _definition.Nodes.Count);

        Sender.Tell(progress);
    }

    /// <summary>
    /// Handles Terminated message for child NodeExecutor actors.
    /// Cleans up tracking if unexpected termination~ 💔
    /// </summary>
    private void HandleTerminated(Terminated message)
    {
        var terminatedNodeId = _nodeActors
            .FirstOrDefault(kv => kv.Value.Equals(message.ActorRef))
            .Key;

        if (terminatedNodeId != null)
        {
            _log.Warning("💀 NodeExecutor terminated unexpectedly for node {NodeId}", terminatedNodeId);
            _nodeActors.Remove(terminatedNodeId);

            // If the node wasn't already marked as completed/failed, mark it as failed
            if (_nodeStates.TryGetValue(terminatedNodeId, out var state) && state == NodeExecutionState.Running)
            {
                HandleNodeFailure(terminatedNodeId, new Exception("Node executor terminated unexpectedly"));
            }
        }
    }

    /// <summary>
    /// Checks if the workflow execution is complete.
    /// Complete when all nodes are either completed, failed (with continue-on-error), or skipped~ ✅
    /// </summary>
    private bool IsWorkflowComplete()
    {
        // All nodes must be in a terminal state (completed or failed with continue-on-error)
        return _runningNodes.Count == 0 &&
               _completedNodes.Count + _failedNodes.Count >= _definition.Nodes.Count;
    }

    /// <summary>
    /// Calculates completion percentage based on completed nodes.
    /// </summary>
    private int CalculateProgress()
    {
        if (_definition.Nodes.Count == 0)
        {
            return 100;
        }

        return (int)((_completedNodes.Count * 100.0) / _definition.Nodes.Count);
    }

    /// <summary>
    /// Gathers final workflow outputs from completed end nodes.
    /// End nodes are nodes with no successors~ 📤
    /// </summary>
    private Dictionary<string, object?> GatherWorkflowOutputs()
    {
        var outputs = new Dictionary<string, object?>();

        // Get outputs from end nodes (nodes with no successors)
        var endNodes = _nodeSuccessors
            .Where(kv => kv.Value.Count == 0)
            .Select(kv => kv.Key);

        foreach (var endNodeId in endNodes)
        {
            if (_nodeOutputs.TryGetValue(endNodeId, out var nodeOutputs))
            {
                foreach (var (key, value) in nodeOutputs)
                {
                    outputs[$"{endNodeId}.{key}"] = value;
                }
            }
        }

        return outputs;
    }

    /// <summary>
    /// Completes the workflow successfully.
    /// Gathers outputs and notifies parent~ 🎉
    /// </summary>
    private void CompleteWorkflow()
    {
        _state = ExecutionState.Completed;
        _endTime = DateTimeOffset.UtcNow;
        _executionTimer.Stop();

        var outputs = GatherWorkflowOutputs();

        _log.Info(
            "🎉 Workflow execution {ExecutionId} completed in {Duration}ms ({CompletedNodes}/{TotalNodes} nodes)",
            _executionId,
            _executionTimer.ElapsedMilliseconds,
            _completedNodes.Count,
            _definition.Nodes.Count);

        Context.Parent.Tell(new WorkflowCompleted(_executionId, outputs, _executionTimer.Elapsed));
    }

    /// <summary>
    /// Fails the workflow with an error.
    /// Stops all running nodes and notifies parent~ 💔
    /// </summary>
    private void FailWorkflow(Exception error)
    {
        _state = ExecutionState.Failed;
        _endTime = DateTimeOffset.UtcNow;
        _executionTimer.Stop();
        _lastError = error;

        // Stop all running nodes
        foreach (var (nodeId, actor) in _nodeActors.ToList())
        {
            _nodeStates[nodeId] = NodeExecutionState.Cancelled;
            Context.Unwatch(actor);
            Context.Stop(actor);
        }

        _nodeActors.Clear();
        _runningNodes.Clear();

        _log.Error(
            error,
            "💔 Workflow execution {ExecutionId} failed after {Duration}ms: {Error}",
            _executionId,
            _executionTimer.ElapsedMilliseconds,
            error.Message);

        Context.Parent.Tell(new WorkflowFailed(
            _executionId,
            error,
            _executionTimer.Elapsed,
            GatherWorkflowOutputs()));
    }

    /// <summary>
    /// Lifecycle hook - cleanup when actor stops.
    /// </summary>
    protected override void PostStop()
    {
        _executionTimer.Stop();
        _log.Debug("👋 WorkflowExecutor stopping for execution {ExecutionId}", _executionId);
        base.PostStop();
    }
}

