// <copyright file="UploadTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Modules.Components;

using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Forms;
using Workflow.Tests.UI.Api;
using Workflow.UI.Client.Api;
using Workflow.UI.Client.Api.Dtos;
using Workflow.UI.Client.Modules.Components;
using Xunit;

/// <summary>
/// ⬆️ Phase 3.6.2 — bUnit tests for the module upload dialog (file select + validation feedback)~ ✨.
/// </summary>
public sealed class UploadTests : TestContext
{
    public UploadTests() => this.JSInterop.Mode = JSRuntimeMode.Loose;

    private static string InstallJson(params string[] warnings)
    {
        var details = new ModuleDetailsDto("my.mod", "My Mod", "HTTP", "d", "🔧", "1.3.0",
            new ModuleSchemaDto(new(), new(), new()), new List<string>(), true, new List<string> { "1.3.0" });
        var result = new ModuleInstallResultDto(details, new List<string>(warnings));
        return System.Text.Json.JsonSerializer.Serialize(result, ApiHttp.Json);
    }

    private IRenderedComponent<UploadDialog> Render(FakeHttpMessageHandler handler, System.Action? onUploaded = null)
        => this.RenderComponent<UploadDialog>(p => p
            .Add(x => x.Modules, new ModulesClient(handler.CreateClient()))
            .Add(x => x.OnUploaded, () => onUploaded?.Invoke()));

    private static void SelectFile(IRenderedComponent<UploadDialog> cut, string name = "my.mod-1.3.0.wfmod")
        => cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("PKfakepackage", name));

    [Fact]
    public void Upload_SelectFile_ShowsNameAndSize()
    {
        var cut = this.Render(FakeHttpMessageHandler.Json(InstallJson()));

        SelectFile(cut);

        cut.WaitForAssertion(() => cut.Find("[data-testid=upload-file]").TextContent.Should().Contain("my.mod-1.3.0.wfmod"));
        cut.Find("[data-testid=upload-submit]").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void Upload_Success_ShowsInstalledAndWarnings()
    {
        var refreshed = false;
        var cut = this.Render(FakeHttpMessageHandler.Json(InstallJson("package is unsigned", "manifest hash missing")), () => refreshed = true);

        SelectFile(cut);
        cut.Find("[data-testid=upload-submit]").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid=upload-success]").TextContent.Should().Contain("my.mod").And.Contain("1.3.0");
            cut.Find("[data-testid=upload-warnings]").TextContent.Should().Contain("unsigned");
        });
        refreshed.Should().BeTrue();
    }

    [Fact]
    public void Upload_Invalid_422_ShownInline()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = new StringContent("{\"title\":\"Validation failed\",\"detail\":\"manifest.json is missing.\"}", Encoding.UTF8, "application/problem+json"),
        });
        var cut = this.Render(handler);

        SelectFile(cut);
        cut.Find("[data-testid=upload-submit]").Click();

        cut.WaitForAssertion(() => cut.Find("[data-testid=upload-error]").TextContent.Should().Contain("manifest.json"));
    }

    [Fact]
    public void Upload_Duplicate_409_ShownInline()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = new StringContent("{\"title\":\"Conflict\",\"detail\":\"Duplicate version 1.3.0 already installed.\"}", Encoding.UTF8, "application/problem+json"),
        });
        var cut = this.Render(handler);

        SelectFile(cut);
        cut.Find("[data-testid=upload-submit]").Click();

        cut.WaitForAssertion(() => cut.Find("[data-testid=upload-error]").TextContent.Should().Contain("Duplicate"));
    }

    [Fact]
    public void Upload_Forbidden_ShowsAdminHint()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("{\"title\":\"Forbidden\"}", Encoding.UTF8, "application/problem+json"),
        });
        var cut = this.Render(handler);

        SelectFile(cut);
        cut.Find("[data-testid=upload-submit]").Click();

        cut.WaitForAssertion(() => cut.Find("[data-testid=upload-error]").TextContent.Should().Contain("requires admin"));
    }
}
