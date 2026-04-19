// <copyright file="SqliteVariableStore.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Sqlite.Repositories;

using System.Text.Json;
using LinqToDB;
using Workflow.Persistence.Abstractions;
using Workflow.Persistence.Models;
using Workflow.Persistence.Sqlite.Data;
using Workflow.Persistence.Sqlite.Data.Entities;

/// <summary>
/// 💾 SQLite-backed implementation of <see cref="IVariableStore"/>~ ✨💖
/// </summary>
/// <remarks>
/// CopilotNote: Every call to <see cref="SetVariableAsync"/> inserts a NEW versioned row.
/// <c>null</c> value is stored as SQL NULL in the <c>value</c> column with <c>value_type = "null"</c>.
/// This is distinct from "variable not found" (which returns C# <c>null</c>)~ UwU 💡
/// </remarks>
public sealed class SqliteVariableStore : IVariableStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly WorkflowDataConnectionFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteVariableStore"/> class~ 🔌.
    /// </summary>
    /// <param name="factory">The data connection factory.</param>
    public SqliteVariableStore(WorkflowDataConnectionFactory factory)
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
        var now = DateTimeOffset.UtcNow.ToString("O");

        // Get next version — MAX(version) + 1 for this scope+name~ 🔢
        var maxVersion = await db.Variables
            .Where(v => v.ScopeKind == scopeKind && v.ScopeId == scopeId && v.Name == name)
            .MaxAsync(v => (int?)v.Version, token: ct)
            .ConfigureAwait(false);

        var nextVersion = (maxVersion ?? 0) + 1;

        var entity = new VariableEntity
        {
            ScopeKind = scopeKind,
            ScopeId = scopeId,
            Name = name,
            Value = value is not null ? JsonSerializer.Serialize(value, JsonOptions) : null,
            ValueType = value is null ? "null" : value.GetType().FullName ?? value.GetType().Name,
            Version = nextVersion,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await db.InsertAsync(entity, token: ct).ConfigureAwait(false);
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
                .FirstOrDefaultAsync(
                    v => v.ScopeKind == scopeKind && v.ScopeId == scopeId && v.Name == name && v.Version == version.Value,
                    token: ct)
                .ConfigureAwait(false);
        }
        else
        {
            // Latest version = MAX(version)~ ✨
            var maxVersion = await db.Variables
                .Where(v => v.ScopeKind == scopeKind && v.ScopeId == scopeId && v.Name == name)
                .MaxAsync(v => (int?)v.Version, token: ct)
                .ConfigureAwait(false);

            if (maxVersion is null)
            {
                return null; // Variable does not exist at all~ 🔍
            }

            entity = await db.Variables
                .FirstOrDefaultAsync(
                    v => v.ScopeKind == scopeKind && v.ScopeId == scopeId && v.Name == name && v.Version == maxVersion.Value,
                    token: ct)
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

        // Get the latest version per variable name via a subquery~ 📋
        // CopilotNote: SQLite doesn't have window functions in all versions; use a self-join on MAX(version)~ 🪶
        var latestVersions = await db.Variables
            .Where(v => v.ScopeKind == scopeKind && v.ScopeId == scopeId)
            .GroupBy(v => v.Name)
            .Select(g => new { Name = g.Key, MaxVersion = g.Max(v => v.Version) })
            .ToListAsync(token: ct)
            .ConfigureAwait(false);

        var result = new Dictionary<string, object?>();

        foreach (var nameVersion in latestVersions)
        {
            var entity = await db.Variables
                .FirstOrDefaultAsync(
                    v => v.ScopeKind == scopeKind && v.ScopeId == scopeId
                        && v.Name == nameVersion.Name && v.Version == nameVersion.MaxVersion,
                    token: ct)
                .ConfigureAwait(false);

            if (entity is not null)
            {
                result[entity.Name] = DeserializeValue(entity);
            }
        }

        return result;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static (string scopeKind, string? scopeId) GetScopeColumns(VariableScope scope) =>
        scope.Kind switch
        {
            VariableScopeKind.Global => ("Global", null),
            VariableScopeKind.Workflow => ("Workflow", scope.WorkflowId?.ToString()),
            VariableScopeKind.Execution => ("Execution", scope.ExecutionId?.ToString()),
            _ => throw new ArgumentOutOfRangeException(nameof(scope), $"Unknown scope kind: {scope.Kind}~ 😿"),
        };

    private static VariableEntry MapToEntry(VariableScope scope, VariableEntity entity) =>
        new VariableEntry(
            Scope: scope,
            Name: entity.Name,
            Value: DeserializeValue(entity),
            ValueTypeName: entity.ValueType,
            Version: entity.Version,
            CreatedAt: DateTimeOffset.Parse(entity.CreatedAt, null, System.Globalization.DateTimeStyles.RoundtripKind),
            UpdatedAt: DateTimeOffset.Parse(entity.UpdatedAt, null, System.Globalization.DateTimeStyles.RoundtripKind));

    private static object? DeserializeValue(VariableEntity entity)
    {
        if (entity.Value is null)
        {
            // Explicit null value — return null but caller gets a non-null VariableEntry~ 💡
            return null;
        }

        return JsonSerializer.Deserialize<object?>(entity.Value, JsonOptions);
    }
}

