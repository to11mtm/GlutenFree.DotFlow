// <copyright file="PostgresBulkInsertTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Integration.Database;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LinqToDB.Data;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Database.Abstractions;
using Workflow.Modules.Database.Builtin;
using Workflow.Modules.Database.Configuration;
using Workflow.Modules.Database.Connections;
using Workflow.Modules.Database.Providers;
using Xunit;

/// <summary>
/// 🐘 Phase 2.4.a.4 — Postgres Testcontainers tests for <see cref="DatabaseBulkInsertModule"/>~ ✨💖.
/// </summary>
/// <remarks>
/// CopilotNote: Requires Docker. Marked [Trait("Category", "Integration")] so Docker-less CI skips it~ 🐳.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class PostgresBulkInsertTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder()
        .WithImage("postgres:15-alpine")
        .WithDatabase("bulk_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly DatabaseBulkInsertModule module = new();

    private string ConnectionString => this.container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await this.container.StartAsync();

        using DataConnection db = await this.Factory().CreateAsync("postgres", this.ConnectionString);
        db.Execute("CREATE TABLE products (id INT PRIMARY KEY, name TEXT, price NUMERIC(12,4))");
        db.Execute("CREATE TABLE events (id SERIAL PRIMARY KEY, payload JSONB, occurred TIMESTAMPTZ)");
    }

    public async Task DisposeAsync() => await this.container.DisposeAsync();

    [Fact]
    public async Task Postgres_BulkInsert_10kRows_AllInserted()
    {
        var data = new List<object>(10_000);
        for (var i = 1; i <= 10_000; i++)
        {
            data.Add(new Dictionary<string, object?> { ["id"] = i, ["name"] = $"p{i}", ["price"] = 1.50m });
        }

        var result = await this.Run("products", data.ToArray());

        result.Success.Should().BeTrue();
        result.Outputs["insertedCount"].Should().Be(10_000);
        this.Count("products").Should().Be(10_000);
    }

    [Fact]
    public async Task Postgres_BulkInsert_NumericTypes_PreservesPrecision()
    {
        var data = new object[]
        {
            new Dictionary<string, object?> { ["id"] = 1, ["name"] = "precise", ["price"] = 1234.5678m },
        };

        var result = await this.Run("products", data);

        result.Success.Should().BeTrue();
        using DataConnection db = this.Factory().CreateAsync("postgres", this.ConnectionString).AsTask().GetAwaiter().GetResult();
        db.Execute<decimal>("SELECT price FROM products WHERE id = 1").Should().Be(1234.5678m);
    }

    [Fact]
    public async Task Postgres_BulkInsert_TimestamptzColumn_PreservesOffset()
    {
        var when = new DateTimeOffset(2026, 7, 16, 10, 30, 0, TimeSpan.FromHours(2));
        var data = new object[]
        {
            new Dictionary<string, object?> { ["payload"] = null, ["occurred"] = when },
        };

        var result = await this.Run("events", data);

        result.Success.Should().BeTrue();
        using DataConnection db = this.Factory().CreateAsync("postgres", this.ConnectionString).AsTask().GetAwaiter().GetResult();
        var stored = db.Execute<DateTimeOffset>("SELECT occurred FROM events LIMIT 1");
        stored.ToUniversalTime().Should().Be(when.ToUniversalTime());
    }

    [Fact]
    public async Task Postgres_BulkInsert_ReturningGeneratedIds_RoundTrips()
    {
        var data = new object[]
        {
            new Dictionary<string, object?> { ["payload"] = null, ["occurred"] = DateTimeOffset.UtcNow },
            new Dictionary<string, object?> { ["payload"] = null, ["occurred"] = DateTimeOffset.UtcNow },
        };

        var result = await this.Run("events", data, returningColumns: new[] { "id" });

        result.Success.Should().BeTrue();
        result.Outputs["insertedCount"].Should().Be(2);
        var outputRows = (IReadOnlyList<IReadOnlyDictionary<string, object?>>)result.Outputs["outputRows"]!;
        outputRows.Should().HaveCount(2);
        Convert.ToInt64(outputRows[0]["id"]).Should().BeGreaterThan(0);
        Convert.ToInt64(outputRows[1]["id"]).Should().BeGreaterThan(Convert.ToInt64(outputRows[0]["id"]) - 1);
    }

    [Fact]
    public async Task Postgres_BulkInsert_ForeignKeyOrConstraintViolation_RollsBack()
    {
        var data = new object[]
        {
            new Dictionary<string, object?> { ["id"] = 1, ["name"] = "a", ["price"] = 1m },
            new Dictionary<string, object?> { ["id"] = 1, ["name"] = "dup", ["price"] = 2m }, // PK dup in-batch
        };

        var result = await this.Run("products", data);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Bulk insert failed");
        this.Count("products").Should().Be(0, "rolled back~ 🛡️");
    }

    #region Helpers 🛠️

    private DefaultDbConnectionFactory Factory()
    {
        var options = new DatabaseConnectionsOptions();
        options.Connections["Pg"] = new DbConnectionDescriptor("Pg", "postgres", this.ConnectionString);
        var registry = new InMemoryDbConnectionRegistry(Options.Create(options));
        return new DefaultDbConnectionFactory(registry, new DefaultDbProviderRegistry());
    }

    private int Count(string table)
    {
        using DataConnection db = this.Factory().CreateAsync("postgres", this.ConnectionString).AsTask().GetAwaiter().GetResult();
        return db.Execute<int>($"SELECT COUNT(*) FROM {table}");
    }

    private Task<ModuleResult> Run(string tableName, object[] data, string[]? returningColumns = null)
    {
        var props = new Dictionary<string, object?>
        {
            ["connectionId"] = "Pg",
            ["tableName"] = tableName,
            ["data"] = data,
        };
        if (returningColumns is not null)
        {
            props["returningColumns"] = returningColumns;
        }

        var ctx = new ModuleExecutionContext
        {
            Inputs = new Dictionary<string, object?>(),
            Properties = props,
            Variables = new Dictionary<string, object?>(),
            Logger = NullLogger.Instance,
            Services = new FactoryServiceProvider(this.Factory()),
            ExecutionId = Guid.NewGuid(),
            NodeId = "pg-bulk-node",
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

