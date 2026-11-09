// <copyright file="BlobStoreSpillStore.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.Streaming;

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Workflow.Api.Database;
using Workflow.Modules.Streaming;
using Workflow.Persistence.Abstractions;

/// <summary>
/// ☁️ Phase 5.1.6 — spills to the configured <see cref="IBlobStore"/> (Postgres / NATS / S3).
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: This is the answer for hosts whose local disk is slow or throttled — small cloud
/// nodes, overlay filesystems. Scratch leaves the node entirely for infrastructure an operator
/// already sizes and monitors. The trade is a network hop per segment, which is why segments are
/// <b>batched</b> rather than one blob per item: one round trip per item would make spill slower
/// than the OOM it prevents~ 🎯.
/// </para>
/// <para>
/// ⛔ It refuses <see cref="InMemoryBlobStore"/>. That store is a <c>TryAddSingleton</c> dev/test
/// fallback, so spilling to it would move items from one part of the heap to another and call it
/// a memory fix — a silent leak wearing a feature's clothes~ 🚫.
/// </para>
/// </remarks>
public sealed class BlobStoreSpillStore : ISpillStore
{
    /// <summary>
    /// How many records accumulate before a segment blob is written~ 📦.
    /// </summary>
    public const int DefaultSegmentRecords = 1_000;

    private readonly IBlobStore blobs;
    private readonly int segmentRecords;

    /// <summary>
    /// Initializes a new instance of the <see cref="BlobStoreSpillStore"/> class~ ☁️.
    /// </summary>
    /// <param name="blobs">The blob store to spill into.</param>
    /// <param name="segmentRecords">How many records per segment blob.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="blobs"/> is the in-memory fallback store.
    /// </exception>
    public BlobStoreSpillStore(IBlobStore blobs, int? segmentRecords = null)
    {
        ArgumentNullException.ThrowIfNull(blobs);

        // Stage 7.1: not just the dev fallback — the default persistence (SQLite :memory:) has a blob
        // store too, and it lives in memory just the same~ 🧠
        if (blobs is InMemoryBlobStore || blobs.IsInMemory)
        {
            throw new InvalidOperationException(
                $"Streaming:Spill:Store is 'Blob' but the active blob store ({blobs.GetType().Name}) is an in-memory store, " +
                "so spilling there would just move items around the heap. Configure a persistence provider whose blobs " +
                "are durable (Postgres, NATS, S3, or SQLite on a file), or set Streaming:Spill:Store to 'Sqlite' or " +
                "'Directory'~ 🚫");
        }

        this.blobs = blobs;
        this.segmentRecords = segmentRecords ?? DefaultSegmentRecords;

        if (this.segmentRecords <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(segmentRecords), "A segment must hold at least one record~ 📦");
        }
    }

    /// <inheritdoc/>
    public string Name => "blob";

    /// <inheritdoc/>
    public Task<ISpillSession> BeginAsync(SpillSessionKey key, CancellationToken ct = default)
        => Task.FromResult<ISpillSession>(new Session(this.blobs, key, this.segmentRecords));

    /// <inheritdoc/>
    public async Task<int> PurgeExecutionAsync(Guid executionId, CancellationToken ct = default)
    {
        var keys = new List<string>();
        await foreach (var info in this.blobs.ListAsync(SpillSessionKey.PrefixFor(executionId), ct).ConfigureAwait(false))
        {
            keys.Add(info.Key);
        }

        var removed = 0;
        foreach (var key in keys)
        {
            if (await this.blobs.DeleteAsync(key, ct).ConfigureAwait(false))
            {
                removed++;
            }
        }

        return removed;
    }

    /// <inheritdoc/>
    public async Task<DateTimeOffset?> GetLastWrittenAsync(Guid executionId, CancellationToken ct = default)
    {
        // §9.6 — the newest segment's LastModified~
        DateTimeOffset? newest = null;
        await foreach (var info in this.blobs.ListAsync(SpillSessionKey.PrefixFor(executionId), ct).ConfigureAwait(false))
        {
            if (newest is null || info.LastModified > newest)
            {
                newest = info.LastModified;
            }
        }

        return newest;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<Guid> ListExecutionsAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        var seen = new HashSet<Guid>();

        await foreach (var info in this.blobs.ListAsync("spill/", ct).ConfigureAwait(false))
        {
            // key shape: spill/{executionId:N}/{region}/{node}/{segment}
            var parts = info.Key.Split('/');
            if (parts.Length < 2 || !Guid.TryParseExact(parts[1], "N", out var id))
            {
                continue;
            }

            if (seen.Add(id))
            {
                yield return id;
            }
        }
    }

    private sealed class Session : ISpillSession
    {
        private readonly IBlobStore blobs;
        private readonly SpillSessionKey key;
        private readonly int segmentRecords;
        private readonly List<byte[]> pending = [];
        private readonly List<string> writtenSegments = [];
        private int segmentIndex;
        private bool disposed;

        public Session(IBlobStore blobs, SpillSessionKey key, int segmentRecords)
        {
            this.blobs = blobs;
            this.key = key;
            this.segmentRecords = segmentRecords;
        }

        public long Count { get; private set; }

        public long BytesWritten { get; private set; }

        public async Task AppendAsync(ReadOnlyMemory<byte> payload, CancellationToken ct = default)
        {
            ObjectDisposedException.ThrowIf(this.disposed, this);

            this.pending.Add(payload.ToArray());
            this.Count++;

            if (this.pending.Count >= this.segmentRecords)
            {
                await this.WriteSegmentAsync(ct).ConfigureAwait(false);
            }
        }

        public Task FlushAsync(CancellationToken ct = default)
        {
            ObjectDisposedException.ThrowIf(this.disposed, this);
            return this.pending.Count == 0 ? Task.CompletedTask : this.WriteSegmentAsync(ct);
        }

        public async IAsyncEnumerable<byte[]> ReadAllAsync([EnumeratorCancellation] CancellationToken ct = default)
        {
            ObjectDisposedException.ThrowIf(this.disposed, this);

            foreach (var segmentKey in this.writtenSegments)
            {
                await using var stream = await this.blobs.GetAsync(segmentKey, ct).ConfigureAwait(false);
                if (stream is null)
                {
                    continue;
                }

                await foreach (var record in ReadSegmentAsync(stream, ct).ConfigureAwait(false))
                {
                    yield return record;
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (this.disposed)
            {
                return;
            }

            this.disposed = true;
            this.pending.Clear();

            foreach (var segmentKey in this.writtenSegments)
            {
                try
                {
                    await this.blobs.DeleteAsync(segmentKey, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Best-effort — the sweeper reclaims what dispose misses~ 🧹
                }
            }

            this.writtenSegments.Clear();
        }

        /// <summary>
        /// Reads a length-prefixed record stream~ 📖.
        /// </summary>
        /// <remarks>
        /// CopilotNote: Length prefixes rather than a delimiter, because spilled payloads are
        /// arbitrary bytes once encryption is on — there is no byte value safe to use as a
        /// separator, and a "mostly works" delimiter is the kind of bug that only appears on the
        /// one record that happens to contain it~ 🔒
        /// </remarks>
        private static async IAsyncEnumerable<byte[]> ReadSegmentAsync(
            Stream stream,
            [EnumeratorCancellation] CancellationToken ct)
        {
            var header = new byte[4];
            while (true)
            {
                var read = await stream.ReadAtLeastAsync(header, 4, throwOnEndOfStream: false, ct).ConfigureAwait(false);
                if (read < 4)
                {
                    yield break;
                }

                var length = BinaryPrimitives.ReadInt32LittleEndian(header);
                if (length <= 0)
                {
                    yield break;
                }

                var record = new byte[length];
                await stream.ReadExactlyAsync(record, ct).ConfigureAwait(false);
                yield return record;
            }
        }

        private async Task WriteSegmentAsync(CancellationToken ct)
        {
            var segmentKey = string.Create(
                CultureInfo.InvariantCulture,
                $"{this.key.Prefix()}{this.segmentIndex:D6}");

            using var buffer = new MemoryStream();
            foreach (var record in this.pending)
            {
                var header = new byte[4];
                BinaryPrimitives.WriteInt32LittleEndian(header, record.Length);
                buffer.Write(header);
                buffer.Write(record);
            }

            buffer.Position = 0;
            await this.blobs.PutAsync(segmentKey, buffer, "application/octet-stream", ct).ConfigureAwait(false);

            this.BytesWritten += buffer.Length;
            this.writtenSegments.Add(segmentKey);
            this.segmentIndex++;
            this.pending.Clear();
        }
    }
}
