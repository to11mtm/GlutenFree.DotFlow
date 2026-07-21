// ────────────────────────────────────────────────────────────────────────────────
// Copyright © GlutenFree 2025. Made with love by Ami-Chan! UwU 💖
// Licensed under the MIT License.
// ────────────────────────────────────────────────────────────────────────────────

using LanguageExt;

namespace Workflow.Core.Models;

/// <summary>
/// Defines how a workflow is triggered/started. 🚀.
/// </summary>
/// <param name="Type">The type of trigger. 🎯.</param>
/// <param name="Configuration">Immutable map of configuration specific to the trigger type. ⚙️.</param>
/// <remarks>
/// CopilotNote: Triggers determine when workflows execute!
/// Manual triggers require explicit API calls, while scheduled triggers run automatically, nya~! ⏰💖
/// Uses LanguageExt HashMap for structural equality!.
/// </remarks>
public record TriggerDefinition(
    TriggerType Type,
    HashMap<string, string>? Configuration = null);

/// <summary>
/// Types of triggers that can start a workflow. ✨.
/// </summary>
public enum TriggerType
{
    /// <summary>
    /// Manual trigger - started via API call. 🖱️.
    /// </summary>
    Manual,

    /// <summary>
    /// Scheduled trigger - runs on a schedule (cron expression). ⏰.
    /// </summary>
    Scheduled,

    /// <summary>
    /// Webhook trigger - started by incoming HTTP request. 🌐.
    /// </summary>
    Webhook,

    /// <summary>
    /// Event trigger - started by system events. 📡.
    /// </summary>
    Event,
}
