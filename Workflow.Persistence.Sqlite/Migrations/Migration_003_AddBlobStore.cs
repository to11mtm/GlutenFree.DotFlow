// <copyright file="Migration_003_AddBlobStore.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Sqlite.Migrations;

using FluentMigrator;

/// <summary>
/// 🗃️ Adds the blob_store table for SQLite-backed binary large object storage~ ✨💖
/// </summary>
/// <remarks>
/// CopilotNote: SQLite stores BLOBs as BLOB columns. This is not ideal for large files
/// (prefer S3 or MinIO in production) but makes local dev and trying the app zero-config!
/// Max recommended single blob size: ~50 MB (SQLite page cache). Files are stored inline.
/// The <c>etag</c> column is a SHA-256 hex digest of the raw bytes~ 🔐
/// </remarks>
[Migration(3)]
public sealed class Migration_003_AddBlobStore : Migration
{
    /// <inheritdoc/>
    public override void Up()
    {
        // 🗃️ blob_store table — inline binary storage for local dev / zero-config mode
        Create.Table("blob_store")
            .WithColumn("key").AsString(1024).PrimaryKey()
            .WithColumn("data").AsBinary(int.MaxValue).NotNullable()    // BLOB column
            .WithColumn("content_type").AsString(256).Nullable()
            .WithColumn("etag").AsString(64).NotNullable()              // SHA-256 hex
            .WithColumn("byte_size").AsInt64().NotNullable()
            .WithColumn("created_at").AsString().NotNullable()
            .WithColumn("updated_at").AsString().NotNullable();
    }

    /// <inheritdoc/>
    public override void Down()
    {
        Delete.Table("blob_store");
    }
}

