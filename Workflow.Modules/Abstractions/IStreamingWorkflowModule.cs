// <copyright file="IStreamingWorkflowModule.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Abstractions;

using System.Collections.Generic;
using System.Threading;
using Workflow.Core.Models;

/// <summary>
/// 🌊 Phase 5.1 — implemented by modules that can run as a <b>stage inside a streaming region</b>,
/// processing items one at a time with backpressure instead of one payload per run.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: This is an <b>optional</b> companion to <see cref="IWorkflowModule"/>, never a
/// replacement. A module implements both: <c>ExecuteAsync</c> for a normal batch run, and
/// <c>ExecuteStreamAsync</c> when the author wired its streaming ports. The engine picks the path
/// based on the ports the workflow actually connected~ ✨.
/// </para>
/// <para>
/// Roles fall out of how a module uses the parameters:
/// <list type="bullet">
/// <item><description><b>Source</b> — ignores <c>input</c>, yields items (db query, file reader).</description></item>
/// <item><description><b>Transform</b> — consumes and yields (map, filter).</description></item>
/// <item><description><b>Sink</b> — consumes and yields nothing (bulk insert, file writer).</description></item>
/// </list>
/// </para>
/// <para>
/// Design: <see href="../../new-feature-design/snaplogic-analysis/06-streaming-data-plane-design.md">06 — Streaming Data Plane</see> §3.3 (D5).
/// </para>
/// </remarks>
public interface IStreamingWorkflowModule : IWorkflowModule
{
    /// <summary>
    /// Gets how many output items this stage produces per input item. 🔢.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Under review (phase-plan Q7).</b> This hint originally gated which stages could sit
    /// under a resequencing buffer. D25 removed the resequencer — Akka.Streams orders by input
    /// slot, so ordering holds at every cardinality — which leaves this member without behaviour
    /// attached. 5.1.4 decides whether to delete it or demote it to a purely cosmetic designer
    /// hint ("this stage may change item counts")~ 🌸.
    /// </remarks>
    public StreamCardinality Cardinality => StreamCardinality.OneToOne;

    /// <summary>
    /// Runs this module as a streaming stage. 🌊.
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
    /// loop and the engine can't parallelise it. 5.1.4 adds an optional per-item entry point
    /// (D26) for stages that want <c>maxWorkers &gt; 1</c>; modules using this method keep working
    /// and simply run single-worker~ ✨.
    /// </remarks>
    public IAsyncEnumerable<StreamItem> ExecuteStreamAsync(
        ModuleExecutionContext context,
        IAsyncEnumerable<StreamItem> input,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 🔢 How many output items a streaming stage produces per input item.
/// </summary>
/// <remarks>
/// ⚠️ Under review — see <see cref="IStreamingWorkflowModule.Cardinality"/> and phase-plan Q7.
/// </remarks>
public enum StreamCardinality
{
    /// <summary>
    /// Exactly one output per input. 1️⃣.
    /// </summary>
    OneToOne,

    /// <summary>
    /// Zero-or-more outputs per input (filters, splitters). 🔀.
    /// </summary>
    Variable,
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
