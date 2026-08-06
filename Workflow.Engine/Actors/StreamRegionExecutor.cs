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

                var registry = this.serviceProvider.GetService<IModuleRegistry>();
                if (registry?.GetModule(nodeDefinition.ModuleId) is not IStreamingWorkflowModule module)
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

            runner.RunAsync(new StreamRegionPlan(stages), this.parentToken)
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

    private void HandleFinished(RegionRunFinished message)
    {
        var parent = Context.Parent;

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

        var binder = this.serviceProvider.GetService<IPropertyBinder>() ?? new PropertyBinder();
        var bindingContext = new PropertyBindingContext(
            variableDictionary,
            new Dictionary<string, IReadOnlyDictionary<string, object?>>(),
            this.serviceProvider,
            this.inputs);

        var bound = binder.BindModuleProperties(properties, module.Schema.Properties, bindingContext);
        if (bound.Success)
        {
            properties = new Dictionary<string, object?>(bound.BoundValues);
        }

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
}
