// <copyright file="ApiClientTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Api;

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using LanguageExt;
using static LanguageExt.Prelude;
using Workflow.Core.Models;
using Workflow.UI.Client.Api;
using Workflow.UI.Client.Api.Dtos;
using Xunit;

/// <summary>
/// ðŸ§ª Phase 3.3.a.0 â€” Tests for the typed API clients + DTO wire fidelity. The round-trip specs
/// double as the React-port contract spec (D2)~ âœ¨.
/// </summary>
public sealed class ApiClientTests
{
    private static JsonElement El(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static WorkflowDefinition SampleDefinition()
    {
        var node1 = new NodeDefinition(
            "http-1",
            "builtin.http.request",
            "HTTP Request",
            HashMap<string, JsonElement>(("url", El("\"https://api.example.com\"")), ("timeout", El("30"))),
            new Position(120, 80),
            Metadata: HashMap<string, string>(("moduleVersion", "1.0.0")));

        var node2 = new NodeDefinition(
            "log-1",
            "builtin.log",
            "Log",
            HashMap<string, JsonElement>(("level", El("\"info\""))),
            new Position(400, 80));

        var conn = new ConnectionDefinition("http-1", "success", "log-1", "input");

        return new WorkflowDefinition(
            Guid.NewGuid(),
            "order-pipeline",
            "Processes orders",
            new Version(1, 4, 0),
            Arr.create(node1, node2),
            Arr.create(conn),
            HashMap<string, VariableDefinition>(),
            Tags: Arr.create("prod", "orders"));
    }

    [Fact]
    public void Dtos_RoundTrip_NoDataLoss()
    {
        var original = SampleDefinition();

        // Serialize with the server's exact converters â†’ wire JSON.
        var serverJson = JsonSerializer.Serialize(original, WireJson.ServerOptions);

        // Deserialize into the CLIENT DTO mirror, then re-serialize with plain Web options.
        var dto = JsonSerializer.Deserialize<WorkflowDto>(serverJson, ApiHttp.Json);
        dto.Should().NotBeNull();
        var clientJson = JsonSerializer.Serialize(dto, ApiHttp.Json);

        // Deserialize the client's JSON back into the domain type via the server converters.
        var roundTripped = JsonSerializer.Deserialize<WorkflowDefinition>(clientJson, WireJson.ServerOptions);

        // Compare canonical wire JSON (JsonElement has no value-equality, so record .Be() can't be used).
        var reSerialized = JsonSerializer.Serialize(roundTripped, WireJson.ServerOptions);
        reSerialized.Should().Be(serverJson, "the client DTO must round-trip the wire format losslessly");
    }

    [Fact]
    public async Task WorkflowsClient_Get_ParsesFullDefinition()
    {
        var json = JsonSerializer.Serialize(SampleDefinition(), WireJson.ServerOptions);
        var handler = FakeHttpMessageHandler.Json(json);
        var client = new WorkflowsClient(handler.CreateClient());

        var dto = await client.GetAsync(Guid.NewGuid());

        dto.Nodes.Should().HaveCount(2);
        dto.Nodes[0].Position.Should().NotBeNull();
        dto.Nodes[0].Position!.X.Should().Be(120);
        dto.Nodes[0].Properties["timeout"].ValueKind.Should().Be(JsonValueKind.Number);
        dto.Connections.Should().ContainSingle();
        dto.Nodes[0].Metadata!["moduleVersion"].Should().Be("1.0.0");
    }

    [Fact]
    public async Task WorkflowsClient_List_ParsesPagedEnvelope()
    {
        var page = new PageDto<WorkflowSummaryDto>(
            new List<WorkflowSummaryDto> { new(Guid.NewGuid(), "wf", null, "1.0.0", new List<string>(), 3, null, null) },
            1, 1, 20, 1);
        var handler = FakeHttpMessageHandler.Json(JsonSerializer.Serialize(page, ApiHttp.Json));
        var client = new WorkflowsClient(handler.CreateClient());

        var result = await client.ListAsync(search: "wf");

        result.TotalCount.Should().Be(1);
        result.Items.Should().ContainSingle(x => x.Name == "wf" && x.NodeCount == 3);
        handler.Requests[0].RequestUri!.Query.Should().Contain("name=wf");
    }

    [Fact]
    public async Task WorkflowsClient_Update_SendsWireShapeJson()
    {
        var def = SampleDefinition();
        var serverJson = JsonSerializer.Serialize(def, WireJson.ServerOptions);
        var dto = JsonSerializer.Deserialize<WorkflowDto>(serverJson, ApiHttp.Json)! with { Id = def.Id };

        // Echo back the same body so the client can parse the response.
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(dto, ApiHttp.Json), System.Text.Encoding.UTF8, "application/json"),
        });
        var client = new WorkflowsClient(handler.CreateClient());

        await client.UpdateAsync(dto);

        // What we PUT must deserialize straight back into a WorkflowDefinition (server-parseable).
        var sent = JsonSerializer.Deserialize<WorkflowDefinition>(handler.Bodies[0], WireJson.ServerOptions);
        JsonSerializer.Serialize(sent, WireJson.ServerOptions).Should().Be(serverJson);
        handler.Requests[0].Method.Should().Be(HttpMethod.Put);
    }

    [Fact]
    public async Task WorkflowsClient_ServerError_SurfacesProblemDetails()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = new StringContent(
                "{\"title\":\"Validation failed\",\"detail\":\"node http-1 is invalid\",\"errors\":{\"url\":[\"required\"]}}",
                System.Text.Encoding.UTF8,
                "application/problem+json"),
        });
        var client = new WorkflowsClient(handler.CreateClient());

        var act = async () => await client.GetAsync(Guid.NewGuid());

        var ex = await act.Should().ThrowAsync<ApiException>();
        ex.Which.Error.StatusCode.Should().Be(422);
        ex.Which.Error.Title.Should().Be("Validation failed");
        ex.Which.Error.Errors!["url"].Should().Contain("required");
    }

    [Fact]
    public async Task ModulesClient_List_ParsesSchemas_AndCaches()
    {
        var modules = new List<ModuleSummaryDto>
        {
            new("builtin.http.request", "HTTP Request", "HTTP", "makes requests", "ðŸŒ", "1.0.0"),
        };
        var handler = FakeHttpMessageHandler.Json(JsonSerializer.Serialize(modules, ApiHttp.Json));
        var client = new ModulesClient(handler.CreateClient());

        var first = await client.ListAsync();
        var second = await client.ListAsync();

        first.Should().ContainSingle(m => m.Id == "builtin.http.request");
        handler.Requests.Should().HaveCount(1, "the second call must be served from cache");
    }

    [Fact]
    public async Task ModulesClient_Get_ParsesEditorTypes()
    {
        var details = new ModuleDetailsDto(
            "builtin.script", "Script", "Scripting", "runs scripts", "ðŸ“œ", "1.0.0",
            new ModuleSchemaDto(
                new List<PortDefinitionDto>(),
                new List<PortDefinitionDto>(),
                new List<ModulePropertyDefinitionDto>
                {
                    new("code", "Code", "String", null, true, null, "Code", null),
                    new("language", "Language", "String", null, true, null, "Dropdown", new List<JsonElement> { El("\"javascript\"") }),
                }),
            new List<string>());
        var handler = FakeHttpMessageHandler.Json(JsonSerializer.Serialize(details, ApiHttp.Json));
        var client = new ModulesClient(handler.CreateClient());

        var result = await client.GetAsync("builtin.script");

        result!.Schema.Properties.Should().Contain(p => p.Name == "code" && p.EditorType == "Code");
        result.Schema.Properties.Should().Contain(p => p.Name == "language" && p.EditorType == "Dropdown");
    }

    [Fact]
    public async Task ExecutionsClient_Execute_ReturnsExecutionId()
    {
        var started = new ExecutionStartedDto(Guid.NewGuid(), "accepted");
        var handler = FakeHttpMessageHandler.Json(JsonSerializer.Serialize(started, ApiHttp.Json));
        var client = new ExecutionsClient(handler.CreateClient());

        var result = await client.ExecuteAsync(Guid.NewGuid());

        result.ExecutionId.Should().Be(started.ExecutionId);
        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
    }

    [Fact]
    public async Task AuthState_Handler_StampsBearer()
    {
        var auth = new AuthState();
        auth.SetToken("jwt-token-123");
        var inner = FakeHttpMessageHandler.Json("{}");
        var handler = new AuthMessageHandler(auth) { InnerHandler = inner };
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };

        await client.GetAsync("api/v1/status");

        inner.Requests[0].Headers.Authorization!.Scheme.Should().Be("Bearer");
        inner.Requests[0].Headers.Authorization!.Parameter.Should().Be("jwt-token-123");
    }

    [Fact]
    public async Task AuthState_Handler_StampsApiKey()
    {
        var auth = new AuthState();
        auth.SetApiKey("key-abc");
        var inner = FakeHttpMessageHandler.Json("{}");
        var handler = new AuthMessageHandler(auth) { InnerHandler = inner };
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };

        await client.GetAsync("api/v1/status");

        inner.Requests[0].Headers.GetValues("X-API-Key").Should().Contain("key-abc");
    }
}
