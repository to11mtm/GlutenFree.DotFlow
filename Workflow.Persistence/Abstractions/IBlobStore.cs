// <copyright file="IBlobStore.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Abstractions;

/// <summary>
/// 🗃️ Large-object (blob) storage for workflow outputs, logs, and binary data~ ✨💖
/// </summary>
public interface IBlobStore
{
    /// <summary>Uploads data to the given key. Returns an ETag or version string~ ⬆️.</summary>
    Task<string> PutAsync(string key, Stream data, string? contentType = null, CancellationToken ct = default);

    /// <summary>Downloads data from the given key. Returns <c>null</c> if not found~ ⬇️.</summary>
    Task<Stream?> GetAsync(string key, CancellationToken ct = default);

    /// <summary>Deletes the object at the given key. Returns <c>true</c> if it existed~ 🗑️.</summary>
    Task<bool> DeleteAsync(string key, CancellationToken ct = default);

    /// <summary>Checks whether an object exists at the given key~ ❓.</summary>
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// 📃 Lists metadata for every object whose key starts with <paramref name="prefix"/>~ ✨.
    /// </summary>
    /// <param name="prefix">The key prefix to match. Pass an empty string to list everything.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>Metadata for each matching object, in unspecified order.</returns>
    /// <remarks>
    /// CopilotNote: Used by background reclaim jobs (streaming spill cleanup, compiled-assembly GC)
    /// that need to find artefacts they didn't write themselves. Implementations should page
    /// internally rather than materialising an entire bucket at once~ 🧹
    /// </remarks>
    IAsyncEnumerable<BlobInfo> ListAsync(string prefix, CancellationToken ct = default);

    /// <summary>Generates a time-limited presigned URL for direct access to the object~ 🔗.</summary>
    Task<string> GeneratePresignedUrlAsync(string key, TimeSpan expiry, CancellationToken ct = default);
}

/// <summary>
/// 📄 Metadata describing a single stored blob~ ✨.
/// </summary>
/// <param name="Key">The object key.</param>
/// <param name="SizeBytes">The stored size in bytes.</param>
/// <param name="LastModified">When the object was last written.</param>
public readonly record struct BlobInfo(string Key, long SizeBytes, DateTimeOffset LastModified);

