// <copyright file="NatsProviderTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Integration.Persistence;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using LanguageExt;
using Testcontainers.Nats;
using Workflow.Core.Models;
using Workflow.Persistence.Models;
using Workflow.Persistence.Nats;
using Xunit;

/// <summary>
/// ?? Phase 2.1.3 � Integration tests for the NATS KV persistence provider~ ???
/// </summary>
/// <remarks>
/// CopilotNote: Requires Docker. Spins up one NATS container (with JetStream enabled)
/// shared across all tests in the class for speed.
/// Tests are marked [Trait("Category", "Integration")] to allow skipping in CI without Docker~ ??
/// </remarks>
[Trait("Category", "Integration")]
public sealed class NatsProviderTests : IAsyncLifetime
{
    private readonly NatsContainer _container = new NatsBuilder()
        .WithImage("nats:2.10-alpine")
        .WithCommand("-js") // CopilotNote: -js flag enables JetStream (required for KV)~ ??
        .Build();

    private NatsPersistenceProvider _provider = null!;

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _provider = new NatsPersistenceProvider(_container.GetConnectionString());
        await _provider.InitializeAsync();
    }

    /// <inheritdoc/>
    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _container.DisposeAsync();
    }

    // -- Provider Lifecycle ----------------------------------------------------

    [Fact]
    public void Provider_ShouldBeInitialized_AfterInitializeAsync()
    {
        _provider.IsInitialized.Should().BeTrue();
    }

    [Fact]
    public void Provider_ShouldHaveCorrectName()
    {
        _provider.ProviderName.Should().Be("nats");
    }

    [Fact]
    public async Task Provider_HealthCheck_ShouldBeHealthy()
    {
        var result = await _provider.HealthCheckAsync();
        result.IsHealthy.Should().BeTrue();
        result.ProviderName.Should().Be("nats");
    }

    [Fact]
    public async Task Provider_HealthCheck_ShouldBeUnhealthy_OnBadUrl()
    {
        await using var bad = new NatsPersistenceProvider("nats://localhost:19999");
        var result = await bad.HealthCheckAsync(new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(3)).Token);
        result.IsHealthy.Should().BeFalse();
    }

    [Fact]
    public void Provider_Blobs_ShouldBeWired()
    {
        // CopilotNote: Blobs live in the JetStream *Object Store*, not KV — KV would be the wrong
        // tool for binaries, but the Object Store chunks them properly~
        _provider.Blobs.Should().NotBeNull();
    }

    [Fact]
    public async Task Blobs_PutGet_ShouldRoundTrip()
    {
        var key = $"nats-blob-{Guid.NewGuid():N}.txt";
        var payload = "hello from the object store~"u8.ToArray();

        var etag = await _provider.Blobs!.PutAsync(key, new MemoryStream(payload), "text/plain");
        etag.Should().NotBeNullOrEmpty("the object digest doubles as an ETag~");

        await using var stream = await _provider.Blobs.GetAsync(key);
        stream.Should().NotBeNull();
        using var ms = new MemoryStream();
        await stream!.CopyToAsync(ms);
        ms.ToArray().Should().Equal(payload);
    }

    [Fact]
    public async Task Blobs_Get_ShouldReturnNull_WhenMissing()
    {
        var stream = await _provider.Blobs!.GetAsync($"absent-{Guid.NewGuid():N}");
        stream.Should().BeNull("a missing key is not an exceptional condition~");
    }

    [Fact]
    public async Task Blobs_Delete_ShouldReportWhetherItExisted()
    {
        var key = $"nats-blob-{Guid.NewGuid():N}";
        await _provider.Blobs!.PutAsync(key, new MemoryStream("data"u8.ToArray()));

        (await _provider.Blobs.ExistsAsync(key)).Should().BeTrue();
        (await _provider.Blobs.DeleteAsync(key)).Should().BeTrue();
        (await _provider.Blobs.ExistsAsync(key)).Should().BeFalse();
        (await _provider.Blobs.DeleteAsync($"absent-{Guid.NewGuid():N}")).Should().BeFalse();
    }

    [Fact]
    public async Task Blobs_List_ShouldFilterByPrefix()
    {
        var prefix = $"listing/{Guid.NewGuid():N}/";
        await _provider.Blobs!.PutAsync(prefix + "a", new MemoryStream("aa"u8.ToArray()));
        await _provider.Blobs.PutAsync(prefix + "b", new MemoryStream("bbb"u8.ToArray()));
        await _provider.Blobs.PutAsync($"other/{Guid.NewGuid():N}", new MemoryStream("zz"u8.ToArray()));

        var found = new List<string>();
        await foreach (var info in _provider.Blobs.ListAsync(prefix))
        {
            found.Add(info.Key);
            info.SizeBytes.Should().BeGreaterThan(0);
        }

        found.Should().BeEquivalentTo([prefix + "a", prefix + "b"]);
    }

    [Fact]
    public async Task Blobs_List_ShouldNotIncludeDeletedObjects()
    {
        var prefix = $"tombstones/{Guid.NewGuid():N}/";
        await _provider.Blobs!.PutAsync(prefix + "gone", new MemoryStream("x"u8.ToArray()));
        await _provider.Blobs.DeleteAsync(prefix + "gone");

        var found = new List<string>();
        await foreach (var info in _provider.Blobs.ListAsync(prefix))
        {
            found.Add(info.Key);
        }

        // CopilotNote: the Object Store keeps a tombstone entry after delete — a sweep that
        // "reclaimed" those would loop forever trying to delete what's already gone~
        found.Should().BeEmpty();
    }

    [Fact]
    public async Task Blobs_PresignedUrl_ShouldThrow()
    {
        var act = () => _provider.Blobs!.GeneratePresignedUrlAsync("any", TimeSpan.FromMinutes(1));
        await act.Should().ThrowAsync<NotSupportedException>();
    }

    // -- Workflow CRUD ---------------------------------------------------------

    [Fact]
    public async Task Workflow_CreateAndGet_ShouldRoundTrip()
    {
        var definition = MakeWorkflow("RoundTrip Test");
        var id = await _provider.Workflows.CreateAsync(definition);

        var retrieved = await _provider.Workflows.GetByIdAsync(id);

        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be("RoundTrip Test");
        retrieved.Id.Should().Be(id);
    }

    [Fact]
    public async Task Workflow_Update_ShouldChangeDefinition()
    {
        var id = await _provider.Workflows.CreateAsync(MakeWorkflow("Original"));

        var updated = MakeWorkflow("Updated") with { Id = id };
        await _provider.Workflows.UpdateAsync(id, updated);

        var retrieved = await _provider.Workflows.GetByIdAsync(id);
        retrieved!.Name.Should().Be("Updated");
    }

    [Fact]
    public async Task Workflow_Delete_ShouldSoftDelete()
    {
        var id = await _provider.Workflows.CreateAsync(MakeWorkflow("ToDelete"));

        var deleted = await _provider.Workflows.DeleteAsync(id);

        deleted.Should().BeTrue();
        var retrieved = await _provider.Workflows.GetByIdAsync(id);
        retrieved.Should().BeNull("soft-deleted workflows should not appear in normal Get~ ???");
    }

    [Fact]
    public async Task Workflow_GetById_IncludeDeleted_ShouldReturnSoftDeleted()
    {
        var id = await _provider.Workflows.CreateAsync(MakeWorkflow("SoftDeletedGet"));
        await _provider.Workflows.DeleteAsync(id);

        var retrieved = await _provider.Workflows.GetByIdAsync(id, includeDeleted: true);
        retrieved.Should().NotBeNull("includeDeleted: true should return soft-deleted workflow~ ??");
    }

    [Fact]
    public async Task Workflow_Purge_ShouldHardDelete()
    {
        var id = await _provider.Workflows.CreateAsync(MakeWorkflow("ToPurge"));
        await _provider.Workflows.PurgeAsync(id);

        var retrieved = await _provider.Workflows.GetByIdAsync(id, includeDeleted: true);
        retrieved.Should().BeNull("purged workflows should be completely gone~ ??");
    }

    [Fact]
    public async Task Workflow_Restore_ShouldBringBackSoftDeleted()
    {
        var id = await _provider.Workflows.CreateAsync(MakeWorkflow("ToRestore"));
        await _provider.Workflows.DeleteAsync(id);

        var restored = await _provider.Workflows.RestoreAsync(id);

        restored.Should().BeTrue();
        var retrieved = await _provider.Workflows.GetByIdAsync(id);
        retrieved.Should().NotBeNull("restored workflow should be active again~ ??");
    }

    [Fact]
    public async Task Workflow_Exists_ShouldReturnTrue_WhenActive()
    {
        var id = await _provider.Workflows.CreateAsync(MakeWorkflow("ExistsTest"));
        (await _provider.Workflows.ExistsAsync(id)).Should().BeTrue();
    }

    [Fact]
    public async Task Workflow_Exists_ShouldReturnFalse_WhenNotFound()
    {
        (await _provider.Workflows.ExistsAsync(Guid.NewGuid())).Should().BeFalse();
    }

    [Fact]
    public async Task Workflow_Search_ShouldReturnMatchingByName()
    {
        await _provider.Workflows.CreateAsync(MakeWorkflow("SearchableWorkflow"));
        await _provider.Workflows.CreateAsync(MakeWorkflow("OtherWorkflow"));

        var results = await _provider.Workflows.SearchAsync("Searchable");

        results.Should().Contain(w => w.Name == "SearchableWorkflow");
        results.Should().NotContain(w => w.Name == "OtherWorkflow");
    }

    [Fact]
    public async Task Workflow_GetAll_ShouldFilterByIsActive()
    {
        var id1 = await _provider.Workflows.CreateAsync(MakeWorkflow("Active1_GetAll"));
        var id2 = await _provider.Workflows.CreateAsync(MakeWorkflow("Inactive1_GetAll"));
        await _provider.Workflows.DeleteAsync(id2);

        var result = await _provider.Workflows.GetAllAsync(
            new WorkflowFilter(IsActive: true),
            Pagination.Default);

        result.Items.Should().Contain(w => w.Id == id1);
        result.Items.Should().NotContain(w => w.Id == id2);
    }

    [Fact]
    public async Task Workflow_OptimisticConcurrency_UpdateWithStaleDocument_ShouldSucceed()
    {
        // CopilotNote: NATS KV is last-write-wins, not optimistic concurrency by default.
        // This test validates that two sequential updates don't corrupt data~ ??
        var id = await _provider.Workflows.CreateAsync(MakeWorkflow("Concurrency"));
        await _provider.Workflows.UpdateAsync(id, MakeWorkflow("Updated-1") with { Id = id });
        await _provider.Workflows.UpdateAsync(id, MakeWorkflow("Updated-2") with { Id = id });

        var final = await _provider.Workflows.GetByIdAsync(id);
        final!.Name.Should().Be("Updated-2");
    }

    // -- Execution History -----------------------------------------------------

    [Fact]
    public async Task Execution_CreateAndGet_ShouldRoundTrip()
    {
        var workflowId = Guid.NewGuid();
        var record = new ExecutionRecord(
            ExecutionId: Guid.NewGuid(),
            WorkflowId: workflowId,
            State: ExecutionState.Pending,
            StartedAt: DateTimeOffset.UtcNow);

        var id = await _provider.ExecutionHistory.CreateExecutionAsync(record);
        var retrieved = await _provider.ExecutionHistory.GetExecutionAsync(id);

        retrieved.Should().NotBeNull();
        retrieved!.ExecutionId.Should().Be(id);
        retrieved.WorkflowId.Should().Be(workflowId);
        retrieved.State.Should().Be(ExecutionState.Pending);
    }

    [Fact]
    public async Task Execution_UpdateStatus_ShouldTransitionState()
    {
        var record = new ExecutionRecord(Guid.NewGuid(), Guid.NewGuid(), ExecutionState.Pending, DateTimeOffset.UtcNow);
        var id = await _provider.ExecutionHistory.CreateExecutionAsync(record);

        await _provider.ExecutionHistory.UpdateExecutionStatusAsync(id, ExecutionState.Running);
        var running = await _provider.ExecutionHistory.GetExecutionAsync(id);
        running!.State.Should().Be(ExecutionState.Running);

        var completedAt = DateTimeOffset.UtcNow;
        await _provider.ExecutionHistory.UpdateExecutionStatusAsync(id, ExecutionState.Completed, completedAt);
        var completed = await _provider.ExecutionHistory.GetExecutionAsync(id);
        completed!.State.Should().Be(ExecutionState.Completed);
        completed.CompletedAt.Should().BeCloseTo(completedAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Execution_RecordNode_ThenGetNodes_ShouldRoundTrip()
    {
        var executionId = Guid.NewGuid();
        var nodeRecord = new NodeExecutionRecord(
            ExecutionId: executionId,
            NodeId: "node-1",
            State: NodeExecutionState.Completed,
            StartedAt: DateTimeOffset.UtcNow,
            CompletedAt: DateTimeOffset.UtcNow);

        await _provider.ExecutionHistory.RecordNodeExecutionAsync(nodeRecord);

        var nodes = await _provider.ExecutionHistory.GetNodeExecutionsAsync(executionId);
        nodes.Should().ContainSingle(n => n.NodeId == "node-1");
    }

    /// <summary>
    /// A node that runs more than once in an execution — each loop iteration, each retry attempt —
    /// keeps a row per run with that run's own inputs, iteration, scope metadata, state and error.
    /// </summary>
    [Fact]
    public async Task Execution_RecordNode_SeveralRunsOfOneNode_KeepsARowPerRun()
    {
        var execId = Guid.NewGuid();
        await _provider.ExecutionHistory.CreateExecutionAsync(
            new ExecutionRecord(execId, Guid.NewGuid(), ExecutionState.Running, DateTimeOffset.UtcNow));
        var t0 = DateTimeOffset.UtcNow;

        NodeExecutionRecord Run(int iteration, NodeExecutionState state, int second, string? error = null)
            => new(
                ExecutionId: execId,
                NodeId: "body",
                State: state,
                StartedAt: t0.AddSeconds(second),
                CompletedAt: t0.AddSeconds(second),
                Inputs: new Dictionary<string, object?> { ["item"] = $"item-{iteration}" },
                Error: error,
                Duration: TimeSpan.FromMilliseconds(5),
                LoopId: "loop",
                LoopIteration: iteration,
                Metadata: new Dictionary<string, object?> { ["subGraphId"] = $"loop-iter-{iteration}", ["scopePath"] = $"loop#{iteration}" });

        await _provider.ExecutionHistory.RecordNodeExecutionAsync(Run(1, NodeExecutionState.Completed, 1));
        await _provider.ExecutionHistory.RecordNodeExecutionAsync(Run(2, NodeExecutionState.Failed, 2, "boom"));
        await _provider.ExecutionHistory.RecordNodeExecutionAsync(Run(2, NodeExecutionState.Completed, 3));

        var nodes = (await _provider.ExecutionHistory.GetNodeExecutionsAsync(execId)).OrderBy(n => n.StartedAt).ToList();

        nodes.Should().HaveCount(3, "every run is its own row (each iteration, each retry attempt)");
        nodes.Select(n => n.LoopIteration).Should().Equal(1, 2, 2);
        nodes.Select(n => n.Inputs!["item"]?.ToString()).Should().Equal("item-1", "item-2", "item-2");
        nodes.Select(n => n.State).Should().Equal(NodeExecutionState.Completed, NodeExecutionState.Failed, NodeExecutionState.Completed);
        nodes[1].Error.Should().Be("boom", "the failed attempt keeps its error");
        nodes.Should().OnlyContain(n => n.LoopId == "loop");
        nodes.Select(n => n.Metadata!["scopePath"]?.ToString()).Should().Equal("loop#1", "loop#2", "loop#2");
        nodes.Select(n => n.Metadata!["subGraphId"]?.ToString()).Should().Equal("loop-iter-1", "loop-iter-2", "loop-iter-2");
    }

    [Fact]
    public async Task Execution_GetForWorkflow_ShouldFilterByState()
    {
        var workflowId = Guid.NewGuid();
        await _provider.ExecutionHistory.CreateExecutionAsync(
            new ExecutionRecord(Guid.NewGuid(), workflowId, ExecutionState.Completed, DateTimeOffset.UtcNow));
        await _provider.ExecutionHistory.CreateExecutionAsync(
            new ExecutionRecord(Guid.NewGuid(), workflowId, ExecutionState.Failed, DateTimeOffset.UtcNow));

        var result = await _provider.ExecutionHistory.GetExecutionsForWorkflowAsync(
            workflowId,
            new ExecutionFilter(States: [ExecutionState.Completed]),
            Pagination.Default);

        result.Items.Should().OnlyContain(e => e.State == ExecutionState.Completed);
    }

    // -- Variable Store --------------------------------------------------------

    [Fact]
    public async Task Variable_Set_ShouldCreateVersion1()
    {
        var scope = VariableScope.Global;
        await _provider.Variables.SetVariableAsync(scope, "v1test", "hello");

        var entry = await _provider.Variables.GetVariableAsync(scope, "v1test");
        entry.Should().NotBeNull();
        entry!.Value.ToString().Should().Be("hello");
        entry.Version.Should().Be(1);
    }

    [Fact]
    public async Task Variable_SetTwice_ShouldCreateVersion2_AndGetLatest()
    {
        var scope = VariableScope.ForWorkflow(Guid.NewGuid());
        await _provider.Variables.SetVariableAsync(scope, "myvar", "first");
        await _provider.Variables.SetVariableAsync(scope, "myvar", "second");

        var entry = await _provider.Variables.GetVariableAsync(scope, "myvar");
        entry!.Value!.ToString().Should().Be("second");
        entry.Version.Should().Be(2);
    }

    [Fact]
    public async Task Variable_SetNull_ShouldCreateNullEntry_NotMissing()
    {
        var scope = VariableScope.ForExecution(Guid.NewGuid());
        await _provider.Variables.SetVariableAsync(scope, "nullvar", "hello");
        await _provider.Variables.SetVariableAsync(scope, "nullvar", null);

        var entry = await _provider.Variables.GetVariableAsync(scope, "nullvar");

        // CopilotNote: null value is a VALID entry. entry != null, but entry.Value == null~ ??
        entry.Should().NotBeNull("variable entry with null value is distinct from 'not found'~ ??");
        entry!.Value.Should().BeNull();
        entry.Version.Should().Be(2);
    }

    [Fact]
    public async Task Variable_GetByVersion_ShouldReturnSpecificVersion()
    {
        var scope = VariableScope.Global;
        var varName = $"versioned_{Guid.NewGuid():N}";
        await _provider.Variables.SetVariableAsync(scope, varName, "v1value");
        await _provider.Variables.SetVariableAsync(scope, varName, "v2value");
        await _provider.Variables.SetVariableAsync(scope, varName, "v3value");

        var v1 = await _provider.Variables.GetVariableAsync(scope, varName, version: 1);
        v1!.Value.ToString().Should().Be("v1value");

        var v2 = await _provider.Variables.GetVariableAsync(scope, varName, version: 2);
        v2!.Value.ToString().Should().Be("v2value");
    }

    [Fact]
    public async Task Variable_GetHistory_ShouldReturnAllVersionsOrdered()
    {
        var scope = VariableScope.Global;
        var varName = $"hist_{Guid.NewGuid():N}";
        await _provider.Variables.SetVariableAsync(scope, varName, "h1");
        await _provider.Variables.SetVariableAsync(scope, varName, "h2");
        await _provider.Variables.SetVariableAsync(scope, varName, "h3");

        var history = await _provider.Variables.GetVariableHistoryAsync(scope, varName);

        history.Should().HaveCount(3);
        history.Select(e => e.Version).Should().BeInAscendingOrder();
        history[0].Value.ToString().Should().Be("h1");
        history[2].Value.ToString().Should().Be("h3");
    }

    [Fact]
    public async Task Variable_Delete_ShouldRemoveAll_AndGetReturnsNull()
    {
        var scope = VariableScope.Global;
        var varName = $"del{Guid.NewGuid():N}";
        await _provider.Variables.SetVariableAsync(scope, varName, "something");

        var deleted = await _provider.Variables.DeleteVariableAsync(scope, varName);

        deleted.Should().BeTrue();
        var entry = await _provider.Variables.GetVariableAsync(scope, varName);
        entry.Should().BeNull("deleted variable should not be found~ ???");
    }

    [Fact]
    public async Task Variable_Delete_ShouldReturnFalse_WhenNotFound()
    {
        var scope = VariableScope.Global;
        var result = await _provider.Variables.DeleteVariableAsync(scope, $"nonexistent_{Guid.NewGuid():N}");
        result.Should().BeFalse();
    }

    [Fact]
    public async Task Variable_GetAll_ShouldIncludeNullValuedVariable()
    {
        var scope = VariableScope.ForWorkflow(Guid.NewGuid());
        await _provider.Variables.SetVariableAsync(scope, "withValue", "hello");
        await _provider.Variables.SetVariableAsync(scope, "withNull", null);

        var all = await _provider.Variables.GetAllVariablesAsync(scope);

        all.Should().ContainKey("withValue");
        all.Should().ContainKey("withNull");
        all["withNull"].Should().BeNull("null-valued variable should be present in GetAll~ ??");
    }

    [Fact]
    public async Task Variable_Scopes_ShouldBeIsolated()
    {
        var globalScope = VariableScope.Global;
        var workflowScope = VariableScope.ForWorkflow(Guid.NewGuid());
        var executionScope = VariableScope.ForExecution(Guid.NewGuid());
        var varName = $"isolated_{Guid.NewGuid():N}";

        await _provider.Variables.SetVariableAsync(globalScope, varName, "global");
        await _provider.Variables.SetVariableAsync(workflowScope, varName, "workflow");
        await _provider.Variables.SetVariableAsync(executionScope, varName, "execution");

        var g = await _provider.Variables.GetVariableAsync(globalScope, varName);
        var w = await _provider.Variables.GetVariableAsync(workflowScope, varName);
        var e = await _provider.Variables.GetVariableAsync(executionScope, varName);

        g!.Value.ToString().Should().Be("global");
        w!.Value.ToString().Should().Be("workflow");
        e!.Value.ToString().Should().Be("execution");
    }

    [Fact]
    public async Task Variable_Watch_ShouldFireOnChange()
    {
        // CopilotNote: Basic watch test � confirm that after Set, the updated value is immediately readable.
        // Full reactive watch (IAsyncEnumerable) is a more advanced usage~ ?
        var scope = VariableScope.ForExecution(Guid.NewGuid());
        var varName = $"watch_{Guid.NewGuid():N}";

        await _provider.Variables.SetVariableAsync(scope, varName, "before");
        await _provider.Variables.SetVariableAsync(scope, varName, "after");

        var entry = await _provider.Variables.GetVariableAsync(scope, varName);
        entry!.Value!.ToString().Should().Be("after");
    }

    // -- Helpers ---------------------------------------------------------------

    private static WorkflowDefinition MakeWorkflow(string name) => new(
        Id: Guid.NewGuid(),
        Name: name,
        Description: $"Test workflow: {name}",
        Version: new Version(1, 0),
        Nodes: Arr<NodeDefinition>.Empty,
        Connections: Arr<ConnectionDefinition>.Empty,
        Variables: HashMap<string, VariableDefinition>.Empty,
        CreatedAt: DateTimeOffset.UtcNow,
        UpdatedAt: DateTimeOffset.UtcNow);
}


