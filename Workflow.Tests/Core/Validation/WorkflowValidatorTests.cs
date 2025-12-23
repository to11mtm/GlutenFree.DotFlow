// ────────────────────────────────────────────────────────────────────────────────
// Copyright © GlutenFree 2025. Made with love by Ami-Chan! UwU 💖
// Licensed under the MIT License.
// ────────────────────────────────────────────────────────────────────────────────

using FluentAssertions;
using LanguageExt;
using static LanguageExt.Prelude;
using Workflow.Core.Abstractions;
using Workflow.Core.Models;
using HashMap = LanguageExt.HashMap;

namespace Workflow.Tests.Core.Validation;

/// <summary>
/// Tests for WorkflowValidator to ensure it catches all validation errors! 🧪✨
/// </summary>
public class WorkflowValidatorTests
{
	private readonly WorkflowValidator _validator = new();

	/// <summary>
	/// Helper to create a minimal valid workflow for testing. 💖
	/// </summary>
	private static WorkflowDefinition CreateValidWorkflow()
	{
		return new WorkflowDefinition(
			Id: Guid.NewGuid(),
			Name: "Test Workflow",
			Description: "A test workflow",
			Version: new Version(1, 0, 0),
			Nodes: Arr.create(
				new NodeDefinition(
					Id: "node1",
					ModuleId: "test.module",
					Name: "Test Node",
					Properties: HashMap.empty<string, System.Text.Json.JsonElement>())),
			Connections: Arr<ConnectionDefinition>.Empty,
			Variables: HashMap.empty<string, VariableDefinition>());
	}

	[Fact]
	public void Validate_ValidWorkflow_ReturnsSuccess()
	{
		// Arrange - Create a valid workflow
		var workflow = CreateValidWorkflow();

		// Act - Validate it
		var result = _validator.Validate(workflow);

		// Assert - Should be valid!
		result.IsValid.Should().BeTrue();
		result.Errors.Should().BeEmpty();
	}

	[Fact]
	public void Validate_EmptyWorkflow_ReturnsError()
	{
		// Arrange - Workflow with no nodes
		var workflow = new WorkflowDefinition(
			Id: Guid.NewGuid(),
			Name: "Empty Workflow",
			Description: null,
			Version: new Version(1, 0, 0),
			Nodes: Arr<NodeDefinition>.Empty,
			Connections: Arr<ConnectionDefinition>.Empty,
			Variables: HashMap.empty<string, VariableDefinition>());

		// Act
		var result = _validator.Validate(workflow);

		// Assert - Should have error WF001
		result.IsValid.Should().BeFalse();
		result.Errors.Should().ContainSingle(e => e.Code == "WF001");
		result.Errors[0].Message.Should().Contain("at least one node");
	}

	[Fact]
	public void Validate_EmptyWorkflowName_ReturnsError()
	{
		// Arrange - Workflow with empty name
		var workflow = CreateValidWorkflow() with { Name = "" };

		// Act
		var result = _validator.Validate(workflow);

		// Assert - Should have error WF002
		result.IsValid.Should().BeFalse();
		result.Errors.Should().ContainSingle(e => e.Code == "WF002");
	}

	[Fact]
	public void Validate_DuplicateNodeIds_ReturnsError()
	{
		// Arrange - Two nodes with same ID
		var workflow = CreateValidWorkflow() with
		{
			Nodes = Arr.create(
				new NodeDefinition("duplicate", "mod1", "Node 1", HashMap.empty<string, System.Text.Json.JsonElement>()),
				new NodeDefinition("duplicate", "mod2", "Node 2", HashMap.empty<string, System.Text.Json.JsonElement>()))
		};

		// Act
		var result = _validator.Validate(workflow);

		// Assert - Should have error WF003
		result.IsValid.Should().BeFalse();
		result.Errors.Should().ContainSingle(e => e.Code == "WF003");
		result.Errors[0].Message.Should().Contain("duplicate");
	}

	[Fact]
	public void Validate_CyclicWorkflow_ReturnsError()
	{
		// Arrange - Create a cycle: A → B → C → A
		var workflow = new WorkflowDefinition(
			Id: Guid.NewGuid(),
			Name: "Cyclic Workflow",
			Description: "Has a cycle!",
			Version: new Version(1, 0, 0),
			Nodes: Arr.create(
				new NodeDefinition("A", "mod", "Node A", HashMap.empty<string, System.Text.Json.JsonElement>()),
				new NodeDefinition("B", "mod", "Node B", HashMap.empty<string, System.Text.Json.JsonElement>()),
				new NodeDefinition("C", "mod", "Node C", HashMap.empty<string, System.Text.Json.JsonElement>())),
			Connections: Arr.create(
				new ConnectionDefinition("A", "out", "B", "in"),
				new ConnectionDefinition("B", "out", "C", "in"),
				new ConnectionDefinition("C", "out", "A", "in")), // Cycle!
			Variables: HashMap.empty<string, VariableDefinition>());

		// Act
		var result = _validator.Validate(workflow);

		// Assert - Should detect cycle with error WF012
		result.IsValid.Should().BeFalse();
		result.Errors.Should().ContainSingle(e => e.Code == "WF012");
		result.Errors[0].Message.Should().Contain("Cycle detected");
	}

	[Fact]
	public void Validate_SelfConnection_ReturnsError()
	{
		// Arrange - Node connecting to itself
		var workflow = CreateValidWorkflow() with
		{
			Connections = Arr.create(
				new ConnectionDefinition("node1", "out", "node1", "in"))
		};

		// Act
		var result = _validator.Validate(workflow);

		// Assert - Should have error WF007
		result.IsValid.Should().BeFalse();
		result.Errors.Should().ContainSingle(e => e.Code == "WF007");
		result.Errors[0].Message.Should().Contain("cannot connect to itself");
	}

	[Fact]
	public void Validate_InvalidSourceNode_ReturnsError()
	{
		// Arrange - Connection references non-existent source node
		var workflow = CreateValidWorkflow() with
		{
			Connections = Arr.create(
				new ConnectionDefinition("nonexistent", "out", "node1", "in"))
		};

		// Act
		var result = _validator.Validate(workflow);

		// Assert - Should have error WF005
		result.IsValid.Should().BeFalse();
		result.Errors.Should().ContainSingle(e => e.Code == "WF005");
		result.Errors[0].Message.Should().Contain("nonexistent");
	}

	[Fact]
	public void Validate_InvalidTargetNode_ReturnsError()
	{
		// Arrange - Connection references non-existent target node
		var workflow = CreateValidWorkflow() with
		{
			Connections = Arr.create(
				new ConnectionDefinition("node1", "out", "nonexistent", "in"))
		};

		// Act
		var result = _validator.Validate(workflow);

		// Assert - Should have error WF006
		result.IsValid.Should().BeFalse();
		result.Errors.Should().ContainSingle(e => e.Code == "WF006");
	}

	[Fact]
	public void Validate_EmptySourcePortName_ReturnsError()
	{
		// Arrange - Connection with empty source port
		var workflow = new WorkflowDefinition(
			Id: Guid.NewGuid(),
			Name: "Test",
			Description: null,
			Version: new Version(1, 0, 0),
			Nodes: Arr.create(
				new NodeDefinition("node1", "mod", "Node 1", HashMap.empty<string, System.Text.Json.JsonElement>()),
				new NodeDefinition("node2", "mod", "Node 2", HashMap.empty<string, System.Text.Json.JsonElement>())),
			Connections: Arr.create(
				new ConnectionDefinition("node1", "", "node2", "in")),
			Variables: HashMap.empty<string, VariableDefinition>());

		// Act
		var result = _validator.Validate(workflow);

		// Assert - Should have error WF008
		result.IsValid.Should().BeFalse();
		result.Errors.Should().ContainSingle(e => e.Code == "WF008");
	}

	[Fact]
	public void Validate_EmptyTargetPortName_ReturnsError()
	{
		// Arrange - Connection with empty target port
		var workflow = new WorkflowDefinition(
			Id: Guid.NewGuid(),
			Name: "Test",
			Description: null,
			Version: new Version(1, 0, 0),
			Nodes: Arr.create(
				new NodeDefinition("node1", "mod", "Node 1", HashMap.empty<string, System.Text.Json.JsonElement>()),
				new NodeDefinition("node2", "mod", "Node 2", HashMap.empty<string, System.Text.Json.JsonElement>())),
			Connections: Arr.create(
				new ConnectionDefinition("node1", "out", "node2", "")),
			Variables: HashMap.empty<string, VariableDefinition>());

		// Act
		var result = _validator.Validate(workflow);

		// Assert - Should have error WF009
		result.IsValid.Should().BeFalse();
		result.Errors.Should().ContainSingle(e => e.Code == "WF009");
	}

	[Fact]
	public void Validate_NoStartNode_ReturnsError()
	{
		// Arrange - All nodes have incoming connections (no entry point)
		var workflow = new WorkflowDefinition(
			Id: Guid.NewGuid(),
			Name: "No Start",
			Description: null,
			Version: new Version(1, 0, 0),
			Nodes: Arr.create(
				new NodeDefinition("node1", "mod", "Node 1", HashMap.empty<string, System.Text.Json.JsonElement>()),
				new NodeDefinition("node2", "mod", "Node 2", HashMap.empty<string, System.Text.Json.JsonElement>())),
			Connections: Arr.create(
				new ConnectionDefinition("node1", "out", "node2", "in"),
				new ConnectionDefinition("node2", "out", "node1", "in")), // Both have incoming
			Variables: HashMap.empty<string, VariableDefinition>());

		// Act
		var result = _validator.Validate(workflow);

		// Assert - Should have start node error (WF010) AND cycle error (WF012)
		result.IsValid.Should().BeFalse();
		result.Errors.Should().Contain(e => e.Code == "WF010");
	}

	[Fact]
	public void Validate_OrphanedNodes_ReturnsWarning()
	{
		// Arrange - One connected subgraph and one disconnected node
		var workflow = new WorkflowDefinition(
			Id: Guid.NewGuid(),
			Name: "Orphaned Test",
			Description: null,
			Version: new Version(1, 0, 0),
			Nodes: Arr.create(
				new NodeDefinition("node1", "mod", "Node 1", HashMap.empty<string, System.Text.Json.JsonElement>()),
				new NodeDefinition("node2", "mod", "Node 2", HashMap.empty<string, System.Text.Json.JsonElement>()),
				new NodeDefinition("orphan", "mod", "Orphan Node", HashMap.empty<string, System.Text.Json.JsonElement>())),
			Connections: Arr.create(
				new ConnectionDefinition("node1", "out", "node2", "in")),
			Variables: HashMap.empty<string, VariableDefinition>());

		// Act
		var result = _validator.Validate(workflow);

		// Assert - Should have warning about orphaned node
		result.IsValid.Should().BeTrue(); // Warnings don't make it invalid
		result.Warnings.Should().ContainSingle(w => w.Code == "WF011");
		result.Warnings[0].Message.Should().Contain("orphaned");
		result.Warnings[0].Message.Should().Contain("orphan");
	}

	[Fact]
	public void Validate_InvalidErrorHandler_ReturnsError()
	{
		// Arrange - Workflow with error handler pointing to non-existent node
		var workflow = CreateValidWorkflow() with
		{
			ErrorHandling = new ErrorHandling(ErrorBehavior.UseErrorHandler, "nonexistent")
		};

		// Act
		var result = _validator.Validate(workflow);

		// Assert - Should have error WF013
		result.IsValid.Should().BeFalse();
		result.Errors.Should().ContainSingle(e => e.Code == "WF013");
	}

	[Fact]
	public void Validate_NodeLevelInvalidErrorHandler_ReturnsError()
	{
		// Arrange - Node with error handler pointing to non-existent node
		var workflow = CreateValidWorkflow() with
		{
			Nodes = Arr.create(
				new NodeDefinition(
					"node1",
					"mod",
					"Node 1",
					HashMap.empty<string, System.Text.Json.JsonElement>(),
					ErrorHandling: new ErrorHandling(ErrorBehavior.UseErrorHandler, "nonexistent")))
		};

		// Act
		var result = _validator.Validate(workflow);

		// Assert - Should have error WF014
		result.IsValid.Should().BeFalse();
		result.Errors.Should().ContainSingle(e => e.Code == "WF014");
		result.Errors[0].NodeId.Should().Be("node1");
	}

	[Fact]
	public void Validate_ComplexValidWorkflow_ReturnsSuccess()
	{
		// Arrange - A more complex but valid workflow
		var workflow = new WorkflowDefinition(
			Id: Guid.NewGuid(),
			Name: "Complex Valid Workflow",
			Description: "Multiple nodes and connections",
			Version: new Version(2, 1, 3),
			Nodes: Arr.create(
				new NodeDefinition("start", "mod", "Start", HashMap.empty<string, System.Text.Json.JsonElement>()),
				new NodeDefinition("process", "mod", "Process", HashMap.empty<string, System.Text.Json.JsonElement>()),
				new NodeDefinition("end", "mod", "End", HashMap.empty<string, System.Text.Json.JsonElement>())),
			Connections: Arr.create(
				new ConnectionDefinition("start", "out", "process", "in"),
				new ConnectionDefinition("process", "out", "end", "in")),
			Variables: HashMap.create(
				("counter", new VariableDefinition("counter", PropertyType.Int, null, "A counter variable"))),
			Tags: Arr.create("test", "complex", "valid"));

		// Act
		var result = _validator.Validate(workflow);

		// Assert - Should be valid!
		result.IsValid.Should().BeTrue();
		result.Errors.Should().BeEmpty();
		result.Warnings.Should().BeEmpty();
	}

	[Fact]
	public void Validate_MultipleErrors_ReturnsAllErrors()
	{
		// Arrange - Workflow with multiple issues
		var workflow = new WorkflowDefinition(
			Id: Guid.NewGuid(),
			Name: "", // Error 1: Empty name
			Description: null,
			Version: new Version(1, 0, 0),
			Nodes: Arr.create(
				new NodeDefinition("dup", "mod", "Node 1", HashMap.empty<string, System.Text.Json.JsonElement>()),
				new NodeDefinition("dup", "mod", "Node 2", HashMap.empty<string, System.Text.Json.JsonElement>())), // Error 2: Duplicate ID
			Connections: Arr.create(
				new ConnectionDefinition("dup", "out", "nonexistent", "in")), // Error 3: Invalid target
			Variables: HashMap.empty<string, VariableDefinition>());

		// Act
		var result = _validator.Validate(workflow);

		// Assert - Should have all three errors
		result.IsValid.Should().BeFalse();
		result.Errors.Should().HaveCountGreaterOrEqualTo(3);
		result.Errors.Should().Contain(e => e.Code == "WF002"); // Empty name
		result.Errors.Should().Contain(e => e.Code == "WF003"); // Duplicate ID
		result.Errors.Should().Contain(e => e.Code == "WF006"); // Invalid target
	}
}

