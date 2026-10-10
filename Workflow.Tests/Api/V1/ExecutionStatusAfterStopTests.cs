// <copyright file="ExecutionStatusAfterStopTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Api.V1;

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Engine.Models;
using Xunit;

/// <summary>
/// §12.2: finished executors stop, so status for a done run comes from execution history. That
/// answer must still carry the final state AND per-node states, not "not found".
/// </summary>
public sealed class ExecutionStatusAfterStopTests : IClassFixture<ExecutionStatusAfterStopTests.ZeroGraceApiFactory>
{
    private readonly ZeroGraceApiFactory factory;

    public ExecutionStatusAfterStopTests(ZeroGraceApiFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Status_AfterExecutorStopped_ComesFromHistory_WithNodeStates()
    {
        var client = this.factory.CreateClient();
        var created = await client.PostAsync(
            "/api/v1/workflows",
            ExecutionEndpointsTests.PassthroughWorkflowJson("stopped-" + Guid.NewGuid().ToString("N")));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var wfId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var start = await client.PostAsJsonAsync($"/api/v1/workflows/{wfId}/execute/sync?timeoutSeconds=30", new { inputs = new { } });
        var execId = (await start.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("executionId").GetGuid();

        // Zero grace: the executor stops as soon as its writes drain. Give it ample time to be gone.
        await Task.Delay(TimeSpan.FromSeconds(1));

        var status = await client.GetAsync($"/api/v1/executions/{execId}");
        status.StatusCode.Should().Be(HttpStatusCode.OK, because: "a stopped run's status falls back to history");
        var body = await status.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("state").GetString().Should().Be("Completed");
        body.GetProperty("nodeStates").GetProperty("n1").GetString().Should().Be(
            "Completed",
            because: "the history fallback should still report per-node states");
    }

    /// <summary>SQLite-backed API whose executors stop immediately after a run finishes.</summary>
    public sealed class ZeroGraceApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Persistence:Provider"] = "sqlite",
                    ["Persistence:ConnectionString"] = Workflow.Tests.TestSqlite.InMemory(),
                });
            });
            builder.ConfigureServices(services =>
                services.AddSingleton(new ExecutorLifecycleOptions { TerminalGracePeriod = TimeSpan.Zero }));
        }
    }
}
