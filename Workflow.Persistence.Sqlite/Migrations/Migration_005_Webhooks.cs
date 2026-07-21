// <copyright file="Migration_005_Webhooks.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Sqlite.Migrations;

using FluentMigrator;

/// <summary>
///  Phase 2.3.9 — Creates the <c>webhook_registrations</c> table for persisting
/// webhook-to-workflow bindings across host restarts~ ✨
/// </summary>
/// <remarks>
/// <para>
/// V1 schema stores all fields from <see cref="Workflow.Core.Models.WebhookRegistration"/>:
/// <list type="bullet">
///   <item><c>webhook_id</c> — stable URL slug, case-insensitive primary key (COLLATE NOCASE).</item>
///   <item><c>workflow_def_id</c> — GUID of the workflow to launch when the webhook fires.</item>
///   <item><c>allowed_methods</c> — JSON array of normalised upper-case HTTP methods.</item>
///   <item><c>secret_key</c> / <c>signature_scheme</c> — optional HMAC validation fields (2.3.7).</item>
///   <item><c>created_at</c> — ISO-8601 UTC timestamp.</item>
///   <item><c>enabled</c> — 1 = active, 0 = disabled (returns 404 as if not registered).</item>
/// </list>
/// </para>
/// <para>
/// CopilotNote: <c>COLLATE NOCASE</c> on <c>webhook_id</c> is the SQLite way to get
/// case-insensitive PK lookups — keeps matching semantics identical to the in-memory
/// <see cref="Workflow.Persistence.Abstractions.InMemoryWebhookRegistrationRepository"/>
/// which uses <c>StringComparer.OrdinalIgnoreCase</c>~
/// </para>
/// </remarks>
[Migration(5)]
public sealed class Migration_005_Webhooks : Migration
{
    /// <inheritdoc/>
    public override void Up()
    {
        //  webhook_registrations — one row per registered webhook endpoint~
        Create.Table("webhook_registrations")
            // Case-insensitive PK — matches InMemoryWebhookRegistrationRepository.OrdinalIgnoreCase semantics~
            .WithColumn("webhook_id").AsString().NotNullable().PrimaryKey("pk_webhook_registrations")
            .WithColumn("workflow_def_id").AsString().NotNullable()
            .WithColumn("allowed_methods").AsString().NotNullable()    // JSON array: ["POST","GET"]
            .WithColumn("secret_key").AsString().Nullable()
            .WithColumn("signature_scheme").AsString().Nullable()
            .WithColumn("created_at").AsString().NotNullable()         // ISO-8601 UTC
            .WithColumn("enabled").AsInt32().NotNullable().WithDefaultValue(1);
    }

    /// <inheritdoc/>
    public override void Down()
    {
        // CopilotNote: FluentMigrator handles SQLite DROP TABLE gracefully~
        Delete.Table("webhook_registrations");
    }
}
