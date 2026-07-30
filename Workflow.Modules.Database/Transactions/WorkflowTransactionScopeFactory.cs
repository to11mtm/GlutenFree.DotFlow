// <copyright file="WorkflowTransactionScopeFactory.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Transactions;

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using LinqToDB.Data;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Database.Abstractions;

/// <summary>
/// 💼 Opens database transaction scopes for the engine-facing structural transaction seam~ ✨
/// </summary>
public sealed class WorkflowTransactionScopeFactory : IWorkflowTransactionScopeFactory
{
    private static readonly IReadOnlyDictionary<string, IsolationLevel> IsolationLevels =
        new Dictionary<string, IsolationLevel>(StringComparer.OrdinalIgnoreCase)
        {
            ["ReadUncommitted"] = IsolationLevel.ReadUncommitted,
            ["ReadCommitted"] = IsolationLevel.ReadCommitted,
            ["RepeatableRead"] = IsolationLevel.RepeatableRead,
            ["Serializable"] = IsolationLevel.Serializable,
            ["Snapshot"] = IsolationLevel.Snapshot,
        };

    private readonly IDbConnectionFactory connectionFactory;

    /// <summary>Initializes a new instance of the <see cref="WorkflowTransactionScopeFactory"/> class.</summary>
    public WorkflowTransactionScopeFactory(IDbConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    /// <inheritdoc/>
    public async Task<IWorkflowTransactionScope> OpenAsync(
        string connectionId,
        string? isolationLevel,
        int? timeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        var connection = await this.connectionFactory.CreateAsync(connectionId, cancellationToken).ConfigureAwait(false);
        return await CreateScopeAsync(connection, isolationLevel, timeoutSeconds, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IWorkflowTransactionScope> OpenAsync(
        string provider,
        string connectionString,
        string? isolationLevel,
        int? timeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        var connection = await this.connectionFactory.CreateAsync(provider, connectionString, cancellationToken).ConfigureAwait(false);
        return await CreateScopeAsync(connection, isolationLevel, timeoutSeconds, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IWorkflowTransactionScope> CreateScopeAsync(
        DataConnection connection,
        string? isolationLevel,
        int? timeoutSeconds,
        CancellationToken cancellationToken)
    {
        try
        {
            ApplyTimeout(connection, timeoutSeconds);
            var isolation = ClampIsolationForProvider(connection.DataProvider.Name, ResolveIsolation(isolationLevel));
            var scope = await DefaultDbTransactionScope.CreateAsync(connection, isolation, cancellationToken).ConfigureAwait(false);
            return new WorkflowTransactionScopeAdapter(scope);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static void ApplyTimeout(DataConnection connection, int? timeoutSeconds)
    {
        if (timeoutSeconds is > 0)
        {
            connection.CommandTimeout = timeoutSeconds.Value;
        }
    }

    private static IsolationLevel ResolveIsolation(string? isolation)
        => isolation is not null && IsolationLevels.TryGetValue(isolation, out var level)
            ? level
            : IsolationLevel.ReadCommitted;

    private static IsolationLevel ClampIsolationForProvider(string providerName, IsolationLevel requested)
    {
        if (providerName.Contains("SQLite", StringComparison.OrdinalIgnoreCase))
        {
            return requested == IsolationLevel.ReadUncommitted
                ? IsolationLevel.ReadUncommitted
                : IsolationLevel.Serializable;
        }

        return requested == IsolationLevel.Snapshot ? IsolationLevel.Serializable : requested;
    }

    private sealed class WorkflowTransactionScopeAdapter : IWorkflowTransactionScope
    {
        private readonly IDbTransactionScope inner;

        public WorkflowTransactionScopeAdapter(IDbTransactionScope inner)
        {
            this.inner = inner;
        }

        /// <inheritdoc/>
        public object DataConnection => this.inner.Connection;

        /// <inheritdoc/>
        public Task CommitAsync(CancellationToken cancellationToken = default)
            => this.inner.CommitAsync(cancellationToken);

        /// <inheritdoc/>
        public Task RollbackAsync(CancellationToken cancellationToken = default)
            => this.inner.RollbackAsync(cancellationToken);

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
            => this.inner.DisposeAsync();
    }
}
