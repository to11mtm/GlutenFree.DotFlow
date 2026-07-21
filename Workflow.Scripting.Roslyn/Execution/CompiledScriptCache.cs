// <copyright file="CompiledScriptCache.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Scripting.Roslyn.Execution;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Workflow.Persistence.Abstractions;
using Workflow.Scripting.Roslyn.Abstractions;

/// <summary>
/// 📦 HMAC-verified, LRU-fronted <see cref="ICompiledScriptCache"/> over an <see cref="IBlobStore"/>~ ✨.
/// </summary>
public sealed class CompiledScriptCache : ICompiledScriptCache
{
    private readonly IBlobStore blobStore;
    private readonly IScriptAssemblySigner signer;
    private readonly int capacity;
    private readonly object gate = new();
    private readonly Dictionary<string, byte[]> lruMap = new(StringComparer.Ordinal);
    private readonly LinkedList<string> lru = new();

    /// <summary>Initializes a new instance of the <see cref="CompiledScriptCache"/> class~ 📦.</summary>
    /// <param name="blobStore">The backing blob store.</param>
    /// <param name="signer">The HMAC signer.</param>
    /// <param name="lruCapacity">Max in-memory verified assemblies (default 64).</param>
    public CompiledScriptCache(IBlobStore blobStore, IScriptAssemblySigner signer, int lruCapacity = 64)
    {
        this.blobStore = blobStore;
        this.signer = signer;
        this.capacity = Math.Max(1, lruCapacity);
    }

    /// <inheritdoc/>
    public async Task<string> StoreAsync(string key, byte[] assemblyBytes, CancellationToken ct = default)
    {
        var signed = this.signer.Sign(assemblyBytes);
        using var ms = new MemoryStream(signed, writable: false);
        var result = await this.blobStore.PutAsync(key, ms, "application/octet-stream", ct).ConfigureAwait(false);
        this.Remember(key, assemblyBytes);
        return result;
    }

    /// <inheritdoc/>
    public async Task<byte[]?> TryGetAsync(string key, CancellationToken ct = default)
    {
        lock (this.gate)
        {
            if (this.lruMap.TryGetValue(key, out var cached))
            {
                this.Touch(key);
                return cached;
            }
        }

        var stream = await this.blobStore.GetAsync(key, ct).ConfigureAwait(false);
        if (stream is null)
        {
            return null;
        }

        byte[] tagged;
        await using (stream)
        {
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct).ConfigureAwait(false);
            tagged = ms.ToArray();
        }

        if (!this.signer.TryVerify(tagged, out var verified))
        {
            // Tampered — treat as a miss, never hand to the loader~ 🛡️
            return null;
        }

        this.Remember(key, verified);
        return verified;
    }

    private void Remember(string key, byte[] bytes)
    {
        lock (this.gate)
        {
            this.lruMap[key] = bytes;
            this.Touch(key);
            while (this.lruMap.Count > this.capacity && this.lru.Last is { } last)
            {
                this.lru.RemoveLast();
                this.lruMap.Remove(last.Value);
            }
        }
    }

    private void Touch(string key)
    {
        this.lru.Remove(key);
        this.lru.AddFirst(key);
    }
}
