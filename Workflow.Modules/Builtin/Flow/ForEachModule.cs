// <copyright file="ForEachModule.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.Flow;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using Microsoft.Extensions.Logging;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;

/// <summary>
/// 🔁 Built-in foreach loop module (<c>builtin.loop.foreach</c>)~
/// Iterates over a collection, running a downstream sub-graph for each item.
/// Emits <c>loopBody</c> port activations (one per iteration) and a final <c>done</c> port~ ✨💖
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: Phase 2.2.2 — This module is DECLARATIVE. It validates inputs and returns
/// <see cref="ModuleResult.WithLoop"/> with a <see cref="LoopRequest"/>. The engine
/// (<c>LoopExecutorActor</c>) owns all iteration orchestration — the module just packages the spec~ 🌸.
/// </para>
/// <para>
/// Properties / Inputs resolution priority (highest → lowest):
/// <list type="number">
///   <item>Input port value (runtime data from upstream)</item>
///   <item>Property value (static configuration)</item>
///   <item>Default value</item>
/// </list>
/// </para>
/// <para>
/// Collection input coercion supports: <c>IEnumerable</c>, <c>JsonElement</c> arrays,
/// JSON-string arrays, single objects (wrapped as single-element list)~ 🎁.
/// </para>
/// </remarks>
public sealed class ForEachModule : IWorkflowModule
{
    // ── IWorkflowModule identity ──────────────────────────────────────────────────────

    /// <inheritdoc/>
    public string ModuleId => "builtin.loop.foreach";

    /// <inheritdoc/>
    public string DisplayName => "For Each";

    /// <inheritdoc/>
    public string Category => "Flow Control";

    /// <inheritdoc/>
    public string Description => "Iterates over a collection, running a sub-graph body for each item~ 🔁";

    /// <inheritdoc/>
    public string Icon => "🔁";

    /// <inheritdoc/>
    public Version Version => new(1, 0, 0);

    // ── Schema ────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public ModuleSchema Schema { get; } = new(
        Inputs: Arr.create(
            PortDefinition.Create<object>("collection", isRequired: false)),
        Outputs: Arr.create(
            PortDefinition.Create<object>("loopBody", isRequired: false),
            PortDefinition.Create<object>("results", isRequired: false),
            PortDefinition.Create<int>("count", isRequired: false),
            PortDefinition.Create<object>("errors", isRequired: false),
            PortDefinition.Create<object>("done", isRequired: false)),
        Properties: Arr.create(
            ModulePropertyDefinition.Create<object>("collection", isRequired: false),
            ModulePropertyDefinition.Create<int>("maxIterations", isRequired: false),
            ModulePropertyDefinition.Create<bool>("continueOnError", isRequired: false)));

    // ── Execution ──────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<ModuleResult> ExecuteAsync(ModuleExecutionContext ctx, CancellationToken ct = default)
    {
        // Resolve collection (input port takes priority over property)
        var rawCollection = ctx.Inputs.TryGetValue("collection", out var inputCol) && inputCol != null
            ? inputCol
            : ctx.Properties.TryGetValue("collection", out var propCol) ? propCol : null;

        if (rawCollection == null)
        {
            return Task.FromResult(ModuleResult.Fail(
                "ForEachModule: 'collection' input or property is required but was null~ 🔁❌"));
        }

        IReadOnlyList<object?> items;
        try
        {
            items = CoerceToList(rawCollection);
        }
        catch (Exception ex)
        {
            return Task.FromResult(ModuleResult.Fail(
                $"ForEachModule: could not coerce 'collection' to a list: {ex.Message}~ ❌", ex));
        }

        // Resolve optional settings
        var maxIterations = ResolveInt(ctx, "maxIterations") ?? 1000;
        var continueOnError = ResolveBool(ctx, "continueOnError") ?? false;

        ctx.Logger.LogDebug(
            "🔁 ForEachModule node '{NodeId}': {Count} items, maxIterations={Max}, continueOnError={Cont}",
            ctx.NodeId, items.Count, maxIterations, continueOnError);

        var loopRequest = new LoopRequest
        {
            Items = items,
            LoopBodyPort = "loopBody",
            DonePort = "done",
            MaxIterations = maxIterations,
            ContinueOnError = continueOnError,
        };

        // Initial outputs are placeholders — overwritten by LoopExecutorActor on completion
        var outputs = new Dictionary<string, object?>
        {
            ["results"] = null,
            ["count"] = 0,
            ["errors"] = null,
        };

        return Task.FromResult(ModuleResult.WithLoop(outputs, loopRequest));
    }

    // ── Collection coercion ────────────────────────────────────────────────────────────

    /// <summary>
    /// Coerces a raw input value to a list of items~ 🎁.
    /// Supports: IEnumerable, JsonElement (Array), JSON-string array, single object.
    /// </summary>
    private static IReadOnlyList<object?> CoerceToList(object raw)
    {
        if (raw is IReadOnlyList<object?> readOnly) return readOnly;
        if (raw is System.Collections.IEnumerable enumerable and not string)
            return enumerable.Cast<object?>().ToList();
        if (raw is JsonElement je && je.ValueKind == JsonValueKind.Array)
            return je.EnumerateArray().Select(e => (object?)ConvertJsonElement(e)).ToList();
        if (raw is string s && s.TrimStart().StartsWith('['))
        {
            return JsonSerializer.Deserialize<List<JsonElement>>(s)
                ?.Select(e => (object?)ConvertJsonElement(e)).ToList()
                ?? new List<object?>();
        }

        // Wrap single value
        return new List<object?> { raw };
    }

    private static object? ConvertJsonElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Object => element,
        JsonValueKind.Array => element.EnumerateArray().Select(e => ConvertJsonElement(e)).ToList(),
        _ => element.GetRawText(),
    };

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



