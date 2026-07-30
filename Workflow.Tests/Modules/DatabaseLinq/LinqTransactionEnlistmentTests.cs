// <copyright file="LinqTransactionEnlistmentTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.DatabaseLinq;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LanguageExt;
using LinqToDB.Data;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Database.Abstractions;
using Workflow.Modules.Database.Configuration;
using Workflow.Modules.Database.Connections;
using Workflow.Modules.Database.Linq.Abstractions;
using Workflow.Modules.Database.Linq.Builtin;
using Workflow.Modules.Database.Linq.Compilation;
using Workflow.Modules.Database.Linq.Execution;
using Workflow.Modules.Database.Providers;
using Workflow.Persistence.Abstractions;
using Workflow.Tests.Scripting;
using Xunit;

/// <summary>
/// 💼🧬 A LINQ step used as a transaction-body node must run on the SAME open connection and
/// transaction as the body — so it sees the body's uncommitted writes and rolls back with it~ ✨.
/// </summary>
public sealed class LinqTransactionEnlistmentTests : IAsyncLifetime, IDisposable
{
    private const string ConnId = "TxnDb";

    private readonly string dbPath = Path.Combine(Path.GetTempPath(), $"dotflow-linqtxn-{Guid.NewGuid():N}.db");
    private readonly LinqQueryModule module = new();
    private readonly WorkflowLinqCompiler compiler = new(new TableTypeResolver());
    private readonly InMemoryBlobStore blobStore = new();
    private readonly HmacLinqAssemblySigner signer = new(new EphemeralLinqHmacKeyProvider());

    private string ConnString => $"Data Source={this.dbPath}";

    public async Task InitializeAsync()
    {
        using DataConnection db = await this.Factory().CreateAsync(ConnId);
        db.Execute("CREATE TABLE Orders (id INTEGER, total NUMERIC)");
        db.Execute("INSERT INTO Orders (id, total) VALUES (1, 5.0)");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(this.dbPath))
            {
                File.Delete(this.dbPath);
            }
        }
        catch (IOException)
        {
            // best-effort temp cleanup~ 🧹
        }
    }

    [Fact]
    public async Task LinqStep_InsideTransactionBody_SeesUncommittedRows_AndRollsBackWithIt()
    {
        var ambient = new FakeAmbientTransactions();
        var executionId = Guid.NewGuid();

        using var open = await this.Factory().CreateAsync(ConnId);
        open.BeginTransaction();
        open.Execute("INSERT INTO Orders (id, total) VALUES (99, 999.0)");
        ambient.Register(executionId, ConnId, open);

        var result = await this.RunLinq(
            "return db.Orders.Where(o => o.total > 900m).ToList();",
            ambient,
            executionId);

        result.Success.Should().BeTrue(Dump(result));
        result.Outputs["rowCount"].Should().Be(
            1, "the linq step joined the open transaction and sees its uncommitted row~ 💼");

        open.RollbackTransaction();
        ambient.Remove(executionId, ConnId);

        using var check = await this.Factory().CreateAsync(ConnId);
        check.Execute<int>("SELECT COUNT(*) FROM Orders WHERE id = 99")
            .Should().Be(0, "everything in the body rolled back~ 🔒");
    }

    [Fact]
    public async Task LinqStep_AmbientOnADifferentConnection_IsNotEnlisted()
    {
        // Auto-enlistment matches on connectionId — a transaction on another connection
        // must not capture this node~ 🎯
        var ambient = new FakeAmbientTransactions();
        var executionId = Guid.NewGuid();

        using var open = await this.Factory().CreateAsync(ConnId);
        open.BeginTransaction();
        open.Execute("INSERT INTO Orders (id, total) VALUES (98, 998.0)");
        ambient.Register(executionId, "SomeOtherDb", open);

        var result = await this.RunLinq(
            "return db.Orders.Where(o => o.total > 900m).ToList();",
            ambient,
            executionId);

        result.Success.Should().BeTrue(Dump(result));
        result.Outputs["rowCount"].Should().Be(
            0, "it opened its own connection and cannot see the other transaction~");

        open.RollbackTransaction();
    }

    [Fact]
    public async Task LinqStep_WithNoAmbientTransaction_RunsNormally()
    {
        var result = await this.RunLinq("return db.Orders.ToList();", ambient: null, executionId: Guid.NewGuid());

        result.Success.Should().BeTrue(Dump(result));
        result.Outputs["rowCount"].Should().Be(1);
    }

    private static string Dump(ModuleResult r) => r.ErrorMessage ?? "(no error)";

    private static WorkflowTableMetadata OrdersTable() =>
        new(
            ConnectionId: ConnId,
            TableName: "Orders",
            Columns: new[]
            {
                new WorkflowColumnMetadata("id", "integer", false),
                new WorkflowColumnMetadata("total", "numeric", false),
            });

    private static ModuleSchema Schema() =>
        new(Arr<PortDefinition>.Empty, Arr<PortDefinition>.Empty, Arr<ModulePropertyDefinition>.Empty);

    private IDbConnectionFactory Factory()
    {
        var options = Options.Create(new DatabaseConnectionsOptions
        {
            Connections = new Dictionary<string, DbConnectionDescriptor>
            {
                [ConnId] = new(ConnId, "sqlite", this.ConnString),
            },
        });

        return new DefaultDbConnectionFactory(
            new InMemoryDbConnectionRegistry(options),
            new DefaultDbProviderRegistry());
    }

    private CompiledAssemblyCache Cache() =>
        new(this.blobStore, this.signer, Options.Create(new LinqCompileCacheOptions()));

    private async Task<ModuleResult> RunLinq(string body, IAmbientDbTransactions? ambient, Guid executionId)
    {
        var tables = new[] { OrdersTable() };
        var compile = await this.compiler.CompileAsync(new LinqCompileRequest("def1", "node1", body, tables, Schema()));
        compile.Success.Should().BeTrue(
            "compile: " + string.Join(" | ", System.Linq.Enumerable.Select(compile.Errors, e => e.Id + ":" + e.Message)));

        var cache = this.Cache();
        var key = cache.ComputeKey("def1", "node1", body, LinqCodegen.SchemaVersion, tables);
        await cache.StoreAsync(key, compile.AssemblyBytes!);

        var context = new ModuleExecutionContext
        {
            Inputs = new Dictionary<string, object?>(),
            Properties = new Dictionary<string, object?>
            {
                ["connectionId"] = ConnId,
                ["compiledAssemblyKey"] = key,
            },
            Variables = new Dictionary<string, object?>(),
            Logger = NullLogger.Instance,
            Services = new Provider(cache, new CollectibleScriptRunner(), this.Factory(), ambient),
            ExecutionId = executionId,
            NodeId = "linq-node",
        };

        return await this.module.ExecuteAsync(context, CancellationToken.None);
    }

    /// <summary>🧪 Minimal in-memory ambient-transaction registry~.</summary>
    private sealed class FakeAmbientTransactions : IAmbientDbTransactions
    {
        private readonly Dictionary<string, object> entries = new(StringComparer.OrdinalIgnoreCase);

        public object? TryGet(Guid executionId, string connectionId)
            => this.entries.TryGetValue(Key(executionId, connectionId), out var c) ? c : null;

        public void Register(Guid executionId, string connectionId, object connection)
            => this.entries[Key(executionId, connectionId)] = connection;

        public void Remove(Guid executionId, string connectionId)
            => this.entries.Remove(Key(executionId, connectionId));

        private static string Key(Guid executionId, string connectionId)
            => executionId.ToString("N") + "|" + connectionId;
    }

    private sealed class Provider : IServiceProvider
    {
        private readonly ICompiledAssemblyCache cache;
        private readonly ILinqScriptRunner runner;
        private readonly IDbConnectionFactory factory;
        private readonly IAmbientDbTransactions? ambient;

        public Provider(
            ICompiledAssemblyCache cache,
            ILinqScriptRunner runner,
            IDbConnectionFactory factory,
            IAmbientDbTransactions? ambient)
        {
            this.cache = cache;
            this.runner = runner;
            this.factory = factory;
            this.ambient = ambient;
        }

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(ICompiledAssemblyCache))
            {
                return this.cache;
            }

            if (serviceType == typeof(ILinqScriptRunner))
            {
                return this.runner;
            }

            if (serviceType == typeof(IDbConnectionFactory))
            {
                return this.factory;
            }

            if (serviceType == typeof(IAmbientDbTransactions))
            {
                return this.ambient;
            }

            return null;
        }
    }
}
