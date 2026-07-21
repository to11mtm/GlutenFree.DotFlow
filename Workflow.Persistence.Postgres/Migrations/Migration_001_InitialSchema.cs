// <copyright file="Migration_001_InitialSchema.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Postgres.Migrations;

using FluentMigrator;

/// <summary>
/// 🐘 Creates the initial PostgreSQL schema for the workflow persistence layer~ ✨💖
/// </summary>
/// <remarks>
/// CopilotNote: Uses Postgres-native types throughout — jsonb for JSON, text[] for tags,
/// UUID for PKs, TIMESTAMPTZ for timestamps, BIGSERIAL for auto-increment~ 🗄️
/// </remarks>
[Migration(1)]
public sealed class Migration_001_InitialSchema : Migration
{
    /// <inheritdoc/>
    public override void Up()
    {
        // 📋 workflows table
        Create.Table("workflows")
            .WithColumn("id").AsGuid().PrimaryKey().NotNullable()
            .WithColumn("name").AsString(500).NotNullable()
            .WithColumn("description").AsString(2000).Nullable()
            .WithColumn("definition").AsCustom("jsonb").NotNullable()
            .WithColumn("version").AsString(50).NotNullable()
            .WithColumn("is_active").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("created_at").AsCustom("timestamptz").NotNullable()
            .WithColumn("updated_at").AsCustom("timestamptz").NotNullable()
            .WithColumn("tags").AsCustom("text[]").Nullable()
            .WithColumn("metadata").AsCustom("jsonb").Nullable();

        // 📊 executions table
        Create.Table("executions")
            .WithColumn("id").AsGuid().PrimaryKey().NotNullable()
            .WithColumn("workflow_id").AsGuid().NotNullable()
            .WithColumn("state").AsString(50).NotNullable()
            .WithColumn("started_at").AsCustom("timestamptz").NotNullable()
            .WithColumn("completed_at").AsCustom("timestamptz").Nullable()
            .WithColumn("inputs").AsCustom("jsonb").Nullable()
            .WithColumn("outputs").AsCustom("jsonb").Nullable()
            .WithColumn("error").AsString(4000).Nullable()
            .WithColumn("triggered_by").AsString(500).Nullable();

        // 🌸 execution_nodes table
        Create.Table("execution_nodes")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("execution_id").AsGuid().NotNullable()
            .WithColumn("node_id").AsString(500).NotNullable()
            .WithColumn("state").AsString(50).NotNullable()
            .WithColumn("started_at").AsCustom("timestamptz").NotNullable()
            .WithColumn("completed_at").AsCustom("timestamptz").Nullable()
            .WithColumn("inputs").AsCustom("jsonb").Nullable()
            .WithColumn("outputs").AsCustom("jsonb").Nullable()
            .WithColumn("error").AsString(4000).Nullable()
            .WithColumn("duration_ms").AsInt64().Nullable();

        // 💾 variables table — append-only versioned store
        Create.Table("variables")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("scope_kind").AsString(50).NotNullable()
            .WithColumn("scope_id").AsGuid().Nullable()
            .WithColumn("name").AsString(500).NotNullable()
            .WithColumn("value").AsCustom("jsonb").Nullable()
            .WithColumn("value_type").AsString(500).NotNullable()
            .WithColumn("version").AsInt32().NotNullable()
            .WithColumn("created_at").AsCustom("timestamptz").NotNullable()
            .WithColumn("updated_at").AsCustom("timestamptz").NotNullable();
    }

    /// <inheritdoc/>
    public override void Down()
    {
        Delete.Table("variables");
        Delete.Table("execution_nodes");
        Delete.Table("executions");
        Delete.Table("workflows");
    }
}

