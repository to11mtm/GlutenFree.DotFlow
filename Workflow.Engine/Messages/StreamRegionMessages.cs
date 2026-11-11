// <copyright file="StreamRegionMessages.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Engine.Messages;

using System;
using System.Collections.Generic;

/// <summary>
/// Tells a <see cref="Workflow.Engine.Actors.StreamRegionExecutor"/> to start its region~ 🌊▶️
/// </summary>
public record ExecuteStreamRegion;

/// <summary>
/// Sent by <see cref="Workflow.Engine.Actors.StreamRegionExecutor"/> to its parent
/// (<c>WorkflowExecutor</c>) when the whole region has drained successfully~ 🌊✅
/// </summary>
/// <param name="RegionIndex">The region's stable index. 🔢.</param>
/// <param name="NodeIds">Every node the region ran, in pipeline order. 🗺️.</param>
/// <param name="TerminalNodeId">
/// The last node in the chain — its outputs are what downstream (non-streaming) nodes receive. 🪣.
/// </param>
/// <param name="Outputs">The terminal stage's outputs (empty when the region had no terminal stage). 📦.</param>
/// <param name="ItemCounts">Items emitted per stage, for history and the monitor. 📊.</param>
/// <param name="Duration">Wall-clock duration of the region. ⏱️.</param>
/// <param name="ItemErrors">
/// Per-node skipped-item error documents (doc 07 §2 shape), surfaced as each node's <c>errors</c>
/// output so a designer can wire a stage's error path~ 🧯.
/// </param>
/// <remarks>
/// CopilotNote: Phase 5.1.3 — WorkflowExecutor marks every member node Completed, stores the
/// terminal outputs under <c>TerminalNodeId</c>, and resumes ordinary port dispatch from there~ 💖.
/// </remarks>
public record StreamRegionCompleted(
    int RegionIndex,
    IReadOnlyList<string> NodeIds,
    string TerminalNodeId,
    IReadOnlyDictionary<string, object?> Outputs,
    IReadOnlyDictionary<string, long> ItemCounts,
    TimeSpan Duration,
    IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>? ItemErrors = null);

/// <summary>
/// Sent by <see cref="Workflow.Engine.Actors.StreamRegionExecutor"/> to its parent when the region
/// fails — a stage threw, a module wasn't streaming, or a terminal guard tripped~ 🌊❌
/// </summary>
/// <param name="RegionIndex">The region's stable index. 🔢.</param>
/// <param name="NodeIds">Every node in the region. 🗺️.</param>
/// <param name="FailedNodeId">The node blamed for the failure (the source when unknown). 🔍.</param>
/// <param name="Error">The exception. ⚠️.</param>
/// <remarks>
/// CopilotNote: Handled exactly like a regular <see cref="NodeExecutionFailed"/>, so per-node error
/// handling, retries and enclosing try/catch boundaries all behave as usual~ 🛡️.
/// </remarks>
public record StreamRegionFailed(
    int RegionIndex,
    IReadOnlyList<string> NodeIds,
    string FailedNodeId,
    Exception Error)
{
    /// <summary>🧯 Stage 7.8 — gets the region's stages in pipeline order (source first); empty = unknown.</summary>
    public IReadOnlyList<string> ChainOrder { get; init; } = Array.Empty<string>();

    /// <summary>🧯 Stage 7.8 — gets the items each stage had emitted when the region failed.</summary>
    public IReadOnlyDictionary<string, long> ItemCounts { get; init; } = new Dictionary<string, long>();

    /// <summary>
    /// 🧯 Stage 7.8 — gets a value indicating whether a per-item stage already retried the failing item
    /// in place until its policy ran out, so the region isn't re-run for another retry.
    /// </summary>
    public bool ItemRetriesExhausted { get; init; }
}

/// <summary>
/// 📡 Published to the Akka <c>EventStream</c> when a streaming region finishes, carrying the
/// per-stage item counts that are the streaming equivalent of a node's outputs~ 🌊📊
/// </summary>
/// <param name="ExecutionId">The execution the region ran in. 🔗.</param>
/// <param name="RegionIndex">The region's stable index. 🔢.</param>
/// <param name="ItemCounts">Items emitted per stage node id. 📊.</param>
/// <param name="Duration">Wall-clock duration of the region. ⏱️.</param>
/// <param name="Timestamp">When the region finished (or, live, when the snapshot was taken). ⏰.</param>
/// <param name="Completed">
/// 📡 Stage 7.6 — false for a live snapshot sent while the region runs (counts so far, elapsed
/// time); true for the final event when it finishes.
/// </param>
/// <remarks>
/// CopilotNote: The 3.2 <c>ExecutionEventBridge</c> already forwards EventStream messages to
/// SignalR, so publishing here is all the monitor needs to learn about stage throughput — no hub
/// or engine-to-API coupling required~ 📡.
/// </remarks>
public record StreamRegionProgress(
    Guid ExecutionId,
    int RegionIndex,
    IReadOnlyDictionary<string, long> ItemCounts,
    TimeSpan Duration,
    DateTimeOffset Timestamp,
    bool Completed = true);

/// <summary>
/// ⏱️ Stage 7.5 — limits on how long a streaming region may run. Registered by the host from
/// <c>Streaming:RegionStallTimeoutMs</c> / <c>Streaming:RegionTimeoutMs</c> (0 = off, the default);
/// a region's source node can override either through its metadata (<c>0</c> = off for that region).
/// </summary>
/// <param name="StallTimeoutMs">Fail when no item moves through any stage for this long (0 = off).</param>
/// <param name="TotalTimeoutMs">Fail when the region runs longer than this (0 = off).</param>
public sealed record StreamingTimeoutOptions(int StallTimeoutMs = 0, int TotalTimeoutMs = 0)
{
    /// <summary>The source-node metadata key overriding the stall timeout (milliseconds; 0 = off).</summary>
    public const string StallMetadataKey = "regionStallTimeoutMs";

    /// <summary>The source-node metadata key overriding the overall timeout (milliseconds; 0 = off).</summary>
    public const string TotalMetadataKey = "regionTimeoutMs";

    /// <summary>The limits for a region whose source node carries <paramref name="sourceMetadata"/>.</summary>
    /// <param name="sourceMetadata">The source node's metadata (null = none).</param>
    /// <returns>The stall and overall limits (null = off).</returns>
    public (TimeSpan? Stall, TimeSpan? Total) For(LanguageExt.HashMap<string, string>? sourceMetadata)
    {
        static TimeSpan? Ms(int value) => value > 0 ? TimeSpan.FromMilliseconds(value) : null;

        int Pick(string key, int fallback)
            => sourceMetadata is { } metadata
               && metadata.Find(key).IfNoneUnsafe((string?)null) is { } raw
               && int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : fallback;

        return (Ms(Pick(StallMetadataKey, this.StallTimeoutMs)), Ms(Pick(TotalMetadataKey, this.TotalTimeoutMs)));
    }
}

/// <summary>
/// 📡 Stage 7.6 — how often a running streaming region publishes live progress. Registered by the
/// host from <c>Streaming:ProgressIntervalMs</c>; when absent, the default applies.
/// </summary>
/// <param name="IntervalMs">Milliseconds between live snapshots (0 or less = final summary only).</param>
public sealed record StreamingProgressOptions(int IntervalMs = StreamingProgressOptions.DefaultIntervalMs)
{
    /// <summary>The default interval.</summary>
    public const int DefaultIntervalMs = 1000;
}
