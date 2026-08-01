// <copyright file="VariableSeedingIntegrationTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Engine;

using System;
using System.Collections.Concurrent;
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
/// 🌱 Phase 3.5 (V2/V3) — end-to-end proof that a declared variable reaches a running node, and
/// that the variable store is finally <em>read</em> as well as written.
/// </summary>
/// <remarks>
/// Before this work the engine seeded execution variables from the run request alone
/// (<c>initialVariables: inputs.ToHashMap()</c>) and never called <c>IVariableStore</c> for reads,
/// so declared defaults and global variables were both inert.
/// </remarks>
public sealed class VariableSeedingIntegrationTests : TestKit, IAsyncLifetime
{
    private SqlitePersistenceProvider _provider = null!;
    private SqliteConnection _heldConnection = null!;

    public async Task InitializeAsync()
    {
        var dbName = $"var_seed_{Guid.NewGuid():N}";
        _heldConnection = new SqliteConnection($"Filename=file:memdb-{dbName}.db;Mode=Memory;Cache=Shared");
        await _heldConnection.OpenAsync();

        _provider = new SqlitePersistenceProvider($"Filename=file:memdb-{dbName}.db;Mode=Memory;Cache=Shared");
        await _provider.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _heldConnection.DisposeAsync();
    }

    [Fact]
    public async Task DeclaredInitialValue_IsVisibleToANode()
    {
        var capture = new CaptureVariablesModule();
        var services = BuildServiceProvider(capture);
        var supervisor = Sys.ActorOf(WorkflowSupervisor.Props(services));

        var workflow = WorkflowWith(new VariableDefinition("greeting", PropertyType.String, Json("\"hello\"")));

        await RunToCompletion(supervisor, workflow, HashMap<string, object?>.Empty);

        capture.Seen.Should().ContainKey("greeting");
        capture.Seen["greeting"].Should().Be("hello");
    }

    [Fact]
    public async Task RunInput_OverridesTheDeclaredInitialValue()
    {
        var capture = new CaptureVariablesModule();
        var services = BuildServiceProvider(capture);
        var supervisor = Sys.ActorOf(WorkflowSupervisor.Props(services));

        var workflow = WorkflowWith(new VariableDefinition("greeting", PropertyType.String, Json("\"hello\"")));
        var inputs = new[] { ("greeting", (object?)"override") }.ToHashMap();

        await RunToCompletion(supervisor, workflow, inputs);

        capture.Seen["greeting"].Should().Be("override");
    }

    [Fact]
    public async Task GlobalVariable_IsVisibleToANode()
    {
        // The headline of V3: a value set once, shared by every workflow. Previously the store was
        // written by the engine but never read, so this could not work at all.
        await _provider.Variables.SetVariableAsync(VariableScope.Global, "apiHost", "https://global.example.com");

        var capture = new CaptureVariablesModule();
        var services = BuildServiceProvider(capture);
        var supervisor = Sys.ActorOf(WorkflowSupervisor.Props(services));

        await RunToCompletion(supervisor, WorkflowWith(), HashMap<string, object?>.Empty);

        capture.Seen.Should().ContainKey("apiHost");
        capture.Seen["apiHost"].Should().Be("https://global.example.com");
    }

    [Fact]
    public async Task WorkflowScopedValue_WrittenByOneRun_IsVisibleToTheNext()
    {
        // Closes the round-trip: VariableWriteMode.Workflow used to be a one-way trip.
        var writer = new WriteVariableModule("lastRunMarker", "run-1");
        var workflow = WorkflowWith();

        var firstServices = BuildServiceProvider(writer);
        var firstSupervisor = Sys.ActorOf(WorkflowSupervisor.Props(firstServices));
        await RunToCompletion(
            firstSupervisor,
            workflow,
            HashMap<string, object?>.Empty,
            new ExecutionStartOptions("tester", VariableWriteMode.Workflow));

        // The write is queued off-thread, so give it a moment to land before the second run.
        await WaitForStoredValue(VariableScope.ForWorkflow(workflow.Id), "lastRunMarker", TimeSpan.FromSeconds(5));

        var capture = new CaptureVariablesModule();
        var secondServices = BuildServiceProvider(capture);
        var secondSupervisor = Sys.ActorOf(WorkflowSupervisor.Props(secondServices));
        await RunToCompletion(secondSupervisor, workflow, HashMap<string, object?>.Empty);

        capture.Seen.Should().ContainKey("lastRunMarker");
        capture.Seen["lastRunMarker"].Should().Be("run-1");
    }

    [Fact]
    public async Task StoreFailure_IsNonFatal_AndTheRunStillUsesDeclaredValues()
    {
        // The store is an optional dependency — an unreachable one must degrade, not fail the run.
        var capture = new CaptureVariablesModule();
        var services = BuildServiceProvider(capture, variableStore: new ThrowingVariableStore());
        var supervisor = Sys.ActorOf(WorkflowSupervisor.Props(services));

        var workflow = WorkflowWith(new VariableDefinition("greeting", PropertyType.String, Json("\"hello\"")));

        var status = await RunToCompletion(supervisor, workflow, HashMap<string, object?>.Empty);

        status.State.Should().Be(ExecutionState.Completed);
        capture.Seen["greeting"].Should().Be("hello");
    }

    [Fact]
    public async Task DeclaredWithoutValue_StartsAsNullRatherThanFailing()
    {
        var capture = new CaptureVariablesModule();
        var services = BuildServiceProvider(capture);
        var supervisor = Sys.ActorOf(WorkflowSupervisor.Props(services));

        var workflow = WorkflowWith(new VariableDefinition("orderId", PropertyType.String));

        var status = await RunToCompletion(supervisor, workflow, HashMap<string, object?>.Empty);

        status.State.Should().Be(ExecutionState.Completed);
        capture.Seen.Should().ContainKey("orderId");
        capture.Seen["orderId"].Should().BeNull();
    }

    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement.Clone();

    private IServiceProvider BuildServiceProvider(IWorkflowModule module, IVariableStore? variableStore = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<WorkflowValidator>();
        services.AddSingleton<IPersistenceProvider>(_provider);
        services.AddSingleton<IWorkflowRepository>(_provider.Workflows);
        services.AddSingleton<IExecutionHistoryRepository>(_provider.ExecutionHistory);
        services.AddSingleton<IVariableStore>(variableStore ?? _provider.Variables);

        var registry = new InMemoryModuleRegistry();
        registry.RegisterModule(module);
        services.AddSingleton<IModuleRegistry>(registry);

        return services.BuildServiceProvider();
    }

    private async Task<WorkflowStatusResponse> RunToCompletion(
        IActorRef supervisor,
        WorkflowDefinition workflow,
        HashMap<string, object?> inputs,
        ExecutionStartOptions? startOptions = null)
    {
        supervisor.Tell(new CreateWorkflowInstance(
            workflow.Id,
            workflow,
            inputs,
            startOptions ?? new ExecutionStartOptions("tester", VariableWriteMode.Execution)));
        var created = ExpectMsg<WorkflowInstanceCreated>(TimeSpan.FromSeconds(5));

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            supervisor.Tell(new GetWorkflowStatus(created.ExecutionId));
            var status = ExpectMsg<WorkflowStatusResponse>(TimeSpan.FromSeconds(5));
            if (status.State is ExecutionState.Completed or ExecutionState.Failed or ExecutionState.Cancelled)
            {
                return status;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("Execution did not reach a terminal state.");
    }

    private async Task WaitForStoredValue(VariableScope scope, string name, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await _provider.Variables.GetVariableAsync(scope, name) is not null)
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"Variable '{name}' never appeared in {scope.Kind} scope.");
    }

    private static WorkflowDefinition WorkflowWith(params VariableDefinition[] variables)
    {
        var node = new NodeDefinition(
            Id: "node_1",
            ModuleId: "test.capture",
            Name: "Capture",
            Properties: HashMap<string, JsonElement>.Empty);

        return new WorkflowDefinition(
            Id: Guid.NewGuid(),
            Name: "Variable seeding workflow",
            Description: null,
            Version: new Version(1, 0, 0),
            Nodes: Arr.create(node),
            Connections: Arr<ConnectionDefinition>.Empty,
            Variables: variables.Select(v => (v.Name, v)).ToHashMap(),
            Trigger: null,
            ErrorHandling: null,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: null,
            Tags: null);
    }

    /// <summary>Records the variables the engine handed to the node~ 🔍.</summary>
    private sealed class CaptureVariablesModule : IWorkflowModule
    {
        public ConcurrentDictionary<string, object?> Seen { get; } = new();

        public string ModuleId => "test.capture";

        public string DisplayName => "Capture variables";

        public string Category => "Testing";

        public string Description => "Records the execution's variables.";

        public string Icon => "🔍";

        public Version Version => new(1, 0, 0);

        public ModuleSchema Schema => ModuleSchema.Empty;

        public Task<ModuleResult> ExecuteAsync(ModuleExecutionContext context, CancellationToken cancellationToken = default)
        {
            foreach (var (name, value) in context.Variables)
            {
                this.Seen[name] = value;
            }

            return Task.FromResult(ModuleResult.Ok(new Dictionary<string, object?> { ["ok"] = true }));
        }
    }

    /// <summary>Writes one variable so the next run can read it back~ ✍️.</summary>
    private sealed class WriteVariableModule : IWorkflowModule
    {
        private readonly string name;
        private readonly object? value;

        public WriteVariableModule(string name, object? value)
        {
            this.name = name;
            this.value = value;
        }

        public string ModuleId => "test.capture";

        public string DisplayName => "Write variable";

        public string Category => "Testing";

        public string Description => "Publishes one variable update.";

        public string Icon => "✍️";

        public Version Version => new(1, 0, 0);

        public ModuleSchema Schema => ModuleSchema.Empty;

        public Task<ModuleResult> ExecuteAsync(ModuleExecutionContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(ModuleResult.Ok(
                new Dictionary<string, object?> { ["ok"] = true },
                new Dictionary<string, object?> { [this.name] = this.value }));
    }

    /// <summary>A store whose reads always fail, standing in for an unreachable backend~ 💥.</summary>
    private sealed class ThrowingVariableStore : IVariableStore
    {
        public Task SetVariableAsync(VariableScope scope, string name, object? value, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<VariableEntry?> GetVariableAsync(VariableScope scope, string name, int? version = null, CancellationToken ct = default)
            => throw new InvalidOperationException("store unavailable~ 💥");

        public Task<IReadOnlyList<VariableEntry>> GetVariableHistoryAsync(VariableScope scope, string name, CancellationToken ct = default)
            => throw new InvalidOperationException("store unavailable~ 💥");

        public Task<bool> DeleteVariableAsync(VariableScope scope, string name, CancellationToken ct = default)
            => throw new InvalidOperationException("store unavailable~ 💥");

        public Task<IReadOnlyDictionary<string, object?>> GetAllVariablesAsync(VariableScope scope, CancellationToken ct = default)
            => throw new InvalidOperationException("store unavailable~ 💥");
    }
}
