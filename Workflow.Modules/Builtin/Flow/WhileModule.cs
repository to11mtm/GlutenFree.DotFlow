// <copyright file="WhileModule.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.Flow;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Workflow.Core.Abstractions;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;

/// <summary>
/// 🌀 Built-in while loop module (<c>builtin.loop.while</c>)~
/// Evaluates a condition before each iteration; continues while condition is <see langword="true"/>.
/// Emits <c>loopBody</c> per iteration and <c>done</c> when complete~ ✨💖
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: Phase 2.2.2 — declarative like <see cref="ForEachModule"/>.
/// If condition is false from the start, returns <see cref="ModuleResult.WithActivePorts"/>
/// with <c>"done"</c> directly (no loop actor spawned). Otherwise returns
/// <see cref="ModuleResult.WithLoop"/> with a <see cref="LoopRequest.ContinueCondition"/> delegate.
/// </para>
/// <para>
/// After each iteration, <c>LoopExecutorActor</c> calls the condition delegate with the merged
/// iteration outputs context. A <c>SetVariable</c> inside the body can write a key that this
/// condition then reads, enabling real variable-driven while loops~ 🔄.
/// </para>
/// <para>
/// Condition resolution:
/// <list type="number">
///   <item>Input port <c>condition</c> → runtime bool / expression string.</item>
///   <item>Property <c>condition</c> → static bool / expression string.</item>
/// </list>
/// Coercion: bool → direct; numeric → <c>!= 0</c>; string truthy/falsy literals;
/// unknown string → delegates to <see cref="IExpressionEvaluator"/> if registered~ 🧮.
/// </para>
/// </remarks>
public sealed class WhileModule : IWorkflowModule
{
    // ── IWorkflowModule identity ──────────────────────────────────────────────────────

    /// <inheritdoc/>
    public string ModuleId => "builtin.loop.while";

    /// <inheritdoc/>
    public string DisplayName => "While Loop";

    /// <inheritdoc/>
    public string Category => "Flow Control";

    /// <inheritdoc/>
    public string Description => "Repeats a sub-graph body while a condition holds true~ 🌀";

    /// <inheritdoc/>
    public string Icon => "🌀";

    /// <inheritdoc/>
    public Version Version => new(1, 0, 0);

    // ── Schema ────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public ModuleSchema Schema { get; } = new(
        Inputs: Arr.create(
            PortDefinition.Create<object>("condition", isRequired: false)),
        Outputs: Arr.create(
            PortDefinition.Create<object>("loopBody", isRequired: false),
            PortDefinition.Create<object>("results", isRequired: false),
            PortDefinition.Create<int>("count", isRequired: false),
            PortDefinition.Create<object>("errors", isRequired: false),
            PortDefinition.Create<object>("done", isRequired: false)),
        Properties: Arr.create(
            ModulePropertyDefinition.Create<object>("condition", isRequired: false),
            ModulePropertyDefinition.Create<int>("maxIterations", isRequired: false),
            ModulePropertyDefinition.Create<bool>("continueOnError", isRequired: false)));

    // ── Execution ──────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<ModuleResult> ExecuteAsync(ModuleExecutionContext ctx, CancellationToken ct = default)
    {
        // Resolve condition value (input port takes priority over property)
        var rawCondition = ctx.Inputs.TryGetValue("condition", out var inputCond) && inputCond != null
            ? inputCond
            : ctx.Properties.TryGetValue("condition", out var propCond) ? propCond : null;

        if (rawCondition == null)
        {
            return ModuleResult.Fail(
                "WhileModule: 'condition' input or property is required but was null~ 🌀❌");
        }

        var evaluator = ctx.Services.GetService<IExpressionEvaluator>();
        var maxIterations = ResolveInt(ctx, "maxIterations") ?? 1000;
        var continueOnError = ResolveBool(ctx, "continueOnError") ?? false;

        // Evaluate initial condition
        bool initialResult;
        try
        {
            initialResult = await CoerceToBoolAsync(rawCondition, ctx.Variables, evaluator, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return ModuleResult.Fail(
                $"WhileModule: failed to evaluate initial condition: {ex.Message}~ ❌", ex);
        }

        ctx.Logger.LogDebug(
            "🌀 WhileModule node '{NodeId}': initial condition={Condition}, maxIterations={Max}",
            ctx.NodeId, initialResult, maxIterations);

        // If condition is already false — complete immediately via done port~ ✅
        if (!initialResult)
        {
            return ModuleResult.WithActivePorts(
                new Dictionary<string, object?>
                {
                    ["results"] = new List<object?>(),
                    ["count"] = 0,
                    ["errors"] = new List<string>(),
                },
                new[] { "done" });
        }

        // Build condition delegate for LoopExecutorActor to re-evaluate each iteration~
        // Capture the raw condition and evaluator for re-use
        var rawCond = rawCondition;
        var evaluatorRef = evaluator; // may be null — CoerceToBoolAsync handles that

        Func<IReadOnlyDictionary<string, object?>, CancellationToken, ValueTask<bool>> continueCondition =
            async (context, token) =>
            {
                // Re-read 'condition' from the updated context if available
                var condValue = context.TryGetValue("condition", out var c) && c != null ? c : rawCond;
                return await CoerceToBoolAsync(condValue, context, evaluatorRef, token).ConfigureAwait(false);
            };

        var loopRequest = new LoopRequest
        {
            Items = null, // condition-driven, not item-driven
            LoopBodyPort = "loopBody",
            DonePort = "done",
            MaxIterations = maxIterations,
            ContinueOnError = continueOnError,
            ContinueCondition = continueCondition,
        };

        var outputs = new Dictionary<string, object?>
        {
            ["results"] = null,
            ["count"] = 0,
            ["errors"] = null,
        };

        return ModuleResult.WithLoop(outputs, loopRequest);
    }

    // ── Bool coercion (shared with ConditionalModule logic) ───────────────────────────

    private static readonly System.Collections.Generic.HashSet<string> TruthyStrings =
        new(StringComparer.OrdinalIgnoreCase) { "true", "1", "yes", "on" };
    private static readonly System.Collections.Generic.HashSet<string> FalsyStrings =
        new(StringComparer.OrdinalIgnoreCase) { "false", "0", "no", "off" };

    private static async ValueTask<bool> CoerceToBoolAsync(
        object? value,
        IReadOnlyDictionary<string, object?> variables,
        IExpressionEvaluator? evaluator,
        CancellationToken ct)
    {
        return value switch
        {
            bool b => b,
            int i => i != 0,
            long l => l != 0,
            double d => d != 0.0,
            float f => f != 0.0f,
            decimal dec => dec != 0m,
            string s when TruthyStrings.Contains(s) => true,
            string s when FalsyStrings.Contains(s) => false,
            string expr when evaluator != null =>
                await evaluator.EvaluateAsync<bool>(expr, variables, ct).ConfigureAwait(false),
            string expr =>
                throw new InvalidOperationException(
                    $"WhileModule: cannot coerce string '{expr}' to bool. " +
                    "Register IExpressionEvaluator in DI for expression support~ 🧮"),
            _ => value != null,
        };
    }

    private static int? ResolveInt(ModuleExecutionContext ctx, string key)
    {
        if (ctx.Inputs.TryGetValue(key, out var v) && v != null) return Convert.ToInt32(v);
        if (ctx.Properties.TryGetValue(key, out v) && v != null) return Convert.ToInt32(v);
        return null;
    }

    private static bool? ResolveBool(ModuleExecutionContext ctx, string key)
    {
        if (ctx.Inputs.TryGetValue(key, out var v) && v != null) return Convert.ToBoolean(v);
        if (ctx.Properties.TryGetValue(key, out v) && v != null) return Convert.ToBoolean(v);
        return null;
    }
}


