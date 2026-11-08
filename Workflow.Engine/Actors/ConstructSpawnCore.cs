// <copyright file="ConstructSpawnCore.cs" company="GlutenFree">
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
using Workflow.Core.Models;
using Workflow.Engine.Messages;
using Workflow.Engine.Models;

/// <summary>
/// Shared construct lifecycle (loop / parallel / try-catch / transaction): remember the request a
/// construct node sends, spawn the construct actor when the node completes, and on the construct's
/// completion mark its body handled and fire its done-port successors~ 🧩✨
/// </summary>
/// <remarks>
/// <para>
/// §12.10: extracted from <see cref="WorkflowExecutor"/> so that <see cref="SubGraphExecutor"/> runs a
/// construct nested inside a body (loop in a loop, try/catch in a loop, …) as a construct. Before
/// this, only the top-level executor handled <c>Node*ExecutionRequested</c>, so a nested construct
/// completed as a plain node and every outgoing edge fired once.
/// </para>
/// <para>
/// Same design as <see cref="DispatchCore"/> / <see cref="NodeFailureCore"/>: node state (completed,
/// failed, skipped, outputs) stays in the OWNING actor and is read and changed through delegates. The
/// core only owns the construct bookkeeping (requests waiting for their node to complete, constructs
/// still running). The owner applies the construct's <c>VariableUpdates</c>, unwatches the sender and
/// routes failures through its own failure path. Not thread-safe — single actor use only.
/// </para>
/// </remarks>
internal sealed class ConstructSpawnCore
{
    private readonly Func<IReadOnlyDictionary<string, Dictionary<string, object?>>>? _outerOutputs;
    private readonly WorkflowDefinition _definition;
    private readonly Guid _executionId;
    private readonly IServiceProvider _serviceProvider;
    private readonly Func<string, bool> _isSettled;
    private readonly Func<string, bool> _isSkipped;
    private readonly Action<string> _markSkipped;
    private readonly Action<string, Arr<string>> _executeReadySuccessors;
    private readonly Action<string, Dictionary<string, object?>> _setNodeOutputs;
    private readonly Func<Props, string, IActorRef> _spawnChild;
    private readonly Func<CancellationToken> _cancellationToken;
    private readonly Func<IReadOnlyDictionary<string, object?>> _currentVariables;
    private readonly Func<Dictionary<string, object?>> _initialContext;
    private readonly Func<IReadOnlyList<ScopeStep>> _scopePath;
    private readonly ILoggingAdapter _log;

    // Requests sent by a construct node just before its NodeExecutionCompleted (the "FIFO trick")~
    private readonly Dictionary<string, LoopRequest> _requestedLoops = new();
    private readonly Dictionary<string, ParallelRequest> _requestedParallels = new();
    private readonly Dictionary<string, TryCatchRequest> _requestedTryCatches = new();
    private readonly Dictionary<string, TransactionRequest> _requestedTransactions = new();

    // Constructs whose actor is running; the owner is not complete while any are~
    private readonly Dictionary<string, IActorRef> _running = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TransactionRequest> _runningTransactions = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ConstructSpawnCore"/> class.
    /// </summary>
    /// <param name="definition">The full workflow definition.</param>
    /// <param name="executionId">The run the constructs belong to.</param>
    /// <param name="serviceProvider">Passed on to the construct actors.</param>
    /// <param name="isSettled">True when a node is completed, failed or skipped in the owner.</param>
    /// <param name="isSkipped">True when a node is skipped in the owner.</param>
    /// <param name="markSkipped">Marks a construct's body node as handled (skipped) in the owner.</param>
    /// <param name="executeReadySuccessors">The owner's port-aware routing (<see cref="DispatchCore"/>).</param>
    /// <param name="setNodeOutputs">Stores a construct node's outputs in the owner.</param>
    /// <param name="spawnChild">Creates and watches a child actor of the owner.</param>
    /// <param name="cancellationToken">The owner's token, which the construct actor links to.</param>
    /// <param name="currentVariables">The variables the construct's body should start from.</param>
    /// <param name="initialContext">Inputs + variables for loop conditions and parallel branches.</param>
    /// <param name="scopePath">
    /// The constructs the owner runs inside (empty for <see cref="WorkflowExecutor"/>); each construct
    /// adds its own step for the history records of its bodies.
    /// </param>
    /// <param name="log">The owner's logger.</param>
    /// <param name="outerOutputs">§10.1: completed nodes' outputs (outer scopes included) handed to construct bodies.</param>
    public ConstructSpawnCore(
        WorkflowDefinition definition,
        Guid executionId,
        IServiceProvider serviceProvider,
        Func<string, bool> isSettled,
        Func<string, bool> isSkipped,
        Action<string> markSkipped,
        Action<string, Arr<string>> executeReadySuccessors,
        Action<string, Dictionary<string, object?>> setNodeOutputs,
        Func<Props, string, IActorRef> spawnChild,
        Func<CancellationToken> cancellationToken,
        Func<IReadOnlyDictionary<string, object?>> currentVariables,
        Func<Dictionary<string, object?>> initialContext,
        Func<IReadOnlyList<ScopeStep>> scopePath,
        ILoggingAdapter log,
        Func<IReadOnlyDictionary<string, Dictionary<string, object?>>>? outerOutputs = null)
    {
        _definition = definition;
        _executionId = executionId;
        _serviceProvider = serviceProvider;
        _isSettled = isSettled;
        _isSkipped = isSkipped;
        _markSkipped = markSkipped;
        _executeReadySuccessors = executeReadySuccessors;
        _setNodeOutputs = setNodeOutputs;
        _spawnChild = spawnChild;
        _cancellationToken = cancellationToken;
        _currentVariables = currentVariables;
        _initialContext = initialContext;
        _scopePath = scopePath;
        _outerOutputs = outerOutputs;
        _log = log;
    }

    /// <summary>Gets the number of constructs whose actor is still running.</summary>
    public int RunningCount => _running.Count;

    /// <summary>Gets the construct node whose running actor is <paramref name="actor"/>, or null.</summary>
    public string? RunningNodeFor(IActorRef actor)
        => _running.FirstOrDefault(kv => kv.Value.Equals(actor)).Key;

    // ── Requests ─────────────────────────────────────────────────────────────────────────────────

    public void Request(NodeLoopExecutionRequested message) => _requestedLoops[message.NodeId] = message.Loop;

    public void Request(NodeParallelExecutionRequested message) => _requestedParallels[message.NodeId] = message.Parallel;

    public void Request(NodeTryCatchExecutionRequested message) => _requestedTryCatches[message.NodeId] = message.TryCatch;

    public void Request(NodeTransactionExecutionRequested message) => _requestedTransactions[message.NodeId] = message.Transaction;

    /// <summary>
    /// Starts the transaction construct for a node that just completed, if it requested one.
    /// </summary>
    /// <remarks>
    /// A transaction may have no body connections, so <see cref="WorkflowExecutor"/> starts it
    /// before its own completion check; the other constructs start after it (see
    /// <see cref="TryStartOther"/>)~ 💼.
    /// </remarks>
    /// <returns>True when the node was a transaction (the caller must not route its successors).</returns>
    public bool TryStartTransaction(string nodeId)
    {
        if (!_requestedTransactions.Remove(nodeId, out var request))
        {
            return false;
        }

        SpawnTransaction(nodeId, request);
        return true;
    }

    /// <summary>
    /// Starts the loop, parallel or try/catch construct for a node that just completed, if it
    /// requested one.
    /// </summary>
    /// <returns>True when the node was such a construct (the caller must not route its successors).</returns>
    public bool TryStartOther(string nodeId)
    {
        // Phase 2.2.2: the loop actor drives 'done' port activation~ 🔁
        if (_requestedLoops.Remove(nodeId, out var loop))
        {
            SpawnLoop(nodeId, loop);
            return true;
        }

        // Phase 2.2.3a: the coordinator owns all branches; done fires on ParallelCompleted~ 🌐
        if (_requestedParallels.Remove(nodeId, out var parallel))
        {
            SpawnParallel(nodeId, parallel);
            return true;
        }

        // Phase 2.2.4: the try/catch actor owns the try/catch/finally sequence~ 🛡️
        if (_requestedTryCatches.Remove(nodeId, out var tryCatch))
        {
            SpawnTryCatch(nodeId, tryCatch);
            return true;
        }

        return false;
    }

    /// <summary>Starts whichever construct a node that just completed requested, if any.</summary>
    public bool TryStart(string nodeId) => TryStartTransaction(nodeId) || TryStartOther(nodeId);

    /// <summary>A construct reported failure: it is no longer running (the owner handles the failure).</summary>
    public void OnFailed(string constructNodeId)
    {
        _running.Remove(constructNodeId);
        _runningTransactions.Remove(constructNodeId);
    }

    // ── Loop ─────────────────────────────────────────────────────────────────────────────────────

    private void SpawnLoop(string loopNodeId, LoopRequest loop)
    {
        // Resolve body entry nodes (targets of loopBodyPort connections from the loop node)
        var bodyEntryNodeIds = _definition.Connections
            .Where(c => c.SourceNodeId == loopNodeId && c.SourcePortName == loop.LoopBodyPort)
            .Select(c => c.TargetNodeId)
            .ToList();

        if (bodyEntryNodeIds.Count == 0)
        {
            _log.Warning(
                "⚠️ LoopExecutor spawn: loop node '{LoopNodeId}' has no connections via '{LoopBodyPort}' port — " +
                "completing loop immediately with 0 iterations~ 🔁",
                loopNodeId, loop.LoopBodyPort);

            // Complete loop immediately — fire done port
            _setNodeOutputs(loopNodeId, new Dictionary<string, object?>
            {
                ["results"] = new List<object?>(),
                ["count"] = 0,
                ["errors"] = new List<string>(),
            });
            _executeReadySuccessors(loopNodeId, new Arr<string>(new[] { loop.DonePort }));
            return;
        }

        var bodyScopeIds = LoopExecutorActor.ComputeBodyScope(_definition, loopNodeId, bodyEntryNodeIds);

        var actorName = $"loop-{loopNodeId.Replace(".", "-")}";
        _running[loopNodeId] = _spawnChild(
            LoopExecutorActor.Props(
                loopNodeId, loop, _definition,
                bodyEntryNodeIds, bodyScopeIds,
                _executionId, _initialContext(),
                _serviceProvider, _cancellationToken(),
                _currentVariables(), _scopePath(), _outerOutputs?.Invoke()),
            actorName);

        _log.Info(
            "🔁 Spawned LoopExecutorActor '{ActorName}' for node '{LoopNodeId}': " +
            "{BodyEntryCount} body entry nodes, {ScopeCount} scope nodes",
            actorName, loopNodeId, bodyEntryNodeIds.Count, bodyScopeIds.Count);
    }

    /// <summary>
    /// All loop iterations completed: store the outputs, mark the body handled and fire the
    /// <c>done</c> port successors~ ✅🔁.
    /// </summary>
    /// <param name="message">The loop's completion.</param>
    /// <param name="tryComplete">Completes the owner if everything is done; true when it did.</param>
    public void OnLoopCompleted(LoopCompleted message, Func<bool> tryComplete)
    {
        var loopNodeId = message.LoopNodeId;
        _running.Remove(loopNodeId);

        _log.Info("✅ Loop '{LoopNodeId}' completed with {Count} iteration(s)", loopNodeId,
            message.Outputs.TryGetValue("count", out var cnt) ? cnt : "?");

        // Store aggregated outputs so downstream 'done' nodes can read them
        _setNodeOutputs(loopNodeId, message.Outputs.ToDictionary(kv => kv.Key, kv => kv.Value));

        if (tryComplete())
        {
            return;
        }

        // Compute the body scope (all nodes reachable from loopBody port before looping back).
        // These nodes were executed by LoopExecutorActor — the owner never ran them directly.
        // Mark them as skipped here so the owner's completion check counts them and we don't re-fire them~ 🔁✨
        // §12.9: seed the scope from body-port targets ONLY (matching SpawnLoop). Seeding it
        // from every outgoing edge swallowed the done-port successors into the body and skipped them.
        var bodyEntryTargets = _definition.Connections
            .Where(c => c.SourceNodeId == loopNodeId && c.SourcePortName == message.LoopBodyPort)
            .Select(c => c.TargetNodeId)
            .ToList();

        var bodyScope = LoopExecutorActor
            .ComputeBodyScope(_definition, loopNodeId, bodyEntryTargets)
            .ToHashSet();

        // CopilotNote: skipping body-scope nodes prevents double-execution on the done-port routing below.
        foreach (var bodyScopeNodeId in bodyScope)
        {
            if (!_isSettled(bodyScopeNodeId))
            {
                _markSkipped(bodyScopeNodeId);
                _log.Debug(
                    "⏭️ Marked body-scope node '{NodeId}' as skipped (handled by LoopExecutor)~ 🔁",
                    bodyScopeNodeId);
            }
        }

        if (tryComplete())
        {
            return;
        }

        // Fire only connections that are NOT body-scope targets (i.e., the 'done' port out-edges)~
        var donePorts = _definition.Connections
            .Where(c => c.SourceNodeId == loopNodeId && !bodyScope.Contains(c.TargetNodeId))
            .Select(c => c.SourcePortName)
            .Distinct()
            .ToList();

        if (donePorts.Count > 0)
        {
            _executeReadySuccessors(loopNodeId, new Arr<string>(donePorts));
        }

        // If there are no done-port edges, all reachable work is body-scope → already complete above.
    }

    // ── Parallel ─────────────────────────────────────────────────────────────────────────────────

    private void SpawnParallel(string parallelNodeId, ParallelRequest parallel)
    {
        // Pre-compute the union of branch scopes — these nodes will be driven by SubGraphExecutors,
        // not by the owner; we mark them skipped here to prevent double-execution from the done-port
        // routing that follows ParallelCompleted (same fix as Phase 2.2.2 loops)~ 🌐✨
        var unionScope = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        var anyEntries = false;

        // Phase 2.2.3b: detect overlap across branch scopes — convergent patterns must use FanInModule~ 🪄
        var overlapping = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

        // Phase 2.2.3b: resolve branch ports — per-item mode uses the single BranchPort,
        // static mode uses the BranchPorts list~ 🌟🌐
        var portsToScope = parallel.Items is not null
            ? new[] { parallel.BranchPort }
            : parallel.BranchPorts;

        foreach (var port in portsToScope)
        {
            var entries = _definition.Connections
                .Where(c => c.SourceNodeId == parallelNodeId && c.SourcePortName == port)
                .Select(c => c.TargetNodeId)
                .ToList();
            if (entries.Count == 0)
            {
                continue;
            }

            anyEntries = true;
            var thisBranch = new System.Collections.Generic.HashSet<string>(
                ParallelExecutionCoordinator.ComputeBranchScope(_definition, parallelNodeId, entries),
                StringComparer.Ordinal);

            // Detect overlap with any previously-seen branch~
            foreach (var n in thisBranch)
            {
                if (!unionScope.Add(n))
                {
                    overlapping.Add(n);
                }
            }
        }

        // Per-item mode: an empty Items collection is a valid no-op (0 branches), distinct from
        // "no connections" (degenerate). Treat as immediate-complete only when the port itself is unwired~
        if (parallel.Items is not null && parallel.Items.Count == 0)
        {
            _log.Info(
                "🌟 ParallelExecutor '{NodeId}' per-item fan-out: empty items collection — completing with 0 work~",
                parallelNodeId);
            _setNodeOutputs(parallelNodeId, new Dictionary<string, object?>
            {
                ["results"] = new List<object?>(),
                ["count"] = 0,
            });
            _executeReadySuccessors(parallelNodeId, new Arr<string>(new[] { parallel.DonePort }));
            return;
        }

        if (overlapping.Count > 0)
        {
            // Defensive guard (Phase 2.2.3b carry-over): branch scopes overlap.
            // Use builtin.fanin (2.2.3b) for convergent patterns instead of letting both branches
            // claim the same nodes (which produces ambiguous skip-marking + double execution risk)~ 🪄
            var nodes = string.Join(", ", overlapping);
            _log.Warning(
                "⚠️ ParallelExecutor '{NodeId}': branch scopes overlap on node(s) [{Overlap}]. " +
                "For convergent patterns, terminate branches at a 'builtin.fanin' barrier node instead. " +
                "Continuing with first-branch-wins skip-marking semantics for now~ 🪄",
                parallelNodeId, nodes);
        }

        if (!anyEntries)
        {
            _log.Warning(
                "⚠️ ParallelExecutor spawn: parallel node '{NodeId}' has no branch connections — " +
                "completing immediately with 0 branches~ 🌐",
                parallelNodeId);

            _setNodeOutputs(parallelNodeId, new Dictionary<string, object?>
            {
                ["results"] = new List<object?>(),
                ["count"] = 0,
            });
            _executeReadySuccessors(parallelNodeId, new Arr<string>(new[] { parallel.DonePort }));
            return;
        }

        // Pre-mark branch-scope nodes skipped so OnParallelCompleted's done-port routing
        // doesn't re-fire them~
        foreach (var nid in unionScope)
        {
            if (!_isSettled(nid))
            {
                _markSkipped(nid);
            }
        }

        var actorName = $"par-{parallelNodeId.Replace(".", "-")}";
        _running[parallelNodeId] = _spawnChild(
            ParallelExecutionCoordinator.Props(
                parallelNodeId, parallel, _definition,
                _executionId, _initialContext(),
                _serviceProvider, _cancellationToken(),
                _currentVariables(), _scopePath(), _outerOutputs?.Invoke()),
            actorName);

        _log.Info(
            "🌐 Spawned ParallelExecutionCoordinator '{ActorName}' for node '{ParallelNodeId}': " +
            "{BranchCount} branch(es), {ScopeCount} scope nodes pre-marked",
            actorName, parallelNodeId, parallel.BranchPorts.Count, unionScope.Count);
    }

    /// <summary>
    /// All parallel branches completed: store the outputs and fire the <c>done</c> port successors~ ✅🌐.
    /// </summary>
    public void OnParallelCompleted(ParallelCompleted message, Func<bool> tryComplete)
    {
        var parallelNodeId = message.ParallelNodeId;
        _running.Remove(parallelNodeId);

        _log.Info("✅ Parallel '{ParallelNodeId}' completed with {Count} branch(es)",
            parallelNodeId,
            message.Outputs.TryGetValue("count", out var cnt) ? cnt : "?");

        _setNodeOutputs(parallelNodeId, message.Outputs.ToDictionary(kv => kv.Key, kv => kv.Value));

        if (tryComplete())
        {
            return;
        }

        // Done-port routing: only fire connections that aren't branch-scope (those are already skipped)~
        // We compute the union scope again to filter out branch out-edges from the source node itself~
        var unionScope = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        foreach (var conn in _definition.Connections.Where(c => c.SourceNodeId == parallelNodeId))
        {
            // Any node we already marked skipped (pre-marked at spawn) is in scope.
            if (_isSkipped(conn.TargetNodeId))
            {
                unionScope.Add(conn.TargetNodeId);
            }
        }

        var donePorts = _definition.Connections
            .Where(c => c.SourceNodeId == parallelNodeId && !unionScope.Contains(c.TargetNodeId))
            .Select(c => c.SourcePortName)
            .Distinct()
            .ToList();

        if (donePorts.Count > 0)
        {
            _executeReadySuccessors(parallelNodeId, new Arr<string>(donePorts));
        }
    }

    // ── Try/catch ────────────────────────────────────────────────────────────────────────────────

    private void SpawnTryCatch(string tryCatchNodeId, TryCatchRequest request)
    {
        // Pre-compute union of all branch scopes (try + catch + finally) and pre-mark as skipped
        // so the owner's completion check counts them once the executor completes~ 🛡️✨
        var allBranchPorts = new[] { request.TryPort, request.CatchPort, request.FinallyPort };
        var unionScope = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

        foreach (var port in allBranchPorts)
        {
            var entries = _definition.Connections
                .Where(c => c.SourceNodeId == tryCatchNodeId && c.SourcePortName == port)
                .Select(c => c.TargetNodeId)
                .ToList();

            if (entries.Count == 0)
            {
                continue;
            }

            var branchScope = TryCatchExecutorActor.ComputeScope(_definition, tryCatchNodeId, entries);
            foreach (var n in branchScope)
            {
                unionScope.Add(n);
            }
        }

        // Pre-mark branch-scope nodes as skipped (TryCatchExecutorActor runs them internally)~
        foreach (var scopeNodeId in unionScope)
        {
            if (!_isSettled(scopeNodeId))
            {
                _markSkipped(scopeNodeId);
                _log.Debug(
                    "⏭️ Pre-marked trycatch branch-scope node '{NodeId}' as skipped for '{TryCatchNodeId}'~ 🛡️",
                    scopeNodeId, tryCatchNodeId);
            }
        }

        var actorName = $"tc-{tryCatchNodeId.Replace(".", "-")}";
        _running[tryCatchNodeId] = _spawnChild(
            TryCatchExecutorActor.Props(
                tryCatchNodeId, request, _definition,
                _executionId, _serviceProvider, _cancellationToken(),
                _currentVariables(), _scopePath(), _outerOutputs?.Invoke()),
            actorName);

        _log.Info(
            "🛡️ Spawned TryCatchExecutorActor '{ActorName}' for node '{TryCatchNodeId}' " +
            "(rethrow={Rethrow}, catchTypes={CatchTypes}, {ScopeCount} branch-scope nodes pre-marked)",
            actorName, tryCatchNodeId, request.Rethrow,
            request.CatchTypes is null ? "any" : string.Join(",", request.CatchTypes),
            unionScope.Count);
    }

    /// <summary>
    /// A try/catch/finally sequence completed: store the outputs and fire the <c>done</c> port
    /// successors of the trycatch node~ ✅🛡️.
    /// </summary>
    public void OnTryCatchCompleted(TryCatchCompleted message, Func<bool> tryComplete)
    {
        var tryCatchNodeId = message.TryCatchNodeId;
        _running.Remove(tryCatchNodeId);

        _log.Info(
            "✅ TryCatch '{TryCatchNodeId}' sequence completed (success={Success})",
            tryCatchNodeId,
            message.Outputs.TryGetValue("success", out var s) ? s : "?");

        // Store outputs so downstream 'done' nodes can read them~
        _setNodeOutputs(tryCatchNodeId, message.Outputs.ToDictionary(kv => kv.Key, kv => kv.Value));

        if (tryComplete())
        {
            return;
        }

        // Fire 'done' port connections (connections from tryCatchNode that don't target the branch scopes)~
        var allBranchPorts = new[] { "try", "catch", "finally" };
        var branchTargets = _definition.Connections
            .Where(c => c.SourceNodeId == tryCatchNodeId && allBranchPorts.Contains(c.SourcePortName))
            .Select(c => c.TargetNodeId)
            .ToHashSet(StringComparer.Ordinal);

        var donePorts = _definition.Connections
            .Where(c => c.SourceNodeId == tryCatchNodeId && !branchTargets.Contains(c.TargetNodeId))
            .Select(c => c.SourcePortName)
            .Distinct()
            .ToList();

        if (donePorts.Count > 0)
        {
            _executeReadySuccessors(tryCatchNodeId, new Arr<string>(donePorts));
        }

        // If no done-port connections, all work is in branch scope → completion check above handles it~
    }

    // ── Transaction ──────────────────────────────────────────────────────────────────────────────

    private void SpawnTransaction(string transactionNodeId, TransactionRequest request)
    {
        var entries = _definition.Connections
            .Where(c => c.SourceNodeId == transactionNodeId && c.SourcePortName == request.BodyPort)
            .Select(c => c.TargetNodeId)
            .ToList();

        var bodyScope = TransactionExecutorActor.ComputeScope(_definition, transactionNodeId, entries);

        // Pre-mark body-scope nodes as skipped (TransactionExecutorActor runs them internally)~
        foreach (var scopeNodeId in bodyScope)
        {
            if (!_isSettled(scopeNodeId))
            {
                _markSkipped(scopeNodeId);
                _log.Debug(
                    "⏭️ Pre-marked transaction body-scope node '{NodeId}' as skipped for '{TransactionNodeId}'~ 💼",
                    scopeNodeId, transactionNodeId);
            }
        }

        _runningTransactions[transactionNodeId] = request;

        var actorName = $"txn-{transactionNodeId.Replace(".", "-")}";
        _running[transactionNodeId] = _spawnChild(
            TransactionExecutorActor.Props(
                transactionNodeId, request, _definition,
                _executionId, _serviceProvider, _cancellationToken(),
                _currentVariables(), _scopePath(), _outerOutputs?.Invoke()),
            actorName);

        _log.Info(
            "💼 Spawned TransactionExecutorActor '{ActorName}' for node '{TransactionNodeId}' ({ScopeCount} body-scope nodes pre-marked)",
            actorName, transactionNodeId, bodyScope.Count);
    }

    /// <summary>
    /// A transaction completed: store the outputs and fire the committed / rolledBack successors~ ✅💼.
    /// </summary>
    public void OnTransactionCompleted(TransactionCompleted message, Func<bool> tryComplete)
    {
        var transactionNodeId = message.TransactionNodeId;
        _running.Remove(transactionNodeId);

        var request = _runningTransactions.Remove(transactionNodeId, out var r)
            ? r
            : new TransactionRequest();

        _log.Info(
            "✅ Transaction '{TransactionNodeId}' completed (committed={Committed}, rolledBack={RolledBack})",
            transactionNodeId,
            message.Outputs.TryGetValue("committed", out var c) ? c : "?",
            message.Outputs.TryGetValue("rolledBack", out var rb) ? rb : "?");

        _setNodeOutputs(transactionNodeId, message.Outputs.ToDictionary(kv => kv.Key, kv => kv.Value));

        if (tryComplete())
        {
            return;
        }

        var port = message.Outputs.TryGetValue("rolledBack", out var rolledBack)
                   && rolledBack is bool rbBool
                   && rbBool
            ? request.RolledBackPort
            : request.CommittedPort;

        _executeReadySuccessors(transactionNodeId, new Arr<string>(new[] { port }));
    }
}
