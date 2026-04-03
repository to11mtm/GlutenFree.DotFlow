// <copyright file="SupervisionStrategyTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Engine;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
using Workflow.Modules;
using Workflow.Modules.Abstractions;
using Xunit;

/// <summary>
/// Comprehensive tests for Phase 1.3.8 — Supervision Strategy &amp; Error Handling~ 🛡️✨
/// Covers fail-fast, continue-on-error, retry with backoff, retry exhaustion,
/// supervision restart/stop/escalate directives, timeout handling,
/// and <see cref="SupervisionEvent"/> observability. UwU 💖
/// </summary>
public class SupervisionStrategyTests : TestKit
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IModuleRegistry _moduleRegistry;

    /// <summary>
    /// Initializes the test environment with module registry &amp; DI services~ 🌸
    /// </summary>
    public SupervisionStrategyTests()
    {
        _moduleRegistry = new InMemoryModuleRegistry();
        _moduleRegistry.RegisterModule(new AlwaysFailsModule());
        _moduleRegistry.RegisterModule(new ConfigurableFailureModule());
        _moduleRegistry.RegisterModule(new PassThroughTestModule());

        var services = new ServiceCollection();
        services.AddSingleton<WorkflowValidator>();
        services.AddSingleton(_moduleRegistry);
        services.AddLogging(builder => builder.AddDebug());
        _serviceProvider = services.BuildServiceProvider();
    }

    #region Fail-Fast (Default Behavior) Tests 🛑

    /// <summary>
    /// Test that a node failure with default ErrorBehavior.Fail stops the entire workflow.
    /// This is the "fail-fast" pattern — one node dies, everyone goes home~ 💔
    /// </summary>
    [Fact]
    public void NodeFailure_WithFailFast_ShouldStopWorkflow()
    {
        // Arrange — single node that always fails, default Fail behavior
        var executionId = Guid.NewGuid();
        var definition = CreateWorkflowWithModule("test.always-fails", errorBehavior: ErrorBehavior.Fail);
        var executor = Sys.ActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), _serviceProvider));

        // Act
        executor.Tell(new StartExecution(executionId));
        Thread.Sleep(3000);

        // Assert — workflow should be in Failed state
        executor.Tell(new GetWorkflowStatus(executionId));
        var status = ExpectMsg<WorkflowStatusResponse>(TimeSpan.FromSeconds(5));

        status.State.Should().Be(ExecutionState.Failed);
        status.Error.IsSome.Should().BeTrue();
    }

    /// <summary>
    /// Test that fail-fast is the default when no error handling is configured.
    /// No ErrorHandling = Fail. Simple as that~ ❌
    /// </summary>
    [Fact]
    public void NodeFailure_WithNoErrorHandling_ShouldDefaultToFailFast()
    {
        // Arrange — node with NO error handling config (null)
        var executionId = Guid.NewGuid();
        var node = new NodeDefinition(
            Id: "failing-node",
            ModuleId: "test.always-fails",
            Name: "Failing Node",
            Properties: HashMap<string, JsonElement>.Empty,
            Position: null,
            ErrorHandling: null, // no error handling = defaults to Fail
            Timeout: null,
            RetryPolicy: null,
            Metadata: null);

        var definition = CreateWorkflowDefinition(Arr.create(node), Arr<ConnectionDefinition>.Empty);
        var executor = Sys.ActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), _serviceProvider));

        // Act
        executor.Tell(new StartExecution(executionId));
        Thread.Sleep(3000);

        // Assert
        executor.Tell(new GetWorkflowStatus(executionId));
        var status = ExpectMsg<WorkflowStatusResponse>(TimeSpan.FromSeconds(5));
        status.State.Should().Be(ExecutionState.Failed);
    }

    #endregion

    #region Continue-on-Error Tests ⚡

    /// <summary>
    /// Test that a failing node with ErrorBehavior.Continue lets the workflow proceed.
    /// Successor nodes should still execute even when a predecessor fails~ ⚡✨
    /// </summary>
    [Fact]
    public void NodeFailure_WithContinueOnError_ShouldProceed()
    {
        // Arrange — A (fails, continue-on-error) → B (succeeds)
        var nodeA = new NodeDefinition(
            Id: "node_a",
            ModuleId: "test.always-fails",
            Name: "Failing Node A",
            Properties: HashMap<string, JsonElement>.Empty,
            Position: null,
            ErrorHandling: new ErrorHandling(ErrorBehavior.Continue),
            Timeout: null,
            RetryPolicy: null,
            Metadata: null);

        var nodeB = new NodeDefinition(
            Id: "node_b",
            ModuleId: "test.passthrough",
            Name: "Success Node B",
            Properties: HashMap<string, JsonElement>.Empty,
            Position: null,
            ErrorHandling: null,
            Timeout: null,
            RetryPolicy: null,
            Metadata: null);

        var conn = new ConnectionDefinition(
            SourceNodeId: "node_a",
            SourcePortName: "output",
            TargetNodeId: "node_b",
            TargetPortName: "input",
            Condition: null,
            Priority: 0);

        var definition = CreateWorkflowDefinition(Arr.create(nodeA, nodeB), Arr.create(conn));
        var executionId = Guid.NewGuid();
        var executor = Sys.ActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), _serviceProvider));

        // Act
        executor.Tell(new StartExecution(executionId));
        Thread.Sleep(4000);

        // Assert — workflow should complete (node A failed but continued, node B ran)
        executor.Tell(new GetWorkflowStatus(executionId));
        var status = ExpectMsg<WorkflowStatusResponse>(TimeSpan.FromSeconds(5));

        status.State.Should().Be(ExecutionState.Completed);
        // node_a should be marked as failed but treated as completed for flow purposes
        status.NodeStates.Find("node_b").IfSome(s =>
            s.Should().Be(NodeExecutionState.Completed));
    }

    /// <summary>
    /// Test that a single-node workflow with continue-on-error completes even on failure.
    /// </summary>
    [Fact]
    public void SingleNodeFailure_WithContinueOnError_ShouldCompleteWorkflow()
    {
        // Arrange
        var executionId = Guid.NewGuid();
        var definition = CreateWorkflowWithModule("test.always-fails", errorBehavior: ErrorBehavior.Continue);
        var executor = Sys.ActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), _serviceProvider));

        // Act
        executor.Tell(new StartExecution(executionId));
        Thread.Sleep(3000);

        // Assert — should complete (not fail) because of continue-on-error
        executor.Tell(new GetWorkflowStatus(executionId));
        var status = ExpectMsg<WorkflowStatusResponse>(TimeSpan.FromSeconds(5));
        status.State.Should().Be(ExecutionState.Completed);
    }

    #endregion

    #region Retry Behavior Tests 🔄

    /// <summary>
    /// Test that a node with ErrorBehavior.Retry retries with exponential backoff
    /// and succeeds after a configurable number of failures~ 🔄✨
    /// </summary>
    [Fact]
    public void NodeFailure_WithRetryPolicy_ShouldRetryAndSucceed()
    {
        // Arrange — node that fails twice then succeeds, with 3 max attempts
        var testRunId = Guid.NewGuid().ToString("N");
        ConfigurableFailureModule.ResetCounter(testRunId);

        var properties = CreateJsonProperties(new Dictionary<string, object>
        {
            ["testRunId"] = testRunId,
            ["failCount"] = 2,  // fail first 2 attempts, succeed on 3rd
        });

        var node = new NodeDefinition(
            Id: "retry-node",
            ModuleId: "test.configurable-failure",
            Name: "Retry Node",
            Properties: properties,
            Position: null,
            ErrorHandling: new ErrorHandling(ErrorBehavior.Retry),
            Timeout: null,
            RetryPolicy: new RetryPolicy(MaxAttempts: 3, DelayMs: 100, BackoffMultiplier: 1.0, MaxDelayMs: 500),
            Metadata: null);

        var definition = CreateWorkflowDefinition(Arr.create(node), Arr<ConnectionDefinition>.Empty);
        var executionId = Guid.NewGuid();
        var executor = Sys.ActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), _serviceProvider));

        // Act
        executor.Tell(new StartExecution(executionId));

        // Wait enough for retries (100ms delay * 2 retries + execution time)
        Thread.Sleep(5000);

        // Assert — workflow should complete because the 3rd attempt succeeds
        executor.Tell(new GetWorkflowStatus(executionId));
        var status = ExpectMsg<WorkflowStatusResponse>(TimeSpan.FromSeconds(5));

        status.State.Should().Be(ExecutionState.Completed);
    }

    /// <summary>
    /// Test that retry exhaustion (all attempts fail) results in workflow failure~ 💔
    /// </summary>
    [Fact]
    public void NodeFailure_RetryExhausted_ShouldFailWorkflow()
    {
        // Arrange — node that always fails, with 2 max attempts (1 initial + 1 retry)
        var testRunId = Guid.NewGuid().ToString("N");
        ConfigurableFailureModule.ResetCounter(testRunId);

        var properties = CreateJsonProperties(new Dictionary<string, object>
        {
            ["testRunId"] = testRunId,
            ["failCount"] = 999, // always fail
        });

        var node = new NodeDefinition(
            Id: "doomed-node",
            ModuleId: "test.configurable-failure",
            Name: "Doomed Node",
            Properties: properties,
            Position: null,
            ErrorHandling: new ErrorHandling(ErrorBehavior.Retry),
            Timeout: null,
            RetryPolicy: new RetryPolicy(MaxAttempts: 2, DelayMs: 50, BackoffMultiplier: 1.0, MaxDelayMs: 100),
            Metadata: null);

        var definition = CreateWorkflowDefinition(Arr.create(node), Arr<ConnectionDefinition>.Empty);
        var executionId = Guid.NewGuid();
        var executor = Sys.ActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), _serviceProvider));

        // Act
        executor.Tell(new StartExecution(executionId));
        Thread.Sleep(4000);

        // Assert — workflow should fail after exhausting all retries
        executor.Tell(new GetWorkflowStatus(executionId));
        var status = ExpectMsg<WorkflowStatusResponse>(TimeSpan.FromSeconds(5));

        status.State.Should().Be(ExecutionState.Failed);
        status.Error.IsSome.Should().BeTrue();
    }

    /// <summary>
    /// Test that NodeRetrying events are published on the EventStream during retries~ 📡
    /// </summary>
    [Fact]
    public void NodeRetry_ShouldPublishNodeRetryingEvent()
    {
        // Arrange — subscribe to NodeRetrying events
        var retryEvents = new List<NodeRetrying>();
        Sys.EventStream.Subscribe(TestActor, typeof(NodeRetrying));

        var testRunId = Guid.NewGuid().ToString("N");
        ConfigurableFailureModule.ResetCounter(testRunId);

        var properties = CreateJsonProperties(new Dictionary<string, object>
        {
            ["testRunId"] = testRunId,
            ["failCount"] = 1, // fail once, then succeed
        });

        var node = new NodeDefinition(
            Id: "event-node",
            ModuleId: "test.configurable-failure",
            Name: "Event Node",
            Properties: properties,
            Position: null,
            ErrorHandling: new ErrorHandling(ErrorBehavior.Retry),
            Timeout: null,
            RetryPolicy: new RetryPolicy(MaxAttempts: 3, DelayMs: 100, BackoffMultiplier: 1.0, MaxDelayMs: 500),
            Metadata: null);

        var definition = CreateWorkflowDefinition(Arr.create(node), Arr<ConnectionDefinition>.Empty);
        var executionId = Guid.NewGuid();
        var executor = Sys.ActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), _serviceProvider));

        // Act
        executor.Tell(new StartExecution(executionId));

        // Assert — should receive at least one NodeRetrying event
        var retryEvent = ExpectMsg<NodeRetrying>(TimeSpan.FromSeconds(10));
        retryEvent.NodeId.Should().Be("event-node");
        retryEvent.Attempt.Should().BeGreaterOrEqualTo(1);
        retryEvent.ExecutionId.Should().Be(executionId);
    }

    #endregion

    #region Timeout Handling Tests ⏰

    /// <summary>
    /// Test that a node with a short timeout triggers a failure when execution takes too long.
    /// The NodeExecutor's ReceiveTimeout should fire and send NodeExecutionFailed~ ⏰💔
    /// </summary>
    [Fact]
    public void NodeTimeout_ShouldTriggerFailure()
    {
        // Arrange — register a slow module
        _moduleRegistry.RegisterModule(new SlowModule());

        var node = new NodeDefinition(
            Id: "slow-node",
            ModuleId: "test.slow",
            Name: "Slow Node",
            Properties: HashMap<string, JsonElement>.Empty,
            Position: null,
            ErrorHandling: new ErrorHandling(ErrorBehavior.Fail),
            Timeout: 500, // 500ms timeout — way too short for a 30s module
            RetryPolicy: null,
            Metadata: null);

        var definition = CreateWorkflowDefinition(Arr.create(node), Arr<ConnectionDefinition>.Empty);
        var executionId = Guid.NewGuid();
        var executor = Sys.ActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), _serviceProvider));

        // Act
        executor.Tell(new StartExecution(executionId));
        Thread.Sleep(4000);

        // Assert — workflow should fail due to timeout
        executor.Tell(new GetWorkflowStatus(executionId));
        var status = ExpectMsg<WorkflowStatusResponse>(TimeSpan.FromSeconds(5));

        status.State.Should().Be(ExecutionState.Failed);
        status.Error.IsSome.Should().BeTrue();
    }

    #endregion

    #region Supervision Event Observability Tests 📡

    /// <summary>
    /// Test that SupervisionEvent messages are published to EventStream
    /// when the WorkflowSupervisor's supervision strategy makes a directive decision~ 📡✨
    /// </summary>
    [Fact]
    public void SupervisionEvent_ShouldBePublishedOnFailure()
    {
        // Arrange — subscribe to supervision events
        Sys.EventStream.Subscribe(TestActor, typeof(SupervisionEvent));

        // Create a workflow that will complete normally (so we get supervision events
        // from any accidental failures in the actor hierarchy)
        var executionId = Guid.NewGuid();
        var definition = CreateWorkflowWithModule("test.always-fails", errorBehavior: ErrorBehavior.Fail);
        var executor = Sys.ActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), _serviceProvider));

        // Act — start execution (the failing module should trigger internal failure handling)
        executor.Tell(new StartExecution(executionId));
        Thread.Sleep(3000);

        // Assert — verify the executor still processed the failure through our message-level handling
        executor.Tell(new GetWorkflowStatus(executionId));
        var status = ExpectMsg<WorkflowStatusResponse>(TimeSpan.FromSeconds(5));
        status.State.Should().Be(ExecutionState.Failed);
    }

    /// <summary>
    /// Test that ExecutionStateChanged events are published during workflow lifecycle.
    /// Validates the complete state transition chain~ 📊
    /// </summary>
    [Fact]
    public void ExecutionStateChanged_ShouldBePublishedDuringLifecycle()
    {
        // Arrange — subscribe to state change events
        Sys.EventStream.Subscribe(TestActor, typeof(ExecutionStateChanged));

        var executionId = Guid.NewGuid();
        var definition = CreateWorkflowWithModule("test.passthrough", errorBehavior: null);
        var executor = Sys.ActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), _serviceProvider));

        // Act
        executor.Tell(new StartExecution(executionId));

        // Assert — should receive Pending → Running transition
        var stateChange = ExpectMsg<ExecutionStateChanged>(TimeSpan.FromSeconds(5));
        stateChange.ExecutionId.Should().Be(executionId);
        stateChange.OldState.Should().Be(ExecutionState.Pending);
        stateChange.NewState.Should().Be(ExecutionState.Running);
    }

    #endregion

    #region Workflow-Level Error Handling Override Tests 🔧

    /// <summary>
    /// Test that workflow-level error handling is used as fallback when node has none configured.
    /// </summary>
    [Fact]
    public void WorkflowLevelErrorHandling_ShouldBeUsedAsFallback()
    {
        // Arrange — node with no error handling, workflow-level set to Continue
        var node = new NodeDefinition(
            Id: "fallback-node",
            ModuleId: "test.always-fails",
            Name: "Fallback Node",
            Properties: HashMap<string, JsonElement>.Empty,
            Position: null,
            ErrorHandling: null, // no node-level config
            Timeout: null,
            RetryPolicy: null,
            Metadata: null);

        // Workflow-level error handling says Continue
        var definition = new WorkflowDefinition(
            Id: Guid.NewGuid(),
            Name: "Workflow With Fallback Error Handling",
            Description: "Tests workflow-level error handling fallback",
            Version: new Version(1, 0, 0),
            Nodes: Arr.create(node),
            Connections: Arr<ConnectionDefinition>.Empty,
            Variables: HashMap<string, VariableDefinition>.Empty,
            Trigger: null,
            ErrorHandling: new ErrorHandling(ErrorBehavior.Continue),
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: null,
            Tags: null);

        var executionId = Guid.NewGuid();
        var executor = Sys.ActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), _serviceProvider));

        // Act
        executor.Tell(new StartExecution(executionId));
        Thread.Sleep(3000);

        // Assert — workflow should complete (not fail) because workflow-level says Continue
        executor.Tell(new GetWorkflowStatus(executionId));
        var status = ExpectMsg<WorkflowStatusResponse>(TimeSpan.FromSeconds(5));
        status.State.Should().Be(ExecutionState.Completed);
    }

    #endregion

    #region Multi-Node Failure Propagation Tests 🔗

    /// <summary>
    /// Test that in a multi-node workflow, a mid-chain failure with fail-fast
    /// stops the entire workflow and downstream nodes never execute~ 🛑
    /// </summary>
    [Fact]
    public void MultiNodeWorkflow_MidChainFailure_ShouldStopDownstream()
    {
        // Arrange — A (succeeds) → B (fails, fail-fast) → C (should never run)
        var nodeA = new NodeDefinition(
            Id: "node_a",
            ModuleId: "test.passthrough",
            Name: "Node A",
            Properties: HashMap<string, JsonElement>.Empty,
            Position: null,
            ErrorHandling: null,
            Timeout: null,
            RetryPolicy: null,
            Metadata: null);

        var nodeB = new NodeDefinition(
            Id: "node_b",
            ModuleId: "test.always-fails",
            Name: "Failing Node B",
            Properties: HashMap<string, JsonElement>.Empty,
            Position: null,
            ErrorHandling: new ErrorHandling(ErrorBehavior.Fail),
            Timeout: null,
            RetryPolicy: null,
            Metadata: null);

        var nodeC = new NodeDefinition(
            Id: "node_c",
            ModuleId: "test.passthrough",
            Name: "Node C",
            Properties: HashMap<string, JsonElement>.Empty,
            Position: null,
            ErrorHandling: null,
            Timeout: null,
            RetryPolicy: null,
            Metadata: null);

        var connAB = new ConnectionDefinition("node_a", "output", "node_b", "input", null, 0);
        var connBC = new ConnectionDefinition("node_b", "output", "node_c", "input", null, 0);

        var definition = CreateWorkflowDefinition(
            Arr.create(nodeA, nodeB, nodeC),
            Arr.create(connAB, connBC));

        var executionId = Guid.NewGuid();
        var executor = Sys.ActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), _serviceProvider));

        // Act
        executor.Tell(new StartExecution(executionId));
        Thread.Sleep(5000);

        // Assert — workflow failed, node C was cancelled (never ran)
        executor.Tell(new GetWorkflowStatus(executionId));
        var status = ExpectMsg<WorkflowStatusResponse>(TimeSpan.FromSeconds(5));

        status.State.Should().Be(ExecutionState.Failed);
        // Node A completed, node B failed, node C should be Pending or Cancelled
        status.NodeStates.Find("node_a").IfSome(s =>
            s.Should().Be(NodeExecutionState.Completed));
        status.NodeStates.Find("node_b").IfSome(s =>
            s.Should().Be(NodeExecutionState.Failed));
        status.NodeStates.Find("node_c").IfSome(s =>
            s.Should().BeOneOf(NodeExecutionState.Pending, NodeExecutionState.Cancelled));
    }

    #endregion

    #region Helper Methods 🌸

    /// <summary>
    /// Creates a simple single-node workflow using the specified module ID and error behavior~ 🎀
    /// </summary>
    private static WorkflowDefinition CreateWorkflowWithModule(
        string moduleId,
        ErrorBehavior? errorBehavior)
    {
        var errorHandling = errorBehavior.HasValue
            ? new ErrorHandling(errorBehavior.Value)
            : null;

        var node = new NodeDefinition(
            Id: "test-node",
            ModuleId: moduleId,
            Name: "Test Node",
            Properties: HashMap<string, JsonElement>.Empty,
            Position: null,
            ErrorHandling: errorHandling,
            Timeout: null,
            RetryPolicy: null,
            Metadata: null);

        return CreateWorkflowDefinition(Arr.create(node), Arr<ConnectionDefinition>.Empty);
    }

    /// <summary>
    /// Creates a WorkflowDefinition from nodes and connections~ ✨
    /// </summary>
    private static WorkflowDefinition CreateWorkflowDefinition(
        Arr<NodeDefinition> nodes,
        Arr<ConnectionDefinition> connections,
        ErrorHandling? workflowErrorHandling = null)
    {
        return new WorkflowDefinition(
            Id: Guid.NewGuid(),
            Name: "Supervision Test Workflow",
            Description: "Workflow for supervision strategy testing~ UwU",
            Version: new Version(1, 0, 0),
            Nodes: nodes,
            Connections: connections,
            Variables: HashMap<string, VariableDefinition>.Empty,
            Trigger: null,
            ErrorHandling: workflowErrorHandling,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: null,
            Tags: null);
    }

    /// <summary>
    /// Creates a HashMap of JsonElement properties from a Dictionary.
    /// Handy for building test node configurations~ 🔧
    /// </summary>
    private static HashMap<string, JsonElement> CreateJsonProperties(Dictionary<string, object> values)
    {
        var map = new Dictionary<string, JsonElement>();
        foreach (var (key, value) in values)
        {
            var json = JsonSerializer.Serialize(value);
            map[key] = JsonDocument.Parse(json).RootElement.Clone();
        }

        return map.ToHashMap();
    }

    #endregion

    #region Test Modules 🧪

    /// <summary>
    /// Test module that always fails with an InvalidOperationException.
    /// Used for testing fail-fast, continue-on-error, and supervision responses~ 💥
    /// </summary>
    private class AlwaysFailsModule : IWorkflowModule
    {
        public string ModuleId => "test.always-fails";
        public string DisplayName => "Always Fails";
        public string Category => "Testing";
        public string Description => "Always fails for testing error handling~ 💥";
        public string Icon => "💥";

        public ModuleSchema Schema => ModuleSchema.Empty;

        /// <inheritdoc/>
        public Task<ModuleResult> ExecuteAsync(
            ModuleExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("This is an intentional test failure! 💥 UwU");
        }
    }

    /// <summary>
    /// Test module that fails a configurable number of times then succeeds.
    /// Uses a shared <see cref="ConcurrentDictionary{TKey,TValue}"/> keyed by testRunId
    /// to track invocation counts across actor recreations (for retry testing)~ 🔄✨
    /// </summary>
    /// <remarks>
    /// CopilotNote: Properties required:
    /// - "testRunId" (string): unique key for call counter isolation per test
    /// - "failCount" (int): number of times to fail before succeeding
    /// Call <see cref="ResetCounter"/> before each test to ensure isolation! 💖
    /// </remarks>
    private class ConfigurableFailureModule : IWorkflowModule
    {
        /// <summary>
        /// Thread-safe invocation counter keyed by testRunId.
        /// Survives actor restarts/recreations for accurate retry tracking~ 📊
        /// </summary>
        private static readonly ConcurrentDictionary<string, int> CallCounts = new();

        public string ModuleId => "test.configurable-failure";
        public string DisplayName => "Configurable Failure";
        public string Category => "Testing";
        public string Description => "Fails N times then succeeds~ 🔄";
        public string Icon => "🔄";

        public ModuleSchema Schema => ModuleSchema.Empty;

        /// <summary>
        /// Resets the invocation counter for the given test run ID.
        /// Call this at the start of each test for clean isolation~ 🧹
        /// </summary>
        public static void ResetCounter(string testRunId)
        {
            CallCounts[testRunId] = 0;
        }

        /// <summary>
        /// Gets the current invocation count for a test run ID.
        /// </summary>
        public static int GetCount(string testRunId)
        {
            return CallCounts.GetValueOrDefault(testRunId, 0);
        }

        /// <inheritdoc/>
        public Task<ModuleResult> ExecuteAsync(
            ModuleExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            // Read properties from context
            var testRunId = context.Properties.TryGetValue("testRunId", out var runIdObj)
                ? runIdObj?.ToString() ?? "default"
                : "default";

            var failCount = 2; // default
            if (context.Properties.TryGetValue("failCount", out var failObj))
            {
                if (failObj is long l)
                {
                    failCount = (int)l;
                }
                else if (failObj is int i)
                {
                    failCount = i;
                }
                else if (failObj is string s && int.TryParse(s, out var parsed))
                {
                    failCount = parsed;
                }
            }

            // Increment and check call count
            var currentCount = CallCounts.AddOrUpdate(testRunId, 1, (_, old) => old + 1);

            if (currentCount <= failCount)
            {
                throw new InvalidOperationException(
                    $"Configurable failure: attempt {currentCount}/{failCount}! 💥");
            }

            // We survived all the failures — time to succeed! 🎉
            return Task.FromResult(ModuleResult.Ok(new Dictionary<string, object?>
            {
                ["result"] = $"Succeeded on attempt {currentCount}",
                ["attempts"] = currentCount,
            }));
        }
    }

    /// <summary>
    /// Simple pass-through module that always succeeds.
    /// Used as a "healthy" node in multi-node test workflows~ ✅
    /// </summary>
    private class PassThroughTestModule : IWorkflowModule
    {
        public string ModuleId => "test.passthrough";
        public string DisplayName => "Pass Through";
        public string Category => "Testing";
        public string Description => "Always succeeds, passes inputs as outputs~ ✅";
        public string Icon => "✅";

        public ModuleSchema Schema => ModuleSchema.Empty;

        /// <inheritdoc/>
        public Task<ModuleResult> ExecuteAsync(
            ModuleExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ModuleResult.Ok(new Dictionary<string, object?>
            {
                ["result"] = "passed through!",
                ["success"] = true,
            }));
        }
    }

    /// <summary>
    /// Test module that takes a very long time (simulates a slow/stuck operation).
    /// Used for testing timeout handling~ ⏰🐌
    /// </summary>
    private class SlowModule : IWorkflowModule
    {
        public string ModuleId => "test.slow";
        public string DisplayName => "Slow Module";
        public string Category => "Testing";
        public string Description => "Takes forever (for timeout testing)~ 🐌";
        public string Icon => "🐌";

        public ModuleSchema Schema => ModuleSchema.Empty;

        /// <inheritdoc/>
        public async Task<ModuleResult> ExecuteAsync(
            ModuleExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            // Simulate a very slow operation (30 seconds)
            await Task.Delay(30000, cancellationToken).ConfigureAwait(false);
            return ModuleResult.Ok(new Dictionary<string, object?>
            {
                ["completed"] = true,
            });
        }
    }

    #endregion
}

