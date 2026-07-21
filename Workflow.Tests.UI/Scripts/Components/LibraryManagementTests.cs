// <copyright file="LibraryManagementTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Scripts.Components;

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
using Workflow.UI.Client.Scripts.Components;
using Workflow.UI.Client.Services;
using ClientScriptStudio = Workflow.UI.Client.Pages.ScriptStudio;
using Xunit;

/// <summary>
/// 📚 Phase 3.4.4 — bUnit tests for library management (list/open/save/delete + deep link)~ ✨.
/// </summary>
public sealed class LibraryManagementTests : TestContext
{
    public LibraryManagementTests()
    {
        this.JSInterop.Mode = JSRuntimeMode.Loose;
        this.Services.AddSingleton(new AuthState());
        this.Services.AddSingleton(new ToastService());
        this.Services.AddSingleton(new ApiClientOptions { BaseUrl = "http://localhost" });
        this.Services.AddSingleton<ILocalStorage>(new InMemoryLocalStorage());
        this.Services.AddSingleton(new Workflow.UI.Client.Scripts.State.ScriptStudioHandoff());
    }

    private static ScriptLibraryDto Lib(string id = "order-utils", string code = "function f(){}")
        => new(id, "Order Utils", "helpers", "javascript", code, new List<string> { "f" }, new List<string>());

    private ScriptsClient Client(FakeHttpMessageHandler handler)
    {
        var client = new ScriptsClient(handler.CreateClient());
        this.Services.AddSingleton(client);
        return client;
    }

    [Fact]
    public void Libraries_List_RendersFromClient()
    {
        this.Client(FakeHttpMessageHandler.Json(JsonSerializer.Serialize(new List<ScriptLibraryDto> { Lib() }, ApiHttp.Json)));

        var cut = this.RenderComponent<LibraryBar>();
        cut.Find("[data-testid=libraries-btn]").Click();

        cut.WaitForAssertion(() => cut.Find("[data-testid=library-item]").TextContent.Should().Contain("Order Utils"));
    }

    [Fact]
    public void Library_Open_LoadsIntoEditor()
    {
        var lib = Lib(code: "const loaded = 123;");
        this.Client(new FakeHttpMessageHandler(req =>
            req.RequestUri!.AbsolutePath.EndsWith("/scripts/languages")
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[{\"languageId\":\"javascript\",\"displayName\":\"JavaScript\"}]") }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new List<ScriptLibraryDto> { lib }, ApiHttp.Json)) }));

        var cut = this.RenderComponent<ClientScriptStudio>();
        cut.WaitForAssertion(() => cut.Find("[data-testid=libraries-btn]"));
        cut.Find("[data-testid=libraries-btn]").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid=library-open]"));
        cut.Find("[data-testid=library-open]").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid=active-library]").TextContent.Should().Contain("Order Utils");
            cut.Find("[data-testid=script-textarea]").GetAttribute("value").Should().Contain("const loaded = 123;");
        });
    }

    [Fact]
    public void SaveExisting_CallsPut()
    {
        var handler = FakeHttpMessageHandler.Json(JsonSerializer.Serialize(Lib(), ApiHttp.Json));
        this.Client(handler);
        ScriptLibraryDto? saved = null;

        var cut = this.RenderComponent<SaveLibraryDialog>(p => p
            .Add(x => x.Code, "function f(){ return 1; }")
            .Add(x => x.Language, "javascript")
            .Add(x => x.ActiveLibrary, Lib())
            .Add(x => x.OnSaved, s => saved = s));

        cut.Find("[data-testid=save-confirm]").Click();

        cut.WaitForAssertion(() =>
        {
            handler.Requests.Should().Contain(r => r.Method == HttpMethod.Put && r.RequestUri!.AbsolutePath.EndsWith("/scripts/libraries/order-utils"));
            saved.Should().NotBeNull();
        });
    }

    [Fact]
    public void SaveAsNew_Posts_WithMetadata()
    {
        var handler = FakeHttpMessageHandler.Json(JsonSerializer.Serialize(Lib("fresh-lib"), ApiHttp.Json));
        this.Client(handler);

        var cut = this.RenderComponent<SaveLibraryDialog>(p => p
            .Add(x => x.Code, "function g(){}")
            .Add(x => x.Language, "javascript"));

        cut.Find("[data-testid=lib-id]").Change("fresh-lib");
        cut.Find("[data-testid=lib-name]").Change("Fresh Lib");
        cut.Find("[data-testid=lib-exports]").Change("g, h");
        cut.Find("[data-testid=save-confirm]").Click();

        cut.WaitForAssertion(() =>
        {
            handler.Requests.Should().Contain(r => r.Method == HttpMethod.Put && r.RequestUri!.AbsolutePath.EndsWith("/scripts/libraries/fresh-lib"));
            var body = handler.Bodies.Last();
            body.Should().Contain("Fresh Lib");
            body.Should().Contain("\"g\"");
            body.Should().Contain("\"h\"");
        });
    }

    [Fact]
    public void Save_ServerValidationError_ShownInDialog()
    {
        this.Client(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = new StringContent("{\"title\":\"Validation failed\",\"detail\":\"Dependency cycle: a → b → a.\"}", System.Text.Encoding.UTF8, "application/problem+json"),
        }));

        var cut = this.RenderComponent<SaveLibraryDialog>(p => p
            .Add(x => x.Code, "x")
            .Add(x => x.Language, "javascript")
            .Add(x => x.ActiveLibrary, Lib()));

        cut.Find("[data-testid=save-confirm]").Click();

        cut.WaitForAssertion(() => cut.Find("[data-testid=save-error]").TextContent.Should().Contain("cycle"));
    }

    [Fact]
    public void Delete_ConfirmsThenCalls_ClearsIfOpen()
    {
        var lib = Lib(code: "const open = 1;");
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Delete)
            {
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            return req.RequestUri!.AbsolutePath.EndsWith("/scripts/languages")
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[{\"languageId\":\"javascript\",\"displayName\":\"JavaScript\"}]") }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new List<ScriptLibraryDto> { lib }, ApiHttp.Json)) };
        });
        this.Client(handler);

        var cut = this.RenderComponent<ClientScriptStudio>();
        cut.WaitForAssertion(() => cut.Find("[data-testid=libraries-btn]"));
        cut.Find("[data-testid=libraries-btn]").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid=library-open]"));
        cut.Find("[data-testid=library-open]").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid=active-library]"));

        // Re-open the menu, delete with confirm.
        cut.Find("[data-testid=libraries-btn]").Click();
        cut.Find("[data-testid=library-delete]").Click();
        handler.Requests.Should().NotContain(r => r.Method == HttpMethod.Delete);
        cut.Find("[data-testid=library-confirm-delete]").Click();

        cut.WaitForAssertion(() =>
        {
            handler.Requests.Should().Contain(r => r.Method == HttpMethod.Delete);
            cut.FindAll("[data-testid=active-library]").Should().BeEmpty();
        });
    }

    [Fact]
    public void DeepLink_LibraryId_LoadsLibrary()
    {
        var lib = Lib("deep-lib", "const deep = 1;");
        this.Client(new FakeHttpMessageHandler(req =>
            req.RequestUri!.AbsolutePath.EndsWith("/scripts/languages")
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[{\"languageId\":\"javascript\",\"displayName\":\"JavaScript\"}]") }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(lib, ApiHttp.Json)) }));

        var cut = this.RenderComponent<ClientScriptStudio>(p => p.Add(x => x.LibraryId, "deep-lib"));

        cut.WaitForAssertion(() => cut.Find("[data-testid=active-library]").TextContent.Should().Contain("Order Utils"));
    }
}
