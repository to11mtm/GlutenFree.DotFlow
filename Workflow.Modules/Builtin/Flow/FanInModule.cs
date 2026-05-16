// <copyright file="FanInModule.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.Flow;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using Microsoft.Extensions.Logging;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;

/// <summary>
/// 🪄 Built-in fan-in / barrier aggregation module (<c>builtin.fanin</c>)~
/// Waits for ALL upstream connections to deliver, then aggregates per the configured mode~ ✨💖
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: Phase 2.2.3b — relies on the engine's natural barrier behaviour
/// (<c>TryFireSuccessor</c> in <c>WorkflowExecutor</c> only fires a node once
/// <b>all</b> predecessors are in a terminal state). Reads ordered per-connection
/// payloads from the engine-supplied <c>__incomingBranches__</c> input~ 🪄
/// </para>
/// <para>
/// <b>Modes</b> (property <c>mode</c>, default <c>Concat</c>):
/// <list type="bullet">
///   <item><c>Concat</c> — array of branch payload dictionaries (preserves connection order)</item>
///   <item><c>Merge</c> — dictionary union, last-writer-wins, with documented precedence (connection order)</item>
///   <item><c>First</c> — payload from the first incoming connection</item>
///   <item><c>Last</c> — payload from the last incoming connection</item>
/// </list>
/// </para>
/// <para>
/// <b>Timeout (deferred):</b> a <c>timeout</c> property is declared in the schema for forward
/// compatibility but not enforced in 2.2.3b — engine-side barrier-timeout machinery is a follow-up.
/// </para>
/// </remarks>
public sealed class FanInModule : IWorkflowModule
{
    /// <summary>Aggregation strategies for incoming branches~ 🎀.</summary>
    public enum FanInMode
    {
        /// <summary>Collect branch payloads into an ordered list (default)~ 📜.</summary>
        Concat,

        /// <summary>Dictionary union; later branches' keys overwrite earlier ones~ 🧪.</summary>
        Merge,

        /// <summary>Return the payload from the first incoming connection~ 🥇.</summary>
        First,

        /// <summary>Return the payload from the last incoming connection~ 🥈.</summary>
        Last,
    }

    /// <inheritdoc />
    public string ModuleId => "builtin.fanin";

    /// <inheritdoc />
    public string DisplayName => "Fan In";

    /// <inheritdoc />
    public string Category => "Flow Control";

    /// <inheritdoc />
    public string Description => "Barrier — waits for all upstream branches and aggregates payloads~ 🪄";

    /// <inheritdoc />
    public string Icon => "🪄";

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public ModuleSchema Schema { get; } = new(
        Inputs: Arr.create(
            // Declarative — actual ordered payloads come from the engine via __incomingBranches__
            PortDefinition.Create<object>("branches", isRequired: false)),
        Outputs: Arr.create(
            PortDefinition.Create<object>("result", isRequired: false),
            PortDefinition.Create<int>("count", isRequired: false),
            PortDefinition.Create<object>("done", isRequired: false)),
        Properties: Arr.create(
            ModulePropertyDefinition.Create<string>("mode", isRequired: false),
            ModulePropertyDefinition.Create<TimeSpan>("timeout", isRequired: false)));

    /// <inheritdoc />
    public ValidationResult ValidateConfiguration(IReadOnlyDictionary<string, object?> configuration)
    {
        if (configuration.TryGetValue("mode", out var modeRaw) && modeRaw is string modeStr
            && !string.IsNullOrWhiteSpace(modeStr)
            && !Enum.TryParse<FanInMode>(modeStr, ignoreCase: true, out _))
        {
            return ValidationResult.Failure(
                new ValidationError("INVALID_MODE",
                    $"FanInModule: 'mode' must be one of Concat, Merge, First, Last (got '{modeStr}')~ 💔",
                    "mode"));
        }

        return ValidationResult.Success();
    }

    /// <inheritdoc />
    public Task<ModuleResult> ExecuteAsync(ModuleExecutionContext ctx, CancellationToken ct = default)
    {
        // Read engine-supplied ordered per-connection payloads~ 🪄
        var branches = ctx.Inputs.TryGetValue("__incomingBranches__", out var branchesRaw)
            && branchesRaw is List<Dictionary<string, object?>> rawList
            ? rawList
            : new List<Dictionary<string, object?>>();

        // Resolve mode
        var mode = FanInMode.Concat;
        if (ctx.Properties.TryGetValue("mode", out var modeRaw) && modeRaw is string modeStr
            && !string.IsNullOrWhiteSpace(modeStr)
            && Enum.TryParse<FanInMode>(modeStr, ignoreCase: true, out var parsedMode))
        {
            mode = parsedMode;
        }

        ctx.Logger.LogInformation(
            "🪄 FanInModule node '{NodeId}': aggregating {Count} branch(es) with mode={Mode}",
            ctx.NodeId, branches.Count, mode);

        object? aggregated = mode switch
        {
            FanInMode.Concat => branches.Cast<object?>().ToList(),
            FanInMode.Merge => MergeBranches(branches),
            FanInMode.First => branches.Count > 0 ? branches[0] : null,
            FanInMode.Last => branches.Count > 0 ? branches[^1] : null,
            _ => branches.Cast<object?>().ToList(),
        };

        var outputs = new Dictionary<string, object?>
        {
            ["result"] = aggregated,
            ["count"] = branches.Count,
        };

        return Task.FromResult(ModuleResult.Ok(outputs));
    }

    /// <summary>
    /// Dictionary union with last-writer-wins precedence~ 🧪
    /// CopilotNote: precedence = connection-order (engine populates branches in
    /// <c>WorkflowDefinition.Connections</c> declaration order), so this is deterministic~ 💖
    /// </summary>
    private static Dictionary<string, object?> MergeBranches(List<Dictionary<string, object?>> branches)
    {
        var merged = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var branch in branches)
        {
            foreach (var (k, v) in branch)
            {
                merged[k] = v;
            }
        }

        return merged;
    }
}

