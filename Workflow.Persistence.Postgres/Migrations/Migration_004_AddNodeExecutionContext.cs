// <copyright file="Migration_004_AddNodeExecutionContext.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Postgres.Migrations;

using FluentMigrator;

/// <summary>
/// Adds the execution-context columns SQLite gained in its migration 4 — <c>loop_id</c>,
/// <c>loop_iteration</c>, <c>sub_graph_id</c> and a <c>metadata</c> JSONB bag (scope path, parallel
/// branch, skip reason…). Until now Postgres dropped them, so loop and body history lost which
/// iteration or branch a row belonged to. Nullable: existing rows keep NULL.
/// </summary>
[Migration(4)]
public sealed class Migration_004_AddNodeExecutionContext : Migration
{
    /// <inheritdoc/>
    public override void Up()
    {
        Alter.Table("execution_nodes")
            .AddColumn("loop_id").AsString().Nullable()
            .AddColumn("loop_iteration").AsInt32().Nullable()
            .AddColumn("sub_graph_id").AsString().Nullable()
            .AddColumn("metadata").AsCustom("JSONB").Nullable();
    }

    /// <inheritdoc/>
    public override void Down()
    {
        Delete.Column("loop_id").FromTable("execution_nodes");
        Delete.Column("loop_iteration").FromTable("execution_nodes");
        Delete.Column("sub_graph_id").FromTable("execution_nodes");
        Delete.Column("metadata").FromTable("execution_nodes");
    }
}
