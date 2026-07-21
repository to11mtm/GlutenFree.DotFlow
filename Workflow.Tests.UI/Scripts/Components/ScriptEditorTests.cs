// <copyright file="ScriptEditorTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Scripts.Components;

using System.Text.Json;
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
/// 🧪 Phase 3.4.0 — bUnit tests for the generalized <see cref="ScriptEditor"/> + the Script Studio
/// shell. Monaco interop is faked (loose mode) so the tested edit surface is the textarea fallback~ ✨.
/// </summary>
public sealed class ScriptEditorTests : TestContext
{
    private readonly AuthState auth = new();
    private readonly ToastService toasts = new();

    public ScriptEditorTests()
    {
        this.JSInterop.Mode = JSRuntimeMode.Loose;
        this.Services.AddSingleton(this.auth);
        this.Services.AddSingleton(this.toasts);
        this.Services.AddSingleton(new ApiClientOptions { BaseUrl = "http://localhost" });
        this.Services.AddSingleton<ILocalStorage>(new InMemoryLocalStorage());
        this.Services.AddSingleton(new Workflow.UI.Client.Scripts.State.ScriptStudioHandoff());
    }

    private void UseHandler(FakeHttpMessageHandler handler)
        => this.Services.AddSingleton(new ScriptsClient(handler.CreateClient()));

    [Fact]
    public void ScriptEditor_FallsBackToTextarea_WhenMonacoUnavailable()
    {
        var cut = this.RenderComponent<ScriptEditor>(p => p
            .Add(e => e.Value, "const x = 1;")
            .Add(e => e.Language, "javascript"));

        // Loose interop makes createEditor return default(false) → the textarea stays.
        cut.WaitForAssertion(() => cut.Find("[data-testid=script-textarea]").Should().NotBeNull());
        cut.Find("[data-testid=script-textarea]").GetAttribute("value").Should().Contain("const x = 1;");
    }

    [Fact]
    public void ScriptStudio_LoadsLanguages_PopulatesDropdown()
    {
        this.UseHandler(FakeHttpMessageHandler.Json("[{\"languageId\":\"javascript\",\"displayName\":\"JavaScript\"},{\"languageId\":\"lua\",\"displayName\":\"Lua\"}]"));

        var cut = this.RenderComponent<ClientScriptStudio>();

        cut.WaitForAssertion(() =>
        {
            var select = cut.Find("[data-testid=language-select]");
            select.InnerHtml.Should().Contain("JavaScript");
            select.InnerHtml.Should().Contain("Lua");
            select.InnerHtml.Should().Contain("Python (edit only)");
        });
    }

    [Fact]
    public void ScriptStudio_PythonSelected_ShowsNonRunnableHint()
    {
        this.UseHandler(FakeHttpMessageHandler.Json("[{\"languageId\":\"javascript\",\"displayName\":\"JavaScript\"}]"));

        var cut = this.RenderComponent<ClientScriptStudio>();
        cut.WaitForAssertion(() => cut.Find("[data-testid=language-select]").InnerHtml.Should().Contain("Python"));

        cut.Find("[data-testid=language-select]").Change("python");

        cut.WaitForAssertion(() => cut.Find("[data-testid=non-runnable-hint]").TextContent.Should().Contain("running it isn't"));
        cut.Find("[data-testid=run-test]").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void ScriptStudio_LanguagesError_ShownInStatus()
    {
        this.UseHandler(new FakeHttpMessageHandler(_ => new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError)
        {
            Content = new System.Net.Http.StringContent("{\"title\":\"Service unavailable\"}", System.Text.Encoding.UTF8, "application/problem+json"),
        }));

        var cut = this.RenderComponent<ClientScriptStudio>();

        // Python is still offered even when the languages call fails.
        cut.WaitForAssertion(() => cut.Find("[data-testid=languages-error]").Should().NotBeNull());
        cut.Find("[data-testid=language-select]").InnerHtml.Should().Contain("Python");
    }
}
