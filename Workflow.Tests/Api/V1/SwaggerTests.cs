// <copyright file="SwaggerTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Api.V1;

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
using Xunit;

/// <summary>
/// 📖 Phase 2.7.8 — Swagger/OpenAPI document generation + security scheme tests~ ✨.
/// </summary>
public sealed class SwaggerTests
{
    [Fact]
    public async Task Swagger_GeneratesV1Document()
    {
        using var factory = new RateLimitTests.ConfiguredFactory(new Dictionary<string, string?>());
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/swagger/v1/swagger.json");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>();
        doc.TryGetProperty("openapi", out _).Should().BeTrue();
        doc.GetProperty("info").GetProperty("title").GetString().Should().Contain("DotFlow");
        doc.GetProperty("paths").EnumerateObject().Should().NotBeEmpty();
    }

    [Fact]
    public async Task Swagger_IncludesSecuritySchemes()
    {
        using var factory = new RateLimitTests.ConfiguredFactory(new Dictionary<string, string?>());
        var client = factory.CreateClient();

        var doc = await client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");
        var schemes = doc.GetProperty("components").GetProperty("securitySchemes");
        schemes.TryGetProperty("ApiKey", out _).Should().BeTrue();
        schemes.TryGetProperty("Bearer", out _).Should().BeTrue();
    }
}

/// <summary>
/// 🚦 Phase 2.7.8 — Rate-limiting seam tests (enabled via config)~ ✨.
/// </summary>
public sealed class RateLimitTests
{
    [Fact]
    public async Task RateLimit_WhenEnabled_Returns429OverLimit()
    {
        using var factory = new ConfiguredFactory(new Dictionary<string, string?>
        {
            ["Api:RateLimit:Enabled"] = "true",
            ["Api:RateLimit:PermitLimit"] = "2",
            ["Api:RateLimit:WindowSeconds"] = "60",
        });
        var client = factory.CreateClient();

        var first = await client.GetAsync("/api/v1/health");
        var second = await client.GetAsync("/api/v1/health");
        var third = await client.GetAsync("/api/v1/health");

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        third.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        third.Headers.Contains("Retry-After").Should().BeTrue();
    }

    [Fact]
    public async Task RateLimit_WhenDisabled_NoLimit()
    {
        using var factory = new ConfiguredFactory(new Dictionary<string, string?>());
        var client = factory.CreateClient();

        for (var i = 0; i < 5; i++)
        {
            (await client.GetAsync("/api/v1/health")).StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    public sealed class ConfiguredFactory : WebApplicationFactory<Program>
    {
        private readonly Dictionary<string, string?> settings;

        public ConfiguredFactory(Dictionary<string, string?> settings)
        {
            this.settings = settings;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(this.settings));
        }
    }
}
