// ────────────────────────────────────────────────────────────────────────────────
// Copyright © GlutenFree 2025. Made with love by Ami-Chan! UwU 💖
// Licensed under the MIT License.
// ────────────────────────────────────────────────────────────────────────────────

namespace Workflow.Core.Models;

using System.Text.Json;

/// <summary>
/// 🌊 A single element flowing through a streaming region (Phase 5.1.0).
/// </summary>
/// <param name="Payload">The item's payload — JSON in v1, binary reserved. 📦.</param>
/// <param name="Index">Zero-based ordinal within the producing stage's output. 🔢.</param>
/// <param name="Offset">Optional source offset for future resumability. 📍.</param>
/// <param name="IsTombstone">
/// Engine-internal marker: a dropped item's placeholder so a downstream resequencer can advance
/// past the gap without stalling. Modules never create or observe tombstones — the region executor
/// injects them when a stage skips an item, and filters them out before the next module sees the
/// stream. 🪦.
/// </param>
/// <remarks>
/// <para>
/// CopilotNote: This is the streaming counterpart to a node's single payload. A batch run passes
/// one value through the graph; a streaming region passes many <see cref="StreamItem"/>s through
/// bounded channels with backpressure~ 🌊.
/// </para>
/// <para>
/// Design: <see href="../../new-feature-design/snaplogic-analysis/06-streaming-data-plane-design.md">06 — Streaming Data Plane</see>
/// decisions D4 (payload union), D8 (tombstones), D12 (offset enablers).
/// </para>
/// </remarks>
public record StreamItem(
    StreamPayload Payload,
    long Index = 0,
    SourceOffset? Offset = null,
    bool IsTombstone = false)
{
    /// <summary>
    /// Creates a JSON-payload item. 🌸.
    /// </summary>
    /// <param name="json">The JSON payload.</param>
    /// <param name="index">Zero-based ordinal within the producing stage's output.</param>
    /// <param name="offset">Optional source offset.</param>
    /// <returns>A new <see cref="StreamItem"/>.</returns>
    public static StreamItem FromJson(JsonElement json, long index = 0, SourceOffset? offset = null)
        => new(new JsonPayload(json), index, offset);

    /// <summary>
    /// Creates a tombstone that preserves <paramref name="index"/> for resequencing. 🪦.
    /// </summary>
    /// <param name="index">The ordinal of the dropped item.</param>
    /// <param name="offset">The dropped item's offset, when known.</param>
    /// <returns>A tombstone <see cref="StreamItem"/>.</returns>
    public static StreamItem Tombstone(long index, SourceOffset? offset = null)
        => new(StreamPayload.Empty, index, offset, IsTombstone: true);
}

/// <summary>
/// 📦 The payload carried by a <see cref="StreamItem"/> — a closed union.
/// </summary>
/// <remarks>
/// CopilotNote: v1 ships <see cref="JsonPayload"/> only. <c>BinaryPayload</c> is deliberately
/// *reserved* (documented, not implemented) so adding binary streams later is additive and the
/// module contract never changes — see design doc 06 §3.3 (D4)~ 💖.
/// </remarks>
public abstract record StreamPayload
{
    /// <summary>
    /// Gets the empty payload used by tombstones. 🪦.
    /// </summary>
    public static StreamPayload Empty { get; } = new EmptyPayload();
}

/// <summary>
/// 🧾 A JSON document payload — the only payload kind in v1.
/// </summary>
/// <param name="Value">The JSON element.</param>
public sealed record JsonPayload(JsonElement Value) : StreamPayload;

/// <summary>
/// 🪦 The payload of a tombstone — carries no data.
/// </summary>
public sealed record EmptyPayload : StreamPayload;

/// <summary>
/// 📍 A source position, reserved for future checkpoint/resume support (Phase 5.1.P2).
/// </summary>
/// <param name="Token">
/// An opaque, source-defined token to seek with (page cursor, byte offset, keyset value). 🔑.
/// </param>
/// <param name="Sequence">
/// A monotonic ordinal within the source — gives every source a uniform ordering key for
/// resequencing even when <paramref name="Token"/> is not numeric. 🔢.
/// </param>
/// <remarks>
/// CopilotNote: Nothing consumes offsets in 5.1 — they exist so source-offset checkpointing stays
/// a purely additive later phase (design doc 06 §5.2, D12)~ ✨.
/// </remarks>
public record SourceOffset(string Token, long Sequence);
