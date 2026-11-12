// <copyright file="LoopBodyHistoryTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Api.V1;

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

/// <summary>
/// A node that runs once per loop iteration keeps one history row per iteration in a real store
/// (SQLite here). The in-memory fakes the engine tests use append every record, so they never saw
/// the stores collapse those rows into one.
/// </summary>
public sealed class LoopBodyHistoryTests : IClassFixture<ExecutionEndpointsTests.EngineApiFactory>
{
    private readonly ExecutionEndpointsTests.EngineApiFactory factory;

    public LoopBodyHistoryTests(ExecutionEndpointsTests.EngineApiFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task ALoopBodyNode_HasOneRowPerIteration_WithThatIterationsInputs()
    {
        var client = this.factory.CreateClient();
        var json = """
            {
              "name": "loop-history-NAME",
              "version": "1.0.0",
              "nodes": [
                { "id": "loop", "moduleId": "builtin.loop.foreach", "name": "Loop", "properties": { "collection": ["a", "b", "c"] } },
                { "id": "body", "moduleId": "builtin.delay", "name": "Body", "properties": { "durationMs": 150 } }
              ],
              "connections": [
                { "sourceNodeId": "loop", "sourcePortName": "loopBody", "targetNodeId": "body", "targetPortName": "input" }
              ],
              "variables": {}
            }
            """.Replace("NAME", Guid.NewGuid().ToString("N"), StringComparison.Ordinal);
        var create = await client.PostAsync("/api/v1/workflows", new StringContent(json, Encoding.UTF8, "application/json"));
        create.StatusCode.Should().Be(HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var run = await client.PostAsJsonAsync($"/api/v1/workflows/{id}/execute/sync?timeoutSeconds=30", new { inputs = new { } });
        var status = await run.Content.ReadFromJsonAsync<JsonElement>();
        status.GetProperty("state").GetString().Should().Be("Completed", status.ToString());
        var executionId = status.GetProperty("executionId").GetGuid();

        // Body rows are written fire-and-forget, so give them a moment to land.
        JsonElement[] body = Array.Empty<JsonElement>();
        for (var i = 0; i < 50 && body.Length < 3; i++)
        {
            var nodes = await client.GetFromJsonAsync<JsonElement>($"/api/v1/executions/{executionId}/nodes");
            body = nodes.EnumerateArray().Where(r => r.GetProperty("nodeId").GetString() == "body").ToArray();
            if (body.Length < 3)
            {
                await Task.Delay(100);
            }
        }

        body.Select(r => r.GetProperty("loopIteration").GetInt32()).Should().BeEquivalentTo(new[] { 1, 2, 3 }, "one row per iteration");
        body.Select(r => r.GetProperty("inputs").GetProperty("item").GetString()).Should().BeEquivalentTo(new[] { "a", "b", "c" },
            "each row keeps its own iteration's inputs");
        body.Should().OnlyContain(r => r.GetProperty("state").GetString() == "Completed");
    }
}
