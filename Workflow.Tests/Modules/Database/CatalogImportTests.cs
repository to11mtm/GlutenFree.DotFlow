// <copyright file="CatalogImportTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.Database;

using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using LinqToDB.Data;
using Microsoft.Extensions.Options;
using Workflow.Modules.Database.Abstractions;
using Workflow.Modules.Database.Catalog;
using Workflow.Modules.Database.Configuration;
using Workflow.Modules.Database.Connections;
using Workflow.Modules.Database.Providers;
using Xunit;

/// <summary>
/// 📥 Phase 2.4.b.4 — Tests for the one-shot catalog schema importer (SQLite)~ ✨💖.
/// </summary>
public sealed class CatalogImportTests : IAsyncLifetime, IDisposable
{
    private const string ConnId = "ImportDb";

    private readonly string dbPath = Path.Combine(Path.GetTempPath(), $"dotflow-catalog-{Guid.NewGuid():N}.db");

    private string ConnString => $"Data Source={this.dbPath}";

    public async Task InitializeAsync()
    {
        using DataConnection db = await this.Factory().CreateAsync("sqlite", this.ConnString);
        db.Execute("CREATE TABLE orders (id INTEGER NOT NULL, customer_id INTEGER, total NUMERIC NOT NULL)");
        db.Execute("CREATE TABLE customers (id INTEGER NOT NULL, name TEXT)");
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
            // best-effort cleanup~ 🧹
        }
    }

    [Fact]
    public async Task CatalogImport_Sqlite_PragmaTableInfo_PopulatesCatalog()
    {
        var catalog = new InMemoryWorkflowTableCatalog();
        var importer = new CatalogSchemaImporter(this.Factory(), catalog);

        var count = await importer.ImportAsync(ConnId);

        count.Should().Be(2);
        var tables = await catalog.ListAsync(ConnId);
        tables.Should().HaveCount(2);

        var orders = tables.Single(t => t.TableName == "orders");
        orders.Columns.Should().NotBeNull();
        orders.Columns!.Select(c => c.Name).Should().BeEquivalentTo("id", "customer_id", "total");

        var total = orders.Columns.Single(c => c.Name == "total");
        total.Nullable.Should().BeFalse("total is declared NOT NULL~");
        var customerId = orders.Columns.Single(c => c.Name == "customer_id");
        customerId.Nullable.Should().BeTrue("customer_id has no NOT NULL constraint~");
    }

    [Fact]
    public async Task CatalogImport_UnknownConnection_ThrowsConnectionNotFound()
    {
        var importer = new CatalogSchemaImporter(this.Factory(), new InMemoryWorkflowTableCatalog());

        var act = () => importer.ImportAsync("does-not-exist");

        await act.Should().ThrowAsync<ConnectionNotFoundException>();
    }

    private DefaultDbConnectionFactory Factory()
    {
        var options = new DatabaseConnectionsOptions();
        options.Connections[ConnId] = new DbConnectionDescriptor(ConnId, "sqlite", this.ConnString);
        var registry = new InMemoryDbConnectionRegistry(Options.Create(options));
        return new DefaultDbConnectionFactory(registry, new DefaultDbProviderRegistry());
    }
}

