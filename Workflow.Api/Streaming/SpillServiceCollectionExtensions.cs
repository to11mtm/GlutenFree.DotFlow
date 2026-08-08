// <copyright file="SpillServiceCollectionExtensions.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.Streaming;

using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Workflow.Modules.Streaming;
using Workflow.Persistence.Abstractions;

/// <summary>
/// 💉 Host wiring for streaming spill (Phase 5.1.6).
/// </summary>
public static class SpillServiceCollectionExtensions
{
    /// <summary>
    /// Registers spill options, the configured spill store (if any), and the spill protector~ 💾.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The host configuration.</param>
    /// <returns>The same service collection, for chaining.</returns>
    /// <remarks>
    /// CopilotNote: <see cref="ISpillStore"/> is registered <b>only</b> when a store is configured.
    /// Its absence is the mechanism by which <c>spill: true</c> fails loudly — resolving a missing
    /// service is a clearer signal than a store that silently writes somewhere unexpected~ 🚧
    /// </remarks>
    public static IServiceCollection AddStreamingSpill(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<SpillOptions>(configuration.GetSection(SpillOptions.SectionName));

        var options = new SpillOptions();
        configuration.GetSection(SpillOptions.SectionName).Bind(options);

        // 🔒 Q-D: encryption is on unless a host explicitly turns it off.
        if (options.Encrypt)
        {
            services.AddSingleton<ISpillProtector, DataProtectionSpillProtector>();
        }
        else
        {
            services.AddSingleton<ISpillProtector>(NoOpSpillProtector.Instance);
        }

        switch (options.Store)
        {
            case SpillStoreKind.Sqlite:
                if (string.IsNullOrWhiteSpace(options.SqlitePath))
                {
                    throw new InvalidOperationException(
                        $"{SpillOptions.SectionName}:Store is 'Sqlite' but {SpillOptions.SectionName}:SqlitePath is not set. " +
                        "Point it at a file on a volume you've provisioned for scratch~ 🗄️");
                }

                services.AddSingleton<ISpillStore>(_ => new SqliteSpillStore(options.SqlitePath));
                break;

            case SpillStoreKind.Blob:
                services.AddSingleton<ISpillStore>(sp =>
                {
                    var blobs = sp.GetService<IBlobStore>()
                        ?? throw new InvalidOperationException(
                            $"{SpillOptions.SectionName}:Store is 'Blob' but no IBlobStore is registered. " +
                            "Configure a persistence provider with blob support (Postgres, NATS or S3)~ ☁️");

                    return new BlobStoreSpillStore(blobs);
                });
                break;

            case SpillStoreKind.Directory:
                if (string.IsNullOrWhiteSpace(options.DirectoryPath))
                {
                    throw new InvalidOperationException(
                        $"{SpillOptions.SectionName}:Store is 'Directory' but {SpillOptions.SectionName}:DirectoryPath is not set~ 📁");
                }

                services.AddSingleton<ISpillStore>(_ => new DirectorySpillStore(options.DirectoryPath));
                break;

            case SpillStoreKind.None:
            default:
                // Nothing registered — see the remark above.
                break;
        }

        if (options.Store != SpillStoreKind.None)
        {
            services.AddHostedService<SpillCleanupHostedService>();
        }

        return services;
    }

    /// <summary>
    /// Emits the startup diagnostics required by Q-D and Q-C1~ 📢.
    /// </summary>
    /// <param name="services">The built service provider.</param>
    /// <remarks>
    /// CopilotNote: Called once from <c>Program.cs</c> after the container is built. Disabling spill
    /// encryption is legitimate, but it must never be a silent choice — this is the first of the
    /// three required warnings (startup / per-execution / designer)~ 📢
    /// </remarks>
    public static void LogSpillConfiguration(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = services.GetRequiredService<IOptions<SpillOptions>>().Value;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Workflow.Streaming.Spill");

        if (options.Store == SpillStoreKind.None)
        {
            logger.LogInformation(
                "💾 Streaming spill is not configured. Nodes with spill enabled will fail with a configuration error — set {Setting} to Sqlite, Blob or Directory to enable it~",
                $"{SpillOptions.SectionName}:Store");
            return;
        }

        logger.LogInformation(
            "💾 Streaming spill enabled using the '{Store}' store (memory budget {Threshold} items, retention {Retention} after completion, abandoned after {Abandoned})~",
            options.Store,
            options.MemoryItemThreshold,
            options.RetentionAfterCompletion,
            options.AbandonedThreshold);

        if (!options.Encrypt)
        {
            logger.LogWarning(
                "⚠️ Spill encryption is DISABLED via {Setting}=false. Workflow data buffered by streaming stages will be written to '{Store}' in the clear — including anything sensitive the stream carries~",
                $"{SpillOptions.SectionName}:Encrypt",
                options.Store);
        }

        if (options.Store == SpillStoreKind.Directory)
        {
            logger.LogWarning(
                "📁 Spill is using the directory store, which assumes local disk is fast. On cloud nodes with throttled local IOPS prefer {Setting}=Blob so scratch leaves the node~",
                $"{SpillOptions.SectionName}:Store");
        }
    }
}
