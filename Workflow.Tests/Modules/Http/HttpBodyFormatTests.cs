// <copyright file="HttpBodyFormatTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.Http;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
/// 📦 Phase 2.3.1 — Body & Response format tests for <see cref="HttpRequestModule"/>~ ✨💖.
/// Exercises form-urlencoded / multipart / XML / octet-stream request bodies, and
/// JSON / text / binary response decoding via WireMock.Net~ 🎀
/// </summary>
public sealed class HttpBodyFormatTests : IDisposable
{
    private readonly HttpRequestModule _module = new();
    private readonly WireMockServer _server;
    private readonly ServiceProvider _services;

    public HttpBodyFormatTests()
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

    private ModuleExecutionContext BuildContext(Dictionary<string, object?> properties)
        => new()
        {
            Inputs = new Dictionary<string, object?>(),
            Properties = properties,
            Variables = new Dictionary<string, object?>(),
            Logger = NullLogger.Instance,
            Services = _services,
            ExecutionId = Guid.NewGuid(),
            NodeId = "http-body-test",
        };

    #region Request body encoding 📤

    [Fact]
    public async Task Post_FormUrlEncoded_SendsCorrectContentType_AndBody()
    {
        _server
            .Given(Request.Create().WithPath("/form").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200));

        var ctx = BuildContext(new Dictionary<string, object?>
        {
            ["url"] = $"{_server.Url}/form",
            ["method"] = "POST",
            ["contentType"] = "application/x-www-form-urlencoded",
            ["body"] = new Dictionary<string, string>
            {
                ["username"] = "ami",
                ["mood"] = "kawaii",
            },
        });

        var result = await _module.ExecuteAsync(ctx, CancellationToken.None);

        result.Success.Should().BeTrue();
        var req = _server.LogEntries[0].RequestMessage;
        string.Join(",", req.Headers!["Content-Type"]).Should().Contain("application/x-www-form-urlencoded");
        req.Body.Should().Contain("username=ami").And.Contain("mood=kawaii");
    }

    [Fact]
    public async Task Post_MultipartFormData_WithByteArrayPart_RoundTrips()
    {
        _server
            .Given(Request.Create().WithPath("/upload").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(201));

        var fileBytes = Encoding.UTF8.GetBytes("hello-from-ami");
        var ctx = BuildContext(new Dictionary<string, object?>
        {
            ["url"] = $"{_server.Url}/upload",
            ["method"] = "POST",
            ["contentType"] = "multipart/form-data",
            ["body"] = new Dictionary<string, object?>
            {
                ["caption"] = "ami file",
                ["payload"] = fileBytes,
            },
        });

        var result = await _module.ExecuteAsync(ctx, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Outputs["statusCode"].Should().Be(201);

        var req = _server.LogEntries[0].RequestMessage;
        string.Join(",", req.Headers!["Content-Type"]).Should().Contain("multipart/form-data");
        req.Body.Should().Contain("caption").And.Contain("payload").And.Contain("hello-from-ami");
    }

    [Fact]
    public async Task Post_XmlBody_PassedThroughAsString()
    {
        _server
            .Given(Request.Create().WithPath("/xml").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200));

        const string xml = "<note><to>Ami</to><body>uwu</body></note>";
        var ctx = BuildContext(new Dictionary<string, object?>
        {
            ["url"] = $"{_server.Url}/xml",
            ["method"] = "POST",
            ["contentType"] = "application/xml",
            ["body"] = xml,
        });

        var result = await _module.ExecuteAsync(ctx, CancellationToken.None);

        result.Success.Should().BeTrue();
        var req = _server.LogEntries[0].RequestMessage;
        string.Join(",", req.Headers!["Content-Type"]).Should().Contain("application/xml");
        req.Body.Should().Be(xml);
    }

    [Fact]
    public async Task Post_XmlBody_Malformed_Fails()
    {
        var ctx = BuildContext(new Dictionary<string, object?>
        {
            ["url"] = $"{_server.Url}/xml",
            ["method"] = "POST",
            ["contentType"] = "application/xml",
            ["body"] = "<bad><unclosed>",
        });

        var result = await _module.ExecuteAsync(ctx, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("XML");
    }

    [Fact]
    public async Task Post_RawBytes_OctetStream_RoundTrips()
    {
        _server
            .Given(Request.Create().WithPath("/bin").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(204));

        var bytes = new byte[] { 0x01, 0x02, 0x03, 0xFE, 0xFF };
        var ctx = BuildContext(new Dictionary<string, object?>
        {
            ["url"] = $"{_server.Url}/bin",
            ["method"] = "POST",
            ["contentType"] = "application/octet-stream",
            ["body"] = bytes,
        });

        var result = await _module.ExecuteAsync(ctx, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Outputs["statusCode"].Should().Be(204);

        var req = _server.LogEntries[0].RequestMessage;
        string.Join(",", req.Headers!["Content-Type"]).Should().Contain("application/octet-stream");
        req.BodyAsBytes.Should().BeEquivalentTo(bytes);
    }

    #endregion

    #region Response body decoding 📥

    [Fact]
    public async Task Response_ApplicationJson_DeserialisedToDictionary()
    {
        _server
            .Given(Request.Create().WithPath("/json").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"name":"ami","tags":["uwu","kawaii"],"count":3}"""));

        var ctx = BuildContext(new Dictionary<string, object?>
        {
            ["url"] = $"{_server.Url}/json",
            ["method"] = "GET",
        });

        var result = await _module.ExecuteAsync(ctx, CancellationToken.None);

        result.Success.Should().BeTrue();
        var body = (IDictionary<string, object?>)result.Outputs["body"]!;
        body["name"].Should().Be("ami");
        body["count"].Should().Be(3L);
        body["tags"].Should().BeAssignableTo<IEnumerable<object?>>();
        ((IEnumerable<object?>)body["tags"]!).ToList().Should().BeEquivalentTo(new object?[] { "uwu", "kawaii" });
    }

    [Fact]
    public async Task Response_TextPlain_ReturnedAsString()
    {
        _server
            .Given(Request.Create().WithPath("/txt").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "text/plain")
                .WithBody("hello, world!"));

        var ctx = BuildContext(new Dictionary<string, object?>
        {
            ["url"] = $"{_server.Url}/txt",
            ["method"] = "GET",
        });

        var result = await _module.ExecuteAsync(ctx, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Outputs["body"].Should().Be("hello, world!");
    }

    [Fact]
    public async Task Response_OctetStream_ReturnedAsByteArray()
    {
        var payload = new byte[] { 0xCA, 0xFE, 0xBA, 0xBE };
        _server
            .Given(Request.Create().WithPath("/bin").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/octet-stream")
                .WithBody(payload));

        var ctx = BuildContext(new Dictionary<string, object?>
        {
            ["url"] = $"{_server.Url}/bin",
            ["method"] = "GET",
        });

        var result = await _module.ExecuteAsync(ctx, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Outputs["body"].Should().BeOfType<byte[]>().And.BeEquivalentTo(payload);
    }

    [Fact]
    public async Task ContentType_OutputPort_Populated()
    {
        _server
            .Given(Request.Create().WithPath("/ct").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json; charset=utf-8")
                .WithBody("""{"ok":true}"""));

        var ctx = BuildContext(new Dictionary<string, object?>
        {
            ["url"] = $"{_server.Url}/ct",
            ["method"] = "GET",
        });

        var result = await _module.ExecuteAsync(ctx, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Outputs.Should().ContainKey("contentType");
        ((string?)result.Outputs["contentType"]).Should().Be("application/json");
    }

    #endregion
}

