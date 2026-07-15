// <copyright file="PostgresVariableStore.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

using LinqToDB.Async;

namespace Workflow.Persistence.Postgres.Repositories;

using System.Text.Json;
using LinqToDB;
using LinqToDB.Data;
using Workflow.Persistence.Abstractions;
using Workflow.Persistence.Models;
using Workflow.Persistence.Postgres.Data;
using Workflow.Persistence.Postgres.Data.Entities;

/// <summary>
/// 💾 PostgreSQL-backed implementation of <see cref="IVariableStore"/>~ ✨💖
/// </summary>
/// <remarks>
/// CopilotNote: Null value semantics — <c>null</c> value stored as SQL NULL in the <c>value</c>
/// jsonb column with <c>value_type = "null"</c>. A non-null <see cref="VariableEntry"/> with
/// <c>Value = null</c> is DISTINCT from "variable not found" (C# <c>null</c>)~ 💡
/// </remarks>
public sealed class PostgresVariableStore : IVariableStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly WorkflowDataConnectionFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresVariableStore"/> class~ 🔌.
    /// </summary>
    /// <param name="factory">The data connection factory.</param>
    public PostgresVariableStore(WorkflowDataConnectionFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <inheritdoc/>
    public async Task SetVariableAsync(
        VariableScope scope,
        string name,
        object? value,
        CancellationToken ct = default)
    {
        await using var db = _factory.Create();
        var (scopeKind, scopeId) = GetScopeColumns(scope);
        var now = DateTimeOffset.UtcNow;
        var jsonValue = value is not null ? JsonSerializer.Serialize(value, JsonOptions) : null;
        var valueType = value is null ? "null" : value.GetType().FullName ?? value.GetType().Name;

        // CopilotNote: Atomic INSERT ... SELECT MAX(version)+1 prevents race conditions when
        // multiple writers call SetVariableAsync concurrently — no separate SELECT needed~ 🔒
        await db.Variables
            .Value(v => v.ScopeKind, scopeKind)
            .Value(v => v.ScopeId, scopeId)
            .Value(v => v.Name, name)
            .Value(v => v.Value, jsonValue)
            .Value(v => v.ValueType, valueType)
            .Value(v => v.Version, () =>
                (db.Variables
                    .Where(x => x.ScopeKind == scopeKind
                                && (x.ScopeId == scopeId || (x.ScopeId == null && scopeId == null))
                                && x.Name == name)
                    .Max(x => (int?)x.Version) ?? 0) + 1)
            .Value(v => v.CreatedAt, now)
            .Value(v => v.UpdatedAt, now)
            .InsertAsync(ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<VariableEntry?> GetVariableAsync(
        VariableScope scope,
        string name,
        int? version = null,
        CancellationToken ct = default)
    {
        await using var db = _factory.Create();
        var (scopeKind, scopeId) = GetScopeColumns(scope);

        VariableEntity? entity;

        if (version.HasValue)
        {
            entity = await db.Variables
                .FirstOrDefaultAsync(v => v.ScopeKind == scopeKind && v.ScopeId == scopeId && v.Name == name && v.Version == version.Value, token: ct)
                .ConfigureAwait(false);
        }
        else
        {
            var maxVersion = await db.Variables
                .Where(v => v.ScopeKind == scopeKind && v.ScopeId == scopeId && v.Name == name)
                .MaxAsync(v => (int?)v.Version, token: ct)
                .ConfigureAwait(false);

            if (maxVersion is null)
            {
                return null;
            }

            entity = await db.Variables
                .FirstOrDefaultAsync(v => v.ScopeKind == scopeKind && v.ScopeId == scopeId && v.Name == name && v.Version == maxVersion.Value, token: ct)
                .ConfigureAwait(false);
        }

        return entity is null ? null : MapToEntry(scope, entity);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<VariableEntry>> GetVariableHistoryAsync(
        VariableScope scope,
        string name,
        CancellationToken ct = default)
    {
        await using var db = _factory.Create();
        var (scopeKind, scopeId) = GetScopeColumns(scope);

        var items = await db.Variables
            .Where(v => v.ScopeKind == scopeKind && v.ScopeId == scopeId && v.Name == name)
            .OrderBy(v => v.Version)
            .ToListAsync(token: ct)
            .ConfigureAwait(false);

        return items.Select(e => MapToEntry(scope, e)).ToList();
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteVariableAsync(
        VariableScope scope,
        string name,
        CancellationToken ct = default)
    {
        await using var db = _factory.Create();
        var (scopeKind, scopeId) = GetScopeColumns(scope);

        var affected = await db.Variables
            .Where(v => v.ScopeKind == scopeKind && v.ScopeId == scopeId && v.Name == name)
            .DeleteAsync(token: ct)
            .ConfigureAwait(false);

        return affected > 0;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, object?>> GetAllVariablesAsync(
        VariableScope scope,
        CancellationToken ct = default)
    {
        await using var db = _factory.Create();
        var (scopeKind, scopeId) = GetScopeColumns(scope);

        // Get latest version per name via GROUP BY + MAX~ 📋
        var latestVersions = await db.Variables
            .Where(v => v.ScopeKind == scopeKind && v.ScopeId == scopeId)
            .GroupBy(v => v.Name)
            .Select(g => new { Name = g.Key, MaxVersion = g.Max(v => v.Version) })
            .ToListAsync(token: ct)
            .ConfigureAwait(false);

        var result = new Dictionary<string, object?>();

        foreach (var nv in latestVersions)
        {
            var entity = await db.Variables
                .FirstOrDefaultAsync(v => v.ScopeKind == scopeKind && v.ScopeId == scopeId && v.Name == nv.Name && v.Version == nv.MaxVersion, token: ct)
                .ConfigureAwait(false);

            if (entity is not null)
            {
                result[entity.Name] = entity.Value is null ? null : JsonSerializer.Deserialize<object?>(entity.Value, JsonOptions);
            }
        }

        return result;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static (string scopeKind, Guid? scopeId) GetScopeColumns(VariableScope scope) =>
        scope.Kind switch
        {
            VariableScopeKind.Global => ("Global", null),
            VariableScopeKind.Workflow => ("Workflow", scope.WorkflowId),
            VariableScopeKind.Execution => ("Execution", scope.ExecutionId),
            _ => throw new ArgumentOutOfRangeException(nameof(scope), $"Unknown scope kind: {scope.Kind}~ 😿"),
        };

    private static VariableEntry MapToEntry(VariableScope scope, VariableEntity entity) =>
        new VariableEntry(
            Scope: scope,
            Name: entity.Name,
            Value: entity.Value is null ? null : JsonSerializer.Deserialize<object?>(entity.Value, JsonOptions),
            ValueTypeName: entity.ValueType,
            Version: entity.Version,
            CreatedAt: entity.CreatedAt,
            UpdatedAt: entity.UpdatedAt);
}



