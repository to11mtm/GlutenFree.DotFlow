// ────────────────────────────────────────────────────────────────────────────────
// Copyright © GlutenFree 2025. Made with love by Ami-Chan! UwU 💖
// Licensed under the MIT License.
// ────────────────────────────────────────────────────────────────────────────────

using FluentAssertions;
using LanguageExt;
using static LanguageExt.Prelude;
using Workflow.Core.Models;

namespace Workflow.Tests.Core.Models;

/// <summary>
/// Tests for WorkflowDefinition to ensure record behavior works correctly! 🌸
/// </summary>
public class WorkflowDefinitionTests
{
	[Fact]
	public void Constructor_WithValidParameters_CreatesWorkflow()
	{
		// Arrange
		var id = Guid.NewGuid();
		var version = new Version(1, 2, 3);
		var nodes = Arr.create(new NodeDefinition("node1", "mod", "Node", LanguageExt.HashMap<string, System.Text.Json.JsonElement>.Empty));
		var connections = LanguageExt.Arr<ConnectionDefinition>.Empty;
		var variables = LanguageExt.HashMap<string, VariableDefinition>.Empty;

		// Act
		var workflow = new WorkflowDefinition(
			id,
			"Test Workflow",
			"A test description",
			version,
			nodes,
			connections,
			variables);

		// Assert - All properties should be set correctly
		workflow.Id.Should().Be(id);
		workflow.Name.Should().Be("Test Workflow");
		workflow.Description.Should().Be("A test description");
		workflow.Version.Should().Be(version);
		workflow.Nodes.Count().Should().Be(1);
		workflow.Connections.Count().Should().Be(0);
		workflow.Variables.Count().Should().Be(0);
	}

	[Fact]
	public void ToString_ReturnsFormattedString()
	{
		// Arrange
		var workflow = new WorkflowDefinition(
			Guid.NewGuid(),
			"My Workflow",
			null,
			new Version(2, 0, 0),
			Arr.create(new NodeDefinition("n1", "m", "N", LanguageExt.HashMap<string, System.Text.Json.JsonElement>.Empty)),
			LanguageExt.Arr<ConnectionDefinition>.Empty,
			LanguageExt.HashMap<string, VariableDefinition>.Empty);

		// Act
		var result = workflow.ToString();

		// Assert - Should contain key information
		result.Should().Contain("My Workflow");
		result.Should().Contain("2.0.0");
		result.Should().Contain("Nodes: 1");
		result.Should().Contain("Connections: 0");
	}

	[Fact]
	public void RecordEquality_SameValues_AreEqual()
	{
		// Arrange - Two workflows with identical values
		var id = Guid.Parse("12345678-1234-1234-1234-123456789012");
		var workflow1 = new WorkflowDefinition(
			id,
			"Test",
			null,
			new Version(1, 0, 0),
			LanguageExt.Arr<NodeDefinition>.Empty,
			LanguageExt.Arr<ConnectionDefinition>.Empty,
			LanguageExt.HashMap<string, VariableDefinition>.Empty);

		var workflow2 = new WorkflowDefinition(
			id,
			"Test",
			null,
			new Version(1, 0, 0),
			LanguageExt.Arr<NodeDefinition>.Empty,
			LanguageExt.Arr<ConnectionDefinition>.Empty,
			LanguageExt.HashMap<string, VariableDefinition>.Empty);

		// Act & Assert - Should be equal (value equality) - NOW WORKS WITH LANGUAGEEXT! 🎉
		workflow1.Should().Be(workflow2);
		(workflow1 == workflow2).Should().BeTrue();
	}

	[Fact]
	public void With_Modifier_CreatesNewInstance()
	{
		// Arrange
		var original = new WorkflowDefinition(
			Guid.NewGuid(),
			"Original",
			null,
			new Version(1, 0, 0),
			LanguageExt.Arr<NodeDefinition>.Empty,
			LanguageExt.Arr<ConnectionDefinition>.Empty,
			LanguageExt.HashMap<string, VariableDefinition>.Empty);

		// Act - Use 'with' to create modified copy
		var modified = original with { Name = "Modified" };

		// Assert - Should be different instances
		modified.Should().NotBeSameAs(original);
		modified.Name.Should().Be("Modified");
		original.Name.Should().Be("Original"); // Original unchanged
	}

	[Fact]
	public void OptionalParameters_DefaultToNull()
	{
		// Arrange & Act
		var workflow = new WorkflowDefinition(
			Guid.NewGuid(),
			"Test",
			null,
			new Version(1, 0, 0),
			LanguageExt.Arr<NodeDefinition>.Empty,
			LanguageExt.Arr<ConnectionDefinition>.Empty,
			LanguageExt.HashMap<string, VariableDefinition>.Empty);

		// Assert - Optional parameters should be null
		workflow.Trigger.Should().BeNull();
		workflow.ErrorHandling.Should().BeNull();
		workflow.CreatedAt.Should().BeNull();
		workflow.UpdatedAt.Should().BeNull();
		workflow.Tags.Should().BeNull();
	}

	[Fact]
	public void WithTimestamps_StoresCorrectValues()
	{
		// Arrange
		var created = DateTimeOffset.UtcNow.AddDays(-1);
		var updated = DateTimeOffset.UtcNow;

		// Act
		var workflow = new WorkflowDefinition(
			Guid.NewGuid(),
			"Test",
			null,
			new Version(1, 0, 0),
			LanguageExt.Arr<NodeDefinition>.Empty,
			LanguageExt.Arr<ConnectionDefinition>.Empty,
			LanguageExt.HashMap<string, VariableDefinition>.Empty,
			CreatedAt: created,
			UpdatedAt: updated);

		// Assert
		workflow.CreatedAt.Should().Be(created);
		workflow.UpdatedAt.Should().Be(updated);
	}

	[Fact]
	public void WithTags_StoresTagsCorrectly()
	{
		// Arrange
		var tags = Arr.create("production", "critical", "v2");

		// Act
		var workflow = new WorkflowDefinition(
			Guid.NewGuid(),
			"Test",
			null,
			new Version(1, 0, 0),
			LanguageExt.Arr<NodeDefinition>.Empty,
			LanguageExt.Arr<ConnectionDefinition>.Empty,
			LanguageExt.HashMap<string, VariableDefinition>.Empty,
			Tags: tags);

		// Assert
		workflow.Tags.Should().BeEquivalentTo(tags);
	}
}

