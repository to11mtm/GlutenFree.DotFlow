// <copyright file="StreamRegionExecutor.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Engine.Actors;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Workflow.Core.Models;
using Workflow.Engine.Messages;
using Workflow.Engine.Streaming;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Binding;

/// <summary>
/// 🌊 Phase 5.1.3 — runs one <see cref="StreamRegion"/> on behalf of <see cref="WorkflowExecutor"/>.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: Sibling of <c>LoopExecutorActor</c> / <c>TryCatchExecutorActor</c> — the executor
/// spawns one of these instead of a per-node <see cref="NodeExecutor"/> when dispatch reaches a
/// region's source node. The whole region completes as a unit, then normal port dispatch resumes
/// from the terminal node's outputs~ ✨.
/// </para>
/// <para>
/// Binding happens <b>once per stage, up front</b>: a region's stages are long-lived, so there's no
/// per-item binding cost. Variables are the region-start snapshot (read-only — the designer refuses
/// SetVariable inside a region, doc 06 §4.1)~ 🛡️.
/// </para>
/// </remarks>
public sealed class StreamRegionExecutor : ReceiveActor
{
    private readonly StreamRegion region;
    private readonly WorkflowDefinition definition;
    private readonly IReadOnlyDictionary<string, object?> inputs;
    private readonly HashMap<string, object?> variables;
    private readonly Guid executionId;
    private readonly IServiceProvider serviceProvider;
    private readonly CancellationToken parentToken;
    private readonly ILoggingAdapter log = Context.GetLogger();

    // 📡 Stage 7.6 — live progress while the region runs~
    private readonly StreamRegionCounters liveCounts = new();
    private readonly System.Diagnostics.Stopwatch elapsed = new();
    private ICancelable? progressTimer;
    private IReadOnlyDictionary<string, long>? lastSent;

    // ⏱️ Stage 7.5 — stall / overall limits (off unless configured)~
    private CancellationTokenSource? runCts;
    private ICancelable? stallWatchdog;
    private ICancelable? totalTimer;
    private TimeSpan stallLimit;
    private TimeSpan totalLimit;
    private TimeSpan lastMovedAt;
    private IReadOnlyDictionary<string, long>? lastWatched;
    private string? timedOut;

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamRegionExecutor"/> class. 🌊.
    /// </summary>
    /// <param name="region">The region to run.</param>
    /// <param name="definition">The workflow definition.</param>
    /// <param name="inputs">Inputs gathered for the region's source node.</param>
    /// <param name="variables">The execution's variables (snapshot for the region).</param>
    /// <param name="executionId">The execution id.</param>
    /// <param name="serviceProvider">The DI container.</param>
    /// <param name="parentToken">The execution's cancellation token.</param>
    public StreamRegionExecutor(
        StreamRegion region,
        WorkflowDefinition definition,
        IReadOnlyDictionary<string, object?> inputs,
        HashMap<string, object?> variables,
        Guid executionId,
        IServiceProvider serviceProvider,
        CancellationToken parentToken)
    {
        this.region = region;
        this.definition = definition;
        this.inputs = inputs;
        this.variables = variables;
        this.executionId = executionId;
        this.serviceProvider = serviceProvider;
        this.parentToken = parentToken;

        this.Receive<ExecuteStreamRegion>(_ => this.HandleExecute());
        this.Receive<RegionRunFinished>(this.HandleFinished);
        this.Receive<ProgressTick>(_ => this.PublishLiveProgress());
        this.Receive<StallCheck>(_ => this.CheckStall());
        this.Receive<TotalElapsed>(_ => this.TimeOut($"it ran longer than its overall limit of {Describe(this.totalLimit)}"));
    }

    /// <summary>
    /// Creates props for a region executor. 🌊.
    /// </summary>
    /// <param name="region">The region to run.</param>
    /// <param name="definition">The workflow definition.</param>
    /// <param name="inputs">Inputs gathered for the region's source node.</param>
    /// <param name="variables">The execution's variables.</param>
    /// <param name="executionId">The execution id.</param>
    /// <param name="serviceProvider">The DI container.</param>
    /// <param name="parentToken">The execution's cancellation token.</param>
    /// <returns>The props.</returns>
    public static Props Props(
        StreamRegion region,
        WorkflowDefinition definition,
        IReadOnlyDictionary<string, object?> inputs,
        HashMap<string, object?> variables,
        Guid executionId,
        IServiceProvider serviceProvider,
        CancellationToken parentToken)
        => Akka.Actor.Props.Create(() => new StreamRegionExecutor(
            region, definition, inputs, variables, executionId, serviceProvider, parentToken));

    private void HandleExecute()
    {
        var parent = Context.Parent;

        try
        {
            var order = this.region.ChainOrder();
            var stages = new List<StreamStage>(order.Count);

            for (var i = 0; i < order.Count; i++)
            {
                var nodeId = order[i];
                var nodeDefinition = this.definition.Nodes.FirstOrDefault(n =>
                    string.Equals(n.Id, nodeId, StringComparison.Ordinal));

                if (nodeDefinition is null)
                {
                    this.Fail(parent, new InvalidOperationException($"Node '{nodeId}' is missing from the definition."));
                    return;
                }

                // §9.4: honour a pinned module version exactly like the batch path (NodeExecutor)~
                var resolution = Workflow.Modules.ModuleResolution.Resolve(
                    this.serviceProvider.GetService<IModuleRegistry>(), nodeDefinition);
                if (resolution.Module is null)
                {
                    this.Fail(parent, new InvalidOperationException(resolution.Error));
                    return;
                }

                if (resolution.Module is not IStreamingWorkflowModule module)
                {
                    this.Fail(
                        parent,
                        new InvalidOperationException(
                            $"Node '{nodeId}' uses module '{nodeDefinition.ModuleId}', which isn't a streaming module."));
                    return;
                }

                stages.Add(new StreamStage(
                    nodeId,
                    module,
                    this.BuildContext(nodeId, nodeDefinition, module, isSource: i == 0),
                    this.BufferCapacityFor(nodeId),
                    MaxWorkersFor(nodeDefinition, module),
                    OrderedFor(nodeDefinition),
                    ItemErrorPolicyFor(nodeDefinition)));
            }

            this.log.Info(
                "🌊 Running streaming region {RegionIndex} with {StageCount} stages: {Stages}",
                this.region.Index,
                stages.Count,
                string.Join(" → ", order));

            var runner = new StreamRegionRunner(Context.System.Materializer());
            var self = Self;

            // 📡 Stage 7.6 — publish per-stage counts every interval while it runs (only when they
            // changed); the final summary comes from WorkflowExecutor when the region finishes~
            this.elapsed.Start();
            var intervalMs = (this.serviceProvider.GetService<StreamingProgressOptions>() ?? new StreamingProgressOptions()).IntervalMs;
            if (intervalMs > 0)
            {
                var interval = TimeSpan.FromMilliseconds(intervalMs);
                this.progressTimer = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(interval, interval, self, ProgressTick.Instance, self);
            }

            // ⏱️ Stage 7.5 — the run gets its own token so a timeout can stop it; the limits come from
            // the server's defaults, overridden by the region's source node~
            this.runCts = CancellationTokenSource.CreateLinkedTokenSource(this.parentToken);
            var sourceNode = this.definition.Nodes.FirstOrDefault(n => string.Equals(n.Id, order[0], StringComparison.Ordinal));
            var (stall, total) = (this.serviceProvider.GetService<StreamingTimeoutOptions>() ?? new StreamingTimeoutOptions()).For(sourceNode?.Metadata);
            if (stall is { } stallLimit)
            {
                this.stallLimit = stallLimit;
                var every = TimeSpan.FromMilliseconds(Math.Clamp(stallLimit.TotalMilliseconds / 4, 25, 1000));
                this.stallWatchdog = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(every, every, self, StallCheck.Instance, self);
            }

            if (total is { } totalLimit)
            {
                this.totalLimit = totalLimit;
                this.totalTimer = Context.System.Scheduler.ScheduleTellOnceCancelable(totalLimit, self, TotalElapsed.Instance, self);
            }

            // Binding (which can evaluate expressions) runs in the task, off this actor's thread~
            Task.Run(() => this.BindAndRunAsync(runner, stages))
                .ContinueWith(
                    task => task.IsFaulted
                        ? new RegionRunFinished(null, task.Exception?.GetBaseException())
                        : task.IsCanceled
                            ? new RegionRunFinished(null, new OperationCanceledException("Streaming region cancelled"))
                            : new RegionRunFinished(task.Result, null),
                    TaskScheduler.Default)
                .PipeTo(self);
        }
        catch (Exception ex)
        {
            this.Fail(parent, ex);
        }
    }

    private void PublishLiveProgress()
    {
        var counts = this.liveCounts.Snapshot();
        if (counts.Count == 0 || (this.lastSent is not null && SameCounts(this.lastSent, counts)))
        {
            return; // nothing new to say~
        }

        this.lastSent = counts;
        Context.System.EventStream.Publish(new StreamRegionProgress(
            this.executionId,
            this.region.Index,
            counts,
            this.elapsed.Elapsed,
            DateTimeOffset.UtcNow,
            Completed: false));
    }

    private static bool SameCounts(IReadOnlyDictionary<string, long> a, IReadOnlyDictionary<string, long> b)
        => a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var v) && v == kv.Value);

    /// <summary>⏱️ Stage 7.5 — fails the region when no item has moved through any stage for the stall limit.</summary>
    private void CheckStall()
    {
        var counts = this.liveCounts.Snapshot();
        if (this.lastWatched is null || !SameCounts(this.lastWatched, counts))
        {
            this.lastWatched = counts;
            this.lastMovedAt = this.elapsed.Elapsed;
            return;
        }

        if (this.elapsed.Elapsed - this.lastMovedAt >= this.stallLimit)
        {
            this.TimeOut($"no item moved through any stage for {Describe(this.stallLimit)} (its stall limit)");
        }
    }

    /// <summary>⏱️ Stage 7.5 — stops the run; <see cref="HandleFinished"/> then reports it as a timeout.</summary>
    private void TimeOut(string reason)
    {
        if (this.timedOut is not null || this.runCts is null)
        {
            return;
        }

        var counts = this.liveCounts.Snapshot();
        this.timedOut =
            $"Streaming region {this.region.Index} timed out: {reason}. Items so far: "
            + (counts.Count == 0 ? "none" : string.Join(", ", counts.Select(kv => $"{kv.Key} {kv.Value}")))
            + $". Limits: {StreamingTimeoutOptions.StallMetadataKey} / {StreamingTimeoutOptions.TotalMetadataKey} on the region's source node, "
            + "or Streaming:RegionStallTimeoutMs / Streaming:RegionTimeoutMs on the server.";
        this.log.Warning("⏱️ {Message}", this.timedOut);
        this.CancelTimers();
        this.runCts.Cancel();
    }

    private static string Describe(TimeSpan limit)
        => limit.TotalSeconds >= 1 ? $"{limit.TotalSeconds:0.#} s" : $"{limit.TotalMilliseconds:0} ms";

    private void CancelTimers()
    {
        this.progressTimer?.Cancel();
        this.stallWatchdog?.Cancel();
        this.totalTimer?.Cancel();
    }

    /// <inheritdoc/>
    protected override void PostStop()
    {
        this.CancelTimers();
        this.runCts?.Dispose();
        base.PostStop();
    }

    private void HandleFinished(RegionRunFinished message)
    {
        this.CancelTimers(); // the final summary is published by WorkflowExecutor~
        var parent = Context.Parent;

        // ⏱️ Stage 7.5 — a run stopped by a timeout is a timeout, not a cancellation~
        if (this.timedOut is not null && message.Error is not null)
        {
            this.Fail(parent, new TimeoutException(this.timedOut));
            return;
        }

        if (message.Error is not null)
        {
            this.Fail(parent, message.Error);
            return;
        }

        var result = message.Result!;
        var terminalNodeId = this.region.ChainOrder()[^1];

        // A terminal stage that returned a *failed* ModuleResult (e.g. the collect guard biting) is
        // still a node failure — route it the same way a batch node's failure would be~ 🛡️
        if (result.TerminalResult is { Success: false } failed)
        {
            this.Fail(
                parent,
                failed.Exception ?? new InvalidOperationException(failed.ErrorMessage ?? "Streaming region failed."));
            return;
        }

        this.log.Info(
            "✅ Streaming region {RegionIndex} completed in {Duration}ms: {Counts}",
            this.region.Index,
            (long)result.Duration.TotalMilliseconds,
            string.Join(", ", result.ItemCounts.Select(kv => $"{kv.Key}={kv.Value}")));

        if (result.ItemErrors.Count > 0)
        {
            this.log.Warning(
                "🧯 Streaming region {RegionIndex} skipped {ErrorCount} item(s): {Nodes}",
                this.region.Index,
                result.ItemErrors.Count,
                string.Join(", ", result.ItemErrors.Select(e => e.NodeId).Distinct()));
        }

        // 🧯 Skipped items become per-node `errors` outputs, so a designer can wire a stage's error
        // port to a logger/dead-letter path. Each entry follows doc 07's per-item error document
        // (`item`/`offset` fields), so error workflows read one schema~ 🧾
        var errorsByNode = result.ItemErrors
            .GroupBy(e => e.NodeId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<IReadOnlyDictionary<string, object?>>)g.Select(ErrorDocument).ToList(),
                StringComparer.Ordinal);

        parent.Tell(new StreamRegionCompleted(
            this.region.Index,
            this.region.ChainOrder(),
            terminalNodeId,
            result.TerminalResult?.Outputs ?? new Dictionary<string, object?>(),
            result.ItemCounts,
            result.Duration,
            errorsByNode));
    }

    /// <summary>
    /// Projects a skipped item into the shared per-item error document
    /// (<see href="../../new-feature-design/snaplogic-analysis/07-reusable-error-workflow-design.md">doc 07</see> §2)~ 🧾.
    /// </summary>
    private static IReadOnlyDictionary<string, object?> ErrorDocument(StreamItemError error)
        => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = 1,
            ["error"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["message"] = error.Error.Message,
                ["errorType"] = error.Error.GetType().Name,
                ["nodeId"] = error.NodeId,
            },
            ["item"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["index"] = error.ItemIndex,
            },
            ["offset"] = error.Offset is null
                ? null
                : new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["token"] = error.Offset.Token,
                    ["sequence"] = error.Offset.Sequence,
                },
        };

    private void Fail(IActorRef parent, Exception error)
    {
        this.log.Error(error, "❌ Streaming region {RegionIndex} failed: {Error}", this.region.Index, error.Message);

        // Blame the source node when we can't tell which stage broke — it's the node the user
        // dispatched, and the region is reported as a unit~
        parent.Tell(new StreamRegionFailed(
            this.region.Index,
            this.region.NodeIds,
            this.region.SourceNodeIds.FirstOrDefault() ?? this.region.NodeIds[0],
            error));
    }

    private int BufferCapacityFor(string nodeId)
        => this.region.Edges
            .FirstOrDefault(e => string.Equals(e.TargetNodeId, nodeId, StringComparison.Ordinal))
            ?.BufferCapacity
           ?? StreamRegionRunner.DefaultBufferCapacity;

    /// <summary>
    /// Reads the stage's <c>maxWorkers</c> knob. Only <b>per-item</b> stages can be parallelised
    /// (D26) — a stream-shaped module owns its own loop, so asking for workers there would silently
    /// do nothing. Pinning it to 1 with a warning is more honest than pretending~ 👷.
    /// </summary>
    private static int MaxWorkersFor(NodeDefinition node, IStreamingWorkflowModule module)
    {
        var requested = ReadInt(node, "maxWorkers") ?? 1;
        if (requested <= 1)
        {
            return 1;
        }

        return module is IStreamItemProcessor ? requested : 1;
    }

    /// <summary>Reads the stage's <c>ordered</c> knob — ordering is free (D25), so it defaults on~ 🔢.</summary>
    private static bool OrderedFor(NodeDefinition node)
        => ReadBool(node, "ordered") ?? true;

    /// <summary>Reads the stage's per-item error policy (<c>fail</c> by default)~ 🧯.</summary>
    private static StreamItemErrorPolicy ItemErrorPolicyFor(NodeDefinition node)
        => ReadString(node, "onItemError")?.Trim().ToLowerInvariant() switch
        {
            "skip" => StreamItemErrorPolicy.Skip,
            _ => StreamItemErrorPolicy.Fail,
        };

    private static int? ReadInt(NodeDefinition node, string property)
        => node.Properties.Find(property).Case switch
        {
            JsonElement { ValueKind: JsonValueKind.Number } e when e.TryGetInt32(out var n) => n,
            JsonElement { ValueKind: JsonValueKind.String } e when int.TryParse(e.GetString(), out var n) => n,
            _ => null,
        };

    private static bool? ReadBool(NodeDefinition node, string property)
        => node.Properties.Find(property).Case switch
        {
            JsonElement { ValueKind: JsonValueKind.True } => true,
            JsonElement { ValueKind: JsonValueKind.False } => false,
            JsonElement { ValueKind: JsonValueKind.String } e when bool.TryParse(e.GetString(), out var b) => b,
            _ => null,
        };

    private static string? ReadString(NodeDefinition node, string property)
        => node.Properties.Find(property).Case is JsonElement { ValueKind: JsonValueKind.String } e
            ? e.GetString()
            : null;

    /// <summary>
    /// Resolves each stage's template-enabled properties, then runs the region. Runs in a task, so it
    /// reads only fields that never change (<see cref="variables"/>, <see cref="inputs"/>, the
    /// services) and touches no actor state.
    /// </summary>
    private async Task<StreamRegionResult> BindAndRunAsync(StreamRegionRunner runner, IReadOnlyList<StreamStage> unbound)
    {
        var binder = this.serviceProvider.GetService<IPropertyBinder>() ?? new PropertyBinder();
        var stages = new List<StreamStage>(unbound.Count);
        foreach (var stage in unbound)
        {
            var context = stage.Context;
            var bindingContext = new PropertyBindingContext(
                context.Variables,
                new Dictionary<string, IReadOnlyDictionary<string, object?>>(),
                this.serviceProvider,
                this.inputs);

            // A property that doesn't resolve keeps its raw value here (unlike the batch path)~
            var bound = await binder.BindModulePropertiesAsync(context.Properties, stage.Module.Schema.Properties, bindingContext, this.RunToken).ConfigureAwait(false);
            stages.Add(bound.Success
                ? stage with { Context = context with { Properties = new Dictionary<string, object?>(bound.BoundValues) } }
                : stage);
        }

        return await runner.RunAsync(new StreamRegionPlan(stages), this.RunToken, this.liveCounts).ConfigureAwait(false);
    }

    private ModuleExecutionContext BuildContext(
        string nodeId,
        NodeDefinition nodeDefinition,
        IWorkflowModule module,
        bool isSource)
    {
        var properties = new Dictionary<string, object?>();
        foreach (var (key, value) in nodeDefinition.Properties)
        {
            properties[key] = ConvertJsonElement(value);
        }

        var variableDictionary = new Dictionary<string, object?>();
        foreach (var (key, value) in this.variables)
        {
            variableDictionary[key] = value;
        }

        // Properties stay raw here; BindAndRunAsync resolves them off the actor thread~
        var loggerFactory = this.serviceProvider.GetService<ILoggerFactory>();
        var logger = loggerFactory?.CreateLogger($"Module.{module.ModuleId}.{nodeId}")
            ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        return new ModuleExecutionContext
        {
            // Only the region's source sees the graph inputs — downstream stages read the stream.
            Inputs = isSource ? this.inputs : new Dictionary<string, object?>(),
            Properties = properties,
            Variables = variableDictionary,
            Logger = logger,
            Services = this.serviceProvider,
            ExecutionId = this.executionId,
            NodeId = nodeId,
        };
    }

    private sealed record RegionRunFinished(StreamRegionResult? Result, Exception? Error);

    /// <summary>Mirrors <see cref="NodeExecutor"/>'s JSON→CLR property conversion~ 🎁.</summary>
    private static object? ConvertJsonElement(System.Text.Json.JsonElement element)
        => element.ValueKind switch
        {
            System.Text.Json.JsonValueKind.String => element.GetString(),
            System.Text.Json.JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            System.Text.Json.JsonValueKind.True => true,
            System.Text.Json.JsonValueKind.False => false,
            System.Text.Json.JsonValueKind.Null => null,
            System.Text.Json.JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElement).ToList(),
            System.Text.Json.JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(p => p.Name, p => ConvertJsonElement(p.Value)),
            _ => element.ToString(),
        };

    private CancellationToken RunToken => this.runCts?.Token ?? this.parentToken;

    /// <summary>⏱️ Stage 7.5 — time to check whether items are still moving.</summary>
    private sealed class StallCheck
    {
        public static readonly StallCheck Instance = new();
    }

    /// <summary>⏱️ Stage 7.5 — the overall limit has passed.</summary>
    private sealed class TotalElapsed
    {
        public static readonly TotalElapsed Instance = new();
    }

    /// <summary>📡 Stage 7.6 — time to publish live progress.</summary>
    private sealed class ProgressTick
    {
        public static readonly ProgressTick Instance = new();
    }
}
