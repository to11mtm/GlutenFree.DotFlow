// <copyright file="RunFlowTests.cs" company="GlutenFree">
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
using Workflow.UI.Client.Designer.State;
using Workflow.UI.Client.Pages;
using Workflow.UI.Client.Services;
using Xunit;

/// <summary>
/// 🧪 Phase 3.3.c.0 — Execute-from-designer + run-mode specs~ ✨.
/// </summary>
public sealed class RunFlowTests : TestContext
{
    private static readonly Guid WfId = Guid.NewGuid();

    public RunFlowTests()
    {
        this.JSInterop.Mode = JSRuntimeMode.Loose;
        this.Services.AddSingleton(new ToastService());
        this.Services.AddSingleton(new AuthState());
        this.Services.AddSingleton(new PaletteDragState());
        this.Services.AddSingleton(new DesignerClipboard());
        this.Services.AddSingleton(new ApiClientOptions { BaseUrl = "http://localhost" });
        this.Services.AddSingleton(sp => new RealTimeClient(sp.GetRequiredService<ApiClientOptions>(), sp.GetRequiredService<AuthState>()));
    }

    private static string J(object o) => JsonSerializer.Serialize(o, ApiHttp.Json);

    private static WorkflowDto Workflow()
        => new(
            WfId, "wf", null, "1.0.0",
            new List<NodeDto> { new("n1", "builtin.http.request", "N1", new Dictionary<string, JsonElement>(), new PositionDto(100, 100)) },
            new List<ConnectionDto>(), new Dictionary<string, JsonElement>(), null, null, null, null, new List<string>());

    private FakeHttpMessageHandler Setup()
    {
        var modules = new List<ModuleSummaryDto> { new("builtin.http.request", "HTTP", "HTTP", "d", "🌐", "1.0.0") };
        var details = new ModuleDetailsDto("builtin.http.request", "HTTP", "HTTP", "d", "🌐", "1.0.0",
            new ModuleSchemaDto(new(), new(), new()), new List<string>());
        var started = new ExecutionStartedDto(Guid.NewGuid(), "accepted");

        var handler = new FakeHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            string body = path switch
            {
                "/api/v1/modules" => J(modules),
                var p when p.StartsWith("/api/v1/modules/") => J(details),
                var p when p.EndsWith("/execute") => J(started),
                var p when p.Contains("/executions/") && p.EndsWith("/cancel") => "{}",
                var p when p.StartsWith("/api/v1/workflows/") => J(Workflow()),
                _ => "{}",
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        });

        var client = handler.CreateClient();
        this.Services.AddSingleton(new WorkflowsClient(client));
        this.Services.AddSingleton(new ModulesClient(client));
        this.Services.AddSingleton(new ExecutionsClient(client));
        return handler;
    }

    private IRenderedComponent<Designer> RenderLoaded()
    {
        var cut = this.RenderComponent<Designer>(p => p.Add(x => x.Id, WfId.ToString()));
        cut.WaitForAssertion(() => cut.FindAll("[data-testid=run]").Should().NotBeEmpty());
        return cut;
    }

    [Fact]
    public void Run_StartsExecution_EntersRunMode()
    {
        this.Setup();
        var cut = this.RenderLoaded();

        cut.Find("[data-testid=run]").Click();
        cut.WaitForAssertion(() => cut.FindAll("[data-testid=run-dialog]").Should().NotBeEmpty());
        cut.Find("[data-testid=run-start]").Click();

        // Run mode: the overlay's Close button appears, the palette is gone.
        cut.WaitForAssertion(() => cut.FindAll("[data-testid=run-close]").Should().NotBeEmpty());
        cut.FindAll(".df-palette").Should().BeEmpty("editing palette is hidden in run mode");
    }

    [Fact]
    public void RunMode_DisablesEditing()
    {
        this.Setup();
        var cut = this.RenderLoaded();
        cut.Find("[data-testid=run]").Click();
        cut.Find("[data-testid=run-start]").Click();
        cut.WaitForAssertion(() => cut.FindAll("[data-testid=run-close]").Should().NotBeEmpty());

        // The Save button (edit-mode toolbar) is not shown in run mode.
        cut.FindAll("[data-testid=save]").Should().BeEmpty();
    }

    [Fact]
    public void Cancel_CallsApi()
    {
        var handler = this.Setup();
        var cut = this.RenderLoaded();
        cut.Find("[data-testid=run]").Click();
        cut.Find("[data-testid=run-start]").Click();
        cut.WaitForAssertion(() => cut.FindAll("[data-testid=run-cancel]").Should().NotBeEmpty());

        cut.Find("[data-testid=run-cancel]").Click();

        cut.WaitForAssertion(() => handler.Requests.Should().Contain(r => r.RequestUri!.AbsolutePath.EndsWith("/cancel")));
    }

    [Fact]
    public void Close_ReturnsToEditMode()
    {
        this.Setup();
        var cut = this.RenderLoaded();
        cut.Find("[data-testid=run]").Click();
        cut.Find("[data-testid=run-start]").Click();
        cut.WaitForAssertion(() => cut.FindAll("[data-testid=run-close]").Should().NotBeEmpty());

        cut.Find("[data-testid=run-close]").Click();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid=save]").Should().NotBeEmpty("edit mode restored"));
    }

    [Fact]
    public void Run_InvalidInputsJson_ShowsError()
    {
        this.Setup();
        var toasts = this.Services.GetRequiredService<ToastService>();
        var cut = this.RenderLoaded();
        cut.Find("[data-testid=run]").Click();
        cut.Find("[data-testid=run-inputs]").Change("{ not json");
        cut.Find("[data-testid=run-start]").Click();

        cut.WaitForAssertion(() => toasts.Toasts.Should().Contain(t => t.Message.Contains("valid JSON")));
    }
}
