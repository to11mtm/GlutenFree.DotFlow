// <copyright file="HttpPersistenceTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.Http;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit.Xunit2;
using FluentAssertions;
using LanguageExt;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Core.Abstractions;
using Workflow.Core.Models;
using Workflow.Engine.Actors;
using Workflow.Engine.Messages;
using Workflow.Engine.Services;
using Workflow.Modules;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin;
using Workflow.Modules.Builtin.Http;
using Workflow.Persistence.Abstractions;
using Workflow.Persistence.Models;
using Workflow.Persistence.Sqlite;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

/// <summary>
/// 💾 Phase 2.3.8 — Persistence integration tests for HTTP + webhook modules~✨💖.
/// Proves that HTTP node executions are persisted with <c>statusCode</c> and <c>durationMs</c>
/// outputs, and that webhook-triggered executions carry the correct module outputs~ 🌸
/// </summary>
/// <remarks>
/// <para>
/// Stack: WireMock.Net (in-process HTTP server) + SQLite in-memory + WorkflowSupervisor (Akka)~
/// </para>
/// <para>
/// CopilotNote: Uses a held SQLiteConnection to keep the in-memory DB alive for the duration
/// of each test (same pattern as PersistenceIntegrationTests)~ 🧠
/// </para>
/// </remarks>
public sealed class HttpPersistenceTests : TestKit, IAsyncLifetime
{
    private SqlitePersistenceProvider _provider = null!;
    private SqliteConnection _heldConnection = null!;
    private WireMockServer _server = null!;

    // =========================================================================
    // IAsyncLifetime setup / teardown 🏗️
    // =========================================================================

    public async Task InitializeAsync()
    {
        // Unique in-memory DB per test class run~
        var dbName = $"http_persist_{Guid.NewGuid():N}";
        var cs = $"Filename=file:memdb-{dbName}.db;Mode=Memory;Cache=Shared";

        _heldConnection = new SqliteConnection(cs);
        await _heldConnection.OpenAsync();

        _provider = new SqlitePersistenceProvider(cs);
        await _provider.InitializeAsync();

        _server = WireMockServer.Start();
    }

    public async Task DisposeAsync()
    {
        _server.Stop();
        _server.Dispose();
        await _provider.DisposeAsync();
        await _heldConnection.DisposeAsync();
    }

    // =========================================================================
    // 🔢 Test 1 — HttpNodeExecution_PersistedWithStatusCodeMetadata
    // =========================================================================

    /// <summary>
    /// After running a single <c>builtin.http.request</c> node, the persisted
    /// <see cref="NodeExecutionRecord.Outputs"/> must contain <c>statusCode</c> and
    /// <c>durationMs</c>~ 📊.
    /// </summary>
    [Fact]
    public async Task HttpNodeExecution_PersistedWithStatusCodeMetadata()
    {
        // Arrange — WireMock responds with 200 + JSON payload~
        _server
            .Given(Request.Create().WithPath("/api/user").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"userId":42,"name":"Ami-chan"}"""));

        var services = BuildServiceProvider();
        var supervisor = Sys.ActorOf(WorkflowSupervisor.Props(services));

        var definition = BuildSingleHttpNodeWorkflow(
            nodeId: "http_node",
            url: $"{_server.Urls[0]}/api/user",
            method: "GET");

        // Act — start the workflow~
        supervisor.Tell(new CreateWorkflowInstance(
            definition.Id, definition, HashMap<string, object?>.Empty));
        var created = ExpectMsg<WorkflowInstanceCreated>(TimeSpan.FromSeconds(5));

        await WaitForTerminalState(supervisor, created.ExecutionId, TimeSpan.FromSeconds(15));

        // Assert — node execution record has statusCode + durationMs in outputs~
        var nodeRecords = await _provider.ExecutionHistory
            .GetNodeExecutionsAsync(created.ExecutionId);

        nodeRecords.Should().NotBeEmpty("at least one node should have been executed~ 🌸");

        var httpRecord = nodeRecords.FirstOrDefault(r => r.NodeId == "http_node");
        httpRecord.Should().NotBeNull(
            "the http_node should be persisted in execution history~ 💖");

        httpRecord!.State.Should().Be(NodeExecutionState.Completed,
            "the HTTP call to WireMock 200 should succeed~ ✅");

        httpRecord.Outputs.Should().NotBeNull(
            "outputs must be persisted for HTTP nodes~ 📊");

        // statusCode must be present and equal 200~
        httpRecord.Outputs!.Should().ContainKey("statusCode",
            "HTTP module always outputs statusCode~ 🔢");
        var statusCode = httpRecord.Outputs["statusCode"];
        // CopilotNote: After round-tripping through SQLite JSON serialisation, numeric values
        // come back as JsonElement — unwrap with GetInt64() for comparison~ 🔢
        var statusCodeLong = statusCode is JsonElement je
            ? je.GetInt64()
            : Convert.ToInt64(statusCode, System.Globalization.CultureInfo.InvariantCulture);
        statusCodeLong.Should().Be(200, "WireMock responded with 200 OK~ ✅");

        // durationMs must be present and non-negative~
        httpRecord.Outputs.Should().ContainKey("durationMs",
            "HTTP module always outputs durationMs~ ⏱️");
        var durationRaw = httpRecord.Outputs["durationMs"];
        var durationMs = durationRaw is JsonElement dj
            ? dj.GetInt64()
            : Convert.ToInt64(durationRaw, System.Globalization.CultureInfo.InvariantCulture);
        durationMs.Should().BeGreaterThanOrEqualTo(0,
            "durationMs is the round-trip time in milliseconds~ ⏱️");
    }

    // =========================================================================
    // 🪝 Test 2 — WebhookTriggeredExecution_PersistedWithWebhookIdMetadata
    // =========================================================================

    /// <summary>
    /// When a workflow is started with <c>__webhook__</c> pre-seeded inputs, the
    /// <see cref="NodeExecutionRecord.Outputs"/> of the <c>builtin.http.webhook</c>
    /// trigger node must contain the unpacked output ports (<c>method</c>, <c>body</c>, etc.)~ 🪝.
    /// </summary>
    [Fact]
    public async Task WebhookTriggeredExecution_PersistedWithWebhookIdMetadata()
    {
        // Arrange — pre-seed the __webhook__ input bag as the dispatcher would~
        var webhookPayload = new Dictionary<string, object?>
        {
            ["body"] = new Dictionary<string, object?> { ["orderId"] = "ord-99", ["event"] = "order.placed" },
            ["headers"] = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
            ["query"] = new Dictionary<string, string> { ["source"] = "github" },
            ["method"] = "POST",
            ["receivedAt"] = DateTimeOffset.UtcNow,
        };

        var workflowInputs = HashMap<string, object?>.Empty.Add(
            WebhookTriggerModule.WebhookInputKey, webhookPayload);

        var services = BuildServiceProvider();
        var supervisor = Sys.ActorOf(WorkflowSupervisor.Props(services));

        var definition = BuildSingleWebhookTriggerWorkflow("wh_trigger");

        // Act~
        supervisor.Tell(new CreateWorkflowInstance(
            definition.Id, definition, workflowInputs));
        var created = ExpectMsg<WorkflowInstanceCreated>(TimeSpan.FromSeconds(5));

        await WaitForTerminalState(supervisor, created.ExecutionId, TimeSpan.FromSeconds(15));

        // Assert — wh_trigger node outputs contain the unpacked webhook fields~
        var nodeRecords = await _provider.ExecutionHistory
            .GetNodeExecutionsAsync(created.ExecutionId);

        var triggerRecord = nodeRecords.FirstOrDefault(r => r.NodeId == "wh_trigger");
        triggerRecord.Should().NotBeNull(
            "wh_trigger node should be persisted~ 🪝");

        triggerRecord!.State.Should().Be(NodeExecutionState.Completed,
            "the webhook trigger module should succeed when __webhook__ input is present~ ✅");

        triggerRecord.Outputs.Should().NotBeNull(
            "trigger outputs must be persisted~ 📊");

        triggerRecord.Outputs!.Should().ContainKey("method",
            "WebhookTriggerModule unpacks 'method' from __webhook__ input~ 🌐");

        var method = triggerRecord.Outputs["method"]?.ToString();
        method.Should().Be("POST",
            "the pre-seeded method was 'POST'~ ✅");
    }

    // =========================================================================
    // Helpers 🛠️
    // =========================================================================

    private IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<WorkflowValidator>();
        services.AddSingleton<IExecutionStateStore, InMemoryExecutionStateStore>();

        // 🌐 Register IHttpClientFactory so HttpRequestModule can resolve it~
        services.AddWorkflowModules();

        // 💾 Persistence via SQLite in-memory~
        services.AddSingleton<IPersistenceProvider>(_provider);
        services.AddSingleton<IWorkflowRepository>(_provider.Workflows);
        services.AddSingleton<IExecutionHistoryRepository>(_provider.ExecutionHistory);
        services.AddSingleton<IVariableStore>(_provider.Variables);

        // 📦 Register ALL built-in modules (includes http.request + http.webhook)~
        var registry = new InMemoryModuleRegistry();
        BuiltinModules.RegisterAll(registry);
        services.AddSingleton<IModuleRegistry>(registry);

        return services.BuildServiceProvider();
    }

    private static WorkflowDefinition BuildSingleHttpNodeWorkflow(
        string nodeId, string url, string method)
    {
        var node = new NodeDefinition(
            Id: nodeId,
            ModuleId: "builtin.http.request",
            Name: "HTTP Node",
            Properties: HashMap.create(
                ("url", JsonString(url)),
                ("method", JsonString(method))),
            Position: null, ErrorHandling: null,
            Timeout: null, RetryPolicy: null, Metadata: null);

        return new WorkflowDefinition(
            Id: Guid.NewGuid(),
            Name: "Http Persist Test",
            Description: "Single http.request node for persistence assertion",
            Version: new Version(1, 0, 0),
            Nodes: Arr.create(node),
            Connections: Arr<ConnectionDefinition>.Empty,
            Variables: HashMap<string, VariableDefinition>.Empty,
            Trigger: null, ErrorHandling: null,
            CreatedAt: DateTimeOffset.UtcNow, UpdatedAt: null, Tags: null);
    }

    private static WorkflowDefinition BuildSingleWebhookTriggerWorkflow(string nodeId)
    {
        var node = new NodeDefinition(
            Id: nodeId,
            ModuleId: "builtin.http.webhook",
            Name: "Webhook Trigger",
            Properties: HashMap.create(
                ("webhookId", JsonString("test-hook"))),
            Position: null, ErrorHandling: null,
            Timeout: null, RetryPolicy: null, Metadata: null);

        return new WorkflowDefinition(
            Id: Guid.NewGuid(),
            Name: "Webhook Persist Test",
            Description: "Single http.webhook node for persistence assertion",
            Version: new Version(1, 0, 0),
            Nodes: Arr.create(node),
            Connections: Arr<ConnectionDefinition>.Empty,
            Variables: HashMap<string, VariableDefinition>.Empty,
            Trigger: null, ErrorHandling: null,
            CreatedAt: DateTimeOffset.UtcNow, UpdatedAt: null, Tags: null);
    }

    private static JsonElement JsonString(string value) =>
        JsonSerializer.SerializeToElement(value);

    private async Task<WorkflowStatusResponse> WaitForTerminalState(
        IActorRef supervisor, Guid executionId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            supervisor.Tell(new GetWorkflowStatus(executionId));
            if (ExpectMsg<object>(TimeSpan.FromSeconds(2)) is WorkflowStatusResponse s
                && s.State is ExecutionState.Completed or ExecutionState.Failed or ExecutionState.Cancelled)
            {
                return s;
            }

            await Task.Delay(200);
        }

        supervisor.Tell(new GetWorkflowStatus(executionId));
        return ExpectMsg<WorkflowStatusResponse>(TimeSpan.FromSeconds(3));
    }
}



