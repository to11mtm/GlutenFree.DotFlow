// <copyright file="ModuleTrustUiTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Modules.Components;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Tests.UI.Api;
using Workflow.UI.Client.Api;
using Workflow.UI.Client.Api.Dtos;
using Workflow.UI.Client.Modules.Components;
using Workflow.UI.Client.Pages;
using Workflow.UI.Client.Services;
using Xunit;

/// <summary>
/// Stage 7.4 — signed module packages in the UI: the trust badge on a module card, the signing result
/// in the upload dialog, and the Trusted publishers page (server entries locked, saves send only the
/// editable ones, reasons shown when refused).
/// </summary>
public sealed class ModuleTrustUiTests : TestContext
{
    private const string View = """
        {
          "certificates": [
            { "name": "Configured", "thumbprint": "AAAA", "subject": "CN=Configured", "notAfter": "2030-01-01T00:00:00Z", "pem": "-----BEGIN CERTIFICATE-----\nAAA\n-----END CERTIFICATE-----", "locked": true },
            { "name": "Saved Publisher", "thumbprint": "BBBB", "subject": "CN=Saved Publisher", "notAfter": "2030-01-01T00:00:00Z", "pem": "-----BEGIN CERTIFICATE-----\nBBB\n-----END CERTIFICATE-----", "locked": false }
          ],
          "rootCertificates": [],
          "checkRevocation": false,
          "checkRevocationLocked": true,
          "requireSigned": true
        }
        """;

    public ModuleTrustUiTests()
    {
        this.JSInterop.Mode = JSRuntimeMode.Loose;
        this.Services.AddSingleton(new ToastService());
        this.Services.AddSingleton(new AuthState());
        this.Services.AddSingleton(new ApiClientOptions { BaseUrl = "http://localhost" });
    }

    [Theory]
    [InlineData("verified", "Acme Modules", "✔ Verified: Acme Modules", "df-trust--ok")]
    [InlineData("keyPinOnly", null, "🔑 Key pin only", "df-trust--warn")]
    [InlineData("changedOnDisk", "Acme Modules", "⚠ Changed on disk", "df-trust--bad")]
    [InlineData("unsigned", null, "Unsigned", "df-trust--none")]
    public void TheModuleCard_ShowsItsTrust(string status, string? publisher, string label, string css)
    {
        var module = new ModuleSummaryDto("sample.log", "Log", "Test", "d", "📝", "1.0.0")
        {
            Trust = new ModuleTrustDto(status, publisher, "ABCD", null, new List<string> { "why it isn't verified" }, DateTimeOffset.UtcNow),
        };

        var cut = this.RenderComponent<ModuleCard>(p => p.Add(x => x.Module, module));

        var badge = cut.Find("[data-testid=trust-sample\\.log]");
        badge.TextContent.Should().Be(label);
        badge.ClassList.Should().Contain(css);
        if (status != "verified")
        {
            badge.GetAttribute("title").Should().Contain("why it isn't verified");
        }
    }

    [Fact]
    public void ABuiltInModuleCard_HasNoTrustBadge()
    {
        var cut = this.RenderComponent<ModuleCard>(p => p.Add(x => x.Module, new ModuleSummaryDto("builtin.log", "Log", "Test", "d", "📝", "1.0.0")));

        cut.FindAll("[data-testid^=trust-]").Should().BeEmpty();
    }

    [Theory]
    [InlineData("verified", false)]
    [InlineData("untrusted", true)]
    public void TheUploadDialog_ShowsWhoSignedIt(string status, bool helpShown)
    {
        var details = new ModuleDetailsDto("sample.log", "Log", "Test", "d", "📝", "1.0.0",
            new ModuleSchemaDto(new(), new(), new()), new List<string>(), true, new List<string> { "1.0.0" });
        var result = new ModuleInstallResultDto(details, new List<string>())
        {
            Trust = new ModuleTrustDto(status, "Acme Modules", "ABCD", status == "verified" ? "pinnedCertificate" : null, new List<string>(), DateTimeOffset.UtcNow),
        };
        var handler = FakeHttpMessageHandler.Json(JsonSerializer.Serialize(result, ApiHttp.Json));
        var cut = this.RenderComponent<UploadDialog>(p => p.Add(x => x.Modules, new ModulesClient(handler.CreateClient())));

        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("PK", "sample.wfmod"));
        cut.Find("[data-testid=upload-submit]").Click();

        cut.WaitForAssertion(() => cut.Find("[data-testid=upload-trust]").TextContent.Should().Contain("Acme Modules"));
        cut.FindAll("[data-testid=upload-trust-help]").Should().HaveCount(helpShown ? 1 : 0);
    }

    [Fact]
    public void ThePublishersPage_LocksServerEntries_AndSavesOnlyTheEditableOnes()
    {
        var handler = this.UsePublishersApi();
        var cut = this.RenderComponent<TrustedPublishersSettings>();
        cut.WaitForAssertion(() => cut.FindAll("[data-testid=tp-publishers-1]").Should().ContainSingle());

        cut.Find("[data-testid=tp-locked-publishers-0]").TextContent.Should().Contain("server's configuration");
        cut.FindAll("[data-testid=tp-remove-publishers-0]").Should().BeEmpty(because: "a server-configured publisher can't be removed here");
        cut.Find("[data-testid=tp-check-revocation]").HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-testid=tp-policy]").TextContent.Should().Contain("Signing is required");

        cut.Find("[data-testid=tp-remove-publishers-1]").Click();
        cut.Find("[data-testid=tp-new-thumbprint-publishers]").Change(new string('C', 64));
        cut.Find("[data-testid=tp-new-add-publishers]").Click();
        cut.Find("[data-testid=tp-save]").Click();

        cut.WaitForAssertion(() => handler.Requests.Should().Contain(r => r.Method == HttpMethod.Put));
        var body = JsonDocument.Parse(handler.Bodies.Last(b => b.Contains("certificates", StringComparison.OrdinalIgnoreCase))).RootElement;
        var sent = body.GetProperty("certificates").EnumerateArray().ToList();
        sent.Should().ContainSingle(because: "the locked one isn't sent and the removed one is gone");
        sent[0].GetProperty("thumbprint").GetString().Should().Be(new string('C', 64));
        body.TryGetProperty("checkRevocation", out var revocation).Should().BeTrue();
        revocation.ValueKind.Should().Be(JsonValueKind.Null, because: "a locked setting isn't sent");
    }

    [Fact]
    public void ARefusedSave_ShowsTheReasons()
    {
        this.UsePublishersApi(onPut: _ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"title":"One or more validation errors occurred.","status":400,"errors":{"publishers":["Publisher 1: the thumbprint must be a SHA-256 (64 hex digits) or SHA-1 (40 hex digits) thumbprint."]}}""", System.Text.Encoding.UTF8, "application/problem+json"),
        });
        var cut = this.RenderComponent<TrustedPublishersSettings>();
        cut.WaitForAssertion(() => cut.FindAll("[data-testid=tp-save]").Should().ContainSingle());

        cut.Find("[data-testid=tp-save]").Click();

        cut.WaitForAssertion(() => cut.Find("[data-testid=tp-save-errors]").TextContent.Should().Contain("64 hex digits"));
    }

    private static HttpResponseMessage Ok(string json) => new(HttpStatusCode.OK) { Content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    private FakeHttpMessageHandler UsePublishersApi(Func<HttpRequestMessage, HttpResponseMessage>? onPut = null)
    {
        var handler = new FakeHttpMessageHandler(req => req.RequestUri!.AbsolutePath switch
        {
            "/api/v1/settings/module-publishers" when req.Method == HttpMethod.Put => onPut?.Invoke(req) ?? Ok(View),
            "/api/v1/settings/module-publishers" => Ok(View),
            "/api/v1/settings/module-publishers/audit" => Ok("""[ { "at": "2026-11-09T10:00:00Z", "by": "admin", "changes": ["Added publisher Saved Publisher (BBBB)"] } ]"""),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        this.Services.AddSingleton(new TrustedPublishersClient(handler.CreateClient()));
        return handler;
    }
}
