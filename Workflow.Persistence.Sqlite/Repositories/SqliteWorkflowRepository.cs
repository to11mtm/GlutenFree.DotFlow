// <copyright file="SqliteWorkflowRepository.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Sqlite.Repositories;

using System.Text.Json;
using LinqToDB;
using Workflow.Core.Models;
using Workflow.Persistence.Abstractions;
using Workflow.Persistence.Models;
using Workflow.Persistence.Sqlite.Data;
using Workflow.Persistence.Sqlite.Data.Entities;

/// <summary>
/// 📋 SQLite-backed implementation of <see cref="IWorkflowRepository"/>~ ✨💖
/// </summary>
/// <remarks>
/// CopilotNote: Uses linq2db for queries. Soft-delete is <c>is_active = 0</c>.
/// JSON serialization of WorkflowDefinition uses System.Text.Json with LanguageExt converters~ UwU 🪶
/// </remarks>
public sealed class SqliteWorkflowRepository : IWorkflowRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly WorkflowDataConnectionFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteWorkflowRepository"/> class~ 🔌.
    /// </summary>
    /// <param name="factory">The data connection factory.</param>
    public SqliteWorkflowRepository(WorkflowDataConnectionFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <inheritdoc/>
    public async Task<Guid> CreateAsync(WorkflowDefinition definition, CancellationToken ct = default)
    {
        var id = definition.Id == Guid.Empty ? Guid.NewGuid() : definition.Id;
        var now = DateTimeOffset.UtcNow.ToString("O");

        var entity = new WorkflowEntity
        {
            Id = id.ToString(),
            Name = definition.Name,
            Description = definition.Description,
            Definition = SerializeDefinition(definition with { Id = id }),
            Version = definition.Version.ToString(),
            IsActive = 1,
            CreatedAt = now,
            UpdatedAt = now,
            Tags = definition.Tags.HasValue && definition.Tags.Value.Count > 0
                ? string.Join(",", definition.Tags.Value)
                : null,
            Metadata = null,
        };

        await using var db = _factory.Create();
        await db.InsertAsync(entity, token: ct).ConfigureAwait(false);
        return id;
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(Guid id, WorkflowDefinition definition, CancellationToken ct = default)
    {
        await using var db = _factory.Create();
        var existing = await db.Workflows
            .FirstOrDefaultAsync(w => w.Id == id.ToString() && w.IsActive == 1, token: ct)
            .ConfigureAwait(false);

        if (existing is null)
        {
            throw new InvalidOperationException($"Workflow {id} not found or is soft-deleted~ 😿");
        }

        var now = DateTimeOffset.UtcNow.ToString("O");

        await db.Workflows
            .Where(w => w.Id == id.ToString())
            .Set(w => w.Definition, SerializeDefinition(definition with { Id = id }))
            .Set(w => w.Name, definition.Name)
            .Set(w => w.Description, definition.Description)
            .Set(w => w.Version, definition.Version.ToString())
            .Set(w => w.UpdatedAt, now)
            .Set(w => w.Tags, definition.Tags.HasValue && definition.Tags.Value.Count > 0
                ? string.Join(",", definition.Tags.Value)
                : null)
            .UpdateAsync(token: ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = _factory.Create();
        var affected = await db.Workflows
            .Where(w => w.Id == id.ToString() && w.IsActive == 1)
            .Set(w => w.IsActive, 0)
            .Set(w => w.UpdatedAt, DateTimeOffset.UtcNow.ToString("O"))
            .UpdateAsync(token: ct)
            .ConfigureAwait(false);
        return affected > 0;
    }

    /// <inheritdoc/>
    public async Task<bool> PurgeAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = _factory.Create();
        var idStr = id.ToString();

        // Delete related records first (no FK cascade by default in SQLite)
        var executionIds = await db.Executions
            .Where(e => e.WorkflowId == idStr)
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

        await db.Executions
            .Where(e => e.WorkflowId == idStr)
            .DeleteAsync(token: ct)
            .ConfigureAwait(false);

        var affected = await db.Workflows
            .Where(w => w.Id == idStr)
            .DeleteAsync(token: ct)
            .ConfigureAwait(false);

        return affected > 0;
    }

    /// <inheritdoc/>
    public async Task<bool> RestoreAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = _factory.Create();
        var affected = await db.Workflows
            .Where(w => w.Id == id.ToString() && w.IsActive == 0)
            .Set(w => w.IsActive, 1)
            .Set(w => w.UpdatedAt, DateTimeOffset.UtcNow.ToString("O"))
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
        var query = db.Workflows.Where(w => w.Id == id.ToString());
        if (!includeDeleted)
        {
            query = query.Where(w => w.IsActive == 1);
        }

        var entity = await query.FirstOrDefaultAsync(token: ct).ConfigureAwait(false);
        return entity is null ? null : DeserializeDefinition(entity);
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
            query = query.Where(w => w.IsActive == (filter.IsActive.Value ? 1 : 0));
        }

        if (!string.IsNullOrWhiteSpace(filter.NameContains))
        {
            var nameLower = filter.NameContains.ToLowerInvariant();
            query = query.Where(w => w.Name.ToLower().Contains(nameLower));
        }

        if (filter.Tags is { Length: > 0 })
        {
            // SQLite: tag stored as comma-joined string — check each requested tag is present via INSTR
            foreach (var tag in filter.Tags)
            {
                var t = tag;
                query = query.Where(w => w.Tags != null && w.Tags.Contains(t));
            }
        }

        if (filter.CreatedAfter.HasValue)
        {
            var after = filter.CreatedAfter.Value.ToString("O");
            query = query.Where(w => string.Compare(w.CreatedAt, after, StringComparison.Ordinal) >= 0);
        }

        if (filter.CreatedBefore.HasValue)
        {
            var before = filter.CreatedBefore.Value.ToString("O");
            query = query.Where(w => string.Compare(w.CreatedAt, before, StringComparison.Ordinal) <= 0);
        }

        var totalCount = await query.CountAsync(token: ct).ConfigureAwait(false);
        var items = await query
            .OrderBy(w => w.CreatedAt)
            .Skip(pagination.Skip)
            .Take(pagination.PageSize)
            .ToListAsync(token: ct)
            .ConfigureAwait(false);

        return new PagedResult<WorkflowDefinition>(
            items.Select(DeserializeDefinition).ToList(),
            totalCount,
            pagination.Page,
            pagination.PageSize);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<WorkflowDefinition>> SearchAsync(string query, CancellationToken ct = default)
    {
        await using var db = _factory.Create();
        var lower = query.ToLowerInvariant();
        var items = await db.Workflows
            .Where(w => w.IsActive == 1 &&
                (w.Name.ToLower().Contains(lower) ||
                 (w.Description != null && w.Description.ToLower().Contains(lower))))
            .ToListAsync(token: ct)
            .ConfigureAwait(false);

        return items.Select(DeserializeDefinition).ToList();
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = _factory.Create();
        return await db.Workflows
            .AnyAsync(w => w.Id == id.ToString() && w.IsActive == 1, token: ct)
            .ConfigureAwait(false);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string SerializeDefinition(WorkflowDefinition definition)
        => JsonSerializer.Serialize(definition, JsonOptions);

    private static WorkflowDefinition DeserializeDefinition(WorkflowEntity entity)
    {
        var definition = JsonSerializer.Deserialize<WorkflowDefinition>(entity.Definition, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to deserialise definition for workflow {entity.Id}~ 😿");

        // Patch in timestamps from the entity columns (source of truth)~ 💖
        var createdAt = DateTimeOffset.Parse(entity.CreatedAt, null, System.Globalization.DateTimeStyles.RoundtripKind);
        var updatedAt = DateTimeOffset.Parse(entity.UpdatedAt, null, System.Globalization.DateTimeStyles.RoundtripKind);

        return definition with { CreatedAt = createdAt, UpdatedAt = updatedAt };
    }
}

