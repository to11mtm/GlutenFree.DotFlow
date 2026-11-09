// <copyright file="OutboundHttpSettingsTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Api.V1;

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Api.Auth;
using Workflow.Core.Http;
using Xunit;

/// <summary>
/// Stage 7.3 — the outbound HTTP setup screen's API: the fail-closed default and its status flag,
/// saving (audited, effective immediately), host settings locked, validation, and "check an address".
/// </summary>
public sealed class OutboundHttpSettingsTests
{
    private const string Base = "/api/v1/settings/outbound-http";

    [Fact]
    public async Task Unconfigured_FailsClosedAsPublicOnly_AndStatusSaysSo()
    {
        using var host = new Host();
        var client = host.CreateClient();

        var status = await client.GetFromJsonAsync<JsonElement>("/api/v1/status");
        var scripts = status.GetProperty("outboundHttp").GetProperty("scripts");
        scripts.GetProperty("mode").GetString().Should().Be("PublicOnly");
        scripts.GetProperty("configured").GetBoolean().Should().BeFalse();

        host.Services.GetRequiredService<IOutboundHttpPolicyProvider>().Get(OutboundHttpChannel.HttpModule).Mode
            .Should().Be(OutboundHttpMode.PublicOnly);
    }

    [Fact]
    public async Task Saving_TakesEffect_IsAudited_AndShowsUpInTheView()
    {
        using var host = new Host();
        var client = host.CreateClient();

        var put = await client.PutAsJsonAsync(Base, new
        {
            scripts = new { mode = "AllowlistOnly", allowedHosts = new[] { "api.partner.com" } },
            httpModule = new { mode = "InternalOnly", allowedCidrs = new[] { "203.0.113.0/24" }, allowCloudMetadata = false },
        });

        put.StatusCode.Should().Be(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
        var provider = host.Services.GetRequiredService<IOutboundHttpPolicyProvider>();
        provider.Get(OutboundHttpChannel.Scripts).Should().Match<EffectiveOutboundPolicy>(p =>
            p.Mode == OutboundHttpMode.AllowlistOnly && p.Configured && p.AllowedHosts.SequenceEqual(new[] { "api.partner.com" }));
        provider.Get(OutboundHttpChannel.HttpModule).Mode.Should().Be(OutboundHttpMode.InternalOnly);

        var view = await client.GetFromJsonAsync<JsonElement>(Base);
        view.GetProperty("channels").EnumerateArray()
            .Single(c => c.GetProperty("channel").GetString() == "Scripts")
            .GetProperty("effective").GetProperty("configured").GetBoolean().Should().BeTrue();

        var audit = await client.GetFromJsonAsync<JsonElement>($"{Base}/audit");
        var changes = audit[0].GetProperty("changes").EnumerateArray().Select(c => c.GetString()).ToList();
        changes.Should().Contain("Scripts.Mode: (unset) → AllowlistOnly").And.Contain("HttpModule.AllowedCidrs: (unset) → 203.0.113.0/24");
    }

    [Fact]
    public async Task HostSettings_AreLocked_AndCantBeOverriddenFromTheScreen()
    {
        using var host = new Host(("Http:Outbound:Scripts:Mode", "AllowlistOnly"));
        var client = host.CreateClient();

        var view = await client.GetFromJsonAsync<JsonElement>(Base);
        var scripts = view.GetProperty("channels").EnumerateArray().Single(c => c.GetProperty("channel").GetString() == "Scripts");
        scripts.GetProperty("effective").GetProperty("lockedFields").EnumerateArray().Select(f => f.GetString()).Should().Contain("Mode");

        var put = await client.PutAsJsonAsync(Base, new { scripts = new { mode = "Unrestricted" } });

        put.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await put.Content.ReadAsStringAsync()).Should().Contain("Scripts.Mode is set by the server's configuration");
        host.Services.GetRequiredService<IOutboundHttpPolicyProvider>().Get(OutboundHttpChannel.Scripts).Mode.Should().Be(OutboundHttpMode.AllowlistOnly);
    }

    [Fact]
    public async Task BadRangesAndHostPatterns_AreRefused()
    {
        using var host = new Host();
        var put = await host.CreateClient().PutAsJsonAsync(Base, new
        {
            @default = new { mode = "PublicOnly", allowedCidrs = new[] { "10.0.0.0/33" }, allowedHosts = new[] { "https://api.partner.com/x" } },
        });

        put.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await put.Content.ReadAsStringAsync();
        body.Should().Contain("10.0.0.0/33").And.Contain("isn't a host name");
    }

    [Fact]
    public async Task Check_ExplainsWhatWouldHappen()
    {
        using var host = new Host();
        var client = host.CreateClient();

        var blocked = await (await client.PostAsJsonAsync($"{Base}/check", new { channel = "HttpModule", url = "http://localhost:8080/x" }))
            .Content.ReadFromJsonAsync<JsonElement>();
        blocked.GetProperty("allowed").GetBoolean().Should().BeFalse();
        blocked.GetProperty("reason").GetString().Should().Contain("internal address");

        var metadata = await (await client.PostAsJsonAsync($"{Base}/check", new { channel = "Scripts", url = "http://169.254.169.254/latest/meta-data" }))
            .Content.ReadFromJsonAsync<JsonElement>();
        metadata.GetProperty("reason").GetString().Should().Contain("cloud metadata");
    }

    [Fact]
    public async Task OnlyAdminsCanSave_WhenAuthIsRequired()
    {
        const string devKey = "dev-key-outbound";
        using var host = new Host(
            ("Api:Auth:Require", "true"),
            ("Api:Auth:ApiKeys:0:KeyHash", ApiKeyHasher.Hash(devKey)),
            ("Api:Auth:ApiKeys:0:CallerId", "dev"),
            ("Api:Auth:ApiKeys:0:Roles:0", AuthConstants.DeveloperRole));
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", devKey);

        (await client.GetAsync(Base)).StatusCode.Should().Be(HttpStatusCode.OK, because: "developers can see the policy");
        (await client.PutAsJsonAsync(Base, new { scripts = new { mode = "Unrestricted" } })).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    private sealed class Host : WebApplicationFactory<Program>
    {
        private readonly (string Key, string Value)[] settings;

        public Host(params (string Key, string Value)[] settings) => this.settings = settings;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Persistence:Provider", "sqlite");
            builder.UseSetting("Persistence:ConnectionString", Workflow.Tests.TestSqlite.InMemory());
            foreach (var (key, value) in this.settings)
            {
                builder.UseSetting(key, value);
            }
        }
    }
}
