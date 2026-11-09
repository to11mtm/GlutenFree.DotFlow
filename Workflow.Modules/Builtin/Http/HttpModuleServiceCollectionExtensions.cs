// <copyright file="HttpModuleServiceCollectionExtensions.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.Http;

using System.Net;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Workflow.Core.Http;
using Workflow.Modules.Builtin.Http.Auth;

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
        static void ConfigurePooling(SocketsHttpHandler handler)
        {
            // Rotate connections every 2 minutes to pick up DNS changes~ 🔄
            handler.PooledConnectionLifetime = TimeSpan.FromMinutes(2);

            // Allow parallel connections to the same host (needed for parallel branches)~ ⚡
            handler.MaxConnectionsPerServer = 256;

            // Respect the module's CancellationToken — don't add an extra layer here~ 🛑
            handler.ConnectTimeout = TimeSpan.FromSeconds(30);
        }

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
            // 🌐 Stage 7.3 — the HttpModule channel's outbound policy: the connect step checks every
            // resolved address; the policy handler checks host rules on every hop and follows redirects
            // (up to 10) itself, so a redirect can't sneak past the rules~
            .ConfigurePrimaryHttpMessageHandler(sp => OutboundHttp.CreatePrimaryHandler(
                OutboundHttpChannel.HttpModule,
                () => sp.GetRequiredService<IOutboundHttpPolicyProvider>().Get(OutboundHttpChannel.HttpModule),
                ConfigurePooling))
            .AddHttpMessageHandler(sp => new OutboundPolicyHandler(
                OutboundHttpChannel.HttpModule,
                () => sp.GetRequiredService<IOutboundHttpPolicyProvider>().Get(OutboundHttpChannel.HttpModule)));

        // 🌐 Stage 7.3 — script HTTP (workflow.http*) gets its own client and its own policy channel~
        services.AddHttpClient(OutboundHttp.ScriptsClientName, client =>
                client.DefaultRequestHeaders.Add("User-Agent", "GlutenFree.DotFlow/1.0 (script)"))
            .ConfigurePrimaryHttpMessageHandler(sp => OutboundHttp.CreatePrimaryHandler(
                OutboundHttpChannel.Scripts,
                () => sp.GetRequiredService<IOutboundHttpPolicyProvider>().Get(OutboundHttpChannel.Scripts),
                ConfigurePooling))
            .AddHttpMessageHandler(sp => new OutboundPolicyHandler(
                OutboundHttpChannel.Scripts,
                () => sp.GetRequiredService<IOutboundHttpPolicyProvider>().Get(OutboundHttpChannel.Scripts)));

        // Hosts that don't register a policy provider get the fail-closed default (unconfigured =
        // PublicOnly). The API host registers the real one (host settings + setup screen)~
        services.TryAddSingleton<IOutboundHttpPolicyProvider>(new StaticOutboundHttpPolicyProvider(null));

        // 🔑 Phase 2.3.3 — Pipeline-scoped OAuth2 token cache (DI singleton)~
        // Keyed on (executionId, authority, clientId, scope); tokens are shared across HTTP nodes
        // inside a single WorkflowExecution but never leak across workflows in V1~
        services.AddSingleton<PerPipelineOAuth2TokenCache>();
        services.AddSingleton<IOAuth2TokenCache>(sp => sp.GetRequiredService<PerPipelineOAuth2TokenCache>());

        return services;
    }
}

