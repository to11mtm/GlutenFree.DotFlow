// <copyright file="ModuleEndpointsTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Api.V1;

using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Workflow.Api.Contracts.Modules;
using Xunit;

/// <summary>
/// 📦 Phase 2.7.3 — API integration tests for read-only module discovery endpoints~ ✨.
/// The default (no-persistence) host still registers <c>IModuleRegistry</c>, so the plain
/// factory is enough here.
/// </summary>
public sealed class ModuleEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public ModuleEndpointsTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task List_ReturnsAllRegisteredModules()
    {
        var client = this.factory.CreateClient();
        var modules = await client.GetFromJsonAsync<List<ModuleSummaryDto>>("/api/v1/modules");

        modules.Should().NotBeNull();
        modules!.Should().NotBeEmpty();
        modules.Select(m => m.Id).Should().Contain("builtin.http.request");
    }

    [Fact]
    public async Task List_FilterByCategory_ReturnsOnlyThatCategory()
    {
        var client = this.factory.CreateClient();
        var all = await client.GetFromJsonAsync<List<ModuleSummaryDto>>("/api/v1/modules");
        var category = all!.First().Category;

        var filtered = await client.GetFromJsonAsync<List<ModuleSummaryDto>>($"/api/v1/modules?category={category}");

        filtered.Should().NotBeEmpty();
        filtered!.Should().OnlyContain(m => m.Category == category);
    }

    [Fact]
    public async Task List_Search_MatchesQuery()
    {
        var client = this.factory.CreateClient();
        var results = await client.GetFromJsonAsync<List<ModuleSummaryDto>>("/api/v1/modules?q=http");

        results.Should().NotBeEmpty();
        results!.Should().Contain(m => m.Id.Contains("http"));
    }

    [Fact]
    public async Task List_GroupByCategory_ReturnsDictionary()
    {
        var client = this.factory.CreateClient();
        var grouped = await client.GetFromJsonAsync<Dictionary<string, List<ModuleSummaryDto>>>(
            "/api/v1/modules?groupByCategory=true");

        grouped.Should().NotBeNull();
        grouped!.Should().NotBeEmpty();
        grouped.Values.SelectMany(v => v).Should().NotBeEmpty();
    }

    [Fact]
    public async Task Get_KnownModule_ReturnsSchema()
    {
        var client = this.factory.CreateClient();
        var details = await client.GetFromJsonAsync<ModuleDetailsDto>("/api/v1/modules/builtin.http.request");

        details.Should().NotBeNull();
        details!.Id.Should().Be("builtin.http.request");
        details.Schema.Should().NotBeNull();
        details.Version.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Get_UnknownModule_Returns404()
    {
        var client = this.factory.CreateClient();
        var resp = await client.GetAsync("/api/v1/modules/nope.not.here");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_ModuleWithProperties_SerializesTypeAndEditor()
    {
        var client = this.factory.CreateClient();
        var resp = await client.GetAsync("/api/v1/modules/builtin.http.request");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();

        var schema = body.GetProperty("schema");
        schema.TryGetProperty("properties", out var props).Should().BeTrue();
        foreach (var prop in props.EnumerateArray())
        {
            prop.GetProperty("editorType").GetString().Should().NotBeNullOrEmpty();
        }
    }
}
