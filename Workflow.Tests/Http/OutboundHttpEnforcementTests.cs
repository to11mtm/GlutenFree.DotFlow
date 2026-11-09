// <copyright file="OutboundHttpEnforcementTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Http;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Workflow.Core.Http;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin.Http;
using Workflow.Scripting.Abstractions;
using Workflow.Scripting.Api;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

/// <summary>
/// Stage 7.3 — the policy is enforced on real requests (a WireMock server on 127.0.0.1): at connect
/// time on the resolved address (so names that resolve internally and redirects are caught), on
/// every redirect hop by host rules, with per-node narrowing, for the HTTP module and script HTTP.
/// </summary>
public sealed class OutboundHttpEnforcementTests : IDisposable
{
    private readonly WireMockServer server = WireMockServer.Start();

    public OutboundHttpEnforcementTests()
    {
        this.server.Given(Request.Create().WithPath("/ok").UsingAnyMethod())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("hello"));
        this.server.Given(Request.Create().WithPath("/to-localhost").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(302).WithHeader("Location", $"http://localhost:{this.Port}/ok"));
        this.server.Given(Request.Create().WithPath("/echo-auth").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(req => req.Headers?.ContainsKey("Authorization") == true ? "auth" : "no-auth"));
        this.server.Given(Request.Create().WithPath("/to-localhost-auth").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(302).WithHeader("Location", $"http://localhost:{this.Port}/echo-auth"));
    }

    private int Port => this.server.Ports[0];

    private string Url(string path, string host = "127.0.0.1") => $"http://{host}:{this.Port}{path}";

    public void Dispose() => this.server.Stop();

    private static HttpClient Client(EffectiveOutboundPolicy policy)
        => new(OutboundHttp.CreatePipeline(policy.Channel, () => policy));

    private static EffectiveOutboundPolicy Policy(OutboundHttpMode mode, string[]? allowedHosts = null, string[]? allowedCidrs = null, string[]? deniedHosts = null)
        => new(OutboundHttpChannel.HttpModule, mode, true, allowedHosts ?? [], deniedHosts ?? [], allowedCidrs ?? [], [], false, []);

    [Fact]
    public async Task PublicOnly_BlocksANameThatResolvesToAnInternalAddress()
    {
        using var client = Client(Policy(OutboundHttpMode.PublicOnly));

        var send = () => client.GetAsync(this.Url("/ok", "localhost"));

        (await send.Should().ThrowAsync<OutboundHttpBlockedException>())
            .Which.Message.Should().Contain("internal address").And.Contain("PublicOnly");
        this.server.LogEntries.Should().BeEmpty(because: "nothing was sent");
    }

    [Fact]
    public async Task AnAllowedRange_LetsItThrough()
    {
        using var client = Client(Policy(OutboundHttpMode.PublicOnly, allowedCidrs: ["127.0.0.1/32", "::1"]));

        (await client.GetStringAsync(this.Url("/ok", "localhost"))).Should().Be("hello");
    }

    [Fact]
    public async Task ARedirectToAnAddressThatIsntAllowed_IsBlockedAtConnectTime()
    {
        // 127.0.0.1 is allowed by name; the redirect goes to "localhost", which isn't.
        using var client = Client(Policy(OutboundHttpMode.AllowlistOnly, allowedHosts: ["127.0.0.1"]));

        var send = () => client.GetAsync(this.Url("/to-localhost"));

        (await send.Should().ThrowAsync<OutboundHttpBlockedException>()).Which.Host.Should().Be("localhost");
        this.server.LogEntries.Select(e => e.RequestMessage.Path).Should().Equal("/to-localhost");
    }

    [Fact]
    public async Task ARedirectToADeniedHost_IsBlockedOnThatHop()
    {
        using var client = Client(Policy(OutboundHttpMode.Unrestricted, deniedHosts: ["localhost"]));

        var send = () => client.GetAsync(this.Url("/to-localhost"));

        (await send.Should().ThrowAsync<OutboundHttpBlockedException>()).Which.Reason.Should().Contain("denied host");
    }

    [Fact]
    public async Task Redirects_AreFollowed_ButCredentialsDontCrossHosts()
    {
        using var client = Client(Policy(OutboundHttpMode.Unrestricted));
        using var request = new HttpRequestMessage(HttpMethod.Get, this.Url("/to-localhost-auth"));
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer secret");

        var response = await client.SendAsync(request);

        (await response.Content.ReadAsStringAsync()).Should().Be("no-auth");
    }

    [Fact]
    public async Task PerNodeNarrowing_CanOnlyNarrow()
    {
        using var client = Client(Policy(OutboundHttpMode.Unrestricted));
        using var request = new HttpRequestMessage(HttpMethod.Get, this.Url("/ok"));
        request.Options.Set(OutboundHttpRequestOptions.AllowedHosts, (IReadOnlyCollection<string>)["api.partner.com"]);

        var send = () => client.SendAsync(request);

        (await send.Should().ThrowAsync<OutboundHttpBlockedException>()).Which.Reason.Should().Contain("this node's allowed hosts");
    }

    [Fact]
    public async Task TheHttpModule_ReportsTheBlock_AndDoesntRetryIt()
    {
        var services = new ServiceCollection();
        services.AddHttpModules();
        var counting = new CountingProvider(new StaticOutboundHttpPolicyProvider(null)); // unconfigured = PublicOnly
        services.AddSingleton<IOutboundHttpPolicyProvider>(counting);
        var provider = services.BuildServiceProvider();
        var module = new HttpRequestModule();

        var result = await module.ExecuteAsync(new ModuleExecutionContext
        {
            NodeId = "call",
            ExecutionId = Guid.NewGuid(),
            Properties = new Dictionary<string, object?> { ["url"] = this.Url("/ok"), ["method"] = "GET", ["retryCount"] = 3, ["retryDelaySeconds"] = 0.01 },
            Inputs = new Dictionary<string, object?>(),
            Variables = new Dictionary<string, object?>(),
            Services = provider,
            Logger = NullLogger.Instance,
        });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("blocked by the HttpModule policy").And.Contain("PublicOnly");
        this.server.LogEntries.Should().BeEmpty();
        counting.Calls.Should().BeLessThanOrEqualTo(2, because: "one attempt (host check + connect) — a policy block isn't retried");
    }

    private sealed class CountingProvider(IOutboundHttpPolicyProvider inner) : IOutboundHttpPolicyProvider
    {
        private int calls;

        public int Calls => this.calls;

        public EffectiveOutboundPolicy Get(OutboundHttpChannel channel)
        {
            System.Threading.Interlocked.Increment(ref this.calls);
            return inner.Get(channel);
        }
    }

    [Fact]
    public async Task TheHttpModule_HonoursPerNodeAllowedHosts()
    {
        var services = new ServiceCollection();
        services.AddHttpModules();
        services.AddSingleton<IOutboundHttpPolicyProvider>(StaticOutboundHttpPolicyProvider.Unrestricted);
        var provider = services.BuildServiceProvider();
        var module = new HttpRequestModule();

        var result = await module.ExecuteAsync(new ModuleExecutionContext
        {
            NodeId = "call",
            ExecutionId = Guid.NewGuid(),
            Properties = new Dictionary<string, object?> { ["url"] = this.Url("/ok"), ["method"] = "GET", ["allowedHosts"] = "[\"api.partner.com\"]" },
            Inputs = new Dictionary<string, object?>(),
            Variables = new Dictionary<string, object?>(),
            Services = provider,
            Logger = NullLogger.Instance,
        });

        result.ErrorMessage.Should().Contain("this node's allowed hosts");
    }

    [Fact]
    public async Task ScriptHttp_UsesTheScriptsPolicy_EvenWithoutAClientFactory()
    {
        WorkflowScriptApi Api(IOutboundHttpPolicyProvider policy, string[]? allowedHosts = null) => new(new WorkflowScriptApiOptions
        {
            Variables = new Dictionary<string, object?>(),
            Config = new ScriptExecutionConfig { AllowNetwork = true, MaxHttpRequests = 5, AllowedHosts = allowedHosts ?? [] },
            ExecutionId = Guid.NewGuid(),
            NodeId = "script",
            Logger = NullLogger.Instance,
            OutboundPolicy = policy,
        });

        var blocked = () => Api(new StaticOutboundHttpPolicyProvider(null)).HttpGetAsync(this.Url("/ok"));
        (await blocked.Should().ThrowAsync<OutboundHttpBlockedException>()).Which.Channel.Should().Be(OutboundHttpChannel.Scripts);

        var narrowed = () => Api(StaticOutboundHttpPolicyProvider.Unrestricted, ["api.partner.com"]).HttpGetAsync(this.Url("/ok"));
        await narrowed.Should().ThrowAsync<OutboundHttpBlockedException>();

        var ok = await Api(StaticOutboundHttpPolicyProvider.Unrestricted).HttpGetAsync(this.Url("/ok"));
        ok.Should().BeAssignableTo<IReadOnlyDictionary<string, object?>>().Which["status"].Should().Be(200);
    }
}
