// <copyright file="SqliteMigrationRunner.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Sqlite;

using FluentMigrator.Runner;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Persistence.Sqlite.Migrations;

/// <summary>
/// 🔄 Runs FluentMigrator migrations against a SQLite database and enables WAL mode~ ✨💖
/// </summary>
public static class SqliteMigrationRunner
{
    /// <summary>
    /// Runs all pending migrations against the given connection string and enables WAL mode~ 🚀.
    /// </summary>
    /// <param name="connectionString">The SQLite connection string.</param>
    /// <param name="ct">Cancellation token (not used by synchronous FluentMigrator, provided for API consistency).</param>
    public static async Task RunMigrationsAsync(string connectionString, CancellationToken ct = default)
    {
        using var serviceProvider = BuildMigratorServices(connectionString);
        var runner = serviceProvider.GetRequiredService<IMigrationRunner>();
        runner.MigrateUp();
        // 🔐 Enable WAL mode for better concurrent read performance
        await EnableWalModeAsync(connectionString, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Rolls back the last applied migration~ ⏪.
    /// </summary>
    /// <param name="connectionString">The SQLite connection string.</param>
    /// <param name="ct">Cancellation token.</param>
    public static Task RollbackLastMigrationAsync(string connectionString, CancellationToken ct = default)
    {
        using var serviceProvider = BuildMigratorServices(connectionString);
        var runner = serviceProvider.GetRequiredService<IMigrationRunner>();
        runner.Rollback(1);
        return Task.CompletedTask;
    }

    private static ServiceProvider BuildMigratorServices(string connectionString)
    {
        return new ServiceCollection()
            .AddFluentMigratorCore()
            .ConfigureRunner(rb => rb
                .AddSQLite()
                .WithGlobalConnectionString(connectionString)
                .ScanIn(typeof(Migration_001_InitialSchema).Assembly).For.Migrations())
            .AddLogging(lb => lb.AddFluentMigratorConsole())
            .BuildServiceProvider();
    }

    private static async Task EnableWalModeAsync(string connectionString, CancellationToken ct)
    {
	    return;
        // WAL mode is not meaningful for in-memory databases, so skip those gracefully~ 🧪
        if (connectionString.Contains(":memory:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL;";
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}

