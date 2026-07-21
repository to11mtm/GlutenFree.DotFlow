// <copyright file="InMemoryWebhookRegistrationRepository.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Abstractions;

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Workflow.Core.Models;

/// <summary>
/// 🧠 Phase 2.3.6 — Thread-safe in-memory implementation of <see cref="IWebhookRegistrationRepository"/>.
/// The default for single-process / test deployments. Loses state on process restart~ ✨💖.
/// </summary>
/// <remarks>
/// CopilotNote: Uses <see cref="ConcurrentDictionary{TKey,TValue}"/> so no lock is needed for
/// individual reads/writes. Lookups are case-insensitive via
/// <see cref="System.StringComparer.OrdinalIgnoreCase"/>~ 🧠
/// </remarks>
public sealed class InMemoryWebhookRegistrationRepository : IWebhookRegistrationRepository
{
    private readonly ConcurrentDictionary<string, WebhookRegistration> _store
        = new(System.StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public Task<WebhookRegistrationResult> RegisterAsync(
        WebhookRegistration registration, CancellationToken ct = default)
    {
        if (_store.ContainsKey(registration.WebhookId))
        {
            return Task.FromResult(WebhookRegistrationResult.Conflict(registration.WebhookId));
        }

        _store[registration.WebhookId] = registration;
        return Task.FromResult(WebhookRegistrationResult.Ok(registration));
    }

    /// <inheritdoc />
    public Task<WebhookRegistrationResult> UpdateAsync(
        WebhookRegistration registration, CancellationToken ct = default)
    {
        if (!_store.ContainsKey(registration.WebhookId))
        {
            return Task.FromResult(WebhookRegistrationResult.NotFound(registration.WebhookId));
        }

        _store[registration.WebhookId] = registration;
        return Task.FromResult(WebhookRegistrationResult.Ok(registration));
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(string webhookId, CancellationToken ct = default)
        => Task.FromResult(_store.TryRemove(webhookId, out _));

    /// <inheritdoc />
    public Task<WebhookRegistration?> GetAsync(string webhookId, CancellationToken ct = default)
    {
        _store.TryGetValue(webhookId, out var reg);
        return Task.FromResult(reg);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<WebhookRegistration>> ListAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<WebhookRegistration>>(_store.Values.ToList());
}

