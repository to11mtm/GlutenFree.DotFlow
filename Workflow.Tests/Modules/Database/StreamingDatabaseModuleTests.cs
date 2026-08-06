// <copyright file="StreamingDatabaseModuleTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.Database;

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
using LinqToDB.Data;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Workflow.Core.Models;
using Workflow.Engine.Streaming;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin.Transform;
using Workflow.Modules.Database.Abstractions;
using Workflow.Modules.Database.Builtin;
using Workflow.Modules.Database.Configuration;
using Workflow.Modules.Database.Connections;
using Workflow.Modules.Database.Providers;
using Xunit;

/// <summary>
/// 🌊 Phase 5.1.4 — the streaming database family over real SQLite: a source that reads with
/// bounded memory, a sink that writes in batches, and the two wired together through the region
/// runner as an actual ETL~ 💾✨
/// </summary>
/// <remarks>
/// Docker-free by design (temp-file SQLite), so the bounded-memory claim is exercised on every
/// CI run rather than only in the gated Postgres suite~ 🌸.
/// </remarks>
public sealed class StreamingDatabaseModuleTests : IDisposable
{
    private readonly string dbPath = Path.Combine(Path.GetTempPath(), $"dotflow-stream-db-{Guid.NewGuid():N}.db");
    private readonly ActorSystem system = ActorSystem.Create("streaming-db-tests");
    private readonly IMaterializer materializer;
    private bool disposed;

    public StreamingDatabaseModuleTests() => this.materializer = this.system.Materializer();

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.system.Terminate().Wait(TimeSpan.FromSeconds(5));
        this.system.Dispose();

        if (File.Exists(this.dbPath))
        {
            File.Delete(this.dbPath);
        }

        this.disposed = true;
    }

    #region Streaming source

    [Fact]
    public async Task Query_StreamsEveryRow()
    {
        this.Seed(rows: 25);

        var items = await this.DrainAsync(new DatabaseQueryModule(), this.QueryContext("SELECT id, name FROM users ORDER BY id"));

        items.Should().HaveCount(25);
        items[0].Payload.Should().BeOfType<JsonPayload>()
            .Which.Value.GetProperty("name").GetString().Should().Be("user-0");
    }

    [Fact]
    public async Task Query_StampsRowOrdinalsAsOffsets()
    {
        this.Seed(rows: 5);

        var items = await this.DrainAsync(new DatabaseQueryModule(), this.QueryContext("SELECT id FROM users ORDER BY id"));

        items.Select(i => i.Index).Should().ContainInOrder(0L, 1L, 2L, 3L, 4L);
        items[3].Offset!.Sequence.Should().Be(3, "the row ordinal is what a future resume would seek with");
    }

    [Fact]
    public async Task Query_EmptyResult_YieldsNothing()
    {
        this.Seed(rows: 3);

        var items = await this.DrainAsync(
            new DatabaseQueryModule(),
            this.QueryContext("SELECT id FROM users WHERE id > 9999"));

        items.Should().BeEmpty();
    }

    [Fact]
    public async Task Query_DoesNotMaterialiseTheResultSet()
    {
        // 🎯 The point of the feature: stop reading early and the rest is never pulled.
        this.Seed(rows: 500);

        var seen = 0;
        await foreach (var _ in new DatabaseQueryModule()
                           .ExecuteStreamAsync(this.QueryContext("SELECT id, name FROM users ORDER BY id"), Empty()))
        {
            if (++seen >= 10)
            {
                break;
            }
        }

        seen.Should().Be(10, "a streaming reader must be abandonable without having read everything");
    }

    [Fact]
    public async Task Query_Cancellation_StopsTheReader()
    {
        this.Seed(rows: 200);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () =>
        {
            await foreach (var _ in new DatabaseQueryModule()
                               .ExecuteStreamAsync(this.QueryContext("SELECT id FROM users"), Empty(), cts.Token))
            {
                // drain
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void Query_DeclaresAStreamingOutputPort()
        => new DatabaseQueryModule().Schema.Outputs.ToArray()
            .Should().ContainSingle(p => p.Name == "items" && p.IsStreaming);

    #endregion

    #region Streaming sink

    [Fact]
    public async Task BulkInsert_DrainsAStreamIntoTheTable()
    {
        this.SeedEmptyTarget();

        var result = await new DatabaseBulkInsertModule().ExecuteTerminalAsync(
            this.InsertContext(batchSize: 10),
            Items(35));

        result.Success.Should().BeTrue();
        result.Outputs["insertedCount"].Should().Be(35);
        this.CountTarget().Should().Be(35);
    }

    [Fact]
    public async Task BulkInsert_WritesInBatches_NotOneBigStatement()
    {
        // A batch size smaller than the stream proves flushing happens mid-stream~ 💾
        this.SeedEmptyTarget();

        var result = await new DatabaseBulkInsertModule().ExecuteTerminalAsync(
            this.InsertContext(batchSize: 7),
            Items(30));

        result.Success.Should().BeTrue();
        this.CountTarget().Should().Be(30);
    }

    [Fact]
    public async Task BulkInsert_EmptyStream_SucceedsWithZero()
    {
        this.SeedEmptyTarget();

        var result = await new DatabaseBulkInsertModule().ExecuteTerminalAsync(this.InsertContext(), Empty());

        result.Success.Should().BeTrue();
        result.Outputs["insertedCount"].Should().Be(0);
    }

    [Fact]
    public async Task BulkInsert_NonObjectItem_FailsClearly()
    {
        this.SeedEmptyTarget();

        var result = await new DatabaseBulkInsertModule().ExecuteTerminalAsync(
            this.InsertContext(),
            Scalars());

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not an object");
    }

    #endregion

    #region End-to-end ETL through the region runner

    [Fact]
    public async Task Etl_QueryToMapToBulkInsert_RunsAsOneStreamingRegion()
    {
        // 🎉 The 5.1 headline: read → transform → write, all streaming, no materialisation.
        this.Seed(rows: 120);
        this.SeedEmptyTarget();

        var mapContext = this.Context("map-1", new Dictionary<string, object?>
        {
            ["mapping"] = new Dictionary<string, object?>
            {
                ["id"] = "id",
                ["name"] = "name",
            },
        });

        var plan = new StreamRegionPlan(
        [
            new StreamStage("read", new DatabaseQueryModule(), this.QueryContext("SELECT id, name FROM users ORDER BY id")),
            new StreamStage("map", new DataMapModule(), mapContext, MaxWorkers: 4, Ordered: true),
            new StreamStage("write", new DatabaseBulkInsertModule(), this.InsertContext(batchSize: 25)),
        ]);

        var result = await new StreamRegionRunner(this.materializer).RunAsync(plan);

        result.TerminalResult!.Success.Should().BeTrue(
            "the ETL should complete; error was: {0}", result.TerminalResult.ErrorMessage);
        result.TerminalResult.Outputs["insertedCount"].Should().Be(120);
        result.ItemCounts["read"].Should().Be(120);
        result.ItemCounts["map"].Should().Be(120);
        this.CountTarget().Should().Be(120);
    }

    #endregion

    #region Helpers

    private static async IAsyncEnumerable<StreamItem> Empty()
    {
        await Task.CompletedTask;
        yield break;
    }

    private static async IAsyncEnumerable<StreamItem> Items(int count)
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

    private static async IAsyncEnumerable<StreamItem> Scalars()
    {
        await Task.CompletedTask;
        yield return StreamItem.FromJson(JsonDocument.Parse("42").RootElement.Clone());
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

    private string SqliteConnectionString => $"Data Source={this.dbPath};Pooling=False";

    private DefaultDbConnectionFactory BuildFactory()
    {
        var options = new DatabaseConnectionsOptions();
        options.Connections["TestDb"] = new DbConnectionDescriptor("TestDb", "sqlite", this.SqliteConnectionString);
        return new DefaultDbConnectionFactory(
            new InMemoryDbConnectionRegistry(Options.Create(options)),
            new DefaultDbProviderRegistry());
    }

    private void Seed(int rows)
    {
        using DataConnection db = this.BuildFactory()
            .CreateAsync("sqlite", this.SqliteConnectionString).AsTask().GetAwaiter().GetResult();

        db.Execute("CREATE TABLE IF NOT EXISTS users (id INTEGER PRIMARY KEY, name TEXT)");
        for (var i = 0; i < rows; i++)
        {
            db.Execute(
                string.Create(CultureInfo.InvariantCulture, $"INSERT INTO users (id, name) VALUES ({i}, 'user-{i}')"));
        }
    }

    private void SeedEmptyTarget()
    {
        using DataConnection db = this.BuildFactory()
            .CreateAsync("sqlite", this.SqliteConnectionString).AsTask().GetAwaiter().GetResult();

        db.Execute("CREATE TABLE IF NOT EXISTS target (id INTEGER, name TEXT)");
    }

    private int CountTarget()
    {
        using DataConnection db = this.BuildFactory()
            .CreateAsync("sqlite", this.SqliteConnectionString).AsTask().GetAwaiter().GetResult();

        return db.Execute<int>("SELECT COUNT(*) FROM target");
    }

    private ModuleExecutionContext QueryContext(string query)
        => this.Context("read-1", new Dictionary<string, object?>
        {
            ["connectionString"] = this.SqliteConnectionString,
            ["provider"] = "sqlite",
            ["query"] = query,
        });

    private ModuleExecutionContext InsertContext(int batchSize = 500)
        => this.Context("write-1", new Dictionary<string, object?>
        {
            ["connectionString"] = this.SqliteConnectionString,
            ["provider"] = "sqlite",
            ["table"] = "target",
            ["batchSize"] = batchSize,
        });

    private ModuleExecutionContext Context(string nodeId, Dictionary<string, object?> properties)
        => new()
        {
            Inputs = new Dictionary<string, object?>(),
            Properties = properties,
            Variables = new Dictionary<string, object?>(),
            Logger = NullLogger.Instance,
            Services = new FactoryServiceProvider(this.BuildFactory()),
            ExecutionId = Guid.NewGuid(),
            NodeId = nodeId,
        };

    private sealed class FactoryServiceProvider : IServiceProvider
    {
        private readonly IDbConnectionFactory factory;
        private readonly Workflow.Engine.Services.JintExpressionEvaluator evaluator =
            new(NullLogger<Workflow.Engine.Services.JintExpressionEvaluator>.Instance);

        public FactoryServiceProvider(IDbConnectionFactory factory) => this.factory = factory;

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IDbConnectionFactory))
            {
                return this.factory;
            }

            // 🧮 The transform family resolves its evaluator from DI exactly as it does in the host;
            // without one, a mapping stage fails before it reaches any streaming logic~
            return serviceType == typeof(Workflow.Core.Abstractions.IExpressionEvaluator)
                ? this.evaluator
                : null;
        }
    }

    #endregion
}
