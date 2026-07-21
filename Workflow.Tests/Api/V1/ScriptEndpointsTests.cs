// <copyright file="ScriptEndpointsTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Api.V1;

using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

/// <summary>
/// 🧪🌐 Phase 3.1.6 — Integration tests for the /api/v1/scripts endpoints~ ✨.
/// </summary>
public sealed class ScriptEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public ScriptEndpointsTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory;
    }

    [Theory]
    [InlineData("javascript", "return input.a + input.b;")]
    [InlineData("lua", "return input.a + input.b")]
    [InlineData("csharp", "return Convert.ToInt32(input[\"a\"]) + Convert.ToInt32(input[\"b\"]);")]
    public async Task Test_EachLanguage_ReturnsResult(string language, string code)
    {
        var client = this.factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/v1/scripts/test", new
        {
            language,
            code,
            inputs = new { a = 4, b = 6 },
        });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("success").GetBoolean().Should().BeTrue(body.GetProperty("error").ToString());
        body.GetProperty("result").GetInt32().Should().Be(10);
    }

    [Fact]
    public async Task Test_ReturnsLogs()
    {
        var client = this.factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/v1/scripts/test", new
        {
            language = "javascript",
            code = "workflow.logInfo('hi'); return 1;",
        });

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var logs = body.GetProperty("logs").EnumerateArray().ToList();
        logs.Should().Contain(l => l.GetProperty("message").GetString() == "hi");
    }

    [Fact]
    public async Task Test_UnknownLanguage_422()
    {
        var client = this.factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/v1/scripts/test", new { language = "cobol", code = "x" });
        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Test_EmptyCode_422()
    {
        var client = this.factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/v1/scripts/test", new { language = "javascript", code = "" });
        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Test_ScriptError_200WithSuccessFalse()
    {
        var client = this.factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/v1/scripts/test", new
        {
            language = "javascript",
            code = "throw new Error('kaboom');",
        });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("success").GetBoolean().Should().BeFalse();
        body.GetProperty("error").GetString().Should().Contain("kaboom");
    }

    [Fact]
    public async Task Languages_ListsRegistered()
    {
        var client = this.factory.CreateClient();
        var langs = await client.GetFromJsonAsync<List<JsonElement>>("/api/v1/scripts/languages");

        var ids = langs!.Select(l => l.GetProperty("languageId").GetString()).ToList();
        ids.Should().Contain(new[] { "javascript", "lua", "csharp" });
    }

    [Fact]
    public async Task Libraries_CrudRoundTrip()
    {
        var client = this.factory.CreateClient();
        var id = "lib-" + System.Guid.NewGuid().ToString("N");

        var put = await client.PutAsJsonAsync($"/api/v1/scripts/libraries/{id}", new
        {
            libraryId = id,
            name = "Math",
            language = "javascript",
            code = "return { add: function(a,b){return a+b;} };",
            exportedFunctions = new[] { "add" },
            dependencies = System.Array.Empty<string>(),
        });
        put.StatusCode.Should().Be(HttpStatusCode.OK);

        var get = await client.GetAsync($"/api/v1/scripts/libraries/{id}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);

        var del = await client.DeleteAsync($"/api/v1/scripts/libraries/{id}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await client.GetAsync($"/api/v1/scripts/libraries/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Test_WithLibrary_ImportsWork()
    {
        var client = this.factory.CreateClient();
        var id = "mathlib-" + System.Guid.NewGuid().ToString("N");

        await client.PutAsJsonAsync($"/api/v1/scripts/libraries/{id}", new
        {
            libraryId = id,
            name = "Math",
            language = "javascript",
            code = "return { triple: function(x){ return x * 3; } };",
            exportedFunctions = new[] { "triple" },
            dependencies = System.Array.Empty<string>(),
        });

        var resp = await client.PostAsJsonAsync("/api/v1/scripts/test", new
        {
            language = "javascript",
            code = $"var m = workflow.require('{id}'); return m.triple(4);",
            libraries = new[] { id },
        });

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("success").GetBoolean().Should().BeTrue(body.GetProperty("error").ToString());
        body.GetProperty("result").GetInt32().Should().Be(12);
    }
}
