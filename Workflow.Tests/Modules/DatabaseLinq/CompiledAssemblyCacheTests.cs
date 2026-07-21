// <copyright file="CompiledAssemblyCacheTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.DatabaseLinq;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Workflow.Modules.Database.Abstractions;
using Workflow.Modules.Database.Linq.Execution;
using Workflow.Persistence.Abstractions;
using Xunit;

/// <summary>
/// 📦🔒 Phase 2.4.b.2 — Tests for the compiled-assembly cache: blob keying, HMAC round-trip/tamper,
/// LRU eviction, and definition eviction~ ✨💖.
/// </summary>
public sealed class CompiledAssemblyCacheTests
{
    private const string Sv = "1";

    private readonly InMemoryBlobStore blobStore = new();
    private readonly HmacLinqAssemblySigner signer = new(new EphemeralLinqHmacKeyProvider());

    [Fact]
    public async Task Store_WritesBlobUnderCompiledModulesNamespace()
    {
        var cache = this.NewCache();
        var key = cache.ComputeKey("def1", "node1", "return db.Orders.ToList();", Sv, Tables());

        await cache.StoreAsync(key, Bytes("hello"));

        key.Should().StartWith("compiled-modules/def1/node1/");
        key.Should().EndWith(".dll");
        this.blobStore.Keys.Should().Contain(key);
    }

    [Fact]
    public void SameCodeAndSchema_ProducesSameKey()
    {
        var cache = this.NewCache();
        var a = cache.ComputeKey("def1", "node1", "code", Sv, Tables());
        var b = cache.ComputeKey("def1", "node1", "code", Sv, Tables());
        a.Should().Be(b);
    }

    [Fact]
    public void ChangedCode_ProducesNewKey()
    {
        var cache = this.NewCache();
        var a = cache.ComputeKey("def1", "node1", "code A", Sv, Tables());
        var b = cache.ComputeKey("def1", "node1", "code B", Sv, Tables());
        a.Should().NotBe(b);
    }

    [Fact]
    public void ChangedSchemaVersion_ProducesNewKey()
    {
        var cache = this.NewCache();
        var a = cache.ComputeKey("def1", "node1", "code", "1", Tables());
        var b = cache.ComputeKey("def1", "node1", "code", "2", Tables());
        a.Should().NotBe(b);
    }

    [Fact]
    public void ChangedSelectedTables_ProducesNewKey()
    {
        var cache = this.NewCache();
        var a = cache.ComputeKey("def1", "node1", "code", Sv, Tables());
        var b = cache.ComputeKey(
            "def1",
            "node1",
            "code",
            Sv,
            new[] { new WorkflowTableMetadata("conn", "Customers", Columns: Cols()) });
        a.Should().NotBe(b);
    }

    [Fact]
    public async Task Hmac_RoundTrips()
    {
        var cache = this.NewCache();
        var key = cache.ComputeKey("def1", "node1", "code", Sv, Tables());
        var payload = Bytes("MZ-fake-assembly");

        await cache.StoreAsync(key, payload);

        // Fresh cache instance (empty LRU) over the same blob store → forces a blob read + verify.
        var reader = this.NewCache();
        var got = await reader.TryGetAsync(key);

        got.Should().NotBeNull();
        got!.Should().Equal(payload);
    }

    [Fact]
    public async Task Hmac_TamperedBlob_RejectedOnRead()
    {
        var cache = this.NewCache();
        var key = cache.ComputeKey("def1", "node1", "code", Sv, Tables());
        await cache.StoreAsync(key, Bytes("original"));

        // Tamper with the stored blob bytes directly.
        this.blobStore.Corrupt(key);

        var reader = this.NewCache();
        var got = await reader.TryGetAsync(key);

        got.Should().BeNull("a tampered blob must be rejected (treated as a cache miss)~ 🔒");
    }

    [Fact]
    public async Task Lru_EvictsLeastRecentlyUsed_ReloadsFromBlobStore()
    {
        var cache = this.NewCache(lruCapacity: 2);
        var k1 = cache.ComputeKey("def1", "n1", "c1", Sv, Tables());
        var k2 = cache.ComputeKey("def1", "n2", "c2", Sv, Tables());
        var k3 = cache.ComputeKey("def1", "n3", "c3", Sv, Tables());

        await cache.StoreAsync(k1, Bytes("one"));
        await cache.StoreAsync(k2, Bytes("two"));
        await cache.StoreAsync(k3, Bytes("three")); // evicts k1 from the LRU (capacity 2)

        // k1 is no longer in the LRU — but a read still succeeds by reloading + verifying from the blob store.
        this.blobStore.GetCount(k1).Should().Be(0);
        var got = await cache.TryGetAsync(k1);
        got.Should().NotBeNull();
        this.blobStore.GetCount(k1).Should().Be(1, "k1 was evicted from the LRU and reloaded from the blob store~");
    }

    [Fact]
    public async Task EvictDefinition_RemovesAllNodeBlobs()
    {
        var cache = this.NewCache();
        var k1 = cache.ComputeKey("defX", "n1", "c1", Sv, Tables());
        var k2 = cache.ComputeKey("defX", "n2", "c2", Sv, Tables());
        var other = cache.ComputeKey("defY", "n1", "c1", Sv, Tables());
        await cache.StoreAsync(k1, Bytes("a"));
        await cache.StoreAsync(k2, Bytes("b"));
        await cache.StoreAsync(other, Bytes("c"));

        var deleted = await cache.EvictDefinitionAsync("defX");

        deleted.Should().Be(2);
        this.blobStore.Keys.Should().NotContain(k1).And.NotContain(k2);
        this.blobStore.Keys.Should().Contain(other, "a different definition's blobs are untouched~");
    }

    // ── Helpers 🛠️ ───────────────────────────────────────────────────────────────────────

    private CompiledAssemblyCache NewCache(int lruCapacity = 64)
        => new(
            this.blobStore,
            this.signer,
            Microsoft.Extensions.Options.Options.Create(new LinqCompileCacheOptions { LruCapacity = lruCapacity }));

    private static byte[] Bytes(string s) => System.Text.Encoding.UTF8.GetBytes(s);

    private static IReadOnlyList<WorkflowColumnMetadata> Cols() =>
        new[] { new WorkflowColumnMetadata("id", "integer", false) };

    private static IReadOnlyList<WorkflowTableMetadata> Tables() =>
        new[] { new WorkflowTableMetadata("conn", "Orders", Columns: Cols()) };

    /// <summary>🗃️ Minimal in-memory <see cref="IBlobStore"/> for cache tests~.</summary>
    private sealed class InMemoryBlobStore : IBlobStore
    {
        private readonly ConcurrentDictionary<string, byte[]> store = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, int> getCounts = new(StringComparer.Ordinal);

        public IReadOnlyCollection<string> Keys => this.store.Keys.ToList();

        public int GetCount(string key) => this.getCounts.TryGetValue(key, out var c) ? c : 0;

        public void Corrupt(string key)
        {
            if (this.store.TryGetValue(key, out var bytes) && bytes.Length > 0)
            {
                var copy = (byte[])bytes.Clone();
                copy[^1] ^= 0xFF; // flip the last byte
                this.store[key] = copy;
            }
        }

        public async Task<string> PutAsync(string key, Stream data, string? contentType = null, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            await data.CopyToAsync(ms, ct).ConfigureAwait(false);
            this.store[key] = ms.ToArray();
            return "etag-" + this.store[key].Length;
        }

        public Task<Stream?> GetAsync(string key, CancellationToken ct = default)
        {
            if (this.store.TryGetValue(key, out var bytes))
            {
                this.getCounts.AddOrUpdate(key, 1, (_, c) => c + 1);
                return Task.FromResult<Stream?>(new MemoryStream(bytes, writable: false));
            }

            return Task.FromResult<Stream?>(null);
        }

        public Task<bool> DeleteAsync(string key, CancellationToken ct = default)
            => Task.FromResult(this.store.TryRemove(key, out _));

        public Task<bool> ExistsAsync(string key, CancellationToken ct = default)
            => Task.FromResult(this.store.ContainsKey(key));

        public Task<string> GeneratePresignedUrlAsync(string key, TimeSpan expiry, CancellationToken ct = default)
            => Task.FromResult($"memory://{key}");
    }
}


