// <copyright file="Migration_001_InitialSchema.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Sqlite.Migrations;

using FluentMigrator;

/// <summary>
/// 🌸 Initial database schema migration — creates all core tables~ ✨💖
/// </summary>
/// <remarks>
/// CopilotNote: SQLite uses TEXT for UUIDs, TEXT for JSON columns, INTEGER for booleans (0/1),
/// and TEXT for DateTimeOffset (ISO-8601). This mirrors the Postgres schema for easy upgrade~ 🐘
/// </remarks>
[Migration(1)]
public sealed class Migration_001_InitialSchema : Migration
{
    /// <inheritdoc/>
    public override void Up()
    {
        // 📋 workflows table — stores workflow definitions
        Create.Table("workflows")
            .WithColumn("id").AsString().PrimaryKey()
            .WithColumn("name").AsString().NotNullable()
            .WithColumn("description").AsString().Nullable()
            .WithColumn("definition").AsString().NotNullable()          // JSON blob
            .WithColumn("version").AsString().NotNullable()
            .WithColumn("is_active").AsInt32().NotNullable().WithDefaultValue(1)
            .WithColumn("created_at").AsString().NotNullable()          // ISO-8601
            .WithColumn("updated_at").AsString().NotNullable()
            .WithColumn("tags").AsString().Nullable()                   // comma-joined
            .WithColumn("metadata").AsString().Nullable();              // JSON blob

        // 📊 executions table — stores workflow execution records
        Create.Table("executions")
            .WithColumn("id").AsString().PrimaryKey()
            .WithColumn("workflow_id").AsString().NotNullable()
            .WithColumn("state").AsString().NotNullable()               // ExecutionState enum name
            .WithColumn("started_at").AsString().NotNullable()
            .WithColumn("completed_at").AsString().Nullable()
            .WithColumn("inputs").AsString().Nullable()                 // JSON
            .WithColumn("outputs").AsString().Nullable()                // JSON
            .WithColumn("error").AsString().Nullable()
            .WithColumn("triggered_by").AsString().Nullable();

        // 🌸 execution_nodes table — per-node execution events
        Create.Table("execution_nodes")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("execution_id").AsString().NotNullable()
            .WithColumn("node_id").AsString().NotNullable()
            .WithColumn("state").AsString().NotNullable()               // NodeExecutionState enum name
            .WithColumn("started_at").AsString().NotNullable()
            .WithColumn("completed_at").AsString().Nullable()
            .WithColumn("inputs").AsString().Nullable()                 // JSON
            .WithColumn("outputs").AsString().Nullable()                // JSON
            .WithColumn("error").AsString().Nullable()
            .WithColumn("duration_ms").AsInt64().Nullable();

        // 💾 variables table — versioned key-value store
        Create.Table("variables")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("scope_kind").AsString().NotNullable()          // VariableScopeKind enum name
            .WithColumn("scope_id").AsString().Nullable()               // WorkflowId or ExecutionId
            .WithColumn("name").AsString().NotNullable()
            .WithColumn("value").AsString().Nullable()                  // JSON; NULL = explicit null
            .WithColumn("value_type").AsString().NotNullable()
            .WithColumn("version").AsInt32().NotNullable()
            .WithColumn("created_at").AsString().NotNullable()
            .WithColumn("updated_at").AsString().NotNullable();
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

