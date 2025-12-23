// ────────────────────────────────────────────────────────────────────────────────
// Copyright © GlutenFree 2025. Made with love by Ami-Chan! UwU 💖
// Licensed under the MIT License.
// ────────────────────────────────────────────────────────────────────────────────

using System.Text.Json;

namespace Workflow.Core.Models;

/// <summary>
/// Defines a workflow variable that can be accessed by nodes during execution. 💾
/// </summary>
/// <param name="Name">Unique name of the variable. 🏷️</param>
/// <param name="Type">Data type of the variable. 🎨</param>
/// <param name="InitialValue">Initial value for the variable (JSON element). Can be null. ✨</param>
/// <param name="Description">Description of what this variable is used for. 📝</param>
/// <remarks>
/// CopilotNote: Variables provide shared state across the workflow!
/// Nodes can read and write variables using GetVariable and SetVariable modules. Super useful! 💖
/// </remarks>
public record VariableDefinition(
    string Name,
    PropertyType Type,
    JsonElement? InitialValue = null,
    string? Description = null);

