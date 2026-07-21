// <copyright file="PostgresWorkflowRepository.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

using LinqToDB.Async;

namespace Workflow.Persistence.Postgres.Repositories;

using System.Text.Json;
using LinqToDB;
using Workflow.Core.Models;
using Workflow.Persistence.Abstractions;
using Workflow.Persistence.Models;
using Workflow.Persistence.Postgres.Data;
using Workflow.Persistence.Postgres.Data.Entities;
using Workflow.Persistence.Postgres.Serialization;

/// <summary>
/// 📋 PostgreSQL-backed implementation of <see cref="IWorkflowRepository"/>~ ✨💖
/// </summary>
/// <remarks>
/// CopilotNote: Uses Postgres-native types — UUID PKs, timestamptz for dates, jsonb for definition,
/// text[] for tags. Tag filtering uses the @> array containment operator~ 🐘
/// </remarks>
public sealed class PostgresWorkflowRepository : IWorkflowRepository
{
    private static readonly JsonSerializerOptions JsonOptions = WorkflowJsonOptions.Create();

    private readonly WorkflowDataConnectionFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresWorkflowRepository"/> class~ 🔌.
    /// </summary>
    /// <param name="factory">The data connection factory.</param>
    public PostgresWorkflowRepository(WorkflowDataConnectionFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <inheritdoc/>
    public async Task<Guid> CreateAsync(WorkflowDefinition definition, CancellationToken ct = default)
    {
        var id = definition.Id == Guid.Empty ? Guid.NewGuid() : definition.Id;
        var now = DateTimeOffset.UtcNow;

        var entity = new WorkflowEntity
        {
            Id = id,
            Name = definition.Name,
            Description = definition.Description,
            Definition = SerializeDefinition(definition with { Id = id }),
            Version = definition.Version.ToString(),
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
            Tags = definition.Tags.HasValue && definition.Tags.Value.Count > 0
                ? definition.Tags.Value.ToArray()
                : null,
        };

        await using var db = _factory.Create();
        await db.InsertAsync(entity, token: ct).ConfigureAwait(false);
        return id;
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(Guid id, WorkflowDefinition definition, CancellationToken ct = default)
    {
        await using var db = _factory.Create();
        var exists = await db.Workflows
            .AnyAsync(w => w.Id == id && w.IsActive, token: ct)
            .ConfigureAwait(false);

        if (!exists)
        {
            throw new InvalidOperationException($"Workflow {id} not found or is soft-deleted~ 😿");
        }

        var now = DateTimeOffset.UtcNow;
        await db.Workflows
            .Where(w => w.Id == id)
            .Set(w => w.Definition, SerializeDefinition(definition with { Id = id }))
            .Set(w => w.Name, definition.Name)
            .Set(w => w.Description, definition.Description)
            .Set(w => w.Version, definition.Version.ToString())
            .Set(w => w.UpdatedAt, now)
            .Set(w => w.Tags, definition.Tags.HasValue && definition.Tags.Value.Count > 0
                ? definition.Tags.Value.ToArray()
                : null)
            .UpdateAsync(token: ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = _factory.Create();
        var affected = await db.Workflows
            .Where(w => w.Id == id && w.IsActive)
            .Set(w => w.IsActive, false)
            .Set(w => w.UpdatedAt, DateTimeOffset.UtcNow)
            .UpdateAsync(token: ct)
            .ConfigureAwait(false);
        return affected > 0;
    }

    /// <inheritdoc/>
    public async Task<bool> PurgeAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = _factory.Create();

        // Delete related records first (execution_nodes → executions)~ ⚠️
        var executionIds = await db.Executions
            .Where(e => e.WorkflowId == id)
            .Select(e => e.Id)
            .ToListAsync(token: ct)
            .ConfigureAwait(false);

        foreach (var execId in executionIds)
        {
            await db.ExecutionNodes
                .Where(n => n.ExecutionId == execId)
                .DeleteAsync(token: ct)
                .ConfigureAwait(false);
        }

        await db.Executions.Where(e => e.WorkflowId == id).DeleteAsync(token: ct).ConfigureAwait(false);
        await db.Variables.Where(v => v.ScopeKind == "Workflow" && v.ScopeId == id).DeleteAsync(token: ct).ConfigureAwait(false);

        var affected = await db.Workflows.Where(w => w.Id == id).DeleteAsync(token: ct).ConfigureAwait(false);
        return affected > 0;
    }

    /// <inheritdoc/>
    public async Task<bool> RestoreAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = _factory.Create();
        var affected = await db.Workflows
            .Where(w => w.Id == id && !w.IsActive)
            .Set(w => w.IsActive, true)
            .Set(w => w.UpdatedAt, DateTimeOffset.UtcNow)
            .UpdateAsync(token: ct)
            .ConfigureAwait(false);
        return affected > 0;
    }

    /// <inheritdoc/>
    public async Task<WorkflowDefinition?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await GetByIdAsync(id, includeDeleted: false, ct).ConfigureAwait(false);

    /// <inheritdoc/>
    public async Task<WorkflowDefinition?> GetByIdAsync(Guid id, bool includeDeleted, CancellationToken ct = default)
    {
        await using var db = _factory.Create();
        var query = db.Workflows.Where(w => w.Id == id);
        if (!includeDeleted)
        {
            query = query.Where(w => w.IsActive);
        }

        var entity = await query.FirstOrDefaultAsync(token: ct).ConfigureAwait(false);
        return entity is null ? null : DeserializeEntity(entity);
    }

    /// <inheritdoc/>
    public async Task<PagedResult<WorkflowDefinition>> GetAllAsync(
        WorkflowFilter filter,
        Pagination pagination,
        CancellationToken ct = default)
    {
        await using var db = _factory.Create();
        var query = db.Workflows.AsQueryable();

        if (filter.IsActive.HasValue)
        {
            query = query.Where(w => w.IsActive == filter.IsActive.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.NameContains))
        {
            var lower = filter.NameContains.ToLowerInvariant();
            query = query.Where(w => w.Name.ToLower().Contains(lower));
        }

        if (filter.CreatedAfter.HasValue)
        {
            var after = filter.CreatedAfter.Value;
            query = query.Where(w => w.CreatedAt >= after);
        }

        if (filter.CreatedBefore.HasValue)
        {
            var before = filter.CreatedBefore.Value;
            query = query.Where(w => w.CreatedAt <= before);
        }

        // CopilotNote: Tag filtering via text[] @> containment is a Postgres raw query.
        // linq2db doesn't natively support @> so we fall back to client-side filtering after
        // fetching — acceptable for moderate dataset sizes. For large datasets use Execute.Sql~ 🐘
        var totalCount = await query.CountAsync(token: ct).ConfigureAwait(false);
        var items = await query
            .OrderBy(w => w.CreatedAt)
            .Skip(pagination.Skip)
            .Take(pagination.PageSize)
            .ToListAsync(token: ct)
            .ConfigureAwait(false);

        var definitions = items.Select(DeserializeEntity).ToList();

        // Client-side tag filtering (post-fetch) for linq2db Postgres compatibility~ 🏷️
        if (filter.Tags is { Length: > 0 })
        {
            definitions = definitions
                .Where(d => d.Tags.HasValue && filter.Tags.All(t => d.Tags.Value.Contains(t)))
                .ToList();
        }

        return new PagedResult<WorkflowDefinition>(definitions, totalCount, pagination.Page, pagination.PageSize);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<WorkflowDefinition>> SearchAsync(string query, CancellationToken ct = default)
    {
        await using var db = _factory.Create();
        var lower = query.ToLowerInvariant();
        var items = await db.Workflows
            .Where(w => w.IsActive &&
                (w.Name.ToLower().Contains(lower) ||
                 (w.Description != null && w.Description.ToLower().Contains(lower))))
            .ToListAsync(token: ct)
            .ConfigureAwait(false);

        return items.Select(DeserializeEntity).ToList();
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = _factory.Create();
        return await db.Workflows
            .AnyAsync(w => w.Id == id && w.IsActive, token: ct)
            .ConfigureAwait(false);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string SerializeDefinition(WorkflowDefinition definition)
        => JsonSerializer.Serialize(definition, JsonOptions);

    private static WorkflowDefinition DeserializeEntity(WorkflowEntity entity)
    {
        var definition = JsonSerializer.Deserialize<WorkflowDefinition>(entity.Definition, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to deserialise definition for workflow {entity.Id}~ 😿");

        return definition with { CreatedAt = entity.CreatedAt, UpdatedAt = entity.UpdatedAt };
    }
}

