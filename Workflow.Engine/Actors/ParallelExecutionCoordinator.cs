// <copyright file="ParallelExecutionCoordinator.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Engine.Actors;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Akka.Actor;
using Akka.Event;
using Workflow.Core.Models;
using Workflow.Engine.Messages;

/// <summary>
/// 🌐 Orchestrates concurrent execution of multiple parallel branches~
/// Spawned by <c>WorkflowExecutor</c> when a node completion carries a
/// <see cref="ParallelRequest"/>. Reports <see cref="ParallelCompleted"/> or
/// <see cref="ParallelFailed"/> to its parent (<c>WorkflowExecutor</c>) when done~ ✨💖
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: Phase 2.2.3a — this actor owns each branch:
/// <list type="number">
///   <item>For each branch port, spawns a <see cref="SubGraphExecutor"/> child (bounded by MaxDegreeOfParallelism).</item>
///   <item>On <see cref="SubGraphCompleted"/>: collects branch outputs, dequeues next pending branch (if any).</item>
///   <item>On <see cref="SubGraphFailed"/>: respects <c>FailFast</c> — cancels siblings on first failure.</item>
///   <item>On final completion: reports <see cref="ParallelCompleted"/> to parent.</item>
/// </list>
/// </para>
/// <para>
/// Bounded concurrency uses a counter+queue (no SemaphoreSlim — never block actor thread~).
/// FailFast cancels via a linked CTS so all in-flight SubGraphExecutors observe cooperative
/// cancellation per the 2.2.0b hierarchical token contract~ 🛑.
/// </para>
/// </remarks>
public sealed class ParallelExecutionCoordinator : ReceiveActor
{
    private readonly string _parallelNodeId;
    private readonly ParallelRequest _parallel;
    private readonly WorkflowDefinition _definition;
    private readonly Guid _executionId;
    private readonly IReadOnlyDictionary<string, object?> _initialContext;
    private readonly IServiceProvider _serviceProvider;
    private readonly CancellationTokenSource _linkedCts;
    private readonly ILoggingAdapter _log;

    // Per-branch state ─────────────────────────────────────────────────────────────
    private readonly Queue<BranchSpec> _pendingBranches = new();
    private readonly Dictionary<string, int> _actorNameToBranchIndex = new(StringComparer.Ordinal);
    private readonly Dictionary<int, IReadOnlyDictionary<string, object?>> _branchResults = new();
    private readonly HashSet<int> _failedBranches = new();
    private int _inFlightCount;
    private int _totalBranches;
    private bool _aborting;
    private Exception? _firstError;
    private string? _firstFailedNodeId;

    /// <summary>Initializes a new <see cref="ParallelExecutionCoordinator"/>.</summary>
    /// <param name="parallelNodeId">Node ID of the parallel module that requested fan-out. 🆔.</param>
    /// <param name="parallel">The parallel specification. 🌐.</param>
    /// <param name="definition">Full workflow definition (for sub-graph scoping). 📋.</param>
    /// <param name="executionId">Parent execution ID for history correlation. 🔑.</param>
    /// <param name="initialContext">Initial variable + input context to seed branches. 📦.</param>
    /// <param name="serviceProvider">DI container. 💉.</param>
    /// <param name="parentToken">Hierarchical cancellation token (2.2.0b)~ 🔗🛑.</param>
    public ParallelExecutionCoordinator(
        string parallelNodeId,
        ParallelRequest parallel,
        WorkflowDefinition definition,
        Guid executionId,
        IReadOnlyDictionary<string, object?> initialContext,
        IServiceProvider serviceProvider,
        CancellationToken parentToken = default)
    {
        _parallelNodeId = parallelNodeId;
        _parallel = parallel;
        _definition = definition;
        _executionId = executionId;
        _initialContext = initialContext;
        _serviceProvider = serviceProvider;
        _log = Context.GetLogger();

        _linkedCts = parentToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(parentToken)
            : new CancellationTokenSource();

        Receive<SubGraphCompleted>(HandleBranchCompleted);
        Receive<SubGraphFailed>(HandleBranchFailed);
        Receive<CooperativeCancelParallel>(_ =>
        {
            _log.Info("🛑 ParallelCoordinator '{ParallelNodeId}': cooperative cancel received", _parallelNodeId);
            _linkedCts.Cancel();
            _aborting = true;
        });
    }

    /// <summary>Creates Props for spawning a ParallelExecutionCoordinator~ ✨.</summary>
    public static Props Props(
        string parallelNodeId,
        ParallelRequest parallel,
        WorkflowDefinition definition,
        Guid executionId,
        IReadOnlyDictionary<string, object?> initialContext,
        IServiceProvider serviceProvider,
        CancellationToken parentToken = default)
    {
        return Akka.Actor.Props.Create(() => new ParallelExecutionCoordinator(
            parallelNodeId, parallel, definition,
            executionId, initialContext, serviceProvider, parentToken));
    }

    #region Lifecycle

    /// <inheritdoc/>
    protected override void PreStart()
    {
        base.PreStart();

        // Phase 2.2.3b: per-item fan-out mode (FanOutModule)~ 🌟
        // When Items is non-null, fan out one branch per item, all routed through BranchPort.
        if (_parallel.Items is not null)
        {
            BuildPerItemBranches();
        }
        else
        {
            BuildStaticBranches();
        }

        _log.Info(
            "🌐 ParallelCoordinator '{ParallelNodeId}' starting under execution {ExecutionId} " +
            "(branches={BranchCount}, maxDoP={MaxDoP}, failFast={FailFast}, mode={Mode})",
            _parallelNodeId, _executionId,
            _totalBranches, _parallel.MaxDegreeOfParallelism, _parallel.FailFast,
            _parallel.Items is not null ? "per-item" : "static");

        // If no branches at all (all empty / no ports) — complete immediately.
        if (_pendingBranches.Count == 0 && _branchResults.Count == _totalBranches)
        {
            ReportCompleted();
            return;
        }

        // Fan out up to MaxDegreeOfParallelism branches initially.
        PumpBranches();
    }

    /// <summary>Static fan-out (2.2.3a): one branch per declared <see cref="ParallelRequest.BranchPorts"/> entry~ 🌐.</summary>
    private void BuildStaticBranches()
    {
        var branchIndex = 0;
        foreach (var portName in _parallel.BranchPorts)
        {
            var entryNodeIds = _definition.Connections
                .Where(c => c.SourceNodeId == _parallelNodeId && c.SourcePortName == portName)
                .Select(c => c.TargetNodeId)
                .ToList();

            if (entryNodeIds.Count == 0)
            {
                _log.Warning(
                    "⚠️ ParallelCoordinator '{ParallelNodeId}': branch port '{Port}' has no connections — " +
                    "treating as immediately completed empty branch~ 🌐",
                    _parallelNodeId, portName);
                _branchResults[branchIndex] = new Dictionary<string, object?>();
                branchIndex++;
                continue;
            }

            var scopeIds = ComputeBranchScope(_definition, _parallelNodeId, entryNodeIds);
            _pendingBranches.Enqueue(new BranchSpec(branchIndex, portName, entryNodeIds, scopeIds, item: null, itemIndex: branchIndex));
            branchIndex++;
        }

        _totalBranches = branchIndex;
    }

    /// <summary>
    /// Per-item fan-out (2.2.3b — FanOutModule): one branch per item in <see cref="ParallelRequest.Items"/>,
    /// all routed through <see cref="ParallelRequest.BranchPort"/>~ 🌟
    /// </summary>
    private void BuildPerItemBranches()
    {
        var port = _parallel.BranchPort;
        var entryNodeIds = _definition.Connections
            .Where(c => c.SourceNodeId == _parallelNodeId && c.SourcePortName == port)
            .Select(c => c.TargetNodeId)
            .ToList();

        if (entryNodeIds.Count == 0)
        {
            _log.Warning(
                "⚠️ ParallelCoordinator '{ParallelNodeId}': fan-out port '{Port}' has no connections — " +
                "completing per-item fan-out immediately with 0 work~ 🌟",
                _parallelNodeId, port);
            _totalBranches = 0;
            return;
        }

        var scopeIds = ComputeBranchScope(_definition, _parallelNodeId, entryNodeIds);

        var items = _parallel.Items!;
        for (var i = 0; i < items.Count; i++)
        {
            _pendingBranches.Enqueue(new BranchSpec(
                Index: i, PortName: port, EntryNodeIds: entryNodeIds, ScopeIds: scopeIds,
                item: items[i], itemIndex: i));
        }

        _totalBranches = items.Count;
    }

    /// <inheritdoc/>
    protected override void PostStop()
    {
        _linkedCts.Dispose();
        base.PostStop();
    }

    #endregion

    #region Branch dispatch

    /// <summary>
    /// Spawns more branches up to <see cref="ParallelRequest.MaxDegreeOfParallelism"/>~ ⚡.
    /// </summary>
    private void PumpBranches()
    {
        while (!_aborting
               && _pendingBranches.Count > 0
               && _inFlightCount < _parallel.MaxDegreeOfParallelism)
        {
            var spec = _pendingBranches.Dequeue();
            SpawnBranch(spec);
        }
    }

    private void SpawnBranch(BranchSpec spec)
    {
        var branchInputs = new Dictionary<string, object?>(_initialContext)
        {
            ["__parallel_branch_index__"] = spec.Index,
            ["__parallel_branch_port__"] = spec.PortName,
        };

        // Phase 2.2.3b: per-item fan-out — seed item + index inputs for the body sub-graph~ 🌟
        if (_parallel.Items is not null)
        {
            branchInputs["item"] = spec.Item;
            branchInputs["index"] = spec.ItemIndex;
        }

        var subGraphId = $"{_parallelNodeId}-branch-{spec.Index}";
        var actorName = $"par-branch-{_parallelNodeId.Replace(".", "-")}-{spec.Index}";

        var subGraphActor = Context.ActorOf(
            SubGraphExecutor.Props(
                _executionId,
                _definition,
                spec.ScopeIds,
                spec.EntryNodeIds,
                branchInputs,
                _serviceProvider,
                subGraphId,
                _linkedCts.Token),
            actorName);

        Context.Watch(subGraphActor);
        _actorNameToBranchIndex[subGraphActor.Path.Name] = spec.Index;
        _inFlightCount++;

        _log.Debug(
            "🌐 ParallelCoordinator '{ParallelNodeId}': spawned branch {Index} (port='{Port}', entries={Entries}, scope={Scope})",
            _parallelNodeId, spec.Index, spec.PortName, spec.EntryNodeIds.Count, spec.ScopeIds.Count);
    }

    #endregion

    #region Completion / Failure Handlers

    private void HandleBranchCompleted(SubGraphCompleted msg)
    {
        Context.Unwatch(Sender);
        _inFlightCount--;

        var branchIndex = ResolveBranchIndex(Sender);

        // Strip internal sentinels for downstream consumption~
        var branchResult = msg.Outputs
            .Where(kv => !kv.Key.StartsWith("__parallel_") && !kv.Key.StartsWith("__loop_"))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        _branchResults[branchIndex] = branchResult;

        _log.Debug(
            "✅ ParallelCoordinator '{ParallelNodeId}': branch {Index} completed",
            _parallelNodeId, branchIndex);

        // Phase 2.2.3b: waitForAll=false → first-success-wins semantics~ 🏁
        // Cancel siblings cooperatively and report ParallelCompleted immediately with this branch's result~
        if (!_parallel.WaitForAll && !_aborting)
        {
            _aborting = true;
            _pendingBranches.Clear();
            _linkedCts.Cancel();
            _log.Info(
                "🏁 ParallelCoordinator '{ParallelNodeId}': waitForAll=false — first branch ({Index}) won, cancelling siblings~",
                _parallelNodeId, branchIndex);

            // Drain immediately — TryFinishOrPump still waits for in-flight count to drop to 0,
            // but since we want "first wins", we report now while siblings wind down asynchronously.
            // Their later SubGraphCompleted/SubGraphFailed messages will arrive after we Stop, which is fine~
            ReportCompleted();
            return;
        }

        TryFinishOrPump();
    }

    private void HandleBranchFailed(SubGraphFailed msg)
    {
        Context.Unwatch(Sender);
        _inFlightCount--;

        var branchIndex = ResolveBranchIndex(Sender);
        _failedBranches.Add(branchIndex);
        _firstError ??= msg.Error;
        _firstFailedNodeId ??= msg.FailedNodeId;

        _log.Warning(
            "❌ ParallelCoordinator '{ParallelNodeId}': branch {Index} failed: {Error}",
            _parallelNodeId, branchIndex, msg.Error.Message);

        if (_parallel.FailFast && !_aborting)
        {
            _aborting = true;
            _pendingBranches.Clear();
            _linkedCts.Cancel();
            _log.Info(
                "🛑 ParallelCoordinator '{ParallelNodeId}': fail-fast triggered — cancelling siblings~",
                _parallelNodeId);
        }

        TryFinishOrPump();
    }

    private void TryFinishOrPump()
    {
        // If any in-flight branches remain (or pending and not aborting), keep going.
        if (_inFlightCount > 0)
        {
            PumpBranches();
            return;
        }

        if (!_aborting && _pendingBranches.Count > 0)
        {
            PumpBranches();
            return;
        }

        // Nothing in flight and nothing pending → finalize.
        if (_failedBranches.Count > 0)
        {
            ReportFailed(_firstError ?? new Exception("Parallel branch failed"), _firstFailedNodeId);
        }
        else
        {
            ReportCompleted();
        }
    }

    private int ResolveBranchIndex(IActorRef sender)
    {
        return _actorNameToBranchIndex.TryGetValue(sender.Path.Name, out var idx) ? idx : -1;
    }

    private void ReportCompleted()
    {
        // For waitForAll=true: include all branches in order (null for non-completed = abnormal).
        // For waitForAll=false: include ONLY actually-completed branches; count = winners~ 🏁
        List<object?> ordered;
        int reportedCount;
        if (_parallel.WaitForAll)
        {
            ordered = new List<object?>(_totalBranches);
            for (var i = 0; i < _totalBranches; i++)
            {
                ordered.Add(_branchResults.TryGetValue(i, out var r) ? r : null);
            }

            reportedCount = _totalBranches;
        }
        else
        {
            // First-wins mode: surface only successful branches (typically 1, but tolerate races
            // where two branches reported back before we cancelled)~
            ordered = _branchResults
                .OrderBy(kv => kv.Key)
                .Select(kv => (object?)kv.Value)
                .ToList();
            reportedCount = ordered.Count;
        }

        var outputs = new Dictionary<string, object?>
        {
            ["results"] = ordered,
            ["count"] = reportedCount,
        };

        if (_parallel.AggregatedOutputs != null)
        {
            foreach (var (k, v) in _parallel.AggregatedOutputs)
            {
                outputs[k] = v;
            }
        }

        _log.Info(
            "✅ ParallelCoordinator '{ParallelNodeId}' completed: {Count} branch(es) (waitForAll={WaitForAll})",
            _parallelNodeId, reportedCount, _parallel.WaitForAll);

        Context.Parent.Tell(new ParallelCompleted(_parallelNodeId, outputs));
        Context.Stop(Self);
    }

    private void ReportFailed(Exception error, string? failedNodeId)
    {
        _log.Error(
            error,
            "❌ ParallelCoordinator '{ParallelNodeId}' failed: {Error}",
            _parallelNodeId, error.Message);

        Context.Parent.Tell(new ParallelFailed(_parallelNodeId, error, failedNodeId));
        Context.Stop(Self);
    }

    #endregion

    #region Helpers

    /// <summary>
    /// BFS from branch entry nodes; stops when reaching the parallel node itself
    /// (mirrors LoopExecutorActor.ComputeBodyScope semantics)~ 🗺️.
    /// </summary>
    public static List<string> ComputeBranchScope(
        WorkflowDefinition definition,
        string parallelNodeId,
        IReadOnlyList<string> entryNodeIds)
    {
        var scope = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>(entryNodeIds);

        while (queue.Count > 0)
        {
            var nodeId = queue.Dequeue();
            if (!scope.Add(nodeId)) continue;

            foreach (var conn in definition.Connections.Where(c => c.SourceNodeId == nodeId))
            {
                if (conn.TargetNodeId != parallelNodeId)
                    queue.Enqueue(conn.TargetNodeId);
            }
        }

        return scope.ToList();
    }

    private sealed record BranchSpec(
        int Index,
        string PortName,
        IReadOnlyList<string> EntryNodeIds,
        IReadOnlyList<string> ScopeIds,
        object? item = null,
        int itemIndex = 0)
    {
        /// <summary>The fan-out item payload (per-item mode only)~ 🎁.</summary>
        public object? Item => item;

        /// <summary>The 0-based item index (per-item mode only)~ 🔢.</summary>
        public int ItemIndex => itemIndex;
    }

    #endregion
}

