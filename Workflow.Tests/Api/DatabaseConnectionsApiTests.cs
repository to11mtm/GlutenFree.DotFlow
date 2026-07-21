// <copyright file="DatabaseConnectionsApiTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Api;

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

/// <summary>
/// 📇 Phase 2.4.a.5 — API integration tests for named database-connection CRUD~ ✨💖.
/// Uses <see cref="WebApplicationFactory{TProgram}"/> (Docker-free, full DI). The default app has
/// no persistence provider configured, so the in-memory connection registry is used~ 🎀.
/// </summary>
public sealed class DatabaseConnectionsApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public DatabaseConnectionsApiTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Api_PostConnection_RegistersAndIsRetrievable()
    {
        var client = this.factory.CreateClient();
        var id = $"orders-{Guid.NewGuid():N}";

        var post = await client.PostAsJsonAsync("/api/database/connections", new
        {
            id,
            providerKey = "postgres",
            connectionString = "Host=localhost;Database=orders",
            displayName = "Orders",
        });

        post.StatusCode.Should().Be(HttpStatusCode.OK);

        var get = await client.GetAsync($"/api/database/connections/{id}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await get.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetString().Should().Be(id);
        body.GetProperty("providerKey").GetString().Should().Be("postgres");
    }

    [Fact]
    public async Task Api_GetConnection_DefaultIsMasked()
    {
        var client = this.factory.CreateClient();
        var id = $"masked-{Guid.NewGuid():N}";

        await client.PostAsJsonAsync("/api/database/connections", new
        {
            id,
            providerKey = "sqlite",
            connectionString = "Data Source=secret.db",
        });

        var body = await (await client.GetAsync($"/api/database/connections/{id}")).Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("connectionString").GetString().Should().Be("***", "connection strings are masked by default~ 🔒");
    }

    [Fact]
    public async Task Api_GetConnection_RevealReturnsFullConnectionString()
    {
        var client = this.factory.CreateClient();
        var id = $"reveal-{Guid.NewGuid():N}";

        await client.PostAsJsonAsync("/api/database/connections", new
        {
            id,
            providerKey = "sqlite",
            connectionString = "Data Source=reveal.db",
        });

        var body = await (await client.GetAsync($"/api/database/connections/{id}?reveal=true")).Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("connectionString").GetString().Should().Be("Data Source=reveal.db", "?reveal=true returns plaintext~ 🔓");
    }

    [Fact]
    public async Task Api_ListConnections_ReturnsMasked()
    {
        var client = this.factory.CreateClient();
        var id = $"list-{Guid.NewGuid():N}";

        await client.PostAsJsonAsync("/api/database/connections", new
        {
            id,
            providerKey = "sqlite",
            connectionString = "Data Source=list.db",
        });

        var list = await (await client.GetAsync("/api/database/connections")).Content.ReadFromJsonAsync<JsonElement>();
        var found = false;
        foreach (var el in list.EnumerateArray())
        {
            if (el.GetProperty("id").GetString() == id)
            {
                el.GetProperty("connectionString").GetString().Should().Be("***");
                found = true;
            }
        }

        found.Should().BeTrue("the posted connection should appear in the list~ 🌸");
    }

    [Fact]
    public async Task Api_DeleteConnection_RemovesFromRegistry()
    {
        var client = this.factory.CreateClient();
        var id = $"del-{Guid.NewGuid():N}";

        await client.PostAsJsonAsync("/api/database/connections", new
        {
            id,
            providerKey = "sqlite",
            connectionString = "Data Source=del.db",
        });

        var del = await client.DeleteAsync($"/api/database/connections/{id}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var get = await client.GetAsync($"/api/database/connections/{id}");
        get.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Api_DeleteUnknownConnection_Returns404()
    {
        var client = this.factory.CreateClient();
        var del = await client.DeleteAsync($"/api/database/connections/nope-{Guid.NewGuid():N}");
        del.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Api_PostConnection_WhenDisableRuntimeCrudTrue_Returns403()
    {
        using var restricted = this.factory.WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Workflow:Database:DisableRuntimeCrud"] = "true",
                })));

        var client = restricted.CreateClient();
        var post = await client.PostAsJsonAsync("/api/database/connections", new
        {
            id = $"blocked-{Guid.NewGuid():N}",
            providerKey = "sqlite",
            connectionString = "Data Source=blocked.db",
        });

        post.StatusCode.Should().Be(HttpStatusCode.Forbidden, "runtime CRUD is opt-out via DisableRuntimeCrud (D4)~ 🔐");
    }
}

