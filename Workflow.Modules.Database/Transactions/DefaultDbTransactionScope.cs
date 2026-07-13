// <copyright file="DefaultDbTransactionScope.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Transactions;

using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using LinqToDB.Data;
using Workflow.Modules.Database.Abstractions;

/// <summary>
/// 💼 Default transaction scope — wraps <c>DataConnection.BeginTransactionAsync</c>
/// with auto-rollback-on-dispose semantics~ 🛡️.
/// </summary>
/// <remarks>
/// <para>
/// State machine: <c>Open → (Commit | Rollback | Dispose→Rollback)</c>. Commit and rollback are
/// terminal — a second call throws <see cref="InvalidOperationException"/>. Dispose after either
/// is a safe no-op (it only disposes the connection)~ ✨.
/// </para>
/// <para>
/// CopilotNote: Phase 2.4.a.0 — the scope OWNS the connection: <see cref="DisposeAsync"/>
/// disposes it after rolling back any uncommitted work. Created via
/// <see cref="DbConnectionFactoryTransactionExtensions.CreateTransactionAsync"/>. Savepoints → 2.4.a.P2~ 🌸.
/// </para>
/// </remarks>
public sealed class DefaultDbTransactionScope : IDbTransactionScope
{
    private bool completed;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultDbTransactionScope"/> class.
    /// Use <see cref="CreateAsync"/> instead — the transaction must be begun asynchronously~ ⚙️.
    /// </summary>
    /// <param name="connection">The connection (owned by this scope).</param>
    private DefaultDbTransactionScope(DataConnection connection)
    {
        this.Connection = connection;
    }

    /// <inheritdoc/>
    public DataConnection Connection { get; }

    /// <summary>
    /// Opens a transaction scope over an owned connection at the given isolation level~ 💼.
    /// </summary>
    /// <param name="connection">The connection to own (disposed with the scope).</param>
    /// <param name="isolationLevel">The transaction isolation level.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An open transaction scope.</returns>
    public static async Task<DefaultDbTransactionScope> CreateAsync(
        DataConnection connection,
        IsolationLevel isolationLevel,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var scope = new DefaultDbTransactionScope(connection);
        await connection.BeginTransactionAsync(isolationLevel, ct).ConfigureAwait(false);
        return scope;
    }

    /// <inheritdoc/>
    public Task<T> RunAsync<T>(Func<DataConnection, CancellationToken, Task<T>> body, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(body);
        this.ThrowIfCompleted();

        return body(this.Connection, ct);
    }

    /// <inheritdoc/>
    public async Task CommitAsync(CancellationToken ct = default)
    {
        this.ThrowIfCompleted();

        await this.Connection.CommitTransactionAsync(ct).ConfigureAwait(false);
        this.completed = true;
    }

    /// <inheritdoc/>
    public async Task RollbackAsync(CancellationToken ct = default)
    {
        this.ThrowIfCompleted();

        await this.Connection.RollbackTransactionAsync(ct).ConfigureAwait(false);
        this.completed = true;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;

        try
        {
            // Auto-rollback anything uncommitted — no half-finished transactions, ever~ 🛡️
            if (!this.completed)
            {
                await this.Connection.RollbackTransactionAsync().ConfigureAwait(false);
                this.completed = true;
            }
        }
        finally
        {
            await this.Connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Guards terminal-state misuse (double commit/rollback, work after completion). 🚧.
    /// </summary>
    private void ThrowIfCompleted()
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);

        if (this.completed)
        {
            throw new InvalidOperationException(
                "This transaction scope has already been committed or rolled back — create a new scope for further work~");
        }
    }
}

/// <summary>
/// 💼 Factory extension for opening transaction scopes from <see cref="IDbConnectionFactory"/>~ ✨.
/// </summary>
public static class DbConnectionFactoryTransactionExtensions
{
    /// <summary>
    /// Creates a connection for a named registration and opens a transaction scope over it~ 💼
    /// The scope owns the connection — one <c>await using</c> cleans up everything.
    /// </summary>
    /// <param name="factory">The connection factory.</param>
    /// <param name="connectionId">The registered connection id (case-insensitive).</param>
    /// <param name="isolationLevel">The transaction isolation level.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An open transaction scope (caller must dispose).</returns>
    public static async Task<IDbTransactionScope> CreateTransactionAsync(
        this IDbConnectionFactory factory,
        string connectionId,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(factory);

        var connection = await factory.CreateAsync(connectionId, ct).ConfigureAwait(false);
        try
        {
            return await DefaultDbTransactionScope.CreateAsync(connection, isolationLevel, ct).ConfigureAwait(false);
        }
        catch
        {
            // BeginTransaction failed — don't leak the connection~ 🧹
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Creates a connection from a raw (provider, connection string) pair and opens a transaction scope over it~ 🔓.
    /// </summary>
    /// <param name="factory">The connection factory.</param>
    /// <param name="providerKey">The provider key ("postgres" / "sqlite").</param>
    /// <param name="connectionString">The raw connection string.</param>
    /// <param name="isolationLevel">The transaction isolation level.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An open transaction scope (caller must dispose).</returns>
    public static async Task<IDbTransactionScope> CreateTransactionAsync(
        this IDbConnectionFactory factory,
        string providerKey,
        string connectionString,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(factory);

        var connection = await factory.CreateAsync(providerKey, connectionString, ct).ConfigureAwait(false);
        try
        {
            return await DefaultDbTransactionScope.CreateAsync(connection, isolationLevel, ct).ConfigureAwait(false);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}

