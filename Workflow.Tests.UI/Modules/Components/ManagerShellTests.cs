// <copyright file="ManagerShellTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Modules.Components;

using System.Collections.Generic;
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
/// 📦 Phase 3.6.0 — bUnit tests for the Module Manager shell (grouped grid + search)~ ✨.
/// </summary>
public sealed class ManagerShellTests : TestContext
{
    public ManagerShellTests()
    {
        this.JSInterop.Mode = JSRuntimeMode.Loose;
        this.Services.AddSingleton(new AuthState());
        this.Services.AddSingleton(new ToastService());
        this.Services.AddSingleton(new ApiClientOptions { BaseUrl = "http://localhost" });
        this.Services.AddSingleton<ILocalStorage>(new Workflow.Tests.UI.Scripts.Components.InMemoryLocalStorage());
    }

    private const string ListJson = """
    [
      {"id":"builtin.http.request","displayName":"HTTP Request","category":"HTTP","description":"Sends a request","icon":"🌐","version":"1.2.0","enabled":true},
      {"id":"builtin.http.response","displayName":"HTTP Response","category":"HTTP","description":"Writes a response","icon":"🌐","version":"1.0.0","enabled":true},
      {"id":"builtin.script","displayName":"Script","category":"Scripting","description":"Runs sandboxed code","icon":"📜","version":"1.0.0","enabled":false}
    ]
    """;

    private void UseList(string json)
        => this.Services.AddSingleton(new ModulesClient(FakeHttpMessageHandler.Json(json).CreateClient()));

    [Fact]
    public void Manager_RendersGrid_Grouped()
    {
        this.UseList(ListJson);

        var cut = this.RenderComponent<ModuleManager>();

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("[data-testid=module-group]").Should().HaveCount(2); // HTTP + Scripting
            cut.Find("[data-testid='modcard-builtin.script']").Should().NotBeNull();
            cut.Markup.Should().Contain("HTTP Request");
        });
    }

    [Fact]
    public void Manager_Search_Filters()
    {
        this.UseList(ListJson);

        var cut = this.RenderComponent<ModuleManager>();
        cut.WaitForAssertion(() => cut.Find("[data-testid=mod-search]"));

        cut.Find("[data-testid=mod-search]").Input("script");

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='modcard-builtin.script']").Should().NotBeNull();
            cut.FindAll("[data-testid='modcard-builtin.http.request']").Should().BeEmpty();
        });
    }

    [Fact]
    public void Manager_CardClick_OpensDrawer()
    {
        var detailsJson = """
        {"id":"builtin.script","displayName":"Script","category":"Scripting","description":"Runs sandboxed code","icon":"📜","version":"1.0.0",
         "schema":{"inputs":[],"outputs":[],"properties":[]},"dependencies":[],"enabled":false,"availableVersions":["1.0.0"]}
        """;
        var handler = new FakeHttpMessageHandler(req =>
            req.RequestUri!.AbsolutePath.EndsWith("/modules")
                ? new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new System.Net.Http.StringContent(ListJson) }
                : new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new System.Net.Http.StringContent(detailsJson) });
        this.Services.AddSingleton(new ModulesClient(handler.CreateClient()));

        var cut = this.RenderComponent<ModuleManager>();
        cut.WaitForAssertion(() => cut.Find("[data-testid='modcard-builtin.script']"));
        cut.Find("[data-testid='modcard-builtin.script']").Click();

        cut.WaitForAssertion(() => cut.Find("[data-testid=drawer-name]").TextContent.Should().Contain("Script"));
    }

    [Fact]
    public void Manager_EnabledOnly_HidesDisabled()
    {
        this.UseList(ListJson);

        var cut = this.RenderComponent<ModuleManager>();
        cut.WaitForAssertion(() => cut.Find("[data-testid=mod-enabled-only]"));

        cut.Find("[data-testid=mod-enabled-only]").Change(true);

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='modcard-builtin.script']").Should().BeEmpty());
    }
}

