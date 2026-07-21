// <copyright file="ErrorBoundaryTests.cs" company="GlutenFree">
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
using Workflow.Engine.Models;
using Workflow.Modules;
using Workflow.Modules.Abstractions;
using Workflow.Persistence.Abstractions;
using Workflow.Persistence.Models;
using Xunit;
#pragma warning disable SA1204 // StaticElementsMustAppearBeforeInstanceElements

/// <summary>
/// Tests for Phase 2.2.0b Error Boundary infrastructure in <see cref="WorkflowExecutor"/>~ 🛡️✨
/// Validates that PushErrorBoundary/PopErrorBoundary messages correctly maintain the
/// <c>_boundaryStack</c>, that node failures within an active boundary are caught + routed
/// to the <see cref="ErrorBoundary.CatchEntryNodeId"/> instead of failing the parent execution,
/// and that no double terminal-write happens for history (regression guard for 2.1.5 follow-up)~ 💖.
/// </summary>
public class ErrorBoundaryTests : TestKit
{
    // ── Test 1: Boundary catches failure — parent stays Running, routes to catch node ─────────────

    /// <summary>
    /// When a node fails inside an active error boundary, the workflow does NOT fail.
    /// Instead it routes to the boundary's CatchEntryNodeId and eventually completes~ 🛡️→✅
    /// </summary>
    [Fact]
    public void ErrorBoundary_CatchesNodeFailure_RoutesToCatchNode_WorkflowCompletes()
    {
        // Arrange
        // Workflow:  failNode ──[output]──► catchNode
        // - failNode: always fails at runtime
        // - catchNode: simple pass-through (acts as catch handler)
        // - Error boundary: BoundaryId="b1", CatchEntryNodeId="catchNode"
        var registry = new InMemoryModuleRegistry(skipValidation: true);
        registry.RegisterModule(new StubFailingModule("mod.fail"));
        registry.RegisterModule(new StubPassthroughModule("mod.catch"));

        var sp = new ServiceCollection()
            .AddSingleton<IModuleRegistry>(registry)
            .BuildServiceProvider();

        var definition = new WorkflowDefinition(
            Id: Guid.NewGuid(),
            Name: "boundary-catch-test",
            Description: null,
            Version: new Version(1, 0),
            Nodes: new[]
            {
                new NodeDefinition("failNode",  "mod.fail",  "Fail",  HashMap<string, System.Text.Json.JsonElement>.Empty),
                new NodeDefinition("catchNode", "mod.catch", "Catch", HashMap<string, System.Text.Json.JsonElement>.Empty),
            }.ToArr(),
            Connections: new[]
            {
                // Connection exists so catchNode's predecessor list = [failNode].
                // TryHandleWithBoundary marks failNode "completed" then fires catchNode~ 🔗
                new ConnectionDefinition("failNode", "output", "catchNode", "input"),
            }.ToArr(),
            Variables: HashMap<string, VariableDefinition>.Empty);

        var executionId = Guid.NewGuid();
        var parentProbe = CreateTestProbe("boundary-catch-parent");
        var executor = parentProbe.ChildActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), sp),
            "boundary-catch-exec");

        // Act — push boundary BEFORE starting so it's active when failNode runs
        executor.Tell(new PushErrorBoundary(new ErrorBoundary(
            BoundaryId: "b1",
            CatchEntryNodeId: "catchNode")));
        executor.Tell(new StartExecution(executionId));

        // Assert — workflow should COMPLETE (boundary caught the failure)
        // CopilotNote: FishForMessage skips ExecutionStateChanged etc. to find terminal~ 🐟
        var result = parentProbe.FishForMessage(
            m => m is WorkflowCompleted or WorkflowFailed,
            TimeSpan.FromSeconds(5));

        result.Should().BeOfType<WorkflowCompleted>(
            because: "the error boundary should catch failNode's failure and route to catchNode, " +
                     "allowing the workflow to complete instead of fail");
    }

    // ── Test 2: No boundary — workflow still fails normally ──────────────────────────────────────

    /// <summary>
    /// Without an active boundary, a failing node causes the workflow to fail. This is a
    /// regression guard ensuring boundary logic is purely additive~ ❌→❌ (expected)
    /// </summary>
    [Fact]
    public void ErrorBoundary_NoBoundary_WorkflowFails_NormalPath()
    {
        var registry = new InMemoryModuleRegistry(skipValidation: true);
        registry.RegisterModule(new StubFailingModule("mod.fail2"));

        var sp = new ServiceCollection()
            .AddSingleton<IModuleRegistry>(registry)
            .BuildServiceProvider();

        var failNode = new NodeDefinition("failNode", "mod.fail2", "Fail",
            HashMap<string, System.Text.Json.JsonElement>.Empty);
        var definition = new WorkflowDefinition(
            Id: Guid.NewGuid(), Name: "no-boundary-test", Description: null,
            Version: new Version(1, 0),
            Nodes: new[] { failNode }.ToArr(),
            Connections: Arr<ConnectionDefinition>.Empty,
            Variables: HashMap<string, VariableDefinition>.Empty);

        var executionId = Guid.NewGuid();
        var parentProbe = CreateTestProbe("no-boundary-parent");
        var executor = parentProbe.ChildActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), sp),
            "no-boundary-exec");

        // Act — NO PushErrorBoundary, just start
        executor.Tell(new StartExecution(executionId));

        var result = parentProbe.FishForMessage(
            m => m is WorkflowCompleted or WorkflowFailed,
            TimeSpan.FromSeconds(5));

        result.Should().BeOfType<WorkflowFailed>(
            because: "without a boundary the failing node should propagate to workflow failure");
    }

    // ── Test 3: Boundary with finally — both catch AND finally nodes fire on failure ─────────────

    /// <summary>
    /// When an error boundary specifies both a CatchEntryNodeId and a FinallyEntryNodeId,
    /// both are fired when a failure is caught: catch handles the error, finally always runs~ 🛡️✨
    /// </summary>
    [Fact]
    public void ErrorBoundary_CatchWithFinally_BothNodesFireOnFailure()
    {
        // Arrange
        // failNode ──[output]──► catchNode
        // failNode ──[output]──► finallyNode  (two connections from same source port)
        var captured = new List<NodeExecutionRecord>();
        var histRepo = new CapturingHistoryRepository(captured);

        var registry = new InMemoryModuleRegistry(skipValidation: true);
        registry.RegisterModule(new StubFailingModule("mod.fail3"));
        registry.RegisterModule(new StubPassthroughModule("mod.catch3"));
        registry.RegisterModule(new StubPassthroughModule("mod.finally3"));

        var sp = new ServiceCollection()
            .AddSingleton<IModuleRegistry>(registry)
            .AddSingleton<IExecutionHistoryRepository>(histRepo)
            .BuildServiceProvider();

        var definition = new WorkflowDefinition(
            Id: Guid.NewGuid(), Name: "boundary-finally-test", Description: null,
            Version: new Version(1, 0),
            Nodes: new[]
            {
                new NodeDefinition("failNode",    "mod.fail3",    "Fail",    HashMap<string, System.Text.Json.JsonElement>.Empty),
                new NodeDefinition("catchNode",   "mod.catch3",   "Catch",   HashMap<string, System.Text.Json.JsonElement>.Empty),
                new NodeDefinition("finallyNode", "mod.finally3", "Finally", HashMap<string, System.Text.Json.JsonElement>.Empty),
            }.ToArr(),
            Connections: new[]
            {
                new ConnectionDefinition("failNode", "output", "catchNode",   "input"),
                new ConnectionDefinition("failNode", "output", "finallyNode", "input"),
            }.ToArr(),
            Variables: HashMap<string, VariableDefinition>.Empty);

        var executionId = Guid.NewGuid();
        var parentProbe = CreateTestProbe("boundary-finally-parent");
        var executor = parentProbe.ChildActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), sp),
            "boundary-finally-exec");

        // Act
        executor.Tell(new PushErrorBoundary(new ErrorBoundary(
            BoundaryId: "b-finally",
            CatchEntryNodeId: "catchNode",
            FinallyEntryNodeId: "finallyNode")));
        executor.Tell(new StartExecution(executionId));

        // Assert — workflow completes (both catch + finally run)
        var result = parentProbe.FishForMessage(
            m => m is WorkflowCompleted or WorkflowFailed,
            TimeSpan.FromSeconds(5));

        result.Should().BeOfType<WorkflowCompleted>(
            because: "boundary catches failure and routes to both catch and finally nodes");

        Thread.Sleep(200);

        // Both catch & finally nodes should have been executed
        var completedNodeIds = captured
            .Where(r => r.State == NodeExecutionState.Completed)
            .Select(r => r.NodeId)
            .ToList();
        completedNodeIds.Should().Contain("catchNode",   "catch handler should have run");
        completedNodeIds.Should().Contain("finallyNode", "finally handler should always run");
    }

    // ── Test 4: Boundary-handled failure does NOT double-write terminal status to history ─────────

    /// <summary>
    /// When a boundary catches a failure, <c>UpdateExecutionStatusAsync(Failed)</c> must NOT
    /// be called for the parent execution — only the node record (state=Failed) + catch node
    /// records should appear. Regression guard for the open 2.1.5 double-write follow-up~ 🛡️
    /// </summary>
    [Fact]
    public void ErrorBoundary_CaughtFailure_DoesNotCallUpdateStatusFailed()
    {
        // Arrange — use a tracking history repository that records UpdateExecutionStatus calls
        var statusUpdates = new List<(Guid id, ExecutionState state)>();
        var trackingRepo = new TrackingHistoryRepository(statusUpdates);

        var registry = new InMemoryModuleRegistry(skipValidation: true);
        registry.RegisterModule(new StubFailingModule("mod.fail4"));
        registry.RegisterModule(new StubPassthroughModule("mod.catch4"));

        var sp = new ServiceCollection()
            .AddSingleton<IModuleRegistry>(registry)
            .AddSingleton<IExecutionHistoryRepository>(trackingRepo)
            .BuildServiceProvider();

        var executionId = Guid.NewGuid();
        var definition = new WorkflowDefinition(
            Id: Guid.NewGuid(), Name: "no-double-write-test", Description: null,
            Version: new Version(1, 0),
            Nodes: new[]
            {
                new NodeDefinition("failNode",  "mod.fail4",  "Fail",  HashMap<string, System.Text.Json.JsonElement>.Empty),
                new NodeDefinition("catchNode", "mod.catch4", "Catch", HashMap<string, System.Text.Json.JsonElement>.Empty),
            }.ToArr(),
            Connections: new[]
            {
                new ConnectionDefinition("failNode", "output", "catchNode", "input"),
            }.ToArr(),
            Variables: HashMap<string, VariableDefinition>.Empty);

        var parentProbe = CreateTestProbe("no-double-write-parent");
        var executor = parentProbe.ChildActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), sp),
            "no-double-write-exec");

        // Act
        executor.Tell(new PushErrorBoundary(new ErrorBoundary(
            BoundaryId: "b-no-double",
            CatchEntryNodeId: "catchNode")));
        executor.Tell(new StartExecution(executionId));

        parentProbe.FishForMessage(m => m is WorkflowCompleted or WorkflowFailed, TimeSpan.FromSeconds(5));
        Thread.Sleep(200);

        // Assert — no UpdateExecutionStatusAsync(Failed) should have been called for the parent
        var failedUpdates = statusUpdates
            .Where(u => u.id == executionId && u.state == ExecutionState.Failed)
            .ToList();

        failedUpdates.Should().BeEmpty(
            because: "the boundary swallowed the failure — " +
                     "UpdateExecutionStatus(Failed) should not be called for the parent execution");
    }

    // ── Test 5: Nested boundaries — inner catches before outer ───────────────────────────────────

    /// <summary>
    /// With two nested boundaries, the innermost (most recently pushed) catches the failure first.
    /// The outer boundary never routes to its catch node, so the workflow completes via the
    /// inner catch handler only~ 🛡️🛡️
    /// </summary>
    [Fact]
    public void ErrorBoundary_Nested_InnerCatchesBeforeOuter()
    {
        var captured = new List<NodeExecutionRecord>();
        var histRepo = new CapturingHistoryRepository(captured);

        var registry = new InMemoryModuleRegistry(skipValidation: true);
        registry.RegisterModule(new StubFailingModule("mod.inner.fail"));
        registry.RegisterModule(new StubPassthroughModule("mod.inner.catch"));

        var sp = new ServiceCollection()
            .AddSingleton<IModuleRegistry>(registry)
            .AddSingleton<IExecutionHistoryRepository>(histRepo)
            .BuildServiceProvider();

        var definition = new WorkflowDefinition(
            Id: Guid.NewGuid(), Name: "nested-boundary-test", Description: null,
            Version: new Version(1, 0),
            Nodes: new[]
            {
                new NodeDefinition("failNode",       "mod.inner.fail",  "Fail",       HashMap<string, System.Text.Json.JsonElement>.Empty),
                new NodeDefinition("innerCatchNode", "mod.inner.catch", "InnerCatch", HashMap<string, System.Text.Json.JsonElement>.Empty),
                // CopilotNote: There is no outerCatchNode in the workflow graph because
                // a "catch" node with no predecessors would be treated as a start node.
                // The outer boundary references a *conceptual* nodeId that does NOT exist
                // in the definition — the inner boundary fires first (TryHandleWithBoundary
                // walks innermost first and returns true immediately), so the outer
                // boundary's CatchEntryNodeId is never reached~ 🛡️
            }.ToArr(),
            Connections: new[]
            {
                new ConnectionDefinition("failNode", "output", "innerCatchNode", "input"),
            }.ToArr(),
            Variables: HashMap<string, VariableDefinition>.Empty);

        var executionId = Guid.NewGuid();
        var parentProbe = CreateTestProbe("nested-boundary-parent");
        var executor = parentProbe.ChildActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), sp),
            "nested-boundary-exec");

        // Act — push outer boundary first, then inner (inner is on top of stack)
        // The inner boundary's CatchEntryNodeId DOES exist in the graph.
        // The outer boundary's CatchEntryNodeId ("phantomCatchNode") does NOT exist — but
        // it should never be reached because the inner boundary fires first~ 🛡️
        executor.Tell(new PushErrorBoundary(new ErrorBoundary(
            BoundaryId: "outerBoundary",
            CatchEntryNodeId: "phantomCatchNode")));
        executor.Tell(new PushErrorBoundary(new ErrorBoundary(
            BoundaryId: "innerBoundary",
            CatchEntryNodeId: "innerCatchNode")));
        executor.Tell(new StartExecution(executionId));

        var result = parentProbe.FishForMessage(
            m => m is WorkflowCompleted or WorkflowFailed, TimeSpan.FromSeconds(5));

        result.Should().BeOfType<WorkflowCompleted>(
            because: "inner boundary should catch the failure and route to innerCatchNode, " +
                     "completing the workflow without the outer boundary ever being triggered");

        Thread.Sleep(200);

        // innerCatchNode should have run
        var nodeIds = captured.Select(r => r.NodeId).ToList();
        nodeIds.Should().Contain("innerCatchNode", "inner boundary caught the failure and routed here");
    }

    // ── Stubs & Helpers ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Module that always fails at runtime but declares schema outputs properly for
    /// port validation to pass at workflow load time~ 💥
    /// </summary>
    private sealed class StubFailingModule : IWorkflowModule
    {
        public StubFailingModule(string moduleId) => ModuleId = moduleId;
        public string ModuleId { get; }
        public string DisplayName => ModuleId;
        public string Category => "Test";
        public string Description => "Always fails";
        public string Icon => "💥";
        public Version Version => new(1, 0);

        public ModuleSchema Schema => new(
            Inputs: Arr<PortDefinition>.Empty,
            // CopilotNote: "output" must be declared so ValidateConnectionPorts (Phase 2.2.0a) passes~ 🛡️
            Outputs: new[] { PortDefinition.Create<object>("output", isRequired: false) }.ToArr(),
            Properties: Arr<ModulePropertyDefinition>.Empty);

        public Task<ModuleResult> ExecuteAsync(ModuleExecutionContext ctx, CancellationToken ct = default)
            => Task.FromResult(ModuleResult.Fail(
                "Intentional failure for error boundary test",
                new InvalidOperationException("Intentional failure for error boundary test")));
    }

    /// <summary>Simple pass-through module~ 🌿</summary>
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
            => Task.FromResult(ModuleResult.Ok(new Dictionary<string, object?> { ["output"] = $"{ctx.NodeId}-ok" }));
    }

    /// <summary>Thread-safe capturing history repository~ 💾</summary>
    private sealed class CapturingHistoryRepository : IExecutionHistoryRepository
    {
        private readonly List<NodeExecutionRecord> _records;
        public CapturingHistoryRepository(List<NodeExecutionRecord> records) => _records = records;

        public Task<Guid> CreateExecutionAsync(ExecutionRecord record, CancellationToken ct = default)
            => Task.FromResult(record.ExecutionId);

        public Task RecordNodeExecutionAsync(NodeExecutionRecord nodeRecord, CancellationToken ct = default)
        {
            lock (_records)
            {
                _records.Add(nodeRecord);
            }

            return Task.CompletedTask;
        }

        public Task UpdateExecutionStatusAsync(Guid id, ExecutionState state,
            DateTimeOffset? endTime = null, string? error = null, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<NodeExecutionRecord>> GetNodeExecutionsAsync(Guid id,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<NodeExecutionRecord>>(new List<NodeExecutionRecord>());

        public Task<ExecutionRecord?> GetExecutionAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult<ExecutionRecord?>(null);

        public Task<PagedResult<ExecutionRecord>> GetExecutionsForWorkflowAsync(
            Guid workflowId, ExecutionFilter filter, Pagination pagination, CancellationToken ct = default)
            => Task.FromResult(new PagedResult<ExecutionRecord>(new List<ExecutionRecord>(), 0, 1, 20));
    }

    /// <summary>History repository that tracks UpdateExecutionStatus calls~ 📊</summary>
    private sealed class TrackingHistoryRepository : IExecutionHistoryRepository
    {
        private readonly List<(Guid, ExecutionState)> _updates;
        public TrackingHistoryRepository(List<(Guid, ExecutionState)> updates) => _updates = updates;

        public Task<Guid> CreateExecutionAsync(ExecutionRecord record, CancellationToken ct = default)
            => Task.FromResult(record.ExecutionId);

        public Task RecordNodeExecutionAsync(NodeExecutionRecord nodeRecord, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task UpdateExecutionStatusAsync(Guid id, ExecutionState state,
            DateTimeOffset? endTime = null, string? error = null, CancellationToken ct = default)
        {
            lock (_updates)
            {
                _updates.Add((id, state));
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<NodeExecutionRecord>> GetNodeExecutionsAsync(Guid id,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<NodeExecutionRecord>>(new List<NodeExecutionRecord>());

        public Task<ExecutionRecord?> GetExecutionAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult<ExecutionRecord?>(null);

        public Task<PagedResult<ExecutionRecord>> GetExecutionsForWorkflowAsync(
            Guid workflowId, ExecutionFilter filter, Pagination pagination, CancellationToken ct = default)
            => Task.FromResult(new PagedResult<ExecutionRecord>(new List<ExecutionRecord>(), 0, 1, 20));
    }
}




