// <copyright file="VersionsAndToggleTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Modules.Components;

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Tests.UI.Api;
using Workflow.UI.Client.Api;
using Workflow.UI.Client.Api.Dtos;
using Workflow.UI.Client.Modules.Components;
using Workflow.UI.Client.Modules.State;
using Workflow.UI.Client.Pages;
using Workflow.UI.Client.Services;
using Xunit;

/// <summary>
/// 🔘🔢🗑 Phase 3.6.3 — bUnit tests for enable/disable, version panel, and uninstall~ ✨.
/// </summary>
public sealed class VersionsAndToggleTests : TestContext
{
    public VersionsAndToggleTests() => this.JSInterop.Mode = JSRuntimeMode.Loose;

    private static ModuleDetailsDto Details(string id, bool enabled, params string[] deps)
        => new(id, id, "HTTP", "d", "🔧", "1.0.0",
            new ModuleSchemaDto(new(), new(), new()), new List<string>(deps), enabled, new List<string> { "1.0.0", "0.9.0" });

    private static ModuleDocModel Doc(string id = "my.mod", bool enabled = true)
        => ModuleDocModel.From(Details(id, enabled));

    private static string Toggle(string id, bool enabled)
        => System.Text.Json.JsonSerializer.Serialize(new ModuleToggleResultDto(id, enabled, new List<string> { "1.0.0" }), ApiHttp.Json);

    // ── ModuleActions (enable/disable + uninstall) ──

    [Fact]
    public void Toggle_Disable_CallsClient_AndWarnsOnDependents()
    {
        var handler = FakeHttpMessageHandler.Json(Toggle("my.mod", false));
        var changed = false;
        var known = new List<ModuleDetailsDto> { Details("other", true, "my.mod") };

        var cut = this.RenderComponent<ModuleActions>(p => p
            .Add(x => x.Doc, Doc(enabled: true))
            .Add(x => x.Modules, new ModulesClient(handler.CreateClient()))
            .Add(x => x.KnownDetails, known)
            .Add(x => x.OnChanged, () => changed = true));

        cut.Find("[data-testid=action-disable]").Click();

        // A dependents heads-up appears listing 'other'.
        cut.Find("[data-testid=disable-confirm]").TextContent.Should().Contain("other");
        cut.Find("[data-testid=disable-confirm-yes]").Click();

        cut.WaitForAssertion(() =>
        {
            handler.Requests.Should().Contain(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/disable"));
            changed.Should().BeTrue();
        });
    }

    [Fact]
    public void Toggle_Enable_CallsClient()
    {
        var handler = FakeHttpMessageHandler.Json(Toggle("my.mod", true));
        var cut = this.RenderComponent<ModuleActions>(p => p
            .Add(x => x.Doc, Doc(enabled: false))
            .Add(x => x.Modules, new ModulesClient(handler.CreateClient())));

        cut.Find("[data-testid=action-enable]").Click();

        cut.WaitForAssertion(() => handler.Requests.Should().Contain(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/enable")));
    }

    [Fact]
    public void Uninstall_Confirm_CallsDelete()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var uninstalled = false;
        var cut = this.RenderComponent<ModuleActions>(p => p
            .Add(x => x.Doc, Doc())
            .Add(x => x.Modules, new ModulesClient(handler.CreateClient()))
            .Add(x => x.OnUninstalled, () => uninstalled = true));

        cut.Find("[data-testid=action-uninstall]").Click();
        cut.Find("[data-testid=uninstall-confirm-yes]").Click();

        cut.WaitForAssertion(() =>
        {
            handler.Requests.Should().Contain(r => r.Method == HttpMethod.Delete);
            uninstalled.Should().BeTrue();
        });
    }

    [Fact]
    public void Uninstall_Conflict_409_ShownClearly()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = new StringContent("{\"title\":\"Conflict\",\"detail\":\"required by: other.mod\"}", Encoding.UTF8, "application/problem+json"),
        });
        var cut = this.RenderComponent<ModuleActions>(p => p
            .Add(x => x.Doc, Doc())
            .Add(x => x.Modules, new ModulesClient(handler.CreateClient())));

        cut.Find("[data-testid=action-uninstall]").Click();
        cut.Find("[data-testid=uninstall-confirm-yes]").Click();

        cut.WaitForAssertion(() => cut.Find("[data-testid=action-error]").TextContent.Should().Contain("required by: other.mod"));
    }

    // ── VersionPanel ──

    [Fact]
    public void Versions_ListsAll_FlagsActive()
    {
        (string, bool)? toggled = null;
        var cut = this.RenderComponent<VersionPanel>(p => p
            .Add(x => x.Versions, Doc().Versions)
            .Add(x => x.OnToggle, r => toggled = (r.Version, r.Enable)));

        cut.Find("[data-testid='version-1.0.0']").TextContent.Should().Contain("active");
        cut.Find("[data-testid='version-0.9.0']").TextContent.Should().NotContain("active");

        cut.Find("[data-testid='ver-enable-0.9.0']").Click();
        toggled.Should().Be(("0.9.0", true));
    }

    // ── Page integration: version enable posts ?version= ──

    [Fact]
    public void Versions_EnableSpecific_PostsVersion()
    {
        var detailsJson = System.Text.Json.JsonSerializer.Serialize(Details("builtin.script", true), ApiHttp.Json);
        var listJson = "[{\"id\":\"builtin.script\",\"displayName\":\"Script\",\"category\":\"Scripting\",\"description\":\"d\",\"icon\":\"📜\",\"version\":\"1.0.0\",\"enabled\":true}]";
        var handler = new FakeHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (req.Method == HttpMethod.Post && path.Contains("/enable"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Toggle("builtin.script", true)) };
            }

            return path.EndsWith("/modules")
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(listJson) }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(detailsJson) };
        });
        this.Services.AddSingleton(new AuthState());
        this.Services.AddSingleton(new ToastService());
        this.Services.AddSingleton(new ApiClientOptions { BaseUrl = "http://localhost" });
        this.Services.AddSingleton(new ModulesClient(handler.CreateClient()));
        this.Services.AddSingleton<ILocalStorage>(new Workflow.Tests.UI.Scripts.Components.InMemoryLocalStorage());

        var cut = this.RenderComponent<ModuleManager>();
        cut.WaitForAssertion(() => cut.Find("[data-testid='modcard-builtin.script']"));
        cut.Find("[data-testid='modcard-builtin.script']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='ver-enable-0.9.0']"));
        cut.Find("[data-testid='ver-enable-0.9.0']").Click();

        cut.WaitForAssertion(() => handler.Requests.Should().Contain(r =>
            r.Method == HttpMethod.Post &&
            r.RequestUri!.ToString().Contains("/enable") &&
            r.RequestUri!.ToString().Contains("version=0.9.0")));
    }
}
