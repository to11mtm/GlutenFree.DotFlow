// <copyright file="DatabaseExecuteModuleTests.cs" company="GlutenFree">
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
/// ✏️ Phase 2.4.a.2 — SQLite unit tests for <see cref="DatabaseExecuteModule"/>~ ✨💖.
/// </summary>
public sealed class DatabaseExecuteModuleTests : IDisposable
{
    private readonly string dbPath = Path.Combine(Path.GetTempPath(), $"dotflow-exec-test-{Guid.NewGuid():N}.db");
    private readonly DatabaseExecuteModule module = new();

    public void Dispose()
    {
        if (File.Exists(this.dbPath))
        {
            File.Delete(this.dbPath);
        }
    }

    [Fact]
    public void ExecuteModule_Metadata_IsCorrect()
    {
        this.module.ModuleId.Should().Be("builtin.database.execute");
        this.module.Category.Should().Be("Database");
        this.module.Icon.Should().Be("✏️");
        this.module.Version.Should().Be(new Version(1, 0, 0));
    }

    [Fact]
    public async Task Insert_ReturnsAffectedRowsOne()
    {
        this.Seed();
        var result = await this.Run("INSERT INTO users (id, name) VALUES (4, 'dave')");

        result.Success.Should().BeTrue();
        result.Outputs["affectedRows"].Should().Be(1);
        result.Outputs["lastInsertId"].Should().BeNull("expectsLastInsertId defaults to false~ 🌸");
    }

    [Fact]
    public async Task Update_MatchingRows_ReturnsAffectedRowsCount()
    {
        this.Seed();
        var result = await this.Run(
            "UPDATE users SET name = @name WHERE id <= @maxId",
            new Dictionary<string, object?> { ["name"] = "renamed", ["maxId"] = 2 });

        result.Success.Should().BeTrue();
        result.Outputs["affectedRows"].Should().Be(2);
    }

    [Fact]
    public async Task Update_NoMatch_ReturnsZeroAffected()
    {
        this.Seed();
        var result = await this.Run(
            "UPDATE users SET name = 'x' WHERE id = @id",
            new Dictionary<string, object?> { ["id"] = 999 });

        result.Success.Should().BeTrue();
        result.Outputs["affectedRows"].Should().Be(0);
    }

    [Fact]
    public async Task Delete_RemovesRow()
    {
        this.Seed();
        var result = await this.Run("DELETE FROM users WHERE id = @id", new Dictionary<string, object?> { ["id"] = 1 });

        result.Success.Should().BeTrue();
        result.Outputs["affectedRows"].Should().Be(1);
    }

    [Fact]
    public async Task Insert_WithExpectsLastInsertId_ReturnsAutoIncrementId()
    {
        this.Seed();
        var result = await this.Run(
            "INSERT INTO logs (message) VALUES (@msg)",
            new Dictionary<string, object?> { ["msg"] = "hello" },
            expectsLastInsertId: true);

        result.Success.Should().BeTrue();
        result.Outputs["affectedRows"].Should().Be(1);
        Convert.ToInt64(result.Outputs["lastInsertId"]).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Insert_UniqueConstraintViolation_ReturnsFailWithConstraintContext()
    {
        this.Seed();
        var result = await this.Run("INSERT INTO users (id, name) VALUES (1, 'dupe')");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Database execute failed");
        result.ErrorMessage.Should().Contain("users", "SQLite surfaces the constraint target in the message~ 🌸");
    }

    [Fact]
    public async Task Execute_InvalidSql_ReturnsFail()
    {
        this.Seed();
        var result = await this.Run("UPDATE nonexistent SET x = 1");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Database execute failed");
    }

    [Fact]
    public void ValidateConfiguration_MissingCommand_Fails()
    {
        var result = this.module.ValidateConfiguration(new Dictionary<string, object?>
        {
            ["connectionId"] = "TestDb",
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
        db.Execute("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT)");
        db.Execute("INSERT INTO users (id, name) VALUES (1, 'alice'), (2, 'bob'), (3, 'carol')");
        db.Execute("CREATE TABLE logs (id INTEGER PRIMARY KEY AUTOINCREMENT, message TEXT)");
    }

    private Task<ModuleResult> Run(
        string command,
        Dictionary<string, object?>? parameters = null,
        bool expectsLastInsertId = false)
    {
        var props = new Dictionary<string, object?>
        {
            ["connectionId"] = "TestDb",
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
            Services = new FactoryServiceProvider(this.BuildFactory()),
            ExecutionId = Guid.NewGuid(),
            NodeId = "db-exec-node",
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

