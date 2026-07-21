// <copyright file="SqliteProviderTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

using Microsoft.Data.Sqlite;

namespace Workflow.Tests.Persistence;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using LanguageExt;
using Workflow.Core.Models;
using Workflow.Persistence.Models;
using Workflow.Persistence.Sqlite;
using Xunit;

/// <summary>
/// 🪶 Phase 2.1.1 — Integration tests for the SQLite persistence provider~ ✨💖
/// </summary>
/// <remarks>
/// CopilotNote: All tests use ":memory:" databases — no files, no Docker, no cleanup needed!
/// Each test gets a unique DB name to ensure isolation~ 🧪
/// </remarks>
public sealed class SqliteProviderTests : IAsyncLifetime
{
    private SqlitePersistenceProvider _provider = null!;
    private string _connectionString;
    private SqliteConnection _heldConnection;

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        // CopilotNote: Use a unique name per test class run to avoid cross-test contamination~ 🔒
        // var dbName = $"c:\\temp\\test_{Guid.NewGuid():N}";
        // var csb = new SqliteConnectionStringBuilder() { DataSource = dbName, Cache = SqliteCacheMode.Shared };//, Mode = SqliteOpenMode.Memory};
        var dbName = $"test_{Guid.NewGuid():N}";
        var csb = new SqliteConnectionStringBuilder() { DataSource = dbName, Cache = SqliteCacheMode.Shared, Mode = SqliteOpenMode.Memory};

        //_connectionString = csb.ToString();
        _connectionString = $"Filename=file:memdb-{dbName}.db;Mode=Memory;Cache=Shared";
        _heldConnection = new SqliteConnection(_connectionString);
		_heldConnection.Open();
        _provider = new SqlitePersistenceProvider(_connectionString);
        await _provider.InitializeAsync();
    }

    /// <inheritdoc/>
    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _heldConnection.DisposeAsync();
    }

    // ── Provider Lifecycle ────────────────────────────────────────────────────

    [Fact]
    public void Provider_ShouldBeInitialized_AfterInitializeAsync()
    {
        _provider.IsInitialized.Should().BeTrue("migrations should have run~ ✨");
        _provider.ProviderName.Should().Be("sqlite");
    }

    [Fact]
    public async Task Provider_HealthCheck_ShouldReturnHealthy()
    {
        var result = await _provider.HealthCheckAsync();
        result.IsHealthy.Should().BeTrue("SQLite in-memory should always be accessible~ 💖");
        result.ErrorMessage.Should().BeNull();
    }

    // ── Workflow CRUD ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Workflow_Create_ThenGetById_ShouldRoundTripAllFields()
    {
        var definition = MakeWorkflow(name: "Order Processing", tags: new[] { "orders", "finance" });
        var id = await _provider.Workflows.CreateAsync(definition);

        var retrieved = await _provider.Workflows.GetByIdAsync(id);

        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be(id);
        retrieved.Name.Should().Be("Order Processing");
        retrieved.Description.Should().Be(definition.Description);
        retrieved.Version.Should().Be(definition.Version);
        retrieved.Tags.Should().NotBeNull();
        var tagsList = retrieved.Tags!.Value.ToArray();
        tagsList.Should().Contain("orders").And.Contain("finance");
    }

    [Fact]
    public async Task Workflow_UpdateAsync_ShouldChangeName()
    {
        var id = await _provider.Workflows.CreateAsync(MakeWorkflow("Original Name"));

        var updated = MakeWorkflow("Updated Name") with { Id = id };
        await _provider.Workflows.UpdateAsync(id, updated);

        var retrieved = await _provider.Workflows.GetByIdAsync(id);
        retrieved!.Name.Should().Be("Updated Name");
    }

    [Fact]
    public async Task Workflow_UpdateAsync_OnNonExistentId_ShouldThrow()
    {
        var act = async () => await _provider.Workflows.UpdateAsync(Guid.NewGuid(), MakeWorkflow());
        await act.Should().ThrowAsync<InvalidOperationException>("workflow doesn't exist~ 😿");
    }

    [Fact]
    public async Task Workflow_DeleteAsync_ShouldSoftDelete()
    {
        var id = await _provider.Workflows.CreateAsync(MakeWorkflow());
        var deleted = await _provider.Workflows.DeleteAsync(id);

        deleted.Should().BeTrue();
        var retrieved = await _provider.Workflows.GetByIdAsync(id);
        retrieved.Should().BeNull("soft-deleted workflow should not be returned by default~ 🗑️");
    }

    [Fact]
    public async Task Workflow_GetByIdAsync_WithIncludeDeleted_ShouldReturnSoftDeleted()
    {
        var id = await _provider.Workflows.CreateAsync(MakeWorkflow());
        await _provider.Workflows.DeleteAsync(id);

        var retrieved = await _provider.Workflows.GetByIdAsync(id, includeDeleted: true);
        retrieved.Should().NotBeNull("includeDeleted:true should return soft-deleted~ ♻️");
    }

    [Fact]
    public async Task Workflow_PurgeAsync_ShouldHardDelete()
    {
        var id = await _provider.Workflows.CreateAsync(MakeWorkflow());
        await _provider.Workflows.PurgeAsync(id);

        var retrieved = await _provider.Workflows.GetByIdAsync(id, includeDeleted: true);
        retrieved.Should().BeNull("purged workflow should be gone completely~ ⚠️");
    }

    [Fact]
    public async Task Workflow_RestoreAsync_ShouldBringBackSoftDeleted()
    {
        var id = await _provider.Workflows.CreateAsync(MakeWorkflow());
        await _provider.Workflows.DeleteAsync(id);
        var restored = await _provider.Workflows.RestoreAsync(id);

        restored.Should().BeTrue();
        var retrieved = await _provider.Workflows.GetByIdAsync(id);
        retrieved.Should().NotBeNull("restored workflow should be visible again~ 🌸");
    }

    [Fact]
    public async Task Workflow_GetAllAsync_IsActiveTrue_ShouldExcludeSoftDeleted()
    {
        var id1 = await _provider.Workflows.CreateAsync(MakeWorkflow("Active 1"));
        var id2 = await _provider.Workflows.CreateAsync(MakeWorkflow("Active 2"));
        var id3 = await _provider.Workflows.CreateAsync(MakeWorkflow("ToDelete"));
        await _provider.Workflows.DeleteAsync(id3);

        var result = await _provider.Workflows.GetAllAsync(
            new WorkflowFilter(IsActive: true),
            Pagination.Default);

        result.Items.Select(w => w.Id).Should().Contain(id1).And.Contain(id2)
            .And.NotContain(id3, "soft-deleted should be excluded~ 🗑️");
    }

    [Fact]
    public async Task Workflow_SearchAsync_ShouldFindByNameSubstring_CaseInsensitive()
    {
        await _provider.Workflows.CreateAsync(MakeWorkflow("Invoice Generator"));
        await _provider.Workflows.CreateAsync(MakeWorkflow("Order Processor"));
        await _provider.Workflows.CreateAsync(MakeWorkflow("Email Sender"));

        var results = await _provider.Workflows.SearchAsync("invoice");

        results.Should().HaveCount(1);
        results[0].Name.Should().Be("Invoice Generator");
    }

    [Fact]
    public async Task Workflow_GetAllAsync_Pagination_ShouldReturnCorrectPages()
    {
        for (var i = 1; i <= 5; i++)
        {
            await _provider.Workflows.CreateAsync(MakeWorkflow($"Workflow {i:D2}"));
        }

        var page1 = await _provider.Workflows.GetAllAsync(
            WorkflowFilter.None,
            new Pagination(page: 1, pageSize: 3));

        var page2 = await _provider.Workflows.GetAllAsync(
            WorkflowFilter.None,
            new Pagination(page: 2, pageSize: 3));

        page1.Items.Should().HaveCount(3, "page 1 should have 3 items~ 📄");
        page2.Items.Should().HaveCount(2, "page 2 should have the remaining 2~ 📄");
        page1.TotalCount.Should().Be(5);
        page1.HasNextPage.Should().BeTrue();
        page2.HasNextPage.Should().BeFalse();
    }

    [Fact]
    public async Task Workflow_ExistsAsync_ShouldReturnTrueForActive()
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
            TriggeredBy: "test-runner");

        var id = await _provider.ExecutionHistory.CreateExecutionAsync(record);
        var retrieved = await _provider.ExecutionHistory.GetExecutionAsync(id);

        retrieved.Should().NotBeNull();
        retrieved!.WorkflowId.Should().Be(workflowId);
        retrieved.State.Should().Be(ExecutionState.Running);
        retrieved.TriggeredBy.Should().Be("test-runner");
    }

    [Fact]
    public async Task Execution_UpdateStatus_Running_To_Completed_ShouldSetCompletedAt()
    {
        var workflowId = await _provider.Workflows.CreateAsync(MakeWorkflow());
        var id = await _provider.ExecutionHistory.CreateExecutionAsync(
            new ExecutionRecord(Guid.NewGuid(), workflowId, ExecutionState.Running, DateTimeOffset.UtcNow));

        var completedAt = DateTimeOffset.UtcNow;
        await _provider.ExecutionHistory.UpdateExecutionStatusAsync(
            id, ExecutionState.Completed, completedAt);

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

        var nodeRecord = new NodeExecutionRecord(
            ExecutionId: execId,
            NodeId: "node-1",
            State: NodeExecutionState.Completed,
            StartedAt: DateTimeOffset.UtcNow,
            CompletedAt: DateTimeOffset.UtcNow,
            Duration: TimeSpan.FromMilliseconds(42));

        await _provider.ExecutionHistory.RecordNodeExecutionAsync(nodeRecord);
        var nodes = await _provider.ExecutionHistory.GetNodeExecutionsAsync(execId);

        nodes.Should().HaveCount(1);
        nodes[0].NodeId.Should().Be("node-1");
        nodes[0].Duration.TotalMilliseconds.Should().BeApproximately(42, precision: 1);
    }

    [Fact]
    public async Task Execution_FilterByState_ShouldReturnOnlyMatchingExecutions()
    {
        var workflowId = await _provider.Workflows.CreateAsync(MakeWorkflow());
        var now = DateTimeOffset.UtcNow;

        await _provider.ExecutionHistory.CreateExecutionAsync(
            new ExecutionRecord(Guid.NewGuid(), workflowId, ExecutionState.Running, now));
        await _provider.ExecutionHistory.CreateExecutionAsync(
            new ExecutionRecord(Guid.NewGuid(), workflowId, ExecutionState.Completed, now));
        await _provider.ExecutionHistory.CreateExecutionAsync(
            new ExecutionRecord(Guid.NewGuid(), workflowId, ExecutionState.Failed, now));

        var result = await _provider.ExecutionHistory.GetExecutionsForWorkflowAsync(
            workflowId,
            new ExecutionFilter(States: new[] { ExecutionState.Completed }),
            Pagination.Default);

        result.Items.Should().HaveCount(1);
        result.Items[0].State.Should().Be(ExecutionState.Completed);
    }

    // ── Variable Store ────────────────────────────────────────────────────────

    [Fact]
    public async Task Variable_Set_ShouldCreateVersion1()
    {
        var scope = VariableScope.Global;
        await _provider.Variables.SetVariableAsync(scope, "x", "hello");

        var entry = await _provider.Variables.GetVariableAsync(scope, "x");
        entry.Should().NotBeNull();
        entry!.Version.Should().Be(1);
        entry.Value.Should().BeOfType<System.Text.Json.JsonElement>()
            .Which.GetString().Should().Be("hello");
    }

    [Fact]
    public async Task Variable_SetTwice_ShouldCreateVersion2_AndGetLatest()
    {
        var scope = VariableScope.Global;
        await _provider.Variables.SetVariableAsync(scope, "x", "hello");
        await _provider.Variables.SetVariableAsync(scope, "x", "world");

        var entry = await _provider.Variables.GetVariableAsync(scope, "x");
        entry!.Version.Should().Be(2);
        entry.Value.Should().BeOfType<System.Text.Json.JsonElement>()
            .Which.GetString().Should().Be("world");
    }

    [Fact]
    public async Task Variable_SetNull_ShouldCreateNullValuedEntry_NotMissingEntry()
    {
        var scope = VariableScope.Global;
        await _provider.Variables.SetVariableAsync(scope, "x", "initial");
        await _provider.Variables.SetVariableAsync(scope, "x", null);

        var entry = await _provider.Variables.GetVariableAsync(scope, "x");
        // Must return a VariableEntry (not C# null) — the variable exists but its value is null~ 💡
        entry.Should().NotBeNull("variable exists with null value — distinct from not-found!");
        entry!.Version.Should().Be(2);
        entry.Value.Should().BeNull();
        entry.ValueTypeName.Should().Be("null");
    }

    [Fact]
    public async Task Variable_GetSpecificVersion_ShouldReturnCorrectValue()
    {
        var scope = VariableScope.Global;
        await _provider.Variables.SetVariableAsync(scope, "x", "v1");
        await _provider.Variables.SetVariableAsync(scope, "x", "v2");

        var v1Entry = await _provider.Variables.GetVariableAsync(scope, "x", version: 1);
        v1Entry.Should().NotBeNull();
        v1Entry!.Value.Should().BeOfType<System.Text.Json.JsonElement>()
            .Which.GetString().Should().Be("v1");
    }

    [Fact]
    public async Task Variable_GetHistory_ShouldReturnAllVersionsOrdered()
    {
        var scope = VariableScope.Global;
        await _provider.Variables.SetVariableAsync(scope, "x", "a");
        await _provider.Variables.SetVariableAsync(scope, "x", "b");
        await _provider.Variables.SetVariableAsync(scope, "x", null);

        var history = await _provider.Variables.GetVariableHistoryAsync(scope, "x");
        history.Should().HaveCount(3);
        history[0].Version.Should().Be(1);
        history[1].Version.Should().Be(2);
        history[2].Version.Should().Be(3);
        history[2].Value.Should().BeNull("null is a valid versioned entry~ 💡");
    }

    [Fact]
    public async Task Variable_Delete_ShouldRemoveAllVersions()
    {
        var scope = VariableScope.Global;
        await _provider.Variables.SetVariableAsync(scope, "x", "hello");
        await _provider.Variables.SetVariableAsync(scope, "x", "world");

        var deleted = await _provider.Variables.DeleteVariableAsync(scope, "x");
        deleted.Should().BeTrue();

        var entry = await _provider.Variables.GetVariableAsync(scope, "x");
        entry.Should().BeNull("all versions were deleted~ 🗑️");
    }

    [Fact]
    public async Task Variable_GetAllVariablesAsync_ShouldIncludeNullValuedVariable()
    {
        var scope = VariableScope.Global;
        await _provider.Variables.SetVariableAsync(scope, "a", 42);
        await _provider.Variables.SetVariableAsync(scope, "b", null);

        var all = await _provider.Variables.GetAllVariablesAsync(scope);
        all.Should().ContainKey("a").And.ContainKey("b");
        all["b"].Should().BeNull("null-valued variable is included~ 💖");
    }

    [Fact]
    public async Task Variable_Scopes_ShouldBeIsolated()
    {
        var global = VariableScope.Global;
        var workflow = VariableScope.ForWorkflow(Guid.NewGuid());
        var execution = VariableScope.ForExecution(Guid.NewGuid());

        await _provider.Variables.SetVariableAsync(global, "x", "global-val");
        await _provider.Variables.SetVariableAsync(workflow, "x", "workflow-val");
        await _provider.Variables.SetVariableAsync(execution, "x", "execution-val");

        var g = await _provider.Variables.GetVariableAsync(global, "x");
        var w = await _provider.Variables.GetVariableAsync(workflow, "x");
        var e = await _provider.Variables.GetVariableAsync(execution, "x");

        g!.Value.Should().BeOfType<System.Text.Json.JsonElement>()
            .Which.GetString().Should().Be("global-val");
        w!.Value.Should().BeOfType<System.Text.Json.JsonElement>()
            .Which.GetString().Should().Be("workflow-val");
        e!.Value.Should().BeOfType<System.Text.Json.JsonElement>()
            .Which.GetString().Should().Be("execution-val");
    }

    // ── Parallel Metadata Round-trip ──────────────────────────────────────────

    /// <summary>
    /// 🌐 Verifies that <c>Metadata["parallelId"]</c>, <c>Metadata["branchIndex"]</c>, and
    /// <c>Metadata["subGraphId"]</c> survive a full SQLite write → read round-trip.
    /// These are the keys stamped by <see cref="Workflow.Engine.Actors.SubGraphExecutor.QueuePersistNode"/>
    /// when sentinel inputs <c>__parallel_node_id__</c> and <c>__parallel_branch_index__</c> are present~
    /// 🌐💖 Phase 2.2.3-followup: Persistence metadata stamps for parallel branches.
    /// </summary>
    [Fact]
    public async Task NodeExecution_WithParallelMetadata_ShouldRoundTripAllMetadataFields()
    {
        // Arrange — prerequisite execution record
        var workflowId = await _provider.Workflows.CreateAsync(MakeWorkflow());
        var execId = await _provider.ExecutionHistory.CreateExecutionAsync(
            new ExecutionRecord(Guid.NewGuid(), workflowId, ExecutionState.Running, DateTimeOffset.UtcNow));

        const string ParallelNodeId = "par-node-1";
        const int BranchIndex = 2;
        var subGraphId = $"{ParallelNodeId}-branch-{BranchIndex}";

        // CopilotNote: this metadata dict mirrors exactly what SubGraphExecutor.QueuePersistNode produces
        // when __parallel_node_id__ and __parallel_branch_index__ sentinel inputs are present.
        // subGraphId goes into the dedicated sub_graph_id column; parallelId + branchIndex go into
        // the JSON metadata blob — both must survive round-trip~ 🗂️
        var metadata = new Dictionary<string, object?>
        {
            ["subGraphId"] = subGraphId,
            ["parallelId"] = ParallelNodeId,
            ["branchIndex"] = BranchIndex,
        };

        var nodeRecord = new NodeExecutionRecord(
            ExecutionId: execId,
            NodeId: "branch-body-node",
            State: NodeExecutionState.Completed,
            StartedAt: DateTimeOffset.UtcNow,
            CompletedAt: DateTimeOffset.UtcNow,
            Duration: TimeSpan.FromMilliseconds(75),
            Metadata: metadata);

        // Act
        await _provider.ExecutionHistory.RecordNodeExecutionAsync(nodeRecord);
        var nodes = await _provider.ExecutionHistory.GetNodeExecutionsAsync(execId);

        // Assert
        nodes.Should().HaveCount(1);
        var retrieved = nodes[0];

        retrieved.Metadata.Should().NotBeNull(
            because: "parallel metadata must survive the SQLite write → read round-trip~ 🌐");

        retrieved.Metadata!.Should().ContainKey("parallelId",
            because: "Metadata[\"parallelId\"] is stamped by SubGraphExecutor.QueuePersistNode when "
                   + "the __parallel_node_id__ sentinel input is present~ 🌐");
        retrieved.Metadata["parallelId"]?.ToString().Should().Be(ParallelNodeId,
            because: "parallelId must equal the parallel node ID for history correlation~ 💖");

        retrieved.Metadata.Should().ContainKey("branchIndex",
            because: "Metadata[\"branchIndex\"] is stamped by SubGraphExecutor.QueuePersistNode when "
                   + "__parallel_branch_index__ sentinel input is present~ 🔢");
        // CopilotNote: after round-tripping through the JSON metadata blob, numeric values
        // come back as JsonElement — use GetInt32() for robust extraction~ 🔢
        var branchIndexRaw = retrieved.Metadata!["branchIndex"];
        var branchIndexRestored = branchIndexRaw is System.Text.Json.JsonElement je
            ? je.GetInt32()
            : Convert.ToInt32(branchIndexRaw, System.Globalization.CultureInfo.InvariantCulture);
        branchIndexRestored.Should().Be(BranchIndex,
            because: "branchIndex must be the 0-based branch index~ 🔢");

        retrieved.Metadata.Should().ContainKey("subGraphId",
            because: "subGraphId is stored in its own dedicated column but must also be restored "
                   + "into Metadata by BuildMetadata so consumers see a unified dict~ 🌿");
        retrieved.Metadata["subGraphId"]!.ToString().Should().Be(subGraphId,
            because: "subGraphId must equal the coordinator-assigned sub-graph ID~ 🌿");
    }

    /// <summary>
    /// 🛡️ Regression guard — a plain node record with no parallel metadata must not
    /// gain any phantom keys after a round-trip through SQLite~ 💖
    /// Phase 2.2.3-followup: ensures the metadata serialization path is additive-only.
    /// </summary>
    [Fact]
    public async Task NodeExecution_WithNoMetadata_ShouldRoundTripWithNullMetadata()
    {
        // Arrange
        var workflowId = await _provider.Workflows.CreateAsync(MakeWorkflow());
        var execId = await _provider.ExecutionHistory.CreateExecutionAsync(
            new ExecutionRecord(Guid.NewGuid(), workflowId, ExecutionState.Running, DateTimeOffset.UtcNow));

        var nodeRecord = new NodeExecutionRecord(
            ExecutionId: execId,
            NodeId: "plain-node",
            State: NodeExecutionState.Completed,
            StartedAt: DateTimeOffset.UtcNow,
            CompletedAt: DateTimeOffset.UtcNow,
            Duration: TimeSpan.FromMilliseconds(10));

        // Act
        await _provider.ExecutionHistory.RecordNodeExecutionAsync(nodeRecord);
        var nodes = await _provider.ExecutionHistory.GetNodeExecutionsAsync(execId);

        // Assert — no metadata keys should appear when none were written~ 🛡️
        nodes.Should().HaveCount(1);
        var retrieved = nodes[0];
        retrieved.Metadata.Should().BeNull(
            because: "a record written without metadata must not gain phantom keys on read~ 🛡️");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static WorkflowDefinition MakeWorkflow(
        string name = "Test Workflow",
        string[]? tags = null)
    {
        var arr = tags is not null
            ? new Arr<string>(tags)
            : (Arr<string>?)null;

        return new WorkflowDefinition(
            Id: Guid.NewGuid(),
            Name: name,
            Description: $"Description for {name}",
            Version: new Version(1, 0, 0),
            Nodes: Arr<NodeDefinition>.Empty,
            Connections: Arr<ConnectionDefinition>.Empty,
            Variables: HashMap<string, VariableDefinition>.Empty,
            Tags: arr);
    }
}



