// <copyright file="WorkflowEndpointsTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Api.V1;

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using LanguageExt;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Workflow.Core.Models;
using Workflow.Persistence.Sqlite.Serialization;
using Xunit;

/// <summary>
/// 📋 Phase 2.7.1 — API integration tests for workflow CRUD over an in-memory SQLite provider~ ✨.
/// </summary>
public sealed class WorkflowEndpointsTests : IClassFixture<WorkflowEndpointsTests.SqliteApiFactory>
{
    private static readonly JsonSerializerOptions WfJson = LanguageExtJsonConverters.CreateOptions();
    private readonly SqliteApiFactory factory;

    public WorkflowEndpointsTests(SqliteApiFactory factory)
    {
        this.factory = factory;
    }

    private static WorkflowDefinition MinimalWorkflow(string name)
        => new(
            Id: Guid.Empty,
            Name: name,
            Description: "test",
            Version: new Version(1, 0, 0),
            Nodes: Arr.create(new NodeDefinition("n1", "builtin.passthrough", "N1", HashMap<string, JsonElement>.Empty)),
            Connections: Arr<ConnectionDefinition>.Empty,
            Variables: HashMap<string, VariableDefinition>.Empty,
            Tags: Arr.create("etl", "demo"));

    private static StringContent AsJson(WorkflowDefinition def)
        => new(JsonSerializer.Serialize(def, WfJson), Encoding.UTF8, "application/json");

    private async Task<Guid> CreateAsync(HttpClient client, string name)
    {
        var resp = await client.PostAsync("/api/v1/workflows", AsJson(MinimalWorkflow(name)));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Create_Then_Get_RoundTrips()
    {
        var client = this.factory.CreateClient();
        var id = await this.CreateAsync(client, "wf-" + Guid.NewGuid().ToString("N"));

        var get = await client.GetAsync($"/api/v1/workflows/{id}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await get.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetGuid().Should().Be(id);
        body.GetProperty("nodes").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Create_InvalidDefinition_Returns422()
    {
        var client = this.factory.CreateClient();
        var invalid = MinimalWorkflow("bad") with
        {
            Nodes = Arr.create(new NodeDefinition("n1", "does.not.exist", "N1", HashMap<string, JsonElement>.Empty)),
        };

        var resp = await client.PostAsync("/api/v1/workflows", AsJson(invalid));
        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Get_UnknownId_Returns404()
    {
        var client = this.factory.CreateClient();
        (await client.GetAsync($"/api/v1/workflows/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task List_ReturnsCreatedWorkflow_WithSupportedVersionsHeader()
    {
        var client = this.factory.CreateClient();
        var name = "list-" + Guid.NewGuid().ToString("N");
        await this.CreateAsync(client, name);

        var resp = await client.GetAsync("/api/v1/workflows?pageSize=200");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Headers.Contains("api-supported-versions").Should().BeTrue();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("totalCount").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Update_Existing_Succeeds()
    {
        var client = this.factory.CreateClient();
        var id = await this.CreateAsync(client, "upd-" + Guid.NewGuid().ToString("N"));

        var updated = MinimalWorkflow("renamed") with { Id = id, Version = new Version(1, 1, 0), Description = "changed" };
        var resp = await client.PutAsync($"/api/v1/workflows/{id}", AsJson(updated));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("description").GetString().Should().Be("changed");
    }

    [Fact]
    public async Task Update_Unknown_Returns404()
    {
        var client = this.factory.CreateClient();
        var resp = await client.PutAsync($"/api/v1/workflows/{Guid.NewGuid()}", AsJson(MinimalWorkflow("x")));
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Update_OlderVersion_Returns409()
    {
        var client = this.factory.CreateClient();
        var id = await this.CreateAsync(client, "ver-" + Guid.NewGuid().ToString("N"));

        // stored is v1.0.0; send v0.9 → conflict
        var older = MinimalWorkflow("older") with { Id = id, Version = new Version(0, 9, 0) };
        var resp = await client.PutAsync($"/api/v1/workflows/{id}", AsJson(older));
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Delete_SoftDeletes_ThenGet404_ThenRestore()
    {
        var client = this.factory.CreateClient();
        var id = await this.CreateAsync(client, "del-" + Guid.NewGuid().ToString("N"));

        (await client.DeleteAsync($"/api/v1/workflows/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync($"/api/v1/workflows/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await client.PostAsync($"/api/v1/workflows/{id}/restore", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync($"/api/v1/workflows/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Delete_Unknown_Returns404()
    {
        var client = this.factory.CreateClient();
        (await client.DeleteAsync($"/api/v1/workflows/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Validate_ValidWorkflow_ReturnsValid()
    {
        var client = this.factory.CreateClient();
        var resp = await client.PostAsync("/api/v1/workflows/validate", AsJson(MinimalWorkflow("valid-" + Guid.NewGuid().ToString("N"))));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("valid").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Validate_UnknownModule_ReturnsIssues_WithoutPersisting()
    {
        var client = this.factory.CreateClient();
        var broken = MinimalWorkflow("broken") with
        {
            Nodes = Arr.create(new NodeDefinition("n1", "builtin.does.not.exist", "N1", HashMap<string, JsonElement>.Empty)),
        };

        var resp = await client.PostAsync("/api/v1/workflows/validate", AsJson(broken));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("valid").GetBoolean().Should().BeFalse();
        body.GetProperty("issues").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Validate_DoesNotCreateWorkflow()
    {
        var client = this.factory.CreateClient();
        var name = "novalpersist-" + Guid.NewGuid().ToString("N");
        await client.PostAsync("/api/v1/workflows/validate", AsJson(MinimalWorkflow(name)));

        // The validate call must not have persisted anything.
        var list = await client.GetFromJsonAsync<JsonElement>($"/api/v1/workflows?name={name}");
        list.GetProperty("totalCount").GetInt32().Should().Be(0);
    }

    /// <summary>
    /// A <see cref="WebApplicationFactory{TProgram}"/> configured with an in-memory SQLite provider~ 🗄️.
    /// </summary>
    public sealed class SqliteApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
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
