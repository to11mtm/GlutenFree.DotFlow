// <copyright file="DesignerPageTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Components;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
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
/// 🧪 Phase 3.3.a.3 — bUnit tests for the Designer page load path~ ✨.
/// </summary>
public sealed class DesignerPageTests : TestContext
{
    public DesignerPageTests()
    {
        this.JSInterop.Mode = JSRuntimeMode.Loose;
        this.Services.AddSingleton(new ToastService());
        this.Services.AddSingleton(new AuthState());
        this.Services.AddSingleton(new Workflow.UI.Client.Services.PaletteDragState());
        this.Services.AddSingleton(new Workflow.UI.Client.Designer.State.DesignerClipboard());
        this.Services.AddSingleton(new Workflow.UI.Client.Scripts.State.ScriptStudioHandoff());
        this.Services.AddSingleton(new ApiClientOptions { BaseUrl = "http://localhost" });
        this.Services.AddSingleton(sp => new RealTimeClient(sp.GetRequiredService<ApiClientOptions>(), sp.GetRequiredService<AuthState>()));
    }

    private static string Json(object o) => JsonSerializer.Serialize(o, ApiHttp.Json);

    private void UseHandler(FakeHttpMessageHandler handler)
    {
        var client = handler.CreateClient();
        this.Services.AddSingleton(new WorkflowsClient(client));
        this.Services.AddSingleton(new ModulesClient(client));
        this.Services.AddSingleton(new ExecutionsClient(client));
    }

    [Fact]
    public void DesignerPage_LoadsAndRenders_FromClients()
    {
        var id = Guid.NewGuid();
        var workflow = new WorkflowDto(
            id, "order-pipeline", null, "1.0.0",
            new List<NodeDto>
            {
                new("http-1", "builtin.http.request", "HTTP", new Dictionary<string, JsonElement>(), new PositionDto(100, 100)),
            },
            new List<ConnectionDto>(), new Dictionary<string, JsonElement>(), null, null, null, null, new List<string>());

        var modules = new List<ModuleSummaryDto> { new("builtin.http.request", "HTTP Request", "HTTP", "d", "🌐", "1.0.0") };
        var details = new ModuleDetailsDto(
            "builtin.http.request", "HTTP Request", "HTTP", "d", "🌐", "1.0.0",
            new ModuleSchemaDto(new List<PortDefinitionDto>(), new List<PortDefinitionDto>(), new List<ModulePropertyDefinitionDto>()),
            new List<string>());

        var handler = new FakeHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            var body = path switch
            {
                "/api/v1/modules" => Json(modules),
                var p when p.StartsWith("/api/v1/modules/") => Json(details),
                var p when p.StartsWith("/api/v1/workflows/") => Json(workflow),
                _ => "{}",
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        });
        this.UseHandler(handler);

        var cut = this.RenderComponent<Designer>(p => p.Add(x => x.Id, id.ToString()));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("HTTP"));
        cut.WaitForAssertion(() => cut.FindAll(".df-node").Should().ContainSingle());
    }

    [Fact]
    public void DesignerPage_LoadError_ShowsRetry()
    {
        var handler = new FakeHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path == "/api/v1/modules")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") };
            }

            // Workflow GET fails.
            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{\"title\":\"Not found\"}", System.Text.Encoding.UTF8, "application/problem+json"),
            };
        });
        this.UseHandler(handler);

        var cut = this.RenderComponent<Designer>(p => p.Add(x => x.Id, Guid.NewGuid().ToString()));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Retry"));
    }

    [Fact]
    public void DesignerPage_New_RendersBlankCanvas()
    {
        var handler = new FakeHttpMessageHandler(req =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") });
        this.UseHandler(handler);

        var cut = this.RenderComponent<Designer>(p => p.Add(x => x.Id, "new"));

        cut.WaitForAssertion(() => cut.FindAll(".df-canvas-viewport").Should().NotBeEmpty());
        cut.FindAll(".df-node").Should().BeEmpty();
    }

    [Fact]
    public void MergeOutputs_AddsFanInNode_ConnectedToSelection()
    {
        var id = Guid.NewGuid();
        var workflow = new WorkflowDto(
            id, "wf", null, "1.0.0",
            new List<NodeDto>
            {
                new("a", "builtin.log", "A", new Dictionary<string, JsonElement>(), new PositionDto(100, 100)),
                new("b", "builtin.log", "B", new Dictionary<string, JsonElement>(), new PositionDto(100, 300)),
            },
            new List<ConnectionDto>(), new Dictionary<string, JsonElement>(), null, null, null, null, new List<string>());

        var fanInDetails = new ModuleDetailsDto(
            "builtin.fanin", "Fan In", "Flow", "d", "🪄", "1.0.0",
            new ModuleSchemaDto(
                new List<PortDefinitionDto> { new("branches", "Branches", "object", null, false, null) },
                new List<PortDefinitionDto> { new("result", "Result", "object", null, false, null) },
                new List<ModulePropertyDefinitionDto>()),
            new List<string>());

        var handler = new FakeHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            var body = path switch
            {
                "/api/v1/modules" => Json(new List<ModuleSummaryDto>()),
                var p when p.StartsWith("/api/v1/modules/") => Json(fanInDetails),
                var p when p.StartsWith("/api/v1/workflows/") => Json(workflow),
                _ => "{}",
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        });
        this.UseHandler(handler);

        var cut = this.RenderComponent<Designer>(p => p.Add(x => x.Id, id.ToString()));
        cut.WaitForAssertion(() => cut.FindAll(".df-node").Should().HaveCount(2));

        // Select both nodes, then open the node context menu.
        cut.Find("[data-node-id=a]").PointerDown(new Microsoft.AspNetCore.Components.Web.PointerEventArgs());
        cut.Find("[data-node-id=b]").PointerDown(new Microsoft.AspNetCore.Components.Web.PointerEventArgs { CtrlKey = true });
        cut.Find("[data-node-id=b]").ContextMenu();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Merge outputs"));
        cut.FindAll(".df-ctxmenu__item").First(b => b.TextContent.Contains("Merge outputs")).Click();

        cut.WaitForAssertion(() =>
        {
            cut.FindAll(".df-node").Should().HaveCount(3);
            cut.Markup.Should().Contain("builtin.fanin");
            // Both sources feed the fan-in: two edges rendered.
            cut.FindAll("path.df-edge").Should().HaveCount(2);
        });
    }
}
