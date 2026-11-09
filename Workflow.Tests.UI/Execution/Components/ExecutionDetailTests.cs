// <copyright file="ExecutionDetailTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Execution.Components;

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Tests.UI.Api;
using Workflow.UI.Client.Api;
using Workflow.UI.Client.Api.Dtos;
using Workflow.UI.Client.Execution.Components;
using Workflow.UI.Client.Pages;
using Workflow.UI.Client.Services;
using Xunit;

/// <summary>
/// 🔎 Phase 3.5.3 — bUnit tests for the execution detail page + node inspector~ ✨.
/// </summary>
public sealed class ExecutionDetailTests : TestContext
{
    public ExecutionDetailTests()
    {
        this.JSInterop.Mode = JSRuntimeMode.Loose;
        this.Services.AddSingleton(new AuthState());
        this.Services.AddSingleton(new ToastService());
        this.Services.AddSingleton(new ApiClientOptions { BaseUrl = "http://localhost" });
        this.Services.AddSingleton(sp => new RealTimeClient(sp.GetRequiredService<ApiClientOptions>(), sp.GetRequiredService<AuthState>()));
    }

    private static string DetailJson(Guid id, Guid wf, string state) => $$"""
        { "executionId": "{{id}}", "workflowId": "{{wf}}", "state": "{{state}}", "startedAt": "2026-07-20T16:31:02Z", "completedAt": "2026-07-20T16:31:03.4Z", "durationMs": 1400, "inputs": {}, "outputs": {}, "error": null, "triggeredBy": "alice" }
        """;

    private const string NodesJson = """
        [
          { "nodeId": "validate", "state": "Completed", "startedAt": "2026-07-20T16:31:02Z", "completedAt": "2026-07-20T16:31:02.008Z", "durationMs": 8, "inputs": { "x": 1 }, "outputs": { "ok": true }, "error": null, "loopId": null, "loopIteration": null },
          { "nodeId": "enrich", "state": "Failed", "startedAt": "2026-07-20T16:31:02.5Z", "completedAt": "2026-07-20T16:31:02.541Z", "durationMs": 41, "inputs": { "id": 42 }, "outputs": null, "error": "TimeoutError: upstream", "loopId": null, "loopIteration": null }
        ]
        """;

    private void UseHandler(Guid id, Guid wf, string state)
    {
        var handler = new FakeHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.EndsWith("/nodes"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(NodesJson) };
            }

            if (path.EndsWith("/detail"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(DetailJson(id, wf, state)) };
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        });
        this.Services.AddSingleton(new ExecutionsClient(handler.CreateClient()));
    }

    [Fact]
    public void Detail_Historical_LoadsNodes_ShowsHeader()
    {
        var id = Guid.NewGuid();
        var wf = Guid.NewGuid();
        this.UseHandler(id, wf, "Failed");

        var cut = this.RenderComponent<ExecutionDetail>(p => p.Add(x => x.ExecutionId, id.ToString()));

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid=detail-state]").TextContent.Should().Be("Failed");
            cut.Find("[data-testid=node-validate]").Should().NotBeNull();
            cut.Find("[data-testid=node-enrich]").Should().NotBeNull();
        });
    }

    [Fact]
    public void NodeInspector_Select_ShowsInputsOutputsError()
    {
        var id = Guid.NewGuid();
        this.UseHandler(id, Guid.NewGuid(), "Failed");

        var cut = this.RenderComponent<ExecutionDetail>(p => p.Add(x => x.ExecutionId, id.ToString()));
        cut.WaitForAssertion(() => cut.Find("[data-testid=node-enrich]"));

        // First node auto-selected → validate's I/O.
        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid=node-inputs]").TextContent.Should().Contain("x");
            cut.Find("[data-testid=node-outputs]").TextContent.Should().Contain("ok");
        });

        // Select the failed node → error shows.
        cut.Find("[data-testid=node-enrich]").Click();
        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid=node-error]").TextContent.Should().Contain("TimeoutError");
            cut.Find("[data-testid=node-inputs]").TextContent.Should().Contain("42");
        });
    }

    [Fact]
    public void Detail_Running_ShowsCancel()
    {
        var id = Guid.NewGuid();
        this.UseHandler(id, Guid.NewGuid(), "Running");

        var cut = this.RenderComponent<ExecutionDetail>(p => p.Add(x => x.ExecutionId, id.ToString()));

        cut.WaitForAssertion(() => cut.Find("[data-testid=detail-cancel]").Should().NotBeNull());
    }

    [Fact]
    public void Detail_Unknown_ShowsNotFound()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("{\"title\":\"Not found\"}", System.Text.Encoding.UTF8, "application/problem+json"),
        });
        this.Services.AddSingleton(new ExecutionsClient(handler.CreateClient()));

        var cut = this.RenderComponent<ExecutionDetail>(p => p.Add(x => x.ExecutionId, Guid.NewGuid().ToString()));

        cut.WaitForAssertion(() => cut.Find("[data-testid=detail-error]").TextContent.Should().Contain("not found"));
    }

    [Fact]
    public void Detail_OpenInDesigner_Navigates()
    {
        var id = Guid.NewGuid();
        var wf = Guid.NewGuid();
        this.UseHandler(id, wf, "Completed");

        var cut = this.RenderComponent<ExecutionDetail>(p => p.Add(x => x.ExecutionId, id.ToString()));
        cut.WaitForAssertion(() => cut.Find("[data-testid=open-in-designer]"));
        cut.Find("[data-testid=open-in-designer]").Click();

        this.Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith($"/designer/{wf}");
    }

    /// <summary>📡 Stage 7.6 — the detail page shows per-stage streaming item counts as they arrive.</summary>
    [Fact]
    public void Detail_ShowsStreamingItemCounts_LiveThenFinal()
    {
        var id = Guid.NewGuid();
        this.UseHandler(id, Guid.NewGuid(), "Running");
        var cut = this.RenderComponent<ExecutionDetail>(p => p.Add(x => x.ExecutionId, id.ToString()));
        cut.WaitForAssertion(() => cut.Find("[data-testid=detail-state]"));
        cut.FindAll("[data-testid=stream-progress]").Should().BeEmpty();
        var realTime = this.Services.GetRequiredService<RealTimeClient>();

        RaiseStreamProgress(realTime, new StreamProgressEvent(Guid.NewGuid(), 0, new Dictionary<string, long> { ["src"] = 9 }, 100, false, DateTimeOffset.UtcNow));
        cut.FindAll("[data-testid=stream-progress]").Should().BeEmpty(because: "another execution's counts");

        RaiseStreamProgress(realTime, new StreamProgressEvent(id, 0, new Dictionary<string, long> { ["src"] = 1234, ["sink"] = 1200 }, 1500, false, DateTimeOffset.UtcNow));
        cut.WaitForAssertion(() =>
        {
            var row = cut.Find("[data-testid=stream-stage-src]").TextContent;
            row.Should().Contain(1234.ToString("N0")).And.Contain("running");
        });

        RaiseStreamProgress(realTime, new StreamProgressEvent(id, 0, new Dictionary<string, long> { ["src"] = 2000, ["sink"] = 2000 }, 2600, true, DateTimeOffset.UtcNow));
        cut.WaitForAssertion(() =>
        {
            var row = cut.Find("[data-testid=stream-stage-sink]").TextContent;
            row.Should().Contain(2000.ToString("N0")).And.Contain("done");
        });
    }

    /// <summary>Raises <see cref="RealTimeClient.StreamProgress"/> as the hub connection would.</summary>
    private static void RaiseStreamProgress(RealTimeClient client, StreamProgressEvent e)
    {
        var field = typeof(RealTimeClient).GetField(nameof(RealTimeClient.StreamProgress), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        ((Action<StreamProgressEvent>?)field.GetValue(client))?.Invoke(e);
    }
}
