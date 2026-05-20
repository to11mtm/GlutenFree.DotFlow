// <copyright file="HttpTransformationTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.Http;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin.Http;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

/// <summary>
/// 🔀 Phase 2.3.5 — Request/Response Transformation tests for <see cref="HttpRequestModule"/>~ ✨💖.
/// Covers URL templating confirmation, JSONPath extraction, regex extraction, and header extraction~
/// </summary>
/// <remarks>
/// All tests use WireMock.Net for an in-process mock HTTP server (Docker-free)~ 🎀
/// CopilotNote: URL templating is resolved upstream by PropertyBinder before ExecuteAsync sees the URL;
/// the "templating" test here confirms the module accepts a pre-resolved URL happily~ 🧠
/// </remarks>
public sealed class HttpTransformationTests : IDisposable
{
    private readonly HttpRequestModule _module = new();
    private readonly WireMockServer _server;
    private readonly ServiceProvider _services;

    public HttpTransformationTests()
    {
        _server = WireMockServer.Start();
        var sc = new ServiceCollection();
        sc.AddHttpModules();
        _services = sc.BuildServiceProvider();
    }

    public void Dispose()
    {
        _server.Stop();
        _server.Dispose();
        _services.Dispose();
    }

    #region Helpers 🛠️

    /// <summary>Build a <see cref="ModuleExecutionContext"/> with the given properties~ 🔧.</summary>
    private ModuleExecutionContext BuildContext(Dictionary<string, object?> properties)
        => new()
        {
            Inputs = new Dictionary<string, object?>(),
            Properties = properties,
            Variables = new Dictionary<string, object?>(),
            Logger = NullLogger.Instance,
            Services = _services,
            ExecutionId = Guid.NewGuid(),
            NodeId = "transform-node",
        };

    /// <summary>Mount a WireMock stub that returns a JSON body at <paramref name="path"/>~ 🌐.</summary>
    private void StubJson(string path, string jsonBody, int statusCode = 200)
        => _server
            .Given(Request.Create().WithPath(path).UsingAnyMethod())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(statusCode)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(jsonBody));

    /// <summary>Mount a WireMock stub that returns a plain-text body at <paramref name="path"/>~ 📄.</summary>
    private void StubText(string path, string textBody, int statusCode = 200)
        => _server
            .Given(Request.Create().WithPath(path).UsingAnyMethod())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(statusCode)
                    .WithHeader("Content-Type", "text/plain; charset=utf-8")
                    .WithBody(textBody));

    #endregion

    // =========================================================================
    // 🪄 URL templating confirmation
    // =========================================================================

    /// <summary>
    /// Confirms the module accepts and uses a pre-resolved URL (the {{variable}} substitution
    /// happens upstream in PropertyBinder before ExecuteAsync sees the properties)~ 🪄
    /// </summary>
    [Fact]
    public async Task Url_WithDoubleBraceVariable_Resolved()
    {
        // PropertyBinder has already resolved {{userId}} → "42" before ExecuteAsync runs~
        var resolvedUrl = $"{_server.Urls[0]}/users/42";
        StubJson("/users/42", """{"id":42,"name":"Alice"}""");

        var ctx = BuildContext(new Dictionary<string, object?>
        {
            ["url"] = resolvedUrl,
            ["method"] = "GET",
        });

        var result = await _module.ExecuteAsync(ctx, CancellationToken.None);

        result.Success.Should().BeTrue("a resolved URL should succeed~ 🌸");
        result.Outputs["statusCode"].Should().Be(200);
        var body = result.Outputs["body"].Should().BeAssignableTo<IDictionary<string, object?>>().Subject;
        body["name"].Should().Be("Alice");
    }

    // =========================================================================
    // 🎯 JSONPath response extraction
    // =========================================================================

    /// <summary>
    /// Extracts a single scalar field with <c>responseExtract</c> — should unwrap to a plain value
    /// rather than a single-element list~ 🎯
    /// </summary>
    [Fact]
    public async Task JsonPath_ExtractSingleField_PopulatesOutput()
    {
        StubJson("/me", """{"user":{"id":"u-abc","email":"alice@example.com"}}""");

        var ctx = BuildContext(new Dictionary<string, object?>
        {
            ["url"] = $"{_server.Urls[0]}/me",
            ["method"] = "GET",
            ["responseExtract"] = new Dictionary<string, string>
            {
                ["userId"] = "$.user.id",
            },
        });

        var result = await _module.ExecuteAsync(ctx, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Outputs.Should().ContainKey("userId");
        result.Outputs["userId"].Should().Be("u-abc",
            "single scalar match should be unwrapped — not wrapped in a list~ 💖");
    }

    /// <summary>
    /// Extracts a nested field several levels deep with a dot-path expression~ 🎯
    /// </summary>
    [Fact]
    public async Task JsonPath_ExtractNestedField_PopulatesOutput()
    {
        StubJson("/profile",
            """{"data":{"address":{"city":"Tokyo","zip":"100-0001"}}}""");

        var ctx = BuildContext(new Dictionary<string, object?>
        {
            ["url"] = $"{_server.Urls[0]}/profile",
            ["method"] = "GET",
            ["responseExtract"] = new Dictionary<string, string>
            {
                ["city"] = "$.data.address.city",
                ["zip"]  = "$.data.address.zip",
            },
        });

        var result = await _module.ExecuteAsync(ctx, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Outputs["city"].Should().Be("Tokyo");
        result.Outputs["zip"].Should().Be("100-0001");
    }

    /// <summary>
    /// A path pointing to a non-existent key should produce <c>null</c> (not fail the module)
    /// when <c>responseExtractRequired</c> is false (default)~ 🎯
    /// </summary>
    [Fact]
    public async Task JsonPath_MissingPath_OutputIsNull()
    {
        StubJson("/items", """{"items":[]}""");

        var ctx = BuildContext(new Dictionary<string, object?>
        {
            ["url"] = $"{_server.Urls[0]}/items",
            ["method"] = "GET",
            ["responseExtract"] = new Dictionary<string, string>
            {
                ["ghost"] = "$.does.not.exist",
            },
            // responseExtractRequired defaults to false — missing → null, not error~
        });

        var result = await _module.ExecuteAsync(ctx, CancellationToken.None);

        result.Success.Should().BeTrue("missing path with required=false should NOT fail~ 💖");
        result.Outputs.Should().ContainKey("ghost");
        result.Outputs["ghost"].Should().BeNull();
    }

    /// <summary>
    /// A wildcard array selector should return multiple values as a list~ 🎯
    /// </summary>
    [Fact]
    public async Task JsonPath_ArrayQuery_ReturnsList()
    {
        StubJson("/products",
            """{"products":[{"id":"p1","name":"Widget"},{"id":"p2","name":"Gadget"},{"id":"p3","name":"Donut"}]}""");

        var ctx = BuildContext(new Dictionary<string, object?>
        {
            ["url"] = $"{_server.Urls[0]}/products",
            ["method"] = "GET",
            ["responseExtract"] = new Dictionary<string, string>
            {
                ["ids"]   = "$.products[*].id",
                ["names"] = "$.products[*].name",
            },
        });

        var result = await _module.ExecuteAsync(ctx, CancellationToken.None);

        result.Success.Should().BeTrue();

        var ids = result.Outputs["ids"].Should().BeAssignableTo<System.Collections.IEnumerable>().Subject
            .Cast<object>().ToList();
        ids.Should().BeEquivalentTo(new[] { "p1", "p2", "p3" },
            "wildcard array query should return all matching values as a list~ 🌸");

        var names = result.Outputs["names"].Should().BeAssignableTo<System.Collections.IEnumerable>().Subject
            .Cast<object>().ToList();
        names.Should().HaveCount(3);
    }

    // =========================================================================
    // 🔍 Regex extraction
    // =========================================================================

    /// <summary>
    /// A regex with a named capture group <c>(?&lt;value&gt;...)</c> should populate the output
    /// with the captured substring~ 🔍
    /// </summary>
    [Fact]
    public async Task Regex_NamedCapture_PopulatesOutput()
    {
        StubText("/version", "DotFlow-Server/v2.3.5 (production)");

        var ctx = BuildContext(new Dictionary<string, object?>
        {
            ["url"] = $"{_server.Urls[0]}/version",
            ["method"] = "GET",
            ["responseRegex"] = new Dictionary<string, string>
            {
                ["version"] = @"v(?<value>\d+\.\d+\.\d+)",
            },
        });

        var result = await _module.ExecuteAsync(ctx, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Outputs["version"].Should().Be("2.3.5",
            "named capture group <value> should surface just the semantic version~ 💖");
    }

    /// <summary>
    /// When the regex finds no match in the body, the output should be <c>null</c> (not an error)~ 🔍
    /// </summary>
    [Fact]
    public async Task Regex_NoMatch_OutputIsNull()
    {
        StubText("/plain", "Hello, World! No version here.");

        var ctx = BuildContext(new Dictionary<string, object?>
        {
            ["url"] = $"{_server.Urls[0]}/plain",
            ["method"] = "GET",
            ["responseRegex"] = new Dictionary<string, string>
            {
                ["version"] = @"v(?<value>\d+\.\d+\.\d+)",
            },
        });

        var result = await _module.ExecuteAsync(ctx, CancellationToken.None);

        result.Success.Should().BeTrue("no-match regex should not fail the module~ 🌸");
        result.Outputs.Should().ContainKey("version");
        result.Outputs["version"].Should().BeNull();
    }

    // =========================================================================
    // 🏷️ Header extraction
    // =========================================================================

    /// <summary>
    /// The <c>Location</c> header from a <c>201 Created</c> response should be surfaced via
    /// <c>headerExtract</c>~ 🏷️
    /// </summary>
    [Fact]
    public async Task HeaderExtract_LocationFrom201_Populated()
    {
        _server
            .Given(Request.Create().WithPath("/resources").UsingPost())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(201)
                    .WithHeader("Content-Type", "application/json")
                    .WithHeader("Location", "/resources/9f8b2a")
                    .WithBody("""{"id":"9f8b2a"}"""));

        var ctx = BuildContext(new Dictionary<string, object?>
        {
            ["url"] = $"{_server.Urls[0]}/resources",
            ["method"] = "POST",
            ["body"] = new Dictionary<string, object?> { ["name"] = "NewResource" },
            ["headerExtract"] = new Dictionary<string, string>
            {
                ["location"]  = "Location",
                ["ct"]        = "Content-Type",
                ["missing"]   = "X-Does-Not-Exist",
            },
        });

        var result = await _module.ExecuteAsync(ctx, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Outputs["location"].Should().Be("/resources/9f8b2a",
            "Location header should be surfaced under the mapped output port name~ 🌸");
        result.Outputs["ct"].Should().NotBeNull("Content-Type should be extractable too~ 💖");
        result.Outputs["missing"].Should().BeNull("unknown headers produce null, not an error~ 🌸");
    }
}
