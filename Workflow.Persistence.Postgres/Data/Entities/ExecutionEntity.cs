// <copyright file="ExecutionEntity.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Postgres.Data.Entities;

using LinqToDB;
using LinqToDB.Mapping;

/// <summary>
/// 📊 Linq2Db entity mapping for the <c>executions</c> table~ ✨💖
/// </summary>
[Table("executions")]
public class ExecutionEntity
{
    /// <summary>Gets or sets the execution UUID~ 🆔.</summary>
    [PrimaryKey]
    [Column("id")]
    public Guid Id { get; set; }

    /// <summary>Gets or sets the parent workflow UUID~ 📋.</summary>
    [Column("workflow_id")]
    [NotNull]
    public Guid WorkflowId { get; set; }

    /// <summary>Gets or sets the <c>ExecutionState</c> enum name~ 🔄.</summary>
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

    /// <summary>Gets or sets the JSON-serialized input dictionary (nullable)~ 📥.</summary>
    [Column("inputs", DataType = DataType.BinaryJson)]
    [Nullable]
    public string? Inputs { get; set; }

    /// <summary>Gets or sets the JSON-serialized output dictionary (nullable)~ 📤.</summary>
    [Column("outputs", DataType = DataType.BinaryJson)]
    [Nullable]
    public string? Outputs { get; set; }

    /// <summary>Gets or sets the error message (nullable)~ ❌.</summary>
    [Column("error")]
    [Nullable]
    public string? Error { get; set; }

    /// <summary>Gets or sets who or what triggered this execution (nullable)~ 🚀.</summary>
    [Column("triggered_by")]
    [Nullable]
    public string? TriggeredBy { get; set; }
}



