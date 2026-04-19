// <copyright file="PostgresPersistenceProvider.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Postgres;

using System.Diagnostics;
using Npgsql;
using Workflow.Persistence.Abstractions;
using Workflow.Persistence.Models;
using Workflow.Persistence.Postgres.Data;
using Workflow.Persistence.Postgres.Repositories;

/// <summary>
/// 🐘 PostgreSQL-backed persistence provider for production use~ ✨💖
/// </summary>
/// <remarks>
/// CopilotNote: Requires PostgreSQL 15+. Uses Npgsql for connections and linq2db for queries.
/// Run <see cref="InitializeAsync"/> once at startup — it applies FluentMigrator migrations.
/// For tests use <c>Testcontainers.PostgreSql</c> to spin up a real Postgres instance~ 🐳
/// </remarks>
public sealed class PostgresPersistenceProvider : IPersistenceProvider
{
    private readonly WorkflowDataConnectionFactory _factory;
    private bool _initialized;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresPersistenceProvider"/> class~ 🔌.
    /// </summary>
    /// <param name="connectionString">The Npgsql connection string.</param>
    public PostgresPersistenceProvider(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ConnectionString = connectionString;
        _factory = new WorkflowDataConnectionFactory(connectionString);

        Workflows = new PostgresWorkflowRepository(_factory);
        ExecutionHistory = new PostgresExecutionHistoryRepository(_factory);
        Variables = new PostgresVariableStore(_factory);
    }

    /// <inheritdoc/>
    public string ProviderName => "postgres";

    /// <inheritdoc/>
    public bool IsInitialized => _initialized;

    /// <inheritdoc/>
    public IWorkflowRepository Workflows { get; }

    /// <inheritdoc/>
    public IExecutionHistoryRepository ExecutionHistory { get; }

    /// <inheritdoc/>
    public IVariableStore Variables { get; }

    /// <inheritdoc/>
    public IBlobStore? Blobs => null;

    /// <summary>Gets the connection string used by this provider~ 🔗.</summary>
    public string ConnectionString { get; }

    /// <inheritdoc/>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await PostgresMigrationRunner.RunMigrationsAsync(ConnectionString, ct).ConfigureAwait(false);
        _initialized = true;
    }

    /// <inheritdoc/>
    public async Task<HealthCheckResult> HealthCheckAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await using var conn = new NpgsqlConnection(ConnectionString);
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
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

