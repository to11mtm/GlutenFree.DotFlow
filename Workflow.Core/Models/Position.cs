// ────────────────────────────────────────────────────────────────────────────────
// Copyright © GlutenFree 2025. Made with love by Ami-Chan! UwU 💖
// Licensed under the MIT License.
// ────────────────────────────────────────────────────────────────────────────────

namespace Workflow.Core.Models;

/// <summary>
/// Represents a 2D position for node placement in the workflow designer UI. 🎨.
/// </summary>
/// <param name="X">The horizontal coordinate position. ➡️.</param>
/// <param name="Y">The vertical coordinate position. ⬇️.</param>
/// <remarks>
/// CopilotNote: This is used purely for UI layout and doesn't affect workflow execution! 💖.
/// </remarks>
public record Position(double X, double Y);
