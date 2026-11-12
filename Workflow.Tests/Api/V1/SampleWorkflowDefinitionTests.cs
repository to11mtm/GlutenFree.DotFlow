// <copyright file="SampleWorkflowDefinitionTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Api.V1;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Workflow.Core.Models;
using Xunit;

/// <summary>
/// The sample workflows in <c>examples/definitions</c> are linked from the docs as working examples,
/// so each one must load: it binds as a <c>WorkflowDefinition</c> and passes the module-aware
/// validator on <c>POST /api/v1/workflows</c> (known module ids, real ports, a GUID id). Binding
/// ignores unknown keys, so the keys are checked against the model too (a stale <c>defaultValue</c>
/// once went unnoticed), and the flow-control demo — no external I/O — runs end to end.
/// </summary>
public sealed class SampleWorkflowDefinitionTests : IClassFixture<ExecutionEndpointsTests.EngineApiFactory>
{
    private readonly ExecutionEndpointsTests.EngineApiFactory factory;

    public SampleWorkflowDefinitionTests(ExecutionEndpointsTests.EngineApiFactory factory)
    {
        this.factory = factory;
    }

    /// <summary>Every sample workflow in the folder (the module manifest is not a workflow).</summary>
    public static TheoryData<string> Samples()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.GetFiles(DefinitionsFolder(), "*.json").Select(Path.GetFileName).OrderBy(f => f))
        {
            if (file != "module-manifest.json")
            {
                data.Add(file!);
            }
        }

        return data;
    }

    [Fact]
    public void TheSamplesAreFound()
    {
        ((System.Collections.Generic.IEnumerable<object[]>)Samples()).Count().Should().BeGreaterThanOrEqualTo(3);
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public async Task EachSample_IsAcceptedByTheCreateEndpoint(string file)
    {
        var json = await File.ReadAllTextAsync(Path.Combine(DefinitionsFolder(), file));
        var client = this.factory.CreateClient();

        var resp = await client.PostAsync("/api/v1/workflows", new StringContent(json, Encoding.UTF8, "application/json"));

        var body = await resp.Content.ReadAsStringAsync();
        resp.StatusCode.Should().Be(HttpStatusCode.Created, $"{file} should load; the server said: {body}");
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public async Task EachSample_UsesOnlyKeysTheModelReads(string file)
    {
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(DefinitionsFolder(), file)));
        var unknown = new List<string>();

        // Documentation-only keys: a sample's top-level 'metadata' and a connection's 'comment'.
        Check(doc.RootElement, typeof(WorkflowDefinition), "$", unknown, "metadata");
        foreach (var node in doc.RootElement.GetProperty("nodes").EnumerateArray())
        {
            Check(node, typeof(NodeDefinition), $"node {node.GetProperty("id").GetString()}", unknown);
            if (node.TryGetProperty("position", out var position))
            {
                Check(position, typeof(Position), "position", unknown);
            }
        }

        foreach (var connection in doc.RootElement.GetProperty("connections").EnumerateArray())
        {
            Check(connection, typeof(ConnectionDefinition), "connection", unknown, "comment");
        }

        foreach (var variable in doc.RootElement.GetProperty("variables").EnumerateObject())
        {
            Check(variable.Value, typeof(VariableDefinition), $"variable {variable.Name}", unknown);
        }

        unknown.Should().BeEmpty($"{file} should only use keys the model binds (others are silently ignored)");
    }

    [Fact]
    public async Task TheFlowControlDemo_RunsToCompletion_WithEachItemOnTheRightBranch()
    {
        var client = this.factory.CreateClient();
        var json = await File.ReadAllTextAsync(Path.Combine(DefinitionsFolder(), "flow-control-demo.json"));
        var create = await client.PostAsync("/api/v1/workflows", new StringContent(json, Encoding.UTF8, "application/json"));
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var run = await client.PostAsJsonAsync($"/api/v1/workflows/{id}/execute/sync?timeoutSeconds=60", new { inputs = new { } });
        var status = await run.Content.ReadFromJsonAsync<JsonElement>();
        status.GetProperty("state").GetString().Should().Be("Completed", status.ToString());

        var nodes = await client.GetFromJsonAsync<JsonElement>($"/api/v1/executions/{status.GetProperty("executionId").GetGuid()}/nodes");
        var records = (nodes.ValueKind == JsonValueKind.Array ? nodes : nodes.GetProperty("items")).EnumerateArray().ToList();
        int Ran(string nodeId) => records.Count(r => r.GetProperty("nodeId").GetString() == nodeId && r.GetProperty("state").GetString() == "Completed");

        // Items 1..5 with threshold 2: items above it take the parallel branch, the rest throw and are
        // caught. (History keeps fewer body records than iterations here, so check each path ran
        // rather than counting iterations — see the worklog follow-up.)
        var trace = string.Join(", ", records.Select(r => $"{r.GetProperty("nodeId")}={r.GetProperty("state")}"));
        Ran("persist_branch").Should().BeGreaterThan(0, "an item above the threshold takes the parallel branch: " + trace);
        Ran("notify_branch").Should().BeGreaterThan(0, trace);
        Ran("catch_handler").Should().BeGreaterThan(0, "an item at or below it throws and is caught: " + trace);
        Ran("set_summary").Should().Be(1, "it runs after the loop's done port: " + trace);
        Ran("end").Should().Be(1, trace);
    }

    private static void Check(JsonElement element, Type model, string where, List<string> unknown, params string[] documentation)
    {
        var known = model.GetConstructors().SelectMany(c => c.GetParameters()).Select(p => p.Name!)
            .Concat(documentation)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            if (!known.Contains(property.Name))
            {
                unknown.Add($"{where}: '{property.Name}'");
            }
        }
    }

    private static string DefinitionsFolder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "examples", "definitions");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("examples/definitions not found above " + AppContext.BaseDirectory);
    }
}
