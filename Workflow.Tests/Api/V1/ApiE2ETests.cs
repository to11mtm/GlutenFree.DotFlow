// <copyright file="ApiE2ETests.cs" company="GlutenFree">
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
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

/// <summary>
/// 🧪 Phase 2.7.8 — End-to-end smoke: create → execute → poll → read execution → variable → modules → health~ ✨.
/// </summary>
public sealed class ApiE2ETests : IClassFixture<ApiE2ETests.E2EFactory>
{
    private readonly E2EFactory factory;

    public ApiE2ETests(E2EFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task E2E_CreateExecutePollReadVariable_Works()
    {
        var client = this.factory.CreateClient();

        // 1️⃣ Create a workflow~
        var create = await client.PostAsync("/api/v1/workflows", ExecutionEndpointsTests.PassthroughWorkflowJson("e2e-" + Guid.NewGuid().ToString("N")));
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var wfId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // 2️⃣ Execute synchronously to a terminal state~
        var run = await client.PostAsJsonAsync($"/api/v1/workflows/{wfId}/execute/sync?timeoutSeconds=30", new { inputs = new { } });
        run.StatusCode.Should().Be(HttpStatusCode.OK);
        var execId = (await run.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("executionId").GetGuid();

        // 3️⃣ Read the execution status back~
        var status = await client.GetFromJsonAsync<JsonElement>($"/api/v1/executions/{execId}");
        status.GetProperty("state").GetString().Should().Be("Completed");

        // 4️⃣ List executions for the workflow~
        var list = await client.GetFromJsonAsync<JsonElement>($"/api/v1/executions?workflowId={wfId}");
        list.GetProperty("totalCount").GetInt32().Should().BeGreaterThanOrEqualTo(1);

        // 5️⃣ Set + read a variable~
        var varName = "e2e-var-" + Guid.NewGuid().ToString("N");
        var put = await client.PutAsync($"/api/v1/variables/{varName}?scope=global", JsonContent.Create(new { value = 42 }));
        put.StatusCode.Should().Be(HttpStatusCode.OK);
        var got = await client.GetFromJsonAsync<JsonElement>($"/api/v1/variables/{varName}?scope=global");
        got.GetProperty("value").GetInt32().Should().Be(42);

        // 6️⃣ List modules~
        var modules = await client.GetFromJsonAsync<List<JsonElement>>("/api/v1/modules");
        modules.Should().NotBeNullOrEmpty();

        // 7️⃣ Health is 200~
        (await client.GetAsync("/api/v1/health")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    public sealed class E2EFactory : WebApplicationFactory<Program>
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
}
