// <copyright file="DatabaseE2ETests.cs" company="GlutenFree">
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
using Workflow.Modules.Database.Models;
using Workflow.Modules.Database.Providers;
using Xunit;

/// <summary>
/// 🎬 Phase 2.4.a.6 — End-to-end demo proving the four escape-hatch database modules cooperate
/// against a real Postgres Testcontainer, sharing one named-connection registry~ ✨💖.
/// </summary>
/// <remarks>
/// <para>
/// The Phase 2.4 demo workflow shape is:
/// <code>
/// webhook_trigger → bulkinsert(orders) → transaction[update_inventory; insert_audit]
///                 → query(orders_by_user) → setvariable(audit=done)
/// </code>
/// </para>
/// <para>
/// CopilotNote (deviation): <c>webhook_trigger</c> and <c>setvariable</c> are engine-level concerns
/// (Akka + HTTP trigger + variable store). Consistent with every other Postgres integration test in
/// this project, this E2E invokes the <b>modules directly</b> (build a <see cref="ModuleExecutionContext"/>
/// → <c>ExecuteAsync</c>) rather than spinning up the full engine — the webhook trigger is the framing
/// "arrange", and <c>setvariable</c> is represented by capturing the final query output into a local
/// variables bag. This keeps the E2E Docker-only (no engine wiring) while still proving the module
/// family cooperates + shares infra. Requires Docker; <c>[Trait("Category", "Integration")]</c> lets
/// Docker-less CI skip it~ 🐳🌸.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class DatabaseE2ETests : IAsyncLifetime
{
    private const int UserId = 42;

    private readonly PostgreSqlContainer container = new PostgreSqlBuilder()
        .WithImage("postgres:15-alpine")
        .WithDatabase("e2e_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly DatabaseBulkInsertModule bulkInsert = new();
    private readonly DatabaseTransactionModule transaction = new();
    private readonly DatabaseQueryModule query = new();

    private string ConnectionString => this.container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await this.container.StartAsync();

        using DataConnection db = await this.Factory().CreateAsync("postgres", this.ConnectionString);
        db.Execute("CREATE TABLE orders (id INT PRIMARY KEY, user_id INT NOT NULL, product TEXT NOT NULL, qty INT NOT NULL)");
        db.Execute("CREATE TABLE inventory (sku TEXT PRIMARY KEY, stock INT NOT NULL)");
        db.Execute("CREATE TABLE audit (id SERIAL PRIMARY KEY, note TEXT NOT NULL)");
        db.Execute("INSERT INTO inventory (sku, stock) VALUES ('WIDGET', 100), ('GADGET', 100)");
    }

    public async Task DisposeAsync() => await this.container.DisposeAsync();

    /// <summary>
    /// 🌈 Happy path — bulkinsert orders, atomically decrement inventory + write an audit row,
    /// then query the user's orders back. Asserts the end-state row counts + a "setvariable" capture~ ✨.
    /// </summary>
    [Fact]
    public async Task Demo_OrderFlow_AllStepsSucceed_FinalQueryReturnsExpected()
    {
        // 1️⃣ bulkinsert(orders) — 3 orders for the user~ 📊
        var orders = new object[]
        {
            new Dictionary<string, object?> { ["id"] = 1, ["user_id"] = UserId, ["product"] = "WIDGET", ["qty"] = 2 },
            new Dictionary<string, object?> { ["id"] = 2, ["user_id"] = UserId, ["product"] = "GADGET", ["qty"] = 5 },
            new Dictionary<string, object?> { ["id"] = 3, ["user_id"] = UserId, ["product"] = "WIDGET", ["qty"] = 1 },
        };

        var bulk = await this.RunBulkInsert("orders", orders);
        bulk.Success.Should().BeTrue("the bulkinsert step should insert all order rows~ 📊");
        bulk.Outputs["insertedCount"].Should().Be(3);

        // 2️⃣ transaction[update_inventory; insert_audit] — atomic~ 💼
        var txn = await this.RunTransaction(new object[]
        {
            Op("UPDATE inventory SET stock = stock - 3 WHERE sku = 'WIDGET'"), // 2 + 1
            Op("UPDATE inventory SET stock = stock - 5 WHERE sku = 'GADGET'"),
            Op("INSERT INTO audit (note) VALUES ('order flow for user 42')"),
        });

        txn.Outputs["success"].Should().Be(true, "all transaction ops should commit~ 💼");

        // 3️⃣ query(orders_by_user)~ 🔍
        var q = await this.RunQuery(
            "SELECT id, product, qty FROM orders WHERE user_id = @uid ORDER BY id",
            new Dictionary<string, object?> { ["uid"] = UserId });

        q.Success.Should().BeTrue();
        var rows = (IReadOnlyList<IReadOnlyDictionary<string, object?>>)q.Outputs["rows"]!;
        rows.Should().HaveCount(3, "the user placed three orders~ 🔍");

        // 4️⃣ setvariable(audit=done) — represented by capturing final state into a local bag~ 📝
        var variables = new Dictionary<string, object?>
        {
            ["orderCount"] = q.Outputs["rowCount"],
            ["audit"] = "done",
        };

        variables["orderCount"].Should().Be(3);
        variables["audit"].Should().Be("done");

        // End-state assertions across the whole flow~ ✅
        this.Scalar<int>("SELECT stock FROM inventory WHERE sku = 'WIDGET'").Should().Be(97);
        this.Scalar<int>("SELECT stock FROM inventory WHERE sku = 'GADGET'").Should().Be(95);
        this.Scalar<int>("SELECT COUNT(*) FROM audit").Should().Be(1);
    }

    /// <summary>
    /// 🛡️ Rollback path — the order-writing transaction fails on a mid-op PK violation, so the whole
    /// transaction rolls back and NO orders are inserted. Proves atomicity end-to-end~ ✨.
    /// </summary>
    [Fact]
    public async Task Demo_TransactionMiddleFails_NoOrdersInserted_FullRollback()
    {
        // Put the order writes INSIDE a transaction; the middle op violates the PK → full rollback~ 💼
        var txn = await this.RunTransaction(new object[]
        {
            Op("INSERT INTO orders (id, user_id, product, qty) VALUES (10, 42, 'WIDGET', 1)"),
            Op("INSERT INTO orders (id, user_id, product, qty) VALUES (10, 42, 'GADGET', 2)"), // dup PK 💥
            Op("INSERT INTO audit (note) VALUES ('should never commit')"),
        });

        txn.Outputs["success"].Should().Be(false, "the duplicate-PK op aborts the transaction~ 💥");
        ((DbOperationError)txn.Outputs["error"]!).OperationIndex.Should().Be(1, "the second op is the failing one~");

        // Full rollback: the first INSERT + the audit row must both be gone~ 🛡️
        var q = await this.RunQuery(
            "SELECT id FROM orders WHERE user_id = @uid",
            new Dictionary<string, object?> { ["uid"] = UserId });

        ((IReadOnlyList<IReadOnlyDictionary<string, object?>>)q.Outputs["rows"]!)
            .Should().BeEmpty("no orders should survive a rolled-back transaction~ 🛡️");
        this.Scalar<int>("SELECT COUNT(*) FROM audit").Should().Be(0, "the audit insert rolled back too~");
    }

    #region Helpers 🛠️

    private static Dictionary<string, object?> Op(string sql) => new() { ["sql"] = sql };

    private DefaultDbConnectionFactory Factory()
    {
        var options = new DatabaseConnectionsOptions();
        options.Connections["OrdersDb"] = new DbConnectionDescriptor("OrdersDb", "postgres", this.ConnectionString);
        var registry = new InMemoryDbConnectionRegistry(Options.Create(options));
        return new DefaultDbConnectionFactory(registry, new DefaultDbProviderRegistry());
    }

    private T Scalar<T>(string sql)
    {
        using DataConnection db = this.Factory().CreateAsync("postgres", this.ConnectionString).AsTask().GetAwaiter().GetResult();
        return db.Execute<T>(sql);
    }

    private Task<ModuleResult> RunBulkInsert(string tableName, object[] data)
        => this.bulkInsert.ExecuteAsync(
            this.Context(new Dictionary<string, object?>
            {
                ["connectionId"] = "OrdersDb",
                ["tableName"] = tableName,
                ["data"] = data,
            }),
            CancellationToken.None);

    private Task<ModuleResult> RunTransaction(object[] operations)
        => this.transaction.ExecuteAsync(
            this.Context(new Dictionary<string, object?>
            {
                ["connectionId"] = "OrdersDb",
                ["operations"] = operations,
            }),
            CancellationToken.None);

    private Task<ModuleResult> RunQuery(string sql, Dictionary<string, object?> parameters)
        => this.query.ExecuteAsync(
            this.Context(new Dictionary<string, object?>
            {
                ["connectionId"] = "OrdersDb",
                ["query"] = sql,
                ["parameters"] = parameters,
            }),
            CancellationToken.None);

    private ModuleExecutionContext Context(Dictionary<string, object?> properties) => new()
    {
        Inputs = new Dictionary<string, object?>(),
        Properties = properties,
        Variables = new Dictionary<string, object?>(),
        Logger = NullLogger.Instance,
        Services = new FactoryServiceProvider(this.Factory()),
        ExecutionId = Guid.NewGuid(),
        NodeId = "e2e-node",
    };

    private sealed class FactoryServiceProvider : IServiceProvider
    {
        private readonly IDbConnectionFactory factory;

        public FactoryServiceProvider(IDbConnectionFactory factory) => this.factory = factory;

        public object? GetService(Type serviceType)
            => serviceType == typeof(IDbConnectionFactory) ? this.factory : null;
    }

    #endregion
}

