// <copyright file="CompiledAssemblyCache.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Linq.Execution;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Workflow.Modules.Database.Abstractions;
using Workflow.Modules.Database.Linq.Abstractions;
using Workflow.Persistence.Abstractions;

/// <summary>⚙️ Options for the compiled-assembly cache~ ✨.</summary>
public sealed class LinqCompileCacheOptions
{
    /// <summary>Gets or sets the max number of assemblies held in the in-memory LRU (default 64)~.</summary>
    public int LruCapacity { get; set; } = 64;
}

/// <summary>
/// 📦 <see cref="ICompiledAssemblyCache"/> over <c>IBlobStore</c> with HMAC signing + an in-memory LRU (2.4.b.2)~ 💖.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: <c>IBlobStore</c> has no list-by-prefix API, so <see cref="EvictDefinitionAsync"/>
/// deletes the keys this process has stored (tracked in-memory). Cross-restart orphan GC needs a
/// blob-store list API — tracked as a follow-up~ 🌸.
/// </para>
/// </remarks>
public sealed class CompiledAssemblyCache : ICompiledAssemblyCache
{
    private readonly IBlobStore blobStore;
    private readonly ILinqAssemblySigner signer;
    private readonly ILogger<CompiledAssemblyCache> logger;
    private readonly int capacity;

    // LRU: most-recently-used at the front of the list.
    private readonly object gate = new();
    private readonly LinkedList<string> lruOrder = new();
    private readonly Dictionary<string, (LinkedListNode<string> Node, byte[] Bytes)> lru = new(StringComparer.Ordinal);

    // Keys stored this process, so EvictDefinition can delete without a blob-store list API.
    private readonly ConcurrentDictionary<string, byte> knownKeys = new(StringComparer.Ordinal);

    /// <summary>Initializes a new instance of the <see cref="CompiledAssemblyCache"/> class~ 📦.</summary>
    /// <param name="blobStore">The blob store.</param>
    /// <param name="signer">The HMAC signer.</param>
    /// <param name="options">Cache options.</param>
    /// <param name="logger">Logger (optional).</param>
    public CompiledAssemblyCache(
        IBlobStore blobStore,
        ILinqAssemblySigner signer,
        IOptions<LinqCompileCacheOptions>? options = null,
        ILogger<CompiledAssemblyCache>? logger = null)
    {
        this.blobStore = blobStore ?? throw new ArgumentNullException(nameof(blobStore));
        this.signer = signer ?? throw new ArgumentNullException(nameof(signer));
        this.logger = logger ?? NullLogger<CompiledAssemblyCache>.Instance;
        this.capacity = Math.Max(1, (options?.Value ?? new LinqCompileCacheOptions()).LruCapacity);
    }

    /// <inheritdoc/>
    public string ComputeKey(
        string definitionId,
        string nodeId,
        string userCode,
        string schemaVersion,
        IReadOnlyList<WorkflowTableMetadata> selectedTables)
        => CompiledAssemblyKey.Compute(definitionId, nodeId, userCode, schemaVersion, selectedTables);

    /// <inheritdoc/>
    public async ValueTask<byte[]?> TryGetAsync(string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (this.TryGetFromLru(key, out var cached))
        {
            return cached;
        }

        await using var stream = await this.blobStore.GetAsync(key, ct).ConfigureAwait(false);
        if (stream is null)
        {
            return null;
        }

        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct).ConfigureAwait(false);
        var signed = ms.ToArray();

        if (!this.signer.TryVerify(signed, out var assemblyBytes))
        {
            // Tampered or corrupt — treat as a miss so we never hand bad bytes to the ALC loader~ 🔒
            this.logger.LogWarning("Compiled-assembly blob '{Key}' failed HMAC verification — treating as a cache miss~", key);
            return null;
        }

        this.knownKeys.TryAdd(key, 0);
        this.PutInLru(key, assemblyBytes);
        return assemblyBytes;
    }

    /// <inheritdoc/>
    public async ValueTask StoreAsync(string key, byte[] assemblyBytes, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(assemblyBytes);

        var signed = this.signer.Sign(assemblyBytes);
        using var ms = new MemoryStream(signed, writable: false);
        await this.blobStore.PutAsync(key, ms, "application/octet-stream", ct).ConfigureAwait(false);

        this.knownKeys.TryAdd(key, 0);
        this.PutInLru(key, assemblyBytes);
    }

    /// <inheritdoc/>
    public async ValueTask<int> EvictDefinitionAsync(string definitionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);

        var prefix = CompiledAssemblyKey.DefinitionPrefix(definitionId);
        var toDelete = this.knownKeys.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList();

        var deleted = 0;
        foreach (var key in toDelete)
        {
            if (await this.blobStore.DeleteAsync(key, ct).ConfigureAwait(false))
            {
                deleted++;
            }

            this.knownKeys.TryRemove(key, out _);
            this.RemoveFromLru(key);
        }

        return deleted;
    }

    private bool TryGetFromLru(string key, out byte[] bytes)
    {
        lock (this.gate)
        {
            if (this.lru.TryGetValue(key, out var entry))
            {
                this.lruOrder.Remove(entry.Node);
                this.lruOrder.AddFirst(entry.Node);
                bytes = entry.Bytes;
                return true;
            }
        }

        bytes = Array.Empty<byte>();
        return false;
    }

    private void PutInLru(string key, byte[] bytes)
    {
        lock (this.gate)
        {
            if (this.lru.TryGetValue(key, out var existing))
            {
                this.lruOrder.Remove(existing.Node);
                this.lruOrder.AddFirst(existing.Node);
                this.lru[key] = (existing.Node, bytes);
                return;
            }

            var node = new LinkedListNode<string>(key);
            this.lruOrder.AddFirst(node);
            this.lru[key] = (node, bytes);

            while (this.lru.Count > this.capacity && this.lruOrder.Last is { } last)
            {
                this.lruOrder.RemoveLast();
                this.lru.Remove(last.Value);
            }
        }
    }

    private void RemoveFromLru(string key)
    {
        lock (this.gate)
        {
            if (this.lru.TryGetValue(key, out var entry))
            {
                this.lruOrder.Remove(entry.Node);
                this.lru.Remove(key);
            }
        }
    }
}

