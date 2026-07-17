// <copyright file="LinqQueryModuleTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.DatabaseLinq;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LanguageExt;
using LinqToDB;
using LinqToDB.Data;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Database.Abstractions;
using Workflow.Modules.Database.Configuration;
using Workflow.Modules.Database.Connections;
using Workflow.Modules.Database.Providers;
using Workflow.Modules.Database.Linq.Abstractions;
using Workflow.Modules.Database.Linq.Builtin;
using Workflow.Modules.Database.Linq.Compilation;
using Workflow.Modules.Database.Linq.Execution;
using Workflow.Persistence.Abstractions;
using Xunit;

/// <summary>
/// 🌟🔴 Phase 2.4.b.3 — Tests for <see cref="LinqQueryModule"/> + collectible ALC execution
/// (compile via 2.4.b.1 → cache via 2.4.b.2 → run against a SQLite temp file)~ ✨💖.
/// </summary>
public sealed class LinqQueryModuleTests : IAsyncLifetime, IDisposable
{
    private const string ConnId = "TestDb";

    private readonly string dbPath = Path.Combine(Path.GetTempPath(), $"dotflow-linq-{Guid.NewGuid():N}.db");
    private readonly LinqQueryModule module = new();
    private readonly WorkflowLinqCompiler compiler = new(new TableTypeResolver());
    private readonly InMemoryBlobStore blobStore = new();
    private readonly HmacLinqAssemblySigner signer = new(new EphemeralLinqHmacKeyProvider());

    private string ConnString => $"Data Source={this.dbPath}";

    public async Task InitializeAsync()
    {
        using DataConnection db = await this.Factory().CreateAsync("sqlite", this.ConnString);
        db.Execute("CREATE TABLE Orders (id INTEGER, customer_id INTEGER, name TEXT, total NUMERIC)");
        db.Execute("CREATE TABLE Customers (id INTEGER, name TEXT)");
        db.Execute("INSERT INTO Orders (id, customer_id, name, total) VALUES (1,100,'A',5.0),(2,100,'B',20.0),(3,200,'C',50.0)");
        db.Execute("INSERT INTO Customers (id, name) VALUES (100,'Alice'),(200,'Bob')");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        try
        {
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
    public void LinqModule_Metadata_IsCorrect()
    {
        this.module.ModuleId.Should().Be("builtin.database.linq");
        this.module.Category.Should().Be("Database");
        this.module.Version.Should().Be(new Version(1, 0, 0));
    }

    [Fact]
    public async Task LinqModule_SimpleWhere_ReturnsFilteredRows()
    {
        var result = await this.RunModule(
            "return db.Orders.Where(o => o.total > 10m).ToList();",
            new[] { OrdersTable() },
            Schema());

        result.Success.Should().BeTrue(Dump(result));
        result.Outputs["rowCount"].Should().Be(2);
        var rows = (IReadOnlyList<IReadOnlyDictionary<string, object?>>)result.Outputs["rows"]!;
        rows.Should().HaveCount(2);
    }

    [Fact]
    public async Task LinqModule_TypedInputs_BindCorrectly()
    {
        var result = await this.RunModule(
            "return db.Orders.Where(o => o.total >= inputs.MinTotal).ToList();",
            new[] { OrdersTable() },
            Schema(Prop("MinTotal", typeof(decimal))),
            inputs: new Dictionary<string, object?> { ["MinTotal"] = 30m });

        result.Success.Should().BeTrue(Dump(result));
        result.Outputs["rowCount"].Should().Be(1, "only the 50.0 order is >= 30~");
    }

    [Fact]
    public async Task LinqModule_JoinAcrossTables_Works()
    {
        var result = await this.RunModule(
            "return (from o in db.Orders join c in db.Customers on o.customer_id equals c.id "
            + "select new { OrderId = o.id, Customer = c.name }).ToList();",
            new[] { OrdersTable(), CustomersTable() },
            Schema());

        result.Success.Should().BeTrue(Dump(result));
        result.Outputs["rowCount"].Should().Be(3);
    }

    [Fact]
    public async Task LinqModule_ReturnsIQueryable_FailsWithMaterialisationDiagnostic()
    {
        var result = await this.RunModule(
            "return db.Orders.Where(o => o.total > 0m);",
            new[] { OrdersTable() },
            Schema());

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("materialise", "an un-materialised IQueryable must be rejected~ 🚫");
    }

    [Fact]
    public async Task LinqModule_MissingCompiledAssembly_Fails()
    {
        var ctx = this.Context(new Dictionary<string, object?>
        {
            ["connectionId"] = ConnId,
            ["compiledAssemblyKey"] = "compiled-modules/none/none/deadbeef.dll",
        });

        var result = await this.module.ExecuteAsync(ctx, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("No compiled assembly");
    }

    [Fact]
    public async Task LinqModule_TamperedBlobHmac_RejectedAtLoad()
    {
        var (key, _) = await this.CompileAndStore(
            "return db.Orders.ToList();",
            new[] { OrdersTable() },
            Schema());

        this.blobStore.Corrupt(key);

        var ctx = this.Context(new Dictionary<string, object?>
        {
            ["connectionId"] = ConnId,
            ["compiledAssemblyKey"] = key,
        });
        var result = await this.module.ExecuteAsync(ctx, CancellationToken.None);

        result.Success.Should().BeFalse("a tampered blob fails HMAC verification and is treated as absent~ 🔒");
    }

    [Fact]
    public async Task LinqModule_Cancellation_PropagatesToUserCode()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var (key, _) = await this.CompileAndStore(
            "ct.ThrowIfCancellationRequested(); return db.Orders.ToList();",
            new[] { OrdersTable() },
            Schema());

        var ctx = this.Context(new Dictionary<string, object?>
        {
            ["connectionId"] = ConnId,
            ["compiledAssemblyKey"] = key,
        });
        var result = await this.module.ExecuteAsync(ctx, cts.Token);

        result.Success.Should().BeFalse("a cancelled token flows into the user body~");
    }

    [Fact]
    public async Task LinqModule_Alc_UnloadInvoked_ResultIsAlcFree()
    {
        var (key, bytes) = await this.CompileAndStore(
            "return db.Orders.ToList();",
            new[] { OrdersTable() },
            Schema());
        _ = key;

        var options = await this.Factory().CreateOptionsAsync(ConnId);

        // The runner LoadFromStream → runs → materialises → disposes the context → Unload(), all
        // without throwing, and hands back a WeakReference to the (now-unloaded) collectible ALC~ 🧹
        var run = await new CollectibleScriptRunner().RunAsync(
            bytes, options, new Dictionary<string, object?>(), 30, CancellationToken.None);

        run.AlcWeakRef.Should().NotBeNull("the runner created + unloaded a collectible ALC~");

        // The materialised rows must be pure BCL types — NO ALC-rooted reference escapes (D8 / §8.4).
        run.Rows.Should().NotBeNull();
        foreach (var row in run.Rows!)
        {
            row.Should().BeAssignableTo<Dictionary<string, object?>>("rows are copied out into BCL dictionaries~");
            foreach (var value in row.Values)
            {
                if (value is not null)
                {
                    value.GetType().Assembly.Should().BeSameAs(
                        typeof(object).Assembly,
                        "cell values are BCL scalars, never ALC-rooted POCO types~ 🔒");
                }
            }
        }
    }

    // CopilotNote: Strict "ALC collected within N GCs under sustained load" is deliberately NOT asserted
    // here — linq2db caches compiled query delegates by entity type, transiently rooting the ALC types
    // (design §8.4.4 says treat non-unload as a warning, not an error). The no-accumulation-under-load
    // guarantee is covered by 2.4.b.6's Security_1000Executions_NoAlcAccumulation~ 🌸

    // ── Helpers 🛠️ ───────────────────────────────────────────────────────────────────────

    private static WorkflowTableMetadata OrdersTable() =>
        new(
            ConnectionId: ConnId,
            TableName: "Orders",
            Columns: new[]
            {
                new WorkflowColumnMetadata("id", "integer", false),
                new WorkflowColumnMetadata("customer_id", "integer", false),
                new WorkflowColumnMetadata("name", "text", true),
                new WorkflowColumnMetadata("total", "numeric", false),
            });

    private static WorkflowTableMetadata CustomersTable() =>
        new(
            ConnectionId: ConnId,
            TableName: "Customers",
            Columns: new[]
            {
                new WorkflowColumnMetadata("id", "integer", false),
                new WorkflowColumnMetadata("name", "text", true),
            });

    private static ModulePropertyDefinition Prop(string name, Type type, bool required = false) =>
        new(name, name, type, IsRequired: required);

    private static ModuleSchema Schema(params ModulePropertyDefinition[] props) =>
        new(Arr<PortDefinition>.Empty, Arr<PortDefinition>.Empty, Arr.create(props));

    private static string Dump(ModuleResult r) => "error: " + r.ErrorMessage;

    private DefaultDbConnectionFactory Factory()
    {
        var options = new DatabaseConnectionsOptions();
        options.Connections[ConnId] = new DbConnectionDescriptor(ConnId, "sqlite", this.ConnString);
        var registry = new InMemoryDbConnectionRegistry(Options.Create(options));
        return new DefaultDbConnectionFactory(registry, new DefaultDbProviderRegistry());
    }

    private CompiledAssemblyCache Cache() =>
        new(this.blobStore, this.signer, Options.Create(new LinqCompileCacheOptions()));

    private async Task<(string Key, byte[] Bytes)> CompileAndStore(
        string body,
        IReadOnlyList<WorkflowTableMetadata> tables,
        ModuleSchema schema)
    {
        var compile = await this.compiler.CompileAsync(new LinqCompileRequest("def1", "node1", body, tables, schema));
        compile.Success.Should().BeTrue("compile: " + string.Join(" | ", System.Linq.Enumerable.Select(compile.Errors, e => e.Id + ":" + e.Message)));

        var cache = this.Cache();
        var key = cache.ComputeKey("def1", "node1", body, LinqCodegen.SchemaVersion, tables);
        await cache.StoreAsync(key, compile.AssemblyBytes!);
        return (key, compile.AssemblyBytes!);
    }

    private async Task<ModuleResult> RunModule(
        string body,
        IReadOnlyList<WorkflowTableMetadata> tables,
        ModuleSchema schema,
        Dictionary<string, object?>? inputs = null)
    {
        var (key, _) = await this.CompileAndStore(body, tables, schema);
        var props = new Dictionary<string, object?>
        {
            ["connectionId"] = ConnId,
            ["compiledAssemblyKey"] = key,
        };
        if (inputs is not null)
        {
            props["inputs"] = inputs;
        }

        return await this.module.ExecuteAsync(this.Context(props), CancellationToken.None);
    }

    private ModuleExecutionContext Context(Dictionary<string, object?> properties) => new()
    {
        Inputs = new Dictionary<string, object?>(),
        Properties = properties,
        Variables = new Dictionary<string, object?>(),
        Logger = NullLogger.Instance,
        Services = new LinqServiceProvider(this.Cache(), new CollectibleScriptRunner(), this.Factory()),
        ExecutionId = Guid.NewGuid(),
        NodeId = "linq-node",
    };

    private sealed class LinqServiceProvider : IServiceProvider
    {
        private readonly ICompiledAssemblyCache cache;
        private readonly ILinqScriptRunner runner;
        private readonly IDbConnectionFactory factory;

        public LinqServiceProvider(ICompiledAssemblyCache cache, ILinqScriptRunner runner, IDbConnectionFactory factory)
        {
            this.cache = cache;
            this.runner = runner;
            this.factory = factory;
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

            return null;
        }
    }

    /// <summary>🗃️ Minimal in-memory <see cref="IBlobStore"/> shared across compile+run~.</summary>
    private sealed class InMemoryBlobStore : IBlobStore
    {
        private readonly ConcurrentDictionary<string, byte[]> store = new(StringComparer.Ordinal);

        public void Corrupt(string key)
        {
            if (this.store.TryGetValue(key, out var bytes) && bytes.Length > 0)
            {
                var copy = (byte[])bytes.Clone();
                copy[^1] ^= 0xFF;
                this.store[key] = copy;
            }
        }

        public async Task<string> PutAsync(string key, Stream data, string? contentType = null, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            await data.CopyToAsync(ms, ct).ConfigureAwait(false);
            this.store[key] = ms.ToArray();
            return "etag";
        }

        public Task<Stream?> GetAsync(string key, CancellationToken ct = default)
            => Task.FromResult<Stream?>(this.store.TryGetValue(key, out var b) ? new MemoryStream(b, writable: false) : null);

        public Task<bool> DeleteAsync(string key, CancellationToken ct = default)
            => Task.FromResult(this.store.TryRemove(key, out _));

        public Task<bool> ExistsAsync(string key, CancellationToken ct = default)
            => Task.FromResult(this.store.ContainsKey(key));

        public Task<string> GeneratePresignedUrlAsync(string key, TimeSpan expiry, CancellationToken ct = default)
            => Task.FromResult($"memory://{key}");
    }
}





