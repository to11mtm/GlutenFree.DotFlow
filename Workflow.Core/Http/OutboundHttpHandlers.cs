// <copyright file="OutboundHttpHandlers.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Core.Http;

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

/// <summary>🌐 Stage 7.3 — a request blocked by the outbound HTTP policy.</summary>
public sealed class OutboundHttpBlockedException : HttpRequestException
{
    /// <summary>Initializes a new instance of the <see cref="OutboundHttpBlockedException"/> class.</summary>
    /// <param name="channel">The channel whose policy blocked it.</param>
    /// <param name="host">The destination host.</param>
    /// <param name="reason">The rule that blocked it.</param>
    public OutboundHttpBlockedException(OutboundHttpChannel channel, string host, string reason)
        : base($"Outbound HTTP to '{host}' was blocked by the {channel} policy: {reason}. " +
               $"An administrator can change this under Settings → Outbound HTTP, or in {OutboundHttpOptions.SectionName}:{channel}.")
    {
        this.Channel = channel;
        this.Host = host;
        this.Reason = reason;
    }

    /// <summary>Gets the channel.</summary>
    public OutboundHttpChannel Channel { get; }

    /// <summary>Gets the destination host.</summary>
    public string Host { get; }

    /// <summary>Gets the rule that blocked it.</summary>
    public string Reason { get; }
}

/// <summary>🌐 Stage 7.3 — per-request options callers set on <see cref="HttpRequestMessage.Options"/>.</summary>
public static class OutboundHttpRequestOptions
{
    /// <summary>Per-node narrowing: the request may only go to these hosts (can't widen the policy).</summary>
    public static readonly HttpRequestOptionsKey<IReadOnlyCollection<string>> AllowedHosts = new("dotflow.outbound.allowedHosts");

    /// <summary>Whether redirects are followed (default true).</summary>
    public static readonly HttpRequestOptionsKey<bool> FollowRedirects = new("dotflow.outbound.followRedirects");
}

/// <summary>
/// 🌐 Stage 7.3 — builds HTTP handlers that enforce a channel's policy: a <see cref="SocketsHttpHandler"/>
/// whose connect step checks every resolved address, and an <see cref="OutboundPolicyHandler"/> that
/// checks host rules on every hop and follows redirects itself.
/// </summary>
public static class OutboundHttp
{
    /// <summary>The most redirects followed.</summary>
    public const int MaxRedirects = 10;

    /// <summary>The named <c>HttpClient</c> for the HTTP Request module (channel <see cref="OutboundHttpChannel.HttpModule"/>).</summary>
    public const string HttpModuleClientName = "dotflow.http";

    /// <summary>The named <c>HttpClient</c> for script HTTP (channel <see cref="OutboundHttpChannel.Scripts"/>).</summary>
    public const string ScriptsClientName = "dotflow.script.http";

    /// <summary>
    /// The primary handler: automatic redirects off (the policy handler follows them), and a connect
    /// step that resolves the host and connects only to an address the policy allows.
    /// </summary>
    /// <param name="channel">The channel.</param>
    /// <param name="policy">Supplies the channel's current policy.</param>
    /// <param name="configure">Further handler settings (pooling, timeouts…).</param>
    /// <returns>The handler.</returns>
    public static SocketsHttpHandler CreatePrimaryHandler(
        OutboundHttpChannel channel,
        Func<EffectiveOutboundPolicy> policy,
        Action<SocketsHttpHandler>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(policy);

        var handler = new SocketsHttpHandler();
        configure?.Invoke(handler);
        handler.AllowAutoRedirect = false;
        handler.ConnectCallback = async (context, cancellationToken) =>
        {
            var current = policy();
            var host = context.DnsEndPoint.Host;
            var addresses = IPAddress.TryParse(host, out var literal)
                ? [literal]
                : await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);

            OutboundHttpDecision? firstBlock = null;
            foreach (var address in addresses)
            {
                var decision = OutboundHttpRules.CheckAddress(current, host, address);
                if (!decision.Allowed)
                {
                    firstBlock ??= decision;
                    continue;
                }

                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken).ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }

            throw new OutboundHttpBlockedException(channel, host, firstBlock?.Reason ?? "the host didn't resolve to any address");
        };

        return handler;
    }

    /// <summary>A complete client pipeline for callers without an <c>IHttpClientFactory</c>.</summary>
    /// <param name="channel">The channel.</param>
    /// <param name="policy">Supplies the channel's current policy.</param>
    /// <returns>The handler chain (policy handler over the primary handler).</returns>
    public static HttpMessageHandler CreatePipeline(OutboundHttpChannel channel, Func<EffectiveOutboundPolicy> policy)
        => new OutboundPolicyHandler(channel, policy) { InnerHandler = CreatePrimaryHandler(channel, policy) };
}

/// <summary>
/// 🌐 Stage 7.3 — checks host rules (and per-node narrowing) before every request and every redirect
/// hop, following redirects itself (up to <see cref="OutboundHttp.MaxRedirects"/>). Address rules
/// are enforced by the primary handler's connect step.
/// </summary>
public sealed class OutboundPolicyHandler : DelegatingHandler
{
    private readonly OutboundHttpChannel channel;
    private readonly Func<EffectiveOutboundPolicy> policy;

    /// <summary>Initializes a new instance of the <see cref="OutboundPolicyHandler"/> class.</summary>
    /// <param name="channel">The channel.</param>
    /// <param name="policy">Supplies the channel's current policy.</param>
    public OutboundPolicyHandler(OutboundHttpChannel channel, Func<EffectiveOutboundPolicy> policy)
    {
        this.channel = channel;
        this.policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var follow = !request.Options.TryGetValue(OutboundHttpRequestOptions.FollowRedirects, out var f) || f;
        request.Options.TryGetValue(OutboundHttpRequestOptions.AllowedHosts, out var nodeHosts);
        if (request.Content is not null)
        {
            await request.Content.LoadIntoBufferAsync().ConfigureAwait(false); // 307/308 re-send it~
        }

        var current = request;
        for (var hop = 0; ; hop++)
        {
            var uri = current.RequestUri ?? throw new InvalidOperationException("The request has no URI.");
            var decision = OutboundHttpRules.CheckHost(this.policy(), uri, nodeHosts);
            if (!decision.Allowed)
            {
                throw new OutboundHttpBlockedException(this.channel, uri.Host, decision.Reason);
            }

            HttpResponseMessage response;
            try
            {
                response = await base.SendAsync(current, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (FindBlocked(ex) is { } blocked)
            {
                throw blocked; // the connect step's clear message, not "connection failed"~
            }

            var status = (int)response.StatusCode;
            if (!follow || status is not (301 or 302 or 303 or 307 or 308) || response.Headers.Location is null || hop >= OutboundHttp.MaxRedirects)
            {
                return response;
            }

            var next = new Uri(uri, response.Headers.Location);
            var method = status == 303 || (status is 301 or 302 && current.Method == HttpMethod.Post) ? HttpMethod.Get : current.Method;
            var nextRequest = new HttpRequestMessage(method, next);
            foreach (var header in current.Headers)
            {
                // Credentials don't follow a redirect to another host~
                if (header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(next.Host, uri.Host, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                nextRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            if (method == current.Method && current.Content is not null)
            {
                nextRequest.Content = current.Content;
            }

            response.Dispose();
            current = nextRequest;
        }
    }

    private static OutboundHttpBlockedException? FindBlocked(Exception? ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is OutboundHttpBlockedException blocked)
            {
                return blocked;
            }
        }

        return null;
    }
}
