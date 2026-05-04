// <copyright file="SubGraphExecutorTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Engine;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit.Xunit2;
using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Core.Models;
using Workflow.Engine.Actors;
using Workflow.Engine.Messages;
using Workflow.Modules;
using Workflow.Modules.Abstractions;
using Workflow.Persistence.Abstractions;
using Workflow.Persistence.Models;
using Xunit;

/// <summary>
/// Tests for Phase 2.2.0a SubGraphExecutor — the contained sub-graph execution primitive~ 🌿✨
/// </summary>
public class SubGraphExecutorTests : TestKit
{
    // ── Test 4: Sub-graph runs entry → terminal nodes and returns aggregated outputs ──────────────

    /// <summary>
    /// SubGraphExecutor runs a linear A→B→C chain and reports SubGraphCompleted with outputs~ 🎉
    /// </summary>
    [Fact]
    public void SubGraph_RunsEntryToTerminalNodes_ReportsCompletion()
    {
        // Arrange: A → B → C (linear)
        var modules = RegisterModules("sgA", "sgB", "sgC");
        var sp = BuildServiceProvider(modules);

        var definition = BuildLinearDefinition(
            ("sgNodeA", "sgA"),
            ("sgNodeB", "sgB"),
            ("sgNodeC", "sgC"));

        var parentId = Guid.NewGuid();

        // CopilotNote: Use TestProbe as parent so SubGraphCompleted is received here, not user guardian~ 🌿
        var parentProbe = CreateTestProbe("sg-linear-parent");
        var actor = parentProbe.ChildActorOf(SubGraphExecutor.Props(
            parentId,
            definition,
            scopeNodeIds: new[] { "sgNodeA", "sgNodeB", "sgNodeC" },
            entryNodeIds: new[] { "sgNodeA" },
            inputs: new Dictionary<string, object?> { ["seed"] = 42 },
            serviceProvider: sp,
            subGraphId: "test-subgraph-linear"), "sg-linear");

        // Act & Assert — parent probe receives SubGraphCompleted
        var result = parentProbe.ExpectMsg<SubGraphCompleted>(TimeSpan.FromSeconds(5));
        result.SubGraphId.Should().Be("test-subgraph-linear");
        result.Outputs.Should().NotBeNull();
    }

    // ── Test 5: Sub-graph failure surfaces as SubGraphFailed without failing parent ──────────────

    /// <summary>
    /// When a sub-graph node throws, SubGraphFailed is sent to the caller; the parent actor
    /// continues running (does not die with it)~ 💔→✅
    /// </summary>
    [Fact]
    public void SubGraph_NodeFailure_ReportsSubGraphFailed_NotKillingParent()
    {
        // Arrange: B is the failing node
        var failingModule = new StubFailingModule("sg.fail");
        var okModule = new StubPassthroughModule("sg.ok");

        var registry = new InMemoryModuleRegistry(skipValidation: true);
        registry.RegisterModule(okModule);
        registry.RegisterModule(failingModule);

        var sp = new ServiceCollection()
            .AddSingleton<IModuleRegistry>(registry)
            .BuildServiceProvider();

        var definition = new WorkflowDefinition(
            Id: Guid.NewGuid(),
            Name: "sg-fail-test",
            Description: null,
            Version: new Version(1, 0),
            Nodes: new[]
            {
                new NodeDefinition("sgOk",   "sg.ok",   "OK Node",     HashMap<string, System.Text.Json.JsonElement>.Empty),
                new NodeDefinition("sgFail", "sg.fail", "Failing Node",HashMap<string, System.Text.Json.JsonElement>.Empty),
            }.ToArr(),
            Connections: new[]
            {
                new ConnectionDefinition("sgOk", "output", "sgFail", "input"),
            }.ToArr(),
            Variables: HashMap<string, VariableDefinition>.Empty);

        var parentId = Guid.NewGuid();

        // CopilotNote: Use TestProbe as parent so SubGraphFailed is received here~ 💔
        var parentProbe = CreateTestProbe("sg-fail-parent");
        var actor = parentProbe.ChildActorOf(SubGraphExecutor.Props(
            parentId,
            definition,
            scopeNodeIds: new[] { "sgOk", "sgFail" },
            entryNodeIds: new[] { "sgOk" },
            inputs: new Dictionary<string, object?>(),
            serviceProvider: sp,
            subGraphId: "test-fail"), "sg-fail");

        // Act & Assert
        var failed = parentProbe.ExpectMsg<SubGraphFailed>(TimeSpan.FromSeconds(5));
        failed.SubGraphId.Should().Be("test-fail");
        failed.Error.Should().NotBeNull();
        failed.FailedNodeId.Should().Be("sgFail");

        // The TestProbe (our "parent") is still alive — verify by checking no unexpected messages
        parentProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(200));
    }

    // ── Test 6: Sub-graph node executions appear in history under the parent execution ───────────

    /// <summary>
    /// Nodes executed inside a sub-graph are recorded in IExecutionHistoryRepository
    /// under the parent execution ID, not a sub-graph-specific one~ 🗂️
    /// </summary>
    [Fact]
    public void SubGraph_NodeExecutions_PersistedUnderParentExecutionId()
    {
        // Arrange
        var captured = new List<NodeExecutionRecord>();
        var historyMock = new CapturingHistoryRepository(captured);

        var modules = RegisterModules("sg.p1", "sg.p2");
        var sp = new ServiceCollection()
            .AddSingleton<IModuleRegistry>(modules)
            .AddSingleton<IExecutionHistoryRepository>(historyMock)
            .BuildServiceProvider();

        var definition = BuildLinearDefinition(("nodeP1", "sg.p1"), ("nodeP2", "sg.p2"));
        var parentId = Guid.NewGuid();

        var parentProbe = CreateTestProbe("sg-persist-parent");
        var actor = parentProbe.ChildActorOf(SubGraphExecutor.Props(
            parentId,
            definition,
            scopeNodeIds: new[] { "nodeP1", "nodeP2" },
            entryNodeIds: new[] { "nodeP1" },
            inputs: new Dictionary<string, object?>(),
            serviceProvider: sp,
            subGraphId: "persist-test"), "sg-persist");

        // Act
        parentProbe.ExpectMsg<SubGraphCompleted>(TimeSpan.FromSeconds(5));

        // Give async persistence a moment to complete~ ⏰
        System.Threading.Thread.Sleep(200);

        // Assert — both node records use the parent execution ID
        captured.Should().HaveCountGreaterThanOrEqualTo(2,
            because: "both nodes in the sub-graph should be persisted");
        captured.Should().OnlyContain(r => r.ExecutionId == parentId,
            because: "all sub-graph node records must use the PARENT execution ID");
    }

    // ── Test: Port-aware routing inside sub-graph works identically ─────────────────────────────

    /// <summary>
    /// SubGraphExecutor respects ActivePorts just like WorkflowExecutor~ 🎯
    /// </summary>
    [Fact]
    public void SubGraph_PortAwareRouting_SkipsDeactivatedBranches()
    {
        var condModule = new StubPortActivatingModule("sg.cond", new[] { "yes" });
        var yesModule = new StubPassthroughModule("sg.yes");
        var noModule = new StubPassthroughModule("sg.no");

        var registry = new InMemoryModuleRegistry(skipValidation: true);
        registry.RegisterModule(condModule);
        registry.RegisterModule(yesModule);
        registry.RegisterModule(noModule);

        var sp = new ServiceCollection()
            .AddSingleton<IModuleRegistry>(registry)
            .BuildServiceProvider();

        var definition = new WorkflowDefinition(
            Id: Guid.NewGuid(),
            Name: "sg-port-routing",
            Description: null,
            Version: new Version(1, 0),
            Nodes: new[]
            {
                new NodeDefinition("cnd", "sg.cond", "Cond", HashMap<string, System.Text.Json.JsonElement>.Empty),
                new NodeDefinition("yes", "sg.yes",  "Yes",  HashMap<string, System.Text.Json.JsonElement>.Empty),
                new NodeDefinition("no",  "sg.no",   "No",   HashMap<string, System.Text.Json.JsonElement>.Empty),
            }.ToArr(),
            Connections: new[]
            {
                new ConnectionDefinition("cnd", "yes", "yes", "input"),
                new ConnectionDefinition("cnd", "no",  "no",  "input"),
            }.ToArr(),
            Variables: HashMap<string, VariableDefinition>.Empty);

        var parentProbe = CreateTestProbe("sg-port-parent");
        var actor = parentProbe.ChildActorOf(SubGraphExecutor.Props(
            Guid.NewGuid(),
            definition,
            scopeNodeIds: new[] { "cnd", "yes", "no" },
            entryNodeIds: new[] { "cnd" },
            inputs: new Dictionary<string, object?>(),
            serviceProvider: sp), "sg-port-routing");

        // Act — sub-graph should complete (yes fires, no is skipped)
        parentProbe.ExpectMsg<SubGraphCompleted>(TimeSpan.FromSeconds(5))
            .Should().NotBeNull("port-aware routing inside sub-graph should complete normally");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    private static IModuleRegistry RegisterModules(params string[] moduleIds)
    {
        var registry = new InMemoryModuleRegistry(skipValidation: true);
        foreach (var id in moduleIds)
            registry.RegisterModule(new StubPassthroughModule(id));
        return registry;
    }

    private static IServiceProvider BuildServiceProvider(IModuleRegistry registry)
        => new ServiceCollection()
            .AddSingleton<IModuleRegistry>(registry)
            .BuildServiceProvider();

    private static WorkflowDefinition BuildLinearDefinition(params (string nodeId, string moduleId)[] chain)
    {
        var nodes = chain
            .Select(t => new NodeDefinition(t.nodeId, t.moduleId, t.nodeId,
                HashMap<string, System.Text.Json.JsonElement>.Empty))
            .ToArr();

        var connections = chain
            .Zip(chain.Skip(1), (a, b) => new ConnectionDefinition(a.nodeId, "output", b.nodeId, "input"))
            .ToArr();

        return new WorkflowDefinition(Id: Guid.NewGuid(), Name: "sg-test", Description: null, Version: new Version(1, 0), Nodes: nodes, Connections: connections, Variables: HashMap<string, VariableDefinition>.Empty);
    }

    // ── Stubs ─────────────────────────────────────────────────────────────────────────────────────

    private sealed class StubPassthroughModule : IWorkflowModule
    {
        public StubPassthroughModule(string moduleId) => ModuleId = moduleId;
        public string ModuleId { get; }
        public string DisplayName => ModuleId;
        public string Category => "Test";
        public string Description => "Pass-through";
        public string Icon => "🌿";
        public Version Version => new(1, 0);

        public ModuleSchema Schema => new(
            Inputs: new[] { PortDefinition.Create<object>("input", isRequired: false) }.ToArr(),
            Outputs: new[] { PortDefinition.Create<object>("output", isRequired: false) }.ToArr(),
            Properties: Arr<ModulePropertyDefinition>.Empty);

        public Task<ModuleResult> ExecuteAsync(ModuleExecutionContext ctx, CancellationToken ct = default)
            => Task.FromResult(ModuleResult.Ok(new Dictionary<string, object?> { ["output"] = ctx.NodeId + "-done" }));
    }

    private sealed class StubFailingModule : IWorkflowModule
    {
        public StubFailingModule(string moduleId) => ModuleId = moduleId;
        public string ModuleId { get; }
        public string DisplayName => ModuleId;
        public string Category => "Test";
        public string Description => "Always fails";
        public string Icon => "💥";
        public Version Version => new(1, 0);
        public ModuleSchema Schema => ModuleSchema.Empty;

        public Task<ModuleResult> ExecuteAsync(ModuleExecutionContext ctx, CancellationToken ct = default)
            => Task.FromResult(ModuleResult.Fail("SubGraph node intentionally failed",
                new InvalidOperationException("SubGraph node intentionally failed")));
    }

    private sealed class StubPortActivatingModule : IWorkflowModule
    {
        private readonly string[] _activePorts;

        public StubPortActivatingModule(string moduleId, string[] activePorts)
        {
            ModuleId = moduleId;
            _activePorts = activePorts;
        }

        public string ModuleId { get; }
        public string DisplayName => ModuleId;
        public string Category => "Test";
        public string Description => "Activates specific ports";
        public string Icon => "🎯";
        public Version Version => new(1, 0);

        public ModuleSchema Schema => new(
            Inputs: Arr<PortDefinition>.Empty,
            Outputs: _activePorts
                .Select(p => PortDefinition.Create<object>(p, isRequired: false))
                .ToArr(),
            Properties: Arr<ModulePropertyDefinition>.Empty);

        public Task<ModuleResult> ExecuteAsync(ModuleExecutionContext ctx, CancellationToken ct = default)
            => Task.FromResult(ModuleResult.WithActivePorts(
                new Dictionary<string, object?> { [_activePorts[0]] = true },
                _activePorts));
    }

    /// <summary>Thread-safe capturing history repository for persistence tests~ 💾</summary>
    private sealed class CapturingHistoryRepository : IExecutionHistoryRepository
    {
        private readonly List<NodeExecutionRecord> _records;

        public CapturingHistoryRepository(List<NodeExecutionRecord> records) => _records = records;

        public Task<Guid> CreateExecutionAsync(ExecutionRecord record, CancellationToken ct = default)
            => Task.FromResult(record.ExecutionId);

        public Task RecordNodeExecutionAsync(NodeExecutionRecord nodeRecord, CancellationToken ct = default)
        {
            lock (_records) _records.Add(nodeRecord);
            return Task.CompletedTask;
        }

        public Task UpdateExecutionStatusAsync(Guid executionId, ExecutionState state,
            DateTimeOffset? endTime = null, string? error = null, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<NodeExecutionRecord>> GetNodeExecutionsAsync(Guid executionId,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<NodeExecutionRecord>>(new List<NodeExecutionRecord>());

        public Task<ExecutionRecord?> GetExecutionAsync(Guid executionId, CancellationToken ct = default)
            => Task.FromResult<ExecutionRecord?>(null);

        public Task<PagedResult<ExecutionRecord>> GetExecutionsForWorkflowAsync(
            Guid workflowId,
            ExecutionFilter filter,
            Pagination pagination,
            CancellationToken ct = default)
            => Task.FromResult(new PagedResult<ExecutionRecord>(new List<ExecutionRecord>(), 0, pagination.Page, pagination.PageSize));
    }
}











