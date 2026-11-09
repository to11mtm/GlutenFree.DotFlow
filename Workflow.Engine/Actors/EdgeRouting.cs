// <copyright file="EdgeRouting.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Engine.Actors;

using System;
using System.Collections.Generic;
using System.Linq;
using LanguageExt;
using Workflow.Core.Models;
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
    /// <returns>True when routing must go through <see cref="Fired"/>.</returns>
    public static bool IsConditional(WorkflowDefinition definition, string nodeId)
        => definition.Connections.Exists(c => c.SourceNodeId == nodeId && (!string.IsNullOrWhiteSpace(c.Condition) || c.Priority > 0));

    /// <summary>
    /// Works out which outgoing connections fire, building the condition context the way the
    /// executors need it: the run's variables, plus the outputs of the node and its ancestors.
    /// </summary>
    /// <param name="definition">The workflow.</param>
    /// <param name="nodeId">The node that completed.</param>
    /// <param name="activePorts">The ports the module activated (empty = all).</param>
    /// <param name="outputs">The node's (shaped) outputs.</param>
    /// <param name="services">For the binder / expression evaluator.</param>
    /// <param name="variables">The run's variables, including the node's own writes.</param>
    /// <param name="completedOutputs">Completed nodes' outputs (outer scopes first).</param>
    /// <returns>The connections that fire.</returns>
    /// <exception cref="EdgeConditionException">A condition couldn't be evaluated.</exception>
    public static IReadOnlyList<ConnectionDefinition> Fired(
        WorkflowDefinition definition,
        string nodeId,
        Arr<string> activePorts,
        IReadOnlyDictionary<string, object?> outputs,
        IServiceProvider services,
        IReadOnlyDictionary<string, object?> variables,
        params IReadOnlyDictionary<string, Dictionary<string, object?>>?[] completedOutputs)
    {
        ArgumentNullException.ThrowIfNull(services);

        var binder = services.GetService(typeof(IPropertyBinder)) as IPropertyBinder
                     ?? new PropertyBinder(services.GetService(typeof(Workflow.Core.Abstractions.IExpressionEvaluator)) as Workflow.Core.Abstractions.IExpressionEvaluator);
        var view = NodeAncestry.ViewFor(definition, nodeId, completedOutputs);
        var nodeOutputs = new Dictionary<string, IReadOnlyDictionary<string, object?>>(view.Outputs, StringComparer.Ordinal)
        {
            [nodeId] = outputs,
        };

        return Fired(definition, nodeId, activePorts, outputs, binder, new PropertyBindingContext(variables, nodeOutputs, services));
    }

    /// <summary>Works out which outgoing connections fire.</summary>
    /// <param name="definition">The workflow.</param>
    /// <param name="nodeId">The node that completed.</param>
    /// <param name="activePorts">The ports the module activated (empty = all).</param>
    /// <param name="outputs">The node's (shaped) outputs — each edge's <c>value</c> is its port's output.</param>
    /// <param name="binder">Evaluates conditions.</param>
    /// <param name="context">Variables and upstream outputs for conditions.</param>
    /// <returns>The connections that fire.</returns>
    /// <exception cref="EdgeConditionException">A condition couldn't be evaluated.</exception>
    public static IReadOnlyList<ConnectionDefinition> Fired(
        WorkflowDefinition definition,
        string nodeId,
        Arr<string> activePorts,
        IReadOnlyDictionary<string, object?> outputs,
        IPropertyBinder binder,
        PropertyBindingContext context)
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
                if (Passes(connection))
                {
                    fired.Add(connection);
                }
            }

            foreach (var (connection, _) in port.Where(x => x.Connection.Priority > 0).OrderBy(x => x.Connection.Priority).ThenBy(x => x.Index))
            {
                if (Passes(connection))
                {
                    fired.Add(connection);
                    break; // first true wins~
                }
            }
        }

        return fired;

        bool Passes(ConnectionDefinition connection)
        {
            if (string.IsNullOrWhiteSpace(connection.Condition))
            {
                return true;
            }

            outputs.TryGetValue(connection.SourcePortName, out var value);
            var result = binder.EvaluateCondition(connection.Condition, context, value);
            return result.Failure is null
                ? result.Value
                : throw new EdgeConditionException(connection, result.Failure);
        }
    }
}

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
