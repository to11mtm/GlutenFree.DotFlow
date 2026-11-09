// <copyright file="OutboundHttpRules.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Core.Http;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;

/// <summary>🌐 Stage 7.3 — the outcome of checking a destination against a policy.</summary>
/// <param name="Allowed">Whether it may be reached.</param>
/// <param name="Reason">Why not (or which rule allowed it), for messages and the setup screen's "check" box.</param>
public sealed record OutboundHttpDecision(bool Allowed, string Reason)
{
    /// <summary>An allowed outcome.</summary>
    /// <param name="reason">The rule that allowed it.</param>
    /// <returns>The decision.</returns>
    public static OutboundHttpDecision Allow(string reason) => new(true, reason);

    /// <summary>A blocked outcome.</summary>
    /// <param name="reason">The rule that blocked it.</param>
    /// <returns>The decision.</returns>
    public static OutboundHttpDecision Block(string reason) => new(false, reason);
}

/// <summary>
/// 🌐 Stage 7.3 — the policy rules, in two stages so they can't be bypassed:
/// <list type="number">
///   <item><see cref="CheckHost"/> on every request and every redirect hop (host-name rules and
///         per-node narrowing);</item>
///   <item><see cref="CheckAddress"/> on every address the client is about to connect to (mode,
///         address ranges, cloud metadata) — so redirects and names that resolve to internal
///         addresses are caught.</item>
/// </list>
/// Deny always wins; cloud metadata needs <see cref="EffectiveOutboundPolicy.AllowCloudMetadata"/> in
/// every mode.
/// </summary>
public static class OutboundHttpRules
{
    /// <summary>Host names of cloud metadata services.</summary>
    public static readonly IReadOnlySet<string> MetadataHostNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "metadata.google.internal",
        "metadata",
        "instance-data",
    };

    private static readonly IPAddress[] MetadataAddresses =
    [
        IPAddress.Parse("169.254.169.254"),
        IPAddress.Parse("fd00:ec2::254"),
        IPAddress.Parse("100.100.100.200"), // Alibaba Cloud
    ];

    private static readonly IPNetwork[] InternalRanges =
    [
        IPNetwork.Parse("0.0.0.0/8"),
        IPNetwork.Parse("10.0.0.0/8"),
        IPNetwork.Parse("100.64.0.0/10"),
        IPNetwork.Parse("127.0.0.0/8"),
        IPNetwork.Parse("169.254.0.0/16"),
        IPNetwork.Parse("172.16.0.0/12"),
        IPNetwork.Parse("192.168.0.0/16"),
        IPNetwork.Parse("198.18.0.0/15"),
        IPNetwork.Parse("224.0.0.0/4"),
        IPNetwork.Parse("240.0.0.0/4"),
        IPNetwork.Parse("::/128"),
        IPNetwork.Parse("::1/128"),
        IPNetwork.Parse("fc00::/7"),
        IPNetwork.Parse("fe80::/10"),
        IPNetwork.Parse("ff00::/8"),
    ];

    /// <summary>Whether an address is internal (loopback, private, link-local, CGNAT, multicast…).</summary>
    /// <param name="address">The address.</param>
    /// <returns>True when internal.</returns>
    public static bool IsInternal(IPAddress address)
    {
        var ip = Normalize(address);
        return InternalRanges.Any(r => r.BaseAddress.AddressFamily == ip.AddressFamily && r.Contains(ip));
    }

    /// <summary>Whether an address is a cloud metadata endpoint.</summary>
    /// <param name="address">The address.</param>
    /// <returns>True when it is.</returns>
    public static bool IsCloudMetadata(IPAddress address) => MetadataAddresses.Contains(Normalize(address));

    /// <summary>Stage 1 — host-name rules, checked before every request and redirect hop.</summary>
    /// <param name="policy">The channel's policy.</param>
    /// <param name="uri">The request URI.</param>
    /// <param name="nodeAllowedHosts">Per-node narrowing (null/empty = none).</param>
    /// <returns>The decision.</returns>
    public static OutboundHttpDecision CheckHost(EffectiveOutboundPolicy policy, Uri uri, IReadOnlyCollection<string>? nodeAllowedHosts)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(uri);

        if (uri.Scheme is not ("http" or "https"))
        {
            return OutboundHttpDecision.Block($"only http and https are allowed, not '{uri.Scheme}'");
        }

        var host = uri.IdnHost.TrimEnd('.');
        if (nodeAllowedHosts is { Count: > 0 } && !nodeAllowedHosts.Any(p => HostMatches(p, host)))
        {
            return OutboundHttpDecision.Block($"'{host}' isn't in this node's allowed hosts ({string.Join(", ", nodeAllowedHosts)})");
        }

        if (policy.DeniedHosts.FirstOrDefault(p => HostMatches(p, host)) is { } denied)
        {
            return OutboundHttpDecision.Block($"'{host}' matches the denied host '{denied}'");
        }

        if (MetadataHostNames.Contains(host) && !policy.AllowCloudMetadata)
        {
            return OutboundHttpDecision.Block($"'{host}' is a cloud metadata endpoint (AllowCloudMetadata is off)");
        }

        return OutboundHttpDecision.Allow("host rules passed");
    }

    /// <summary>Stage 2 — address rules, checked for every address the client connects to.</summary>
    /// <param name="policy">The channel's policy.</param>
    /// <param name="host">The host name being connected to.</param>
    /// <param name="address">The resolved address.</param>
    /// <returns>The decision.</returns>
    public static OutboundHttpDecision CheckAddress(EffectiveOutboundPolicy policy, string host, IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(address);

        var ip = Normalize(address);
        if (IsCloudMetadata(ip) && !policy.AllowCloudMetadata)
        {
            return OutboundHttpDecision.Block($"{ip} is a cloud metadata endpoint (AllowCloudMetadata is off)");
        }

        if (policy.DeniedCidrs.FirstOrDefault(c => CidrContains(c, ip)) is { } deniedRange)
        {
            return OutboundHttpDecision.Block($"{ip} is in the denied range {deniedRange}");
        }

        if (policy.DeniedHosts.FirstOrDefault(p => HostMatches(p, host)) is { } deniedHost)
        {
            return OutboundHttpDecision.Block($"'{host}' matches the denied host '{deniedHost}'");
        }

        if (policy.AllowedHosts.FirstOrDefault(p => HostMatches(p, host)) is { } allowedHost)
        {
            return OutboundHttpDecision.Allow($"'{host}' matches the allowed host '{allowedHost}'");
        }

        if (policy.AllowedCidrs.FirstOrDefault(c => CidrContains(c, ip)) is { } allowedRange)
        {
            return OutboundHttpDecision.Allow($"{ip} is in the allowed range {allowedRange}");
        }

        var internalAddress = IsInternal(ip);
        return policy.Mode switch
        {
            OutboundHttpMode.Unrestricted => OutboundHttpDecision.Allow("mode Unrestricted"),
            OutboundHttpMode.PublicOnly when internalAddress => OutboundHttpDecision.Block($"{ip} is an internal address and the mode is PublicOnly"),
            OutboundHttpMode.PublicOnly => OutboundHttpDecision.Allow("public address, mode PublicOnly"),
            OutboundHttpMode.InternalOnly when internalAddress => OutboundHttpDecision.Allow("internal address, mode InternalOnly"),
            OutboundHttpMode.InternalOnly => OutboundHttpDecision.Block($"{ip} is a public address and the mode is InternalOnly"),
            _ => OutboundHttpDecision.Block($"'{host}' ({ip}) isn't in the allowed hosts or ranges and the mode is AllowlistOnly"),
        };
    }

    /// <summary>Whether a host pattern (exact, or <c>*.example.com</c>) matches a host.</summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="host">The host.</param>
    /// <returns>True when it matches.</returns>
    public static bool HostMatches(string pattern, string host)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return false;
        }

        var p = pattern.Trim().TrimEnd('.');
        var h = host.Trim().TrimEnd('.');
        return p.StartsWith("*.", StringComparison.Ordinal)
            ? h.EndsWith(p[1..], StringComparison.OrdinalIgnoreCase) && h.Length > p.Length - 1
            : string.Equals(p, h, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whether a range (CIDR or single address) is valid.</summary>
    /// <param name="cidr">The range.</param>
    /// <returns>True when it parses.</returns>
    public static bool IsValidCidr(string cidr) => TryParseRange(cidr, out _);

    private static bool CidrContains(string cidr, IPAddress ip)
        => TryParseRange(cidr, out var range) && range.BaseAddress.AddressFamily == ip.AddressFamily && range.Contains(ip);

    private static bool TryParseRange(string value, out IPNetwork range)
    {
        var text = (value ?? string.Empty).Trim();
        if (IPAddress.TryParse(text, out var single))
        {
            single = Normalize(single);
            range = new IPNetwork(single, single.AddressFamily == AddressFamily.InterNetwork ? 32 : 128);
            return true;
        }

        return IPNetwork.TryParse(text, out range);
    }

    private static IPAddress Normalize(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}
