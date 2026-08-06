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
using Akka;
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
        var errors = new List<StreamItemError>();

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
            var upstream = source.Buffer(stage.BufferCapacity, OverflowStrategy.Backpressure);

            source = stage.Module is IStreamItemProcessor processor
                ? PerItemStage(upstream, stage, processor, counters, errors, cancellationToken)
                : Source.From(() => stage.Module.ExecuteStreamAsync(
                        stage.Context,
                        upstream.RunAsAsyncEnumerable(this.materializer),
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
            stopwatch.Elapsed,
            errors);
    }

    /// <summary>
    /// Builds a <b>per-item</b> stage (D26): the engine owns the loop, so it can run
    /// <c>maxWorkers</c> concurrent calls and — with <c>ordered</c> — still emit in source order,
    /// because Akka orders by input slot (D25)~ 🧩.
    /// </summary>
    private Source<StreamItem, NotUsed> PerItemStage(
        Source<StreamItem, NotUsed> upstream,
        StreamStage stage,
        IStreamItemProcessor processor,
        IReadOnlyDictionary<string, StageCounter> counters,
        List<StreamItemError> errors,
        CancellationToken cancellationToken)
    {
        async Task<IReadOnlyList<StreamItem>> Process(StreamItem item)
        {
            try
            {
                return await processor.ProcessAsync(item, stage.Context, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException
                                       && stage.OnItemError != StreamItemErrorPolicy.Fail)
            {
                // 🧯 'skip' drops the item — which, post-D25, needs no tombstone: contributing
                // nothing at this slot leaves the order of everything else untouched.
                lock (errors)
                {
                    errors.Add(new StreamItemError(stage.NodeId, item.Index, item.Offset, ex));
                }

                return Array.Empty<StreamItem>();
            }
        }

        var mapped = stage.Ordered
            ? upstream.SelectAsync(stage.MaxWorkers, Process)
            : upstream.SelectAsyncUnordered(stage.MaxWorkers, Process);

        return mapped
            .SelectMany(items => items)
            .Select(item => Count(counters, stage.NodeId, item));
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
/// <param name="MaxWorkers">
/// How many items this stage may process concurrently (per-item stages only; D26). Default 1. 👷.
/// </param>
/// <param name="Ordered">
/// Whether output keeps source order when <paramref name="MaxWorkers"/> &gt; 1. Default
/// <b>true</b> — ordering is free (D25), so opting out is a deliberate throughput choice. 🔢.
/// </param>
/// <param name="OnItemError">What to do when processing a single item throws. 🧯.</param>
public sealed record StreamStage(
    string NodeId,
    IStreamingWorkflowModule Module,
    ModuleExecutionContext Context,
    int BufferCapacity = StreamRegionRunner.DefaultBufferCapacity,
    int MaxWorkers = 1,
    bool Ordered = true,
    StreamItemErrorPolicy OnItemError = StreamItemErrorPolicy.Fail);

/// <summary>
/// 🧯 What a stage does when processing one item throws.
/// </summary>
public enum StreamItemErrorPolicy
{
    /// <summary>
    /// Fail the whole region — the default, and the right choice when items are not independent. 🛑.
    /// </summary>
    Fail,

    /// <summary>
    /// Drop the item and carry on; the failure is recorded in
    /// <see cref="StreamRegionResult.ItemErrors"/>. ⏭️.
    /// </summary>
    Skip,
}

/// <summary>
/// 🧾 One item that failed while a stage was processing it (with <see cref="StreamItemErrorPolicy.Skip"/>).
/// </summary>
/// <param name="NodeId">The stage that failed. 🆔.</param>
/// <param name="ItemIndex">The item's ordinal within the stage's input. 🔢.</param>
/// <param name="Offset">The item's source offset, when the source supplied one. 📍.</param>
/// <param name="Error">The exception. ⚠️.</param>
/// <remarks>
/// CopilotNote: This is the per-item half of the error story — the envelope shape that feeds the
/// streaming <c>error</c> port and doc 07's per-item error document (`item` + `offset` fields)~ 🧾.
/// </remarks>
public sealed record StreamItemError(
    string NodeId,
    long ItemIndex,
    SourceOffset? Offset,
    Exception Error);

/// <summary>
/// 📊 The outcome of running a region.
/// </summary>
/// <param name="TerminalResult">
/// The terminal stage's <see cref="ModuleResult"/> (null when the region had no terminal stage) —
/// this is what the engine dispatches downstream through normal port routing.
/// </param>
/// <param name="ItemCounts">Items emitted per stage, for history and the monitor.</param>
/// <param name="Duration">Wall-clock duration of the region.</param>
/// <param name="ItemErrors">Items dropped by a <c>skip</c> policy (empty when none). 🧯.</param>
public sealed record StreamRegionResult(
    ModuleResult? TerminalResult,
    IReadOnlyDictionary<string, long> ItemCounts,
    TimeSpan Duration,
    IReadOnlyList<StreamItemError> ItemErrors);
