// <copyright file="DatabaseTransactionModuleTests.cs" company="GlutenFree">
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
using Workflow.Modules.Database.Models;
using Workflow.Modules.Database.Providers;
using Xunit;

/// <summary>
/// 💼 Phase 2.4.a.3 — SQLite unit tests for <see cref="DatabaseTransactionModule"/>~ ✨💖.
/// </summary>
public sealed class DatabaseTransactionModuleTests : IDisposable
{
    private readonly string dbPath = Path.Combine(Path.GetTempPath(), $"dotflow-txn-test-{Guid.NewGuid():N}.db");
    private readonly DatabaseTransactionModule module = new();

    public void Dispose()
    {
        if (File.Exists(this.dbPath))
        {
            File.Delete(this.dbPath);
        }
    }

    [Fact]
    public void TransactionModule_Metadata_IsCorrect()
    {
        this.module.ModuleId.Should().Be("builtin.database.transaction");
        this.module.Category.Should().Be("Database");
        this.module.Icon.Should().Be("💼");
        this.module.Version.Should().Be(new Version(1, 0, 0));
    }

    [Fact]
    public async Task Transaction_AllOpsSucceed_Commits()
    {
        this.Seed();
        var result = await this.Run(new object[]
        {
            Op("INSERT INTO users (id, name) VALUES (10, 'x')"),
            Op("UPDATE users SET name = 'y' WHERE id = 1"),
        });

        result.Success.Should().BeTrue();
        result.Outputs["success"].Should().Be(true);
        this.CountUsers().Should().Be(4);
        this.NameOf(1).Should().Be("y");
    }

    [Fact]
    public async Task Transaction_FirstOpFails_RollsBackEverything()
    {
        this.Seed();
        var result = await this.Run(new object[]
        {
            Op("INSERT INTO users (id, name) VALUES (1, 'dupe')"), // PK violation
            Op("UPDATE users SET name = 'never' WHERE id = 2"),
        });

        result.Outputs["success"].Should().Be(false);
        var error = (DbOperationError)result.Outputs["error"]!;
        error.OperationIndex.Should().Be(0);
        this.NameOf(2).Should().Be("bob", "the update must have rolled back~ 🛡️");
    }

    [Fact]
    public async Task Transaction_MiddleOpFails_RollsBackPriorOps()
    {
        this.Seed();
        var result = await this.Run(new object[]
        {
            Op("INSERT INTO users (id, name) VALUES (10, 'ten')"),
            Op("INSERT INTO users (id, name) VALUES (1, 'dupe')"), // PK violation
            Op("INSERT INTO users (id, name) VALUES (11, 'eleven')"),
        });

        result.Outputs["success"].Should().Be(false);
        ((DbOperationError)result.Outputs["error"]!).OperationIndex.Should().Be(1);
        this.CountUsers().Should().Be(3, "op[0] insert rolled back too~ 🛡️");
    }

    [Fact]
    public async Task Transaction_LastOpFails_RollsBackEverything()
    {
        this.Seed();
        var result = await this.Run(new object[]
        {
            Op("INSERT INTO users (id, name) VALUES (10, 'ten')"),
            Op("INSERT INTO users (id, name) VALUES (11, 'eleven')"),
            Op("INSERT INTO nonexistent (x) VALUES (1)"),
        });

        result.Outputs["success"].Should().Be(false);
        ((DbOperationError)result.Outputs["error"]!).OperationIndex.Should().Be(2);
        this.CountUsers().Should().Be(3);
    }

    [Fact]
    public async Task Transaction_EmptyOperations_ReturnsSuccessNoOp()
    {
        this.Seed();
        var result = await this.Run(Array.Empty<object>());

        result.Success.Should().BeTrue();
        result.Outputs["success"].Should().Be(true);
        ((IReadOnlyList<DbOperationResult>)result.Outputs["results"]!).Should().BeEmpty();
    }

    [Fact]
    public async Task Transaction_SingleOp_Commits()
    {
        this.Seed();
        var result = await this.Run(new object[] { Op("DELETE FROM users WHERE id = 3") });

        result.Outputs["success"].Should().Be(true);
        this.CountUsers().Should().Be(2);
    }

    [Fact]
    public async Task Transaction_OpWithExpectLastInsertId_PopulatesPerOpResult()
    {
        this.Seed();
        var op = new Dictionary<string, object?>
        {
            ["sql"] = "INSERT INTO logs (message) VALUES (@m)",
            ["parameters"] = new Dictionary<string, object?> { ["m"] = "hi" },
            ["expectLastInsertId"] = true,
        };
        var result = await this.Run(new object[] { op });

        result.Outputs["success"].Should().Be(true);
        var results = (IReadOnlyList<DbOperationResult>)result.Outputs["results"]!;
        results[0].LastInsertId.Should().NotBeNull();
        results[0].LastInsertId!.Value.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Transaction_DefaultIsolation_CommitsUnderClampedLevel()
    {
        // SQLite clamps ReadCommitted → Serializable; the op must still commit cleanly~ 🔒
        this.Seed();
        var result = await this.Run(new object[] { Op("UPDATE users SET name = 'z' WHERE id = 1") });

        result.Outputs["success"].Should().Be(true);
        this.NameOf(1).Should().Be("z");
    }

    [Fact]
    public async Task Transaction_FailureIncludesOperationIndexAndSqlContext()
    {
        this.Seed();
        var result = await this.Run(new object[]
        {
            Op("INSERT INTO users (id, name) VALUES (1, 'dupe')"),
        });

        var error = (DbOperationError)result.Outputs["error"]!;
        error.OperationIndex.Should().Be(0);
        error.Message.Should().Contain("users");
    }

    [Fact]
    public void ValidateConfiguration_OpWithBothParametersAndParameterSets_Fails()
    {
        var config = new Dictionary<string, object?>
        {
            ["connectionId"] = "TestDb",
            ["operations"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["sql"] = "UPDATE users SET name = @n WHERE id = @id",
                    ["parameters"] = new Dictionary<string, object?> { ["n"] = "a", ["id"] = 1 },
                    ["parameterSets"] = new object[] { new Dictionary<string, object?> { ["n"] = "b", ["id"] = 2 } },
                },
            },
        };

        this.module.ValidateConfiguration(config).IsValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateConfiguration_SavepointInOpsSpec_RejectedAtValidation()
    {
        var config = new Dictionary<string, object?>
        {
            ["connectionId"] = "TestDb",
            ["operations"] = new object[]
            {
                new Dictionary<string, object?> { ["sql"] = "UPDATE users SET name = 'a'", ["savepoint"] = "sp1" },
            },
        };

        this.module.ValidateConfiguration(config).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Transaction_BatchOp_ParameterSets_AllRowsUpdated()
    {
        this.Seed();
        var op = new Dictionary<string, object?>
        {
            ["sql"] = "UPDATE users SET name = @name WHERE id = @id",
            ["parameterSets"] = new object[]
            {
                new Dictionary<string, object?> { ["name"] = "A", ["id"] = 1 },
                new Dictionary<string, object?> { ["name"] = "B", ["id"] = 2 },
                new Dictionary<string, object?> { ["name"] = "C", ["id"] = 3 },
            },
        };
        var result = await this.Run(new object[] { op });

        result.Outputs["success"].Should().Be(true);
        var results = (IReadOnlyList<DbOperationResult>)result.Outputs["results"]!;
        results[0].IsBatchOp.Should().BeTrue();
        results[0].BatchExecutionCount.Should().Be(3);
        results[0].AffectedRows.Should().Be(3);
        this.NameOf(1).Should().Be("A");
        this.NameOf(3).Should().Be("C");
    }

    [Fact]
    public async Task Transaction_BatchOp_WhereGuard_ZeroAffectedRowNotError()
    {
        this.Seed();
        var op = new Dictionary<string, object?>
        {
            ["sql"] = "UPDATE users SET name = @name WHERE id = @id AND name != @name",
            ["parameterSets"] = new object[]
            {
                new Dictionary<string, object?> { ["name"] = "alice", ["id"] = 1 }, // guard: already 'alice' → 0 rows
                new Dictionary<string, object?> { ["name"] = "BOB", ["id"] = 2 },    // changes
            },
        };
        var result = await this.Run(new object[] { op });

        result.Outputs["success"].Should().Be(true, "a zero-row guard hit is not a failure~ 🌸");
        ((IReadOnlyList<DbOperationResult>)result.Outputs["results"]!)[0].AffectedRows.Should().Be(1);
    }

    [Fact]
    public async Task Transaction_BatchOp_ConstraintViolationMidBatch_RollsBackWithBatchRowIndex()
    {
        this.Seed();
        var op = new Dictionary<string, object?>
        {
            ["sql"] = "INSERT INTO users (id, name) VALUES (@id, @name)",
            ["parameterSets"] = new object[]
            {
                new Dictionary<string, object?> { ["id"] = 20, ["name"] = "a" },
                new Dictionary<string, object?> { ["id"] = 21, ["name"] = "b" },
                new Dictionary<string, object?> { ["id"] = 1, ["name"] = "dupe" }, // PK violation at set index 2
            },
        };
        var result = await this.Run(new object[] { op });

        result.Outputs["success"].Should().Be(false);
        var error = (DbOperationError)result.Outputs["error"]!;
        error.OperationIndex.Should().Be(0);
        error.BatchRowIndex.Should().Be(2);
        this.CountUsers().Should().Be(3, "whole batch rolled back~ 🛡️");
    }

    [Fact]
    public async Task Transaction_MixedSingleAndBatchOps_AllCommit()
    {
        this.Seed();
        var batch = new Dictionary<string, object?>
        {
            ["sql"] = "UPDATE users SET name = @name WHERE id = @id",
            ["parameterSets"] = new object[]
            {
                new Dictionary<string, object?> { ["name"] = "one", ["id"] = 1 },
                new Dictionary<string, object?> { ["name"] = "two", ["id"] = 2 },
            },
        };
        var result = await this.Run(new object[]
        {
            Op("INSERT INTO users (id, name) VALUES (30, 'thirty')"),
            batch,
            Op("INSERT INTO logs (message) VALUES ('audit')"),
        });

        result.Outputs["success"].Should().Be(true);
        var results = (IReadOnlyList<DbOperationResult>)result.Outputs["results"]!;
        results.Should().HaveCount(3);
        results[1].IsBatchOp.Should().BeTrue();
        this.NameOf(1).Should().Be("one");
        this.CountUsers().Should().Be(4);
    }

    #region Helpers 🛠️

    private static Dictionary<string, object?> Op(string sql) => new() { ["sql"] = sql };

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

    private int CountUsers()
    {
        var factory = this.BuildFactory();
        using DataConnection db = factory.CreateAsync("sqlite", this.SqliteConnectionString).AsTask().GetAwaiter().GetResult();
        return db.Execute<int>("SELECT COUNT(*) FROM users");
    }

    private string NameOf(int id)
    {
        var factory = this.BuildFactory();
        using DataConnection db = factory.CreateAsync("sqlite", this.SqliteConnectionString).AsTask().GetAwaiter().GetResult();
        return db.Execute<string>("SELECT name FROM users WHERE id = @id", new DataParameter("id", id));
    }

    private Task<ModuleResult> Run(object[] operations, string? isolationLevel = null)
    {
        var props = new Dictionary<string, object?>
        {
            ["connectionId"] = "TestDb",
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
            Services = new FactoryServiceProvider(this.BuildFactory()),
            ExecutionId = Guid.NewGuid(),
            NodeId = "db-txn-node",
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

