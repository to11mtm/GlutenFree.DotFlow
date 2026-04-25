// <copyright file="NatsVariableStore.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Nats.Repositories;

using NATS.Client.KeyValueStore;
using Workflow.Persistence.Abstractions;
using Workflow.Persistence.Models;
using Workflow.Persistence.Nats.Internal;

/// <summary>
/// 💾 NATS KV-backed implementation of <see cref="IVariableStore"/>~ ✨💖
/// </summary>
/// <remarks>
/// CopilotNote: Uses bucket <c>WF_VARIABLES</c> with <c>History = 100</c> to retain versions.
/// Key format:
/// <list type="bullet">
///   <item>Global scope:    <c>global::{name}</c></item>
///   <item>Workflow scope:  <c>workflow:{workflowId}:{name}</c></item>
///   <item>Execution scope: <c>execution:{executionId}:{name}</c></item>
/// </list>
/// Each <c>SetVariableAsync</c> call creates a new NATS KV revision (version). The
/// <see cref="NatsVariableDocument.Version"/> field is the sequential 1-based version counter.
/// NATS KV <c>PurgeAsync</c> is used for <see cref="DeleteVariableAsync"/> (removes all revisions).
/// </remarks>
public sealed class NatsVariableStore : IVariableStore
{
    /// <summary>NATS KV bucket name for variables~ 💾.</summary>
    public const string BucketName = "WF_VARIABLES";

    private readonly INatsKVStore _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="NatsVariableStore"/> class~ 🔌.
    /// </summary>
    /// <param name="store">The pre-initialised NATS KV store for variables.</param>
    public NatsVariableStore(INatsKVStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc/>
    public async Task SetVariableAsync(
        VariableScope scope,
        string name,
        object? value,
        CancellationToken ct = default)
    {
        var key = BuildKey(scope, name);

        // CopilotNote: Get current version to compute next version number~ 🧠
        var history = await GetAllHistoryDocsAsync(key, ct).ConfigureAwait(false);
        var nextVersion = history.Count + 1;

        var now = DateTimeOffset.UtcNow.ToString("O");
        var doc = new NatsVariableDocument(
            Value: value,
            ValueTypeName: value is null ? "null" : value.GetType().FullName ?? "unknown",
            Version: nextVersion,
            CreatedAt: history.Count == 0 ? now : history[0].CreatedAt,
            UpdatedAt: now);

        await _store.PutAsync(key, NatsJsonHelper.Serialize(doc), cancellationToken: ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<VariableEntry?> GetVariableAsync(
        VariableScope scope,
        string name,
        int? version = null,
        CancellationToken ct = default)
    {
        var key = BuildKey(scope, name);

        if (version is null)
        {
            // Latest version
            return await GetLatestEntryAsync(key, scope, name, ct).ConfigureAwait(false);
        }

        // Specific version — scan history
        var history = await GetAllHistoryDocsAsync(key, ct).ConfigureAwait(false);
        var doc = history.FirstOrDefault(d => d.Version == version.Value);
        return doc is null ? null : MapToEntry(doc, scope, name);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<VariableEntry>> GetVariableHistoryAsync(
        VariableScope scope,
        string name,
        CancellationToken ct = default)
    {
        var key = BuildKey(scope, name);
        var history = await GetAllHistoryDocsAsync(key, ct).ConfigureAwait(false);
        return history.Select(d => MapToEntry(d, scope, name)).ToList();
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteVariableAsync(
        VariableScope scope,
        string name,
        CancellationToken ct = default)
    {
        var key = BuildKey(scope, name);

        // Check if variable exists at all
        var exists = await GetLatestEntryAsync(key, scope, name, ct).ConfigureAwait(false);
        if (exists is null)
        {
            return false;
        }

        // CopilotNote: PurgeAsync removes all revisions and marks as purged tombstone~ 🗑️
        await _store.PurgeAsync(key, cancellationToken: ct).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, object?>> GetAllVariablesAsync(
        VariableScope scope,
        CancellationToken ct = default)
    {
        var prefix = BuildScopePrefix(scope);
        var result = new Dictionary<string, object?>();

        await foreach (var key in _store.GetKeysAsync(cancellationToken: ct).ConfigureAwait(false))
        {
            if (!key.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var varName = ExtractNameFromKey(key, prefix);
            var entry = await GetLatestEntryAsync(key, scope, varName, ct).ConfigureAwait(false);

            if (entry is not null)
            {
                result[varName] = entry.Value;
            }
        }

        return result;
    }

    // ── Key Helpers ───────────────────────────────────────────────────────────

    /// <summary>Builds a KV key from a scope and variable name~ 🔑.</summary>
    private static string BuildKey(VariableScope scope, string name)
    {
        return scope.Kind switch
        {
            VariableScopeKind.Global => $"global.{name}",
            VariableScopeKind.Workflow => $"workflow.{scope.WorkflowId}.{name}",
            VariableScopeKind.Execution => $"execution.{scope.ExecutionId}.{name}",
            _ => throw new ArgumentOutOfRangeException(nameof(scope), $"Unknown scope kind: {scope.Kind}"),
        };
    }

    /// <summary>Builds a prefix to match all keys for the given scope~ 🔑.</summary>
    private static string BuildScopePrefix(VariableScope scope)
    {
        return scope.Kind switch
        {
            VariableScopeKind.Global => "global..",
            VariableScopeKind.Workflow => $"workflow.{scope.WorkflowId}.",
            VariableScopeKind.Execution => $"execution.{scope.ExecutionId}.",
            _ => throw new ArgumentOutOfRangeException(nameof(scope), $"Unknown scope kind: {scope.Kind}"),
        };
    }

    /// <summary>Extracts the variable name from a full KV key given the scope prefix~ 🔑.</summary>
    private static string ExtractNameFromKey(string key, string prefix)
        => key.Length > prefix.Length ? key[prefix.Length..] : key;

    // ── Data Helpers ──────────────────────────────────────────────────────────

    private static bool IsNotFoundOrDeleted(Exception ex)
    {
	    return ex is NatsKVKeyNotFoundException or NatsKVKeyDeletedException;
    }

    private async Task<VariableEntry?> GetLatestEntryAsync(
        string key,
        VariableScope scope,
        string name,
        CancellationToken ct)
    {
        try
        {
            var entry = await _store.TryGetEntryAsync<string>(key, cancellationToken: ct).ConfigureAwait(false);

            if ((entry.Success &&  entry.Value.Operation != NatsKVOperation.Put)
                || entry.Value.Value is null
                || (entry.Success == false && IsNotFoundOrDeleted(entry.Error)))
            {
                return null;
            }

            var doc = NatsJsonHelper.Deserialize<NatsVariableDocument>(entry.Value.Value);
            return doc is null ? null : MapToEntry(doc, scope, name);
        }
        catch (NatsKVKeyNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Loads all non-deleted historical revisions for a key~ 📜.</summary>
    private async Task<List<NatsVariableDocument>> GetAllHistoryDocsAsync(string key, CancellationToken ct)
    {
        var result = new List<NatsVariableDocument>();

        try
        {
            await foreach (var entry in _store.HistoryAsync<string>(key, cancellationToken: ct)
                               .ConfigureAwait(false))
            {
                // CopilotNote: Skip deleted/purged tombstone entries — only include Put operations~ 🗑️
                if (entry.Operation != NatsKVOperation.Put || entry.Value is null)
                {
                    continue;
                }

                var doc = NatsJsonHelper.Deserialize<NatsVariableDocument>(entry.Value);
                if (doc is not null)
                {
                    result.Add(doc);
                }
            }
        }
        catch (NatsKVKeyNotFoundException)
        {
            // Key doesn't exist yet — return empty list~ 🙈
        }

        return result;
    }

    private static VariableEntry MapToEntry(NatsVariableDocument doc, VariableScope scope, string name)
    {
        var createdAt = DateTimeOffset.TryParse(doc.CreatedAt, null,
            System.Globalization.DateTimeStyles.RoundtripKind, out var c) ? c : DateTimeOffset.UtcNow;
        var updatedAt = DateTimeOffset.TryParse(doc.UpdatedAt, null,
            System.Globalization.DateTimeStyles.RoundtripKind, out var u) ? u : DateTimeOffset.UtcNow;

        return new VariableEntry(
            Scope: scope,
            Name: name,
            Value: doc.Value,
            ValueTypeName: doc.ValueTypeName,
            Version: doc.Version,
            CreatedAt: createdAt,
            UpdatedAt: updatedAt);
    }
}


