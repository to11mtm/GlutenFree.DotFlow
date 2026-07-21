// <copyright file="ExecutionEndpointsTests.cs" company="GlutenFree">
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
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Workflow.Core.Models;
using Workflow.Persistence.Sqlite.Serialization;
using Xunit;

/// <summary>
/// ⚡ Phase 2.7.2 — API integration tests for execution start/status/cancel/list over SQLite + the
/// real actor engine~ ✨.
/// </summary>
public sealed class ExecutionEndpointsTests : IClassFixture<ExecutionEndpointsTests.EngineApiFactory>
{
    private static readonly JsonSerializerOptions WfJson = LanguageExtJsonConverters.CreateOptions();
    private readonly EngineApiFactory factory;

    public ExecutionEndpointsTests(EngineApiFactory factory)
    {
        this.factory = factory;
    }

    private static WorkflowDefinition PassthroughWorkflow(string name)
        => new(
            Id: Guid.Empty,
            Name: name,
            Description: null,
            Version: new Version(1, 0, 0),
            Nodes: Arr.create(new NodeDefinition("n1", "builtin.passthrough", "N1", HashMap<string, JsonElement>.Empty)),
            Connections: Arr<ConnectionDefinition>.Empty,
            Variables: HashMap<string, VariableDefinition>.Empty);

    /// <summary>Builds a POST body for a single-node passthrough workflow (shared with monitoring tests)~ 🧩.</summary>
    /// <param name="name">The workflow name.</param>
    /// <returns>JSON content ready to POST to <c>/api/v1/workflows</c>.</returns>
    public static HttpContent PassthroughWorkflowJson(string name)
        => new StringContent(JsonSerializer.Serialize(PassthroughWorkflow(name), WfJson), Encoding.UTF8, "application/json");

    private async Task<Guid> CreateWorkflowAsync(HttpClient client, string name)
    {
        var content = new StringContent(JsonSerializer.Serialize(PassthroughWorkflow(name), WfJson), Encoding.UTF8, "application/json");
        var resp = await client.PostAsync("/api/v1/workflows", content);
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Execute_ReturnsAcceptedWithExecutionIdAndLocation()
    {
        var client = this.factory.CreateClient();
        var wfId = await this.CreateWorkflowAsync(client, "exec-" + Guid.NewGuid().ToString("N"));

        var resp = await client.PostAsJsonAsync($"/api/v1/workflows/{wfId}/execute", new { inputs = new { greeting = "hi" } });

        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        resp.Headers.Location.Should().NotBeNull();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("executionId").GetGuid().Should().NotBeEmpty();
    }

    [Fact]
    public async Task Execute_UnknownWorkflow_Returns404()
    {
        var client = this.factory.CreateClient();
        var resp = await client.PostAsJsonAsync($"/api/v1/workflows/{Guid.NewGuid()}/execute", new { inputs = new { } });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ExecuteSync_CompletesWithinTimeout_ReturnsFinalStatus()
    {
        var client = this.factory.CreateClient();
        var wfId = await this.CreateWorkflowAsync(client, "sync-" + Guid.NewGuid().ToString("N"));

        var resp = await client.PostAsJsonAsync($"/api/v1/workflows/{wfId}/execute/sync?timeoutSeconds=30", new { inputs = new { x = 1 } });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("state").GetString().Should().Be("Completed");
    }

    [Fact]
    public async Task Status_AfterCompletion_ReturnsCompleted()
    {
        var client = this.factory.CreateClient();
        var wfId = await this.CreateWorkflowAsync(client, "status-" + Guid.NewGuid().ToString("N"));

        var start = await client.PostAsJsonAsync($"/api/v1/workflows/{wfId}/execute/sync?timeoutSeconds=30", new { inputs = new { } });
        var startBody = await start.Content.ReadFromJsonAsync<JsonElement>();
        var execId = startBody.GetProperty("executionId").GetGuid();

        var status = await client.GetAsync($"/api/v1/executions/{execId}");
        status.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await status.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("state").GetString().Should().Be("Completed");
    }

    [Fact]
    public async Task Status_UnknownExecution_Returns404()
    {
        var client = this.factory.CreateClient();
        (await client.GetAsync($"/api/v1/executions/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Cancel_UnknownExecution_Returns404()
    {
        var client = this.factory.CreateClient();
        (await client.PostAsync($"/api/v1/executions/{Guid.NewGuid()}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ExecuteByName_ResolvesNewestActive()
    {
        var client = this.factory.CreateClient();
        var name = "named-" + Guid.NewGuid().ToString("N");
        await this.CreateWorkflowAsync(client, name);

        var resp = await client.PostAsJsonAsync($"/api/v1/workflows/execute/{name}", new { inputs = new { } });
        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task ExecuteByName_Unknown_Returns404()
    {
        var client = this.factory.CreateClient();
        var resp = await client.PostAsJsonAsync($"/api/v1/workflows/execute/nope-{Guid.NewGuid():N}", new { inputs = new { } });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListExecutions_ByWorkflow_ReturnsRows()
    {
        var client = this.factory.CreateClient();
        var wfId = await this.CreateWorkflowAsync(client, "list-exec-" + Guid.NewGuid().ToString("N"));
        await client.PostAsJsonAsync($"/api/v1/workflows/{wfId}/execute/sync?timeoutSeconds=30", new { inputs = new { } });

        var resp = await client.GetAsync($"/api/v1/executions?workflowId={wfId}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("totalCount").GetInt32().Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task ListExecutions_MissingWorkflowId_Returns400()
    {
        var client = this.factory.CreateClient();
        (await client.GetAsync("/api/v1/executions")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Execute_PersistsTriggeredBy_FromCallerId()
    {
        var client = this.factory.CreateClient();
        var wfId = await this.CreateWorkflowAsync(client, "trig-" + Guid.NewGuid().ToString("N"));

        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/workflows/{wfId}/execute/sync?timeoutSeconds=30")
        {
            Content = JsonContent.Create(new { inputs = new { } }),
        };
        req.Headers.Add("X-Caller-Id", "alice");
        var start = await client.SendAsync(req);
        var startBody = await start.Content.ReadFromJsonAsync<JsonElement>();
        var execId = startBody.GetProperty("executionId").GetGuid();

        var list = await client.GetAsync($"/api/v1/executions?workflowId={wfId}");
        var body = await list.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("items");
        var found = false;
        foreach (var item in items.EnumerateArray())
        {
            if (item.GetProperty("executionId").GetGuid() == execId)
            {
                item.GetProperty("triggeredBy").GetString().Should().Be("alice");
                found = true;
            }
        }

        found.Should().BeTrue();
    }

    /// <summary>
    /// A factory with SQLite persistence (the real actor engine boots as part of the host)~ 🗄️.
    /// </summary>
    public sealed class EngineApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
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
}
