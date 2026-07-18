// <copyright file="LinqSecurityTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.DatabaseLinq;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
using Workflow.Modules.Database.Linq.Abstractions;
using Workflow.Modules.Database.Linq.Builtin;
using Workflow.Modules.Database.Linq.Compilation;
using Workflow.Modules.Database.Linq.Execution;
using Workflow.Modules.Database.Providers;
using Workflow.Persistence.Abstractions;
using Xunit;

/// <summary>
/// 🔒 Phase 2.4.b.6 — Security-review tests for the typed-linq family: the collectible-ALC
/// runner must NOT accumulate ALCs under sustained load (design §8.4.4), and failure surfaces
/// must never leak the resolved connection string~ 🛡️✨.
/// </summary>
public sealed class LinqSecurityTests : IAsyncLifetime, IDisposable
{
    private const string ConnId = "SecDb";

    // A distinctive secret embedded in the connection string (via the data-source path) so we can
    // assert it never surfaces in a module error message or diagnostics~ 🕵️
    private const string Secret = "SUPERSECRETC0NNSTRINGT0KEN";

    private readonly string dbPath = Path.Combine(Path.GetTempPath(), $"dotflow-linqsec-{Secret}-{Guid.NewGuid():N}.db");
    private readonly LinqQueryModule module = new();
    private readonly WorkflowLinqCompiler compiler = new(new TableTypeResolver());
    private readonly InMemoryBlobStore blobStore = new();
    private readonly HmacLinqAssemblySigner signer = new(new EphemeralLinqHmacKeyProvider());

    private string ConnString => $"Data Source={this.dbPath}";

    public async Task InitializeAsync()
    {
        using DataConnection db = await this.Factory().CreateAsync("sqlite", this.ConnString);
        db.Execute("CREATE TABLE Orders (id INTEGER, customer_id INTEGER, name TEXT, total NUMERIC)");
        db.Execute("INSERT INTO Orders (id, customer_id, name, total) VALUES (1,100,'A',5.0),(2,100,'B',20.0),(3,200,'C',50.0)");
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
    public async Task Security_1000Executions_NoAlcAccumulation()
    {
        var (key, bytes) = await this.CompileAndStore(
            "return db.Orders.Where(o => o.total > 0m).ToList();",
            new[] { OrdersTable() },
            Schema());

        var options = await this.Factory().CreateOptionsAsync(ConnId);
        using var runner = new CollectibleScriptRunner();

        // Run the SAME compiled assembly many times. Because the runner loads one collectible ALC per
        // assembly key and reuses it, the loaded-assembly count must stay at exactly 1 — bounded by the
        // number of distinct assemblies, NOT by the execution count (design §8.4.4 leak fix)~ 🔒
        for (var i = 0; i < 1000; i++)
        {
            var run = await runner.RunAsync(key, bytes, options, new Dictionary<string, object?>(), 30, CancellationToken.None);
            run.Rows.Should().NotBeNull();
        }

        runner.LoadedAssemblyCount.Should().Be(
            1,
            "one distinct compiled assembly ⇒ exactly one collectible ALC, regardless of execution count~ 🌸");
    }

    [Fact]
    public async Task Security_ManyDistinctAssemblies_BoundedByLruCapacity()
    {
        var options = await this.Factory().CreateOptionsAsync(ConnId);
        using var runner = new CollectibleScriptRunner(loadedAssemblyCapacity: 4);

        // Compile 20 DISTINCT assemblies (different bodies ⇒ different keys) and run each once. The
        // runner must evict + unload past its LRU capacity, so the loaded count is bounded by 4~ 🧹
        for (var i = 0; i < 20; i++)
        {
            var (key, bytes) = await this.CompileAndStore(
                $"return db.Orders.Where(o => o.total > {i}m).ToList();",
                new[] { OrdersTable() },
                Schema());
            await runner.RunAsync(key, bytes, options, new Dictionary<string, object?>(), 30, CancellationToken.None);
        }

        runner.LoadedAssemblyCount.Should().BeLessThanOrEqualTo(4, "evicted ALCs are unloaded — count is LRU-bounded~ 🌸");
    }

    [Fact]
    public async Task Security_DiagnosticsNeverContainConnectionString()
    {
        // Compile a query against a table declared in metadata but ABSENT from the physical DB, so the
        // run fails at the database with a "no such table" error while the connection string (with its
        // embedded secret) is in scope. The surfaced error must NOT echo the connection string~ 🕵️
        var (key, _) = await this.CompileAndStore(
            "return db.Ghosts.ToList();",
            new[] { GhostsTable() },
            Schema());

        var ctx = this.Context(new Dictionary<string, object?>
        {
            ["connectionId"] = ConnId,
            ["compiledAssemblyKey"] = key,
        });

        var result = await this.module.ExecuteAsync(ctx, CancellationToken.None);

        result.Success.Should().BeFalse("querying a non-existent table fails at the database~");
        (result.ErrorMessage ?? string.Empty).Should().NotContain(
            Secret,
            "a failure surface must never leak the resolved connection string / its secrets~ 🔒");
        (result.ErrorMessage ?? string.Empty).Should().NotContain(
            this.ConnString,
            "the full connection string must never appear in an error message~ 🔒");
    }

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

    private static WorkflowTableMetadata GhostsTable() =>
        new(
            ConnectionId: ConnId,
            TableName: "Ghosts",
            Columns: new[]
            {
                new WorkflowColumnMetadata("id", "integer", false),
                new WorkflowColumnMetadata("name", "text", true),
            });

    private static ModuleSchema Schema(params ModulePropertyDefinition[] props) =>
        new(Arr<PortDefinition>.Empty, Arr<PortDefinition>.Empty, Arr.create(props));

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
        var compile = await this.compiler.CompileAsync(new LinqCompileRequest("secdef", "secnode", body, tables, schema));
        compile.Success.Should().BeTrue("compile: " + string.Join(" | ", compile.Errors.Select(e => e.Id + ":" + e.Message)));

        var cache = this.Cache();
        var key = cache.ComputeKey("secdef", "secnode", body, LinqCodegen.SchemaVersion, tables);
        await cache.StoreAsync(key, compile.AssemblyBytes!);
        return (key, compile.AssemblyBytes!);
    }

    private ModuleExecutionContext Context(Dictionary<string, object?> properties) => new()
    {
        Inputs = new Dictionary<string, object?>(),
        Properties = properties,
        Variables = new Dictionary<string, object?>(),
        Logger = NullLogger.Instance,
        Services = new LinqServiceProvider(this.Cache(), new CollectibleScriptRunner(), this.Factory()),
        ExecutionId = Guid.NewGuid(),
        NodeId = "linq-sec-node",
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

