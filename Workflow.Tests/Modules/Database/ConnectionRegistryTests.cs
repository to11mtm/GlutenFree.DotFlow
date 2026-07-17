// <copyright file="ConnectionRegistryTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.Database;

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using LinqToDB;
using LinqToDB.Async;
using Workflow.Modules.Database.Abstractions;
using Workflow.Persistence.Sqlite;
using Workflow.Persistence.Sqlite.Data;
using Xunit;

/// <summary>
/// 📇🔒 Phase 2.4.a.5 — Tests for the SQLite-persisted <see cref="IDbConnectionRegistry"/>
/// and its at-rest connection-string encryption~ ✨💖.
/// </summary>
public sealed class ConnectionRegistryTests : IAsyncLifetime, IDisposable
{
    private readonly string dbPath = Path.Combine(Path.GetTempPath(), $"dotflow-connreg-test-{Guid.NewGuid():N}.db");
    private readonly FakeProtector protector = new();
    private SqlitePersistenceProvider provider = null!;

    private string ConnectionString => $"Data Source={this.dbPath};Pooling=False";

    public async Task InitializeAsync()
    {
        this.provider = new SqlitePersistenceProvider(this.ConnectionString);
        await this.provider.InitializeAsync();
    }

    public async Task DisposeAsync() => await this.provider.DisposeAsync();

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
            // Best-effort temp cleanup — a lingering pooled handle shouldn't fail the test~ 🧹
        }
    }

    [Fact]
    public async Task SqliteRegistry_UpsertThenGet_RoundTrips()
    {
        var registry = this.provider.CreateDbConnectionRegistry(this.protector);
        var descriptor = new DbConnectionDescriptor("OrdersDb", "postgres", "Host=localhost;Database=orders", "Orders", true);

        await registry.UpsertAsync(descriptor);
        var got = await registry.GetAsync("OrdersDb");

        got.IsSome.Should().BeTrue();
        var value = got.Match(d => d, () => throw new Xunit.Sdk.XunitException("expected Some"));
        value.ConnectionString.Should().Be("Host=localhost;Database=orders", "the plaintext round-trips through encryption~ 🔓");
        value.ProviderKey.Should().Be("postgres");
        value.DisplayName.Should().Be("Orders");
    }

    [Fact]
    public async Task SqliteRegistry_UpsertEncryptsConnectionString()
    {
        var registry = this.provider.CreateDbConnectionRegistry(this.protector);
        await registry.UpsertAsync(new DbConnectionDescriptor("SecretDb", "sqlite", "Data Source=secret.db"));

        // Read the RAW stored value — it must be ciphertext, not the plaintext~ 🔐
        var factory = new WorkflowDataConnectionFactory(this.ConnectionString);
        await using var db = factory.Create();
        var entity = await db.DbConnections.FirstOrDefaultAsync(c => c.ConnectionId == "SecretDb");

        entity.Should().NotBeNull();
        entity!.ConnectionStringEncrypted.Should().NotBe("Data Source=secret.db", "the stored value must be encrypted~ 🔒");
        entity.ConnectionStringEncrypted.Should().StartWith("ENC(", "the fake protector wraps ciphertext~");
    }

    [Fact]
    public async Task SqliteRegistry_List_ReturnsAllDecrypted()
    {
        var registry = this.provider.CreateDbConnectionRegistry(this.protector);
        await registry.UpsertAsync(new DbConnectionDescriptor("A", "sqlite", "Data Source=a.db"));
        await registry.UpsertAsync(new DbConnectionDescriptor("B", "postgres", "Host=b"));

        var all = await registry.ListAsync();

        all.Should().HaveCount(2);
        all.Should().Contain(d => d.Id == "A" && d.ConnectionString == "Data Source=a.db");
        all.Should().Contain(d => d.Id == "B" && d.ConnectionString == "Host=b");
    }

    [Fact]
    public async Task SqliteRegistry_Upsert_UpdatesExisting()
    {
        var registry = this.provider.CreateDbConnectionRegistry(this.protector);
        await registry.UpsertAsync(new DbConnectionDescriptor("Db", "sqlite", "v1"));
        await registry.UpsertAsync(new DbConnectionDescriptor("Db", "sqlite", "v2", "renamed"));

        var got = await registry.GetAsync("Db");
        var value = got.Match(d => d, () => throw new Xunit.Sdk.XunitException("expected Some"));
        value.ConnectionString.Should().Be("v2");
        value.DisplayName.Should().Be("renamed");
        (await registry.ListAsync()).Should().HaveCount(1, "upsert updates, not duplicates~ 🌸");
    }

    [Fact]
    public async Task SqliteRegistry_DeleteUnknown_ReturnsFalse()
    {
        var registry = this.provider.CreateDbConnectionRegistry(this.protector);
        (await registry.DeleteAsync("nope")).Should().BeFalse();
    }

    [Fact]
    public async Task SqliteRegistry_Persist_SurvivesAcrossRegistryInstances()
    {
        // Simulate a "restart": upsert via one registry, read via a fresh registry over the same file~ 🔁
        var writer = this.provider.CreateDbConnectionRegistry(this.protector);
        await writer.UpsertAsync(new DbConnectionDescriptor("Durable", "postgres", "Host=durable"));

        var reader = this.provider.CreateDbConnectionRegistry(this.protector);
        var got = await reader.GetAsync("Durable");

        got.IsSome.Should().BeTrue();
        got.Match(d => d.ConnectionString, () => string.Empty).Should().Be("Host=durable");
    }

    /// <summary>Reversible fake protector: <c>ENC(base64)</c>~ 🔐 (test-only).</summary>
    private sealed class FakeProtector : IConnectionStringProtector
    {
        public string Protect(string plaintext)
            => "ENC(" + Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext)) + ")";

        public string Unprotect(string protectedValue)
        {
            var inner = protectedValue.Substring(4, protectedValue.Length - 5);
            return Encoding.UTF8.GetString(Convert.FromBase64String(inner));
        }
    }
}

