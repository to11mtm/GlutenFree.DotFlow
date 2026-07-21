// <copyright file="TryCatchExecutorActor.cs" company="GlutenFree">
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
/// 🛡️ Orchestrates the try → catch? → finally? sequence for a <c>builtin.trycatch</c> node~
/// Spawned by <c>WorkflowExecutor</c> when a node completion carries a
/// <see cref="TryCatchRequest"/>. Reports <see cref="TryCatchCompleted"/> or
/// <see cref="TryCatchFailed"/> to its parent when the sequence ends~ ✨💖
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: Phase 2.2.4 — lifecycle mirrors <c>LoopExecutorActor</c>:
/// <list type="number">
///   <item>PreStart: find try-branch entry nodes, spawn <see cref="SubGraphExecutor"/> for try body.</item>
///   <item>On <see cref="SubGraphCompleted"/> (try): if finally nodes exist run finally; else report success.</item>
///   <item>On <see cref="SubGraphFailed"/> (try): build <see cref="WorkflowError"/>, if catch nodes exist run catch; else skip to finally.</item>
///   <item>On <see cref="SubGraphCompleted"/> (catch): if finally nodes exist run finally; else report (rethrow → fail, else success).</item>
///   <item>On <see cref="SubGraphCompleted"/> (finally): report success or rethrow depending on <see cref="TryCatchRequest.Rethrow"/>.</item>
/// </list>
/// </para>
/// <para>
/// The <see cref="WorkflowError"/> built from the caught exception is injected as the
/// <c>error</c> input into the catch-branch <see cref="SubGraphExecutor"/> so catch-handler
/// nodes can inspect it~ 🌸.
/// </para>
/// </remarks>
public sealed class TryCatchExecutorActor : ReceiveActor
{
    // Sub-graph ID constants to distinguish phases~
    private const string SubGraphTry = "try";
    private const string SubGraphCatch = "catch";
    private const string SubGraphFinally = "finally";

    private readonly string _tryCatchNodeId;
    private readonly TryCatchRequest _request;
    private readonly WorkflowDefinition _definition;
    private readonly Guid _executionId;
    private readonly IServiceProvider _serviceProvider;
    private readonly CancellationTokenSource _linkedCts;
    private readonly ILoggingAdapter _log;

    // ── Phase tracking ───────────────────────────────────────────────────────────────────
    private enum Phase { RunningTry, RunningCatch, RunningFinally }

    private Phase _phase = Phase.RunningTry;

    /// <summary>
    /// Set when a failure is caught from the try-sub-graph; null when try succeeded~
    /// </summary>
    private WorkflowError? _caughtError;

    /// <summary>
    /// Original exception for rethrow path~
    /// </summary>
    private Exception? _caughtException;

    /// <summary>
    /// True when the error was caught by a catch branch (vs. unhandled / filter-miss).
    /// CopilotNote: distinguishes "rethrow after catch" (explicit user choice)
    /// from "no catch configured" (always re-escalate)~ 🎯
    /// </summary>
    private bool _errorWasCaught;

    /// <summary>
    /// Initializes a new <see cref="TryCatchExecutorActor"/>~ 🛡️✨
    /// </summary>
    /// <param name="tryCatchNodeId">Node ID of the trycatch module. 🆔.</param>
    /// <param name="request">The try/catch specification. 🛡️.</param>
    /// <param name="definition">Full workflow definition for sub-graph scoping. 📋.</param>
    /// <param name="executionId">Parent execution ID for history correlation. 🔑.</param>
    /// <param name="serviceProvider">DI container. 💉.</param>
    /// <param name="parentToken">Hierarchical cancellation token (2.2.0b)~ 🔗🛑.</param>
    public TryCatchExecutorActor(
        string tryCatchNodeId,
        TryCatchRequest request,
        WorkflowDefinition definition,
        Guid executionId,
        IServiceProvider serviceProvider,
        CancellationToken parentToken = default)
    {
        _tryCatchNodeId = tryCatchNodeId;
        _request = request;
        _definition = definition;
        _executionId = executionId;
        _serviceProvider = serviceProvider;
        _log = Context.GetLogger();

        // Phase 2.2.0b: linked CTS for cooperative cancellation~ 🔗🛑
        _linkedCts = parentToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(parentToken)
            : new CancellationTokenSource();

        // CopilotNote: SubGraphCompleted/SubGraphFailed messages come from child SubGraphExecutors.
        // We inspect SubGraphId to determine which phase completed~ 🎯
        Receive<SubGraphCompleted>(HandleSubGraphCompleted);
        Receive<SubGraphFailed>(HandleSubGraphFailed);
        Receive<Terminated>(_ =>
        {
            // watched child stopped — handled via SubGraphFailed already or no-op~
        });
    }

    /// <summary>Creates Props for spawning a <see cref="TryCatchExecutorActor"/>~ 🏭✨</summary>
    public static Props Props(
        string tryCatchNodeId,
        TryCatchRequest request,
        WorkflowDefinition definition,
        Guid executionId,
        IServiceProvider serviceProvider,
        CancellationToken parentToken = default)
    {
        return Akka.Actor.Props.Create(() => new TryCatchExecutorActor(
            tryCatchNodeId, request, definition, executionId, serviceProvider, parentToken));
    }

    #region Lifecycle

    /// <inheritdoc/>
    protected override void PreStart()
    {
        base.PreStart();
        _log.Info(
            "🛡️ TryCatchExecutorActor starting for node '{TryCatchNodeId}' (rethrow={Rethrow}) under execution {ExecutionId}",
            _tryCatchNodeId, _request.Rethrow, _executionId);
        RunTryPhase();
    }

    /// <inheritdoc/>
    protected override void PostStop()
    {
        // Phase 2.2.0b: dispose linked CTS to avoid token registration leaks~ 🧹
        _linkedCts.Dispose();
        base.PostStop();
    }

    #endregion

    #region Sub-graph phase runners

    private void RunTryPhase()
    {
        _phase = Phase.RunningTry;
        var entryNodes = FindEntryNodes(_request.TryPort);

        if (entryNodes.Count == 0)
        {
            _log.Warning(
                "⚠️ TryCatch '{TryCatchNodeId}': no connections via '{TryPort}' port — skipping try body",
                _tryCatchNodeId, _request.TryPort);
            // No try body → go straight to finally (if any) then complete~
            RunFinallyOrComplete();
            return;
        }

        var scope = ComputeBranchScope(_request.TryPort);
        SpawnSubGraph(SubGraphTry, entryNodes, scope, inputs: null);
    }

    private void RunCatchPhase(WorkflowError error)
    {
        _phase = Phase.RunningCatch;
        _errorWasCaught = true; // mark that a catch handler ran (for rethrow check)~
        var entryNodes = FindEntryNodes(_request.CatchPort);

        if (entryNodes.Count == 0)
        {
            _log.Warning(
                "⚠️ TryCatch '{TryCatchNodeId}': no connections via '{CatchPort}' port — no catch handler",
                _tryCatchNodeId, _request.CatchPort);
            RunFinallyOrComplete();
            return;
        }

        // Inject the WorkflowError as the 'error' input for catch handler nodes~
        var catchInputs = new Dictionary<string, object?> { ["error"] = error };
        var scope = ComputeBranchScope(_request.CatchPort);
        SpawnSubGraph(SubGraphCatch, entryNodes, scope, inputs: catchInputs);
    }

    private void RunFinallyPhase()
    {
        _phase = Phase.RunningFinally;
        var entryNodes = FindEntryNodes(_request.FinallyPort);

        if (entryNodes.Count == 0)
        {
            // No finally body — conclude immediately~
            ConcludeSequence();
            return;
        }

        var scope = ComputeBranchScope(_request.FinallyPort);
        SpawnSubGraph(SubGraphFinally, entryNodes, scope, inputs: null);
    }

    private void RunFinallyOrComplete()
    {
        var finallyEntries = FindEntryNodes(_request.FinallyPort);
        if (finallyEntries.Count > 0)
        {
            RunFinallyPhase();
        }
        else
        {
            ConcludeSequence();
        }
    }

    private void ConcludeSequence()
    {
        // Re-escalate conditions:
        // 1. Error was NOT caught (no catch branch configured, or catchTypes filter miss) → ALWAYS re-escalate
        // 2. Error WAS caught by a catch branch BUT rethrow=true → re-escalate per user request
        // CopilotNote: try { } finally { } with no catch = exception escapes (same as C# semantics)~ 💖
        var shouldRethrow = _caughtException is not null &&
                            (!_errorWasCaught || _request.Rethrow);

        if (shouldRethrow)
        {
            _log.Warning(
                "⚠️ TryCatch '{TryCatchNodeId}': {Reason} — re-escalating error after finally~ ❗",
                _tryCatchNodeId,
                _errorWasCaught ? "rethrow=true" : "no catch handler handled the error");
            Context.Parent.Tell(new TryCatchFailed(_tryCatchNodeId, _caughtException!));
        }
        else
        {
            var outputs = new Dictionary<string, object?>
            {
                ["success"] = _caughtError is null || _errorWasCaught,
                ["error"] = _errorWasCaught ? _caughtError : null,
            };
            _log.Info(
                "✅ TryCatch '{TryCatchNodeId}' sequence complete (caught={Caught}, rethrow={Rethrow})",
                _tryCatchNodeId, _errorWasCaught, _request.Rethrow);
            Context.Parent.Tell(new TryCatchCompleted(_tryCatchNodeId, outputs));
        }

        Context.Stop(Self);
    }

    #endregion

    #region SubGraph message handlers

    private void HandleSubGraphCompleted(SubGraphCompleted message)
    {
        _log.Debug(
            "🛡️ TryCatch '{TryCatchNodeId}': sub-graph '{SubGraphId}' completed (phase={Phase})",
            _tryCatchNodeId, message.SubGraphId, _phase);

        // CopilotNote: use _phase (not SubGraphId prefix) to decide next step —
        // SubGraphId = "try:tc" / "catch:tc" / "finally:tc" but _phase is unambiguous~ 🎯
        switch (_phase)
        {
            case Phase.RunningTry:
                // Try succeeded — move to finally (or done)~
                RunFinallyOrComplete();
                break;

            case Phase.RunningCatch:
                // Catch completed — move to finally (or conclude with rethrow check)~
                RunFinallyOrComplete();
                break;

            case Phase.RunningFinally:
                // Finally done — conclude the whole sequence~
                ConcludeSequence();
                break;

            default:
                _log.Warning(
                    "⚠️ TryCatch '{TryCatchNodeId}': SubGraphCompleted in unexpected phase '{Phase}'",
                    _tryCatchNodeId, _phase);
                break;
        }
    }

    private void HandleSubGraphFailed(SubGraphFailed message)
    {
        _log.Warning(
            "⚡ TryCatch '{TryCatchNodeId}': sub-graph '{SubGraphId}' failed (phase={Phase}) — {Error}",
            _tryCatchNodeId, message.SubGraphId, _phase, message.Error.Message);

        // CopilotNote: use _phase to route failures — same reasoning as subgraph completion~ 🎯
        switch (_phase)
        {
            case Phase.RunningTry:
                // Try failed — check if we should catch it~
                HandleTryFailure(message.Error, message.FailedNodeId);
                break;

            case Phase.RunningCatch:
                // Catch handler itself failed — escalate via finally then rethrow~
                _caughtException = message.Error;
                _caughtError = WorkflowError.FromException(message.Error, message.FailedNodeId);
                RunFinallyOrComplete();
                break;

            case Phase.RunningFinally:
                // Finally failed — always escalate finally failures~
                Context.Parent.Tell(new TryCatchFailed(_tryCatchNodeId, message.Error));
                Context.Stop(Self);
                break;

            default:
                _log.Warning(
                    "⚠️ TryCatch '{TryCatchNodeId}': SubGraphFailed in unexpected phase '{Phase}'~ ⚠️",
                    _tryCatchNodeId, _phase);
                Context.Parent.Tell(new TryCatchFailed(_tryCatchNodeId, message.Error));
                Context.Stop(Self);
                break;
        }
    }

    private void HandleTryFailure(Exception error, string? failedNodeId)
    {
        // Build the structured error payload~
        _caughtException = error;
        _caughtError = WorkflowError.FromException(error, failedNodeId);

        // Check if error type matches the catchTypes filter~
        if (!ShouldCatch(error))
        {
            _log.Info(
                "🛡️ TryCatch '{TryCatchNodeId}': error type '{ErrorType}' not in catchTypes — treating as uncaught, will re-escalate after finally",
                _tryCatchNodeId, _caughtError.ErrorType);
            // Not caught by filter — run finally then escalate (force-rethrow regardless of setting)~
            // CopilotNote: if catchTypes filter excludes this error, it was NOT handled — must escalate~
            RunFinallyOrComplete();
            return;
        }

        // Check if there's a catch branch to route to~
        var catchEntries = FindEntryNodes(_request.CatchPort);
        if (catchEntries.Count > 0)
        {
            _log.Info(
                "🛡️ TryCatch '{TryCatchNodeId}': routing to catch handler for error '{ErrorType}'~ 🪤",
                _tryCatchNodeId, _caughtError.ErrorType);
            RunCatchPhase(_caughtError);
        }
        else
        {
            // No catch handler configured — run finally then re-escalate (unhandled error)~
            _log.Info(
                "🛡️ TryCatch '{TryCatchNodeId}': no catch handler configured — running finally then re-escalating~ 🧹",
                _tryCatchNodeId);
            // CopilotNote: _caughtException is set above; ConcludeSequence will rethrow because
            // an unhandled error (no catch branch was available) ALWAYS escalates after finally.
            // This is consistent with try { throw; } finally { } semantics in C#~ 💖
            RunFinallyOrComplete();
        }
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Determines if this boundary should catch the given exception based on <see cref="TryCatchRequest.CatchTypes"/>~
    /// </summary>
    private bool ShouldCatch(Exception ex)
    {
        if (_request.CatchTypes is not { Length: > 0 }) return true;

        var actualType = ex.GetType().Name;
        var errorType = ex is WorkflowUserException wue ? wue.ErrorType : actualType;

        return _request.CatchTypes.Any(ct =>
            string.Equals(ct, actualType, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ct, errorType, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Finds entry-node IDs connected via the given port from the trycatch module node~
    /// </summary>
    private List<string> FindEntryNodes(string portName)
    {
        return _definition.Connections
            .Where(c => c.SourceNodeId == _tryCatchNodeId && c.SourcePortName == portName)
            .Select(c => c.TargetNodeId)
            .ToList();
    }

    /// <summary>
    /// Computes the full scope of nodes reachable from the given port via BFS~
    /// </summary>
    private IReadOnlyCollection<string> ComputeBranchScope(string portName)
    {
        var entryNodes = FindEntryNodes(portName);
        return ComputeScope(_definition, _tryCatchNodeId, entryNodes);
    }

    /// <summary>
    /// BFS from entryNodeIds within the workflow graph, stopping at nodes that are
    /// successors of <paramref name="ownerNodeId"/> via a different port (branch boundary)~
    /// CopilotNote: mirrors <c>LoopExecutorActor.ComputeBodyScope</c>~ 🔁
    /// </summary>
    public static IReadOnlyCollection<string> ComputeScope(
        WorkflowDefinition definition,
        string ownerNodeId,
        IReadOnlyList<string> entryNodeIds)
    {
        if (entryNodeIds.Count == 0) return Array.Empty<string>();

        var scope = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>(entryNodeIds);

        while (queue.Count > 0)
        {
            var nodeId = queue.Dequeue();
            if (!scope.Add(nodeId)) continue;

            foreach (var conn in definition.Connections)
            {
                if (conn.SourceNodeId != nodeId) continue;
                if (conn.TargetNodeId == ownerNodeId) continue; // avoid looping back~
                if (!scope.Contains(conn.TargetNodeId))
                {
                    queue.Enqueue(conn.TargetNodeId);
                }
            }
        }

        return scope;
    }

    private void SpawnSubGraph(
        string subGraphId,
        List<string> entryNodes,
        IReadOnlyCollection<string> scope,
        Dictionary<string, object?>? inputs)
    {
        var subGraphInputs = inputs ?? new Dictionary<string, object?>();
        var actorName = $"tc-{subGraphId}-{_tryCatchNodeId.Replace(".", "-")}";

        var child = Context.ActorOf(
            SubGraphExecutor.Props(
                _executionId,
                _definition,
                scope,
                entryNodes,
                subGraphInputs,
                _serviceProvider,
                subGraphId: $"{subGraphId}:{_tryCatchNodeId}",
                parentToken: _linkedCts.Token),
            actorName);

        Context.Watch(child);

        _log.Info(
            "🛡️ TryCatch '{TryCatchNodeId}': spawned {Phase} sub-graph '{ActorName}' " +
            "({EntryCount} entry nodes, {ScopeCount} scope nodes)",
            _tryCatchNodeId, subGraphId, actorName, entryNodes.Count, scope.Count);
    }

    #endregion
}






