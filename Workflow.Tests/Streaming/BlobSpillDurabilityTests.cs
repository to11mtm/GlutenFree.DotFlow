// <copyright file="BlobSpillDurabilityTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Streaming;

using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Api.Database;
using Workflow.Api.Streaming;
using Workflow.Modules.Streaming;
using Workflow.Persistence.Abstractions;
using Workflow.Persistence.Sqlite.Data;
using Workflow.Persistence.Sqlite.Repositories;
using Xunit;

/// <summary>
/// Stage 7.1 — <c>Streaming:Spill:Store=Blob</c> must not land in a blob store that lives in memory
/// (it would give no memory relief). The default persistence (SQLite <c>:memory:</c>) has such a
/// blob store, so a host configured that way refuses to start with a clear message.
/// </summary>
public sealed class BlobSpillDurabilityTests
{
    [Fact]
    public void BlobSpill_OnTheDefaultInMemorySqlitePersistence_RefusesToStart()
    {
        using var factory = new SpillHost(Workflow.Tests.TestSqlite.InMemory());

        var start = () => factory.CreateClient();

        start.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("in-memory").And.Contain("Streaming:Spill:Store");
    }

    [Fact]
    public void BlobSpill_OnAFileBackedSqliteDatabase_Starts()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dotflow-blobspill-{Guid.NewGuid():N}.db");
        try
        {
            using (var factory = new SpillHost($"Data Source={path};Pooling=False"))
            {
                factory.CreateClient();
                factory.Services.GetRequiredService<ISpillStore>().Should().BeOfType<BlobStoreSpillStore>();
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Theory]
    [InlineData(":memory:", true)]
    [InlineData("Data Source=:memory:", true)]
    [InlineData("Data Source=file:shared-x?mode=memory&cache=shared", true)]
    [InlineData("Data Source=file:dotflow-x;Mode=Memory;Cache=Shared", true)]
    [InlineData("Data Source=C:\\data\\workflow.db", false)]
    public void SqliteBlobStore_KnowsWhenItsDatabaseIsInMemory(string connectionString, bool inMemory)
    {
        IBlobStore store = new SqliteBlobStore(new WorkflowDataConnectionFactory(connectionString));

        store.IsInMemory.Should().Be(inMemory);
    }

    [Fact]
    public void TheInMemoryFallback_IsInMemory_AndIsRejected()
    {
        IBlobStore store = new InMemoryBlobStore();

        store.IsInMemory.Should().BeTrue();
        var create = () => new BlobStoreSpillStore(store);
        create.Should().Throw<InvalidOperationException>();
    }

    private sealed class SpillHost : WebApplicationFactory<Program>
    {
        private readonly string connectionString;

        public SpillHost(string connectionString) => this.connectionString = connectionString;

        // UseSetting, not ConfigureAppConfiguration: Program.cs reads the spill settings while it
        // registers services, before app-configuration callbacks from the factory are applied.
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Persistence:Provider", "sqlite");
            builder.UseSetting("Persistence:ConnectionString", this.connectionString);
            builder.UseSetting("Streaming:Spill:Store", "Blob");
            builder.UseSetting("Streaming:Spill:Encrypt", "false");
        }
    }
}
