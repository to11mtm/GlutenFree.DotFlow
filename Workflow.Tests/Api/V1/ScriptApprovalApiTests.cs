// <copyright file="ScriptApprovalApiTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Api.V1;

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Workflow.Api.Auth;
using Xunit;

/// <summary>
/// §9.2 end to end over HTTP: a C# script using a sensitive API is denied with its flags and hash;
/// the author requests review; only a ScriptApprover/Admin who isn't the author can approve; then
/// that exact code runs.
/// </summary>
public sealed class ScriptApprovalApiTests : IClassFixture<ScriptApprovalApiTests.AuthedApiFactory>
{
    private const string DevKey = "dev-key-92";
    private const string ApproverKey = "approver-key-92";
    private const string DevApproverKey = "dev-approver-key-92";
    private const string ViewerKey = "viewer-key-92";

    private readonly AuthedApiFactory factory;

    public ScriptApprovalApiTests(AuthedApiFactory factory) => this.factory = factory;

    [Fact]
    public async Task FlaggedScript_IsDenied_ThenRunsOnceAnotherPersonApproves()
    {
        var dev = this.ClientFor(DevKey);
        var code = $"return System.IO.File.Exists(\"/nonexistent-{Guid.NewGuid():N}\");";

        // 1) Denied, with the danger explained and a hash to request review with.
        var denied = await RunCSharp(dev, code);
        denied.GetProperty("success").GetBoolean().Should().BeFalse();
        var flag = denied.GetProperty("securityFlags")[0];
        flag.GetProperty("category").GetString().Should().Be("FileSystem");
        flag.GetProperty("danger").GetString().Should().NotBeNullOrWhiteSpace();
        var hash = denied.GetProperty("codeHash").GetString()!;

        // 2) The author requests review.
        var requested = await dev.PostAsJsonAsync($"/api/v1/script-approvals/{hash}/request", new { note = "checks a marker file" });
        requested.StatusCode.Should().Be(HttpStatusCode.OK);
        (await requested.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString().Should().Be("Requested");

        // 3) A Developer can't approve.
        (await dev.PostAsJsonAsync($"/api/v1/script-approvals/{hash}/approve", new { note = "lgtm" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 4) An approver sees it in the queue and approves it.
        var approver = this.ClientFor(ApproverKey);
        var queue = await approver.GetFromJsonAsync<JsonElement>("/api/v1/script-approvals?status=requested");
        queue.EnumerateArray().Should().Contain(r => r.GetProperty("codeHash").GetString() == hash);
        var approved = await approver.PostAsJsonAsync($"/api/v1/script-approvals/{hash}/approve", new { note = "read-only check" });
        approved.StatusCode.Should().Be(HttpStatusCode.OK);
        var record = await approved.Content.ReadFromJsonAsync<JsonElement>();
        record.GetProperty("decidedBy").GetString().Should().Be("approver-user");

        // 5) That exact code now runs.
        var ran = await RunCSharp(dev, code);
        ran.GetProperty("success").GetBoolean().Should().BeTrue(ran.ToString());
    }

    [Fact]
    public async Task AuthorWhoIsAlsoAnApprover_CannotApproveTheirOwnRequest()
    {
        var client = this.ClientFor(DevApproverKey);
        var denied = await RunCSharp(client, $"return System.Environment.GetEnvironmentVariable(\"X{Guid.NewGuid():N}\");");
        var hash = denied.GetProperty("codeHash").GetString()!;
        await client.PostAsJsonAsync($"/api/v1/script-approvals/{hash}/request", new { note = (string?)null });

        var response = await client.PostAsJsonAsync($"/api/v1/script-approvals/{hash}/approve", new { note = "self" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain("someone else");
    }

    [Fact]
    public async Task Viewer_CanReadCategories_ButCannotRequestOrApprove()
    {
        var viewer = this.ClientFor(ViewerKey);

        var categories = await viewer.GetFromJsonAsync<JsonElement>("/api/v1/script-approvals/categories");
        categories.EnumerateArray().Should().Contain(c => c.GetProperty("category").GetString() == "Reflection"
            && !string.IsNullOrWhiteSpace(c.GetProperty("danger").GetString()));

        (await viewer.PostAsJsonAsync("/api/v1/script-approvals/abc/request", new { note = "x" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await viewer.PostAsJsonAsync("/api/v1/script-approvals/abc/approve", new { note = "x" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Approving_UnknownHash_Is404_AndUnrequestedCandidate_Is409()
    {
        var approver = this.ClientFor(ApproverKey);
        (await approver.PostAsJsonAsync("/api/v1/script-approvals/does-not-exist/approve", new { note = "x" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        var denied = await RunCSharp(this.ClientFor(DevKey), $"return typeof(string).GetMethods().Length + {Random.Shared.Next()};");
        var hash = denied.GetProperty("codeHash").GetString()!;
        (await approver.PostAsJsonAsync($"/api/v1/script-approvals/{hash}/approve", new { note = "x" }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, because: "nobody requested review of this candidate yet");
    }

    private static async Task<JsonElement> RunCSharp(HttpClient client, string code)
    {
        var response = await client.PostAsJsonAsync("/api/v1/scripts/test", new { language = "csharp", code });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private HttpClient ClientFor(string apiKey)
    {
        var client = this.factory.CreateClient();
        client.DefaultRequestHeaders.Add(AuthConstants.ApiKeyHeader, apiKey);
        return client;
    }

    /// <summary>SQLite-backed API with auth on and one key per role.</summary>
    public sealed class AuthedApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Persistence:Provider"] = "sqlite",
                    ["Persistence:ConnectionString"] = Workflow.Tests.TestSqlite.InMemory(),
                    ["Api:Auth:Require"] = "true",

                    ["Api:Auth:ApiKeys:0:KeyHash"] = ApiKeyHasher.Hash(DevKey),
                    ["Api:Auth:ApiKeys:0:CallerId"] = "developer-user",
                    ["Api:Auth:ApiKeys:0:Roles:0"] = AuthConstants.DeveloperRole,

                    ["Api:Auth:ApiKeys:1:KeyHash"] = ApiKeyHasher.Hash(ApproverKey),
                    ["Api:Auth:ApiKeys:1:CallerId"] = "approver-user",
                    ["Api:Auth:ApiKeys:1:Roles:0"] = AuthConstants.ScriptApproverRole,

                    ["Api:Auth:ApiKeys:2:KeyHash"] = ApiKeyHasher.Hash(DevApproverKey),
                    ["Api:Auth:ApiKeys:2:CallerId"] = "dev-approver-user",
                    ["Api:Auth:ApiKeys:2:Roles:0"] = AuthConstants.DeveloperRole,
                    ["Api:Auth:ApiKeys:2:Roles:1"] = AuthConstants.ScriptApproverRole,

                    ["Api:Auth:ApiKeys:3:KeyHash"] = ApiKeyHasher.Hash(ViewerKey),
                    ["Api:Auth:ApiKeys:3:CallerId"] = "viewer-user",
                    ["Api:Auth:ApiKeys:3:Roles:0"] = AuthConstants.ViewerRole,
                });
            });
        }
    }
}
