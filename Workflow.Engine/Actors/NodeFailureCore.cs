// <copyright file="NodeFailureCore.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Engine.Actors;

using System;
using Akka.Event;
using Workflow.Core.Models;

/// <summary>
/// Shared per-node error-handling decision tree (boundary → retry / continue / fail)~ 🛡️✨
/// </summary>
/// <remarks>
/// <para>
/// §12.4: extracted from <c>WorkflowExecutor.HandleNodeFailure</c> so that
/// <see cref="SubGraphExecutor"/> (loop iterations, parallel branches, try/catch and transaction
/// bodies) honours a node's <see cref="ErrorHandling"/> and <see cref="RetryPolicy"/> exactly like a
/// top-level node. Before this, sub-graphs failed fast on any node failure.
/// </para>
/// <para>
/// Same design as <see cref="DispatchCore"/>: all mutable state (retry attempt counts, failed /
/// completed sets) stays in the OWNING actor. The core reads it via <c>Func</c> delegates and asks
/// the actor to act via <c>Action</c> delegates. Not thread-safe — single actor use only.
/// </para>
/// </remarks>
public sealed class NodeFailureCore
{
    private readonly Func<string, NodeDefinition?> _getNode;
    private readonly ErrorHandling? _workflowErrorHandling;
    private readonly Func<string, Exception, bool> _tryHandleWithBoundary;
    private readonly Func<string, int> _retryAttempts;
    private readonly Action<RetryDecision> _scheduleRetry;
    private readonly Action<string> _continueAfterFailure;
    private readonly Action<string, Exception> _fail;
    private readonly ILoggingAdapter _log;
    private readonly Action<string, string, Exception>? _runErrorHandler;
    private readonly Guid _executionId;

    /// <summary>
    /// Initializes a new instance of the <see cref="NodeFailureCore"/> class.
    /// </summary>
    /// <param name="getNode">Looks up a node definition by id.</param>
    /// <param name="workflowErrorHandling">Workflow-level fallback when a node has no own error handling.</param>
    /// <param name="tryHandleWithBoundary">
    /// Gives the actor's error-boundary stack first refusal; returns <c>true</c> if it handled the
    /// failure. Sub-graphs have no boundary stack and return <c>false</c> (their owning construct —
    /// e.g. a try/catch — sees the sub-graph failure instead).
    /// </param>
    /// <param name="retryAttempts">How many retries of this node have already been scheduled.</param>
    /// <param name="scheduleRetry">Records the attempt and schedules the delayed re-run.</param>
    /// <param name="continueAfterFailure">Treats the failed node as done and routes its successors.</param>
    /// <param name="fail">Fails the owner (workflow or sub-graph) because of this node.</param>
    /// <param name="log">The owning actor's logger.</param>
    /// <param name="runErrorHandler">
    /// §12.7: runs a failed node's error handler (failed node id, handler id, error). Null = an
    /// owner that can't, so <see cref="ErrorBehavior.UseErrorHandler"/> fails as before.
    /// </param>
    /// <param name="executionId">§12.7: the run, for <see cref="ErrorHandlerCounters"/>.</param>
    public NodeFailureCore(
        Func<string, NodeDefinition?> getNode,
        ErrorHandling? workflowErrorHandling,
        Func<string, Exception, bool> tryHandleWithBoundary,
        Func<string, int> retryAttempts,
        Action<RetryDecision> scheduleRetry,
        Action<string> continueAfterFailure,
        Action<string, Exception> fail,
        ILoggingAdapter log,
        Action<string, string, Exception>? runErrorHandler = null,
        Guid executionId = default)
    {
        _getNode = getNode;
        _workflowErrorHandling = workflowErrorHandling;
        _tryHandleWithBoundary = tryHandleWithBoundary;
        _retryAttempts = retryAttempts;
        _scheduleRetry = scheduleRetry;
        _continueAfterFailure = continueAfterFailure;
        _fail = fail;
        _log = log;
        _runErrorHandler = runErrorHandler;
        _executionId = executionId;
    }

    /// <summary>
    /// Decides what a node failure means, after the owner has recorded the node as failed.
    /// </summary>
    /// <param name="nodeId">The failed node.</param>
    /// <param name="error">Why it failed.</param>
    public void Handle(string nodeId, Exception error)
    {
        if (_tryHandleWithBoundary(nodeId, error))
        {
            return;
        }

        var nodeDef = _getNode(nodeId);
        var errorHandling = nodeDef?.ErrorHandling ?? _workflowErrorHandling;
        var behavior = errorHandling?.OnErrorBehavior ?? ErrorBehavior.Fail;

        switch (behavior)
        {
            case ErrorBehavior.Retry:
                HandleRetry(nodeId, error, nodeDef, onExhausted: () => _fail(nodeId, error));
                break;

            case ErrorBehavior.Continue:
                _log.Warning("⚡ Continue-on-error: proceeding despite failure in node {NodeId}", nodeId);
                _continueAfterFailure(nodeId);
                break;

            case ErrorBehavior.UseErrorHandler:
                // §12.7: retry first when the node has a policy; the handler runs once retries are
                // exhausted (or straight away without a policy)~ 🚑
                if (nodeDef?.RetryPolicy is not null)
                {
                    HandleRetry(nodeId, error, nodeDef, onExhausted: () => RunErrorHandler(nodeId, error, errorHandling!));
                }
                else
                {
                    RunErrorHandler(nodeId, error, errorHandling!);
                }

                break;

            case ErrorBehavior.Fail:
            default:
                _log.Error("🛑 Fail-fast: stopping because of failure in node {NodeId}", nodeId);
                _fail(nodeId, error);
                break;
        }
    }

    /// <summary>
    /// §12.7: run the node's error handler — unless it has failed more than
    /// <see cref="ErrorHandling.MaxConsecutiveErrors"/> times in a row, which fails the owner.
    /// </summary>
    private void RunErrorHandler(string nodeId, Exception error, ErrorHandling errorHandling)
    {
        var handlerId = errorHandling.ErrorNodeId;
        if (string.IsNullOrWhiteSpace(handlerId) || _runErrorHandler is null || _getNode(handlerId) is null)
        {
            _log.Error(
                "🛑 Node {NodeId} uses an error handler, but '{HandlerId}' isn't a node in this workflow — failing~",
                nodeId,
                handlerId ?? "(none)");
            _fail(nodeId, error);
            return;
        }

        var handled = ErrorHandlerCounters.Increment(_executionId, nodeId);
        if (errorHandling.MaxConsecutiveErrors is { } max && handled > max)
        {
            _log.Error(
                "🛑 Node {NodeId} failed {Count} times in a row (MaxConsecutiveErrors = {Max}) — failing~",
                nodeId,
                handled,
                max);
            _fail(nodeId, new InvalidOperationException(
                $"Node '{nodeId}' failed {handled} times in a row, more than its MaxConsecutiveErrors ({max}). Last error: {error.Message}",
                error));
            return;
        }

        _runErrorHandler(nodeId, handlerId, error);
    }

    private void HandleRetry(string nodeId, Exception error, NodeDefinition? nodeDef, Action onExhausted)
    {
        var retryPolicy = nodeDef?.RetryPolicy ?? RetryPolicy.Default;
        var attempt = _retryAttempts(nodeId) + 1;

        if (attempt >= retryPolicy.MaxAttempts)
        {
            _log.Error(
                "🛑 Node {NodeId} exhausted all {MaxAttempts} retry attempts~ 💔",
                nodeId,
                retryPolicy.MaxAttempts);
            onExhausted();
            return;
        }

        // Exponential backoff: baseDelay * multiplier^(attempt-1), capped at MaxDelayMs~ 📈
        var delayMs = (int)Math.Min(
            retryPolicy.DelayMs * Math.Pow(retryPolicy.BackoffMultiplier, attempt - 1),
            retryPolicy.MaxDelayMs);

        _log.Warning(
            "🔄 Scheduling retry {Attempt}/{MaxAttempts} for node {NodeId} in {DelayMs}ms (backoff)~ UwU",
            attempt,
            retryPolicy.MaxAttempts,
            nodeId,
            delayMs);

        _scheduleRetry(new RetryDecision(nodeId, error, attempt, retryPolicy.MaxAttempts, TimeSpan.FromMilliseconds(delayMs)));
    }

    /// <summary>A retry the owning actor should record and schedule.</summary>
    /// <param name="NodeId">The node to re-run.</param>
    /// <param name="Error">The failure being retried.</param>
    /// <param name="Attempt">This retry's attempt number (1-based).</param>
    /// <param name="MaxAttempts">The policy's attempt limit.</param>
    /// <param name="Delay">Backoff delay before re-running.</param>
    public sealed record RetryDecision(string NodeId, Exception Error, int Attempt, int MaxAttempts, TimeSpan Delay);
}
