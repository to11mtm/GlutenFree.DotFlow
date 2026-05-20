// <copyright file="OAuth2Tests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.Http;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin.Http;
using Workflow.Modules.Builtin.Http.Auth;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

/// <summary>
/// 🔑 Phase 2.3.3 — OAuth2 Client Credentials tests~ ✨💖.
/// Token fetch + caching (module/pipeline scope) + refresh-on-401 + structured errors~ 🎀
/// </summary>
public sealed class OAuth2Tests : IDisposable
{
    private readonly WireMockServer _server;
    private readonly ServiceProvider _services;

    public OAuth2Tests()
    {
        _server = WireMockServer.Start();
        var sc = new ServiceCollection();
        sc.AddHttpModules();
        _services = sc.BuildServiceProvider();
    }

    public void Dispose()
    {
        _server.Stop();
        _server.Dispose();
        _services.Dispose();
    }

    private ModuleExecutionContext BuildContext(
        HttpRequestModule? module,
        Dictionary<string, object?> properties,
        Guid? executionId = null)
    {
        _ = module; // For symmetry — the test creates its own module so each test controls instance scope
        return new ModuleExecutionContext
        {
            Inputs = new Dictionary<string, object?>(),
            Properties = properties,
            Variables = new Dictionary<string, object?>(),
            Logger = NullLogger.Instance,
            Services = _services,
            ExecutionId = executionId ?? Guid.NewGuid(),
            NodeId = "oauth2-test-node",
        };
    }

    private void StubTokenEndpoint(string path, string accessToken, int expiresIn = 3600, string scope = "")
    {
        var bodyJson = scope.Length == 0
            ? $$"""{"access_token":"{{accessToken}}","token_type":"Bearer","expires_in":{{expiresIn}}}"""
            : $$"""{"access_token":"{{accessToken}}","token_type":"Bearer","expires_in":{{expiresIn}},"scope":"{{scope}}"}""";

        _server
            .Given(Request.Create().WithPath(path).UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(bodyJson));
    }

    private Dictionary<string, object?> BaseProps(string urlPath, string tokenPath, string scope = "")
    {
        var props = new Dictionary<string, object?>
        {
            ["url"] = $"{_server.Url}{urlPath}",
            ["method"] = "GET",
            ["authType"] = "oauth2",
            ["oauth2TokenUrl"] = $"{_server.Url}{tokenPath}",
            ["oauth2ClientId"] = "ami-client",
            ["oauth2ClientSecret"] = "sekret",
        };
        if (scope.Length > 0)
        {
            props["oauth2Scope"] = scope;
        }

        return props;
    }

    private int TokenEndpointHitCount(string tokenPath)
        => _server.LogEntries.Count(e => e.RequestMessage.Path == tokenPath && e.RequestMessage.Method == "POST");

    private int ProtectedEndpointHitCount(string path)
        => _server.LogEntries.Count(e => e.RequestMessage.Path == path && e.RequestMessage.Method == "GET");

    #region Token fetch + caching 🔑

    [Fact]
    public async Task OAuth2_FirstCall_FetchesTokenFromAuthority()
    {
        StubTokenEndpoint("/oauth/token", "tok-1");
        _server.Given(Request.Create().WithPath("/data").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{}"));

        var module = new HttpRequestModule();
        var ctx = BuildContext(module, BaseProps("/data", "/oauth/token"));

        var result = await module.ExecuteAsync(ctx, CancellationToken.None);

        result.Success.Should().BeTrue();
        TokenEndpointHitCount("/oauth/token").Should().Be(1);

        // Verify the protected call carried the Bearer token~
        var dataReq = _server.LogEntries.Single(e => e.RequestMessage.Path == "/data");
        string.Join(",", dataReq.RequestMessage.Headers!["Authorization"]).Should().Be("Bearer tok-1");
    }

    [Fact]
    public async Task OAuth2_SecondCall_UsesCachedToken_NoTokenFetch()
    {
        StubTokenEndpoint("/oauth/token", "tok-1");
        _server.Given(Request.Create().WithPath("/data").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{}"));

        var module = new HttpRequestModule();
        var props = BaseProps("/data", "/oauth/token");

        await module.ExecuteAsync(BuildContext(module, props), CancellationToken.None);
        await module.ExecuteAsync(BuildContext(module, props), CancellationToken.None);

        TokenEndpointHitCount("/oauth/token").Should().Be(1, "second call should reuse cached token~");
        ProtectedEndpointHitCount("/data").Should().Be(2);
    }

    [Fact]
    public async Task OAuth2_TokenCache_EvictionTimingRespectsExpiresIn()
    {
        // expires_in=1 minus 30s safety margin → already-expired the moment we cache it.
        // So the second call should refetch.
        StubTokenEndpoint("/oauth/token", "tok-short", expiresIn: 1);
        _server.Given(Request.Create().WithPath("/data").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{}"));

        var module = new HttpRequestModule();
        var props = BaseProps("/data", "/oauth/token");

        await module.ExecuteAsync(BuildContext(module, props), CancellationToken.None);
        await module.ExecuteAsync(BuildContext(module, props), CancellationToken.None);

        TokenEndpointHitCount("/oauth/token").Should().Be(2, "the safety-margin makes the short-TTL token expire immediately~");
    }

    [Fact]
    public async Task OAuth2_TokenExpired_RefetchesToken()
    {
        // Same shape as eviction-timing but more semantic: verifies an expired token triggers re-fetch
        StubTokenEndpoint("/oauth/token", "tok-x", expiresIn: 1);
        _server.Given(Request.Create().WithPath("/data").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{}"));

        var module = new HttpRequestModule();
        var props = BaseProps("/data", "/oauth/token");

        await module.ExecuteAsync(BuildContext(module, props), CancellationToken.None);
        await module.ExecuteAsync(BuildContext(module, props), CancellationToken.None);

        TokenEndpointHitCount("/oauth/token").Should().Be(2);
    }

    [Fact]
    public async Task OAuth2_DifferentScopes_CachedSeparately()
    {
        StubTokenEndpoint("/oauth/token", "tok-r", scope: "read");
        _server.Given(Request.Create().WithPath("/data").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{}"));

        var module = new HttpRequestModule();

        // Two calls with different scopes — should fetch twice
        await module.ExecuteAsync(BuildContext(module, BaseProps("/data", "/oauth/token", scope: "read")), CancellationToken.None);
        await module.ExecuteAsync(BuildContext(module, BaseProps("/data", "/oauth/token", scope: "write")), CancellationToken.None);

        TokenEndpointHitCount("/oauth/token").Should().Be(2, "different scopes → different cache keys~");
    }

    #endregion

    #region 401-refresh-and-retry 🔄

    [Fact]
    public async Task OAuth2_401Response_InvalidatesCacheAndRetries()
    {
        // Token endpoint always serves "tok-current" (different token each call would require WireMock scenarios).
        StubTokenEndpoint("/oauth/token", "tok-current");

        // First /data call returns 401, second returns 200 — uses WireMock's scenario state.
        const string scenario = "401-once";
        _server.Given(Request.Create().WithPath("/data").UsingGet())
            .InScenario(scenario).WillSetStateTo("after-401")
            .RespondWith(Response.Create().WithStatusCode(401));
        _server.Given(Request.Create().WithPath("/data").UsingGet())
            .InScenario(scenario).WhenStateIs("after-401")
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{\"ok\":true}"));

        var module = new HttpRequestModule();
        var ctx = BuildContext(module, BaseProps("/data", "/oauth/token"));

        var result = await module.ExecuteAsync(ctx, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Outputs["statusCode"].Should().Be(200);
        // Two protected calls (401 + retry-200), two token fetches (initial + after invalidation)~
        ProtectedEndpointHitCount("/data").Should().Be(2);
        TokenEndpointHitCount("/oauth/token").Should().Be(2);
    }

    [Fact]
    public async Task OAuth2_DoubleAuth401_Fails()
    {
        StubTokenEndpoint("/oauth/token", "tok-bad");
        // /data ALWAYS returns 401 → after one retry, module should give up and return the 401
        _server.Given(Request.Create().WithPath("/data").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(401));

        var module = new HttpRequestModule();
        var ctx = BuildContext(module, BaseProps("/data", "/oauth/token"));

        var result = await module.ExecuteAsync(ctx, CancellationToken.None);

        // The call SUCCEEDS at the transport level (HTTP completed), but statusCode is 401 + success=false~
        result.Success.Should().BeTrue("transport-level success — module did not throw");
        result.Outputs["statusCode"].Should().Be(401);
        result.Outputs["success"].Should().Be(false);
        ProtectedEndpointHitCount("/data").Should().Be(2, "exactly one retry after the first 401~");
    }

    #endregion

    #region Structured errors 💔

    [Fact]
    public async Task OAuth2_InvalidClient_ReturnsFail()
    {
        _server.Given(Request.Create().WithPath("/oauth/token").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(401)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"error":"invalid_client","error_description":"client auth failed"}"""));
        _server.Given(Request.Create().WithPath("/data").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200));

        var module = new HttpRequestModule();
        var ctx = BuildContext(module, BaseProps("/data", "/oauth/token"));

        var result = await module.ExecuteAsync(ctx, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("invalid_client");
        result.Exception.Should().BeOfType<OAuth2AuthorizationException>()
            .Which.ErrorCode.Should().Be("invalid_client");
    }

    #endregion

    #region Cache scope semantics 🌸

    [Fact]
    public async Task OAuth2_ModuleScope_FreshCachePerModuleInstance()
    {
        StubTokenEndpoint("/oauth/token", "tok-shared");
        _server.Given(Request.Create().WithPath("/data").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{}"));

        // Two MODULE instances → two PerModuleOAuth2TokenCache instances → two token fetches
        var moduleA = new HttpRequestModule();
        var moduleB = new HttpRequestModule();
        var props = BaseProps("/data", "/oauth/token");
        props["oauth2TokenCacheScope"] = "module"; // default, but explicit for clarity~

        await moduleA.ExecuteAsync(BuildContext(moduleA, props), CancellationToken.None);
        await moduleB.ExecuteAsync(BuildContext(moduleB, props), CancellationToken.None);

        TokenEndpointHitCount("/oauth/token").Should().Be(2, "module-scope caches are isolated per HttpRequestModule instance~");
    }

    [Fact]
    public async Task OAuth2_PipelineScope_SharesCacheAcrossModulesInSameExecution()
    {
        StubTokenEndpoint("/oauth/token", "tok-shared");
        _server.Given(Request.Create().WithPath("/data").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{}"));

        var sharedExecutionId = Guid.NewGuid();
        var moduleA = new HttpRequestModule();
        var moduleB = new HttpRequestModule();
        var props = BaseProps("/data", "/oauth/token");
        props["oauth2TokenCacheScope"] = "pipeline";

        await moduleA.ExecuteAsync(BuildContext(moduleA, props, executionId: sharedExecutionId), CancellationToken.None);
        await moduleB.ExecuteAsync(BuildContext(moduleB, props, executionId: sharedExecutionId), CancellationToken.None);

        TokenEndpointHitCount("/oauth/token").Should().Be(1, "pipeline-scope cache is shared across modules in the same execution~");
    }

    #endregion
}

