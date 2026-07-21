// <copyright file="HttpSqliteWebhookTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.Http;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Workflow.Core.Models;
using Workflow.Persistence.Sqlite;
using Workflow.Persistence.Sqlite.Repositories;
using Xunit;

/// <summary>
/// 💾 Phase 2.3.9 — SQLite-backed webhook registration repository tests~ ✨💖.
/// Verifies that <see cref="SqliteWebhookRegistrationRepository"/> correctly round-trips
/// all <see cref="WebhookRegistration"/> fields through a real <c>:memory:</c> SQLite database~ 🧪
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: Each test gets its own uniquely-named in-memory SQLite database
/// (via <c>InitializeAsync</c>) to guarantee full isolation. The pattern is identical to
/// <c>HttpPersistenceTests</c> — hold a connection open for the DB lifetime~ 🧠
/// </para>
/// </remarks>
public sealed class HttpSqliteWebhookTests : IAsyncLifetime
{
    private SqlitePersistenceProvider _provider = null!;
    private SqliteConnection _heldConnection = null!;

    // =========================================================================
    // IAsyncLifetime setup / teardown 🏗️
    // =========================================================================

    public async Task InitializeAsync()
    {
        var dbName = $"webhook_test_{Guid.NewGuid():N}";
        var connStr = $"Data Source={dbName};Cache=Shared;Mode=Memory";

        // Keep a connection open so the in-memory DB persists for the test lifetime~
        _heldConnection = new SqliteConnection(connStr);
        await _heldConnection.OpenAsync();

        _provider = new SqlitePersistenceProvider(connStr);
        await _provider.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _heldConnection.DisposeAsync();
    }

    // =========================================================================
    // ✅ Test 1 — Register + GetAsync round-trip
    // =========================================================================

    /// <summary>Register a webhook and retrieve it — all fields must round-trip exactly~ 💾.</summary>
    [Fact]
    public async Task SqliteRepository_RegisterAndGet_RoundTrips()
    {
        // Arrange~
        var repo = _provider.Webhooks!;
        var reg = WebhookRegistration.Create(
            "order-placed",
            Guid.NewGuid(),
            new[] { "POST", "PUT" });

        // Act~
        var registerResult = await repo.RegisterAsync(reg);
        var retrieved = await repo.GetAsync("order-placed");

        // Assert~
        registerResult.Success.Should().BeTrue("registration should succeed on first insert~ ✅");
        retrieved.Should().NotBeNull("GetAsync should find the freshly registered webhook~");
        retrieved!.WebhookId.Should().Be("order-placed");
        retrieved.WorkflowDefinitionId.Should().Be(reg.WorkflowDefinitionId);
        retrieved.AllowedMethods.ToArray().Should().BeEquivalentTo(new[] { "POST", "PUT" },
            "allowed methods should round-trip~ 💖");
        retrieved.Enabled.Should().BeTrue("default registration is enabled~");
        retrieved.SecretKey.IsNone.Should().BeTrue("no secret was set~");
        retrieved.SignatureScheme.IsNone.Should().BeTrue("no signature scheme was set~");
    }

    // =========================================================================
    // ❌ Test 2 — Duplicate ID returns Conflict
    // =========================================================================

    /// <summary>Registering two webhooks with the same ID returns a conflict result~ ❌.</summary>
    [Fact]
    public async Task SqliteRepository_DuplicateId_ReturnsConflict()
    {
        // Arrange~
        var repo = _provider.Webhooks!;
        var reg = WebhookRegistration.Create("dupe-hook", Guid.NewGuid());

        // Act~
        await repo.RegisterAsync(reg);
        var secondResult = await repo.RegisterAsync(
            WebhookRegistration.Create("dupe-hook", Guid.NewGuid()));

        // Assert~
        secondResult.Success.Should().BeFalse("duplicate webhook IDs must be rejected~");
        secondResult.ErrorCode.Should().Be("CONFLICT",
            "error code should be CONFLICT for a duplicate ID~ 💔");
    }

    // =========================================================================
    // 🗑️ Test 3 — Delete removes entry
    // =========================================================================

    /// <summary>Deleted webhook registration should no longer be returned by GetAsync~ 🗑️.</summary>
    [Fact]
    public async Task SqliteRepository_Delete_RemovesEntry()
    {
        // Arrange~
        var repo = _provider.Webhooks!;
        var reg = WebhookRegistration.Create("to-delete", Guid.NewGuid());
        await repo.RegisterAsync(reg);

        // Act~
        var deleted = await repo.DeleteAsync("to-delete");
        var retrieved = await repo.GetAsync("to-delete");

        // Assert~
        deleted.Should().BeTrue("DeleteAsync should confirm the row was removed~ ✅");
        retrieved.Should().BeNull("GetAsync should return null after deletion~ 🌸");
    }

    // =========================================================================
    // 🔄 Test 4 — Update modifies fields
    // =========================================================================

    /// <summary>Updated webhook registration should persist the new values~ 🔄.</summary>
    [Fact]
    public async Task SqliteRepository_Update_ModifiesFields()
    {
        // Arrange~
        var repo = _provider.Webhooks!;
        var original = WebhookRegistration.Create("updatable", Guid.NewGuid());
        await repo.RegisterAsync(original);

        // Build updated registration with different methods and disabled~
        var updated = original with
        {
            AllowedMethods = LanguageExt.Arr.create(new[] { "GET", "HEAD" }),
            Enabled = false,
        };

        // Act~
        var updateResult = await repo.UpdateAsync(updated);
        var retrieved = await repo.GetAsync("updatable");

        // Assert~
        updateResult.Success.Should().BeTrue("update of existing webhook should succeed~");
        retrieved.Should().NotBeNull();
        retrieved!.AllowedMethods.ToArray().Should().BeEquivalentTo(new[] { "GET", "HEAD" },
            "allowed methods should be updated~ 🔄");
        retrieved.Enabled.Should().BeFalse("enabled flag should be updated~ 💔");
    }

    // =========================================================================
    // 📋 Test 5 — ListAll returns all entries
    // =========================================================================

    /// <summary>ListAsync should return all registered webhooks~ 📋.</summary>
    [Fact]
    public async Task SqliteRepository_ListAll_ReturnsAllRegistered()
    {
        // Arrange — register three webhooks~
        var repo = _provider.Webhooks!;
        await repo.RegisterAsync(WebhookRegistration.Create("hook-a", Guid.NewGuid()));
        await repo.RegisterAsync(WebhookRegistration.Create("hook-b", Guid.NewGuid()));
        await repo.RegisterAsync(WebhookRegistration.Create("hook-c", Guid.NewGuid()));

        // Act~
        var all = await repo.ListAsync();

        // Assert~
        all.Should().HaveCount(3, "all three webhooks should be listed~ 📋");
        all.Select(r => r.WebhookId).ToList().Should().BeEquivalentTo(
            new[] { "hook-a", "hook-b", "hook-c" },
            "all registered webhook IDs should appear in ListAsync~ 💖");
    }
}




