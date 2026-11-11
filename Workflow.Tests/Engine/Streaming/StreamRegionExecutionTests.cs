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
        => Services(null, modules);

    private static IServiceProvider TimeoutServices(StreamingTimeoutOptions timeouts, params IWorkflowModule[] modules)
    {
        var registry = new InMemoryModuleRegistry(skipValidation: true);
        foreach (var module in modules)
        {
            registry.RegisterModule(module);
        }

        return new ServiceCollection()
            .AddSingleton<IModuleRegistry>(registry)
            .AddSingleton(timeouts)
            .AddSingleton(new StreamingProgressOptions(0))
            .BuildServiceProvider();
    }

    /// <summary>Runs a definition to its end and returns WorkflowCompleted or WorkflowFailed.</summary>
    private object RunToEnd(WorkflowDefinition definition, IServiceProvider services)
    {
        var executionId = Guid.NewGuid();
        var parent = CreateTestProbe();
        var executor = parent.ChildActorOf(WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), services));
        executor.Tell(new StartExecution(executionId));
        return parent.FishForMessage(m => m is WorkflowCompleted or WorkflowFailed, TimeSpan.FromSeconds(15));
    }

    private static IServiceProvider Services(StreamingProgressOptions? progress, params IWorkflowModule[] modules)
    {
        var registry = new InMemoryModuleRegistry(skipValidation: true);
        foreach (var module in modules)
        {
            registry.RegisterModule(module);
        }

        var services = new ServiceCollection().AddSingleton<IModuleRegistry>(registry);
        if (progress is not null)
        {
            services.AddSingleton(progress);
        }

        return services.BuildServiceProvider();
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

        var progress = FishForMessage<StreamRegionProgress>(p => p.Completed, TimeSpan.FromSeconds(15));
        progress.ExecutionId.Should().Be(executionId);
        progress.ItemCounts["src"].Should().Be(6);
        progress.ItemCounts["collect"].Should().Be(6);
        progress.Duration.Should().BeGreaterThan(TimeSpan.Zero);
    }

    /// <summary>
    /// 📡 Stage 7.6 — while a region runs it publishes live counts (Completed = false) every
    /// interval, only when they changed; then the final summary (Completed = true) with the totals.
    /// </summary>
    [Fact]
    public void Region_PublishesLiveCounts_WhileItRuns_ThenTheFinalSummary()
    {
        var definition = Definition(
            [Node("src", "test.stream.source"), Node("collect", "test.stream.collect")],
            [new ConnectionDefinition("src", "items", "collect", "items")]);

        var services = Services(new StreamingProgressOptions(25), new FakeSource(8, TimeSpan.FromMilliseconds(150)), new FakeCollector());
        var executionId = Guid.NewGuid();
        Sys.EventStream.Subscribe(TestActor, typeof(StreamRegionProgress));

        var executor = StartExecutor(definition, services, executionId);
        executor.Tell(new StartExecution(executionId));

        var live = new List<StreamRegionProgress>();
        StreamRegionProgress final;
        while (true)
        {
            var p = ExpectMsg<StreamRegionProgress>(TimeSpan.FromSeconds(15));
            p.ExecutionId.Should().Be(executionId);
            if (p.Completed)
            {
                final = p;
                break;
            }

            live.Add(p);
        }

        live.Should().NotBeEmpty(because: "a region running ~1.2 s with a 25 ms interval reports while it runs");
        live.Select(p => p.ItemCounts["src"]).Should().BeInAscendingOrder().And.OnlyContain(n => n < 8);
        Enumerable.Zip(live, live.Skip(1)).Should().OnlyContain(
            pair => pair.Item1.ItemCounts["src"] != pair.Item2.ItemCounts["src"] || pair.Item1.ItemCounts["collect"] != pair.Item2.ItemCounts["collect"],
            because: "unchanged counts aren't re-sent");
        live.Select(p => p.Duration).Should().BeInAscendingOrder();
        final.ItemCounts["src"].Should().Be(8);
        final.ItemCounts["collect"].Should().Be(8);
        ExpectNoMsg(TimeSpan.FromMilliseconds(200)); // the timer stops with the region~
    }

    /// <summary>📡 Stage 7.6 — <c>Streaming:ProgressIntervalMs</c> = 0 means the final summary only.</summary>
    [Fact]
    public void Region_WithIntervalZero_PublishesOnlyTheFinalSummary()
    {
        var definition = Definition(
            [Node("src", "test.stream.source"), Node("collect", "test.stream.collect")],
            [new ConnectionDefinition("src", "items", "collect", "items")]);

        var services = Services(new StreamingProgressOptions(0), new FakeSource(10, TimeSpan.FromMilliseconds(40)), new FakeCollector());
        var executionId = Guid.NewGuid();
        Sys.EventStream.Subscribe(TestActor, typeof(StreamRegionProgress));

        var executor = StartExecutor(definition, services, executionId);
        executor.Tell(new StartExecution(executionId));

        var first = ExpectMsg<StreamRegionProgress>(TimeSpan.FromSeconds(15));
        first.Completed.Should().BeTrue();
        first.ItemCounts["src"].Should().Be(10);
    }

    /// <summary>
    /// 📡 Follow-up — every stage of a finished region reports its completion (clients get
    /// NodeCompleted), at the top level and inside a loop body.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryStageOfAFinishedRegion_ReportsCompletion(bool insideALoop)
    {
        var probe = CreateTestProbe();
        Sys.EventStream.Subscribe(probe, typeof(NodeExecutionCompleted));
        var definition = insideALoop
            ? Definition(
                [Foreach("loop", 1), Node("src", "test.stream.source"), Node("collect", "test.stream.collect")],
                [new ConnectionDefinition("loop", "loopBody", "src", "items"), new ConnectionDefinition("src", "items", "collect", "items")])
            : SourceToCollect(Node("src", "test.stream.source"));

        this.RunToEnd(definition, Services(new Workflow.Modules.Builtin.Flow.ForEachModule(), new FakeSource(3), new FakeCollector()))
            .Should().BeOfType<WorkflowCompleted>();

        var completed = probe.ReceiveWhile<object>(TimeSpan.FromMilliseconds(500), m => m).OfType<NodeExecutionCompleted>().Select(m => m.NodeId).ToList();
        completed.Should().Contain("src").And.Contain("collect");
    }

    // ── Stage 7.5 (§9.2): region timeouts ────────────────────────────────────────────────────────

    private static WorkflowDefinition SourceToCollect(NodeDefinition source)
        => Definition([source, Node("collect", "test.stream.collect")], [new ConnectionDefinition("src", "items", "collect", "items")]);

    private static NodeDefinition SourceWith(params (string Key, string Value)[] metadata)
        => Node("src", "test.stream.source") with
        {
            Metadata = metadata.Aggregate(HashMap<string, string>.Empty, (m, kv) => m.AddOrUpdate(kv.Key, kv.Value)),
        };

    /// <summary>⏱️ Stage 7.5 — a region where nothing moves for the stall limit fails, saying how far it got.</summary>
    [Fact]
    public void AStalledRegion_FailsWithATimeout_ThatNamesTheCounts()
    {
        var services = TimeoutServices(new StreamingTimeoutOptions(StallTimeoutMs: 300), new FakeHangingSource(3, hangRuns: int.MaxValue), new FakeCollector());

        var terminal = this.RunToEnd(SourceToCollect(Node("src", "test.stream.source")), services);

        var error = terminal.Should().BeOfType<WorkflowFailed>().Which.Error;
        error.Should().BeOfType<TimeoutException>();
        error.Message.Should().Contain("no item moved through any stage").And.Contain("src 3");
    }

    /// <summary>⏱️ Stage 7.5 — slow but moving isn't a stall.</summary>
    [Fact]
    public void ASlowButMovingRegion_IsNotStalled()
    {
        var services = TimeoutServices(new StreamingTimeoutOptions(StallTimeoutMs: 400), new FakeSource(8, TimeSpan.FromMilliseconds(120)), new FakeCollector());

        this.RunToEnd(SourceToCollect(Node("src", "test.stream.source")), services).Should().BeOfType<WorkflowCompleted>();
    }

    /// <summary>⏱️ Stage 7.5 — the overall limit stops a region that keeps moving but runs too long.</summary>
    [Fact]
    public void TheOverallLimit_StopsALongRegion()
    {
        var services = TimeoutServices(new StreamingTimeoutOptions(TotalTimeoutMs: 400), new FakeSource(40, TimeSpan.FromMilliseconds(50)), new FakeCollector());

        var terminal = this.RunToEnd(SourceToCollect(Node("src", "test.stream.source")), services);

        terminal.Should().BeOfType<WorkflowFailed>().Which.Error.Message.Should().Contain("overall limit");
    }

    /// <summary>⏱️ Stage 7.5 — the source node's metadata overrides the server: 0 turns a limit off for that region.</summary>
    [Fact]
    public void ASourceNodeOverride_OfZero_TurnsTheServerLimitOff()
    {
        var services = TimeoutServices(new StreamingTimeoutOptions(StallTimeoutMs: 150), new FakeSource(4, TimeSpan.FromMilliseconds(400)), new FakeCollector());

        var terminal = this.RunToEnd(SourceToCollect(SourceWith((StreamingTimeoutOptions.StallMetadataKey, "0"))), services);

        terminal.Should().BeOfType<WorkflowCompleted>();
    }

    /// <summary>⏱️ Stage 7.5 — with the server's limits off (the default), a region can still set its own.</summary>
    [Fact]
    public void ASourceNodeOverride_SetsALimit_WhenTheServerHasNone()
    {
        var services = TimeoutServices(new StreamingTimeoutOptions(), new FakeHangingSource(2, hangRuns: int.MaxValue), new FakeCollector());

        var terminal = this.RunToEnd(SourceToCollect(SourceWith((StreamingTimeoutOptions.StallMetadataKey, "300"))), services);

        terminal.Should().BeOfType<WorkflowFailed>().Which.Error.Should().BeOfType<TimeoutException>();
    }

    /// <summary>⏱️ Stage 7.5 — a timeout is a region failure, so the source node's retry re-runs the region.</summary>
    [Fact]
    public void ATimedOutRegion_IsRetried_LikeAnyRegionFailure()
    {
        var source = new FakeHangingSource(2, hangRuns: 1);
        var services = TimeoutServices(new StreamingTimeoutOptions(StallTimeoutMs: 300), source, new FakeCollector());
        var node = Node("src", "test.stream.source") with
        {
            ErrorHandling = new ErrorHandling(ErrorBehavior.Retry),
            RetryPolicy = new RetryPolicy(MaxAttempts: 2, DelayMs: 10, BackoffMultiplier: 1.0, MaxDelayMs: 50),
        };

        var terminal = this.RunToEnd(SourceToCollect(node), services);

        terminal.Should().BeOfType<WorkflowCompleted>();
        source.Runs.Should().Be(2);
    }

    // ── Stage 7.7 (§9.5): regions inside construct bodies ─────────────────────────────────────────

    private static NodeDefinition Foreach(string id, int count)
        => new(
            id,
            "builtin.loop.foreach",
            id,
            HashMap<string, JsonElement>.Empty.Add("collection", JsonSerializer.SerializeToElement(Enumerable.Range(1, count).ToArray())));

    /// <summary>
    /// 🔁 Stage 7.7 — a streaming chain inside a loop body runs as one pipeline in every iteration
    /// (like at the top level), and the body carries on from the terminal stage's outputs.
    /// </summary>
    [Fact]
    public void Region_InsideALoopBody_RunsAsAPipeline_InEveryIteration()
    {
        var recorder = new FakeRecorder();
        var definition = Definition(
            [Foreach("loop", 3), Node("src", "test.stream.source"), Node("collect", "test.stream.collect"), Node("rec", "test.record")],
            [
                new ConnectionDefinition("loop", "loopBody", "src", "items"),
                new ConnectionDefinition("src", "items", "collect", "items"),
                new ConnectionDefinition("collect", "count", "rec", "input"),
            ]);

        var services = Services(new StreamingProgressOptions(0), new Workflow.Modules.Builtin.Flow.ForEachModule(), new FakeSource(5), new FakeCollector(), recorder);
        var executionId = Guid.NewGuid();
        Sys.EventStream.Subscribe(TestActor, typeof(StreamRegionProgress));

        var executor = StartExecutor(definition, services, executionId);
        executor.Tell(new StartExecution(executionId));

        for (var i = 0; i < 3; i++)
        {
            var progress = FishForMessage<StreamRegionProgress>(p => p.ExecutionId == executionId && p.Completed, TimeSpan.FromSeconds(15));
            progress.ItemCounts["collect"].Should().Be(5);
        }

        AwaitTerminalState(executor, executionId).Should().Be(ExecutionState.Completed);
        recorder.Seen.Should().Equal(new object?[] { 5L, 5L, 5L }, because: "each iteration's region hands its terminal output to the next body node");
    }

    /// <summary>
    /// 🌐 Stage 7.7 — each parallel branch can hold its own region; both run as pipelines at once.
    /// </summary>
    [Fact]
    public void Regions_InParallelBranches_EachRunAsAPipeline()
    {
        var recorder = new FakeRecorder();
        var definition = Definition(
            [
                new NodeDefinition("par", "builtin.parallel", "par", HashMap<string, JsonElement>.Empty.Add("branches", JsonSerializer.SerializeToElement(new[] { "b0", "b1" }))),
                Node("srcA", "test.stream.source"), Node("collectA", "test.stream.collect"), Node("recA", "test.record"),
                Node("srcB", "test.stream.source"), Node("collectB", "test.stream.collect"), Node("recB", "test.record"),
            ],
            [
                new ConnectionDefinition("par", "b0", "srcA", "items"),
                new ConnectionDefinition("srcA", "items", "collectA", "items"),
                new ConnectionDefinition("collectA", "count", "recA", "input"),
                new ConnectionDefinition("par", "b1", "srcB", "items"),
                new ConnectionDefinition("srcB", "items", "collectB", "items"),
                new ConnectionDefinition("collectB", "count", "recB", "input"),
            ]);

        var services = Services(new Workflow.Modules.Builtin.Flow.ParallelModule(), new FakeSource(6), new FakeCollector(), recorder);
        var executionId = Guid.NewGuid();

        var executor = StartExecutor(definition, services, executionId);
        executor.Tell(new StartExecution(executionId));

        AwaitTerminalState(executor, executionId).Should().Be(ExecutionState.Completed);
        recorder.Seen.Should().Equal(new object?[] { 6L, 6L });
    }

    /// <summary>
    /// 🔁 Stage 7.7 — a stage failing inside a body's region fails the body like a node failure,
    /// with the stage's own error.
    /// </summary>
    [Fact]
    public void Region_InsideALoopBody_FailsTheBody_WithTheStagesError()
    {
        var definition = Definition(
            [Foreach("loop", 2), Node("src", "test.stream.source"), Node("boom", "test.stream.boom"), Node("collect", "test.stream.collect")],
            [
                new ConnectionDefinition("loop", "loopBody", "src", "items"),
                new ConnectionDefinition("src", "items", "boom", "items"),
                new ConnectionDefinition("boom", "items", "collect", "items"),
            ]);

        var services = Services(new Workflow.Modules.Builtin.Flow.ForEachModule(), new FakeSource(5), new FakeThrowingStage(), new FakeCollector());
        var executionId = Guid.NewGuid();
        var parent = CreateTestProbe();
        var executor = parent.ChildActorOf(WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), services));
        executor.Tell(new StartExecution(executionId));

        var terminal = parent.FishForMessage(m => m is WorkflowCompleted or WorkflowFailed, TimeSpan.FromSeconds(15));
        terminal.Should().BeOfType<WorkflowFailed>()
            .Which.Error.Message.Should().Contain("stage exploded");
    }

    /// <summary>
    /// 🔁 Stage 7.7 — retry inside a body re-runs the whole region (a failure blames the source, as
    /// at the top level), and the second run completes the body.
    /// </summary>
    [Fact]
    public void Region_InsideALoopBody_RetriesTheWholeRegion()
    {
        var flaky = new FakeFlakyStage();
        var recorder = new FakeRecorder();
        // Stage 7.8: a failure is blamed on the stage that failed (flaky) and its own settings apply,
        // so the retry policy sits on it; a whole-stream stage's retry re-runs the region~
        var flakyNode = Node("flaky", "test.stream.flaky") with
        {
            ErrorHandling = new ErrorHandling(ErrorBehavior.Retry),
            RetryPolicy = new RetryPolicy(MaxAttempts: 3, DelayMs: 10, BackoffMultiplier: 1.0, MaxDelayMs: 50),
        };
        var definition = Definition(
            [Foreach("loop", 1), Node("src", "test.stream.source"), flakyNode, Node("collect", "test.stream.collect"), Node("rec", "test.record")],
            [
                new ConnectionDefinition("loop", "loopBody", "src", "items"),
                new ConnectionDefinition("src", "items", "flaky", "items"),
                new ConnectionDefinition("flaky", "items", "collect", "items"),
                new ConnectionDefinition("collect", "count", "rec", "input"),
            ]);

        var services = Services(new Workflow.Modules.Builtin.Flow.ForEachModule(), new FakeSource(4), flaky, new FakeCollector(), recorder);
        var executionId = Guid.NewGuid();

        var executor = StartExecutor(definition, services, executionId);
        executor.Tell(new StartExecution(executionId));

        AwaitTerminalState(executor, executionId).Should().Be(ExecutionState.Completed);
        flaky.Runs.Should().Be(2);
        recorder.Seen.Should().Equal(new object?[] { 4L });
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
        private readonly TimeSpan delay;

        public FakeSource(int count, TimeSpan delay = default)
        {
            this.count = count;
            this.delay = delay;
        }

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
                if (this.delay > TimeSpan.Zero)
                {
                    await Task.Delay(this.delay, cancellationToken);
                }

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

    /// <summary>
    /// A source that emits <c>count</c> items and then, on its first <c>hangRuns</c> runs, waits
    /// forever (until cancelled) instead of finishing.
    /// </summary>
    private sealed class FakeHangingSource : FakeStreamModule
    {
        private readonly int count;
        private readonly int hangRuns;
        private int runs;

        public FakeHangingSource(int count, int hangRuns)
        {
            this.count = count;
            this.hangRuns = hangRuns;
        }

        public int Runs => this.runs;

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
            var run = Interlocked.Increment(ref this.runs);
            for (var i = 0; i < this.count; i++)
            {
                await Task.Yield();
                yield return StreamItem.FromJson(JsonSerializer.SerializeToElement(new { n = i }), i);
            }

            if (run <= this.hangRuns)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
        }
    }

    /// <summary>A stage whose first run throws; later runs pass items through.</summary>
    private sealed class FakeFlakyStage : FakeStreamModule
    {
        private int runs;

        public int Runs => this.runs;

        public override string ModuleId => "test.stream.flaky";

        public override async IAsyncEnumerable<StreamItem> ExecuteStreamAsync(
            ModuleExecutionContext context,
            IAsyncEnumerable<StreamItem> input,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var run = Interlocked.Increment(ref this.runs);
            await foreach (var item in input.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                if (run == 1)
                {
                    throw new InvalidOperationException("first run fails");
                }

                yield return item;
            }
        }
    }

    /// <summary>A batch node that records the <c>input</c> it was given.</summary>
    private sealed class FakeRecorder : IWorkflowModule
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<object?> seen = new();

        public IReadOnlyList<object?> Seen => this.seen.ToList();

        public string ModuleId => "test.record";

        public string DisplayName => "Record";

        public string Category => "Test";

        public string Description => "records its input";

        public string Icon => "📝";

        public Version Version => new(1, 0);

        public ModuleSchema Schema => new(
            Arr.create(PortDefinition.Create<object>("input", isRequired: false)),
            Arr<PortDefinition>.Empty,
            Arr<ModulePropertyDefinition>.Empty);

        public Task<ModuleResult> ExecuteAsync(ModuleExecutionContext context, CancellationToken cancellationToken = default)
        {
            this.seen.Enqueue(context.Inputs.TryGetValue("input", out var v) ? v : null);
            return Task.FromResult(ModuleResult.Ok(new Dictionary<string, object?>()));
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
