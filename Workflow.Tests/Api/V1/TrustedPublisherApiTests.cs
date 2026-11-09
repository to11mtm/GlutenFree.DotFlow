// <copyright file="TrustedPublisherApiTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Api.V1;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Modules.Security;
using Xunit;
using static Workflow.Tests.Modules.Security.SigningTestKit;

/// <summary>
/// Stage 7.4 — the API side of signed module packages: an upload reports who signed it and whether
/// they're trusted, the module list shows each installed module's trust, and the Trusted publishers
/// settings (server-configured ones locked, saves validated and audited, effective immediately).
/// </summary>
public sealed class TrustedPublisherApiTests
{
    private const string Settings = "/api/v1/settings/module-publishers";

    [Fact]
    public async Task ASignedUpload_ReportsItsPublisher_AndTheListShowsIt()
    {
        using var publisher = SelfSigned("Acme Modules");
        using var host = new Host(("Modules:Security:TrustedPublishers:Certificates:0:Thumbprint", publisher.GetCertHashString(HashAlgorithmName.SHA256)));
        var client = host.CreateClient();

        var upload = await UploadAsync(client, Package(publisher));

        upload.StatusCode.Should().Be(HttpStatusCode.Created, await upload.Content.ReadAsStringAsync());
        var trust = (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("trust");
        trust.GetProperty("status").GetString().Should().Be("verified");
        trust.GetProperty("publisher").GetString().Should().Be("Acme Modules");
        trust.GetProperty("trustSource").GetString().Should().Be("pinnedCertificate");

        var list = await client.GetFromJsonAsync<JsonElement>("/api/v1/modules");
        var sample = list.EnumerateArray().Single(m => m.GetProperty("id").GetString() == "sample.log");
        sample.GetProperty("trust").GetProperty("status").GetString().Should().Be("verified");
        list.EnumerateArray().Where(m => m.GetProperty("id").GetString() != "sample.log")
            .Select(m => m.TryGetProperty("trust", out var t) ? t.ValueKind : JsonValueKind.Null)
            .Should().OnlyContain(k => k == JsonValueKind.Null, because: "built-in modules have no package trust");
    }

    [Fact]
    public async Task AnUnsignedUpload_ReportsUnsigned()
    {
        using var host = new Host();
        var client = host.CreateClient();

        var upload = await UploadAsync(client, Package(signer: null));

        upload.StatusCode.Should().Be(HttpStatusCode.Created, await upload.Content.ReadAsStringAsync());
        (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("trust").GetProperty("status").GetString().Should().Be("unsigned");
    }

    [Fact]
    public async Task ThePublishersPage_ShowsServerOnesLocked_AndSavesAreAuditedAndTakeEffect()
    {
        using var configured = SelfSigned("Configured Publisher");
        using var added = SelfSigned("Added Publisher");
        using var host = new Host(
            ("Modules:Security:RequireSigned", "true"),
            ("Modules:Security:TrustedPublishers:Certificates:0:Name", "Configured"),
            ("Modules:Security:TrustedPublishers:Certificates:0:Pem", configured.ExportCertificatePem()));
        var client = host.CreateClient();

        var view = await client.GetFromJsonAsync<JsonElement>(Settings);
        view.GetProperty("requireSigned").GetBoolean().Should().BeTrue();
        var first = view.GetProperty("certificates")[0];
        first.GetProperty("locked").GetBoolean().Should().BeTrue();
        first.GetProperty("thumbprint").GetString().Should().Be(configured.GetCertHashString(HashAlgorithmName.SHA256));

        // Before: a package by the not-yet-trusted publisher is refused under RequireSigned~
        (await UploadAsync(client, Package(added))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var put = await client.PutAsJsonAsync(Settings, new { certificates = new[] { new { pem = added.ExportCertificatePem() } } });
        put.StatusCode.Should().Be(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
        var saved = (await put.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("certificates").EnumerateArray().ToList();
        saved.Should().HaveCount(2, because: "the server's publisher stays, the saved one is added");
        saved[1].GetProperty("name").GetString().Should().Be("Added Publisher", because: "the name defaults to the certificate's common name");
        saved[1].GetProperty("locked").GetBoolean().Should().BeFalse();

        var audit = await client.GetFromJsonAsync<JsonElement>($"{Settings}/audit");
        audit[0].GetProperty("changes")[0].GetString().Should().StartWith("Added publisher Added Publisher");

        // After: the same package installs, verified~
        var upload = await UploadAsync(client, Package(added));
        upload.StatusCode.Should().Be(HttpStatusCode.Created, await upload.Content.ReadAsStringAsync());
        (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("trust").GetProperty("status").GetString().Should().Be("verified");
    }

    [Fact]
    public async Task InvalidPublishers_AreRefused_WithReasons()
    {
        using var host = new Host(("Modules:Security:TrustedPublishers:CheckRevocation", "false"));
        var client = host.CreateClient();

        var put = await client.PutAsJsonAsync(Settings, new
        {
            certificates = new object[] { new { name = "No id" }, new { thumbprint = "abc" } },
            rootCertificates = new object[] { new { name = "Root", thumbprint = new string('A', 64) } },
            checkRevocation = true,
        });

        put.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await put.Content.ReadAsStringAsync();
        body.Should().Contain("give a thumbprint or the certificate")
            .And.Contain("SHA-256 (64 hex digits)")
            .And.Contain("a CA root needs its certificate as valid PEM")
            .And.Contain("set by the server's configuration");
        host.Services.GetRequiredService<ITrustedPublisherProvider>().Current.IsEmpty.Should().BeTrue();
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, byte[] package)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(package);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(file, "package", "module.wfmod");
        return await client.PostAsync("/api/v1/modules/upload", content);
    }

    private sealed class Host : WebApplicationFactory<Program>
    {
        private readonly (string Key, string Value)[] settings;
        private readonly string packages = Path.Combine(Path.GetTempPath(), "wfmod-pub-" + Guid.NewGuid().ToString("N"));

        public Host(params (string Key, string Value)[] settings) => this.settings = settings;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Persistence:Provider", "sqlite");
            builder.UseSetting("Persistence:ConnectionString", Workflow.Tests.TestSqlite.InMemory());
            builder.UseSetting("Modules:PackagesPath", this.packages);
            foreach (var (key, value) in this.settings)
            {
                builder.UseSetting(key, value);
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            try
            {
                if (Directory.Exists(this.packages))
                {
                    Directory.Delete(this.packages, recursive: true);
                }
            }
            catch
            {
                // best effort (a loaded plugin DLL may still be locked)
            }
        }
    }
}
