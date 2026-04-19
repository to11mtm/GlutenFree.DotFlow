// <copyright file="ServiceCollectionExtensions.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence;

using Microsoft.Extensions.DependencyInjection;
using Workflow.Persistence.Abstractions;
using Workflow.Persistence.Composite;

/// <summary>
/// 💉 DI extension methods for registering the persistence layer~ ✨💖
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers a single persistence provider and its repositories into the DI container~ 🔌.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="provider">The pre-configured persistence provider instance.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddWorkflowPersistence(
        this IServiceCollection services,
        IPersistenceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        services.AddSingleton(provider);
        services.AddSingleton(provider.Workflows);
        services.AddSingleton(provider.ExecutionHistory);
        services.AddSingleton(provider.Variables);

        if (provider.Blobs != null)
        {
            services.AddSingleton(provider.Blobs);
        }

        return services;
    }

    /// <summary>
    /// Registers a composite persistence provider from a factory and config.
    /// Requires <see cref="IPersistenceProviderFactory"/> instances to be available~ 🔀.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="config">The composite persistence configuration.</param>
    /// <param name="factory">The provider factory to create sub-providers.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddWorkflowPersistence(
        this IServiceCollection services,
        CompositePersistenceConfiguration config,
        IPersistenceProviderFactory factory)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(factory);

        var workflowsProvider = factory.Create(config.WorkflowsProvider);
        var execProvider = factory.Create(config.EffectiveExecutionHistoryProvider);
        var varsProvider = factory.Create(config.EffectiveVariablesProvider);
        var blobsProvider = config.BlobsProvider != null ? factory.Create(config.EffectiveBlobsProvider) : null;

        var composite = new CompositePersistenceProvider(workflowsProvider, execProvider, varsProvider, blobsProvider);
        return services.AddWorkflowPersistence(composite);
    }
}

