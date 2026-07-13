// <copyright file="IDbTransactionScope.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Abstractions;

using System;
using System.Threading;
using System.Threading.Tasks;
using LinqToDB.Data;

/// <summary>
/// 💼 A transaction scope over a linq2db <see cref="DataConnection"/>~ 🛡️
/// Auto-rolls-back on dispose when not committed — no half-finished transactions, ever!.
/// </summary>
/// <remarks>
/// <para>
/// Usage pattern:
/// <code>
/// await using var scope = await factory.CreateTransactionAsync("OrdersDb", IsolationLevel.ReadCommitted, ct);
/// // ... execute N operations against scope.Connection ...
/// await scope.CommitAsync(ct); // skip on failure → dispose rolls back~
/// </code>
/// </para>
/// <para>
/// CopilotNote: Phase 2.4.a.0 — consumed by <c>DatabaseTransactionModule</c> (2.4.a.3).
/// The scope owns BOTH the transaction and the underlying connection: disposing the scope
/// disposes the connection too. Savepoint support is deferred to 2.4.a.P2~ 🌸.
/// </para>
/// </remarks>
public interface IDbTransactionScope : IAsyncDisposable
{
    /// <summary>
    /// Gets the connection this transaction is bound to. 🔌
    /// All operations inside the scope must run through this connection.
    /// </summary>
    public DataConnection Connection { get; }

    /// <summary>
    /// Runs a body delegate against the transaction's connection~ 🏃
    /// Convenience wrapper — equivalent to calling <paramref name="body"/> with <see cref="Connection"/>.
    /// </summary>
    /// <typeparam name="T">The body's result type.</typeparam>
    /// <param name="body">The work to run inside the transaction.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The body's result.</returns>
    public Task<T> RunAsync<T>(Func<DataConnection, CancellationToken, Task<T>> body, CancellationToken ct = default);

    /// <summary>
    /// Commits the transaction. ✅ After commit, dispose is a no-op (no rollback).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the async commit.</returns>
    public Task CommitAsync(CancellationToken ct = default);

    /// <summary>
    /// Rolls the transaction back explicitly. ↩️ Dispose after rollback is a no-op.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the async rollback.</returns>
    public Task RollbackAsync(CancellationToken ct = default);
}

