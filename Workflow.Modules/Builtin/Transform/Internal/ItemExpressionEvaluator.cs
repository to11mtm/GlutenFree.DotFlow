// <copyright file="ItemExpressionEvaluator.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.Transform.Internal;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Core.Abstractions;
using Workflow.Modules.Abstractions;

/// <summary>
/// 🧮 Bridges transform modules to the 2.2.5 <see cref="IExpressionEvaluator"/> seam, building the
/// per-item variable context (<c>item</c>, <c>index</c>, workflow <c>Variables</c>) each expression sees (D7)~ ✨.
/// </summary>
/// <remarks>
/// CopilotNote: Phase 2.6.a.0. Default engine is JS (Jint); set <c>language: "csharp"</c> to opt into
/// DynamicExpresso via keyed DI (<see cref="IExpressionEvaluator"/> keyed <c>"csharp"</c>) — the same
/// mechanism <c>KeyedExpressionEvaluatorFactory</c> uses, without dragging in <c>Workflow.Engine</c>~ 🌸.
/// </remarks>
public sealed class ItemExpressionEvaluator
{
    private readonly IExpressionEvaluator evaluator;

    private ItemExpressionEvaluator(IExpressionEvaluator evaluator) => this.evaluator = evaluator;

    /// <summary>
    /// Resolves the evaluator for the given language from the module's service provider~ 🔌.
    /// </summary>
    /// <param name="context">The module execution context.</param>
    /// <param name="language">The engine key (<c>null</c>/<c>"js"</c>/<c>"javascript"</c> → default; <c>"csharp"</c> → keyed).</param>
    /// <param name="bridge">The resolved bridge when successful.</param>
    /// <param name="failure">A ready failure result when no evaluator is registered.</param>
    /// <returns><c>true</c> when an evaluator resolved; otherwise <c>false</c>.</returns>
    public static bool TryResolve(
        ModuleExecutionContext context,
        string? language,
        out ItemExpressionEvaluator bridge,
        out ModuleResult? failure)
    {
        failure = null;
        bridge = null!;

        IExpressionEvaluator? evaluator;
        if (string.IsNullOrWhiteSpace(language)
            || language.Equals("js", StringComparison.OrdinalIgnoreCase)
            || language.Equals("javascript", StringComparison.OrdinalIgnoreCase))
        {
            evaluator = context.Services.GetService<IExpressionEvaluator>();
        }
        else
        {
            evaluator = context.Services.GetKeyedService<IExpressionEvaluator>(language)
                ?? context.Services.GetService<IExpressionEvaluator>();
        }

        if (evaluator is null)
        {
            failure = ModuleResult.Fail(
                "🧮 No IExpressionEvaluator is registered — register one (e.g. JintExpressionEvaluator) at host startup~ 💔");
            return false;
        }

        bridge = new ItemExpressionEvaluator(evaluator);
        return true;
    }

    /// <summary>
    /// Builds the per-item variable scope: <c>item</c>, <c>index</c>, plus workflow variables~ 🧩.
    /// </summary>
    /// <param name="context">The module execution context (source of workflow variables).</param>
    /// <param name="item">The current item.</param>
    /// <param name="index">The current item index.</param>
    /// <param name="extra">Optional additional variables (e.g. <c>group</c>/<c>items</c>, <c>left</c>/<c>right</c>).</param>
    /// <returns>The variable scope.</returns>
    public static IReadOnlyDictionary<string, object?> Scope(
        ModuleExecutionContext context,
        object? item,
        int index,
        IReadOnlyDictionary<string, object?>? extra = null)
    {
        var scope = new Dictionary<string, object?>(context.Variables)
        {
            ["item"] = item,
            ["index"] = index,
        };

        if (extra is not null)
        {
            foreach (var kvp in extra)
            {
                scope[kvp.Key] = kvp.Value;
            }
        }

        return scope;
    }

    /// <summary>
    /// Evaluates a boolean predicate expression~ ✅.
    /// </summary>
    /// <param name="expression">The predicate expression.</param>
    /// <param name="scope">The variable scope.</param>
    /// <param name="index">The item index (for error context).</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The boolean result.</returns>
    public async Task<bool> EvalPredicateAsync(
        string expression,
        IReadOnlyDictionary<string, object?> scope,
        int index,
        CancellationToken ct)
    {
        try
        {
            return await this.evaluator.EvaluateAsync<bool>(expression, scope, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ExpressionParseException or ExpressionRuntimeException)
        {
            throw new TransformModuleException($"expression '{expression}' failed: {ex.Message}", index, ex);
        }
    }

    /// <summary>
    /// Evaluates an expression to an untyped value~ 🔬.
    /// </summary>
    /// <param name="expression">The value expression.</param>
    /// <param name="scope">The variable scope.</param>
    /// <param name="index">The item index (for error context).</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The evaluated value.</returns>
    public async Task<object?> EvalValueAsync(
        string expression,
        IReadOnlyDictionary<string, object?> scope,
        int index,
        CancellationToken ct)
    {
        try
        {
            return await this.evaluator.EvaluateAsync(expression, scope, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ExpressionParseException or ExpressionRuntimeException)
        {
            throw new TransformModuleException($"expression '{expression}' failed: {ex.Message}", index, ex);
        }
    }
}
