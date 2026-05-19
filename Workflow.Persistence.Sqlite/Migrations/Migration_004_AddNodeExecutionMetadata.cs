// <copyright file="Migration_004_AddNodeExecutionMetadata.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Sqlite.Migrations;

using FluentMigrator;

/// <summary>
/// 🗂️ Adds execution-context metadata columns to <c>execution_nodes</c>~ ✨💖
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: Phase 2.2.3-followup — three new columns land here:
/// <list type="bullet">
///   <item><c>loop_id</c> / <c>loop_iteration</c> — loop scope correlation (values were already
///         present on <c>NodeExecutionRecord</c> from Phase 2.2.0b but were silently dropped
///         at the persistence boundary; now properly round-tripped).</item>
///   <item><c>sub_graph_id</c> — identifies which <c>SubGraphExecutor</c> instance spawned
///         a node, enabling the Phase 2.2.6 history query "show me all nodes that ran inside
///         sub-graph X".</item>
///   <item><c>metadata</c> — JSON bag for future keys (e.g. <c>parallelId</c>, <c>branchIndex</c>
///         from Phase 2.2.3b parallel-branch metadata) without requiring additional migrations.</item>
/// </list>
/// </para>
/// <para>
/// SQLite <c>ALTER TABLE … ADD COLUMN</c> always adds nullable columns — existing rows get NULL.
/// This is safe; all four columns are optional on <c>NodeExecutionRecord</c>~ 🛡️.
/// </para>
/// </remarks>
[Migration(4)]
public sealed class Migration_004_AddNodeExecutionMetadata : Migration
{
    /// <inheritdoc/>
    public override void Up()
    {
        // Loop scope correlation — stamped by WorkflowExecutor when a LoopContext is active~ 🔁
        Alter.Table("execution_nodes")
            .AddColumn("loop_id").AsString().Nullable();

        Alter.Table("execution_nodes")
            .AddColumn("loop_iteration").AsInt32().Nullable();

        // Sub-graph ID — stamped by SubGraphExecutor; queryable for history correlation~ 🌿
        Alter.Table("execution_nodes")
            .AddColumn("sub_graph_id").AsString().Nullable();

        // Generic metadata bag — JSON blob for future extensibility (parallelId, etc.)~ 🗂️
        Alter.Table("execution_nodes")
            .AddColumn("metadata").AsString().Nullable();
    }

    /// <inheritdoc/>
    public override void Down()
    {
        // CopilotNote: SQLite doesn't support DROP COLUMN before v3.35 — FluentMigrator
        // works around this by recreating the table. Using Delete.Column to document intent;
        // FluentMigrator handles the SQLite-specific DDL~ 🔄.
        Delete.Column("loop_id").FromTable("execution_nodes");
        Delete.Column("loop_iteration").FromTable("execution_nodes");
        Delete.Column("sub_graph_id").FromTable("execution_nodes");
        Delete.Column("metadata").FromTable("execution_nodes");
    }
}

