// <copyright file="IWorkflowTransactionScopeFactory.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Abstractions;

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// 💼 Engine-facing seam for opening database transactions without referencing database modules~ ✨
/// </summary>
public interface IWorkflowTransactionScopeFactory
{
    /// <summary>Opens a transaction for a named connection id~ 📇.</summary>
    public Task<IWorkflowTransactionScope> OpenAsync(
        string connectionId,
        string? isolationLevel,
        int? timeoutSeconds,
        CancellationToken cancellationToken = default);

    /// <summary>Opens a transaction for a raw provider + connection string pair~ 🔓.</summary>
    public Task<IWorkflowTransactionScope> OpenAsync(
        string provider,
        string connectionString,
        string? isolationLevel,
        int? timeoutSeconds,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 💼 Engine-facing transaction handle with no database-provider types exposed~ 🛡️
/// </summary>
public interface IWorkflowTransactionScope : IAsyncDisposable
{
    /// <summary>Gets the underlying database connection object for ambient registration~ 🔌.</summary>
    public object DataConnection { get; }

    /// <summary>Commits the transaction~ ✅.</summary>
    public Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>Rolls the transaction back~ ↩️.</summary>
    public Task RollbackAsync(CancellationToken cancellationToken = default);
}
