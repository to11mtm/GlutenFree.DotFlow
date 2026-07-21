// <copyright file="RealTimeServiceCollectionExtensions.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.RealTime;

using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// 📡 Phase 3.2 — Registers the real-time hub surface: SignalR, the connection tracker, the
/// execution-event bridge (hosted service), and the CORS policy for browser clients~ ✨.
/// </summary>
public static class RealTimeServiceCollectionExtensions
{
    /// <summary>The hub route~ 🛣️.</summary>
    public const string HubPath = "/hubs/workflow";

    /// <summary>The CORS policy name applied to the hub endpoint~ 🌐.</summary>
    public const string CorsPolicy = "dotflow.realtime";

    /// <summary>Adds the Phase 3.2 real-time services~ 📡.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The app configuration (reads <c>Api:RealTime:AllowedOrigins</c>).</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddRealTime(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSignalR();
        services.AddSingleton<IConnectionTracker, ConnectionTracker>();
        services.AddHostedService<ExecutionEventBridge>();

        var origins = configuration.GetSection("Api:RealTime:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();

        services.AddCors(options => options.AddPolicy(CorsPolicy, policy =>
        {
            // Deny-by-default: with no configured origins there is no cross-origin access.
            // SignalR requires AllowCredentials, which forbids a wildcard origin~ 🔒
            if (origins.Length > 0)
            {
                policy.WithOrigins(origins)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            }
        }));

        return services;
    }
}
