// <copyright file="ExecutionNodeEntity.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Sqlite.Data.Entities;

using LinqToDB.Mapping;

/// <summary>
/// 🌸 Linq2Db entity mapping for the <c>execution_nodes</c> table~ ✨💖
/// </summary>
[Table("execution_nodes")]
public class ExecutionNodeEntity
{
    /// <summary>Gets or sets the auto-increment primary key~ 🔢.</summary>
    [PrimaryKey]
    [Identity]
    [Column("id")]
    public long Id { get; set; }

    /// <summary>Gets or sets the parent execution UUID as string~ 📊.</summary>
    [Column("execution_id")]
    [NotNull]
    public string ExecutionId { get; set; } = string.Empty;

    /// <summary>Gets or sets the node identifier within the workflow definition~ 🧩.</summary>
    [Column("node_id")]
    [NotNull]
    public string NodeId { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>NodeExecutionState</c> enum name~ 🔄.</summary>
    [Column("state")]
    [NotNull]
    public string State { get; set; } = string.Empty;

    /// <summary>Gets or sets the ISO-8601 start timestamp~ ⏱️.</summary>
    [Column("started_at")]
    [NotNull]
    public string StartedAt { get; set; } = string.Empty;

    /// <summary>Gets or sets the ISO-8601 completion timestamp (nullable)~ ⏹️.</summary>
    [Column("completed_at")]
    [Nullable]
    public string? CompletedAt { get; set; }

    /// <summary>Gets or sets JSON-serialized node inputs (nullable)~ 📥.</summary>
    [Column("inputs")]
    [Nullable]
    public string? Inputs { get; set; }

    /// <summary>Gets or sets JSON-serialized node outputs (nullable)~ 📤.</summary>
    [Column("outputs")]
    [Nullable]
    public string? Outputs { get; set; }

    /// <summary>Gets or sets error message (nullable)~ ❌.</summary>
    [Column("error")]
    [Nullable]
    public string? Error { get; set; }

    /// <summary>Gets or sets the node duration in milliseconds (nullable)~ ⏲️.</summary>
    [Column("duration_ms")]
    [Nullable]
    public long? DurationMs { get; set; }

    /// <summary>
    /// Gets or sets the loop scope ID this node ran inside, if any~ 🔁.
    /// CopilotNote: Phase 2.2.3-followup — persisted alongside <see cref="LoopIteration"/>
    /// for per-iteration history correlation. Stamped by WorkflowExecutor via LoopContext~ 💖.
    /// </summary>
    [Column("loop_id")]
    [Nullable]
    public string? LoopId { get; set; }

    /// <summary>Gets or sets the 1-based loop iteration number, if any~ 🔢.</summary>
    [Column("loop_iteration")]
    [Nullable]
    public int? LoopIteration { get; set; }

    /// <summary>
    /// Gets or sets the sub-graph instance ID for nodes that executed inside a
    /// <c>SubGraphExecutor</c>~ 🌿.
    /// CopilotNote: Phase 2.2.3-followup — subGraphId tagging. Stored as a dedicated queryable
    /// column rather than inside the JSON metadata blob so history queries can filter efficiently.
    /// </summary>
    [Column("sub_graph_id")]
    [Nullable]
    public string? SubGraphId { get; set; }

    /// <summary>
    /// Gets or sets arbitrary additional metadata as a JSON dictionary (nullable)~ 🗂️.
    /// CopilotNote: Extensibility hook — future keys like <c>parallelId</c>, <c>branchIndex</c>
    /// from Phase 2.2.3b parallel branch metadata can land here without a schema migration.
    /// </summary>
    [Column("metadata")]
    [Nullable]
    public string? MetadataJson { get; set; }
}

