// <copyright file="DatabaseQueryModuleTests.cs" company="GlutenFree">
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
/// 🔍 Phase 2.4.a.1 — SQLite unit tests for <see cref="DatabaseQueryModule"/>~ ✨💖.
/// Docker-free: every test seeds a temp-file SQLite database through the shared factory.
/// </summary>
public sealed class DatabaseQueryModuleTests : IDisposable
{
    private readonly string dbPath = Path.Combine(Path.GetTempPath(), $"dotflow-query-test-{Guid.NewGuid():N}.db");
    private readonly DatabaseQueryModule module = new();

    public void Dispose()
    {
        if (File.Exists(this.dbPath))
        {
            File.Delete(this.dbPath);
        }
    }

    #region Metadata + Schema 🏷️

    [Fact]
    public void QueryModule_Metadata_IsCorrect()
    {
        this.module.ModuleId.Should().Be("builtin.database.query");
        this.module.Category.Should().Be("Database");
        this.module.Icon.Should().Be("🔍");
        this.module.Version.Should().Be(new Version(1, 0, 0));
    }

    [Fact]
    public void QueryModule_Schema_HasRequiredPorts()
    {
        var outputNames = new HashSet<string>();
        foreach (var p in this.module.Schema.Outputs)
        {
            outputNames.Add(p.Name);
        }

        outputNames.Should().Contain(new[] { "rows", "rowCount", "columns", "success", "durationMs" });

        var propNames = new HashSet<string>();
        foreach (var p in this.module.Schema.Properties)
        {
            propNames.Add(p.Name);
        }

        propNames.Should().Contain(new[] { "connectionId", "connectionString", "provider", "query", "parameters", "timeoutSeconds", "commandType" });
    }

    #endregion

    #region Validation ✅

    [Fact]
    public void ValidateConfiguration_NeitherConnectionIdNorString_Fails()
    {
        var result = this.module.ValidateConfiguration(new Dictionary<string, object?>
        {
            ["query"] = "SELECT 1",
        });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateConfiguration_StoredProcedure_FailsAsDeferred()
    {
        var result = this.module.ValidateConfiguration(new Dictionary<string, object?>
        {
            ["connectionId"] = "TestDb",
            ["query"] = "SELECT 1",
            ["commandType"] = "storedProcedure",
        });

        result.IsValid.Should().BeFalse();
    }

    #endregion

    #region Execution 🔍

    [Fact]
    public async Task SimpleSelect_ReturnsAllRows()
    {
        this.Seed();
        var ctx = this.BuildContext(new Dictionary<string, object?>
        {
            ["connectionId"] = "TestDb",
            ["query"] = "SELECT id, name FROM users ORDER BY id",
        });

        var result = await this.module.ExecuteAsync(ctx, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Outputs["rowCount"].Should().Be(3);
        var rows = (IReadOnlyList<IReadOnlyDictionary<string, object?>>)result.Outputs["rows"]!;
        rows.Should().HaveCount(3);
        rows[0]["name"].Should().Be("alice");
        var columns = (IReadOnlyList<string>)result.Outputs["columns"]!;
        columns.Should().ContainInOrder("id", "name");
    }

    [Fact]
    public async Task SelectWithParameter_BindsCorrectly()
    {
        this.Seed();
        var ctx = this.BuildContext(new Dictionary<string, object?>
        {
            ["connectionId"] = "TestDb",
            ["query"] = "SELECT id, name FROM users WHERE id = @id",
            ["parameters"] = new Dictionary<string, object?> { ["id"] = 2 },
        });

        var result = await this.module.ExecuteAsync(ctx, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Outputs["rowCount"].Should().Be(1);
        var rows = (IReadOnlyList<IReadOnlyDictionary<string, object?>>)result.Outputs["rows"]!;
        rows[0]["name"].Should().Be("bob");
    }

    [Fact]
    public async Task SelectWithMultipleParameters_BindsAll()
    {
        this.Seed();
        var ctx = this.BuildContext(new Dictionary<string, object?>
        {
            ["connectionId"] = "TestDb",
            ["query"] = "SELECT id, name FROM users WHERE id >= @lo AND id <= @hi ORDER BY id",
            ["parameters"] = new Dictionary<string, object?> { ["lo"] = 2, ["hi"] = 3 },
        });

        var result = await this.module.ExecuteAsync(ctx, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Outputs["rowCount"].Should().Be(2);
    }

    [Fact]
    public async Task SelectWithNullParameter_HandlesNull()
    {
        this.Seed();
        var ctx = this.BuildContext(new Dictionary<string, object?>
        {
            ["connectionId"] = "TestDb",
            ["query"] = "SELECT id, name FROM users WHERE (@name IS NULL OR name = @name) ORDER BY id",
            ["parameters"] = new Dictionary<string, object?> { ["name"] = null },
        });

        var result = await this.module.ExecuteAsync(ctx, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Outputs["rowCount"].Should().Be(3, "a null filter matches every row~ 🌸");
    }

    [Fact]
    public async Task SelectEmptyResultSet_ReturnsEmptyRowsAndZeroCount()
    {
        this.Seed();
        var ctx = this.BuildContext(new Dictionary<string, object?>
        {
            ["connectionId"] = "TestDb",
            ["query"] = "SELECT id, name FROM users WHERE id = @id",
            ["parameters"] = new Dictionary<string, object?> { ["id"] = 999 },
        });

        var result = await this.module.ExecuteAsync(ctx, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Outputs["rowCount"].Should().Be(0);
        var rows = (IReadOnlyList<IReadOnlyDictionary<string, object?>>)result.Outputs["rows"]!;
        rows.Should().BeEmpty();
    }

    [Fact]
    public async Task SelectInvalidSql_ReturnsFailWithSqliteError()
    {
        this.Seed();
        var ctx = this.BuildContext(new Dictionary<string, object?>
        {
            ["connectionId"] = "TestDb",
            ["query"] = "SELECT * FROM nonexistent_table",
        });

        var result = await this.module.ExecuteAsync(ctx, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Database query failed");
    }

    [Fact]
    public async Task RawConnectionString_Works()
    {
        this.Seed();
        var ctx = this.BuildContext(new Dictionary<string, object?>
        {
            ["connectionString"] = this.SqliteConnectionString,
            ["provider"] = "sqlite",
            ["query"] = "SELECT COUNT(*) AS n FROM users",
        });

        var result = await this.module.ExecuteAsync(ctx, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Outputs["rowCount"].Should().Be(1);
    }

    [Fact]
    public async Task UnknownConnectionId_ReturnsFail()
    {
        this.Seed();
        var ctx = this.BuildContext(new Dictionary<string, object?>
        {
            ["connectionId"] = "DoesNotExist",
            ["query"] = "SELECT 1",
        });

        var result = await this.module.ExecuteAsync(ctx, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not found");
    }

    [Fact]
    public async Task MissingConnectionFactory_Fails()
    {
        var ctx = new ModuleExecutionContext
        {
            Inputs = new Dictionary<string, object?>(),
            Properties = new Dictionary<string, object?>
            {
                ["connectionId"] = "TestDb",
                ["query"] = "SELECT 1",
            },
            Variables = new Dictionary<string, object?>(),
            Logger = NullLogger.Instance,
            Services = new EmptyServiceProvider(),
            ExecutionId = Guid.NewGuid(),
            NodeId = "db-query-node",
        };

        var result = await this.module.ExecuteAsync(ctx, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("IDbConnectionFactory");
    }

    #endregion

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
    }

    private ModuleExecutionContext BuildContext(Dictionary<string, object?> properties)
        => new()
        {
            Inputs = new Dictionary<string, object?>(),
            Properties = properties,
            Variables = new Dictionary<string, object?>(),
            Logger = NullLogger.Instance,
            Services = new FactoryServiceProvider(this.BuildFactory()),
            ExecutionId = Guid.NewGuid(),
            NodeId = "db-query-node",
        };

    private sealed class FactoryServiceProvider : IServiceProvider
    {
        private readonly IDbConnectionFactory factory;

        public FactoryServiceProvider(IDbConnectionFactory factory) => this.factory = factory;

        public object? GetService(Type serviceType)
            => serviceType == typeof(IDbConnectionFactory) ? this.factory : null;
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    #endregion
}

