// <copyright file="Migration_002_AddIndexes.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Postgres.Migrations;

using FluentMigrator;

/// <summary>
/// 🚀 Adds performance indexes to the core Postgres tables~ ✨💖
/// </summary>
/// <remarks>
/// CopilotNote: Uses Postgres-specific index types — GIN for jsonb/array columns,
/// BTREE (default) for regular columns. The tag GIN index enables fast @> queries~ 🐘
/// </remarks>
[Migration(2)]
public sealed class Migration_002_AddIndexes : Migration
{
    /// <inheritdoc/>
    public override void Up()
    {
        // 📊 executions indexes
        Create.Index("idx_executions_workflow_id").OnTable("executions").OnColumn("workflow_id");
        Create.Index("idx_executions_state").OnTable("executions").OnColumn("state");
        Create.Index("idx_executions_started_at").OnTable("executions").OnColumn("started_at");

        // 🌸 execution_nodes index
        Create.Index("idx_execution_nodes_execution_id").OnTable("execution_nodes").OnColumn("execution_id");

        // 💾 variables unique constraint — one row per scope+name+version
        Create.UniqueConstraint("uix_variables_scope_name_version")
            .OnTable("variables")
            .Columns("scope_kind", "scope_id", "name", "version");

        // 📋 workflows indexes
        Create.Index("idx_workflows_name").OnTable("workflows").OnColumn("name");
        Create.Index("idx_workflows_is_active").OnTable("workflows").OnColumn("is_active");

        // 🏷️ GIN index on tags array for fast @> containment queries~ 🐘
        Execute.Sql("CREATE INDEX idx_workflows_tags ON workflows USING GIN (tags);");

        // 🔍 GIN index on definition jsonb for potential JSON path queries~ 💡
        Execute.Sql("CREATE INDEX idx_workflows_definition ON workflows USING GIN (definition);");
    }

    /// <inheritdoc/>
    public override void Down()
    {
        Execute.Sql("DROP INDEX IF EXISTS idx_workflows_definition;");
        Execute.Sql("DROP INDEX IF EXISTS idx_workflows_tags;");
        Delete.Index("idx_workflows_is_active").OnTable("workflows");
        Delete.Index("idx_workflows_name").OnTable("workflows");
        Delete.UniqueConstraint("uix_variables_scope_name_version").FromTable("variables");
        Delete.Index("idx_execution_nodes_execution_id").OnTable("execution_nodes");
        Delete.Index("idx_executions_started_at").OnTable("executions");
        Delete.Index("idx_executions_state").OnTable("executions");
        Delete.Index("idx_executions_workflow_id").OnTable("executions");
    }
}

