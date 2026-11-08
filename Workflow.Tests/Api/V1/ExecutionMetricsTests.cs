// <copyright file="ExecutionMetricsTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Api.V1;

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using LanguageExt;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Core.Models;
using Workflow.Modules.Loading;
using Workflow.Persistence.Sqlite.Serialization;
using Xunit;

/// <summary>
/// §9.3 — execution metrics are right for every way a run starts (async API, sync API, webhook)
/// and every way it ends (completed, failed, cancelled). Terminal outcomes come from the engine's
/// EventStream via <c>ExecutionMetricsRecorder</c>, so <c>executions_active</c> returns to zero and
/// the hot-reload drain gate clears. Each test uses its own host so the counters start at zero.
/// </summary>
public sealed class ExecutionMetricsTests
{
    private static readonly JsonSerializerOptions WfJson = LanguageExtJsonConverters.CreateOptions();

    [Fact]
    public async Task AsyncRun_WhenComplete_ReturnsActiveCountToZero()
    {
        using var host = new SqliteHost();
        var client = host.CreateClient();
        var wfId = await CreateWorkflowAsync(client, Node("n1", "builtin.passthrough"));

        var start = await client.PostAsJsonAsync($"/api/v1/workflows/{wfId}/execute", new { inputs = new { } });
        start.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var metrics = await WaitForMetricsAsync(client, m => m["executions_completed_total"] == 1);
        metrics["executions_started_total"].Should().Be(1);
        metrics["executions_active"].Should().Be(0, because: "a completed async run must decrement the active count (§9.3)");
    }

    [Fact]
    public async Task SyncRun_IsCountedOnce()
    {
        using var host = new SqliteHost();
        var client = host.CreateClient();
        var wfId = await CreateWorkflowAsync(client, Node("n1", "builtin.passthrough"));

        var run = await client.PostAsJsonAsync($"/api/v1/workflows/{wfId}/execute/sync?timeoutSeconds=30", new { inputs = new { } });
        run.EnsureSuccessStatusCode();

        var metrics = await WaitForMetricsAsync(client, m => m["executions_completed_total"] >= 1);
        await Task.Delay(300); // a double count would land right after the first~
        metrics = await GetMetricsAsync(client);
        metrics["executions_completed_total"].Should().Be(1, because: "the wait path no longer records terminal outcomes itself");
        metrics["executions_active"].Should().Be(0);
    }

    [Fact]
    public async Task WebhookRun_CountsAsStarted_AndReturnsActiveCountToZero()
    {
        using var host = new SqliteHost();
        var client = host.CreateClient();
        var wfId = await CreateWorkflowAsync(client, Node("n1", "builtin.passthrough"));
        var webhookId = $"metrics-{Guid.NewGuid():N}";
        (await client.PostAsJsonAsync("/api/webhooks", new { webhookId, workflowDefinitionId = wfId, allowedMethods = new[] { "POST" } }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        (await client.PostAsJsonAsync($"/webhooks/{webhookId}", new { orderId = "o-1" }))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);

        var metrics = await WaitForMetricsAsync(client, m => m["executions_completed_total"] == 1);
        metrics["executions_started_total"].Should().Be(1, because: "webhook launches now count as started");
        metrics["executions_active"].Should().Be(0);
    }

    [Fact]
    public async Task FailedRun_IsCountedAsFailed()
    {
        using var host = new SqliteHost();
        var client = host.CreateClient();
        var wfId = await CreateWorkflowAsync(client, Node("boom", "builtin.throw", ("message", "intentional")));

        await client.PostAsJsonAsync($"/api/v1/workflows/{wfId}/execute", new { inputs = new { } });

        var metrics = await WaitForMetricsAsync(client, m => m["executions_failed_total"] == 1);
        metrics["executions_completed_total"].Should().Be(0);
        metrics["executions_active"].Should().Be(0);
    }

    [Fact]
    public async Task CancelledRun_IsCountedAsCancelled()
    {
        using var host = new SqliteHost();
        var client = host.CreateClient();
        var wfId = await CreateWorkflowAsync(client, Node("wait", "builtin.delay", ("durationMs", 30_000)));

        var start = await client.PostAsJsonAsync($"/api/v1/workflows/{wfId}/execute", new { inputs = new { } });
        var execId = (await start.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("executionId").GetGuid();
        await WaitForMetricsAsync(client, m => m["executions_active"] == 1);

        (await client.PostAsync($"/api/v1/executions/{execId}/cancel", null)).IsSuccessStatusCode.Should().BeTrue();

        var metrics = await WaitForMetricsAsync(client, m => m["executions_cancelled_total"] == 1);
        metrics["executions_failed_total"].Should().Be(0, because: "a user cancel is not a failure");
        metrics["executions_active"].Should().Be(0);
    }

    [Fact]
    public async Task HotReloadDrainGate_ClearsAfterAnAsyncRun()
    {
        using var host = new SqliteHost();
        var client = host.CreateClient();
        var tracker = host.Services.GetRequiredService<IActiveExecutionTracker>();
        var wfId = await CreateWorkflowAsync(client, Node("n1", "builtin.passthrough"));

        await client.PostAsJsonAsync($"/api/v1/workflows/{wfId}/execute", new { inputs = new { } });
        await WaitForMetricsAsync(client, m => m["executions_completed_total"] == 1);

        tracker.HasActiveExecutions.Should().BeFalse(
            because: "the hot-reload drain gate must not report activity once runs have finished (§9.3)");
    }

    private static NodeDefinition Node(string id, string moduleId, params (string Key, object? Value)[] properties)
    {
        var props = HashMap<string, JsonElement>.Empty;
        foreach (var (key, value) in properties)
        {
            props = props.AddOrUpdate(key, JsonSerializer.SerializeToElement(value));
        }

        return new NodeDefinition(id, moduleId, id, props);
    }

    private static async Task<Guid> CreateWorkflowAsync(HttpClient client, NodeDefinition node)
    {
        var definition = new WorkflowDefinition(
            Id: Guid.Empty,
            Name: "metrics-" + Guid.NewGuid().ToString("N"),
            Description: null,
            Version: new Version(1, 0, 0),
            Nodes: Arr.create(node),
            Connections: Arr<ConnectionDefinition>.Empty,
            Variables: HashMap<string, VariableDefinition>.Empty);
        var content = new StringContent(JsonSerializer.Serialize(definition, WfJson), Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/v1/workflows", content);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<Dictionary<string, long>> GetMetricsAsync(HttpClient client)
        => (await client.GetFromJsonAsync<Dictionary<string, long>>("/api/v1/metrics"))!;

    /// <summary>Terminal metrics land asynchronously (EventStream), so poll for the expected state.</summary>
    private static async Task<Dictionary<string, long>> WaitForMetricsAsync(HttpClient client, Func<Dictionary<string, long>, bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        var metrics = await GetMetricsAsync(client);
        while (!condition(metrics) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
            metrics = await GetMetricsAsync(client);
        }

        condition(metrics).Should().BeTrue($"metrics never reached the expected state: {JsonSerializer.Serialize(metrics)}");
        return metrics;
    }

    private sealed class SqliteHost : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Persistence:Provider"] = "sqlite",
                    ["Persistence:ConnectionString"] = Workflow.Tests.TestSqlite.InMemory(),
                });
            });
        }
    }
}
