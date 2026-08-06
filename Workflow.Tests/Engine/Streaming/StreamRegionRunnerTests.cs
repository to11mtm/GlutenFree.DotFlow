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
/// Phase 5.1.3 — the proving slice: a region really does stream with bounded memory, really does
/// stop when cancelled, and really does hand its terminal outputs back to the batch world~ 🌊⚙️
/// </summary>
public class StreamRegionRunnerTests : IDisposable
{
    private readonly ActorSystem system = ActorSystem.Create("stream-region-tests");
    private readonly IMaterializer materializer;
    private bool disposed;

    public StreamRegionRunnerTests() => this.materializer = this.system.Materializer();

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

    #endregion

    [Fact]
    public async Task Region_SourceToTerminal_StreamsEveryItem()
    {
        var plan = new StreamRegionPlan(
        [
            new StreamStage("src", new CountingSource(5), Context("src")),
            new StreamStage("sink", new CollectingTerminal(), Context("sink")),
        ]);

        var result = await new StreamRegionRunner(this.materializer).RunAsync(plan);

        result.TerminalResult!.Success.Should().BeTrue();
        result.TerminalResult.Outputs["count"].Should().Be(5L);
        result.ItemCounts["src"].Should().Be(5);
        result.ItemCounts["sink"].Should().Be(5);
    }

    [Fact]
    public async Task Region_ThreeStages_TransformsInTheMiddle()
    {
        var plan = new StreamRegionPlan(
        [
            new StreamStage("src", new CountingSource(4), Context("src")),
            new StreamStage("map", new DoublingTransform(), Context("map")),
            new StreamStage("sink", new CollectingTerminal(), Context("sink")),
        ]);

        var result = await new StreamRegionRunner(this.materializer).RunAsync(plan);

        var collected = (IReadOnlyList<int>)result.TerminalResult!.Outputs["values"]!;
        collected.Should().ContainInOrder(0, 2, 4, 6);
        result.ItemCounts["map"].Should().Be(4);
    }

    [Fact]
    public async Task Region_IsBoundedInMemory_SlowSinkThrottlesTheSource()
    {
        // 🎯 The whole point of the feature: a fast source must NOT run ahead of a slow sink.
        var source = new CountingSource(10_000, trackProduced: true);
        var plan = new StreamRegionPlan(
        [
            new StreamStage("src", source, Context("src"), BufferCapacity: 8),
            new StreamStage("sink", new SlowTerminal(consumeCount: 20), Context("sink"), BufferCapacity: 8),
        ]);

        await new StreamRegionRunner(this.materializer).RunAsync(plan);

        source.Produced.Should().BeLessThan(
            2_000,
            "a bounded pipeline must not materialize the whole source to satisfy a slow consumer");
    }

    [Fact]
    public async Task Region_Cancellation_StopsTheSource()
    {
        using var cts = new CancellationTokenSource();
        var source = new CountingSource(int.MaxValue, trackProduced: true);
        var plan = new StreamRegionPlan(
        [
            new StreamStage("src", source, Context("src")),
            new StreamStage("sink", new SlowTerminal(consumeCount: int.MaxValue), Context("sink")),
        ]);

        var run = new StreamRegionRunner(this.materializer).RunAsync(plan, cts.Token);
        await Task.Delay(150);
        await cts.CancelAsync();

        var act = async () => await run;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Region_StageFailure_SurfacesToTheCaller()
    {
        var plan = new StreamRegionPlan(
        [
            new StreamStage("src", new CountingSource(10), Context("src")),
            new StreamStage("boom", new ThrowingTransform(failAt: 3), Context("boom")),
            new StreamStage("sink", new CollectingTerminal(), Context("sink")),
        ]);

        var act = async () => await new StreamRegionRunner(this.materializer).RunAsync(plan);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*item 3*");
    }

    [Fact]
    public async Task Region_WithoutTerminalStage_StillDrains()
    {
        var sink = new CountingSource(3);
        var plan = new StreamRegionPlan(
        [
            new StreamStage("src", sink, Context("src")),
            new StreamStage("map", new DoublingTransform(), Context("map")),
        ]);

        var result = await new StreamRegionRunner(this.materializer).RunAsync(plan);

        result.TerminalResult.Should().BeNull();
        result.ItemCounts["map"].Should().Be(3, "transforms still run when nothing collects them");
    }

    [Fact]
    public async Task Region_EmptySource_CompletesCleanly()
    {
        var plan = new StreamRegionPlan(
        [
            new StreamStage("src", new CountingSource(0), Context("src")),
            new StreamStage("sink", new CollectingTerminal(), Context("sink")),
        ]);

        var result = await new StreamRegionRunner(this.materializer).RunAsync(plan);

        result.TerminalResult!.Outputs["count"].Should().Be(0L);
    }

    [Fact]
    public async Task Region_ReportsDuration()
    {
        var plan = new StreamRegionPlan(
        [
            new StreamStage("src", new CountingSource(1), Context("src")),
            new StreamStage("sink", new CollectingTerminal(), Context("sink")),
        ]);

        var result = await new StreamRegionRunner(this.materializer).RunAsync(plan);

        result.Duration.Should().BeGreaterThan(TimeSpan.Zero);
    }

    /// <summary>
    /// 🌉 End-to-end with the <b>real</b> 5.1.1 bridge modules — the batch world enters a region
    /// through <c>fromitems</c> and leaves it through <c>collect</c>, with the runner in between.
    /// </summary>
    [Fact]
    public async Task Region_RealBridgeModules_RoundTripBatchToStreamAndBack()
    {
        var fromItems = new Workflow.Modules.Builtin.Stream.StreamFromItemsModule();
        var collect = new Workflow.Modules.Builtin.Stream.StreamCollectModule();

        var sourceContext = Context("from-items") with
        {
            Inputs = new Dictionary<string, object?> { ["items"] = new List<object?> { 10, 20, 30 } },
        };

        var plan = new StreamRegionPlan(
        [
            new StreamStage("from-items", fromItems, sourceContext),
            new StreamStage("collect", collect, Context("collect")),
        ]);

        var result = await new StreamRegionRunner(this.materializer).RunAsync(plan);

        result.TerminalResult!.Success.Should().BeTrue();
        result.TerminalResult.Outputs["count"].Should().Be(3L);
        ((IReadOnlyList<object?>)result.TerminalResult.Outputs["items"]!)
            .Should().BeEquivalentTo(new object?[] { 10d, 20d, 30d });
    }

    /// <summary>
    /// 🛡️ The collect guard still bites inside a real region — the failure arrives as a failed
    /// <c>ModuleResult</c> the engine can route, not an unhandled stream fault.
    /// </summary>
    [Fact]
    public async Task Region_CollectGuard_FailsTheTerminalStageCleanly()
    {
        var collectContext = Context("collect") with
        {
            Properties = new Dictionary<string, object?> { ["maxItems"] = 2 },
        };

        var plan = new StreamRegionPlan(
        [
            new StreamStage("src", new CountingSource(50), Context("src")),
            new StreamStage("collect", new Workflow.Modules.Builtin.Stream.StreamCollectModule(), collectContext),
        ]);

        var result = await new StreamRegionRunner(this.materializer).RunAsync(plan);

        result.TerminalResult!.Success.Should().BeFalse();
        result.TerminalResult.ErrorMessage.Should().Contain("more than 2 items");
    }

    #region Fake stages

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

        public abstract IAsyncEnumerable<StreamItem> ExecuteStreamAsync(
            ModuleExecutionContext context,
            IAsyncEnumerable<StreamItem> input,
            CancellationToken cancellationToken = default);
    }

    private sealed class CountingSource : FakeStage
    {
        private readonly int count;
        private readonly bool trackProduced;
        private int produced;

        public CountingSource(int count, bool trackProduced = false)
        {
            this.count = count;
            this.trackProduced = trackProduced;
        }

        public int Produced => Volatile.Read(ref this.produced);

        public override async IAsyncEnumerable<StreamItem> ExecuteStreamAsync(
            ModuleExecutionContext context,
            IAsyncEnumerable<StreamItem> input,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            for (var i = 0; i < this.count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();

                if (this.trackProduced)
                {
                    Interlocked.Increment(ref this.produced);
                }

                yield return StreamItem.FromJson(Json(i), i);
            }
        }
    }

    private sealed class DoublingTransform : FakeStage
    {
        public override async IAsyncEnumerable<StreamItem> ExecuteStreamAsync(
            ModuleExecutionContext context,
            IAsyncEnumerable<StreamItem> input,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var item in input.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                var n = ((JsonPayload)item.Payload).Value.GetProperty("n").GetInt32();
                yield return item with { Payload = new JsonPayload(Json(n * 2)) };
            }
        }
    }

    private sealed class ThrowingTransform : FakeStage
    {
        private readonly int failAt;

        public ThrowingTransform(int failAt) => this.failAt = failAt;

        public override async IAsyncEnumerable<StreamItem> ExecuteStreamAsync(
            ModuleExecutionContext context,
            IAsyncEnumerable<StreamItem> input,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var item in input.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                if (item.Index == this.failAt)
                {
                    throw new InvalidOperationException($"stage blew up on item {this.failAt}");
                }

                yield return item;
            }
        }
    }

    private sealed class CollectingTerminal : FakeStage, IStreamTerminalModule
    {
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
            var values = new List<int>();
            await foreach (var item in input.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                values.Add(((JsonPayload)item.Payload).Value.GetProperty("n").GetInt32());
            }

            return ModuleResult.Ok(new Dictionary<string, object?>
            {
                ["values"] = (IReadOnlyList<int>)values,
                ["count"] = (long)values.Count,
            });
        }
    }

    private sealed class SlowTerminal : FakeStage, IStreamTerminalModule
    {
        private readonly int consumeCount;

        public SlowTerminal(int consumeCount) => this.consumeCount = consumeCount;

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
            var seen = 0;
            await foreach (var _ in input.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                await Task.Delay(2, cancellationToken).ConfigureAwait(false);
                if (++seen >= this.consumeCount)
                {
                    break;
                }
            }

            return ModuleResult.Ok(new Dictionary<string, object?> { ["count"] = (long)seen });
        }
    }

    #endregion
}
