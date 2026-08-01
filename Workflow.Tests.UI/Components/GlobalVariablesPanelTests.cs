// <copyright file="GlobalVariablesPanelTests.cs" company="GlutenFree">
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
using Microsoft.Extensions.DependencyInjection;
using Workflow.Tests.UI.Api;
using Workflow.UI.Client.Api;
using Workflow.UI.Client.Services;
using Workflow.UI.Client.Settings.Components;
using Xunit;

/// <summary>
/// 🌍 Phase 3.5 (V9.2/V9.3) — the admin-gated global variables panel in Settings.
/// </summary>
public sealed class GlobalVariablesPanelTests : TestContext
{
    public GlobalVariablesPanelTests()
    {
        this.JSInterop.Mode = JSRuntimeMode.Loose;
        this.Services.AddSingleton(new ToastService());
    }

    private IRenderedComponent<GlobalVariablesPanel> Render(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new FakeHttpMessageHandler(respond);
        this.Services.AddSingleton(new VariablesClient(handler.CreateClient()));
        return this.RenderComponent<GlobalVariablesPanel>();
    }

    private static HttpResponseMessage Ok(string json)
        => new(HttpStatusCode.OK) { Content = new StringContent(json) };

    [Fact]
    public void ListsGlobals_AndCarriesTheInterimCredentialWarning()
    {
        var cut = this.Render(_ => Ok("""{ "apiBaseUrl": "https://example.com", "retries": 3 }"""));

        cut.WaitForAssertion(() => cut.FindAll("[data-testid=gv-row-apiBaseUrl]").Should().NotBeEmpty());
        cut.Find("[data-testid=gv-row-retries]").TextContent.Should().Contain("3");

        // V3.5's warning belongs most on the write surface.
        cut.Find("[data-testid=gv-warning]").TextContent.Should().Contain("Not for credentials yet");
    }

    [Fact]
    public void EmptyStore_ShowsTheEmptyState()
    {
        var cut = this.Render(_ => Ok("{}"));

        cut.WaitForAssertion(() => cut.Find("[data-testid=gv-empty]").Should().NotBeNull());
    }

    [Fact]
    public void ForbiddenWrite_DegradesToAClearAdminOnlyMessage()
    {
        // V9.3 — AuthState only knows *whether* there's a credential, not what it can do, so the
        // server's 403 is the authoritative signal rather than a client-side role guess.
        var cut = this.Render(req => req.Method == HttpMethod.Put
            ? new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("""{ "title": "Forbidden", "detail": "requires the Admin role." }"""),
            }
            : Ok("{}"));

        cut.WaitForAssertion(() => cut.Find("[data-testid=gv-add]").Should().NotBeNull());
        cut.Find("[data-testid=gv-add]").Click();
        cut.Find("[data-testid=gv-name]").Input("apiBaseUrl");
        cut.Find("[data-testid=gv-value]").Input("\"https://example.com\"");
        cut.Find("[data-testid=gv-save]").Click();

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid=gv-admin-only]").TextContent.Should().Contain("Admin role"));

        // …and the write controls stop pretending they'll work.
        cut.Find("[data-testid=gv-add]").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void ForbiddenList_ExplainsRatherThanShowingAnEmptyStore()
    {
        var cut = this.Render(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("""{ "title": "Forbidden" }"""),
        });

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid=gv-load-error]").TextContent.Should().Contain("permission"));
        cut.FindAll("[data-testid=gv-empty]").Should().BeEmpty(because: "'no access' and 'none exist' are different");
    }

    [Fact]
    public void InvalidName_IsRejectedBeforeCallingTheApi()
    {
        var puts = 0;
        var cut = this.Render(req =>
        {
            if (req.Method == HttpMethod.Put)
            {
                puts++;
            }

            return Ok("{}");
        });

        cut.WaitForAssertion(() => cut.Find("[data-testid=gv-add]").Should().NotBeNull());
        cut.Find("[data-testid=gv-add]").Click();
        cut.Find("[data-testid=gv-name]").Input("has space");
        cut.Find("[data-testid=gv-value]").Input("1");
        cut.Find("[data-testid=gv-save]").Click();

        cut.Find("[data-testid=gv-error]").TextContent.Should().Contain("letter or underscore");
        puts.Should().Be(0);
    }

    [Fact]
    public void InvalidJsonValue_IsRejected()
    {
        var cut = this.Render(_ => Ok("{}"));

        cut.WaitForAssertion(() => cut.Find("[data-testid=gv-add]").Should().NotBeNull());
        cut.Find("[data-testid=gv-add]").Click();
        cut.Find("[data-testid=gv-name]").Input("ok");
        cut.Find("[data-testid=gv-value]").Input("{ not json");
        cut.Find("[data-testid=gv-save]").Click();

        cut.Find("[data-testid=gv-error]").TextContent.Should().Contain("valid JSON");
    }

    [Fact]
    public void History_ListsEveryStoredVersion()
    {
        var history = JsonSerializer.Serialize(new[]
        {
            new
            {
                name = "apiBaseUrl", value = "v1", valueTypeName = "String", version = 1,
                scope = "Global", scopeId = (Guid?)null,
                createdAt = DateTimeOffset.UtcNow, updatedAt = DateTimeOffset.UtcNow,
            },
        }, ApiHttp.Json);

        var cut = this.Render(req => req.RequestUri!.AbsolutePath.EndsWith("/history", StringComparison.Ordinal)
            ? Ok(history)
            : Ok("""{ "apiBaseUrl": "v1" }"""));

        cut.WaitForAssertion(() => cut.Find("[data-testid=gv-history-apiBaseUrl]").Should().NotBeNull());
        cut.Find("[data-testid=gv-history-apiBaseUrl]").Click();

        cut.WaitForAssertion(() => cut.Find("[data-testid=gv-history-list]").TextContent.Should().Contain("v1"));
    }
}
