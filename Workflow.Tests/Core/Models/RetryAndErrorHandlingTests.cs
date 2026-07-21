// ────────────────────────────────────────────────────────────────────────────────
// Copyright © GlutenFree 2025. Made with love by Ami-Chan! UwU 💖
// Licensed under the MIT License.
// ────────────────────────────────────────────────────────────────────────────────

using FluentAssertions;
using Workflow.Core.Models;

namespace Workflow.Tests.Core.Models;

/// <summary>
/// Tests for RetryPolicy and ErrorHandling! 🔄🛡️
/// </summary>
public class RetryAndErrorHandlingTests
{
	[Fact]
	public void RetryPolicy_Constructor_SetsAllValues()
	{
		// Arrange & Act
		var policy = new RetryPolicy(
			MaxAttempts: 5,
			DelayMs: 2000,
			BackoffMultiplier: 3.0,
			MaxDelayMs: 120000);

		// Assert
		policy.MaxAttempts.Should().Be(5);
		policy.DelayMs.Should().Be(2000);
		policy.BackoffMultiplier.Should().Be(3.0);
		policy.MaxDelayMs.Should().Be(120000);
	}

	[Fact]
	public void RetryPolicy_DefaultValues_AreCorrect()
	{
		// Arrange & Act
		var policy = new RetryPolicy();

		// Assert - Check default values
		policy.MaxAttempts.Should().Be(1); // No retries by default
		policy.DelayMs.Should().Be(1000);
		policy.BackoffMultiplier.Should().Be(2.0);
		policy.MaxDelayMs.Should().Be(60000);
	}

	[Fact]
	public void RetryPolicy_None_HasNoRetries()
	{
		// Act
		var policy = RetryPolicy.None;

		// Assert
		policy.MaxAttempts.Should().Be(1);
	}

	[Fact]
	public void RetryPolicy_Default_Has3Attempts()
	{
		// Act
		var policy = RetryPolicy.Default;

		// Assert
		policy.MaxAttempts.Should().Be(3);
		policy.DelayMs.Should().Be(1000);
		policy.BackoffMultiplier.Should().Be(2.0);
	}

	[Fact]
	public void RetryPolicy_Aggressive_Has5Attempts()
	{
		// Act
		var policy = RetryPolicy.Aggressive;

		// Assert
		policy.MaxAttempts.Should().Be(5);
		policy.DelayMs.Should().Be(2000);
	}

	[Fact]
	public void ErrorHandling_Constructor_SetsAllProperties()
	{
		// Arrange & Act
		var errorHandling = new ErrorHandling(
			ErrorBehavior.UseErrorHandler,
			"error_handler_node",
			5);

		// Assert
		errorHandling.OnErrorBehavior.Should().Be(ErrorBehavior.UseErrorHandler);
		errorHandling.ErrorNodeId.Should().Be("error_handler_node");
		errorHandling.MaxConsecutiveErrors.Should().Be(5);
	}

	[Fact]
	public void ErrorHandling_DefaultValues_AreCorrect()
	{
		// Arrange & Act
		var errorHandling = new ErrorHandling();

		// Assert
		errorHandling.OnErrorBehavior.Should().Be(ErrorBehavior.Fail);
		errorHandling.ErrorNodeId.Should().BeNull();
		errorHandling.MaxConsecutiveErrors.Should().BeNull();
	}

	[Fact]
	public void ErrorBehavior_HasAllExpectedValues()
	{
		// Assert - Verify enum has expected values
		Enum.GetValues<ErrorBehavior>().Should().Contain(new[]
		{
			ErrorBehavior.Fail,
			ErrorBehavior.Continue,
			ErrorBehavior.UseErrorHandler,
			ErrorBehavior.Retry
		});
	}

	[Fact]
	public void ErrorHandling_RecordEquality_WorksCorrectly()
	{
		// Arrange
		var eh1 = new ErrorHandling(ErrorBehavior.Retry);
		var eh2 = new ErrorHandling(ErrorBehavior.Retry);
		var eh3 = new ErrorHandling(ErrorBehavior.Fail);

		// Act & Assert
		eh1.Should().Be(eh2);
		eh1.Should().NotBe(eh3);
	}
}

