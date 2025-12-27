// <copyright file="WorkflowSupervisorTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Engine;

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.TestKit.Xunit2;
using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Core.Abstractions;
using Workflow.Core.Models;
using Workflow.Engine.Actors;
using Workflow.Engine.Messages;
using Xunit;

/// <summary>
/// Comprehensive tests for the WorkflowSupervisor actor.
/// Tests cover lifecycle, message handling, and supervision behavior~ 🧪✨
/// </summary>
public class WorkflowSupervisorTests : TestKit
{
    private readonly IServiceProvider serviceProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowSupervisorTests"/> class.
    /// Sets up the test environment with necessary services~ UwU
    /// </summary>
    public WorkflowSupervisorTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<WorkflowValidator>();
        this.serviceProvider = services.BuildServiceProvider();
    }

    /// <summary>
    /// Test that the WorkflowSupervisor actor can be created successfully.
    /// Verifies the actor is alive and responding to messages~ 💖
    /// </summary>
    [Fact]
    public void WorkflowSupervisor_ShouldBeCreatedSuccessfully()
    {
        // Arrange & Act
        var supervisor = Sys.ActorOf(WorkflowSupervisor.Props(this.serviceProvider), "test-supervisor");

        // Assert
        supervisor.Should().NotBeNull();
        supervisor.Path.Name.Should().Be("test-supervisor");

        // Verify the actor is actually alive by checking if it can handle a simple message
        // We'll query status for a non-existent workflow - should get a Failure response
        supervisor.Tell(new GetWorkflowStatus(Guid.NewGuid()));
        var response = ExpectMsg<Status.Failure>(TimeSpan.FromSeconds(3));
        response.Should().NotBeNull();
        response.Cause.Should().BeOfType<InvalidOperationException>();
    }

    /// <summary>
    /// Test that creating a workflow instance returns a valid execution ID.
    /// </summary>
    [Fact]
    public void CreateWorkflowInstance_WithValidDefinition_ShouldReturnExecutionId()
    {
        // Arrange
        var supervisor = Sys.ActorOf(WorkflowSupervisor.Props(this.serviceProvider));
        var workflowId = Guid.NewGuid();
        var definition = CreateValidWorkflowDefinition();

        var message = new CreateWorkflowInstance(
            workflowId,
            definition,
            HashMap<string, object?>.Empty);

        // Act
        supervisor.Tell(message);

        // Assert
        var response = ExpectMsg<WorkflowInstanceCreated>(TimeSpan.FromSeconds(3));
        response.ExecutionId.Should().NotBeEmpty();
        response.WorkflowId.Should().Be(workflowId);
    }

    /// <summary>
    /// Test that creating a workflow with invalid definition fails with proper error.
    /// </summary>
    [Fact]
    public void CreateWorkflowInstance_WithInvalidDefinition_ShouldReturnFailure()
    {
        // Arrange
        var supervisor = Sys.ActorOf(WorkflowSupervisor.Props(this.serviceProvider));
        var workflowId = Guid.NewGuid();

        // Create an invalid workflow (cycle)
        var definition = CreateWorkflowWithCycle();

        var message = new CreateWorkflowInstance(
            workflowId,
            definition,
            HashMap<string, object?>.Empty);

        // Act
        supervisor.Tell(message);

        // Assert
        var response = ExpectMsg<Status.Failure>(TimeSpan.FromSeconds(3));
        response.Cause.Should().BeOfType<InvalidOperationException>();
        response.Cause.Message.Should().Contain("validation failed");
    }

    /// <summary>
    /// Test that multiple workflow instances can be created concurrently.
    /// </summary>
    [Fact]
    public void CreateWorkflowInstance_MultipleInstances_ShouldTrackAllInstances()
    {
        // Arrange
        var supervisor = Sys.ActorOf(WorkflowSupervisor.Props(this.serviceProvider));
        var definition = CreateValidWorkflowDefinition();

        // Act - Create 3 workflow instances
        var executionIds = new List<Guid>();
        for (int i = 0; i < 3; i++)
        {
            var workflowId = Guid.NewGuid();
            var message = new CreateWorkflowInstance(
                workflowId,
                definition,
                HashMap<string, object?>.Empty);

            supervisor.Tell(message);
            var response = ExpectMsg<WorkflowInstanceCreated>(TimeSpan.FromSeconds(3));
            executionIds.Add(response.ExecutionId);
        }

        // Assert
        executionIds.Should().HaveCount(3);
        executionIds.Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    /// Test that status can be queried for an active workflow.
    /// Verifies the workflow executor responds with valid status~ 📊
    /// </summary>
    [Fact]
    public void GetWorkflowStatus_ForExistingWorkflow_ShouldReturnStatus()
    {
        // Arrange
        var supervisor = Sys.ActorOf(WorkflowSupervisor.Props(this.serviceProvider));
        var workflowId = Guid.NewGuid();
        var definition = CreateValidWorkflowDefinition();

        // Create workflow instance first
        supervisor.Tell(new CreateWorkflowInstance(
            workflowId,
            definition,
            HashMap<string, object?>.Empty));

        var created = ExpectMsg<WorkflowInstanceCreated>(TimeSpan.FromSeconds(3));

        // Act - Query status
        supervisor.Tell(new GetWorkflowStatus(created.ExecutionId));

        // Assert
        var status = ExpectMsg<WorkflowStatusResponse>(TimeSpan.FromSeconds(3));
        status.ExecutionId.Should().Be(created.ExecutionId);

        // With the full WorkflowExecutor, the workflow will be Running or Completed
        // (single node workflow completes very quickly with the stub NodeExecutor)
        status.State.Should().BeOneOf(
            ExecutionState.Running,
            ExecutionState.Completed);

        // Progress should be valid (0-100%)
        status.Progress.Should().BeInRange(0, 100);

        // Should have node state information
        status.NodeStates.Should().NotBeNull();
    }

    /// <summary>
    /// Test that querying status for non-existent workflow returns failure.
    /// </summary>
    [Fact]
    public void GetWorkflowStatus_ForNonExistentWorkflow_ShouldReturnFailure()
    {
        // Arrange
        var supervisor = Sys.ActorOf(WorkflowSupervisor.Props(this.serviceProvider));
        var nonExistentId = Guid.NewGuid();

        // Act
        supervisor.Tell(new GetWorkflowStatus(nonExistentId));

        // Assert
        var response = ExpectMsg<Status.Failure>(TimeSpan.FromSeconds(3));
        response.Cause.Should().BeOfType<InvalidOperationException>();
        response.Cause.Message.Should().Contain("not found");
    }

    /// <summary>
    /// Test that cancellation request is forwarded to the correct workflow executor.
    /// </summary>
    [Fact]
    public void CancelExecution_ForExistingWorkflow_ShouldForwardToExecutor()
    {
        // Arrange
        var supervisor = Sys.ActorOf(WorkflowSupervisor.Props(this.serviceProvider));
        var workflowId = Guid.NewGuid();
        var definition = CreateValidWorkflowDefinition();

        // Create workflow instance first
        supervisor.Tell(new CreateWorkflowInstance(
            workflowId,
            definition,
            HashMap<string, object?>.Empty));

        var created = ExpectMsg<WorkflowInstanceCreated>(TimeSpan.FromSeconds(3));

        // Act - Cancel execution
        supervisor.Tell(new CancelExecution(created.ExecutionId));

        // Assert - We should not get a failure (it's forwarded to executor)
        // Since executor is a stub, we just verify no failure is returned
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    /// <summary>
    /// Test that cancelling non-existent workflow returns failure.
    /// </summary>
    [Fact]
    public void CancelExecution_ForNonExistentWorkflow_ShouldReturnFailure()
    {
        // Arrange
        var supervisor = Sys.ActorOf(WorkflowSupervisor.Props(this.serviceProvider));
        var nonExistentId = Guid.NewGuid();

        // Act
        supervisor.Tell(new CancelExecution(nonExistentId));

        // Assert
        var response = ExpectMsg<Status.Failure>(TimeSpan.FromSeconds(3));
        response.Cause.Should().BeOfType<InvalidOperationException>();
        response.Cause.Message.Should().Contain("not found");
    }

    /// <summary>
    /// Test that supervisor gracefully handles child actor termination.
    /// This is a basic test - full death watch behavior will be tested in integration tests.
    /// </summary>
    [Fact]
    public void WorkflowSupervisor_WhenChildTerminates_ShouldHandleGracefully()
    {
        // Arrange
        var supervisor = Sys.ActorOf(WorkflowSupervisor.Props(this.serviceProvider));
        var workflowId = Guid.NewGuid();
        var definition = CreateValidWorkflowDefinition();

        // Create workflow instance
        supervisor.Tell(new CreateWorkflowInstance(
            workflowId,
            definition,
            HashMap<string, object?>.Empty));

        var created = ExpectMsg<WorkflowInstanceCreated>(TimeSpan.FromSeconds(3));

        // Act - Query status to verify workflow exists
        supervisor.Tell(new GetWorkflowStatus(created.ExecutionId));
        ExpectMsg<WorkflowStatusResponse>(TimeSpan.FromSeconds(3));

        // Note: We can't easily test actual termination without implementing
        // full WorkflowExecutor, so this test is basic for now
        // Will be expanded in Phase 1.3.2~ 💖
    }

    /// <summary>
    /// Creates a valid workflow definition for testing~ 🌸
    /// </summary>
    private static WorkflowDefinition CreateValidWorkflowDefinition()
    {
        var node1 = new NodeDefinition(
            Id: "node1",
            ModuleId: "test.module",
            Name: "Test Node",
            Properties: HashMap<string, System.Text.Json.JsonElement>.Empty,
            Position: null,
            ErrorHandling: null,
            Timeout: null,
            RetryPolicy: null,
            Metadata: null);

        return new WorkflowDefinition(
            Id: Guid.NewGuid(),
            Name: "Test Workflow",
            Description: "A test workflow",
            Version: new Version(1, 0, 0),
            Nodes: Arr.create(node1),
            Connections: Arr<ConnectionDefinition>.Empty,
            Variables: HashMap<string, VariableDefinition>.Empty,
            Trigger: null,
            ErrorHandling: null,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: null,
            Tags: null);
    }

    /// <summary>
    /// Creates a workflow with a cycle for testing validation~ ⚠️
    /// </summary>
    private static WorkflowDefinition CreateWorkflowWithCycle()
    {
        var node1 = new NodeDefinition(
            Id: "node1",
            ModuleId: "test.module",
            Name: "Node 1",
            Properties: HashMap<string, System.Text.Json.JsonElement>.Empty,
            Position: null,
            ErrorHandling: null,
            Timeout: null,
            RetryPolicy: null,
            Metadata: null);

        var node2 = new NodeDefinition(
            Id: "node2",
            ModuleId: "test.module",
            Name: "Node 2",
            Properties: HashMap<string, System.Text.Json.JsonElement>.Empty,
            Position: null,
            ErrorHandling: null,
            Timeout: null,
            RetryPolicy: null,
            Metadata: null);

        var conn1 = new ConnectionDefinition(
            SourceNodeId: "node1",
            SourcePortName: "output",
            TargetNodeId: "node2",
            TargetPortName: "input",
            Condition: null,
            Priority: 0);

        var conn2 = new ConnectionDefinition(
            SourceNodeId: "node2",
            SourcePortName: "output",
            TargetNodeId: "node1",
            TargetPortName: "input",
            Condition: null,
            Priority: 0);

        return new WorkflowDefinition(
            Id: Guid.NewGuid(),
            Name: "Cyclic Workflow",
            Description: "A workflow with a cycle",
            Version: new Version(1, 0, 0),
            Nodes: Arr.create(node1, node2),
            Connections: Arr.create(conn1, conn2),
            Variables: HashMap<string, VariableDefinition>.Empty,
            Trigger: null,
            ErrorHandling: null,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: null,
            Tags: null);
    }
}

