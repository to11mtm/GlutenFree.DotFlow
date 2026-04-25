// <copyright file="NatsWorkflowRepository.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Nats.Repositories;

using NATS.Client.KeyValueStore;
using Workflow.Core.Models;
using Workflow.Persistence.Abstractions;
using Workflow.Persistence.Models;
using Workflow.Persistence.Nats.Internal;

/// <summary>
/// 📋 NATS KV-backed implementation of <see cref="IWorkflowRepository"/>~ ✨💖
/// </summary>
/// <remarks>
/// CopilotNote: All workflow documents are stored in the <c>WF_WORKFLOWS</c> bucket.
/// Key = workflow UUID (as string). Value = JSON-serialised <see cref="NatsWorkflowDocument"/>.
/// <list type="bullet">
///   <item>Soft delete → update <c>IsActive = false</c> in the document.</item>
///   <item>Hard delete (Purge) → NATS KV <c>PurgeAsync</c> removes all revisions.</item>
///   <item><c>GetAllAsync</c> / <c>SearchAsync</c> are in-memory scans (NATS KV has no server-side filter).</item>
/// </list>
/// </remarks>
public sealed class NatsWorkflowRepository : IWorkflowRepository
{
    /// <summary>NATS KV bucket name for workflow documents~ 🗃️.</summary>
    public const string BucketName = "WF_WORKFLOWS";

    private readonly INatsKVStore _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="NatsWorkflowRepository"/> class~ 🔌.
    /// </summary>
    /// <param name="store">The pre-initialised NATS KV store for workflows.</param>
    public NatsWorkflowRepository(INatsKVStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc/>
    public async Task<Guid> CreateAsync(WorkflowDefinition definition, CancellationToken ct = default)
    {
        var id = definition.Id == Guid.Empty ? Guid.NewGuid() : definition.Id;
        var now = DateTimeOffset.UtcNow.ToString("O");

        var doc = new NatsWorkflowDocument(
            Definition: definition with { Id = id },
            IsActive: true,
            CreatedAt: now,
            UpdatedAt: now);

        await _store.PutAsync(id.ToString(), NatsJsonHelper.Serialize(doc), cancellationToken: ct)
            .ConfigureAwait(false);

        return id;
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(Guid id, WorkflowDefinition definition, CancellationToken ct = default)
    {
        var existing = await GetDocumentAsync(id.ToString(), ct).ConfigureAwait(false);

        if (existing is null || !existing.IsActive)
        {
            throw new InvalidOperationException($"Workflow {id} not found or is soft-deleted~ 😿");
        }

        var updated = existing with
        {
            Definition = definition with { Id = id },
            UpdatedAt = DateTimeOffset.UtcNow.ToString("O"),
        };

        await _store.PutAsync(id.ToString(), NatsJsonHelper.Serialize(updated), cancellationToken: ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var existing = await GetDocumentAsync(id.ToString(), ct).ConfigureAwait(false);

        if (existing is null || !existing.IsActive)
        {
            return false;
        }

        var updated = existing with
        {
            IsActive = false,
            UpdatedAt = DateTimeOffset.UtcNow.ToString("O"),
        };

        await _store.PutAsync(id.ToString(), NatsJsonHelper.Serialize(updated), cancellationToken: ct)
            .ConfigureAwait(false);

        return true;
    }

    /// <inheritdoc/>
    public async Task<bool> PurgeAsync(Guid id, CancellationToken ct = default)
    {
        var existing = await GetDocumentAsync(id.ToString(), ct).ConfigureAwait(false);

        if (existing is null)
        {
            return false;
        }

        // CopilotNote: NATS KV PurgeAsync removes all revisions and creates a purge tombstone~
        await _store.PurgeAsync(id.ToString(), cancellationToken: ct).ConfigureAwait(false);

        return true;
    }

    /// <inheritdoc/>
    public async Task<bool> RestoreAsync(Guid id, CancellationToken ct = default)
    {
        var existing = await GetDocumentAsync(id.ToString(), ct).ConfigureAwait(false);

        if (existing is null || existing.IsActive)
        {
            return false;
        }

        var restored = existing with
        {
            IsActive = true,
            UpdatedAt = DateTimeOffset.UtcNow.ToString("O"),
        };

        await _store.PutAsync(id.ToString(), NatsJsonHelper.Serialize(restored), cancellationToken: ct)
            .ConfigureAwait(false);

        return true;
    }

    /// <inheritdoc/>
    public async Task<WorkflowDefinition?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await GetByIdAsync(id, includeDeleted: false, ct).ConfigureAwait(false);

    /// <inheritdoc/>
    public async Task<WorkflowDefinition?> GetByIdAsync(Guid id, bool includeDeleted, CancellationToken ct = default)
    {
        var doc = await GetDocumentAsync(id.ToString(), ct).ConfigureAwait(false);

        if (doc is null)
        {
            return null;
        }

        if (!includeDeleted && !doc.IsActive)
        {
            return null;
        }

        return EnrichDefinition(doc);
    }

    /// <inheritdoc/>
    public async Task<PagedResult<WorkflowDefinition>> GetAllAsync(
        WorkflowFilter filter,
        Pagination pagination,
        CancellationToken ct = default)
    {
        var all = await LoadAllDocumentsAsync(ct).ConfigureAwait(false);

        // Apply filter in memory (NATS KV has no server-side query)
        var filtered = all.AsEnumerable();

        if (filter.IsActive.HasValue)
        {
            filtered = filtered.Where(d => d.IsActive == filter.IsActive.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.NameContains))
        {
            var lower = filter.NameContains.ToLowerInvariant();
            filtered = filtered.Where(d => d.Definition.Name.ToLowerInvariant().Contains(lower));
        }

        if (filter.Tags is { Length: > 0 })
        {
            foreach (var tag in filter.Tags)
            {
                var t = tag;
                filtered = filtered.Where(d =>
                    d.Definition.Tags.HasValue && d.Definition.Tags.Value.Contains(t));
            }
        }

        if (filter.CreatedAfter.HasValue)
        {
            var after = filter.CreatedAfter.Value;
            filtered = filtered.Where(d =>
                DateTimeOffset.TryParse(d.CreatedAt, out var dt) && dt >= after);
        }

        if (filter.CreatedBefore.HasValue)
        {
            var before = filter.CreatedBefore.Value;
            filtered = filtered.Where(d =>
                DateTimeOffset.TryParse(d.CreatedAt, out var dt) && dt <= before);
        }

        var list = filtered
            .OrderBy(d => d.CreatedAt)
            .Select(EnrichDefinition)
            .ToList();

        var totalCount = list.Count;
        var paged = list
            .Skip(pagination.Skip)
            .Take(pagination.PageSize)
            .ToList();

        return new PagedResult<WorkflowDefinition>(paged, totalCount, pagination.Page, pagination.PageSize);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<WorkflowDefinition>> SearchAsync(string query, CancellationToken ct = default)
    {
        var all = await LoadAllDocumentsAsync(ct).ConfigureAwait(false);
        var lower = query.ToLowerInvariant();

        return all
            .Where(d => d.IsActive &&
                (d.Definition.Name.ToLowerInvariant().Contains(lower) ||
                 (d.Definition.Description?.ToLowerInvariant().Contains(lower) == true)))
            .Select(EnrichDefinition)
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsAsync(Guid id, CancellationToken ct = default)
    {
        var doc = await GetDocumentAsync(id.ToString(), ct).ConfigureAwait(false);
        return doc is { IsActive: true };
    }

    // ── Private Helpers ───────────────────────────────────────────────────────

    /// <summary>Tries to load and deserialise a workflow document by key~ 🔍.</summary>
    private async Task<NatsWorkflowDocument?> GetDocumentAsync(string key, CancellationToken ct)
    {
	    // Note for Copilot: Should we be using TryGetEntryAsync here, using the result from that to bubble up?
	    //   Case that comes to mind is Purging deletes keyes, which causes a NatsKVKeyDeletedException, we should handle that
	    //   and return null instead of throwing.
	    //   But also we should give internal callers more control over the exception types as needed.
        try
        {
            var entry = await _store.GetEntryAsync<string>(key, cancellationToken: ct).ConfigureAwait(false);

            // CopilotNote: operation != Put means the key was deleted/purged~ 🗑️
            if (entry.Operation != NatsKVOperation.Put || entry.Value is null)
            {
                return null;
            }

            return NatsJsonHelper.Deserialize<NatsWorkflowDocument>(entry.Value);
        }
        catch (NatsKVKeyNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Loads all workflow documents from the KV bucket~ 📦.</summary>
    private async Task<List<NatsWorkflowDocument>> LoadAllDocumentsAsync(CancellationToken ct)
    {
        var result = new List<NatsWorkflowDocument>();

        await foreach (var key in _store.GetKeysAsync(cancellationToken: ct).ConfigureAwait(false))
        {
            var doc = await GetDocumentAsync(key, ct).ConfigureAwait(false);
            if (doc is not null)
            {
                result.Add(doc);
            }
        }

        return result;
    }

    /// <summary>Enriches the definition with timestamps from the document~ 💖.</summary>
    private static WorkflowDefinition EnrichDefinition(NatsWorkflowDocument doc)
    {
        var createdAt = DateTimeOffset.TryParse(doc.CreatedAt, null,
            System.Globalization.DateTimeStyles.RoundtripKind, out var c) ? c : DateTimeOffset.UtcNow;
        var updatedAt = DateTimeOffset.TryParse(doc.UpdatedAt, null,
            System.Globalization.DateTimeStyles.RoundtripKind, out var u) ? u : DateTimeOffset.UtcNow;

        return doc.Definition with { CreatedAt = createdAt, UpdatedAt = updatedAt };
    }
}

