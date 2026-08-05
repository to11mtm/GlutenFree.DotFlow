// <copyright file="BoundedAccumulator.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Streaming;

using System;
using System.Globalization;
using System.Text.Json;
using Workflow.Core.Models;

/// <summary>
/// 🛡️ Phase 5.1 — the <b>one</b> memory policy shared by every stage that buffers items
/// (<c>builtin.stream.collect</c>, streaming <c>aggregate</c>, and the resequencer).
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: The whole point of streaming is bounded memory, so any stage that accumulates
/// must say where the ceiling is. We <b>fail loudly</b> at the limit rather than silently
/// degrading — SnapLogic's equivalent (Gate/Aggregate) just warns "don't do that with big data"
/// and then OOMs, which is exactly the failure mode we don't want~ 🌸.
/// </para>
/// <para>
/// Design: doc 06 §4.5 (D10). Spill-to-storage is <b>reserved</b> (<see cref="AccumulatorLimitBehavior.Spill"/>)
/// and lands in phase 5.1.6 — until then choosing it is a configuration error, not a silent no-op.
/// Guard defaults become host-memory-derived in a later slice; the constant here is a deliberate
/// placeholder to be calibrated by the 5.1.3 proving slice~ 📏.
/// </para>
/// </remarks>
public sealed class BoundedAccumulator
{
    /// <summary>
    /// The default item ceiling when a stage doesn't configure one. 📏.
    /// </summary>
    /// <remarks>Placeholder pending 5.1.3 calibration (design doc 06 §4.5).</remarks>
    public const int DefaultMaxItems = 100_000;

    private readonly BoundedAccumulatorPolicy policy;
    private long bytes;

    /// <summary>
    /// Initializes a new instance of the <see cref="BoundedAccumulator"/> class. 🛡️.
    /// </summary>
    /// <param name="policy">The limits to enforce; null uses <see cref="BoundedAccumulatorPolicy.Default"/>.</param>
    /// <exception cref="NotSupportedException">
    /// Thrown when <see cref="AccumulatorLimitBehavior.Spill"/> is requested before phase 5.1.6.
    /// </exception>
    public BoundedAccumulator(BoundedAccumulatorPolicy? policy = null)
    {
        this.policy = policy ?? BoundedAccumulatorPolicy.Default;

        if (this.policy.OnLimit == AccumulatorLimitBehavior.Spill)
        {
            throw new NotSupportedException(
                "Spill mode isn't available yet (phase 5.1.6) — lower the limit, or collect fewer items~ 💾");
        }
    }

    /// <summary>
    /// Gets how many items have been admitted so far. 🔢.
    /// </summary>
    public long Count { get; private set; }

    /// <summary>
    /// Gets the accumulated payload size in bytes, when a byte ceiling is configured. 🧠.
    /// </summary>
    public long EstimatedBytes => this.bytes;

    /// <summary>
    /// Admits one item, throwing when a ceiling would be crossed. 🛡️.
    /// </summary>
    /// <param name="item">The item about to be buffered.</param>
    /// <param name="nodeId">The node id, so the message says where to look.</param>
    /// <exception cref="StreamAccumulatorLimitException">Thrown when a configured limit is exceeded.</exception>
    public void Admit(StreamItem item, string nodeId)
    {
        ArgumentNullException.ThrowIfNull(item);

        this.Count++;

        if (this.policy.MaxItems is { } maxItems && this.Count > maxItems)
        {
            throw new StreamAccumulatorLimitException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Node '{nodeId}' buffered more than {maxItems} items. Raise its item limit, or keep the data streaming instead of collecting it~ 🪣"));
        }

        // Only pay for measuring when a byte ceiling is actually configured~ 💸
        if (this.policy.MaxBytes is { } maxBytes)
        {
            this.bytes += EstimateBytes(item);

            if (this.bytes > maxBytes)
            {
                throw new StreamAccumulatorLimitException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Node '{nodeId}' buffered about {this.bytes} bytes, over its {maxBytes} byte limit. Raise the limit, or keep the data streaming instead of collecting it~ 🧠"));
            }
        }
    }

    private static long EstimateBytes(StreamItem item)
        => item.Payload switch
        {
            JsonPayload json => json.Value.ValueKind == JsonValueKind.Undefined ? 0 : json.Value.GetRawText().Length,
            _ => 0,
        };
}

/// <summary>
/// 📏 Limits for a buffering streaming stage.
/// </summary>
/// <param name="MaxItems">Maximum items to buffer; null disables the item ceiling. 🔢.</param>
/// <param name="MaxBytes">Maximum estimated payload bytes; null (default) skips byte measurement. 🧠.</param>
/// <param name="OnLimit">What to do at the ceiling. Fail is the default and the only v1 option. 🚦.</param>
public sealed record BoundedAccumulatorPolicy(
    int? MaxItems = BoundedAccumulator.DefaultMaxItems,
    long? MaxBytes = null,
    AccumulatorLimitBehavior OnLimit = AccumulatorLimitBehavior.Fail)
{
    /// <summary>
    /// Gets the default policy: fail past <see cref="BoundedAccumulator.DefaultMaxItems"/>. 🛡️.
    /// </summary>
    public static BoundedAccumulatorPolicy Default { get; } = new();
}

/// <summary>
/// 🚦 What a buffering stage does when it hits its ceiling.
/// </summary>
public enum AccumulatorLimitBehavior
{
    /// <summary>
    /// Fail the node with a message naming the limit. The v1 default~ 🛑.
    /// </summary>
    Fail,

    /// <summary>
    /// Spill accumulated state to configured storage and continue. <b>Reserved for phase 5.1.6</b>~ 💾.
    /// </summary>
    Spill,
}

/// <summary>
/// 🛑 Thrown when a streaming stage buffers past its configured ceiling.
/// </summary>
public sealed class StreamAccumulatorLimitException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StreamAccumulatorLimitException"/> class.
    /// </summary>
    public StreamAccumulatorLimitException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamAccumulatorLimitException"/> class.
    /// </summary>
    /// <param name="message">The message describing which limit was crossed.</param>
    public StreamAccumulatorLimitException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamAccumulatorLimitException"/> class.
    /// </summary>
    /// <param name="message">The message describing which limit was crossed.</param>
    /// <param name="innerException">The inner exception.</param>
    public StreamAccumulatorLimitException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
