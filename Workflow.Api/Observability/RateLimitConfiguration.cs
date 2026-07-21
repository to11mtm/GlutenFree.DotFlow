// <copyright file="RateLimitConfiguration.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.Observability;

using System;
using System.Globalization;
using System.Threading;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Workflow.Api.Auth;

/// <summary>
/// 🚦 Phase 2.7.8 — Bound configuration for the API rate-limiting seam (<c>Api:RateLimit</c>)~ ✨.
/// </summary>
public sealed class RateLimitOptions
{
    /// <summary>The configuration section name~ 📇.</summary>
    public const string SectionName = "Api:RateLimit";

    /// <summary>Gets or sets a value indicating whether rate limiting is enforced (default off, Q3).</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the number of requests permitted per window (per caller).</summary>
    public int PermitLimit { get; set; } = 100;

    /// <summary>Gets or sets the fixed window length in seconds.</summary>
    public int WindowSeconds { get; set; } = 60;
}

/// <summary>
/// 🚦 Phase 2.7.8 — Registers a global fixed-window rate limiter partitioned by API key / caller id.
/// The limiter is a no-op until <c>Api:RateLimit:Enabled</c> is set, evaluated per-request from the
/// live options so it stays testable~ ✨💖.
/// </summary>
public static class RateLimitConfiguration
{
    private const string AnonymousPartition = "anonymous";

    /// <summary>Adds the workflow API rate-limiting seam~ 🚦.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The app configuration.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddWorkflowRateLimiting(
        this IServiceCollection services,
        Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        services.Configure<RateLimitOptions>(configuration.GetSection("Api").GetSection("RateLimit"));

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = (context, _) =>
            {
                var opts = context.HttpContext.RequestServices.GetRequiredService<IOptionsMonitor<RateLimitOptions>>();
                context.HttpContext.Response.Headers["Retry-After"] =
                    opts.CurrentValue.WindowSeconds.ToString(CultureInfo.InvariantCulture);
                return ValueTask.CompletedTask;
            };

            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            {
                var opts = httpContext.RequestServices
                    .GetRequiredService<IOptionsMonitor<RateLimitOptions>>().CurrentValue;

                if (!opts.Enabled)
                {
                    return RateLimitPartition.GetNoLimiter(AnonymousPartition);
                }

                var key = ResolvePartitionKey(httpContext);
                return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = Math.Max(1, opts.PermitLimit),
                    Window = TimeSpan.FromSeconds(Math.Max(1, opts.WindowSeconds)),
                    QueueLimit = 0,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    AutoReplenishment = true,
                });
            });
        });

        return services;
    }

    private static string ResolvePartitionKey(HttpContext httpContext)
    {
        if (httpContext.Request.Headers.TryGetValue(AuthConstants.ApiKeyHeader, out var apiKey)
            && !string.IsNullOrWhiteSpace(apiKey))
        {
            return "key:" + apiKey.ToString();
        }

        var caller = httpContext.ResolveCallerId();
        return caller == CallerIdentity.SystemCaller ? AnonymousPartition : "caller:" + caller;
    }
}
