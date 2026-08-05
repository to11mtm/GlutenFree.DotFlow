// ────────────────────────────────────────────────────────────────────────────────
// Copyright © GlutenFree 2025. Made with love by Ami-Chan! UwU 💖
// Licensed under the MIT License.
// ────────────────────────────────────────────────────────────────────────────────

using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin.Stream;
using Workflow.Modules.Streaming;

namespace Workflow.Tests.Modules.Stream;

/// <summary>
/// Phase 5.1.1 — the stream ↔ batch bridges and the shared bounded-accumulator guard~ 🌉🛡️
/// </summary>
/// <remarks>
/// CopilotNote: No region executor exists yet (5.1.3), so these drive the modules directly with a
/// synthetic <c>IAsyncEnumerable</c> — which is exactly how the executor will call them~ ✨.
/// </remarks>
public class StreamBridgeTests
{
    #region Helpers

    private static ModuleExecutionContext Context(
        Dictionary<string, object?>? properties = null,
        Dictionary<string, object?>? inputs = null)
        => new()
        {
            Inputs = inputs ?? new Dictionary<string, object?>(),
            Properties = properties ?? new Dictionary<string, object?>(),
            Variables = new Dictionary<string, object?>(),
            Logger = NullLogger.Instance,
            Services = new ServiceCollection().BuildServiceProvider(),
            ExecutionId = Guid.NewGuid(),
            NodeId = "collect-1",
        };

    private static async IAsyncEnumerable<StreamItem> Items(
        int count,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return StreamItem.FromJson(
                JsonDocument.Parse($$"""{"n":{{i}}}""").RootElement.Clone(), i);
        }
    }

    private static async IAsyncEnumerable<StreamItem> Empty()
    {
        await Task.CompletedTask;
        yield break;
    }

    #endregion

    #region builtin.stream.collect

    [Fact]
    public async Task Collect_DrainsStreamIntoArrayWithCount()
    {
        var result = await new StreamCollectModule().ExecuteTerminalAsync(Context(), Items(3));

        result.Success.Should().BeTrue();
        result.Outputs["count"].Should().Be(3L);
        result.Outputs["items"].Should().BeAssignableTo<IReadOnlyList<object?>>()
            .Which.Should().HaveCount(3);
    }

    [Fact]
    public async Task Collect_EmptyStream_SucceedsWithZeroItems()
    {
        var result = await new StreamCollectModule().ExecuteTerminalAsync(Context(), Empty());

        result.Success.Should().BeTrue();
        result.Outputs["count"].Should().Be(0L);
    }

    [Fact]
    public async Task Collect_OverItemLimit_FailsLoudly()
    {
        var context = Context(new Dictionary<string, object?> { ["maxItems"] = 2 });

        var result = await new StreamCollectModule().ExecuteTerminalAsync(context, Items(10));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("more than 2 items")
            .And.Contain("collect-1", "the message must say which node to fix");
        result.Exception.Should().BeOfType<StreamAccumulatorLimitException>();
    }

    [Fact]
    public async Task Collect_OverByteLimit_FailsLoudly()
    {
        var context = Context(new Dictionary<string, object?> { ["maxBytes"] = 10 });

        var result = await new StreamCollectModule().ExecuteTerminalAsync(context, Items(50));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("byte limit");
    }

    [Fact]
    public async Task Collect_InvalidLimit_FailsWithGuidance()
    {
        var context = Context(new Dictionary<string, object?> { ["maxItems"] = -5 });

        var result = await new StreamCollectModule().ExecuteTerminalAsync(context, Items(1));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("positive whole number");
    }

    [Fact]
    public async Task Collect_LimitAcceptsJsonNumberProperties()
    {
        var context = Context(new Dictionary<string, object?>
        {
            ["maxItems"] = JsonDocument.Parse("2").RootElement.Clone(),
        });

        var result = await new StreamCollectModule().ExecuteTerminalAsync(context, Items(10));

        result.Success.Should().BeFalse("properties arrive as JsonElements from the definition");
    }

    [Fact]
    public async Task Collect_Cancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await new StreamCollectModule()
            .ExecuteTerminalAsync(Context(), Items(100), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Collect_InBatchMode_FailsWithWiringGuidance()
    {
        var result = await new StreamCollectModule().ExecuteAsync(Context());

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("streaming connection");
    }

    [Fact]
    public void Collect_DeclaresStreamingInputAndBatchOutputs()
    {
        var schema = new StreamCollectModule().Schema;

        schema.Inputs.ToArray().Should().ContainSingle().Which.IsStreaming.Should().BeTrue();
        schema.Outputs.ToArray().Should().OnlyContain(p => !p.IsStreaming, "collect leaves the streaming world");
    }

    #endregion

    #region builtin.stream.fromitems

    [Fact]
    public async Task FromItems_StreamsEachArrayElement()
    {
        var context = Context(inputs: new Dictionary<string, object?>
        {
            ["items"] = new List<object?> { 1, 2, 3 },
        });

        var items = new List<StreamItem>();
        await foreach (var item in new StreamFromItemsModule().ExecuteStreamAsync(context, Empty()))
        {
            items.Add(item);
        }

        items.Should().HaveCount(3);
        items.Select(i => i.Index).Should().ContainInOrder(0L, 1L, 2L);
        ((JsonPayload)items[1].Payload).Value.GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task FromItems_StampsSourceOffsetsForFutureResume()
    {
        var context = Context(inputs: new Dictionary<string, object?> { ["items"] = new[] { "a", "b" } });

        var items = new List<StreamItem>();
        await foreach (var item in new StreamFromItemsModule().ExecuteStreamAsync(context, Empty()))
        {
            items.Add(item);
        }

        items[1].Offset.Should().NotBeNull();
        items[1].Offset!.Sequence.Should().Be(1);
    }

    [Fact]
    public async Task FromItems_JsonArray_IsExpandedElementwise()
    {
        var context = Context(inputs: new Dictionary<string, object?>
        {
            ["items"] = JsonDocument.Parse("""[{"id":1},{"id":2}]""").RootElement.Clone(),
        });

        var count = 0;
        await foreach (var _ in new StreamFromItemsModule().ExecuteStreamAsync(context, Empty()))
        {
            count++;
        }

        count.Should().Be(2);
    }

    [Fact]
    public async Task FromItems_String_IsOneItemNotManyCharacters()
    {
        var context = Context(inputs: new Dictionary<string, object?> { ["items"] = "hello" });

        var items = new List<StreamItem>();
        await foreach (var item in new StreamFromItemsModule().ExecuteStreamAsync(context, Empty()))
        {
            items.Add(item);
        }

        items.Should().ContainSingle("a string must never be streamed as its characters");
    }

    [Fact]
    public async Task FromItems_MissingInput_YieldsEmptyStream()
    {
        var items = new List<StreamItem>();
        await foreach (var item in new StreamFromItemsModule().ExecuteStreamAsync(Context(), Empty()))
        {
            items.Add(item);
        }

        items.Should().BeEmpty();
    }

    [Fact]
    public void FromItems_DeclaresBatchInputAndStreamingOutput()
    {
        var schema = new StreamFromItemsModule().Schema;

        schema.Inputs.ToArray().Should().ContainSingle().Which.IsStreaming.Should().BeFalse();
        schema.Outputs.ToArray().Should().ContainSingle().Which.IsStreaming.Should().BeTrue();
    }

    #endregion

    #region BoundedAccumulator

    [Fact]
    public void Accumulator_DefaultPolicy_AllowsOrdinaryVolumes()
    {
        var accumulator = new BoundedAccumulator();

        for (var i = 0; i < 1_000; i++)
        {
            accumulator.Admit(StreamItem.FromJson(default, i), "n1");
        }

        accumulator.Count.Should().Be(1_000);
    }

    [Fact]
    public void Accumulator_SpillMode_IsRejectedUntilItExists()
    {
        var act = () => new BoundedAccumulator(
            new BoundedAccumulatorPolicy(OnLimit: AccumulatorLimitBehavior.Spill));

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*5.1.6*", "a knob that silently does nothing is worse than one that says 'not yet'");
    }

    [Fact]
    public void Accumulator_NullMaxItems_DisablesTheItemCeiling()
    {
        var accumulator = new BoundedAccumulator(new BoundedAccumulatorPolicy(MaxItems: null));

        for (var i = 0; i < 10_000; i++)
        {
            accumulator.Admit(StreamItem.FromJson(default, i), "n1");
        }

        accumulator.Count.Should().Be(10_000);
    }

    [Fact]
    public void Accumulator_WithoutByteCeiling_SkipsMeasurement()
    {
        var accumulator = new BoundedAccumulator(new BoundedAccumulatorPolicy(MaxBytes: null));

        accumulator.Admit(
            StreamItem.FromJson(JsonDocument.Parse("""{"a":"xxxxxxxxxx"}""").RootElement.Clone()),
            "n1");

        accumulator.EstimatedBytes.Should().Be(0, "measuring costs a GetRawText() we shouldn't pay for");
    }

    #endregion
}
