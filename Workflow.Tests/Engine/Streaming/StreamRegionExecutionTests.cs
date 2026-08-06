// ────────────────────────────────────────────────────────────────────────────────
// Copyright © GlutenFree 2025. Made with love by Ami-Chan! UwU 💖
// Licensed under the MIT License.
// ────────────────────────────────────────────────────────────────────────────────

using System.Runtime.CompilerServices;
using System.Text.Json;
using Akka.Actor;
using Akka.TestKit;
using Akka.TestKit.Xunit2;
using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Core.Models;
using Workflow.Engine.Actors;
using Workflow.Engine.Messages;
using Workflow.Modules;
using Workflow.Modules.Abstractions;

namespace Workflow.Tests.Engine.Streaming;

/// <summary>
/// Phase 5.1.3 — a streaming region reached through the <b>real</b> <see cref="WorkflowExecutor"/>:
/// dispatch enters the region at its source, the whole pipeline runs as a unit, and ordinary port
/// dispatch resumes from the terminal node's outputs~ 🌊⚙️
/// </summary>
public class StreamRegionExecutionTests : TestKit
{
    #region Fixture

    private static NodeDefinition Node(string id, string moduleId)
        => new(id, moduleId, id, HashMap<string, JsonElement>.Empty);

    private static IServiceProvider Services(params IWorkflowModule[] modules)
    {
        var registry = new InMemoryModuleRegistry(skipValidation: true);
        foreach (var module in modules)
        {
            registry.RegisterModule(module);
        }

        return new ServiceCollection()
            .AddSingleton<IModuleRegistry>(registry)
            .BuildServiceProvider();
    }

    private static WorkflowDefinition Definition(
        IEnumerable<NodeDefinition> nodes,
        IEnumerable<ConnectionDefinition> connections)
        => new(
            Guid.NewGuid(),
            "streaming-wf",
            null,
            new Version(1, 0),
            new Arr<NodeDefinition>(nodes),
            new Arr<ConnectionDefinition>(connections),
            HashMap<string, VariableDefinition>.Empty);

    private IActorRef StartExecutor(
        WorkflowDefinition definition,
        IServiceProvider services,
        Guid executionId)
        => Sys.ActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), services),
            $"exec-{Guid.NewGuid():N}");

    /// <summary>Polls until the execution reaches a terminal state, then returns it~ ⏳.</summary>
    private ExecutionState AwaitTerminalState(IActorRef executor, Guid executionId)
    {
        ExecutionState state = ExecutionState.Pending;

        AwaitAssert(
            () =>
            {
                executor.Tell(new GetWorkflowStatus(executionId));
                var status = ExpectMsg<WorkflowStatusResponse>(TimeSpan.FromSeconds(3));
                state = status.State;
                state.Should().BeOneOf(ExecutionState.Completed, ExecutionState.Failed);
            },
            TimeSpan.FromSeconds(15),
            TimeSpan.FromMilliseconds(250));

        return state;
    }

    #endregion

    [Fact]
    public void Region_RunsAsOneUnit_AndFeedsDownstreamBatchNode()
    {
        // src(stream) → collect(terminal) → after(batch)
        var definition = Definition(
            [Node("src", "test.stream.source"), Node("collect", "test.stream.collect"), Node("after", "test.batch")],
            [
                new ConnectionDefinition("src", "items", "collect", "items"),
                new ConnectionDefinition("collect", "count", "after", "input"),
            ]);

        var services = Services(new FakeSource(4), new FakeCollector(), new FakeBatch());
        var executionId = Guid.NewGuid();

        var executor = StartExecutor(definition, services, executionId);
        executor.Tell(new StartExecution(executionId));

        AwaitTerminalState(executor, executionId).Should().Be(ExecutionState.Completed);
    }

    [Fact]
    public void Region_MembersAllReachCompleted()
    {
        var definition = Definition(
            [Node("src", "test.stream.source"), Node("map", "test.stream.map"), Node("collect", "test.stream.collect")],
            [
                new ConnectionDefinition("src", "items", "map", "items"),
                new ConnectionDefinition("map", "items", "collect", "items"),
            ]);

        var services = Services(new FakeSource(3), new FakeMap(), new FakeCollector());
        var executionId = Guid.NewGuid();

        var executor = StartExecutor(definition, services, executionId);
        executor.Tell(new StartExecution(executionId));

        AwaitTerminalState(executor, executionId).Should().Be(ExecutionState.Completed);

        executor.Tell(new GetWorkflowStatus(executionId));
        var status = ExpectMsg<WorkflowStatusResponse>(TimeSpan.FromSeconds(3));
        status.NodeStates.Values.Should().AllSatisfy(
            s => s.Should().Be(NodeExecutionState.Completed),
            "every stage in the region completes together");
    }

    [Fact]
    public void Region_StageFailure_FailsTheExecution()
    {
        var definition = Definition(
            [Node("src", "test.stream.source"), Node("boom", "test.stream.boom"), Node("collect", "test.stream.collect")],
            [
                new ConnectionDefinition("src", "items", "boom", "items"),
                new ConnectionDefinition("boom", "items", "collect", "items"),
            ]);

        var services = Services(new FakeSource(5), new FakeThrowingStage(), new FakeCollector());
        var executionId = Guid.NewGuid();

        var executor = StartExecutor(definition, services, executionId);
        executor.Tell(new StartExecution(executionId));

        AwaitTerminalState(executor, executionId).Should().Be(ExecutionState.Failed);
    }

    [Fact]
    public void NonStreamingWorkflow_IsUnaffected()
    {
        // 🛡️ Regression guard: region detection must be invisible to every existing workflow.
        var definition = Definition(
            [Node("a", "test.batch"), Node("b", "test.batch")],
            [new ConnectionDefinition("a", "output", "b", "input")]);

        var services = Services(new FakeBatch());
        var executionId = Guid.NewGuid();

        var executor = StartExecutor(definition, services, executionId);
        executor.Tell(new StartExecution(executionId));

        AwaitTerminalState(executor, executionId).Should().Be(ExecutionState.Completed);
    }

    [Fact]
    public void Region_PublishesPerStageItemCounts()
    {
        var definition = Definition(
            [Node("src", "test.stream.source"), Node("collect", "test.stream.collect")],
            [new ConnectionDefinition("src", "items", "collect", "items")]);

        var services = Services(new FakeSource(6), new FakeCollector());
        var executionId = Guid.NewGuid();

        // 📡 Subscribe before starting so the progress event can't be missed~
        Sys.EventStream.Subscribe(TestActor, typeof(StreamRegionProgress));

        var executor = StartExecutor(definition, services, executionId);
        executor.Tell(new StartExecution(executionId));

        var progress = ExpectMsg<StreamRegionProgress>(TimeSpan.FromSeconds(15));
        progress.ExecutionId.Should().Be(executionId);
        progress.ItemCounts["src"].Should().Be(6);
        progress.ItemCounts["collect"].Should().Be(6);
        progress.Duration.Should().BeGreaterThan(TimeSpan.Zero);
    }

    /// <summary>
    /// 🧯 5.1.4 — an item skipped by `onItemError: skip` survives as a per-item error document on
    /// the stage's `errors` output, so a designer can wire it to a dead-letter path~ 🧾
    /// </summary>
    [Fact]
    public void SkippedItems_SurfaceOnTheStagesErrorsOutput()
    {
        var skipper = Node("skip", "test.stream.skipper") with
        {
            Properties = LanguageExt.HashMap<string, JsonElement>.Empty
                .Add("onItemError", JsonDocument.Parse("\"skip\"").RootElement.Clone()),
        };

        var definition = Definition(
            [Node("src", "test.stream.source"), skipper, Node("collect", "test.stream.collect")],
            [
                new ConnectionDefinition("src", "items", "skip", "items"),
                new ConnectionDefinition("skip", "items", "collect", "items"),
            ]);

        var services = Services(new FakeSource(5), new FakeSkipper(failAt: 2), new FakeCollector());
        var executionId = Guid.NewGuid();

        var executor = StartExecutor(definition, services, executionId);
        executor.Tell(new StartExecution(executionId));

        AwaitTerminalState(executor, executionId).Should().Be(
            ExecutionState.Completed,
            "one bad item must not fail a run configured to skip");

        executor.Tell(new GetExecutionSnapshot(executionId));
        var snapshot = ExpectMsg<ExecutionSnapshotResponse>(TimeSpan.FromSeconds(5));
        snapshot.ExecutionId.Should().Be(executionId);
    }

    #region Fake modules

    private abstract class FakeStreamModule : IStreamingWorkflowModule
    {
        public abstract string ModuleId { get; }

        public string DisplayName => ModuleId;

        public string Category => "Test";

        public string Description => "test streaming module";

        public string Icon => "🌊";

        public Version Version => new(1, 0);

        public virtual ModuleSchema Schema => new(
            Arr.create(PortDefinition.CreateStreaming("items", isRequired: false)),
            Arr.create(PortDefinition.CreateStreaming("items", isRequired: false)),
            Arr<ModulePropertyDefinition>.Empty);

        public Task<ModuleResult> ExecuteAsync(ModuleExecutionContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(ModuleResult.Fail("streaming only"));

        public abstract IAsyncEnumerable<StreamItem> ExecuteStreamAsync(
            ModuleExecutionContext context,
            IAsyncEnumerable<StreamItem> input,
            CancellationToken cancellationToken = default);
    }

    private sealed class FakeSource : FakeStreamModule
    {
        private readonly int count;

        public FakeSource(int count) => this.count = count;

        public override string ModuleId => "test.stream.source";

        public override ModuleSchema Schema => new(
            Arr<PortDefinition>.Empty,
            Arr.create(PortDefinition.CreateStreaming("items", isRequired: false)),
            Arr<ModulePropertyDefinition>.Empty);

        public override async IAsyncEnumerable<StreamItem> ExecuteStreamAsync(
            ModuleExecutionContext context,
            IAsyncEnumerable<StreamItem> input,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            for (var i = 0; i < this.count; i++)
            {
                await Task.Yield();
                yield return StreamItem.FromJson(JsonDocument.Parse($$"""{"n":{{i}}}""").RootElement.Clone(), i);
            }
        }
    }

    private sealed class FakeMap : FakeStreamModule
    {
        public override string ModuleId => "test.stream.map";

        public override async IAsyncEnumerable<StreamItem> ExecuteStreamAsync(
            ModuleExecutionContext context,
            IAsyncEnumerable<StreamItem> input,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var item in input.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                yield return item;
            }
        }
    }

    /// <summary>A per-item stage that throws on one specific item, so `skip` has something to skip~ 🧯.</summary>
    private sealed class FakeSkipper : FakeStreamModule, IStreamItemProcessor
    {
        private readonly int failAt;

        public FakeSkipper(int failAt) => this.failAt = failAt;

        public override string ModuleId => "test.stream.skipper";

        public override IAsyncEnumerable<StreamItem> ExecuteStreamAsync(
            ModuleExecutionContext context,
            IAsyncEnumerable<StreamItem> input,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("per-item stage");

        public ValueTask<IReadOnlyList<StreamItem>> ProcessAsync(
            StreamItem item,
            ModuleExecutionContext context,
            CancellationToken cancellationToken = default)
            => item.Index == this.failAt
                ? throw new InvalidOperationException($"bad item {this.failAt}")
                : ValueTask.FromResult<IReadOnlyList<StreamItem>>(new[] { item });
    }

    private sealed class FakeThrowingStage : FakeStreamModule
    {
        public override string ModuleId => "test.stream.boom";

        public override async IAsyncEnumerable<StreamItem> ExecuteStreamAsync(
            ModuleExecutionContext context,
            IAsyncEnumerable<StreamItem> input,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var item in input.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                if (item.Index == 2)
                {
                    throw new InvalidOperationException("stage exploded");
                }

                yield return item;
            }
        }
    }

    private sealed class FakeCollector : FakeStreamModule, IStreamTerminalModule    {
        public override string ModuleId => "test.stream.collect";

        public override ModuleSchema Schema => new(
            Arr.create(PortDefinition.CreateStreaming("items", isRequired: false)),
            Arr.create(PortDefinition.Create<long>("count", isRequired: false)),
            Arr<ModulePropertyDefinition>.Empty);

        public override IAsyncEnumerable<StreamItem> ExecuteStreamAsync(
            ModuleExecutionContext context,
            IAsyncEnumerable<StreamItem> input,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("terminal");

        public async Task<ModuleResult> ExecuteTerminalAsync(
            ModuleExecutionContext context,
            IAsyncEnumerable<StreamItem> input,
            CancellationToken cancellationToken = default)
        {
            var count = 0L;
            await foreach (var _ in input.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                count++;
            }

            return ModuleResult.Ok(new Dictionary<string, object?> { ["count"] = count });
        }
    }

    private sealed class FakeBatch : IWorkflowModule
    {
        public string ModuleId => "test.batch";

        public string DisplayName => "Batch";

        public string Category => "Test";

        public string Description => "ordinary batch node";

        public string Icon => "📦";

        public Version Version => new(1, 0);

        public ModuleSchema Schema => new(
            Arr.create(PortDefinition.Create<object>("input", isRequired: false)),
            Arr.create(PortDefinition.Create<object>("output", isRequired: false)),
            Arr<ModulePropertyDefinition>.Empty);

        public Task<ModuleResult> ExecuteAsync(ModuleExecutionContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(ModuleResult.Ok(new Dictionary<string, object?>
            {
                ["output"] = context.Inputs.TryGetValue("input", out var v) ? v : null,
            }));
    }

    #endregion
}
