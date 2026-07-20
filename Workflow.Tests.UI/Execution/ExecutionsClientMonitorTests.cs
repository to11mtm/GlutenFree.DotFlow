// <copyright file="ExecutionsClientMonitorTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Execution;

using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Workflow.Tests.UI.Api;
using Workflow.UI.Client.Api;
using Xunit;

/// <summary>
/// 📊 Phase 3.5.0 — Tests for the monitor additions to <see cref="ExecutionsClient"/>
/// (<c>/detail</c> + <c>/nodes</c> + the list filters)~ ✨.
/// </summary>
public sealed class ExecutionsClientMonitorTests
{
    [Fact]
    public async Task ExecutionsClient_Detail_Parses()
    {
        var execId = Guid.NewGuid();
        var wfId = Guid.NewGuid();
        var json = $$"""
        {
          "executionId": "{{execId}}",
          "workflowId": "{{wfId}}",
          "state": "Completed",
          "startedAt": "2026-07-20T16:31:02+00:00",
          "completedAt": "2026-07-20T16:31:03.4+00:00",
          "durationMs": 1400,
          "inputs": { "greeting": "hi" },
          "outputs": { "result": 42 },
          "error": null,
          "triggeredBy": "alice"
        }
        """;
        var client = new ExecutionsClient(FakeHttpMessageHandler.Json(json).CreateClient());

        var detail = await client.GetDetailAsync(execId);

        detail.Should().NotBeNull();
        detail!.State.Should().Be("Completed");
        detail.DurationMs.Should().Be(1400);
        detail.TriggeredBy.Should().Be("alice");
        detail.Inputs.Should().ContainKey("greeting");
        detail.Outputs.Should().ContainKey("result");
    }

    [Fact]
    public async Task ExecutionsClient_Detail_Unknown_ReturnsNull()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("{\"title\":\"Not found\"}", System.Text.Encoding.UTF8, "application/problem+json"),
        });
        var client = new ExecutionsClient(handler.CreateClient());

        (await client.GetDetailAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task ExecutionsClient_Nodes_Parses()
    {
        var json = """
        [
          { "nodeId": "validate", "state": "Completed", "startedAt": "2026-07-20T16:31:02+00:00", "completedAt": "2026-07-20T16:31:02.008+00:00", "durationMs": 8, "inputs": { "x": 1 }, "outputs": { "ok": true }, "error": null, "loopId": null, "loopIteration": null },
          { "nodeId": "enrich", "state": "Failed", "startedAt": "2026-07-20T16:31:02.5+00:00", "completedAt": "2026-07-20T16:31:02.541+00:00", "durationMs": 41, "inputs": { "id": 42 }, "outputs": null, "error": "TimeoutError", "loopId": null, "loopIteration": null }
        ]
        """;
        var client = new ExecutionsClient(FakeHttpMessageHandler.Json(json).CreateClient());

        var nodes = await client.GetNodesAsync(Guid.NewGuid());

        nodes.Should().HaveCount(2);
        nodes[0].NodeId.Should().Be("validate");
        nodes[0].Outputs.Should().ContainKey("ok");
        nodes[1].State.Should().Be("Failed");
        nodes[1].Error.Should().Be("TimeoutError");
    }

    [Fact]
    public async Task ExecutionsClient_List_AppendsStatusAndDateFilters()
    {
        var wfId = Guid.NewGuid();
        var handler = FakeHttpMessageHandler.Json("{\"items\":[],\"totalCount\":0,\"page\":1,\"pageSize\":20,\"totalPages\":0}");
        var client = new ExecutionsClient(handler.CreateClient());

        await client.ListAsync(wfId, status: "Failed", from: DateTimeOffset.Parse("2026-07-20T00:00:00Z"));

        var url = handler.Requests[0].RequestUri!.ToString();
        url.Should().Contain($"workflowId={wfId}");
        url.Should().Contain("status=Failed");
        url.Should().Contain("from=");
    }
}
