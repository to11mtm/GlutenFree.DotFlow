// ────────────────────────────────────────────────────────────────────────────────
// Copyright © GlutenFree 2025. Made with love by Ami-Chan! UwU 💖
// Licensed under the MIT License.
// ────────────────────────────────────────────────────────────────────────────────

namespace Workflow.Core.Models;

/// <summary>
/// Defines a complete workflow with nodes, connections, variables, and configuration. 🌸
/// </summary>
/// <param name="Id">Unique identifier for this workflow definition. 🆔</param>
/// <param name="Name">Human-readable name of the workflow. 🏷️</param>
/// <param name="Description">Optional description explaining what this workflow does. 📝</param>
/// <param name="Version">Semantic version of this workflow definition. 📊</param>
/// <param name="Nodes">Collection of nodes that make up the workflow. 🧩</param>
/// <param name="Connections">Collection of connections between nodes. 🔗</param>
/// <param name="Variables">Dictionary of workflow variables (key = variable name). 💾</param>
/// <param name="Trigger">Optional trigger configuration for how the workflow starts. 🚀</param>
/// <param name="ErrorHandling">Default error handling behavior for all nodes. 🛡️</param>
/// <param name="CreatedAt">Timestamp when this workflow was created. 📅</param>
/// <param name="UpdatedAt">Timestamp when this workflow was last updated. 🔄</param>
/// <param name="Tags">Tags for categorization and search. 🏷️</param>
/// <remarks>
/// CopilotNote: This is the master definition of a workflow!
/// Think of it as a blueprint - it defines the structure but doesn't contain runtime state.
/// When executed, an instance is created with its own state, nya~! 💖✨
/// </remarks>
public record WorkflowDefinition(
	Guid Id,
	string Name,
	string? Description,
	Version Version,
	IReadOnlyList<NodeDefinition> Nodes,
	IReadOnlyList<ConnectionDefinition> Connections,
	IReadOnlyDictionary<string, VariableDefinition> Variables,
	TriggerDefinition? Trigger = null,
	ErrorHandling? ErrorHandling = null,
	DateTimeOffset? CreatedAt = null,
	DateTimeOffset? UpdatedAt = null,
	IReadOnlyList<string>? Tags = null)
{
	/// <summary>
	/// Returns a string representation of this workflow definition. 📝
	/// </summary>
	/// <returns>A formatted string with workflow details. ✨</returns>
	public override string ToString()
	{
		return $"Workflow '{Name}' v{Version} (ID: {Id}, Nodes: {Nodes.Count}, Connections: {Connections.Count})";
	}
}

