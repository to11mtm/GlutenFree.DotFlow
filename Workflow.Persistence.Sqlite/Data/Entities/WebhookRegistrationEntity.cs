// <copyright file="WebhookRegistrationEntity.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Sqlite.Data.Entities;

using LinqToDB.Mapping;

/// <summary>
///  Phase 2.3.9 — Linq2Db entity mapping for the <c>webhook_registrations</c> table~ ✨
/// </summary>
/// <remarks>
/// CopilotNote: <c>webhook_id</c> uses COLLATE NOCASE at the SQLite DDL level (via Migration_005)
/// so case-insensitive lookups require no extra application logic on individual queries~
/// </remarks>
[Table("webhook_registrations")]
public sealed class WebhookRegistrationEntity
{
    /// <summary>Gets or sets the stable URL slug (e.g. <c>"order-placed"</c>)~ .</summary>
    [PrimaryKey]
    [Column("webhook_id")]
    public string WebhookId { get; set; } = string.Empty;

    /// <summary>Gets or sets the workflow definition GUID string~ .</summary>
    [Column("workflow_def_id")]
    [NotNull]
    public string WorkflowDefId { get; set; } = string.Empty;

    /// <summary>Gets or sets the JSON-encoded array of allowed HTTP methods (e.g. <c>["POST"]</c>)~ .</summary>
    [Column("allowed_methods")]
    [NotNull]
    public string AllowedMethods { get; set; } = "[]";

    /// <summary>Gets or sets the optional HMAC secret key~ .</summary>
    [Column("secret_key")]
    [Nullable]
    public string? SecretKey { get; set; }

    /// <summary>Gets or sets the optional signature scheme name (e.g. <c>"github"</c>, <c>"stripe"</c>)~ ️.</summary>
    [Column("signature_scheme")]
    [Nullable]
    public string? SignatureScheme { get; set; }

    /// <summary>Gets or sets the ISO-8601 UTC creation timestamp~ .</summary>
    [Column("created_at")]
    [NotNull]
    public string CreatedAt { get; set; } = string.Empty;

    /// <summary>Gets or sets 1 if the webhook is active, 0 if disabled~ .</summary>
    [Column("enabled")]
    [NotNull]
    public int Enabled { get; set; } = 1;
}
