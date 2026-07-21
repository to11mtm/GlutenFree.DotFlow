// <copyright file="Migration_006_DbConnections.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Sqlite.Migrations;

using FluentMigrator;

/// <summary>
/// 📇 Phase 2.4.a.5 — Creates the <c>db_connections</c> table for persisting named database
/// connections (with encrypted-at-rest connection strings) across host restarts~ ✨.
/// </summary>
/// <remarks>
/// CopilotNote: Mirrors Migration_005 (webhooks). <c>connection_string_encrypted</c> stores the
/// ciphertext from the host's <c>IConnectionStringProtector</c>; the registry never persists
/// plaintext secrets~ 🔒.
/// </remarks>
[Migration(6)]
public sealed class Migration_006_DbConnections : Migration
{
    /// <inheritdoc/>
    public override void Up()
    {
        Create.Table("db_connections")
            .WithColumn("connection_id").AsString().NotNullable().PrimaryKey("pk_db_connections")
            .WithColumn("provider_key").AsString().NotNullable()
            .WithColumn("connection_string_encrypted").AsString().NotNullable()
            .WithColumn("display_name").AsString().Nullable()
            .WithColumn("enabled").AsInt32().NotNullable().WithDefaultValue(1);
    }

    /// <inheritdoc/>
    public override void Down()
    {
        Delete.Table("db_connections");
    }
}

