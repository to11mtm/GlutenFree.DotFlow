// <copyright file="TransformScriptApiTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Api;

using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

/// <summary>
/// 🌟 Phase 2.6.b.2 — API tests for the transform-script validate/preview/compile endpoints~ ✨.
/// </summary>
public sealed class TransformScriptApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public TransformScriptApiTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Validate_CleanBody_ReturnsSuccess()
    {
        var client = this.factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/transform/script/validate", new { code = "return rows.Count;" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<ValidateResp>();
        body!.success.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_ForbiddenApi_ReturnsDiagnostics()
    {
        var client = this.factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/transform/script/validate", new { code = "System.IO.File.Delete(\"x\"); return null;" });
        var body = await resp.Content.ReadFromJsonAsync<ValidateResp>();
        body!.success.Should().BeFalse();
        body.diagnostics.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Preview_RunsAgainstSampleRows()
    {
        var client = this.factory.CreateClient();
        var request = new
        {
            code = "return rows.Count + (int)(long)inputs[\"bonus\"];",
            sampleRows = new[] { new Dictionary<string, object?> { ["x"] = 1 }, new Dictionary<string, object?> { ["x"] = 2 } },
            inputs = new Dictionary<string, object?> { ["bonus"] = 10 },
        };
        var resp = await client.PostAsJsonAsync("/api/transform/script/preview", request);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<PreviewResp>();
        body!.success.Should().BeTrue(string.Join("; ", (body.diagnostics ?? new()).ConvertAll(d => d.id + ":" + d.message)));
    }

    [Fact]
    public async Task Preview_CompileError_ReturnsDiagnosticsNotException()
    {
        var client = this.factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/transform/script/preview", new { code = "return this is bad;", sampleRows = new object[0] });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<PreviewResp>();
        body!.success.Should().BeFalse();
    }

    [Fact]
    public async Task Compile_WithoutTrustedAuthor_Forbidden()
    {
        var client = this.factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/transform/script/compile", new { code = "return rows.Count;" });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Compile_WithTrustedAuthor_ReturnsKey()
    {
        var client = this.factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/transform/script/compile")
        {
            Content = JsonContent.Create(new { code = "return rows.Count;", definitionId = "d1", nodeId = "n1" }),
        };
        request.Headers.Add("X-Trusted-Author", "true");

        var resp = await client.SendAsync(request);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<CompileResp>();
        body!.compiledAssemblyKey.Should().Contain("compiled-modules/transform/d1/n1/");
    }

    [Fact]
    public async Task Compile_ForbiddenApi_BadRequest()
    {
        var client = this.factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/transform/script/compile")
        {
            Content = JsonContent.Create(new { code = "System.IO.File.Delete(\"x\"); return null;" }),
        };
        request.Headers.Add("X-Trusted-Author", "true");

        var resp = await client.SendAsync(request);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private sealed record ValidateResp(bool success, List<DiagResp> diagnostics);

    private sealed record PreviewResp(bool success, object? result, long durationMs, List<DiagResp> diagnostics);

    private sealed record CompileResp(string compiledAssemblyKey);

    private sealed record DiagResp(string id, string severity, string message);
}
