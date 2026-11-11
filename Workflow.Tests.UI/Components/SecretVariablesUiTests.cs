// <copyright file="SecretVariablesUiTests.cs" company="GlutenFree">
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
using Workflow.UI.Client.Settings.Components;
using Xunit;

/// <summary>
/// Plan 08 part 3 — secret variables in the UI: Settings shows each global's secret mode (secrets
/// masked with who/when; follow-mode warned), marking a variable secret asks before erasing earlier
/// values, a secret's value isn't put back in the form, Settings shows where the encryption keys
/// live, and the designer asks before a save turns stored "follow" variables secret.
/// </summary>
public sealed class SecretVariablesUiTests : TestContext
{
    public SecretVariablesUiTests()
    {
        this.JSInterop.Mode = JSRuntimeMode.Loose;
        this.Services.AddSingleton(new ToastService());
        this.Services.AddSingleton(new AuthState());
        this.Services.AddSingleton(new ApiClientOptions { BaseUrl = "http://localhost" });
    }

    private static HttpResponseMessage Ok(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    private const string ApiKeyDetail = """{ "name": "apiKey", "value": null, "valueTypeName": "string", "version": 1, "scope": "Global", "scopeId": null, "createdAt": "2026-11-11T10:00:00Z", "updatedAt": "2026-11-11T10:00:00Z", "isSecret": true, "secretMode": "Secret", "lastSetBy": "admin", "lastSetAt": "2026-11-11T10:00:00Z" }""";
    private const string UrlDetail = """{ "name": "url", "value": "https://x", "valueTypeName": "string", "version": 2, "scope": "Global", "scopeId": null, "createdAt": "2026-11-11T10:00:00Z", "updatedAt": "2026-11-11T10:00:00Z", "isSecret": false, "secretMode": "FollowWorkflows" }""";

    private FakeHttpMessageHandler UseVariablesApi(Func<HttpRequestMessage, HttpResponseMessage>? onSecret = null)
    {
        var handler = new FakeHttpMessageHandler(req => req.RequestUri!.AbsolutePath switch
        {
            "/api/v1/variables" => Ok("""{ "apiKey": "••••••", "url": "https://x" }"""),
            "/api/v1/variables/apiKey" => Ok(ApiKeyDetail),
            "/api/v1/variables/url" => Ok(UrlDetail),
            var p when p.EndsWith("/secret", StringComparison.Ordinal) => onSecret?.Invoke(req) ?? Ok(UrlDetail),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        this.Services.AddSingleton(new VariablesClient(handler.CreateClient()));
        return handler;
    }

    [Fact]
    public void TheGlobalsPanel_MasksSecrets_WithWhoAndWhen_AndWarnsAboutFollowMode()
    {
        this.UseVariablesApi();

        var cut = this.RenderComponent<GlobalVariablesPanel>();

        cut.WaitForAssertion(() => cut.Find("[data-testid=gv-secret-apiKey]").TextContent.Should().Contain("secret").And.Contain("set by admin"));
        cut.Find("[data-testid=gv-row-apiKey]").TextContent.Should().NotContain("••••••", because: "the row says it's secret instead of showing a mask");
        cut.Find("[data-testid=gv-follow-url]").TextContent.Should().Contain("becomes secret");
    }

    [Fact]
    public void MarkingSecret_AsksBeforeErasingEarlierValues_ThenConfirms()
    {
        var handler = this.UseVariablesApi(req => req.RequestUri!.Query.Contains("confirmPurge=true", StringComparison.Ordinal)
            ? Ok(UrlDetail)
            : new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = new StringContent("""{"title":"Confirm erasing earlier values","status":409,"detail":"Marking 'url' secret stores it encrypted and erases its 2 earlier values, which can't be undone.","versions":2}""", System.Text.Encoding.UTF8, "application/problem+json"),
            });
        var cut = this.RenderComponent<GlobalVariablesPanel>();
        cut.WaitForAssertion(() => cut.FindAll("[data-testid=gv-edit-url]").Should().ContainSingle());

        cut.Find("[data-testid=gv-edit-url]").Click();
        cut.Find("[data-testid=gv-mode]").Change("Secret");
        cut.Find("[data-testid=gv-mode-help]").TextContent.Should().Contain("write-only");
        cut.Find("[data-testid=gv-save]").Click();

        cut.WaitForAssertion(() => cut.Find("[data-testid=gv-purge-confirm]").TextContent.Should().Contain("erases its 2 earlier values"));
        handler.Requests.Should().NotContain(r => r.RequestUri!.Query.Contains("confirmPurge=true", StringComparison.Ordinal), because: "nothing is erased before the user confirms");

        cut.Find("[data-testid=gv-purge-yes]").Click();

        cut.WaitForAssertion(() => handler.Requests.Should().Contain(r => r.RequestUri!.Query.Contains("confirmPurge=true", StringComparison.Ordinal)));
        cut.WaitForAssertion(() => cut.FindAll("[data-testid=gv-purge-confirm]").Should().BeEmpty());
    }

    [Fact]
    public void EditingASecret_DoesntPutItsValueInTheForm()
    {
        this.UseVariablesApi();
        var cut = this.RenderComponent<GlobalVariablesPanel>();
        cut.WaitForAssertion(() => cut.FindAll("[data-testid=gv-edit-apiKey]").Should().ContainSingle());

        cut.Find("[data-testid=gv-edit-apiKey]").Click();

        var value = cut.Find("[data-testid=gv-value]");
        (value.GetAttribute("value") ?? string.Empty).Should().BeEmpty();
        value.GetAttribute("placeholder").Should().Contain("can't be shown");
        cut.Find("[data-testid=gv-mode]").GetAttribute("value").Should().Be("Secret");
    }

    [Fact]
    public void Settings_ShowsWhereTheKeysLive_AndTheWarning()
    {
        var status = """{ "version": "1", "dataProtection": { "keyStorage": "Default", "location": "ASP.NET default", "warning": "DataProtection:KeyStorage is 'Default' (dangerous): keys can be lost" } }""";
        var handler = new FakeHttpMessageHandler(_ => Ok(status));
        this.Services.AddSingleton(new SystemClient(handler.CreateClient()));
        this.Services.AddSingleton<ILocalStorage>(new MemoryStorage());
        this.Services.AddSingleton(new VariablesClient(handler.CreateClient()));

        var cut = this.RenderComponent<Settings>();

        cut.WaitForAssertion(() => cut.Find("[data-testid=settings-keys]").TextContent.Should().Contain("Default"));
        cut.Find("[data-testid=settings-keys-warning]").TextContent.Should().Contain("dangerous");
    }

    [Fact]
    public void TheDesigner_AsksBeforeASaveTurnsStoredVariablesSecret()
    {
        var id = Guid.NewGuid();
        var variables = new Dictionary<string, JsonElement>
        {
            ["dbpass"] = JsonDocument.Parse("""{ "name": "dbpass", "type": "String", "isSecret": true }""").RootElement.Clone(),
        };
        var workflow = JsonSerializer.Serialize(
            new WorkflowDto(id, "wf", null, "1.0.0", new List<NodeDto>(), new List<ConnectionDto>(), variables, null, null, null, null, new List<string>()),
            ApiHttp.Json);
        var handler = new FakeHttpMessageHandler(req => req.RequestUri!.AbsolutePath switch
        {
            "/api/v1/status" => Ok("""{ "version": "1" }"""),
            "/api/v1/modules" => Ok("[]"),
            "/api/v1/variables" => Ok("{}"),
            "/api/v1/variables/secret-impact" => Ok("""[ { "name": "dbpass", "scope": "Global", "scopeId": null } ]"""),
            "/api/v1/workflows/validate" => Ok("""{ "valid": true, "issues": [] }"""),
            var path when path.StartsWith("/api/v1/workflows/", StringComparison.Ordinal) => Ok(workflow),
            _ => Ok("{}"),
        });
        var client = handler.CreateClient();
        this.Services.AddSingleton(new SystemClient(client));
        this.Services.AddSingleton(new WorkflowsClient(client));
        this.Services.AddSingleton(new ModulesClient(client));
        this.Services.AddSingleton(new VariablesClient(client));
        this.Services.AddSingleton(new ExecutionsClient(client));
        this.Services.AddSingleton<ILocalStorage>(new MemoryStorage());
        this.Services.AddSingleton(new Workflow.UI.Client.Services.PaletteDragState());
        this.Services.AddSingleton(new Workflow.UI.Client.Designer.State.DesignerClipboard());
        this.Services.AddSingleton(new Workflow.UI.Client.Scripts.State.ScriptStudioHandoff());
        this.Services.AddSingleton(new Workflow.UI.Client.Linq.State.LinqStudioHandoff());
        this.Services.AddSingleton(new Workflow.UI.Client.Designer.State.WorkflowImportHandoff());
        this.Services.AddSingleton<Workflow.UI.Client.Services.IFileDownloader>(new NoopFileDownloader());
        this.Services.AddSingleton(sp => new RealTimeClient(sp.GetRequiredService<ApiClientOptions>(), sp.GetRequiredService<AuthState>()));

        var cut = this.RenderComponent<Designer>(p => p.Add(x => x.Id, id.ToString()));
        cut.WaitForAssertion(() => cut.FindAll("[data-testid=save]").Should().ContainSingle());

        cut.Find("[data-testid=save]").Click();

        cut.WaitForAssertion(() => cut.Find("[data-testid=secret-impact-dialog]").TextContent.Should().Contain("dbpass"));
        handler.Requests.Should().NotContain(r => r.Method == HttpMethod.Put, because: "nothing is saved before the user confirms");

        cut.Find("[data-testid=secret-impact-save]").Click();

        cut.WaitForAssertion(() => handler.Requests.Should().Contain(r => r.Method == HttpMethod.Put && r.RequestUri!.AbsolutePath == $"/api/v1/workflows/{id}"));
    }

    private sealed class MemoryStorage : ILocalStorage
    {
        private readonly Dictionary<string, string> store = new();

        public System.Threading.Tasks.ValueTask<string?> GetAsync(string key) => new(this.store.TryGetValue(key, out var v) ? v : null);

        public System.Threading.Tasks.ValueTask SetAsync(string key, string value)
        {
            this.store[key] = value;
            return default;
        }

        public System.Threading.Tasks.ValueTask RemoveAsync(string key)
        {
            this.store.Remove(key);
            return default;
        }
    }
}
