// ────────────────────────────────────────────────────────────────────────────────
// Copyright © GlutenFree 2025. Made with love by Ami-Chan! UwU 💖
// Licensed under the MIT License.
// ────────────────────────────────────────────────────────────────────────────────

namespace Workflow.Core.Models;

/// <summary>
/// Represents the result of validating a workflow definition. ✅
/// </summary>
/// <param name="IsValid">Whether the workflow passed all validation checks. 🎯</param>
/// <param name="Errors">Collection of validation errors that must be fixed. ❌</param>
/// <param name="Warnings">Collection of validation warnings (non-blocking issues). ⚠️</param>
/// <remarks>
/// CopilotNote: Use this to check if a workflow is safe to execute!
/// Errors prevent execution, warnings are just helpful hints, nya~! 💖
/// </remarks>
public record ValidationResult(
	bool IsValid,
	IReadOnlyList<ValidationError> Errors,
	IReadOnlyList<ValidationWarning> Warnings)
{
	/// <summary>
	/// Creates a successful validation result with no errors or warnings. ✨
	/// </summary>
	public static ValidationResult Success() =>
		new(true, Array.Empty<ValidationError>(), Array.Empty<ValidationWarning>());

	/// <summary>
	/// Creates a failed validation result with the specified errors. 💥
	/// </summary>
	/// <param name="errors">Collection of validation errors. ❌</param>
	public static ValidationResult Failure(params ValidationError[] errors) =>
		new(false, errors, Array.Empty<ValidationWarning>());

	/// <summary>
	/// Creates a validation result with both errors and warnings. 📋
	/// </summary>
	/// <param name="errors">Collection of validation errors. ❌</param>
	/// <param name="warnings">Collection of validation warnings. ⚠️</param>
	public static ValidationResult WithErrorsAndWarnings(
		IReadOnlyList<ValidationError> errors,
		IReadOnlyList<ValidationWarning> warnings) =>
		new(errors.Count == 0, errors, warnings);
}

/// <summary>
/// Represents a validation error that prevents workflow execution. ❌
/// </summary>
/// <param name="Code">Error code for programmatic handling. 🔢</param>
/// <param name="Message">Human-readable error message. 💬</param>
/// <param name="NodeId">Optional node ID where the error occurred. 🧩</param>
/// <param name="PropertyName">Optional property name where the error occurred. 📋</param>
public record ValidationError(
	string Code,
	string Message,
	string? NodeId = null,
	string? PropertyName = null)
{
	/// <summary>
	/// Returns a formatted string representation of this error. 📝
	/// </summary>
	public override string ToString()
	{
		var location = NodeId != null
			? $" (Node: {NodeId}{(PropertyName != null ? $", Property: {PropertyName}" : "")})"
			: "";
		return $"[{Code}] {Message}{location}";
	}
}

/// <summary>
/// Represents a validation warning (non-blocking issue). ⚠️
/// </summary>
/// <param name="Code">Warning code for programmatic handling. 🔢</param>
/// <param name="Message">Human-readable warning message. 💬</param>
/// <param name="NodeId">Optional node ID where the warning occurred. 🧩</param>
public record ValidationWarning(
	string Code,
	string Message,
	string? NodeId = null)
{
	/// <summary>
	/// Returns a formatted string representation of this warning. 📝
	/// </summary>
	public override string ToString()
	{
		var location = NodeId != null ? $" (Node: {NodeId})" : "";
		return $"[{Code}] {Message}{location}";
	}
}

