// <copyright file="ServiceCollectionExtensions.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.S3;

using Microsoft.Extensions.DependencyInjection;
using Workflow.Persistence.Abstractions;

/// <summary>
///  DI registration helpers for the S3 blob store~ ✨
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the S3 blob store + provider with the given configuration~ ☁️.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="config">The S3 configuration.</param>
    /// <returns>The same <paramref name="services"/> for chaining~ .</returns>
    /// <remarks>
    /// CopilotNote: The provider must be initialised before use. Call
    /// <c>provider.InitializeAsync()</c> during application startup to verify the bucket
    /// exists / create it~
    /// </remarks>
    public static IServiceCollection AddS3BlobStore(
        this IServiceCollection services,
        S3Configuration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();

        var provider = new S3PersistenceProvider(config);
        services.AddSingleton(config);
        services.AddSingleton(provider);
        services.AddSingleton<IPersistenceProvider>(provider);

        // CopilotNote: The blob store is set during InitializeAsync — register a factory
        // so consumers always resolve the live store~
        services.AddSingleton<IBlobStore>(sp =>
            sp.GetRequiredService<S3PersistenceProvider>().Blobs
                ?? throw new InvalidOperationException(
                    "S3PersistenceProvider has not been initialised — call InitializeAsync() first~ "));

        return services;
    }
}
