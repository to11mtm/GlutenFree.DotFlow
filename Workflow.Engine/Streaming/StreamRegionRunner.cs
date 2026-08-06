// <copyright file="StreamRegionRunner.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Engine.Streaming;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Streams;
using Akka.Streams.Dsl;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;

/// <summary>
/// 🌊 Phase 5.1.3 — materializes a <see cref="StreamRegion"/> as an Akka.Streams graph and runs it
/// with real backpressure and bounded memory.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: Akka.Streams is an <b>internal implementation detail</b>. Modules only ever see
/// <c>IAsyncEnumerable&lt;StreamItem&gt;</c> (<see cref="IStreamingWorkflowModule"/>), so this can
/// be swapped without touching a single module — which is exactly why the Q2 spike chose it: it
/// buys backpressure, bounded concurrency, ordering and cancellation without inventing any of them.
/// See the phase plan's D19 for the decision and its long-term reasoning~ ✨.
/// </para>
/// <para>
/// Shape in v1: a linear chain (<see cref="StreamRegion.ChainOrder"/>) of
/// <c>source → transform* → terminal?</c>. Buffers between stages are bounded by the connection's
/// <c>BufferCapacity</c>, then the workflow default, then <see cref="DefaultBufferCapacity"/>~ 🎚️.
/// </para>
/// </remarks>
public sealed class StreamRegionRunner
{
    /// <summary>
    /// The engine-wide default bounded-channel capacity between two streaming stages. 🎚️.
    /// </summary>
    public const int DefaultBufferCapacity = 64;

    private readonly IMaterializer materializer;

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamRegionRunner"/> class. 🌊.
    /// </summary>
    /// <param name="materializer">The Akka.Streams materializer (from the engine's actor system).</param>
    public StreamRegionRunner(IMaterializer materializer)
        => this.materializer = materializer ?? throw new ArgumentNullException(nameof(materializer));

    /// <summary>
    /// Runs a region to completion. 🌊.
    /// </summary>
    /// <param name="plan">The stages to run, in pipeline order, with their bound contexts.</param>
    /// <param name="cancellationToken">Cancellation, linked to the execution as usual.</param>
    /// <returns>The region's result: the terminal node's outputs plus per-stage item counts.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the plan has no stages.</exception>
    public async Task<StreamRegionResult> RunAsync(
        StreamRegionPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.Stages.Count == 0)
        {
            throw new InvalidOperationException("A streaming region needs at least one stage~ 🚧");
        }

        var stopwatch = Stopwatch.StartNew();
        var counters = plan.Stages.ToDictionary(s => s.NodeId, _ => new StageCounter(), StringComparer.Ordinal);

        var head = plan.Stages[0];
        var source = Source.From(() => head.Module.ExecuteStreamAsync(
                head.Context,
                EmptyStream(),
                cancellationToken))
            .Select(item => Count(counters, head.NodeId, item));

        // Middle stages: each consumes the previous stage's output. The bounded buffer between them
        // is what makes memory predictable — a fast source can't outrun a slow sink~ 🎚️
        var terminal = plan.Stages[^1].Module as IStreamTerminalModule;
        var pipelineStages = terminal is null ? plan.Stages.Count : plan.Stages.Count - 1;

        for (var i = 1; i < pipelineStages; i++)
        {
            var stage = plan.Stages[i];
            var upstream = source;

            source = Source.From(() => stage.Module.ExecuteStreamAsync(
                    stage.Context,
                    upstream.Buffer(stage.BufferCapacity, OverflowStrategy.Backpressure)
                        .RunAsAsyncEnumerable(this.materializer),
                    cancellationToken))
                .Select(item => Count(counters, stage.NodeId, item));
        }

        ModuleResult? terminalResult = null;

        if (terminal is not null)
        {
            var last = plan.Stages[^1];
            var upstream = source
                .Buffer(last.BufferCapacity, OverflowStrategy.Backpressure)
                .RunAsAsyncEnumerable(this.materializer);

            terminalResult = await terminal
                .ExecuteTerminalAsync(last.Context, Count(counters, last.NodeId, upstream), cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            // No terminal stage: drain the pipeline so sources/transforms still run to completion.
            await source.RunWith(Sink.Ignore<StreamItem>(), this.materializer).ConfigureAwait(false);
        }

        stopwatch.Stop();

        return new StreamRegionResult(
            terminalResult,
            counters.ToDictionary(kv => kv.Key, kv => kv.Value.Count, StringComparer.Ordinal),
            stopwatch.Elapsed);
    }

    private static StreamItem Count(IReadOnlyDictionary<string, StageCounter> counters, string nodeId, StreamItem item)
    {
        counters[nodeId].Increment();
        return item;
    }

    private static async IAsyncEnumerable<StreamItem> Count(
        IReadOnlyDictionary<string, StageCounter> counters,
        string nodeId,
        IAsyncEnumerable<StreamItem> items)
    {
        await foreach (var item in items.ConfigureAwait(false))
        {
            counters[nodeId].Increment();
            yield return item;
        }
    }

    private static async IAsyncEnumerable<StreamItem> EmptyStream()
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }

    private sealed class StageCounter
    {
        private long count;

        public long Count => Interlocked.Read(ref this.count);

        public void Increment() => Interlocked.Increment(ref this.count);
    }
}

/// <summary>
/// 📋 What to run: the region's stages in pipeline order, each with its bound execution context.
/// </summary>
/// <param name="Stages">The stages, source first.</param>
public sealed record StreamRegionPlan(IReadOnlyList<StreamStage> Stages);

/// <summary>
/// 🎬 One stage of a streaming region.
/// </summary>
/// <param name="NodeId">The node this stage runs.</param>
/// <param name="Module">The streaming module implementation.</param>
/// <param name="Context">The bound execution context (properties, variable snapshot, services).</param>
/// <param name="BufferCapacity">
/// How many items may wait upstream of this stage — the connection's <c>BufferCapacity</c>, else
/// the engine default. 🎚️.
/// </param>
public sealed record StreamStage(
    string NodeId,
    IStreamingWorkflowModule Module,
    ModuleExecutionContext Context,
    int BufferCapacity = StreamRegionRunner.DefaultBufferCapacity);

/// <summary>
/// 📊 The outcome of running a region.
/// </summary>
/// <param name="TerminalResult">
/// The terminal stage's <see cref="ModuleResult"/> (null when the region had no terminal stage) —
/// this is what the engine dispatches downstream through normal port routing.
/// </param>
/// <param name="ItemCounts">Items emitted per stage, for history and the monitor.</param>
/// <param name="Duration">Wall-clock duration of the region.</param>
public sealed record StreamRegionResult(
    ModuleResult? TerminalResult,
    IReadOnlyDictionary<string, long> ItemCounts,
    TimeSpan Duration);
