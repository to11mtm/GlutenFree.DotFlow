// <copyright file="MonitoringEndpointsTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Api.V1;

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Persistence.Abstractions;
using Workflow.Persistence.Models;
using Xunit;

/// <summary>
/// 📊 Phase 2.7.5 — API integration tests for health / readiness / liveness / status / metrics~ ✨.
/// </summary>
public sealed class MonitoringEndpointsTests
{
    [Fact]
    public async Task Health_WithProvider_Returns200()
    {
        using var factory = new SqliteFactory();
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/api/v1/health");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("components").TryGetProperty("persistence", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Health_NoProvider_IsNotUnhealthy()
    {
        using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        // No provider → persistence is Degraded, actor-system is Healthy → overall not 503~
        var resp = await client.GetAsync("/api/v1/health");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ready_RunsPersistenceCheck()
    {
        using var factory = new SqliteFactory();
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/api/v1/health/ready");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("components").TryGetProperty("persistence", out _).Should().BeTrue();
        body.GetProperty("components").TryGetProperty("actor-system", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Live_AlwaysReturnsOk()
    {
        using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/api/v1/health/live");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("components").TryGetProperty("actor-system", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Status_ReturnsProviderModuleCountAndUptime()
    {
        using var factory = new SqliteFactory();
        var client = factory.CreateClient();

        var body = await client.GetFromJsonAsync<JsonElement>("/api/v1/status");
        body.GetProperty("persistence").GetProperty("provider").GetString().Should().NotBeNullOrEmpty();
        body.GetProperty("moduleCount").GetInt32().Should().BeGreaterThan(0);
        body.GetProperty("uptimeSeconds").GetDouble().Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task ModuleCount_MatchesRegistry()
    {
        using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();
        var registry = factory.Services.GetRequiredService<Workflow.Modules.Abstractions.IModuleRegistry>();

        var body = await client.GetFromJsonAsync<JsonElement>("/api/v1/status");
        body.GetProperty("moduleCount").GetInt32().Should().Be(registry.GetAllModules().Count);
    }

    [Fact]
    public async Task Metrics_ReflectsExecutionCounters()
    {
        using var factory = new SqliteFactory();
        var client = factory.CreateClient();

        // Create + run a workflow so the started counter increments~
        var wf = ExecutionEndpointsTests.PassthroughWorkflowJson("metrics-" + Guid.NewGuid().ToString("N"));
        var create = await client.PostAsync("/api/v1/workflows", wf);
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var wfId = created.GetProperty("id").GetGuid();

        await client.PostAsJsonAsync($"/api/v1/workflows/{wfId}/execute/sync?timeoutSeconds=30", new { inputs = new { } });

        var metrics = await client.GetFromJsonAsync<Dictionary<string, long>>("/api/v1/metrics");
        metrics!["executions_started_total"].Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Health_ProviderUnhealthy_Returns503()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IPersistenceProvider>(new FailingProvider());
            });
        });
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/api/v1/health");
        resp.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    private sealed class SqliteFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Persistence:Provider"] = "sqlite",
                    ["Persistence:ConnectionString"] = ":memory:",
                });
            });
        }
    }

    private sealed class FailingProvider : IPersistenceProvider
    {
        public string ProviderName => "failing";

        public bool IsInitialized => true;

        public IWorkflowRepository Workflows => throw new NotSupportedException();

        public IExecutionHistoryRepository ExecutionHistory => throw new NotSupportedException();

        public IVariableStore Variables => throw new NotSupportedException();

        public IBlobStore? Blobs => null;

        public IWebhookRegistrationRepository? Webhooks => null;

        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<HealthCheckResult> HealthCheckAsync(CancellationToken ct = default)
            => Task.FromResult(new HealthCheckResult(false, "failing", TimeSpan.Zero, "simulated failure"));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
