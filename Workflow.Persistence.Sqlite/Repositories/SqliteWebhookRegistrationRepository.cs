// <copyright file="SqliteWebhookRegistrationRepository.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

using LinqToDB.Async;

namespace Workflow.Persistence.Sqlite.Repositories;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LinqToDB;
using Workflow.Core.Models;
using Workflow.Persistence.Abstractions;
using Workflow.Persistence.Sqlite.Data;
using Workflow.Persistence.Sqlite.Data.Entities;
using LanguageExt;

/// <summary>
///  Phase 2.3.9 — SQLite-backed implementation of <see cref="IWebhookRegistrationRepository"/>~ ✨.
/// </summary>
/// <remarks>
/// <para>
/// Webhook registrations survive host restarts when SQLite is configured with a file-based
/// connection string. In-memory databases (<c>Cache=Shared;Mode=Memory</c>) are supported for testing~
/// </para>
/// <para>
/// CopilotNote: The <c>webhook_id</c> column is COLLATE NOCASE at the DDL level (Migration_005)
/// so all lookups are automatically case-insensitive — no extra <c>ToLower()</c> needed here~
/// </para>
/// </remarks>
public sealed class SqliteWebhookRegistrationRepository : IWebhookRegistrationRepository
{
    private readonly WorkflowDataConnectionFactory _factory;

    /// <summary>
    /// Initialises a new <see cref="SqliteWebhookRegistrationRepository"/>~ .
    /// </summary>
    /// <param name="factory">Data connection factory shared with other SQLite repositories.</param>
    public SqliteWebhookRegistrationRepository(WorkflowDataConnectionFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <inheritdoc />
    public async Task<WebhookRegistrationResult> RegisterAsync(
        WebhookRegistration registration,
        CancellationToken ct = default)
    {
        await using var db = _factory.Create();

        //  Check for existing registration (COLLATE NOCASE handles case-insensitivity in DB)~
        var exists = await db.WebhookRegistrations
            .AnyAsync(w => w.WebhookId == registration.WebhookId, token: ct)
            .ConfigureAwait(false);

        if (exists)
        {
            return WebhookRegistrationResult.Conflict(registration.WebhookId);
        }

        var entity = ToEntity(registration);
        await db.InsertAsync(entity, token: ct).ConfigureAwait(false);
        return WebhookRegistrationResult.Ok(registration);
    }

    /// <inheritdoc />
    public async Task<WebhookRegistrationResult> UpdateAsync(
        WebhookRegistration registration,
        CancellationToken ct = default)
    {
        await using var db = _factory.Create();

        var existing = await db.WebhookRegistrations
            .FirstOrDefaultAsync(w => w.WebhookId == registration.WebhookId, token: ct)
            .ConfigureAwait(false);

        if (existing is null)
        {
            return WebhookRegistrationResult.NotFound(registration.WebhookId);
        }

        var entity = ToEntity(registration);
        await db.UpdateAsync(entity, token: ct).ConfigureAwait(false);
        return WebhookRegistrationResult.Ok(registration);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string webhookId, CancellationToken ct = default)
    {
        await using var db = _factory.Create();
        var affected = await db.WebhookRegistrations
            .Where(w => w.WebhookId == webhookId)
            .DeleteAsync(token: ct)
            .ConfigureAwait(false);
        return affected > 0;
    }

    /// <inheritdoc />
    public async Task<WebhookRegistration?> GetAsync(string webhookId, CancellationToken ct = default)
    {
        await using var db = _factory.Create();
        var entity = await db.WebhookRegistrations
            .FirstOrDefaultAsync(w => w.WebhookId == webhookId, token: ct)
            .ConfigureAwait(false);
        return entity is null ? null : ToDomain(entity);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WebhookRegistration>> ListAsync(CancellationToken ct = default)
    {
        await using var db = _factory.Create();
        var entities = await db.WebhookRegistrations
            .ToListAsync(token: ct)
            .ConfigureAwait(false);
        return entities.Select(ToDomain).ToList();
    }

    // ── Mapping helpers ────────────────────────────────────────────────────────────

    private static WebhookRegistrationEntity ToEntity(WebhookRegistration reg) =>
        new WebhookRegistrationEntity
        {
            WebhookId = reg.WebhookId,
            WorkflowDefId = reg.WorkflowDefinitionId.ToString(),
            AllowedMethods = JsonSerializer.Serialize(reg.AllowedMethods.ToArray()),
            // CopilotNote: Use MatchUnsafe — the None branch returns C# null, which is valid for
            // nullable SQL columns but would throw ResultIsNullException with plain Match~
            SecretKey = reg.SecretKey.MatchUnsafe(s => s, () => null),
            SignatureScheme = reg.SignatureScheme.MatchUnsafe(s => s, () => null),
            CreatedAt = reg.CreatedAt.ToString("O"),
            Enabled = reg.Enabled ? 1 : 0,
        };

    private static WebhookRegistration ToDomain(WebhookRegistrationEntity entity)
    {
        var methods = JsonSerializer.Deserialize<string[]>(entity.AllowedMethods)
            ?? Array.Empty<string>();

        return new WebhookRegistration(
            WebhookId: entity.WebhookId,
            WorkflowDefinitionId: Guid.Parse(entity.WorkflowDefId),
            AllowedMethods: Arr.create(methods),
            SecretKey: entity.SecretKey is not null
                ? Option<string>.Some(entity.SecretKey)
                : Option<string>.None,
            SignatureScheme: entity.SignatureScheme is not null
                ? Option<string>.Some(entity.SignatureScheme)
                : Option<string>.None,
            CreatedAt: DateTimeOffset.Parse(
                entity.CreatedAt,
                null,
                System.Globalization.DateTimeStyles.RoundtripKind),
            Enabled: entity.Enabled != 0);
    }
}

