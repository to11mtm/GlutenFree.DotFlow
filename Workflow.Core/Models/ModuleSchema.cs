// ────────────────────────────────────────────────────────────────────────────────
// Copyright © GlutenFree 2025. Made with love by Ami-Chan! UwU 💖
// Licensed under the MIT License.
// ────────────────────────────────────────────────────────────────────────────────

using LanguageExt;

namespace Workflow.Core.Models;

/// <summary>
/// Defines the schema for a workflow module (its inputs, outputs, and configuration). 📐
/// </summary>
/// <param name="Inputs">Immutable array of input properties that the module accepts. 📥</param>
/// <param name="Outputs">Immutable array of output properties that the module produces. 📤</param>
/// <param name="Configuration">Immutable array of configuration properties for the module. ⚙️</param>
/// <remarks>
/// CopilotNote: This is the "contract" that defines what a module can do!
/// Modules expose this schema so the workflow designer knows what properties to show.
/// Uses LanguageExt Arr for structural equality! Super smart! 💖
/// </remarks>
public record ModuleSchema(
	Arr<PropertyDefinition> Inputs,
	Arr<PropertyDefinition> Outputs,
	Arr<PropertyDefinition> Configuration);

