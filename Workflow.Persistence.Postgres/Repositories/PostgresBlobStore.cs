// <copyright file="PostgresBlobStore.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Postgres.Repositories;

using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using LinqToDB;
using LinqToDB.Async;
using Workflow.Persistence.Abstractions;
using Workflow.Persistence.Postgres.Data;
using Workflow.Persistence.Postgres.Data.Entities;

/// <summary>
/// 🐘 PostgreSQL-backed implementation of <see cref="IBlobStore"/>~ ✨💖
/// </summary>
/// <remarks>
/// CopilotNote: Stores bytes in a <c>BYTEA</c> column. Sized for megabyte-scale objects — compiled
/// assemblies and streaming spill segments — not for gigabyte media. If a genuine >1 GB single-blob
/// need ever appears, that's the moment to reconsider Large Objects, not before~ 🗃️
/// </remarks>
public sealed class PostgresBlobStore : IBlobStore
{
    private readonly WorkflowDataConnectionFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresBlobStore"/> class~ 🔌.
    /// </summary>
    /// <param name="factory">The data connection factory.</param>
    public PostgresBlobStore(WorkflowDataConnectionFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
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

        using var ms = new MemoryStream();
        await data.CopyToAsync(ms, ct).ConfigureAwait(false);
        var bytes = ms.ToArray();
        var etag = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;

        await using var db = _factory.Create();

        // CopilotNote: single-statement upsert — a read-then-write would race two hosts writing
        // the same key, and blob writes are idempotent by content anyway~
        await db.Blobs
            .Merge()
            .Using(
            [
                new BlobEntity
                {
                    Key = key,
                    Data = bytes,
                    ContentType = contentType,
                    ETag = etag,
                    ByteSize = bytes.Length,
                    CreatedAt = now,
                    UpdatedAt = now,
                },
            ])
            .On((t, s) => t.Key == s.Key)
            .UpdateWhenMatched((t, s) => new BlobEntity
            {
                Key = t.Key,
                Data = s.Data,
                ContentType = s.ContentType,
                ETag = s.ETag,
                ByteSize = s.ByteSize,
                CreatedAt = t.CreatedAt,
                UpdatedAt = s.UpdatedAt,
            })
            .InsertWhenNotMatched()
            .MergeAsync(ct)
            .ConfigureAwait(false);

        return etag;
    }

    /// <inheritdoc/>
    public async Task<Stream?> GetAsync(string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await using var db = _factory.Create();
        var bytes = await db.Blobs
            .Where(b => b.Key == key)
            .Select(b => b.Data)
            .FirstOrDefaultAsync(token: ct)
            .ConfigureAwait(false);

        return bytes is null ? null : new MemoryStream(bytes, writable: false);
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync(string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await using var db = _factory.Create();
        var affected = await db.Blobs
            .Where(b => b.Key == key)
            .DeleteAsync(token: ct)
            .ConfigureAwait(false);

        return affected > 0;
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await using var db = _factory.Create();
        return await db.Blobs
            .AnyAsync(b => b.Key == key, token: ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<BlobInfo> ListAsync(
        string prefix,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(prefix);

        await using var db = _factory.Create();

        // CopilotNote: metadata projection only — pulling the BYTEA column here would defeat the
        // whole point of a sweep and load the entire store into memory~ 🧨
        var rows = await db.Blobs
            .Where(b => b.Key.StartsWith(prefix))
            .Select(b => new { b.Key, b.ByteSize, b.UpdatedAt })
            .ToListAsync(token: ct)
            .ConfigureAwait(false);

        foreach (var row in rows)
        {
            yield return new BlobInfo(row.Key, row.ByteSize, row.UpdatedAt);
        }
    }

    /// <inheritdoc/>
    public Task<string> GeneratePresignedUrlAsync(string key, TimeSpan expiry, CancellationToken ct = default)
        => throw new NotSupportedException(
            "Presigned URLs are not supported by the Postgres blob store — there is no URL-addressable " +
            "endpoint in front of a database table. Use the S3 blob store if you need them~ 🔗");
}
