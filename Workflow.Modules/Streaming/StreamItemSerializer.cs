// <copyright file="StreamItemSerializer.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Streaming;

using System;
using System.Buffers;
using System.Text.Json;
using Workflow.Core.Models;

/// <summary>
/// 🧾 Phase 5.1.6 — turns a <see cref="StreamItem"/> into spillable bytes and back.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: Round-tripping must be <b>lossless for everything the pipeline can observe</b>.
/// <see cref="StreamItem.Index"/> matters because downstream ordering and error documents quote it,
/// and <see cref="StreamItem.Offset"/> matters because checkpoint/resume (5.1.P2) will read it — a
/// serializer that quietly dropped either would turn spill into a subtle correctness bug rather
/// than a memory feature~ 🎯.
/// </para>
/// <para>
/// The format is a small JSON envelope. Not the most compact option, but spill is already
/// IO-bound and JSON keeps the payload inspectable when someone is debugging a stuck job~ 🔍.
/// </para>
/// </remarks>
public static class StreamItemSerializer
{
    private static readonly JsonWriterOptions WriterOptions = new() { Indented = false };

    /// <summary>
    /// Serializes one item~ ⬇️.
    /// </summary>
    /// <param name="item">The item to serialize.</param>
    /// <returns>The serialized bytes.</returns>
    public static byte[] Serialize(StreamItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var buffer = new ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteNumber("i", item.Index);

            switch (item.Payload)
            {
                case JsonPayload json when json.Value.ValueKind != JsonValueKind.Undefined:
                    writer.WritePropertyName("p");
                    json.Value.WriteTo(writer);
                    break;

                case JsonPayload:
                    // CopilotNote: an Undefined JsonElement can't be written — it isn't JSON at
                    // all. Treat it as the empty payload rather than throwing mid-spill~
                    break;

                default:
                    break;
            }

            if (item.Offset is { } offset)
            {
                writer.WriteStartObject("o");
                writer.WriteString("t", offset.Token);
                writer.WriteNumber("s", offset.Sequence);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// Deserializes one item~ ⬆️.
    /// </summary>
    /// <param name="bytes">Bytes produced by <see cref="Serialize"/>.</param>
    /// <returns>The reconstructed item.</returns>
    public static StreamItem Deserialize(ReadOnlySpan<byte> bytes)
    {
        // CopilotNote: JsonDocument.Parse over a span copies, which is what we want — the returned
        // item outlives this call and a JsonElement over a pooled buffer would dangle~ 🔒
        using var doc = JsonDocument.Parse(bytes.ToArray());
        var root = doc.RootElement;

        var index = root.TryGetProperty("i", out var indexElement) && indexElement.TryGetInt64(out var parsedIndex)
            ? parsedIndex
            : 0L;

        SourceOffset? offset = null;
        if (root.TryGetProperty("o", out var offsetElement) && offsetElement.ValueKind == JsonValueKind.Object)
        {
            var token = offsetElement.TryGetProperty("t", out var t) ? t.GetString() ?? string.Empty : string.Empty;
            var sequence = offsetElement.TryGetProperty("s", out var s) && s.TryGetInt64(out var parsedSeq)
                ? parsedSeq
                : 0L;
            offset = new SourceOffset(token, sequence);
        }

        if (!root.TryGetProperty("p", out var payloadElement))
        {
            return new StreamItem(StreamPayload.Empty, index, offset);
        }

        // Clone detaches the element from the JsonDocument we're about to dispose~ 🧬
        return new StreamItem(new JsonPayload(payloadElement.Clone()), index, offset);
    }
}
