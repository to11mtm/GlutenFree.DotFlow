// <copyright file="NestedConstructHistoryTests.cs" company="GlutenFree">
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
using Microsoft.Extensions.DependencyInjection;
using Workflow.Core.Abstractions;
using Workflow.Core.Models;
using Workflow.Engine.Actors;
using Workflow.Engine.Messages;
using Workflow.Engine.Models;
using Workflow.Modules;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin.Flow;
using Workflow.Persistence.Abstractions;
using Workflow.Persistence.Models;
using Xunit;

/// <summary>
/// §12.10 follow-up: history records written for body nodes carry the scope path of the constructs
/// they ran inside (<c>Metadata["scopePath"]</c>, outermost first) and the innermost loop step as
/// <c>LoopId</c> / <c>LoopIteration</c>, so a leaf in a loop-in-loop can be told apart per outer
/// iteration. <c>Metadata["subGraphId"]</c> keeps its old format.
/// </summary>
public class NestedConstructHistoryTests : TestKit
{
    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(15);

    /// <summary>foreach [x,y,z] → body: leaf. Each leaf row names the loop and its 1-based iteration.</summary>
    [Fact]
    public void LoopBodyRows_CarryLoopIdAndIteration()
    {
        var definition = Workflow(
            new[] { Foreach("loop", "x", "y", "z"), Node("leaf", "h.leaf") },
            new[] { Edge("loop", "loopBody", "leaf") });

        var leafRows = this.RunAndCollect(definition, "leaf", 3, new ForEachModule(), Pass("h.leaf"));

        leafRows.Should().OnlyContain(r => r.LoopId == "loop");
        leafRows.Select(r => r.LoopIteration).Should().BeEquivalentTo(new int?[] { 1, 2, 3 });
        leafRows.Select(PathOf).Should().BeEquivalentTo("loop#1", "loop#2", "loop#3");
        leafRows.Select(r => r.Metadata!["subGraphId"]).Should().BeEquivalentTo(
            new object[] { "loop-iter-1", "loop-iter-2", "loop-iter-3" },
            because: "the subGraphId format is unchanged");
    }

    /// <summary>
    /// foreach outer [1,2,3] → body: foreach inner [a,b] → body: leaf. The 6 leaf rows cover every
    /// (outer, inner) iteration pair; LoopId / LoopIteration name the innermost loop.
    /// </summary>
    [Fact]
    public void LoopInLoopLeafRows_CarryBothIterations()
    {
        var definition = Workflow(
            new[] { Foreach("outer", 1, 2, 3), Foreach("inner", "a", "b"), Node("leaf", "h.leaf") },
            new[] { Edge("outer", "loopBody", "inner"), Edge("inner", "loopBody", "leaf") });

        var leafRows = this.RunAndCollect(definition, "leaf", 6, new ForEachModule(), Pass("h.leaf"));

        leafRows.Select(PathOf).Should().BeEquivalentTo(
            "outer#1/inner#1", "outer#1/inner#2",
            "outer#2/inner#1", "outer#2/inner#2",
            "outer#3/inner#1", "outer#3/inner#2");
        leafRows.Should().OnlyContain(r => r.LoopId == "inner");
        leafRows.Should().OnlyContain(r => r.LoopIteration == ScopePath(r)[1].Iteration);
    }

    /// <summary>
    /// foreach [1,2,3] → body: trycatch (try: failing node, catch: handler). Catch-body rows carry
    /// loop#k / tc:catch; the loop is still the innermost loop.
    /// </summary>
    [Fact]
    public void TryCatchInLoop_CatchRowsCarryLoopIterationAndCatchBranch()
    {
        var definition = Workflow(
            new[]
            {
                Foreach("loop", 1, 2, 3),
                Node("tc", "builtin.trycatch"),
                Node("boom", "h.boom"),
                Node("handler", "h.handler"),
            },
            new[]
            {
                Edge("loop", "loopBody", "tc"),
                Edge("tc", "try", "boom"),
                Edge("tc", "catch", "handler"),
            });

        var boom = new StubModule("h.boom", () => ModuleResult.Fail("intentional", new InvalidOperationException("intentional")));
        var handlerRows = this.RunAndCollect(definition, "handler", 3, new ForEachModule(), new TryCatchModule(), boom, Pass("h.handler"));

        handlerRows.Select(PathOf).Should().BeEquivalentTo("loop#1/tc:catch", "loop#2/tc:catch", "loop#3/tc:catch");
        handlerRows.Should().OnlyContain(r => r.LoopId == "loop");
        handlerRows.Select(r => r.LoopIteration).Should().BeEquivalentTo(new int?[] { 1, 2, 3 });
    }

    /// <summary>
    /// foreach [1,2] → body: parallel (b0 → left, b1 → right). Branch rows carry loop#k / par[i].
    /// </summary>
    [Fact]
    public void ParallelInLoop_BranchRowsCarryLoopIterationAndBranchIndex()
    {
        var definition = Workflow(
            new[]
            {
                Foreach("loop", 1, 2),
                Node("par", "builtin.parallel", ("branches", new[] { "b0", "b1" })),
                Node("left", "h.left"),
                Node("right", "h.right"),
            },
            new[]
            {
                Edge("loop", "loopBody", "par"),
                Edge("par", "b0", "left"),
                Edge("par", "b1", "right"),
            });

        var rows = this.RunAndCollect(
            definition, new[] { "left", "right" }, 4,
            new ForEachModule(), new ParallelModule(), Pass("h.left"), Pass("h.right"));

        rows.Where(r => r.NodeId == "left").Select(PathOf).Should().BeEquivalentTo("loop#1/par[0]", "loop#2/par[0]");
        rows.Where(r => r.NodeId == "right").Select(PathOf).Should().BeEquivalentTo("loop#1/par[1]", "loop#2/par[1]");
        rows.Should().OnlyContain(r => r.LoopId == "loop" && r.LoopIteration == ScopePath(r)[0].Iteration);
    }

    private static IReadOnlyList<ScopeStep> ScopePath(NodeExecutionRecord record)
        => record.Metadata is not null && record.Metadata.TryGetValue("scopePath", out var path) && path is IEnumerable<ScopeStep> steps
            ? steps.ToList()
            : Array.Empty<ScopeStep>();

    private static string PathOf(NodeExecutionRecord record) => string.Join("/", ScopePath(record));

    private List<NodeExecutionRecord> RunAndCollect(
        WorkflowDefinition definition, string nodeId, int expectedRows, params IWorkflowModule[] modules)
        => this.RunAndCollect(definition, new[] { nodeId }, expectedRows, modules);

    /// <summary>
    /// Runs the workflow to completion, then waits for the body rows of <paramref name="nodeIds"/>:
    /// body records are written fire-and-forget, so they can land after <see cref="WorkflowCompleted"/>.
    /// </summary>
    private List<NodeExecutionRecord> RunAndCollect(
        WorkflowDefinition definition, string[] nodeIds, int expectedRows, params IWorkflowModule[] modules)
    {
        var registry = new InMemoryModuleRegistry(skipValidation: true);
        foreach (var module in modules)
        {
            registry.RegisterModule(module);
        }

        var records = new List<NodeExecutionRecord>();
        var services = new ServiceCollection()
            .AddSingleton<IModuleRegistry>(registry)
            .AddSingleton<IExecutionHistoryRepository>(new CapturingHistoryRepository(records));

        var executionId = Guid.NewGuid();
        var parent = this.CreateTestProbe();
        var executor = parent.ChildActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), services.BuildServiceProvider()));
        executor.Tell(new StartExecution(executionId));
        parent.FishForMessage(m => m is WorkflowCompleted or WorkflowFailed, RunTimeout)
            .Should().BeOfType<WorkflowCompleted>();

        List<NodeExecutionRecord> Matching()
        {
            lock (records)
            {
                return records.Where(r => nodeIds.Contains(r.NodeId) && r.State == NodeExecutionState.Completed).ToList();
            }
        }

        this.AwaitCondition(() => Matching().Count >= expectedRows, RunTimeout);
        var rows = Matching();
        rows.Should().HaveCount(expectedRows);
        return rows;
    }

    private static StubModule Pass(string moduleId)
        => new(moduleId, () => ModuleResult.Ok(new Dictionary<string, object?> { ["output"] = "ok" }));

    private static NodeDefinition Foreach(string id, params object?[] items)
        => Node(id, "builtin.loop.foreach", ("collection", items));

    private static NodeDefinition Node(string id, string moduleId, params (string Key, object? Value)[] properties)
    {
        var props = HashMap<string, JsonElement>.Empty;
        foreach (var (key, value) in properties)
        {
            props = props.AddOrUpdate(key, JsonSerializer.SerializeToElement(value));
        }

        return new NodeDefinition(id, moduleId, id, props);
    }

    private static ConnectionDefinition Edge(string from, string port, string to) => new(from, port, to, "input");

    private static WorkflowDefinition Workflow(NodeDefinition[] nodes, ConnectionDefinition[] connections)
        => new(
            Id: Guid.NewGuid(),
            Name: "nested-construct-history-test",
            Description: null,
            Version: new Version(1, 0),
            Nodes: nodes.ToArr(),
            Connections: connections.ToArr(),
            Variables: HashMap<string, VariableDefinition>.Empty);

    /// <summary>Returns a fixed result per call.</summary>
    private sealed class StubModule : IWorkflowModule
    {
        private readonly Func<ModuleResult> result;

        public StubModule(string moduleId, Func<ModuleResult> result)
        {
            this.ModuleId = moduleId;
            this.result = result;
        }

        public string ModuleId { get; }

        public string DisplayName => this.ModuleId;

        public string Category => "Test";

        public string Description => "Stub test module";

        public string Icon => "📜";

        public Version Version => new(1, 0);

        public ModuleSchema Schema => new(
            Inputs: new[] { PortDefinition.Create<object>("input", isRequired: false) }.ToArr(),
            Outputs: new[] { PortDefinition.Create<object>("output", isRequired: false) }.ToArr(),
            Properties: Arr<ModulePropertyDefinition>.Empty);

        public Task<ModuleResult> ExecuteAsync(ModuleExecutionContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(this.result());
    }

    /// <summary>Thread-safe capturing history repository.</summary>
    private sealed class CapturingHistoryRepository : IExecutionHistoryRepository
    {
        private readonly List<NodeExecutionRecord> records;

        public CapturingHistoryRepository(List<NodeExecutionRecord> records) => this.records = records;

        public Task<Guid> CreateExecutionAsync(ExecutionRecord record, CancellationToken ct = default)
            => Task.FromResult(record.ExecutionId);

        public Task RecordNodeExecutionAsync(NodeExecutionRecord nodeRecord, CancellationToken ct = default)
        {
            lock (this.records)
            {
                this.records.Add(nodeRecord);
            }

            return Task.CompletedTask;
        }

        public Task UpdateExecutionStatusAsync(
            Guid executionId, ExecutionState state, DateTimeOffset? endTime = null, string? error = null, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<NodeExecutionRecord>> GetNodeExecutionsAsync(Guid executionId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<NodeExecutionRecord>>(new List<NodeExecutionRecord>());

        public Task<ExecutionRecord?> GetExecutionAsync(Guid executionId, CancellationToken ct = default)
            => Task.FromResult<ExecutionRecord?>(null);

        public Task<PagedResult<ExecutionRecord>> GetExecutionsForWorkflowAsync(
            Guid workflowId, ExecutionFilter filter, Pagination pagination, CancellationToken ct = default)
            => Task.FromResult(new PagedResult<ExecutionRecord>(new List<ExecutionRecord>(), 0, pagination.Page, pagination.PageSize));
    }
}
