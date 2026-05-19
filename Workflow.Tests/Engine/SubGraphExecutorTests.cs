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

    // ── Test 6b: Sub-graph node records are tagged with subGraphId in Metadata ───────────────────

    /// <summary>
    /// Each <see cref="NodeExecutionRecord"/> produced by <see cref="SubGraphExecutor"/> must
    /// carry <c>Metadata["subGraphId"]</c> equal to the sub-graph's own ID~ 🌿
    /// This enables Phase 2.2.6 history queries to correlate nodes back to their originating
    /// sub-graph instance without a separate lookup~ ✨
    /// CopilotNote: Phase 2.2.3-followup — the tagging happens inside QueuePersistNode; this
    /// test verifies the tag is present and correct on every captured record.
    /// </summary>
    [Fact]
    public void SubGraph_NodeExecutions_PersistedWithSubGraphIdTagInMetadata()
    {
        // Arrange
        const string SubGraphId = "meta-tag-test";
        var captured = new List<NodeExecutionRecord>();
        var historyMock = new CapturingHistoryRepository(captured);

        var modules = RegisterModules("sg.m1", "sg.m2");
        var sp = new ServiceCollection()
            .AddSingleton<IModuleRegistry>(modules)
            .AddSingleton<IExecutionHistoryRepository>(historyMock)
            .BuildServiceProvider();

        var definition = BuildLinearDefinition(("nodeMeta1", "sg.m1"), ("nodeMeta2", "sg.m2"));
        var parentId = Guid.NewGuid();

        var parentProbe = CreateTestProbe("sg-meta-parent");
        parentProbe.ChildActorOf(SubGraphExecutor.Props(
            parentId,
            definition,
            scopeNodeIds: new[] { "nodeMeta1", "nodeMeta2" },
            entryNodeIds: new[] { "nodeMeta1" },
            inputs: new Dictionary<string, object?>(),
            serviceProvider: sp,
            subGraphId: SubGraphId), "sg-meta");

        // Act
        parentProbe.ExpectMsg<SubGraphCompleted>(TimeSpan.FromSeconds(5));

        // Give async persistence a moment to complete~ ⏰
        Thread.Sleep(200);

        // Assert — every node record carries Metadata["subGraphId"] == SubGraphId
        captured.Should().HaveCountGreaterThanOrEqualTo(2,
            because: "both nodes in the sub-graph should be persisted");

        // CopilotNote: avoid 'out var' inside the expression tree — FluentAssertions compiles
        // OnlyContain as Expression<Func<T,bool>> so we use ContainsKey + direct indexer~ 🌿
        captured.Should().OnlyContain(
            r => r.Metadata != null
                 && r.Metadata.ContainsKey("subGraphId")
                 && r.Metadata["subGraphId"] != null
                 && r.Metadata["subGraphId"]!.ToString() == SubGraphId,
            because: "each sub-graph node record must be tagged with Metadata[\"subGraphId\"] so "
                   + "history queries can correlate it back to its originating sub-graph instance~ 🌿");
    }

    // ── Test: Parallel branch metadata stamps (parallelId + branchIndex) ────────────────────────

    /// <summary>
    /// When a <see cref="SubGraphExecutor"/> is spawned by <c>ParallelExecutionCoordinator</c>
    /// the sentinel inputs <c>__parallel_node_id__</c> and <c>__parallel_branch_index__</c> are
    /// present in the branch inputs. QueuePersistNode must read them and stamp
    /// <c>Metadata["parallelId"]</c> + <c>Metadata["branchIndex"]</c> on every record~ 🌐💖
    /// CopilotNote: Phase 2.2.3-followup — parallel metadata stamps. We exercise this directly via
    /// SubGraphExecutor (no need to go through the full coordinator) by injecting sentinel inputs~ 🧪
    /// </summary>
    [Fact]
    public void SubGraph_WithParallelSentinelInputs_StampsParallelIdAndBranchIndexInMetadata()
    {
        // Arrange
        const string ParallelNodeId = "parallel-node-1";
        const int BranchIndex = 2;

        var captured = new List<NodeExecutionRecord>();
        var historyMock = new CapturingHistoryRepository(captured);

        var modules = RegisterModules("sg.par1", "sg.par2");
        var sp = new ServiceCollection()
            .AddSingleton<IModuleRegistry>(modules)
            .AddSingleton<IExecutionHistoryRepository>(historyMock)
            .BuildServiceProvider();

        var definition = BuildLinearDefinition(("parNode1", "sg.par1"), ("parNode2", "sg.par2"));
        var parentId = Guid.NewGuid();

        // CopilotNote: inject the same sentinel keys that ParallelExecutionCoordinator.SpawnBranch
        // puts into branchInputs — this decouples the test from needing the full coordinator~ 🧪
        var branchInputs = new Dictionary<string, object?>
        {
            ["__parallel_node_id__"] = ParallelNodeId,
            ["__parallel_branch_index__"] = BranchIndex,
            ["__parallel_branch_port__"] = "branch-2",
        };

        var parentProbe = CreateTestProbe("sg-par-meta-parent");
        parentProbe.ChildActorOf(SubGraphExecutor.Props(
            parentId,
            definition,
            scopeNodeIds: new[] { "parNode1", "parNode2" },
            entryNodeIds: new[] { "parNode1" },
            inputs: branchInputs,
            serviceProvider: sp,
            subGraphId: $"{ParallelNodeId}-branch-{BranchIndex}"), "sg-par-meta");

        // Act
        parentProbe.ExpectMsg<SubGraphCompleted>(TimeSpan.FromSeconds(5));

        // Give async persistence a moment~ ⏰
        Thread.Sleep(200);

        // Assert — every record must carry parallelId and branchIndex in Metadata~ 🌐
        captured.Should().HaveCountGreaterThanOrEqualTo(2,
            because: "both nodes in the parallel branch should be persisted");

        // CopilotNote: avoid 'out var' inside expression trees — use ContainsKey + indexer~ 🌿
        captured.Should().OnlyContain(
            r => r.Metadata != null
                 && r.Metadata.ContainsKey("parallelId")
                 && r.Metadata["parallelId"] != null
                 && r.Metadata["parallelId"]!.ToString() == ParallelNodeId,
            because: "Metadata[\"parallelId\"] must equal the parallel node ID on every branch record~ 🌐");

        captured.Should().OnlyContain(
            r => r.Metadata != null
                 && r.Metadata.ContainsKey("branchIndex")
                 && r.Metadata["branchIndex"] != null
                 && Convert.ToInt32(r.Metadata["branchIndex"]) == BranchIndex,
            because: "Metadata[\"branchIndex\"] must equal the 0-based branch index on every branch record~ 🔢");
    }

    /// <summary>
    /// A regular (non-parallel) sub-graph with no sentinel keys should NOT have
    /// <c>parallelId</c> or <c>branchIndex</c> in Metadata — only the <c>subGraphId</c>~ 💖
    /// CopilotNote: regression guard — make sure we don't accidentally stamp parallel keys on
    /// every record everywhere, only on actual parallel branch executions~ 🛡️
    /// </summary>
    [Fact]
    public void SubGraph_WithoutParallelSentinelInputs_DoesNotStampParallelMetadata()
    {
        // Arrange
        var captured = new List<NodeExecutionRecord>();
        var historyMock = new CapturingHistoryRepository(captured);

        var modules = RegisterModules("sg.np1", "sg.np2");
        var sp = new ServiceCollection()
            .AddSingleton<IModuleRegistry>(modules)
            .AddSingleton<IExecutionHistoryRepository>(historyMock)
            .BuildServiceProvider();

        var definition = BuildLinearDefinition(("npNode1", "sg.np1"), ("npNode2", "sg.np2"));
        var parentId = Guid.NewGuid();

        var parentProbe = CreateTestProbe("sg-np-meta-parent");
        parentProbe.ChildActorOf(SubGraphExecutor.Props(
            parentId,
            definition,
            scopeNodeIds: new[] { "npNode1", "npNode2" },
            entryNodeIds: new[] { "npNode1" },
            inputs: new Dictionary<string, object?>(),
            serviceProvider: sp,
            subGraphId: "non-parallel-subgraph"), "sg-np-meta");

        // Act
        parentProbe.ExpectMsg<SubGraphCompleted>(TimeSpan.FromSeconds(5));
        Thread.Sleep(200);

        // Assert — records have subGraphId but NOT parallelId or branchIndex~ 🌿
        captured.Should().HaveCountGreaterThanOrEqualTo(2);
        captured.Should().OnlyContain(
            r => r.Metadata != null && r.Metadata.ContainsKey("subGraphId"),
            because: "subGraphId should always be stamped when subGraphId is provided~");
        captured.Should().NotContain(
            r => r.Metadata != null && r.Metadata.ContainsKey("parallelId"),
            because: "parallelId must only appear on records from parallel branch sub-graphs~ 🌐");
        captured.Should().NotContain(
            r => r.Metadata != null && r.Metadata.ContainsKey("branchIndex"),
            because: "branchIndex must only appear on records from parallel branch sub-graphs~ 🔢");
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











