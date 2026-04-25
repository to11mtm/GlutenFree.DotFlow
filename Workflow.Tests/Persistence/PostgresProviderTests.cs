// <copyright file="PostgresProviderTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Persistence;

using System;
using System.Linq;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using FluentAssertions;
using LanguageExt;
using Testcontainers.PostgreSql;
using Workflow.Core.Models;
using Workflow.Persistence.Models;
using Workflow.Persistence.Postgres;
using Xunit;

/// <summary>
/// 🐘 Phase 2.1.2 — Integration tests for the PostgreSQL persistence provider~ ✨💖
/// </summary>
/// <remarks>
/// CopilotNote: Requires Docker. Each test class spins up one postgres:15-alpine container
/// via Testcontainers — shared across all tests in the class for speed.
/// Tests are marked [Trait("Category", "Integration")] so they can be skipped in CI without Docker~ 🐳
/// </remarks>
[Trait("Category", "Integration")]
public sealed class PostgresProviderTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:15-alpine")
        .WithDatabase("workflow_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private PostgresPersistenceProvider _provider = null!;

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _provider = new PostgresPersistenceProvider(_container.GetConnectionString());
        await _provider.InitializeAsync();
    }

    /// <inheritdoc/>
    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _container.DisposeAsync();
    }

    // ── Provider Lifecycle ────────────────────────────────────────────────────

    [Fact]
    public void Provider_ShouldBeInitialized_AfterInitializeAsync()
    {
        _provider.IsInitialized.Should().BeTrue("migrations should have run~ ✨");
        _provider.ProviderName.Should().Be("postgres");
    }

    [Fact]
    public async Task Provider_HealthCheck_ShouldReturnHealthy()
    {
        var result = await _provider.HealthCheckAsync();
        result.IsHealthy.Should().BeTrue("Postgres container should be reachable~ 💖");
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task Provider_HealthCheck_ShouldReturnUnhealthy_OnBadConnectionString()
    {
        var bad = new PostgresPersistenceProvider("Host=localhost;Port=19999;Database=nope;Username=nope;Password=nope;Timeout=2");
        var result = await bad.HealthCheckAsync();
        result.IsHealthy.Should().BeFalse("bad connection should be unhealthy~ 😿");
        result.ErrorMessage.Should().NotBeNullOrEmpty();
        await bad.DisposeAsync();
    }

    // ── Workflow CRUD ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Workflow_Create_ThenGetById_ShouldRoundTripAllFields()
    {
        var definition = MakeWorkflow(name: "Postgres Order Processing", tags: new[] { "orders", "finance" });
        var id = await _provider.Workflows.CreateAsync(definition);

        var retrieved = await _provider.Workflows.GetByIdAsync(id);

        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be(id);
        retrieved.Name.Should().Be("Postgres Order Processing");
        retrieved.Version.Should().Be(definition.Version);
        var tagsList = retrieved.Tags!.Value.ToArray();
        tagsList.Should().Contain("orders").And.Contain("finance");
    }

    [Fact]
    public async Task Workflow_UpdateAsync_ShouldChangeName()
    {
        var id = await _provider.Workflows.CreateAsync(MakeWorkflow("Original"));
        await _provider.Workflows.UpdateAsync(id, MakeWorkflow("Updated") with { Id = id });
        var retrieved = await _provider.Workflows.GetByIdAsync(id);
        retrieved!.Name.Should().Be("Updated");
    }

    [Fact]
    public async Task Workflow_UpdateAsync_OnNonExistentId_ShouldThrow()
    {
        var act = async () => await _provider.Workflows.UpdateAsync(Guid.NewGuid(), MakeWorkflow());
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Workflow_DeleteAsync_ShouldSoftDelete()
    {
        var id = await _provider.Workflows.CreateAsync(MakeWorkflow());
        (await _provider.Workflows.DeleteAsync(id)).Should().BeTrue();
        (await _provider.Workflows.GetByIdAsync(id)).Should().BeNull();
    }

    [Fact]
    public async Task Workflow_GetByIdAsync_WithIncludeDeleted_ShouldReturnSoftDeleted()
    {
        var id = await _provider.Workflows.CreateAsync(MakeWorkflow());
        await _provider.Workflows.DeleteAsync(id);
        (await _provider.Workflows.GetByIdAsync(id, includeDeleted: true)).Should().NotBeNull();
    }

    [Fact]
    public async Task Workflow_PurgeAsync_ShouldHardDelete()
    {
        var id = await _provider.Workflows.CreateAsync(MakeWorkflow());
        await _provider.Workflows.PurgeAsync(id);
        (await _provider.Workflows.GetByIdAsync(id, includeDeleted: true)).Should().BeNull();
    }

    [Fact]
    public async Task Workflow_RestoreAsync_ShouldBringBackSoftDeleted()
    {
        var id = await _provider.Workflows.CreateAsync(MakeWorkflow());
        await _provider.Workflows.DeleteAsync(id);
        (await _provider.Workflows.RestoreAsync(id)).Should().BeTrue();
        (await _provider.Workflows.GetByIdAsync(id)).Should().NotBeNull();
    }

    [Fact]
    public async Task Workflow_GetAllAsync_ByName_ShouldFilter()
    {
        await _provider.Workflows.CreateAsync(MakeWorkflow("Alpha Flow"));
        await _provider.Workflows.CreateAsync(MakeWorkflow("Beta Flow"));
        var result = await _provider.Workflows.GetAllAsync(
            new WorkflowFilter(NameContains: "Alpha", IsActive: true), Pagination.Default);
        result.Items.Should().OnlyContain(w => w.Name.Contains("Alpha"));
    }

    [Fact]
    public async Task Workflow_GetAllAsync_IsActiveOnly_ShouldExcludeSoftDeleted()
    {
        var id1 = await _provider.Workflows.CreateAsync(MakeWorkflow("Active PG"));
        var id2 = await _provider.Workflows.CreateAsync(MakeWorkflow("Deleted PG"));
        await _provider.Workflows.DeleteAsync(id2);

        var result = await _provider.Workflows.GetAllAsync(new WorkflowFilter(IsActive: true), Pagination.Default);
        result.Items.Select(w => w.Id).Should().Contain(id1).And.NotContain(id2);
    }

    [Fact]
    public async Task Workflow_GetAllAsync_Pagination_ShouldReturnCorrectPages()
    {
        for (var i = 1; i <= 5; i++)
        {
            await _provider.Workflows.CreateAsync(MakeWorkflow($"PG Workflow {i:D2}"));
        }

        var page1 = await _provider.Workflows.GetAllAsync(WorkflowFilter.None, new Pagination(page: 1, pageSize: 3));
        var page2 = await _provider.Workflows.GetAllAsync(WorkflowFilter.None, new Pagination(page: 2, pageSize: 3));

        page1.Items.Should().HaveCount(3);
        page2.Items.Should().HaveCountGreaterThanOrEqualTo(2);
        page1.HasNextPage.Should().BeTrue();
    }

    [Fact]
    public async Task Workflow_SearchAsync_ShouldFindByNameSubstring_CaseInsensitive()
    {
        await _provider.Workflows.CreateAsync(MakeWorkflow("PG Invoice Generator"));
        await _provider.Workflows.CreateAsync(MakeWorkflow("PG Order Processor"));

        var results = await _provider.Workflows.SearchAsync("pg invoice");
        results.Should().HaveCount(1);
        results[0].Name.Should().Be("PG Invoice Generator");
    }

    [Fact]
    public async Task Workflow_ExistsAsync_ShouldWork()
    {
        var id = await _provider.Workflows.CreateAsync(MakeWorkflow());
        (await _provider.Workflows.ExistsAsync(id)).Should().BeTrue();
        (await _provider.Workflows.ExistsAsync(Guid.NewGuid())).Should().BeFalse();
    }

    // ── Execution History ─────────────────────────────────────────────────────

    [Fact]
    public async Task Execution_Create_ThenGet_ShouldRoundTrip()
    {
        var workflowId = await _provider.Workflows.CreateAsync(MakeWorkflow());
        var record = new ExecutionRecord(
            ExecutionId: Guid.NewGuid(),
            WorkflowId: workflowId,
            State: ExecutionState.Running,
            StartedAt: DateTimeOffset.UtcNow,
            TriggeredBy: "pg-test");

        var id = await _provider.ExecutionHistory.CreateExecutionAsync(record);
        var retrieved = await _provider.ExecutionHistory.GetExecutionAsync(id);

        retrieved.Should().NotBeNull();
        retrieved!.WorkflowId.Should().Be(workflowId);
        retrieved.State.Should().Be(ExecutionState.Running);
        retrieved.TriggeredBy.Should().Be("pg-test");
    }

    [Fact]
    public async Task Execution_UpdateStatus_Pending_Running_Completed()
    {
        var workflowId = await _provider.Workflows.CreateAsync(MakeWorkflow());
        var id = await _provider.ExecutionHistory.CreateExecutionAsync(
            new ExecutionRecord(Guid.NewGuid(), workflowId, ExecutionState.Pending, DateTimeOffset.UtcNow));

        await _provider.ExecutionHistory.UpdateExecutionStatusAsync(id, ExecutionState.Running);
        await _provider.ExecutionHistory.UpdateExecutionStatusAsync(id, ExecutionState.Completed, DateTimeOffset.UtcNow);

        var retrieved = await _provider.ExecutionHistory.GetExecutionAsync(id);
        retrieved!.State.Should().Be(ExecutionState.Completed);
        retrieved.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Execution_RecordNode_ThenGetNodes_ShouldWork()
    {
        var workflowId = await _provider.Workflows.CreateAsync(MakeWorkflow());
        var execId = await _provider.ExecutionHistory.CreateExecutionAsync(
            new ExecutionRecord(Guid.NewGuid(), workflowId, ExecutionState.Running, DateTimeOffset.UtcNow));

        await _provider.ExecutionHistory.RecordNodeExecutionAsync(new NodeExecutionRecord(
            ExecutionId: execId,
            NodeId: "pg-node-1",
            State: NodeExecutionState.Completed,
            StartedAt: DateTimeOffset.UtcNow,
            CompletedAt: DateTimeOffset.UtcNow,
            Duration: TimeSpan.FromMilliseconds(99)));

        var nodes = await _provider.ExecutionHistory.GetNodeExecutionsAsync(execId);
        nodes.Should().HaveCount(1);
        nodes[0].NodeId.Should().Be("pg-node-1");
        nodes[0].Duration.TotalMilliseconds.Should().BeApproximately(99, precision: 1);
    }

    [Fact]
    public async Task Execution_FilterByState_ShouldReturnOnlyMatching()
    {
        var workflowId = await _provider.Workflows.CreateAsync(MakeWorkflow());
        var now = DateTimeOffset.UtcNow;

        await _provider.ExecutionHistory.CreateExecutionAsync(new ExecutionRecord(Guid.NewGuid(), workflowId, ExecutionState.Running, now));
        await _provider.ExecutionHistory.CreateExecutionAsync(new ExecutionRecord(Guid.NewGuid(), workflowId, ExecutionState.Completed, now));

        var result = await _provider.ExecutionHistory.GetExecutionsForWorkflowAsync(
            workflowId, new ExecutionFilter(States: new[] { ExecutionState.Completed }), Pagination.Default);

        result.Items.Should().HaveCount(1);
        result.Items[0].State.Should().Be(ExecutionState.Completed);
    }

    [Fact]
    public async Task Execution_DateRangeFilter_ShouldWork()
    {
        var workflowId = await _provider.Workflows.CreateAsync(MakeWorkflow());
        var past = DateTimeOffset.UtcNow.AddDays(-1);
        var future = DateTimeOffset.UtcNow.AddDays(1);

        await _provider.ExecutionHistory.CreateExecutionAsync(new ExecutionRecord(Guid.NewGuid(), workflowId, ExecutionState.Completed, DateTimeOffset.UtcNow));

        var result = await _provider.ExecutionHistory.GetExecutionsForWorkflowAsync(
            workflowId, new ExecutionFilter(StartedAfter: past, StartedBefore: future), Pagination.Default);

        result.Items.Should().HaveCount(1, "execution falls within date range~ 📅");
    }

    [Fact]
    public async Task Execution_Pagination_ShouldWork()
    {
        var workflowId = await _provider.Workflows.CreateAsync(MakeWorkflow());
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < 5; i++)
        {
            await _provider.ExecutionHistory.CreateExecutionAsync(
                new ExecutionRecord(Guid.NewGuid(), workflowId, ExecutionState.Completed, now.AddSeconds(i)));
        }

        var page1 = await _provider.ExecutionHistory.GetExecutionsForWorkflowAsync(
            workflowId, ExecutionFilter.None, new Pagination(page: 1, pageSize: 3));
        var page2 = await _provider.ExecutionHistory.GetExecutionsForWorkflowAsync(
            workflowId, ExecutionFilter.None, new Pagination(page: 2, pageSize: 3));

        page1.Items.Should().HaveCount(3);
        page2.Items.Should().HaveCount(2);
    }

    // ── Variable Store ────────────────────────────────────────────────────────

    [Fact]
    public async Task Variable_Set_ShouldCreateVersion1()
    {
        var scope = VariableScope.Global;
        await _provider.Variables.SetVariableAsync(scope, "pg_x", "hello");
        var entry = await _provider.Variables.GetVariableAsync(scope, "pg_x");
        entry.Should().NotBeNull();
        entry!.Version.Should().Be(1);
    }

    [Fact]
    public async Task Variable_SetTwice_ShouldCreateVersion2_AndGetLatest()
    {
        var scope = VariableScope.Global;
        await _provider.Variables.SetVariableAsync(scope, "pg_y", "v1");
        await _provider.Variables.SetVariableAsync(scope, "pg_y", "v2");
        var entry = await _provider.Variables.GetVariableAsync(scope, "pg_y");
        entry!.Version.Should().Be(2);
    }

    [Fact]
    public async Task Variable_SetNull_ShouldCreateNullValuedEntry_NotMissingEntry()
    {
        var scope = VariableScope.Global;
        await _provider.Variables.SetVariableAsync(scope, "pg_null_var", "initial");
        await _provider.Variables.SetVariableAsync(scope, "pg_null_var", null);
        var entry = await _provider.Variables.GetVariableAsync(scope, "pg_null_var");
        entry.Should().NotBeNull("null value is a valid versioned entry, not missing~ 💡");
        entry!.Value.Should().BeNull();
        entry.ValueTypeName.Should().Be("null");
    }

    [Fact]
    public async Task Variable_GetSpecificVersion_ShouldWork()
    {
        var scope = VariableScope.Global;
        await _provider.Variables.SetVariableAsync(scope, "pg_ver", "v1");
        await _provider.Variables.SetVariableAsync(scope, "pg_ver", "v2");
        var v1 = await _provider.Variables.GetVariableAsync(scope, "pg_ver", version: 1);
        v1.Should().NotBeNull();
        v1!.Version.Should().Be(1);
    }

    [Fact]
    public async Task Variable_GetHistory_ShouldReturnAllVersionsOrdered()
    {
        var scope = VariableScope.Global;
        await _provider.Variables.SetVariableAsync(scope, "pg_hist", "a");
        await _provider.Variables.SetVariableAsync(scope, "pg_hist", "b");
        await _provider.Variables.SetVariableAsync(scope, "pg_hist", null);
        var history = await _provider.Variables.GetVariableHistoryAsync(scope, "pg_hist");
        history.Should().HaveCount(3);
        history.Select(e => e.Version).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Variable_Delete_ShouldRemoveAllVersions()
    {
        var scope = VariableScope.Global;
        await _provider.Variables.SetVariableAsync(scope, "pg_del", "x");
        (await _provider.Variables.DeleteVariableAsync(scope, "pg_del")).Should().BeTrue();
        (await _provider.Variables.GetVariableAsync(scope, "pg_del")).Should().BeNull();
    }

    [Fact]
    public async Task Variable_GetAll_ShouldIncludeNullValuedVariable()
    {
        var scope = VariableScope.ForWorkflow(Guid.NewGuid());
        await _provider.Variables.SetVariableAsync(scope, "a", 42);
        await _provider.Variables.SetVariableAsync(scope, "b", null);
        var all = await _provider.Variables.GetAllVariablesAsync(scope);
        all.Should().ContainKey("a").And.ContainKey("b");
        all["b"].Should().BeNull();
    }

    [Fact]
    public async Task Variable_Scopes_ShouldBeIsolated()
    {
        var global = VariableScope.Global;
        var workflow = VariableScope.ForWorkflow(Guid.NewGuid());
        var execution = VariableScope.ForExecution(Guid.NewGuid());

        await _provider.Variables.SetVariableAsync(global, "scope_x", "global-val");
        await _provider.Variables.SetVariableAsync(workflow, "scope_x", "workflow-val");
        await _provider.Variables.SetVariableAsync(execution, "scope_x", "execution-val");

        var g = await _provider.Variables.GetVariableAsync(global, "scope_x");
        var w = await _provider.Variables.GetVariableAsync(workflow, "scope_x");
        var e = await _provider.Variables.GetVariableAsync(execution, "scope_x");

        g!.Version.Should().Be(1, "global scope is independent~ 🌍");
        w!.Version.Should().Be(1, "workflow scope is independent~ 📋");
        e!.Version.Should().Be(1, "execution scope is independent~ ⚡");
    }

    [Fact]
    public async Task Variable_ConcurrentWrites_ShouldNotCorruptVersions()
    {
        var scope = VariableScope.ForExecution(Guid.NewGuid());

        // CopilotNote: 5 parallel SetVariableAsync calls — versions must be 1..5 with no duplicates~ 🔒
        await Task.WhenAll(Enumerable.Range(0, 5).Select(i =>
            _provider.Variables.SetVariableAsync(scope, "concurrent", i)));

        var history = await _provider.Variables.GetVariableHistoryAsync(scope, "concurrent");
        history.Should().HaveCount(5, "all 5 writes should succeed~ 💪");
        history.Select(e => e.Version).Should().OnlyHaveUniqueItems("no duplicate versions~ 🔒");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static WorkflowDefinition MakeWorkflow(
        string name = "PG Test Workflow",
        string[]? tags = null)
    {
        var arr = tags is not null ? new Arr<string>(tags) : (Arr<string>?)null;

        return new WorkflowDefinition(
            Id: Guid.NewGuid(),
            Name: name,
            Description: $"Postgres description for {name}",
            Version: new Version(1, 0, 0),
            Nodes: Arr<NodeDefinition>.Empty,
            Connections: Arr<ConnectionDefinition>.Empty,
            Variables: HashMap<string, VariableDefinition>.Empty,
            Tags: arr);
    }
}

