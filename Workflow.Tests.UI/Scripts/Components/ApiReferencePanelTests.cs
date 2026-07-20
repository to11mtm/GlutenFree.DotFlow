// <copyright file="ApiReferencePanelTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Scripts.Components;

using System.Linq;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Tests.UI.Api;
using Workflow.UI.Client.Api;
using Workflow.UI.Client.Scripts.Components;
using Workflow.UI.Client.Scripts.State;
using Workflow.UI.Client.Services;
using ClientScriptStudio = Workflow.UI.Client.Pages.ScriptStudio;
using Xunit;

/// <summary>
/// 💡 Phase 3.4.1 — bUnit tests for the API reference panel + Monaco provider registration~ ✨.
/// </summary>
public sealed class ApiReferencePanelTests : TestContext
{
    public ApiReferencePanelTests()
    {
        this.JSInterop.Mode = JSRuntimeMode.Loose;
        this.Services.AddSingleton(new AuthState());
        this.Services.AddSingleton(new ToastService());
        this.Services.AddSingleton(new ApiClientOptions { BaseUrl = "http://localhost" });
        this.Services.AddSingleton<ILocalStorage>(new InMemoryLocalStorage());
        this.Services.AddSingleton(new Workflow.UI.Client.Scripts.State.ScriptStudioHandoff());
    }

    [Fact]
    public void ApiPanel_RendersGroups_FromDescriptor()
    {
        var cut = this.RenderComponent<ApiReferencePanel>();

        var groups = cut.FindAll("[data-testid=api-group]");
        groups.Should().HaveCountGreaterThanOrEqualTo(4);
        cut.Markup.Should().Contain("getVariable");
        cut.Markup.Should().Contain("httpGet");
        // Gated methods carry the lock marker.
        cut.Markup.Should().Contain("🔒");
    }

    [Fact]
    public void ApiPanel_Search_Filters()
    {
        var cut = this.RenderComponent<ApiReferencePanel>();

        cut.Find("[data-testid=api-search]").Input("http");

        var methods = cut.FindAll("[data-testid=api-method]").Select(e => e.TextContent.Trim()).ToList();
        methods.Should().NotBeEmpty();
        methods.Should().OnlyContain(t => t.StartsWith("http", System.StringComparison.OrdinalIgnoreCase));
        cut.Markup.Should().NotContain("getVariable");
    }

    [Fact]
    public void ApiPanel_ClickMethod_InsertsCallAtCursor()
    {
        ApiMethodInfo? inserted = null;
        var cut = this.RenderComponent<ApiReferencePanel>(p => p
            .Add(x => x.OnInsert, m => inserted = m));

        cut.FindAll("[data-testid=api-method]").First(e => e.TextContent.Contains("getVariable")).Click();

        inserted.Should().NotBeNull();
        inserted!.CallSnippet.Should().Be("workflow.getVariable(name)");
    }

    [Fact]
    public void ApiPanel_ShowsSignatureAndDoc()
    {
        var cut = this.RenderComponent<ApiReferencePanel>();

        cut.FindAll("[data-testid=api-method]").First(e => e.TextContent.Contains("getVariable")).Click();

        var detail = cut.Find("[data-testid=api-detail]");
        detail.TextContent.Should().Contain("getVariable(name: string): object?");
        detail.TextContent.Should().Contain("Gets a workflow variable value");
    }

    [Fact]
    public void Completions_ProviderRegistered_ForJavaScript()
    {
        // Make Monaco "load" so ScriptEditor raises OnEditorReady → the page registers providers.
        this.JSInterop.Setup<bool>("dotflowMonaco.createEditor", _ => true).SetResult(true);
        this.Services.AddSingleton(new ScriptsClient(
            FakeHttpMessageHandler.Json("[{\"languageId\":\"javascript\",\"displayName\":\"JavaScript\"}]").CreateClient()));

        var cut = this.RenderComponent<ClientScriptStudio>();

        cut.WaitForAssertion(() =>
        {
            this.JSInterop.VerifyInvoke("dotflowMonaco.registerCompletions");
            this.JSInterop.VerifyInvoke("dotflowMonaco.registerHover");
        });
    }
}
