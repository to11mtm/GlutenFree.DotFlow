// <copyright file="WorkflowEntity.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Postgres.Data.Entities;

using LinqToDB;
using LinqToDB.Mapping;

/// <summary>
/// 📋 Linq2Db entity mapping for the <c>workflows</c> table~ ✨💖
/// </summary>
[Table("workflows")]
public class WorkflowEntity
{
    /// <summary>Gets or sets the workflow UUID~ 🆔.</summary>
    [PrimaryKey]
    [Column("id")]
    public Guid Id { get; set; }

    /// <summary>Gets or sets the workflow name~ 🏷️.</summary>
    [Column("name")]
    [NotNull]
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the optional description~ 📝.</summary>
    [Column("description")]
    [Nullable]
    public string? Description { get; set; }

    /// <summary>Gets or sets the serialized <c>WorkflowDefinition</c> as jsonb~ 📦.</summary>
    [Column("definition", DataType = DataType.BinaryJson)]
    [NotNull]
    public string Definition { get; set; } = string.Empty;

    /// <summary>Gets or sets the semantic version string~ 📊.</summary>
    [Column("version")]
    [NotNull]
    public string Version { get; set; } = string.Empty;

    /// <summary>Gets or sets whether the workflow is active (not soft-deleted)~ 🔘.</summary>
    [Column("is_active")]
    [NotNull]
    public bool IsActive { get; set; } = true;

    /// <summary>Gets or sets the UTC creation timestamp~ 📅.</summary>
    [Column("created_at")]
    [NotNull]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets the UTC last-updated timestamp~ 🔄.</summary>
    [Column("updated_at")]
    [NotNull]
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Gets or sets the tag array (nullable)~ 🏷️.</summary>
    [Column("tags")]
    [Nullable]
    public string[]? Tags { get; set; }

    /// <summary>Gets or sets the metadata jsonb blob (nullable)~ 🗂️.</summary>
    [Column("metadata", DataType = DataType.BinaryJson)]
    [Nullable]
    public string? Metadata { get; set; }
}




