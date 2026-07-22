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
using Microsoft.AspNetCore.Components.Web;
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

    [Fact]
    public void DroppingFanIn_OnNodeOutputSide_AutoWiresAllOutputs()
    {
        var id = Guid.NewGuid();
        var workflow = new WorkflowDto(
            id, "wf", null, "1.0.0",
            new List<NodeDto>
            {
                new("http-1", "builtin.http.request", "HTTP", new Dictionary<string, JsonElement>(), new PositionDto(100, 100)),
            },
            new List<ConnectionDto>(), new Dictionary<string, JsonElement>(), null, null, null, null, new List<string>());

        // The source node has two outputs (success + error); the fan-in has a branches input.
        var httpDetails = new ModuleDetailsDto(
            "builtin.http.request", "HTTP Request", "HTTP", "d", "🌐", "1.0.0",
            new ModuleSchemaDto(
                new List<PortDefinitionDto> { new("input", "Input", "object", null, true, null) },
                new List<PortDefinitionDto>
                {
                    new("success", "Success", "object", null, false, null),
                    new("error", "Error", "object", null, false, null),
                },
                new List<ModulePropertyDefinitionDto>()),
            new List<string>());
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
                "/api/v1/modules/builtin.fanin" => Json(fanInDetails),
                var p when p.StartsWith("/api/v1/modules/") => Json(httpDetails),
                var p when p.StartsWith("/api/v1/workflows/") => Json(workflow),
                _ => "{}",
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        });
        this.UseHandler(handler);

        var cut = this.RenderComponent<Designer>(p => p.Add(x => x.Id, id.ToString()));
        cut.WaitForAssertion(() => cut.FindAll(".df-node").Should().ContainSingle());

        // Simulate a palette drag of the fan-in dropped just right of http-1 (right edge = 100+200).
        // The designer auto-fits after load, so convert the canvas point through the live transform.
        var transform = cut.FindComponent<Workflow.UI.Client.Designer.Components.CanvasView>().Instance.Transform;
        var screen = Workflow.UI.Client.Designer.State.CanvasGeometry.CanvasToScreen(
            new Workflow.UI.Client.Designer.State.Point(320, 130), transform);
        var dragState = this.Services.GetRequiredService<PaletteDragState>();
        dragState.Begin("builtin.fanin");
        cut.Find(".df-canvas-viewport").Drop(new Microsoft.AspNetCore.Components.Web.DragEventArgs
        {
            OffsetX = screen.X,
            OffsetY = screen.Y,
        });

        cut.WaitForAssertion(() =>
        {
            cut.FindAll(".df-node").Should().HaveCount(2);
            cut.Markup.Should().Contain("builtin.fanin");
            // Both http outputs (success + error) are wired into the fan-in.
            cut.FindAll("path.df-edge").Should().HaveCount(2);
        });
    }

    [Fact]
    public void CanvasMenu_InsertLoopSkeleton_AddsWiredPair_UndoRemovesAll()
    {
        var handler = new FakeHttpMessageHandler(req =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(req.RequestUri!.AbsolutePath == "/api/v1/modules" ? "[]" : "{}") });
        this.UseHandler(handler);

        var cut = this.RenderComponent<Designer>(p => p.Add(x => x.Id, "new"));
        cut.WaitForAssertion(() => cut.FindAll(".df-canvas-viewport").Should().NotBeEmpty());

        cut.Find(".df-canvas-viewport").ContextMenu(new MouseEventArgs { OffsetX = 200, OffsetY = 200 });
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Insert loop skeleton"));
        cut.FindAll(".df-ctxmenu__item").First(b => b.TextContent.Contains("Insert loop skeleton")).Click();

        cut.WaitForAssertion(() =>
        {
            cut.FindAll(".df-node").Should().HaveCount(2);
            cut.Markup.Should().Contain("builtin.loop.foreach");
            cut.FindAll("path.df-edge--structural").Should().ContainSingle();
            cut.FindAll(".df-region--loop").Should().ContainSingle();
        });

        // One undo removes the whole skeleton (composite command).
        cut.Find("[data-testid=undo]").Click();
        cut.WaitForAssertion(() => cut.FindAll(".df-node").Should().BeEmpty());
    }

    [Fact]
    public void DroppingForEach_OnEmptyCanvas_InsertsSkeleton()
    {
        var handler = new FakeHttpMessageHandler(req =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(req.RequestUri!.AbsolutePath == "/api/v1/modules" ? "[]" : "{}") });
        this.UseHandler(handler);

        var cut = this.RenderComponent<Designer>(p => p.Add(x => x.Id, "new"));
        cut.WaitForAssertion(() => cut.FindAll(".df-canvas-viewport").Should().NotBeEmpty());

        var drag = this.Services.GetRequiredService<PaletteDragState>();
        drag.Begin("builtin.loop.foreach");
        cut.Find(".df-canvas-viewport").Drop(new DragEventArgs { OffsetX = 200, OffsetY = 200 });

        cut.WaitForAssertion(() =>
        {
            cut.FindAll(".df-node").Should().HaveCount(2);
            cut.Markup.Should().Contain("builtin.loop.foreach");
            cut.FindAll("path.df-edge--structural").Should().ContainSingle();
            cut.FindAll(".df-region--loop").Should().ContainSingle();
        });
    }

    [Fact]
    public void DroppingTryCatch_OnNodeOutputSide_ScaffoldsAndWiresFromSource()
    {
        var id = Guid.NewGuid();
        var workflow = new WorkflowDto(
            id, "wf", null, "1.0.0",
            new List<NodeDto>
            {
                new("http-1", "builtin.http.request", "HTTP", new Dictionary<string, JsonElement>(), new PositionDto(100, 100)),
            },
            new List<ConnectionDto>(), new Dictionary<string, JsonElement>(), null, null, null, null, new List<string>());

        var handler = new FakeHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            var body = path switch
            {
                "/api/v1/modules" => Json(new List<ModuleSummaryDto>()),
                var p when p.StartsWith("/api/v1/workflows/") => Json(workflow),
                _ => "{}",
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        });
        this.UseHandler(handler);

        var cut = this.RenderComponent<Designer>(p => p.Add(x => x.Id, id.ToString()));
        cut.WaitForAssertion(() => cut.FindAll(".df-node").Should().ContainSingle());

        // Drop trycatch just right of http-1 (right edge = 100+200), through the live transform.
        var transform = cut.FindComponent<Workflow.UI.Client.Designer.Components.CanvasView>().Instance.Transform;
        var screen = Workflow.UI.Client.Designer.State.CanvasGeometry.CanvasToScreen(
            new Workflow.UI.Client.Designer.State.Point(320, 130), transform);
        var drag = this.Services.GetRequiredService<PaletteDragState>();
        drag.Begin("builtin.trycatch");
        cut.Find(".df-canvas-viewport").Drop(new DragEventArgs { OffsetX = screen.X, OffsetY = screen.Y });

        cut.WaitForAssertion(() =>
        {
            // http-1 + guard + try step + catch step.
            cut.FindAll(".df-node").Should().HaveCount(4);
            cut.Markup.Should().Contain("builtin.trycatch");
            // try + catch structural routes, plus the plain wire http-1 → guard.
            cut.FindAll("path.df-edge--structural").Should().HaveCount(2);
            cut.FindAll("path.df-edge").Should().HaveCount(3);
        });
    }

    [Fact]
    public void CanvasMenu_InsertTryCatchSkeleton_AddsThreeNodes_TwoRegions()
    {
        var handler = new FakeHttpMessageHandler(req =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(req.RequestUri!.AbsolutePath == "/api/v1/modules" ? "[]" : "{}") });
        this.UseHandler(handler);

        var cut = this.RenderComponent<Designer>(p => p.Add(x => x.Id, "new"));
        cut.WaitForAssertion(() => cut.FindAll(".df-canvas-viewport").Should().NotBeEmpty());

        cut.Find(".df-canvas-viewport").ContextMenu(new MouseEventArgs { OffsetX = 200, OffsetY = 200 });
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Insert try/catch skeleton"));
        cut.FindAll(".df-ctxmenu__item").First(b => b.TextContent.Contains("Insert try/catch skeleton")).Click();

        cut.WaitForAssertion(() =>
        {
            cut.FindAll(".df-node").Should().HaveCount(3);
            cut.Markup.Should().Contain("builtin.trycatch");
            cut.FindAll("path.df-edge--structural").Should().HaveCount(2);
            cut.FindAll(".df-region--try").Should().ContainSingle();
            cut.FindAll(".df-region--catch").Should().ContainSingle();
        });
    }
}
