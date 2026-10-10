// <copyright file="RealtimeCompletionFindingTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Findings.Engine;

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using LanguageExt;
using Microsoft.AspNetCore.SignalR.Client;
using Workflow.Api.Contracts.RealTime;
using Workflow.Core.Models;
using Workflow.Persistence.Sqlite.Serialization;
using Workflow.Tests.Api.RealTime;
using Xunit;

/// <summary>
/// Finding 01 §12.1 — terminal and node-completion events don't reach SignalR.
/// </summary>
/// <remarks>
/// The existing bridge tests publish <c>WorkflowCompleted</c> / <c>NodeExecutionCompleted</c> onto
/// the EventStream by hand, so they can't catch that the engine never publishes those types. These
/// tests drive a REAL run through the API and assert that a subscribed SignalR client hears the
/// completion events. They reuse the in-memory SQLite <see cref="SignalRTestHarness"/> — no external
/// services. They assert the correct behaviour, so a failure confirms the finding.
/// </remarks>
[Trait("Category", FindingDefs.Category)]
public sealed class RealtimeCompletionFindingTests
{
    private static readonly JsonSerializerOptions WfJson = LanguageExtJsonConverters.CreateOptions();

    private static WorkflowDefinition Passthrough()
        => new(
            Id: Guid.Empty,
            Name: "rt-finding-" + Guid.NewGuid().ToString("N")[..8],
            Description: null,
            Version: new Version(1, 0, 0),
            Nodes: Arr.create(new NodeDefinition("n1", "builtin.passthrough", "N1", HashMap<string, JsonElement>.Empty)),
            Connections: Arr<ConnectionDefinition>.Empty,
            Variables: HashMap<string, VariableDefinition>.Empty);

    private static async Task<Guid> CreateWorkflowAsync(HttpClient client)
    {
        var content = new StringContent(JsonSerializer.Serialize(Passthrough(), WfJson), Encoding.UTF8, "application/json");
        var resp = await client.PostAsync("/api/v1/workflows", content);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task RealRun_ShouldBroadcast_ExecutionCompleted_ToSubscribedClient()
    {
        await using var harness = new SignalRTestHarness();
        var client = harness.CreateClient();
        var workflowId = await CreateWorkflowAsync(client);

        var connection = harness.CreateHubConnection();
        await connection.StartAsync();

        // Subscribe to the whole workflow so we catch the execution regardless of its generated id.
        await connection.InvokeAsync("SubscribeToWorkflow", workflowId);

        var completed = new TaskCompletionSource<ExecutionCompletedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<ExecutionCompletedEvent>("ExecutionCompleted", e => completed.TrySetResult(e));

        // Start the run AFTER subscribing.
        var start = await client.PostAsJsonAsync($"/api/v1/workflows/{workflowId}/execute", new { inputs = new { } });
        start.EnsureSuccessStatusCode();

        var act = async () => await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));

        try
        {
            await act.Should().NotThrowAsync(
                "a completed run should broadcast ExecutionCompleted to subscribed clients (01 §12.1)");
        }
        finally
        {
            await connection.DisposeAsync();
        }
    }

    [Fact]
    public async Task RealRun_ShouldBroadcast_NodeCompleted_ToSubscribedClient()
    {
        await using var harness = new SignalRTestHarness();
        var client = harness.CreateClient();
        var workflowId = await CreateWorkflowAsync(client);

        var connection = harness.CreateHubConnection();
        await connection.StartAsync();
        await connection.InvokeAsync("SubscribeToWorkflow", workflowId);

        var nodeCompleted = new TaskCompletionSource<NodeCompletedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<NodeCompletedEvent>("NodeCompleted", e => nodeCompleted.TrySetResult(e));

        var start = await client.PostAsJsonAsync($"/api/v1/workflows/{workflowId}/execute", new { inputs = new { } });
        start.EnsureSuccessStatusCode();

        var act = async () => await nodeCompleted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        try
        {
            await act.Should().NotThrowAsync(
                "a run should broadcast NodeCompleted for each finished node (01 §12.1)");
        }
        finally
        {
            await connection.DisposeAsync();
        }
    }
}
