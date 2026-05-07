// <copyright file="SubGraphExecutor.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Engine.Actors;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Akka.Actor;
using Akka.Event;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Core.Models;
using Workflow.Engine.Messages;
using Workflow.Persistence.Abstractions;
using Workflow.Persistence.Models;

/// <summary>
/// Executes a scoped subset of a workflow's nodes as a contained unit~ ✨
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: Phase 2.2.0a — SubGraphExecutor is the primitive that loop modules (2.2.2)
/// and parallel modules (2.2.3) use to run iteration/branch bodies. It is intentionally
/// lean — no pause/resume/snapshot, no variable-store integration (those come in 2.2.0b+).
/// </para>
/// <para>
/// Lifecycle: construct → PreStart auto-runs entry nodes → node actors report back →
/// when all scope nodes complete/fail/skip → send SubGraphCompleted/SubGraphFailed to Context.Parent.
/// </para>
/// <para>
/// Port-aware routing is applied here identically to <see cref="WorkflowExecutor"/>:
/// if a NodeExecutionCompleted carries ActivePorts, only those branches fire~ .
/// </para>
/// <para>
/// The refactor to extract a shared <c>WorkflowDispatchCore</c> used by both this and
/// WorkflowExecutor is deferred to Phase 2.2.0b when loop scope adds enough complexity
/// to make the abstraction pay off. For now, core logic is duplicated and clearly marked. UwU .
/// </para>
/// </remarks>
public class SubGraphExecutor : ReceiveActor
{
    private readonly Guid _parentExecutionId;
    private readonly WorkflowDefinition _definition;
    private readonly System.Collections.Generic.HashSet<string> _scopeNodeIds;   // null = no restriction (all reachable)
    private readonly IReadOnlyList<string> _entryNodeIds;
    private readonly Dictionary<string, object?> _inputs;
    private readonly IServiceProvider _serviceProvider;
    private readonly string? _subGraphId;
    private readonly ILoggingAdapter _log;
    private readonly IExecutionHistoryRepository? _historyRepository;

    /// <summary>
    /// CopilotNote: Phase 2.2.0b hierarchical cancellation — linked to the parent executor's token.
    /// When the parent workflow completes/fails, the linked CTS fires and in-flight node modules
    /// observe their token and throw OperationCanceledException cooperatively~ 🔗🛑
    /// </summary>
    private readonly CancellationTokenSource _linkedCts;

    // ── Execution tracking (inlined: no WorkflowExecutionContext — lean on purpose) ─────────────
    private readonly Dictionary<string, IActorRef> _nodeActors = new();
    private readonly Dictionary<string, Dictionary<string, object?>> _nodeOutputs = new();
    private readonly Dictionary<string, Dictionary<string, object?>> _nodeInputs = new();
    private readonly System.Collections.Generic.HashSet<string> _completedNodes = new();
    private readonly System.Collections.Generic.HashSet<string> _failedNodes = new();
    private readonly System.Collections.Generic.HashSet<string> _runningNodes = new();
    private readonly System.Collections.Generic.HashSet<string> _skippedNodes = new();

    // ── Graph (built from scoped connections in PreStart) ────────────────────────────────────────
    private readonly Dictionary<string, List<string>> _nodeSuccessors = new();
    private readonly Dictionary<string, List<string>> _nodePredecessors = new();

    // ── Private persistence confirmation ────────────────────────────────────────────────────────
    private sealed record NodePersisted(string NodeId, bool Success);

    /// <summary>
    /// Initializes a new SubGraphExecutor.
    /// The actor auto-starts execution from <paramref name="entryNodeIds"/> in PreStart~ .
    /// </summary>
    /// <param name="parentExecutionId">Parent execution ID used for history record correlation. .</param>
    /// <param name="definition">The full workflow definition containing nodes/connections. .</param>
    /// <param name="scopeNodeIds">
    /// The node IDs in scope for this sub-graph. Only these nodes will be executed.
    /// Pass an empty collection to run all nodes reachable from <paramref name="entryNodeIds"/>.
    /// </param>
    /// <param name="entryNodeIds">Entry-point node IDs (first to be executed in this sub-graph). .</param>
    /// <param name="inputs">Inputs available to all nodes in this sub-graph. .</param>
    /// <param name="serviceProvider">DI service provider for module resolution and persistence. .</param>
    /// <param name="subGraphId">Optional identifier persisted on node records for query correlation. ️.</param>
    /// <param name="parentToken">
    /// Optional parent cancellation token (Phase 2.2.0b).
    /// When non-default, the sub-graph's linked CTS is chained to this token so the parent
    /// workflow executor can cancel all in-flight sub-graph nodes cooperatively~ 🔗🛑
    /// </param>
    public SubGraphExecutor(
        Guid parentExecutionId,
        WorkflowDefinition definition,
        IReadOnlyCollection<string> scopeNodeIds,
        IReadOnlyCollection<string> entryNodeIds,
        Dictionary<string, object?> inputs,
        IServiceProvider serviceProvider,
        string? subGraphId = null,
        CancellationToken parentToken = default)
    {
        _parentExecutionId = parentExecutionId;
        _definition = definition;
        _scopeNodeIds = scopeNodeIds.Count > 0 ? scopeNodeIds.ToHashSet() : new System.Collections.Generic.HashSet<string>();
        _entryNodeIds = entryNodeIds.ToList();
        _inputs = inputs;
        _serviceProvider = serviceProvider;
        _subGraphId = subGraphId;
        _log = Context.GetLogger();
        _historyRepository = serviceProvider.GetService(typeof(IExecutionHistoryRepository)) as IExecutionHistoryRepository;

        // Phase 2.2.0b: create a linked CTS so parent cancellation propagates here~ 🔗🛑
        _linkedCts = parentToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(parentToken)
            : new CancellationTokenSource();

        // Message handlers
        Receive<NodeExecutionCompleted>(HandleNodeCompleted);
        Receive<NodeExecutionFailed>(HandleNodeFailed);
        Receive<Terminated>(HandleTerminated);
        Receive<NodePersisted>(_ => { }); // fire-and-forget persistence acknowledgement

        // Phase 2.2.0b: cooperative cancel — cancels the linked CTS so in-flight nodes observe it~ 🛑
        Receive<CooperativeCancelSubGraph>(msg =>
        {
            _log.Info(
                "🛑 SubGraph {SubGraphId}: cooperative cancel requested (reason: {Reason})",
                _subGraphId, msg.Reason ?? "none");
            _linkedCts.Cancel();
        });
    }

    /// <summary>
    /// Creates Props for spawning a SubGraphExecutor actor~ .
    /// </summary>
    public static Props Props(
        Guid parentExecutionId,
        WorkflowDefinition definition,
        IReadOnlyCollection<string> scopeNodeIds,
        IReadOnlyCollection<string> entryNodeIds,
        Dictionary<string, object?> inputs,
        IServiceProvider serviceProvider,
        string? subGraphId = null,
        CancellationToken parentToken = default)
    {
        return Akka.Actor.Props.Create(() => new SubGraphExecutor(
            parentExecutionId, definition, scopeNodeIds, entryNodeIds,
            inputs, serviceProvider, subGraphId, parentToken));
    }

    #region Lifecycle

    /// <inheritdoc/>
    protected override void PreStart()
    {
        base.PreStart();
        BuildGraph();

        if (_nodeSuccessors.Count == 0)
        {
            // Empty sub-graph — complete immediately
            _log.Warning("⚠️ SubGraph {SubGraphId} has no scope nodes — completing immediately", _subGraphId);
            Context.Parent.Tell(new SubGraphCompleted(_subGraphId, new Dictionary<string, object?>()));
            Context.Stop(Self);
            return;
        }

        // Start entry nodes (or all zero-inDegree nodes within scope if not specified)
        var startNodes = _entryNodeIds.Count > 0
            ? _entryNodeIds.Where(id => _nodeSuccessors.ContainsKey(id))
            : _nodePredecessors.Where(kv => kv.Value.Count == 0).Select(kv => kv.Key);

        foreach (var nodeId in startNodes)
        {
            ExecuteNode(nodeId);
        }

        _log.Info(
            " SubGraph {SubGraphId} started under execution {ExecutionId} ({ScopeCount} node(s) in scope)",
            _subGraphId,
            _parentExecutionId,
            _nodeSuccessors.Count);
    }

    /// <inheritdoc/>
    protected override void PostStop()
    {
        // Phase 2.2.0b: dispose linked CTS to unregister from parent token — prevents leaks~ 🧹💖
        _linkedCts.Dispose();
        base.PostStop();
    }

    #endregion

    #region Graph Building

    /// <summary>
    /// Builds the sub-graph's adjacency lists from definition connections,
    /// restricted to nodes in <see cref="_scopeNodeIds"/>~ .
    /// </summary>
    private void BuildGraph()
    {
        // Determine which nodes are in scope
        var scopedNodes = _scopeNodeIds.Count > 0
            ? _definition.Nodes.Where(n => _scopeNodeIds.Contains(n.Id))
            : _definition.Nodes;

        foreach (var node in scopedNodes)
        {
            _nodeSuccessors[node.Id] = new List<string>();
            _nodePredecessors[node.Id] = new List<string>();
        }

        foreach (var conn in _definition.Connections)
        {
            // Only include connections where both ends are in scope
            if (!_nodeSuccessors.ContainsKey(conn.SourceNodeId) ||
                !_nodePredecessors.ContainsKey(conn.TargetNodeId))
            {
                continue;
            }

            if (!_nodeSuccessors[conn.SourceNodeId].Contains(conn.TargetNodeId))
                _nodeSuccessors[conn.SourceNodeId].Add(conn.TargetNodeId);

            if (!_nodePredecessors[conn.TargetNodeId].Contains(conn.SourceNodeId))
                _nodePredecessors[conn.TargetNodeId].Add(conn.SourceNodeId);
        }

        _log.Debug(
            " SubGraph {SubGraphId} graph built: {NodeCount} nodes, starting from: [{EntryNodes}]",
            _subGraphId,
            _nodeSuccessors.Count,
            string.Join(", ", _entryNodeIds));
    }

    #endregion

    #region Node Execution ⚡

    /// <summary>
    /// Executes a scoped node by spawning a <see cref="NodeExecutor"/> child actor~ ⚡.
    /// </summary>
    private void ExecuteNode(string nodeId)
    {
        var nodeDef = _definition.Nodes.Find(n => n.Id == nodeId).Match(
            Some: n => (NodeDefinition?)n,
            None: () => null);

        if (nodeDef == null)
        {
            _log.Error("❌ SubGraph {SubGraphId}: node definition not found for '{NodeId}'", _subGraphId, nodeId);
            ReportFailure(nodeId, new InvalidOperationException($"Node '{nodeId}' not found in sub-graph scope"));
            return;
        }

        _log.Debug("⚡ SubGraph {SubGraphId}: executing node {NodeId}", _subGraphId, nodeId);

        var nodeInputs = GatherNodeInputs(nodeId);
        _nodeInputs[nodeId] = new Dictionary<string, object?>(nodeInputs);
        _runningNodes.Add(nodeId);

        var actorName = $"sgnode-{nodeId.Replace(".", "-")}";
        var nodeActor = Context.ActorOf(
            NodeExecutor.Props(nodeId, nodeDef, nodeInputs, _parentExecutionId, _serviceProvider, _linkedCts.Token),
            actorName);

        Context.Watch(nodeActor);
        _nodeActors[nodeId] = nodeActor;

        nodeActor.Tell(new Execute(nodeId, nodeInputs.ToHashMap(), _parentExecutionId));
    }

    /// <summary>
    /// Gathers inputs for a node from workflow inputs and predecessor outputs,
    /// following connection port mappings~ .
    /// </summary>
    private Dictionary<string, object?> GatherNodeInputs(string nodeId)
    {
        var inputs = new Dictionary<string, object?>(_inputs);

        var incomingConnections = _definition.Connections
            .Where(c => c.TargetNodeId == nodeId && _nodeSuccessors.ContainsKey(c.SourceNodeId))
            .ToList();

        foreach (var conn in incomingConnections)
        {
            if (!_nodeOutputs.TryGetValue(conn.SourceNodeId, out var sourceOutputs)) continue;

            if (sourceOutputs.TryGetValue(conn.SourcePortName, out var outputValue))
            {
                inputs[conn.TargetPortName] = outputValue;
            }

            // Also copy all outputs with prefix for flexibility
            foreach (var (key, value) in sourceOutputs)
            {
                inputs[$"{conn.SourceNodeId}.{key}"] = value;
            }
        }

        return inputs;
    }

    #endregion

    #region Node Completion / Failure

    /// <summary>
    /// Handles a successful node completion, applies port-aware routing, checks for sub-graph completion~ ✅.
    /// </summary>
    private void HandleNodeCompleted(NodeExecutionCompleted message)
    {
        var nodeId = message.NodeId;
        _runningNodes.Remove(nodeId);
        _completedNodes.Add(nodeId);

        _nodeOutputs[nodeId] = message.Outputs
            .Select(kv => new KeyValuePair<string, object?>(kv.Key, kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        // Clean up actor ref
        if (_nodeActors.TryGetValue(nodeId, out var actor))
        {
            Context.Unwatch(actor);
            Context.Stop(actor);
            _nodeActors.Remove(nodeId);
        }

        _nodeInputs.Remove(nodeId);

        // Persist under the parent execution ID for history correlation (test 6)~
        QueuePersistNode(nodeId, message.Duration, NodeExecutionState.Completed);

        if (IsComplete())
        {
            ReportSuccess();
            return;
        }

        ExecuteReadySuccessors(nodeId, message.ActivePorts);
    }

    /// <summary>
    /// Handles a node failure — reports SubGraphFailed to parent immediately (failFast)~ ❌.
    /// </summary>
    private void HandleNodeFailed(NodeExecutionFailed message)
    {
        var nodeId = message.NodeId;
        _runningNodes.Remove(nodeId);
        _failedNodes.Add(nodeId);

        if (_nodeActors.TryGetValue(nodeId, out var actor))
        {
            Context.Unwatch(actor);
            Context.Stop(actor);
            _nodeActors.Remove(nodeId);
        }

        _log.Error(
            message.Error,
            "❌ SubGraph {SubGraphId}: node '{NodeId}' failed: {Error}",
            _subGraphId,
            nodeId,
            message.Error.Message);

        QueuePersistNode(nodeId, message.Duration, NodeExecutionState.Failed, message.Error.Message);
        ReportFailure(nodeId, message.Error);
    }

    /// <summary>
    /// Handles unexpected actor termination for a node~ .
    /// </summary>
    private void HandleTerminated(Terminated terminated)
    {
        var terminatedNodeId = _nodeActors
            .FirstOrDefault(kv => kv.Value.Equals(terminated.ActorRef)).Key;

        if (terminatedNodeId != null && _runningNodes.Contains(terminatedNodeId))
        {
            _nodeActors.Remove(terminatedNodeId);
            ReportFailure(terminatedNodeId, new Exception($"SubGraph node '{terminatedNodeId}' terminated unexpectedly"));
        }
    }

    #endregion

    #region Port-Aware Routing

    /// <summary>
    /// Port-aware successor routing — mirrors <see cref="WorkflowExecutor.ExecuteReadySuccessors"/> logic.
    /// CopilotNote: Duplicated intentionally for 2.2.0a; shared dispatch core comes in 2.2.0b~ .
    /// </summary>
    private void ExecuteReadySuccessors(string completedNodeId, Arr<string> activePorts = default)
    {
        if (!_nodeSuccessors.TryGetValue(completedNodeId, out var successors) || successors.Count == 0)
        {
            return;
        }

        if (activePorts.Count == 0)
        {
            foreach (var successorId in successors)
                TryFireSuccessor(successorId);

            return;
        }

        var activatedTargets = _definition.Connections
            .Where(c => c.SourceNodeId == completedNodeId && activePorts.Contains(c.SourcePortName))
            .Select(c => c.TargetNodeId)
            .ToHashSet();

        var deactivatedTargets = _definition.Connections
            .Where(c => c.SourceNodeId == completedNodeId && !activePorts.Contains(c.SourcePortName))
            .Select(c => c.TargetNodeId)
            .Where(t => !activatedTargets.Contains(t))
            .ToHashSet();

        foreach (var targetId in activatedTargets)
            TryFireSuccessor(targetId);

        foreach (var targetId in deactivatedTargets)
            TrySkipNodeDownstream(targetId);

        if (IsComplete())
            ReportSuccess();
    }

    private void TryFireSuccessor(string successorId)
    {
        if (_runningNodes.Contains(successorId) ||
            _completedNodes.Contains(successorId) ||
            _failedNodes.Contains(successorId) ||
            _skippedNodes.Contains(successorId))
        {
            return;
        }

        var preds = _nodePredecessors.GetValueOrDefault(successorId, new List<string>());
        if (preds.All(p => _completedNodes.Contains(p) || _skippedNodes.Contains(p)))
        {
            ExecuteNode(successorId);
        }
    }

    private void TrySkipNodeDownstream(string nodeId)
    {
        if (_completedNodes.Contains(nodeId) || _failedNodes.Contains(nodeId) ||
            _runningNodes.Contains(nodeId) || _skippedNodes.Contains(nodeId))
        {
            return;
        }

        var preds = _nodePredecessors.GetValueOrDefault(nodeId, new List<string>());
        if (!preds.All(p => _completedNodes.Contains(p) || _failedNodes.Contains(p) || _skippedNodes.Contains(p)))
            return;

        _skippedNodes.Add(nodeId);
        _log.Debug("⏭️ SubGraph {SubGraphId}: skipping node {NodeId}", _subGraphId, nodeId);

        if (_nodeSuccessors.TryGetValue(nodeId, out var ownSuccessors))
        {
            foreach (var successorId in ownSuccessors)
                TrySkipNodeDownstream(successorId);
        }
    }

    #endregion

    #region Completion

    private bool IsComplete()
    {
        return _runningNodes.Count == 0 &&
               _completedNodes.Count + _failedNodes.Count + _skippedNodes.Count >= _nodeSuccessors.Count;
    }

    private void ReportSuccess()
    {
        // Aggregate outputs from all terminal nodes (nodes with no in-scope successors)
        var outputs = new Dictionary<string, object?>();
        var terminalNodes = _nodeSuccessors.Where(kv => kv.Value.Count == 0).Select(kv => kv.Key);

        foreach (var termNodeId in terminalNodes)
        {
            if (_nodeOutputs.TryGetValue(termNodeId, out var termOutputs))
            {
                foreach (var (key, value) in termOutputs)
                {
                    outputs[$"{termNodeId}.{key}"] = value;
                }
            }
        }

        _log.Info(
            " SubGraph {SubGraphId} completed ({CompletedCount} completed, {SkippedCount} skipped)",
            _subGraphId,
            _completedNodes.Count,
            _skippedNodes.Count);

        Context.Parent.Tell(new SubGraphCompleted(_subGraphId, outputs));
        Context.Stop(Self);
    }

    private void ReportFailure(string? failedNodeId, Exception error)
    {
        // Stop all remaining running node actors
        foreach (var (_, actor) in _nodeActors.ToList())
        {
            Context.Unwatch(actor);
            Context.Stop(actor);
        }

        _nodeActors.Clear();

        _log.Error(
            error,
            " SubGraph {SubGraphId} failed at node '{FailedNodeId}': {Error}",
            _subGraphId,
            failedNodeId,
            error.Message);

        Context.Parent.Tell(new SubGraphFailed(_subGraphId, error, failedNodeId));
        Context.Stop(Self);
    }

    #endregion

    #region Persistence

    /// <summary>
    /// Persists a node execution record under the parent execution ID for history correlation (test 6)~ .
    /// </summary>
    private void QueuePersistNode(string nodeId, TimeSpan duration, NodeExecutionState state, string? error = null)
    {
        if (_historyRepository == null) return;

        var inputs = _nodeInputs.TryGetValue(nodeId, out var captured)
            ? new Dictionary<string, object?>(captured)
            : null;

        var outputs = _nodeOutputs.TryGetValue(nodeId, out var outCaptured)
            ? new Dictionary<string, object?>(outCaptured)
            : null;

        var now = DateTimeOffset.UtcNow;
        var record = new NodeExecutionRecord(
            ExecutionId: _parentExecutionId,
            NodeId: nodeId,
            State: state,
            StartedAt: now.Add(-duration),
            CompletedAt: now,
            Inputs: inputs,
            Outputs: outputs,
            Error: error,
            Duration: duration);

        Task.Run(async () =>
        {
            try
            {
                await _historyRepository.RecordNodeExecutionAsync(record).ConfigureAwait(false);
                return new NodePersisted(nodeId, true);
            }
            catch
            {
                return new NodePersisted(nodeId, false);
            }
        }).PipeTo(Self);
    }

    #endregion
}



