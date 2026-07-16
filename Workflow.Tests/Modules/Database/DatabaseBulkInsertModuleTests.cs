// <copyright file="DatabaseBulkInsertModuleTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.Database;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LinqToDB.Data;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Database.Abstractions;
using Workflow.Modules.Database.Builtin;
using Workflow.Modules.Database.Configuration;
using Workflow.Modules.Database.Connections;
using Workflow.Modules.Database.Providers;
using Xunit;

/// <summary>
/// 📊 Phase 2.4.a.4 — SQLite unit tests for <see cref="DatabaseBulkInsertModule"/>~ ✨💖.
/// </summary>
public sealed class DatabaseBulkInsertModuleTests : IDisposable
{
    private readonly string dbPath = Path.Combine(Path.GetTempPath(), $"dotflow-bulk-test-{Guid.NewGuid():N}.db");
    private readonly DatabaseBulkInsertModule module = new();

    public void Dispose()
    {
        if (File.Exists(this.dbPath))
        {
            File.Delete(this.dbPath);
        }
    }

    [Fact]
    public void BulkInsertModule_Metadata_IsCorrect()
    {
        this.module.ModuleId.Should().Be("builtin.database.bulkinsert");
        this.module.Category.Should().Be("Database");
        this.module.Icon.Should().Be("📊");
        this.module.Version.Should().Be(new Version(1, 0, 0));
    }

    [Fact]
    public async Task BulkInsert_100Rows_AllInserted()
    {
        this.Seed();
        var data = new List<object>();
        for (var i = 1; i <= 100; i++)
        {
            data.Add(new Dictionary<string, object?> { ["id"] = i, ["name"] = $"n{i}" });
        }

        var result = await this.Run("items", data.ToArray());

        result.Success.Should().BeTrue();
        result.Outputs["insertedCount"].Should().Be(100);
        this.CountItems().Should().Be(100);
    }

    [Fact]
    public async Task BulkInsert_EmptyData_ReturnsZeroInserted()
    {
        this.Seed();
        var result = await this.Run("items", Array.Empty<object>());

        result.Success.Should().BeTrue();
        result.Outputs["insertedCount"].Should().Be(0);
    }

    [Fact]
    public async Task BulkInsert_SmallBatchSize_With95Rows_AllInserted()
    {
        this.Seed();
        var data = new List<object>();
        for (var i = 1; i <= 95; i++)
        {
            data.Add(new Dictionary<string, object?> { ["id"] = i, ["name"] = $"n{i}" });
        }

        var result = await this.Run("items", data.ToArray(), batchSize: 10);

        result.Success.Should().BeTrue();
        result.Outputs["insertedCount"].Should().Be(95, "10 full batches of 10 + one partial batch of 5~ 📦");
        this.CountItems().Should().Be(95);
    }

    [Fact]
    public async Task BulkInsert_ColumnMapping_AppliesCorrectly()
    {
        this.Seed();
        var data = new object[]
        {
            new Dictionary<string, object?> { ["Identifier"] = 1, ["Label"] = "alpha" },
            new Dictionary<string, object?> { ["Identifier"] = 2, ["Label"] = "beta" },
        };
        var mapping = new Dictionary<string, object?> { ["Identifier"] = "id", ["Label"] = "name" };

        var result = await this.Run("items", data, columnMapping: mapping);

        result.Success.Should().BeTrue();
        result.Outputs["insertedCount"].Should().Be(2);
        this.NameOf(1).Should().Be("alpha");
    }

    [Fact]
    public async Task BulkInsert_MissingColumn_InsertsNull()
    {
        this.Seed();
        var data = new object[]
        {
            new Dictionary<string, object?> { ["id"] = 1, ["name"] = "has-name" },
            new Dictionary<string, object?> { ["id"] = 2 }, // no 'name' → NULL
        };

        var result = await this.Run("items", data);

        result.Success.Should().BeTrue();
        this.NameOf(2).Should().BeNull("missing key inserts NULL~ 🌸");
    }

    [Fact]
    public async Task BulkInsert_NullableColumns_HandlesNullsCorrectly()
    {
        this.Seed();
        var data = new object[]
        {
            new Dictionary<string, object?> { ["id"] = 1, ["name"] = null },
        };

        var result = await this.Run("items", data);

        result.Success.Should().BeTrue();
        this.NameOf(1).Should().BeNull();
    }

    [Fact]
    public async Task BulkInsert_UniqueConstraintViolation_FailsAndRollsBack()
    {
        this.Seed();

        // batchSize=1 → row 10 commits in batch 1, then the duplicate id=10 fails in batch 2;
        // the whole transaction must roll back (including the first batch)~ 🛡️
        var data = new object[]
        {
            new Dictionary<string, object?> { ["id"] = 10, ["name"] = "a" },
            new Dictionary<string, object?> { ["id"] = 10, ["name"] = "dupe" },
        };

        var result = await this.Run("items", data, batchSize: 1);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Bulk insert failed");
        this.CountItems().Should().Be(0, "the whole transaction rolled back~ 🛡️");
    }

    [Fact]
    public async Task BulkInsert_TypeMismatch_FailsWithRowIndex()
    {
        this.Seed();
        var data = new object[]
        {
            new Dictionary<string, object?> { ["id"] = 1, ["name"] = "ok" },
            new Dictionary<string, object?> { ["id"] = 2, ["name"] = new object() }, // unsupported type at row 1
        };

        var result = await this.Run("items", data);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("row 1");
    }

    [Fact]
    public async Task BulkInsert_ReturningColumns_PopulatesOutputRows()
    {
        this.Seed();
        var data = new object[]
        {
            new Dictionary<string, object?> { ["name"] = "first" },
            new Dictionary<string, object?> { ["name"] = "second" },
        };

        var result = await this.Run("auto_items", data, returningColumns: new[] { "id" });

        result.Success.Should().BeTrue();
        result.Outputs["insertedCount"].Should().Be(2);
        var outputRows = (IReadOnlyList<IReadOnlyDictionary<string, object?>>)result.Outputs["outputRows"]!;
        outputRows.Should().HaveCount(2);
        Convert.ToInt64(outputRows[0]["id"]).Should().BeGreaterThan(0);
    }

    [Fact]
    public void ValidateConfiguration_MissingTableName_Fails()
    {
        var result = this.module.ValidateConfiguration(new Dictionary<string, object?>
        {
            ["connectionId"] = "TestDb",
            ["data"] = Array.Empty<object>(),
        });

        result.IsValid.Should().BeFalse();
    }

    #region Helpers 🛠️

    private string SqliteConnectionString => $"Data Source={this.dbPath};Pooling=False";

    private DefaultDbConnectionFactory BuildFactory()
    {
        var options = new DatabaseConnectionsOptions();
        options.Connections["TestDb"] = new DbConnectionDescriptor("TestDb", "sqlite", this.SqliteConnectionString);
        var registry = new InMemoryDbConnectionRegistry(Options.Create(options));
        return new DefaultDbConnectionFactory(registry, new DefaultDbProviderRegistry());
    }

    private void Seed()
    {
        var factory = this.BuildFactory();
        using DataConnection db = factory.CreateAsync("sqlite", this.SqliteConnectionString).AsTask().GetAwaiter().GetResult();

        // NOTE: 'items' starts EMPTY so bulk inserts of ids 1..N don't collide with seed rows~ 🌸
        db.Execute("CREATE TABLE items (id INTEGER PRIMARY KEY, name TEXT)");
        db.Execute("CREATE TABLE auto_items (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT)");
    }

    private int CountItems()
    {
        var factory = this.BuildFactory();
        using DataConnection db = factory.CreateAsync("sqlite", this.SqliteConnectionString).AsTask().GetAwaiter().GetResult();
        return db.Execute<int>("SELECT COUNT(*) FROM items");
    }

    private string? NameOf(int id)
    {
        var factory = this.BuildFactory();
        using DataConnection db = factory.CreateAsync("sqlite", this.SqliteConnectionString).AsTask().GetAwaiter().GetResult();
        return db.Execute<string?>("SELECT name FROM items WHERE id = @id", new DataParameter("id", id));
    }

    private Task<ModuleResult> Run(
        string tableName,
        object[] data,
        Dictionary<string, object?>? columnMapping = null,
        string[]? returningColumns = null,
        int? batchSize = null)
    {
        var props = new Dictionary<string, object?>
        {
            ["connectionId"] = "TestDb",
            ["tableName"] = tableName,
            ["data"] = data,
        };
        if (columnMapping is not null)
        {
            props["columnMapping"] = columnMapping;
        }

        if (returningColumns is not null)
        {
            props["returningColumns"] = returningColumns;
        }

        if (batchSize is not null)
        {
            props["batchSize"] = batchSize.Value;
        }

        var ctx = new ModuleExecutionContext
        {
            Inputs = new Dictionary<string, object?>(),
            Properties = props,
            Variables = new Dictionary<string, object?>(),
            Logger = NullLogger.Instance,
            Services = new FactoryServiceProvider(this.BuildFactory()),
            ExecutionId = Guid.NewGuid(),
            NodeId = "db-bulk-node",
        };

        return this.module.ExecuteAsync(ctx, CancellationToken.None);
    }

    private sealed class FactoryServiceProvider : IServiceProvider
    {
        private readonly IDbConnectionFactory factory;

        public FactoryServiceProvider(IDbConnectionFactory factory) => this.factory = factory;

        public object? GetService(Type serviceType)
            => serviceType == typeof(IDbConnectionFactory) ? this.factory : null;
    }

    #endregion
}



