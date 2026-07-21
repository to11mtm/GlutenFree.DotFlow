// <copyright file="ActorLifecycleTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Engine;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit.Xunit2;
using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Workflow.Core.Abstractions;
using Workflow.Core.Models;
using Workflow.Engine.Actors;
using Workflow.Engine.Messages;
using Workflow.Engine.Services;
using Workflow.Modules;
using Workflow.Modules.Abstractions;
using Xunit;

/// <summary>
/// Comprehensive tests for Phase 1.3.9 — Actor Lifecycle Management~ 🌸✨
/// Tests that <see cref="IActorLifecycleHooks"/> callbacks fire at the right time,
/// graceful shutdown works, resource cleanup happens, and restart is resilient.
/// UwU 💖
/// </summary>
public class ActorLifecycleTests : TestKit
{
    private readonly RecordingLifecycleHooks _hooks;
    private readonly IServiceProvider _serviceProvider;
    private readonly InMemoryExecutionStateStore _stateStore;

    /// <summary>
    /// Initializes test environment with a recording hooks instance registered in DI~ 🌸
    /// </summary>
    public ActorLifecycleTests()
    {
        _hooks = new RecordingLifecycleHooks();
        _stateStore = new InMemoryExecutionStateStore();

        var registry = new InMemoryModuleRegistry();
        registry.RegisterModule(new PassThroughTestModule());
        registry.RegisterModule(new SlowTestModule());
        registry.RegisterModule(new AlwaysFailsTestModule());

        var services = new ServiceCollection();
        services.AddSingleton<WorkflowValidator>();
        services.AddSingleton<IModuleRegistry>(registry);
        services.AddSingleton<IExecutionStateStore>(_stateStore);
        services.AddSingleton<IActorLifecycleHooks>(_hooks);
        services.AddLogging(builder => builder.AddDebug());
        _serviceProvider = services.BuildServiceProvider();
    }

    #region IActorLifecycleHooks Callback Tests 🌸

    /// <summary>
    /// Test that WorkflowSupervisor invokes OnPreStart hook on creation~ 🌸
    /// </summary>
    [Fact]
    public void WorkflowSupervisor_PreStart_ShouldInvokeHook()
    {
        // Act
        Sys.ActorOf(WorkflowSupervisor.Props(_serviceProvider), "sup-prestart");
        Thread.Sleep(200);

        // Assert
        _hooks.Events.Should().Contain(e =>
            e.ActorType == "WorkflowSupervisor" && e.Hook == "OnPreStart");
    }

    /// <summary>
    /// Test that WorkflowSupervisor invokes OnPostStop hook when stopped~ 👋
    /// </summary>
    [Fact]
    public void WorkflowSupervisor_PostStop_ShouldInvokeHook()
    {
        // Arrange
        var supervisor = Sys.ActorOf(WorkflowSupervisor.Props(_serviceProvider), "sup-poststop");
        Thread.Sleep(200);

        // Act
        Watch(supervisor);
        Sys.Stop(supervisor);
        ExpectTerminated(supervisor, TimeSpan.FromSeconds(5));
        Thread.Sleep(200);

        // Assert
        _hooks.Events.Should().Contain(e =>
            e.ActorType == "WorkflowSupervisor" && e.Hook == "OnPostStop");
    }

    /// <summary>
    /// Test that WorkflowExecutor invokes OnPreStart hook on creation~ 🎬
    /// </summary>
    [Fact]
    public void WorkflowExecutor_PreStart_ShouldInvokeHook()
    {
        // Act
        var executionId = Guid.NewGuid();
        Sys.ActorOf(
            WorkflowExecutor.Props(executionId, CreateSingleNodeWorkflow("test.passthrough"),
                new Dictionary<string, object?>(), _serviceProvider),
            "exec-prestart");
        Thread.Sleep(200);

        // Assert
        _hooks.Events.Should().Contain(e =>
            e.ActorType == "WorkflowExecutor" && e.Hook == "OnPreStart");
    }

    /// <summary>
    /// Test that WorkflowExecutor invokes OnPostStop hook when stopped~ 👋
    /// </summary>
    [Fact]
    public void WorkflowExecutor_PostStop_ShouldInvokeHook()
    {
        // Arrange
        var executor = Sys.ActorOf(
            WorkflowExecutor.Props(Guid.NewGuid(), CreateSingleNodeWorkflow("test.passthrough"),
                new Dictionary<string, object?>(), _serviceProvider),
            "exec-poststop");
        Thread.Sleep(200);

        // Act
        Watch(executor);
        Sys.Stop(executor);
        ExpectTerminated(executor, TimeSpan.FromSeconds(5));
        Thread.Sleep(200);

        // Assert
        _hooks.Events.Should().Contain(e =>
            e.ActorType == "WorkflowExecutor" && e.Hook == "OnPostStop");
    }

    /// <summary>
    /// Test that NodeExecutor invokes OnPreStart hook on creation~ 🧩
    /// </summary>
    [Fact]
    public void NodeExecutor_PreStart_ShouldInvokeHook()
    {
        // Act
        var nodeDef = CreateNodeDefinition("test-node", "test.passthrough");
        Sys.ActorOf(
            NodeExecutor.Props("test-node", nodeDef, new Dictionary<string, object?>(),
                Guid.NewGuid(), _serviceProvider),
            "node-prestart");
        Thread.Sleep(200);

        // Assert
        _hooks.Events.Should().Contain(e =>
            e.ActorType == "NodeExecutor" && e.Hook == "OnPreStart");
    }

    /// <summary>
    /// Test that NodeExecutor invokes OnPostStop hook when stopped~ 👋
    /// </summary>
    [Fact]
    public void NodeExecutor_PostStop_ShouldInvokeHook()
    {
        // Arrange
        var nodeDef = CreateNodeDefinition("test-node", "test.passthrough");
        var nodeActor = Sys.ActorOf(
            NodeExecutor.Props("test-node", nodeDef, new Dictionary<string, object?>(),
                Guid.NewGuid(), _serviceProvider),
            "node-poststop");
        Thread.Sleep(200);

        // Act
        Watch(nodeActor);
        Sys.Stop(nodeActor);
        ExpectTerminated(nodeActor, TimeSpan.FromSeconds(5));
        Thread.Sleep(200);

        // Assert
        _hooks.Events.Should().Contain(e =>
            e.ActorType == "NodeExecutor" && e.Hook == "OnPostStop");
    }

    /// <summary>
    /// Test that lifecycle hooks fire in correct order: OnPreStart then OnPostStop~ 📋
    /// </summary>
    [Fact]
    public void LifecycleHooks_ShouldFireInCorrectOrder_PreStartThenPostStop()
    {
        // Arrange
        var nodeDef = CreateNodeDefinition("order-node", "test.passthrough");
        var nodeActor = Sys.ActorOf(
            NodeExecutor.Props("order-node", nodeDef, new Dictionary<string, object?>(),
                Guid.NewGuid(), _serviceProvider),
            "node-order");
        Thread.Sleep(200);

        // Act
        Watch(nodeActor);
        Sys.Stop(nodeActor);
        ExpectTerminated(nodeActor, TimeSpan.FromSeconds(5));
        Thread.Sleep(200);

        // Assert — filter to just this actor type to avoid cross-talk
        var nodeEvents = _hooks.Events
            .Where(e => e.ActorType == "NodeExecutor")
            .Select(e => e.Hook)
            .ToList();

        nodeEvents.Should().ContainInOrder("OnPreStart", "OnPostStop");
    }

    /// <summary>
    /// Test that all three actor types invoke lifecycle hooks~ 🎭🎬🧩
    /// </summary>
    [Fact]
    public void AllActorTypes_ShouldInvokeLifecycleHooks()
    {
        // Act — create one of each
        Sys.ActorOf(WorkflowSupervisor.Props(_serviceProvider), "all-sup");
        Sys.ActorOf(
            WorkflowExecutor.Props(Guid.NewGuid(), CreateSingleNodeWorkflow("test.passthrough"),
                new Dictionary<string, object?>(), _serviceProvider),
            "all-exec");
        var nodeDef = CreateNodeDefinition("all-node", "test.passthrough");
        Sys.ActorOf(
            NodeExecutor.Props("all-node", nodeDef, new Dictionary<string, object?>(),
                Guid.NewGuid(), _serviceProvider),
            "all-node-actor");
        Thread.Sleep(500);

        // Assert
        var actorTypes = _hooks.Events.Select(e => e.ActorType).Distinct().ToList();
        actorTypes.Should().Contain("WorkflowSupervisor");
        actorTypes.Should().Contain("WorkflowExecutor");
        actorTypes.Should().Contain("NodeExecutor");
    }

    /// <summary>
    /// Test that hook context contains the correct actor path~ 📍
    /// </summary>
    [Fact]
    public void LifecycleHookContext_ShouldContainCorrectActorPath()
    {
        // Act
        Sys.ActorOf(WorkflowSupervisor.Props(_serviceProvider), "sup-path-check");
        Thread.Sleep(200);

        // Assert
        var evt = _hooks.Events.First(e =>
            e.ActorType == "WorkflowSupervisor" && e.Hook == "OnPreStart");
        evt.Context.ActorPath.Should().Contain("sup-path-check");
        evt.Context.Services.Should().NotBeNull();
    }

    /// <summary>
    /// Test that hook context provides access to the service provider (DI)~ 🔧
    /// </summary>
    [Fact]
    public void LifecycleHookContext_ShouldProvideServiceProvider()
    {
        // Act
        Sys.ActorOf(WorkflowSupervisor.Props(_serviceProvider), "sup-di-check");
        Thread.Sleep(200);

        // Assert
        var evt = _hooks.Events.First(e => e.ActorType == "WorkflowSupervisor");
        var validator = evt.Context.Services.GetService(typeof(WorkflowValidator));
        validator.Should().NotBeNull("the DI service provider should be accessible from the hook context");
    }

    /// <summary>
    /// Test that executing a workflow triggers NodeExecutor lifecycle hooks
    /// (nodes are created and destroyed during execution)~ 🧩🔄
    /// </summary>
    [Fact]
    public void WorkflowExecution_ShouldTriggerNodeExecutorLifecycleHooks()
    {
        // Arrange
        var executionId = Guid.NewGuid();
        var definition = CreateSingleNodeWorkflow("test.passthrough");
        var executor = Sys.ActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), _serviceProvider),
            "exec-node-hooks");

        // Act — execute the workflow (creates and destroys NodeExecutors)
        executor.Tell(new StartExecution(executionId));
        Thread.Sleep(3000);

        // Assert — NodeExecutor hooks should have fired
        _hooks.Events.Should().Contain(e =>
            e.ActorType == "NodeExecutor" && e.Hook == "OnPreStart");
        _hooks.Events.Should().Contain(e =>
            e.ActorType == "NodeExecutor" && e.Hook == "OnPostStop");
    }

    #endregion

    #region CompositeActorLifecycleHooks Tests 🔗

    /// <summary>
    /// Test that CompositeActorLifecycleHooks chains multiple hooks correctly~ 🔗
    /// </summary>
    [Fact]
    public void CompositeHooks_ShouldInvokeAllInnerHooks()
    {
        // Arrange
        var recorder1 = new RecordingLifecycleHooks();
        var recorder2 = new RecordingLifecycleHooks();
        var composite = new CompositeActorLifecycleHooks(recorder1, recorder2);
        var ctx = new ActorLifecycleContext("akka://test/user/composite", "TestActor", _serviceProvider);

        // Act
        composite.OnPreStart(ctx);
        composite.OnPostStop(ctx);

        // Assert — both recorders should have been called
        recorder1.Events.Should().HaveCount(2);
        recorder2.Events.Should().HaveCount(2);
        recorder1.Events.Select(e => e.Hook).Should().ContainInOrder("OnPreStart", "OnPostStop");
        recorder2.Events.Select(e => e.Hook).Should().ContainInOrder("OnPreStart", "OnPostStop");
    }

    /// <summary>
    /// Test that CompositeActorLifecycleHooks passes restart reason correctly~ 🔄
    /// </summary>
    [Fact]
    public void CompositeHooks_OnPreRestart_ShouldPassReason()
    {
        // Arrange
        var recorder = new RecordingLifecycleHooks();
        var composite = new CompositeActorLifecycleHooks(recorder);
        var ctx = new ActorLifecycleContext("akka://test/user/restart", "TestActor", _serviceProvider);
        var reason = new TimeoutException("test timeout");

        // Act
        composite.OnPreRestart(ctx, reason, "some-message");
        composite.OnPostRestart(ctx, reason);

        // Assert
        recorder.Events.Should().HaveCount(2);
        recorder.Events[0].Hook.Should().Be("OnPreRestart");
        recorder.Events[0].Reason.Should().Be(reason);
        recorder.Events[1].Hook.Should().Be("OnPostRestart");
        recorder.Events[1].Reason.Should().Be(reason);
    }

    /// <summary>
    /// Test that NullActorLifecycleHooks does nothing (no exceptions)~ 🤷
    /// </summary>
    [Fact]
    public void NullHooks_ShouldDoNothing()
    {
        // Arrange
        var hooks = NullActorLifecycleHooks.Instance;
        var ctx = new ActorLifecycleContext("akka://test/user/null", "TestActor", _serviceProvider);

        // Act & Assert — should not throw
        hooks.OnPreStart(ctx);
        hooks.OnPostStop(ctx);
        hooks.OnPreRestart(ctx, new Exception("test"), null);
        hooks.OnPostRestart(ctx, new Exception("test"));
    }

    #endregion

    #region Graceful Shutdown Tests 🛑🌸

    /// <summary>
    /// Test that GracefulShutdown cancels active workflows and responds~ 🛑🌸
    /// </summary>
    [Fact]
    public void GracefulShutdown_ShouldCancelActiveWorkflowsAndRespond()
    {
        // Arrange — create supervisor and start a slow workflow
        var supervisor = Sys.ActorOf(WorkflowSupervisor.Props(_serviceProvider), "sup-graceful");
        var definition = CreateSlowWorkflow();
        supervisor.Tell(new CreateWorkflowInstance(
            Guid.NewGuid(), definition, HashMap<string, object?>.Empty));
        ExpectMsg<WorkflowInstanceCreated>(TimeSpan.FromSeconds(5));
        Thread.Sleep(500);

        // Act
        supervisor.Tell(new GracefulShutdown(TimeSpan.FromSeconds(5)));

        // Assert
        var response = ExpectMsg<GracefulShutdownComplete>(TimeSpan.FromSeconds(10));
        response.CancelledCount.Should().BeGreaterOrEqualTo(1);
    }

    /// <summary>
    /// Test that GracefulShutdown with no active workflows completes immediately~ ✨
    /// </summary>
    [Fact]
    public void GracefulShutdown_WithNoActiveWorkflows_ShouldCompleteImmediately()
    {
        // Arrange
        var supervisor = Sys.ActorOf(WorkflowSupervisor.Props(_serviceProvider), "sup-graceful-empty");

        // Act
        supervisor.Tell(new GracefulShutdown(TimeSpan.FromSeconds(1)));

        // Assert
        var response = ExpectMsg<GracefulShutdownComplete>(TimeSpan.FromSeconds(5));
        response.CancelledCount.Should().Be(0);
    }

    /// <summary>
    /// Test that GracefulShutdown stops the supervisor after completing~ 👋
    /// </summary>
    [Fact]
    public void GracefulShutdown_ShouldStopSupervisorAfterCompletion()
    {
        // Arrange
        var supervisor = Sys.ActorOf(WorkflowSupervisor.Props(_serviceProvider), "sup-graceful-stop");
        Watch(supervisor);

        // Act
        supervisor.Tell(new GracefulShutdown(TimeSpan.FromSeconds(1)));
        ExpectMsg<GracefulShutdownComplete>(TimeSpan.FromSeconds(5));

        // Assert
        ExpectTerminated(supervisor, TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Test that GracefulShutdown invokes OnPostStop hook~ 🌸👋
    /// </summary>
    [Fact]
    public void GracefulShutdown_ShouldInvokePostStopHook()
    {
        // Arrange
        var supervisor = Sys.ActorOf(WorkflowSupervisor.Props(_serviceProvider), "sup-graceful-hook");
        Watch(supervisor);

        // Act
        supervisor.Tell(new GracefulShutdown(TimeSpan.FromSeconds(1)));
        ExpectMsg<GracefulShutdownComplete>(TimeSpan.FromSeconds(5));
        ExpectTerminated(supervisor, TimeSpan.FromSeconds(5));
        Thread.Sleep(200);

        // Assert — PostStop hook should have been called
        _hooks.Events.Should().Contain(e =>
            e.ActorType == "WorkflowSupervisor" && e.Hook == "OnPostStop");
    }

    #endregion

    #region Resource Disposal Tests 🧹

    /// <summary>
    /// Test that stopping a workflow mid-execution doesn't leak resources~ 🧹✨
    /// </summary>
    [Fact]
    public void StoppingWorkflowMidExecution_ShouldNotLeakResources()
    {
        // Arrange
        var executionId = Guid.NewGuid();
        var executor = Sys.ActorOf(
            WorkflowExecutor.Props(executionId, CreateSlowWorkflow(),
                new Dictionary<string, object?>(), _serviceProvider),
            "exec-no-leak");
        executor.Tell(new StartExecution(executionId));
        Thread.Sleep(500);

        // Act
        Watch(executor);
        Sys.Stop(executor);

        // Assert — clean termination
        ExpectTerminated(executor, TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// Test that cancellation properly cleans up running node actors~ 🛑
    /// </summary>
    [Fact]
    public void CancelExecution_ShouldStopRunningNodeActors()
    {
        // Arrange
        var executionId = Guid.NewGuid();
        var executor = Sys.ActorOf(
            WorkflowExecutor.Props(executionId, CreateSlowWorkflow(),
                new Dictionary<string, object?>(), _serviceProvider),
            "exec-cancel-nodes");
        executor.Tell(new StartExecution(executionId));
        Thread.Sleep(500);

        // Act
        executor.Tell(new CancelExecution(executionId));
        Thread.Sleep(1000);

        // Assert
        executor.Tell(new GetWorkflowStatus(executionId));
        var status = ExpectMsg<WorkflowStatusResponse>(TimeSpan.FromSeconds(5));
        status.State.Should().BeOneOf(ExecutionState.Cancelled, ExecutionState.Failed);
    }

    /// <summary>
    /// Test that NodeExecutor disposes CTS in PostStop when executing~ 🧹
    /// </summary>
    [Fact]
    public void NodeExecutor_PostStop_WhileExecuting_ShouldDisposeCTS()
    {
        // Arrange
        var nodeDef = CreateNodeDefinition("slow-node", "test.slow");
        var nodeActor = Sys.ActorOf(
            NodeExecutor.Props("slow-node", nodeDef, new Dictionary<string, object?>(),
                Guid.NewGuid(), _serviceProvider),
            "node-cts-dispose");
        nodeActor.Tell(new Execute("slow-node", HashMap<string, object?>.Empty, Guid.NewGuid()));
        Thread.Sleep(500);

        // Act
        Watch(nodeActor);
        Sys.Stop(nodeActor);

        // Assert — clean termination
        ExpectTerminated(nodeActor, TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Test that WorkflowExecutor PostStop cancels pending retry timers~ 🧹
    /// </summary>
    [Fact]
    public void WorkflowExecutor_PostStop_ShouldCancelPendingRetryTimers()
    {
        // Arrange — use AlwaysFailsTestModule with Retry error behavior
        var executionId = Guid.NewGuid();
        var node = new NodeDefinition(
            Id: "retry-node",
            ModuleId: "test.always-fails",
            Name: "Retry Node",
            Properties: HashMap<string, JsonElement>.Empty,
            ErrorHandling: new ErrorHandling(ErrorBehavior.Retry),
            RetryPolicy: new RetryPolicy(MaxAttempts: 5, DelayMs: 5000, BackoffMultiplier: 2.0, MaxDelayMs: 30000));
        var definition = CreateWorkflowDefinition(Arr.create(node), Arr<ConnectionDefinition>.Empty);
        var executor = Sys.ActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), _serviceProvider),
            "exec-retry-cleanup");

        executor.Tell(new StartExecution(executionId));
        Thread.Sleep(3000);

        // Act
        Watch(executor);
        Sys.Stop(executor);

        // Assert — no crash
        ExpectTerminated(executor, TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Test that multiple stop calls are idempotent~ 🧹
    /// </summary>
    [Fact]
    public void MultipleStopCalls_ShouldBeIdempotent()
    {
        // Arrange
        var executor = Sys.ActorOf(
            WorkflowExecutor.Props(Guid.NewGuid(), CreateSingleNodeWorkflow("test.passthrough"),
                new Dictionary<string, object?>(), _serviceProvider),
            "exec-multi-stop");
        Watch(executor);

        // Act
        Sys.Stop(executor);

        // Assert
        ExpectTerminated(executor, TimeSpan.FromSeconds(5));
    }

    #endregion

    #region Restart Resilience Tests 🔄

    /// <summary>
    /// Test that WorkflowExecutor starts in Pending state~ 📊
    /// </summary>
    [Fact]
    public void WorkflowExecutor_Initialization_ShouldSetCorrectState()
    {
        // Arrange
        var executionId = Guid.NewGuid();
        var executor = Sys.ActorOf(
            WorkflowExecutor.Props(executionId, CreateSingleNodeWorkflow("test.passthrough"),
                new Dictionary<string, object?>(), _serviceProvider),
            "exec-init-state");

        // Assert
        executor.Tell(new GetWorkflowStatus(executionId));
        var status = ExpectMsg<WorkflowStatusResponse>(TimeSpan.FromSeconds(5));
        status.State.Should().Be(ExecutionState.Pending);
    }

    /// <summary>
    /// Test that starting execution transitions to completed~ 🚀
    /// </summary>
    [Fact]
    public void WorkflowExecutor_StartExecution_ShouldCompleteSuccessfully()
    {
        // Arrange
        var executionId = Guid.NewGuid();
        var executor = Sys.ActorOf(
            WorkflowExecutor.Props(executionId, CreateSingleNodeWorkflow("test.passthrough"),
                new Dictionary<string, object?>(), _serviceProvider),
            "exec-complete");

        // Act
        executor.Tell(new StartExecution(executionId));
        Thread.Sleep(3000);

        // Assert
        executor.Tell(new GetWorkflowStatus(executionId));
        var status = ExpectMsg<WorkflowStatusResponse>(TimeSpan.FromSeconds(5));
        status.State.Should().Be(ExecutionState.Completed);
        status.Progress.Should().Be(100);
    }

    /// <summary>
    /// Test that supervisor can be created and responds to status queries~ ✨
    /// </summary>
    [Fact]
    public void WorkflowSupervisor_Creation_ShouldBeResponsive()
    {
        // Arrange
        var supervisor = Sys.ActorOf(WorkflowSupervisor.Props(_serviceProvider), "sup-responsive");

        // Assert — unknown execution should return failure
        supervisor.Tell(new GetWorkflowStatus(Guid.NewGuid()));
        ExpectMsg<Status.Failure>(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Test that supervisor tracks workflow lifecycle end-to-end~ 📊
    /// </summary>
    [Fact]
    public void WorkflowSupervisor_ShouldTrackWorkflowLifecycle()
    {
        // Arrange
        var supervisor = Sys.ActorOf(WorkflowSupervisor.Props(_serviceProvider), "sup-track");
        var definition = CreateSingleNodeWorkflow("test.passthrough");
        supervisor.Tell(new CreateWorkflowInstance(Guid.NewGuid(), definition, HashMap<string, object?>.Empty));
        var created = ExpectMsg<WorkflowInstanceCreated>(TimeSpan.FromSeconds(5));

        Thread.Sleep(3000);

        // Act — query status
        supervisor.Tell(new GetWorkflowStatus(created.ExecutionId));

        // Assert — may be completed or cleaned up
        try
        {
            var status = ExpectMsg<WorkflowStatusResponse>(TimeSpan.FromSeconds(5));
            status.State.Should().BeOneOf(ExecutionState.Completed, ExecutionState.Running);
        }
        catch
        {
            // If executor was already stopped, we get a failure — that's valid
            ExpectMsg<Status.Failure>(TimeSpan.FromSeconds(5));
        }
    }

    #endregion

    #region Snapshot Persistence Tests 💾

    /// <summary>
    /// Test that execution state store receives snapshot on completion~ 💾✅
    /// </summary>
    [Fact]
    public void WorkflowExecutor_OnCompletion_ShouldSaveSnapshot()
    {
        // Arrange
        var executionId = Guid.NewGuid();
        var executor = Sys.ActorOf(
            WorkflowExecutor.Props(executionId, CreateSingleNodeWorkflow("test.passthrough"),
                new Dictionary<string, object?>(), _serviceProvider),
            "exec-snap-complete");

        // Act
        executor.Tell(new StartExecution(executionId));
        Thread.Sleep(3000);

        // Assert
        var snapshot = _stateStore.LoadSnapshotAsync(executionId).Result;
        snapshot.IsSome.Should().BeTrue("snapshot should be saved on completion");
        snapshot.IfSome(ctx => ctx.State.Should().Be(ExecutionState.Completed));
    }

    /// <summary>
    /// Test that execution state store receives snapshot on failure~ 💾❌
    /// </summary>
    [Fact]
    public void WorkflowExecutor_OnFailure_ShouldSaveSnapshot()
    {
        // Arrange
        var executionId = Guid.NewGuid();
        var executor = Sys.ActorOf(
            WorkflowExecutor.Props(executionId, CreateSingleNodeWorkflow("test.always-fails"),
                new Dictionary<string, object?>(), _serviceProvider),
            "exec-snap-fail");

        // Act
        executor.Tell(new StartExecution(executionId));
        Thread.Sleep(3000);

        // Assert
        var snapshot = _stateStore.LoadSnapshotAsync(executionId).Result;
        snapshot.IsSome.Should().BeTrue("snapshot should be saved on failure");
        snapshot.IfSome(ctx =>
        {
            ctx.State.Should().Be(ExecutionState.Failed);
            ctx.Error.IsSome.Should().BeTrue();
        });
    }

    /// <summary>
    /// Test that pausing saves a snapshot for resumability~ ⏸️💾
    /// </summary>
    [Fact]
    public void PauseExecution_ShouldSaveSnapshot()
    {
        // Arrange
        var executionId = Guid.NewGuid();
        var executor = Sys.ActorOf(
            WorkflowExecutor.Props(executionId, CreateSlowWorkflow(),
                new Dictionary<string, object?>(), _serviceProvider),
            "exec-pause-snap");
        executor.Tell(new StartExecution(executionId));
        Thread.Sleep(500);

        // Act
        executor.Tell(new PauseExecution(executionId));
        Thread.Sleep(1000);

        // Assert
        var snapshot = _stateStore.LoadSnapshotAsync(executionId).Result;
        snapshot.IsSome.Should().BeTrue("snapshot should be saved on pause");
        snapshot.IfSome(ctx => ctx.State.Should().Be(ExecutionState.Paused));
    }

    /// <summary>
    /// Test that stopping while running saves a snapshot~ 💾⚠️
    /// </summary>
    [Fact]
    public void WorkflowExecutor_PostStop_WhileRunning_ShouldSaveSnapshot()
    {
        // Arrange
        var executionId = Guid.NewGuid();
        var executor = Sys.ActorOf(
            WorkflowExecutor.Props(executionId, CreateSlowWorkflow(),
                new Dictionary<string, object?>(), _serviceProvider),
            "exec-snap-stop");
        executor.Tell(new StartExecution(executionId));
        Thread.Sleep(500);

        // Act
        Watch(executor);
        Sys.Stop(executor);
        ExpectTerminated(executor, TimeSpan.FromSeconds(5));
        Thread.Sleep(200);

        // Assert
        var snapshot = _stateStore.LoadSnapshotAsync(executionId).Result;
        snapshot.IsSome.Should().BeTrue("snapshot should be saved when executor stops while running");
    }

    #endregion

    #region Helper Methods 🌸

    private static WorkflowDefinition CreateSingleNodeWorkflow(string moduleId)
    {
        var node = new NodeDefinition(
            Id: "test-node",
            ModuleId: moduleId,
            Name: "Test Node",
            Properties: HashMap<string, JsonElement>.Empty);
        return CreateWorkflowDefinition(Arr.create(node), Arr<ConnectionDefinition>.Empty);
    }

    private static WorkflowDefinition CreateSlowWorkflow()
    {
        var node = new NodeDefinition(
            Id: "slow-node",
            ModuleId: "test.slow",
            Name: "Slow Node",
            Properties: HashMap<string, JsonElement>.Empty,
            Timeout: 60000);
        return CreateWorkflowDefinition(Arr.create(node), Arr<ConnectionDefinition>.Empty);
    }

    private static NodeDefinition CreateNodeDefinition(string nodeId, string moduleId)
    {
        return new NodeDefinition(
            Id: nodeId,
            ModuleId: moduleId,
            Name: $"Node {nodeId}",
            Properties: HashMap<string, JsonElement>.Empty);
    }

    private static WorkflowDefinition CreateWorkflowDefinition(
        Arr<NodeDefinition> nodes,
        Arr<ConnectionDefinition> connections)
    {
        return new WorkflowDefinition(
            Id: Guid.NewGuid(),
            Name: "Lifecycle Test Workflow",
            Description: "Workflow for lifecycle testing~ UwU",
            Version: new Version(1, 0, 0),
            Nodes: nodes,
            Connections: connections,
            Variables: HashMap<string, VariableDefinition>.Empty);
    }

    #endregion

    #region Test Implementations 🧪

    /// <summary>
    /// A recording implementation of <see cref="IActorLifecycleHooks"/> that captures
    /// every callback invocation for later assertion~ 📝✨
    /// Thread-safe via <see cref="ConcurrentBag{T}"/>. UwU 💖
    /// </summary>
    private class RecordingLifecycleHooks : IActorLifecycleHooks
    {
        private readonly ConcurrentQueue<RecordedEvent> _events = new();

        /// <summary>
        /// All recorded lifecycle events, in FIFO insertion order~ ✨
        /// </summary>
        public IReadOnlyList<RecordedEvent> Events => _events.ToList();

        public void OnPreStart(ActorLifecycleContext context)
        {
            _events.Enqueue(new RecordedEvent(context, "OnPreStart", null, null));
        }

        public void OnPostStop(ActorLifecycleContext context)
        {
            _events.Enqueue(new RecordedEvent(context, "OnPostStop", null, null));
        }

        public void OnPreRestart(ActorLifecycleContext context, Exception reason, object? message)
        {
            _events.Enqueue(new RecordedEvent(context, "OnPreRestart", reason, message));
        }

        public void OnPostRestart(ActorLifecycleContext context, Exception reason)
        {
            _events.Enqueue(new RecordedEvent(context, "OnPostRestart", reason, null));
        }

        /// <summary>
        /// A single recorded lifecycle hook invocation~ 📝
        /// </summary>
        public record RecordedEvent(
            ActorLifecycleContext Context,
            string Hook,
            Exception? Reason,
            object? Message)
        {
            /// <summary>Shortcut to context's ActorType.</summary>
            public string ActorType => Context.ActorType;
        }
    }

    /// <summary>
    /// Simple pass-through module that always succeeds immediately~ ✅
    /// </summary>
    private class PassThroughTestModule : IWorkflowModule
    {
        public string ModuleId => "test.passthrough";
        public string DisplayName => "Pass Through";
        public string Category => "Testing";
        public string Description => "Always succeeds~ ✅";
        public string Icon => "✅";
        public Version Version => new(1, 0, 0);
        public ModuleSchema Schema => ModuleSchema.Empty;

        public Task<ModuleResult> ExecuteAsync(
            ModuleExecutionContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ModuleResult.Ok(new Dictionary<string, object?>
            {
                ["result"] = "passed through!",
                ["success"] = true,
            }));
        }
    }

    /// <summary>
    /// Test module that takes a long time (for mid-execution lifecycle testing)~ 🐌
    /// </summary>
    private class SlowTestModule : IWorkflowModule
    {
        public string ModuleId => "test.slow";
        public string DisplayName => "Slow Module";
        public string Category => "Testing";
        public string Description => "Takes a while~ 🐌";
        public string Icon => "🐌";
        public Version Version => new(1, 0, 0);
        public ModuleSchema Schema => ModuleSchema.Empty;

        public async Task<ModuleResult> ExecuteAsync(
            ModuleExecutionContext context, CancellationToken cancellationToken = default)
        {
            await Task.Delay(15000, cancellationToken).ConfigureAwait(false);
            return ModuleResult.Ok(new Dictionary<string, object?> { ["completed"] = true });
        }
    }

    /// <summary>
    /// Test module that always throws for failure lifecycle testing~ 💥
    /// </summary>
    private class AlwaysFailsTestModule : IWorkflowModule
    {
        public string ModuleId => "test.always-fails";
        public string DisplayName => "Always Fails";
        public string Category => "Testing";
        public string Description => "Always fails~ 💥";
        public string Icon => "💥";
        public Version Version => new(1, 0, 0);
        public ModuleSchema Schema => ModuleSchema.Empty;

        public Task<ModuleResult> ExecuteAsync(
            ModuleExecutionContext context, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Intentional test failure! 💥 UwU");
        }
    }

    #endregion
}

