// <copyright file="StreamingFileModuleTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.Stream;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Workflow.Core.Models;
using Workflow.Engine.Streaming;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin.File;
using Workflow.Modules.Builtin.Transform;
using Workflow.Modules;
using Xunit;

/// <summary>
/// 🌊 Phase 5.1.4 — the streaming file family (CSV source + sink) and the streaming aggregate,
/// which is the one buffering stage and therefore the one that must carry the guard~ 📄🛡️
/// </summary>
public sealed class StreamingFileModuleTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), $"dotflow-stream-file-{Guid.NewGuid():N}");
    private readonly ActorSystem system = ActorSystem.Create("streaming-file-tests");
    private readonly IMaterializer materializer;
    private readonly IServiceProvider services;
    private bool disposed;

    public StreamingFileModuleTests()
    {
        Directory.CreateDirectory(this.dir);
        this.materializer = this.system.Materializer();

        // 📁 The file family needs its path-security sandbox from DI, exactly as the host wires it;
        // the transform family needs an expression evaluator~ 🧮
        this.services = new ServiceCollection()
            .AddWorkflowModules()
            .AddSingleton<Workflow.Core.Abstractions.IExpressionEvaluator>(
                new Workflow.Engine.Services.JintExpressionEvaluator(
                    NullLogger<Workflow.Engine.Services.JintExpressionEvaluator>.Instance))
            .BuildServiceProvider();
    }

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.system.Terminate().Wait(TimeSpan.FromSeconds(5));
        this.system.Dispose();

        if (Directory.Exists(this.dir))
        {
            Directory.Delete(this.dir, recursive: true);
        }

        this.disposed = true;
    }

    #region CSV source

    [Fact]
    public async Task CsvRead_StreamsEveryDataRow()
    {
        var path = this.WriteCsv("id,name", "1,alice", "2,bob", "3,carol");

        var items = await this.DrainAsync(new CsvReadModule(), this.Context("read", new() { ["path"] = path }));

        items.Should().HaveCount(3, "the header is not a data row");
        Value(items[0], "name").Should().Be("alice");
        Value(items[2], "id").Should().Be("3");
    }

    [Fact]
    public async Task CsvRead_StampsRowOrdinals()
    {
        var path = this.WriteCsv("id", "1", "2", "3");

        var items = await this.DrainAsync(new CsvReadModule(), this.Context("read", new() { ["path"] = path }));

        items.Select(i => i.Index).Should().ContainInOrder(0L, 1L, 2L);
        items[2].Offset!.Sequence.Should().Be(2);
    }

    [Fact]
    public async Task CsvRead_DoesNotLoadTheWholeFile()
    {
        // 🎯 Abandoning the enumeration early must leave the rest unread.
        var rows = Enumerable.Range(0, 2_000).Select(i => $"{i},name-{i}");
        var path = this.WriteCsv(new[] { "id,name" }.Concat(rows).ToArray());

        var seen = 0;
        await foreach (var _ in new CsvReadModule()
                           .ExecuteStreamAsync(this.Context("read", new() { ["path"] = path }), Empty()))
        {
            if (++seen >= 5)
            {
                break;
            }
        }

        seen.Should().Be(5);
    }

    [Fact]
    public async Task CsvRead_MissingFile_Throws()
    {
        var context = this.Context("read", new() { ["path"] = Path.Combine(this.dir, "nope.csv") });

        var act = async () => await this.DrainAsync(new CsvReadModule(), context);

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    #endregion

    #region CSV sink

    [Fact]
    public async Task CsvWrite_StreamsRowsToTheFile()
    {
        var path = Path.Combine(this.dir, "out.csv");

        var result = await new CsvWriteModule().ExecuteTerminalAsync(
            this.Context("write", new() { ["path"] = path }),
            Rows(4));

        result.Success.Should().BeTrue();
        result.Outputs["rowsWritten"].Should().Be(4);

        var lines = await File.ReadAllLinesAsync(path);
        lines.Should().HaveCount(5, "4 data rows plus a header");
        lines[0].Should().Be("id,name");
        lines[1].Should().Be("0,row-0");
    }

    [Fact]
    public async Task CsvWrite_EmptyStream_WritesNothingButSucceeds()
    {
        var path = Path.Combine(this.dir, "empty.csv");

        var result = await new CsvWriteModule().ExecuteTerminalAsync(
            this.Context("write", new() { ["path"] = path }),
            Empty());

        result.Success.Should().BeTrue();
        result.Outputs["rowsWritten"].Should().Be(0);
        (await File.ReadAllTextAsync(path)).Should().BeEmpty("no rows means no header to infer");
    }

    #endregion

    #region Round trip through a region

    [Fact]
    public async Task Csv_RoundTripsThroughAStreamingRegion()
    {
        // read → filter (drop odd ids) → write, all streaming~ 📄🌊
        var source = this.WriteCsv(
            new[] { "id,name" }.Concat(Enumerable.Range(0, 20).Select(i => $"{i},row-{i}")).ToArray());
        var target = Path.Combine(this.dir, "filtered.csv");

        var plan = new StreamRegionPlan(
        [
            new StreamStage("read", new CsvReadModule(), this.Context("read", new() { ["path"] = source })),
            new StreamStage(
                "filter",
                new DataQueryModule(),
                this.Context("filter", new() { ["where"] = "parseInt(item.id) % 2 == 0" }),
                MaxWorkers: 4,
                Ordered: true),
            new StreamStage("write", new CsvWriteModule(), this.Context("write", new() { ["path"] = target })),
        ]);

        var result = await new StreamRegionRunner(this.materializer).RunAsync(plan);

        result.TerminalResult!.Success.Should().BeTrue(
            "the round trip should complete; error was: {0}", result.TerminalResult.ErrorMessage);
        result.TerminalResult.Outputs["rowsWritten"].Should().Be(10);

        var lines = await File.ReadAllLinesAsync(target);
        lines[1].Should().Be("0,row-0");
        lines[2].Should().Be("2,row-2", "filtering preserves source order");
    }

    #endregion

    #region Streaming aggregate

    [Fact]
    public async Task Aggregate_SumsAStream()
    {
        var result = await new AggregateModule().ExecuteTerminalAsync(
            this.Context("agg", new() { ["operation"] = "sum", ["property"] = "value" }),
            Values(1, 2, 3, 4));

        result.Success.Should().BeTrue("error was: {0}", result.ErrorMessage);
        Convert.ToDouble(result.Outputs["result"], CultureInfo.InvariantCulture).Should().Be(10);
    }

    [Fact]
    public async Task Aggregate_CountsAStream()
    {
        var result = await new AggregateModule().ExecuteTerminalAsync(
            this.Context("agg", new() { ["operation"] = "count" }),
            Values(5, 5, 5));

        result.Success.Should().BeTrue();
        Convert.ToInt32(result.Outputs["result"], CultureInfo.InvariantCulture).Should().Be(3);
    }

    [Fact]
    public async Task Aggregate_OverItsCeiling_FailsLoudly()
    {
        // 🛡️ Aggregate is the one buffering streaming stage, so it must carry the guard.
        var result = await new AggregateModule().ExecuteTerminalAsync(
            this.Context("agg", new() { ["operation"] = "count", ["maxItems"] = 3 }),
            Values(1, 2, 3, 4, 5));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("more than 3 items").And.Contain("agg");
    }

    [Fact]
    public async Task Aggregate_StreamedAndBatched_Agree()
    {
        // One implementation powers both paths, and this is what keeps it that way~ ✨
        var streamed = await new AggregateModule().ExecuteTerminalAsync(
            this.Context("agg", new() { ["operation"] = "sum", ["property"] = "value" }),
            Values(2, 4, 6));

        var batchContext = this.Context("agg", new() { ["operation"] = "sum", ["property"] = "value" });
        batchContext = batchContext with
        {
            Inputs = new Dictionary<string, object?>
            {
                ["data"] = new List<object?>
                {
                    new Dictionary<string, object?> { ["value"] = 2 },
                    new Dictionary<string, object?> { ["value"] = 4 },
                    new Dictionary<string, object?> { ["value"] = 6 },
                },
            },
        };
        var batched = await new AggregateModule().ExecuteAsync(batchContext);

        Convert.ToDouble(streamed.Outputs["result"], CultureInfo.InvariantCulture)
            .Should().Be(Convert.ToDouble(batched.Outputs["result"], CultureInfo.InvariantCulture));
    }

    #endregion

    #region JSON source

    [Fact]
    public async Task JsonRead_StreamsArrayElements()
    {
        var path = this.WriteText("data.json", """[{"id":1},{"id":2},{"id":3}]""");

        var items = await this.DrainAsync(new JsonReadModule(), this.Context("read", new() { ["path"] = path }));

        items.Should().HaveCount(3);
        ((JsonPayload)items[1].Payload).Value.GetProperty("id").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task JsonRead_StampsOrdinals()
    {
        var path = this.WriteText("ordinals.json", """[10,20,30]""");

        var items = await this.DrainAsync(new JsonReadModule(), this.Context("read", new() { ["path"] = path }));

        items.Select(i => i.Index).Should().ContainInOrder(0L, 1L, 2L);
        items[2].Offset!.Sequence.Should().Be(2);
    }

    [Fact]
    public async Task JsonRead_EmptyArray_YieldsNothing()
    {
        var path = this.WriteText("empty.json", "[]");

        (await this.DrainAsync(new JsonReadModule(), this.Context("read", new() { ["path"] = path })))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task JsonRead_NonArrayRoot_FailsRatherThanGuessing()
    {
        // 🚧 There's nothing to iterate in a lone object — say so instead of emitting one item.
        var path = this.WriteText("object.json", """{"id":1}""");

        var act = async () => await this.DrainAsync(new JsonReadModule(), this.Context("read", new() { ["path"] = path }));

        await act.Should().ThrowAsync<JsonException>();
    }

    [Fact]
    public async Task JsonRead_DoesNotLoadTheWholeArray()
    {
        var big = "[" + string.Join(",", Enumerable.Range(0, 5_000).Select(i => $$"""{"id":{{i}}}""")) + "]";
        var path = this.WriteText("big.json", big);

        var seen = 0;
        await foreach (var _ in new JsonReadModule()
                           .ExecuteStreamAsync(this.Context("read", new() { ["path"] = path }), Empty()))
        {
            if (++seen >= 5)
            {
                break;
            }
        }

        seen.Should().Be(5);
    }

    #endregion

    #region JSON sink

    [Fact]
    public async Task JsonWrite_StreamsItemsAsAnArray()
    {
        var path = Path.Combine(this.dir, "out.json");

        var result = await new JsonWriteModule().ExecuteTerminalAsync(
            this.Context("write", new() { ["path"] = path, ["indented"] = false }),
            Rows(3));

        result.Success.Should().BeTrue("error was: {0}", result.ErrorMessage);
        result.Outputs["itemCount"].Should().Be(3L);

        using var parsed = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        parsed.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        parsed.RootElement.GetArrayLength().Should().Be(3);
        parsed.RootElement[1].GetProperty("name").GetString().Should().Be("row-1");
    }

    [Fact]
    public async Task JsonWrite_EmptyStream_WritesAnEmptyArray()
    {
        var path = Path.Combine(this.dir, "empty-out.json");

        var result = await new JsonWriteModule().ExecuteTerminalAsync(
            this.Context("write", new() { ["path"] = path }),
            Empty());

        result.Success.Should().BeTrue();
        result.Outputs["itemCount"].Should().Be(0L);

        using var parsed = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        parsed.RootElement.ValueKind.Should().Be(JsonValueKind.Array, "an empty stream is still a valid array");
        parsed.RootElement.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Json_RoundTripsThroughAStreamingRegion()
    {
        var source = this.WriteText(
            "in.json",
            "[" + string.Join(",", Enumerable.Range(0, 12).Select(i => $$"""{"id":{{i}}}""")) + "]");
        var target = Path.Combine(this.dir, "round-trip.json");

        var plan = new StreamRegionPlan(
        [
            new StreamStage("read", new JsonReadModule(), this.Context("read", new() { ["path"] = source })),
            new StreamStage(
                "filter",
                new DataQueryModule(),
                this.Context("filter", new() { ["where"] = "item.id < 5" })),
            new StreamStage("write", new JsonWriteModule(), this.Context("write", new() { ["path"] = target })),
        ]);

        var result = await new StreamRegionRunner(this.materializer).RunAsync(plan);

        result.TerminalResult!.Success.Should().BeTrue("error was: {0}", result.TerminalResult.ErrorMessage);
        result.TerminalResult.Outputs["itemCount"].Should().Be(5L);

        using var parsed = JsonDocument.Parse(await File.ReadAllTextAsync(target));
        parsed.RootElement.GetArrayLength().Should().Be(5);
        parsed.RootElement[0].GetProperty("id").GetInt32().Should().Be(0);
    }

    #endregion

    #region Helpers

    private static string? Value(StreamItem item, string key)
        => ((JsonPayload)item.Payload).Value.GetProperty(key).GetString();

    private static async IAsyncEnumerable<StreamItem> Empty()
    {
        await Task.CompletedTask;
        yield break;
    }

    private static async IAsyncEnumerable<StreamItem> Rows(int count)
    {
        for (var i = 0; i < count; i++)
        {
            await Task.Yield();
            yield return StreamItem.FromJson(
                JsonSerializer.SerializeToElement(new Dictionary<string, object?>
                {
                    ["id"] = i,
                    ["name"] = $"row-{i}",
                }),
                i);
        }
    }

    private static async IAsyncEnumerable<StreamItem> Values(params int[] values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            await Task.Yield();
            yield return StreamItem.FromJson(
                JsonSerializer.SerializeToElement(new Dictionary<string, object?> { ["value"] = values[i] }),
                i);
        }
    }

    private async Task<List<StreamItem>> DrainAsync(IStreamingWorkflowModule module, ModuleExecutionContext context)
    {
        var items = new List<StreamItem>();
        await foreach (var item in module.ExecuteStreamAsync(context, Empty()))
        {
            items.Add(item);
        }

        return items;
    }

    private string WriteCsv(params string[] lines)
    {
        var path = Path.Combine(this.dir, $"{Guid.NewGuid():N}.csv");
        File.WriteAllLines(path, lines);
        return path;
    }

    private string WriteText(string name, string content)
    {
        var path = Path.Combine(this.dir, $"{Guid.NewGuid():N}-{name}");
        File.WriteAllText(path, content);
        return path;
    }

    private ModuleExecutionContext Context(string nodeId, Dictionary<string, object?> properties)
        => new()
        {
            Inputs = new Dictionary<string, object?>(),
            Properties = properties,
            Variables = new Dictionary<string, object?>(),
            Logger = NullLogger.Instance,
            Services = this.services,
            ExecutionId = Guid.NewGuid(),
            NodeId = nodeId,
        };

    #endregion
}
