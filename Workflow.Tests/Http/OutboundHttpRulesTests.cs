// <copyright file="OutboundHttpRulesTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Http;

using System;
using System.Collections.Generic;
using System.Net;
using FluentAssertions;
using Workflow.Core.Http;
using Xunit;

/// <summary>Stage 7.3 — the outbound HTTP policy rules and how the settings layers merge.</summary>
public sealed class OutboundHttpRulesTests
{
    private static EffectiveOutboundPolicy Policy(
        OutboundHttpMode mode,
        string[]? allowedHosts = null,
        string[]? deniedHosts = null,
        string[]? allowedCidrs = null,
        string[]? deniedCidrs = null,
        bool metadata = false)
        => new(OutboundHttpChannel.HttpModule, mode, true, allowedHosts ?? [], deniedHosts ?? [], allowedCidrs ?? [], deniedCidrs ?? [], metadata, []);

    private static bool Allowed(EffectiveOutboundPolicy policy, string host, string ip)
        => OutboundHttpRules.CheckAddress(policy, host, IPAddress.Parse(ip)).Allowed;

    [Theory]
    [InlineData(OutboundHttpMode.Unrestricted, true, true)]
    [InlineData(OutboundHttpMode.PublicOnly, false, true)]
    [InlineData(OutboundHttpMode.InternalOnly, true, false)]
    [InlineData(OutboundHttpMode.AllowlistOnly, false, false)]
    public void EachMode_DecidesInternalAndPublicAddresses(OutboundHttpMode mode, bool internalAllowed, bool publicAllowed)
    {
        var policy = Policy(mode);

        Allowed(policy, "intranet", "10.1.2.3").Should().Be(internalAllowed);
        Allowed(policy, "localhost", "127.0.0.1").Should().Be(internalAllowed);
        Allowed(policy, "intranet6", "fd12::1").Should().Be(internalAllowed);
        Allowed(policy, "example.com", "93.184.216.34").Should().Be(publicAllowed);
        Allowed(policy, "example6.com", "2606:2800:220:1::1").Should().Be(publicAllowed);
    }

    [Fact]
    public void AllowLists_OpenAnExceptionInAnyMode_ButDenyAlwaysWins()
    {
        Allowed(Policy(OutboundHttpMode.PublicOnly, allowedCidrs: ["10.20.0.0/16"]), "build", "10.20.1.1").Should().BeTrue();
        Allowed(Policy(OutboundHttpMode.InternalOnly, allowedHosts: ["*.partner.com"]), "api.partner.com", "93.184.216.34").Should().BeTrue();
        Allowed(Policy(OutboundHttpMode.AllowlistOnly, allowedHosts: ["api.partner.com"]), "api.partner.com", "93.184.216.34").Should().BeTrue();
        Allowed(Policy(OutboundHttpMode.AllowlistOnly, allowedHosts: ["api.partner.com"]), "evil.com", "93.184.216.35").Should().BeFalse();

        Allowed(Policy(OutboundHttpMode.Unrestricted, deniedCidrs: ["10.0.5.0/24"]), "db", "10.0.5.9").Should().BeFalse();
        Allowed(Policy(OutboundHttpMode.Unrestricted, allowedCidrs: ["10.0.0.0/8"], deniedCidrs: ["10.0.5.0/24"]), "db", "10.0.5.9")
            .Should().BeFalse(because: "deny wins over allow");
        Allowed(Policy(OutboundHttpMode.Unrestricted, allowedHosts: ["db.local"], deniedHosts: ["*.local"]), "db.local", "10.0.5.9").Should().BeFalse();
    }

    [Theory]
    [InlineData(OutboundHttpMode.Unrestricted)]
    [InlineData(OutboundHttpMode.InternalOnly)]
    [InlineData(OutboundHttpMode.PublicOnly)]
    public void CloudMetadata_NeedsTheExplicitOption_InEveryMode(OutboundHttpMode mode)
    {
        Allowed(Policy(mode), "169.254.169.254", "169.254.169.254").Should().BeFalse();
        Allowed(Policy(mode, allowedCidrs: ["169.254.169.254"], allowedHosts: ["169.254.169.254"]), "169.254.169.254", "169.254.169.254")
            .Should().BeFalse(because: "listing it isn't enough — AllowCloudMetadata is the switch");
        Allowed(Policy(mode, allowedCidrs: ["169.254.169.254"], metadata: true), "169.254.169.254", "169.254.169.254").Should().BeTrue();

        OutboundHttpRules.CheckHost(Policy(mode), new Uri("http://metadata.google.internal/computeMetadata/v1/"), null).Allowed.Should().BeFalse();
        Allowed(Policy(mode), "aws", "::ffff:169.254.169.254").Should().BeFalse(because: "IPv4-mapped IPv6 is the same address");
    }

    [Fact]
    public void HostRules_NodeNarrowing_DeniedHosts_AndSchemes()
    {
        var policy = Policy(OutboundHttpMode.Unrestricted, deniedHosts: ["*.internal.corp"]);

        OutboundHttpRules.CheckHost(policy, new Uri("https://api.partner.com/x"), ["*.partner.com"]).Allowed.Should().BeTrue();
        OutboundHttpRules.CheckHost(policy, new Uri("https://evil.com/x"), ["*.partner.com"]).Reason.Should().Contain("this node's allowed hosts");
        OutboundHttpRules.CheckHost(policy, new Uri("https://hr.internal.corp/x"), null).Allowed.Should().BeFalse();
        OutboundHttpRules.CheckHost(policy, new Uri("ftp://files.partner.com/x"), null).Allowed.Should().BeFalse();
    }

    [Theory]
    [InlineData("*.partner.com", "api.partner.com", true)]
    [InlineData("*.partner.com", "a.b.partner.com", true)]
    [InlineData("*.partner.com", "partner.com", false)]
    [InlineData("*.partner.com", "evilpartner.com", false)]
    [InlineData("API.Partner.com", "api.partner.com.", true)]
    public void HostPatterns(string pattern, string host, bool matches)
        => OutboundHttpRules.HostMatches(pattern, host).Should().Be(matches);

    [Fact]
    public void Layers_HostSettingsWinAndAreLocked_UnsetFallsBackToFailClosedPublicOnly()
    {
        var host = new OutboundHttpOptions
        {
            Default = new OutboundHttpPolicySettings { DeniedCidrs = ["10.0.5.0/24"] },
            Scripts = new OutboundHttpPolicySettings { Mode = OutboundHttpMode.AllowlistOnly },
        };
        var saved = new OutboundHttpOptions
        {
            Scripts = new OutboundHttpPolicySettings { Mode = OutboundHttpMode.Unrestricted, AllowedHosts = ["api.partner.com"] },
            Default = new OutboundHttpPolicySettings { AllowCloudMetadata = true },
        };

        var scripts = EffectiveOutboundPolicy.Resolve(OutboundHttpChannel.Scripts, host, saved);
        scripts.Mode.Should().Be(OutboundHttpMode.AllowlistOnly, because: "host settings beat the saved policy");
        scripts.AllowedHosts.Should().Equal("api.partner.com");
        scripts.DeniedCidrs.Should().Equal("10.0.5.0/24");
        scripts.AllowCloudMetadata.Should().BeTrue();
        scripts.LockedFields.Should().BeEquivalentTo("Mode", "DeniedCidrs");

        var module = EffectiveOutboundPolicy.Resolve(OutboundHttpChannel.HttpModule, host, null);
        module.Configured.Should().BeFalse();
        module.Mode.Should().Be(OutboundHttpMode.PublicOnly, because: "an unconfigured channel fails closed");
    }
}
