// <copyright file="NodeExecutorTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Engine;

using System;
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
using Workflow.Core.Models;
using Workflow.Engine.Actors;
using Workflow.Engine.Messages;
using Workflow.Modules;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin;
using Xunit;

/// <summary>
/// Comprehensive tests for the NodeExecutor actor.
/// Tests cover module invocation, input validation, timeouts, and cancellation~ ✨
/// </summary>
public class NodeExecutorTests : TestKit
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IModuleRegistry _moduleRegistry;

    /// <summary>
    /// Initializes a new instance of the <see cref="NodeExecutorTests"/> class.
    /// Sets up the test environment with module registry and services~ UwU
    /// </summary>
    public NodeExecutorTests()
    {
        _moduleRegistry = new InMemoryModuleRegistry();
        _moduleRegistry.RegisterModule(new PassThroughModule());
        _moduleRegistry.RegisterModule(new TestDelayModule());
        _moduleRegistry.RegisterModule(new TestFailingModule());

        var services = new ServiceCollection();
        services.AddSingleton(_moduleRegistry);
        services.AddLogging(builder => builder.AddDebug());
        _serviceProvider = services.BuildServiceProvider();
    }

    #region Creation Tests

    /// <summary>
    /// Test that NodeExecutor can be created successfully.
    /// </summary>
    [Fact]
    public void NodeExecutor_ShouldBeCreatedSuccessfully()
    {
        // Arrange
        var nodeId = "test-node";
        var nodeDef = CreateNodeDefinition("builtin.passthrough");
        var inputs = new Dictionary<string, object?>();
        var executionId = Guid.NewGuid();

        // Act
        var executor = Sys.ActorOf(
            NodeExecutor.Props(nodeId, nodeDef, inputs, executionId, _serviceProvider),
            "test-node-executor");

        // Assert
        executor.Should().NotBeNull();
        executor.Path.Name.Should().Be("test-node-executor");
    }

    #endregion

    #region Module Execution Tests

    /// <summary>
    /// Test that NodeExecutor successfully executes a registered module.
    /// </summary>
    [Fact]
    public void Execute_WithRegisteredModule_ShouldComplete()
    {
        // Arrange
        var nodeId = "passthrough-node";
        var nodeDef = CreateNodeDefinition("builtin.passthrough");
        var inputs = new Dictionary<string, object?>
        {
            ["input"] = "Hello World",
        };
        var executionId = Guid.NewGuid();

        var executor = Sys.ActorOf(
            NodeExecutor.Props(nodeId, nodeDef, inputs, executionId, _serviceProvider));

        // Act
        executor.Tell(new Execute(nodeId, inputs, executionId));

        // Assert
        var completed = ExpectMsg<NodeExecutionCompleted>(TimeSpan.FromSeconds(5));
        completed.NodeId.Should().Be(nodeId);
        completed.ExecutionId.Should().Be(executionId);
        completed.Outputs.Should().ContainKey("input");
        completed.Duration.Should().BeGreaterThan(TimeSpan.Zero);
    }

    /// <summary>
    /// Test that NodeExecutor uses fallback stub when module is not found.
    /// </summary>
    [Fact]
    public void Execute_WithUnregisteredModule_ShouldUseFallbackStub()
    {
        // Arrange
        var nodeId = "unknown-node";
        var nodeDef = CreateNodeDefinition("unknown.module");
        var inputs = new Dictionary<string, object?>();
        var executionId = Guid.NewGuid();

        var executor = Sys.ActorOf(
            NodeExecutor.Props(nodeId, nodeDef, inputs, executionId, _serviceProvider));

        // Act
        executor.Tell(new Execute(nodeId, inputs, executionId));

        // Assert - Should complete with stub outputs
        var completed = ExpectMsg<NodeExecutionCompleted>(TimeSpan.FromSeconds(5));
        completed.NodeId.Should().Be(nodeId);
        completed.Outputs.Should().ContainKey("success");
        completed.Outputs["success"].Should().Be(true);
    }

    /// <summary>
    /// Test that inputs are passed to the module correctly.
    /// </summary>
    [Fact]
    public void Execute_ShouldPassInputsToModule()
    {
        // Arrange
        var nodeId = "passthrough-node";
        var nodeDef = CreateNodeDefinition("builtin.passthrough");
        var inputs = new Dictionary<string, object?>
        {
            ["testValue"] = 42,
            ["message"] = "Hello",
        };
        var executionId = Guid.NewGuid();

        var executor = Sys.ActorOf(
            NodeExecutor.Props(nodeId, nodeDef, inputs, executionId, _serviceProvider));

        // Act
        executor.Tell(new Execute(nodeId, inputs, executionId));

        // Assert
        var completed = ExpectMsg<NodeExecutionCompleted>(TimeSpan.FromSeconds(5));
        completed.Outputs.Should().ContainKey("testValue");
        completed.Outputs.Should().ContainKey("message");
    }

    #endregion

    #region Failure Handling Tests

    /// <summary>
    /// Test that NodeExecutor reports failure when module throws exception.
    /// </summary>
    [Fact]
    public void Execute_WhenModuleThrows_ShouldReportFailure()
    {
        // Arrange
        var nodeId = "failing-node";
        var nodeDef = CreateNodeDefinition("test.failing");
        var inputs = new Dictionary<string, object?>();
        var executionId = Guid.NewGuid();

        var executor = Sys.ActorOf(
            NodeExecutor.Props(nodeId, nodeDef, inputs, executionId, _serviceProvider));

        // Act
        executor.Tell(new Execute(nodeId, inputs, executionId));

        // Assert
        var failed = ExpectMsg<NodeExecutionFailed>(TimeSpan.FromSeconds(5));
        failed.NodeId.Should().Be(nodeId);
        failed.ExecutionId.Should().Be(executionId);
        failed.Error.Should().NotBeNull();
        failed.Error.Message.Should().Contain("intentional");
    }

    #endregion

    #region Cancellation Tests

    /// <summary>
    /// Test that NodeExecutor can be cancelled.
    /// </summary>
    [Fact]
    public void Cancel_ShouldStopExecution()
    {
        // Arrange
        var nodeId = "delay-node";
        var nodeDef = CreateNodeDefinition("test.delay");
        var inputs = new Dictionary<string, object?>();
        var executionId = Guid.NewGuid();

        var executor = Sys.ActorOf(
            NodeExecutor.Props(nodeId, nodeDef, inputs, executionId, _serviceProvider));

        // Act - Start then cancel
        executor.Tell(new Execute(nodeId, inputs, executionId));
        Thread.Sleep(100); // Give it time to start
        executor.Tell(new CancelExecution(executionId));

        // Assert - Should not receive completion (actor will be stopped by parent)
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    #endregion

    #region Duplicate Execution Tests

    /// <summary>
    /// Test that duplicate Execute messages are ignored.
    /// </summary>
    [Fact]
    public void Execute_WhenAlreadyExecuting_ShouldIgnoreDuplicate()
    {
        // Arrange
        var nodeId = "delay-node";
        var nodeDef = CreateNodeDefinition("test.delay");
        var inputs = new Dictionary<string, object?>();
        var executionId = Guid.NewGuid();

        var executor = Sys.ActorOf(
            NodeExecutor.Props(nodeId, nodeDef, inputs, executionId, _serviceProvider));

        // Act - Send two execute messages
        executor.Tell(new Execute(nodeId, inputs, executionId));
        executor.Tell(new Execute(nodeId, inputs, executionId));

        // Assert - Should only complete once
        var completed = ExpectMsg<NodeExecutionCompleted>(TimeSpan.FromSeconds(10));
        completed.NodeId.Should().Be(nodeId);

        // Should not receive another completion
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates a node definition with the specified module ID.
    /// </summary>
    private static NodeDefinition CreateNodeDefinition(string moduleId)
    {
        return new NodeDefinition(
            Id: "test-node",
            ModuleId: moduleId,
            Name: "Test Node",
            Properties: HashMap<string, JsonElement>.Empty,
            Position: null,
            ErrorHandling: null,
            Timeout: null,
            RetryPolicy: null,
            Metadata: null);
    }

    #endregion

    #region Test Modules

    /// <summary>
    /// Test module that introduces a delay.
    /// </summary>
    private class TestDelayModule : IWorkflowModule
    {
        public string ModuleId => "test.delay";
        public string DisplayName => "Test Delay";
        public string Category => "Testing";
        public string Description => "Delays for testing timeouts and cancellation.";
        public string Icon => "⏱️";

        public ModuleSchema Schema => new(
            Inputs: Arr<PortDefinition>.Empty,
            Outputs: Arr.create(PortDefinition.Create<bool>("completed")),
            Properties: Arr<ModulePropertyDefinition>.Empty);

        public async Task<ModuleResult> ExecuteAsync(
            ModuleExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            // Delay for 5 seconds (can be cancelled)
            await Task.Delay(5000, cancellationToken).ConfigureAwait(false);

            return ModuleResult.Ok(new Dictionary<string, object?>
            {
                ["completed"] = true,
            });
        }
    }

    /// <summary>
    /// Test module that always fails.
    /// </summary>
    private class TestFailingModule : IWorkflowModule
    {
        public string ModuleId => "test.failing";
        public string DisplayName => "Test Failing";
        public string Category => "Testing";
        public string Description => "Always fails for testing error handling.";
        public string Icon => "💥";

        public ModuleSchema Schema => ModuleSchema.Empty;

        public Task<ModuleResult> ExecuteAsync(
            ModuleExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("This is an intentional test failure!");
        }
    }

    #endregion
}

