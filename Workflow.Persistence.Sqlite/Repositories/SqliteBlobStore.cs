// <copyright file="SqliteBlobStore.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Sqlite.Repositories;

using System.Security.Cryptography;
using LinqToDB;
using Workflow.Persistence.Abstractions;
using Workflow.Persistence.Sqlite.Data;
using Workflow.Persistence.Sqlite.Data.Entities;

/// <summary>
/// 🗃️ SQLite-backed implementation of <see cref="IBlobStore"/> for dev/test use~ ✨💖
/// </summary>
/// <remarks>
/// CopilotNote: This stores binary data directly in a SQLite BLOB column. Great for dev and tests,
/// but swap to S3/MinIO for production via the composite provider~ 🪣
/// </remarks>
public sealed class SqliteBlobStore : IBlobStore
{
    private readonly WorkflowDataConnectionFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteBlobStore"/> class~ 🔌.
    /// </summary>
    /// <param name="factory">The data connection factory.</param>
    public SqliteBlobStore(WorkflowDataConnectionFactory factory)
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
        using var ms = new MemoryStream();
        await data.CopyToAsync(ms, ct).ConfigureAwait(false);
        var bytes = ms.ToArray();
        var etag = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var now = DateTimeOffset.UtcNow.ToString("O");

        await using var db = _factory.Create();
        var existing = await db.Blobs
            .FirstOrDefaultAsync(b => b.Key == key, token: ct)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            await db.Blobs
                .Where(b => b.Key == key)
                .Set(b => b.Data, bytes)
                .Set(b => b.ContentType, contentType)
                .Set(b => b.ETag, etag)
                .Set(b => b.ByteSize, (long)bytes.Length)
                .Set(b => b.UpdatedAt, now)
                .UpdateAsync(token: ct)
                .ConfigureAwait(false);
        }
        else
        {
            await db.InsertAsync(new BlobEntity
            {
                Key = key,
                Data = bytes,
                ContentType = contentType,
                ETag = etag,
                ByteSize = bytes.Length,
                CreatedAt = now,
                UpdatedAt = now,
            }, token: ct).ConfigureAwait(false);
        }

        return etag;
    }

    /// <inheritdoc/>
    public async Task<Stream?> GetAsync(string key, CancellationToken ct = default)
    {
        await using var db = _factory.Create();
        var entity = await db.Blobs
            .FirstOrDefaultAsync(b => b.Key == key, token: ct)
            .ConfigureAwait(false);

        return entity is null ? null : new MemoryStream(entity.Data);
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync(string key, CancellationToken ct = default)
    {
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
        await using var db = _factory.Create();
        return await db.Blobs
            .AnyAsync(b => b.Key == key, token: ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task<string> GeneratePresignedUrlAsync(string key, TimeSpan expiry, CancellationToken ct = default)
    {
        // SQLite doesn't have URL-based access; return a placeholder~ 🔗
        throw new NotSupportedException(
            "Presigned URLs are not supported by the SQLite blob store. " +
            "Use S3/MinIO in production~ 🪣");
    }
}

