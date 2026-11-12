// <copyright file="RunAfterEdgeApiTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Api.V1;

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Workflow.Core.Models;
using Workflow.Persistence.Sqlite.Serialization;
using Xunit;

/// <summary>
/// Plan 09 — a run-after edge travels as <c>"kind": "RunAfter"</c> (by name in every serializer),
/// a data edge omits <c>kind</c>, and old JSON without it reads as data.
/// </summary>
public sealed class RunAfterEdgeApiTests : IClassFixture<ExecutionEndpointsTests.EngineApiFactory>
{
    private static readonly JsonSerializerOptions StoreJson = LanguageExtJsonConverters.CreateOptions();
    private readonly ExecutionEndpointsTests.EngineApiFactory factory;

    public RunAfterEdgeApiTests(ExecutionEndpointsTests.EngineApiFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public void StoreJson_WritesRunAfterByName_AndOmitsKindForData()
    {
        var after = JsonSerializer.Serialize(new ConnectionDefinition("a", "out", "b", string.Empty, Kind: ConnectionKind.RunAfter), StoreJson);
        var data = JsonSerializer.Serialize(new ConnectionDefinition("a", "out", "b", "in"), StoreJson);

        after.Should().MatchRegex("\"[Kk]ind\":\\s*\"RunAfter\"");
        data.Should().NotContainEquivalentOf("kind");
    }

    [Theory]
    [InlineData("{\"sourceNodeId\":\"a\",\"sourcePortName\":\"out\",\"targetNodeId\":\"b\",\"targetPortName\":\"in\"}", ConnectionKind.Data)]
    [InlineData("{\"sourceNodeId\":\"a\",\"sourcePortName\":\"out\",\"targetNodeId\":\"b\",\"targetPortName\":\"\",\"kind\":\"runAfter\"}", ConnectionKind.RunAfter)]
    [InlineData("{\"sourceNodeId\":\"a\",\"sourcePortName\":\"out\",\"targetNodeId\":\"b\",\"targetPortName\":\"\",\"kind\":1}", ConnectionKind.RunAfter)]
    public void StoreJson_Reads(string json, ConnectionKind expected)
    {
        var options = new JsonSerializerOptions(StoreJson) { PropertyNameCaseInsensitive = true };
        JsonSerializer.Deserialize<ConnectionDefinition>(json, options)!.Kind.Should().Be(expected);
    }

    [Fact]
    public async Task Api_StoresAndReturnsARunAfterEdge()
    {
        var client = this.factory.CreateClient();
        var json = """
            {
              "name": "run-after-NAME",
              "version": "1.0.0",
              "nodes": [
                { "id": "a", "moduleId": "builtin.passthrough", "name": "A", "properties": {} },
                { "id": "c", "moduleId": "builtin.condition", "name": "C", "properties": { "condition": "true" } }
              ],
              "connections": [
                { "sourceNodeId": "a", "sourcePortName": "output", "targetNodeId": "c", "targetPortName": "", "kind": "RunAfter" }
              ],
              "variables": {}
            }
            """.Replace("NAME", Guid.NewGuid().ToString("N"), StringComparison.Ordinal);

        var create = await client.PostAsync("/api/v1/workflows", new StringContent(json, Encoding.UTF8, "application/json"));
        create.StatusCode.Should().Be(HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var stored = await client.GetFromJsonAsync<JsonElement>($"/api/v1/workflows/{id}");
        var edge = stored.GetProperty("connections").EnumerateArray().Single();
        edge.GetProperty("kind").GetString().Should().Be("RunAfter");
    }
}
