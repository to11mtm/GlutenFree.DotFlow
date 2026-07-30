// <copyright file="AmbientDbTransactions.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Transactions;

using System;
using System.Collections.Concurrent;
using Workflow.Modules.Abstractions;

/// <summary>
/// 💼 Default in-memory ambient transaction registry keyed by execution + connection id~ ✨
/// </summary>
public sealed class AmbientDbTransactions : IAmbientDbTransactions
{
    private readonly ConcurrentDictionary<Key, object> connections = new();

    /// <inheritdoc/>
    public object? TryGet(Guid executionId, string connectionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        return this.connections.TryGetValue(new Key(executionId, Normalize(connectionId)), out var connection) ? connection : null;
    }

    /// <inheritdoc/>
    public void Register(Guid executionId, string connectionId, object connection)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentNullException.ThrowIfNull(connection);
        this.connections[new Key(executionId, Normalize(connectionId))] = connection;
    }

    /// <inheritdoc/>
    public void Remove(Guid executionId, string connectionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        this.connections.TryRemove(new Key(executionId, Normalize(connectionId)), out _);
    }

    private static string Normalize(string connectionId) => connectionId.ToUpperInvariant();

    private readonly record struct Key(Guid ExecutionId, string ConnectionId);
}
