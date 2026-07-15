// <copyright file="PostgresTransactionTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Integration.Database;

using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using Workflow.Modules.Database.Models;
using Workflow.Modules.Database.Providers;
using Xunit;

/// <summary>
/// 🐘 Phase 2.4.a.3 — Postgres Testcontainers tests for <see cref="DatabaseTransactionModule"/>~ ✨💖.
/// </summary>
/// <remarks>
/// CopilotNote: Requires Docker. Marked [Trait("Category", "Integration")] so Docker-less CI skips it~ 🐳.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class PostgresTransactionTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder()
        .WithImage("postgres:15-alpine")
        .WithDatabase("txn_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly DatabaseTransactionModule module = new();

    private string ConnectionString => this.container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await this.container.StartAsync();

        using DataConnection db = await this.Factory().CreateAsync("postgres", this.ConnectionString);
        db.Execute("CREATE TABLE accounts (id INT PRIMARY KEY, balance NUMERIC(12,2))");
        db.Execute("CREATE TABLE audit (id SERIAL PRIMARY KEY, note TEXT)");
        db.Execute("INSERT INTO accounts (id, balance) VALUES (1, 100.00), (2, 50.00)");
    }

    public async Task DisposeAsync() => await this.container.DisposeAsync();

    [Fact]
    public async Task Postgres_Transaction_AllOpsCommit()
    {
        var result = await this.Run(new object[]
        {
            OpP("UPDATE accounts SET balance = balance - 25 WHERE id = 1"),
            OpP("UPDATE accounts SET balance = balance + 25 WHERE id = 2"),
            OpP("INSERT INTO audit (note) VALUES ('transfer 25')"),
        });

        result.Outputs["success"].Should().Be(true);
        this.Balance(1).Should().Be(75.00m);
        this.Balance(2).Should().Be(75.00m);
    }

    [Fact]
    public async Task Postgres_Transaction_HalfwayFails_FullRollback()
    {
        var result = await this.Run(new object[]
        {
            OpP("UPDATE accounts SET balance = balance - 25 WHERE id = 1"),
            OpP("INSERT INTO accounts (id, balance) VALUES (1, 0)"), // PK violation
        });

        result.Outputs["success"].Should().Be(false);
        ((DbOperationError)result.Outputs["error"]!).OperationIndex.Should().Be(1);
        this.Balance(1).Should().Be(100.00m, "the debit rolled back~ 🛡️");
    }

    [Fact]
    public async Task Postgres_SerializableIsolation_Commits()
    {
        var result = await this.Run(
            new object[] { OpP("UPDATE accounts SET balance = balance + 1 WHERE id = 1") },
            isolationLevel: "Serializable");

        result.Outputs["success"].Should().Be(true);
    }

    [Fact]
    public async Task Postgres_RepeatableRead_Commits()
    {
        var result = await this.Run(
            new object[] { OpP("UPDATE accounts SET balance = balance + 1 WHERE id = 2") },
            isolationLevel: "RepeatableRead");

        result.Outputs["success"].Should().Be(true);
    }

    [Fact]
    public async Task Postgres_Transaction_50OpsAllCommit()
    {
        var ops = new List<object>();
        for (var i = 100; i < 150; i++)
        {
            ops.Add(new Dictionary<string, object?>
            {
                ["sql"] = "INSERT INTO audit (note) VALUES (@n)",
                ["parameters"] = new Dictionary<string, object?> { ["n"] = $"row-{i}" },
            });
        }

        var result = await this.Run(ops.ToArray());

        result.Outputs["success"].Should().Be(true);
        ((IReadOnlyList<DbOperationResult>)result.Outputs["results"]!).Should().HaveCount(50);
    }

    [Fact]
    public async Task Postgres_BatchOp_500ParameterSets_Commit()
    {
        var sets = new List<object>();
        for (var i = 1000; i < 1500; i++)
        {
            sets.Add(new Dictionary<string, object?> { ["n"] = $"batch-{i}" });
        }

        var op = new Dictionary<string, object?>
        {
            ["sql"] = "INSERT INTO audit (note) VALUES (@n)",
            ["parameterSets"] = sets.ToArray(),
        };

        var sw = Stopwatch.StartNew();
        var result = await this.Run(new object[] { op });
        sw.Stop();

        result.Outputs["success"].Should().Be(true);
        var results = (IReadOnlyList<DbOperationResult>)result.Outputs["results"]!;
        results[0].IsBatchOp.Should().BeTrue();
        results[0].AffectedRows.Should().Be(500);
    }

    #region Helpers 🛠️

    private static Dictionary<string, object?> OpP(string sql) => new() { ["sql"] = sql };

    private DefaultDbConnectionFactory Factory()
    {
        var options = new DatabaseConnectionsOptions();
        options.Connections["Pg"] = new DbConnectionDescriptor("Pg", "postgres", this.ConnectionString);
        var registry = new InMemoryDbConnectionRegistry(Options.Create(options));
        return new DefaultDbConnectionFactory(registry, new DefaultDbProviderRegistry());
    }

    private decimal Balance(int id)
    {
        using DataConnection db = this.Factory().CreateAsync("postgres", this.ConnectionString).AsTask().GetAwaiter().GetResult();
        return db.Execute<decimal>("SELECT balance FROM accounts WHERE id = @id", new DataParameter("id", id));
    }

    private Task<ModuleResult> Run(object[] operations, string? isolationLevel = null)
    {
        var props = new Dictionary<string, object?>
        {
            ["connectionId"] = "Pg",
            ["operations"] = operations,
        };
        if (isolationLevel is not null)
        {
            props["isolationLevel"] = isolationLevel;
        }

        var ctx = new ModuleExecutionContext
        {
            Inputs = new Dictionary<string, object?>(),
            Properties = props,
            Variables = new Dictionary<string, object?>(),
            Logger = NullLogger.Instance,
            Services = new FactoryServiceProvider(this.Factory()),
            ExecutionId = Guid.NewGuid(),
            NodeId = "pg-txn-node",
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

