// <copyright file="PersistenceIntegrationTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Engine;

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
using Workflow.Modules;
using Workflow.Modules.Abstractions;
using Workflow.Persistence.Abstractions;
using Workflow.Persistence.Models;
using Workflow.Persistence.Sqlite;
using Xunit;

/// <summary>
/// Integration tests for Phase 2.1.5 engine persistence wiring using SQLite in-memory storage.
/// </summary>
public sealed class PersistenceIntegrationTests : TestKit, IAsyncLifetime
{
    private SqlitePersistenceProvider _provider = null!;
    private SqliteConnection _heldConnection = null!;
    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        var dbName = $"engine_persist_{Guid.NewGuid():N}";
        _connectionString = $"Filename=file:memdb-{dbName}.db;Mode=Memory;Cache=Shared";

        _heldConnection = new SqliteConnection(_connectionString);
        await _heldConnection.OpenAsync();

        _provider = new SqlitePersistenceProvider(_connectionString);
        await _provider.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _heldConnection.DisposeAsync();
    }

    [Fact]
    public async Task Engine_WithSqliteProvider_ShouldCreateExecutionRecord()
    {
        var services = BuildServiceProvider();
        var supervisor = Sys.ActorOf(WorkflowSupervisor.Props(services));

        var workflow = CreateSingleNodeWorkflow(moduleId: "missing.module");
        var startOptions = new ExecutionStartOptions("api-user-1", VariableWriteMode.Execution);

        supervisor.Tell(new CreateWorkflowInstance(workflow.Id, workflow, HashMap<string, object?>.Empty, startOptions));
        var created = ExpectMsg<WorkflowInstanceCreated>(TimeSpan.FromSeconds(5));

        await WaitForTerminalState(supervisor, created.ExecutionId, TimeSpan.FromSeconds(10));

        var record = await _provider.ExecutionHistory.GetExecutionAsync(created.ExecutionId);
        record.Should().NotBeNull();
        record!.TriggeredBy.Should().Be("api-user-1");
        record.State.Should().Be(ExecutionState.Completed);
    }

    [Fact]
    public async Task Engine_WithSqliteProvider_ShouldRecordNodeCompletions()
    {
        var services = BuildServiceProvider();
        var supervisor = Sys.ActorOf(WorkflowSupervisor.Props(services));

        var workflow = CreateLinearWorkflow(moduleId: "missing.module");
        supervisor.Tell(new CreateWorkflowInstance(workflow.Id, workflow, HashMap<string, object?>.Empty));
        var created = ExpectMsg<WorkflowInstanceCreated>(TimeSpan.FromSeconds(5));

        await WaitForTerminalState(supervisor, created.ExecutionId, TimeSpan.FromSeconds(10));

        var nodeRecords = await _provider.ExecutionHistory.GetNodeExecutionsAsync(created.ExecutionId);
        nodeRecords.Should().NotBeEmpty();
        nodeRecords.Should().OnlyContain(r => r.State == NodeExecutionState.Completed);
    }

    [Fact]
    public async Task Engine_WithSqliteProvider_ShouldRecordFailureWithError()
    {
        var services = BuildServiceProvider(registerFailingModule: true);
        var supervisor = Sys.ActorOf(WorkflowSupervisor.Props(services));

        var workflow = CreateSingleNodeWorkflow(moduleId: "test.fail");
        supervisor.Tell(new CreateWorkflowInstance(workflow.Id, workflow, HashMap<string, object?>.Empty));
        var created = ExpectMsg<WorkflowInstanceCreated>(TimeSpan.FromSeconds(5));

        var status = await WaitForTerminalState(supervisor, created.ExecutionId, TimeSpan.FromSeconds(10));
        status.State.Should().Be(ExecutionState.Failed);

        var record = await _provider.ExecutionHistory.GetExecutionAsync(created.ExecutionId);
        record.Should().NotBeNull();
        record!.State.Should().Be(ExecutionState.Failed);
        record.Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Engine_WithoutProvider_ShouldStillRun()
    {
        var services = new ServiceCollection();
        services.AddSingleton<WorkflowValidator>();
        var serviceProvider = services.BuildServiceProvider();

        var executionId = Guid.NewGuid();
        var workflow = CreateSingleNodeWorkflow(moduleId: "missing.module");
        var executor = Sys.ActorOf(WorkflowExecutor.Props(executionId, workflow, new Dictionary<string, object?>(), serviceProvider));

        executor.Tell(new StartExecution(executionId));
        await Task.Delay(1200);

        executor.Tell(new GetWorkflowStatus(executionId));
        var status = ExpectMsg<WorkflowStatusResponse>(TimeSpan.FromSeconds(5));
        status.State.Should().BeOneOf(ExecutionState.Running, ExecutionState.Completed);
    }

    [Fact]
    public async Task Engine_WithSqliteProvider_ShouldPersistNodeStatesConsistently()
    {
        var services = BuildServiceProvider();
        var supervisor = Sys.ActorOf(WorkflowSupervisor.Props(services));

        var workflow = CreateLinearWorkflow(moduleId: "missing.module");
        supervisor.Tell(new CreateWorkflowInstance(workflow.Id, workflow, HashMap<string, object?>.Empty));
        var created = ExpectMsg<WorkflowInstanceCreated>(TimeSpan.FromSeconds(5));

        var status = await WaitForTerminalState(supervisor, created.ExecutionId, TimeSpan.FromSeconds(10));
        status.State.Should().Be(ExecutionState.Completed);

        var nodeRecords = await _provider.ExecutionHistory.GetNodeExecutionsAsync(created.ExecutionId);
        var completedCount = status.NodeStates.Values.Count(s => s == NodeExecutionState.Completed);
        nodeRecords.Count.Should().Be(completedCount);
    }

    [Fact]
    public async Task Engine_VariableUpdates_ShouldDefaultToExecutionScope()
    {
        var services = BuildServiceProvider(registerVariableModule: true);
        var supervisor = Sys.ActorOf(WorkflowSupervisor.Props(services));

        var workflow = CreateSingleNodeWorkflow(moduleId: "test.var.update");
        var options = new ExecutionStartOptions("api-user-2", VariableWriteMode.Execution);
        supervisor.Tell(new CreateWorkflowInstance(workflow.Id, workflow, HashMap<string, object?>.Empty, options));
        var created = ExpectMsg<WorkflowInstanceCreated>(TimeSpan.FromSeconds(5));

        await WaitForTerminalState(supervisor, created.ExecutionId, TimeSpan.FromSeconds(10));

        var executionEntry = await _provider.Variables.GetVariableAsync(VariableScope.ForExecution(created.ExecutionId), "persistedVar");
        var workflowEntry = await _provider.Variables.GetVariableAsync(VariableScope.ForWorkflow(workflow.Id), "persistedVar");

        executionEntry.Should().NotBeNull();
        workflowEntry.Should().BeNull();
    }

    private IServiceProvider BuildServiceProvider(
        bool registerFailingModule = false,
        bool registerVariableModule = false)
    {
        var services = new ServiceCollection();
        services.AddSingleton<WorkflowValidator>();

        services.AddSingleton<IPersistenceProvider>(_provider);
        services.AddSingleton<IWorkflowRepository>(_provider.Workflows);
        services.AddSingleton<IExecutionHistoryRepository>(_provider.ExecutionHistory);
        services.AddSingleton<IVariableStore>(_provider.Variables);

        var registry = new InMemoryModuleRegistry();
        if (registerFailingModule)
        {
            registry.RegisterModule(new AlwaysFailModule());
        }

        if (registerVariableModule)
        {
            registry.RegisterModule(new VariableUpdateModule());
        }

        services.AddSingleton<IModuleRegistry>(registry);

        return services.BuildServiceProvider();
    }

    private async Task<WorkflowStatusResponse> WaitForTerminalState(
        IActorRef supervisor,
        Guid executionId,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            supervisor.Tell(new GetWorkflowStatus(executionId));

            if (ExpectMsg<object>(TimeSpan.FromSeconds(2)) is WorkflowStatusResponse status)
            {
                if (status.State is ExecutionState.Completed or ExecutionState.Failed or ExecutionState.Cancelled)
                {
                    return status;
                }
            }

            await Task.Delay(150);
        }

        supervisor.Tell(new GetWorkflowStatus(executionId));
        return ExpectMsg<WorkflowStatusResponse>(TimeSpan.FromSeconds(3));
    }

    private static WorkflowDefinition CreateSingleNodeWorkflow(string moduleId)
    {
        var node = new NodeDefinition(
            Id: "node_1",
            ModuleId: moduleId,
            Name: "Node 1",
            Properties: HashMap<string, JsonElement>.Empty,
            Position: null,
            ErrorHandling: null,
            Timeout: null,
            RetryPolicy: null,
            Metadata: null);

        return new WorkflowDefinition(
            Id: Guid.NewGuid(),
            Name: "Single node workflow",
            Description: "Persistence integration single node",
            Version: new Version(1, 0, 0),
            Nodes: Arr.create(node),
            Connections: Arr<ConnectionDefinition>.Empty,
            Variables: HashMap<string, VariableDefinition>.Empty,
            Trigger: null,
            ErrorHandling: null,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: null,
            Tags: null);
    }

    private static WorkflowDefinition CreateLinearWorkflow(string moduleId)
    {
        var first = new NodeDefinition(
            Id: "node_1",
            ModuleId: moduleId,
            Name: "Node 1",
            Properties: HashMap<string, JsonElement>.Empty,
            Position: null,
            ErrorHandling: null,
            Timeout: null,
            RetryPolicy: null,
            Metadata: null);

        var second = new NodeDefinition(
            Id: "node_2",
            ModuleId: moduleId,
            Name: "Node 2",
            Properties: HashMap<string, JsonElement>.Empty,
            Position: null,
            ErrorHandling: null,
            Timeout: null,
            RetryPolicy: null,
            Metadata: null);

        var connection = new ConnectionDefinition(
            SourceNodeId: "node_1",
            SourcePortName: "output",
            TargetNodeId: "node_2",
            TargetPortName: "input",
            Condition: null,
            Priority: 0);

        return new WorkflowDefinition(
            Id: Guid.NewGuid(),
            Name: "Linear workflow",
            Description: "Persistence integration linear workflow",
            Version: new Version(1, 0, 0),
            Nodes: Arr.create(first, second),
            Connections: Arr.create(connection),
            Variables: HashMap<string, VariableDefinition>.Empty,
            Trigger: null,
            ErrorHandling: null,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: null,
            Tags: null);
    }

    private sealed class AlwaysFailModule : IWorkflowModule
    {
        public string ModuleId => "test.fail";

        public string DisplayName => "Always fail";

        public string Category => "Testing";

        public string Description => "Fails on every execution.";

        public string Icon => "x";

        public Version Version => new(1, 0, 0);

        public ModuleSchema Schema => ModuleSchema.Empty;

        public Task<ModuleResult> ExecuteAsync(ModuleExecutionContext context, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Boom from test.fail");
    }

    private sealed class VariableUpdateModule : IWorkflowModule
    {
        public string ModuleId => "test.var.update";

        public string DisplayName => "Variable update";

        public string Category => "Testing";

        public string Description => "Publishes variable updates.";

        public string Icon => "v";

        public Version Version => new(1, 0, 0);

        public ModuleSchema Schema => ModuleSchema.Empty;

        public Task<ModuleResult> ExecuteAsync(ModuleExecutionContext context, CancellationToken cancellationToken = default)
        {
            var outputs = new Dictionary<string, object?> { ["ok"] = true };
            var varUpdates = new Dictionary<string, object?> { ["persistedVar"] = "uwu" };
            return Task.FromResult(ModuleResult.Ok(outputs, varUpdates));
        }
    }
}


