// <copyright file="MonitoringEndpoints.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.V1;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Workflow.Api.Observability;
using Workflow.Modules.Abstractions;
using Workflow.Persistence.Abstractions;

/// <summary>
/// 📊 Phase 2.7.5 — Health / readiness / liveness + status + metrics endpoints (D10/D11)~ ✨💖.
/// </summary>
public static class MonitoringEndpoints
{
    /// <summary>The tag that marks a health check as part of readiness~ 🏁.</summary>
    public const string ReadyTag = "ready";

    /// <summary>The tag that marks a health check as part of liveness~ 💓.</summary>
    public const string LiveTag = "live";

    private static readonly DateTimeOffset StartedAt = DateTimeOffset.UtcNow;

    /// <summary>Registers the monitoring endpoints under <c>/api/v1</c>~ 📊.</summary>
    /// <param name="app">The endpoint route builder.</param>
    /// <returns>The same <paramref name="app"/> for chaining.</returns>
    public static IEndpointRouteBuilder MapMonitoringEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapV1Group().WithTags("Monitoring");

        group.MapGet("/health", HealthHandler).WithName("Health");
        group.MapGet("/health/ready", ReadyHandler).WithName("HealthReady");
        group.MapGet("/health/live", LiveHandler).WithName("HealthLive");
        group.MapGet("/status", StatusHandler).WithName("Status");
        group.MapGet("/metrics", MetricsHandler).WithName("Metrics");

        return app;
    }

    private static Task<IResult> HealthHandler(HttpContext http, CancellationToken ct)
        => RunHealthAsync(http, _ => true, ct);

    private static Task<IResult> ReadyHandler(HttpContext http, CancellationToken ct)
        => RunHealthAsync(http, reg => reg.Tags.Contains(ReadyTag), ct);

    private static Task<IResult> LiveHandler(HttpContext http, CancellationToken ct)
        => RunHealthAsync(http, reg => reg.Tags.Contains(LiveTag), ct);

    private static async Task<IResult> RunHealthAsync(
        HttpContext http,
        Func<HealthCheckRegistration, bool> predicate,
        CancellationToken ct)
    {
        var service = http.RequestServices.GetService<HealthCheckService>();
        if (service is null)
        {
            return ApiResults.ServiceUnavailableProblem("Health checks are not configured.");
        }

        var report = await service.CheckHealthAsync(predicate, ct).ConfigureAwait(false);
        var payload = new
        {
            status = report.Status.ToString(),
            totalDurationMs = report.TotalDuration.TotalMilliseconds,
            components = report.Entries.ToDictionary(
                e => e.Key,
                e => new
                {
                    status = e.Value.Status.ToString(),
                    description = e.Value.Description,
                    durationMs = e.Value.Duration.TotalMilliseconds,
                }),
        };

        var code = report.Status == HealthStatus.Unhealthy
            ? StatusCodes.Status503ServiceUnavailable
            : StatusCodes.Status200OK;

        return Results.Json(payload, statusCode: code);
    }

    private static async Task<IResult> StatusHandler(HttpContext http, CancellationToken ct)
    {
        var registry = http.RequestServices.GetService<IModuleRegistry>();
        var provider = http.RequestServices.GetService<IPersistenceProvider>();
        var metrics = http.RequestServices.GetService<IWorkflowMetrics>();
        var tracker = http.RequestServices.GetService<Workflow.Api.RealTime.IConnectionTracker>();

        var providerHealthy = false;
        string providerName = "none";
        if (provider is not null)
        {
            providerName = provider.ProviderName;
            var health = await provider.HealthCheckAsync(ct).ConfigureAwait(false);
            providerHealthy = health.IsHealthy;
        }

        var snapshot = metrics?.Snapshot();
        var spill = http.RequestServices.GetService<IOptions<Workflow.Api.Streaming.SpillOptions>>()?.Value;

        var payload = new
        {
            version = ApiVersion,
            uptimeSeconds = Math.Max(0, (DateTimeOffset.UtcNow - StartedAt).TotalSeconds),
            persistence = new { provider = providerName, healthy = providerHealthy },
            moduleCount = registry?.GetAllModules().Count ?? 0,
            activeExecutions = snapshot?.Active ?? 0,
            activeConnections = tracker?.ConnectionCount ?? 0,
            activeSubscriptions = tracker?.SubscriptionCount ?? 0,

            // 💾 Phase 5.1.6 (Q-D) — the designer can't see host settings, so spill availability and
            // whether spill is encrypted ride along here. Without this the designer would have to
            // stay silent about an unencrypted spill, and "quietly unencrypted" is exactly the
            // outcome the decision rules out~
            spill = new
            {
                configured = spill is not null && spill.Store != Workflow.Api.Streaming.SpillStoreKind.None,
                store = spill?.Store.ToString() ?? "None",
                encrypted = spill?.Encrypt ?? true,
            },

            // 🌐 Stage 7.3 — so the designer can warn when outbound HTTP hasn't been set up~
            outboundHttp = OutboundHttpStatus(http.RequestServices.GetService<Workflow.Core.Http.IOutboundHttpPolicyProvider>()),

            // 🔑 Plan 08 — where the keys that decrypt stored secrets live, and whether that's safe~
            dataProtection = http.RequestServices.GetService<Workflow.Api.Security.DataProtectionKeyStatus>() is { } keys
                ? new { keyStorage = keys.Storage.ToString(), location = keys.Location, warning = keys.Warning }
                : null,
        };

        return Results.Ok(payload);
    }

    private static object? OutboundHttpStatus(Workflow.Core.Http.IOutboundHttpPolicyProvider? provider)
    {
        if (provider is null)
        {
            return null;
        }

        object Channel(Workflow.Core.Http.OutboundHttpChannel channel)
        {
            var policy = provider.Get(channel);
            return new { mode = policy.Mode.ToString(), configured = policy.Configured };
        }

        return new
        {
            scripts = Channel(Workflow.Core.Http.OutboundHttpChannel.Scripts),
            httpModule = Channel(Workflow.Core.Http.OutboundHttpChannel.HttpModule),
        };
    }

    private static IResult MetricsHandler(HttpContext http)
    {
        var metrics = http.RequestServices.GetService<IWorkflowMetrics>();
        if (metrics is null)
        {
            return ApiResults.ServiceUnavailableProblem("Metrics are not configured.");
        }

        // 📡 Phase 3.2 — augment execution counters with real-time connection/subscription gauges~
        var values = new Dictionary<string, long>(metrics.Snapshot().ToDictionary());
        var tracker = http.RequestServices.GetService<Workflow.Api.RealTime.IConnectionTracker>();
        if (tracker is not null)
        {
            values["realtime_connections_active"] = tracker.ConnectionCount;
            values["realtime_subscriptions_active"] = tracker.SubscriptionCount;
        }

        return Results.Ok(values);
    }

    private static string ApiVersion
        => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
}
