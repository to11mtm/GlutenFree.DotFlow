// <copyright file="DatabaseLinqApiTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Api;

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

/// <summary>
/// 🧬 Phase 2.4.b.5 — API integration tests for the typed-linq validate/preview/compile + catalog
/// import endpoints (via <see cref="WebApplicationFactory{TProgram}"/>, Docker-free)~ ✨💖.
/// </summary>
public sealed class DatabaseLinqApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public DatabaseLinqApiTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Api_Validate_ValidCode_ReturnsSuccess()
    {
        var client = this.factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/database/linq/validate", Body("return db.Orders.ToList();"));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("success").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Api_Validate_InvalidCode_ReturnsDiagnosticsWithLineInfo()
    {
        var client = this.factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/database/linq/validate", Body("return db.Ordrs.ToList();"));

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("success").GetBoolean().Should().BeFalse();
        var errors = body.GetProperty("errors");
        errors.GetArrayLength().Should().BeGreaterThan(0);

        var hasLineInfo = false;
        foreach (var e in errors.EnumerateArray())
        {
            if (e.GetProperty("id").GetString() == "CS1061")
            {
                e.GetProperty("line").GetInt32().Should().BeGreaterThan(0, "diagnostics carry line info for UI squigglies~");
                hasLineInfo = true;
            }
        }

        hasLineInfo.Should().BeTrue();
    }

    [Fact]
    public async Task Api_Preview_ReturnsSampleResult()
    {
        var client = this.factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/database/linq/preview", Body("return db.Orders.ToList();"));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("success").GetBoolean().Should().BeTrue();
        body.GetProperty("rowCount").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task Api_Preview_ForbiddenApiInCode_ReturnsRejectionDiagnostic()
    {
        var client = this.factory.CreateClient();
        var resp = await client.PostAsJsonAsync(
            "/api/database/linq/preview",
            Body("System.IO.File.Delete(\"/etc/passwd\"); return null;"));

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("success").GetBoolean().Should().BeFalse();

        var found = false;
        foreach (var d in body.GetProperty("diagnostics").EnumerateArray())
        {
            if (d.GetProperty("id").GetString() == "WFLINQ100")
            {
                found = true;
            }
        }

        found.Should().BeTrue("forbidden API usage is rejected before execution~ 🚫");
    }

    [Fact]
    public async Task Api_Compile_WithTrustedAuthor_ReturnsBlobKey()
    {
        var client = this.factory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/database/linq/compile")
        {
            Content = JsonContent.Create(Body("return db.Orders.ToList();")),
        };
        req.Headers.Add("X-Trusted-Author", "true");

        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("compiledAssemblyKey").GetString().Should().StartWith("compiled-modules/");
    }

    [Fact]
    public async Task Api_Compile_WithoutTrustedAuthor_Returns403()
    {
        var client = this.factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/database/linq/compile", Body("return db.Orders.ToList();"));

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden, "compile is trusted-author gated (D17)~ 🔐");
    }

    [Fact]
    public async Task Api_CatalogImport_UnknownConnection_Returns404()
    {
        var client = this.factory.CreateClient();
        var resp = await client.PostAsync($"/api/database/catalog/nope-{Guid.NewGuid():N}/import", content: null);

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static object Body(string userCode) => new
    {
        definitionId = "def1",
        nodeId = "node1",
        userCode,
        tables = new[]
        {
            new
            {
                tableName = "Orders",
                schema = (string?)null,
                columns = new[]
                {
                    new { name = "id", dataType = "integer", nullable = false },
                    new { name = "name", dataType = "text", nullable = true },
                    new { name = "total", dataType = "numeric", nullable = false },
                },
                clrTypeName = (string?)null,
                assemblyName = (string?)null,
            },
        },
    };
}

