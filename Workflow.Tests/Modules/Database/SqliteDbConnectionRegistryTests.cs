// <copyright file="SqliteDbConnectionRegistryTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.Database;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Workflow.Modules.Database;
using Workflow.Modules.Database.Abstractions;
using Workflow.Modules.Database.Configuration;
using Workflow.Modules.Database.Connections;
using Xunit;

/// <summary>
/// 💾 Tests for the SQLite-persisted named-connection registry: durability across restarts, the
/// at-rest protector seam, and config-vs-user row precedence~ ✨.
/// </summary>
public sealed class SqliteDbConnectionRegistryTests : IDisposable
{
    private readonly string dbPath = Path.Combine(Path.GetTempPath(), $"dotflow-conn-{Guid.NewGuid():N}.db");

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
            // Temp files are the OS's problem after this~
        }
    }

    [Fact]
    public async Task Upsert_ThenGet_RoundTrips()
    {
        var registry = this.Create();

        await registry.UpsertAsync(new DbConnectionDescriptor("pg-main", "postgres", "Host=db;Database=x", "Main"));

        var found = await registry.GetAsync("PG-MAIN"); // case-insensitive~
        found.IsSome.Should().BeTrue();
        var descriptor = found.IfNone(() => throw new InvalidOperationException());
        descriptor.ProviderKey.Should().Be("postgres");
        descriptor.ConnectionString.Should().Be("Host=db;Database=x");
        descriptor.DisplayName.Should().Be("Main");
        descriptor.Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task Connections_SurviveARestart()
    {
        var first = this.Create();
        await first.UpsertAsync(new DbConnectionDescriptor("sq", "sqlite", "Data Source=x.db", "Local"));

        // A brand-new registry over the same file = a process restart~ 🔁
        var second = this.Create();

        var list = await second.ListAsync();
        list.Should().ContainSingle(c => c.Id == "sq" && c.ConnectionString == "Data Source=x.db");
    }

    [Fact]
    public async Task Delete_RemovesTheRow_AndReportsWhetherItExisted()
    {
        var registry = this.Create();
        await registry.UpsertAsync(new DbConnectionDescriptor("gone", "sqlite", "Data Source=y.db"));

        (await registry.DeleteAsync("gone")).Should().BeTrue();
        (await registry.DeleteAsync("gone")).Should().BeFalse();
        (await registry.GetAsync("gone")).IsNone.Should().BeTrue();
    }

    [Fact]
    public async Task ConnectionStrings_AreProtectedAtRest()
    {
        var protector = new ReversingProtector();
        var registry = this.Create(protector);

        await registry.UpsertAsync(new DbConnectionDescriptor("secret", "postgres", "Password=hunter2"));

        // Raw storage never holds the plaintext…
        var raw = ReadRawConnectionString(this.dbPath, "secret");
        raw.Should().NotBe("Password=hunter2");
        protector.Protected.Should().Contain("Password=hunter2");

        // …but reads come back plain through the same seam~ 🔐
        var found = await registry.GetAsync("secret");
        found.IfNone(() => throw new InvalidOperationException()).ConnectionString.Should().Be("Password=hunter2");
    }

    [Fact]
    public async Task UnreadableRow_DoesNotBreakListing_AndSurfacesDisabled()
    {
        // Written under one protector, read under another (key rotation / different host).
        var writer = this.Create(new ReversingProtector());
        await writer.UpsertAsync(new DbConnectionDescriptor("rotated", "postgres", "Password=hunter2"));

        var reader = this.Create(new ThrowingProtector());

        var list = await reader.ListAsync();
        list.Should().ContainSingle(c => c.Id == "rotated" && !c.Enabled);
    }

    [Fact]
    public async Task ConfigConnections_AreSeeded_AndReSeededOnRestart()
    {
        var config = new DatabaseConnectionsOptions
        {
            Connections = new Dictionary<string, DbConnectionDescriptor>
            {
                ["ops"] = new("ignored-id", "postgres", "Host=ops", "Ops DB"),
            },
        };

        var first = this.Create(config: config);
        (await first.ListAsync()).Should().ContainSingle(c => c.Id == "ops", "the dictionary key wins over the descriptor id~");

        // User rows persist alongside config rows across restarts~
        await first.UpsertAsync(new DbConnectionDescriptor("from-ui", "sqlite", "Data Source=ui.db"));
        var second = this.Create(config: config);

        var list = await second.ListAsync();
        list.Should().HaveCount(2);
        list.Should().Contain(c => c.Id == "from-ui");
        list.Should().Contain(c => c.Id == "ops" && c.ConnectionString == "Host=ops");
    }

    [Fact]
    public void Di_UsesInMemoryRegistry_ByDefault_AndSqliteWhenEnabled()
    {
        var plain = new ServiceCollection();
        plain.AddDatabaseModules();
        plain.BuildServiceProvider().GetRequiredService<IDbConnectionRegistry>()
            .Should().BeOfType<InMemoryDbConnectionRegistry>();

        var persisted = new ServiceCollection();
        persisted.AddDatabaseModules();
        persisted.Configure<ConnectionStoreOptions>(o =>
        {
            o.Enabled = true;
            o.Path = this.dbPath;
        });
        persisted.BuildServiceProvider().GetRequiredService<IDbConnectionRegistry>()
            .Should().BeOfType<SqliteDbConnectionRegistry>();
    }

    private static string ReadRawConnectionString(string path, string id)
    {
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT connection_string FROM connections WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        return (string)cmd.ExecuteScalar()!;
    }

    private SqliteDbConnectionRegistry Create(
        IConnectionStringProtector? protector = null,
        DatabaseConnectionsOptions? config = null)
        => new(
            Options.Create(new ConnectionStoreOptions { Enabled = true, Path = this.dbPath }),
            Options.Create(config ?? new DatabaseConnectionsOptions()),
            protector ?? new NoOpConnectionStringProtector());

    /// <summary>A trivially reversible stand-in for a real at-rest protector~ 🔐.</summary>
    private sealed class ReversingProtector : IConnectionStringProtector
    {
        public List<string> Protected { get; } = new();

        public string Protect(string plaintext)
        {
            this.Protected.Add(plaintext);
            return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plaintext));
        }

        public string Unprotect(string protectedValue)
            => System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(protectedValue));
    }

    /// <summary>Stands in for keys that can no longer decrypt existing rows~ 🔑💥.</summary>
    private sealed class ThrowingProtector : IConnectionStringProtector
    {
        public string Protect(string plaintext) => plaintext;

        public string Unprotect(string protectedValue)
            => throw new System.Security.Cryptography.CryptographicException("key mismatch");
    }
}
