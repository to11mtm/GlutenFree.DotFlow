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
    /// CopilotNote: Only <see cref="StreamCardinality.OneToOne"/> stages may sit between a
    /// multi-worker stage and its resequencer — ordering can't be restored when a stage multiplies
    /// or collapses items. The designer validates this at design time so nobody discovers it at
    /// run time~ 🛡️ (design doc 06 §5.1, D8).
    /// </remarks>
    public StreamCardinality Cardinality => StreamCardinality.OneToOne;

    /// <summary>
    /// Runs this module as a streaming stage. 🌊.
    /// </summary>
    /// <param name="context">
    /// Execution context — <c>Inputs</c> holds any non-streaming inputs, <c>Variables</c> is the
    /// region-start snapshot (read-only: variable writes are forbidden inside a region).
    /// </param>
    /// <param name="input">
    /// The upstream item stream. <b>Empty</b> for sources. Tombstones are filtered out by the
    /// region executor before they reach a module.
    /// </param>
    /// <param name="cancellationToken">Cancellation token, linked to the region and execution.</param>
    /// <returns>The produced item stream — empty for sinks.</returns>
    public IAsyncEnumerable<StreamItem> ExecuteStreamAsync(
        ModuleExecutionContext context,
        IAsyncEnumerable<StreamItem> input,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 🔢 How many output items a streaming stage produces per input item.
/// </summary>
public enum StreamCardinality
{
    /// <summary>
    /// Exactly one output per input — order can be restored by a resequencer. 1️⃣.
    /// </summary>
    OneToOne,

    /// <summary>
    /// Zero-or-more outputs per input (filters, splitters) — cannot be resequenced. 🔀.
    /// </summary>
    Variable,
}
