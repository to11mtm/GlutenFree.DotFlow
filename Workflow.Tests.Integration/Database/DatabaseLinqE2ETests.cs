// <copyright file="DatabaseLinqE2ETests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Integration.Database;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LanguageExt;
using LinqToDB.Data;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Database.Abstractions;
using Workflow.Modules.Database.Builtin;
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
/// 🎬🌟 Phase 2.4.b.6 — End-to-end demo proving the typed-linq family (compile → publish → execute)
/// works against a real Postgres Testcontainer, and that a typed-linq node cooperates with the raw-SQL
/// escape-hatch family in one workflow, sharing a single named-connection registry~ ✨💖.
/// </summary>
/// <remarks>
/// <para>
/// The Phase 2.4.b demo workflow shape is:
/// <code>
/// webhook_trigger → linq(orders_over_threshold) → condition
///                 → transaction[update_inventory; insert_audit] (escape hatch) → setvariable
/// </code>
/// </para>
/// <para>
/// CopilotNote (deviation): consistent with every other Postgres integration test in this project, the
/// E2E invokes the <b>modules directly</b> rather than spinning up the Akka engine. "Publish" is
/// modelled by compiling the typed body and storing the assembly into the compile cache (exactly what
/// <c>POST /api/database/linq/compile</c> does); "condition" + "setvariable" are represented by plain
/// assertions + a local variables bag. Requires Docker; <c>[Trait("Category", "Integration")]</c> lets
/// Docker-less CI skip it~ 🐳🌸.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class DatabaseLinqE2ETests : IAsyncLifetime
{
    private const string ConnId = "ShopDb";

    private readonly PostgreSqlContainer container = new PostgreSqlBuilder()
        .WithImage("postgres:15-alpine")
        .WithDatabase("linq_e2e")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly LinqQueryModule linq = new();
    private readonly DatabaseTransactionModule transaction = new();
    private readonly WorkflowLinqCompiler compiler = new(new TableTypeResolver());
    private readonly InMemoryBlobStore blobStore = new();
    private readonly HmacLinqAssemblySigner signer = new(new EphemeralLinqHmacKeyProvider());

    private string ConnString => this.container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await this.container.StartAsync();

        using DataConnection db = await this.Factory().CreateAsync("postgres", this.ConnString);
        db.Execute("CREATE TABLE orders (id INT PRIMARY KEY, product TEXT NOT NULL, total NUMERIC(12,2) NOT NULL)");
        db.Execute("CREATE TABLE inventory (sku TEXT PRIMARY KEY, stock INT NOT NULL)");
        db.Execute("CREATE TABLE audit (id SERIAL PRIMARY KEY, note TEXT NOT NULL)");
        db.Execute("INSERT INTO orders (id, product, total) VALUES (1,'WIDGET',5.0),(2,'GADGET',20.0),(3,'WIDGET',50.0)");
        db.Execute("INSERT INTO inventory (sku, stock) VALUES ('WIDGET', 100), ('GADGET', 100)");
    }

    public async Task DisposeAsync() => await this.container.DisposeAsync();

    /// <summary>
    /// 🌟 Compile a typed-linq body → "publish" (store the compiled assembly into the cache) → execute
    /// the <see cref="LinqQueryModule"/> against real Postgres, asserting the filtered rows come back~ ✨.
    /// </summary>
    [Fact]
    public async Task Demo_TypedLinqFlow_CompilePublishExecute_Succeeds()
    {
        // 1️⃣ compile + publish (store into the compile cache — same as POST /compile)~ 🧬
        var key = await this.CompileAndStore(
            "return db.orders.Where(o => o.total >= inputs.MinTotal).OrderBy(o => o.id).ToList();",
            Schema(Prop("MinTotal", typeof(decimal))));

        // 2️⃣ execute the typed node~ 🌟
        var result = await this.RunLinq(key, new Dictionary<string, object?> { ["MinTotal"] = 10m });

        result.Success.Should().BeTrue("error: " + result.ErrorMessage);
        result.Outputs["rowCount"].Should().Be(2, "orders 2 (20.0) and 3 (50.0) clear the 10.0 threshold~ 🌟");
        var rows = (IReadOnlyList<IReadOnlyDictionary<string, object?>>)result.Outputs["rows"]!;
        rows.Should().HaveCount(2);
    }

    /// <summary>
    /// 🤝 A typed-linq read + an escape-hatch transaction cooperate in one flow over one shared
    /// connection registry — proving both families interoperate (D12/D13)~ ✨.
    /// </summary>
    [Fact]
    public async Task Demo_MixedTypedAndEscapeHatch_Workflow_Succeeds()
    {
        // 1️⃣ typed linq — find orders over a threshold~ 🌟
        var key = await this.CompileAndStore(
            "return db.orders.Where(o => o.total >= inputs.MinTotal).ToList();",
            Schema(Prop("MinTotal", typeof(decimal))));
        var big = await this.RunLinq(key, new Dictionary<string, object?> { ["MinTotal"] = 20m });

        big.Success.Should().BeTrue("error: " + big.ErrorMessage);
        var bigCount = (int)big.Outputs["rowCount"]!;
        bigCount.Should().Be(2, "orders 2 + 3 are >= 20~");

        // 2️⃣ condition — only proceed when there ARE big orders (represented as a plain guard)~ 🔀
        bigCount.Should().BeGreaterThan(0, "the condition gate opens when big orders exist~");

        // 3️⃣ escape-hatch transaction — atomically adjust inventory + write an audit row~ 💼
        var txn = await this.transaction.ExecuteAsync(
            this.Context(new Dictionary<string, object?>
            {
                ["connectionId"] = ConnId,
                ["operations"] = new object[]
                {
                    new Dictionary<string, object?> { ["sql"] = "UPDATE inventory SET stock = stock - 1 WHERE sku = 'WIDGET'" },
                    new Dictionary<string, object?> { ["sql"] = "INSERT INTO audit (note) VALUES ('big orders processed')" },
                },
            }),
            CancellationToken.None);

        txn.Outputs["success"].Should().Be(true, "the escape-hatch transaction commits~ 💼");

        // 4️⃣ typed linq again — confirm the same connection registry sees committed state~ 🌟
        var reread = await this.RunLinq(key, new Dictionary<string, object?> { ["MinTotal"] = 20m });
        reread.Outputs["rowCount"].Should().Be(2, "the typed re-read still sees the two big orders~");

        // 5️⃣ setvariable — capture the cooperative end-state into a local bag~ 📝
        var variables = new Dictionary<string, object?> { ["bigOrders"] = bigCount, ["done"] = true };
        variables["bigOrders"].Should().Be(2);

        // End-state: the escape-hatch step's side effects are visible~ ✅
        this.Scalar<int>("SELECT stock FROM inventory WHERE sku = 'WIDGET'").Should().Be(99);
        this.Scalar<int>("SELECT COUNT(*) FROM audit").Should().Be(1);
    }

    // ── Helpers 🛠️ ───────────────────────────────────────────────────────────────────────

    private static WorkflowTableMetadata OrdersTable() =>
        new(
            ConnectionId: ConnId,
            TableName: "orders",
            Columns: new[]
            {
                new WorkflowColumnMetadata("id", "integer", false),
                new WorkflowColumnMetadata("product", "text", true),
                new WorkflowColumnMetadata("total", "numeric", false),
            });

    private static ModulePropertyDefinition Prop(string name, Type type, bool required = false) =>
        new(name, name, type, IsRequired: required);

    private static ModuleSchema Schema(params ModulePropertyDefinition[] props) =>
        new(Arr<PortDefinition>.Empty, Arr<PortDefinition>.Empty, Arr.create(props));

    private DefaultDbConnectionFactory Factory()
    {
        var options = new DatabaseConnectionsOptions();
        options.Connections[ConnId] = new DbConnectionDescriptor(ConnId, "postgres", this.ConnString);
        var registry = new InMemoryDbConnectionRegistry(Options.Create(options));
        return new DefaultDbConnectionFactory(registry, new DefaultDbProviderRegistry());
    }

    private CompiledAssemblyCache Cache() =>
        new(this.blobStore, this.signer, Options.Create(new LinqCompileCacheOptions()));

    private T Scalar<T>(string sql)
    {
        using DataConnection db = this.Factory().CreateAsync("postgres", this.ConnString).AsTask().GetAwaiter().GetResult();
        return db.Execute<T>(sql);
    }

    private async Task<string> CompileAndStore(string body, ModuleSchema schema)
    {
        var compile = await this.compiler.CompileAsync(
            new LinqCompileRequest("shopdef", "shopnode", body, new[] { OrdersTable() }, schema));
        compile.Success.Should().BeTrue("compile: " + string.Join(" | ", compile.Errors.Select(e => e.Id + ":" + e.Message)));

        var cache = this.Cache();
        var key = cache.ComputeKey("shopdef", "shopnode", body, LinqCodegen.SchemaVersion, new[] { OrdersTable() });
        await cache.StoreAsync(key, compile.AssemblyBytes!);
        return key;
    }

    private Task<ModuleResult> RunLinq(string key, Dictionary<string, object?> inputs)
        => this.linq.ExecuteAsync(
            this.Context(new Dictionary<string, object?>
            {
                ["connectionId"] = ConnId,
                ["compiledAssemblyKey"] = key,
                ["inputs"] = inputs,
            }),
            CancellationToken.None);

    private ModuleExecutionContext Context(Dictionary<string, object?> properties) => new()
    {
        Inputs = new Dictionary<string, object?>(),
        Properties = properties,
        Variables = new Dictionary<string, object?>(),
        Logger = NullLogger.Instance,
        Services = new LinqServiceProvider(this.Cache(), new CollectibleScriptRunner(), this.Factory()),
        ExecutionId = Guid.NewGuid(),
        NodeId = "linq-e2e-node",
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
            => serviceType == typeof(ICompiledAssemblyCache) ? this.cache
             : serviceType == typeof(ILinqScriptRunner) ? this.runner
             : serviceType == typeof(IDbConnectionFactory) ? this.factory
             : null;
    }

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

        public Task<bool> DeleteAsync(string key, CancellationToken ct = default) => Task.FromResult(this.store.TryRemove(key, out _));

        public Task<bool> ExistsAsync(string key, CancellationToken ct = default) => Task.FromResult(this.store.ContainsKey(key));

        public Task<string> GeneratePresignedUrlAsync(string key, TimeSpan expiry, CancellationToken ct = default) => Task.FromResult($"memory://{key}");
    }
}

