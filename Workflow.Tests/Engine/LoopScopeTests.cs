// <copyright file="LoopScopeTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Engine;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit.Xunit2;
using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Core.Models;
using Workflow.Engine.Actors;
using Workflow.Engine.Messages;
using Workflow.Engine.Models;
using Workflow.Modules;
using Workflow.Modules.Abstractions;
using Workflow.Persistence.Abstractions;
using Workflow.Persistence.Models;
using Xunit;
#pragma warning disable SA1204 // StaticElementsMustAppearBeforeInstanceElements

/// <summary>
/// Tests for Phase 2.2.0b Loop Scope infrastructure in <see cref="WorkflowExecutor"/>~ 🔁✨
/// Validates that PushLoopScope/PopLoopScope messages correctly maintain the executor's
/// <c>_loopContextStack</c> and that <see cref="NodeExecutionRecord"/> objects are stamped
/// with the active loop's LoopId + LoopIteration when a loop scope is on the stack~ 💖.
/// </summary>
public class LoopScopeTests : TestKit
{
    // ── Test 1: Pushed loop scope is stamped onto NodeExecutionRecords ───────────────────────────

    /// <summary>
    /// When a PushLoopScope message is sent before StartExecution, all node records
    /// produced during that execution should carry the active LoopId and LoopIteration~ 🔁
    /// </summary>
    [Fact]
    public void LoopScope_PushedBeforeExecution_StampsNodeRecordWithLoopId()
    {
        // Arrange — single-node workflow, capturing history repo
        var captured = new List<NodeExecutionRecord>();
        var histRepo = new CapturingHistoryRepository(captured);

        var registry = new InMemoryModuleRegistry(skipValidation: true);
        registry.RegisterModule(new StubPassthroughModule("pass.loop"));

        var sp = new ServiceCollection()
            .AddSingleton<IModuleRegistry>(registry)
            .AddSingleton<IExecutionHistoryRepository>(histRepo)
            .BuildServiceProvider();

        var nodeA = new NodeDefinition("nodeA", "pass.loop", "A",
            HashMap<string, System.Text.Json.JsonElement>.Empty);
        var definition = new WorkflowDefinition(
            Id: Guid.NewGuid(),
            Name: "loop-stamp-test",
            Description: null,
            Version: new Version(1, 0),
            Nodes: new[] { nodeA }.ToArr(),
            Connections: Arr<ConnectionDefinition>.Empty,
            Variables: HashMap<string, VariableDefinition>.Empty);

        var executionId = Guid.NewGuid();
        var parentProbe = CreateTestProbe("loop-stamp-parent");
        var executor = parentProbe.ChildActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), sp),
            "loop-stamp-exec");

        // Act — push loop scope FIRST (processed before StartExecution due to actor message ordering)
        var loopCtx = new LoopContext("myLoop", initialIteration: 2);
        executor.Tell(new PushLoopScope(loopCtx));
        executor.Tell(new StartExecution(executionId));

        // Wait for workflow to complete
        parentProbe.FishForMessage(m => m is WorkflowCompleted, TimeSpan.FromSeconds(5));

        // Give async persistence a moment to flush~ ⏰
        Thread.Sleep(200);

        // Assert — node record should be stamped with the loop context
        captured.Should().HaveCountGreaterThanOrEqualTo(1, "nodeA should have a NodeExecutionRecord");
        var rec = captured.FirstOrDefault(r => r.NodeId == "nodeA");
        rec.Should().NotBeNull("nodeA should have been recorded");
        rec!.LoopId.Should().Be("myLoop", "the active loop scope's id should be stamped");
        rec.LoopIteration.Should().Be(2, "the loop context was initialised with iteration=2");
    }

    // ── Test 2: Records have no loop stamp when no scope is active ───────────────────────────────

    /// <summary>
    /// When no loop scope is active, NodeExecutionRecords have null LoopId/LoopIteration
    /// (regression guard — existing behaviour unchanged)~ ✅
    /// </summary>
    [Fact]
    public void LoopScope_NotPushed_RecordsHaveNullLoopId()
    {
        // Arrange
        var captured = new List<NodeExecutionRecord>();
        var histRepo = new CapturingHistoryRepository(captured);

        var registry = new InMemoryModuleRegistry(skipValidation: true);
        registry.RegisterModule(new StubPassthroughModule("pass.noloop"));

        var sp = new ServiceCollection()
            .AddSingleton<IModuleRegistry>(registry)
            .AddSingleton<IExecutionHistoryRepository>(histRepo)
            .BuildServiceProvider();

        var nodeA = new NodeDefinition("nodeA", "pass.noloop", "A",
            HashMap<string, System.Text.Json.JsonElement>.Empty);
        var definition = new WorkflowDefinition(
            Id: Guid.NewGuid(),
            Name: "no-loop-test",
            Description: null,
            Version: new Version(1, 0),
            Nodes: new[] { nodeA }.ToArr(),
            Connections: Arr<ConnectionDefinition>.Empty,
            Variables: HashMap<string, VariableDefinition>.Empty);

        var executionId = Guid.NewGuid();
        var parentProbe = CreateTestProbe("no-loop-parent");
        var executor = parentProbe.ChildActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), sp),
            "no-loop-exec");

        // Act — start WITHOUT pushing any loop scope
        executor.Tell(new StartExecution(executionId));
        parentProbe.FishForMessage(m => m is WorkflowCompleted, TimeSpan.FromSeconds(5));
        Thread.Sleep(200);

        // Assert
        captured.Should().HaveCountGreaterThanOrEqualTo(1);
        var rec = captured.FirstOrDefault(r => r.NodeId == "nodeA");
        rec.Should().NotBeNull();
        rec!.LoopId.Should().BeNull("no loop scope was active");
        rec.LoopIteration.Should().BeNull("no loop scope was active");
    }

    // ── Test 3: Pop scope after it's active → loop stamp clears for subsequent nodes ─────────────

    /// <summary>
    /// Popping the loop scope before any nodes run means those node records won't be stamped.
    /// PushLoopScope → PopLoopScope → StartExecution → records have null LoopId~ ⬆️✅
    /// </summary>
    [Fact]
    public void LoopScope_PopBeforeExecution_RecordsHaveNullLoopId()
    {
        var captured = new List<NodeExecutionRecord>();
        var histRepo = new CapturingHistoryRepository(captured);

        var registry = new InMemoryModuleRegistry(skipValidation: true);
        registry.RegisterModule(new StubPassthroughModule("pass.pop"));

        var sp = new ServiceCollection()
            .AddSingleton<IModuleRegistry>(registry)
            .AddSingleton<IExecutionHistoryRepository>(histRepo)
            .BuildServiceProvider();

        var nodeA = new NodeDefinition("nodeA", "pass.pop", "A",
            HashMap<string, System.Text.Json.JsonElement>.Empty);
        var definition = new WorkflowDefinition(
            Id: Guid.NewGuid(), Name: "pop-loop-test", Description: null,
            Version: new Version(1, 0),
            Nodes: new[] { nodeA }.ToArr(),
            Connections: Arr<ConnectionDefinition>.Empty,
            Variables: HashMap<string, VariableDefinition>.Empty);

        var executionId = Guid.NewGuid();
        var parentProbe = CreateTestProbe("pop-loop-parent");
        var executor = parentProbe.ChildActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), sp),
            "pop-loop-exec");

        // Act — push then immediately pop scope (net effect: no active scope)
        executor.Tell(new PushLoopScope(new LoopContext("vanishingLoop")));
        executor.Tell(new PopLoopScope("vanishingLoop"));
        executor.Tell(new StartExecution(executionId));

        parentProbe.FishForMessage(m => m is WorkflowCompleted, TimeSpan.FromSeconds(5));
        Thread.Sleep(200);

        // Assert — records should have no loop stamp because scope was popped before execution
        var rec = captured.FirstOrDefault(r => r.NodeId == "nodeA");
        rec.Should().NotBeNull();
        rec!.LoopId.Should().BeNull("loop scope was popped before execution started");
    }

    // ── Test 4: Nested loop scopes — innermost is on top after double-push ───────────────────────

    /// <summary>
    /// Pushing two loop scopes makes the second (inner) scope active.
    /// After popping the inner scope, the outer scope becomes active again.
    /// NodeExecutionRecord is stamped with whichever scope is currently on top~ 🔁🔁
    /// </summary>
    [Fact]
    public void LoopScope_Nested_InnerScopeStampsWhenActive()
    {
        var captured = new List<NodeExecutionRecord>();
        var histRepo = new CapturingHistoryRepository(captured);

        var registry = new InMemoryModuleRegistry(skipValidation: true);
        registry.RegisterModule(new StubPassthroughModule("pass.inner"));

        var sp = new ServiceCollection()
            .AddSingleton<IModuleRegistry>(registry)
            .AddSingleton<IExecutionHistoryRepository>(histRepo)
            .BuildServiceProvider();

        var nodeA = new NodeDefinition("nodeA", "pass.inner", "A",
            HashMap<string, System.Text.Json.JsonElement>.Empty);
        var definition = new WorkflowDefinition(
            Id: Guid.NewGuid(), Name: "nested-loop-test", Description: null,
            Version: new Version(1, 0),
            Nodes: new[] { nodeA }.ToArr(),
            Connections: Arr<ConnectionDefinition>.Empty,
            Variables: HashMap<string, VariableDefinition>.Empty);

        var executionId = Guid.NewGuid();
        var parentProbe = CreateTestProbe("nested-loop-parent");
        var executor = parentProbe.ChildActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), sp),
            "nested-loop-exec");

        // Act — push outer, then inner loop scope → inner is on top when execution happens
        executor.Tell(new PushLoopScope(new LoopContext("outerLoop", initialIteration: 1)));
        executor.Tell(new PushLoopScope(new LoopContext("innerLoop", initialIteration: 3)));
        executor.Tell(new StartExecution(executionId));

        parentProbe.FishForMessage(m => m is WorkflowCompleted, TimeSpan.FromSeconds(5));
        Thread.Sleep(200);

        // Assert — the INNER loop (top of stack) should stamp records, not the outer one
        var rec = captured.FirstOrDefault(r => r.NodeId == "nodeA");
        rec.Should().NotBeNull();
        rec!.LoopId.Should().Be("innerLoop", "innerLoop is on top of the stack when nodeA runs");
        rec.LoopIteration.Should().Be(3, "innerLoop was initialised with iteration=3");
    }

    // ── Stubs & Helpers ──────────────────────────────────────────────────────────────────────────

    /// <summary>Simple pass-through module that always succeeds~ 🌿</summary>
    private sealed class StubPassthroughModule : IWorkflowModule
    {
        public StubPassthroughModule(string moduleId) => ModuleId = moduleId;
        public string ModuleId { get; }
        public string DisplayName => ModuleId;
        public string Category => "Test";
        public string Description => "Pass-through";
        public string Icon => "🌿";
        public Version Version => new(1, 0);

        public ModuleSchema Schema => new(
            Inputs: new[] { PortDefinition.Create<object>("input", isRequired: false) }.ToArr(),
            Outputs: new[] { PortDefinition.Create<object>("output", isRequired: false) }.ToArr(),
            Properties: Arr<ModulePropertyDefinition>.Empty);

        public Task<ModuleResult> ExecuteAsync(ModuleExecutionContext ctx, CancellationToken ct = default)
            => Task.FromResult(ModuleResult.Ok(new Dictionary<string, object?> { ["output"] = $"{ctx.NodeId}-done" }));
    }

    /// <summary>Thread-safe capturing history repository~ 💾</summary>
    private sealed class CapturingHistoryRepository : IExecutionHistoryRepository
    {
        private readonly List<NodeExecutionRecord> _records;
        public CapturingHistoryRepository(List<NodeExecutionRecord> records) => _records = records;

        public Task<Guid> CreateExecutionAsync(ExecutionRecord record, CancellationToken ct = default)
            => Task.FromResult(record.ExecutionId);

        public Task RecordNodeExecutionAsync(NodeExecutionRecord nodeRecord, CancellationToken ct = default)
        {
            lock (_records)
            {
                _records.Add(nodeRecord);
            }

            return Task.CompletedTask;
        }

        public Task UpdateExecutionStatusAsync(Guid executionId, ExecutionState state,
            DateTimeOffset? endTime = null, string? error = null, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<NodeExecutionRecord>> GetNodeExecutionsAsync(Guid executionId,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<NodeExecutionRecord>>(new List<NodeExecutionRecord>());

        public Task<ExecutionRecord?> GetExecutionAsync(Guid executionId, CancellationToken ct = default)
            => Task.FromResult<ExecutionRecord?>(null);

        public Task<PagedResult<ExecutionRecord>> GetExecutionsForWorkflowAsync(
            Guid workflowId, ExecutionFilter filter, Pagination pagination, CancellationToken ct = default)
            => Task.FromResult(new PagedResult<ExecutionRecord>(new List<ExecutionRecord>(), 0, 1, 20));
    }
}



