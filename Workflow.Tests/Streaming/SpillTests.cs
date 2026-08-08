// ────────────────────────────────────────────────────────────────────────────────
// Copyright © GlutenFree 2025. Made with love by Ami-Chan! UwU 💖
// Licensed under the MIT License.
// ────────────────────────────────────────────────────────────────────────────────

using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Workflow.Api.Database;
using Workflow.Api.Streaming;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin.Stream;
using Workflow.Modules.Streaming;

namespace Workflow.Tests.Streaming;

/// <summary>
/// Phase 5.1.6 — spill mode: serialization, the spillable buffer, the stores, and the guard rails~ 💾
/// </summary>
/// <remarks>
/// CopilotNote: The behaviours worth defending here are the ones whose failure would be *silent*:
/// ordering across the memory/spill boundary, losing <c>Index</c> or <c>Offset</c> on round-trip,
/// spilling into the in-memory dev store, and spill quietly turning into "no limit"~ 🛡️
/// </remarks>
public class SpillTests
{
    #region Helpers

    private static StreamItem Item(int n, long index, SourceOffset? offset = null)
        => StreamItem.FromJson(
            JsonDocument.Parse($$"""{"n":{{n}}}""").RootElement.Clone(),
            index,
            offset);

    private static int NumberOf(StreamItem item)
        => ((JsonPayload)item.Payload).Value.GetProperty("n").GetInt32();

    private static SqliteSpillStore NewSqliteStore()
        => new($"file:spill-{Guid.NewGuid():N}?mode=memory");

    private static SpillSessionKey Key(string node = "collect-1")
        => new(Guid.NewGuid(), 0, node);

    private static async IAsyncEnumerable<StreamItem> Items(
        int count,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return Item(i, i);
        }
    }

    #endregion

    #region Serialization

    [Fact]
    public void Serializer_RoundTrips_Payload()
    {
        var original = Item(42, 7);

        var restored = StreamItemSerializer.Deserialize(StreamItemSerializer.Serialize(original));

        NumberOf(restored).Should().Be(42);
    }

    [Fact]
    public void Serializer_RoundTrips_Index()
    {
        // Index isn't decoration — error documents quote it and ordering reasoning depends on it.
        var restored = StreamItemSerializer.Deserialize(StreamItemSerializer.Serialize(Item(1, 12_345)));

        restored.Index.Should().Be(12_345);
    }

    [Fact]
    public void Serializer_RoundTrips_Offset()
    {
        // Offsets exist so checkpoint/resume (5.1.P2) stays purely additive. A serializer that
        // dropped them would quietly break that future without failing anything today~
        var original = Item(1, 0, new SourceOffset("cursor-abc", 99));

        var restored = StreamItemSerializer.Deserialize(StreamItemSerializer.Serialize(original));

        restored.Offset.Should().NotBeNull();
        restored.Offset!.Token.Should().Be("cursor-abc");
        restored.Offset.Sequence.Should().Be(99);
    }

    [Fact]
    public void Serializer_RoundTrips_EmptyPayload()
    {
        var restored = StreamItemSerializer.Deserialize(
            StreamItemSerializer.Serialize(new StreamItem(StreamPayload.Empty, 3)));

        restored.Payload.Should().BeOfType<EmptyPayload>();
        restored.Index.Should().Be(3);
    }

    [Fact]
    public void Serializer_TreatsUndefinedJson_AsEmpty()
    {
        // An Undefined JsonElement isn't writable JSON. Throwing mid-spill would turn a degenerate
        // item into a job failure, so it degrades to the empty payload instead~
        var act = () => StreamItemSerializer.Serialize(StreamItem.FromJson(default, 0));

        act.Should().NotThrow();
    }

    #endregion

    #region SpillableItemBuffer

    [Fact]
    public async Task Buffer_KeepsEverythingInMemory_BelowTheThreshold()
    {
        var store = NewSqliteStore();
        await using (var buffer = new SpillableItemBuffer(store, NoOpSpillProtector.Instance, Key(), memoryItemThreshold: 100))
        {
            for (var i = 0; i < 50; i++)
            {
                await buffer.AddAsync(Item(i, i));
            }

            buffer.HasSpilled.Should().BeFalse("a job that never grows big should never touch the spill store~");
            buffer.SpilledCount.Should().Be(0);
        }

        await store.DisposeAsync();
    }

    [Fact]
    public async Task Buffer_PreservesOrder_AcrossTheMemorySpillBoundary()
    {
        // 🎯 The one that matters. Items below the threshold live in memory, the rest are spilled,
        // and replay must be memory-then-spill. Getting this wrong is invisible in small tests and
        // catastrophic in exactly the large runs spill exists to serve.
        var store = NewSqliteStore();
        await using (var buffer = new SpillableItemBuffer(store, NoOpSpillProtector.Instance, Key(), memoryItemThreshold: 10))
        {
            for (var i = 0; i < 100; i++)
            {
                await buffer.AddAsync(Item(i, i));
            }

            buffer.HasSpilled.Should().BeTrue();
            buffer.SpilledCount.Should().Be(90);

            var replayed = new List<int>();
            await foreach (var item in buffer.ReadAllAsync())
            {
                replayed.Add(NumberOf(item));
            }

            replayed.Should().Equal(Enumerable.Range(0, 100));
        }

        await store.DisposeAsync();
    }

    [Fact]
    public async Task Buffer_WithZeroThreshold_SpillsEverything()
    {
        var store = NewSqliteStore();
        await using (var buffer = new SpillableItemBuffer(store, NoOpSpillProtector.Instance, Key(), memoryItemThreshold: 0))
        {
            for (var i = 0; i < 20; i++)
            {
                await buffer.AddAsync(Item(i, i));
            }

            buffer.SpilledCount.Should().Be(20);

            var replayed = new List<int>();
            await foreach (var item in buffer.ReadAllAsync())
            {
                replayed.Add(NumberOf(item));
            }

            replayed.Should().Equal(Enumerable.Range(0, 20));
        }

        await store.DisposeAsync();
    }

    [Fact]
    public async Task Buffer_RoundTripsThroughAProtector()
    {
        var store = NewSqliteStore();
        var protector = new ReversingProtector();
        await using (var buffer = new SpillableItemBuffer(store, protector, Key(), memoryItemThreshold: 0))
        {
            await buffer.AddAsync(Item(7, 0));

            var replayed = new List<int>();
            await foreach (var item in buffer.ReadAllAsync())
            {
                replayed.Add(NumberOf(item));
            }

            replayed.Should().Equal(7);
        }

        protector.ProtectCalls.Should().Be(1, "every spilled record goes through the protector~");
        protector.UnprotectCalls.Should().Be(1);
        await store.DisposeAsync();
    }

    [Fact]
    public async Task Buffer_Dispose_ReclaimsTheSpill()
    {
        // Eager cleanup on the happy path is the primary reclaim mechanism; the sweeper is only a
        // safety net for crashes. If dispose leaked, every completed job would leave scratch behind
        // until the timer happened to fire~
        var store = NewSqliteStore();
        var key = Key();

        var buffer = new SpillableItemBuffer(store, NoOpSpillProtector.Instance, key, memoryItemThreshold: 0);
        await buffer.AddAsync(Item(1, 0));
        await buffer.DisposeAsync();

        var remaining = new List<Guid>();
        await foreach (var id in store.ListExecutionsAsync())
        {
            remaining.Add(id);
        }

        remaining.Should().NotContain(key.ExecutionId);
        await store.DisposeAsync();
    }

    #endregion

    #region SqliteSpillStore

    [Fact]
    public async Task SqliteStore_ReplaysInAppendOrder()
    {
        var store = NewSqliteStore();
        await using (var session = await store.BeginAsync(Key()))
        {
            for (var i = 0; i < 500; i++)
            {
                await session.AppendAsync(BitConverter.GetBytes(i));
            }

            var read = new List<int>();
            await foreach (var record in session.ReadAllAsync())
            {
                read.Add(BitConverter.ToInt32(record));
            }

            read.Should().Equal(Enumerable.Range(0, 500));
        }

        await store.DisposeAsync();
    }

    [Fact]
    public async Task SqliteStore_IsolatesSessionsFromEachOther()
    {
        // Two nodes in the same execution must not read each other's scratch.
        var store = NewSqliteStore();
        var executionId = Guid.NewGuid();

        await using (var a = await store.BeginAsync(new SpillSessionKey(executionId, 0, "node-a")))
        await using (var b = await store.BeginAsync(new SpillSessionKey(executionId, 0, "node-b")))
        {
            await a.AppendAsync("aaa"u8.ToArray());
            await b.AppendAsync("bbbb"u8.ToArray());

            var fromA = new List<int>();
            await foreach (var record in a.ReadAllAsync())
            {
                fromA.Add(record.Length);
            }

            fromA.Should().Equal(3);
        }

        await store.DisposeAsync();
    }

    [Fact]
    public async Task SqliteStore_PurgeExecution_RemovesEveryNodesScratch()
    {
        var store = NewSqliteStore();
        var executionId = Guid.NewGuid();

        var a = await store.BeginAsync(new SpillSessionKey(executionId, 0, "node-a"));
        var b = await store.BeginAsync(new SpillSessionKey(executionId, 1, "node-b"));
        await a.AppendAsync("x"u8.ToArray());
        await b.AppendAsync("y"u8.ToArray());

        var removed = await store.PurgeExecutionAsync(executionId);

        removed.Should().Be(2);

        var remaining = new List<Guid>();
        await foreach (var id in store.ListExecutionsAsync())
        {
            remaining.Add(id);
        }

        remaining.Should().NotContain(executionId);
        await store.DisposeAsync();
    }

    #endregion

    #region BlobStoreSpillStore

    [Fact]
    public void BlobSpillStore_RefusesTheInMemoryFallback()
    {
        // ⛔ The in-memory blob store is a TryAddSingleton dev fallback. Spilling into it would move
        // items from one part of the heap to another and call it a memory fix — a silent leak
        // wearing a feature's clothes. Refusing loudly is the entire point.
        var act = () => new BlobStoreSpillStore(new InMemoryBlobStore());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*in-memory*");
    }

    [Fact]
    public async Task BlobSpillStore_ReplaysInAppendOrder_AcrossSegmentBoundaries()
    {
        // Records are batched into segment blobs, so the interesting case is more records than fit
        // in one segment — a reader that forgot to continue to the next segment would silently
        // return a prefix.
        var blobs = new FakeBlobStore();
        var store = new BlobStoreSpillStore(blobs, segmentRecords: 10);
        await using var session = await store.BeginAsync(Key());

        for (var i = 0; i < 95; i++)
        {
            await session.AppendAsync(BitConverter.GetBytes(i));
        }

        await session.FlushAsync();

        var read = new List<int>();
        await foreach (var record in session.ReadAllAsync())
        {
            read.Add(BitConverter.ToInt32(record));
        }

        read.Should().Equal(Enumerable.Range(0, 95));
    }

    [Fact]
    public async Task BlobSpillStore_HandlesArbitraryRecordBytes()
    {
        // Once encryption is on, records are arbitrary bytes — including whatever a delimiter-based
        // format would have used as a separator. Length prefixes are what make that safe.
        var blobs = new FakeBlobStore();
        var store = new BlobStoreSpillStore(blobs, segmentRecords: 4);
        await using var session = await store.BeginAsync(Key());

        var payloads = new[]
        {
            new byte[] { 0x00, 0x0A, 0xFF },
            new byte[] { 0x0A, 0x0A, 0x0A, 0x0A },
            new byte[] { 0xFF },
        };

        foreach (var payload in payloads)
        {
            await session.AppendAsync(payload);
        }

        await session.FlushAsync();

        var read = new List<byte[]>();
        await foreach (var record in session.ReadAllAsync())
        {
            read.Add(record);
        }

        read.Should().HaveCount(3);
        read[0].Should().Equal(payloads[0]);
        read[1].Should().Equal(payloads[1]);
        read[2].Should().Equal(payloads[2]);
    }

    [Fact]
    public async Task BlobSpillStore_Dispose_DeletesItsSegments()
    {
        var blobs = new FakeBlobStore();
        var store = new BlobStoreSpillStore(blobs, segmentRecords: 2);
        var session = await store.BeginAsync(Key());

        await session.AppendAsync("a"u8.ToArray());
        await session.AppendAsync("b"u8.ToArray());
        blobs.Keys.Should().NotBeEmpty();

        await session.DisposeAsync();

        blobs.Keys.Should().BeEmpty();
    }

    [Fact]
    public async Task BlobSpillStore_PurgeExecution_RemovesEverythingUnderThePrefix()
    {
        var blobs = new FakeBlobStore();
        var store = new BlobStoreSpillStore(blobs, segmentRecords: 1);
        var executionId = Guid.NewGuid();

        var session = await store.BeginAsync(new SpillSessionKey(executionId, 0, "n1"));
        await session.AppendAsync("a"u8.ToArray());
        await session.AppendAsync("b"u8.ToArray());

        var removed = await store.PurgeExecutionAsync(executionId);

        removed.Should().Be(2);
        blobs.Keys.Should().BeEmpty();
    }

    #endregion

    #region Module integration

    [Fact]
    public async Task Collect_WithSpillEnabled_ButNoStoreConfigured_FailsLoudly()
    {
        // 🚧 The whole point of "no implicit default": a host without a spill store must say so,
        // naming the setting, rather than silently landing scratch in a temp directory nobody
        // provisioned.
        var module = new StreamCollectModule();
        var context = ContextWith(new ServiceCollection().BuildServiceProvider(), spill: true);

        var result = await module.ExecuteTerminalAsync(context, Items(5));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Streaming:Spill:Store");
    }

    [Fact]
    public async Task Collect_WithSpillEnabled_CollectsEverythingInOrder()
    {
        var store = NewSqliteStore();
        var services = new ServiceCollection()
            .AddSingleton<ISpillStore>(store)
            .AddSingleton<ISpillProtector>(NoOpSpillProtector.Instance)
            .BuildServiceProvider();

        var module = new StreamCollectModule();
        var context = ContextWith(services, spill: true);

        var result = await module.ExecuteTerminalAsync(context, Items(200));

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.Outputs!["count"].Should().Be(200L);
        await store.DisposeAsync();
    }

    [Fact]
    public async Task Collect_WithSpillEnabled_StillEnforcesTheCeiling()
    {
        // Spill changes *where* items live, not whether there's a bound. A spill flag that meant
        // "unlimited" would quietly undo the guard the whole streaming design rests on~
        var store = NewSqliteStore();
        var services = new ServiceCollection()
            .AddSingleton<ISpillStore>(store)
            .AddSingleton<ISpillProtector>(NoOpSpillProtector.Instance)
            .BuildServiceProvider();

        var module = new StreamCollectModule();
        var context = ContextWith(services, spill: true, maxItems: 10);

        var result = await module.ExecuteTerminalAsync(context, Items(50));

        result.Success.Should().BeFalse("the item ceiling still applies when spilling~");
        result.ErrorMessage.Should().Contain("10");
        await store.DisposeAsync();
    }

    [Fact]
    public async Task Collect_WithoutSpill_BehavesExactlyAsBefore()
    {
        var module = new StreamCollectModule();
        var context = ContextWith(new ServiceCollection().BuildServiceProvider(), spill: false);

        var result = await module.ExecuteTerminalAsync(context, Items(25));

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.Outputs!["count"].Should().Be(25L);
    }

    private static ModuleExecutionContext ContextWith(
        IServiceProvider services,
        bool spill,
        int? maxItems = null)
    {
        var properties = new Dictionary<string, object?>(StringComparer.Ordinal) { ["spill"] = spill };
        if (maxItems is { } limit)
        {
            properties["maxItems"] = limit;
        }

        return new ModuleExecutionContext
        {
            Inputs = new Dictionary<string, object?>(),
            Properties = properties,
            Variables = new Dictionary<string, object?>(),
            Logger = NullLogger.Instance,
            Services = services,
            ExecutionId = Guid.NewGuid(),
            NodeId = "collect-1",
        };
    }

    #endregion

    #region Test doubles

    /// <summary>A protector that is trivially reversible, so encryption wiring is observable~ 🧪.</summary>
    private sealed class ReversingProtector : ISpillProtector
    {
        public bool IsEnabled => true;

        public int ProtectCalls { get; private set; }

        public int UnprotectCalls { get; private set; }

        public byte[] Protect(ReadOnlySpan<byte> plaintext)
        {
            this.ProtectCalls++;
            var copy = plaintext.ToArray();
            Array.Reverse(copy);
            return copy;
        }

        public byte[] Unprotect(ReadOnlySpan<byte> protectedBytes)
        {
            this.UnprotectCalls++;
            var copy = protectedBytes.ToArray();
            Array.Reverse(copy);
            return copy;
        }
    }

    /// <summary>An in-test blob store that is *not* <c>InMemoryBlobStore</c>, so spill accepts it~ 🧪.</summary>
    private sealed class FakeBlobStore : Workflow.Persistence.Abstractions.IBlobStore
    {
        private readonly Dictionary<string, byte[]> store = new(StringComparer.Ordinal);

        public IReadOnlyCollection<string> Keys => this.store.Keys.ToList();

        public async Task<string> PutAsync(string key, System.IO.Stream data, string? contentType = null, CancellationToken ct = default)
        {
            using var ms = new System.IO.MemoryStream();
            await data.CopyToAsync(ms, ct);
            this.store[key] = ms.ToArray();
            return "etag";
        }

        public Task<System.IO.Stream?> GetAsync(string key, CancellationToken ct = default)
            => Task.FromResult<System.IO.Stream?>(
                this.store.TryGetValue(key, out var bytes) ? new System.IO.MemoryStream(bytes, writable: false) : null);

        public Task<bool> DeleteAsync(string key, CancellationToken ct = default)
            => Task.FromResult(this.store.Remove(key));

        public Task<bool> ExistsAsync(string key, CancellationToken ct = default)
            => Task.FromResult(this.store.ContainsKey(key));

        public async IAsyncEnumerable<Workflow.Persistence.Abstractions.BlobInfo> ListAsync(
            string prefix,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var kvp in this.store.ToList())
            {
                if (kvp.Key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    yield return new Workflow.Persistence.Abstractions.BlobInfo(kvp.Key, kvp.Value.Length, DateTimeOffset.UtcNow);
                }
            }

            await Task.CompletedTask;
        }

        public Task<string> GeneratePresignedUrlAsync(string key, TimeSpan expiry, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    #endregion
}
