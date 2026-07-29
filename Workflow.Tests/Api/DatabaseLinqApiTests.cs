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

    // ── 📚 Linq Studio L0 — catalog management ─────────────────────────────────────

    [Fact]
    public async Task Api_Catalog_UpsertListRemove_RoundTrips()
    {
        var client = this.factory.CreateClient();
        var conn = $"studio-test-{Guid.NewGuid():N}";

        // Manual upsert with a columns grid.
        var upsert = await client.PutAsJsonAsync($"/api/database/catalog/{conn}/Users", new
        {
            schema = "public",
            columns = new[]
            {
                new { name = "id", dataType = "integer", nullable = false },
                new { name = "email", dataType = "text", nullable = true },
            },
            clrTypeName = (string?)null,
            assemblyName = (string?)null,
        });
        upsert.StatusCode.Should().Be(HttpStatusCode.OK);

        // List returns it with columns.
        var list = await client.GetFromJsonAsync<JsonElement>($"/api/database/catalog/{conn}");
        list.GetArrayLength().Should().Be(1);
        var table = list[0];
        table.GetProperty("tableName").GetString().Should().Be("Users");
        table.GetProperty("schema").GetString().Should().Be("public");
        table.GetProperty("columns").GetArrayLength().Should().Be(2);

        // Remove → 204, then the list is empty and a re-remove 404s.
        (await client.DeleteAsync($"/api/database/catalog/{conn}/Users")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<JsonElement>($"/api/database/catalog/{conn}")).GetArrayLength().Should().Be(0);
        (await client.DeleteAsync($"/api/database/catalog/{conn}/Users")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Api_Catalog_Upsert_NoColumnsNoClrType_Returns400()
    {
        var client = this.factory.CreateClient();
        var resp = await client.PutAsJsonAsync($"/api/database/catalog/c-{Guid.NewGuid():N}/Bad", new
        {
            schema = (string?)null,
            columns = (object?)null,
            clrTypeName = (string?)null,
            assemblyName = (string?)null,
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Api_Catalog_ManualTable_UsableByValidate()
    {
        var client = this.factory.CreateClient();
        var conn = $"studio-vali-{Guid.NewGuid():N}";

        await client.PutAsJsonAsync($"/api/database/catalog/{conn}/Orders", new
        {
            schema = (string?)null,
            columns = new[]
            {
                new { name = "id", dataType = "integer", nullable = false },
                new { name = "total", dataType = "numeric", nullable = false },
            },
            clrTypeName = (string?)null,
            assemblyName = (string?)null,
        });

        // Validate resolving tables FROM THE CATALOG (no inline tables) — the Studio's path.
        var resp = await client.PostAsJsonAsync("/api/database/linq/validate", new
        {
            definitionId = "def1",
            nodeId = "node1",
            userCode = "return db.Orders.Where(o => o.total > 0).ToList();",
            connectionId = conn,
            tableNames = new[] { "Orders" },
        });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("success").GetBoolean().Should().BeTrue();
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

