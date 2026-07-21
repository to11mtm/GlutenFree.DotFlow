// <copyright file="BridgeAndPolishTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Modules.Components;

using System.Net;
using System.Net.Http;
using System.Text;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Tests.UI.Api;
using Workflow.Tests.UI.Scripts.Components;
using Workflow.UI.Client.Api;
using Workflow.UI.Client.Designer.Components;
using Workflow.UI.Client.Pages;
using Workflow.UI.Client.Services;
using Xunit;

/// <summary>
/// 🔗 Phase 3.6.4 — bUnit tests for the designer bridge + enabled-only persistence + error retry~ ✨.
/// </summary>
public sealed class BridgeAndPolishTests : TestContext
{
    private readonly InMemoryLocalStorage storage = new();

    public BridgeAndPolishTests()
    {
        this.JSInterop.Mode = JSRuntimeMode.Loose;
        this.Services.AddSingleton(new AuthState());
        this.Services.AddSingleton(new ToastService());
        this.Services.AddSingleton(new ApiClientOptions { BaseUrl = "http://localhost" });
        this.Services.AddSingleton<ILocalStorage>(this.storage);
    }

    private void UseModules(FakeHttpMessageHandler handler)
        => this.Services.AddSingleton(new ModulesClient(handler.CreateClient()));

    [Fact]
    public void Palette_ManageLink_NavigatesToModules()
    {
        this.UseModules(FakeHttpMessageHandler.Json("[]"));
        this.Services.AddSingleton(new PaletteDragState());

        var cut = this.RenderComponent<ModulePalette>();
        cut.Find("[data-testid=palette-manage]").Click();

        this.Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith("/modules");
    }

    [Fact]
    public void Manager_EnabledOnly_Persists()
    {
        this.UseModules(FakeHttpMessageHandler.Json(
            "[{\"id\":\"m\",\"displayName\":\"M\",\"category\":\"C\",\"description\":\"d\",\"icon\":\"🔧\",\"version\":\"1.0.0\",\"enabled\":true}]"));

        var cut = this.RenderComponent<ModuleManager>();
        cut.WaitForAssertion(() => cut.Find("[data-testid=mod-enabled-only]"));
        cut.Find("[data-testid=mod-enabled-only]").Change(true);

        cut.WaitForAssertion(() =>
        {
            this.storage.Store.Should().ContainKey("dotflow.modules.enabledOnly");
            this.storage.Store["dotflow.modules.enabledOnly"].Should().Be("1");
        });
    }

    [Fact]
    public void Manager_EnabledOnly_RestoredFromStorage()
    {
        this.storage.Store["dotflow.modules.enabledOnly"] = "1";
        this.UseModules(FakeHttpMessageHandler.Json(
            "[{\"id\":\"on\",\"displayName\":\"On\",\"category\":\"C\",\"description\":\"d\",\"icon\":\"🔧\",\"version\":\"1.0.0\",\"enabled\":true}," +
            "{\"id\":\"off\",\"displayName\":\"Off\",\"category\":\"C\",\"description\":\"d\",\"icon\":\"🔧\",\"version\":\"1.0.0\",\"enabled\":false}]"));

        var cut = this.RenderComponent<ModuleManager>();

        // Restored EnabledOnly=true hides the disabled module.
        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid=modcard-on]").Should().NotBeNull();
            cut.FindAll("[data-testid=modcard-off]").Should().BeEmpty();
        });
    }

    [Fact]
    public void Manager_LoadError_ShowsRetry()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("{\"title\":\"Service unavailable\"}", Encoding.UTF8, "application/problem+json"),
        });
        this.UseModules(handler);

        var cut = this.RenderComponent<ModuleManager>();

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid=modules-error]").Should().NotBeNull();
            cut.Find("[data-testid=modules-retry]").Should().NotBeNull();
        });
    }
}
