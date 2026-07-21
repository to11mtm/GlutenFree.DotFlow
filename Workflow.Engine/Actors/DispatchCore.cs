// <copyright file="DispatchCore.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Engine.Actors;

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Event;
using LanguageExt;
using Workflow.Core.Models;

/// <summary>
/// Shared port-aware routing core for workflow node dispatch~ 🎯✨
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: Phase 2.2.3-followup — extracted from the duplicate routing logic
/// that existed in both <see cref="WorkflowExecutor"/> and <see cref="SubGraphExecutor"/>
/// since Phase 2.2.0a. Both actors now hold a <c>DispatchCore</c> instance wired to
/// their own state sets and side-effect callbacks. Behaviour is identical to before —
/// this is a pure refactor with no semantic changes~ 🧠💖.
/// </para>
/// <para>
/// Design notes:
/// <list type="bullet">
///   <item>All mutable state lives in the OWNING ACTOR's fields — DispatchCore never
///         owns or caches state. It reads via Func delegates and writes via Action delegates.</item>
///   <item>WorkflowExecutor uses immutable LanguageExt sets; SubGraphExecutor uses mutable
///         HashSets — both expose consistent <c>Func&lt;string, bool&gt;</c> queries,
///         so DispatchCore is collection-type-agnostic.</item>
///   <item>Side effects that differ between the two actors (e.g. <c>TransitionNodeState</c>
///         in WorkflowExecutor) are injected via the <paramref name="markNodeSkipped"/> and
///         <paramref name="checkCompletionAfterSkipPropagation"/> delegates.</item>
/// </list>
/// </para>
/// <para>
/// Thread safety: DispatchCore is NOT thread-safe. It is designed for single-threaded use
/// inside an Akka <see cref="Akka.Actor.ReceiveActor"/> — only one message is processed at
/// a time, so no locking is needed~ 🛡️.
/// </para>
/// </remarks>
public sealed class DispatchCore
{
    // ── Workflow graph (read-only, stable after construction) ────────────────────────────────────

    private readonly WorkflowDefinition _definition;
    private readonly IReadOnlyDictionary<string, List<string>> _nodeSuccessors;
    private readonly IReadOnlyDictionary<string, List<string>> _nodePredecessors;

    // ── State query delegates — injected by caller to avoid coupling to collection types ─────────

    private readonly Func<string, bool> _isRunning;
    private readonly Func<string, bool> _isCompleted;
    private readonly Func<string, bool> _isFailed;
    private readonly Func<string, bool> _isSkipped;

    // ── Action delegates — side-effects remain in the owning actor ───────────────────────────────

    /// <summary>The actor-specific node spawning action (creates a NodeExecutor child)~ ⚡</summary>
    private readonly Action<string> _executeNode;

    /// <summary>
    /// Called when DispatchCore decides a node should be skipped.
    /// The delegate is responsible for: adding the node to the skipped set,
    /// any required logging, and actor-specific side-effects (e.g. <c>TransitionNodeState</c>
    /// in WorkflowExecutor)~ ⏭️.
    /// </summary>
    private readonly Action<string> _markNodeSkipped;

    /// <summary>
    /// Called after skip propagation completes, giving the actor a chance to check
    /// for overall completion and trigger workflow/subgraph done callbacks~ 🎉.
    /// </summary>
    private readonly Action _checkCompletionAfterSkipPropagation;

    private readonly ILoggingAdapter _log;

    /// <summary>
    /// Initializes a new <see cref="DispatchCore"/> instance wired to an actor's state.
    /// </summary>
    /// <param name="definition">The workflow definition (provides connection graph).</param>
    /// <param name="nodeSuccessors">Adjacency list: node → outgoing neighbour IDs.</param>
    /// <param name="nodePredecessors">Adjacency list: node → incoming neighbour IDs.</param>
    /// <param name="isRunning">Query: is this node currently running?</param>
    /// <param name="isCompleted">Query: has this node completed successfully?</param>
    /// <param name="isFailed">Query: has this node failed?</param>
    /// <param name="isSkipped">Query: has this node been skipped by port routing?</param>
    /// <param name="executeNode">Action: spawn a NodeExecutor for the given node ID.</param>
    /// <param name="markNodeSkipped">
    /// Action: record that a node is being skipped (add to set + any actor-specific side-effects).
    /// </param>
    /// <param name="checkCompletionAfterSkipPropagation">
    /// Action: after all deactivated branches are propagated, check if the execution unit
    /// (workflow or sub-graph) is now fully complete and report accordingly.
    /// </param>
    /// <param name="log">Akka logger for routing debug messages.</param>
    public DispatchCore(
        WorkflowDefinition definition,
        IReadOnlyDictionary<string, List<string>> nodeSuccessors,
        IReadOnlyDictionary<string, List<string>> nodePredecessors,
        Func<string, bool> isRunning,
        Func<string, bool> isCompleted,
        Func<string, bool> isFailed,
        Func<string, bool> isSkipped,
        Action<string> executeNode,
        Action<string> markNodeSkipped,
        Action checkCompletionAfterSkipPropagation,
        ILoggingAdapter log)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _nodeSuccessors = nodeSuccessors ?? throw new ArgumentNullException(nameof(nodeSuccessors));
        _nodePredecessors = nodePredecessors ?? throw new ArgumentNullException(nameof(nodePredecessors));
        _isRunning = isRunning ?? throw new ArgumentNullException(nameof(isRunning));
        _isCompleted = isCompleted ?? throw new ArgumentNullException(nameof(isCompleted));
        _isFailed = isFailed ?? throw new ArgumentNullException(nameof(isFailed));
        _isSkipped = isSkipped ?? throw new ArgumentNullException(nameof(isSkipped));
        _executeNode = executeNode ?? throw new ArgumentNullException(nameof(executeNode));
        _markNodeSkipped = markNodeSkipped ?? throw new ArgumentNullException(nameof(markNodeSkipped));
        _checkCompletionAfterSkipPropagation = checkCompletionAfterSkipPropagation
            ?? throw new ArgumentNullException(nameof(checkCompletionAfterSkipPropagation));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>
    /// Checks which successor nodes are ready to execute after <paramref name="completedNodeId"/>
    /// finished, and either fires or skips them based on <paramref name="activePorts"/>~ 🎯✨.
    /// </summary>
    /// <param name="completedNodeId">The node that just completed.</param>
    /// <param name="activePorts">
    /// Which output ports to route through. Empty/default = fire all (backwards-compatible legacy path).
    /// Non-empty = only connections whose <c>SourcePortName</c> is in this set will activate;
    /// others propagate as skipped~ 💖.
    /// </param>
    /// <remarks>
    /// CopilotNote: The backwards-compatibility contract is critical — modules that don't set
    /// ActivePorts (all Phase 1 modules, plain passthrough modules) pass an empty Arr, so all
    /// outgoing connections fire exactly as before Phase 2.2.0a was written. This ensures zero
    /// regression for existing workflows~ 🌸.
    /// </remarks>
    public void ExecuteReadySuccessors(string completedNodeId, Arr<string> activePorts = default)
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
        // Build the set of targets activated via the selected ports.
        var activatedTargets = _definition.Connections
            .Where(c => c.SourceNodeId == completedNodeId && activePorts.Contains(c.SourcePortName))
            .Select(c => c.TargetNodeId)
            .ToHashSet();

        // Build the set of targets on deactivated ports (NOT in activatedTargets).
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

        // After propagating skips, give the actor a chance to check for completion~ 🎉
        _checkCompletionAfterSkipPropagation();
    }

    /// <summary>
    /// Attempts to fire a successor node if all its predecessors are satisfied
    /// (completed or skipped)~ ✅.
    /// </summary>
    /// <param name="successorId">The candidate successor node ID.</param>
    public void TryFireSuccessor(string successorId)
    {
        // Skip nodes already in a terminal or running state
        if (_isRunning(successorId) ||
            _isCompleted(successorId) ||
            _isFailed(successorId) ||
            _isSkipped(successorId))
        {
            return;
        }

        // All predecessors must be in a "satisfied" state (complete or skipped)
        var preds = _nodePredecessors.GetValueOrDefault(successorId, new List<string>());
        if (preds.All(p => _isCompleted(p) || _isSkipped(p)))
        {
            _log.Debug(
                "🔗 Firing successor {SuccessorNode} (all predecessors satisfied)",
                successorId);
            _executeNode(successorId);
        }
    }

    /// <summary>
    /// Marks a node as Skipped and recursively skips all downstream nodes whose only
    /// remaining path was through this node~ ⏭️✨.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CopilotNote: Called when a module sets ActivePorts that don't include the connection
    /// leading to this node. Only skips if ALL of the node's predecessors are now satisfied
    /// (complete/failed/skipped) — protects against skipping a node that has another active
    /// predecessor still pending. Recursion bottoms out at terminal nodes (no successors)~ 🌸.
    /// </para>
    /// <para>
    /// The actual "add to skipped set" side-effect is delegated to <c>_markNodeSkipped</c>
    /// so the owning actor can run its own bookkeeping (e.g. TransitionNodeState in
    /// WorkflowExecutor, or just a HashSet.Add in SubGraphExecutor)~ 💖.
    /// </para>
    /// </remarks>
    public void TrySkipNodeDownstream(string nodeId)
    {
        // Don't skip a node already in any terminal or running state
        if (_isCompleted(nodeId) ||
            _isFailed(nodeId) ||
            _isRunning(nodeId) ||
            _isSkipped(nodeId))
        {
            return;
        }

        // Only skip if ALL this node's predecessors are now satisfied (complete/failed/skipped).
        // If a predecessor is still pending/running, that path could still activate this node.
        var preds = _nodePredecessors.GetValueOrDefault(nodeId, new List<string>());
        var allSatisfied = preds.All(p =>
            _isCompleted(p) || _isFailed(p) || _isSkipped(p));

        if (!allSatisfied)
        {
            return;
        }

        // Delegate the actual state mutation + side-effects to the owning actor~ ⏭️
        _markNodeSkipped(nodeId);

        // Propagate skip to all downstream successors~ 🌊
        if (_nodeSuccessors.TryGetValue(nodeId, out var ownSuccessors))
        {
            foreach (var successorId in ownSuccessors)
            {
                TrySkipNodeDownstream(successorId);
            }
        }
    }
}

