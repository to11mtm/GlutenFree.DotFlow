// <copyright file="SaveFlowTests.cs" company="GlutenFree">
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
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Tests.UI.Api;
using Workflow.UI.Client.Api;
using Workflow.UI.Client.Api.Dtos;
using Workflow.UI.Client.Designer.State;
using Workflow.UI.Client.Pages;
using Workflow.UI.Client.Services;
using Xunit;

/// <summary>
/// 🧪 Phase 3.3.b.4 — Save-pipeline + shortcut specs on the Designer page~ ✨.
/// </summary>
public sealed class SaveFlowTests : TestContext
{
    private static readonly Guid WfId = Guid.NewGuid();

    public SaveFlowTests()
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

    private static WorkflowDto Workflow(string moduleId)
        => new(
            WfId, "wf", null, "1.0.0",
            new List<NodeDto> { new("n1", moduleId, "N1", new Dictionary<string, JsonElement>(), new PositionDto(100, 100)) },
            new List<ConnectionDto>(), new Dictionary<string, JsonElement>(), null, null, null, null, new List<string>());

    private static ModuleDetailsDto Details(string moduleId)
        => new(moduleId, moduleId, "HTTP", "d", "🌐", "1.0.0",
            new ModuleSchemaDto(new(), new(), new()), new List<string>());

    private FakeHttpMessageHandler Setup(
        string workflowModuleId,
        bool serverValid = true,
        List<WorkflowValidationIssueDto>? serverIssues = null)
    {
        var modules = new List<ModuleSummaryDto> { new("builtin.http.request", "HTTP", "HTTP", "d", "🌐", "1.0.0") };
        var handler = new FakeHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            var method = req.Method;
            if (path == "/api/v1/modules")
            {
                return Ok(J(modules));
            }

            if (path.StartsWith("/api/v1/modules/"))
            {
                return Ok(J(Details(workflowModuleId)));
            }

            if (path == "/api/v1/workflows/validate" && method == HttpMethod.Post)
            {
                return Ok(J(new WorkflowValidationDto(serverValid, serverIssues ?? new List<WorkflowValidationIssueDto>())));
            }

            if (path.StartsWith("/api/v1/workflows/") && method == HttpMethod.Get)
            {
                return Ok(J(Workflow(workflowModuleId)));
            }

            if (path.StartsWith("/api/v1/workflows/") && method == HttpMethod.Put)
            {
                return Ok(J(Workflow(workflowModuleId)));
            }

            return Ok("{}");
        });

        var client = handler.CreateClient();
        this.Services.AddSingleton(new WorkflowsClient(client));
        this.Services.AddSingleton(new ModulesClient(client));
        this.Services.AddSingleton(new ExecutionsClient(client));
        return handler;

        static HttpResponseMessage Ok(string body)
            => new(HttpStatusCode.OK) { Content = new StringContent(body) };
    }

    [Fact]
    public void Save_ClientValid_CallsServerValidate_ThenPut()
    {
        var handler = this.Setup("builtin.http.request", serverValid: true);
        var cut = this.RenderComponent<Designer>(p => p.Add(x => x.Id, WfId.ToString()));
        cut.WaitForAssertion(() => cut.FindAll("[data-testid=save]").Should().NotBeEmpty());

        cut.Find("[data-testid=save]").Click();

        cut.WaitForAssertion(() =>
        {
            handler.Requests.Should().Contain(r => r.RequestUri!.AbsolutePath == "/api/v1/workflows/validate");
            handler.Requests.Should().Contain(r => r.Method == HttpMethod.Put);
        });
    }

    [Fact]
    public void Save_ClientValidatorError_BlocksWithDialog_NoPut()
    {
        // Node uses a module the server doesn't list → client GraphValidator flags "unknown module".
        var handler = this.Setup("unknown.module");
        var cut = this.RenderComponent<Designer>(p => p.Add(x => x.Id, WfId.ToString()));
        cut.WaitForAssertion(() => cut.FindAll("[data-testid=save]").Should().NotBeEmpty());

        cut.Find("[data-testid=save]").Click();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid=save-dialog]").Should().NotBeEmpty());
        handler.Requests.Should().NotContain(r => r.Method == HttpMethod.Put);
    }

    [Fact]
    public void Save_ServerValidateIssues_ShowsDialog_NoPut()
    {
        var issues = new List<WorkflowValidationIssueDto> { new("error", "bad config", "n1") };
        var handler = this.Setup("builtin.http.request", serverValid: false, serverIssues: issues);
        var cut = this.RenderComponent<Designer>(p => p.Add(x => x.Id, WfId.ToString()));
        cut.WaitForAssertion(() => cut.FindAll("[data-testid=save]").Should().NotBeEmpty());

        cut.Find("[data-testid=save]").Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("bad config"));
        handler.Requests.Should().NotContain(r => r.Method == HttpMethod.Put);
    }

    [Fact]
    public void Shortcut_Delete_RemovesSelectedNode()
    {
        this.Setup("builtin.http.request");
        var cut = this.RenderComponent<Designer>(p => p.Add(x => x.Id, WfId.ToString()));
        cut.WaitForAssertion(() => cut.FindAll("[data-node-id=n1]").Should().NotBeEmpty());

        // Select the node, then fire the Delete shortcut.
        cut.Find("[data-node-id=n1]").PointerDown(new PointerEventArgs { ClientX = 100, ClientY = 100 });
        cut.InvokeAsync(() => cut.Instance.OnShortcut("Delete", false, false));

        cut.WaitForAssertion(() => cut.FindAll("[data-node-id=n1]").Should().BeEmpty());
    }

    [Fact]
    public void Shortcut_Undo_RestoresDelete()
    {
        this.Setup("builtin.http.request");
        var cut = this.RenderComponent<Designer>(p => p.Add(x => x.Id, WfId.ToString()));
        cut.WaitForAssertion(() => cut.FindAll("[data-node-id=n1]").Should().NotBeEmpty());

        cut.Find("[data-node-id=n1]").PointerDown(new PointerEventArgs { ClientX = 100, ClientY = 100 });
        cut.InvokeAsync(() => cut.Instance.OnShortcut("Delete", false, false));
        cut.WaitForAssertion(() => cut.FindAll("[data-node-id=n1]").Should().BeEmpty());

        cut.InvokeAsync(() => cut.Instance.OnShortcut("z", true, false));
        cut.WaitForAssertion(() => cut.FindAll("[data-node-id=n1]").Should().NotBeEmpty());
    }
}
