// <copyright file="PostgresLinqTests.cs" company="GlutenFree">
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
/// 🐘🌟 Phase 2.4.b.3 — Postgres Testcontainers tests for <see cref="LinqQueryModule"/> (typed linq)~ ✨💖.
/// </summary>
/// <remarks>CopilotNote: Requires Docker. <c>[Trait("Category", "Integration")]</c> lets Docker-less CI skip~ 🐳.</remarks>
[Trait("Category", "Integration")]
public sealed class PostgresLinqTests : IAsyncLifetime
{
    private const string ConnId = "Pg";

    private readonly PostgreSqlContainer container = new PostgreSqlBuilder()
        .WithImage("postgres:15-alpine")
        .WithDatabase("linq_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly LinqQueryModule module = new();
    private readonly WorkflowLinqCompiler compiler = new(new TableTypeResolver());
    private readonly InMemoryBlobStore blobStore = new();
    private readonly HmacLinqAssemblySigner signer = new(new EphemeralLinqHmacKeyProvider());

    private string ConnString => this.container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await this.container.StartAsync();
        using DataConnection db = await this.Factory().CreateAsync("postgres", this.ConnString);
        db.Execute("CREATE TABLE orders (id INT PRIMARY KEY, name TEXT, total NUMERIC(12,2))");
        db.Execute("INSERT INTO orders (id, name, total) VALUES (1,'A',5.0),(2,'B',20.0),(3,'C',50.0)");
    }

    public async Task DisposeAsync() => await this.container.DisposeAsync();

    [Fact]
    public async Task Postgres_LinqModule_RoundTrips()
    {
        var result = await this.RunModule("return db.orders.Where(o => o.total > 10m).ToList();");

        result.Success.Should().BeTrue("error: " + result.ErrorMessage);
        result.Outputs["rowCount"].Should().Be(2);
    }

    [Fact]
    public async Task Postgres_LinqModule_ConcurrentExecutions_IsolatedAlcs()
    {
        var key = await this.CompileAndStore("return db.orders.Where(o => o.total > 10m).ToList();");

        var tasks = Enumerable.Range(0, 8).Select(_ => this.module.ExecuteAsync(this.Context(key), CancellationToken.None));
        var results = await Task.WhenAll(tasks);

        results.Should().OnlyContain(r => r.Success);
        results.Should().OnlyContain(r => (int)r.Outputs["rowCount"]! == 2);
    }

    // ── Helpers 🛠️ ───────────────────────────────────────────────────────────────────────

    private static WorkflowTableMetadata OrdersTable() =>
        new(
            ConnectionId: ConnId,
            TableName: "orders",
            Columns: new[]
            {
                new WorkflowColumnMetadata("id", "integer", false),
                new WorkflowColumnMetadata("name", "text", true),
                new WorkflowColumnMetadata("total", "numeric", false),
            });

    private DefaultDbConnectionFactory Factory()
    {
        var options = new DatabaseConnectionsOptions();
        options.Connections[ConnId] = new DbConnectionDescriptor(ConnId, "postgres", this.ConnString);
        var registry = new InMemoryDbConnectionRegistry(Options.Create(options));
        return new DefaultDbConnectionFactory(registry, new DefaultDbProviderRegistry());
    }

    private CompiledAssemblyCache Cache() =>
        new(this.blobStore, this.signer, Options.Create(new LinqCompileCacheOptions()));

    private async Task<string> CompileAndStore(string body)
    {
        var schema = new ModuleSchema(Arr<PortDefinition>.Empty, Arr<PortDefinition>.Empty, Arr<ModulePropertyDefinition>.Empty);
        var compile = await this.compiler.CompileAsync(new LinqCompileRequest("def1", "node1", body, new[] { OrdersTable() }, schema));
        compile.Success.Should().BeTrue();

        var cache = this.Cache();
        var key = cache.ComputeKey("def1", "node1", body, LinqCodegen.SchemaVersion, new[] { OrdersTable() });
        await cache.StoreAsync(key, compile.AssemblyBytes!);
        return key;
    }

    private async Task<ModuleResult> RunModule(string body)
    {
        var key = await this.CompileAndStore(body);
        return await this.module.ExecuteAsync(this.Context(key), CancellationToken.None);
    }

    private ModuleExecutionContext Context(string key) => new()
    {
        Inputs = new Dictionary<string, object?>(),
        Properties = new Dictionary<string, object?> { ["connectionId"] = ConnId, ["compiledAssemblyKey"] = key },
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

