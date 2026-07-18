// <copyright file="AggregateModule.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.Transform;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin.Transform.Internal;

/// <summary>
/// 📊 Built-in Aggregate module (<c>builtin.transform.aggregate</c>) — sum/count/avg/min/max/
/// first/last/distinct/median/mode over a collection, with optional grouping~ ✨.
/// </summary>
public sealed class AggregateModule : IWorkflowModule
{
    private static readonly System.Collections.Generic.HashSet<string> KnownOps = new(StringComparer.OrdinalIgnoreCase)
    {
        "sum", "count", "avg", "min", "max", "first", "last", "distinct", "median", "mode",
    };

    /// <inheritdoc />
    public string ModuleId => "builtin.transform.aggregate";

    /// <inheritdoc />
    public string DisplayName => "Aggregate Data";

    /// <inheritdoc />
    public string Category => "Transformation";

    /// <inheritdoc />
    public string Description => "Sum, count, avg, min, max, distinct, median, mode — with grouping~ 📊✨";

    /// <inheritdoc />
    public string Icon => "📊";

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public ModuleSchema Schema => new(
        Inputs: Arr.create(
            new PortDefinition("data", "Data", typeof(object), "Array of records to aggregate~ 📥", false)),
        Outputs: Arr.create(
            new PortDefinition("result", "Result", typeof(object), "Aggregation result~ 📤", false),
            new PortDefinition("groups", "Groups", typeof(object), "Per-group results (when groupBy set)~ 🗂️", false),
            new PortDefinition("success", "Success", typeof(bool), "Whether aggregation succeeded~ ✅", false)),
        Properties: Arr.create(
            new ModulePropertyDefinition("data", "Data", typeof(object), "Array data when not connected via port~ 📥", false, null, PropertyEditorType.Json),
            new ModulePropertyDefinition("operation", "Operation", typeof(string), "sum/count/avg/min/max/first/last/distinct/median/mode~ 📊", true, "count", PropertyEditorType.Dropdown),
            new ModulePropertyDefinition("property", "Property", typeof(string), "Dot-path to aggregate (required for numeric ops on records)~ 🔢", false, null, PropertyEditorType.Text),
            new ModulePropertyDefinition("groupBy", "Group By", typeof(string), "Dot-path or expression to group by~ 🗂️", false, null, PropertyEditorType.Text),
            new ModulePropertyDefinition("language", "Expression Language", typeof(string), "js (default) or csharp~ 🧮", false, "js", PropertyEditorType.Text)));

    /// <inheritdoc />
    public ValidationResult ValidateConfiguration(IReadOnlyDictionary<string, object?> configuration)
    {
        var op = TransformSupport.GetString(configuration, "operation");
        if (op is null || !KnownOps.Contains(op))
        {
            return ValidationResult.Failure(new ValidationError("INVALID_OPERATION", $"operation must be one of: {string.Join(", ", KnownOps)}~ 💔", PropertyName: "operation"));
        }

        return ValidationResult.Success();
    }

    /// <inheritdoc />
    public async Task<ModuleResult> ExecuteAsync(
        ModuleExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        if (!TransformDataNormalizer.AsRows(TransformSupport.ReadData(context, "data"), out var rows, out var rowErr))
        {
            return ModuleResult.Fail($"📊 Invalid data: {rowErr}~ 💔");
        }

        var operation = (TransformSupport.GetString(context.Properties, "operation") ?? string.Empty).ToLowerInvariant();
        if (!KnownOps.Contains(operation))
        {
            return ModuleResult.Fail($"📊 Unknown operation '{operation}'~ 💔");
        }

        var property = TransformSupport.GetString(context.Properties, "property");
        var groupBy = TransformSupport.GetString(context.Properties, "groupBy");
        var language = TransformSupport.GetString(context.Properties, "language");

        if (!ItemExpressionEvaluator.TryResolve(context, language, out var evaluator, out var evalFail))
        {
            return evalFail!;
        }

        var sw = Stopwatch.StartNew();
        try
        {
            var outputs = new Dictionary<string, object?> { ["success"] = true };

            if (groupBy is null)
            {
                outputs["result"] = Aggregate(operation, rows, property);
            }
            else
            {
                var groups = new List<object?>();
                var keyed = new List<(string Key, object? RawKey, IReadOnlyDictionary<string, object?> Row)>();
                var idx = 0;
                foreach (var row in rows)
                {
                    var rawKey = DotPath.Resolve(row, groupBy, out var found) is var v && found
                        ? v
                        : await evaluator.EvalValueAsync(groupBy, ItemExpressionEvaluator.Scope(context, row, idx), idx, cancellationToken).ConfigureAwait(false);
                    keyed.Add((TransformComparer.KeyOf(rawKey), rawKey, row));
                    idx++;
                }

                foreach (var g in keyed.GroupBy(k => k.Key))
                {
                    var groupRows = g.Select(x => x.Row).ToList();
                    groups.Add(new Dictionary<string, object?>
                    {
                        ["key"] = g.First().RawKey,
                        ["result"] = Aggregate(operation, groupRows, property),
                        ["count"] = groupRows.Count,
                    });
                }

                outputs["groups"] = groups;
                outputs["result"] = groups;
            }

            sw.Stop();
            return ModuleResult.Ok(outputs, ExecutionMetrics.FromDuration(sw.Elapsed));
        }
        catch (TransformModuleException ex)
        {
            return ModuleResult.Fail($"📊 Aggregate failed: {ex.Message}~ 💔", ex);
        }
    }

    private static object? Aggregate(string operation, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, string? property)
    {
        object? ValueOf(IReadOnlyDictionary<string, object?> row)
            => property is null ? row : DotPath.Resolve(row, property, out _);

        switch (operation)
        {
            case "count":
                return rows.Count;
            case "first":
                return rows.Count > 0 ? ValueOf(rows[0]) : null;
            case "last":
                return rows.Count > 0 ? ValueOf(rows[^1]) : null;
            case "distinct":
                var seen = new System.Collections.Generic.HashSet<string>();
                var uniques = new List<object?>();
                foreach (var row in rows)
                {
                    var val = ValueOf(row);
                    if (seen.Add(TransformComparer.KeyOf(val)))
                    {
                        uniques.Add(val);
                    }
                }

                return uniques;
            case "mode":
                return rows
                    .Select(ValueOf)
                    .GroupBy(TransformComparer.KeyOf)
                    .OrderByDescending(g => g.Count())
                    .Select(g => g.First())
                    .FirstOrDefault();
        }

        // Numeric operations~ 🔢
        var numbers = new List<double>();
        foreach (var row in rows)
        {
            if (TransformComparer.TryToDouble(ValueOf(row), out var n))
            {
                numbers.Add(n);
            }
        }

        if (numbers.Count == 0)
        {
            return operation == "sum" ? 0d : null;
        }

        return operation switch
        {
            "sum" => numbers.Sum(),
            "avg" => numbers.Average(),
            "min" => numbers.Min(),
            "max" => numbers.Max(),
            "median" => Median(numbers),
            _ => null,
        };
    }

    private static double Median(List<double> numbers)
    {
        numbers.Sort();
        var mid = numbers.Count / 2;
        return numbers.Count % 2 == 0 ? (numbers[mid - 1] + numbers[mid]) / 2.0 : numbers[mid];
    }
}
