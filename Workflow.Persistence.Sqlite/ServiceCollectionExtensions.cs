// <copyright file="ServiceCollectionExtensions.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Sqlite;

using Microsoft.Extensions.DependencyInjection;
using Workflow.Persistence.Abstractions;

/// <summary>
/// 💉 DI registration helpers for the SQLite persistence provider~ ✨💖
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the SQLite persistence provider with a file-based connection string~ 🪶.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">SQLite connection string (e.g. <c>"Data Source=workflow.db"</c>).</param>
    /// <returns>The same <paramref name="services"/> for chaining~ 🔗.</returns>
    public static IServiceCollection AddSqlitePersistence(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var provider = new SqlitePersistenceProvider(connectionString);
        services.AddSingleton<IPersistenceProvider>(provider);
        services.AddSingleton(provider);
        services.AddSingleton(provider.Workflows);
        services.AddSingleton(provider.ExecutionHistory);
        services.AddSingleton(provider.Variables);
        if (provider.Blobs is not null)
        {
            services.AddSingleton(provider.Blobs);
        }

        return services;
    }

    /// <summary>
    /// Registers the SQLite persistence provider using an in-memory database.
    /// Perfect for unit tests and integration tests — no files, no Docker~ 🧪.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="databaseName">
    /// Optional shared memory database name. Defaults to <c>"workflow_test"</c>.
    /// Use different names to isolate test databases from each other~ 🔒.
    /// </param>
    /// <returns>The same <paramref name="services"/> for chaining~ 🔗.</returns>
    public static IServiceCollection AddSqlitePersistenceInMemory(
        this IServiceCollection services,
        string databaseName = "workflow_test")
    {
        // CopilotNote: Cache=Shared;Mode=Memory allows multiple connections to see the same DB~ 🧠
        var connectionString = $"Data Source={databaseName};Cache=Shared;Mode=Memory";
        return services.AddSqlitePersistence(connectionString);
    }
}

