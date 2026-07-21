// ────────────────────────────────────────────────────────────────────────────────
// Copyright © GlutenFree 2025. Made with love by Ami-Chan! UwU 💖
// Licensed under the MIT License.
// ────────────────────────────────────────────────────────────────────────────────

using FluentAssertions;
using Workflow.Core.Models;

namespace Workflow.Tests.Core.Models;

/// <summary>
/// Tests for ValidationResult and related classes! ✅
/// </summary>
public class ValidationResultTests
{
	[Fact]
	public void Success_CreatesValidResult()
	{
		// Act
		var result = ValidationResult.Success();

		// Assert
		result.IsValid.Should().BeTrue();
		result.Errors.Should().BeEmpty();
		result.Warnings.Should().BeEmpty();
	}

	[Fact]
	public void Failure_WithErrors_CreatesInvalidResult()
	{
		// Arrange
		var errors = new[] { new ValidationError("TEST001", "Test error") };

		// Act
		var result = ValidationResult.Failure(errors);

		// Assert
		result.IsValid.Should().BeFalse();
		result.Errors.Should().HaveCount(1);
		result.Errors[0].Code.Should().Be("TEST001");
		result.Warnings.Should().BeEmpty();
	}

	[Fact]
	public void WithErrorsAndWarnings_SetsIsValidCorrectly()
	{
		// Arrange
		var warnings = new[] { new ValidationWarning("W001", "A warning") };

		// Act - No errors, only warnings
		var validResult = ValidationResult.WithErrorsAndWarnings(
			Array.Empty<ValidationError>(),
			warnings);

		// Assert - Should still be valid
		validResult.IsValid.Should().BeTrue();
		validResult.Warnings.Should().HaveCount(1);
	}

	[Fact]
	public void WithErrorsAndWarnings_WithErrors_IsInvalid()
	{
		// Arrange
		var errors = new[] { new ValidationError("E001", "Error") };
		var warnings = new[] { new ValidationWarning("W001", "Warning") };

		// Act
		var result = ValidationResult.WithErrorsAndWarnings(errors, warnings);

		// Assert
		result.IsValid.Should().BeFalse();
		result.Errors.Should().HaveCount(1);
		result.Warnings.Should().HaveCount(1);
	}

	[Fact]
	public void ValidationError_ToString_FormatsCorrectly()
	{
		// Arrange
		var error = new ValidationError(
			"WF001",
			"Workflow must have nodes",
			NodeId: "node1",
			PropertyName: "count");

		// Act
		var result = error.ToString();

		// Assert
		result.Should().Contain("WF001");
		result.Should().Contain("Workflow must have nodes");
		result.Should().Contain("Node: node1");
		result.Should().Contain("Property: count");
	}

	[Fact]
	public void ValidationError_WithoutNodeId_ToStringExcludesLocation()
	{
		// Arrange
		var error = new ValidationError("WF001", "General error");

		// Act
		var result = error.ToString();

		// Assert
		result.Should().Contain("WF001");
		result.Should().Contain("General error");
		result.Should().NotContain("Node:");
	}

	[Fact]
	public void ValidationWarning_ToString_FormatsCorrectly()
	{
		// Arrange
		var warning = new ValidationWarning("WF011", "Orphaned node", "orphan1");

		// Act
		var result = warning.ToString();

		// Assert
		result.Should().Contain("WF011");
		result.Should().Contain("Orphaned node");
		result.Should().Contain("Node: orphan1");
	}

	[Fact]
	public void ValidationError_RecordEquality_WorksCorrectly()
	{
		// Arrange
		var error1 = new ValidationError("E001", "Error", "node1");
		var error2 = new ValidationError("E001", "Error", "node1");
		var error3 = new ValidationError("E002", "Different", "node1");

		// Act & Assert
		error1.Should().Be(error2); // Same values
		error1.Should().NotBe(error3); // Different code
	}
}

