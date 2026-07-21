// <copyright file="VariableEndpointsTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Api.V1;

using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

/// <summary>
/// 🔧 Phase 2.7.4 — API integration tests for scoped, versioned variable management over SQLite~ ✨.
/// </summary>
public sealed class VariableEndpointsTests : IClassFixture<VariableEndpointsTests.SqliteApiFactory>
{
    private readonly SqliteApiFactory factory;

    public VariableEndpointsTests(SqliteApiFactory factory)
    {
        this.factory = factory;
    }

    private static JsonContent Body(object? value) => JsonContent.Create(new { value });

    [Fact]
    public async Task Set_Then_Get_RoundTrips()
    {
        var client = this.factory.CreateClient();
        var name = "v-" + System.Guid.NewGuid().ToString("N");

        var put = await client.PutAsync($"/api/v1/variables/{name}?scope=global", Body("hello"));
        put.StatusCode.Should().Be(HttpStatusCode.OK);

        var get = await client.GetAsync($"/api/v1/variables/{name}?scope=global");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await get.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("value").GetString().Should().Be("hello");
        body.GetProperty("version").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Set_NullValue_PersistsAsPresentNull()
    {
        var client = this.factory.CreateClient();
        var name = "v-" + System.Guid.NewGuid().ToString("N");

        var put = await client.PutAsync($"/api/v1/variables/{name}?scope=global", Body(null));
        put.StatusCode.Should().Be(HttpStatusCode.OK);

        var get = await client.GetAsync($"/api/v1/variables/{name}?scope=global");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await get.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("value").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Get_Unknown_Returns404()
    {
        var client = this.factory.CreateClient();
        var resp = await client.GetAsync($"/api/v1/variables/nope-{System.Guid.NewGuid():N}?scope=global");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Set_Twice_IncrementsVersion_And_GetSpecificVersion_Works()
    {
        var client = this.factory.CreateClient();
        var name = "v-" + System.Guid.NewGuid().ToString("N");

        await client.PutAsync($"/api/v1/variables/{name}?scope=global", Body("one"));
        var second = await client.PutAsync($"/api/v1/variables/{name}?scope=global", Body("two"));
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();
        secondBody.GetProperty("version").GetInt32().Should().Be(2);

        var v1 = await client.GetAsync($"/api/v1/variables/{name}?scope=global&version=1");
        var v1Body = await v1.Content.ReadFromJsonAsync<JsonElement>();
        v1Body.GetProperty("value").GetString().Should().Be("one");
    }

    [Fact]
    public async Task History_ReturnsAllVersions()
    {
        var client = this.factory.CreateClient();
        var name = "v-" + System.Guid.NewGuid().ToString("N");

        await client.PutAsync($"/api/v1/variables/{name}?scope=global", Body("a"));
        await client.PutAsync($"/api/v1/variables/{name}?scope=global", Body("b"));

        var hist = await client.GetFromJsonAsync<List<JsonElement>>($"/api/v1/variables/{name}/history?scope=global");
        hist.Should().HaveCount(2);
    }

    [Fact]
    public async Task Delete_RemovesVariableAndHistory()
    {
        var client = this.factory.CreateClient();
        var name = "v-" + System.Guid.NewGuid().ToString("N");
        await client.PutAsync($"/api/v1/variables/{name}?scope=global", Body("x"));

        var del = await client.DeleteAsync($"/api/v1/variables/{name}?scope=global");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var get = await client.GetAsync($"/api/v1/variables/{name}?scope=global");
        get.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_Unknown_Returns404()
    {
        var client = this.factory.CreateClient();
        var resp = await client.DeleteAsync($"/api/v1/variables/nope-{System.Guid.NewGuid():N}?scope=global");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task List_ByScope_ReturnsScopedVariables()
    {
        var client = this.factory.CreateClient();
        var wfId = System.Guid.NewGuid();
        var name = "v-" + System.Guid.NewGuid().ToString("N");

        await client.PutAsync($"/api/v1/variables/{name}?scope=workflow&scopeId={wfId}", Body("scoped"));

        var list = await client.GetFromJsonAsync<Dictionary<string, JsonElement>>(
            $"/api/v1/variables?scope=workflow&scopeId={wfId}");
        list.Should().ContainKey(name);
    }

    [Fact]
    public async Task WorkflowScope_MissingScopeId_Returns400()
    {
        var client = this.factory.CreateClient();
        var resp = await client.GetAsync("/api/v1/variables?scope=workflow");
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>A factory with SQLite persistence (in-memory)~ 🗄️.</summary>
    public sealed class SqliteApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Persistence:Provider"] = "sqlite",
                    ["Persistence:ConnectionString"] = ":memory:",
                });
            });
        }
    }
}
