// <copyright file="InMemoryBlobStore.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Scripting;

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Workflow.Persistence.Abstractions;

/// <summary>🗃️ Minimal in-memory <see cref="IBlobStore"/> for scripting tests~ ✨.</summary>
public sealed class InMemoryBlobStore : IBlobStore
{
    private readonly ConcurrentDictionary<string, byte[]> store = new(StringComparer.Ordinal);

    public int Count => this.store.Count;

    public Task<string> PutAsync(string key, Stream data, string? contentType = null, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        data.CopyTo(ms);
        this.store[key] = ms.ToArray();
        return Task.FromResult("etag-" + key.GetHashCode());
    }

    public Task<Stream?> GetAsync(string key, CancellationToken ct = default)
        => Task.FromResult<Stream?>(this.store.TryGetValue(key, out var bytes) ? new MemoryStream(bytes, writable: false) : null);

    public Task<bool> DeleteAsync(string key, CancellationToken ct = default)
        => Task.FromResult(this.store.TryRemove(key, out _));

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default)
        => Task.FromResult(this.store.ContainsKey(key));

    public Task<string> GeneratePresignedUrlAsync(string key, TimeSpan expiry, CancellationToken ct = default)
        => Task.FromResult($"memory://{key}");

    /// <summary>Corrupts a stored blob (tamper test helper)~ 🧪.</summary>
    public void Corrupt(string key)
    {
        if (this.store.TryGetValue(key, out var bytes) && bytes.Length > 0)
        {
            var copy = (byte[])bytes.Clone();
            copy[^1] ^= 0xFF;
            this.store[key] = copy;
        }
    }
}
