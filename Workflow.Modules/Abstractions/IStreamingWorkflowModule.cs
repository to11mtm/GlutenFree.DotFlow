// <copyright file="IStreamingWorkflowModule.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Abstractions;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Workflow.Core.Models;

/// <summary>
/// 🌊 Phase 5.1 — implemented by modules that can run as a <b>stage inside a streaming region</b>,
/// processing items with backpressure instead of one payload per run.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: This is an <b>optional</b> companion to <see cref="IWorkflowModule"/>, never a
/// replacement. A module implements both: <c>ExecuteAsync</c> for a normal batch run, and a
/// streaming entry point for when the author wired its streaming ports~ ✨.
/// </para>
/// <para>
/// <b>Pick the right shape</b> (phase 5.1.4, D26):
/// <list type="bullet">
/// <item><description>
/// <see cref="IStreamItemProcessor"/> — <b>per item</b>. The engine owns the loop, so it can run
/// <c>maxWorkers</c> copies of your code concurrently and still deliver items in source order.
/// <b>Prefer this</b>: it's less code and it's the only shape that can be parallelised.
/// </description></item>
/// <item><description>
/// <see cref="ExecuteStreamAsync"/> — <b>whole stream</b>. Your module owns the loop, which is what
/// sources and accumulating stages need. Runs single-worker by definition.
/// </description></item>
/// </list>
/// </para>
/// <para>
/// Roles fall out of how a module uses the parameters:
/// <list type="bullet">
/// <item><description><b>Source</b> — ignores <c>input</c>, yields items (db query, file reader).</description></item>
/// <item><description><b>Transform</b> — consumes and yields (map, filter).</description></item>
/// <item><description><b>Sink</b> — see <see cref="IStreamTerminalModule"/>.</description></item>
/// </list>
/// </para>
/// <para>
/// Design: <see href="../../new-feature-design/snaplogic-analysis/06-streaming-data-plane-design.md">06 — Streaming Data Plane</see> §3.3 (D5, D26).
/// </para>
/// </remarks>
public interface IStreamingWorkflowModule : IWorkflowModule
{
    /// <summary>
    /// Runs this module as a streaming stage that owns its own iteration. 🌊.
    /// </summary>
    /// <param name="context">
    /// Execution context — <c>Inputs</c> holds any non-streaming inputs, <c>Variables</c> is the
    /// region-start snapshot (read-only: variable writes are forbidden inside a region).
    /// </param>
    /// <param name="input">The upstream item stream. <b>Empty</b> for sources.</param>
    /// <param name="cancellationToken">Cancellation token, linked to the region and execution.</param>
    /// <returns>The produced item stream — empty for sinks.</returns>
    /// <remarks>
    /// CopilotNote: this shape hands the module the <b>whole stream</b>, so the module owns the
    /// loop and the engine can't parallelise it — a stage using it always runs single-worker.
    /// Implement <see cref="IStreamItemProcessor"/> instead when your work is per item~ ✨.
    /// </remarks>
    public IAsyncEnumerable<StreamItem> ExecuteStreamAsync(
        ModuleExecutionContext context,
        IAsyncEnumerable<StreamItem> input,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 🧩 Phase 5.1.4 (D26) — a streaming stage whose work is defined <b>one item at a time</b>, which
/// lets the engine run it with bounded concurrency (<c>maxWorkers</c>) while preserving order.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: This is the shape to reach for. Because the <i>engine</i> drives the loop, it can
/// hand N items to N concurrent calls and still emit results in source order — Akka.Streams orders
/// by input slot, so a call returning zero items simply contributes nothing at its slot and one
/// returning many keeps them contiguous. That's why the resequencing buffer, overflow policy and
/// tombstones this design once specified were all deleted (D25)~ 🌸.
/// </para>
/// <para>
/// Return zero items to <b>drop</b> an item (a filter), one to transform it, or many to split it.
/// Implementations must be safe to call concurrently: keep state in locals, not fields.
/// </para>
/// </remarks>
public interface IStreamItemProcessor : IStreamingWorkflowModule
{
    /// <summary>
    /// Processes a single item. 🧩.
    /// </summary>
    /// <param name="item">The incoming item.</param>
    /// <param name="context">
    /// Execution context — properties are bound once when the region starts, and
    /// <c>Variables</c> is the read-only region-start snapshot.
    /// </param>
    /// <param name="cancellationToken">Cancellation token, linked to the region and execution.</param>
    /// <returns>
    /// Zero items to drop, one to transform, many to split. Order within the returned sequence is
    /// preserved.
    /// </returns>
    public ValueTask<IReadOnlyList<StreamItem>> ProcessAsync(
        StreamItem item,
        ModuleExecutionContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 🪣 Phase 5.1 — a streaming stage that <b>ends</b> the stream and produces ordinary batch
/// outputs, rather than emitting more items.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: Two kinds of node need this — the <c>builtin.stream.collect</c> bridge (turns a
/// stream back into an array for the batch world) and every <b>sink</b> (a bulk insert or file
/// writer that reports <c>rowsAffected</c> / <c>itemCount</c> when the stream drains). Returning a
/// plain <see cref="ModuleResult"/> means the engine's normal port dispatch takes over at the
/// region's edge with no special cases~ ✨.
/// </para>
/// <para>
/// State is safe here: everything lives in locals for the duration of the call, so a singleton
/// module instance can serve many concurrent executions~ 🛡️.
/// </para>
/// </remarks>
public interface IStreamTerminalModule : IStreamingWorkflowModule
{
    /// <summary>
    /// Consumes the whole item stream and returns this node's batch outputs. 🪣.
    /// </summary>
    /// <param name="context">Execution context (properties, region-start variable snapshot).</param>
    /// <param name="input">The upstream item stream, drained to completion.</param>
    /// <param name="cancellationToken">Cancellation token, linked to the region and execution.</param>
    /// <returns>The node's result — outputs feed non-streaming successors as usual.</returns>
    public Task<ModuleResult> ExecuteTerminalAsync(
        ModuleExecutionContext context,
        IAsyncEnumerable<StreamItem> input,
        CancellationToken cancellationToken = default);
}
