// <copyright file="DataQueryModule.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.Transform;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin.Transform.Internal;
using Workflow.Modules.Internal;

/// <summary>
/// 🔍 Built-in Data Query module (<c>builtin.transform.query</c>) — a fixed filter → project →
/// sort → paginate pipeline over a collection (D1/Q4)~ ✨.
/// </summary>
public sealed class DataQueryModule : IStreamItemProcessor
{
    /// <inheritdoc />
    public string ModuleId => "builtin.transform.query";

    /// <inheritdoc />
    public string DisplayName => "Query Data";

    /// <inheritdoc />
    public string Category => "Transformation";

    /// <inheritdoc />
    public string Description => "Filter, project, sort, and paginate a collection~ 🔍✨";

    /// <inheritdoc />
    public string Icon => "🔍";

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public ModuleSchema Schema => new(
        Inputs: Arr.create(
            new PortDefinition("data", "Data", typeof(object), "Array of records to query~ 📥", false),
            PortDefinition.CreateStreaming("items", isRequired: false, description: "Item stream to filter/project~ 🌊")),
        Outputs: Arr.create(
            new PortDefinition("result", "Result", typeof(object), "Query results~ 📤", false),
            new PortDefinition("count", "Count", typeof(int), "Result count (post-slice)~ 🔢", false),
            new PortDefinition("totalCount", "Total Count", typeof(int), "Match count (pre-skip/take)~ 🔢", false),
            new PortDefinition("success", "Success", typeof(bool), "Whether the query succeeded~ ✅", false),
            PortDefinition.CreateStreaming("items", isRequired: false, description: "Matching items~ 🌊")),
        Properties: Arr.create(
            new ModulePropertyDefinition("data", "Data", typeof(object), "Array data when not connected via port~ 📥", false, null, PropertyEditorType.Json),
            new ModulePropertyDefinition("where", "Where", typeof(string), "Filter predicate expression (sees item/index)~ 🔎", false, null, PropertyEditorType.Expression),
            new ModulePropertyDefinition("select", "Select", typeof(object), "Projection: expression string or mapping object~ 🎯", false, null, PropertyEditorType.Json),
            new ModulePropertyDefinition("orderBy", "Order By", typeof(string), "Sort key: dot-path or expression~ ↕️", false, null, PropertyEditorType.Text),
            new ModulePropertyDefinition("descending", "Descending", typeof(bool), "Sort descending~ ⬇️", false, false, PropertyEditorType.Boolean),
            new ModulePropertyDefinition("skip", "Skip", typeof(int), "Skip N results~ ⏭️", false, null, PropertyEditorType.Number),
            new ModulePropertyDefinition("take", "Take", typeof(int), "Take N results~ 🔢", false, null, PropertyEditorType.Number),
            new ModulePropertyDefinition("language", "Expression Language", typeof(string), "js (default) or csharp~ 🧮", false, "js", PropertyEditorType.Text)));

    /// <inheritdoc />
    public async Task<ModuleResult> ExecuteAsync(
        ModuleExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        if (!TransformDataNormalizer.AsRows(TransformSupport.ReadData(context, "data"), out var rows, out var rowErr))
        {
            return ModuleResult.Fail($"🔍 Invalid data: {rowErr}~ 💔");
        }

        var language = TransformSupport.GetString(context.Properties, "language");
        if (!ItemExpressionEvaluator.TryResolve(context, language, out var evaluator, out var evalFail))
        {
            return evalFail!;
        }

        var where = TransformSupport.GetString(context.Properties, "where");
        var orderBy = TransformSupport.GetString(context.Properties, "orderBy");
        var descending = TransformSupport.GetBool(context.Properties, "descending");
        var skip = TransformSupport.GetInt(context.Properties, "skip");
        var take = TransformSupport.GetInt(context.Properties, "take");
        var selectRaw = context.Properties.GetValueOrDefault("select");

        var sw = Stopwatch.StartNew();
        try
        {
            // where~ 🔎
            var filtered = new List<IReadOnlyDictionary<string, object?>>();
            var index = 0;
            foreach (var row in rows)
            {
                if (where is null || await evaluator.EvalPredicateAsync(where, ItemExpressionEvaluator.Scope(context, row, index), index, cancellationToken).ConfigureAwait(false))
                {
                    filtered.Add(row);
                }

                index++;
            }

            var totalCount = filtered.Count;

            // orderBy~ ↕️
            IEnumerable<IReadOnlyDictionary<string, object?>> ordered = filtered;
            if (orderBy is not null)
            {
                var keyed = new List<(IReadOnlyDictionary<string, object?> Row, object? Key)>();
                var i = 0;
                foreach (var row in filtered)
                {
                    var key = await this.ResolveKey(context, evaluator, orderBy, row, i, cancellationToken).ConfigureAwait(false);
                    keyed.Add((row, key));
                    i++;
                }

                keyed.Sort((a, b) => TransformComparer.Compare(a.Key, b.Key));
                if (descending)
                {
                    keyed.Reverse();
                }

                ordered = keyed.Select(k => k.Row);
            }

            // skip/take~ ⏭️
            var sliced = ordered;
            if (skip is { } s)
            {
                sliced = sliced.Skip(s);
            }

            if (take is { } t)
            {
                sliced = sliced.Take(t);
            }

            var slicedList = sliced.ToList();

            // select~ 🎯
            List<object?> result;
            if (selectRaw is null)
            {
                result = slicedList.Cast<object?>().ToList();
            }
            else
            {
                result = new List<object?>();
                var j = 0;
                foreach (var row in slicedList)
                {
                    result.Add(await this.Project(context, evaluator, selectRaw, row, j, cancellationToken).ConfigureAwait(false));
                    j++;
                }
            }

            sw.Stop();
            return ModuleResult.Ok(
                new Dictionary<string, object?>
                {
                    ["result"] = result,
                    ["count"] = result.Count,
                    ["totalCount"] = totalCount,
                    ["success"] = true,
                },
                ExecutionMetrics.FromDuration(sw.Elapsed));
        }
        catch (TransformModuleException ex)
        {
            return ModuleResult.Fail($"🔍 Query failed: {ex.Message}~ 💔", ex);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// 🌊 Phase 5.1.4 — the per-item streaming path: <c>where</c> decides whether the item survives
    /// (return zero items to drop it — no tombstone needed, D25) and <c>select</c> reshapes it.
    /// <para>
    /// <b>Deliberately rejected here:</b> <c>orderBy</c>, <c>skip</c> and <c>take</c> are
    /// whole-stream operations — sorting needs every item before it can emit the first one. Rather
    /// than silently ignore them (and quietly return wrong data), a streaming node configured with
    /// them fails with an explanation. Collect the stream first, or wait for
    /// <c>builtin.stream.sort</c> (5.1.P5)~ 🚧.
    /// </para>
    /// </remarks>
    public async ValueTask<IReadOnlyList<StreamItem>> ProcessAsync(
        StreamItem item,
        ModuleExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(context);

        foreach (var wholeStreamOnly in new[] { "orderBy", "skip", "take" })
        {
            if (context.Properties.TryGetValue(wholeStreamOnly, out var v) && v is not null)
            {
                throw new TransformModuleException(
                    $"'{wholeStreamOnly}' needs the whole result set, so it can't run on a stream. " +
                    "Collect the stream first, then query it~ 🚧");
            }
        }

        if (item.Payload is not JsonPayload json
            || TransformDataNormalizer.Normalize(JsonValueConverter.FromElement(json.Value))
                is not IReadOnlyDictionary<string, object?> row)
        {
            return Array.Empty<StreamItem>();
        }

        var language = TransformSupport.GetString(context.Properties, "language");
        if (!ItemExpressionEvaluator.TryResolve(context, language, out var evaluator, out _))
        {
            throw new TransformModuleException($"expression language '{language}' is not available~ 💔");
        }

        var index = (int)item.Index;
        var where = TransformSupport.GetString(context.Properties, "where");
        if (where is not null)
        {
            var matches = await evaluator!
                .EvalPredicateAsync(where, ItemExpressionEvaluator.Scope(context, row, index), index, cancellationToken)
                .ConfigureAwait(false);

            if (!matches)
            {
                return Array.Empty<StreamItem>();
            }
        }

        var select = context.Properties.TryGetValue("select", out var selectRaw) ? selectRaw : null;
        if (select is null)
        {
            return new[] { item };
        }

        var projected = await this
            .Project(context, evaluator!, select, row, index, cancellationToken)
            .ConfigureAwait(false);

        return new[]
        {
            item with { Payload = new JsonPayload(JsonSerializer.SerializeToElement(projected)) },
        };
    }

    /// <inheritdoc />
    /// <remarks>Query is per item — the region executor calls <see cref="ProcessAsync"/>~ 🧩.</remarks>
    public IAsyncEnumerable<StreamItem> ExecuteStreamAsync(
        ModuleExecutionContext context,
        IAsyncEnumerable<StreamItem> input,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            "builtin.transform.query is a per-item stage — the region executor calls ProcessAsync~ 🧩");

    private async Task<object?> ResolveKey(
        ModuleExecutionContext context, ItemExpressionEvaluator evaluator, string orderBy, IReadOnlyDictionary<string, object?> row, int index, CancellationToken ct)
    {
        var byPath = DotPath.Resolve(row, orderBy, out var found);
        if (found)
        {
            return byPath;
        }

        return await evaluator.EvalValueAsync(orderBy, ItemExpressionEvaluator.Scope(context, row, index), index, ct).ConfigureAwait(false);
    }

    private async Task<object?> Project(
        ModuleExecutionContext context, ItemExpressionEvaluator evaluator, object? selectRaw, IReadOnlyDictionary<string, object?> row, int index, CancellationToken ct)
    {
        if (selectRaw is string expr)
        {
            return await evaluator.EvalValueAsync(expr, ItemExpressionEvaluator.Scope(context, row, index), index, ct).ConfigureAwait(false);
        }

        if (TransformDataNormalizer.Normalize(selectRaw) is IReadOnlyDictionary<string, object?> map)
        {
            var scope = ItemExpressionEvaluator.Scope(context, row, index);
            var output = new Dictionary<string, object?>();
            foreach (var (target, specRaw) in map)
            {
                output[target] = specRaw switch
                {
                    string path when DotPath.Resolve(row, path, out var found) is var v && found => v,
                    string expr2 => await evaluator.EvalValueAsync(expr2, scope, index, ct).ConfigureAwait(false),
                    _ => specRaw,
                };
            }

            return output;
        }

        return row;
    }
}
