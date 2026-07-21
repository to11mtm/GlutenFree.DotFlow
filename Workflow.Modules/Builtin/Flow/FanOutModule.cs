// <copyright file="FanOutModule.cs" company="GlutenFree">
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
/// 🌟 Built-in fan-out module (<c>builtin.fanout</c>)~
/// Like <see cref="ForEachModule"/>, but each item runs CONCURRENTLY in its own sub-graph
/// via <c>ParallelExecutionCoordinator</c>. Activates the <c>branch</c> port once per item
/// (parallel) and fires <c>done</c> when all items complete~ ✨💖
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: Phase 2.2.3b — DECLARATIVE. Returns
/// <see cref="ModuleResult.WithParallel"/> with <see cref="ParallelRequest.Items"/>
/// set; the engine's <c>ParallelExecutionCoordinator</c> orchestrates per-item branches
/// (bounded concurrency, fail-fast, hierarchical cancellation)~ 🌸
/// </para>
/// <para>
/// Inputs/Properties:
/// <list type="bullet">
///   <item><c>items</c> — required collection (input or property)</item>
///   <item><c>maxDegreeOfParallelism</c> — int, ≤0 = unbounded</item>
///   <item><c>failFast</c> — default <c>true</c></item>
/// </list>
/// </para>
/// <para>
/// Output ports:
/// <list type="bullet">
///   <item><c>branch</c> — fires once per item (in its own sub-graph) with inputs <c>item</c> + <c>index</c></item>
///   <item><c>done</c> — fires after all items complete</item>
/// </list>
/// </para>
/// </remarks>
public sealed class FanOutModule : IWorkflowModule
{
    /// <inheritdoc />
    public string ModuleId => "builtin.fanout";

    /// <inheritdoc />
    public string DisplayName => "Fan Out";

    /// <inheritdoc />
    public string Category => "Flow Control";

    /// <inheritdoc />
    public string Description => "Runs a sub-graph concurrently for each item in a collection~ 🌟";

    /// <inheritdoc />
    public string Icon => "🌟";

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public ModuleSchema Schema { get; } = new(
        Inputs: Arr.create(
            PortDefinition.Create<object>("items", isRequired: false)),
        Outputs: Arr.create(
            PortDefinition.Create<object>("branch", isRequired: false),
            PortDefinition.Create<object>("results", isRequired: false),
            PortDefinition.Create<int>("count", isRequired: false),
            PortDefinition.Create<object>("done", isRequired: false)),
        Properties: Arr.create(
            ModulePropertyDefinition.Create<object>("items", isRequired: false),
            ModulePropertyDefinition.Create<int>("maxDegreeOfParallelism", isRequired: false),
            ModulePropertyDefinition.Create<bool>("failFast", isRequired: false)));

    /// <inheritdoc />
    public ValidationResult ValidateConfiguration(IReadOnlyDictionary<string, object?> configuration)
    {
        // Items can be supplied at runtime via input port — no required property check here~
        return ValidationResult.Success();
    }

    /// <inheritdoc />
    public Task<ModuleResult> ExecuteAsync(ModuleExecutionContext ctx, CancellationToken ct = default)
    {
        // Resolve items — input port takes priority over property~ 🎁
        var rawItems = ctx.Inputs.TryGetValue("items", out var inputItems) && inputItems != null
            ? inputItems
            : ctx.Properties.TryGetValue("items", out var propItems) ? propItems : null;

        if (rawItems == null)
        {
            return Task.FromResult(ModuleResult.Fail(
                "FanOutModule: 'items' input or property is required but was null~ 🌟❌"));
        }

        IReadOnlyList<object?> items;
        try
        {
            items = CoerceToList(rawItems);
        }
        catch (Exception ex)
        {
            return Task.FromResult(ModuleResult.Fail(
                $"FanOutModule: could not coerce 'items' to a list: {ex.Message}~ ❌", ex));
        }

        var maxDoP = int.MaxValue;
        if (ctx.Properties.TryGetValue("maxDegreeOfParallelism", out var dopRaw) && dopRaw is not null)
        {
            try
            {
                var parsed = Convert.ToInt32(dopRaw);
                if (parsed > 0)
                {
                    maxDoP = parsed;
                }
            }
            catch (Exception)
            {
                // Ignore — fall back to unbounded~
            }
        }

        var failFast = true;
        if (ctx.Properties.TryGetValue("failFast", out var ffRaw) && ffRaw is bool ffBool)
        {
            failFast = ffBool;
        }

        ctx.Logger.LogInformation(
            "🌟 FanOutModule node '{NodeId}': fan-out {Count} items (maxDoP={MaxDoP}, failFast={FailFast})",
            ctx.NodeId, items.Count, maxDoP, failFast);

        var request = new ParallelRequest
        {
            BranchPorts = Array.Empty<string>(), // ignored in per-item mode
            Items = items,
            BranchPort = "branch",
            MaxDegreeOfParallelism = maxDoP,
            FailFast = failFast,
            WaitForAll = true,
            DonePort = "done",
        };

        var outputs = new Dictionary<string, object?>
        {
            ["results"] = null,
            ["count"] = items.Count,
        };

        return Task.FromResult(ModuleResult.WithParallel(outputs, request));
    }

    // ── Collection coercion (mirrors ForEachModule.CoerceToList)~ 🎁 ─────────────────

    private static IReadOnlyList<object?> CoerceToList(object raw)
    {
        if (raw is IReadOnlyList<object?> readOnly)
        {
            return readOnly;
        }

        if (raw is IEnumerable enumerable and not string)
        {
            return enumerable.Cast<object?>().ToList();
        }

        if (raw is JsonElement je && je.ValueKind == JsonValueKind.Array)
        {
            return je.EnumerateArray().Select(e => (object?)ConvertJsonElement(e)).ToList();
        }

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
}

