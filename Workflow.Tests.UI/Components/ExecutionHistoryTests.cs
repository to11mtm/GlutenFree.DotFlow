// <copyright file="ExecutionHistoryTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Components;

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Tests.UI.Api;
using Workflow.UI.Client.Api;
using Workflow.UI.Client.Api.Dtos;
using Workflow.UI.Client.Designer.Components;
using Workflow.UI.Client.Pages;
using Workflow.UI.Client.Designer.State;
using Workflow.UI.Client.Services;
using Xunit;

/// <summary>
/// 🧪 Phase 3.3.c.2 — Execution history panel + inspect-past-run specs~ ✨.
/// </summary>
public sealed class ExecutionHistoryTests : TestContext
{
    private static readonly Guid WfId = Guid.NewGuid();

    public ExecutionHistoryTests()
    {
        this.JSInterop.Mode = JSRuntimeMode.Loose;
        this.Services.AddSingleton(new ToastService());
        this.Services.AddSingleton(new AuthState());
        this.Services.AddSingleton(new PaletteDragState());
        this.Services.AddSingleton(new DesignerClipboard());
        this.Services.AddSingleton(new Workflow.UI.Client.Scripts.State.ScriptStudioHandoff());
        this.Services.AddSingleton(new ApiClientOptions { BaseUrl = "http://localhost" });
        this.Services.AddSingleton(sp => new RealTimeClient(sp.GetRequiredService<ApiClientOptions>(), sp.GetRequiredService<AuthState>()));
    }

    private static string J(object o) => JsonSerializer.Serialize(o, ApiHttp.Json);

    [Fact]
    public void History_ListsExecutions_PagedWithStates()
    {
        var page = new PageDto<ExecutionDto>(
            new List<ExecutionDto>
            {
                new(Guid.NewGuid(), WfId, "Completed", DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(-4), "user"),
                new(Guid.NewGuid(), WfId, "Failed", DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow.AddMinutes(-9), "user"),
            },
            2, 1, 20, 1);
        var handler = FakeHttpMessageHandler.Json(J(page));
        this.Services.AddSingleton(new ExecutionsClient(handler.CreateClient()));

        var cut = this.RenderComponent<ExecutionHistory>(p => p.Add(x => x.WorkflowId, WfId));

        cut.WaitForAssertion(() => cut.FindAll(".df-history__item").Should().HaveCount(2));
        cut.Markup.Should().Contain("✅").And.Contain("❌");
    }

    [Fact]
    public void History_Empty_ShowsEmptyState()
    {
        var page = new PageDto<ExecutionDto>(new List<ExecutionDto>(), 0, 1, 20, 0);
        var handler = FakeHttpMessageHandler.Json(J(page));
        this.Services.AddSingleton(new ExecutionsClient(handler.CreateClient()));

        var cut = this.RenderComponent<ExecutionHistory>(p => p.Add(x => x.WorkflowId, WfId));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("No executions yet"));
    }

    [Fact]
    public void History_Select_RaisesCallback()
    {
        var execId = Guid.NewGuid();
        var page = new PageDto<ExecutionDto>(
            new List<ExecutionDto> { new(execId, WfId, "Completed", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "u") },
            1, 1, 20, 1);
        var handler = FakeHttpMessageHandler.Json(J(page));
        this.Services.AddSingleton(new ExecutionsClient(handler.CreateClient()));

        Guid? selected = null;
        var cut = this.RenderComponent<ExecutionHistory>(p => p
            .Add(x => x.WorkflowId, WfId)
            .Add(x => x.OnSelect, id => selected = id));
        cut.WaitForAssertion(() => cut.FindAll($"[data-testid=exec-{execId}]").Should().NotBeEmpty());

        cut.Find($"[data-testid=exec-{execId}]").Click();

        selected.Should().Be(execId);
    }

    [Fact]
    public void DesignerPage_HistoryToggle_ThenInspect_EntersHistoryMode()
    {
        var execId = Guid.NewGuid();
        var workflow = new WorkflowDto(WfId, "wf", null, "1.0.0",
            new List<NodeDto> { new("n1", "builtin.http.request", "N1", new Dictionary<string, JsonElement>(), new PositionDto(100, 100)) },
            new List<ConnectionDto>(), new Dictionary<string, JsonElement>(), null, null, null, null, new List<string>());
        var modules = new List<ModuleSummaryDto> { new("builtin.http.request", "HTTP", "HTTP", "d", "🌐", "1.0.0") };
        var details = new ModuleDetailsDto("builtin.http.request", "HTTP", "HTTP", "d", "🌐", "1.0.0", new ModuleSchemaDto(new(), new(), new()), new List<string>());
        var execPage = new PageDto<ExecutionDto>(new List<ExecutionDto> { new(execId, WfId, "Completed", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "u") }, 1, 1, 20, 1);
        var status = new ExecutionStatusDto(execId, "Completed", 100, new Dictionary<string, string> { ["n1"] = "Completed" }, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null);

        var handler = new FakeHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            var query = req.RequestUri!.Query;
            string body = path switch
            {
                "/api/v1/modules" => J(modules),
                var p when p.StartsWith("/api/v1/modules/") => J(details),
                "/api/v1/executions" => J(execPage),
                var p when p.StartsWith("/api/v1/executions/") => J(status),
                var p when p.StartsWith("/api/v1/workflows/") => J(workflow),
                _ => "{}",
            };
            _ = query;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        });
        var client = handler.CreateClient();
        this.Services.AddSingleton(new WorkflowsClient(client));
        this.Services.AddSingleton(new ModulesClient(client));
        this.Services.AddSingleton(new ExecutionsClient(client));

        var cut = this.RenderComponent<Designer>(p => p.Add(x => x.Id, WfId.ToString()));
        cut.WaitForAssertion(() => cut.FindAll("[data-testid=history]").Should().NotBeEmpty());

        cut.Find("[data-testid=history]").Click();
        cut.WaitForAssertion(() => cut.FindAll($"[data-testid=exec-{execId}]").Should().NotBeEmpty());
        cut.Find($"[data-testid=exec-{execId}]").Click();

        // History mode: RunOverlay present (close button) + terminal → re-run button.
        cut.WaitForAssertion(() => cut.FindAll("[data-testid=run-close]").Should().NotBeEmpty());
        cut.FindAll("[data-testid=run-rerun]").Should().NotBeEmpty();
        // Node painted completed from the snapshot.
        cut.Find("[data-node-id=n1]").ClassList.Should().Contain("df-node--completed");
    }
}
