// <copyright file="ErrorHandlerCore.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Engine.Actors;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Akka.Actor;
using Akka.Event;
using Workflow.Core.Models;
using Workflow.Engine.Models;

/// <summary>
/// 🚑 §12.7 — <see cref="ErrorBehavior.UseErrorHandler"/>: when a node fails (after its retries),
/// run the node named by <see cref="ErrorHandling.ErrorNodeId"/> and the path after it, with the
/// error available as <c>{{Variable.error}}</c>. Shared by <see cref="WorkflowExecutor"/> and
/// <see cref="SubGraphExecutor"/> (same pattern as <see cref="ConstructSpawnCore"/>).
/// </summary>
/// <remarks>
/// <para>Semantics (decided 2026-11-08 — see <c>docs/error-handling.md</c>):</para>
/// <list type="bullet">
///   <item>A node referenced as an <c>ErrorNodeId</c> never starts with the run on its own; it runs
///         when a failure routes to it (or through its own incoming connections, if it has any).</item>
///   <item>The failing node's downstream is skipped; the handler's own outgoing path continues, like
///         a catch body. The run carries on unless something on the handler path fails.</item>
///   <item>The handler path runs in a <see cref="SubGraphExecutor"/>: its scope is the handler plus
///         every node reachable <b>only</b> through it; <c>error</c> is a body-scoped variable.</item>
/// </list>
/// <para>Single actor use only (like the other cores).</para>
/// </remarks>
public sealed class ErrorHandlerCore
{
    private static readonly ConditionalWeakTable<WorkflowDefinition, Dictionary<string, IReadOnlyCollection<string>>> ScopeCache = new();

    private readonly WorkflowDefinition _definition;
    private readonly Guid _executionId;
    private readonly IServiceProvider _serviceProvider;
    private readonly Func<Props, string, IActorRef> _spawnChild;
    private readonly Func<CancellationToken> _cancellationToken;
    private readonly Func<IReadOnlyDictionary<string, object?>> _currentVariables;
    private readonly Func<IReadOnlyList<ScopeStep>> _scopePath;
    private readonly Func<IReadOnlyDictionary<string, Dictionary<string, object?>>> _outerOutputs;
    private readonly ILoggingAdapter _log;
    private readonly Dictionary<IActorRef, string> _running = new();
    private int _sequence;

    /// <summary>Initializes a new instance of the <see cref="ErrorHandlerCore"/> class.</summary>
    /// <param name="definition">The workflow.</param>
    /// <param name="executionId">The run.</param>
    /// <param name="serviceProvider">Passed on to the handler sub-graph.</param>
    /// <param name="spawnChild">Creates and watches a child actor of the owner.</param>
    /// <param name="cancellationToken">The owner's token.</param>
    /// <param name="currentVariables">The variables the handler path starts from.</param>
    /// <param name="scopePath">The owner's scope path (history).</param>
    /// <param name="outerOutputs">Completed nodes' outputs, for <c>{{nodeId.port}}</c> on the handler path.</param>
    /// <param name="log">The owner's logger.</param>
    public ErrorHandlerCore(
        WorkflowDefinition definition,
        Guid executionId,
        IServiceProvider serviceProvider,
        Func<Props, string, IActorRef> spawnChild,
        Func<CancellationToken> cancellationToken,
        Func<IReadOnlyDictionary<string, object?>> currentVariables,
        Func<IReadOnlyList<ScopeStep>> scopePath,
        Func<IReadOnlyDictionary<string, Dictionary<string, object?>>> outerOutputs,
        ILoggingAdapter log)
    {
        _definition = definition;
        _executionId = executionId;
        _serviceProvider = serviceProvider;
        _spawnChild = spawnChild;
        _cancellationToken = cancellationToken;
        _currentVariables = currentVariables;
        _scopePath = scopePath;
        _outerOutputs = outerOutputs;
        _log = log;
    }

    /// <summary>Gets how many handler paths are still running (the owner isn't done while any are).</summary>
    public int RunningCount => _running.Count;

    /// <summary>Every node referenced as an <c>ErrorNodeId</c> (node- or workflow-level) that exists.</summary>
    /// <param name="definition">The workflow.</param>
    /// <returns>The handler node ids.</returns>
    public static IReadOnlySet<string> HandlerIds(WorkflowDefinition definition)
    {
        var nodeIds = definition.Nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        var ids = definition.Nodes
            .Select(n => n.ErrorHandling?.ErrorNodeId)
            .Append(definition.ErrorHandling?.ErrorNodeId)
            .Where(id => id is not null && nodeIds.Contains(id))
            .Select(id => id!)
            .ToHashSet(StringComparer.Ordinal);
        return ids;
    }

    /// <summary>
    /// The handler path for a handler node: the handler plus every node reachable only through it
    /// (all of a node's predecessors are already in the path). Cached per definition.
    /// </summary>
    /// <param name="definition">The workflow.</param>
    /// <param name="handlerId">The handler node.</param>
    /// <returns>The node ids the handler sub-graph runs.</returns>
    public static IReadOnlyCollection<string> ScopeOf(WorkflowDefinition definition, string handlerId)
    {
        var perHandler = ScopeCache.GetValue(definition, _ => new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal));
        lock (perHandler)
        {
            if (perHandler.TryGetValue(handlerId, out var cached))
            {
                return cached;
            }

            var predecessors = definition.Connections
                .GroupBy(c => c.TargetNodeId, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Select(c => c.SourceNodeId).Distinct().ToList(), StringComparer.Ordinal);
            var scope = new HashSet<string>(StringComparer.Ordinal) { handlerId };
            var changed = true;
            while (changed)
            {
                changed = false;
                foreach (var c in definition.Connections)
                {
                    if (scope.Contains(c.SourceNodeId)
                        && !scope.Contains(c.TargetNodeId)
                        && predecessors[c.TargetNodeId].All(scope.Contains))
                    {
                        scope.Add(c.TargetNodeId);
                        changed = true;
                    }
                }
            }

            perHandler[handlerId] = scope.ToArray();
            return perHandler[handlerId];
        }
    }

    /// <summary>
    /// Nodes the top level must not start on its own: the paths of handler nodes that have no
    /// incoming connections (they run only when a failure routes to them).
    /// </summary>
    /// <param name="definition">The workflow.</param>
    /// <returns>The node ids to treat as skipped unless a handler runs them.</returns>
    public static IReadOnlySet<string> ErrorOnlyNodes(WorkflowDefinition definition)
    {
        var targets = definition.Connections.Select(c => c.TargetNodeId).ToHashSet(StringComparer.Ordinal);
        return HandlerIds(definition)
            .Where(id => !targets.Contains(id))
            .SelectMany(id => ScopeOf(definition, id))
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Runs a handler path for a failed node.</summary>
    /// <param name="failedNodeId">The node that failed.</param>
    /// <param name="handlerId">Its handler.</param>
    /// <param name="error">Why it failed.</param>
    public void Start(string failedNodeId, string handlerId, Exception error)
    {
        var scope = ScopeOf(_definition, handlerId);
        var workflowError = WorkflowError.FromException(error, failedNodeId);
        var sequence = ++_sequence;
        var subGraphId = $"errorHandler:{failedNodeId}#{sequence}";

        var child = _spawnChild(
            SubGraphExecutor.Props(
                _executionId,
                _definition,
                scope,
                new[] { handlerId },
                new Dictionary<string, object?> { ["error"] = workflowError },
                _serviceProvider,
                subGraphId,
                _cancellationToken(),
                BodyVariables.WithBodyContext(_currentVariables(), ("error", workflowError)),
                ScopeStep.Append(_scopePath(), new ScopeStep(failedNodeId, ScopeStep.ErrorHandler)),
                outerOutputs: NodeAncestry.Snapshot(_outerOutputs())),
            $"error-handler-{failedNodeId.Replace(".", "-")}-{sequence}");
        _running[child] = failedNodeId;

        _log.Warning(
            "🚑 Node {NodeId} failed; running its error handler {HandlerId} ({Count} node(s) on the handler path)~",
            failedNodeId,
            handlerId,
            scope.Count);
    }

    /// <summary>Whether <paramref name="actor"/> is a running handler path (and marks it finished).</summary>
    /// <param name="actor">The sender / terminated actor.</param>
    /// <param name="failedNodeId">The node whose failure it handled.</param>
    /// <returns>True when it was one of ours.</returns>
    public bool TryFinish(IActorRef actor, out string failedNodeId)
    {
        if (_running.Remove(actor, out var nodeId))
        {
            failedNodeId = nodeId;
            return true;
        }

        failedNodeId = string.Empty;
        return false;
    }
}

/// <summary>
/// 🚑 §12.7 — handled-failure counts for <see cref="ErrorHandling.MaxConsecutiveErrors"/>, per node
/// per run. Shared across the run's actors because a node in a loop body fails in a different
/// <see cref="SubGraphExecutor"/> each iteration; a success of the node resets its count.
/// </summary>
public static class ErrorHandlerCounters
{
    private static readonly ConcurrentDictionary<(Guid ExecutionId, string NodeId), int> Counts = new();

    /// <summary>Counts one more handled failure of the node.</summary>
    /// <param name="executionId">The run.</param>
    /// <param name="nodeId">The node.</param>
    /// <returns>Its consecutive handled failures, including this one.</returns>
    public static int Increment(Guid executionId, string nodeId) => Counts.AddOrUpdate((executionId, nodeId), 1, (_, n) => n + 1);

    /// <summary>Resets the node's count (it succeeded).</summary>
    /// <param name="executionId">The run.</param>
    /// <param name="nodeId">The node.</param>
    public static void Reset(Guid executionId, string nodeId) => Counts.TryRemove((executionId, nodeId), out _);

    /// <summary>Forgets a finished run.</summary>
    /// <param name="executionId">The run.</param>
    public static void Remove(Guid executionId)
    {
        foreach (var key in Counts.Keys.Where(k => k.ExecutionId == executionId).ToList())
        {
            Counts.TryRemove(key, out _);
        }
    }
}
