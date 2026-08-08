// <copyright file="NatsBlobStore.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Nats.Repositories;

using System.Runtime.CompilerServices;
using NATS.Client.ObjectStore;
using Workflow.Persistence.Abstractions;

/// <summary>
/// 🪣 NATS JetStream Object Store implementation of <see cref="IBlobStore"/>~ ✨💖
/// </summary>
/// <remarks>
/// CopilotNote: This is the *Object Store*, not the KV store — KV is the wrong tool for binaries
/// (whole-value messages, small max value size), whereas the Object Store chunks objects across
/// JetStream messages and is built for exactly this. Object names are opaque here: nats.net
/// base64url-encodes the name into the meta subject, so keys containing <c>/</c>, <c>:</c> or
/// spaces are all safe~ 🔑
/// </remarks>
public sealed class NatsBlobStore : IBlobStore
{
    /// <summary>The Object Store bucket name used for workflow blobs~ 🪣.</summary>
    public const string BucketName = "WF_BLOBS";

    private readonly INatsObjStore _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="NatsBlobStore"/> class~ 🔌.
    /// </summary>
    /// <param name="store">The object store bucket handle.</param>
    public NatsBlobStore(INatsObjStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc/>
    public async Task<string> PutAsync(
        string key,
        Stream data,
        string? contentType = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(data);

        // CopilotNote: leaveOpen: true — the caller owns the stream they handed us~
        var meta = await _store
            .PutAsync(key, data, leaveOpen: true, cancellationToken: ct)
            .ConfigureAwait(false);

        // The digest is a content hash, which is exactly the contract an ETag wants~ 🔖
        return meta.Digest ?? string.Empty;
    }

    /// <inheritdoc/>
    public async Task<Stream?> GetAsync(string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        try
        {
            var bytes = await _store.GetBytesAsync(key, ct).ConfigureAwait(false);
            return new MemoryStream(bytes, writable: false);
        }
        catch (NatsObjNotFoundException)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync(string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        try
        {
            await _store.DeleteAsync(key, ct).ConfigureAwait(false);
            return true;
        }
        catch (NatsObjNotFoundException)
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        try
        {
            await _store.GetInfoAsync(key, cancellationToken: ct).ConfigureAwait(false);
            return true;
        }
        catch (NatsObjNotFoundException)
        {
            return false;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// CopilotNote: The Object Store has no server-side prefix filter, so the prefix is applied
    /// client-side over the metadata listing. That's metadata only — no object bodies are fetched —
    /// so the cost is proportional to bucket *cardinality*, not bucket size~ 📃
    /// </remarks>
    public async IAsyncEnumerable<BlobInfo> ListAsync(
        string prefix,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(prefix);

        await foreach (var meta in _store.ListAsync(cancellationToken: ct).ConfigureAwait(false))
        {
            if (meta.Deleted || meta.Name is null)
            {
                continue;
            }

            if (meta.Name.StartsWith(prefix, StringComparison.Ordinal))
            {
                yield return new BlobInfo(meta.Name, (long)meta.Size, meta.MTime);
            }
        }
    }

    /// <inheritdoc/>
    public Task<string> GeneratePresignedUrlAsync(string key, TimeSpan expiry, CancellationToken ct = default)
        => throw new NotSupportedException(
            "Presigned URLs are not supported by the NATS object store — NATS has no HTTP object " +
            "endpoint to sign against. Use the S3 blob store if you need them~ 🔗");
}
