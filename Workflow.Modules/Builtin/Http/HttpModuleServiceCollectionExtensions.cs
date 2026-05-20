// <copyright file="HttpModuleServiceCollectionExtensions.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.Http;

using System.Net;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// 🌐 DI registration helpers for the HTTP built-in module family~ ✨💖.
/// </summary>
/// <remarks>
/// <para>
/// Call <see cref="AddHttpModules"/> once in your host startup
/// (e.g. <c>builder.Services.AddHttpModules()</c> in <c>Program.cs</c>).
/// </para>
/// <para>
/// CopilotNote: We use a named <c>IHttpClientFactory</c> client ("dotflow.http") to avoid
/// socket exhaustion and DNS-cache staleness that plague newing up <c>HttpClient</c> directly.
/// The named client is configured with a <c>SocketsHttpHandler</c> and sensible pool lifetime
/// defaults — individual modules may create short-lived scoped instances via
/// <c>httpClientFactory.CreateClient("dotflow.http")</c> for per-request timeout overrides~ 🧠.
/// </para>
/// </remarks>
public static class HttpModuleServiceCollectionExtensions
{
    /// <summary>
    /// Named <c>HttpClient</c> key used by all HTTP modules in this family~ 🏷️.
    /// </summary>
    public const string HttpClientName = "dotflow.http";

    /// <summary>
    /// Registers the <c>IHttpClientFactory</c> named client and any singleton services
    /// required by the HTTP built-in module family~ 🌐✨.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <returns>The same <paramref name="services"/> for chaining~ 💖.</returns>
    public static IServiceCollection AddHttpModules(this IServiceCollection services)
    {
        // Register the named HttpClient with a SocketsHttpHandler that:
        //   • Pools connections for up to 2 minutes (PooledConnectionLifetime)
        //     — prevents stale DNS entries from persisting indefinitely~ 🧠
        //   • Allows up to 256 connections per endpoint (sensible ceiling for workflows)
        //   • Does NOT set a request timeout here — modules control that per-request
        //     via a CancellationTokenSource linked to their CancellationToken~ ⏱️
        services.AddHttpClient(HttpClientName, client =>
            {
                // Default User-Agent so servers can identify DotFlow requests~ 🏷️
                client.DefaultRequestHeaders.Add(
                    "User-Agent",
                    "GlutenFree.DotFlow/1.0 (https://github.com/GlutenFree/DotFlow)");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // Rotate connections every 2 minutes to pick up DNS changes~ 🔄
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),

                // Allow parallel connections to the same host (needed for parallel branches)~ ⚡
                MaxConnectionsPerServer = 256,

                // Respect the module's CancellationToken — don't add an extra layer here~ 🛑
                ConnectTimeout = TimeSpan.FromSeconds(30),

                // Follow redirects by default; modules can override via HttpRequestModule.followRedirects~ 🔗
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10,

                // Default certificate validation ON; modules can flip via HttpRequestModule.validateCertificate~ 🔒
                SslOptions = new System.Net.Security.SslClientAuthenticationOptions
                {
                    // Inherits system cert store — no custom roots set here
                },
            });

        return services;
    }
}

