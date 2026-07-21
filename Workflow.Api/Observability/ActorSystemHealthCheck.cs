// <copyright file="ActorSystemHealthCheck.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.Observability;

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Workflow.Api.Webhooks;

/// <summary>
/// ❤️ Phase 2.7.5 — Liveness check for the Akka <c>WorkflowSupervisor</c>. Pings the supervisor
/// with a lightweight <see cref="Identify"/> and expects an <see cref="ActorIdentity"/> back~ ✨.
/// </summary>
public sealed class ActorSystemHealthCheck : IHealthCheck
{
    private static readonly TimeSpan PingTimeout = TimeSpan.FromSeconds(3);
    private readonly IServiceProvider services;

    /// <summary>Initializes a new instance of the <see cref="ActorSystemHealthCheck"/> class~ ❤️.</summary>
    /// <param name="services">The root service provider.</param>
    public ActorSystemHealthCheck(IServiceProvider services)
    {
        this.services = services;
    }

    /// <inheritdoc/>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var supervisor = this.services.GetService<WorkflowSupervisorActorRef>();
        if (supervisor is null)
        {
            return HealthCheckResult.Degraded("The workflow supervisor is not configured.");
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(PingTimeout);

            var identity = await supervisor.ActorRef
                .Ask<ActorIdentity>(new Identify(null), cancellationToken: cts.Token)
                .ConfigureAwait(false);

            return identity.Subject is not null
                ? HealthCheckResult.Healthy("The workflow supervisor is responsive.")
                : HealthCheckResult.Unhealthy("The workflow supervisor did not identify itself.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("The workflow supervisor did not respond.", ex);
        }
    }
}
