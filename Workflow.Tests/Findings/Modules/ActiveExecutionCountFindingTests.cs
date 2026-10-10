// <copyright file="ActiveExecutionCountFindingTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Findings.Modules;

using System;
using System.Collections.Generic;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Workflow.Tests.Api.V1;
using Xunit;

/// <summary>
/// Finding 04 §9.3 — the active-execution counter only drops for synchronous runs.
/// </summary>
/// <remarks>
/// <c>ActorWorkflowExecutionService</c> records "started" for every run but records a terminal
/// outcome only in <c>StartAndWaitAsync</c> (the sync path). After an ASYNC run finishes,
/// <c>executions_active</c> never returns to zero, which also keeps the hot-reload drain gate
/// (<c>MetricsActiveExecutionTracker</c>) reporting activity forever. This test starts one async
/// run, waits for it to complete, and asserts <c>executions_active</c> falls back to 0. In-memory
/// SQLite host; no external services.
/// </remarks>
[Trait("Category", FindingDefs.Category)]
public sealed class ActiveExecutionCountFindingTests
{
    [Fact]
    public async Task AsyncRun_WhenComplete_ShouldReturnActiveCountToZero()
    {
        using var factory = new SqliteHost();
        var client = factory.CreateClient();

        var wf = ExecutionEndpointsTests.PassthroughWorkflowJson("active-" + Guid.NewGuid().ToString("N"));
        var create = await client.PostAsync("/api/v1/workflows", wf);
        create.EnsureSuccessStatusCode();
        var created = await create.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var wfId = created.GetProperty("id").GetGuid();

        // Async start (not the wait-for-result endpoint).
        var start = await client.PostAsJsonAsync($"/api/v1/workflows/{wfId}/execute", new { inputs = new { } });
        start.EnsureSuccessStatusCode();
        var startBody = await start.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var execId = startBody.GetProperty("executionId").GetGuid();

        // Wait until the run itself is terminal.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            var status = await client.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/v1/executions/{execId}");
            var state = status.GetProperty("state").GetString();
            if (state is "Completed" or "Failed" or "Cancelled")
            {
                break;
            }

            await Task.Delay(150);
        }

        // Give the service a moment to settle any post-completion bookkeeping.
        await Task.Delay(500);

        var metrics = await client.GetFromJsonAsync<Dictionary<string, long>>("/api/v1/metrics");
        metrics!["executions_active"].Should().Be(
            0,
            because: "a completed async run should decrement the active-execution counter (04 §9.3)");
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
