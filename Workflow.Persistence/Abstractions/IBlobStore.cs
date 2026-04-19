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

    /// <summary>Generates a time-limited presigned URL for direct access to the object~ 🔗.</summary>
    Task<string> GeneratePresignedUrlAsync(string key, TimeSpan expiry, CancellationToken ct = default);
}

