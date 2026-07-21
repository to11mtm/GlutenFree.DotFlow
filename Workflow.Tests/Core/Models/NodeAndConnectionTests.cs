// ────────────────────────────────────────────────────────────────────────────────
// Copyright © GlutenFree 2025. Made with love by Ami-Chan! UwU 💖
// Licensed under the MIT License.
// ────────────────────────────────────────────────────────────────────────────────

using System.Text.Json;
using FluentAssertions;
using LanguageExt;
using Workflow.Core.Models;

namespace Workflow.Tests.Core.Models;

/// <summary>
/// Tests for NodeDefinition and ConnectionDefinition! 🧩
/// </summary>
public class NodeAndConnectionTests
{
	[Fact]
	public void NodeDefinition_Constructor_SetsAllProperties()
	{
		// Arrange
		var props = new HashMap<string, JsonElement>();
		var position = new Position(100, 200);
		var errorHandling = new ErrorHandling(ErrorBehavior.Retry);
		var retryPolicy = RetryPolicy.Default;
		var metadata = new HashMap<string, string>().Add("key", "value");

		// Act
		var node = new NodeDefinition(
			"node1",
			"test.module",
			"Test Node",
			props,
			position,
			errorHandling,
			5000,
			retryPolicy,
			metadata);

		// Assert
		node.Id.Should().Be("node1");
		node.ModuleId.Should().Be("test.module");
		node.Name.Should().Be("Test Node");
		node.Properties.Should().BeEmpty();
		node.Position.Should().Be(position);
		node.ErrorHandling.Should().Be(errorHandling);
		node.Timeout.Should().Be(5000);
		node.RetryPolicy.Should().Be(retryPolicy);
		node.Metadata.Should().BeEquivalentTo(metadata);
	}

	[Fact]
	public void NodeDefinition_OptionalParameters_DefaultToNull()
	{
		// Arrange & Act
		var node = new NodeDefinition(
			"node1",
			"mod",
			"Node",
			new HashMap<string, JsonElement>());

		// Assert
		node.Position.Should().BeNull();
		node.ErrorHandling.Should().BeNull();
		node.Timeout.Should().BeNull();
		node.RetryPolicy.Should().BeNull();
		node.Metadata.Should().BeNull();
	}

	[Fact]
	public void NodeDefinition_RecordEquality_WorksCorrectly()
	{
		// Arrange
		var props = new HashMap<string, JsonElement>();
		var node1 = new NodeDefinition("node1", "mod", "Node", props);
		var node2 = new NodeDefinition("node1", "mod", "Node", props);
		var node3 = new NodeDefinition("node2", "mod", "Node", props);

		// Act & Assert
		node1.Should().Be(node2); // Same values
		node1.Should().NotBe(node3); // Different ID
	}

	[Fact]
	public void ConnectionDefinition_Constructor_SetsAllProperties()
	{
		// Arrange & Act
		var connection = new ConnectionDefinition(
			"source",
			"output",
			"target",
			"input",
			"${value} > 10",
			5);

		// Assert
		connection.SourceNodeId.Should().Be("source");
		connection.SourcePortName.Should().Be("output");
		connection.TargetNodeId.Should().Be("target");
		connection.TargetPortName.Should().Be("input");
		connection.Condition.Should().Be("${value} > 10");
		connection.Priority.Should().Be(5);
	}

	[Fact]
	public void ConnectionDefinition_OptionalParameters_HaveDefaults()
	{
		// Arrange & Act
		var connection = new ConnectionDefinition("src", "out", "tgt", "in");

		// Assert
		connection.Condition.Should().BeNull();
		connection.Priority.Should().Be(0);
	}

	[Fact]
	public void ConnectionDefinition_RecordEquality_WorksCorrectly()
	{
		// Arrange
		var conn1 = new ConnectionDefinition("a", "out", "b", "in");
		var conn2 = new ConnectionDefinition("a", "out", "b", "in");
		var conn3 = new ConnectionDefinition("a", "out", "c", "in");

		// Act & Assert
		conn1.Should().Be(conn2); // Same values
		conn1.Should().NotBe(conn3); // Different target
	}

	[Fact]
	public void Position_Constructor_SetsCoordinates()
	{
		// Arrange & Act
		var position = new Position(123.45, 678.90);

		// Assert
		position.X.Should().Be(123.45);
		position.Y.Should().Be(678.90);
	}

	[Fact]
	public void Position_RecordEquality_WorksCorrectly()
	{
		// Arrange
		var pos1 = new Position(10, 20);
		var pos2 = new Position(10, 20);
		var pos3 = new Position(10, 21);

		// Act & Assert
		pos1.Should().Be(pos2);
		pos1.Should().NotBe(pos3);
	}
}

