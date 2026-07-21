// <copyright file="ServiceCollectionExtensions.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Nats;

using Microsoft.Extensions.DependencyInjection;
using Workflow.Persistence.Abstractions;

/// <summary>
/// 💉 DI registration helpers for the NATS persistence provider~ ✨💖
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the NATS persistence provider with the given NATS server URL~ 🚀.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="natsUrl">
    /// NATS server URL, e.g. <c>nats://localhost:4222</c> or <c>nats://user:pass@host:4222</c>~ 🔗.
    /// </param>
    /// <returns>The same <paramref name="services"/> for chaining~ 🔗.</returns>
    /// <remarks>
    /// CopilotNote: The provider must be initialised before use. Call
    /// <c>provider.InitializeAsync()</c> during application startup (e.g. in Program.cs after
    /// building the host) to create the KV buckets~ 🌸
    /// </remarks>
    public static IServiceCollection AddNatsPersistence(
        this IServiceCollection services,
        string natsUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(natsUrl);

        var provider = new NatsPersistenceProvider(natsUrl);
        services.AddSingleton<IPersistenceProvider>(provider);
        services.AddSingleton(provider);

        // CopilotNote: Repositories are assigned during InitializeAsync, so we register
        // factories that resolve through the provider at runtime~ 🏭
        services.AddSingleton(sp => sp.GetRequiredService<NatsPersistenceProvider>().Workflows);
        services.AddSingleton(sp => sp.GetRequiredService<NatsPersistenceProvider>().ExecutionHistory);
        services.AddSingleton(sp => sp.GetRequiredService<NatsPersistenceProvider>().Variables);

        return services;
    }
}

