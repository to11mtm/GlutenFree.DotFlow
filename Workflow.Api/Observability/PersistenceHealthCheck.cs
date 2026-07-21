// <copyright file="PersistenceHealthCheck.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.Observability;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Workflow.Persistence.Abstractions;

/// <summary>
/// ❤️ Phase 2.7.5 — Health check that wraps <see cref="IPersistenceProvider.HealthCheckAsync"/>.
/// Reports <see cref="HealthStatus.Healthy"/> when a provider is configured and healthy, and a
/// benign "no provider configured" degraded state otherwise~ ✨.
/// </summary>
public sealed class PersistenceHealthCheck : IHealthCheck
{
    private readonly IServiceProvider services;

    /// <summary>Initializes a new instance of the <see cref="PersistenceHealthCheck"/> class~ ❤️.</summary>
    /// <param name="services">The root service provider (provider may be absent).</param>
    public PersistenceHealthCheck(IServiceProvider services)
    {
        this.services = services;
    }

    /// <inheritdoc/>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var provider = this.services.GetService<IPersistenceProvider>();
        if (provider is null)
        {
            return HealthCheckResult.Degraded("No persistence provider is configured.");
        }

        var result = await provider.HealthCheckAsync(cancellationToken).ConfigureAwait(false);
        var data = new Dictionary<string, object>
        {
            ["provider"] = result.ProviderName,
            ["latencyMs"] = result.Latency.TotalMilliseconds,
        };

        return result.IsHealthy
            ? HealthCheckResult.Healthy($"Persistence provider '{result.ProviderName}' is healthy.", data)
            : HealthCheckResult.Unhealthy(
                result.ErrorMessage ?? $"Persistence provider '{result.ProviderName}' is unhealthy.",
                data: data);
    }
}
