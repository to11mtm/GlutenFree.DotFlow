// <copyright file="DataJoinModule.cs" company="GlutenFree">
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
/// 🔗 Built-in Data Join module (<c>builtin.transform.join</c>) — hash-joins two collections on
/// key expressions with inner/left/full semantics (D15)~ ✨.
/// </summary>
public sealed class DataJoinModule : IWorkflowModule
{
    /// <inheritdoc />
    public string ModuleId => "builtin.transform.join";

    /// <inheritdoc />
    public string DisplayName => "Join Data";

    /// <inheritdoc />
    public string Category => "Transformation";

    /// <inheritdoc />
    public string Description => "Hash-join two collections on key expressions (inner/left/full)~ 🔗✨";

    /// <inheritdoc />
    public string Icon => "🔗";

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public ModuleSchema Schema => new(
        Inputs: Arr.create(
            new PortDefinition("left", "Left", typeof(object), "Left collection~ 📥", false),
            new PortDefinition("right", "Right", typeof(object), "Right collection~ 📥", false)),
        Outputs: Arr.create(
            new PortDefinition("result", "Result", typeof(object), "Joined records~ 📤", false),
            new PortDefinition("count", "Count", typeof(int), "Number of joined rows~ 🔢", false),
            new PortDefinition("unmatchedLeft", "Unmatched Left", typeof(int), "Left rows with no match~ 🔢", false),
            new PortDefinition("unmatchedRight", "Unmatched Right", typeof(int), "Right rows with no match~ 🔢", false),
            new PortDefinition("success", "Success", typeof(bool), "Whether the join succeeded~ ✅", false)),
        Properties: Arr.create(
            new ModulePropertyDefinition("left", "Left", typeof(object), "Left data when not connected via port~ 📥", false, null, PropertyEditorType.Json),
            new ModulePropertyDefinition("right", "Right", typeof(object), "Right data when not connected via port~ 📥", false, null, PropertyEditorType.Json),
            new ModulePropertyDefinition("leftKey", "Left Key", typeof(string), "Dot-path or expression for the left key~ 🔑", true, null, PropertyEditorType.Text),
            new ModulePropertyDefinition("rightKey", "Right Key", typeof(string), "Dot-path or expression for the right key~ 🔑", true, null, PropertyEditorType.Text),
            new ModulePropertyDefinition("joinType", "Join Type", typeof(string), "inner (default) / left / full~ 🔗", false, "inner", PropertyEditorType.Dropdown, Arr.create<object>("inner", "left", "full")),
            new ModulePropertyDefinition("select", "Select", typeof(object), "Projection over { left, right } (expression or mapping)~ 🎯", false, null, PropertyEditorType.Json),
            new ModulePropertyDefinition("language", "Expression Language", typeof(string), "js (default) or csharp~ 🧮", false, "js", PropertyEditorType.Text)));

    /// <inheritdoc />
    public ValidationResult ValidateConfiguration(IReadOnlyDictionary<string, object?> configuration)
    {
        var errors = new List<ValidationError>();
        if (TransformSupport.GetString(configuration, "leftKey") is null)
        {
            errors.Add(new ValidationError("LEFTKEY_REQUIRED", "leftKey is required~ 💔", PropertyName: "leftKey"));
        }

        if (TransformSupport.GetString(configuration, "rightKey") is null)
        {
            errors.Add(new ValidationError("RIGHTKEY_REQUIRED", "rightKey is required~ 💔", PropertyName: "rightKey"));
        }

        var joinType = (TransformSupport.GetString(configuration, "joinType") ?? "inner").ToLowerInvariant();
        if (joinType is not ("inner" or "left" or "full"))
        {
            errors.Add(new ValidationError("INVALID_JOINTYPE", $"joinType '{joinType}' must be inner, left, or full~ 💔", PropertyName: "joinType"));
        }

        return errors.Count > 0 ? ValidationResult.Failure(errors.ToArray()) : ValidationResult.Success();
    }

    /// <inheritdoc />
    public async Task<ModuleResult> ExecuteAsync(
        ModuleExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        if (!TransformDataNormalizer.AsRows(TransformSupport.ReadData(context, "left"), out var leftRows, out var leftErr))
        {
            return ModuleResult.Fail($"🔗 Invalid left data: {leftErr}~ 💔");
        }

        if (!TransformDataNormalizer.AsRows(TransformSupport.ReadData(context, "right"), out var rightRows, out var rightErr))
        {
            return ModuleResult.Fail($"🔗 Invalid right data: {rightErr}~ 💔");
        }

        var leftKey = TransformSupport.GetString(context.Properties, "leftKey")!;
        var rightKey = TransformSupport.GetString(context.Properties, "rightKey")!;
        var joinType = (TransformSupport.GetString(context.Properties, "joinType") ?? "inner").ToLowerInvariant();
        var selectRaw = context.Properties.GetValueOrDefault("select");
        var language = TransformSupport.GetString(context.Properties, "language");

        if (!ItemExpressionEvaluator.TryResolve(context, language, out var evaluator, out var evalFail))
        {
            return evalFail!;
        }

        var sw = Stopwatch.StartNew();
        try
        {
            // Build the right-side lookup~ 🗂️
            var rightLookup = new Dictionary<string, List<IReadOnlyDictionary<string, object?>>>();
            var rightMatched = new bool[rightRows.Count];
            for (var r = 0; r < rightRows.Count; r++)
            {
                var key = TransformComparer.KeyOf(await this.ResolveKey(context, evaluator, rightKey, rightRows[r], r, cancellationToken).ConfigureAwait(false));
                if (!rightLookup.TryGetValue(key, out var bucket))
                {
                    bucket = new List<IReadOnlyDictionary<string, object?>>();
                    rightLookup[key] = bucket;
                }

                bucket.Add(rightRows[r]);
            }

            var rightIndexByRef = new Dictionary<IReadOnlyDictionary<string, object?>, int>();
            for (var r = 0; r < rightRows.Count; r++)
            {
                rightIndexByRef[rightRows[r]] = r;
            }

            var result = new List<object?>();
            var unmatchedLeft = 0;

            for (var l = 0; l < leftRows.Count; l++)
            {
                var left = leftRows[l];
                var key = TransformComparer.KeyOf(await this.ResolveKey(context, evaluator, leftKey, left, l, cancellationToken).ConfigureAwait(false));

                if (rightLookup.TryGetValue(key, out var matches) && matches.Count > 0)
                {
                    foreach (var right in matches)
                    {
                        rightMatched[rightIndexByRef[right]] = true;
                        result.Add(await this.ProjectPair(context, evaluator, selectRaw, left, right, l, cancellationToken).ConfigureAwait(false));
                    }
                }
                else
                {
                    unmatchedLeft++;
                    if (joinType is "left" or "full")
                    {
                        result.Add(await this.ProjectPair(context, evaluator, selectRaw, left, null, l, cancellationToken).ConfigureAwait(false));
                    }
                }
            }

            var unmatchedRight = rightMatched.Count(m => !m);
            if (joinType == "full")
            {
                for (var r = 0; r < rightRows.Count; r++)
                {
                    if (!rightMatched[r])
                    {
                        result.Add(await this.ProjectPair(context, evaluator, selectRaw, null, rightRows[r], r, cancellationToken).ConfigureAwait(false));
                    }
                }
            }

            sw.Stop();
            return ModuleResult.Ok(
                new Dictionary<string, object?>
                {
                    ["result"] = result,
                    ["count"] = result.Count,
                    ["unmatchedLeft"] = unmatchedLeft,
                    ["unmatchedRight"] = unmatchedRight,
                    ["success"] = true,
                },
                ExecutionMetrics.FromDuration(sw.Elapsed));
        }
        catch (TransformModuleException ex)
        {
            return ModuleResult.Fail($"🔗 Join failed: {ex.Message}~ 💔", ex);
        }
    }

    private async Task<object?> ResolveKey(
        ModuleExecutionContext context, ItemExpressionEvaluator evaluator, string key, IReadOnlyDictionary<string, object?> row, int index, CancellationToken ct)
    {
        var byPath = DotPath.Resolve(row, key, out var found);
        return found ? byPath : await evaluator.EvalValueAsync(key, ItemExpressionEvaluator.Scope(context, row, index), index, ct).ConfigureAwait(false);
    }

    private async Task<object?> ProjectPair(
        ModuleExecutionContext context,
        ItemExpressionEvaluator evaluator,
        object? selectRaw,
        IReadOnlyDictionary<string, object?>? left,
        IReadOnlyDictionary<string, object?>? right,
        int index,
        CancellationToken ct)
    {
        var extra = new Dictionary<string, object?> { ["left"] = left, ["right"] = right };

        if (selectRaw is string expr)
        {
            return await evaluator.EvalValueAsync(expr, ItemExpressionEvaluator.Scope(context, left, index, extra), index, ct).ConfigureAwait(false);
        }

        if (TransformDataNormalizer.Normalize(selectRaw) is IReadOnlyDictionary<string, object?> map)
        {
            var scope = ItemExpressionEvaluator.Scope(context, left, index, extra);
            var output = new Dictionary<string, object?>();
            foreach (var (target, specRaw) in map)
            {
                output[target] = specRaw is string e
                    ? await evaluator.EvalValueAsync(e, scope, index, ct).ConfigureAwait(false)
                    : specRaw;
            }

            return output;
        }

        // Default merge shape: left fields spread, right nested under "right"~ 🔗
        var merged = new Dictionary<string, object?>();
        if (left is not null)
        {
            foreach (var kvp in left)
            {
                merged[kvp.Key] = kvp.Value;
            }
        }

        merged["right"] = right;
        return merged;
    }
}
