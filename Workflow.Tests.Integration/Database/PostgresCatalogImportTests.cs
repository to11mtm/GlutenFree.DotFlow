// <copyright file="PostgresCatalogImportTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Integration.Database;

using System.Linq;
using FluentAssertions;
using LinqToDB.Data;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;
using Workflow.Modules.Database.Abstractions;
using Workflow.Modules.Database.Catalog;
using Workflow.Modules.Database.Configuration;
using Workflow.Modules.Database.Connections;
using Workflow.Modules.Database.Providers;
using Xunit;

/// <summary>
/// 🐘📥 Phase 2.4.b.4 — Postgres Testcontainers test for the catalog schema importer
/// (<c>information_schema</c> introspection)~ ✨💖.
/// </summary>
/// <remarks>CopilotNote: Requires Docker; <c>[Trait("Category", "Integration")]</c> lets Docker-less CI skip~ 🐳.</remarks>
[Trait("Category", "Integration")]
public sealed class PostgresCatalogImportTests : IAsyncLifetime
{
    private const string ConnId = "Pg";

    private readonly PostgreSqlContainer container = new PostgreSqlBuilder()
        .WithImage("postgres:15-alpine")
        .WithDatabase("catalog_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private string ConnString => this.container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await this.container.StartAsync();
        using DataConnection db = await this.Factory().CreateAsync("postgres", this.ConnString);
        db.Execute("CREATE TABLE orders (id INT PRIMARY KEY, name TEXT, total NUMERIC(12,2) NOT NULL)");
        db.Execute("CREATE TABLE customers (id INT PRIMARY KEY, email TEXT)");
    }

    public async Task DisposeAsync() => await this.container.DisposeAsync();

    [Fact]
    public async Task CatalogImport_Postgres_InformationSchema_PopulatesCatalog()
    {
        var catalog = new InMemoryWorkflowTableCatalog();
        var importer = new CatalogSchemaImporter(this.Factory(), catalog);

        var count = await importer.ImportAsync(ConnId);

        count.Should().BeGreaterThanOrEqualTo(2);
        var tables = await catalog.ListAsync(ConnId);

        var orders = tables.Single(t => t.TableName == "orders");
        orders.Schema.Should().Be("public");
        orders.Columns!.Select(c => c.Name).Should().Contain(new[] { "id", "name", "total" });
        orders.Columns!.Single(c => c.Name == "total").Nullable.Should().BeFalse();
        orders.Columns!.Single(c => c.Name == "name").Nullable.Should().BeTrue();
    }

    private DefaultDbConnectionFactory Factory()
    {
        var options = new DatabaseConnectionsOptions();
        options.Connections[ConnId] = new DbConnectionDescriptor(ConnId, "postgres", this.ConnString);
        var registry = new InMemoryDbConnectionRegistry(Options.Create(options));
        return new DefaultDbConnectionFactory(registry, new DefaultDbProviderRegistry());
    }
}

