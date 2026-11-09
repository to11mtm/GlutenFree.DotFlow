// <copyright file="OutboundHttpUiTests.cs" company="GlutenFree">
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
using Xunit;

/// <summary>
/// Stage 7.3 — the outbound HTTP setup screen (locked fields, saving, validation, address check) and
/// the designer banner shown while a channel is unconfigured.
/// </summary>
public sealed class OutboundHttpUiTests : TestContext
{
    private const string View = """
        {
          "channels": [
            { "channel": "HttpModule",
              "effective": { "channel": "HttpModule", "mode": "PublicOnly", "configured": false, "allowedHosts": [], "deniedHosts": [], "allowedCidrs": [], "deniedCidrs": [], "allowCloudMetadata": false, "lockedFields": [] },
              "saved": null, "host": null },
            { "channel": "Scripts",
              "effective": { "channel": "Scripts", "mode": "AllowlistOnly", "configured": true, "allowedHosts": [], "deniedHosts": [], "allowedCidrs": [], "deniedCidrs": [], "allowCloudMetadata": false, "lockedFields": ["Mode"] },
              "saved": null, "host": { "mode": "AllowlistOnly" } }
          ],
          "savedDefault": null, "hostDefault": null, "unconfiguredMode": "PublicOnly"
        }
        """;

    public OutboundHttpUiTests()
    {
        this.JSInterop.Mode = JSRuntimeMode.Loose;
        this.Services.AddSingleton<ILocalStorage>(new FakeLocalStorage());
        this.Services.AddSingleton(new ToastService());
        this.Services.AddSingleton(new AuthState());
        this.Services.AddSingleton(new ApiClientOptions { BaseUrl = "http://localhost" });
    }

    private static HttpResponseMessage Ok(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    private FakeHttpMessageHandler UsePolicyApi(Func<HttpRequestMessage, HttpResponseMessage>? onPut = null, string checkJson = "{}")
    {
        var handler = new FakeHttpMessageHandler(req => req.RequestUri!.AbsolutePath switch
        {
            "/api/v1/settings/outbound-http" when req.Method == HttpMethod.Put => onPut?.Invoke(req) ?? Ok(View),
            "/api/v1/settings/outbound-http" => Ok(View),
            "/api/v1/settings/outbound-http/audit" => Ok("""[ { "at": "2026-11-09T10:00:00Z", "by": "admin", "changes": ["Scripts.Mode: (unset) → AllowlistOnly"] } ]"""),
            "/api/v1/settings/outbound-http/check" => Ok(checkJson),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        this.Services.AddSingleton(new OutboundHttpClient(handler.CreateClient()));
        return handler;
    }

    [Fact]
    public void ThePage_ShowsUnconfiguredChannels_LockedFields_AndTheAudit()
    {
        this.UsePolicyApi();

        var cut = this.RenderComponent<OutboundHttpSettings>();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid=ob-unconfigured-HttpModule]").Should().ContainSingle());
        cut.FindAll("[data-testid=ob-unconfigured-Scripts]").Should().BeEmpty();
        cut.Find("[data-testid=ob-locked-Scripts-Mode]").TextContent.Should().Contain("AllowlistOnly").And.Contain("server's configuration");
        cut.FindAll("[data-testid=ob-mode-Scripts]").Should().BeEmpty(because: "a locked field isn't editable");
        cut.Find("[data-testid=ob-audit]").TextContent.Should().Contain("Scripts.Mode");
    }

    [Fact]
    public void Saving_SendsOnlyEditableFields()
    {
        var handler = this.UsePolicyApi();
        var cut = this.RenderComponent<OutboundHttpSettings>();
        cut.WaitForAssertion(() => cut.FindAll("[data-testid=ob-mode-HttpModule]").Should().ContainSingle());

        cut.Find("[data-testid=ob-mode-HttpModule]").Change("InternalOnly");
        cut.Find("[data-testid=ob-AllowedHosts-HttpModule]").Change("api.partner.com\n*.partner.com\n");
        cut.Find("[data-testid=ob-metadata-HttpModule]").Change("block");
        cut.Find("[data-testid=ob-save]").Click();

        cut.WaitForAssertion(() => handler.Requests.Should().Contain(r => r.Method == HttpMethod.Put));
        var body = JsonDocument.Parse(handler.Bodies.Last(b => b.Contains("httpModule", StringComparison.OrdinalIgnoreCase))).RootElement;
        var module = body.GetProperty("httpModule");
        module.GetProperty("mode").GetString().Should().Be("InternalOnly");
        module.GetProperty("allowedHosts").EnumerateArray().Select(e => e.GetString()).Should().Equal("api.partner.com", "*.partner.com");
        module.GetProperty("allowCloudMetadata").GetBoolean().Should().BeFalse();
        (body.TryGetProperty("scripts", out var scripts) && scripts.ValueKind == JsonValueKind.Object && scripts.TryGetProperty("mode", out var m) && m.ValueKind != JsonValueKind.Null)
            .Should().BeFalse(because: "the locked Scripts mode isn't sent");
    }

    [Fact]
    public void ARefusedSave_ShowsTheReasons()
    {
        this.UsePolicyApi(onPut: _ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"title":"One or more validation errors occurred.","status":400,"errors":{"policy":["HttpModule: '10.0.0.0/33' isn't an address or address range (e.g. 10.20.0.0/16)."]}}""", System.Text.Encoding.UTF8, "application/problem+json"),
        });
        var cut = this.RenderComponent<OutboundHttpSettings>();
        cut.WaitForAssertion(() => cut.FindAll("[data-testid=ob-save]").Should().ContainSingle());

        cut.Find("[data-testid=ob-save]").Click();

        cut.WaitForAssertion(() => cut.Find("[data-testid=ob-save-errors]").TextContent.Should().Contain("10.0.0.0/33"));
    }

    [Fact]
    public void CheckingAnAddress_ShowsTheOutcome()
    {
        this.UsePolicyApi(checkJson: """{ "allowed": false, "reason": "127.0.0.1 is an internal address and the mode is PublicOnly", "addresses": [ { "address": "127.0.0.1", "allowed": false, "reason": "internal" } ] }""");
        var cut = this.RenderComponent<OutboundHttpSettings>();
        cut.WaitForAssertion(() => cut.FindAll("[data-testid=ob-check-run]").Should().ContainSingle());

        cut.Find("[data-testid=ob-check-url]").Change("http://localhost/x");
        cut.Find("[data-testid=ob-check-run]").Click();

        cut.WaitForAssertion(() => cut.Find("[data-testid=ob-check-result]").TextContent.Should().Contain("Blocked").And.Contain("PublicOnly"));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void TheDesigner_WarnsWhileOutboundHttpIsUnconfigured(bool configured, bool bannerExpected)
    {
        var status = JsonSerializer.Serialize(new
        {
            version = "1",
            outboundHttp = new
            {
                scripts = new { mode = "PublicOnly", configured },
                httpModule = new { mode = "PublicOnly", configured },
            },
        });
        var id = Guid.NewGuid();
        var workflow = JsonSerializer.Serialize(
            new WorkflowDto(id, "wf", null, "1.0.0", new List<NodeDto>(), new List<ConnectionDto>(), new Dictionary<string, JsonElement>(), null, null, null, null, new List<string>()),
            ApiHttp.Json);
        var handler = new FakeHttpMessageHandler(req => req.RequestUri!.AbsolutePath switch
        {
            "/api/v1/status" => Ok(status),
            "/api/v1/modules" => Ok("[]"),
            var path when path.StartsWith("/api/v1/workflows/", StringComparison.Ordinal) => Ok(workflow),
            _ => Ok("{}"),
        });
        var client = handler.CreateClient();
        this.Services.AddSingleton(new SystemClient(client));
        this.Services.AddSingleton(new WorkflowsClient(client));
        this.Services.AddSingleton(new ModulesClient(client));
        this.Services.AddSingleton(new VariablesClient(client));
        this.Services.AddSingleton(new ExecutionsClient(client));
        this.Services.AddSingleton(new Workflow.UI.Client.Services.PaletteDragState());
        this.Services.AddSingleton(new Workflow.UI.Client.Designer.State.DesignerClipboard());
        this.Services.AddSingleton(new Workflow.UI.Client.Scripts.State.ScriptStudioHandoff());
        this.Services.AddSingleton(new Workflow.UI.Client.Linq.State.LinqStudioHandoff());
        this.Services.AddSingleton(new Workflow.UI.Client.Designer.State.WorkflowImportHandoff());
        this.Services.AddSingleton<Workflow.UI.Client.Services.IFileDownloader>(new NoopFileDownloader());
        this.Services.AddSingleton(sp => new RealTimeClient(sp.GetRequiredService<ApiClientOptions>(), sp.GetRequiredService<AuthState>()));

        var cut = this.RenderComponent<Designer>(p => p.Add(x => x.Id, id.ToString()));

        if (bannerExpected)
        {
            cut.WaitForAssertion(() => cut.Find("[data-testid=outbound-banner]").TextContent.Should().Contain("HTTP Request").And.Contain("PublicOnly"));
            cut.Find("[data-testid=outbound-banner-setup]").GetAttribute("href").Should().Be("/settings/outbound-http");
            cut.Find("[data-testid=outbound-banner-dismiss]").Click();
            cut.FindAll("[data-testid=outbound-banner]").Should().BeEmpty();
        }
        else
        {
            cut.WaitForAssertion(() => handler.Requests.Should().Contain(r => r.RequestUri!.AbsolutePath == "/api/v1/status"));
            cut.FindAll("[data-testid=outbound-banner]").Should().BeEmpty();
        }
    }

    private sealed class FakeLocalStorage : ILocalStorage
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
