// <copyright file="BlobEntity.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Postgres.Data.Entities;

using LinqToDB;
using LinqToDB.Mapping;

/// <summary>
/// 🗃️ Linq2Db entity mapping for the <c>blobs</c> table~ ✨💖
/// </summary>
/// <remarks>
/// CopilotNote: Data lives in a <c>BYTEA</c> column, not a Large Object. Blobs here are compiled
/// assemblies and streaming spill segments — megabytes, not gigabytes — so BYTEA's simple
/// transaction-free lifetime beats the LO API's orphan-OID bookkeeping~ 🐘
/// </remarks>
[Table("blobs")]
public class BlobEntity
{
    /// <summary>Gets or sets the object key (primary key)~ 🔑.</summary>
    [PrimaryKey]
    [Column("key")]
    [NotNull]
    public string Key { get; set; } = string.Empty;

    /// <summary>Gets or sets the raw stored bytes~ 💾.</summary>
    [Column("data", DataType = DataType.Binary)]
    [NotNull]
    public byte[] Data { get; set; } = [];

    /// <summary>Gets or sets the MIME content type, if known~ 🏷️.</summary>
    [Column("content_type")]
    [Nullable]
    public string? ContentType { get; set; }

    /// <summary>Gets or sets the content hash used as an ETag~ 🔖.</summary>
    [Column("etag")]
    [NotNull]
    public string ETag { get; set; } = string.Empty;

    /// <summary>Gets or sets the stored size in bytes~ 📏.</summary>
    [Column("byte_size")]
    [NotNull]
    public long ByteSize { get; set; }

    /// <summary>Gets or sets the UTC creation timestamp~ 📅.</summary>
    [Column("created_at")]
    [NotNull]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets the UTC last-updated timestamp~ 🔄.</summary>
    [Column("updated_at")]
    [NotNull]
    public DateTimeOffset UpdatedAt { get; set; }
}
