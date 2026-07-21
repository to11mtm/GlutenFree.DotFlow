// <copyright file="PostgresExecuteTests.cs" company="GlutenFree">
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
/// 🐘 Phase 2.4.a.2 — Postgres Testcontainers tests for <see cref="DatabaseExecuteModule"/>~ ✨💖.
/// </summary>
/// <remarks>
/// CopilotNote: Requires Docker. Marked [Trait("Category", "Integration")] so Docker-less CI skips it~ 🐳.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class PostgresExecuteTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder()
        .WithImage("postgres:15-alpine")
        .WithDatabase("exec_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly DatabaseExecuteModule module = new();

    private string ConnectionString => this.container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await this.container.StartAsync();

        using DataConnection db = await this.Factory().CreateAsync("postgres", this.ConnectionString);
        db.Execute("CREATE TABLE customers (id INT PRIMARY KEY, name TEXT)");
        db.Execute("CREATE TABLE orders (id SERIAL PRIMARY KEY, customer_id INT REFERENCES customers(id) ON DELETE CASCADE, total NUMERIC(10,2))");
        db.Execute("INSERT INTO customers (id, name) VALUES (1, 'alice'), (2, 'bob')");
        db.Execute("INSERT INTO orders (customer_id, total) VALUES (1, 10.00), (1, 20.00), (2, 30.00)");
    }

    public async Task DisposeAsync() => await this.container.DisposeAsync();

    [Fact]
    public async Task Postgres_InsertReturningId_PopulatesLastInsertId()
    {
        var result = await this.Run(
            "INSERT INTO orders (customer_id, total) VALUES (@cid, @total) RETURNING id",
            new Dictionary<string, object?> { ["cid"] = 2, ["total"] = 99.00m },
            expectsLastInsertId: true);

        result.Success.Should().BeTrue();
        result.Outputs["affectedRows"].Should().Be(1);
        Convert.ToInt64(result.Outputs["lastInsertId"]).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Postgres_UpdateWithCte_AffectsCorrectRows()
    {
        var result = await this.Run(
            "WITH big AS (SELECT id FROM orders WHERE total >= @min) UPDATE orders SET total = total + 1 WHERE id IN (SELECT id FROM big)",
            new Dictionary<string, object?> { ["min"] = 20.00m });

        result.Success.Should().BeTrue();
        Convert.ToInt32(result.Outputs["affectedRows"]).Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Postgres_DeleteCascade_AffectsExpectedCount()
    {
        var result = await this.Run("DELETE FROM customers WHERE id = @id", new Dictionary<string, object?> { ["id"] = 1 });

        result.Success.Should().BeTrue();
        result.Outputs["affectedRows"].Should().Be(1, "one customer row deleted (cascade removes its orders too)~ 🌸");
    }

    [Fact]
    public async Task Postgres_ForeignKeyViolation_ReturnsFailWithConstraintName()
    {
        var result = await this.Run(
            "INSERT INTO orders (customer_id, total) VALUES (@cid, @total)",
            new Dictionary<string, object?> { ["cid"] = 999, ["total"] = 5.00m });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Database execute failed");
        result.ErrorMessage.Should().Contain("constraint=", "PostgresException.ConstraintName is surfaced via DbErrorContext~ 🚨");
    }

    #region Helpers 🛠️

    private DefaultDbConnectionFactory Factory()
    {
        var options = new DatabaseConnectionsOptions();
        options.Connections["Pg"] = new DbConnectionDescriptor("Pg", "postgres", this.ConnectionString);
        var registry = new InMemoryDbConnectionRegistry(Options.Create(options));
        return new DefaultDbConnectionFactory(registry, new DefaultDbProviderRegistry());
    }

    private Task<ModuleResult> Run(
        string command,
        Dictionary<string, object?>? parameters = null,
        bool expectsLastInsertId = false)
    {
        var props = new Dictionary<string, object?>
        {
            ["connectionId"] = "Pg",
            ["command"] = command,
            ["expectsLastInsertId"] = expectsLastInsertId,
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
            NodeId = "pg-exec-node",
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

