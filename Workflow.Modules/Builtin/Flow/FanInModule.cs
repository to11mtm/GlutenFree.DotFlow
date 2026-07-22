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
///   <item><c>Named</c> — object keyed by each branch's source port name (UX-F2); on key collision the
///     colliding entries fall back to <c>nodeId.port</c> keys</item>
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

        /// <summary>Object keyed by each branch's source port name (collisions → <c>nodeId.port</c>)~ 🏷️.</summary>
        Named,

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
            new PortDefinition("result", "Result", typeof(object), "⭐ The aggregated payload (shape depends on mode) — connect downstream nodes here~", false),
            new PortDefinition("count", "Count", typeof(int), "Auxiliary: number of incoming branches~", false),
            new PortDefinition("done", "Done", typeof(object), "Auxiliary: activation signal for ordering-only successors~", false)),
        Properties: Arr.create(
            new ModulePropertyDefinition("mode", "Mode", typeof(string), "concat (default) / merge / named / first / last~ 🪄", false, "concat", PropertyEditorType.Dropdown, Arr.create<object>("concat", "merge", "named", "first", "last")),
            new ModulePropertyDefinition("meta", "Count/Done Outputs", typeof(string), "separate (default — count/done as their own ports) / embedded (result = { value, count }) / hidden (result only)~ 🎚️", false, "separate", PropertyEditorType.Dropdown, Arr.create<object>("separate", "embedded", "hidden")),
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
                    $"FanInModule: 'mode' must be one of Concat, Merge, Named, First, Last (got '{modeStr}')~ 💔",
                    "mode"));
        }

        if (configuration.TryGetValue("meta", out var metaRaw2) && metaRaw2 is string metaStr
            && !string.IsNullOrWhiteSpace(metaStr)
            && metaStr.ToLowerInvariant() is not ("separate" or "embedded" or "hidden"))
        {
            return ValidationResult.Failure(
                new ValidationError("INVALID_META",
                    $"FanInModule: 'meta' must be one of separate, embedded, hidden (got '{metaStr}')~ 💔",
                    "meta"));
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

        // UX-F2: index-aligned branch metadata (source node + port) for the 'named' mode~ 🏷️
        var meta = ctx.Inputs.TryGetValue("__incomingBranchMeta__", out var metaRaw)
            && metaRaw is List<Dictionary<string, object?>> rawMeta
            ? rawMeta
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
            FanInMode.Named => NamedBranches(branches, meta),
            FanInMode.First => branches.Count > 0 ? branches[0] : null,
            FanInMode.Last => branches.Count > 0 ? branches[^1] : null,
            _ => branches.Cast<object?>().ToList(),
        };

        // UX-R1: shape the count/done metadata per the 'meta' property~ 🎚️
        var metaMode = ctx.Properties.TryGetValue("meta", out var mm) && mm is string ms && !string.IsNullOrWhiteSpace(ms)
            ? ms.ToLowerInvariant()
            : "separate";

        var outputs = metaMode switch
        {
            // One item: result wraps the payload together with the branch count.
            "embedded" => new Dictionary<string, object?>
            {
                ["result"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["value"] = aggregated,
                    ["count"] = branches.Count,
                },
            },

            // Metadata suppressed entirely.
            "hidden" => new Dictionary<string, object?> { ["result"] = aggregated },

            // Default: count as its own output port (current behaviour).
            _ => new Dictionary<string, object?>
            {
                ["result"] = aggregated,
                ["count"] = branches.Count,
            },
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

    /// <summary>
    /// UX-F2 — object keyed by each branch's source port name: with ports <c>foo, bar, baz</c> the
    /// result is <c>{ foo: …, bar: …, baz: … }</c>. Each value is the port's payload from the branch
    /// snapshot (falling back to the whole snapshot when the port key is absent). Key collisions
    /// (same port name from different nodes) fall back to <c>nodeId.port</c> keys for the colliding
    /// entries; branches without metadata get positional <c>branch{i}</c> keys~ 🏷️
    /// </summary>
    private static Dictionary<string, object?> NamedBranches(
        List<Dictionary<string, object?>> branches,
        List<Dictionary<string, object?>> meta)
    {
        static string? MetaString(List<Dictionary<string, object?>> meta, int i, string key)
            => i < meta.Count && meta[i].TryGetValue(key, out var v) && v is string s && !string.IsNullOrEmpty(s) ? s : null;

        // First pass: count port-name occurrences to detect collisions.
        var portCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < branches.Count; i++)
        {
            var port = MetaString(meta, i, "sourcePortName");
            if (port is not null)
            {
                portCounts[port] = portCounts.TryGetValue(port, out var c) ? c + 1 : 1;
            }
        }

        var named = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (var i = 0; i < branches.Count; i++)
        {
            var port = MetaString(meta, i, "sourcePortName");
            var node = MetaString(meta, i, "sourceNodeId");

            var key = port is null
                ? $"branch{i}"
                : portCounts[port] > 1 && node is not null ? $"{node}.{port}" : port;

            // The port's own payload when present; otherwise the whole branch snapshot.
            var value = port is not null && branches[i].TryGetValue(port, out var portPayload)
                ? portPayload
                : branches[i];

            named[key] = value;
        }

        return named;
    }
}

