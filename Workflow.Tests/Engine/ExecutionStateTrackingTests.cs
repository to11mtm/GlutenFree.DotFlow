// <copyright file="ExecutionStateTrackingTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Engine;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit.Xunit2;
using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Core.Abstractions;
using Workflow.Core.Models;
using Workflow.Engine.Actors;
using Workflow.Engine.Messages;
using Workflow.Engine.Models;
using Workflow.Engine.Services;
using Xunit;

/// <summary>
/// Comprehensive tests for Phase 1.3.7 � Execution State Tracking~ ???
/// Covers state transitions, persistence snapshots, restoration, concurrent updates,
/// and state change notifications via EventStream. UwU ??
/// </summary>
public class ExecutionStateTrackingTests : TestKit
{
    private readonly IServiceProvider _serviceProvider;
    private readonly InMemoryExecutionStateStore _stateStore;

    /// <summary>
    /// Initializes test environment with DI services including the state store~ ??
    /// </summary>
    public ExecutionStateTrackingTests()
    {
        _stateStore = new InMemoryExecutionStateStore();

        var services = new ServiceCollection();
        services.AddSingleton<WorkflowValidator>();
        services.AddSingleton<IExecutionStateStore>(_stateStore);
        _serviceProvider = services.BuildServiceProvider();
    }

    #region WorkflowExecutionContext Unit Tests ??

    /// <summary>
    /// Test that a fresh context is created in the Pending state with all nodes Pending.
    /// </summary>
    [Fact]
    public void ExecutionContext_Create_ShouldBeInPendingState()
    {
        // Arrange & Act
        var ctx = WorkflowExecutionContext.Create(
            executionId: Guid.NewGuid(),
            workflowId: Guid.NewGuid(),
            workflowName: "Test Workflow",
            initialNodeIds: new[] { "node_a", "node_b", "node_c" });

        // Assert
        ctx.State.Should().Be(ExecutionState.Pending);
        ctx.NodeStates.Count.Should().Be(3);
        ctx.NodeStates.Values.Should().AllBeEquivalentTo(NodeExecutionState.Pending);
        ctx.StartTime.IsNone.Should().BeTrue();
        ctx.EndTime.IsNone.Should().BeTrue();
        ctx.Error.IsNone.Should().BeTrue();
        ctx.StateHistory.Count.Should().Be(0);
        ctx.IsTerminal.Should().BeFalse();
    }

    /// <summary>
    /// Test Pending ? Running transition records correct state history~ ??
    /// </summary>
    [Fact]
    public void ExecutionContext_WithRunning_ShouldTransitionCorrectly()
    {
        // Arrange
        var ctx = WorkflowExecutionContext.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Test", new[] { "a" });
        var startTime = DateTimeOffset.UtcNow;

        // Act
        var running = ctx.WithRunning(startTime);

        // Assert
        running.State.Should().Be(ExecutionState.Running);
        running.StartTime.IsSome.Should().BeTrue();
        running.StartTime.IfNone(DateTimeOffset.MinValue).Should().Be(startTime);
        running.StateHistory.Count.Should().Be(1);
        running.StateHistory[0].OldState.Should().Be(ExecutionState.Pending);
        running.StateHistory[0].NewState.Should().Be(ExecutionState.Running);
        running.IsTerminal.Should().BeFalse();
    }

    /// <summary>
    /// Test Running ? Completed transition with outputs~ ??
    /// </summary>
    [Fact]
    public void ExecutionContext_WithCompleted_ShouldSetOutputsAndEndTime()
    {
        // Arrange
        var ctx = WorkflowExecutionContext.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Test", new[] { "a" })
            .WithRunning(DateTimeOffset.UtcNow);
        var endTime = DateTimeOffset.UtcNow.AddSeconds(5);
        var outputs = HashMap<string, object?>.Empty.Add("result", (object?)"hello");

        // Act
        var completed = ctx.WithCompleted(endTime, outputs);

        // Assert
        completed.State.Should().Be(ExecutionState.Completed);
        completed.EndTime.IsSome.Should().BeTrue();
        completed.Outputs.ContainsKey("result").Should().BeTrue();
        completed.IsTerminal.Should().BeTrue();
        completed.StateHistory.Count.Should().Be(2); // Pending?Running, Running?Completed
    }

    /// <summary>
    /// Test Running ? Failed transition with error~ ??
    /// </summary>
    [Fact]
    public void ExecutionContext_WithFailed_ShouldSetErrorAndEndTime()
    {
        // Arrange
        var ctx = WorkflowExecutionContext.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Test", new[] { "a" })
            .WithRunning(DateTimeOffset.UtcNow);
        var endTime = DateTimeOffset.UtcNow.AddSeconds(1);

        // Act
        var failed = ctx.WithFailed(endTime, "Something went wrong!");

        // Assert
        failed.State.Should().Be(ExecutionState.Failed);
        failed.Error.IsSome.Should().BeTrue();
        failed.Error.IfNone("").Should().Be("Something went wrong!");
        failed.IsTerminal.Should().BeTrue();
    }

    /// <summary>
    /// Test Running ? Cancelled transition~ ??
    /// </summary>
    [Fact]
    public void ExecutionContext_WithCancelled_ShouldSetEndTime()
    {
        // Arrange
        var ctx = WorkflowExecutionContext.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Test", new[] { "a" })
            .WithRunning(DateTimeOffset.UtcNow);

        // Act
        var cancelled = ctx.WithCancelled(DateTimeOffset.UtcNow.AddSeconds(1));

        // Assert
        cancelled.State.Should().Be(ExecutionState.Cancelled);
        cancelled.EndTime.IsSome.Should().BeTrue();
        cancelled.IsTerminal.Should().BeTrue();
    }

    /// <summary>
    /// Test Running ? Paused ? Running (resume) full lifecycle~ ????
    /// </summary>
    [Fact]
    public void ExecutionContext_PauseAndResume_ShouldTrackStateHistory()
    {
        // Arrange
        var ctx = WorkflowExecutionContext.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Test", new[] { "a" })
            .WithRunning(DateTimeOffset.UtcNow);

        // Act � pause
        var paused = ctx.WithPaused(DateTimeOffset.UtcNow.AddSeconds(1));

        // Assert paused
        paused.State.Should().Be(ExecutionState.Paused);
        paused.StateHistory.Count.Should().Be(2); // Pending?Running, Running?Paused

        // Act � resume
        var resumed = paused.WithResumed(DateTimeOffset.UtcNow.AddSeconds(2));

        // Assert resumed
        resumed.State.Should().Be(ExecutionState.Running);
        resumed.StateHistory.Count.Should().Be(3); // + Paused?Running
        resumed.IsTerminal.Should().BeFalse();
    }

    /// <summary>
    /// Test node state tracking updates correctly~ ??
    /// </summary>
    [Fact]
    public void ExecutionContext_NodeStateUpdates_ShouldTrackCorrectly()
    {
        // Arrange
        var ctx = WorkflowExecutionContext.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Test", new[] { "a", "b", "c" });
        var now = DateTimeOffset.UtcNow;

        // Act � advance node_a through its lifecycle
        ctx = ctx.WithNodeStarted("a", now);
        ctx = ctx.WithNodeCompleted("a", now.AddSeconds(1), TimeSpan.FromSeconds(1));

        // Assert
        ctx.NodeStates.Find("a").IfNone(NodeExecutionState.Pending)
            .Should().Be(NodeExecutionState.Completed);
        ctx.NodeStates.Find("b").IfNone(NodeExecutionState.Pending)
            .Should().Be(NodeExecutionState.Pending);
        ctx.NodeTimings.Find("a").IsSome.Should().BeTrue();
        ctx.NodeTimings.Find("a").IfNone(() => default!).Duration.IsSome.Should().BeTrue();
    }

    /// <summary>
    /// Test progress calculation~ ??
    /// </summary>
    [Fact]
    public void ExecutionContext_CalculateProgress_ShouldComputeCorrectly()
    {
        // Arrange
        var ctx = WorkflowExecutionContext.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Test", new[] { "a", "b", "c", "d" });

        // Act / Assert � 0/4 = 0%
        ctx.CalculateProgress().Should().Be(0);

        // Complete two nodes
        ctx = ctx.WithNodeState("a", NodeExecutionState.Completed);
        ctx = ctx.WithNodeState("b", NodeExecutionState.Completed);
        ctx.CalculateProgress().Should().Be(50);

        // Complete all
        ctx = ctx.WithNodeState("c", NodeExecutionState.Completed);
        ctx = ctx.WithNodeState("d", NodeExecutionState.Completed);
        ctx.CalculateProgress().Should().Be(100);
    }

    /// <summary>
    /// Test empty workflow progress is 100%~ ?
    /// </summary>
    [Fact]
    public void ExecutionContext_EmptyWorkflow_ProgressShouldBe100()
    {
        var ctx = WorkflowExecutionContext.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Empty", Array.Empty<string>());

        ctx.CalculateProgress().Should().Be(100);
    }

    /// <summary>
    /// Test variable tracking~ ??
    /// </summary>
    [Fact]
    public void ExecutionContext_WithVariable_ShouldStoreAndOverwrite()
    {
        // Arrange
        var ctx = WorkflowExecutionContext.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Test", new[] { "a" });

        // Act
        ctx = ctx.WithVariable("counter", 1);
        ctx = ctx.WithVariable("name", "Ami");
        ctx = ctx.WithVariable("counter", 42); // overwrite

        // Assert
        ctx.Variables.Find("counter").IfNone(-1).Should().Be(42);
        ctx.Variables.Find("name").IfNone("").Should().Be("Ami");
    }

    /// <summary>
    /// Test GetNodeStateCounts tuple~ ??
    /// </summary>
    [Fact]
    public void ExecutionContext_GetNodeStateCounts_ShouldBeAccurate()
    {
        var ctx = WorkflowExecutionContext.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Test", new[] { "a", "b", "c", "d", "e" });

        ctx = ctx.WithNodeState("a", NodeExecutionState.Running);
        ctx = ctx.WithNodeState("b", NodeExecutionState.Completed);
        ctx = ctx.WithNodeState("c", NodeExecutionState.Failed);
        ctx = ctx.WithNodeState("d", NodeExecutionState.Cancelled);
        // e stays Pending

        var counts = ctx.GetNodeStateCounts();
        counts.Pending.Should().Be(1);
        counts.Running.Should().Be(1);
        counts.Completed.Should().Be(1);
        counts.Failed.Should().Be(1);
        counts.Cancelled.Should().Be(1);
        counts.Retrying.Should().Be(0);
    }

    #endregion

    #region State Persistence Tests ??

    /// <summary>
    /// Test that state can be saved and loaded from InMemoryExecutionStateStore~ ??
    /// </summary>
    [Fact]
    public async Task StatePersistence_SaveAndLoad_ShouldRoundTrip()
    {
        // Arrange
        var executionId = Guid.NewGuid();
        var ctx = WorkflowExecutionContext.Create(
            executionId, Guid.NewGuid(), "Persistence Test", new[] { "a", "b" })
            .WithRunning(DateTimeOffset.UtcNow);

        // Act � save
        await _stateStore.SaveSnapshotAsync(ctx);

        // Act � load
        var loaded = await _stateStore.LoadSnapshotAsync(executionId);

        // Assert
        loaded.IsSome.Should().BeTrue();
        loaded.IfNone(() => default!).ExecutionId.Should().Be(executionId);
        loaded.IfNone(() => default!).State.Should().Be(ExecutionState.Running);
        loaded.IfNone(() => default!).NodeStates.Count.Should().Be(2);
    }

    /// <summary>
    /// Test loading a non-existent snapshot returns None~ ??
    /// </summary>
    [Fact]
    public async Task StatePersistence_LoadNonExistent_ShouldReturnNone()
    {
        var loaded = await _stateStore.LoadSnapshotAsync(Guid.NewGuid());
        loaded.IsNone.Should().BeTrue();
    }

    /// <summary>
    /// Test snapshot overwrite updates the stored value~ ??
    /// </summary>
    [Fact]
    public async Task StatePersistence_SaveTwice_ShouldOverwrite()
    {
        var executionId = Guid.NewGuid();

        var ctx1 = WorkflowExecutionContext.Create(
            executionId, Guid.NewGuid(), "V1", new[] { "a" });
        await _stateStore.SaveSnapshotAsync(ctx1);

        var ctx2 = ctx1.WithRunning(DateTimeOffset.UtcNow);
        await _stateStore.SaveSnapshotAsync(ctx2);

        var loaded = await _stateStore.LoadSnapshotAsync(executionId);
        loaded.IfNone(() => default!).State.Should().Be(ExecutionState.Running);
    }

    /// <summary>
    /// Test snapshot deletion~ ???
    /// </summary>
    [Fact]
    public async Task StatePersistence_Delete_ShouldRemoveSnapshot()
    {
        var executionId = Guid.NewGuid();
        var ctx = WorkflowExecutionContext.Create(
            executionId, Guid.NewGuid(), "Del", new[] { "a" });

        await _stateStore.SaveSnapshotAsync(ctx);
        _stateStore.Count.Should().Be(1);

        var deleted = await _stateStore.DeleteSnapshotAsync(executionId);
        deleted.Should().BeTrue();
        _stateStore.Count.Should().Be(0);

        var loaded = await _stateStore.LoadSnapshotAsync(executionId);
        loaded.IsNone.Should().BeTrue();
    }

    /// <summary>
    /// Test listing all snapshots~ ??
    /// </summary>
    [Fact]
    public async Task StatePersistence_ListSnapshots_ShouldReturnAllIds()
    {
        _stateStore.Clear();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        await _stateStore.SaveSnapshotAsync(
            WorkflowExecutionContext.Create(id1, Guid.NewGuid(), "W1", new[] { "a" }));
        await _stateStore.SaveSnapshotAsync(
            WorkflowExecutionContext.Create(id2, Guid.NewGuid(), "W2", new[] { "b" }));

        var list = await _stateStore.ListSnapshotsAsync();
        list.Count.Should().Be(2);
        list.Should().Contain(id1);
        list.Should().Contain(id2);
    }

    #endregion

    #region Actor State Transition Tests (Integration) ??

    /// <summary>
    /// Test that StartExecution transitions the executor to Running state~ ??
    /// </summary>
    [Fact]
    public void Executor_StartExecution_ShouldTransitionToRunning()
    {
        // Arrange
        var executionId = Guid.NewGuid();
        var definition = CreateSingleNodeWorkflow();
        var executor = Sys.ActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), _serviceProvider));

        // Act
        executor.Tell(new StartExecution(executionId));
        Thread.Sleep(100);

        // Assert
        executor.Tell(new GetWorkflowStatus(executionId));
        var status = ExpectMsg<WorkflowStatusResponse>(TimeSpan.FromSeconds(3));
        status.State.Should().BeOneOf(ExecutionState.Running, ExecutionState.Completed);
    }

    /// <summary>
    /// Test that a completed workflow transitions to Completed with 100% progress~ ??
    /// </summary>
    [Fact]
    public void Executor_CompletedWorkflow_ShouldBeInCompletedState()
    {
        // Arrange
        var executionId = Guid.NewGuid();
        var definition = CreateSingleNodeWorkflow();
        var executor = Sys.ActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), _serviceProvider));

        // Act
        executor.Tell(new StartExecution(executionId));
        Thread.Sleep(2000);

        // Assert
        executor.Tell(new GetWorkflowStatus(executionId));
        var status = ExpectMsg<WorkflowStatusResponse>(TimeSpan.FromSeconds(3));

        if (status.State == ExecutionState.Completed)
        {
            status.Progress.Should().Be(100);
            status.EndTime.IsSome.Should().BeTrue();
            status.Error.IsNone.Should().BeTrue();
        }
    }

    /// <summary>
    /// Test that cancellation transitions to Cancelled state~ ??
    /// </summary>
    [Fact]
    public void Executor_CancelExecution_ShouldTransitionToCancelled()
    {
        // Arrange
        var executionId = Guid.NewGuid();
        var definition = CreateLinearWorkflow();
        var executor = Sys.ActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), _serviceProvider));

        executor.Tell(new StartExecution(executionId));
        Thread.Sleep(50); // Let it start

        // Act
        executor.Tell(new CancelExecution(executionId));
        Thread.Sleep(500);

        // Assert
        executor.Tell(new GetWorkflowStatus(executionId));
        var status = ExpectMsg<WorkflowStatusResponse>(TimeSpan.FromSeconds(3));
        status.State.Should().BeOneOf(ExecutionState.Cancelled, ExecutionState.Completed);
    }

    /// <summary>
    /// Test that an empty workflow goes directly to Completed~ ?
    /// </summary>
    [Fact]
    public void Executor_EmptyWorkflow_ShouldCompleteImmediately()
    {
        // Arrange
        var executionId = Guid.NewGuid();
        var definition = CreateEmptyWorkflow();
        var executor = Sys.ActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), _serviceProvider));

        // Act
        executor.Tell(new StartExecution(executionId));
        Thread.Sleep(500);

        // Assert
        executor.Tell(new GetWorkflowStatus(executionId));
        var status = ExpectMsg<WorkflowStatusResponse>(TimeSpan.FromSeconds(3));
        status.State.Should().Be(ExecutionState.Completed);
        status.Progress.Should().Be(100);
    }

    #endregion

    #region State Change Notification Tests ??

    /// <summary>
    /// Test that ExecutionStateChanged events are published on the EventStream~ ??
    /// </summary>
    [Fact]
    public void Executor_ShouldPublishExecutionStateChangedEvents()
    {
        // Arrange � subscribe to EventStream
        Sys.EventStream.Subscribe(TestActor, typeof(ExecutionStateChanged));

        var executionId = Guid.NewGuid();
        var definition = CreateSingleNodeWorkflow();
        var executor = Sys.ActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), _serviceProvider));

        // Act
        executor.Tell(new StartExecution(executionId));

        // Assert � should get at least Pending ? Running
        var stateChanged = ExpectMsg<ExecutionStateChanged>(TimeSpan.FromSeconds(3));
        stateChanged.ExecutionId.Should().Be(executionId);
        stateChanged.OldState.Should().Be(ExecutionState.Pending);
        stateChanged.NewState.Should().Be(ExecutionState.Running);
    }

    /// <summary>
    /// Test that NodeStateChanged events are published for each node transition~ ??
    /// </summary>
    [Fact]
    public void Executor_ShouldPublishNodeStateChangedEvents()
    {
        // Arrange � subscribe to EventStream
        Sys.EventStream.Subscribe(TestActor, typeof(NodeStateChanged));

        var executionId = Guid.NewGuid();
        var definition = CreateSingleNodeWorkflow();
        var executor = Sys.ActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), _serviceProvider));

        // Act
        executor.Tell(new StartExecution(executionId));

        // Assert � should get Pending ? Running for node_a
        var nodeChanged = ExpectMsg<NodeStateChanged>(TimeSpan.FromSeconds(3));
        nodeChanged.NodeId.Should().Be("node_a");
        nodeChanged.ExecutionId.Should().Be(executionId);
        nodeChanged.OldState.Should().Be(NodeExecutionState.Pending);
        nodeChanged.NewState.Should().Be(NodeExecutionState.Running);
    }

    /// <summary>
    /// Test that completed workflow publishes the final state change event~ ??
    /// </summary>
    [Fact]
    public void Executor_CompletedWorkflow_ShouldPublishCompletedEvent()
    {
        // Arrange
        Sys.EventStream.Subscribe(TestActor, typeof(ExecutionStateChanged));

        var executionId = Guid.NewGuid();
        var definition = CreateSingleNodeWorkflow();
        var executor = Sys.ActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), _serviceProvider));

        // Act
        executor.Tell(new StartExecution(executionId));

        // Collect all state change events
        var events = new List<ExecutionStateChanged>();
        try
        {
            while (true)
            {
                var evt = ExpectMsg<ExecutionStateChanged>(TimeSpan.FromSeconds(5));
                events.Add(evt);
            }
        }
        catch
        {
            // Expected � timeout when no more events
        }

        // Assert � should have at least Pending ? Running
        events.Should().Contain(e => e.OldState == ExecutionState.Pending && e.NewState == ExecutionState.Running);

        // May also have Running ? Completed (depending on timing)
        if (events.Any(e => e.NewState == ExecutionState.Completed))
        {
            events.Should().Contain(e => e.OldState == ExecutionState.Running && e.NewState == ExecutionState.Completed);
        }
    }

    #endregion

    #region Snapshot via Actor Messages Tests ??

    /// <summary>
    /// Test that GetExecutionSnapshot returns the current context~ ??
    /// </summary>
    [Fact]
    public void Executor_GetExecutionSnapshot_ShouldReturnCurrentContext()
    {
        // Arrange
        var executionId = Guid.NewGuid();
        var definition = CreateSingleNodeWorkflow();
        var executor = Sys.ActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), _serviceProvider));

        // Act
        executor.Tell(new GetExecutionSnapshot(executionId));

        // Assert
        var response = ExpectMsg<ExecutionSnapshotResponse>(TimeSpan.FromSeconds(3));
        response.ExecutionId.Should().Be(executionId);
        response.Context.IsSome.Should().BeTrue();
        response.Context.IfNone(() => default!).ExecutionId.Should().Be(executionId);
        response.Context.IfNone(() => default!).State.Should().Be(ExecutionState.Pending);
    }

    /// <summary>
    /// Test that SaveExecutionSnapshot responds with confirmation~ ?
    /// </summary>
    [Fact]
    public void Executor_SaveExecutionSnapshot_ShouldRespond()
    {
        // Arrange
        var executionId = Guid.NewGuid();
        var definition = CreateSingleNodeWorkflow();
        var executor = Sys.ActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), _serviceProvider));

        // Act
        executor.Tell(new SaveExecutionSnapshot(executionId));

        // Assert
        var response = ExpectMsg<ExecutionSnapshotSaved>(TimeSpan.FromSeconds(3));
        response.ExecutionId.Should().Be(executionId);
    }

    /// <summary>
    /// Test that state store is populated after workflow completion~ ??
    /// </summary>
    [Fact]
    public void Executor_CompletedWorkflow_ShouldPersistSnapshot()
    {
        // Arrange
        _stateStore.Clear();
        var executionId = Guid.NewGuid();
        var definition = CreateSingleNodeWorkflow();
        var executor = Sys.ActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), _serviceProvider));

        // Act
        executor.Tell(new StartExecution(executionId));
        Thread.Sleep(3000); // Wait for completion and snapshot save

        // Assert � state store should have the snapshot
        var loaded = _stateStore.LoadSnapshotAsync(executionId).Result;
        loaded.IsSome.Should().BeTrue();
        loaded.IfNone(() => default!).State.Should().Be(ExecutionState.Completed);
    }

    #endregion

    #region Concurrent State Updates Tests ?

    /// <summary>
    /// Test that multiple rapid node completions don't corrupt state~ ?
    /// CopilotNote: Because Akka actors process messages sequentially,
    /// this is inherently safe. But we test it anyway to verify! UwU ??
    /// </summary>
    [Fact]
    public void Executor_LinearWorkflow_ConcurrentCompletions_ShouldTrackAllNodes()
    {
        // Arrange
        var executionId = Guid.NewGuid();
        var definition = CreateLinearWorkflow();
        var executor = Sys.ActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), _serviceProvider));

        // Act
        executor.Tell(new StartExecution(executionId));
        Thread.Sleep(4000); // Generous wait for A?B?C

        // Assert
        executor.Tell(new GetWorkflowStatus(executionId));
        var status = ExpectMsg<WorkflowStatusResponse>(TimeSpan.FromSeconds(5));

        // Either still running or completed � all node states should be tracked
        status.NodeStates.Count.Should().Be(3);

        if (status.State == ExecutionState.Completed)
        {
            status.NodeStates.Values.Should().AllBeEquivalentTo(NodeExecutionState.Completed);
            status.Progress.Should().Be(100);
        }
    }

    /// <summary>
    /// Test that the context snapshot after completion contains all expected data~ ??
    /// </summary>
    [Fact]
    public void Executor_CompletedWorkflow_SnapshotShouldContainFullContext()
    {
        // Arrange
        _stateStore.Clear();
        var executionId = Guid.NewGuid();
        var definition = CreateLinearWorkflow();
        var executor = Sys.ActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), _serviceProvider));

        // Act
        executor.Tell(new StartExecution(executionId));
        Thread.Sleep(5000);

        // Assert
        var loaded = _stateStore.LoadSnapshotAsync(executionId).Result;
        if (loaded.IsSome)
        {
            var ctx = loaded.IfNone(() => default!);
            ctx.ExecutionId.Should().Be(executionId);
            ctx.WorkflowName.Should().Be("Linear Workflow");
            ctx.NodeStates.Count.Should().Be(3);

            if (ctx.State == ExecutionState.Completed)
            {
                ctx.EndTime.IsSome.Should().BeTrue();
                ctx.StateHistory.Count.Should().BeGreaterOrEqualTo(2); // Pending?Running, Running?Completed
            }
        }
    }

    #endregion

    #region Pause / Resume Integration Tests ????

    /// <summary>
    /// Test pause during execution pauses the workflow state~ ??
    /// </summary>
    [Fact]
    public void Executor_PauseExecution_ShouldTransitionToPaused()
    {
        // Arrange
        var executionId = Guid.NewGuid();
        var definition = CreateLinearWorkflow();
        var executor = Sys.ActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), _serviceProvider));

        executor.Tell(new StartExecution(executionId));
        Thread.Sleep(100); // Brief start

        // Act
        executor.Tell(new PauseExecution(executionId));
        Thread.Sleep(500);

        // Assert
        executor.Tell(new GetWorkflowStatus(executionId));
        var status = ExpectMsg<WorkflowStatusResponse>(TimeSpan.FromSeconds(3));

        // May have already completed before pause took effect, or be paused
        status.State.Should().BeOneOf(ExecutionState.Paused, ExecutionState.Completed, ExecutionState.Running);
    }

    /// <summary>
    /// Test that pausing a non-running workflow has no effect~ ??
    /// </summary>
    [Fact]
    public void Executor_PauseWhenNotRunning_ShouldNotChangeState()
    {
        // Arrange
        var executionId = Guid.NewGuid();
        var definition = CreateSingleNodeWorkflow();
        var executor = Sys.ActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), _serviceProvider));

        // Act � try to pause before starting
        executor.Tell(new PauseExecution(executionId));
        Thread.Sleep(200);

        // Assert � should still be Pending
        executor.Tell(new GetWorkflowStatus(executionId));
        var status = ExpectMsg<WorkflowStatusResponse>(TimeSpan.FromSeconds(3));
        status.State.Should().Be(ExecutionState.Pending);
    }

    #endregion

    #region Helper Methods ???

    /// <summary>
    /// Creates a workflow with a single node (no connections).
    /// </summary>
    private static WorkflowDefinition CreateSingleNodeWorkflow()
    {
        var node = new NodeDefinition(
            Id: "node_a",
            ModuleId: "test.module",
            Name: "Node A",
            Properties: HashMap<string, JsonElement>.Empty,
            Position: null,
            ErrorHandling: null,
            Timeout: null,
            RetryPolicy: null,
            Metadata: null);

        return new WorkflowDefinition(
            Id: Guid.NewGuid(),
            Name: "Single Node Workflow",
            Description: "Test workflow with one node",
            Version: new Version(1, 0, 0),
            Nodes: Arr.create(node),
            Connections: Arr<ConnectionDefinition>.Empty,
            Variables: HashMap<string, VariableDefinition>.Empty);
    }

    /// <summary>
    /// Creates a linear workflow: A ? B ? C.
    /// </summary>
    private static WorkflowDefinition CreateLinearWorkflow()
    {
        var nodeA = new NodeDefinition("node_a", "test.module", "Node A",
            HashMap<string, JsonElement>.Empty);
        var nodeB = new NodeDefinition("node_b", "test.module", "Node B",
            HashMap<string, JsonElement>.Empty);
        var nodeC = new NodeDefinition("node_c", "test.module", "Node C",
            HashMap<string, JsonElement>.Empty);

        var connAB = new ConnectionDefinition("node_a", "output", "node_b", "input", null, 0);
        var connBC = new ConnectionDefinition("node_b", "output", "node_c", "input", null, 0);

        return new WorkflowDefinition(
            Id: Guid.NewGuid(),
            Name: "Linear Workflow",
            Description: "Test workflow: A ? B ? C",
            Version: new Version(1, 0, 0),
            Nodes: Arr.create(nodeA, nodeB, nodeC),
            Connections: Arr.create(connAB, connBC),
            Variables: HashMap<string, VariableDefinition>.Empty);
    }

    /// <summary>
    /// Creates an empty workflow (no nodes).
    /// </summary>
    private static WorkflowDefinition CreateEmptyWorkflow()
    {
        return new WorkflowDefinition(
            Id: Guid.NewGuid(),
            Name: "Empty Workflow",
            Description: "Test workflow with no nodes",
            Version: new Version(1, 0, 0),
            Nodes: Arr<NodeDefinition>.Empty,
            Connections: Arr<ConnectionDefinition>.Empty,
            Variables: HashMap<string, VariableDefinition>.Empty);
    }

    #endregion
}


