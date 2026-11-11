// <copyright file="StreamStageBlameTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Engine.Streaming;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit;
using Akka.TestKit.Xunit2;
using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Core.Models;
using Workflow.Engine.Actors;
using Workflow.Engine.Messages;
using Workflow.Engine.Streaming;
using Workflow.Modules;
using Workflow.Modules.Abstractions;
using Workflow.Persistence.Abstractions;
using Workflow.Persistence.Models;
using Xunit;

/// <summary>
/// Stage 7.8 (03 §9.3) — a failure inside a streaming region is blamed on the stage that failed:
/// <list type="bullet">
///   <item>stages before it show Completed, stages after it Skipped with a recorded reason;</item>
///   <item>that stage's own error handling applies (or the source's, with "use the region's");</item>
///   <item>a per-item stage retries the failing item in place; other stages re-run the region;</item>
///   <item>continue / an error handler run what follows the region, unless the stage says skip it.</item>
/// </list>
/// </summary>
public sealed class StreamStageBlameTests : TestKit
{
    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(15);

    // ── Blame and states ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AMidStageFailure_BlamesThatStage_BeforeCompleted_AfterSkippedWithAReason()
    {
        var records = new List<NodeExecutionRecord>();
        var run = this.Run(
            Chain(Src(), Stage("boom", "s.boom"), Collect()),
            records,
            new Source(5), new Boom(failAt: 2), new Collector());

        run.Terminal.Should().BeOfType<WorkflowFailed>().Which.Error.Message.Should().Contain("stage exploded");
        run.FinalState("src").Should().Be(NodeExecutionState.Completed);
        run.FinalState("boom").Should().Be(NodeExecutionState.Failed);
        run.FinalState("collect").Should().Be(NodeExecutionState.Skipped);

        AwaitAssert(() =>
        {
            lock (records)
            {
                records.Should().Contain(r => r.NodeId == "collect" && r.State == NodeExecutionState.Skipped
                    && r.Metadata != null && Equals(r.Metadata["skipReason"], "skipped: upstream stage 'boom' failed"));
                records.Should().Contain(r => r.NodeId == "src" && r.State == NodeExecutionState.Completed);
            }
        }, RunTimeout);
    }

    // ── Whose settings ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheFailedStagesOwnSettings_Apply_NotTheSources()
    {
        var src = Src() with { ErrorHandling = new ErrorHandling(ErrorBehavior.Continue) };

        var run = this.Run(Chain(src, Stage("boom", "s.boom"), Collect()), null, new Source(5), new Boom(failAt: 2), new Collector());

        run.Terminal.Should().BeOfType<WorkflowFailed>(because: "boom has no error handling of its own, so it fails the run");
    }

    [Fact]
    public void UseTheRegionsErrorHandling_AppliesTheSourcesSettings()
    {
        var src = Src() with { ErrorHandling = new ErrorHandling(ErrorBehavior.Continue) };
        var boom = Stage("boom", "s.boom") with { Metadata = Meta((RegionErrorHandling.UseRegionErrorHandlingKey, "true")) };

        var run = this.Run(Chain(src, boom, Collect()), null, new Source(5), new Boom(failAt: 2), new Collector());

        run.Terminal.Should().BeOfType<WorkflowCompleted>();
    }

    // ── Continue / error handler ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ContinueOnAStage_RunsWhatFollowsTheRegion_ByDefault()
    {
        var after = new Recorder("b.after");
        var boom = Stage("boom", "s.boom") with { ErrorHandling = new ErrorHandling(ErrorBehavior.Continue) };

        var run = this.Run(
            Chain(Src(), boom, Collect(), Stage("after", "b.after")),
            null,
            new Source(5), new Boom(failAt: 2), new Collector(), after);

        run.Terminal.Should().BeOfType<WorkflowCompleted>();
        after.Calls.Should().Be(1, because: "what follows the region runs, with the region's outputs missing");
        run.FinalState("collect").Should().Be(NodeExecutionState.Skipped);
    }

    [Fact]
    public void ContinueOnAStage_WithSkipWhatFollows_SkipsIt()
    {
        var after = new Recorder("b.after");
        var boom = Stage("boom", "s.boom") with
        {
            ErrorHandling = new ErrorHandling(ErrorBehavior.Continue),
            Metadata = Meta((RegionErrorHandling.SkipWhatFollowsKey, "true")),
        };

        var run = this.Run(
            Chain(Src(), boom, Collect(), Stage("after", "b.after")),
            null,
            new Source(5), new Boom(failAt: 2), new Collector(), after);

        run.Terminal.Should().BeOfType<WorkflowCompleted>();
        after.Calls.Should().Be(0);
        run.FinalState("after").Should().Be(NodeExecutionState.Skipped);
    }

    [Fact]
    public void ContinueOnTheSource_RunsWhatFollows()
    {
        var after = new Recorder("b.after");
        var src = Src() with { ErrorHandling = new ErrorHandling(ErrorBehavior.Continue) };

        var run = this.Run(
            Chain(src, Collect(), Stage("after", "b.after")),
            null,
            new Source(5, throwAt: 2), new Collector(), after);

        run.Terminal.Should().BeOfType<WorkflowCompleted>(because: "continue on the source must not leave the run waiting");
        after.Calls.Should().Be(1);
    }

    [Fact]
    public void AnErrorHandlerOnAStage_RunsTheHandler_AndWhatFollows()
    {
        var handler = new Recorder("b.handler");
        var after = new Recorder("b.after");
        var boom = Stage("boom", "s.boom") with { ErrorHandling = new ErrorHandling(ErrorBehavior.UseErrorHandler, "handler") };
        var definition = Chain(Src(), boom, Collect(), Stage("after", "b.after"));
        definition = definition with { Nodes = definition.Nodes.Add(Stage("handler", "b.handler")) };

        var run = this.Run(definition, null, new Source(5), new Boom(failAt: 2), new Collector(), handler, after);

        run.Terminal.Should().BeOfType<WorkflowCompleted>();
        handler.Calls.Should().Be(1);
        after.Calls.Should().Be(1);
    }

    // ── Retry ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void APerItemStage_RetriesTheFailingItemInPlace_WithoutReRunningTheRegion()
    {
        var source = new Source(5);
        var flaky = new FlakyItem(failingItem: 2, failures: 2);
        var collector = new Collector();
        var stage = Stage("flaky", "s.flakyitem") with { ErrorHandling = new ErrorHandling(ErrorBehavior.Retry), RetryPolicy = QuickRetry(3) };

        var run = this.Run(Chain(Src(), stage, Collect()), null, source, flaky, collector);

        run.Terminal.Should().BeOfType<WorkflowCompleted>();
        source.Runs.Should().Be(1, because: "the item was retried in place; the source didn't run again");
        flaky.CallsFor(2).Should().Be(3);
        collector.LastCount.Should().Be(5);
    }

    [Fact]
    public void APerItemStage_WhoseItemRetriesRunOut_DoesNotReRunTheRegion()
    {
        var source = new Source(5);
        var stage = Stage("flaky", "s.flakyitem") with { ErrorHandling = new ErrorHandling(ErrorBehavior.Retry), RetryPolicy = QuickRetry(2) };

        var run = this.Run(Chain(Src(), stage, Collect()), null, source, new FlakyItem(failingItem: 2, failures: int.MaxValue), new Collector());

        run.Terminal.Should().BeOfType<WorkflowFailed>();
        source.Runs.Should().Be(1);
    }

    [Fact]
    public void AWholeStreamStagesRetry_ReRunsTheRegion_CountedOnThatStage()
    {
        var source = new Source(4);
        var flaky = new FlakyStream(failingRuns: 1);
        var stage = Stage("flaky", "s.flakystream") with { ErrorHandling = new ErrorHandling(ErrorBehavior.Retry), RetryPolicy = QuickRetry(3) };

        var run = this.Run(Chain(Src(), stage, Collect()), null, source, flaky, new Collector());

        run.Terminal.Should().BeOfType<WorkflowCompleted>(because: "retrying a mid-pipeline stage re-runs its region (no stall)");
        source.Runs.Should().Be(2);
        flaky.Runs.Should().Be(2);
    }

    // ── Bodies ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void InsideALoopBody_ContinueOnAStage_RunsWhatFollows()
    {
        var after = new Recorder("b.after");
        var boom = Stage("boom", "s.boom") with { ErrorHandling = new ErrorHandling(ErrorBehavior.Continue) };
        var loop = new NodeDefinition("loop", "builtin.loop.foreach", "loop", HashMap<string, JsonElement>.Empty.Add("collection", JsonSerializer.SerializeToElement(new[] { 1, 2 })));
        var definition = Workflow(
            [loop, Src(), boom, Collect(), Stage("after", "b.after")],
            [
                new ConnectionDefinition("loop", "loopBody", "src", "items"),
                new ConnectionDefinition("src", "items", "boom", "items"),
                new ConnectionDefinition("boom", "items", "collect", "items"),
                new ConnectionDefinition("collect", "count", "after", "input"),
            ]);

        var run = this.Run(definition, null, new Workflow.Modules.Builtin.Flow.ForEachModule(), new Source(5), new Boom(failAt: 2), new Collector(), after);

        run.Terminal.Should().BeOfType<WorkflowCompleted>();
        after.Calls.Should().Be(2, because: "each iteration continues past the failed stage");
    }

    // ── Harness ───────────────────────────────────────────────────────────────────────────────────

    private sealed record RunResult(object Terminal, IReadOnlyList<NodeStateChanged> States)
    {
        public NodeExecutionState? FinalState(string nodeId)
            => this.States.LastOrDefault(s => s.NodeId == nodeId)?.NewState;
    }

    private RunResult Run(WorkflowDefinition definition, List<NodeExecutionRecord>? records, params IWorkflowModule[] modules)
    {
        var registry = new InMemoryModuleRegistry(skipValidation: true);
        foreach (var module in modules)
        {
            registry.RegisterModule(module);
        }

        var services = new ServiceCollection().AddSingleton<IModuleRegistry>(registry);
        if (records is not null)
        {
            services.AddSingleton<IExecutionHistoryRepository>(new CapturingHistory(records));
        }

        var states = this.CreateTestProbe();
        Sys.EventStream.Subscribe(states, typeof(NodeStateChanged));

        var executionId = Guid.NewGuid();
        var parent = this.CreateTestProbe();
        var executor = parent.ChildActorOf(WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), services.BuildServiceProvider()));
        executor.Tell(new StartExecution(executionId));
        var terminal = parent.FishForMessage(m => m is WorkflowCompleted or WorkflowFailed, RunTimeout);

        var changes = states.ReceiveWhile<object>(TimeSpan.FromMilliseconds(300), m => m).OfType<NodeStateChanged>().Where(s => s.ExecutionId == executionId).ToList();
        return new RunResult(terminal, changes);
    }

    private static RetryPolicy QuickRetry(int maxAttempts) => new(MaxAttempts: maxAttempts, DelayMs: 10, BackoffMultiplier: 1.0, MaxDelayMs: 50);

    private static HashMap<string, string> Meta(params (string Key, string Value)[] entries)
        => entries.Aggregate(HashMap<string, string>.Empty, (m, kv) => m.AddOrUpdate(kv.Key, kv.Value));

    private static NodeDefinition Src() => Stage("src", "s.source");

    private static NodeDefinition Collect() => Stage("collect", "s.collect");

    private static NodeDefinition Stage(string id, string moduleId) => new(id, moduleId, id, HashMap<string, JsonElement>.Empty);

    /// <summary>A straight chain; streaming ports are called "items", the collector's output "count".</summary>
    private static WorkflowDefinition Chain(params NodeDefinition[] nodes)
    {
        var connections = new List<ConnectionDefinition>();
        for (var i = 0; i + 1 < nodes.Length; i++)
        {
            var from = nodes[i];
            var to = nodes[i + 1];
            var fromPort = from.Id == "collect" ? "count" : "items";
            var toPort = to.ModuleId.StartsWith("b.", StringComparison.Ordinal) ? "input" : "items";
            connections.Add(new ConnectionDefinition(from.Id, fromPort, to.Id, toPort));
        }

        return Workflow(nodes, connections.ToArray());
    }

    private static WorkflowDefinition Workflow(NodeDefinition[] nodes, ConnectionDefinition[] connections)
        => new(Guid.NewGuid(), "stage-blame", null, new Version(1, 0), nodes.ToArr(), connections.ToArr(), HashMap<string, VariableDefinition>.Empty);

    private static StreamItem Item(int i) => StreamItem.FromJson(JsonSerializer.SerializeToElement(new { n = i }), i);

    private abstract class StreamModule : IStreamingWorkflowModule
    {
        public abstract string ModuleId { get; }

        public string DisplayName => this.ModuleId;

        public string Category => "Test";

        public string Description => "test";

        public string Icon => "🌊";

        public Version Version => new(1, 0);

        public virtual ModuleSchema Schema => new(
            Arr.create(PortDefinition.CreateStreaming("items", isRequired: false)),
            Arr.create(PortDefinition.CreateStreaming("items", isRequired: false)),
            Arr<ModulePropertyDefinition>.Empty);

        public Task<ModuleResult> ExecuteAsync(ModuleExecutionContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(ModuleResult.Fail("streaming only"));

        public abstract IAsyncEnumerable<StreamItem> ExecuteStreamAsync(ModuleExecutionContext context, IAsyncEnumerable<StreamItem> input, CancellationToken cancellationToken = default);
    }

    private sealed class Source : StreamModule
    {
        private readonly int count;
        private readonly int throwAt;
        private int runs;

        public Source(int count, int throwAt = -1)
        {
            this.count = count;
            this.throwAt = throwAt;
        }

        public int Runs => this.runs;

        public override string ModuleId => "s.source";

        public override ModuleSchema Schema => new(
            Arr.create(PortDefinition.Create<object>("input", isRequired: false)),
            Arr.create(PortDefinition.CreateStreaming("items", isRequired: false)),
            Arr<ModulePropertyDefinition>.Empty);

        public override async IAsyncEnumerable<StreamItem> ExecuteStreamAsync(ModuleExecutionContext context, IAsyncEnumerable<StreamItem> input, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref this.runs);
            for (var i = 0; i < this.count; i++)
            {
                await Task.Yield();
                if (i == this.throwAt)
                {
                    throw new InvalidOperationException("source exploded");
                }

                yield return Item(i);
            }
        }
    }

    /// <summary>A whole-stream stage that throws when it reaches item <c>failAt</c>.</summary>
    private sealed class Boom : StreamModule
    {
        private readonly int failAt;

        public Boom(int failAt) => this.failAt = failAt;

        public override string ModuleId => "s.boom";

        public override async IAsyncEnumerable<StreamItem> ExecuteStreamAsync(ModuleExecutionContext context, IAsyncEnumerable<StreamItem> input, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var item in input.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                if (item.Index == this.failAt)
                {
                    throw new InvalidOperationException("stage exploded");
                }

                yield return item;
            }
        }
    }

    /// <summary>A whole-stream stage whose first <c>failingRuns</c> runs throw.</summary>
    private sealed class FlakyStream : StreamModule
    {
        private readonly int failingRuns;
        private int runs;

        public FlakyStream(int failingRuns) => this.failingRuns = failingRuns;

        public int Runs => this.runs;

        public override string ModuleId => "s.flakystream";

        public override async IAsyncEnumerable<StreamItem> ExecuteStreamAsync(ModuleExecutionContext context, IAsyncEnumerable<StreamItem> input, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var run = Interlocked.Increment(ref this.runs);
            await foreach (var item in input.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                if (run <= this.failingRuns)
                {
                    throw new InvalidOperationException("flaky stream");
                }

                yield return item;
            }
        }
    }

    /// <summary>A per-item stage: item <c>failingItem</c> fails its first <c>failures</c> calls.</summary>
    private sealed class FlakyItem : StreamModule, IStreamItemProcessor
    {
        private readonly int failingItem;
        private readonly int failures;
        private readonly Dictionary<long, int> calls = new();

        public FlakyItem(int failingItem, int failures)
        {
            this.failingItem = failingItem;
            this.failures = failures;
        }

        public override string ModuleId => "s.flakyitem";

        public int CallsFor(long index)
        {
            lock (this.calls)
            {
                return this.calls.GetValueOrDefault(index);
            }
        }

        public override IAsyncEnumerable<StreamItem> ExecuteStreamAsync(ModuleExecutionContext context, IAsyncEnumerable<StreamItem> input, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("per-item stage");

        public ValueTask<IReadOnlyList<StreamItem>> ProcessAsync(StreamItem item, ModuleExecutionContext context, CancellationToken cancellationToken = default)
        {
            int call;
            lock (this.calls)
            {
                call = this.calls[item.Index] = this.calls.GetValueOrDefault(item.Index) + 1;
            }

            return item.Index == this.failingItem && call <= this.failures
                ? throw new InvalidOperationException($"item {item.Index} failed (call {call})")
                : ValueTask.FromResult<IReadOnlyList<StreamItem>>(new[] { item });
        }
    }

    private sealed class Collector : StreamModule, IStreamTerminalModule
    {
        public long LastCount { get; private set; }

        public override string ModuleId => "s.collect";

        public override ModuleSchema Schema => new(
            Arr.create(PortDefinition.CreateStreaming("items", isRequired: false)),
            Arr.create(PortDefinition.Create<long>("count", isRequired: false)),
            Arr<ModulePropertyDefinition>.Empty);

        public override IAsyncEnumerable<StreamItem> ExecuteStreamAsync(ModuleExecutionContext context, IAsyncEnumerable<StreamItem> input, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("terminal");

        public async Task<ModuleResult> ExecuteTerminalAsync(ModuleExecutionContext context, IAsyncEnumerable<StreamItem> input, CancellationToken cancellationToken = default)
        {
            var count = 0L;
            await foreach (var _ in input.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                count++;
            }

            this.LastCount = count;
            return ModuleResult.Ok(new Dictionary<string, object?> { ["count"] = count });
        }
    }

    /// <summary>An ordinary batch node that counts its runs.</summary>
    private sealed class Recorder : IWorkflowModule
    {
        private int calls;

        public Recorder(string moduleId) => this.ModuleId = moduleId;

        public int Calls => this.calls;

        public string ModuleId { get; }

        public string DisplayName => this.ModuleId;

        public string Category => "Test";

        public string Description => "test";

        public string Icon => "📝";

        public Version Version => new(1, 0);

        public ModuleSchema Schema => new(
            Arr.create(PortDefinition.Create<object>("input", isRequired: false)),
            Arr.create(PortDefinition.Create<object>("output", isRequired: false)),
            Arr<ModulePropertyDefinition>.Empty);

        public Task<ModuleResult> ExecuteAsync(ModuleExecutionContext context, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref this.calls);
            return Task.FromResult(ModuleResult.Ok(new Dictionary<string, object?> { ["output"] = "ok" }));
        }
    }

    private sealed class CapturingHistory : IExecutionHistoryRepository
    {
        private readonly List<NodeExecutionRecord> records;

        public CapturingHistory(List<NodeExecutionRecord> records) => this.records = records;

        public Task<Guid> CreateExecutionAsync(ExecutionRecord record, CancellationToken ct = default) => Task.FromResult(record.ExecutionId);

        public Task RecordNodeExecutionAsync(NodeExecutionRecord nodeRecord, CancellationToken ct = default)
        {
            lock (this.records)
            {
                this.records.Add(nodeRecord);
            }

            return Task.CompletedTask;
        }

        public Task UpdateExecutionStatusAsync(Guid executionId, ExecutionState state, DateTimeOffset? endTime = null, string? error = null, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<NodeExecutionRecord>> GetNodeExecutionsAsync(Guid executionId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<NodeExecutionRecord>>(new List<NodeExecutionRecord>());

        public Task<ExecutionRecord?> GetExecutionAsync(Guid executionId, CancellationToken ct = default) => Task.FromResult<ExecutionRecord?>(null);

        public Task<PagedResult<ExecutionRecord>> GetExecutionsForWorkflowAsync(Guid workflowId, ExecutionFilter filter, Pagination pagination, CancellationToken ct = default)
            => Task.FromResult(new PagedResult<ExecutionRecord>(new List<ExecutionRecord>(), 0, pagination.Page, pagination.PageSize));
    }
}
