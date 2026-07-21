// <copyright file="VariableEntity.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Postgres.Data.Entities;

using LinqToDB;
using LinqToDB.Mapping;

/// <summary>
/// 💾 Linq2Db entity mapping for the <c>variables</c> table~ ✨💖
/// </summary>
[Table("variables")]
public class VariableEntity
{
    /// <summary>Gets or sets the auto-increment primary key (BIGSERIAL)~ 🔢.</summary>
    [PrimaryKey]
    [Identity]
    [Column("id")]
    public long Id { get; set; }

    /// <summary>Gets or sets the <c>VariableScopeKind</c> enum name~ 🎯.</summary>
    [Column("scope_kind")]
    [NotNull]
    public string ScopeKind { get; set; } = string.Empty;

    /// <summary>Gets or sets the scope UUID (WorkflowId or ExecutionId, null for Global)~ 🔑.</summary>
    [Column("scope_id")]
    [Nullable]
    public Guid? ScopeId { get; set; }

    /// <summary>Gets or sets the variable name~ 🏷️.</summary>
    [Column("name")]
    [NotNull]
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the JSON value (NULL = explicit null value, not absent)~ 💡.</summary>
    [Column("value", DataType = DataType.BinaryJson)]
    [Nullable]
    public string? Value { get; set; }

    /// <summary>Gets or sets the CLR type name of the value~ 📝.</summary>
    [Column("value_type")]
    [NotNull]
    public string ValueType { get; set; } = string.Empty;

    /// <summary>Gets or sets the monotonically increasing version number (1-based)~ 🔢.</summary>
    [Column("version")]
    [NotNull]
    public int Version { get; set; }

    /// <summary>Gets or sets the UTC creation timestamp~ 📅.</summary>
    [Column("created_at")]
    [NotNull]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets the UTC last-updated timestamp~ 🔄.</summary>
    [Column("updated_at")]
    [NotNull]
    public DateTimeOffset UpdatedAt { get; set; }
}



