// <copyright file="VariableEntity.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Sqlite.Data.Entities;

using LinqToDB.Mapping;

/// <summary>
/// 💾 Linq2Db entity mapping for the <c>variables</c> table~ ✨💖
/// </summary>
[Table("variables")]
public class VariableEntity
{
    /// <summary>Gets or sets the auto-increment primary key~ 🔢.</summary>
    [PrimaryKey]
    [Identity]
    [Column("id")]
    public long Id { get; set; }

    /// <summary>Gets or sets the <c>VariableScopeKind</c> enum name~ 🎯.</summary>
    [Column("scope_kind")]
    [NotNull]
    public string ScopeKind { get; set; } = string.Empty;

    /// <summary>Gets or sets the scope ID (WorkflowId or ExecutionId as string, null for Global)~ 🔑.</summary>
    [Column("scope_id")]
    [Nullable]
    public string? ScopeId { get; set; }

    /// <summary>Gets or sets the variable name~ 🏷️.</summary>
    [Column("name")]
    [NotNull]
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the JSON-serialized value (NULL means the value is explicitly null, not absent)~ 💡.</summary>
    [Column("value")]
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

    /// <summary>Gets or sets the ISO-8601 creation timestamp~ 📅.</summary>
    [Column("created_at")]
    [NotNull]
    public string CreatedAt { get; set; } = string.Empty;

    /// <summary>Gets or sets the ISO-8601 last-updated timestamp~ 🔄.</summary>
    [Column("updated_at")]
    [NotNull]
    public string UpdatedAt { get; set; } = string.Empty;
}

