// <copyright file="ShellAndListTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Components;

using System;
using System.Linq;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Tests.UI.Api;
using Workflow.UI.Client.Api;
using Workflow.UI.Client.Api.Dtos;
using Workflow.UI.Client.Pages;
using Workflow.UI.Client.Services;
using Xunit;

/// <summary>
/// ðŸ§ª Phase 3.3.a.1 â€” bUnit tests for the app shell, auth pane, and workflow list~ âœ¨.
/// </summary>
public sealed class ShellAndListTests : TestContext
{
    private readonly AuthState auth = new();
    private readonly ToastService toasts = new();
    private readonly FakeLocalStorage storage = new();

    public ShellAndListTests()
    {
        this.JSInterop.Mode = JSRuntimeMode.Loose;
        this.Services.AddSingleton(this.auth);
        this.Services.AddSingleton(this.toasts);
        this.Services.AddSingleton<ILocalStorage>(this.storage);
        this.Services.AddSingleton(new ApiClientOptions { BaseUrl = "http://localhost" });
    }

    private void UseHandler(FakeHttpMessageHandler handler)
    {
        var client = handler.CreateClient();
        this.Services.AddSingleton(new WorkflowsClient(client));
        this.Services.AddSingleton(new ExecutionsClient(client));
        this.Services.AddSingleton(new SystemClient(client));
    }

    [Fact]
    public void WorkflowList_RendersRows_FromClient()
    {
        var page = new PageDto<WorkflowSummaryDto>(
            new List<WorkflowSummaryDto>
            {
                new(Guid.NewGuid(), "order-pipeline", null, "1.4.0", new List<string>(), 12, null, DateTimeOffset.UtcNow),
            },
            1, 1, 20, 1);
        this.UseHandler(FakeHttpMessageHandler.Json(JsonSerializer.Serialize(page, ApiHttp.Json)));

        var cut = this.RenderComponent<WorkflowList>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("order-pipeline"));
        cut.Markup.Should().Contain("DotFlow Designer"); // top bar
    }

    [Fact]
    public void WorkflowList_Empty_ShowsEmptyState()
    {
        var page = new PageDto<WorkflowSummaryDto>(new List<WorkflowSummaryDto>(), 0, 1, 20, 0);
        this.UseHandler(FakeHttpMessageHandler.Json(JsonSerializer.Serialize(page, ApiHttp.Json)));

        var cut = this.RenderComponent<WorkflowList>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("No workflows yet"));
    }

    [Fact]
    public void WorkflowList_Delete_ConfirmsThenCalls()
    {
        var id = Guid.NewGuid();
        var page = new PageDto<WorkflowSummaryDto>(
            new List<WorkflowSummaryDto> { new(id, "wf", null, "1.0.0", new List<string>(), 1, null, null) },
            1, 1, 20, 1);
        var handler = new FakeHttpMessageHandler(req =>
            req.Method == HttpMethod.Delete
                ? new HttpResponseMessage(HttpStatusCode.NoContent)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(page, ApiHttp.Json)) });
        this.UseHandler(handler);

        var cut = this.RenderComponent<WorkflowList>();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("wf"));

        // First click â†’ asks to confirm (no DELETE yet).
        cut.Find("[data-testid=delete]").Click();
        handler.Requests.Should().NotContain(r => r.Method == HttpMethod.Delete);

        // Confirm â†’ DELETE is sent.
        cut.Find("[data-testid=confirm-delete]").Click();
        cut.WaitForAssertion(() => handler.Requests.Should().Contain(r => r.Method == HttpMethod.Delete));
    }

    [Fact]
    public void WorkflowList_Run_ShowsExecutionToast()
    {
        var id = Guid.NewGuid();
        var page = new PageDto<WorkflowSummaryDto>(
            new List<WorkflowSummaryDto> { new(id, "wf", null, "1.0.0", new List<string>(), 1, null, null) },
            1, 1, 20, 1);
        var started = new ExecutionStartedDto(Guid.NewGuid(), "accepted");
        var handler = new FakeHttpMessageHandler(req =>
            req.Method == HttpMethod.Post
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(started, ApiHttp.Json)) }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(page, ApiHttp.Json)) });
        this.UseHandler(handler);

        var cut = this.RenderComponent<WorkflowList>();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("wf"));

        cut.Find("[data-testid=run]").Click();

        cut.WaitForAssertion(() => this.toasts.Toasts.Should().Contain(t => t.Message.Contains("Execution started")));
    }

    [Fact]
    public void Settings_TestConnection_ShowsStatus()
    {
        this.UseHandler(FakeHttpMessageHandler.Json("{\"version\":\"1.2.3\"}"));

        var cut = this.RenderComponent<Settings>();
        cut.FindAll("button").First(b => b.TextContent.Contains("Test connection"))!.Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("API v1.2.3"));
    }

    [Fact]
    public void AuthPane_SavesToken_UpdatesAuthState()
    {
        this.UseHandler(FakeHttpMessageHandler.Json("{}"));

        var cut = this.RenderComponent<Settings>();
        cut.Find("#token").Change("my-jwt-token");
        cut.FindAll("button").First(b => b.TextContent.Contains("Save credential"))!.Click();

        cut.WaitForAssertion(() => this.auth.Token.Should().Be("my-jwt-token"));
        this.storage.Store.Should().ContainKey("dotflow.token");
    }

    private sealed class FakeLocalStorage : ILocalStorage
    {
        public Dictionary<string, string> Store { get; } = new();

        public ValueTask<string?> GetAsync(string key)
            => new(this.Store.TryGetValue(key, out var v) ? v : null);

        public ValueTask SetAsync(string key, string value)
        {
            this.Store[key] = value;
            return default;
        }

        public ValueTask RemoveAsync(string key)
        {
            this.Store.Remove(key);
            return default;
        }
    }
}

