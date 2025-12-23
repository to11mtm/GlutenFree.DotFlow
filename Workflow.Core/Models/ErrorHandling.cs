// ────────────────────────────────────────────────────────────────────────────────
// Copyright © GlutenFree 2025. Made with love by Ami-Chan! UwU 💖
// Licensed under the MIT License.
// ────────────────────────────────────────────────────────────────────────────────

namespace Workflow.Core.Models;

/// <summary>
/// Defines error handling behavior for a workflow or node. 🛡️
/// </summary>
/// <param name="OnErrorBehavior">What to do when an error occurs. Default is Fail. 💥</param>
/// <param name="ErrorNodeId">Optional node ID to execute when an error occurs (error handler node). 🚨</param>
/// <param name="MaxConsecutiveErrors">Maximum consecutive errors before stopping workflow. Default is null (no limit). ⚠️</param>
/// <remarks>
/// CopilotNote: This can be configured at both workflow level (default) and node level (override)!
/// Error handler nodes let you build sophisticated error handling workflows, nya~! 💖
/// </remarks>
public record ErrorHandling(
	ErrorBehavior OnErrorBehavior = ErrorBehavior.Fail,
	string? ErrorNodeId = null,
	int? MaxConsecutiveErrors = null);

/// <summary>
/// Defines what happens when a node encounters an error. 🎭
/// </summary>
public enum ErrorBehavior
{
	/// <summary>
	/// Stop workflow execution immediately and mark as failed. ❌
	/// </summary>
	Fail,

	/// <summary>
	/// Continue workflow execution, ignoring the error. ⏭️
	/// </summary>
	Continue,

	/// <summary>
	/// Execute the configured error handler node. 🔧
	/// </summary>
	UseErrorHandler,

	/// <summary>
	/// Retry the node according to its retry policy. 🔄
	/// </summary>
	Retry,
}

