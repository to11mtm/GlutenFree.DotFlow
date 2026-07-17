// <copyright file="SqlitePersistenceProvider.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Sqlite;

using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Workflow.Modules.Database.Abstractions;
using Workflow.Persistence.Abstractions;
using Workflow.Persistence.Models;
using Workflow.Persistence.Sqlite.Data;
using Workflow.Persistence.Sqlite.Repositories;

/// <summary>
///  SQLite-backed persistence provider for local dev and testing~ ✨
/// </summary>
/// <remarks>
/// CopilotNote: For in-memory test databases use the <c>AddSqlitePersistenceInMemory()</c> DI helper.
/// For file-based dev use <c>AddSqlitePersistence(connectionString)</c>.
/// Blobs are supported via <see cref="SqliteBlobStore"/> stored in the <c>blob_store</c> table~ ️
/// </remarks>
public sealed class SqlitePersistenceProvider : IPersistenceProvider
{
    private readonly WorkflowDataConnectionFactory _factory;

    // CopilotNote: For SQLite in-memory databases (Cache=Shared;Mode=Memory), the database
    // is tied to the lifetime of open connections sharing the same name. We keep one connection
    // alive for the full provider lifetime so the database doesn't vanish between calls~
    private SqliteConnection? _keepAliveConnection;

    private bool _initialized;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlitePersistenceProvider"/> class~ .
    /// </summary>
    /// <param name="connectionString">The SQLite connection string.</param>
    public SqlitePersistenceProvider(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        WorkflowDataConnectionFactory.EnsureProviderRegistered();
        _factory = new WorkflowDataConnectionFactory(connectionString);
        ConnectionString = connectionString;

        Workflows = new SqliteWorkflowRepository(_factory);
        ExecutionHistory = new SqliteExecutionHistoryRepository(_factory);
        Variables = new SqliteVariableStore(_factory);
        Blobs = new SqliteBlobStore(_factory);
        Webhooks = new SqliteWebhookRegistrationRepository(_factory);
    }

    /// <inheritdoc/>
    public string ProviderName => "sqlite";

    /// <inheritdoc/>
    public bool IsInitialized => _initialized;

    /// <inheritdoc/>
    public IWorkflowRepository Workflows { get; }

    /// <inheritdoc/>
    public IExecutionHistoryRepository ExecutionHistory { get; }

    /// <inheritdoc/>
    public IVariableStore Variables { get; }

    /// <inheritdoc/>
    public IBlobStore? Blobs { get; }

    /// <inheritdoc/>
    /// <remarks>Phase 2.3.9 — backed by <see cref="SqliteWebhookRegistrationRepository"/>~ ✨.</remarks>
    public IWebhookRegistrationRepository? Webhooks { get; }

    /// <summary>Gets the connection string used by this provider~ .</summary>
    public string ConnectionString { get; }

    /// <summary>
    /// Creates a SQLite-backed <see cref="IDbConnectionRegistry"/> sharing this provider's
    /// database (Phase 2.4.a.5)~ 📇.
    /// </summary>
    /// <param name="protector">Connection-string protector for at-rest encryption.</param>
    /// <returns>A persisted connection registry over the same SQLite database.</returns>
    /// <remarks>
    /// CopilotNote: Exposed as a factory method (rather than an <see cref="IPersistenceProvider"/>
    /// member) to keep the persistence abstraction free of a Workflow.Modules.Database dependency.
    /// The <c>db_connections</c> table is created by Migration_006 during <see cref="InitializeAsync"/>~ 🌸.
    /// </remarks>
    public IDbConnectionRegistry CreateDbConnectionRegistry(IConnectionStringProtector protector)
        => new SqliteDbConnectionRegistry(_factory, protector);

    /// <inheritdoc/>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        // Open a keep-alive connection for in-memory DBs before running migrations~
        if (ConnectionString.Contains(":memory:", StringComparison.OrdinalIgnoreCase)
            || ConnectionString.Contains("Mode=Memory", StringComparison.OrdinalIgnoreCase))
        {
            _keepAliveConnection = new SqliteConnection(ConnectionString);
            await _keepAliveConnection.OpenAsync(ct).ConfigureAwait(false);
        }

        await SqliteMigrationRunner.RunMigrationsAsync(ConnectionString, ct).ConfigureAwait(false);
        _initialized = true;
    }

    /// <inheritdoc/>
    public async Task<HealthCheckResult> HealthCheckAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await using var conn = new SqliteConnection(ConnectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1;";
            await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            sw.Stop();

            return new HealthCheckResult(true, ProviderName, sw.Elapsed);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new HealthCheckResult(false, ProviderName, sw.Elapsed, ex.Message);
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_keepAliveConnection is not null)
        {
            await _keepAliveConnection.DisposeAsync().ConfigureAwait(false);
        }
    }
}


