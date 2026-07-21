// <copyright file="IWorkflowRepository.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Abstractions;

using Workflow.Core.Models;
using Workflow.Persistence.Models;

/// <summary>
/// 📋 Repository for persisting and querying workflow definitions.
/// Supports soft delete by default with optional hard purge~ ✨💖
/// </summary>
/// <remarks>
/// CopilotNote: <see cref="DeleteAsync"/> is a soft delete (sets <c>is_active = false</c>).
/// Use <see cref="PurgeAsync"/> to hard-delete a workflow and all its related records.
/// Use <see cref="RestoreAsync"/> to un-delete a soft-deleted workflow~ UwU 🔄
/// </remarks>
public interface IWorkflowRepository
{
    /// <summary>Creates a new workflow definition and returns its ID~ ✨.</summary>
    Task<Guid> CreateAsync(WorkflowDefinition definition, CancellationToken ct = default);

    /// <summary>Updates an existing workflow definition~ 📝.</summary>
    Task UpdateAsync(Guid id, WorkflowDefinition definition, CancellationToken ct = default);

    /// <summary>Soft-deletes a workflow (sets <c>is_active = false</c>). Returns <c>true</c> if found~ 🗑️.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Hard-deletes a workflow and all related records (executions, variables).
    /// Returns <c>true</c> if found and purged~ ⚠️.
    /// </summary>
    Task<bool> PurgeAsync(Guid id, CancellationToken ct = default);

    /// <summary>Restores a soft-deleted workflow. Returns <c>true</c> if found and restored~ ♻️.</summary>
    Task<bool> RestoreAsync(Guid id, CancellationToken ct = default);

    /// <summary>Gets a workflow by ID (active only). Returns <c>null</c> if not found or soft-deleted~ 🔍.</summary>
    Task<WorkflowDefinition?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Gets a workflow by ID. If <paramref name="includeDeleted"/> is <c>true</c>, returns soft-deleted too~ 🔍.</summary>
    Task<WorkflowDefinition?> GetByIdAsync(Guid id, bool includeDeleted, CancellationToken ct = default);

    /// <summary>Gets a filtered, paginated list of workflows~ 📄.</summary>
    Task<PagedResult<WorkflowDefinition>> GetAllAsync(WorkflowFilter filter, Pagination pagination, CancellationToken ct = default);

    /// <summary>Searches workflows by name/description substring (case-insensitive, active only)~ 🔎.</summary>
    Task<IReadOnlyList<WorkflowDefinition>> SearchAsync(string query, CancellationToken ct = default);

    /// <summary>Checks whether an active workflow with the given ID exists~ ❓.</summary>
    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);
}

