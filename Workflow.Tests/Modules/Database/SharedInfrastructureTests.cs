// <copyright file="SharedInfrastructureTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.Database;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using LinqToDB;
using LinqToDB.Data;
using Microsoft.Extensions.Options;
using Workflow.Modules.Database.Abstractions;
using Workflow.Modules.Database.Configuration;
using Workflow.Modules.Database.Connections;
using Workflow.Modules.Database.Providers;
using Workflow.Modules.Database.Transactions;
using Xunit;

/// <summary>
/// 🛠️ Phase 2.4.a.0 — Tests for the shared database infrastructure!
/// Provider registry, connection registry, connection factory, and transaction scope~ ✨💖.
/// </summary>
public sealed class SharedInfrastructureTests : IDisposable
{
    /// <summary>
    /// Temp SQLite database file for tests that need a real connection.
    /// Pooling=False so the file is deletable in Dispose~ 🧹.
    /// </summary>
    private readonly string dbPath = Path.Combine(Path.GetTempPath(), $"dotflow-infra-test-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        if (File.Exists(this.dbPath))
        {
            File.Delete(this.dbPath);
        }
    }

    #region Helpers 🛠️

    private string SqliteConnectionString => $"Data Source={this.dbPath};Pooling=False";

    private static InMemoryDbConnectionRegistry BuildRegistry(params DbConnectionDescriptor[] configured)
    {
        var options = new DatabaseConnectionsOptions();
        foreach (var descriptor in configured)
        {
            options.Connections[descriptor.Id] = descriptor;
        }

        return new InMemoryDbConnectionRegistry(Options.Create(options));
    }

    private DefaultDbConnectionFactory BuildFactory(params DbConnectionDescriptor[] configured)
        => new(BuildRegistry(configured), new DefaultDbProviderRegistry());

    private DbConnectionDescriptor SqliteDescriptor(string id = "TestDb")
        => new(id, "sqlite", this.SqliteConnectionString);

    #endregion

    #region Provider Registry 🗂️

    [Fact]
    public void ProviderRegistry_KnownPostgres_ResolvesToPostgreSQL15()
    {
        var registry = new DefaultDbProviderRegistry();

        registry.ResolveLinq2DbProvider("postgres").Should().Be(ProviderName.PostgreSQL15);
        registry.ResolveLinq2DbProvider("POSTGRES").Should().Be(ProviderName.PostgreSQL15, "keys are case-insensitive~ 🔑");
    }

    [Fact]
    public void ProviderRegistry_KnownSqlite_ResolvesToSQLiteMS()
    {
        var registry = new DefaultDbProviderRegistry();

        registry.ResolveLinq2DbProvider("sqlite").Should().Be(ProviderName.SQLiteMS);
    }

    [Fact]
    public void ProviderRegistry_UnknownKey_ThrowsUnknownProviderException()
    {
        var registry = new DefaultDbProviderRegistry();

        var act = () => registry.ResolveLinq2DbProvider("oracle");

        act.Should().Throw<UnknownProviderException>()
            .Which.ProviderKey.Should().Be("oracle");
    }

    [Fact]
    public void ProviderRegistry_KnownProviders_ContainsPostgresAndSqlite()
    {
        var registry = new DefaultDbProviderRegistry();

        registry.KnownProviders.Should().BeEquivalentTo("postgres", "sqlite");
    }

    #endregion

    #region Connection Registry 📇

    [Fact]
    public async Task ConnectionRegistry_ConfigBoundEntry_LookupByIdReturnsDescriptor()
    {
        var registry = BuildRegistry(new DbConnectionDescriptor("OrdersDb", "postgres", "Host=localhost;Database=orders"));

        var result = await registry.GetAsync("OrdersDb");

        result.IsSome.Should().BeTrue("config-bound entries hydrate at construction~ ⚙️");
        result.IfSome(d =>
        {
            d.ProviderKey.Should().Be("postgres");
            d.Enabled.Should().BeTrue();
        });
    }

    [Fact]
    public async Task ConnectionRegistry_UpsertThenGet_RoundTrips()
    {
        var registry = BuildRegistry();
        var descriptor = new DbConnectionDescriptor("NewDb", "sqlite", "Data Source=:memory:", DisplayName: "New DB");

        await registry.UpsertAsync(descriptor);
        var result = await registry.GetAsync("NewDb");

        result.IsSome.Should().BeTrue();
        result.IfSome(d => d.Should().Be(descriptor));
    }

    [Fact]
    public async Task ConnectionRegistry_DeleteUnknown_ReturnsFalse()
    {
        var registry = BuildRegistry();

        (await registry.DeleteAsync("does-not-exist")).Should().BeFalse();
    }

    [Fact]
    public async Task ConnectionRegistry_LookupCaseInsensitive()
    {
        var registry = BuildRegistry(new DbConnectionDescriptor("OrdersDb", "postgres", "Host=localhost"));

        (await registry.GetAsync("ordersdb")).IsSome.Should().BeTrue();
        (await registry.GetAsync("ORDERSDB")).IsSome.Should().BeTrue();

        // Delete is case-insensitive too~ 🗑️
        (await registry.DeleteAsync("oRdErSdB")).Should().BeTrue();
        (await registry.GetAsync("OrdersDb")).IsNone.Should().BeTrue();
    }

    [Fact]
    public async Task ConnectionRegistry_List_ReturnsAllEntries()
    {
        var registry = BuildRegistry(
            new DbConnectionDescriptor("A", "postgres", "Host=a"),
            new DbConnectionDescriptor("B", "sqlite", "Data Source=b.db"));

        var all = await registry.ListAsync();

        all.Should().HaveCount(2);
    }

    #endregion

    #region Connection Factory 🔌

    [Fact]
    public async Task ConnectionFactory_NamedConnection_CreatesDataConnection()
    {
        var factory = this.BuildFactory(this.SqliteDescriptor());

        await using DataConnection db = await factory.CreateAsync("TestDb");

        // Prove the connection actually works — round-trip a scalar~ ✨
        db.Execute<int>("SELECT 1").Should().Be(1);
    }

    [Fact]
    public async Task ConnectionFactory_UnknownConnectionId_ThrowsConnectionNotFound()
    {
        var factory = this.BuildFactory();

        var act = async () => await factory.CreateAsync("nope");

        (await act.Should().ThrowAsync<ConnectionNotFoundException>())
            .Which.ConnectionId.Should().Be("nope");
    }

    [Fact]
    public async Task ConnectionFactory_DisabledConnection_ThrowsConnectionNotFound()
    {
        var factory = this.BuildFactory(this.SqliteDescriptor() with { Enabled = false });

        var act = async () => await factory.CreateAsync("TestDb");

        await act.Should().ThrowAsync<ConnectionNotFoundException>("disabled connections must not be usable~ 🔒");
    }

    [Fact]
    public async Task ConnectionFactory_RawProviderAndConnectionString_CreatesDataConnection()
    {
        var factory = this.BuildFactory();

        await using DataConnection db = await factory.CreateAsync("sqlite", this.SqliteConnectionString);

        db.Execute<int>("SELECT 41 + 1").Should().Be(42);
    }

    [Fact]
    public async Task ConnectionFactory_RawUnknownProvider_ThrowsUnknownProviderException()
    {
        var factory = this.BuildFactory();

        var act = async () => await factory.CreateAsync("mssql", "Server=.;Database=x");

        await act.Should().ThrowAsync<UnknownProviderException>();
    }

    #endregion

    #region Transaction Scope 💼

    [Fact]
    public async Task TransactionScope_AutoRollbackOnDispose_NoCommit()
    {
        var factory = this.BuildFactory(this.SqliteDescriptor());

        // Seed schema outside any transaction~ 🌱
        await using (var setup = await factory.CreateAsync("TestDb"))
        {
            setup.Execute("CREATE TABLE items (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL)");
        }

        // INSERT inside a scope that is never committed → dispose must roll back~ 🛡️
        await using (var scope = await factory.CreateTransactionAsync("TestDb"))
        {
            scope.Connection.Execute("INSERT INTO items (name) VALUES ('ghost')");
        }

        await using var check = await factory.CreateAsync("TestDb");
        check.Execute<int>("SELECT COUNT(*) FROM items").Should().Be(0, "uncommitted work must vanish on dispose~ 👻");
    }

    [Fact]
    public async Task TransactionScope_Commit_PersistsWork()
    {
        var factory = this.BuildFactory(this.SqliteDescriptor());

        await using (var setup = await factory.CreateAsync("TestDb"))
        {
            setup.Execute("CREATE TABLE items (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL)");
        }

        await using (var scope = await factory.CreateTransactionAsync("TestDb"))
        {
            scope.Connection.Execute("INSERT INTO items (name) VALUES ('kept')");
            await scope.CommitAsync();
        }

        await using var check = await factory.CreateAsync("TestDb");
        check.Execute<int>("SELECT COUNT(*) FROM items").Should().Be(1, "committed work must persist~ 💾");
    }

    [Fact]
    public async Task TransactionScope_DoubleCommit_ThrowsInvalidOperation()
    {
        var factory = this.BuildFactory(this.SqliteDescriptor());

        await using var scope = await factory.CreateTransactionAsync("TestDb");
        await scope.CommitAsync();

        var act = async () => await scope.CommitAsync();

        await act.Should().ThrowAsync<InvalidOperationException>("commit/rollback are terminal — no double-completion~ 🚧");
    }

    #endregion
}

