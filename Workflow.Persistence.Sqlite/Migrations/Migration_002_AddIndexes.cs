// <copyright file="Migration_002_AddIndexes.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Sqlite.Migrations;

using FluentMigrator;

/// <summary>
/// 🚀 Adds performance indexes to the core tables~ ✨💖
/// </summary>
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

        // 🌸 execution_nodes indexes
        Create.Index("idx_execution_nodes_execution_id").OnTable("execution_nodes").OnColumn("execution_id");

        // 💾 variables unique index — ensures one row per scope+name+version
        Create.UniqueConstraint("uix_variables_scope_name_version")
            .OnTable("variables")
            .Columns("scope_kind", "scope_id", "name", "version");

        // 📋 workflows index on name for search
        Create.Index("idx_workflows_name").OnTable("workflows").OnColumn("name");
    }

    /// <inheritdoc/>
    public override void Down()
    {
        Delete.Index("idx_workflows_name").OnTable("workflows");
        Delete.UniqueConstraint("uix_variables_scope_name_version").FromTable("variables");
        Delete.Index("idx_execution_nodes_execution_id").OnTable("execution_nodes");
        Delete.Index("idx_executions_started_at").OnTable("executions");
        Delete.Index("idx_executions_state").OnTable("executions");
        Delete.Index("idx_executions_workflow_id").OnTable("executions");
    }
}



