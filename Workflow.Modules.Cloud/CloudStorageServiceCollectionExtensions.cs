// <copyright file="CloudStorageServiceCollectionExtensions.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Cloud;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Cloud.Abstractions;
using Workflow.Modules.Cloud.Builtin;
using Workflow.Modules.Cloud.Configuration;
using Workflow.Modules.Cloud.Connections;

/// <summary>
/// ☁️ DI registration for the cloud-storage module family~ ✨💖.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: Phase 2.5.b.0. Wired by the host (not <c>AddWorkflowModules()</c>) because
/// <c>Workflow.Modules.Cloud</c> references <c>Workflow.Modules</c> — the reverse call would be
/// circular, exactly like <c>AddDatabaseModules()</c>. Keeps the AWS/Azure SDK weight out of
/// SDK-free deployments (D4)~ 🌸.
/// </para>
/// </remarks>
public static class CloudStorageServiceCollectionExtensions
{
    /// <summary>
    /// Registers the cloud-storage registry, client factory, and built-in modules~ ☁️✨.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <returns>The same <paramref name="services"/> for chaining~ 💖.</returns>
    public static IServiceCollection AddCloudStorageModules(this IServiceCollection services)
    {
        services.AddOptions<CloudStorageOptions>();

        services.TryAddSingleton<IStorageConnectionRegistry, InMemoryStorageConnectionRegistry>();
        services.TryAddSingleton<IStorageClientFactory, DefaultStorageClientFactory>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorkflowModule, S3Module>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorkflowModule, AzureBlobModule>());

        return services;
    }
}
