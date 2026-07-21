// <copyright file="HttpRetryTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.Http;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin.Http;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

/// <summary>
/// 🔄 Phase 2.3.4 — Resilience tests for <see cref="HttpRequestModule"/>~ ✨💖.
/// Retry / Retry-After header / circuit breaker / timeout / attemptCount output~ 🎀
/// </summary>
public sealed class HttpRetryTests : IDisposable
{
    private readonly WireMockServer _server;
    private readonly ServiceProvider _services;

    public HttpRetryTests()
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

    private ModuleExecutionContext BuildContext(Dictionary<string, object?> properties)
        => new()
        {
            Inputs = new Dictionary<string, object?>(),
            Properties = properties,
            Variables = new Dictionary<string, object?>(),
            Logger = NullLogger.Instance,
            Services = _services,
            ExecutionId = Guid.NewGuid(),
            NodeId = "http-retry-test",
        };

    private int HitCount(string path)
        => _server.LogEntries.Count(e => e.RequestMessage.Path == path);

    #region Retry 🔁

    [Fact]
    public async Task Retry_OnTransient500_RetriesAndSucceeds()
    {
        // First call → 500, second → 200
        const string scenario = "500-once";
        _server.Given(Request.Create().WithPath("/x").UsingGet())
            .InScenario(scenario).WillSetStateTo("after-500")
            .RespondWith(Response.Create().WithStatusCode(500));
        _server.Given(Request.Create().WithPath("/x").UsingGet())
            .InScenario(scenario).WhenStateIs("after-500")
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{}"));

        var module = new HttpRequestModule();
        var result = await module.ExecuteAsync(BuildContext(new Dictionary<string, object?>
        {
            ["url"] = $"{_server.Url}/x",
            ["retryCount"] = 2,
            ["retryDelaySeconds"] = 0.01,
            ["retryBackoff"] = "constant",
        }), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Outputs["statusCode"].Should().Be(200);
        result.Outputs["attemptCount"].Should().Be(2);
        HitCount("/x").Should().Be(2);
    }

    [Fact]
    public async Task Retry_OnPermanent404_DoesNotRetry()
    {
        _server.Given(Request.Create().WithPath("/missing").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(404));

        var module = new HttpRequestModule();
        var result = await module.ExecuteAsync(BuildContext(new Dictionary<string, object?>
        {
            ["url"] = $"{_server.Url}/missing",
            ["retryCount"] = 3,
            ["retryDelaySeconds"] = 0.01,
        }), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Outputs["statusCode"].Should().Be(404);
        result.Outputs["attemptCount"].Should().Be(1, "404 is not in the default retry set~");
        HitCount("/missing").Should().Be(1);
    }

    [Fact]
    public async Task Retry_MaxAttemptsExceeded_FailsWithLastError()
    {
        _server.Given(Request.Create().WithPath("/always500").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(500));

        var module = new HttpRequestModule();
        var result = await module.ExecuteAsync(BuildContext(new Dictionary<string, object?>
        {
            ["url"] = $"{_server.Url}/always500",
            ["retryCount"] = 2,
            ["retryDelaySeconds"] = 0.01,
        }), CancellationToken.None);

        // Transport-level success; statusCode is 500
        result.Success.Should().BeTrue();
        result.Outputs["statusCode"].Should().Be(500);
        result.Outputs["attemptCount"].Should().Be(3, "1 initial + 2 retries~");
        HitCount("/always500").Should().Be(3);
    }

    [Fact]
    public async Task Retry_ExponentialBackoff_DelaysIncrease()
    {
        _server.Given(Request.Create().WithPath("/eb").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(500));

        var module = new HttpRequestModule();
        var sw = Stopwatch.StartNew();
        var result = await module.ExecuteAsync(BuildContext(new Dictionary<string, object?>
        {
            ["url"] = $"{_server.Url}/eb",
            ["retryCount"] = 2,
            ["retryDelaySeconds"] = 0.1,
            ["retryBackoff"] = "exponential",
        }), CancellationToken.None);
        sw.Stop();

        // Exponential w/ initial 100ms: attempt1 → 100ms wait → attempt2 → ~200ms wait → attempt3
        // With jitter (Polly's default), the total should be at least ~150ms but well under 5s
        result.Outputs["attemptCount"].Should().Be(3);
        sw.ElapsedMilliseconds.Should().BeGreaterThan(150, "exponential backoff should produce visible delays~");
        sw.ElapsedMilliseconds.Should().BeLessThan(5000, "but not crazy long for a 100ms initial delay~");
    }

    #endregion

    #region Retry-After header 🎀

    [Fact]
    public async Task Retry_RetryAfterHeader_WithinCap_HonouredOverBackoff()
    {
        // 429 with Retry-After: 1 second. Configured backoff is 10ms (linear), cap is 30s.
        // Expect retry to wait ~1s (the header value), not 10ms (the backoff).
        const string scenario = "ra-once";
        _server.Given(Request.Create().WithPath("/ra").UsingGet())
            .InScenario(scenario).WillSetStateTo("after-429")
            .RespondWith(Response.Create().WithStatusCode(429).WithHeader("Retry-After", "1"));
        _server.Given(Request.Create().WithPath("/ra").UsingGet())
            .InScenario(scenario).WhenStateIs("after-429")
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{}"));

        var module = new HttpRequestModule();
        var sw = Stopwatch.StartNew();
        var result = await module.ExecuteAsync(BuildContext(new Dictionary<string, object?>
        {
            ["url"] = $"{_server.Url}/ra",
            ["retryCount"] = 1,
            ["retryDelaySeconds"] = 0.01,
            ["retryBackoff"] = "constant",
            ["maxRetryBackoffSeconds"] = 30.0,
        }), CancellationToken.None);
        sw.Stop();

        result.Success.Should().BeTrue();
        result.Outputs["statusCode"].Should().Be(200);
        sw.ElapsedMilliseconds.Should().BeGreaterThan(900, "Retry-After: 1s should be honoured~");
    }

    [Fact]
    public async Task Retry_RetryAfterHeader_ExceedsCap_FallsBackToConfiguredBackoff()
    {
        // Server says wait 600s — but our cap is 1s. Should fall back to configured (10ms) backoff.
        const string scenario = "ra-cap";
        _server.Given(Request.Create().WithPath("/racap").UsingGet())
            .InScenario(scenario).WillSetStateTo("after")
            .RespondWith(Response.Create().WithStatusCode(429).WithHeader("Retry-After", "600"));
        _server.Given(Request.Create().WithPath("/racap").UsingGet())
            .InScenario(scenario).WhenStateIs("after")
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{}"));

        var module = new HttpRequestModule();
        var sw = Stopwatch.StartNew();
        var result = await module.ExecuteAsync(BuildContext(new Dictionary<string, object?>
        {
            ["url"] = $"{_server.Url}/racap",
            ["retryCount"] = 1,
            ["retryDelaySeconds"] = 0.01,
            ["retryBackoff"] = "constant",
            ["maxRetryBackoffSeconds"] = 1.0,
        }), CancellationToken.None);
        sw.Stop();

        result.Success.Should().BeTrue();
        result.Outputs["statusCode"].Should().Be(200);
        sw.ElapsedMilliseconds.Should().BeLessThan(2000, "Retry-After of 600s exceeded cap → fall back to short backoff~");
    }

    #endregion

    #region Timeout ⏱️

    [Fact]
    public async Task Timeout_AbortsRequestAfterDuration()
    {
        _server.Given(Request.Create().WithPath("/slow").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithDelay(TimeSpan.FromSeconds(3)));

        // Use the existing per-request timeout (Phase 2.3.0) — Polly timeout strategy not added in 2.3.4 (would duplicate)
        var module = new HttpRequestModule();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        var result = await module.ExecuteAsync(BuildContext(new Dictionary<string, object?>
        {
            ["url"] = $"{_server.Url}/slow",
        }), cts.Token);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("cancel");
    }

    [Fact]
    public async Task Timeout_CancellationToken_Honoured()
    {
        _server.Given(Request.Create().WithPath("/stall").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithDelay(TimeSpan.FromSeconds(3)));

        var module = new HttpRequestModule();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var result = await module.ExecuteAsync(BuildContext(new Dictionary<string, object?>
        {
            ["url"] = $"{_server.Url}/stall",
            ["retryCount"] = 5, // even with retry configured, cancellation should win~
        }), cts.Token);

        result.Success.Should().BeFalse();
        result.Exception.Should().BeAssignableTo<OperationCanceledException>();
    }

    #endregion

    #region Circuit Breaker 🛑

    [Fact]
    public async Task CircuitBreaker_OpensAfterThresholdFailures()
    {
        // Hit threshold: 3 consecutive 500s should open the breaker; next call short-circuits.
        _server.Given(Request.Create().WithPath("/cb").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(500));

        var module = new HttpRequestModule();
        var props = new Dictionary<string, object?>
        {
            ["url"] = $"{_server.Url}/cb",
            ["circuitBreakerFailureThreshold"] = 3,
            ["circuitBreakerSamplingDurationSeconds"] = 30.0,
        };

        // Three failing calls — they all go through, last one opens the circuit
        for (int i = 0; i < 3; i++)
        {
            await module.ExecuteAsync(BuildContext(props), CancellationToken.None);
        }

        // Fourth call should be short-circuited via BrokenCircuitException
        var blocked = await module.ExecuteAsync(BuildContext(props), CancellationToken.None);
        blocked.Success.Should().BeFalse();
        blocked.ErrorMessage.Should().Contain("circuit");
    }

    [Fact]
    public async Task CircuitBreaker_HalfOpenAllowsTestRequest()
    {
        // Open the breaker, wait for BreakDuration (500ms in factory), verify a probe request goes through.
        _server.Given(Request.Create().WithPath("/cbho").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(500));

        var module = new HttpRequestModule();
        var props = new Dictionary<string, object?>
        {
            ["url"] = $"{_server.Url}/cbho",
            ["circuitBreakerFailureThreshold"] = 2,
            ["circuitBreakerSamplingDurationSeconds"] = 30.0,
        };

        await module.ExecuteAsync(BuildContext(props), CancellationToken.None);
        await module.ExecuteAsync(BuildContext(props), CancellationToken.None);

        // Immediately blocked
        var blocked = await module.ExecuteAsync(BuildContext(props), CancellationToken.None);
        blocked.Success.Should().BeFalse();

        // Wait past the break duration (factory uses 500ms)
        await Task.Delay(600);

        // Now switch to 200 — probe request should succeed via half-open state
        _server.ResetMappings();
        _server.Given(Request.Create().WithPath("/cbho").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{}"));

        var probe = await module.ExecuteAsync(BuildContext(props), CancellationToken.None);
        probe.Success.Should().BeTrue();
        probe.Outputs["statusCode"].Should().Be(200);
    }

    #endregion

    #region Outputs 📊

    [Fact]
    public async Task AttemptCount_OutputReflectsActualAttempts()
    {
        _server.Given(Request.Create().WithPath("/ac").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{}"));

        var module = new HttpRequestModule();
        var result = await module.ExecuteAsync(BuildContext(new Dictionary<string, object?>
        {
            ["url"] = $"{_server.Url}/ac",
        }), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Outputs["attemptCount"].Should().Be(1, "successful first call → 1 attempt~");
        result.Outputs["circuitState"].Should().Be("closed");
    }

    #endregion
}

