// <copyright file="EdgeRouting.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Engine.Actors;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using LanguageExt;
using Workflow.Core.Models;
using Workflow.Engine.Messages;
using Workflow.Modules.Binding;

/// <summary>
/// 🔀 Stage 7.2 — conditional edges: which of a completed node's outgoing connections fire, given
/// their <see cref="ConnectionDefinition.Condition"/> and <see cref="ConnectionDefinition.Priority"/>.
/// See <c>docs/conditional-edges.md</c>.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>Only connections on <b>active ports</b> are considered (a module's ActivePorts still
///         decide first; empty = all ports).</item>
///   <item><b>Priority 0</b> (the default): independent — fires when it has no condition or its
///         condition is true.</item>
///   <item><b>Priority 1 and up</b>, per output port: an if / else-if / else chain — checked in
///         Priority order (ties: definition order); only the first with no condition or a true
///         condition fires, and later conditions aren't evaluated.</item>
///   <item>A condition that can't be evaluated throws <see cref="EdgeConditionException"/>; the
///         executor turns that into a failure of the source node.</item>
/// </list>
/// </remarks>
public static class EdgeRouting
{
    /// <summary>Whether any outgoing connection of the node has a condition or a priority.</summary>
    /// <param name="definition">The workflow.</param>
    /// <param name="nodeId">The node.</param>
    /// <returns>True when routing must go through <see cref="FiredAsync"/>.</returns>
    public static bool IsConditional(WorkflowDefinition definition, string nodeId)
        => definition.Connections.Exists(c => c.SourceNodeId == nodeId && (!string.IsNullOrWhiteSpace(c.Condition) || c.Priority > 0));

    /// <summary>
    /// Builds what <see cref="FiredAsync"/> needs to evaluate conditions: the binder, plus a context
    /// with the run's variables and the outputs of the node and its ancestors. Call it on the
    /// executor's thread: it copies what it reads, so the evaluation can run elsewhere.
    /// </summary>
    /// <param name="definition">The workflow.</param>
    /// <param name="nodeId">The node that completed.</param>
    /// <param name="outputs">The node's (shaped) outputs.</param>
    /// <param name="services">For the binder / expression evaluator.</param>
    /// <param name="variables">The run's variables, including the node's own writes (not copied: pass a fresh dictionary).</param>
    /// <param name="completedOutputs">Completed nodes' outputs (outer scopes first).</param>
    /// <returns>The binder and the condition context.</returns>
    public static (IPropertyBinder Binder, PropertyBindingContext Context) ConditionContext(
        WorkflowDefinition definition,
        string nodeId,
        IReadOnlyDictionary<string, object?> outputs,
        IServiceProvider services,
        IReadOnlyDictionary<string, object?> variables,
        params IReadOnlyDictionary<string, Dictionary<string, object?>>?[] completedOutputs)
    {
        ArgumentNullException.ThrowIfNull(services);

        var binder = services.GetService(typeof(IPropertyBinder)) as IPropertyBinder
                     ?? new PropertyBinder(services.GetService(typeof(Workflow.Core.Abstractions.IExpressionEvaluator)) as Workflow.Core.Abstractions.IExpressionEvaluator);
        var view = NodeAncestry.ViewFor(definition, nodeId, completedOutputs); // a copy of the outer map~
        var nodeOutputs = new Dictionary<string, IReadOnlyDictionary<string, object?>>(view.Outputs, StringComparer.Ordinal)
        {
            [nodeId] = outputs,
        };

        return (binder, new PropertyBindingContext(variables, nodeOutputs, services));
    }

    /// <summary>
    /// Decides a completed node's edges in a task and pipes an <see cref="EdgesDecided"/> back to
    /// <paramref name="self"/>. The executor routes (or fails the node) when that arrives; until
    /// then the node is still running, and the executor keeps handling other messages.
    /// </summary>
    /// <param name="completed">The node's completion (handed back unchanged).</param>
    /// <param name="definition">The workflow.</param>
    /// <param name="outputs">The node's (shaped) outputs.</param>
    /// <param name="condition">From <see cref="ConditionContext"/>, built on the executor's thread.</param>
    /// <param name="self">The executor.</param>
    /// <param name="cancellationToken">The run's token.</param>
    public static void DecideInBackground(
        NodeExecutionCompleted completed,
        WorkflowDefinition definition,
        IReadOnlyDictionary<string, object?> outputs,
        (IPropertyBinder Binder, PropertyBindingContext Context) condition,
        IActorRef self,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(completed);

        Task.Run(() => FiredAsync(definition, completed.NodeId, completed.ActivePorts, outputs, condition.Binder, condition.Context, cancellationToken), cancellationToken)
            .ContinueWith(
                task => task.IsCompletedSuccessfully
                    ? new EdgesDecided(completed, task.Result, null)
                    : new EdgesDecided(
                        completed,
                        null,
                        task.IsCanceled ? new OperationCanceledException("Edge conditions were cancelled") : task.Exception!.GetBaseException()),
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default)
            .PipeTo(self);
    }

    /// <summary>
    /// Works out which outgoing connections fire. Conditions are awaited one at a time, in the order
    /// the rules need (a Priority chain stops at its first true edge).
    /// </summary>
    /// <remarks>
    /// The executors run this in a task (<c>Task.Run</c>) and pipe the result back, so a slow
    /// condition never holds their thread: a binder or evaluator may finish synchronously
    /// (DynamicExpresso does), so awaiting alone wouldn't leave the calling thread.
    /// </remarks>
    /// <param name="definition">The workflow.</param>
    /// <param name="nodeId">The node that completed.</param>
    /// <param name="activePorts">The ports the module activated (empty = all).</param>
    /// <param name="outputs">The node's (shaped) outputs — each edge's <c>value</c> is its port's output.</param>
    /// <param name="binder">Evaluates conditions.</param>
    /// <param name="context">Variables and upstream outputs for conditions.</param>
    /// <param name="cancellationToken">Cancels the evaluation.</param>
    /// <returns>The connections that fire.</returns>
    /// <exception cref="EdgeConditionException">A condition couldn't be evaluated.</exception>
    public static async Task<IReadOnlyList<ConnectionDefinition>> FiredAsync(
        WorkflowDefinition definition,
        string nodeId,
        Arr<string> activePorts,
        IReadOnlyDictionary<string, object?> outputs,
        IPropertyBinder binder,
        PropertyBindingContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(binder);

        var outgoing = definition.Connections
            .Select((c, index) => (Connection: c, Index: index))
            .Where(x => x.Connection.SourceNodeId == nodeId
                        && (activePorts.Count == 0 || activePorts.Contains(x.Connection.SourcePortName)))
            .ToList();

        var fired = new List<ConnectionDefinition>();
        foreach (var port in outgoing.GroupBy(x => x.Connection.SourcePortName, StringComparer.Ordinal))
        {
            foreach (var (connection, _) in port.Where(x => x.Connection.Priority <= 0))
            {
                if (await PassesAsync(connection).ConfigureAwait(false))
                {
                    fired.Add(connection);
                }
            }

            foreach (var (connection, _) in port.Where(x => x.Connection.Priority > 0).OrderBy(x => x.Connection.Priority).ThenBy(x => x.Index))
            {
                if (await PassesAsync(connection).ConfigureAwait(false))
                {
                    fired.Add(connection);
                    break; // first true wins~
                }
            }
        }

        return fired;

        async Task<bool> PassesAsync(ConnectionDefinition connection)
        {
            if (string.IsNullOrWhiteSpace(connection.Condition))
            {
                return true;
            }

            outputs.TryGetValue(connection.SourcePortName, out var value);
            var result = await binder.EvaluateConditionAsync(connection.Condition, context, value, cancellationToken).ConfigureAwait(false);
            return result.Failure is null
                ? result.Value
                : throw new EdgeConditionException(connection, result.Failure);
        }
    }
}

/// <summary>
/// 🔀 A completed node's edges were decided off the executor's thread (see
/// <see cref="EdgeRouting.DecideInBackground"/>).
/// </summary>
/// <param name="Completed">The node's completion, as the executor received it.</param>
/// <param name="Fired">The connections that fire, or null when <paramref name="Error"/> is set.</param>
/// <param name="Error">Why the edges couldn't be decided (usually <see cref="EdgeConditionException"/>);
/// the executor fails the source node with it.</param>
public sealed record EdgesDecided(NodeExecutionCompleted Completed, IReadOnlyList<ConnectionDefinition>? Fired, Exception? Error);

/// <summary>🔀 Stage 7.2 — an edge condition couldn't be evaluated (counts as the source node failing).</summary>
public sealed class EdgeConditionException : InvalidOperationException
{
    /// <summary>Initializes a new instance of the <see cref="EdgeConditionException"/> class.</summary>
    /// <param name="connection">The connection.</param>
    /// <param name="failure">Why.</param>
    public EdgeConditionException(ConnectionDefinition connection, string failure)
        : base($"The condition on the connection {connection.SourceNodeId}.{connection.SourcePortName} → " +
               $"{connection.TargetNodeId}.{connection.TargetPortName} couldn't be evaluated: {failure}")
        => this.Connection = connection;

    /// <summary>Gets the connection.</summary>
    public ConnectionDefinition Connection { get; }
}
