// <copyright file="IWebhookRegistrationRepository.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Abstractions;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Workflow.Core.Models;

/// <summary>
/// 🪝 Phase 2.3.6 — Repository for persisting and retrieving webhook registrations~ ✨💖.
/// </summary>
/// <remarks>
/// <para>
/// V1 ships with an in-memory default (<see cref="InMemoryWebhookRegistrationRepository"/>).
/// Production deployments can swap in a SQLite or Postgres-backed implementation.
/// CopilotNote: future 2.3.P1 arbitrary-path routing wires through here too~ 🧠
/// </para>
/// </remarks>
public interface IWebhookRegistrationRepository
{
    /// <summary>Register a new webhook. Returns a conflict error if <see cref="WebhookRegistration.WebhookId"/> already exists~ 📋.</summary>
    Task<WebhookRegistrationResult> RegisterAsync(WebhookRegistration registration, CancellationToken ct = default);

    /// <summary>Replace an existing webhook registration. Returns a not-found error if missing~ 🔄.</summary>
    Task<WebhookRegistrationResult> UpdateAsync(WebhookRegistration registration, CancellationToken ct = default);

    /// <summary>Remove a registration by ID. Returns <c>true</c> when found and deleted~ 🗑️.</summary>
    Task<bool> DeleteAsync(string webhookId, CancellationToken ct = default);

    /// <summary>Fetch a registration by ID. Returns <c>null</c> when not found~ 🔍.</summary>
    Task<WebhookRegistration?> GetAsync(string webhookId, CancellationToken ct = default);

    /// <summary>Return all registered webhooks~ 📋.</summary>
    Task<IReadOnlyList<WebhookRegistration>> ListAsync(CancellationToken ct = default);
}

/// <summary>
/// Result object for <see cref="IWebhookRegistrationRepository"/> write operations~ 📦.
/// </summary>
/// <param name="Success">Whether the operation succeeded.</param>
/// <param name="Error">Error message when <paramref name="Success"/> is <c>false</c>.</param>
/// <param name="ErrorCode">Machine-readable code: <c>"CONFLICT"</c>, <c>"NOT_FOUND"</c>, etc.</param>
/// <param name="Registration">The resulting registration when successful.</param>
public sealed record WebhookRegistrationResult(
    bool Success,
    string? Error = null,
    string? ErrorCode = null,
    WebhookRegistration? Registration = null)
{
    /// <summary>Factory for a successful result~ ✅.</summary>
    public static WebhookRegistrationResult Ok(WebhookRegistration registration)
        => new(Success: true, Registration: registration);

    /// <summary>Factory for a conflict (duplicate ID) error~ ❌.</summary>
    public static WebhookRegistrationResult Conflict(string webhookId)
        => new(Success: false, Error: $"Webhook '{webhookId}' already exists.", ErrorCode: "CONFLICT");

    /// <summary>Factory for a not-found error~ ❌.</summary>
    public static WebhookRegistrationResult NotFound(string webhookId)
        => new(Success: false, Error: $"Webhook '{webhookId}' not found.", ErrorCode: "NOT_FOUND");
}

