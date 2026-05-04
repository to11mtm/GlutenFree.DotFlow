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
using Akka.Pattern;
using LanguageExt;
using Workflow.Core.Models;
using Workflow.Engine.Messages;
using Workflow.Engine.Models;
using Workflow.Engine.Services;
using Workflow.Modules.Abstractions;
using Workflow.Persistence.Abstractions;
using Workflow.Persistence.Models;

/// <summary>
/// Actor responsible for executing a single workflow instance.
/// Orchestrates the execution graph, manages node actors, and tracks completion. 🎬✨.
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
/// The execution follows a topological order based on node dependencies~ 💖.
/// </para>
/// <para>
/// 📊 State tracking: All execution state lives in the immutable
/// <see cref="WorkflowExecutionContext"/>. Every transition publishes
/// <see cref="ExecutionStateChanged"/> and <see cref="NodeStateChanged"/>
/// events to parent + EventStream for observability. Snapshots are saved
/// on terminal states via <see cref="IExecutionStateStore"/>. UwU~ ✨.
/// </para>
/// </remarks>
public class WorkflowExecutor : ReceiveActor
{
    private readonly ILoggingAdapter _log;
    private readonly Guid _executionId;
    private readonly WorkflowDefinition _definition;
    private readonly Dictionary<string, object?> _workflowInputs;
    private readonly IServiceProvider _serviceProvider;
    private readonly string _triggeredBy;
    private readonly VariableWriteMode _variableWriteMode;

    /// <summary>
    /// The canonical execution state — immutable, snapshotable, and sugoi~ 📊✨
    /// Every mutation creates a new instance via <c>With*</c> helpers.
    /// </summary>
    private WorkflowExecutionContext _context;

    // Execution timing (actor-local, not persisted)
    private readonly Stopwatch _executionTimer = new();

    // Node actor tracking (actor-local runtime refs, not persisted)
    private readonly Dictionary<string, IActorRef> _nodeActors = new();
    private readonly Dictionary<string, Dictionary<string, object?>> _nodeOutputs = new();
    private readonly Dictionary<string, Dictionary<string, object?>> _nodeInputs = new();
    private LanguageExt.HashSet<string> _completedNodes;
    private LanguageExt.HashSet<string> _failedNodes;
    private LanguageExt.HashSet<string> _runningNodes;

    /// <summary>
    /// Nodes skipped by port-aware routing — downstream of an unactivated port.
    /// Counted as terminal for <see cref="IsWorkflowComplete"/> purposes~ ⏭️.
    /// CopilotNote: Phase 2.2.0a — populated by <see cref="TrySkipNodeDownstream"/>.
    /// </summary>
    private LanguageExt.HashSet<string> _skippedNodes;

    // Retry tracking — how many attempts each node has used, and its last error~ 🔄
    private readonly Dictionary<string, int> _nodeRetryAttempts = new();
    private readonly Dictionary<string, Exception> _lastNodeErrors = new();

    /// <summary>
    /// Port name validation errors collected at graph-build time.
    /// Checked in <see cref="HandleStartExecution"/> to fail fast before any node runs~ 🛡️.
    /// CopilotNote: Phase 2.2.0a — populated by <see cref="ValidateConnectionPorts"/>.
    /// </summary>
    private readonly List<string> _portValidationErrors = new();

    /// <summary>
    /// Cancelable handles for pending scheduled retry messages.
    /// We track these so we can cancel them in PostStop to avoid memory leaks and
    /// orphaned scheduled messages (fixes AK1004 warning)~ 🧹✨.
    /// </summary>
    /// <remarks>
    /// CopilotNote: Each entry maps a nodeId → its ICancelable from ScheduleTellOnce.
    /// When the actor stops, we cancel all pending retries. When a retry fires, we
    /// remove the entry. Kawaii resource management! UwU 💖.
    /// </remarks>
    private readonly Dictionary<string, ICancelable> _pendingRetryCancellations = new();

    // Graph structure (built from connections, actor-local)
    private readonly Dictionary<string, List<string>> _nodeSuccessors = new();
    private readonly Dictionary<string, List<string>> _nodePredecessors = new();
    private readonly Dictionary<string, int> _inDegree = new();

    // State store for persistence snapshots (optional — resolved from DI)
    private readonly IExecutionStateStore? _stateStore;
    private readonly IExecutionHistoryRepository? _executionHistoryRepository;
    private readonly IVariableStore? _variableStore;

    /// <summary>
    /// Lifecycle hooks resolved from DI — allows consumers to inject custom behavior
    /// during PreStart, PostStop, PreRestart, and PostRestart~ 🌸💖.
    /// </summary>
    private readonly IActorLifecycleHooks _lifecycleHooks;

    // Pause flag — when true, no new nodes will be started~ ⏸️
    private bool _isPaused;

    /// <summary>
    /// Flag indicating whether the actor is in a restart cycle (PreRestart → PostStop → PostRestart).
    /// When true, PostStop skips full cleanup since PreRestart handles child preservation~ 🔄.
    /// </summary>
    /// <remarks>
    /// CopilotNote: This prevents PostStop from cancelling running nodes during a supervised
    /// restart. PreRestart sets this to true, PostStop checks it, and PostRestart resets it.
    /// Without this, we'd accidentally kill child actors we wanted to survive! UwU 💕.
    /// </remarks>
    private bool _isRestarting;
    private bool _executionRecordReady;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowExecutor"/> class.
    /// Sets up message handlers and builds the execution graph. UwU~.
    /// </summary>
    /// <param name="executionId">The unique execution ID.</param>
    /// <param name="definition">The workflow definition to execute.</param>
    /// <param name="inputs">Initial input values for the workflow.</param>
    /// <param name="serviceProvider">Service provider for dependency injection.</param>
    public WorkflowExecutor(
        Guid executionId,
        WorkflowDefinition definition,
        Dictionary<string, object?> inputs,
        IServiceProvider serviceProvider,
        ExecutionStartOptions? startOptions = null)
    {
        _log = Context.GetLogger();
        _executionId = executionId;
        _definition = definition;
        _workflowInputs = inputs;
        _serviceProvider = serviceProvider;
        _triggeredBy = string.IsNullOrWhiteSpace(startOptions?.CallerId) ? "system" : startOptions!.CallerId!;
        _variableWriteMode = startOptions?.VariableWriteMode ?? VariableWriteMode.Execution;

        // Try to resolve the state store from DI (optional)
        _stateStore = serviceProvider.GetService(typeof(IExecutionStateStore)) as IExecutionStateStore;
        _executionHistoryRepository = serviceProvider.GetService(typeof(IExecutionHistoryRepository)) as IExecutionHistoryRepository;
        _variableStore = serviceProvider.GetService(typeof(IVariableStore)) as IVariableStore;
        _lifecycleHooks = serviceProvider.GetService(typeof(IActorLifecycleHooks)) as IActorLifecycleHooks
            ?? NullActorLifecycleHooks.Instance;

        // Initialize the immutable execution context~ 🌱
        _context = WorkflowExecutionContext.Create(
            executionId: executionId,
            workflowId: definition.Id,
            workflowName: definition.Name,
            initialNodeIds: definition.Nodes.Select(n => n.Id),
            initialVariables: inputs.ToHashMap());

        // Build the execution graph from connections
        BuildExecutionGraph();

        // Set up message handlers
        Receive<StartExecution>(HandleStartExecution);
        Receive<NodeExecutionCompleted>(HandleNodeExecutionCompleted);
        Receive<NodeExecutionFailed>(HandleNodeExecutionFailed);
        Receive<GetWorkflowStatus>(HandleGetWorkflowStatus);
        Receive<GetProgress>(HandleGetProgress);
        Receive<CancelExecution>(HandleCancelExecution);
        Receive<PauseExecution>(HandlePauseExecution);
        Receive<ResumeExecution>(HandleResumeExecution);
        Receive<SaveExecutionSnapshot>(HandleSaveSnapshot);
        Receive<GetExecutionSnapshot>(HandleGetSnapshot);
        Receive<RetryNode>(HandleRetryNode);
        Receive<Terminated>(HandleTerminated);
        Receive<PersistenceExecutionCreated>(HandleExecutionRecordCreated);
        Receive<PersistenceNodeRecorded>(HandlePersistenceNodeRecorded);
        Receive<PersistenceExecutionStatusUpdated>(HandlePersistenceExecutionStatusUpdated);
        Receive<PersistenceSnapshotSaved>(HandlePersistenceSnapshotSaved);
        Receive<PersistenceVariableUpdatesSaved>(HandlePersistenceVariableUpdatesSaved);

        _log.Info(
            "🎬 WorkflowExecutor created for execution {ExecutionId}, workflow '{WorkflowName}' with {NodeCount} nodes",
            _executionId,
            _definition.Name,
            _definition.Nodes.Count);
    }

    /// <summary>
    /// Creates Props for spawning a WorkflowExecutor actor.
    /// Use this factory method for proper instantiation~ 💝.
    /// </summary>
    public static Props Props(
        Guid executionId,
        WorkflowDefinition definition,
        Dictionary<string, object?> inputs,
        IServiceProvider serviceProvider,
        ExecutionStartOptions? startOptions = null)
    {
        return Akka.Actor.Props.Create(
            () => new WorkflowExecutor(executionId, definition, inputs, serviceProvider, startOptions));
    }

    /// <summary>
    /// Configures the supervision strategy for child NodeExecutor actors.
    /// Provides resilient error handling with contextual directives per exception type~ 🛡️✨.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CopilotNote: NodeExecutor failures are handled here at the WorkflowExecutor level.
    /// Transient errors (timeout, I/O) → Restart with backoff.
    /// Critical errors (invalid args) → Stop (no point retrying).
    /// Unknown errors → Escalate to WorkflowSupervisor. UwU 💖.
    /// </para>
    /// </remarks>
    /// <returns>The supervision strategy for child actors.</returns>
    protected override SupervisorStrategy SupervisorStrategy()
    {
        return new OneForOneStrategy(
            maxNrOfRetries: 3,
            withinTimeRange: TimeSpan.FromMinutes(1),
            localOnlyDecider: ex =>
            {
                // Helper to publish supervision event for observability~ 📡
                void PublishSupervisionEvent(string directive)
                {
                    var supervisionEvent = new SupervisionEvent(
                        ActorPath: Sender.Path.ToString(),
                        ExceptionType: ex.GetType().Name,
                        ExceptionMessage: ex.Message,
                        Directive: directive,
                        Timestamp: DateTimeOffset.UtcNow,
                        ExecutionId: LanguageExt.Option<Guid>.Some(_executionId));
                    Context.System.EventStream.Publish(supervisionEvent);
                }

                switch (ex)
                {
                    // Transient failures — restart the node executor~ 🔄
                    case TimeoutException:
                    case System.IO.IOException:
                        _log.Warning(
                            "⚠️ Transient failure in node executor: {ErrorType}. Restarting~ UwU",
                            ex.GetType().Name);
                        PublishSupervisionEvent("Restart");
                        return Directive.Restart;

                    // Critical failures — stop the node executor, no point retrying~ 💔
                    case InvalidOperationException:
                    case ArgumentException:
                        _log.Error(
                            ex,
                            "❌ Critical failure in node executor: {ErrorType}. Stopping actor.",
                            ex.GetType().Name);
                        PublishSupervisionEvent("Stop");
                        return Directive.Stop;

                    // Unknown failures — escalate to supervisor~ 🔥
                    default:
                        _log.Error(
                            ex,
                            "🔥 Unknown failure in node executor: {ErrorType}. Escalating...",
                            ex.GetType().Name);
                        PublishSupervisionEvent("Escalate");
                        return Directive.Escalate;
                }
            });
    }

    #region Graph Building 🔗

    /// <summary>
    /// Builds the execution graph (successors, predecessors, in-degrees) from workflow connections.
    /// This enables topological execution order and dependency tracking~ 🔗.
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

        // Phase 2.2.0a: Validate connection SourcePortNames against module schema declarations~ 🛡️
        ValidateConnectionPorts();
    }

    /// <summary>
    /// Validates that every connection's SourcePortName is declared in the source module's output schema.
    /// Errors are stored in <see cref="_portValidationErrors"/> and surfaced at execution start time~ 🛡️✨.
    /// </summary>
    /// <remarks>
    /// CopilotNote: Only validates if <see cref="IModuleRegistry"/> is registered in DI.
    /// If the module isn't found in the registry, the connection is skipped (runtime will fail anyway).
    /// This prevents typo port names from being silently ignored — fail fast, fail loud! UwU 💖.
    /// </remarks>
    private void ValidateConnectionPorts()
    {
        var registry = _serviceProvider.GetService(typeof(IModuleRegistry)) as IModuleRegistry;
        if (registry == null)
        {
            _log.Debug("⚠️ No IModuleRegistry found — skipping port name validation at load time");
            return;
        }

        var nodeModuleMap = _definition.Nodes.ToDictionary(n => n.Id, n => n.ModuleId);

        foreach (var connection in _definition.Connections)
        {
            if (!nodeModuleMap.TryGetValue(connection.SourceNodeId, out var moduleId)) continue;

            var module = registry.GetModule(moduleId);
            if (module == null) continue; // Not registered — runtime execution will catch this

            var declaredOutputs = module.Schema.Outputs.Select(p => p.Name).ToHashSet();

            // Skip validation for modules with no declared outputs (dynamic-port modules like builtin.parallel)
            if (declaredOutputs.Count == 0) continue;

            if (!declaredOutputs.Contains(connection.SourcePortName))
            {
                _portValidationErrors.Add(
                    $"Connection from '{connection.SourceNodeId}' uses undeclared output port " +
                    $"'{connection.SourcePortName}' on module '{moduleId}'. " +
                    $"Declared ports: [{string.Join(", ", declaredOutputs)}]");
            }
        }
    }

    /// <summary>
    /// Gets nodes with no incoming connections (start nodes).
    /// These are executed first when the workflow starts~ 🚀.
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

    #endregion

    #region Execution Handlers 🎬

    /// <summary>
    /// Handles the StartExecution message.
    /// Initializes execution state and starts executing nodes with no dependencies~ 🎬.
    /// </summary>
    private void HandleStartExecution(StartExecution message)
    {
        if (_context.State != ExecutionState.Pending)
        {
            _log.Warning(
                "⚠️ Cannot start execution {ExecutionId} - already in state {State}",
                _executionId,
                _context.State);
            return;
        }

        // Phase 2.2.0a: Fail fast if port validation found errors at graph build time~ 🛡️
        if (_portValidationErrors.Count > 0)
        {
            var errorMsg = "Workflow port validation failed: " + string.Join("; ", _portValidationErrors);
            _log.Error("❌ {Error}", errorMsg);
            FailWorkflow(new InvalidOperationException(errorMsg));
            return;
        }

        _log.Info("🚀 Starting workflow execution {ExecutionId}", _executionId);

        var now = DateTimeOffset.UtcNow;

        // Transition state: Pending → Running
        TransitionExecutionState(ExecutionState.Running, now, "Execution started");
        _context = _context with { StartTime = Option<DateTimeOffset>.Some(now) };
        _executionTimer.Start();

        QueueCreateExecutionRecord(now);
    }

    private void HandleExecutionRecordCreated(PersistenceExecutionCreated message)
    {
        _executionRecordReady = message.Success;

        if (!message.Success)
        {
            _log.Warning(
                "⚠️ Failed to persist execution start record for {ExecutionId}: {Error}. Continuing with in-memory execution~",
                _executionId,
                message.Error ?? "unknown error");
        }

        StartInitialNodes();
    }

    private void StartInitialNodes()
    {
        if (_context.State != ExecutionState.Running)
        {
            return;
        }

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
    /// Gathers inputs from workflow inputs and predecessor outputs~ ⚡.
    /// </summary>
    private void ExecuteNode(string nodeId)
    {
        // If paused, don't start new nodes~ ⏸️
        if (_isPaused)
        {
            _log.Debug("⏸️ Execution paused — deferring node {NodeId}", nodeId);
            return;
        }

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
        _nodeInputs[nodeId] = new Dictionary<string, object?>(nodeInputs);

        // Update context: node → Running with timing~ 🔄
        var now = DateTimeOffset.UtcNow;
        TransitionNodeState(nodeId, NodeExecutionState.Running, now);
        _context = _context.WithNodeStarted(nodeId, now);
        _runningNodes = _runningNodes.Add(nodeId);

        // Create NodeExecutor actor
        var executorName = $"node-{nodeId.Replace(".", "-")}";
        var nodeActor = Context.ActorOf(
            NodeExecutor.Props(nodeId, nodeDef, nodeInputs, _executionId, _serviceProvider),
            executorName);

        // Watch for termination
        Context.Watch(nodeActor);
        _nodeActors[nodeId] = nodeActor;

        // Send execute message (convert Dictionary to HashMap for immutability)
        var inputsHashMap = nodeInputs.ToHashMap();
        nodeActor.Tell(new Execute(nodeId, inputsHashMap, _executionId, _context.Variables));
    }

    /// <summary>
    /// Gathers inputs for a node from workflow inputs and predecessor outputs.
    /// Follows connection mappings to route data correctly~ 📥.
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

        _log.Debug(
            "📥 Gathering inputs for node {NodeId}: {WorkflowInputCount} workflow inputs, {ConnectionCount} incoming connections",
            nodeId,
            _workflowInputs.Count,
            incomingConnections.Count);

        foreach (var conn in incomingConnections)
        {
            if (_nodeOutputs.TryGetValue(conn.SourceNodeId, out var sourceOutputs))
            {
                // Map source output port to target input port
                if (sourceOutputs.TryGetValue(conn.SourcePortName, out var outputValue))
                {
                    inputs[conn.TargetPortName] = outputValue;
                    _log.Debug(
                        "🔗 Data flow: {SourceNode}.{SourcePort} → {TargetNode}.{TargetPort}",
                        conn.SourceNodeId,
                        conn.SourcePortName,
                        nodeId,
                        conn.TargetPortName);
                }
                else
                {
                    _log.Warning(
                        "⚠️ Connection expects output '{SourcePort}' from node '{SourceNode}' but it was not produced",
                        conn.SourcePortName,
                        conn.SourceNodeId);
                }

                // Also copy all outputs with a prefix for flexibility
                foreach (var (key, value) in sourceOutputs)
                {
                    inputs[$"{conn.SourceNodeId}.{key}"] = value;
                }
            }
            else
            {
                _log.Warning(
                    "⚠️ No outputs available from predecessor node '{SourceNode}' for connection to '{TargetNode}'",
                    conn.SourceNodeId,
                    nodeId);
            }
        }

        _log.Debug(
            "📦 Node {NodeId} will receive {InputCount} total inputs: [{InputKeys}]",
            nodeId,
            inputs.Count,
            string.Join(", ", inputs.Keys.Take(10)) + (inputs.Count > 10 ? "..." : ""));

        return inputs;
    }

    #endregion

    #region Node Completion/Failure Handlers ✅❌

    /// <summary>
    /// Handles NodeExecutionCompleted message.
    /// Marks node as complete, stores outputs, applies variable updates, and triggers successor nodes~ ✅.
    /// </summary>
    private void HandleNodeExecutionCompleted(NodeExecutionCompleted message)
    {
        var nodeId = message.NodeId;

        _log.Info(
            "✅ Node {NodeId} completed in {Duration}ms",
            nodeId,
            message.Duration.TotalMilliseconds);

        var now = DateTimeOffset.UtcNow;

        // Update context: node → Completed with timing~ 🎉
        TransitionNodeState(nodeId, NodeExecutionState.Completed, now);
        _context = _context.WithNodeCompleted(nodeId, now, message.Duration);

        // Apply variable updates from the module result, if any~ 💾
        if (message.VariableUpdates is { Count: > 0 } varUpdates)
        {
            foreach (var (name, value) in varUpdates)
            {
                _log.Debug(
                    "💾 Variable update from node {NodeId}: {VarName} = {VarValue}",
                    nodeId,
                    name,
                    value);
                _context = _context.WithVariable(name, value);
            }

            QueuePersistVariableUpdates(nodeId, varUpdates);
        }

        _completedNodes = _completedNodes.Add(nodeId);
        _runningNodes = _runningNodes.Remove(nodeId);
        _nodeOutputs[nodeId] = new Dictionary<string, object?>(
            message.Outputs.Select(kv => new KeyValuePair<string, object?>(kv.Key, kv.Value)));

        var nodeStart = _context.NodeTimings.Find(nodeId)
            .Bind(t => t.StartTime)
            .IfNone(now.Add(-message.Duration));

        var nodeInputs = _nodeInputs.TryGetValue(nodeId, out var capturedInputs)
            ? new Dictionary<string, object?>(capturedInputs)
            : null;

        var nodeOutputs = message.Outputs
            .Select(kv => new KeyValuePair<string, object?>(kv.Key, kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        QueueRecordNodeExecution(new NodeExecutionRecord(
            ExecutionId: _executionId,
            NodeId: nodeId,
            State: NodeExecutionState.Completed,
            StartedAt: nodeStart,
            CompletedAt: now,
            Inputs: nodeInputs,
            Outputs: nodeOutputs,
            Duration: message.Duration));

        // Clean up actor reference
        if (_nodeActors.TryGetValue(nodeId, out var actor))
        {
            Context.Unwatch(actor);
            Context.Stop(actor);
            _nodeActors.Remove(nodeId);
        }

        _nodeInputs.Remove(nodeId);

        // Check if workflow is complete
        if (IsWorkflowComplete())
        {
            CompleteWorkflow();
            return;
        }

        // Find and execute successor nodes whose dependencies are now satisfied.
        // Pass activePorts for port-aware routing (Phase 2.2.0a)~ 🎯
        ExecuteReadySuccessors(nodeId, message.ActivePorts);
    }

    /// <summary>
    /// Checks which successor nodes are ready to execute and starts them.
    /// Phase 2.2.0a: Port-aware routing — when <paramref name="activePorts"/> is non-empty,
    /// only activates successors reachable via those ports; deactivated branches are marked Skipped~ 🎯✨.
    /// </summary>
    /// <param name="completedNodeId">The node that just completed.</param>
    /// <param name="activePorts">
    /// Which output ports to route through. Empty = fire all (legacy/default).
    /// CopilotNote: Backwards-compatible contract — modules that don't set ActivePorts
    /// (i.e. all Phase 1 modules) pass an empty Arr → all outgoing connections fire as before~ 💖.
    /// </param>
    private void ExecuteReadySuccessors(string completedNodeId, Arr<string> activePorts = default)
    {
        if (!_nodeSuccessors.TryGetValue(completedNodeId, out var successors) || successors.Count == 0)
        {
            return;
        }

        if (activePorts.Count == 0)
        {
            // Legacy / fire-all: unchanged behaviour — every successor whose predecessors are satisfied runs~ ✅
            foreach (var successorId in successors)
            {
                TryFireSuccessor(successorId);
            }

            return;
        }

        // Port-aware routing~ 🎯
        // Build the set of targets activated via the selected ports
        var activatedTargets = _definition.Connections
            .Where(c => c.SourceNodeId == completedNodeId && activePorts.Contains(c.SourcePortName))
            .Select(c => c.TargetNodeId)
            .ToHashSet();

        // Build the set of targets on deactivated ports (NOT in activatedTargets)
        var deactivatedTargets = _definition.Connections
            .Where(c => c.SourceNodeId == completedNodeId && !activePorts.Contains(c.SourcePortName))
            .Select(c => c.TargetNodeId)
            .Where(t => !activatedTargets.Contains(t)) // not also targeted via an active port
            .ToHashSet();

        // Fire activated branches
        foreach (var targetId in activatedTargets)
        {
            TryFireSuccessor(targetId);
        }

        // Skip deactivated branches (propagates recursively downstream)~ ⏭️
        foreach (var targetId in deactivatedTargets)
        {
            TrySkipNodeDownstream(targetId);
        }

        // After propagating skips, check if the entire workflow is now complete~ 🎉
        if (IsWorkflowComplete())
        {
            CompleteWorkflow();
        }
    }

    /// <summary>
    /// Attempts to fire a successor node if all its predecessors are satisfied
    /// (completed, failed-with-continue, or skipped)~ ✅.
    /// </summary>
    /// <param name="successorId">The candidate successor node ID.</param>
    private void TryFireSuccessor(string successorId)
    {
        // Skip nodes already in a terminal or running state
        if (_runningNodes.Contains(successorId) ||
            _completedNodes.Contains(successorId) ||
            _failedNodes.Contains(successorId) ||
            _skippedNodes.Contains(successorId))
        {
            return;
        }

        // All predecessors must be in a "satisfied" state (complete, failed-continue, or skipped)
        var predecessors = _nodePredecessors[successorId];
        var allSatisfied = predecessors.All(p =>
            _completedNodes.Contains(p) || _skippedNodes.Contains(p));

        if (allSatisfied)
        {
            _log.Debug(
                "🔗 Firing successor {SuccessorNode} (all predecessors satisfied)",
                successorId);
            ExecuteNode(successorId);
        }
    }

    /// <summary>
    /// Marks a node as Skipped and recursively skips all downstream nodes whose only
    /// remaining path was through this node~ ⏭️✨.
    /// </summary>
    /// <remarks>
    /// CopilotNote: Called when a module sets ActivePorts that don't include the connection
    /// leading to this node. Only skips if ALL of the node's predecessors are now satisfied
    /// (complete/failed/skipped) — protects against skipping a node that has another active
    /// predecessor still pending. Recursion bottoms out at terminal nodes (no successors)~ 🌸.
    /// </remarks>
    private void TrySkipNodeDownstream(string nodeId)
    {
        // Don't skip a node already in any terminal or running state
        if (_completedNodes.Contains(nodeId) ||
            _failedNodes.Contains(nodeId) ||
            _runningNodes.Contains(nodeId) ||
            _skippedNodes.Contains(nodeId))
        {
            return;
        }

        // Only skip if ALL this node's predecessors are now satisfied (complete/failed/skipped).
        // If a predecessor is still pending/running, that path could still activate this node.
        var predecessors = _nodePredecessors.GetValueOrDefault(nodeId, new List<string>());
        var allSatisfied = predecessors.All(p =>
            _completedNodes.Contains(p) || _failedNodes.Contains(p) || _skippedNodes.Contains(p));

        if (!allSatisfied) return;

        _log.Debug("⏭️ Skipping node {NodeId} (all incoming paths deactivated by port routing)", nodeId);

        _skippedNodes = _skippedNodes.Add(nodeId);
        TransitionNodeState(nodeId, NodeExecutionState.Skipped, DateTimeOffset.UtcNow);

        // Propagate skip to all downstream successors~ 🌊
        if (_nodeSuccessors.TryGetValue(nodeId, out var ownSuccessors))
        {
            foreach (var successorId in ownSuccessors)
            {
                TrySkipNodeDownstream(successorId);
            }
        }
    }

    /// <summary>
    /// Handles NodeExecutionFailed message.
    /// Implements error handling based on configuration (fail-fast, continue-on-error, retry)~ ⚠️.
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
        var now = DateTimeOffset.UtcNow;

        // Update context: node → Failed~ 💔
        TransitionNodeState(nodeId, NodeExecutionState.Failed, now);
        _context = _context.WithNodeFailed(nodeId, now, _executionTimer.Elapsed);

        _failedNodes = _failedNodes.Add(nodeId);
        _runningNodes = _runningNodes.Remove(nodeId);

        var nodeStart = _context.NodeTimings.Find(nodeId)
            .Bind(t => t.StartTime)
            .IfNone(now);

        var nodeDuration = _context.NodeTimings.Find(nodeId)
            .Bind(t => t.Duration)
            .IfNone(_executionTimer.Elapsed);

        var nodeInputs = _nodeInputs.TryGetValue(nodeId, out var capturedInputs)
            ? new Dictionary<string, object?>(capturedInputs)
            : null;

        QueueRecordNodeExecution(new NodeExecutionRecord(
            ExecutionId: _executionId,
            NodeId: nodeId,
            State: NodeExecutionState.Failed,
            StartedAt: nodeStart,
            CompletedAt: now,
            Inputs: nodeInputs,
            Error: error.Message,
            Duration: nodeDuration));

        // Clean up actor reference
        if (_nodeActors.TryGetValue(nodeId, out var actor))
        {
            Context.Unwatch(actor);
            Context.Stop(actor);
            _nodeActors.Remove(nodeId);
        }

        _nodeInputs.Remove(nodeId);

        // Get error handling configuration
        var nodeDef = GetNodeDefinition(nodeId);
        var errorHandling = nodeDef?.ErrorHandling ?? _definition.ErrorHandling;
        var behavior = errorHandling?.OnErrorBehavior ?? ErrorBehavior.Fail;

        switch (behavior)
        {
            case ErrorBehavior.Retry:
                HandleRetryBehavior(nodeId, error, nodeDef, errorHandling);
                break;

            case ErrorBehavior.Continue:
                _log.Warning("⚡ Continue-on-error: proceeding despite failure in node {NodeId}", nodeId);

                // Treat as completed for successor execution purposes
                // Remove from failed set to avoid double-counting in IsWorkflowComplete~ 🔄
                _failedNodes = _failedNodes.Remove(nodeId);
                _completedNodes = _completedNodes.Add(nodeId);
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

            case ErrorBehavior.UseErrorHandler:
                _log.Warning(
                    "⚠️ UseErrorHandler behavior is not yet implemented — falling back to Fail for node {NodeId}. UwU",
                    nodeId);
                FailWorkflow(error);
                break;

            case ErrorBehavior.Fail:
            default:
                _log.Error("🛑 Fail-fast: stopping workflow execution due to failure in node {NodeId}", nodeId);
                FailWorkflow(error);
                break;
        }
    }

    /// <summary>
    /// Handles retry behavior when a node fails with <see cref="ErrorBehavior.Retry"/>.
    /// Computes exponential backoff delay and schedules a <see cref="RetryNode"/> message~ 🔄✨.
    /// </summary>
    /// <remarks>
    /// CopilotNote: The retry delay uses exponential backoff:
    /// <c>delay = baseDelay * (backoffMultiplier ^ attemptIndex)</c>, capped at <c>MaxDelayMs</c>.
    /// Scheduling is done via <c>Context.System.Scheduler.ScheduleTellOnce</c> to avoid blocking the actor! UwU 💖.
    /// </remarks>
    private void HandleRetryBehavior(string nodeId, Exception error, NodeDefinition? nodeDef, ErrorHandling? errorHandling)
    {
        var retryPolicy = nodeDef?.RetryPolicy ?? RetryPolicy.Default;
        var currentAttempt = _nodeRetryAttempts.GetValueOrDefault(nodeId, 0) + 1;

        _lastNodeErrors[nodeId] = error;

        if (currentAttempt >= retryPolicy.MaxAttempts)
        {
            _log.Error(
                "🛑 Node {NodeId} exhausted all {MaxAttempts} retry attempts. Failing workflow~ 💔",
                nodeId,
                retryPolicy.MaxAttempts);
            FailWorkflow(error);
            return;
        }

        // Compute exponential backoff delay: baseDelay * multiplier^(attempt-1), capped at MaxDelayMs~ 📈
        var attemptIndex = currentAttempt - 1;
        var delay = (int)Math.Min(
            retryPolicy.DelayMs * Math.Pow(retryPolicy.BackoffMultiplier, attemptIndex),
            retryPolicy.MaxDelayMs);

        _log.Warning(
            "🔄 Scheduling retry {Attempt}/{MaxAttempts} for node {NodeId} in {DelayMs}ms (backoff)~ UwU",
            currentAttempt,
            retryPolicy.MaxAttempts,
            nodeId,
            delay);

        // Update retry tracking
        _nodeRetryAttempts[nodeId] = currentAttempt;

        // Transition node state to Retrying~ 🔄
        TransitionNodeState(nodeId, NodeExecutionState.Retrying, DateTimeOffset.UtcNow);

        // Schedule the retry after backoff delay — non-blocking, idiomatic Akka.NET! ✨
        // We track the ICancelable so we can cancel it in PostStop (fixes AK1004)~ 🧹
        var retryMessage = new RetryNode(nodeId, _executionId, currentAttempt, retryPolicy.MaxAttempts);
        var cancellable = Context.System.Scheduler.ScheduleTellOnceCancelable(
            delay: TimeSpan.FromMilliseconds(delay),
            receiver: Self,
            message: retryMessage,
            sender: Self);
        _pendingRetryCancellations[nodeId] = cancellable;

        // Publish NodeRetrying event for observability~ 📡
        var retryingEvent = new NodeRetrying(nodeId, _executionId, currentAttempt, retryPolicy.MaxAttempts, error);
        Context.Parent.Tell(retryingEvent);
        Context.System.EventStream.Publish(retryingEvent);
    }

    #endregion

    #region Retry Handler 🔄

    /// <summary>
    /// Handles the <see cref="RetryNode"/> message.
    /// Re-creates the NodeExecutor child actor and re-sends Execute to retry the failed node~ 🔄✨.
    /// </summary>
    /// <remarks>
    /// CopilotNote: This handler is triggered by the scheduler after the backoff delay.
    /// We remove the old failed node from tracking, reset its state, and spin up a fresh
    /// NodeExecutor actor. The retry attempt is tracked for exponential backoff! UwU 💖.
    /// </remarks>
    private void HandleRetryNode(RetryNode message)
    {
        var nodeId = message.NodeId;

        _log.Info(
            "🔄 Retrying node {NodeId} — attempt {Attempt}/{MaxAttempts}~ UwU",
            nodeId,
            message.Attempt,
            message.MaxAttempts);

        // Clean up the pending cancellation handle — this retry has fired~ 🧹
        _pendingRetryCancellations.Remove(nodeId);

        // Remove from failed set so it can run again
        _failedNodes = _failedNodes.Remove(nodeId);

        // Clean up any leftover actor ref
        if (_nodeActors.TryGetValue(nodeId, out var oldActor))
        {
            Context.Unwatch(oldActor);
            Context.Stop(oldActor);
            _nodeActors.Remove(nodeId);
        }

        // Re-execute the node — this will create a fresh NodeExecutor actor~ 🚀
        ExecuteNode(nodeId);
    }

    #endregion

    #region Pause / Resume Handlers ⏸️▶️

    /// <summary>
    /// Handles PauseExecution message.
    /// Currently running nodes finish, but no new nodes start~ ⏸️.
    /// </summary>
    private void HandlePauseExecution(PauseExecution message)
    {
        if (_context.State != ExecutionState.Running)
        {
            _log.Warning(
                "⚠️ Cannot pause execution {ExecutionId} - not in Running state (current: {State})",
                _executionId,
                _context.State);
            return;
        }

        _log.Info("⏸️ Pausing workflow execution {ExecutionId}", _executionId);
        _isPaused = true;

        var now = DateTimeOffset.UtcNow;
        TransitionExecutionState(ExecutionState.Paused, now, "Paused by user");

        var counts = _context.GetNodeStateCounts();
        Context.Parent.Tell(new ExecutionPaused(_executionId, counts.Completed, counts.Pending));

        // Save snapshot on pause for resumability~ 💾
        SaveSnapshotAsync();
    }

    /// <summary>
    /// Handles ResumeExecution message.
    /// Resumes execution from where it was paused, starting eligible nodes~ ▶️.
    /// </summary>
    private void HandleResumeExecution(ResumeExecution message)
    {
        if (_context.State != ExecutionState.Paused)
        {
            _log.Warning(
                "⚠️ Cannot resume execution {ExecutionId} - not in Paused state (current: {State})",
                _executionId,
                _context.State);
            return;
        }

        _log.Info("▶️ Resuming workflow execution {ExecutionId}", _executionId);
        _isPaused = false;

        var now = DateTimeOffset.UtcNow;
        TransitionExecutionState(ExecutionState.Running, now, "Resumed by user");

        Context.Parent.Tell(new ExecutionResumed(_executionId));

        // Kick off any nodes that were deferred during pause~ 🚀
        foreach (var nodeId in _definition.Nodes.Select(n => n.Id))
        {
            if (_runningNodes.Contains(nodeId) ||
                _completedNodes.Contains(nodeId) ||
                _failedNodes.Contains(nodeId))
            {
                continue;
            }

            // Check if all predecessors are complete or skipped (Phase 2.2.0a: skipped counts as satisfied)
            if (_nodePredecessors.TryGetValue(nodeId, out var predecessors))
            {
                if (predecessors.All(p => _completedNodes.Contains(p) || _skippedNodes.Contains(p)))
                {
                    ExecuteNode(nodeId);
                }
            }
        }
    }

    #endregion

    #region Cancel Handler 🛑

    /// <summary>
    /// Handles CancelExecution message.
    /// Stops all running node actors and marks workflow as cancelled~ 🛑.
    /// </summary>
    private void HandleCancelExecution(CancelExecution message)
    {
        if (_context.State == ExecutionState.Completed ||
            _context.State == ExecutionState.Failed ||
            _context.State == ExecutionState.Cancelled)
        {
            _log.Warning(
                "⚠️ Cannot cancel execution {ExecutionId} - already in terminal state {State}",
                _executionId, _context.State);
            return;
        }

        _log.Info("🛑 Cancelling workflow execution {ExecutionId}", _executionId);

        var now = DateTimeOffset.UtcNow;

        // Cancel all running nodes
        foreach (var (nodeId, actor) in _nodeActors.ToList())
        {
            _log.Debug("🛑 Cancelling node {NodeId}", nodeId);
            TransitionNodeState(nodeId, NodeExecutionState.Cancelled, now);
            _context = _context.WithNodeCancelled(nodeId);
            Context.Unwatch(actor);
            Context.Stop(actor);
        }

        _nodeActors.Clear();
        _runningNodes = LanguageExt.HashSet<string>.Empty;

        TransitionExecutionState(ExecutionState.Cancelled, now, "Cancelled by user");
        _context = _context with { EndTime = Option<DateTimeOffset>.Some(now) };
        _executionTimer.Stop();
        QueueUpdateExecutionStatus(ExecutionState.Cancelled, now, "Cancelled by user");

        // Save final snapshot~ 💾
        SaveSnapshotAsync();

        // Notify parent
        var partialOutputs = GatherWorkflowOutputs();
        Context.Parent.Tell(new WorkflowFailed(
            _executionId,
            new OperationCanceledException("Workflow execution was cancelled"),
            _executionTimer.Elapsed,
            partialOutputs.Count > 0
                ? Option<HashMap<string, object?>>.Some(partialOutputs.ToHashMap())
                : Option<HashMap<string, object?>>.None));
    }

    #endregion

    #region Status & Progress Handlers 📊

    /// <summary>
    /// Handles GetWorkflowStatus message.
    /// Returns current execution state and progress~ 📊.
    /// </summary>
    private void HandleGetWorkflowStatus(GetWorkflowStatus message)
    {
        var response = new WorkflowStatusResponse(
            _executionId,
            _context.State,
            _context.CalculateProgress(),
            _context.NodeStates,
            _context.StartTime.IfNone(DateTimeOffset.MinValue),
            _context.EndTime,
            _context.Error);

        Sender.Tell(response);
    }

    /// <summary>
    /// Handles GetProgress message.
    /// Returns detailed progress information~ 📈.
    /// </summary>
    private void HandleGetProgress(GetProgress message)
    {
        var currentNode = _runningNodes.HeadOrNone();
        var progress = new ProgressUpdate(
            _executionId,
            _context.CalculateProgress(),
            currentNode,
            _completedNodes.Count,
            _definition.Nodes.Count);

        Sender.Tell(progress);
    }

    #endregion

    #region Snapshot Handlers 💾

    /// <summary>
    /// Handles SaveExecutionSnapshot message — persists current context~ 💾.
    /// </summary>
    private void HandleSaveSnapshot(SaveExecutionSnapshot message)
    {
        SaveSnapshotAsync();
        Sender.Tell(new ExecutionSnapshotSaved(_executionId, DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Handles GetExecutionSnapshot message — returns the current context~ 📋.
    /// </summary>
    private void HandleGetSnapshot(GetExecutionSnapshot message)
    {
        Sender.Tell(new ExecutionSnapshotResponse(
            _executionId,
            Option<WorkflowExecutionContext>.Some(_context)));
    }

    /// <summary>
    /// Saves the current execution context to the state store asynchronously~ 💾.
    /// </summary>
    private void SaveSnapshotAsync()
    {
        if (_stateStore == null && _executionHistoryRepository == null)
        {
            return;
        }

        var snapshot = _context;

        Task.Run(
            async () =>
            {
                var errors = new List<string>();

                if (_executionHistoryRepository != null
                    && _executionRecordReady
                    && snapshot.EndTime.IsSome)
                {
                    try
                    {
                        await _executionHistoryRepository.UpdateExecutionStatusAsync(
                            _executionId,
                            snapshot.State,
                            snapshot.EndTime.Match(Some: x => (DateTimeOffset?)x, None: () => null),
                            snapshot.Error.Match(Some: x => x, None: () => null)).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"execution history update failed: {ex.Message}");
                    }
                }

                if (_stateStore != null)
                {
                    try
                    {
                        await _stateStore.SaveSnapshotAsync(snapshot).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"snapshot store save failed: {ex.Message}");
                    }
                }

                return errors.Count == 0
                    ? new PersistenceSnapshotSaved(true)
                    : new PersistenceSnapshotSaved(false, string.Join(" | ", errors));
            }).PipeTo(Self);
    }

    #endregion

    #region Termination Handler 💀

    /// <summary>
    /// Handles Terminated message for child NodeExecutor actors.
    /// Cleans up tracking if unexpected termination~ 💔.
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
            var currentState = _context.NodeStates
                .Find(terminatedNodeId);

            currentState.IfSome(state =>
            {
                if (state == NodeExecutionState.Running)
                {
                    HandleNodeFailure(terminatedNodeId, new Exception("Node executor terminated unexpectedly"));
                }
            });
        }
    }

    #endregion

    #region Workflow Completion / Failure 🎉💔

    /// <summary>
    /// Checks if the workflow execution is complete.
    /// Phase 2.2.0a: Skipped nodes count as terminal (port-aware routing may skip entire branches)~ ⏭️.
    /// </summary>
    private bool IsWorkflowComplete()
    {
        // All nodes must be in a terminal state (completed, failed-continue, or skipped)
        return _runningNodes.Count == 0 &&
               _completedNodes.Count + _failedNodes.Count + _skippedNodes.Count >= _definition.Nodes.Count;
    }

    /// <summary>
    /// Gathers final workflow outputs from completed end nodes.
    /// End nodes are nodes with no successors~ 📤.
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
    /// Gathers outputs and notifies parent~ 🎉.
    /// </summary>
    private void CompleteWorkflow()
    {
        // Guard against double-completion (port-aware skip propagation may call this concurrently)~ 🛡️
        if (_context.State == ExecutionState.Completed) return;
        var now = DateTimeOffset.UtcNow;
        _executionTimer.Stop();

        var outputs = GatherWorkflowOutputs().ToHashMap();

        TransitionExecutionState(ExecutionState.Completed, now, "All nodes completed");
        _context = _context with
        {
            EndTime = Option<DateTimeOffset>.Some(now),
            Outputs = outputs,
        };

        _log.Info(
            "🎉 Workflow execution {ExecutionId} completed in {Duration}ms ({CompletedNodes}/{TotalNodes} nodes)",
            _executionId,
            _executionTimer.ElapsedMilliseconds,
            _completedNodes.Count,
            _definition.Nodes.Count);

        QueueUpdateExecutionStatus(ExecutionState.Completed, now);

        // Save final snapshot~ 💾
        SaveSnapshotAsync();

        Context.Parent.Tell(new WorkflowCompleted(_executionId, outputs, _executionTimer.Elapsed));
    }

    /// <summary>
    /// Fails the workflow with an error.
    /// Stops all running nodes and notifies parent~ 💔.
    /// </summary>
    private void FailWorkflow(Exception error)
    {
        var now = DateTimeOffset.UtcNow;
        _executionTimer.Stop();

        TransitionExecutionState(ExecutionState.Failed, now, $"Failed: {error.Message}");
        _context = _context with
        {
            EndTime = Option<DateTimeOffset>.Some(now),
            Error = Option<string>.Some(error.Message),
        };

        // Stop all running nodes
        foreach (var (nodeId, actor) in _nodeActors.ToList())
        {
            TransitionNodeState(nodeId, NodeExecutionState.Cancelled, now);
            _context = _context.WithNodeCancelled(nodeId);
            Context.Unwatch(actor);
            Context.Stop(actor);
        }

        _nodeActors.Clear();
        _runningNodes = LanguageExt.HashSet<string>.Empty;

        _log.Error(
            error,
            "💔 Workflow execution {ExecutionId} failed after {Duration}ms: {Error}",
            _executionId,
            _executionTimer.ElapsedMilliseconds,
            error.Message);

        QueueUpdateExecutionStatus(ExecutionState.Failed, now, error.Message);

        // Save final snapshot~ 💾
        SaveSnapshotAsync();

        var partialOutputs = GatherWorkflowOutputs();
        Context.Parent.Tell(new WorkflowFailed(
            _executionId,
            error,
            _executionTimer.Elapsed,
            partialOutputs.Count > 0
                ? Option<HashMap<string, object?>>.Some(partialOutputs.ToHashMap())
                : Option<HashMap<string, object?>>.None));
    }

    #endregion

    #region State Transition & Event Publishing 📡

    /// <summary>
    /// Transitions the workflow execution state, publishes change events, and
    /// updates the immutable context. The single source of truth for state changes~ 🔄✨.
    /// </summary>
    /// <param name="newState">The target state.</param>
    /// <param name="timestamp">When the transition occurred.</param>
    /// <param name="reason">Human-readable reason for the transition.</param>
    private void TransitionExecutionState(
        ExecutionState newState,
        DateTimeOffset timestamp,
        string? reason = null)
    {
        var oldState = _context.State;
        if (oldState == newState)
        {
            return; // No-op for same-state transitions
        }

        _context = _context.WithState(newState, timestamp, reason);

        // Publish state change event to parent and EventStream~ 📡
        var stateChangeEvent = new ExecutionStateChanged(_executionId, oldState, newState, timestamp);
        Context.Parent.Tell(stateChangeEvent);
        Context.System.EventStream.Publish(stateChangeEvent);

        _log.Info(
            "📊 Execution {ExecutionId} state: {OldState} → {NewState} ({Reason})",
            _executionId,
            oldState,
            newState,
            reason ?? "no reason");
    }

    /// <summary>
    /// Transitions a node's execution state and publishes a change event~ 🧩.
    /// </summary>
    /// <param name="nodeId">The node being transitioned.</param>
    /// <param name="newState">The target node state.</param>
    /// <param name="timestamp">When the transition occurred.</param>
    private void TransitionNodeState(
        string nodeId,
        NodeExecutionState newState,
        DateTimeOffset timestamp)
    {
        var oldState = _context.NodeStates
            .Find(nodeId)
            .IfNone(NodeExecutionState.Pending);

        if (oldState == newState)
        {
            return; // No-op for same-state transitions
        }

        _context = _context.WithNodeState(nodeId, newState);

        // Publish node state change event~ 📡
        var nodeStateChangeEvent = new NodeStateChanged(nodeId, _executionId, oldState, newState, timestamp);
        Context.Parent.Tell(nodeStateChangeEvent);
        Context.System.EventStream.Publish(nodeStateChangeEvent);

        _log.Debug(
            "🧩 Node {NodeId} state: {OldState} → {NewState}",
            nodeId,
            oldState,
            newState);
    }

    #endregion

    #region Lifecycle 🌸👋🔄

    /// <summary>
    /// Lifecycle hook called when the actor is starting for the first time.
    /// Validates dependencies and logs initialization details~ 🌸✨.
    /// </summary>
    /// <remarks>
    /// CopilotNote: PreStart is the earliest point where <c>Context</c> is available.
    /// We use it to verify the state store is reachable and log the execution graph
    /// summary. This runs BEFORE any message is processed! UwU 💖.
    /// </remarks>
    protected override void PreStart()
    {
        base.PreStart();

        _log.Info(
            "🌸 WorkflowExecutor initializing for execution {ExecutionId} (workflow: '{WorkflowName}', nodes: {NodeCount}, connections: {ConnectionCount})",
            _executionId,
            _definition.Name,
            _definition.Nodes.Count,
            _definition.Connections.Count);

        if (_stateStore != null)
        {
            _log.Debug("💾 State store available: {StoreType}", _stateStore.GetType().Name);
        }
        else
        {
            _log.Debug("⚠️ No state store registered — snapshots will be skipped");
        }

        if (_executionHistoryRepository != null)
        {
            _log.Debug("📊 Execution history repository available: {RepositoryType}", _executionHistoryRepository.GetType().Name);
        }
        else
        {
            _log.Debug("⚠️ No execution history repository registered — persistence integration disabled");
        }

        _lifecycleHooks.OnPreStart(CreateLifecycleContext());
    }

    /// <summary>
    /// Lifecycle hook called before the actor restarts due to a supervision directive.
    /// Preserves child NodeExecutor actors and saves a snapshot for recovery~ 🔄✨.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CopilotNote: We do NOT call <c>base.PreRestart</c> because that would stop
    /// all child NodeExecutor actors. Instead, we let running node actors survive
    /// the restart so we can re-track them in <see cref="PostRestart"/>. We also
    /// save a snapshot to allow state recovery. Kawaii resilience! 💕.
    /// </para>
    /// </remarks>
    /// <param name="reason">The exception that caused the restart.</param>
    /// <param name="message">The message being processed when the failure occurred.</param>
    protected override void PreRestart(Exception reason, object message)
    {
        // Mark that we're restarting so PostStop doesn't do full cleanup~ 🔄
        _isRestarting = true;

        _log.Warning(
            "🔄 WorkflowExecutor restarting for execution {ExecutionId} due to: {Error}. " +
            "Preserving {RunningCount} running node(s) and {ActorCount} actor ref(s)~ UwU",
            _executionId,
            reason.Message,
            _runningNodes.Count,
            _nodeActors.Count);

        _lifecycleHooks.OnPreRestart(CreateLifecycleContext(), reason, message);

        // Save a snapshot before restart so state can be recovered~ 💾
        SaveSnapshotAsync();

        // Do NOT call base.PreRestart — that would stop all child NodeExecutor actors!
        // We want them to survive the executor restart so work isn't lost~ 💖
        // Only call PostStop for our own cleanup (timer etc.)
        PostStop();
    }

    /// <summary>
    /// Lifecycle hook called after the actor restarts.
    /// Rebuilds node actor tracking from surviving children and restores state from snapshot~ 🌸✨.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CopilotNote: After restart, the constructor runs again (re-initializing fields to defaults),
    /// but child actors may still be alive! We iterate <c>Context.GetChildren()</c> to
    /// rebuild the <c>_nodeActors</c> dictionary, and restore execution sets from the
    /// context's <c>NodeStates</c>. Sugoi recovery pattern! 💕.
    /// </para>
    /// </remarks>
    /// <param name="reason">The exception that caused the restart.</param>
    protected override void PostRestart(Exception reason)
    {
        base.PostRestart(reason);

        // We're done restarting — clear the flag~ 🌸
        _isRestarting = false;

        _log.Info(
            "🌸 WorkflowExecutor restarted for execution {ExecutionId}. Rebuilding node tracking...",
            _executionId);

        // Rebuild _nodeActors from surviving children~ 🔄
        foreach (var child in Context.GetChildren())
        {
            var name = child.Path.Name;
            if (name.StartsWith("node-"))
            {
                // Extract nodeId from actor name (format: "node-{nodeId}" where dots were replaced with dashes)
                var nodeId = name.Substring("node-".Length).Replace("-", ".");
                _nodeActors[nodeId] = child;
                Context.Watch(child);
                _log.Debug("🔄 Re-tracked surviving node actor: {NodeId}", nodeId);
            }
        }

        // Rebuild running/completed/failed/skipped sets from the context's NodeStates~ 📊
        foreach (var (nodeId, state) in _context.NodeStates)
        {
            switch (state)
            {
                case NodeExecutionState.Running:
                case NodeExecutionState.Retrying:
                    _runningNodes = _runningNodes.Add(nodeId);
                    break;
                case NodeExecutionState.Completed:
                    _completedNodes = _completedNodes.Add(nodeId);
                    break;
                case NodeExecutionState.Failed:
                    _failedNodes = _failedNodes.Add(nodeId);
                    break;
                case NodeExecutionState.Skipped:
                    // Phase 2.2.0a: restore skipped nodes from snapshot~ ⏭️
                    _skippedNodes = _skippedNodes.Add(nodeId);
                    break;
            }
        }

        // Restart the execution timer if we're still running~ ⏱️
        if (_context.State == ExecutionState.Running && !_executionTimer.IsRunning)
        {
            _executionTimer.Start();
        }

        _log.Info(
            "✅ WorkflowExecutor recovery complete for execution {ExecutionId}. " +
            "Re-tracked {ActorCount} node actor(s), {RunningCount} running, " +
            "{CompletedCount} completed, {FailedCount} failed~ 🎉",
            _executionId,
            _nodeActors.Count,
            _runningNodes.Count,
            _completedNodes.Count,
            _failedNodes.Count);

        _lifecycleHooks.OnPostRestart(CreateLifecycleContext(), reason);
    }

    /// <summary>
    /// Lifecycle hook called when the actor is stopping.
    /// Cancels pending retry timers, cleans up running nodes (on true shutdown only),
    /// saves a final snapshot, and disposes resources~ 👋🧹.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CopilotNote: PostStop is called in two scenarios:
    /// 1. **True shutdown** — actor is being permanently stopped. We cancel all pending
    ///    retries, stop running node actors, and save a final snapshot.
    /// 2. **During restart** — called by PreRestart (we set <c>_isRestarting = true</c>).
    ///    We skip node cleanup since PreRestart preserves children for recovery.
    ///    Only timer + retry cancellation happens.
    /// </para>
    /// <para>
    /// The <c>_pendingRetryCancellations</c> are ALWAYS cancelled regardless of restart/stop,
    /// because scheduled messages targeting the old actor ref would be pointless. UwU 💖.
    /// </para>
    /// </remarks>
    protected override void PostStop()
    {
        _executionTimer.Stop();

        // 🧹 Cancel all pending scheduled retry messages to prevent memory leaks (AK1004 fix)
        if (_pendingRetryCancellations.Count > 0)
        {
            _log.Debug(
                "🧹 Cancelling {PendingRetryCount} pending retry timer(s) for execution {ExecutionId}",
                _pendingRetryCancellations.Count,
                _executionId);

            foreach (var (nodeId, cancellable) in _pendingRetryCancellations)
            {
                cancellable.Cancel();
                _log.Debug("🧹 Cancelled pending retry for node {NodeId}", nodeId);
            }

            _pendingRetryCancellations.Clear();
        }

        if (!_isRestarting)
        {
            // True shutdown — clean up running node actors~ 🛑
            if (_nodeActors.Count > 0)
            {
                _log.Info(
                    "🛑 True shutdown: stopping {ActorCount} remaining node actor(s) for execution {ExecutionId}",
                    _nodeActors.Count,
                    _executionId);

                foreach (var (nodeId, actor) in _nodeActors.ToList())
                {
                    Context.Unwatch(actor);
                    Context.Stop(actor);
                }

                _nodeActors.Clear();
                _runningNodes = LanguageExt.HashSet<string>.Empty;
            }

            // Save final snapshot if we're in a non-terminal state (unexpected stop)~ 💾
            if (_context.State == ExecutionState.Running ||
                _context.State == ExecutionState.Paused)
            {
                _log.Warning(
                    "⚠️ WorkflowExecutor stopping while still in {State} state for execution {ExecutionId}. Saving snapshot...",
                    _context.State,
                    _executionId);
                SaveSnapshotAsync();
            }
        }

        _log.Info(
            "👋 WorkflowExecutor stopping for execution {ExecutionId} (state: {State}, " +
            "completed: {CompletedCount}/{TotalNodes} nodes, restart: {IsRestarting})",
            _executionId,
            _context.State,
            _completedNodes.Count,
            _definition.Nodes.Count,
            _isRestarting);

        _lifecycleHooks.OnPostStop(CreateLifecycleContext());
        base.PostStop();
    }

    #endregion

    #region Lifecycle Helpers 🌸

    /// <summary>
    /// Creates an <see cref="ActorLifecycleContext"/> for passing to lifecycle hooks~ 🌸.
    /// </summary>
    /// <returns>A context describing this actor instance.</returns>
    private ActorLifecycleContext CreateLifecycleContext()
    {
        return new ActorLifecycleContext(
            ActorPath: Self.Path.ToString(),
            ActorType: nameof(WorkflowExecutor),
            Services: _serviceProvider);
    }

    private void QueueCreateExecutionRecord(DateTimeOffset startedAt)
    {
        if (_executionHistoryRepository == null)
        {
            Self.Tell(new PersistenceExecutionCreated(true));
            return;
        }

        var inputsCopy = new Dictionary<string, object?>(_workflowInputs);

        Task.Run(
            async () =>
            {
                try
                {
                    await _executionHistoryRepository.CreateExecutionAsync(new ExecutionRecord(
                        ExecutionId: _executionId,
                        WorkflowId: _definition.Id,
                        State: ExecutionState.Running,
                        StartedAt: startedAt,
                        Inputs: inputsCopy,
                        TriggeredBy: _triggeredBy)).ConfigureAwait(false);

                    return new PersistenceExecutionCreated(true);
                }
                catch (Exception ex)
                {
                    return new PersistenceExecutionCreated(false, ex.Message);
                }
            }).PipeTo(Self);
    }

    private void QueueRecordNodeExecution(NodeExecutionRecord nodeRecord)
    {
        if (_executionHistoryRepository == null || !_executionRecordReady)
        {
            return;
        }

        Task.Run(
            async () =>
            {
                try
                {
                    await _executionHistoryRepository.RecordNodeExecutionAsync(nodeRecord).ConfigureAwait(false);
                    return new PersistenceNodeRecorded(nodeRecord.NodeId, true);
                }
                catch (Exception ex)
                {
                    return new PersistenceNodeRecorded(nodeRecord.NodeId, false, ex.Message);
                }
            }).PipeTo(Self);
    }

    private void QueuePersistVariableUpdates(string nodeId, HashMap<string, object?> updates)
    {
        if (_variableStore == null)
        {
            return;
        }

        var updatesCopy = updates
            .Select(kv => new KeyValuePair<string, object?>(kv.Key, kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        Task.Run(
            async () =>
            {
                try
                {
                    foreach (var (name, value) in updatesCopy)
                    {
                        if (_variableWriteMode is VariableWriteMode.Execution or VariableWriteMode.Dual)
                        {
                            await _variableStore.SetVariableAsync(VariableScope.ForExecution(_executionId), name, value)
                                .ConfigureAwait(false);
                        }

                        if (_variableWriteMode is VariableWriteMode.Workflow or VariableWriteMode.Dual)
                        {
                            await _variableStore.SetVariableAsync(VariableScope.ForWorkflow(_definition.Id), name, value)
                                .ConfigureAwait(false);
                        }
                    }

                    return new PersistenceVariableUpdatesSaved(nodeId, true);
                }
                catch (Exception ex)
                {
                    return new PersistenceVariableUpdatesSaved(nodeId, false, ex.Message);
                }
            }).PipeTo(Self);
    }

    private void QueueUpdateExecutionStatus(ExecutionState state, DateTimeOffset? endTime = null, string? error = null)
    {
        if (_executionHistoryRepository == null || !_executionRecordReady)
        {
            return;
        }

        Task.Run(
            async () =>
            {
                try
                {
                    await _executionHistoryRepository.UpdateExecutionStatusAsync(
                        _executionId,
                        state,
                        endTime,
                        error).ConfigureAwait(false);

                    return new PersistenceExecutionStatusUpdated(state, true);
                }
                catch (Exception ex)
                {
                    return new PersistenceExecutionStatusUpdated(state, false, ex.Message);
                }
            }).PipeTo(Self);
    }

    private void HandlePersistenceNodeRecorded(PersistenceNodeRecorded message)
    {
        if (!message.Success)
        {
            _log.Warning(
                "⚠️ Failed to persist node execution record for node {NodeId} in execution {ExecutionId}: {Error}",
                message.NodeId,
                _executionId,
                message.Error ?? "unknown error");
        }
    }

    private void HandlePersistenceExecutionStatusUpdated(PersistenceExecutionStatusUpdated message)
    {
        if (!message.Success)
        {
            _log.Warning(
                "⚠️ Failed to persist execution status {State} for {ExecutionId}: {Error}",
                message.State,
                _executionId,
                message.Error ?? "unknown error");
        }
    }

    private void HandlePersistenceSnapshotSaved(PersistenceSnapshotSaved message)
    {
        if (message.Success)
        {
            _log.Debug("💾 Snapshot persisted for execution {ExecutionId}", _executionId);
        }
        else
        {
            _log.Warning(
                "⚠️ Snapshot persistence failed for execution {ExecutionId}: {Error}",
                _executionId,
                message.Error ?? "unknown error");
        }
    }

    private void HandlePersistenceVariableUpdatesSaved(PersistenceVariableUpdatesSaved message)
    {
        if (!message.Success)
        {
            _log.Warning(
                "⚠️ Failed to persist variable updates for node {NodeId} in execution {ExecutionId}: {Error}",
                message.NodeId,
                _executionId,
                message.Error ?? "unknown error");
        }
    }

    private sealed record PersistenceExecutionCreated(bool Success, string? Error = null);

    private sealed record PersistenceNodeRecorded(string NodeId, bool Success, string? Error = null);

    private sealed record PersistenceExecutionStatusUpdated(ExecutionState State, bool Success, string? Error = null);

    private sealed record PersistenceSnapshotSaved(bool Success, string? Error = null);

    private sealed record PersistenceVariableUpdatesSaved(string NodeId, bool Success, string? Error = null);

    #endregion
}
