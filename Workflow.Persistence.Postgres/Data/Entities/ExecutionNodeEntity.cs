// <copyright file="ExecutionNodeEntity.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Postgres.Data.Entities;

using LinqToDB;
using LinqToDB.Mapping;

/// <summary>
/// 🌸 Linq2Db entity mapping for the <c>execution_nodes</c> table~ ✨💖
/// </summary>
[Table("execution_nodes")]
public class ExecutionNodeEntity
{
    /// <summary>Gets or sets the auto-increment primary key (BIGSERIAL)~ 🔢.</summary>
    [PrimaryKey]
    [Identity]
    [Column("id")]
    public long Id { get; set; }

    /// <summary>Gets or sets the parent execution UUID~ 📊.</summary>
    [Column("execution_id")]
    [NotNull]
    public Guid ExecutionId { get; set; }

    /// <summary>Gets or sets the node identifier within the workflow definition~ 🧩.</summary>
    [Column("node_id")]
    [NotNull]
    public string NodeId { get; set; } = string.Empty;

    /// <summary>Gets or sets the <c>NodeExecutionState</c> enum name~ 🔄.</summary>
    [Column("state")]
    [NotNull]
    public string State { get; set; } = string.Empty;

    /// <summary>Gets or sets the UTC start timestamp~ ⏱️.</summary>
    [Column("started_at")]
    [NotNull]
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>Gets or sets the UTC completion timestamp (nullable)~ ⏹️.</summary>
    [Column("completed_at")]
    [Nullable]
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Gets or sets JSON-serialized node inputs (nullable)~ 📥.</summary>
    [Column("inputs", DataType = DataType.BinaryJson)]
    [Nullable]
    public string? Inputs { get; set; }

    /// <summary>Gets or sets JSON-serialized node outputs (nullable)~ 📤.</summary>
    [Column("outputs", DataType = DataType.BinaryJson)]
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
}



