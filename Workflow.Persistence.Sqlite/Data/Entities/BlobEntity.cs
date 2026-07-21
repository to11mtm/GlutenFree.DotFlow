// <copyright file="BlobEntity.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Sqlite.Data.Entities;

using LinqToDB.Mapping;

/// <summary>
/// 🗃️ Linq2Db entity mapping for the <c>blob_store</c> table~ ✨💖
/// </summary>
/// <remarks>
/// CopilotNote: This is intentionally a development/zero-config blob store. Data is stored as raw
/// bytes in a BLOB column. For production use, swap to S3 / MinIO via the composite provider~ 🪣
/// </remarks>
[Table("blob_store")]
public class BlobEntity
{
    /// <summary>Gets or sets the storage key (path-like string)~ 🔑.</summary>
    [PrimaryKey]
    [Column("key")]
    public string Key { get; set; } = string.Empty;

    /// <summary>Gets or sets the raw binary data~ 💾.</summary>
    [Column("data")]
    [NotNull]
    public byte[] Data { get; set; } = Array.Empty<byte>();

    /// <summary>Gets or sets the MIME content type (nullable)~ 📄.</summary>
    [Column("content_type")]
    [Nullable]
    public string? ContentType { get; set; }

    /// <summary>Gets or sets the SHA-256 hex digest used as an ETag~ 🔐.</summary>
    [Column("etag")]
    [NotNull]
    public string ETag { get; set; } = string.Empty;

    /// <summary>Gets or sets the data size in bytes~ 📏.</summary>
    [Column("byte_size")]
    [NotNull]
    public long ByteSize { get; set; }

    /// <summary>Gets or sets the ISO-8601 creation timestamp~ 📅.</summary>
    [Column("created_at")]
    [NotNull]
    public string CreatedAt { get; set; } = string.Empty;

    /// <summary>Gets or sets the ISO-8601 last-updated timestamp~ 🔄.</summary>
    [Column("updated_at")]
    [NotNull]
    public string UpdatedAt { get; set; } = string.Empty;
}

