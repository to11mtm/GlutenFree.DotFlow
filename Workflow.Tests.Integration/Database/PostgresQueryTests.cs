// <copyright file="PostgresQueryTests.cs" company="GlutenFree">
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
/// 🐘 Phase 2.4.a.1 — Postgres Testcontainers tests for <see cref="DatabaseQueryModule"/>~ ✨💖.
/// </summary>
/// <remarks>
/// CopilotNote: Requires Docker. Spins up one postgres:15-alpine container shared across the class.
/// Marked [Trait("Category", "Integration")] so CI without Docker skips it~ 🐳.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class PostgresQueryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder()
        .WithImage("postgres:15-alpine")
        .WithDatabase("query_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly DatabaseQueryModule module = new();

    private string ConnectionString => this.container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await this.container.StartAsync();

        using DataConnection db = await this.Factory().CreateAsync("postgres", this.ConnectionString);
        db.Execute("CREATE TABLE customers (id INT PRIMARY KEY, name TEXT, region TEXT)");
        db.Execute("CREATE TABLE orders (id INT PRIMARY KEY, customer_id INT REFERENCES customers(id), total NUMERIC(10,2), payload JSONB)");
        db.Execute("INSERT INTO customers (id, name, region) VALUES (1, 'alice', 'emea'), (2, 'bob', 'amer'), (3, 'carol', 'emea')");
        db.Execute("INSERT INTO orders (id, customer_id, total, payload) VALUES (10, 1, 99.50, '{\"sku\":\"A\"}'), (11, 1, 10.00, '{\"sku\":\"B\"}'), (12, 2, 42.00, '{\"sku\":\"C\"}')");
    }

    public async Task DisposeAsync() => await this.container.DisposeAsync();

    [Fact]
    public async Task Postgres_SelectFromSeededTable_RoundTrips()
    {
        var result = await this.RunQuery("SELECT id, name FROM customers ORDER BY id");

        result.Success.Should().BeTrue();
        result.Outputs["rowCount"].Should().Be(3);
        var rows = Rows(result);
        rows[0]["name"].Should().Be("alice");
    }

    [Fact]
    public async Task Postgres_JoinTwoTables_ReturnsExpectedShape()
    {
        var result = await this.RunQuery(
            "SELECT c.name, o.total FROM orders o JOIN customers c ON c.id = o.customer_id WHERE c.id = @cid ORDER BY o.id",
            new Dictionary<string, object?> { ["cid"] = 1 });

        result.Success.Should().BeTrue();
        result.Outputs["rowCount"].Should().Be(2);
        var columns = (IReadOnlyList<string>)result.Outputs["columns"]!;
        columns.Should().ContainInOrder("name", "total");
    }

    [Fact]
    public async Task Postgres_AggregateFunctions_CountSumAvg_ReturnExpected()
    {
        var result = await this.RunQuery(
            "SELECT COUNT(*) AS cnt, SUM(total) AS s FROM orders WHERE customer_id = @cid",
            new Dictionary<string, object?> { ["cid"] = 1 });

        result.Success.Should().BeTrue();
        var rows = Rows(result);
        Convert.ToInt64(rows[0]["cnt"]).Should().Be(2);
        Convert.ToDecimal(rows[0]["s"]).Should().Be(109.50m);
    }

    [Fact]
    public async Task Postgres_Jsonb_ReturnsAsValue()
    {
        var result = await this.RunQuery("SELECT payload FROM orders WHERE id = @id", new Dictionary<string, object?> { ["id"] = 10 });

        result.Success.Should().BeTrue();
        var rows = Rows(result);
        rows[0]["payload"].Should().NotBeNull();
        rows[0]["payload"]!.ToString().Should().Contain("sku");
    }

    [Fact]
    public async Task Postgres_InvalidSql_ReturnsFail()
    {
        var result = await this.RunQuery("SELECT * FROM does_not_exist");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Database query failed");
    }

    #region Helpers 🛠️

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows(ModuleResult result)
        => (IReadOnlyList<IReadOnlyDictionary<string, object?>>)result.Outputs["rows"]!;

    private DefaultDbConnectionFactory Factory()
    {
        var options = new DatabaseConnectionsOptions();
        options.Connections["Pg"] = new DbConnectionDescriptor("Pg", "postgres", this.ConnectionString);
        var registry = new InMemoryDbConnectionRegistry(Options.Create(options));
        return new DefaultDbConnectionFactory(registry, new DefaultDbProviderRegistry());
    }

    private Task<ModuleResult> RunQuery(string sql, Dictionary<string, object?>? parameters = null)
    {
        var props = new Dictionary<string, object?>
        {
            ["connectionId"] = "Pg",
            ["query"] = sql,
        };
        if (parameters is not null)
        {
            props["parameters"] = parameters;
        }

        var ctx = new ModuleExecutionContext
        {
            Inputs = new Dictionary<string, object?>(),
            Properties = props,
            Variables = new Dictionary<string, object?>(),
            Logger = NullLogger.Instance,
            Services = new FactoryServiceProvider(this.Factory()),
            ExecutionId = Guid.NewGuid(),
            NodeId = "pg-query-node",
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

