// ────────────────────────────────────────────────────────────────────────────────
// Copyright © GlutenFree 2025. Made with love by Ami-Chan! UwU 💖
// Licensed under the MIT License.
// ────────────────────────────────────────────────────────────────────────────────

using System.Runtime.CompilerServices;
using System.Text.Json;
using Akka.Actor;
using Akka.Streams;
using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Workflow.Core.Models;
using Workflow.Engine.Streaming;
using Workflow.Modules.Abstractions;

namespace Workflow.Tests.Engine.Streaming;

/// <summary>
/// Phase 5.1.4 — per-item stages (D26): bounded concurrency, ordering that's free (D25), and the
/// per-item error policy~ 🧩👷🧯
/// </summary>
public class StreamPerItemStageTests : IDisposable
{
    private readonly ActorSystem system = ActorSystem.Create("per-item-tests");
    private readonly IMaterializer materializer;
    private bool disposed;

    public StreamPerItemStageTests() => this.materializer = this.system.Materializer();

    public void Dispose()
    {
        if (!this.disposed)
        {
            this.system.Terminate().Wait(TimeSpan.FromSeconds(5));
            this.system.Dispose();
            this.disposed = true;
        }

        GC.SuppressFinalize(this);
    }

    #region Helpers

    private static ModuleExecutionContext Context(string nodeId)
        => new()
        {
            Inputs = new Dictionary<string, object?>(),
            Properties = new Dictionary<string, object?>(),
            Variables = new Dictionary<string, object?>(),
            Logger = NullLogger.Instance,
            Services = new ServiceCollection().BuildServiceProvider(),
            ExecutionId = Guid.NewGuid(),
            NodeId = nodeId,
        };

    private static JsonElement Json(int n) => JsonDocument.Parse($$"""{"n":{{n}}}""").RootElement.Clone();

    private static int ValueOf(StreamItem item)
        => ((JsonPayload)item.Payload).Value.GetProperty("n").GetInt32();

    private async Task<(StreamRegionResult Result, List<int> Values)> RunAsync(
        StreamStage sourceStage,
        StreamStage middleStage,
        CollectingTerminal terminal)
    {
        var plan = new StreamRegionPlan([sourceStage, middleStage, new StreamStage("sink", terminal, Context("sink"))]);
        var result = await new StreamRegionRunner(this.materializer).RunAsync(plan);
        return (result, terminal.Values);
    }

    #endregion

    [Fact]
    public async Task PerItemStage_IsUsedForProcessors()
    {
        var terminal = new CollectingTerminal();
        var (result, values) = await this.RunAsync(
            new StreamStage("src", new CountingSource(5), Context("src")),
            new StreamStage("double", new DoublingProcessor(), Context("double")),
            terminal);

        result.TerminalResult!.Success.Should().BeTrue();
        values.Should().ContainInOrder(0, 2, 4, 6, 8);
    }

    [Fact]
    public async Task MaxWorkers_Ordered_PreservesSourceOrder()
    {
        // 🎯 D25's core claim: ordering is free even with concurrency and jittered work.
        var terminal = new CollectingTerminal();
        var (_, values) = await this.RunAsync(
            new StreamStage("src", new CountingSource(40), Context("src")),
            new StreamStage("jitter", new JitteryProcessor(), Context("jitter"), MaxWorkers: 4, Ordered: true),
            terminal);

        values.Should().HaveCount(40);
        values.Should().BeInAscendingOrder("SelectAsync orders by input slot, so no resequencer is needed");
    }

    [Fact]
    public async Task MaxWorkers_RunsItemsConcurrently()
    {
        var processor = new ConcurrencyProbe();
        var terminal = new CollectingTerminal();

        await this.RunAsync(
            new StreamStage("src", new CountingSource(30), Context("src")),
            new StreamStage("probe", processor, Context("probe"), MaxWorkers: 4, Ordered: true),
            terminal);

        processor.MaxObservedConcurrency.Should().BeGreaterThan(
            1, "maxWorkers>1 must actually overlap item processing");
        processor.MaxObservedConcurrency.Should().BeLessThanOrEqualTo(
            4, "and must never exceed the requested bound");
    }

    [Fact]
    public async Task Filter_DroppingItems_KeepsOrderWithoutTombstones()
    {
        // 🪦 The tombstone machinery D25 deleted existed for exactly this case.
        var terminal = new CollectingTerminal();
        var (_, values) = await this.RunAsync(
            new StreamStage("src", new CountingSource(20), Context("src")),
            new StreamStage("evens", new EvenOnlyProcessor(), Context("evens"), MaxWorkers: 4, Ordered: true),
            terminal);

        values.Should().ContainInOrder(0, 2, 4, 6, 8, 10, 12, 14, 16, 18);
        values.Should().HaveCount(10);
    }

    [Fact]
    public async Task Splitter_ExpandingItems_KeepsOrder()
    {
        var terminal = new CollectingTerminal();
        var (_, values) = await this.RunAsync(
            new StreamStage("src", new CountingSource(5), Context("src")),
            new StreamStage("split", new DuplicatingProcessor(), Context("split"), MaxWorkers: 4, Ordered: true),
            terminal);

        // Each n becomes [n*10, n*10+1], contiguous and in order.
        values.Should().ContainInOrder(0, 1, 10, 11, 20, 21, 30, 31, 40, 41);
    }

    [Fact]
    public async Task ItemError_FailPolicy_FailsTheRegion()
    {
        var plan = new StreamRegionPlan(
        [
            new StreamStage("src", new CountingSource(10), Context("src")),
            new StreamStage("boom", new FailingProcessor(failAt: 4), Context("boom")),
            new StreamStage("sink", new CollectingTerminal(), Context("sink")),
        ]);

        var act = async () => await new StreamRegionRunner(this.materializer).RunAsync(plan);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*item 4*");
    }

    [Fact]
    public async Task ItemError_SkipPolicy_DropsOnlyTheBadItem()
    {
        var terminal = new CollectingTerminal();
        var (result, values) = await this.RunAsync(
            new StreamStage("src", new CountingSource(10), Context("src")),
            new StreamStage(
                "boom",
                new FailingProcessor(failAt: 4),
                Context("boom"),
                OnItemError: StreamItemErrorPolicy.Skip),
            terminal);

        values.Should().HaveCount(9).And.NotContain(4);
        values.Should().BeInAscendingOrder("dropping an item leaves everyone else's order intact");

        result.ItemErrors.Should().ContainSingle();
        result.ItemErrors[0].NodeId.Should().Be("boom");
        result.ItemErrors[0].ItemIndex.Should().Be(4);
        result.ItemErrors[0].Error.Message.Should().Contain("item 4");
    }

    [Fact]
    public async Task ItemError_SkipPolicy_CapturesOffsetForDeadLettering()
    {
        // 🧾 The envelope is what a dead-letter path needs: which node, which item, and where in
        // the source it came from (doc 07's per-item error document).
        var terminal = new CollectingTerminal();
        var (result, _) = await this.RunAsync(
            new StreamStage("src", new OffsetSource(6), Context("src")),
            new StreamStage(
                "boom",
                new FailingProcessor(failAt: 3),
                Context("boom"),
                OnItemError: StreamItemErrorPolicy.Skip),
            terminal);

        var error = result.ItemErrors.Should().ContainSingle().Subject;
        error.NodeId.Should().Be("boom");
        error.ItemIndex.Should().Be(3);
        error.Offset.Should().NotBeNull();
        error.Offset!.Sequence.Should().Be(3, "the source offset is what a retry would seek with");
    }

    [Fact]
    public async Task ItemError_SkipPolicy_KeepsCancellationFatal()    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var plan = new StreamRegionPlan(
        [
            new StreamStage("src", new CountingSource(100), Context("src")),
            new StreamStage("p", new DoublingProcessor(), Context("p"), OnItemError: StreamItemErrorPolicy.Skip),
            new StreamStage("sink", new CollectingTerminal(), Context("sink")),
        ]);

        var act = async () => await new StreamRegionRunner(this.materializer).RunAsync(plan, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>(
            "a cancelled run must never be mistaken for a skippable item error");
    }

    [Fact]
    public async Task StreamShapedStage_StillWorksAlongsidePerItemStages()
    {
        // 🛡️ D26 compatibility: whole-stream modules keep working, just single-worker.
        var terminal = new CollectingTerminal();
        var (_, values) = await this.RunAsync(
            new StreamStage("src", new CountingSource(4), Context("src")),
            new StreamStage("passthrough", new WholeStreamPassthrough(), Context("passthrough")),
            terminal);

        values.Should().ContainInOrder(0, 1, 2, 3);
    }

    #region Fakes

    private abstract class FakeStage : IStreamingWorkflowModule
    {
        public virtual string ModuleId => "test.stage";

        public string DisplayName => "Stage";

        public string Category => "Test";

        public string Description => "test stage";

        public string Icon => "🌊";

        public Version Version => new(1, 0, 0);

        public ModuleSchema Schema => new(
            Arr.create(PortDefinition.CreateStreaming("items", isRequired: false)),
            Arr.create(PortDefinition.CreateStreaming("items", isRequired: false)),
            Arr<ModulePropertyDefinition>.Empty);

        public Task<ModuleResult> ExecuteAsync(ModuleExecutionContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(ModuleResult.Fail("streaming only"));

        public virtual IAsyncEnumerable<StreamItem> ExecuteStreamAsync(
            ModuleExecutionContext context,
            IAsyncEnumerable<StreamItem> input,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("per-item stage");
    }

    private sealed class CountingSource : FakeStage
    {
        private readonly int count;

        public CountingSource(int count) => this.count = count;

        public override async IAsyncEnumerable<StreamItem> ExecuteStreamAsync(
            ModuleExecutionContext context,
            IAsyncEnumerable<StreamItem> input,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            for (var i = 0; i < this.count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return StreamItem.FromJson(Json(i), i);
            }
        }
    }

    private sealed class OffsetSource : FakeStage
    {
        private readonly int count;

        public OffsetSource(int count) => this.count = count;

        public override async IAsyncEnumerable<StreamItem> ExecuteStreamAsync(
            ModuleExecutionContext context,
            IAsyncEnumerable<StreamItem> input,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            for (var i = 0; i < this.count; i++)
            {
                await Task.Yield();
                yield return StreamItem.FromJson(
                    Json(i),
                    i,
                    new SourceOffset(i.ToString(System.Globalization.CultureInfo.InvariantCulture), i));
            }
        }
    }

    private sealed class WholeStreamPassthrough : FakeStage
    {
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

    private sealed class DoublingProcessor : FakeStage, IStreamItemProcessor
    {
        public ValueTask<IReadOnlyList<StreamItem>> ProcessAsync(
            StreamItem item,
            ModuleExecutionContext context,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<StreamItem>>(
                new[] { item with { Payload = new JsonPayload(Json(ValueOf(item) * 2)) } });
    }

    private sealed class JitteryProcessor : FakeStage, IStreamItemProcessor
    {
        public async ValueTask<IReadOnlyList<StreamItem>> ProcessAsync(
            StreamItem item,
            ModuleExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Random.Shared.Next(1, 12), cancellationToken).ConfigureAwait(false);
            return new[] { item };
        }
    }

    private sealed class ConcurrencyProbe : FakeStage, IStreamItemProcessor
    {
        private int current;
        private int max;

        public int MaxObservedConcurrency => Volatile.Read(ref this.max);

        public async ValueTask<IReadOnlyList<StreamItem>> ProcessAsync(
            StreamItem item,
            ModuleExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            var now = Interlocked.Increment(ref this.current);
            int observed;
            do
            {
                observed = Volatile.Read(ref this.max);
            }
            while (now > observed && Interlocked.CompareExchange(ref this.max, now, observed) != observed);

            await Task.Delay(5, cancellationToken).ConfigureAwait(false);
            Interlocked.Decrement(ref this.current);
            return new[] { item };
        }
    }

    private sealed class EvenOnlyProcessor : FakeStage, IStreamItemProcessor
    {
        public async ValueTask<IReadOnlyList<StreamItem>> ProcessAsync(
            StreamItem item,
            ModuleExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Random.Shared.Next(1, 8), cancellationToken).ConfigureAwait(false);
            return ValueOf(item) % 2 == 0 ? new[] { item } : Array.Empty<StreamItem>();
        }
    }

    private sealed class DuplicatingProcessor : FakeStage, IStreamItemProcessor
    {
        public async ValueTask<IReadOnlyList<StreamItem>> ProcessAsync(
            StreamItem item,
            ModuleExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Random.Shared.Next(1, 8), cancellationToken).ConfigureAwait(false);
            var n = ValueOf(item) * 10;
            return new[]
            {
                item with { Payload = new JsonPayload(Json(n)) },
                item with { Payload = new JsonPayload(Json(n + 1)) },
            };
        }
    }

    private sealed class FailingProcessor : FakeStage, IStreamItemProcessor
    {
        private readonly int failAt;

        public FailingProcessor(int failAt) => this.failAt = failAt;

        public ValueTask<IReadOnlyList<StreamItem>> ProcessAsync(
            StreamItem item,
            ModuleExecutionContext context,
            CancellationToken cancellationToken = default)
            => ValueOf(item) == this.failAt
                ? throw new InvalidOperationException($"boom on item {this.failAt}")
                : ValueTask.FromResult<IReadOnlyList<StreamItem>>(new[] { item });
    }

    private sealed class CollectingTerminal : FakeStage, IStreamTerminalModule
    {
        public List<int> Values { get; } = new();

        public async Task<ModuleResult> ExecuteTerminalAsync(
            ModuleExecutionContext context,
            IAsyncEnumerable<StreamItem> input,
            CancellationToken cancellationToken = default)
        {
            await foreach (var item in input.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                this.Values.Add(ValueOf(item));
            }

            return ModuleResult.Ok(new Dictionary<string, object?> { ["count"] = (long)this.Values.Count });
        }
    }

    #endregion
}
