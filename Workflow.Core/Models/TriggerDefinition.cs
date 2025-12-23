// ────────────────────────────────────────────────────────────────────────────────
// Copyright © GlutenFree 2025. Made with love by Ami-Chan! UwU 💖
// Licensed under the MIT License.
// ────────────────────────────────────────────────────────────────────────────────

namespace Workflow.Core.Models;

/// <summary>
/// Defines how a workflow is triggered/started. 🚀
/// </summary>
/// <param name="Type">The type of trigger. 🎯</param>
/// <param name="Configuration">Configuration specific to the trigger type. ⚙️</param>
/// <remarks>
/// CopilotNote: Triggers determine when workflows execute!
/// Manual triggers require explicit API calls, while scheduled triggers run automatically, nya~! ⏰💖
/// </remarks>
public record TriggerDefinition(
	TriggerType Type,
	IReadOnlyDictionary<string, string>? Configuration = null);

/// <summary>
/// Types of triggers that can start a workflow. ✨
/// </summary>
public enum TriggerType
{
	/// <summary>
	/// Manual trigger - started via API call. 🖱️
	/// </summary>
	Manual,

	/// <summary>
	/// Scheduled trigger - runs on a schedule (cron expression). ⏰
	/// </summary>
	Scheduled,

	/// <summary>
	/// Webhook trigger - started by incoming HTTP request. 🌐
	/// </summary>
	Webhook,

	/// <summary>
	/// Event trigger - started by system events. 📡
	/// </summary>
	Event,
}

