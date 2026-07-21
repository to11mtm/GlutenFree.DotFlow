// <copyright file="LoopExecutorActor.cs" company="GlutenFree">
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
using Workflow.Engine.Models;

/// <summary>
/// 🔁 Orchestrates all iterations of a loop module's body sub-graph~
/// Spawned by <c>WorkflowExecutor</c> when a node completion carries a
/// <see cref="LoopRequest"/>. Reports <see cref="LoopCompleted"/> or
/// <see cref="LoopFailed"/> to its parent (<c>WorkflowExecutor</c>) when done~ ✨💖
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: Phase 2.2.2 — this actor owns each iteration:
/// <list type="number">
///   <item>Tells parent <see cref="PushLoopScope"/> before iteration starts.</item>
///   <item>Spawns <see cref="SubGraphExecutor"/> for the body sub-graph.</item>
///   <item>On <see cref="SubGraphCompleted"/>: checks break/continue, merges outputs, advances.</item>
///   <item>On <see cref="SubGraphFailed"/>: respects <c>continueOnError</c> flag.</item>
///   <item>On natural completion: tells parent <see cref="PopLoopScope"/>, then <see cref="LoopCompleted"/>.</item>
/// </list>
/// </para>
/// <para>
/// Break/Continue: detected via sentinel keys <c>__loop_break__</c> / <c>__loop_continue__</c>
/// in <see cref="SubGraphCompleted.BreakRequested"/> / <see cref="SubGraphCompleted.ContinueRequested"/>
/// propagated by SubGraphExecutor~ 🌸.
/// </para>
/// </remarks>
public sealed class LoopExecutorActor : ReceiveActor
{
    private readonly string _loopNodeId;
    private readonly LoopRequest _loop;
    private readonly WorkflowDefinition _definition;
    private readonly IReadOnlyList<string> _bodyEntryNodeIds;
    private readonly IReadOnlyList<string> _bodyScopeNodeIds;
    private readonly Guid _executionId;
    private readonly IServiceProvider _serviceProvider;
    private readonly CancellationTokenSource _linkedCts;
    private readonly ILoggingAdapter _log;

    // ── Iteration state ──────────────────────────────────────────────────────────────────────────
    private IEnumerator<object?>? _itemEnumerator;      // non-null = ForEach-style
    private int _iterationCount;
    private readonly List<object?> _results = new();
    private readonly List<string> _errors = new();
    private readonly Dictionary<string, object?> _currentContext = new();
    private LoopContext? _activeLoopContext;

    /// <summary>
    /// Initializes a new <see cref="LoopExecutorActor"/>.
    /// </summary>
    /// <param name="loopNodeId">Node ID of the loop module that requested iteration. 🆔.</param>
    /// <param name="loop">The loop specification. 🔁.</param>
    /// <param name="definition">Full workflow definition (for sub-graph scoping). 📋.</param>
    /// <param name="bodyEntryNodeIds">Entry nodes for the loop body sub-graph. 🚪.</param>
    /// <param name="bodyScopeNodeIds">Full scope of nodes in the loop body. 🗺️.</param>
    /// <param name="executionId">Parent execution ID for history correlation. 🔑.</param>
    /// <param name="initialContext">Initial variable + input context for condition evaluation. 📦.</param>
    /// <param name="serviceProvider">DI container. 💉.</param>
    /// <param name="parentToken">Hierarchical cancellation token (2.2.0b)~ 🔗🛑.</param>
    public LoopExecutorActor(
        string loopNodeId,
        LoopRequest loop,
        WorkflowDefinition definition,
        IReadOnlyList<string> bodyEntryNodeIds,
        IReadOnlyList<string> bodyScopeNodeIds,
        Guid executionId,
        IReadOnlyDictionary<string, object?> initialContext,
        IServiceProvider serviceProvider,
        CancellationToken parentToken = default)
    {
        _loopNodeId = loopNodeId;
        _loop = loop;
        _definition = definition;
        _bodyEntryNodeIds = bodyEntryNodeIds;
        _bodyScopeNodeIds = bodyScopeNodeIds;
        _executionId = executionId;
        _serviceProvider = serviceProvider;
        _log = Context.GetLogger();

        // Seed current context from initial inputs/variables for condition evaluation
        foreach (var (k, v) in initialContext) _currentContext[k] = v;

        _linkedCts = parentToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(parentToken)
            : new CancellationTokenSource();

        if (loop.Items != null)
            _itemEnumerator = loop.Items.GetEnumerator();

        Receive<SubGraphCompleted>(HandleIterationCompleted);
        Receive<SubGraphFailed>(HandleIterationFailed);
        Receive<CooperativeCancelLoop>(_ =>
        {
            _log.Info("🛑 LoopExecutor '{LoopNodeId}': cooperative cancel received", _loopNodeId);
            _linkedCts.Cancel();
        });
    }

    /// <summary>Creates Props for spawning a LoopExecutorActor~ ✨.</summary>
    public static Props Props(
        string loopNodeId,
        LoopRequest loop,
        WorkflowDefinition definition,
        IReadOnlyList<string> bodyEntryNodeIds,
        IReadOnlyList<string> bodyScopeNodeIds,
        Guid executionId,
        IReadOnlyDictionary<string, object?> initialContext,
        IServiceProvider serviceProvider,
        CancellationToken parentToken = default)
    {
        return Akka.Actor.Props.Create(() => new LoopExecutorActor(
            loopNodeId, loop, definition, bodyEntryNodeIds, bodyScopeNodeIds,
            executionId, initialContext, serviceProvider, parentToken));
    }

    #region Lifecycle

    /// <inheritdoc/>
    protected override void PreStart()
    {
        base.PreStart();
        _log.Info(
            "🔁 LoopExecutor '{LoopNodeId}' starting under execution {ExecutionId} " +
            "(items={ItemCount}, maxIter={MaxIter}, continueOnError={ContinueOnError})",
            _loopNodeId,
            _executionId,
            _loop.Items?.Count.ToString() ?? "condition-driven",
            _loop.MaxIterations,
            _loop.ContinueOnError);

        StartNextIteration();
    }

    /// <inheritdoc/>
    protected override void PostStop()
    {
        _itemEnumerator?.Dispose();
        _linkedCts.Dispose();
        base.PostStop();
    }

    #endregion

    #region Iteration Lifecycle

    /// <summary>
    /// Evaluates whether another iteration should run, then either starts the body
    /// sub-graph or completes the loop~ 🔄.
    /// </summary>
    private void StartNextIteration()
    {
        if (_linkedCts.IsCancellationRequested)
        {
            ReportFailed(new OperationCanceledException("Loop cancelled"), null);
            return;
        }

        // Guard: maxIterations
        if (_iterationCount >= _loop.MaxIterations)
        {
            ReportFailed(
                new InvalidOperationException(
                    $"Loop '{_loopNodeId}' exceeded maxIterations limit of {_loop.MaxIterations}. " +
                    "Increase maxIterations or verify your loop body terminates correctly~ 🔢"),
                null);
            return;
        }

        // ── ForEach-style: advance enumerator ────────────────────────────────────────────────
        if (_itemEnumerator != null)
        {
            if (!_itemEnumerator.MoveNext())
            {
                ReportCompleted();
                return;
            }

            var item = _itemEnumerator.Current;
            RunIteration(item, _iterationCount);
            return;
        }

        // ── While-style: evaluate ContinueCondition ──────────────────────────────────────────
        if (_loop.ContinueCondition != null)
        {
            // Fire-and-forget async condition evaluation; result piped back as a local record
            var condCtx = new Dictionary<string, object?>(_currentContext);
            _loop.ContinueCondition(condCtx, _linkedCts.Token)
                .AsTask()
                .PipeTo(Self,
                    success: shouldContinue => new ConditionResult(shouldContinue),
                    failure: ex => new ConditionFailed(ex));

            // Switch to condition-awaiting receive mode
            BecomeConditionAwaiting();
            return;
        }

        // No items and no condition — complete immediately (e.g. empty WhileModule)
        ReportCompleted();
    }

    /// <summary>Temporarily becomes to await a condition evaluation result~ 🔄.</summary>
    private void BecomeConditionAwaiting()
    {
        Become(() =>
        {
            Receive<ConditionResult>(r =>
            {
                Become(DefaultBehavior);
                if (!r.Value)
                {
                    ReportCompleted();
                }
                else
                {
                    RunIteration(null, _iterationCount);
                }
            });

            Receive<ConditionFailed>(f =>
            {
                Become(DefaultBehavior);
                ReportFailed(f.Error, null);
            });
        });
    }

    /// <summary>Restores the default message handlers after condition awaiting~ ✨.</summary>
    private void DefaultBehavior()
    {
        Receive<SubGraphCompleted>(HandleIterationCompleted);
        Receive<SubGraphFailed>(HandleIterationFailed);
        Receive<CooperativeCancelLoop>(_ =>
        {
            _log.Info("🛑 LoopExecutor '{LoopNodeId}': cooperative cancel received", _loopNodeId);
            _linkedCts.Cancel();
        });
    }

    /// <summary>
    /// Runs one body iteration by pushing loop scope and spawning a SubGraphExecutor~ ⚡.
    /// </summary>
    private void RunIteration(object? item, int iterationIndex)
    {
        _iterationCount++;

        // Build and push the LoopContext for this iteration
        var loopCtx = new LoopContext(_loopNodeId, _iterationCount, parentScope: null);
        loopCtx.SetCurrentElement(item, iterationIndex);
        _activeLoopContext = loopCtx;

        Context.Parent.Tell(new PushLoopScope(loopCtx));

        // Build per-iteration inputs: merge current context + current item
        var iterInputs = new Dictionary<string, object?>(_currentContext);
        if (item != null)
        {
            iterInputs["item"] = item;
            iterInputs["index"] = iterationIndex;
        }

        var subGraphId = $"{_loopNodeId}-iter-{_iterationCount}";

        var subGraphActor = Context.ActorOf(
            SubGraphExecutor.Props(
                _executionId,
                _definition,
                _bodyScopeNodeIds,
                _bodyEntryNodeIds,
                iterInputs,
                _serviceProvider,
                subGraphId,
                _linkedCts.Token),
            $"loop-body-{_loopNodeId.Replace(".", "-")}-{_iterationCount}");

        Context.Watch(subGraphActor);

        _log.Debug(
            "🔁 LoopExecutor '{LoopNodeId}': starting iteration {Iteration} (item={Item})",
            _loopNodeId, _iterationCount, item ?? "<condition-driven>");
    }

    #endregion

    #region Completion / Failure Handlers

    private void HandleIterationCompleted(SubGraphCompleted msg)
    {
        Context.Unwatch(Sender);

        // Pop current loop scope
        if (_activeLoopContext != null)
            Context.Parent.Tell(new PopLoopScope(_activeLoopContext.LoopId));

        // Merge iteration outputs into running context
        foreach (var (k, v) in msg.Outputs)
            _currentContext[k] = v;

        // Collect result payload (skip internal sentinel keys)
        var iterResult = msg.Outputs
            .Where(kv => !kv.Key.StartsWith("__loop_"))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        _results.Add(iterResult);

        // Handle break
        if (msg.BreakRequested)
        {
            _log.Info("⏹️ LoopExecutor '{LoopNodeId}': break requested at iteration {Iter}",
                _loopNodeId, _iterationCount);
            ReportCompleted();
            return;
        }

        // Handle continue — advance without error collection
        if (msg.ContinueRequested)
        {
            _log.Debug("⏭️ LoopExecutor '{LoopNodeId}': continue at iteration {Iter}",
                _loopNodeId, _iterationCount);
            StartNextIteration();
            return;
        }

        StartNextIteration();
    }

    private void HandleIterationFailed(SubGraphFailed msg)
    {
        Context.Unwatch(Sender);

        if (_activeLoopContext != null)
            Context.Parent.Tell(new PopLoopScope(_activeLoopContext.LoopId));

        if (_loop.ContinueOnError)
        {
            _log.Warning(
                "⚠️ LoopExecutor '{LoopNodeId}': iteration {Iter} failed (continueOnError=true): {Error}",
                _loopNodeId, _iterationCount, msg.Error.Message);

            _errors.Add(msg.Error.Message);
            _results.Add(null);
            StartNextIteration();
        }
        else
        {
            ReportFailed(msg.Error, msg.FailedNodeId);
        }
    }

    private void ReportCompleted()
    {
        var outputs = new Dictionary<string, object?>
        {
            ["results"] = _results,
            ["count"] = _iterationCount,
            ["errors"] = _errors,
        };

        _log.Info(
            "✅ LoopExecutor '{LoopNodeId}' completed: {Count} iteration(s), {ErrCount} error(s)",
            _loopNodeId, _iterationCount, _errors.Count);

        Context.Parent.Tell(new LoopCompleted(_loopNodeId, outputs));
        Context.Stop(Self);
    }

    private void ReportFailed(Exception error, string? failedNodeId)
    {
        _log.Error(
            error,
            "❌ LoopExecutor '{LoopNodeId}' failed at iteration {Iter}: {Error}",
            _loopNodeId, _iterationCount, error.Message);

        Context.Parent.Tell(new LoopFailed(_loopNodeId, error, failedNodeId));
        Context.Stop(Self);
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Computes the scope nodes for the loop body by BFS from body entry nodes,
    /// stopping at the loop node itself (prevents re-including the loop node)~ 🗺️.
    /// </summary>
    public static List<string> ComputeBodyScope(
        WorkflowDefinition definition,
        string loopNodeId,
        IReadOnlyList<string> bodyEntryNodeIds)
    {
        var scope = new System.Collections.Generic.HashSet<string>();
        var queue = new Queue<string>(bodyEntryNodeIds);

        while (queue.Count > 0)
        {
            var nodeId = queue.Dequeue();
            if (!scope.Add(nodeId)) continue;

            foreach (var conn in definition.Connections.Where(c => c.SourceNodeId == nodeId))
            {
                if (conn.TargetNodeId != loopNodeId)
                    queue.Enqueue(conn.TargetNodeId);
            }
        }

        return scope.ToList();
    }

    // ── Private result messages for async condition evaluation ───────────────────────────────────
    private sealed record ConditionResult(bool Value);
    private sealed record ConditionFailed(Exception Error);

    #endregion
}


