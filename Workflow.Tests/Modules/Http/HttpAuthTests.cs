// <copyright file="HttpAuthTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.Http;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin.Http;
using Workflow.Modules.Builtin.Http.Auth;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

/// <summary>
/// 🔐 Phase 2.3.2 — Auth strategy tests for <see cref="HttpRequestModule"/>~ ✨💖.
/// Covers Basic / Bearer / API Key (header + query) auth + log redaction~ 🎀
/// </summary>
public sealed class HttpAuthTests : IDisposable
{
    private readonly HttpRequestModule _module = new();
    private readonly WireMockServer _server;
    private readonly ServiceProvider _services;

    public HttpAuthTests()
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
        Dictionary<string, object?> properties,
        ILogger? logger = null)
        => new()
        {
            Inputs = new Dictionary<string, object?>(),
            Properties = properties,
            Variables = new Dictionary<string, object?>(),
            Logger = logger ?? NullLogger.Instance,
            Services = _services,
            ExecutionId = Guid.NewGuid(),
            NodeId = "http-auth-test",
        };

    private static string HeaderValue(WireMock.Logging.ILogEntry entry, string header)
        => entry.RequestMessage.Headers!.TryGetValue(header, out var v) ? string.Join(",", v) : string.Empty;

    #region Basic Auth 🔐

    [Fact]
    public async Task BasicAuth_Base64EncodedCorrectly()
    {
        _server
            .Given(Request.Create().WithPath("/basic").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200));

        var ctx = BuildContext(new Dictionary<string, object?>
        {
            ["url"] = $"{_server.Url}/basic",
            ["method"] = "GET",
            ["authType"] = "basic",
            ["username"] = "ami",
            ["password"] = "kawaii",
        });

        var result = await _module.ExecuteAsync(ctx, CancellationToken.None);

        result.Success.Should().BeTrue();
        var auth = HeaderValue(_server.LogEntries[0], "Authorization");
        var expected = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("ami:kawaii"));
        auth.Should().Be(expected);
    }

    [Fact]
    public async Task BasicAuth_MissingCredentials_Fails()
    {
        var ctx = BuildContext(new Dictionary<string, object?>
        {
            ["url"] = $"{_server.Url}/anything",
            ["authType"] = "basic",
            // username deliberately missing
            ["password"] = "kawaii",
        });

        var result = await _module.ExecuteAsync(ctx, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Basic auth");
    }

    #endregion

    #region Bearer Auth 🎟️

    [Fact]
    public async Task BearerAuth_AddsAuthorizationHeader()
    {
        _server
            .Given(Request.Create().WithPath("/bearer").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200));

        var ctx = BuildContext(new Dictionary<string, object?>
        {
            ["url"] = $"{_server.Url}/bearer",
            ["method"] = "GET",
            ["authType"] = "bearer",
            ["bearerToken"] = "secret-token-uwu",
        });

        var result = await _module.ExecuteAsync(ctx, CancellationToken.None);

        result.Success.Should().BeTrue();
        HeaderValue(_server.LogEntries[0], "Authorization").Should().Be("Bearer secret-token-uwu");
    }

    [Fact]
    public async Task BearerAuth_MissingToken_Fails()
    {
        var ctx = BuildContext(new Dictionary<string, object?>
        {
            ["url"] = $"{_server.Url}/anything",
            ["authType"] = "bearer",
            // bearerToken deliberately missing
        });

        var result = await _module.ExecuteAsync(ctx, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Bearer");
    }

    #endregion

    #region API Key 🗝️

    [Fact]
    public async Task ApiKeyAuth_InHeader_AddedToHeaders()
    {
        _server
            .Given(Request.Create().WithPath("/apikey").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200));

        var ctx = BuildContext(new Dictionary<string, object?>
        {
            ["url"] = $"{_server.Url}/apikey",
            ["method"] = "GET",
            ["authType"] = "apikey",
            ["apiKey"] = "k3y-v4l",
        });

        var result = await _module.ExecuteAsync(ctx, CancellationToken.None);

        result.Success.Should().BeTrue();
        HeaderValue(_server.LogEntries[0], "X-API-Key").Should().Be("k3y-v4l");
    }

    [Fact]
    public async Task ApiKeyAuth_InQuery_AppendedToUrl()
    {
        _server
            .Given(Request.Create().WithPath("/apikey-q").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200));

        var ctx = BuildContext(new Dictionary<string, object?>
        {
            ["url"] = $"{_server.Url}/apikey-q?existing=1",
            ["method"] = "GET",
            ["authType"] = "apikey",
            ["apiKey"] = "k3y-v4l",
            ["apiKeyHeader"] = "api_key",
            ["apiKeyLocation"] = "query",
        });

        var result = await _module.ExecuteAsync(ctx, CancellationToken.None);

        result.Success.Should().BeTrue();
        var url = _server.LogEntries[0].RequestMessage.Url;
        url.Should().Contain("api_key=k3y-v4l");
        url.Should().Contain("existing=1"); // pre-existing query preserved~
    }

    [Fact]
    public async Task ApiKeyAuth_CustomHeaderName_Honoured()
    {
        _server
            .Given(Request.Create().WithPath("/apikey-custom").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200));

        var ctx = BuildContext(new Dictionary<string, object?>
        {
            ["url"] = $"{_server.Url}/apikey-custom",
            ["method"] = "GET",
            ["authType"] = "apikey",
            ["apiKey"] = "secret-uwu",
            ["apiKeyHeader"] = "X-Custom-Auth",
        });

        var result = await _module.ExecuteAsync(ctx, CancellationToken.None);

        result.Success.Should().BeTrue();
        HeaderValue(_server.LogEntries[0], "X-Custom-Auth").Should().Be("secret-uwu");
    }

    #endregion

    #region None / Defaults 🚪

    [Fact]
    public async Task AuthType_None_NoAuthHeaderAdded()
    {
        _server
            .Given(Request.Create().WithPath("/noauth").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200));

        var ctx = BuildContext(new Dictionary<string, object?>
        {
            ["url"] = $"{_server.Url}/noauth",
            ["method"] = "GET",
            ["authType"] = "none",
        });

        var result = await _module.ExecuteAsync(ctx, CancellationToken.None);

        result.Success.Should().BeTrue();
        var headers = _server.LogEntries[0].RequestMessage.Headers!;
        headers.Should().NotContainKey("Authorization");
        headers.Should().NotContainKey("X-API-Key");
    }

    #endregion

    #region Log redaction 🔒

    [Fact]
    public void AuthHeaders_RedactedInDebugLog()
    {
        // RedactForLog is the unit of redaction — verify a representative set of headers~
        HttpAuthStrategyFactory.RedactForLog("Authorization", "Bearer secret").Should().Be("***REDACTED***");
        HttpAuthStrategyFactory.RedactForLog("authorization", "Basic xxx").Should().Be("***REDACTED***");
        HttpAuthStrategyFactory.RedactForLog("X-API-Key", "k3y").Should().Be("***REDACTED***");
        HttpAuthStrategyFactory.RedactForLog("Cookie", "session=abc").Should().Be("***REDACTED***");
        HttpAuthStrategyFactory.RedactForLog("X-Trace-Id", "abc-123").Should().Be("abc-123");

        // Also verify the bulk helper~
        var redacted = HttpAuthStrategyFactory.RedactHeaders(new[]
        {
            new KeyValuePair<string, string>("Authorization", "Bearer secret"),
            new KeyValuePair<string, string>("X-Trace-Id", "abc"),
        });
        redacted["Authorization"].Should().Be("***REDACTED***");
        redacted["X-Trace-Id"].Should().Be("abc");
    }

    #endregion
}

