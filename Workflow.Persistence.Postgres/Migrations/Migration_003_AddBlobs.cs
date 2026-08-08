// <copyright file="Migration_003_AddBlobs.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Postgres.Migrations;

using FluentMigrator;

/// <summary>
/// 🗃️ Adds the <c>blobs</c> table backing <see cref="Workflow.Persistence.Abstractions.IBlobStore"/>~ ✨💖
/// </summary>
/// <remarks>
/// CopilotNote: <c>BYTEA</c> rather than a Large Object — see <c>BlobEntity</c> for why. The
/// <c>key</c> column is the primary key, which also gives us the prefix-scan index that
/// <c>ListAsync</c> needs for the streaming spill sweep~ 🧹
/// </remarks>
[Migration(3)]
public sealed class Migration_003_AddBlobs : Migration
{
    /// <inheritdoc/>
    public override void Up()
    {
        Create.Table("blobs")
            .WithColumn("key").AsString().NotNullable().PrimaryKey()
            .WithColumn("data").AsCustom("BYTEA").NotNullable()
            .WithColumn("content_type").AsString().Nullable()
            .WithColumn("etag").AsString().NotNullable()
            .WithColumn("byte_size").AsInt64().NotNullable()
            .WithColumn("created_at").AsCustom("TIMESTAMPTZ").NotNullable()
            .WithColumn("updated_at").AsCustom("TIMESTAMPTZ").NotNullable();

        // 🧹 supports prefix sweeps (LIKE 'prefix%') and age-ordered reclaim~
        Create.Index("idx_blobs_updated_at").OnTable("blobs").OnColumn("updated_at");
    }

    /// <inheritdoc/>
    public override void Down()
    {
        Delete.Index("idx_blobs_updated_at").OnTable("blobs");
        Delete.Table("blobs");
    }
}
