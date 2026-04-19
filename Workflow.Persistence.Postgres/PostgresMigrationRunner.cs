// <copyright file="PostgresMigrationRunner.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Postgres;

using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Persistence.Postgres.Migrations;

/// <summary>
/// 🔄 Runs FluentMigrator migrations against a PostgreSQL database~ ✨💖
/// </summary>
public static class PostgresMigrationRunner
{
    /// <summary>
    /// Runs all pending migrations against the given connection string~ 🚀.
    /// </summary>
    /// <param name="connectionString">The Postgres connection string.</param>
    /// <param name="ct">Cancellation token.</param>
    public static Task RunMigrationsAsync(string connectionString, CancellationToken ct = default)
    {
        using var serviceProvider = BuildMigratorServices(connectionString);
        var runner = serviceProvider.GetRequiredService<IMigrationRunner>();
        runner.MigrateUp();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Rolls back the last applied migration~ ⏪.
    /// </summary>
    /// <param name="connectionString">The Postgres connection string.</param>
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
                .AddPostgres()
                .WithGlobalConnectionString(connectionString)
                .ScanIn(typeof(Migration_001_InitialSchema).Assembly).For.Migrations())
            .AddLogging(lb => lb.AddFluentMigratorConsole())
            .BuildServiceProvider();
    }
}

