// <copyright file="ServiceCollectionExtensions.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Postgres;

using Microsoft.Extensions.DependencyInjection;
using Workflow.Persistence.Abstractions;

/// <summary>
/// 💉 DI registration helpers for the PostgreSQL persistence provider~ ✨💖
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the PostgreSQL persistence provider~ 🐘.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The Npgsql connection string.</param>
    /// <returns>The same <paramref name="services"/> for chaining~ 🔗.</returns>
    public static IServiceCollection AddPostgresPersistence(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var provider = new PostgresPersistenceProvider(connectionString);
        services.AddSingleton<IPersistenceProvider>(provider);
        services.AddSingleton(provider);
        services.AddSingleton(provider.Workflows);
        services.AddSingleton(provider.ExecutionHistory);
        services.AddSingleton(provider.Variables);
        return services;
    }
}

