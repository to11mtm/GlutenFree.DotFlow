// <copyright file="MonitorTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Execution.Components;

using System;
using System.Net;
using System.Net.Http;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Tests.UI.Api;
using Workflow.UI.Client.Api;
using Workflow.UI.Client.Api.Dtos;
using Workflow.UI.Client.Execution.State;
using Workflow.UI.Client.Pages;
using Workflow.UI.Client.Services;
using Xunit;

/// <summary>
/// 📡 Phase 3.5.2 — bUnit tests for the monitor dashboard (row rendering, polling fallback,
/// open/cancel). The hub is unreachable in tests, so the page runs in polling mode~ ✨.
/// </summary>
public sealed class MonitorTests : TestContext
{
    private readonly MonitorState monitor = new();

    public MonitorTests()
    {
        this.JSInterop.Mode = JSRuntimeMode.Loose;
        this.Services.AddSingleton(new AuthState());
        this.Services.AddSingleton(new ToastService());
        this.Services.AddSingleton(new ApiClientOptions { BaseUrl = "http://localhost" });
        this.Services.AddSingleton(sp => new RealTimeClient(sp.GetRequiredService<ApiClientOptions>(), sp.GetRequiredService<AuthState>()));
        this.Services.AddSingleton(this.monitor);
    }

    private void UseHandler(FakeHttpMessageHandler handler)
        => this.Services.AddSingleton(new ExecutionsClient(handler.CreateClient()));

    private static MonitorRow Running(Guid id)
        => new() { ExecutionId = id, WorkflowId = Guid.NewGuid(), State = "Running", StartedAt = DateTimeOffset.UtcNow, Progress = 40, CurrentNode = "enrich" };

    [Fact]
    public void Monitor_RendersRows_FromState()
    {
        this.UseHandler(FakeHttpMessageHandler.Json("{}"));
        var runId = Guid.NewGuid();
        this.monitor.ApplyStarted(new ExecutionStartedEvent(runId, Guid.NewGuid(), DateTimeOffset.UtcNow));
        this.monitor.ApplyProgress(new ExecutionProgressEvent(runId, 40, "enrich", 1, 3, DateTimeOffset.UtcNow));
        this.monitor.ApplyCompleted(new ExecutionCompletedEvent(Guid.NewGuid(), Guid.NewGuid(), 900, DateTimeOffset.UtcNow));

        var cut = this.RenderComponent<ExecutionMonitor>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("1 running");
            cut.FindAll("[data-testid^=execrow-]").Should().HaveCount(2); // 1 live + 1 recent
        });
    }

    [Fact]
    public void Monitor_HubUnreachable_ShowsPollingIndicator()
    {
        this.UseHandler(FakeHttpMessageHandler.Json("{}"));

        var cut = this.RenderComponent<ExecutionMonitor>();

        cut.WaitForAssertion(() => cut.Find("[data-testid=mode-polling]").Should().NotBeNull());
    }

    [Fact]
    public void Monitor_RowOpen_NavigatesToDetail()
    {
        this.UseHandler(FakeHttpMessageHandler.Json("{}"));
        var id = Guid.NewGuid();
        this.monitor.SeedFromList(new[] { new ExecutionDto(id, Guid.NewGuid(), "Running", DateTimeOffset.UtcNow, null, null) });

        var cut = this.RenderComponent<ExecutionMonitor>();
        cut.WaitForAssertion(() => cut.Find("[data-testid=row-open]"));
        cut.Find("[data-testid=row-open]").Click();

        var nav = this.Services.GetRequiredService<NavigationManager>();
        nav.Uri.Should().EndWith($"/monitor/{id}");
    }

    [Fact]
    public void Monitor_Cancel_CallsClient()
    {
        var id = Guid.NewGuid();
        var handler = new FakeHttpMessageHandler(req =>
            req.Method == HttpMethod.Post
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"cancelled\":true}") }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        this.UseHandler(handler);
        this.monitor.SeedFromList(new[] { new ExecutionDto(id, Guid.NewGuid(), "Running", DateTimeOffset.UtcNow, null, null) });

        var cut = this.RenderComponent<ExecutionMonitor>();
        cut.WaitForAssertion(() => cut.Find("[data-testid=row-cancel]"));
        cut.Find("[data-testid=row-cancel]").Click();

        cut.WaitForAssertion(() => handler.Requests.Should().Contain(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.Contains($"/executions/{id}/cancel")));
    }

    [Fact]
    public void Filters_ApplyWithWorkflow_RequeriesList()
    {
        var wf = Guid.NewGuid();
        var handler = FakeHttpMessageHandler.Json("{\"items\":[],\"totalCount\":0,\"page\":1,\"pageSize\":20,\"totalPages\":0}");
        this.UseHandler(handler);

        var cut = this.RenderComponent<ExecutionMonitor>();
        cut.WaitForAssertion(() => cut.Find("[data-testid=filter-apply]"));

        cut.Find("[data-testid=filter-workflow]").Input(wf.ToString());
        cut.Find("[data-testid=filter-status]").Change("Failed");
        cut.Find("[data-testid=filter-apply]").Click();

        cut.WaitForAssertion(() =>
        {
            handler.Requests.Should().Contain(r =>
                r.Method == HttpMethod.Get &&
                r.RequestUri!.ToString().Contains($"workflowId={wf}") &&
                r.RequestUri!.ToString().Contains("status=Failed"));
        });
    }
}
