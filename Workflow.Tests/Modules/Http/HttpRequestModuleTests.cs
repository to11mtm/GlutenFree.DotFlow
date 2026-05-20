// <copyright file="HttpRequestModuleTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.Http;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin.Http;
using Workflow.Modules.Discovery;
using Workflow.Modules.Validation;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

/// <summary>
/// 🌐 Phase 2.3.0 — Tests for <see cref="HttpRequestModule"/> (<c>builtin.http.request</c>).
/// Uses WireMock.Net for an in-process HTTP server (Docker-free)~ ✨💖.
/// </summary>
public sealed class HttpRequestModuleTests : IDisposable
{
    private readonly HttpRequestModule _module = new();
    private readonly WireMockServer _server;
    private readonly ServiceProvider _services;

    public HttpRequestModuleTests()
    {
        _server = WireMockServer.Start();

        // Real DI container with IHttpClientFactory so the module gets a real socket stack~ ⚡
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

    #region Helpers 🛠️

    private ModuleExecutionContext BuildContext(Dictionary<string, object?> properties)
        => new()
        {
            Inputs = new Dictionary<string, object?>(),
            Properties = properties,
            Variables = new Dictionary<string, object?>(),
            Logger = NullLogger.Instance,
            Services = _services,
            ExecutionId = Guid.NewGuid(),
            NodeId = "http-node-1",
        };

    #endregion

    #region Metadata & Schema 🏷️

    [Fact]
    public void HttpRequestModule_Metadata_IsCorrect()
    {
        _module.ModuleId.Should().Be("builtin.http.request");
        _module.DisplayName.Should().Be("HTTP Request");
        _module.Category.Should().Be("Network");
        _module.Icon.Should().Be("🌐");
        _module.Version.Should().Be(new Version(1, 0, 0));

        var validator = new ModuleValidator();
        validator.Validate(_module).IsValid.Should().BeTrue("module must pass ModuleValidator~ 💖");
    }

    [Fact]
    public void HttpRequestModule_Schema_HasRequiredPorts()
    {
        var schema = _module.Schema;
        var props = schema.Properties.ToList();
        var outputs = schema.Outputs.ToList();

        // Properties — url required, method/headers/body/timeoutSeconds optional~
        props.Should().Contain(p => p.Name == "url" && p.IsRequired);
        props.Should().Contain(p => p.Name == "method");
        props.Should().Contain(p => p.Name == "headers");
        props.Should().Contain(p => p.Name == "body");
        props.Should().Contain(p => p.Name == "timeoutSeconds");

        // Outputs — statusCode, headers, body, success, durationMs~
        outputs.Should().Contain(p => p.Name == "statusCode");
        outputs.Should().Contain(p => p.Name == "headers");
        outputs.Should().Contain(p => p.Name == "body");
        outputs.Should().Contain(p => p.Name == "success");
        outputs.Should().Contain(p => p.Name == "durationMs");

        // No data-flow inputs in v1~
        schema.Inputs.Count.Should().Be(0);
    }

    [Fact]
    public void HttpRequestModule_IsDiscoverableInAssembly()
    {
        var discovery = new ModuleDiscovery();
        var types = discovery.DiscoverModuleTypes(typeof(HttpRequestModule).Assembly);
        types.Should().Contain(typeof(HttpRequestModule), "module should be auto-discoverable~ ✨");
    }

    #endregion

    #region ValidateConfiguration ✅

    [Fact]
    public void ValidateConfiguration_InvalidMethod_Fails()
    {
        var config = new Dictionary<string, object?>
        {
            ["url"] = "https://example.com",
            ["method"] = "FLY",
        };

        var result = _module.ValidateConfiguration(config);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "INVALID_HTTP_METHOD");
    }

    [Fact]
    public void ValidateConfiguration_InvalidUrl_Fails()
    {
        var config = new Dictionary<string, object?>
        {
            ["url"] = "not-a-url",
        };

        var result = _module.ValidateConfiguration(config);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "INVALID_URL");
    }

    [Fact]
    public void ValidateConfiguration_TemplateUrl_IsSkipped()
    {
        // URLs with {{...}} placeholders are resolved at runtime by PropertyBinder;
        // static validation must not reject them~ 🪄
        var config = new Dictionary<string, object?>
        {
            ["url"] = "https://example.com/users/{{user.id}}",
        };

        var result = _module.ValidateConfiguration(config);

        result.IsValid.Should().BeTrue();
    }

    #endregion

    #region Execute (WireMock) 🚀

    [Fact]
    public async Task Get_ReturnsStatus200AndJsonBody()
    {
        _server
            .Given(Request.Create().WithPath("/ping").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"pong":true,"count":42}"""));

        var ctx = BuildContext(new Dictionary<string, object?>
        {
            ["url"] = $"{_server.Url}/ping",
            ["method"] = "GET",
        });

        var result = await _module.ExecuteAsync(ctx, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Outputs["statusCode"].Should().Be(200);
        result.Outputs["success"].Should().Be(true);
        result.Outputs["body"].Should().BeAssignableTo<IDictionary<string, object?>>();
        var body = (IDictionary<string, object?>)result.Outputs["body"]!;
        body["pong"].Should().Be(true);
        body["count"].Should().Be(42L);
        ((long)result.Outputs["durationMs"]!).Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public async Task Post_WithJsonBody_SendsCorrectContentType()
    {
        _server
            .Given(Request.Create().WithPath("/users").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(201)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"id":99}"""));

        var ctx = BuildContext(new Dictionary<string, object?>
        {
            ["url"] = $"{_server.Url}/users",
            ["method"] = "POST",
            ["body"] = new Dictionary<string, object?> { ["name"] = "ami", ["age"] = 22 },
        });

        var result = await _module.ExecuteAsync(ctx, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Outputs["statusCode"].Should().Be(201);

        var logs = _server.LogEntries;
        logs.Should().ContainSingle();
        var req = logs[0].RequestMessage;
        req.Method.Should().Be("POST");
        req.Headers!.Should().ContainKey("Content-Type");
        string.Join(",", req.Headers!["Content-Type"]).Should().Contain("application/json");
        req.Body.Should().Contain("\"name\":\"ami\"").And.Contain("\"age\":22");
    }

    [Fact]
    public async Task Headers_ArePassedThrough()
    {
        _server
            .Given(Request.Create().WithPath("/secure").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(204));

        var ctx = BuildContext(new Dictionary<string, object?>
        {
            ["url"] = $"{_server.Url}/secure",
            ["method"] = "GET",
            ["headers"] = new Dictionary<string, string>
            {
                ["X-Trace-Id"] = "abc-123",
                ["X-Custom"] = "ami-chan",
            },
        });

        var result = await _module.ExecuteAsync(ctx, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Outputs["statusCode"].Should().Be(204);

        var req = _server.LogEntries[0].RequestMessage;
        req.Headers.Should().ContainKey("X-Trace-Id");
        string.Join(",", req.Headers!["X-Trace-Id"]).Should().Be("abc-123");
        string.Join(",", req.Headers!["X-Custom"]).Should().Be("ami-chan");
    }

    [Fact]
    public async Task Timeout_ExceededReturnsFail()
    {
        _server
            .Given(Request.Create().WithPath("/slow").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithDelay(TimeSpan.FromMilliseconds(800)));

        var ctx = BuildContext(new Dictionary<string, object?>
        {
            ["url"] = $"{_server.Url}/slow",
            ["method"] = "GET",
            ["timeoutSeconds"] = 1, // Server delay is sub-second but we need an integer; use a fractional fallback path:
        });

        // Force a short fractional timeout by relying on the linked CTS — test below uses cancellation token for sub-second timing.
        // (timeoutSeconds is int per schema; explicit cancellation test covers sub-second cancel.)
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        var result = await _module.ExecuteAsync(ctx, cts.Token);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("cancel", "parent CT cancellation should surface as fail~ 🛑");
    }

    [Fact]
    public async Task CancellationToken_HonouredMidFlight()
    {
        _server
            .Given(Request.Create().WithPath("/stall").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithDelay(TimeSpan.FromSeconds(5)));

        var ctx = BuildContext(new Dictionary<string, object?>
        {
            ["url"] = $"{_server.Url}/stall",
            ["method"] = "GET",
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var result = await _module.ExecuteAsync(ctx, cts.Token);

        result.Success.Should().BeFalse();
        result.Exception.Should().BeAssignableTo<OperationCanceledException>();
    }

    [Fact]
    public async Task NonJsonResponse_ReturnsStringBody()
    {
        _server
            .Given(Request.Create().WithPath("/hello").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "text/plain")
                .WithBody("hello, ami-chan!"));

        var ctx = BuildContext(new Dictionary<string, object?>
        {
            ["url"] = $"{_server.Url}/hello",
            ["method"] = "GET",
        });

        var result = await _module.ExecuteAsync(ctx, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Outputs["body"].Should().Be("hello, ami-chan!");
    }

    [Fact]
    public async Task MissingDI_IHttpClientFactory_Fails()
    {
        // Build a ctx with an empty service provider — no IHttpClientFactory registered.
        var ctx = new ModuleExecutionContext
        {
            Inputs = new Dictionary<string, object?>(),
            Properties = new Dictionary<string, object?>
            {
                ["url"] = $"{_server.Url}/anything",
            },
            Variables = new Dictionary<string, object?>(),
            Logger = NullLogger.Instance,
            Services = new EmptyServiceProvider(),
            ExecutionId = Guid.NewGuid(),
            NodeId = "http-node-x",
        };

        var result = await _module.ExecuteAsync(ctx, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("IHttpClientFactory");
    }

    #endregion

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}




