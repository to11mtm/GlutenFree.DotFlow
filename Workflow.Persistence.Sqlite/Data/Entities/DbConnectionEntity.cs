// <copyright file="DbConnectionEntity.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Sqlite.Data.Entities;

using LinqToDB.Mapping;

/// <summary>
/// 📇 Phase 2.4.a.5 — linq2db entity mapping for the <c>db_connections</c> table
/// (persisted named database connections)~ ✨.
/// </summary>
/// <remarks>
/// CopilotNote: <c>connection_string_encrypted</c> holds the ciphertext produced by the host's
/// <c>IConnectionStringProtector</c> — never a plaintext secret. Mirrors the
/// <c>webhook_registrations</c> entity shape from 2.3.9~ 🔒.
/// </remarks>
[Table("db_connections")]
public sealed class DbConnectionEntity
{
    /// <summary>Gets or sets the unique connection id (case-insensitive PK)~ 🆔.</summary>
    [PrimaryKey]
    [Column("connection_id")]
    public string ConnectionId { get; set; } = string.Empty;

    /// <summary>Gets or sets the provider key ("postgres"/"sqlite")~ 🗂️.</summary>
    [Column("provider_key")]
    [NotNull]
    public string ProviderKey { get; set; } = string.Empty;

    /// <summary>Gets or sets the encrypted connection string (ciphertext, at rest)~ 🔐.</summary>
    [Column("connection_string_encrypted")]
    [NotNull]
    public string ConnectionStringEncrypted { get; set; } = string.Empty;

    /// <summary>Gets or sets the optional friendly display name~ 🏷️.</summary>
    [Column("display_name")]
    [Nullable]
    public string? DisplayName { get; set; }

    /// <summary>Gets or sets 1 when the connection is usable, 0 when disabled~ ✅.</summary>
    [Column("enabled")]
    [NotNull]
    public int Enabled { get; set; } = 1;
}

