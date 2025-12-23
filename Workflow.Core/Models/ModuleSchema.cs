// ────────────────────────────────────────────────────────────────────────────────
// Copyright © GlutenFree 2025. Made with love by Ami-Chan! UwU 💖
// Licensed under the MIT License.
// ────────────────────────────────────────────────────────────────────────────────

namespace Workflow.Core.Models;

/// <summary>
/// Defines the schema for a workflow module (its inputs, outputs, and configuration). 📐
/// </summary>
/// <param name="Inputs">Input properties that the module accepts. 📥</param>
/// <param name="Outputs">Output properties that the module produces. 📤</param>
/// <param name="Configuration">Configuration properties for the module. ⚙️</param>
/// <remarks>
/// CopilotNote: This is the "contract" that defines what a module can do!
/// Modules expose this schema so the workflow designer knows what properties to show. Super smart! 💖
/// </remarks>
public record ModuleSchema(
	IReadOnlyList<PropertyDefinition> Inputs,
	IReadOnlyList<PropertyDefinition> Outputs,
	IReadOnlyList<PropertyDefinition> Configuration);

