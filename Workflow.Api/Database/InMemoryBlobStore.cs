// <copyright file="InMemoryBlobStore.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.Database;

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Workflow.Persistence.Abstractions;

/// <summary>
/// 🗃️ Process-local in-memory <see cref="IBlobStore"/> fallback (Phase 2.4.b.5)~ ✨.
/// </summary>
/// <remarks>
/// CopilotNote: Registered via <c>TryAddSingleton</c> so a real persistence-backed blob store wins
/// when configured. This exists so the typed-linq compiled-assembly cache works out of the box for
/// dev/tests without a persistence provider. **Not durable** — compiled blobs are lost on restart
/// (they're re-compiled on demand). A durable local-FS fallback is tracked for host config (Q5)~ 🌸.
/// </remarks>
public sealed class InMemoryBlobStore : IBlobStore
{
    private readonly ConcurrentDictionary<string, byte[]> store = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public async Task<string> PutAsync(string key, Stream data, string? contentType = null, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        await data.CopyToAsync(ms, ct).ConfigureAwait(false);
        this.store[key] = ms.ToArray();
        return "etag-" + this.store[key].Length;
    }

    /// <inheritdoc/>
    public Task<Stream?> GetAsync(string key, CancellationToken ct = default)
        => Task.FromResult<Stream?>(this.store.TryGetValue(key, out var b) ? new MemoryStream(b, writable: false) : null);

    /// <inheritdoc/>
    public Task<bool> DeleteAsync(string key, CancellationToken ct = default)
        => Task.FromResult(this.store.TryRemove(key, out _));

    /// <inheritdoc/>
    public Task<bool> ExistsAsync(string key, CancellationToken ct = default)
        => Task.FromResult(this.store.ContainsKey(key));

    /// <inheritdoc/>
    public Task<string> GeneratePresignedUrlAsync(string key, TimeSpan expiry, CancellationToken ct = default)
        => Task.FromResult($"memory://{key}");
}

